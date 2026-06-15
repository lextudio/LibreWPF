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
}
