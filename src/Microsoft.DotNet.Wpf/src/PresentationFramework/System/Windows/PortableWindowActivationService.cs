// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Windows.Input;

namespace System.Windows
{
    internal static class PortableWindowActivationService
    {
        private static Func<object, object> _activate;
        private static Action<object> _show;
        private static Action<object> _hide;
        private static Action<object, object> _setWindowState;
        private static Action<object, string> _setTitle;
        private static Action<object, double, double> _setClientSize;
        private static Action<object> _close;
        private static Action<object> _run;
        private static Action<object> _dispose;

        internal static bool IsEnabled
        {
            get
            {
                return !OperatingSystem.IsWindows() && Volatile.Read(ref _activate) != null;
            }
        }

        internal static void Register(
            Func<object, object> activate,
            Action<object> show = null,
            Action<object> hide = null,
            Action<object, object> setWindowState = null,
            Action<object, string> setTitle = null,
            Action<object, double, double> setClientSize = null,
            Action<object> close = null,
            Action<object> run = null,
            Action<object> dispose = null)
        {
            ArgumentNullException.ThrowIfNull(activate);

            Volatile.Write(ref _activate, activate);
            Volatile.Write(ref _show, show);
            Volatile.Write(ref _hide, hide);
            Volatile.Write(ref _setWindowState, setWindowState);
            Volatile.Write(ref _setTitle, setTitle);
            Volatile.Write(ref _setClientSize, setClientSize);
            Volatile.Write(ref _close, close);
            Volatile.Write(ref _run, run);
            Volatile.Write(ref _dispose, dispose);
        }

        internal static void Clear()
        {
            Volatile.Write(ref _activate, null);
            Volatile.Write(ref _show, null);
            Volatile.Write(ref _hide, null);
            Volatile.Write(ref _setWindowState, null);
            Volatile.Write(ref _setTitle, null);
            Volatile.Write(ref _setClientSize, null);
            Volatile.Write(ref _close, null);
            Volatile.Write(ref _run, null);
            Volatile.Write(ref _dispose, null);
        }

        internal static bool TryActivate(Window window, out object activation)
        {
            activation = null;

            if (OperatingSystem.IsWindows())
            {
                return false;
            }

            Func<object, object> activate = Volatile.Read(ref _activate);
            if (activate == null)
            {
                return false;
            }

            activation = activate(window);
            return activation != null;
        }

        internal static void Show(object activation)
        {
            Volatile.Read(ref _show)?.Invoke(activation);
        }

        internal static void Hide(object activation)
        {
            Volatile.Read(ref _hide)?.Invoke(activation);
        }

        internal static void SetWindowState(object activation, WindowState windowState)
        {
            Volatile.Read(ref _setWindowState)?.Invoke(activation, windowState);
        }

        internal static void SetTitle(object activation, string title)
        {
            Volatile.Read(ref _setTitle)?.Invoke(activation, title);
        }

        internal static void SetClientSize(object activation, double width, double height)
        {
            Volatile.Read(ref _setClientSize)?.Invoke(activation, width, height);
        }

        internal static void SetActivationState(Window window, bool isActive)
        {
            if (OperatingSystem.IsWindows() || window == null)
            {
                return;
            }

            window.HandleActivate(isActive);
        }

        internal static void ProcessInput(Window window, PortableInputEventArgs input)
        {
            if (OperatingSystem.IsWindows() || window == null || input == null)
            {
                return;
            }

            PresentationSource source = PresentationSource.CriticalFromVisual(window);
            if (source == null)
            {
                return;
            }

            input.Handled = ProcessInput(source, input);
        }

        private static bool ProcessInput(PresentationSource source, PortableInputEventArgs input)
        {
            InputManager inputManager = InputManager.UnsecureCurrent;
            int timestamp = Environment.TickCount;

            switch (input.Kind)
            {
                case PortableInputEventKind.KeyDown:
                    return ProcessKeyboardInput(inputManager, source, input, timestamp, isDown: true);
                case PortableInputEventKind.KeyUp:
                    return ProcessKeyboardInput(inputManager, source, input, timestamp, isDown: false);
                case PortableInputEventKind.TextInput:
                    return ProcessTextInput(inputManager, source, input, timestamp);
                case PortableInputEventKind.MouseMove:
                    return ProcessMouseInput(inputManager, source, input, timestamp, RawMouseActions.Activate | RawMouseActions.AbsoluteMove);
                case PortableInputEventKind.MouseDown:
                    return TryGetMouseButtonAction(input.Button, isDown: true, out RawMouseActions mouseDownAction)
                        && ProcessMouseInput(inputManager, source, input, timestamp, RawMouseActions.Activate | RawMouseActions.AbsoluteMove | mouseDownAction);
                case PortableInputEventKind.MouseUp:
                    return TryGetMouseButtonAction(input.Button, isDown: false, out RawMouseActions mouseUpAction)
                        && ProcessMouseInput(inputManager, source, input, timestamp, RawMouseActions.Activate | RawMouseActions.AbsoluteMove | mouseUpAction);
                case PortableInputEventKind.MouseWheel:
                    int wheel = ToMouseWheelDelta(input.DeltaY);
                    return wheel != 0
                        && ProcessMouseInput(inputManager, source, input, timestamp, RawMouseActions.Activate | RawMouseActions.AbsoluteMove | RawMouseActions.VerticalWheelRotate, wheel);
                default:
                    return false;
            }
        }

