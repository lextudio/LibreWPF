using System;
using System.Runtime.ExceptionServices;

namespace System.Windows.Media.ProGPU;

public enum ProGpuWpfResizeStage
{
    ClientSizeEntered,
    NativeSizeResolving,
    NativeSizeAssigning,
    NativeSizeAssigned,
    NativeResizeEntered,
    NativeResizeReturned,
    FramebufferResizeEntered,
    FramebufferResizeSkipped,
    FramebufferSourceUnavailable,
    FramebufferRenderEntering,
    FramebufferRenderReturned,
    FramebufferResizeReturned,
    SourceLayoutEntering,
    SourceLayoutReturned,
    SourceLayoutRejected,
    SwapChainConfigureEntering,
    SwapChainConfigureReturned,
    SwapChainConfigureRejected,
    ClientSizeReturned,
    FramebufferRenderDeferred
}

/// <summary>
/// Cached state at a resize checkpoint, not a native geometry query or proof of
/// surface acquisition. Dimensions in ArgumentWidth/Height belong to that stage;
/// cached client/source/requested dimensions remain logical client units.
/// </summary>
public readonly record struct ProGpuWpfResizeCheckpoint(
    ProGpuWpfResizeStage Stage,
    int ManagedThreadId,
    int RegistrationThreadId,
    bool IsOwnerThread,
    bool HasActivity,
    ProGpuWpfRenderActivitySnapshot Activity,
    long ArgumentWidth,
    long ArgumentHeight,
    int ClientWidth,
    int ClientHeight,
    int SourceWidth,
    int SourceHeight,
    int RequestedWidth,
    int RequestedHeight,
    bool HasWindow,
    bool HasTarget,
    bool HasSource,
    bool SameWindow,
    bool SameTarget,
    bool SameSource,
    bool IsDisposed,
    bool CloseStarted,
    bool IsRenderingLiveResize,
    bool IsProcessingDispatcherWork,
    int NativeEventDispatchDepth);

/// <summary>
/// One host-owned, synchronous resize observation. Dispose only detaches; call
/// ThrowIfFailed after a successful resize to reject incomplete diagnostics.
/// Observer failures never escape native callbacks or replace a resize failure.
/// </summary>
public sealed class ProGpuWpfResizeDiagnosticScope : IDisposable
{
    internal const int Capacity = 64;
    private Action<ProGpuWpfResizeCheckpoint>? _observe;
    private Action? _detach;
    private ExceptionDispatchInfo? _failure;
    private int _count;

    internal ProGpuWpfResizeDiagnosticScope(Action<ProGpuWpfResizeCheckpoint> observe,
        Action detach, object? window, object? target, object? source)
    {
        _observe = observe;
        _detach = detach;
        Window = window;
        Target = target;
        Source = source;
        RegistrationThreadId = Environment.CurrentManagedThreadId;
    }

    internal object? Window { get; }
    internal object? Target { get; }
    internal object? Source { get; }
    internal int RegistrationThreadId { get; }
    internal bool CanRecord => _observe is not null && _failure is null;

    internal void Record(ProGpuWpfResizeCheckpoint checkpoint)
    {
        if (!CanRecord) return;
        try
        {
            if (++_count > Capacity)
                throw new InvalidOperationException("Native resize diagnostic budget exceeded.");
            _observe!(checkpoint);
        }
        catch (Exception error) { Fail(error); }
    }

    internal void Fail(Exception error) => _failure ??= ExceptionDispatchInfo.Capture(error);

    public void ThrowIfFailed() => _failure?.Throw();

    public void Dispose()
    {
        _observe = null;
        Action? detach = _detach;
        _detach = null;
        detach?.Invoke();
    }
}
