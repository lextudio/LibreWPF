namespace System.Windows.Media.ProGPU;

public sealed class ProGpuWpfWindowOptions
{
    public string Title { get; set; } = "WPF ProGPU Host";

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 800;

    public bool VSync { get; set; }
}
