using System.Numerics;
using System.Windows.Media.ProGPU.Platform;
using Silk.NET.Input;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class SilkNetWpfInputServiceTests
{
    [Fact]
    public void CreateKeyEventNormalizesSilkKey()
    {
        var input = SilkNetWpfInputService.CreateKeyEvent(
            WpfInputEventKind.KeyDown,
            Key.A,
            scanCode: 38,
            WpfInputModifiers.Control | WpfInputModifiers.Shift);

        Assert.Equal(WpfInputEventKind.KeyDown, input.Kind);
        Assert.Equal("A", input.Key);
        Assert.Equal(38, input.ScanCode);
        Assert.Equal(WpfInputModifiers.Control | WpfInputModifiers.Shift, input.Modifiers);
    }

    [Theory]
    [InlineData(Key.Backspace, "Back")]
    [InlineData(Key.Enter, "Enter")]
    [InlineData(Key.Tab, "Tab")]
    [InlineData(Key.Escape, "Escape")]
    [InlineData(Key.Left, "Left")]
    [InlineData(Key.Right, "Right")]
    [InlineData(Key.Up, "Up")]
    [InlineData(Key.Down, "Down")]
    [InlineData(Key.F7, "F7")]
    [InlineData(Key.Number1, "D1")]
    [InlineData(Key.Keypad1, "NumPad1")]
    [InlineData(Key.ShiftLeft, "LeftShift")]
    [InlineData(Key.ShiftRight, "RightShift")]
    [InlineData(Key.ControlLeft, "LeftCtrl")]
    [InlineData(Key.ControlRight, "RightCtrl")]
    [InlineData(Key.AltLeft, "LeftAlt")]
    [InlineData(Key.AltRight, "RightAlt")]
    [InlineData(Key.SuperLeft, "LWin")]
    [InlineData(Key.SuperRight, "RWin")]
    public void TranslateKeyMapsSilkNamesToPortableWpfKeyNames(Key silkKey, string expected)
    {
        Assert.Equal(expected, SilkNetWpfInputService.TranslateKey(silkKey));
    }

    [Fact]
    public void TranslateKeyMapsUnknownToNull()
    {
        Assert.Null(SilkNetWpfInputService.TranslateKey(Key.Unknown));
    }

    [Fact]
    public void CreateTextInputEventStoresCharacter()
    {
        var input = SilkNetWpfInputService.CreateTextInputEvent('x', WpfInputModifiers.Alt);

        Assert.Equal(WpfInputEventKind.TextInput, input.Kind);
        Assert.Equal('x', input.Character);
        Assert.Equal(WpfInputModifiers.Alt, input.Modifiers);
    }

    [Theory]
    [InlineData(MouseButton.Left, WpfMouseButton.Left)]
    [InlineData(MouseButton.Right, WpfMouseButton.Right)]
    [InlineData(MouseButton.Middle, WpfMouseButton.Middle)]
    [InlineData(MouseButton.Button4, WpfMouseButton.XButton1)]
    [InlineData(MouseButton.Button5, WpfMouseButton.XButton2)]
    [InlineData(MouseButton.Unknown, WpfMouseButton.Other)]
    public void TranslateMouseButtonMapsCommonButtons(MouseButton silkButton, WpfMouseButton expected)
    {
        Assert.Equal(expected, SilkNetWpfInputService.TranslateMouseButton(silkButton));
    }

    [Fact]
    public void CreateMouseButtonEventStoresPositionButtonAndModifiers()
    {
        var input = SilkNetWpfInputService.CreateMouseButtonEvent(
            WpfInputEventKind.MouseDown,
            MouseButton.Button4,
            new Vector2(12, 34),
            WpfInputModifiers.Super);

        Assert.Equal(WpfInputEventKind.MouseDown, input.Kind);
        Assert.Equal(WpfMouseButton.XButton1, input.Button);
        Assert.Equal(12, input.X);
        Assert.Equal(34, input.Y);
        Assert.Equal(WpfInputModifiers.Super, input.Modifiers);
    }

    [Fact]
    public void ResolveMousePositionPrefersLastMouseMoveWhenAvailable()
    {
        var position = SilkNetWpfInputService.ResolveMousePosition(
            currentPosition: Vector2.Zero,
            lastPosition: new Vector2(120, 80),
            hasLastPosition: true);

        Assert.Equal(new Vector2(120, 80), position);
    }

    [Fact]
    public void ResolveMousePositionUsesCurrentPositionBeforeFirstMouseMove()
    {
        var position = SilkNetWpfInputService.ResolveMousePosition(
            currentPosition: new Vector2(42, 24),
            lastPosition: Vector2.Zero,
            hasLastPosition: false);

        Assert.Equal(new Vector2(42, 24), position);
    }

    [Fact]
    public void ResolveMousePositionFallsBackToZeroForInvalidPositions()
    {
        var position = SilkNetWpfInputService.ResolveMousePosition(
            currentPosition: new Vector2(float.NaN, 24),
            lastPosition: new Vector2(12, float.PositiveInfinity),
            hasLastPosition: true);

        Assert.Equal(Vector2.Zero, position);
    }

    [Fact]
    public void CreateMouseWheelEventStoresPositionAndDeltas()
    {
        var input = SilkNetWpfInputService.CreateMouseWheelEvent(
            deltaX: 1,
            deltaY: -2,
            new Vector2(5, 6),
            WpfInputModifiers.None);

        Assert.Equal(WpfInputEventKind.MouseWheel, input.Kind);
        Assert.Equal(5, input.X);
        Assert.Equal(6, input.Y);
        Assert.Equal(1, input.DeltaX);
        Assert.Equal(-2, input.DeltaY);
    }
}
