// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using ProGPU.Wpf.Interop;
using MS.Internal;

namespace System.Windows.Controls
{
    internal interface IPortableScrollContinuation
    {
        bool IsCurrent { get; }
        void Continue(Vector originalSourceDelta);
    }

    // Captured once at admission. A later layout, desktop move or logical
    // viewport change cannot reinterpret an older packet's unused movement.
    internal sealed class PortableScrollAdmissionFrame
    {
        private readonly GeneralTransform _inverse;
        private readonly Point _anchor;
        private readonly Matrix _sourceInverse;
        private readonly Vector _pointScale;

        private PortableScrollAdmissionFrame(GeneralTransform inverse, Point anchor,
            Matrix sourceInverse, Vector pointScale)
        { _inverse = inverse; _anchor = anchor; _sourceInverse = sourceInverse; _pointScale = pointScale; }

        internal static PortableScrollAdmissionFrame Lines { get; } = new(null, default, Matrix.Identity, default);

        internal static bool TryCreate(GeneralTransform inverse, Point anchor, Matrix sourceInverse,
            Vector pointScale, out PortableScrollAdmissionFrame frame)
        {
            frame = null;
            if (inverse == null || !inverse.CanFreeze ||
                !double.IsFinite(pointScale.X) || pointScale.X <= 0 ||
                !double.IsFinite(pointScale.Y) || pointScale.Y <= 0) return false;
            // GetCurrentValueAsFrozen captures animated values too; retaining a
            // caller-owned mutable transform would not be an admission frame.
            frame = new((GeneralTransform)inverse.GetCurrentValueAsFrozen(), anchor, sourceInverse, pointScale);
            return true;
        }

        internal bool TryMap(Vector delta, out Vector result)
        {
            result = -delta;
            if (_inverse == null) return double.IsFinite(result.X) && double.IsFinite(result.Y);
            Vector local = new(-delta.X * _pointScale.X, -delta.Y * _pointScale.Y);
            if (!PortableScrollSession.TryTransformVector(_inverse, _anchor, local, out result)) return false;
            result = _sourceInverse.Transform(result);
            return double.IsFinite(result.X) && double.IsFinite(result.Y);
        }
    }

    internal sealed class PortableScrollProvenance
    {
        private readonly PortableScrollSession _session;
        private readonly PortableScrollLifetime _lifetime;
        private readonly IPortableScrollContinuation _continuation;
        private readonly PortableScrollAdmissionFrame _frame;
        private int _references = 1; // The accepted command initially owns this reservation.
        internal ulong Order { get; }

        internal PortableScrollProvenance(PortableScrollSession session, PortableScrollLifetime lifetime,
            IPortableScrollContinuation continuation, PortableScrollAdmissionFrame frame, ulong order)
        { _session = session; _lifetime = lifetime; _continuation = continuation; _frame = frame; Order = order; }

        internal bool IsCurrent => _lifetime?.IsCancelled != true && _continuation.IsCurrent;
        internal void Retain() => ++_references;
        internal void Release() { if (--_references == 0) _session.ReleaseProvenance(); }
        internal void Continue(Vector delta)
        {
            if (!IsCurrent || !_session.IsCurrent) return;
            if (!_frame.TryMap(delta, out Vector original))
                throw new InvalidOperationException("The admitted native scroll frame cannot represent its overflow.");
            if (original != default && IsCurrent && _session.IsCurrent) _continuation.Continue(original);
        }
    }

    // Each axis keeps original owners in acceptance order. Opposing movement
    // cancels oldest fractions first; it does not forward cancelled motion.
    internal sealed class PortableScrollFractions
    {
        private readonly List<(PortableScrollProvenance Owner, double Value)> _parts = new();
        internal double Total { get; private set; }

        internal void Clear()
        {
            foreach (var part in _parts) part.Owner?.Release();
            _parts.Clear(); Total = 0;
        }

        internal void Prune()
        {
            for (int i = _parts.Count - 1; i >= 0; --i)
                if (_parts[i].Owner is { } owner && !owner.IsCurrent)
                { Total -= _parts[i].Value; _parts.RemoveAt(i); owner.Release(); }
            if (_parts.Count == 0) Total = 0;
        }

