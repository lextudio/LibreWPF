// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Numerics;
using System.Windows.Input;
using System.Windows.Media;
using LibreWinForms.Platform;
using ProGPU.Wpf.Interop;

using SD = System.Drawing;
using SWF = System.Windows.Forms;

namespace System.Windows.Forms.Integration
{
    // The canonical WinForms of this fork (LibreWinForms) gives every control other than a Form a
    // logical handle: there is no child HWND to parent, on any OS. The host therefore draws the control
    // tree into its own DrawingContext and forwards pointer input to it, instead of building a window.
    public partial class WindowsFormsHost
    {
        private bool _visualHostInitialized;

        /// <summary>
        ///     The hosted control has no window of its own (LibreWinForms), so the host owns its visual
        ///     content under a native HwndSource as well as under a portable one.
        /// </summary>
        protected override bool UsesPortableVisualHosting => true;

        private void EnsureVisualHost()
        {
            if (_visualHostInitialized)
            {
                return;
            }

            _visualHostInitialized = true;
            _hostContainerInternal.CreateControl();
            _hostContainerInternal.Invalidated += (_, _) => Dispatcher.BeginInvoke(new Action(InvalidateVisual));
            Loaded += ApplyAllProperties;
            _propertyMap.ApplyAll();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            EnsureVisualHost();
        }

        // WinForms pixels per WPF device-independent unit, the factor Convert applies to layout sizes.
        private double VisualPixelsPerUnitX => Math.Max(_currentScale.X, 1e-6) * GetDpi().DpiScaleX;

        private double VisualPixelsPerUnitY => Math.Max(_currentScale.Y, 1e-6) * GetDpi().DpiScaleY;

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (Child == null)
            {
                return;
            }

            // Everything up to the native paint below is about being a usable element, not about pixels,
            // so it must not depend on the drawing context being a native one: the managed composition
            // path hands OnRender a plain VisualDrawingContext, and returning there left the container
            // unsized and the element without a hit region. A host that has a real child window (the HWND
            // path) is left alone: that window owns its size and its input.
            if (Handle == IntPtr.Zero)
            {
                // Nothing sizes the container in this path: ArrangeOverride gives the hosted control its
                // pixel size and the container stays 0x0, which paints nothing and gives the WinForms hit
                // test no bounds. The container follows the hosted control, which sits at its origin.
                SD.Size childSize = Child.Size;
                if (childSize.Width > 0 && childSize.Height > 0)
                {
                    if (_hostContainerInternal.Size != childSize)
                    {
                        _hostContainerInternal.Size = childSize;
                    }

                    // WPF records no drawing for content painted through the native context, so the input
                    // hit test finds nothing at the element: an HwndHost normally gets its input from the
                    // child HWND, and this host has none. A transparent rectangle gives it the hit region
                    // its content occupies, which routes mouse events to OnMouseDown below.
                    drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
                }
            }

            if (!TryGetNativeDrawingContext(drawingContext, out ProGPU.Scene.DrawingContext nativeContext, out Matrix4x4 outerTransform))
            {
                return;
            }

            SD.Size size = _hostContainerInternal.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            // Draw in WinForms pixels, scaled down into WPF units.
            Matrix4x4 transform = Matrix4x4.CreateScale((float)(1d / VisualPixelsPerUnitX), (float)(1d / VisualPixelsPerUnitY), 1f) * outerTransform;
            using SD.Graphics graphics = SD.Graphics.FromProGpuDrawingContext(nativeContext, transform);
            LibreControlHosting.Paint(_hostContainerInternal, graphics, new SD.Rectangle(SD.Point.Empty, size));
        }

