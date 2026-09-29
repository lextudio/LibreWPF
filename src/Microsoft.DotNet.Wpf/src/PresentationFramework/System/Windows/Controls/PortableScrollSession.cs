// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows.Controls
{
    internal sealed class PortableScrollLifetime
    {
        internal bool IsCancelled { get; private set; }
        internal void Cancel() => IsCancelled = true;
    }

    [Flags]
    internal enum PortableScrollAxes { Pixels = 0, HorizontalItems = 1, VerticalItems = 2 }

    // IScrollInfo deliberately does not declare the units of its offsets. Source
    // implementations publish that fact; CanContentScroll alone cannot prove it.
    internal interface IPortableScrollInfo
    {
        PortableScrollAxes ScrollAxes { get; }
    }

    internal sealed class PortableScrollSession
    {
        internal const int MaximumLinesPerPacket = 4096;
        private readonly ScrollViewer _owner;
        private readonly IScrollInfo _info;
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
            if (!owner.IsEnabled || !owner.IsVisible || !owner.HandlesMouseWheelScrolling || owner.ScrollInfo is not IPortableScrollInfo units ||
                !ReferenceEquals(owner.ScrollInfo.ScrollOwner, owner) ||
                PresentationSource.CriticalFromVisual(owner) is not PortablePresentationSource source || source.IsDisposed ||
                (units.ScrollAxes & ~(PortableScrollAxes.HorizontalItems | PortableScrollAxes.VerticalItems)) != 0)
                return false;
            session = new PortableScrollSession(owner, owner.ScrollInfo, source, units.ScrollAxes, lifetime);
            return true;
        }

        internal bool IsCurrent => !_cancelled && !_source.IsDisposed &&
            _sourceGeneration == _source.PointerInputGeneration &&
            ReferenceEquals(_owner.ScrollInfo, _info) && ReferenceEquals(_info.ScrollOwner, _owner) &&
            ReferenceEquals(PresentationSource.CriticalFromVisual(_owner), _source) &&
            ((IPortableScrollInfo)_info).ScrollAxes == _axes &&
            _info.CanHorizontallyScroll == _horizontalEnabled && _info.CanVerticallyScroll == _verticalEnabled &&
            _owner.IsEnabled && _owner.IsVisible && _owner.HandlesMouseWheelScrolling &&
            PortableWindowActivationService.IsModalInputAllowed(_source.RootVisual as UIElement);

        internal void Cancel() { _owner.VerifyAccess(); _cancelled = true; }

        // The source router owns phase validation and target selection.
        // This consumer keeps native units; it never creates a MouseWheel delta.
        internal bool TryQueue(PortablePointerInput input)
            => TryQueue(input, out _);

        internal bool TryQueue(PortablePointerInput input, out bool queueFull)
            => TryQueue(input, Lifetime, out queueFull);

        internal bool TryQueue(PortablePointerInput input, PortableScrollLifetime lifetime, out bool queueFull)
        {
            queueFull = false;
            _owner.VerifyAccess();
            if (input == null || input.Kind != PortablePointerEventKind.Scroll || lifetime?.IsCancelled == true || !IsCurrent) return false;
            Vector delta = new(-input.ScrollX, -input.ScrollY);
            if (input.ScrollUnit == PortablePointerScrollUnit.Lines)
            {
                if (Math.Abs(delta.X) > MaximumLinesPerPacket || Math.Abs(delta.Y) > MaximumLinesPerPacket)
                    return false;
                if ((!_info.CanHorizontallyScroll && delta.X != 0) || (!_info.CanVerticallyScroll && delta.Y != 0))
                    return false;
            }
            else
            {
                if (!ValidMetrics(_info.HorizontalOffset, _info.ExtentWidth, _info.ViewportWidth) ||
                    !ValidMetrics(_info.VerticalOffset, _info.ExtentHeight, _info.ViewportHeight)) return false;
                // Point vectors belong to the source frame. Transform both ends
                // through the actual visual mapping, without scaling wheel lines.
                GeneralTransform transform = _source.RootVisual.TransformToDescendant(_owner);
                Point start = new(input.X, input.Y);
                if (!transform.TryTransform(start, out Point localStart) ||
                    !transform.TryTransform(start + delta, out Point localEnd)) return false;
                delta = localEnd - localStart;
                Vector scale = _owner.GetScrollPointScale(_info.ViewportWidth, _info.ViewportHeight, _axes);
                if (!TryScale(delta.X, scale.X, _info.CanHorizontallyScroll, out double x) ||
                    !TryScale(delta.Y, scale.Y, _info.CanVerticallyScroll, out double y)) return false;
                delta = new Vector(x, y);
            }
            if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y) || lifetime?.IsCancelled == true || !IsCurrent) return false;
            if (delta == default) return true;
            bool queued = _owner.TryEnqueuePortableScroll(new PortableScrollCommand(this, input.ScrollUnit, delta, lifetime));
            queueFull = !queued;
            return queued;
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

        internal void ApplyPoints(Vector delta, PortableScrollLifetime lifetime = null)
        {
            if (!IsCurrent || lifetime?.IsCancelled == true) return;
            SelectUnit(PortablePointerScrollUnit.Points);
            bool horizontal = _info.CanHorizontallyScroll, vertical = _info.CanVerticallyScroll;
            // Validate both axes before calling the application-owned provider.
            double x = PointOffset(_info.HorizontalOffset, _info.ExtentWidth, _info.ViewportWidth,
                horizontal ? delta.X : 0, _pointRemainder.X, (_axes & PortableScrollAxes.HorizontalItems) != 0, out double remainderX);
            double y = PointOffset(_info.VerticalOffset, _info.ExtentHeight, _info.ViewportHeight,
                vertical ? delta.Y : 0, _pointRemainder.Y, (_axes & PortableScrollAxes.VerticalItems) != 0, out double remainderY);
            _pointRemainder = new Vector(horizontal ? remainderX : 0, vertical ? remainderY : 0);
            if (horizontal && x != _info.HorizontalOffset) _info.SetHorizontalOffset(x);
            if (IsCurrent && lifetime?.IsCancelled != true && vertical && y != _info.VerticalOffset) _info.SetVerticalOffset(y);
        }

        private static double PointOffset(double offset, double extent, double viewport, double delta,
            double remainder, bool logical, out double nextRemainder)
        {
            if (!ValidMetrics(offset, extent, viewport) || !double.IsFinite(offset + delta + remainder))
                throw new InvalidOperationException("Native scrolling requires finite nonnegative IScrollInfo metrics.");
            double maximum = Math.Max(0, extent - viewport);
            double desired = Math.Clamp(offset + delta + remainder, 0, maximum);
            double result = logical ? Math.Clamp(Math.Round(desired), 0, maximum) : desired;
            nextRemainder = desired == 0 || desired == maximum ? 0 : desired - result;
            return result;
        }

        private static bool ValidMetrics(double offset, double extent, double viewport) =>
            double.IsFinite(offset) && double.IsFinite(extent) && double.IsFinite(viewport) &&
            offset >= 0 && extent >= 0 && viewport >= 0;

        internal void BeginLines(Vector delta, out int horizontal, out int vertical)
        {
            SelectUnit(PortablePointerScrollUnit.Lines);
            double x = _info.CanHorizontallyScroll ? delta.X + _lineRemainder.X : 0;
            double y = _info.CanVerticallyScroll ? delta.Y + _lineRemainder.Y : 0;
            horizontal = (int)Math.Truncate(x); vertical = (int)Math.Truncate(y);
            _lineRemainder = new Vector(x - horizontal, y - vertical);
        }

        internal void ApplyLine(bool horizontal, bool positive)
        {
            if (!IsCurrent) return;
            if (horizontal)
            {
                if (!_info.CanHorizontallyScroll) return;
                if (positive) _info.LineRight(); else _info.LineLeft();
            }
            else
            {
                if (!_info.CanVerticallyScroll) return;
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
        private bool _started;
        private int _horizontal, _vertical;

        internal PortableScrollCommand(PortableScrollSession session, PortablePointerScrollUnit unit, Vector delta,
            PortableScrollLifetime lifetime = null)
        { _session = session; _unit = unit; _delta = delta; _lifetime = lifetime; }

        // One line per existing layout/command pass preserves providers whose
        // actual offsets publish only after measure. Later commands cannot pass it.
        internal bool Advance()
        {
            if (!_session.IsCurrent || _lifetime?.IsCancelled == true) return true;
            if (_unit == PortablePointerScrollUnit.Points) { _session.ApplyPoints(_delta, _lifetime); return true; }
            if (!_started)
            {
                _started = true;
                _session.BeginLines(_delta, out _horizontal, out _vertical);
            }
            if (_horizontal != 0)
            {
                int direction = Math.Sign(_horizontal); _horizontal -= direction;
                _session.ApplyLine(true, direction > 0);
            }
            else if (_vertical != 0)
            {
                int direction = Math.Sign(_vertical); _vertical -= direction;
                _session.ApplyLine(false, direction > 0);
            }
            return _horizontal == 0 && _vertical == 0;
        }
    }
}
