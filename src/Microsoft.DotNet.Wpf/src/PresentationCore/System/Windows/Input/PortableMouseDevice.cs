// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Windows.Input
{
    internal sealed class PortableMouseDevice : MouseDevice
    {
        private readonly Dictionary<MouseButton, (PresentationSource Origin, PresentationSource Routed)> _pressedButtons = new();
        private WeakReference<PortablePresentationSource> _nativePointerOrigin;
        internal ulong NativePointerRevision { get; private set; }
        internal bool NativePointerOutside { get; private set; }

        internal bool IsNativePointerOrigin(PortablePresentationSource source) =>
            _nativePointerOrigin != null && _nativePointerOrigin.TryGetTarget(out var origin) && ReferenceEquals(origin, source);

        internal void RecordPointerPosition(RawMouseInputReport report)
        {
            // Synchronization reuses a position; it is not physical re-entry.
            unchecked { ++NativePointerRevision; }
            if (report._isSynchronize && report.NativePointer == null) return;
            NativePointerOutside = false;
            if (report is PortableMouseInputReport native && native.NativePointer != null)
            {
                if (_nativePointerOrigin == null) _nativePointerOrigin = new(native.OriginSource);
                else _nativePointerOrigin.SetTarget(native.OriginSource);
            }
            else _nativePointerOrigin?.SetTarget(null);
        }

        internal void RecordPointerLeave()
        {
            unchecked { ++NativePointerRevision; }
            NativePointerOutside = true;
        }

        internal PortableMouseDevice(InputManager inputManager)
            : base(inputManager)
        {
        }

        internal void SetButtonState(MouseButton button, MouseButtonState buttonState,
            PresentationSource origin, PresentationSource routed)
        {
            VerifyAccess();
            ArgumentNullException.ThrowIfNull(origin);
            ArgumentNullException.ThrowIfNull(routed);
            origin.VerifyAccess();
            routed.VerifyAccess();
            if (button < MouseButton.Left || button > MouseButton.XButton2)
                throw new ArgumentOutOfRangeException(nameof(button));
            if (buttonState != MouseButtonState.Pressed && buttonState != MouseButtonState.Released)
                throw new ArgumentOutOfRangeException(nameof(buttonState));
            ObjectDisposedException.ThrowIf(origin.IsDisposed, origin);
            ObjectDisposedException.ThrowIf(routed.IsDisposed, routed);

            if (buttonState == MouseButtonState.Released)
            {
                // A real up may arrive through another surface after crossing
                // into a popup. It remains authoritative even without a local down.
                _pressedButtons.Remove(button);
            }
            else
            {
                _pressedButtons[button] = (origin, routed);
            }
        }

        internal void ReleaseSourceButtons(PresentationSource source)
        {
            VerifyAccess();
            ArgumentNullException.ThrowIfNull(source);
            source.VerifyAccess();
            // No synthetic mouse-up/click, and no clearing another source's
            // later press. Keep original and capture-routed ownership distinct.
            for (MouseButton button = MouseButton.Left; button <= MouseButton.XButton2; button++)
            {
                if (_pressedButtons.TryGetValue(button, out var owner) &&
                    (ReferenceEquals(owner.Origin, source) || ReferenceEquals(owner.Routed, source)))
                    _pressedButtons.Remove(button);
            }
        }

        internal override MouseButtonState GetButtonStateFromSystem(MouseButton mouseButton)
        {
            return _pressedButtons.ContainsKey(mouseButton)
                ? MouseButtonState.Pressed
                : MouseButtonState.Released;
        }
    }
}
