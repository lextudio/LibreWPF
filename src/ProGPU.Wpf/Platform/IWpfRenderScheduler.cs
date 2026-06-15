using System;

namespace System.Windows.Media.ProGPU.Platform;

public interface IWpfRenderScheduler
{
    event EventHandler? RenderRequested;

    bool HasPendingRenderRequest { get; }

    void RequestRender();

    bool ConsumeRenderRequest();

    void Reset();
}
