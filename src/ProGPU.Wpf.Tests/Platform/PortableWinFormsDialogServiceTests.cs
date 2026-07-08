using ProGPU.Wpf.Interop;
using Forms = System.Windows.Forms;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class PortableWinFormsDialogServiceTests
{
    [Fact]
    public void OpenFileDialogUsesPortableFileDialogService()
    {
        Assert.True(PortableWpfServiceRegistry.TryGetFileDialogService(
            PortableWpfServiceKey.WinForms,
            out var service));

        PortableFileDialogRequest? capturedRequest = null;
        using var registration = service.Register(request =>
        {
            capturedRequest = request;
            return "/tmp/SharpDevelop.cs";
        });

        var dialog = new Forms.OpenFileDialog
        {
            Title = "Open source",
            InitialDirectory = "/tmp",
            Filter = "C# files|*.cs|All files|*.*",
            FilterIndex = 1,
            Multiselect = true
        };

        Assert.Equal(Forms.DialogResult.OK, dialog.ShowDialog());
        Assert.Equal("/tmp/SharpDevelop.cs", dialog.FileName);
        Assert.Equal(new[] { "/tmp/SharpDevelop.cs" }, dialog.FileNames);
        Assert.NotNull(capturedRequest);
        Assert.Equal("OpenFile", capturedRequest.Kind);
        Assert.Equal("Open source", capturedRequest.Title);
        Assert.Equal("/tmp", capturedRequest.InitialDirectory);
        Assert.Equal("C# files|*.cs|All files|*.*", capturedRequest.Filter);
        Assert.Equal(1, capturedRequest.FilterIndex);
    }

    [Fact]
    public void SaveFileDialogUsesPortableFileDialogService()
    {
        Assert.True(PortableWpfServiceRegistry.TryGetFileDialogService(
            PortableWpfServiceKey.WinForms,
            out var service));

        PortableFileDialogRequest? capturedRequest = null;
        using var registration = service.Register(request =>
        {
            capturedRequest = request;
            return "/tmp/SharpDevelop.sln";
        });

        var dialog = new Forms.SaveFileDialog
        {
            Title = "Save solution",
            InitialDirectory = "/tmp",
            FileName = "SharpDevelop.sln",
            DefaultExt = "sln",
            Filter = "Solution files|*.sln|All files|*.*",
            FilterIndex = 1
        };

        Assert.Equal(Forms.DialogResult.OK, dialog.ShowDialog());
        Assert.Equal("/tmp/SharpDevelop.sln", dialog.FileName);
        Assert.NotNull(capturedRequest);
        Assert.Equal("SaveFile", capturedRequest.Kind);
        Assert.Equal("Save solution", capturedRequest.Title);
        Assert.Equal("SharpDevelop.sln", capturedRequest.SuggestedItemName);
        Assert.Equal("sln", capturedRequest.DefaultExtension);
    }

    [Fact]
    public void FolderBrowserDialogUsesPortableFileDialogService()
    {
        Assert.True(PortableWpfServiceRegistry.TryGetFileDialogService(
            PortableWpfServiceKey.WinForms,
            out var service));

        PortableFileDialogRequest? capturedRequest = null;
        using var registration = service.Register(request =>
        {
            capturedRequest = request;
            return "/tmp/SharpDevelop";
        });

        var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Pick project folder",
            SelectedPath = "/tmp"
        };

        Assert.Equal(Forms.DialogResult.OK, dialog.ShowDialog());
        Assert.Equal("/tmp/SharpDevelop", dialog.SelectedPath);
        Assert.NotNull(capturedRequest);
        Assert.Equal("PickFolder", capturedRequest.Kind);
        Assert.Equal("Pick project folder", capturedRequest.Title);
        Assert.Equal("/tmp", capturedRequest.InitialDirectory);
    }
}