        private static bool TryGetNativeDrawingContext(
            DrawingContext drawingContext,
            out ProGPU.Scene.DrawingContext nativeContext,
            out Matrix4x4 outerTransform)
        {
            nativeContext = null;
            outerTransform = Matrix4x4.Identity;
            if (drawingContext is IPortableNativeDrawingContextStateSource stateSource
                && stateSource.TryGetPortableNativeDrawingContextState(out var portableState)
                && ProGPU.Scene.ProGpuDrawingContextState.TryCreate(
                    portableState.NativeDrawingContext,
                    portableState.Transform,
                    out ProGPU.Scene.ProGpuDrawingContextState state))
            {
                nativeContext = state.DrawingContext;
                outerTransform = state.OuterTransform;
                return true;
            }

            if (drawingContext is IPortableNativeDrawingContextSource source
                && source.TryGetPortableNativeDrawingContext(out object resolved)
                && resolved is ProGPU.Scene.DrawingContext context)
            {
                nativeContext = context;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Alt+letter: let the hosted controls claim the mnemonic (a label's or button's underlined key).
        /// </summary>
        protected override bool OnMnemonicCore(ref System.Windows.Interop.MSG msg, ModifierKeys modifiers)
        {
            if (Child == null)
            {
                return false;
            }

            char charCode = (char)(msg.wParam.ToInt64() & 0xFFFF);
            return charCode != '\0' && _hostContainerInternal.TryProcessMnemonic(charCode);
        }

        private LibrePoint ToWinFormsPoint(System.Windows.Input.MouseEventArgs e)
        {
            Point position = e.GetPosition(this);
            return new LibrePoint(
                Convert.ToBoundedInt(position.X * VisualPixelsPerUnitX),
                Convert.ToBoundedInt(position.Y * VisualPixelsPerUnitY));
        }

        private static LibrePointerButton ToLibreButton(MouseButton button) => button switch
        {
            MouseButton.Left => LibrePointerButton.Primary,
            MouseButton.Right => LibrePointerButton.Secondary,
            MouseButton.Middle => LibrePointerButton.Middle,
            MouseButton.XButton1 => LibrePointerButton.XButton1,
            MouseButton.XButton2 => LibrePointerButton.XButton2,
            _ => LibrePointerButton.None,
        };

        private static LibreInputModifiers CurrentModifiers()
        {
            ModifierKeys keys = Keyboard.Modifiers;
            LibreInputModifiers modifiers = LibreInputModifiers.None;
            if ((keys & ModifierKeys.Shift) != 0) modifiers |= LibreInputModifiers.Shift;
            if ((keys & ModifierKeys.Control) != 0) modifiers |= LibreInputModifiers.Control;
            if ((keys & ModifierKeys.Alt) != 0) modifiers |= LibreInputModifiers.Alt;
            if ((keys & ModifierKeys.Windows) != 0) modifiers |= LibreInputModifiers.Meta;
            return modifiers;
        }

        private void ForwardPointer(LibreInputEventKind kind, System.Windows.Input.MouseEventArgs e, LibrePointerButton button)
        {
            if (Child == null)
            {
                return;
            }

            LibreControlHosting.DispatchInput(
                _hostContainerInternal,
                new LibreInputEvent(kind, Environment.TickCount64, CurrentModifiers(), default, null, ToWinFormsPoint(e), default, button));
        }

        private static LibreKey ToLibreKey(Key key)
        {
            switch (key)
            {
                case Key.Return: return LibreKey.Enter;
                case Key.Back: return LibreKey.Backspace;
                case Key.Prior: return LibreKey.PageUp;
                case Key.Next: return LibreKey.PageDown;
                case Key.Oem1: return LibreKey.Semicolon;
                case Key.OemPlus: return LibreKey.Equal;
                case Key.OemComma: return LibreKey.Comma;
                case Key.OemMinus: return LibreKey.Minus;
                case Key.OemPeriod: return LibreKey.Period;
                case Key.Oem2: return LibreKey.Slash;
                case Key.Oem3: return LibreKey.GraveAccent;
                case Key.Oem4: return LibreKey.LeftBracket;
                case Key.Oem5: return LibreKey.Backslash;
                case Key.Oem6: return LibreKey.RightBracket;
                case Key.Oem7: return LibreKey.Apostrophe;
                case Key.Decimal: return LibreKey.NumPadDecimal;
                case Key.Divide: return LibreKey.NumPadDivide;
                case Key.Multiply: return LibreKey.NumPadMultiply;
                case Key.Subtract: return LibreKey.NumPadSubtract;
                case Key.Add: return LibreKey.NumPadAdd;
            }

            // Letters, digits (D0-D9), arrows, F-keys, NumPad0-9, Space, Tab, Escape, Home, End, Insert,
            // Delete and the modifiers share their names with LibreKey.
            return Enum.TryParse(key.ToString(), out LibreKey libreKey) ? libreKey : LibreKey.Unknown;
        }

        private void ForwardKey(LibreInputEventKind kind, System.Windows.Input.KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            LibreControlHosting.DispatchInput(
                _hostContainerInternal,
                new LibreInputEvent(kind, Environment.TickCount64, CurrentModifiers(), ToLibreKey(key), null, default, default, LibrePointerButton.None));
        }

        // Tab moves between the hosted controls while there is a next tab stop in that direction. At either
        // end the key is left unhandled so WPF takes focus out of the host (the WinForms adapter never wraps).
        private bool IsTabKey(System.Windows.Input.KeyEventArgs e) =>
            e.Key == Key.Tab && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == ModifierKeys.None;

        private bool TryTabWithinChild()
        {
            bool forward = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.None;

            // The adapter's ActiveControl is the hosted root, not the control that has focus: every
            // container on the way down tracks its own active control, so the chain ends at the control
            // and at the container that owns it.
            Control current = _hostContainerInternal.ActiveControl;
            Control container = _hostContainerInternal;
            for (int depth = 0; depth < 64 && current is ContainerControl nested; depth++)
            {
                Control active = nested.ActiveControl;
                if (active is null || ReferenceEquals(active, nested))
                {
                    break;
                }

                container = nested;
                current = active;
            }

            // Tab moves inside the container that owns the focused control first. When that container has
            // no tab stop left in this direction, the container itself is the position to continue from in
            // its own parent - that is how a stop after a nested container is reached. The adapter does not
            // wrap, so the end of the tree is reported unhandled and WPF moves focus out of the host.
            while (true)
            {
                if (container.SelectNextControl(current, forward, true, false, false))
                {
                    return true;
                }

                if (ReferenceEquals(container, _hostContainerInternal))
                {
                    return false;
                }

                current = container;
                container = container.Parent as ContainerControl ?? _hostContainerInternal;
            }
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Child == null)
            {
                return;
            }

            if (IsTabKey(e))
            {
                e.Handled = TryTabWithinChild();
                return;
            }

            ForwardKey(LibreInputEventKind.KeyDown, e);
            e.Handled = true;
        }

        protected override void OnKeyUp(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (Child == null || IsTabKey(e))
            {
                return;
            }

            ForwardKey(LibreInputEventKind.KeyUp, e);
            e.Handled = true;
        }

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            base.OnTextInput(e);
            if (Child == null || string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            LibreControlHosting.DispatchInput(
                _hostContainerInternal,
                new LibreInputEvent(LibreInputEventKind.TextInput, Environment.TickCount64, CurrentModifiers(), default, e.Text, default, default, LibrePointerButton.None));
            e.Handled = true;
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            LibreControlHosting.DispatchInput(
                _hostContainerInternal,
                new LibreInputEvent(LibreInputEventKind.FocusGained, Environment.TickCount64, LibreInputModifiers.None, default, null, default, default, LibrePointerButton.None));
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            LibreControlHosting.DispatchInput(
                _hostContainerInternal,
                new LibreInputEvent(LibreInputEventKind.FocusLost, Environment.TickCount64, LibreInputModifiers.None, default, null, default, default, LibrePointerButton.None));
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            ForwardPointer(LibreInputEventKind.PointerDown, e, ToLibreButton(e.ChangedButton));
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            ForwardPointer(LibreInputEventKind.PointerUp, e, ToLibreButton(e.ChangedButton));
        }

        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ForwardPointer(LibreInputEventKind.PointerMove, e, LibrePointerButton.None);
        }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            ForwardPointer(LibreInputEventKind.PointerLeave, e, LibrePointerButton.None);
        }
    }
}