        internal void Add(double value, PortableScrollProvenance owner)
        {
            if (value == 0) return;
            // Use the same exact aggregate cancellation as the source offset
            // arithmetic. Repeated subtraction of individual decimal pieces
            // must not manufacture a tiny residual after a whole consumption.
            if (value == -Total) { Clear(); return; }
            while (_parts.Count != 0 && Math.Sign(_parts[0].Value) != Math.Sign(value))
            {
                var first = _parts[0];
                double cancelled = Math.CopySign(Math.Min(Math.Abs(first.Value), Math.Abs(value)), first.Value);
                value += cancelled; Total -= cancelled;
                if (cancelled == first.Value) { _parts.RemoveAt(0); first.Owner?.Release(); }
                else _parts[0] = (first.Owner, first.Value - cancelled);
                if (value == 0) { if (_parts.Count == 0) Total = 0; return; }
            }
            if (_parts.Count != 0 && ReferenceEquals(_parts[^1].Owner, owner))
            { var last = _parts[^1]; _parts[^1] = (owner, last.Value + value); Total += value; return; }
            // At most 64 distinct reserved owners, interleaved with at most 65
            // unowned/direct-consumer pieces. Admission reserves before enqueue.
            if (_parts.Count >= 2 * PortableScrollSession.MaximumProvenancePackets + 1)
                throw new InvalidOperationException("Native scroll fractional provenance capacity was exceeded.");
            owner?.Retain(); _parts.Add((owner, value)); Total += value;
        }

        // Removes a signed prefix. Over-consumption from item rounding creates
        // a debt owned by this packet, not by an arbitrary previous frame.
        internal void Consume(double amount, PortableScrollProvenance roundingOwner)
            => Add(-amount, roundingOwner);

        internal void MovePrefix(double amount, PortableScrollFractions destination)
        {
            while (amount != 0 && _parts.Count != 0)
            {
                var first = _parts[0];
                double take = Math.CopySign(Math.Min(Math.Abs(first.Value), Math.Abs(amount)), amount);
                destination.Add(take, first.Owner);
                amount -= take; Total -= take;
                if (take == first.Value) { _parts.RemoveAt(0); first.Owner?.Release(); }
                else _parts[0] = (first.Owner, first.Value - take);
            }
            if (_parts.Count == 0) Total = 0;
        }

        internal void MoveTail(double amount, PortableScrollFractions destination)
        {
            if (amount == 0) return;
            double prefix = Total - amount;
            int index = 0;
            while (index < _parts.Count && Math.Abs(prefix) >= Math.Abs(_parts[index].Value))
            { prefix -= _parts[index].Value; ++index; }
            if (index < _parts.Count && prefix != 0)
            {
                var part = _parts[index];
                destination.Add(part.Value - prefix, part.Owner);
                Total -= part.Value - prefix;
                _parts[index++] = (part.Owner, prefix);
            }
            while (index < _parts.Count)
            {
                var part = _parts[index];
                destination.Add(part.Value, part.Owner);
                Total -= part.Value; _parts.RemoveAt(index); part.Owner?.Release();
            }
            if (_parts.Count == 0) Total = 0;
        }

        internal void Drain(PortableScrollOverflow overflow, bool horizontal)
        {
            foreach (var part in _parts)
                overflow.Add(part.Owner, horizontal ? new Vector(part.Value, 0) : new Vector(0, part.Value));
            Clear();
        }
    }

