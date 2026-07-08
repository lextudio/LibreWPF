using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class SilkNetWpfWindowEventServiceTests
{
    [Theory]
    [InlineData(true, WpfWindowEventKind.Activated)]
    [InlineData(false, WpfWindowEventKind.Deactivated)]
    public void CreateFocusChangedEventMapsFocusToActivation(bool isFocused, WpfWindowEventKind expected)
    {
        var args = SilkNetWpfWindowEventService.CreateFocusChangedEvent(isFocused);

        Assert.Equal(expected, args.Kind);
        Assert.Empty(args.Files);
    }

    [Fact]
    public void CreateFileDropEventStoresDroppedFiles()
    {
        var args = SilkNetWpfWindowEventService.CreateFileDropEvent(new[] { "/tmp/a.txt", "/tmp/b.txt" });

        Assert.Equal(WpfWindowEventKind.FilesDropped, args.Kind);
        Assert.Equal(new[] { "/tmp/a.txt", "/tmp/b.txt" }, args.Files);
    }

    [Fact]
    public void CreateFileDropEventHandlesNullFileList()
    {
        var args = SilkNetWpfWindowEventService.CreateFileDropEvent(null);

        Assert.Equal(WpfWindowEventKind.FilesDropped, args.Kind);
        Assert.Empty(args.Files);
    }

    [Fact]
    public void CreateWindowPositionChangingEventStoresCoordinates()
    {
        var args = SilkNetWpfWindowEventService.CreateWindowPositionChangingEvent(-12, 34);

        Assert.Equal(WpfWindowEventKind.WindowPositionChanging, args.Kind);
        Assert.Equal(-12, args.Left);
        Assert.Equal(34, args.Top);
        Assert.Null(args.Width);
        Assert.Null(args.Height);
    }

    [Fact]
    public void CreateWindowPositionChangedEventStoresCoordinates()
    {
        var args = SilkNetWpfWindowEventService.CreateWindowPositionChangedEvent(120, -45);

        Assert.Equal(WpfWindowEventKind.WindowPositionChanged, args.Kind);
        Assert.Equal(120, args.Left);
        Assert.Equal(-45, args.Top);
        Assert.Null(args.Width);
        Assert.Null(args.Height);
    }

    [Fact]
    public void CreateWindowSizeChangedEventStoresSize()
    {
        var args = SilkNetWpfWindowEventService.CreateWindowSizeChangedEvent(800, 600);

        Assert.Equal(WpfWindowEventKind.WindowSizeChanged, args.Kind);
        Assert.Null(args.Left);
        Assert.Null(args.Top);
        Assert.Equal(800, args.Width);
        Assert.Equal(600, args.Height);
    }
}