        private static bool ProcessKeyboardInput(
            InputManager inputManager,
            PresentationSource source,
            PortableInputEventArgs input,
            int timestamp,
            bool isDown)
        {
            if (!TryGetKey(input.Key, out Key key) || key == Key.None)
            {
                return false;
            }

            if (inputManager.PrimaryKeyboardDevice is PortableKeyboardDevice keyboardDevice)
            {
                UpdateModifierKeyStates(keyboardDevice, input.Modifiers);
                keyboardDevice.SetKeyStates(key, isDown ? KeyStates.Down : KeyStates.None);
            }

            RawKeyboardInputReport report = new RawKeyboardInputReport(
                source,
                InputMode.Foreground,
                timestamp,
                RawKeyboardActions.Activate | (isDown ? RawKeyboardActions.KeyDown : RawKeyboardActions.KeyUp),
                input.ScanCode,
                IsExtendedKey(key),
                IsSystemKey(key, input.Modifiers),
                KeyInterop.VirtualKeyFromKey(key),
                IntPtr.Zero);

            return ProcessInputReport(inputManager, report);
        }

        private static bool ProcessTextInput(
            InputManager inputManager,
            PresentationSource source,
            PortableInputEventArgs input,
            int timestamp)
        {
            if (input.Character is not char character)
            {
                return false;
            }

            RawTextInputReport report = new RawTextInputReport(
                source,
                InputMode.Foreground,
                timestamp,
                isDeadCharacter: false,
                isSystemCharacter: (input.Modifiers & PortableInputModifiers.Alt) == PortableInputModifiers.Alt,
                isControlCharacter: char.IsControl(character),
                character);

            return ProcessInputReport(inputManager, report);
        }

        private static bool ProcessMouseInput(
            InputManager inputManager,
            PresentationSource source,
            PortableInputEventArgs input,
            int timestamp,
            RawMouseActions actions,
            int wheel = 0)
        {
            if (inputManager.PrimaryMouseDevice is PortableMouseDevice mouseDevice &&
                TryGetMouseButton(input.Button, out MouseButton mouseButton))
            {
                if ((actions & GetMouseButtonPressAction(mouseButton)) != 0)
                {
                    mouseDevice.SetButtonState(mouseButton, MouseButtonState.Pressed);
                }
                else if ((actions & GetMouseButtonReleaseAction(mouseButton)) != 0)
                {
                    mouseDevice.SetButtonState(mouseButton, MouseButtonState.Released);
                }
            }

            RawMouseInputReport report = new RawMouseInputReport(
                InputMode.Foreground,
                timestamp,
                source,
                actions,
                ToInputCoordinate(input.X),
                ToInputCoordinate(input.Y),
                wheel,
                IntPtr.Zero);

            return ProcessInputReport(inputManager, report);
        }

        private static bool ProcessInputReport(InputManager inputManager, InputReport report)
        {
            InputReportEventArgs input = new InputReportEventArgs(null, report)
            {
                RoutedEvent = InputManager.PreviewInputReportEvent
            };

            return inputManager.ProcessInput(input);
        }

        private static void UpdateModifierKeyStates(PortableKeyboardDevice keyboardDevice, PortableInputModifiers modifiers)
        {
            SetModifierKeyState(keyboardDevice, Key.LeftShift, modifiers, PortableInputModifiers.Shift);
            SetModifierKeyState(keyboardDevice, Key.RightShift, modifiers, PortableInputModifiers.Shift);
            SetModifierKeyState(keyboardDevice, Key.LeftCtrl, modifiers, PortableInputModifiers.Control);
            SetModifierKeyState(keyboardDevice, Key.RightCtrl, modifiers, PortableInputModifiers.Control);
            SetModifierKeyState(keyboardDevice, Key.LeftAlt, modifiers, PortableInputModifiers.Alt);
            SetModifierKeyState(keyboardDevice, Key.RightAlt, modifiers, PortableInputModifiers.Alt);
            SetModifierKeyState(keyboardDevice, Key.LWin, modifiers, PortableInputModifiers.Super);
            SetModifierKeyState(keyboardDevice, Key.RWin, modifiers, PortableInputModifiers.Super);
        }

        private static void SetModifierKeyState(
            PortableKeyboardDevice keyboardDevice,
            Key key,
            PortableInputModifiers modifiers,
            PortableInputModifiers modifier)
        {
            keyboardDevice.SetKeyStates(
                key,
                (modifiers & modifier) == modifier ? KeyStates.Down : KeyStates.None);
        }

        private static bool TryGetKey(string keyName, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrWhiteSpace(keyName))
            {
                return false;
            }