    internal sealed class PortableScrollOverflow
    {
        private readonly List<(PortableScrollProvenance Owner, Vector Delta)> _parts = new();
        internal void Add(PortableScrollProvenance owner, Vector delta)
        {
            if (owner == null || delta == default) return;
            for (int i = 0; i < _parts.Count; ++i)
                if (ReferenceEquals(_parts[i].Owner, owner))
                { _parts[i] = (owner, _parts[i].Delta + delta); return; }
            // The X and Y ledgers can discover old owners in a different order.
            // Publish each packet once, in its original acceptance order.
            int position = _parts.Count;
            for (int i = 0; i < _parts.Count; ++i)
                if (_parts[i].Owner.Order > owner.Order) { position = i; break; }
            owner.Retain(); _parts.Insert(position, (owner, delta));
        }
        internal void Clear()
        {
            foreach (var part in _parts) part.Owner.Release();
            _parts.Clear();
        }
        internal void Deliver()
        {
            Exception failure = null;
            try
            {
                foreach (var part in _parts)
                {
                    try { part.Owner.Continue(part.Delta); }
                    catch (Exception error) { failure ??= error; }
                }
            }
            finally { Clear(); }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal sealed class PortableScrollLifetime
    {
        private readonly WeakReference<PortablePresentationSource> _origin;
        private readonly ulong _generation;
        private bool _cancelled;
        internal PortableScrollLifetime(PortablePresentationSource origin = null)
        {
            if (origin == null) return;
            _origin = new WeakReference<PortablePresentationSource>(origin);
            _generation = origin.PointerInputGeneration;
        }
        internal bool IsCancelled => _cancelled || _origin != null &&
            (!_origin.TryGetTarget(out var origin) || origin.IsDisposed || origin.PointerInputGeneration != _generation);
        internal void Cancel() => _cancelled = true;
    }

    /// <summary>Declares which IScrollInfo offset axes use items instead of device-independent pixels.</summary>
    [Flags]
    public enum PortableScrollAxes
    {
        /// <summary>Both axes use device-independent pixels, not framebuffer pixels.</summary>
        Pixels = 0,
        /// <summary>The horizontal offset, extent and viewport use item units.</summary>
        HorizontalItems = 1,
        /// <summary>The vertical offset, extent and viewport use item units.</summary>
        VerticalItems = 2
    }

    // IScrollInfo deliberately does not declare the units of its offsets. Source
    // implementations publish that fact; CanContentScroll alone cannot prove it.
    /// <summary>Optional native point-scroll unit declaration for built-in and application scroll providers.</summary>
    public interface IPortableScrollInfo : IScrollInfo
    {
        /// <summary>Current offset units; changing them retires previously queued point or line input.</summary>
        PortableScrollAxes ScrollAxes { get; }
    }

    internal sealed class PortableScrollSession
    {
        internal const int MaximumLinesPerPacket = 4096;
        internal const int MaximumProvenancePackets = 64;
        private int _provenancePackets;
        private ulong _provenanceOrder;
        private readonly ScrollViewer _owner;
        private readonly IScrollInfo _info;
        private readonly IPortableScrollInfo _units;
        private readonly PortablePresentationSource _source;
        private readonly ulong _sourceGeneration;
        private readonly PortableScrollAxes _axes;
        private readonly bool _horizontalEnabled, _verticalEnabled;
        private Vector _pointRemainder;
        private Vector _lineRemainder;
        private readonly PortableScrollFractions _pointX = new(), _pointY = new(), _lineX = new(), _lineY = new();
        private PortablePointerScrollUnit? _lastUnit;
        private bool _cancelled;
        internal PortableScrollLifetime Lifetime { get; }
        internal PortablePresentationSource Source => _source;
        internal void ReleaseProvenance() => --_provenancePackets;

        private PortableScrollSession(ScrollViewer owner, IScrollInfo info,
            PortablePresentationSource source, PortableScrollAxes axes, PortableScrollLifetime lifetime)
        {
            _owner = owner; _info = info; _source = source;
            _units = info as IPortableScrollInfo;
            _sourceGeneration = source.PointerInputGeneration; _axes = axes;
            _horizontalEnabled = info.CanHorizontallyScroll; _verticalEnabled = info.CanVerticallyScroll;
            Lifetime = lifetime;
        }

        internal static bool TryCreate(ScrollViewer owner, out PortableScrollSession session)
            => TryCreate(owner, null, out session);

        internal static bool TryCreate(ScrollViewer owner, PortableScrollLifetime lifetime, out PortableScrollSession session)
        {
            owner.VerifyAccess();
            session = null;
            IScrollInfo info = owner.ScrollInfo;
            if (!owner.IsEnabled || !owner.IsVisible || !owner.HandlesMouseWheelScrolling || info == null ||
                !ReferenceEquals(info.ScrollOwner, owner) ||
                PresentationSource.CriticalFromVisual(owner) is not PortablePresentationSource source || source.IsDisposed)
                return false;
            PortableScrollAxes axes = info is IPortableScrollInfo units ? units.ScrollAxes : PortableScrollAxes.Pixels;
            if ((axes & ~(PortableScrollAxes.HorizontalItems | PortableScrollAxes.VerticalItems)) != 0) return false;
            var candidate = new PortableScrollSession(owner, info, source, axes, lifetime);
            if (!candidate.IsCurrent) return false;
            session = candidate;
            return true;
        }

        internal bool IsCurrent => !_cancelled && !_source.IsDisposed &&
            _sourceGeneration == _source.PointerInputGeneration &&
            ReferenceEquals(_owner.ScrollInfo, _info) && ReferenceEquals(_info.ScrollOwner, _owner) &&
            ReferenceEquals(PresentationSource.CriticalFromVisual(_owner), _source) &&
            (_units == null || _units.ScrollAxes == _axes) &&
            _info.CanHorizontallyScroll == _horizontalEnabled && _info.CanVerticallyScroll == _verticalEnabled &&
            _owner.IsEnabled && _owner.IsVisible && _owner.HandlesMouseWheelScrolling &&
            PortableWindowActivationService.IsModalInputAllowed(_source.RootVisual as UIElement) &&
            // Application-owned capability getters can replace their provider or source.
            !_source.IsDisposed && _sourceGeneration == _source.PointerInputGeneration &&
            ReferenceEquals(_owner.ScrollInfo, _info) &&
            ReferenceEquals(PresentationSource.CriticalFromVisual(_owner), _source);

        internal void Cancel()
        {
            _owner.VerifyAccess(); _cancelled = true;
            _pointX.Clear(); _pointY.Clear(); _lineX.Clear(); _lineY.Clear();
            _pointRemainder = _lineRemainder = default;
        }

        // The source router owns phase validation and target selection.
        // This consumer keeps native units; it never creates a MouseWheel delta.
        internal bool TryQueue(PortablePointerInput input)
            => TryQueue(input, out _);

        internal bool TryQueue(PortablePointerInput input, out bool queueFull)
            => TryQueue(input, Lifetime, out queueFull);

        internal bool TryQueue(PortablePointerInput input, PortableScrollLifetime lifetime, out bool queueFull)
            => TryQueueCore(input, lifetime, input == null ? default : new Vector(input.ScrollX, input.ScrollY),
                _source, false, out _, out queueFull);

        internal bool TryQueueRemaining(PortablePointerInput input, PortableScrollLifetime lifetime,
            Vector remaining, out Vector nextRemaining, out bool queueFull)
            => TryQueueRemaining(input, lifetime, remaining, _source, out nextRemaining, out queueFull);

        internal bool TryQueueRemaining(PortablePointerInput input, PortableScrollLifetime lifetime,
            Vector remaining, PortablePresentationSource origin, out Vector nextRemaining, out bool queueFull)
            => TryQueueCore(input, lifetime, remaining, origin, true, out nextRemaining, out queueFull);

        internal bool TryQueueRemaining(PortableScrollEventArgs dispatch, out Vector nextRemaining, out bool queueFull)
            => TryQueueCore(dispatch.NativeInput, dispatch.Lifetime, dispatch.RemainingScroll,
                dispatch.PresentationSource, true, out nextRemaining, out queueFull, dispatch);

        internal bool TryQueueRemaining(PortableScrollEventArgs dispatch, IPortableScrollContinuation continuation,
            out Vector nextRemaining, out bool queueFull)
            => TryQueueCore(dispatch.NativeInput, dispatch.Lifetime, dispatch.RemainingScroll,
                dispatch.PresentationSource, true, out nextRemaining, out queueFull, dispatch, continuation,
                continuation == null ? null : dispatch.OriginToDesktop);

        internal bool TryQueueRemaining(PortablePointerInput input, PortableScrollLifetime lifetime,
            Vector remaining, PortablePresentationSource origin, IPortableScrollContinuation continuation,
            out Vector nextRemaining, out bool queueFull, Matrix? originToDesktop = null)
            => TryQueueCore(input, lifetime, remaining, origin, true, out nextRemaining, out queueFull, null,
                continuation, originToDesktop);

        private bool TryQueueCore(PortablePointerInput input, PortableScrollLifetime lifetime,
            Vector remaining, PortablePresentationSource origin, bool partial, out Vector nextRemaining, out bool queueFull,
            PortableScrollEventArgs dispatch = null, IPortableScrollContinuation continuation = null,
            Matrix? originToDesktop = null)
        {
            nextRemaining = remaining;
            queueFull = false;
            _owner.VerifyAccess();
            if (input == null || input.Kind != PortablePointerEventKind.Scroll || origin == null || origin.IsDisposed ||
                input.ScrollUnit is not (PortablePointerScrollUnit.Points or PortablePointerScrollUnit.Lines) ||
                lifetime?.IsCancelled == true || !IsCurrent || dispatch != null && !dispatch.IsCurrent ||
                continuation != null && !continuation.IsCurrent) return false;
            ulong originGeneration = origin.PointerInputGeneration;
            if (!double.IsFinite(remaining.X) || !double.IsFinite(remaining.Y)) return false;
            if (!ValidMetrics(_info.HorizontalOffset, _info.ExtentWidth, _info.ViewportWidth) ||
                !ValidMetrics(_info.VerticalOffset, _info.ExtentHeight, _info.ViewportHeight)) return false;
            Vector delta = -remaining;
            Vector remainder = default;
            PortableScrollAdmissionFrame frame = PortableScrollAdmissionFrame.Lines;
            if (input.ScrollUnit == PortablePointerScrollUnit.Lines)
            {
                if (Math.Abs(delta.X) > MaximumLinesPerPacket || Math.Abs(delta.Y) > MaximumLinesPerPacket)
                    return false;
                remainder = new Vector(_horizontalEnabled ? 0 : remaining.X, _verticalEnabled ? 0 : remaining.Y);
                if (!partial && remainder != default) return false;
                delta = new Vector(_horizontalEnabled ? delta.X : 0, _verticalEnabled ? delta.Y : 0);
            }
            else
            {
                // Line commands have their own provider-defined meaning. Point
                // offsets require an explicit unit declaration; never guess from
                // CanContentScroll, provider type names or wheel preferences.
                if (_units == null) return false;
                // Point vectors belong to the source frame. Transform both ends
                // through the actual visual mapping, without scaling wheel lines.
                if (!TrySourceFrame(origin, _source, out Matrix sourceFrame, out Matrix inverseSourceFrame,
                    originToDesktop)) return false;
                GeneralTransform transform = _source.RootVisual.TransformToDescendant(_owner);
                Point start = sourceFrame.Transform(new Point(input.X, input.Y));
                delta = sourceFrame.Transform(delta);
                if (!transform.TryTransform(start, out Point localStart) ||
                    !double.IsFinite(localStart.X) || !double.IsFinite(localStart.Y) ||
                    !TryTransformVector(transform, start, delta, out delta)) return false;
                Vector localRemainder = new(_horizontalEnabled ? 0 : delta.X, _verticalEnabled ? 0 : delta.Y);
                if (localRemainder != default)
                {
                    if (!partial || transform.Inverse is not GeneralTransform inverse ||
                        !TryTransformVector(inverse, localStart, -localRemainder, out Vector sourceRemainder)) return false;
                    // The next routed owner receives the unconsumed vector back
                    // in the original source frame, not this viewer's local axes.
                    remainder = inverseSourceFrame.Transform(sourceRemainder);
                }
                delta = new Vector(_horizontalEnabled ? delta.X : 0, _verticalEnabled ? delta.Y : 0);
                Vector scale = _owner.GetScrollPointScale(_info.ViewportWidth, _info.ViewportHeight, _axes);
                if (!TryScale(delta.X, scale.X, _info.CanHorizontallyScroll, out double x) ||
                    !TryScale(delta.Y, scale.Y, _info.CanVerticallyScroll, out double y)) return false;
                delta = new Vector(x, y);
                if (continuation != null && !PortableScrollAdmissionFrame.TryCreate(transform.Inverse,
                    localStart, inverseSourceFrame, new Vector(_horizontalEnabled ? scale.X : 1,
                        _verticalEnabled ? scale.Y : 1), out frame)) return false;
            }
            if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y) ||
                !double.IsFinite(remainder.X) || !double.IsFinite(remainder.Y) ||
                lifetime?.IsCancelled == true || origin.IsDisposed || origin.PointerInputGeneration != originGeneration ||
                !IsCurrent) return false;
            // Provider metrics, capabilities and visual transforms can deliver
            // newer native input without replacing this source or session. This
            // guard owns admission only; accepted commands retain their gesture
            // lifetime and must not be cancelled by an ordinary later packet.
            if (dispatch != null && !dispatch.IsCurrent || continuation != null && !continuation.IsCurrent) return false;
            if (delta == default)
            {
                if (remaining != default) return false;
                nextRemaining = default;
                return true;
            }
            PortableScrollProvenance provenance = null;
            if (continuation != null)
            {
                if (_provenancePackets == MaximumProvenancePackets)
                {
                    _pointX.Prune(); _pointY.Prune(); _lineX.Prune(); _lineY.Prune();
                    if (!IsCurrent || lifetime?.IsCancelled == true || !continuation.IsCurrent ||
                        dispatch != null && !dispatch.IsCurrent) return false;
                }
                if (_provenancePackets == MaximumProvenancePackets) { queueFull = true; return false; }
                ulong order = checked(++_provenanceOrder);
                ++_provenancePackets;
                provenance = new(this, lifetime, continuation, frame, order);
            }
            bool queued = _owner.TryEnqueuePortableScroll(new PortableScrollCommand(this, input.ScrollUnit, delta, lifetime, provenance));
            if (!queued) provenance?.Release();
            queueFull = !queued;
            if (queued) nextRemaining = remainder;
            return queued;
        }

