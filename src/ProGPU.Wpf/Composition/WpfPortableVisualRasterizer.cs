using System.Numerics;
using ProGPU.Wpf.Interop;
using Silk.NET.WebGPU;
using ProGpuGpuTexture = global::ProGPU.Backend.GpuTexture;
using ProGpuGpuTextureAlphaMode = global::ProGPU.Backend.GpuTextureAlphaMode;
using ProGpuGpuTextureReadbackBuffer = global::ProGPU.Backend.GpuTextureReadbackBuffer;

namespace System.Windows.Media.ProGPU.Composition;

/// <summary>
/// <see cref="IPortableVisualRasterizer"/> over a headless ProGPU composition target: the visual subtree is replayed into
/// the scene, rendered into an offscreen texture and read back. One headless device is created lazily and reused
/// (creating a WebGPU device per call would dominate the cost of small bitmaps); calls are serialized.
/// </summary>
internal sealed unsafe class WpfPortableVisualRasterizer : IPortableVisualRasterizer, IDisposable
{
    internal static readonly WpfPortableVisualRasterizer Default = new();

    private readonly object _sync = new();
    private ProGpuWpfCompositionTarget? _target;

    internal static void EnsureRegistered() => PortableWpfServiceRegistry.EnsureVisualRasterizer(Default);

    public byte[] Rasterize(object visual, int pixelWidth, int pixelHeight, double dpiX, double dpiY)
    {
        ArgumentNullException.ThrowIfNull(visual);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiY);

        var width = checked((uint)pixelWidth);
        var height = checked((uint)pixelHeight);

        // Per-axis scale (pixels per logical unit). The scene is laid out in whole logical units, rounded up, and drawn into a
        // floating-point viewport of exactly logical * scale pixels, so that the mapping is exact on each axis even when the
        // bitmap size is not a whole number of logical units or dpiX differs from dpiY. The texture is the viewport rounded
        // up; only its top-left pixelWidth x pixelHeight pixels are read back.
        var scaleX = dpiX / 96.0;
        var scaleY = dpiY / 96.0;
        var logicalWidth = (uint)Math.Max(1, (int)Math.Ceiling(pixelWidth / scaleX));
        var logicalHeight = (uint)Math.Max(1, (int)Math.Ceiling(pixelHeight / scaleY));
        var viewport = new global::ProGPU.Scene.RenderTargetViewport(0f, 0f, (float)(logicalWidth * scaleX), (float)(logicalHeight * scaleY));
        var textureWidth = checked((uint)Math.Max(pixelWidth, (int)Math.Ceiling(logicalWidth * scaleX)));
        var textureHeight = checked((uint)Math.Max(pixelHeight, (int)Math.Ceiling(logicalHeight * scaleY)));
        // Effect and cache resolution follow the finer axis, never the coarser.
        var dpiScale = (float)Math.Max(scaleX, scaleY);

        lock (_sync)
        {
            var target = _target ??= ProGpuWpfCompositionTarget.CreateHeadless(TextureFormat.Rgba8Unorm);
            try
            {
                return RasterizeCore(target, visual, width, height, pixelWidth, pixelHeight, logicalWidth, logicalHeight, textureWidth, textureHeight, viewport, dpiScale);
            }
            catch
            {
                // A lost device or a failed readback must not poison every later call: start from a fresh device next time.
                _target = null;
                target.Dispose();
                throw;
            }
            finally
            {
                // Do not keep the rendered visual (and its invalidation subscriptions) alive in the retained scene.
                _target?.Clear();
            }
        }
    }

    private static byte[] RasterizeCore(
        ProGpuWpfCompositionTarget target, object visual, uint width, uint height, int pixelWidth, int pixelHeight,
        uint logicalWidth, uint logicalHeight, uint textureWidth, uint textureHeight,
        global::ProGPU.Scene.RenderTargetViewport viewport, float dpiScale)
    {
        target.Compositor.ClearColor = Vector4.Zero; // transparent, not the compositor's opaque default
        target.Clear();
        // The replay frame has the size of the texture it is rendered into (they differ by less than a pixel per axis when the
        // bitmap is not a whole number of logical units).
        target.ReplayVisualSubtree(visual, textureWidth, textureHeight);

        using var texture = new ProGpuGpuTexture(
            target.Context,
            textureWidth,
            textureHeight,
            TextureFormat.Rgba8Unorm,
            TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst,
            "RenderTargetBitmap",
            alphaMode: ProGpuGpuTextureAlphaMode.Premultiplied);
        target.Render(logicalWidth, logicalHeight, textureWidth, textureHeight, viewport, dpiScale, texture.ViewPtr);

        using var readback = new ProGpuGpuTextureReadbackBuffer(target.Context);
        readback.EnsureCapacity(width, height, bytesPerPixel: 4);
        var pixels = new byte[checked(pixelWidth * pixelHeight * 4)];
        fixed (byte* destination = pixels)
        {
            if (!readback.TryReadTextureRows(texture, width, height, destination, destinationBytesPerRow: width * 4, bytesPerPixel: 4))
            {
                throw new InvalidOperationException("Reading the rendered visual back from the GPU failed" +
                    (readback.LastMapTimedOut ? " (timed out)." : "."));
            }
        }

        // The texture is RGBA, the bitmap is BGRA; the compositor output is already premultiplied.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }

        return pixels;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _target?.Dispose();
            _target = null;
        }
    }
}
