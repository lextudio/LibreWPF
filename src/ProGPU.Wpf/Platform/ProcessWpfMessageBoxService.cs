using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace System.Windows.Media.ProGPU.Platform;

public enum WpfMessageBoxPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

public readonly record struct WpfMessageBoxProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public delegate ValueTask<WpfMessageBoxProcessResult> WpfMessageBoxProcessRunner(
    ProcessStartInfo startInfo,
    CancellationToken cancellationToken);

public sealed class ProcessWpfMessageBoxService : IWpfMessageBoxService
{
    private readonly Func<WpfMessageBoxPlatform> _platformProvider;
    private readonly WpfMessageBoxProcessRunner _processRunner;

    public ProcessWpfMessageBoxService()
        : this(DetectPlatform, RunProcessAsync)
    {
    }

    public ProcessWpfMessageBoxService(
        Func<WpfMessageBoxPlatform> platformProvider,
        WpfMessageBoxProcessRunner processRunner)
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public string Show(WpfMessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var commands = CreateStartInfos(_platformProvider(), options);
        if (commands.Count == 0)
        {
            throw new PlatformNotSupportedException("Message box services are not available on this platform.");
        }

        Exception? lastStartException = null;
        foreach (var command in commands)
        {
            try
            {
                var result = _processRunner(command, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                return MapProcessResult(options, result);
            }
            catch (Win32Exception exception)
            {
                lastStartException = exception;
            }
            catch (InvalidOperationException exception)
            {
                lastStartException = exception;
            }
        }

        throw new InvalidOperationException("No message box command could be started.", lastStartException);
    }

    public static WpfMessageBoxPlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WpfMessageBoxPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return WpfMessageBoxPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return WpfMessageBoxPlatform.Linux;
        }

