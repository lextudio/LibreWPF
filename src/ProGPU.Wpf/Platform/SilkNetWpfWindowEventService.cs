using System;
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

        window.FocusChanged += focusChanged;
        window.FileDrop += fileDrop;

        return new WindowEventSubscription(window, focusChanged, fileDrop);
    }

    public static WpfWindowEventArgs CreateFocusChangedEvent(bool isFocused)
    {
        return new WpfWindowEventArgs(isFocused ? WpfWindowEventKind.Activated : WpfWindowEventKind.Deactivated);
    }

    public static WpfWindowEventArgs CreateFileDropEvent(IReadOnlyList<string>? files)
    {
        return new WpfWindowEventArgs(WpfWindowEventKind.FilesDropped, files ?? Array.Empty<string>());
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
        private bool _isDisposed;

        public WindowEventSubscription(IWindow window, Action<bool> focusChanged, Action<string[]> fileDrop)
        {
            _window = window;
            _focusChanged = focusChanged;
            _fileDrop = fileDrop;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _window.FocusChanged -= _focusChanged;
            _window.FileDrop -= _fileDrop;
            _isDisposed = true;
        }
    }
}
