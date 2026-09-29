// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;

namespace System.Windows.Input
{
    // Keep native reference data out of the original raw report's storage. Every
    // split retains one immutable packet and its independently mapped client point.
    internal sealed class PortableMouseInputReport : RawMouseInputReport
    {
        private readonly ulong _sourceGeneration;
        private readonly PortablePresentationSource _originSource;
        private readonly ulong _originGeneration;

        internal PortableMouseInputReport(InputMode mode, int timestamp, PresentationSource source,
            RawMouseActions actions, int x, int y, int wheel, IntPtr extraInformation,
            Point clientPoint, PortablePointerInput input, PortablePresentationSource originSource = null)
            : this(mode, timestamp, source, actions, x, y, wheel, extraInformation, clientPoint, input,
                source is PortablePresentationSource portable ? portable.PointerInputGeneration
                    : throw new ArgumentException("Native pointer input requires its portable source.", nameof(source)),
                originSource ?? (PortablePresentationSource)source,
                (originSource ?? (PortablePresentationSource)source).PointerInputGeneration)
        {
            ArgumentNullException.ThrowIfNull(input);
        }

        private PortableMouseInputReport(InputMode mode, int timestamp, PresentationSource source,
            RawMouseActions actions, int x, int y, int wheel, IntPtr extraInformation,
            Point clientPoint, PortablePointerInput input, ulong sourceGeneration,
            PortablePresentationSource originSource, ulong originGeneration)
            : base(mode, timestamp, source, actions, x, y, wheel, extraInformation)
        {
            if (!double.IsFinite(clientPoint.X) || !double.IsFinite(clientPoint.Y))
                throw new ArgumentOutOfRangeException(nameof(clientPoint));
            ClientPoint = clientPoint;
            NativePointer = input;
            _sourceGeneration = sourceGeneration;
            _originSource = originSource;
            _originGeneration = originGeneration;
        }

        internal override Point ClientPoint { get; }
        internal override PortablePointerInput NativePointer { get; }
        internal override bool IsCurrent => InputSource is PortablePresentationSource source &&
            !source.IsDisposed && source.PointerInputGeneration == _sourceGeneration &&
            !_originSource.IsDisposed && _originSource.PointerInputGeneration == _originGeneration;

        internal static RawMouseInputReport Synchronize(int timestamp, PortablePresentationSource source, Point clientPoint) =>
            new PortableMouseInputReport(InputMode.Foreground, timestamp, source, RawMouseActions.AbsoluteMove,
                (int)clientPoint.X, (int)clientPoint.Y, 0, IntPtr.Zero, clientPoint, null,
                source.PointerInputGeneration, source, source.PointerInputGeneration)
            {
                // This is source synchronization, not another physical native
                // event. Preserve the double frame without inventing a packet.
                _isSynchronize = true
            };

        internal override RawMouseInputReport WithActions(RawMouseActions actions, int x, int y, int wheel, IntPtr extraInformation) =>
            new PortableMouseInputReport(Mode, Timestamp, InputSource, actions, x, y, wheel,
                extraInformation, ClientPoint, NativePointer, _sourceGeneration, _originSource, _originGeneration)
            {
                // Activation seeds the last point. Its following exact move
                // must still hit-test, without an integer-only synthetic move.
                _isSynchronize = _isSynchronize || ((Actions & RawMouseActions.Activate) != 0 &&
                    (actions & RawMouseActions.AbsoluteMove) != 0)
            };
    }

    internal interface IPortableMouseEventData
    {
        PortableMouseInputReport NativeReport { get; }
    }

    internal sealed class PortableMouseEventArgs : MouseEventArgs, IPortableMouseEventData
    {
        internal PortableMouseEventArgs(MouseDevice mouse, int timestamp, StylusDevice stylus, PortableMouseInputReport input)
            : base(mouse, timestamp, stylus) => NativeReport = input;

        public PortableMouseInputReport NativeReport { get; }
    }

    internal sealed class PortableMouseButtonEventArgs : MouseButtonEventArgs, IPortableMouseEventData
    {
        internal PortableMouseButtonEventArgs(MouseDevice mouse, int timestamp, MouseButton button,
            StylusDevice stylus, PortableMouseInputReport input) : base(mouse, timestamp, button, stylus)
        {
            NativeReport = input;
            if (input.NativePointer != null) ClickCount = input.NativePointer.ClickCount;
        }

        public PortableMouseInputReport NativeReport { get; }
    }

    internal static class PortableMouseEvents
    {
        internal static PortablePointerInput GetNativePointer(MouseEventArgs input) =>
            GetNativeReport(input)?.NativePointer;

        internal static PortableMouseInputReport GetNativeReport(MouseEventArgs input) =>
            (input as IPortableMouseEventData)?.NativeReport;

        internal static MouseEventArgs Move(MouseDevice mouse, int timestamp, StylusDevice stylus,
            PortableMouseInputReport input, RoutedEvent routedEvent)
        {
            MouseEventArgs result = input == null
                ? new MouseEventArgs(mouse, timestamp, stylus)
                : new PortableMouseEventArgs(mouse, timestamp, stylus, input);
            result.RoutedEvent = routedEvent;
            return result;
        }

        internal static MouseButtonEventArgs Button(MouseDevice mouse, int timestamp, MouseButton button,
            StylusDevice stylus, PortableMouseInputReport input, RoutedEvent routedEvent, int? clickCount = null)
        {
            MouseButtonEventArgs result = input == null
                ? new MouseButtonEventArgs(mouse, timestamp, button, stylus)
                : new PortableMouseButtonEventArgs(mouse, timestamp, button, stylus, input);
            result.RoutedEvent = routedEvent;
            if (clickCount.HasValue) result.ClickCount = clickCount.Value;
            return result;
        }
    }
}
