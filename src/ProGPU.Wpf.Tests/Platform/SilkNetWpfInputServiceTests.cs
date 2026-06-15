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