        private static bool TrySourceFrame(PortablePresentationSource origin, PortablePresentationSource target,
            out Matrix forward, out Matrix inverse, Matrix? originToDesktop = null)
        {
            forward = inverse = Matrix.Identity;
            if (ReferenceEquals(origin, target) && originToDesktop == null) return true;
            if (origin.RootVisual == null || target.RootVisual == null) return false;
            // Portable clients already use native logical coordinates. Framebuffer
            // DPI is intentionally absent from this desktop/root conversion.
            PortableDesktopTransform from = origin.DesktopTransform, to = target.DesktopTransform;
            Matrix targetRoot = PointUtil.GetVisualTransform(target.RootVisual);
            if (originToDesktop == null && !from.IsValid || !to.IsValid || !targetRoot.HasInverse) return false;
            targetRoot.Invert();
            forward = originToDesktop ?? PointUtil.GetVisualTransform(origin.RootVisual);
            if (originToDesktop == null)
            {
                forward.Scale(from.ScaleX, from.ScaleY);
                forward.Translate(from.OriginX, from.OriginY);
            }
            forward.Translate(-to.OriginX, -to.OriginY);
            forward.Scale(1 / to.ScaleX, 1 / to.ScaleY);
            forward.Append(targetRoot);
            if (!Finite(forward) || !forward.HasInverse) return false;
            inverse = forward;
            inverse.Invert();
            return Finite(inverse);
        }

