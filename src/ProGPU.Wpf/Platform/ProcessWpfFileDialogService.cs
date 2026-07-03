using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace System.Windows.Media.ProGPU.Platform;

public enum WpfFileDialogPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

public enum WpfFileDialogKind
{
    OpenFile,
    SaveFile,
    PickFolder
}

public readonly record struct WpfFileDialogProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public delegate ValueTask<WpfFileDialogProcessResult> WpfFileDialogProcessRunner(
    ProcessStartInfo startInfo,
    CancellationToken cancellationToken);

public sealed class ProcessWpfFileDialogService : IWpfFileDialogService
{
    private readonly Func<WpfFileDialogPlatform> _platformProvider;
    private readonly WpfFileDialogProcessRunner _processRunner;

    public ProcessWpfFileDialogService()
        : this(DetectPlatform, RunProcessAsync)
    {
    }

    public ProcessWpfFileDialogService(
        Func<WpfFileDialogPlatform> platformProvider,
        WpfFileDialogProcessRunner processRunner)
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public ValueTask<string?> OpenFileAsync(WpfFileDialogOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RunDialogAsync(WpfFileDialogKind.OpenFile, options, cancellationToken);
    }

    public ValueTask<string?> SaveFileAsync(WpfFileDialogOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RunDialogAsync(WpfFileDialogKind.SaveFile, options, cancellationToken);
    }

