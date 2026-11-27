namespace ProGPU.Wpf.Interop;

/// <summary>
/// Rasterizes a WPF visual subtree into pixels without the native MIL (what <c>RenderTargetBitmap</c> needs on platforms
/// that have no <c>wpfgfx</c>).
/// </summary>
public interface IPortableVisualRasterizer
{
    /// <summary>
    /// Renders <paramref name="visual"/> (a <c>System.Windows.Media.Visual</c>, passed as <see cref="object"/> so that this
    /// assembly does not reference PresentationCore) into a new premultiplied BGRA buffer of exactly
    /// <paramref name="pixelWidth"/> x <paramref name="pixelHeight"/> pixels (stride = width * 4), transparent where
    /// nothing is drawn. One logical unit (1/96 inch) is <c>dpi / 96</c> pixels. The call is synchronous and retains
    /// neither the visual nor the buffer. Failure to render (no GPU device, unsupported content) throws; it never returns a
    /// blank buffer in place of a result.
    /// </summary>
    byte[] Rasterize(object visual, int pixelWidth, int pixelHeight, double dpiX, double dpiY);
}

public static partial class PortableWpfServiceRegistry
{
    private static readonly PortableDefaultServiceSlot<IPortableVisualRasterizer> s_visualRasterizer = new();

    public static bool TryGetVisualRasterizer(out IPortableVisualRasterizer service)
    {
        service = s_visualRasterizer.Current!;
        return service != null;
    }

    /// <summary>Replaces the explicit override; disposal reveals the installed default, if any.</summary>
    public static IDisposable RegisterVisualRasterizer(IPortableVisualRasterizer service) => s_visualRasterizer.Register(service);

    /// <summary>Installs a default without overriding an explicit provider; performs no native work.</summary>
    public static void EnsureVisualRasterizer(IPortableVisualRasterizer service) => s_visualRasterizer.EnsureDefault(service);
}