        private static bool Finite(Matrix value) => double.IsFinite(value.M11) && double.IsFinite(value.M12) &&
            double.IsFinite(value.M21) && double.IsFinite(value.M22) &&
            double.IsFinite(value.OffsetX) && double.IsFinite(value.OffsetY);

        internal static bool TryTransformVector(GeneralTransform transform, Point start, Vector delta, out Vector result)
        {
            if (transform is Transform affine)
            {
                // Desktop/visual translations must not erase a small vector
                // through subtraction of two large transformed positions.
                result = affine.Value.Transform(delta);
            }
            else
            {
                result = default;
                // General projections need their actual endpoint mapping; do
                // not approximate a non-affine transform by an identity matrix.
                if (!transform.TryTransform(start, out Point first) ||
                    !transform.TryTransform(start + delta, out Point last)) return false;
                result = last - first;
            }
            return double.IsFinite(result.X) && double.IsFinite(result.Y);
        }

        private static bool TryScale(double value, double scale, bool enabled, out double result)
        {
            result = 0;
            if (value == 0) return true;
            if (!enabled) return false;
            if (!double.IsFinite(scale) || scale <= 0) return false;
            result = value / scale;
            return double.IsFinite(result);
        }

        private void SelectUnit(PortablePointerScrollUnit unit, PortableScrollOverflow overflow)
        {
            if (_lastUnit == unit) return;
            // A unit switch cannot reinterpret or erase earlier owned fractions.
            // Each previous packet's continuation retains its own native unit.
            _pointX.Drain(overflow, true); _pointY.Drain(overflow, false);
            _lineX.Drain(overflow, true); _lineY.Drain(overflow, false);
            _lastUnit = unit; _pointRemainder = default; _lineRemainder = default;
        }

