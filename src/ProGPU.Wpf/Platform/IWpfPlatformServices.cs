using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace System.Windows.Media.ProGPU.Platform;

public interface IWpfPlatformServices
{
    IWpfClipboard Clipboard { get; }

    IWpfCursorService Cursors { get; }

    IWpfDispatcherService Dispatcher { get; }

    IWpfDragDropService DragDrop { get; }

    IWpfFileDialogService FileDialogs { get; }

    IWpfInputService Input { get; }

    IWpfLauncher Launcher { get; }

    IWpfMonitorService Monitors { get; }

    IWpfTimerService Timers { get; }

    IWpfWindowEventService WindowEvents { get; }
}

public interface IWpfClipboard
{
    ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default);

    ValueTask SetTextAsync(string? text, CancellationToken cancellationToken = default);
}

public interface IWpfCursorService
{
    bool SetCursor(object inputSource, WpfCursor cursor);
}

public interface IWpfDispatcherService
{
    event EventHandler? WorkAvailable;

    bool CheckAccess();

    IWpfDispatcherOperation Post(Action callback, WpfDispatcherPriority priority = WpfDispatcherPriority.Normal);

    bool ProcessPending();
}

public interface IWpfDispatcherOperation : IDisposable
{
    WpfDispatcherPriority Priority { get; }

    bool IsCanceled { get; }

    bool IsCompleted { get; }

    bool Cancel();
}

public interface IWpfDragDropService
{
    event EventHandler<WpfDragDropEventArgs>? DragDropReceived;

    IDisposable Attach(object window);
}

public interface IWpfFileDialogService
{
    ValueTask<string?> OpenFileAsync(WpfFileDialogOptions options, CancellationToken cancellationToken = default);

    ValueTask<string?> SaveFileAsync(WpfFileDialogOptions options, CancellationToken cancellationToken = default);

    ValueTask<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}

public interface IWpfLauncher
{
    ValueTask OpenUriAsync(Uri uri, CancellationToken cancellationToken = default);

    ValueTask OpenFileAsync(string path, CancellationToken cancellationToken = default);
}

public interface IWpfInputService
{
    event EventHandler<WpfInputEventArgs>? InputReceived;

    IDisposable Attach(object window);
}

public interface IWpfMonitorService
{
    IReadOnlyList<WpfMonitorInfo> GetMonitors();
}

public interface IWpfTimerService
{
    IWpfTimer CreateTimer(TimeSpan interval, Action callback, bool isRepeating = true);
}

public interface IWpfTimer : IDisposable
{
    TimeSpan Interval { get; }

    bool IsEnabled { get; }

    void Start();

    void Stop();
}

public interface IWpfWindowEventService
{
    event EventHandler<WpfWindowEventArgs>? WindowEventReceived;

    IDisposable Attach(object window);
}

public sealed class WpfFileDialogOptions
{
    public string? Title { get; set; }

    public string? SuggestedFileName { get; set; }

    public IReadOnlyList<string> FileTypePatterns { get; set; } = Array.Empty<string>();
}

public enum WpfCursor
{
    Default,
    Arrow,
    IBeam,
    Crosshair,
    Hand,
    SizeWE,
    SizeNS,
    SizeNWSE,
    SizeNESW,
    SizeAll,
    No,
    Wait,
    AppStarting
}

public enum WpfDispatcherPriority
{
    Inactive = 0,
    SystemIdle = 1,
    ApplicationIdle = 2,
    ContextIdle = 3,
    Background = 4,
    Input = 5,
    Loaded = 6,
    Render = 7,
    DataBind = 8,
    Normal = 9,
    Send = 10
}

[Flags]
public enum WpfDragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4
}

public enum WpfDragDropEventKind
{
    Drop
}

public sealed class WpfDragDropData
{
    public WpfDragDropData(IReadOnlyList<string>? files = null, string? text = null)
    {
        Files = files ?? Array.Empty<string>();
        Text = text;
    }

    public IReadOnlyList<string> Files { get; }

    public string? Text { get; }

    public bool ContainsFiles => Files.Count > 0;

    public bool ContainsText => !string.IsNullOrEmpty(Text);
}

public sealed class WpfDragDropEventArgs : EventArgs
{
    public WpfDragDropEventArgs(
        WpfDragDropEventKind kind,
        WpfDragDropData data,
        WpfDragDropEffects allowedEffects = WpfDragDropEffects.Copy,
        WpfDragDropEffects acceptedEffect = WpfDragDropEffects.None,
        double x = 0,
        double y = 0)
    {
        ArgumentNullException.ThrowIfNull(data);

        Kind = kind;
        Data = data;
        AllowedEffects = allowedEffects;
        AcceptedEffect = acceptedEffect;
        X = x;
        Y = y;
    }

    public WpfDragDropEventKind Kind { get; }

    public WpfDragDropData Data { get; }

    public WpfDragDropEffects AllowedEffects { get; }

    public WpfDragDropEffects AcceptedEffect { get; set; }

    public double X { get; }

    public double Y { get; }
}

public readonly record struct WpfMonitorInfo(
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    double DpiScale,
    bool IsPrimary);

public enum WpfWindowEventKind
{
    Activated,
    Deactivated,
    FilesDropped
}

public sealed class WpfWindowEventArgs : EventArgs
{
    public WpfWindowEventArgs(WpfWindowEventKind kind, IReadOnlyList<string>? files = null)
    {
        Kind = kind;
        Files = files ?? Array.Empty<string>();
    }

    public WpfWindowEventKind Kind { get; }

    public IReadOnlyList<string> Files { get; }
}

public enum WpfInputEventKind
{
    KeyDown,
    KeyUp,
    TextInput,
    MouseMove,
    MouseDown,
    MouseUp,
    MouseWheel
}

public enum WpfMouseButton
{
    None,
    Left,
    Right,
    Middle,
    XButton1,
    XButton2,
    Other
}

[Flags]
public enum WpfInputModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Super = 8
}

public sealed class WpfInputEventArgs : EventArgs
{
    public WpfInputEventArgs(
        WpfInputEventKind kind,
        string? key = null,
        int scanCode = 0,
        char? character = null,
        double x = 0,
        double y = 0,
        double deltaX = 0,
        double deltaY = 0,
        WpfMouseButton button = WpfMouseButton.None,
        WpfInputModifiers modifiers = WpfInputModifiers.None)
    {
        Kind = kind;
        Key = key;
        ScanCode = scanCode;
        Character = character;
        X = x;
        Y = y;
        DeltaX = deltaX;
        DeltaY = deltaY;
        Button = button;
        Modifiers = modifiers;
    }

    public WpfInputEventKind Kind { get; }

    public string? Key { get; }

    public int ScanCode { get; }

    public char? Character { get; }

    public double X { get; }

    public double Y { get; }

    public double DeltaX { get; }

    public double DeltaY { get; }

    public WpfMouseButton Button { get; }

    public WpfInputModifiers Modifiers { get; }

    public bool Handled { get; set; }
}
