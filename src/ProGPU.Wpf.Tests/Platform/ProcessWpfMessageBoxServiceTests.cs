using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class ProcessWpfMessageBoxServiceTests
{
    [Fact]
    public void CreateMacStartInfoUsesOsascriptDialogWithButtonsAndDefault()
    {
        var options = new WpfMessageBoxOptions
        {
            MessageBoxText = "Continue rendering?",
            Caption = "ProGPU WPF",
            Button = "YesNoCancel",
            Icon = "Warning",
            FallbackResult = "No"
        };

        var startInfo = Assert.Single(ProcessWpfMessageBoxService.CreateStartInfos(
            WpfMessageBoxPlatform.MacOS,
            options));

        Assert.Equal("osascript", startInfo.FileName);
        Assert.Equal(new[] { "-e" }, startInfo.ArgumentList.Take(1).ToArray());
        var script = startInfo.ArgumentList[1];
        Assert.Contains("display dialog", script);
        Assert.Contains("Continue rendering?", script);
        Assert.Contains("ProGPU WPF", script);
        Assert.Contains("buttons {\"Yes\", \"No\", \"Cancel\"}", script);
        Assert.Contains("default button \"No\"", script);
        Assert.Contains("cancel button \"Cancel\"", script);
        Assert.Contains("with icon caution", script);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void CreateLinuxStartInfosUseZenityAndKDialogFallbacks()
    {
        var options = new WpfMessageBoxOptions
        {
            MessageBoxText = "Save changes?",
            Caption = "Document",
            Button = "AbortRetryIgnore",
            Icon = "Question"
        };

        var startInfos = ProcessWpfMessageBoxService.CreateStartInfos(
            WpfMessageBoxPlatform.Linux,
            options);

        Assert.Equal(2, startInfos.Count);

        var zenity = startInfos[0];
        Assert.Equal("zenity", zenity.FileName);
        Assert.Contains("--question", zenity.ArgumentList);
        Assert.Contains("--title=Document", zenity.ArgumentList);
        Assert.Contains("--text=Save changes?", zenity.ArgumentList);
        Assert.Contains("--ok-label=Abort", zenity.ArgumentList);
        Assert.Contains("--cancel-label=Retry", zenity.ArgumentList);
        Assert.Contains("--extra-button=Ignore", zenity.ArgumentList);
        Assert.False(zenity.UseShellExecute);

        var kdialog = startInfos[1];
        Assert.Equal("kdialog", kdialog.FileName);
        Assert.Contains("--title", kdialog.ArgumentList);
        Assert.Contains("Document", kdialog.ArgumentList);
        Assert.Contains("--yesnocancel", kdialog.ArgumentList);
        Assert.Contains("--yes-label", kdialog.ArgumentList);
        Assert.Contains("Abort", kdialog.ArgumentList);
        Assert.Contains("--no-label", kdialog.ArgumentList);
        Assert.Contains("Retry", kdialog.ArgumentList);
        Assert.Contains("--cancel-label", kdialog.ArgumentList);
        Assert.Contains("Ignore", kdialog.ArgumentList);
        Assert.False(kdialog.UseShellExecute);
    }

    [Fact]
    public void CreateWindowsStartInfoUsesPowerShellFormsMessageBox()
    {
        var options = new WpfMessageBoxOptions
        {
            MessageBoxText = "Windows message",
            Caption = "Caption",
            Button = "RetryCancel",
            Icon = "Error",
            FallbackResult = "Cancel"
        };

        var startInfo = Assert.Single(ProcessWpfMessageBoxService.CreateStartInfos(
            WpfMessageBoxPlatform.Windows,
            options));

        Assert.Equal("powershell", startInfo.FileName);
        Assert.Contains("-NoProfile", startInfo.ArgumentList);
        var command = startInfo.ArgumentList[^1];
        Assert.Contains("System.Windows.Forms.MessageBox", command);
        Assert.Contains("Windows message", command);
        Assert.Contains("Caption", command);
        Assert.Contains("MessageBoxButtons]::RetryCancel", command);
        Assert.Contains("MessageBoxIcon]::Error", command);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void ShowReturnsParsedNativeResult()
    {
        var service = new ProcessWpfMessageBoxService(
            () => WpfMessageBoxPlatform.MacOS,
            (_, _) => ValueTask.FromResult(new WpfMessageBoxProcessResult(0, "button returned:Cancel\n", string.Empty)));

        var result = service.Show(new WpfMessageBoxOptions
        {
            Button = "OKCancel",
            FallbackResult = "OK"
        });

        Assert.Equal("Cancel", result);
    }

    [Fact]
    public void ShowReturnsExtraButtonResultFromNativeExitCode()
    {
        var service = new ProcessWpfMessageBoxService(
            () => WpfMessageBoxPlatform.Linux,
            (_, _) => ValueTask.FromResult(new WpfMessageBoxProcessResult(2, string.Empty, string.Empty)));

        var result = service.Show(new WpfMessageBoxOptions
        {
            Button = "AbortRetryIgnore",
            FallbackResult = "Abort"
        });

        Assert.Equal("Ignore", result);
    }

    [Fact]
    public void ShowFallsBackToKDialogWhenZenityCannotStart()
    {
        var calls = new List<string>();
        var service = new ProcessWpfMessageBoxService(
            () => WpfMessageBoxPlatform.Linux,
            (startInfo, _) =>
            {
                calls.Add(startInfo.FileName);
                if (startInfo.FileName == "zenity")
                {
                    throw new Win32Exception(2);
                }

                return ValueTask.FromResult(new WpfMessageBoxProcessResult(1, string.Empty, string.Empty));
            });

        var result = service.Show(new WpfMessageBoxOptions
        {
            Button = "YesNo",
            FallbackResult = "Yes"
        });

        Assert.Equal("No", result);
        Assert.Equal(new[] { "zenity", "kdialog" }, calls);
    }

    [Fact]
    public void UnsupportedPlatformThrowsPlatformNotSupported()
    {
        var service = new ProcessWpfMessageBoxService(
            () => WpfMessageBoxPlatform.Unsupported,
            (_, _) => ValueTask.FromResult(new WpfMessageBoxProcessResult(0, string.Empty, string.Empty)));

        Assert.Throws<PlatformNotSupportedException>(() => service.Show(new WpfMessageBoxOptions()));
    }
}