        internal Vector ApplyPoints(Vector delta, PortableScrollLifetime lifetime,
            PortableScrollProvenance provenance, PortableScrollOverflow overflow)
        {
            if (_units == null || !IsCommandCurrent(lifetime, provenance)) return default;
            bool horizontal = _horizontalEnabled, vertical = _verticalEnabled;
            _pointX.Prune(); _pointY.Prune();
            _pointRemainder = new(_pointX.Total, _pointY.Total);
            Vector remainder = _lastUnit == PortablePointerScrollUnit.Points ? _pointRemainder : default;
            double oldX = _info.HorizontalOffset, oldY = _info.VerticalOffset;
            double width = _info.ExtentWidth, height = _info.ExtentHeight;
            double viewportWidth = _info.ViewportWidth, viewportHeight = _info.ViewportHeight;
            // Validate both axes before calling the application-owned provider.
            double x = PointOffset(oldX, width, viewportWidth,
                horizontal ? delta.X : 0, remainder.X, (_axes & PortableScrollAxes.HorizontalItems) != 0,
                out double remainderX, out double unusedX);
            double y = PointOffset(oldY, height, viewportHeight,
                vertical ? delta.Y : 0, remainder.Y, (_axes & PortableScrollAxes.VerticalItems) != 0,
                out double remainderY, out double unusedY);
            if (!IsCommandCurrent(lifetime, provenance)) return default;
            SelectUnit(PortablePointerScrollUnit.Points, overflow);
            UpdatePointProvenance(_pointX, oldX, width, viewportWidth, horizontal ? delta.X : 0,
                remainderX, unusedX, provenance, overflow, true);
            UpdatePointProvenance(_pointY, oldY, height, viewportHeight, vertical ? delta.Y : 0,
                remainderY, unusedY, provenance, overflow, false);
            _pointRemainder = new Vector(horizontal ? remainderX : 0, vertical ? remainderY : 0);
            if (horizontal && x != oldX) _info.SetHorizontalOffset(x);
            if (IsCommandCurrent(lifetime, provenance) && vertical && y != oldY) _info.SetVerticalOffset(y);
            return IsCommandCurrent(lifetime, provenance) ?
                new Vector(horizontal ? unusedX : 0, vertical ? unusedY : 0) : default;
        }

        private bool IsCommandCurrent(PortableScrollLifetime lifetime, PortableScrollProvenance provenance)
            => IsCurrent && lifetime?.IsCancelled != true && provenance?.IsCurrent != false;

        private static void UpdatePointProvenance(PortableScrollFractions fractions, double offset, double extent,
            double viewport, double delta, double remainder, double unused, PortableScrollProvenance owner,
            PortableScrollOverflow overflow, bool horizontal)
        {
            if (AtBoundary(offset, extent, viewport, fractions.Total))
            {
                unused -= fractions.Total;
                fractions.Drain(overflow, horizontal);
            }
            fractions.Add(delta, owner);
            fractions.Consume(fractions.Total - remainder - unused, owner);
            if (unused != 0) fractions.Drain(overflow, horizontal);
        }

