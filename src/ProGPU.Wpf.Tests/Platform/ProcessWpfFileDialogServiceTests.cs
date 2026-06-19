using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class ProcessWpfFileDialogServiceTests
{
    [Fact]
    public void CreateMacOpenFileStartInfoUsesOsascriptWithPromptAndTypes()
    {
        var options = new WpfFileDialogOptions
        {
            Title = "Choose image",
            FileTypePatterns = new[] { ".png", "*.jpg" }
        };

        var startInfo = Assert.Single(ProcessWpfFileDialogService.CreateStartInfos(
            WpfFileDialogPlatform.MacOS,
            WpfFileDialogKind.OpenFile,
            options));

        Assert.Equal("osascript", startInfo.FileName);
        Assert.Equal(new[] { "-e" }, startInfo.ArgumentList.Take(1).ToArray());
        Assert.Contains("choose file", startInfo.ArgumentList[1]);
        Assert.Contains("Choose image", startInfo.ArgumentList[1]);
        Assert.Contains("of type {\"png\", \"jpg\"}", startInfo.ArgumentList[1]);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void CreateWindowsSaveFileStartInfoUsesPowerShellFormsDialog()
    {
        var options = new WpfFileDialogOptions
        {
            Title = "Save drawing",
            SuggestedFileName = "drawing.xaml",
            FileTypePatterns = new[] { "*.xaml" }
        };

        var startInfo = Assert.Single(ProcessWpfFileDialogService.CreateStartInfos(
            WpfFileDialogPlatform.Windows,
            WpfFileDialogKind.SaveFile,
            options));

        Assert.Equal("powershell", startInfo.FileName);
        Assert.Contains("-NoProfile", startInfo.ArgumentList);
        Assert.Contains("-NonInteractive", startInfo.ArgumentList);
        var command = startInfo.ArgumentList[^1];
        Assert.Contains("System.Windows.Forms.SaveFileDialog", command);
        Assert.Contains("Save drawing", command);
        Assert.Contains("drawing.xaml", command);
        Assert.Contains("Selected Files (*.xaml)|*.xaml", command);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void CreateLinuxOpenFileStartInfosUseZenityAndKDialogWithFilters()
    {
        var options = new WpfFileDialogOptions
        {
            Title = "Open media",
            FileTypePatterns = new[] { ".png", "*.jpg", "*.*" }
        };

        var startInfos = ProcessWpfFileDialogService.CreateStartInfos(
            WpfFileDialogPlatform.Linux,
            WpfFileDialogKind.OpenFile,
            options);

        Assert.Equal(2, startInfos.Count);
        var startInfo = startInfos[0];
        Assert.Equal("zenity", startInfo.FileName);
        Assert.Contains("--file-selection", startInfo.ArgumentList);
        Assert.Contains("--title=Open media", startInfo.ArgumentList);
        Assert.Contains("--file-filter=Selected Files | *.png *.jpg", startInfo.ArgumentList);
        Assert.Contains("--file-filter=All Files | *", startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);

        var fallback = startInfos[1];
        Assert.Equal("kdialog", fallback.FileName);
        Assert.Contains("--title", fallback.ArgumentList);
        Assert.Contains("Open media", fallback.ArgumentList);
        Assert.Contains("--getopenfilename", fallback.ArgumentList);
        Assert.Contains(".", fallback.ArgumentList);
        Assert.Contains(fallback.ArgumentList, argument => argument.Contains("*.png *.jpg|Selected Files", StringComparison.Ordinal));
        Assert.Contains(fallback.ArgumentList, argument => argument.Contains("*|All Files", StringComparison.Ordinal));
        Assert.False(fallback.UseShellExecute);
    }

    [Fact]
    public void CreateLinuxFolderStartInfosUseDirectoryMode()
    {
        var startInfos = ProcessWpfFileDialogService.CreateStartInfos(
            WpfFileDialogPlatform.Linux,
            WpfFileDialogKind.PickFolder,
            new WpfFileDialogOptions { Title = "Choose folder" });

        Assert.Equal(2, startInfos.Count);
        var startInfo = startInfos[0];
        Assert.Equal("zenity", startInfo.FileName);
        Assert.Contains("--file-selection", startInfo.ArgumentList);
        Assert.Contains("--directory", startInfo.ArgumentList);
        Assert.Contains("--title=Choose folder", startInfo.ArgumentList);

        var fallback = startInfos[1];
        Assert.Equal("kdialog", fallback.FileName);
        Assert.Contains("--title", fallback.ArgumentList);
        Assert.Contains("Choose folder", fallback.ArgumentList);
        Assert.Contains("--getexistingdirectory", fallback.ArgumentList);
        Assert.Contains(".", fallback.ArgumentList);
    }

    [Fact]
    public async Task OpenFileAsyncReturnsTrimmedSelectedPath()
    {
        var calls = new List<ProcessStartInfo>();
        var service = new ProcessWpfFileDialogService(
            () => WpfFileDialogPlatform.Linux,
            (startInfo, _) =>
            {
                calls.Add(startInfo);
                return ValueTask.FromResult(new WpfFileDialogProcessResult(0, "/tmp/file.txt\n", string.Empty));
            });

        var selected = await service.OpenFileAsync(new WpfFileDialogOptions());

        Assert.Equal("/tmp/file.txt", selected);
        Assert.Single(calls);
    }

    [Fact]
    public async Task OpenFileAsyncFallsBackToKDialogWhenZenityCannotStart()
    {
        var calls = new List<string>();
        var service = new ProcessWpfFileDialogService(
            () => WpfFileDialogPlatform.Linux,
            (startInfo, _) =>
            {
                calls.Add(startInfo.FileName);
                if (startInfo.FileName == "zenity")
                {
                    throw new Win32Exception(2);
                }

                return ValueTask.FromResult(new WpfFileDialogProcessResult(0, "/tmp/fallback.txt", string.Empty));
            });

        var selected = await service.OpenFileAsync(new WpfFileDialogOptions());

        Assert.Equal("/tmp/fallback.txt", selected);
        Assert.Equal(new[] { "zenity", "kdialog" }, calls);
    }

    [Fact]
    public async Task SaveFileAsyncReturnsNullWhenDialogIsCancelled()
    {
        var service = new ProcessWpfFileDialogService(
            () => WpfFileDialogPlatform.Linux,
            (_, _) => ValueTask.FromResult(new WpfFileDialogProcessResult(1, string.Empty, "cancelled")));

        var selected = await service.SaveFileAsync(new WpfFileDialogOptions());

        Assert.Null(selected);
    }

    [Fact]
    public async Task UnsupportedPlatformThrowsPlatformNotSupported()
    {
        var service = new ProcessWpfFileDialogService(
            () => WpfFileDialogPlatform.Unsupported,
            (_, _) => ValueTask.FromResult(new WpfFileDialogProcessResult(0, string.Empty, string.Empty)));

        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await service.OpenFileAsync(new WpfFileDialogOptions()));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await service.SaveFileAsync(new WpfFileDialogOptions()));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await service.PickFolderAsync());
    }
}
