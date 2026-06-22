namespace System.Windows.Media.ProGPU;

public sealed class ProGpuWpfWindowOptions
{
    public string Title { get; set; } = "WPF ProGPU Host";

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 800;

    public int? Left { get; set; }

    public int? Top { get; set; }

    public bool VSync { get; set; }

    public bool IsEventDriven { get; set; } = true;

    public bool IsVisible { get; set; } = true;

    public ProGpuWpfWindowState WindowState { get; set; } = ProGpuWpfWindowState.Normal;
}

public enum ProGpuWpfWindowState
{
    Normal,
    Minimized,
    Maximized
}
