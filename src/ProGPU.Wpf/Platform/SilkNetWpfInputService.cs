using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.Windowing;
using SilkInput = Silk.NET.Input;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class SilkNetWpfInputService : IWpfInputService
{
    public event EventHandler<WpfInputEventArgs>? InputReceived;

    public IDisposable Attach(object window)
    {
        if (window is not IView silkView)
        {
            throw new ArgumentException("Silk.NET input services require a Silk.NET view instance.", nameof(window));
        }

        return Attach(SilkInput.InputWindowExtensions.CreateInput(silkView));
    }

    public IDisposable Attach(SilkInput.IInputContext inputContext)
    {
        ArgumentNullException.ThrowIfNull(inputContext);

        var subscriptions = new List<Action>();
        var mouseSubscriptions = new Dictionary<SilkInput.IMouse, Action>();
        var keyboardSubscriptions = new Dictionary<SilkInput.IKeyboard, Action>();

        void TrackSubscription(Action unsubscribe)
        {
            subscriptions.Add(unsubscribe);
        }

        void AttachMouse(SilkInput.IMouse mouse)
        {
            if (!mouse.IsConnected || mouseSubscriptions.ContainsKey(mouse))
            {
                return;
            }

            Vector2 lastPosition = mouse.Position;
            bool hasLastPosition = IsFinite(lastPosition);
            Action<SilkInput.IMouse, Vector2> mouseMove = (_, position) =>
            {
                lastPosition = position;
                hasLastPosition = IsFinite(position);
                OnInputReceived(CreateMouseMoveEvent(position, ReadModifiers(inputContext)));
            };
            Action<SilkInput.IMouse, SilkInput.MouseButton> mouseDown = (_, button) =>
            {
                var position = ResolveMousePosition(mouse.Position, lastPosition, hasLastPosition);
                if (IsFinite(position))
                {
                    lastPosition = position;
                    hasLastPosition = true;
                }

                OnInputReceived(CreateMouseButtonEvent(WpfInputEventKind.MouseDown, button, position, ReadModifiers(inputContext)));
            };
            Action<SilkInput.IMouse, SilkInput.MouseButton> mouseUp = (_, button) =>
            {
                var position = ResolveMousePosition(mouse.Position, lastPosition, hasLastPosition);
                if (IsFinite(position))
                {
                    lastPosition = position;
                    hasLastPosition = true;
                }

                OnInputReceived(CreateMouseButtonEvent(WpfInputEventKind.MouseUp, button, position, ReadModifiers(inputContext)));
            };
            Action<SilkInput.IMouse, SilkInput.ScrollWheel> scroll = (_, wheel) =>
            {
                var position = ResolveMousePosition(mouse.Position, lastPosition, hasLastPosition);
                if (IsFinite(position))
                {
                    lastPosition = position;
                    hasLastPosition = true;
                }

                OnInputReceived(CreateMouseWheelEvent(wheel.X, wheel.Y, position, ReadModifiers(inputContext)));
            };

            mouse.MouseMove += mouseMove;
            mouse.MouseDown += mouseDown;
            mouse.MouseUp += mouseUp;
            mouse.Scroll += scroll;

            void Unsubscribe()
            {
                mouse.MouseMove -= mouseMove;
                mouse.MouseDown -= mouseDown;
                mouse.MouseUp -= mouseUp;
                mouse.Scroll -= scroll;
            }

            mouseSubscriptions.Add(mouse, Unsubscribe);
            TrackSubscription(Unsubscribe);
        }

        void DetachMouse(SilkInput.IMouse mouse)
        {
            if (!mouseSubscriptions.Remove(mouse, out var unsubscribe))
            {
                return;
            }

            unsubscribe();
            subscriptions.Remove(unsubscribe);
        }

        void AttachKeyboard(SilkInput.IKeyboard keyboard)
        {
            if (!keyboard.IsConnected || keyboardSubscriptions.ContainsKey(keyboard))
            {
                return;
            }

            Action<SilkInput.IKeyboard, SilkInput.Key, int> keyDown = (_, key, scanCode) =>
                OnInputReceived(CreateKeyEvent(WpfInputEventKind.KeyDown, key, scanCode, ReadModifiers(inputContext)));
            Action<SilkInput.IKeyboard, SilkInput.Key, int> keyUp = (_, key, scanCode) =>
                OnInputReceived(CreateKeyEvent(WpfInputEventKind.KeyUp, key, scanCode, ReadModifiers(inputContext)));
            Action<SilkInput.IKeyboard, char> keyChar = (_, character) =>
                OnInputReceived(CreateTextInputEvent(character, ReadModifiers(inputContext)));

            keyboard.BeginInput();
            keyboard.KeyDown += keyDown;
            keyboard.KeyUp += keyUp;
            keyboard.KeyChar += keyChar;

            void Unsubscribe()
            {
                keyboard.KeyDown -= keyDown;
                keyboard.KeyUp -= keyUp;
                keyboard.KeyChar -= keyChar;
                keyboard.EndInput();
            }

            keyboardSubscriptions.Add(keyboard, Unsubscribe);
            TrackSubscription(Unsubscribe);
        }

        void DetachKeyboard(SilkInput.IKeyboard keyboard)
        {
            if (!keyboardSubscriptions.Remove(keyboard, out var unsubscribe))
            {
                return;
            }

            unsubscribe();
            subscriptions.Remove(unsubscribe);
        }

        foreach (var mouse in inputContext.Mice)
        {
            AttachMouse(mouse);
        }

        foreach (var keyboard in inputContext.Keyboards)
        {
            AttachKeyboard(keyboard);
        }

        void ConnectionChanged(SilkInput.IInputDevice device, bool connected)
        {
            switch (device)
            {
                case SilkInput.IMouse mouse when connected:
                    AttachMouse(mouse);
                    break;
                case SilkInput.IMouse mouse:
                    DetachMouse(mouse);
                    break;
                case SilkInput.IKeyboard keyboard when connected:
                    AttachKeyboard(keyboard);
                    break;
                case SilkInput.IKeyboard keyboard:
                    DetachKeyboard(keyboard);
                    break;
            }
        }

        inputContext.ConnectionChanged += ConnectionChanged;
        TrackSubscription(() => inputContext.ConnectionChanged -= ConnectionChanged);

        return new InputSubscription(inputContext, subscriptions);
    }

    public static WpfInputEventArgs CreateKeyEvent(
        WpfInputEventKind kind,
        SilkInput.Key key,
        int scanCode,
        WpfInputModifiers modifiers)
    {
        if (kind != WpfInputEventKind.KeyDown && kind != WpfInputEventKind.KeyUp)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Key events must be KeyDown or KeyUp.");
        }

        return new WpfInputEventArgs(
            kind,
            key: TranslateKey(key),
            scanCode: scanCode,
            modifiers: modifiers);
    }

    public static WpfInputEventArgs CreateTextInputEvent(char character, WpfInputModifiers modifiers)
    {
        return new WpfInputEventArgs(
            WpfInputEventKind.TextInput,
            character: character,
            modifiers: modifiers);
    }

    public static WpfInputEventArgs CreateMouseMoveEvent(Vector2 position, WpfInputModifiers modifiers)
    {
        return new WpfInputEventArgs(
            WpfInputEventKind.MouseMove,
            x: position.X,
            y: position.Y,
            modifiers: modifiers);
    }

    public static WpfInputEventArgs CreateMouseButtonEvent(
        WpfInputEventKind kind,
        SilkInput.MouseButton button,
        Vector2 position,
        WpfInputModifiers modifiers)
    {
        if (kind != WpfInputEventKind.MouseDown && kind != WpfInputEventKind.MouseUp)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Mouse button events must be MouseDown or MouseUp.");
        }

        return new WpfInputEventArgs(
            kind,
            x: position.X,
            y: position.Y,
            button: TranslateMouseButton(button),
            modifiers: modifiers);
    }

    public static WpfInputEventArgs CreateMouseWheelEvent(
        double deltaX,
        double deltaY,
        Vector2 position,
        WpfInputModifiers modifiers)
    {
        return new WpfInputEventArgs(
            WpfInputEventKind.MouseWheel,
            x: position.X,
            y: position.Y,
            deltaX: deltaX,
            deltaY: deltaY,
            modifiers: modifiers);
    }

    public static WpfMouseButton TranslateMouseButton(SilkInput.MouseButton button)
    {
        return button switch
        {
            SilkInput.MouseButton.Left => WpfMouseButton.Left,
            SilkInput.MouseButton.Right => WpfMouseButton.Right,
            SilkInput.MouseButton.Middle => WpfMouseButton.Middle,
            SilkInput.MouseButton.Button4 => WpfMouseButton.XButton1,
            SilkInput.MouseButton.Button5 => WpfMouseButton.XButton2,
            _ => WpfMouseButton.Other
        };
    }

    public static string? TranslateKey(SilkInput.Key key)
    {
        if (key == SilkInput.Key.Unknown)
        {
            return null;
        }

        return key switch
        {
            SilkInput.Key.Backspace => "Back",
            SilkInput.Key.ShiftLeft => "LeftShift",
            SilkInput.Key.ShiftRight => "RightShift",
            SilkInput.Key.ControlLeft => "LeftCtrl",
            SilkInput.Key.ControlRight => "RightCtrl",
            SilkInput.Key.AltLeft => "LeftAlt",
            SilkInput.Key.AltRight => "RightAlt",
            SilkInput.Key.SuperLeft => "LWin",
            SilkInput.Key.SuperRight => "RWin",
            SilkInput.Key.Number0 => "D0",
            SilkInput.Key.Number1 => "D1",
            SilkInput.Key.Number2 => "D2",
            SilkInput.Key.Number3 => "D3",
            SilkInput.Key.Number4 => "D4",
            SilkInput.Key.Number5 => "D5",
            SilkInput.Key.Number6 => "D6",
            SilkInput.Key.Number7 => "D7",
            SilkInput.Key.Number8 => "D8",
            SilkInput.Key.Number9 => "D9",
            SilkInput.Key.Keypad0 => "NumPad0",
            SilkInput.Key.Keypad1 => "NumPad1",
            SilkInput.Key.Keypad2 => "NumPad2",
            SilkInput.Key.Keypad3 => "NumPad3",
            SilkInput.Key.Keypad4 => "NumPad4",
            SilkInput.Key.Keypad5 => "NumPad5",
            SilkInput.Key.Keypad6 => "NumPad6",
            SilkInput.Key.Keypad7 => "NumPad7",
            SilkInput.Key.Keypad8 => "NumPad8",
            SilkInput.Key.Keypad9 => "NumPad9",
            _ => key.ToString()
        };
    }

    private static WpfInputModifiers ReadModifiers(SilkInput.IInputContext inputContext)
    {
        var modifiers = WpfInputModifiers.None;

        foreach (var keyboard in inputContext.Keyboards)
        {
            if (!keyboard.IsConnected)
            {
                continue;
            }

            if (IsKeyPressed(keyboard, SilkInput.Key.ShiftLeft) || IsKeyPressed(keyboard, SilkInput.Key.ShiftRight))
            {
                modifiers |= WpfInputModifiers.Shift;
            }

            if (IsKeyPressed(keyboard, SilkInput.Key.ControlLeft) || IsKeyPressed(keyboard, SilkInput.Key.ControlRight))
            {
                modifiers |= WpfInputModifiers.Control;
            }

            if (IsKeyPressed(keyboard, SilkInput.Key.AltLeft) || IsKeyPressed(keyboard, SilkInput.Key.AltRight))
            {
                modifiers |= WpfInputModifiers.Alt;
            }

            if (IsKeyPressed(keyboard, SilkInput.Key.SuperLeft) || IsKeyPressed(keyboard, SilkInput.Key.SuperRight))
            {
                modifiers |= WpfInputModifiers.Super;
            }
        }

        return modifiers;
    }

    private static bool IsKeyPressed(SilkInput.IKeyboard keyboard, SilkInput.Key key)
    {
        try
        {
            return keyboard.IsKeyPressed(key);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void OnInputReceived(WpfInputEventArgs args)
    {
        InputReceived?.Invoke(this, args);
    }

    internal static Vector2 ResolveMousePosition(
        Vector2 currentPosition,
        Vector2 lastPosition,
        bool hasLastPosition)
    {
        if (hasLastPosition && IsFinite(lastPosition))
        {
            return lastPosition;
        }

        return IsFinite(currentPosition)
            ? currentPosition
            : Vector2.Zero;
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }

    private sealed class InputSubscription : IDisposable
    {
        private readonly SilkInput.IInputContext _inputContext;
        private readonly List<Action> _unsubscribe;
        private bool _isDisposed;

        public InputSubscription(SilkInput.IInputContext inputContext, List<Action> unsubscribe)
        {
            _inputContext = inputContext;
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            foreach (var unsubscribe in _unsubscribe)
            {
                unsubscribe();
            }

            _inputContext.Dispose();
            _isDisposed = true;
        }
    }
}
