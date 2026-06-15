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
            Action<SilkInput.IMouse, Vector2> mouseMove = (_, position) =>
                OnInputReceived(CreateMouseMoveEvent(position, ReadModifiers(inputContext)));
            Action<SilkInput.IMouse, SilkInput.MouseButton> mouseDown = (_, button) =>
                OnInputReceived(CreateMouseButtonEvent(WpfInputEventKind.MouseDown, button, mouse.Position, ReadModifiers(inputContext)));
            Action<SilkInput.IMouse, SilkInput.MouseButton> mouseUp = (_, button) =>
                OnInputReceived(CreateMouseButtonEvent(WpfInputEventKind.MouseUp, button, mouse.Position, ReadModifiers(inputContext)));
            Action<SilkInput.IMouse, SilkInput.ScrollWheel> scroll = (_, wheel) =>
                OnInputReceived(CreateMouseWheelEvent(wheel.X, wheel.Y, mouse.Position, ReadModifiers(inputContext)));

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
            key: key == SilkInput.Key.Unknown ? null : key.ToString(),
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