        private static double PointOffset(double offset, double extent, double viewport, double delta,
            double remainder, bool logical, out double nextRemainder, out double unused)
        {
            if (!ValidMetrics(offset, extent, viewport))
                throw new InvalidOperationException("Native scrolling requires finite nonnegative IScrollInfo metrics.");
            // An ordinary queued command or layout may have moved this provider
            // to an edge since the fraction was retained. Do not let that old
            // outward fraction cancel new inward motion.
            double previousUnused = AtBoundary(offset, extent, viewport, remainder) ? remainder : 0;
            remainder -= previousUnused;
            double movement = delta + remainder;
            double requested = offset + delta + remainder;
            if (!double.IsFinite(movement) || !double.IsFinite(requested))
                throw new InvalidOperationException("Native scrolling requires finite nonnegative IScrollInfo metrics.");
            double maximum = Math.Max(0, extent - viewport);
            double desired = Math.Clamp(requested, 0, maximum);
            double result = logical ? Math.Clamp(Math.Round(desired), 0, maximum) : desired;
            nextRemainder = desired == 0 || desired == maximum ? 0 : desired - result;
            // Layout can temporarily leave an offset beyond a shrinking extent.
            // Its correction is not additional input available to an ancestor.
            unused = previousUnused + Math.Clamp(requested - desired, Math.Min(0, movement), Math.Max(0, movement));
            return result;
        }

        private static bool ValidMetrics(double offset, double extent, double viewport) =>
            double.IsFinite(offset) && double.IsFinite(extent) && double.IsFinite(viewport) &&
            offset >= 0 && extent >= 0 && viewport >= 0;

        internal bool TryBeginLines(Vector delta, out int horizontal, out int vertical, out Vector unused,
            PortableScrollLifetime lifetime, PortableScrollProvenance provenance, PortableScrollOverflow overflow,
            PortableScrollFractions pendingX, PortableScrollFractions pendingY)
        {
            horizontal = vertical = 0;
            unused = default;
            double offsetX = _info.HorizontalOffset, width = _info.ExtentWidth, viewportWidth = _info.ViewportWidth;
            double offsetY = _info.VerticalOffset, height = _info.ExtentHeight, viewportHeight = _info.ViewportHeight;
            if (!ValidMetrics(offsetX, width, viewportWidth) || !ValidMetrics(offsetY, height, viewportHeight))
                throw new InvalidOperationException("Native scrolling requires finite nonnegative IScrollInfo metrics.");
            if (!IsCommandCurrent(lifetime, provenance)) return false;
            SelectUnit(PortablePointerScrollUnit.Lines, overflow);
            _lineX.Prune(); _lineY.Prune();
            _lineRemainder = new(_lineX.Total, _lineY.Total);
            if (AtBoundary(offsetX, width, viewportWidth, _lineRemainder.X))
            {
                unused.X = _lineRemainder.X; _lineRemainder.X = 0; _lineX.Drain(overflow, true);
            }
            if (AtBoundary(offsetY, height, viewportHeight, _lineRemainder.Y))
            {
                unused.Y = _lineRemainder.Y; _lineRemainder.Y = 0; _lineY.Drain(overflow, false);
            }
            double x = _horizontalEnabled ? delta.X + _lineRemainder.X : 0;
            double y = _verticalEnabled ? delta.Y + _lineRemainder.Y : 0;
            horizontal = (int)Math.Truncate(x); vertical = (int)Math.Truncate(y);
            _lineX.Add(_horizontalEnabled ? delta.X : 0, provenance);
            _lineY.Add(_verticalEnabled ? delta.Y : 0, provenance);
            _lineX.MovePrefix(horizontal, pendingX); _lineY.MovePrefix(vertical, pendingY);
            _lineRemainder = new Vector(x - horizontal, y - vertical);
            return true;
        }

        internal Vector TakeLineOverflow(ref int horizontal, ref int vertical, PortableScrollLifetime lifetime,
            PortableScrollOverflow overflow, PortableScrollFractions pendingX, PortableScrollFractions pendingY,
            PortableScrollProvenance provenance)
        {
            // Read both axes before changing fractions or invoking a provider.
            // Line commands do not declare a point distance; only a published
            // boundary can reject the remaining commands, never an inferred ratio.
            double x = _info.HorizontalOffset, width = _info.ExtentWidth, viewportWidth = _info.ViewportWidth;
            double y = _info.VerticalOffset, height = _info.ExtentHeight, viewportHeight = _info.ViewportHeight;
            if (!ValidMetrics(x, width, viewportWidth) || !ValidMetrics(y, height, viewportHeight))
                throw new InvalidOperationException("Native scrolling requires finite nonnegative IScrollInfo metrics.");
            if (!IsCommandCurrent(lifetime, provenance)) return default;
            pendingX.Prune(); pendingY.Prune(); _lineX.Prune(); _lineY.Prune();
            _lineX.MovePrefix(_lineX.Total, pendingX); _lineY.MovePrefix(_lineY.Total, pendingY);
            // Cancelled provenance never lends its fraction to a later packet.
            // All pending whole commands otherwise retain their original FIFO.
            horizontal = (int)Math.Truncate(pendingX.Total);
            vertical = (int)Math.Truncate(pendingY.Total);
            pendingX.MoveTail(pendingX.Total - horizontal, _lineX);
            pendingY.MoveTail(pendingY.Total - vertical, _lineY);
            _lineRemainder = new(_lineX.Total, _lineY.Total);
            Vector unused = default;
            double remainingX = horizontal + _lineRemainder.X;
            double remainingY = vertical + _lineRemainder.Y;
            if (AtBoundary(x, width, viewportWidth, remainingX))
            {
                unused.X = remainingX; horizontal = 0; _lineRemainder.X = 0;
                pendingX.Drain(overflow, true); _lineX.Drain(overflow, true);
            }
            if (AtBoundary(y, height, viewportHeight, remainingY))
            {
                unused.Y = remainingY; vertical = 0; _lineRemainder.Y = 0;
                pendingY.Drain(overflow, false); _lineY.Drain(overflow, false);
            }
            return unused;
        }