        return WpfMessageBoxPlatform.Unsupported;
    }

    public static IReadOnlyList<ProcessStartInfo> CreateStartInfos(
        WpfMessageBoxPlatform platform,
        WpfMessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return platform switch
        {
            WpfMessageBoxPlatform.Windows => new[] { CreateWindowsStartInfo(options) },
            WpfMessageBoxPlatform.MacOS => new[] { CreateMacStartInfo(options) },
            WpfMessageBoxPlatform.Linux => new[] { CreateLinuxZenityStartInfo(options), CreateLinuxKDialogStartInfo(options) },
            _ => Array.Empty<ProcessStartInfo>()
        };
    }

    private static ProcessStartInfo CreateWindowsStartInfo(WpfMessageBoxOptions options)
    {
        var command = "Add-Type -AssemblyName System.Windows.Forms; "
            + "[Console]::Write([System.Windows.Forms.MessageBox]::Show("
            + $"'{EscapePowerShellString(options.MessageBoxText)}', "
            + $"'{EscapePowerShellString(options.Caption)}', "
            + $"[System.Windows.Forms.MessageBoxButtons]::{MapWindowsButtons(options.Button)}, "
            + $"[System.Windows.Forms.MessageBoxIcon]::{MapWindowsIcon(options.Icon)}, "
            + $"[System.Windows.Forms.MessageBoxDefaultButton]::{MapWindowsDefaultButton(options)}))";

        return CreateStartInfo("powershell", "-NoProfile", "-NonInteractive", "-Command", command);
    }

    private static ProcessStartInfo CreateMacStartInfo(WpfMessageBoxOptions options)
    {
        var buttonSet = GetButtonSet(options.Button);
        var buttons = string.Join(", ", buttonSet.Labels.Select(label => $"\"{EscapeAppleScriptString(label)}\""));
        var script = $"display dialog \"{EscapeAppleScriptString(options.MessageBoxText)}\" "
            + $"with title \"{EscapeAppleScriptString(options.Caption)}\" "
            + $"buttons {{{buttons}}} "
            + $"default button \"{EscapeAppleScriptString(GetDefaultButtonLabel(options, buttonSet))}\"";

        if (!string.IsNullOrEmpty(buttonSet.CancelLabel))
        {
            script += $" cancel button \"{EscapeAppleScriptString(buttonSet.CancelLabel)}\"";
        }

        var icon = MapMacIcon(options.Icon);
        if (!string.IsNullOrEmpty(icon))
        {
            script += $" with icon {icon}";
        }

        return CreateStartInfo("osascript", "-e", script);
    }

    private static ProcessStartInfo CreateLinuxZenityStartInfo(WpfMessageBoxOptions options)
    {
        var buttonSet = GetButtonSet(options.Button);
        var startInfo = CreateStartInfo(
            "zenity",
            MapZenityDialogKind(options),
            $"--title={options.Caption}",
            $"--text={options.MessageBoxText}",
            $"--ok-label={buttonSet.PrimaryLabel}");

        if (!string.Equals(MapZenityDialogKind(options), "--info", StringComparison.Ordinal))
        {
            startInfo.ArgumentList.Add($"--cancel-label={buttonSet.SecondaryLabel}");
        }

        foreach (var extra in buttonSet.ExtraLabels)
        {
            startInfo.ArgumentList.Add($"--extra-button={extra}");
        }

        return startInfo;
    }

    private static ProcessStartInfo CreateLinuxKDialogStartInfo(WpfMessageBoxOptions options)
    {
        var buttonSet = GetButtonSet(options.Button);
        var startInfo = CreateStartInfo(
            "kdialog",
            "--title",
            options.Caption,
            GetKDialogKind(options),
            options.MessageBoxText);

        if (string.Equals(options.Button, "OK", StringComparison.Ordinal))
        {
            startInfo.ArgumentList.Add("--ok-label");
            startInfo.ArgumentList.Add(buttonSet.PrimaryLabel);
        }
        else
        {
            startInfo.ArgumentList.Add("--yes-label");
            startInfo.ArgumentList.Add(buttonSet.PrimaryLabel);
            startInfo.ArgumentList.Add("--no-label");
            startInfo.ArgumentList.Add(buttonSet.SecondaryLabel);

            if (buttonSet.ExtraLabels.Count > 0)
            {
                startInfo.ArgumentList.Add("--cancel-label");
                startInfo.ArgumentList.Add(buttonSet.ExtraLabels[0]);
            }
        }

        return startInfo;
    }

    private static string MapProcessResult(WpfMessageBoxOptions options, WpfMessageBoxProcessResult result)
    {
        var buttonSet = GetButtonSet(options.Button);
        var output = result.StandardOutput.Trim();

        if (TryParseResultName(output, buttonSet, out var parsed))
        {
            return parsed;
        }

        if (output.StartsWith("button returned:", StringComparison.OrdinalIgnoreCase) &&
            TryParseResultName(output["button returned:".Length..].Trim(), buttonSet, out parsed))
        {
            return parsed;
        }

        return result.ExitCode switch
        {
            0 => buttonSet.PrimaryResult,
            1 => buttonSet.SecondaryResult,
            2 when buttonSet.ExtraResults.Count > 0 => buttonSet.ExtraResults[0],
            _ => GetFallbackResult(options)
        };
    }

    private static bool TryParseResultName(string output, ButtonSet buttonSet, out string result)
    {
        foreach (var mapping in buttonSet.ResultByLabel)
        {
            if (string.Equals(output, mapping.Key, StringComparison.OrdinalIgnoreCase))
            {
                result = mapping.Value;
                return true;
            }
        }

        result = string.Empty;
        return false;
    }

    private static ButtonSet GetButtonSet(string? button)
    {
        return button switch
        {
            "OKCancel" => ButtonSet.Create("OK", "Cancel"),
            "YesNo" => ButtonSet.Create("Yes", "No"),
            "YesNoCancel" => ButtonSet.Create("Yes", "No", "Cancel"),
            "RetryCancel" => ButtonSet.Create("Retry", "Cancel"),
            "AbortRetryIgnore" => ButtonSet.Create("Abort", "Retry", "Ignore"),
            "CancelTryContinue" => ButtonSet.Create("TryAgain", "Cancel", "Continue"),
            _ => ButtonSet.Create("OK")
        };
    }

    private static string GetDefaultButtonLabel(WpfMessageBoxOptions options, ButtonSet buttonSet)
    {
        var fallback = GetFallbackResult(options);
        foreach (var mapping in buttonSet.ResultByLabel)
        {
            if (string.Equals(mapping.Value, fallback, StringComparison.Ordinal))
            {
                return mapping.Key;
            }
        }

        return buttonSet.PrimaryLabel;
    }

    private static string GetFallbackResult(WpfMessageBoxOptions options)
    {
        return string.IsNullOrWhiteSpace(options.FallbackResult)
            ? GetButtonSet(options.Button).PrimaryResult
            : options.FallbackResult;
    }

    private static string MapWindowsButtons(string? button)
    {
        return button switch
        {
            "OKCancel" => "OKCancel",
            "YesNo" => "YesNo",
            "YesNoCancel" => "YesNoCancel",
            "RetryCancel" => "RetryCancel",
            "AbortRetryIgnore" => "AbortRetryIgnore",
            "CancelTryContinue" => "CancelTryContinue",
            _ => "OK"
        };
    }

    private static string MapWindowsIcon(string? icon)
    {
        return icon switch
        {
            "Asterisk" or "Information" => "Information",
            "Error" or "Hand" or "Stop" => "Error",
            "Exclamation" or "Warning" => "Warning",
            "Question" => "Question",
            _ => "None"
        };
    }

    private static string MapWindowsDefaultButton(WpfMessageBoxOptions options)
    {
        var buttonSet = GetButtonSet(options.Button);
        var fallback = GetFallbackResult(options);
        for (var index = 0; index < buttonSet.Results.Count; index++)
        {
            if (string.Equals(buttonSet.Results[index], fallback, StringComparison.Ordinal))
            {
                return $"Button{Math.Min(index + 1, 3)}";
            }
        }

        return "Button1";
    }

    private static string MapMacIcon(string? icon)
    {
        return icon switch
        {
            "Error" or "Hand" or "Stop" => "stop",
            "Exclamation" or "Warning" => "caution",
            "Asterisk" or "Information" or "Question" => "note",
            _ => string.Empty
        };
    }

    private static string MapZenityDialogKind(WpfMessageBoxOptions options)
    {
        if (string.Equals(options.Button, "OK", StringComparison.Ordinal))
        {
            return options.Icon switch
            {
                "Error" or "Hand" or "Stop" => "--error",
                "Exclamation" or "Warning" => "--warning",
                _ => "--info"
            };
        }

        return "--question";
    }

    private static string GetKDialogKind(WpfMessageBoxOptions options)
    {
        if (string.Equals(options.Button, "OK", StringComparison.Ordinal))
        {
            return options.Icon switch
            {
                "Error" or "Hand" or "Stop" => "--error",
                "Exclamation" or "Warning" => "--sorry",
                _ => "--msgbox"
            };
        }

        return GetButtonSet(options.Button).ExtraLabels.Count > 0 ? "--yesnocancel" : "--yesno";
    }

    private static string EscapePowerShellString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string EscapeAppleScriptString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async ValueTask<WpfMessageBoxProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start message box command '{startInfo.FileName}'.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return new WpfMessageBoxProcessResult(process.ExitCode, output, error);
    }

    private sealed class ButtonSet
    {
        private ButtonSet(
            IReadOnlyList<string> labels,
            IReadOnlyList<string> results,
            IReadOnlyDictionary<string, string> resultByLabel)
        {
            Labels = labels;
            Results = results;
            ResultByLabel = resultByLabel;
        }

        public IReadOnlyList<string> Labels { get; }

        public IReadOnlyList<string> Results { get; }

        public IReadOnlyDictionary<string, string> ResultByLabel { get; }

        public string PrimaryLabel => Labels[0];

        public string SecondaryLabel => Labels.Count > 1 ? Labels[1] : Labels[0];

        public string? CancelLabel => Labels.Contains("Cancel", StringComparer.Ordinal) ? "Cancel" : null;

        public IReadOnlyList<string> ExtraLabels => Labels.Count > 2
            ? Labels.Skip(2).ToArray()
            : Array.Empty<string>();

        public string PrimaryResult => Results[0];

        public string SecondaryResult => Results.Count > 1 ? Results[1] : Results[0];

        public IReadOnlyList<string> ExtraResults => Results.Count > 2
            ? Results.Skip(2).ToArray()
            : Array.Empty<string>();

        public static ButtonSet Create(params string[] labels)
        {
            var results = labels.Select(label => label == "TryAgain" ? "TryAgain" : label).ToArray();
            var resultByLabel = labels
                .Select((label, index) => new KeyValuePair<string, string>(label, results[index]))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            return new ButtonSet(labels, results, resultByLabel);
        }
    }
}