    public ValueTask<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        return RunDialogAsync(WpfFileDialogKind.PickFolder, new WpfFileDialogOptions(), cancellationToken);
    }

    public static WpfFileDialogPlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WpfFileDialogPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return WpfFileDialogPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return WpfFileDialogPlatform.Linux;
        }

        return WpfFileDialogPlatform.Unsupported;
    }

    public static IReadOnlyList<ProcessStartInfo> CreateStartInfos(
        WpfFileDialogPlatform platform,
        WpfFileDialogKind kind,
        WpfFileDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return platform switch
        {
            WpfFileDialogPlatform.Windows => new[] { CreateWindowsStartInfo(kind, options) },
            WpfFileDialogPlatform.MacOS => new[] { CreateMacStartInfo(kind, options) },
            WpfFileDialogPlatform.Linux => CreateLinuxStartInfos(kind, options),
            _ => Array.Empty<ProcessStartInfo>()
        };
    }

    private async ValueTask<string?> RunDialogAsync(
        WpfFileDialogKind kind,
        WpfFileDialogOptions options,
        CancellationToken cancellationToken)
    {
        var commands = CreateStartInfos(_platformProvider(), kind, options);
        if (commands.Count == 0)
        {
            throw new PlatformNotSupportedException("File dialog services are not available on this platform.");
        }

        Exception? lastStartException = null;
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await _processRunner(command, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    var output = result.StandardOutput.Trim();
                    return string.IsNullOrEmpty(output) ? null : output;
                }

                return null;
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

        throw new InvalidOperationException("No file dialog command could be started.", lastStartException);
    }

    private static ProcessStartInfo CreateWindowsStartInfo(WpfFileDialogKind kind, WpfFileDialogOptions options)
    {
        var command = kind switch
        {
            WpfFileDialogKind.OpenFile => CreateWindowsOpenCommand(options),
            WpfFileDialogKind.SaveFile => CreateWindowsSaveCommand(options),
            WpfFileDialogKind.PickFolder => CreateWindowsFolderCommand(options),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return CreateStartInfo("powershell", "-NoProfile", "-NonInteractive", "-Command", command);
    }

    private static string CreateWindowsOpenCommand(WpfFileDialogOptions options)
    {
        var filter = CreateWindowsFilter(options.FileTypePatterns);
        return "Add-Type -AssemblyName System.Windows.Forms; "
            + "$f = New-Object System.Windows.Forms.OpenFileDialog; "
            + $"$f.Title = '{EscapePowerShellString(options.Title ?? "Open File")}'; "
            + $"$f.Filter = '{EscapePowerShellString(filter)}'; "
            + "if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Write($f.FileName) }";
    }

    private static string CreateWindowsSaveCommand(WpfFileDialogOptions options)
    {
        var filter = CreateWindowsFilter(options.FileTypePatterns);
        var command = "Add-Type -AssemblyName System.Windows.Forms; "
            + "$f = New-Object System.Windows.Forms.SaveFileDialog; "
            + $"$f.Title = '{EscapePowerShellString(options.Title ?? "Save File")}'; "
            + $"$f.Filter = '{EscapePowerShellString(filter)}'; ";

        if (!string.IsNullOrWhiteSpace(options.SuggestedFileName))
        {
            command += $"$f.FileName = '{EscapePowerShellString(options.SuggestedFileName!)}'; ";
        }

        return command + "if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Write($f.FileName) }";
    }

    private static string CreateWindowsFolderCommand(WpfFileDialogOptions options)
    {
        return "Add-Type -AssemblyName System.Windows.Forms; "
            + "$f = New-Object System.Windows.Forms.FolderBrowserDialog; "
            + $"$f.Description = '{EscapePowerShellString(options.Title ?? "Select Folder")}'; "
            + "if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Write($f.SelectedPath) }";
    }

    private static string CreateWindowsFilter(IReadOnlyList<string> patterns)
    {
        var normalized = NormalizeFileTypePatterns(patterns);
        if (normalized.Count == 0)
        {
            return "All Files (*.*)|*.*";
        }

        var joined = string.Join(";", normalized);
        return $"Selected Files ({joined})|{joined}|All Files (*.*)|*.*";
    }

    private static ProcessStartInfo CreateMacStartInfo(WpfFileDialogKind kind, WpfFileDialogOptions options)
    {
        var prompt = EscapeAppleScriptString(options.Title ?? DefaultTitle(kind));
        var script = kind switch
        {
            WpfFileDialogKind.OpenFile => $"POSIX path of (choose file {CreateMacTypeClause(options.FileTypePatterns)}with prompt \"{prompt}\")",
            WpfFileDialogKind.SaveFile => $"POSIX path of (choose file name default name \"{EscapeAppleScriptString(options.SuggestedFileName ?? "untitled")}\" with prompt \"{prompt}\")",
            WpfFileDialogKind.PickFolder => $"POSIX path of (choose folder with prompt \"{prompt}\")",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return CreateStartInfo("osascript", "-e", script);
    }

    private static string CreateMacTypeClause(IReadOnlyList<string> patterns)
    {
        var normalized = NormalizeFileTypePatterns(patterns);
        if (normalized.Count == 0)
        {
            return string.Empty;
        }

        var extensions = new List<string>(normalized.Count);
        foreach (var pattern in normalized)
        {
            var extension = pattern.TrimStart('*', '.');
            if (!string.IsNullOrWhiteSpace(extension) &&
                !extension.Contains('*', StringComparison.Ordinal))
            {
                extensions.Add($"\"{EscapeAppleScriptString(extension)}\"");
            }
        }

        return extensions.Count == 0 ? string.Empty : $"of type {{{string.Join(", ", extensions)}}} ";
    }

    private static IReadOnlyList<ProcessStartInfo> CreateLinuxStartInfos(WpfFileDialogKind kind, WpfFileDialogOptions options)
    {
        return new[]
        {
            CreateLinuxZenityStartInfo(kind, options),
            CreateLinuxKDialogStartInfo(kind, options)
        };
    }

    private static ProcessStartInfo CreateLinuxZenityStartInfo(WpfFileDialogKind kind, WpfFileDialogOptions options)
    {
        var title = options.Title ?? DefaultTitle(kind);
        var startInfo = CreateStartInfo("zenity", "--file-selection", $"--title={title}");

        if (kind == WpfFileDialogKind.SaveFile)
        {
            startInfo.ArgumentList.Add("--save");
            startInfo.ArgumentList.Add("--confirm-overwrite");
            if (!string.IsNullOrWhiteSpace(options.SuggestedFileName))
            {
                startInfo.ArgumentList.Add($"--filename={options.SuggestedFileName}");
            }
        }
        else if (kind == WpfFileDialogKind.PickFolder)
        {
            startInfo.ArgumentList.Add("--directory");
        }

        var normalized = NormalizeFileTypePatterns(options.FileTypePatterns);
        if (normalized.Count > 0 && kind != WpfFileDialogKind.PickFolder)
        {
            startInfo.ArgumentList.Add($"--file-filter=Selected Files | {string.Join(' ', normalized)}");
            startInfo.ArgumentList.Add("--file-filter=All Files | *");
        }

        return startInfo;
    }

    private static ProcessStartInfo CreateLinuxKDialogStartInfo(WpfFileDialogKind kind, WpfFileDialogOptions options)
    {
        var title = options.Title ?? DefaultTitle(kind);
        var startInfo = CreateStartInfo("kdialog", "--title", title);

        if (kind == WpfFileDialogKind.OpenFile)
        {
            startInfo.ArgumentList.Add("--getopenfilename");
            startInfo.ArgumentList.Add(".");
            AddKDialogFileFilter(startInfo, options.FileTypePatterns);
        }
        else if (kind == WpfFileDialogKind.SaveFile)
        {
            startInfo.ArgumentList.Add("--getsavefilename");
            startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(options.SuggestedFileName) ? "." : options.SuggestedFileName!);
            AddKDialogFileFilter(startInfo, options.FileTypePatterns);
        }
        else if (kind == WpfFileDialogKind.PickFolder)
        {
            startInfo.ArgumentList.Add("--getexistingdirectory");
            startInfo.ArgumentList.Add(".");
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return startInfo;
    }

    private static void AddKDialogFileFilter(ProcessStartInfo startInfo, IReadOnlyList<string> patterns)
    {
        var normalized = NormalizeFileTypePatterns(patterns);
        if (normalized.Count == 0)
        {
            return;
        }

        startInfo.ArgumentList.Add($"{string.Join(' ', normalized)}|Selected Files\n*|All Files");
    }

    private static string DefaultTitle(WpfFileDialogKind kind)
    {
        return kind switch
        {
            WpfFileDialogKind.OpenFile => "Open File",
            WpfFileDialogKind.SaveFile => "Save File",
            WpfFileDialogKind.PickFolder => "Select Folder",
            _ => string.Empty
        };
    }

    private static IReadOnlyList<string> NormalizeFileTypePatterns(IReadOnlyList<string>? patterns)
    {
        if (patterns == null)
        {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(patterns.Count);
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var trimmed = pattern.Trim();
            if (trimmed == "*" || trimmed == "*.*")
            {
                continue;
            }

            normalized.Add(trimmed.StartsWith('.') ? $"*{trimmed}" : trimmed);
        }

        return normalized.Count == 0 ? Array.Empty<string>() : normalized;
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

    private static async ValueTask<WpfFileDialogProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start file dialog command '{startInfo.FileName}'.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return new WpfFileDialogProcessResult(process.ExitCode, output, error);
    }
}
