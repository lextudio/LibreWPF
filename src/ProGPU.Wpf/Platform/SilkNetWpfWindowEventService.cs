using System;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class SilkNetWpfWindowEventService : IWpfWindowEventService
{
    public event EventHandler<WpfWindowEventArgs>? WindowEventReceived;

    public IDisposable Attach(object window)
    {
        if (window is not IWindow silkWindow)
        {
            throw new ArgumentException("Silk.NET window event services require a Silk.NET window instance.", nameof(window));
        }

        return Attach(silkWindow);
    }

    public IDisposable Attach(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        Action<bool> focusChanged = isFocused => OnWindowEventReceived(CreateFocusChangedEvent(isFocused));
        Action<string[]> fileDrop = files => OnWindowEventReceived(CreateFileDropEvent(files));
        Action<Vector2D<int>> move = position =>
        {
            OnWindowEventReceived(CreateWindowPositionChangingEvent(position.X, position.Y));
            OnWindowEventReceived(CreateWindowPositionChangedEvent(position.X, position.Y));
        };
        Action<Vector2D<int>> resize = size => OnWindowEventReceived(CreateWindowSizeChangedEvent(size.X, size.Y));

        window.FocusChanged += focusChanged;
        window.FileDrop += fileDrop;
        window.Move += move;
        window.Resize += resize;

        return new WindowEventSubscription(window, focusChanged, fileDrop, move, resize);
    }

    public static WpfWindowEventArgs CreateFocusChangedEvent(bool isFocused)
    {
        return new WpfWindowEventArgs(isFocused ? WpfWindowEventKind.Activated : WpfWindowEventKind.Deactivated);
    }

    public static WpfWindowEventArgs CreateFileDropEvent(IReadOnlyList<string>? files)
    {
        return new WpfWindowEventArgs(WpfWindowEventKind.FilesDropped, files ?? Array.Empty<string>());
    }

    public static WpfWindowEventArgs CreateWindowPositionChangingEvent(int left, int top)
    {
        return new WpfWindowEventArgs(WpfWindowEventKind.WindowPositionChanging, left: left, top: top);
    }

    public static WpfWindowEventArgs CreateWindowPositionChangedEvent(int left, int top)
    {
        return new WpfWindowEventArgs(WpfWindowEventKind.WindowPositionChanged, left: left, top: top);
    }

    public static WpfWindowEventArgs CreateWindowSizeChangedEvent(int width, int height)
    {
        return new WpfWindowEventArgs(WpfWindowEventKind.WindowSizeChanged, width: width, height: height);
    }

    private void OnWindowEventReceived(WpfWindowEventArgs args)
    {
        WindowEventReceived?.Invoke(this, args);
    }

    private sealed class WindowEventSubscription : IDisposable
    {
        private readonly IWindow _window;
        private readonly Action<bool> _focusChanged;
        private readonly Action<string[]> _fileDrop;
        private readonly Action<Vector2D<int>> _move;
        private readonly Action<Vector2D<int>> _resize;
        private bool _isDisposed;

        public WindowEventSubscription(
            IWindow window,
            Action<bool> focusChanged,
            Action<string[]> fileDrop,
            Action<Vector2D<int>> move,
            Action<Vector2D<int>> resize)
        {
            _window = window;
            _focusChanged = focusChanged;
            _fileDrop = fileDrop;
            _move = move;
            _resize = resize;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _window.FocusChanged -= _focusChanged;
            _window.FileDrop -= _fileDrop;
            _window.Move -= _move;
            _window.Resize -= _resize;
            _isDisposed = true;
        }
    }
}
