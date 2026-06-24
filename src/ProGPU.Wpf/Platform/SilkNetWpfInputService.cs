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

        foreach (var mouse in inputContext.Mice)
        {
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

            subscriptions.Add(() => mouse.MouseMove -= mouseMove);
            subscriptions.Add(() => mouse.MouseDown -= mouseDown);
            subscriptions.Add(() => mouse.MouseUp -= mouseUp);
            subscriptions.Add(() => mouse.Scroll -= scroll);
        }

        foreach (var keyboard in inputContext.Keyboards)
        {
            Action<SilkInput.IKeyboard, SilkInput.Key, int> keyDown = (_, key, scanCode) =>
                OnInputReceived(CreateKeyEvent(WpfInputEventKind.KeyDown, key, scanCode, ReadModifiers(inputContext)));
            Action<SilkInput.IKeyboard, SilkInput.Key, int> keyUp = (_, key, scanCode) =>
                OnInputReceived(CreateKeyEvent(WpfInputEventKind.KeyUp, key, scanCode, ReadModifiers(inputContext)));
            Action<SilkInput.IKeyboard, char> keyChar = (_, character) =>
                OnInputReceived(CreateTextInputEvent(character, ReadModifiers(inputContext)));

            keyboard.KeyDown += keyDown;
            keyboard.KeyUp += keyUp;
            keyboard.KeyChar += keyChar;

            subscriptions.Add(() => keyboard.KeyDown -= keyDown);
            subscriptions.Add(() => keyboard.KeyUp -= keyUp);
            subscriptions.Add(() => keyboard.KeyChar -= keyChar);
        }

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
            if (keyboard.IsKeyPressed(SilkInput.Key.ShiftLeft) || keyboard.IsKeyPressed(SilkInput.Key.ShiftRight))
            {
                modifiers |= WpfInputModifiers.Shift;
            }

            if (keyboard.IsKeyPressed(SilkInput.Key.ControlLeft) || keyboard.IsKeyPressed(SilkInput.Key.ControlRight))
            {
                modifiers |= WpfInputModifiers.Control;
            }

            if (keyboard.IsKeyPressed(SilkInput.Key.AltLeft) || keyboard.IsKeyPressed(SilkInput.Key.AltRight))
            {
                modifiers |= WpfInputModifiers.Alt;
            }

            if (keyboard.IsKeyPressed(SilkInput.Key.SuperLeft) || keyboard.IsKeyPressed(SilkInput.Key.SuperRight))
            {
                modifiers |= WpfInputModifiers.Super;
            }
        }

        return modifiers;
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
