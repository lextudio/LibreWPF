using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Silk.NET.WebGPU;
using MediaImageSource = System.Windows.Media.ImageSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfBitmapSourceImageAdapter : IWpfImageSourceAdapter
{
    private static readonly ConditionalWeakTable<MediaImageSource, AdaptedTextureCache> s_adaptedTextures = new();

    public MediaImageSource? AdaptImageSource(object? imageSource)
    {
        if (imageSource == null)
        {
            return null;
        }

        if (imageSource is MediaImageSource mediaImageSource
            && CanProvideGpuTexture(mediaImageSource))
        {
            return mediaImageSource;
        }

        if (!TryCopyPortableBitmapSourceAsPbgra32Buffer(
                imageSource,
                out var portablePixels,
                out var pixelBuffer))
        {
            return null;
        }

        var width = portablePixels.Width;
        var height = portablePixels.Height;
        var context = ResolveGpuContext();
        if (imageSource is MediaImageSource mediaSource
            && TryCreateGpuTexture(context, width, height, pixelBuffer, out var adaptedTexture))
        {
            s_adaptedTextures.GetValue(mediaSource, static _ => new AdaptedTextureCache())
                .Set(context, adaptedTexture);
            return mediaSource;
        }

        return null;
    }

    internal static bool CanProvideGpuTexture(object imageSource)
    {
        return imageSource is IProGpuTextureSource;
    }

    internal static bool TryGetGpuTexture(MediaImageSource imageSource, out GpuTexture texture)
    {
        texture = null!;
        var currentContext = ResolveCurrentGpuContext();

        if (imageSource is IProGpuTextureSource textureSource)
        {
            try
            {
                if (textureSource.TryGetGpuTexture(out var resolvedTexture)
                    && IsUsableInContext(resolvedTexture, currentContext))
                {
                    texture = resolvedTexture;
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (s_adaptedTextures.TryGetValue(imageSource, out var adapted)
            && adapted.TryGet(currentContext, out texture))
        {
            return true;
        }

        return false;
    }

    private static bool TryCreateGpuTexture(
        WgpuContext context,
        int width,
        int height,
        Pbgra32PixelBuffer pixelBuffer,
        out GpuTexture texture)
    {
        texture = null!;

        try
        {
            texture = new GpuTexture(
                context,
                (uint)width,
                (uint)height,
                TextureFormat.Bgra8Unorm,
                TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding,
                "WPF BitmapSource Adapter Texture",
                alphaMode: GpuTextureAlphaMode.Premultiplied);
            texture.WritePbgra32(pixelBuffer);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }

        texture?.Dispose();
        texture = null!;
        return false;
    }

    private static WgpuContext? ResolveCurrentGpuContext()
    {
        var current = WgpuContext.Current;
        if (current != null && !current.IsDisposed)
        {
            return current;
        }

        foreach (var active in WgpuContext.ActiveContexts)
        {
            if (!active.IsDisposed)
            {
                return active;
            }
        }

        return null;
    }

    private static WgpuContext ResolveGpuContext()
    {
        var current = WgpuContext.Current;
        if (current != null && !current.IsDisposed)
        {
            return current;
        }

        foreach (var active in WgpuContext.ActiveContexts)
        {
            if (!active.IsDisposed)
            {
                return active;
            }
        }

        var context = new WgpuContext();
        context.Initialize(null);
        return context;
    }

    private static bool IsUsableInContext(GpuTexture texture, WgpuContext? context)
    {
        var textureContext = texture.Context;
        if (textureContext == null)
        {
            return !texture.IsDisposed;
        }

        return !texture.IsDisposed
            && !textureContext.IsDisposed
            && (context == null || ReferenceEquals(textureContext, context));
    }

    private sealed class AdaptedTextureCache
    {
        private readonly object _gate = new();
        private readonly Dictionary<WgpuContext, GpuTexture> _texturesByContext = new();

        public void Set(WgpuContext context, GpuTexture texture)
        {
            lock (_gate)
            {
                RemoveDisposedNoLock();
                _texturesByContext[context] = texture;
            }
        }

        public bool TryGet(WgpuContext? context, out GpuTexture texture)
        {
            lock (_gate)
            {
                RemoveDisposedNoLock();

                if (context != null)
                {
                    return _texturesByContext.TryGetValue(context, out texture!)
                        && IsUsableInContext(texture, context);
                }

                foreach (var candidate in _texturesByContext.Values)
                {
                    if (IsUsableInContext(candidate, context))
                    {
                        texture = candidate;
                        return true;
                    }
                }
            }

            texture = null!;
            return false;
        }

        private void RemoveDisposedNoLock()
        {
            List<WgpuContext>? staleContexts = null;
            foreach (var entry in _texturesByContext)
            {
                if (entry.Key.IsDisposed || entry.Value.IsDisposed)
                {
                    staleContexts ??= new List<WgpuContext>();
                    staleContexts.Add(entry.Key);
                }
            }

            if (staleContexts == null)
            {
                return;
            }

            foreach (var context in staleContexts)
            {
                _texturesByContext.Remove(context);
            }
        }
    }

    internal static bool TryCopyPixelsAsPbgra32(
        object imageSource,
        int width,
        int height,
        out byte[] pixels,
        out int stride)
    {
        pixels = Array.Empty<byte>();
        stride = 0;

        if (!TryCopyPixelsAsPbgra32Buffer(imageSource, width, height, out var pixelBuffer))
        {
            return false;
        }

        pixels = pixelBuffer.Pixels;
        stride = pixelBuffer.Stride;
        return true;
    }

    internal static bool TryCopyPixelsAsPbgra32Buffer(
        object imageSource,
        int width,
        int height,
        out Pbgra32PixelBuffer pixelBuffer)
    {
        pixelBuffer = default;

        if (imageSource is not IPortableBitmapSourcePixelsSource portableSource
            || !TryCopyPortableBitmapSourceAsPbgra32Buffer(
                portableSource,
                out var portablePixels,
                out pixelBuffer))
        {
            return false;
        }

        if (portablePixels.Width == width && portablePixels.Height == height)
        {
            return true;
        }

        pixelBuffer = default;
        return false;
    }

    private static bool TryCopyPortableBitmapSourceAsPbgra32Buffer(
        object imageSource,
        out PortableBitmapSourcePixels portablePixels,
        out Pbgra32PixelBuffer pixelBuffer)
    {
        if (imageSource is IPortableBitmapSourcePixelsSource portableSource)
        {
            return TryCopyPortableBitmapSourceAsPbgra32Buffer(
                portableSource,
                out portablePixels,
                out pixelBuffer);
        }

        portablePixels = null!;
        pixelBuffer = default;
        return false;
    }

    private static bool TryCopyPortableBitmapSourceAsPbgra32Buffer(
        IPortableBitmapSourcePixelsSource portableSource,
        out PortableBitmapSourcePixels portablePixels,
        out Pbgra32PixelBuffer pixelBuffer)
    {
        portablePixels = null!;
        pixelBuffer = default;

        if (!portableSource.TryGetPortableBitmapSourcePixels(out portablePixels)
            || portablePixels == null
            || portablePixels.Width <= 0
            || portablePixels.Height <= 0
            || portablePixels.Stride <= 0
            || portablePixels.Pixels == null
            || !TryMapPixelDataFormat(portablePixels.Format, out var formatKind))
        {
            return false;
        }

        var palette = CreatePalette(portablePixels.Palette);
        if (PixelDataConverter.RequiresPalette(formatKind) && palette.Length == 0)
        {
            return false;
        }

        var sourceBuffer = new PixelDataBuffer(
            portablePixels.Width,
            portablePixels.Height,
            portablePixels.Stride,
            formatKind,
            portablePixels.Pixels,
            palette);
        if (!sourceBuffer.TryConvertToPbgra32(out var pbgra32Buffer))
        {
            return false;
        }

        pixelBuffer = pbgra32Buffer;
        return true;
    }

    private static bool TryMapPixelDataFormat(
        PortablePixelDataFormat portableFormat,
        out PixelDataFormat format)
    {
        switch (portableFormat)
        {
            case PortablePixelDataFormat.Pbgra32:
                format = PixelDataFormat.Pbgra32;
                return true;
            case PortablePixelDataFormat.Bgra32:
                format = PixelDataFormat.Bgra32;
                return true;
            case PortablePixelDataFormat.Bgr32:
                format = PixelDataFormat.Bgr32;
                return true;
            case PortablePixelDataFormat.Bgr101010:
                format = PixelDataFormat.Bgr101010;
                return true;
            case PortablePixelDataFormat.Bgr24:
                format = PixelDataFormat.Bgr24;
                return true;
            case PortablePixelDataFormat.Rgb24:
                format = PixelDataFormat.Rgb24;
                return true;
            case PortablePixelDataFormat.BlackWhite:
                format = PixelDataFormat.BlackWhite;
                return true;
            case PortablePixelDataFormat.Gray2:
                format = PixelDataFormat.Gray2;
                return true;
            case PortablePixelDataFormat.Gray4:
                format = PixelDataFormat.Gray4;
                return true;
            case PortablePixelDataFormat.Gray8:
                format = PixelDataFormat.Gray8;
                return true;
            case PortablePixelDataFormat.Gray16:
                format = PixelDataFormat.Gray16;
                return true;
            case PortablePixelDataFormat.Bgr555:
                format = PixelDataFormat.Bgr555;
                return true;
            case PortablePixelDataFormat.Bgr565:
                format = PixelDataFormat.Bgr565;
                return true;
            case PortablePixelDataFormat.Rgb48:
                format = PixelDataFormat.Rgb48;
                return true;
            case PortablePixelDataFormat.Rgba64:
                format = PixelDataFormat.Rgba64;
                return true;
            case PortablePixelDataFormat.Prgba64:
                format = PixelDataFormat.Prgba64;
                return true;
            case PortablePixelDataFormat.Cmyk32:
                format = PixelDataFormat.Cmyk32;
                return true;
            case PortablePixelDataFormat.Gray32Float:
                format = PixelDataFormat.Gray32Float;
                return true;
            case PortablePixelDataFormat.Rgb128Float:
                format = PixelDataFormat.Rgb128Float;
                return true;
            case PortablePixelDataFormat.Rgba128Float:
                format = PixelDataFormat.Rgba128Float;
                return true;
            case PortablePixelDataFormat.Prgba128Float:
                format = PixelDataFormat.Prgba128Float;
                return true;
            case PortablePixelDataFormat.Indexed1:
                format = PixelDataFormat.Indexed1;
                return true;
            case PortablePixelDataFormat.Indexed2:
                format = PixelDataFormat.Indexed2;
                return true;
            case PortablePixelDataFormat.Indexed4:
                format = PixelDataFormat.Indexed4;
                return true;
            case PortablePixelDataFormat.Indexed8:
                format = PixelDataFormat.Indexed8;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static Pbgra32Color[] CreatePalette(PortablePbgra32Color[]? portablePalette)
    {
        if (portablePalette == null || portablePalette.Length == 0)
        {
            return Array.Empty<Pbgra32Color>();
        }

        int count = Math.Min(256, portablePalette.Length);
        var palette = new Pbgra32Color[count];
        for (var i = 0; i < count; i++)
        {
            var color = portablePalette[i];
            palette[i] = new Pbgra32Color(color.B, color.G, color.R, color.A);
        }

        return palette;
    }

}