        private static bool AtBoundary(double offset, double extent, double viewport, double direction) =>
            direction < 0 ? offset <= 0 : direction > 0 && offset >= Math.Max(0, extent - viewport);

        internal void ApplyLine(bool horizontal, bool positive, PortableScrollLifetime lifetime = null,
            PortableScrollProvenance provenance = null)
        {
            if (!IsCommandCurrent(lifetime, provenance)) return;
            if (horizontal)
            {
                if (!_horizontalEnabled) return;
                if (positive) _info.LineRight(); else _info.LineLeft();
            }
            else
            {
                if (!_verticalEnabled) return;
                if (positive) _info.LineDown(); else _info.LineUp();
            }
        }
    }

    internal sealed class PortableScrollCommand
    {
        private readonly PortableScrollSession _session;
        private readonly PortablePointerScrollUnit _unit;
        private readonly Vector _delta;
        private readonly PortableScrollLifetime _lifetime;
        private readonly PortableScrollProvenance _provenance;
        private readonly PortableScrollOverflow _overflow = new();
        private readonly PortableScrollFractions _pendingX = new(), _pendingY = new();
        private bool _started, _completed;
        private int _horizontal, _vertical;
        private Vector _unconsumed;

        // Point values use the admitted provider's offset units, not native
        // source coordinates. Line values remain provider commands. A router
        // must retain the original frame conversion before forwarding either.
        internal Vector Unconsumed => _completed && _session.IsCurrent && _lifetime?.IsCancelled != true &&
            _provenance?.IsCurrent != false ?
            _unconsumed : default;

        internal PortableScrollCommand(PortableScrollSession session, PortablePointerScrollUnit unit, Vector delta,
            PortableScrollLifetime lifetime = null, PortableScrollProvenance provenance = null)
        { _session = session; _unit = unit; _delta = delta; _lifetime = lifetime; _provenance = provenance; }

        private bool Complete(bool deliver)
        {
            _completed = true;
            _pendingX.Clear(); _pendingY.Clear();
            _provenance?.Release();
            if (deliver) _overflow.Deliver(); else _overflow.Clear();
            return true;
        }

        // One line per existing layout/command pass preserves providers whose
        // actual offsets publish only after measure. Later commands cannot pass it.
        internal bool Advance()
        {
            if (_completed) return true;
            try { return AdvanceCore(); }
            catch
            {
                if (!_completed) Complete(false);
                throw;
            }
        }

        private bool AdvanceCore()
        {
            if (!_session.IsCurrent || _lifetime?.IsCancelled == true || _provenance?.IsCurrent == false)
                return Complete(false);
            if (_unit == PortablePointerScrollUnit.Points)
            {
                _unconsumed = _session.ApplyPoints(_delta, _lifetime, _provenance, _overflow);
                return Complete(true);
            }
            if (!_started)
            {
                if (!_session.TryBeginLines(_delta, out _horizontal, out _vertical, out _unconsumed, _lifetime,
                    _provenance, _overflow, _pendingX, _pendingY)) return Complete(false);
                _started = true;
            }
            _unconsumed += _session.TakeLineOverflow(ref _horizontal, ref _vertical, _lifetime,
                _overflow, _pendingX, _pendingY, _provenance);
            if (_horizontal != 0)
            {
                int direction = Math.Sign(_horizontal); _horizontal -= direction;
                _pendingX.Consume(direction, _provenance);
                _session.ApplyLine(true, direction > 0, _lifetime, _provenance);
                return false;
            }
            else if (_vertical != 0)
            {
                int direction = Math.Sign(_vertical); _vertical -= direction;
                _pendingY.Consume(direction, _provenance);
                _session.ApplyLine(false, direction > 0, _lifetime, _provenance);
                return false;
            }
            // Even the last issued line needs a layout pass: fractional input
            // left at its newly published boundary belongs to the overflow.
            return Complete(true);
        }
    }
}
