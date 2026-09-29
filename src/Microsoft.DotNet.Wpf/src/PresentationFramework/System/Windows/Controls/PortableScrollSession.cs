// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ProGPU.Wpf.Interop;
using MS.Internal;

namespace System.Windows.Controls
{
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
        private readonly ScrollViewer _owner;
        private readonly IScrollInfo _info;
        private readonly IPortableScrollInfo _units;
        private readonly PortablePresentationSource _source;
        private readonly ulong _sourceGeneration;
        private readonly PortableScrollAxes _axes;
        private readonly bool _horizontalEnabled, _verticalEnabled;
        private Vector _pointRemainder;
        private Vector _lineRemainder;
        private PortablePointerScrollUnit? _lastUnit;
        private bool _cancelled;
        internal PortableScrollLifetime Lifetime { get; }
        internal PortablePresentationSource Source => _source;

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

        internal void Cancel() { _owner.VerifyAccess(); _cancelled = true; }

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

        private bool TryQueueCore(PortablePointerInput input, PortableScrollLifetime lifetime,
            Vector remaining, PortablePresentationSource origin, bool partial, out Vector nextRemaining, out bool queueFull)
        {
            nextRemaining = remaining;
            queueFull = false;
            _owner.VerifyAccess();
            if (input == null || input.Kind != PortablePointerEventKind.Scroll || origin == null || origin.IsDisposed ||
                lifetime?.IsCancelled == true || !IsCurrent) return false;
            ulong originGeneration = origin.PointerInputGeneration;
            if (!double.IsFinite(remaining.X) || !double.IsFinite(remaining.Y)) return false;
            if (!ValidMetrics(_info.HorizontalOffset, _info.ExtentWidth, _info.ViewportWidth) ||
                !ValidMetrics(_info.VerticalOffset, _info.ExtentHeight, _info.ViewportHeight)) return false;
            Vector delta = -remaining;
            Vector remainder = default;
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
                if (!TrySourceFrame(origin, _source, out Matrix sourceFrame, out Matrix inverseSourceFrame)) return false;
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
            }
            if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y) ||
                !double.IsFinite(remainder.X) || !double.IsFinite(remainder.Y) ||
                lifetime?.IsCancelled == true || origin.IsDisposed || origin.PointerInputGeneration != originGeneration ||
                !IsCurrent) return false;
            if (delta == default)
            {
                if (remaining != default) return false;
                nextRemaining = default;
                return true;
            }
            bool queued = _owner.TryEnqueuePortableScroll(new PortableScrollCommand(this, input.ScrollUnit, delta, lifetime));
            queueFull = !queued;
            if (queued) nextRemaining = remainder;
            return queued;
        }

        private static bool TrySourceFrame(PortablePresentationSource origin, PortablePresentationSource target,
            out Matrix forward, out Matrix inverse)
        {
            forward = inverse = Matrix.Identity;
            if (ReferenceEquals(origin, target)) return true;
            if (origin.RootVisual == null || target.RootVisual == null) return false;
            // Portable clients already use native logical coordinates. Framebuffer
            // DPI is intentionally absent from this desktop/root conversion.
            PortableDesktopTransform from = origin.DesktopTransform, to = target.DesktopTransform;
            Matrix targetRoot = PointUtil.GetVisualTransform(target.RootVisual);
            if (!from.IsValid || !to.IsValid || !targetRoot.HasInverse) return false;
            targetRoot.Invert();
            forward = PointUtil.GetVisualTransform(origin.RootVisual);
            forward.Scale(from.ScaleX, from.ScaleY);
            forward.Translate(from.OriginX, from.OriginY);
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

        private static bool TryTransformVector(GeneralTransform transform, Point start, Vector delta, out Vector result)
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

        private void SelectUnit(PortablePointerScrollUnit unit)
        {
            if (_lastUnit == unit) return;
            _lastUnit = unit; _pointRemainder = default; _lineRemainder = default;
        }

        internal Vector ApplyPoints(Vector delta, PortableScrollLifetime lifetime = null)
        {
            if (_units == null || !IsCurrent || lifetime?.IsCancelled == true) return default;
            bool horizontal = _horizontalEnabled, vertical = _verticalEnabled;
            Vector remainder = _lastUnit == PortablePointerScrollUnit.Points ? _pointRemainder : default;
            double oldX = _info.HorizontalOffset, oldY = _info.VerticalOffset;
            // Validate both axes before calling the application-owned provider.
            double x = PointOffset(oldX, _info.ExtentWidth, _info.ViewportWidth,
                horizontal ? delta.X : 0, remainder.X, (_axes & PortableScrollAxes.HorizontalItems) != 0,
                out double remainderX, out double unusedX);
            double y = PointOffset(oldY, _info.ExtentHeight, _info.ViewportHeight,
                vertical ? delta.Y : 0, remainder.Y, (_axes & PortableScrollAxes.VerticalItems) != 0,
                out double remainderY, out double unusedY);
            if (!IsCurrent || lifetime?.IsCancelled == true) return default;
            SelectUnit(PortablePointerScrollUnit.Points);
            _pointRemainder = new Vector(horizontal ? remainderX : 0, vertical ? remainderY : 0);
            if (horizontal && x != oldX) _info.SetHorizontalOffset(x);
            if (IsCurrent && lifetime?.IsCancelled != true && vertical && y != oldY) _info.SetVerticalOffset(y);
            return IsCurrent && lifetime?.IsCancelled != true ?
                new Vector(horizontal ? unusedX : 0, vertical ? unusedY : 0) : default;
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
            PortableScrollLifetime lifetime = null)
        {
            horizontal = vertical = 0;
            unused = default;
            double offsetX = _info.HorizontalOffset, width = _info.ExtentWidth, viewportWidth = _info.ViewportWidth;
            double offsetY = _info.VerticalOffset, height = _info.ExtentHeight, viewportHeight = _info.ViewportHeight;
            if (!ValidMetrics(offsetX, width, viewportWidth) || !ValidMetrics(offsetY, height, viewportHeight))
                throw new InvalidOperationException("Native scrolling requires finite nonnegative IScrollInfo metrics.");
            if (!IsCurrent || lifetime?.IsCancelled == true) return false;
            SelectUnit(PortablePointerScrollUnit.Lines);
            if (AtBoundary(offsetX, width, viewportWidth, _lineRemainder.X))
            {
                unused.X = _lineRemainder.X; _lineRemainder.X = 0;
            }
            if (AtBoundary(offsetY, height, viewportHeight, _lineRemainder.Y))
            {
                unused.Y = _lineRemainder.Y; _lineRemainder.Y = 0;
            }
            double x = _horizontalEnabled ? delta.X + _lineRemainder.X : 0;
            double y = _verticalEnabled ? delta.Y + _lineRemainder.Y : 0;
            horizontal = (int)Math.Truncate(x); vertical = (int)Math.Truncate(y);
            _lineRemainder = new Vector(x - horizontal, y - vertical);
            return true;
        }

        internal Vector TakeLineOverflow(ref int horizontal, ref int vertical, PortableScrollLifetime lifetime = null)
        {
            // Read both axes before changing fractions or invoking a provider.
            // Line commands do not declare a point distance; only a published
            // boundary can reject the remaining commands, never an inferred ratio.
            double x = _info.HorizontalOffset, width = _info.ExtentWidth, viewportWidth = _info.ViewportWidth;
            double y = _info.VerticalOffset, height = _info.ExtentHeight, viewportHeight = _info.ViewportHeight;
            if (!ValidMetrics(x, width, viewportWidth) || !ValidMetrics(y, height, viewportHeight))
                throw new InvalidOperationException("Native scrolling requires finite nonnegative IScrollInfo metrics.");
            if (!IsCurrent || lifetime?.IsCancelled == true) return default;
            Vector unused = default;
            double pendingX = horizontal + _lineRemainder.X;
            double pendingY = vertical + _lineRemainder.Y;
            if (AtBoundary(x, width, viewportWidth, pendingX))
            {
                unused.X = pendingX; horizontal = 0; _lineRemainder.X = 0;
            }
            if (AtBoundary(y, height, viewportHeight, pendingY))
            {
                unused.Y = pendingY; vertical = 0; _lineRemainder.Y = 0;
            }
            return unused;
        }

        private static bool AtBoundary(double offset, double extent, double viewport, double direction) =>
            direction < 0 ? offset <= 0 : direction > 0 && offset >= Math.Max(0, extent - viewport);

        internal void ApplyLine(bool horizontal, bool positive, PortableScrollLifetime lifetime = null)
        {
            if (!IsCurrent || lifetime?.IsCancelled == true) return;
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
        private bool _started, _completed;
        private int _horizontal, _vertical;
        private Vector _unconsumed;

        // Point values use the admitted provider's offset units, not native
        // source coordinates. Line values remain provider commands. A router
        // must retain the original frame conversion before forwarding either.
        internal Vector Unconsumed => _completed && _session.IsCurrent && _lifetime?.IsCancelled != true ?
            _unconsumed : default;

        internal PortableScrollCommand(PortableScrollSession session, PortablePointerScrollUnit unit, Vector delta,
            PortableScrollLifetime lifetime = null)
        { _session = session; _unit = unit; _delta = delta; _lifetime = lifetime; }

        // One line per existing layout/command pass preserves providers whose
        // actual offsets publish only after measure. Later commands cannot pass it.
        internal bool Advance()
        {
            if (_completed) return true;
            if (!_session.IsCurrent || _lifetime?.IsCancelled == true) return _completed = true;
            if (_unit == PortablePointerScrollUnit.Points)
            {
                _unconsumed = _session.ApplyPoints(_delta, _lifetime);
                return _completed = true;
            }
            if (!_started)
            {
                if (!_session.TryBeginLines(_delta, out _horizontal, out _vertical, out _unconsumed, _lifetime))
                    return _completed = true;
                _started = true;
            }
            _unconsumed += _session.TakeLineOverflow(ref _horizontal, ref _vertical, _lifetime);
            if (_horizontal != 0)
            {
                int direction = Math.Sign(_horizontal); _horizontal -= direction;
                _session.ApplyLine(true, direction > 0, _lifetime);
                return false;
            }
            else if (_vertical != 0)
            {
                int direction = Math.Sign(_vertical); _vertical -= direction;
                _session.ApplyLine(false, direction > 0, _lifetime);
                return false;
            }
            // Even the last issued line needs a layout pass: fractional input
            // left at its newly published boundary belongs to the overflow.
            return _completed = true;
        }
    }
}