            if (Enum.TryParse(keyName, ignoreCase: false, out key))
            {
                return true;
            }

            switch (keyName)
            {
                case "Backspace":
                    key = Key.Back;
                    return true;
                case "ShiftLeft":
                    key = Key.LeftShift;
                    return true;
                case "ShiftRight":
                    key = Key.RightShift;
                    return true;
                case "ControlLeft":
                    key = Key.LeftCtrl;
                    return true;
                case "ControlRight":
                    key = Key.RightCtrl;
                    return true;
                case "AltLeft":
                    key = Key.LeftAlt;
                    return true;
                case "AltRight":
                    key = Key.RightAlt;
                    return true;
                case "SuperLeft":
                    key = Key.LWin;
                    return true;
                case "SuperRight":
                    key = Key.RWin;
                    return true;
            }

            if (keyName.Length == 7 &&
                keyName.StartsWith("Number", StringComparison.Ordinal) &&
                char.IsDigit(keyName[6]))
            {
                return Enum.TryParse("D" + keyName[6], ignoreCase: false, out key);
            }

            if (keyName.Length == 7 &&
                keyName.StartsWith("Keypad", StringComparison.Ordinal) &&
                char.IsDigit(keyName[6]))
            {
                return Enum.TryParse("NumPad" + keyName[6], ignoreCase: false, out key);
            }

            return false;
        }

        private static bool TryGetMouseButtonAction(
            PortableMouseButton portableButton,
            bool isDown,
            out RawMouseActions action)
        {
            action = RawMouseActions.None;
            if (!TryGetMouseButton(portableButton, out MouseButton button))
            {
                return false;
            }

            action = isDown ? GetMouseButtonPressAction(button) : GetMouseButtonReleaseAction(button);
            return action != RawMouseActions.None;
        }

        private static bool TryGetMouseButton(PortableMouseButton portableButton, out MouseButton button)
        {
            switch (portableButton)
            {
                case PortableMouseButton.Left:
                    button = MouseButton.Left;
                    return true;
                case PortableMouseButton.Right:
                    button = MouseButton.Right;
                    return true;
                case PortableMouseButton.Middle:
                    button = MouseButton.Middle;
                    return true;
                case PortableMouseButton.XButton1:
                    button = MouseButton.XButton1;
                    return true;
                case PortableMouseButton.XButton2:
                    button = MouseButton.XButton2;
                    return true;
                default:
                    button = MouseButton.Left;
                    return false;
            }
        }

        private static RawMouseActions GetMouseButtonPressAction(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => RawMouseActions.Button1Press,
                MouseButton.Right => RawMouseActions.Button2Press,
                MouseButton.Middle => RawMouseActions.Button3Press,
                MouseButton.XButton1 => RawMouseActions.Button4Press,
                MouseButton.XButton2 => RawMouseActions.Button5Press,
                _ => RawMouseActions.None
            };
        }

        private static RawMouseActions GetMouseButtonReleaseAction(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => RawMouseActions.Button1Release,
                MouseButton.Right => RawMouseActions.Button2Release,
                MouseButton.Middle => RawMouseActions.Button3Release,
                MouseButton.XButton1 => RawMouseActions.Button4Release,
                MouseButton.XButton2 => RawMouseActions.Button5Release,
                _ => RawMouseActions.None
            };
        }

        private static bool IsExtendedKey(Key key)
        {
            return key == Key.RightAlt ||
                key == Key.RightCtrl ||
                key == Key.Insert ||
                key == Key.Delete ||
                key == Key.Home ||
                key == Key.End ||
                key == Key.Prior ||
                key == Key.Next ||
                key == Key.Left ||
                key == Key.Right ||
                key == Key.Up ||
                key == Key.Down;
        }

        private static bool IsSystemKey(Key key, PortableInputModifiers modifiers)
        {
            return key == Key.LeftAlt ||
                key == Key.RightAlt ||
                (modifiers & PortableInputModifiers.Alt) == PortableInputModifiers.Alt;
        }

        private static int ToMouseWheelDelta(double delta)
        {
            return ToInputCoordinate(delta * Mouse.MouseWheelDeltaForOneLine);
        }

        private static int ToInputCoordinate(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            if (value >= int.MaxValue)
            {
                return int.MaxValue;
            }

            if (value <= int.MinValue)
            {
                return int.MinValue;
            }

            return (int)Math.Round(value);
        }

        internal static void Close(object activation)
        {
            Volatile.Read(ref _close)?.Invoke(activation);
        }

        internal static bool TryRun(Window window)
        {
            if (OperatingSystem.IsWindows() || window == null)
            {
                return false;
            }

            object activation = window.PortableWindowActivation;
            if (activation == null)
            {
                return false;
            }

            Action<object> run = Volatile.Read(ref _run);
            if (run == null)
            {
                return false;
            }

            run(activation);
            return true;
        }

        internal static void Dispose(object activation)
        {
            Action<object> dispose = Volatile.Read(ref _dispose);
            if (dispose != null)
            {
                dispose(activation);
            }
            else if (activation is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
