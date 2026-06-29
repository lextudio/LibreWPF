using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Silk.NET.WebGPU;
using MediaImageSource = System.Windows.Media.ImageSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfBitmapSourceImageAdapter : IWpfImageSourceAdapter
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
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

        int width;
        int height;
        double dpiX;
        double dpiY;
        Pbgra32PixelBuffer pixelBuffer;
        if (TryCopyPortableBitmapSourceAsPbgra32Buffer(
                imageSource,
                out var portablePixels,
                out pixelBuffer))
        {
            width = portablePixels.Width;
            height = portablePixels.Height;
            dpiX = portablePixels.DpiX;
            dpiY = portablePixels.DpiY;
        }
        else if (!TryReadIntProperty(imageSource, "PixelWidth", out width)
            || !TryReadIntProperty(imageSource, "PixelHeight", out height)
            || width <= 0
            || height <= 0
            || !TryCopyPixelsAsPbgra32Buffer(imageSource, width, height, out pixelBuffer))
        {
            return null;
        }
        else
        {
            dpiX = TryReadDoubleProperty(imageSource, "DpiX", out var readDpiX) ? readDpiX : 96;
            dpiY = TryReadDoubleProperty(imageSource, "DpiY", out var readDpiY) ? readDpiY : 96;
        }

        var context = ResolveGpuContext();
        if (imageSource is MediaImageSource mediaSource
            && TryCreateGpuTexture(context, width, height, pixelBuffer, out var adaptedTexture))
        {
            s_adaptedTextures.GetValue(mediaSource, static _ => new AdaptedTextureCache())
                .Set(context, adaptedTexture);
            return mediaSource;
        }

        return TryCreateShimWriteableBitmap(width, height, dpiX, dpiY, pixelBuffer);
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

    private static MediaImageSource? TryCreateShimWriteableBitmap(
        int width,
        int height,
        double dpiX,
        double dpiY,
        Pbgra32PixelBuffer pixelBuffer)
    {
        var presentationCore = typeof(MediaImageSource).Assembly;
        var writeableBitmapType = presentationCore.GetType("System.Windows.Media.Imaging.WriteableBitmap");
        var pixelFormatsType = presentationCore.GetType("System.Windows.Media.Imaging.PixelFormats");
        var int32RectType = typeof(MediaImageSource).Assembly.GetType("System.Windows.Int32Rect")
            ?? Type.GetType("System.Windows.Int32Rect, WindowsBase");
        var writePixels = writeableBitmapType?.GetMethod(
            "WritePbgra32Pixels",
            MemberFlags,
            binder: null,
            types: int32RectType == null ? Type.EmptyTypes : new[] { int32RectType, typeof(Pbgra32PixelBuffer) },
            modifiers: null);
        var pbgra32Property = pixelFormatsType?.GetProperty("Pbgra32", BindingFlags.Static | BindingFlags.Public);
        var constructor = writeableBitmapType?.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: pbgra32Property == null
                ? Type.EmptyTypes
                : new[]
                {
                    typeof(int),
                    typeof(int),
                    typeof(double),
                    typeof(double),
                    pbgra32Property.PropertyType,
                    presentationCore.GetType("System.Windows.Media.Imaging.BitmapPalette")
                        ?? typeof(object)
                },
            modifiers: null);
        var int32RectConstructor = int32RectType?.GetConstructor(new[] { typeof(int), typeof(int), typeof(int), typeof(int) });

        if (writeableBitmapType == null
            || pbgra32Property == null
            || constructor == null
            || writePixels == null
            || int32RectConstructor == null)
        {
            return null;
        }

        try
        {
            var bitmap = constructor.Invoke(new[] { width, height, dpiX, dpiY, pbgra32Property.GetValue(null), null });
            var rect = int32RectConstructor.Invoke(new object[] { 0, 0, width, height });
            writePixels.Invoke(bitmap, new object[] { rect, pixelBuffer });
            return bitmap is MediaImageSource mediaImageSource && CanProvideGpuTexture(mediaImageSource)
                ? mediaImageSource
                : null;
        }
        catch (TargetInvocationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (MethodAccessException)
        {
        }

        return null;
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

        if (imageSource is IPortableBitmapSourcePixelsSource portableSource
            && TryCopyPortableBitmapSourceAsPbgra32Buffer(
                portableSource,
                out var portablePixels,
                out pixelBuffer))
        {
            if (portablePixels.Width == width && portablePixels.Height == height)
            {
                return true;
            }

            pixelBuffer = default;
            return false;
        }

        if (width <= 0
            || height <= 0
            || !TryReadPixelFormat(imageSource, out var formatKind, out _))
        {
            return false;
        }

        var palette = Array.Empty<Pbgra32Color>();
        if (PixelDataConverter.RequiresPalette(formatKind)
            && !TryReadPalette(imageSource, out palette))
        {
            return false;
        }

        var copyPixels = imageSource.GetType().GetMethod(
            "CopyPixels",
            MemberFlags,
            binder: null,
            types: new[] { typeof(Array), typeof(int), typeof(int) },
            modifiers: null);
        if (copyPixels == null)
        {
            return false;
        }

        if (!PixelDataConverter.TryGetMinimumStride(width, formatKind, out var sourceStride)
            || !PixelDataConverter.TryGetSourceByteLength(width, height, sourceStride, formatKind, out var sourceByteLength))
        {
            return false;
        }

        var sourcePixels = new byte[sourceByteLength];

        try
        {
            copyPixels.Invoke(imageSource, new object[] { sourcePixels, sourceStride, 0 });
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        var sourceBuffer = new PixelDataBuffer(
            width,
            height,
            sourceStride,
            formatKind,
            sourcePixels,
            palette);
        if (!sourceBuffer.TryConvertToPbgra32(out var pbgra32Buffer))
        {
            return false;
        }

        pixelBuffer = pbgra32Buffer;
        return true;
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

    internal static bool TryReadPixelFormat(
        object imageSource,
        out PixelDataFormat formatKind,
        out int bitsPerPixel)
    {
        formatKind = default;
        bitsPerPixel = 0;

        if (!TryGetPropertyValue(imageSource, "Format", out var format) || format == null)
        {
            return false;
        }

        if (!TryReadIntProperty(format, "BitsPerPixel", out bitsPerPixel))
        {
            return false;
        }

        var formatName = format.ToString();
        if (string.IsNullOrWhiteSpace(formatName))
        {
            if (bitsPerPixel == 32)
            {
                formatKind = PixelDataFormat.Pbgra32;
                return true;
            }

            return false;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Pbgra32", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Pbgra32;
            return true;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Bgra32", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Bgra32;
            return true;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Bgr101010", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Bgr101010;
            return true;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Bgr32", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Bgr32;
            return true;
        }

        if (bitsPerPixel == 24
            && formatName.Contains("Bgr24", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Bgr24;
            return true;
        }

        if (bitsPerPixel == 24
            && formatName.Contains("Rgb24", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Rgb24;
            return true;
        }

        if (bitsPerPixel == 1
            && formatName.Contains("BlackWhite", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.BlackWhite;
            return true;
        }

        if (bitsPerPixel == 2
            && formatName.Contains("Gray2", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Gray2;
            return true;
        }

        if (bitsPerPixel == 4
            && formatName.Contains("Gray4", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Gray4;
            return true;
        }

        if (bitsPerPixel == 8
            && formatName.Contains("Gray8", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Gray8;
            return true;
        }

        if (bitsPerPixel == 16
            && formatName.Contains("Gray16", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Gray16;
            return true;
        }

        if (bitsPerPixel == 16
            && formatName.Contains("Bgr555", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Bgr555;
            return true;
        }

        if (bitsPerPixel == 16
            && formatName.Contains("Bgr565", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Bgr565;
            return true;
        }

        if (bitsPerPixel == 48
            && formatName.Contains("Rgb48", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Rgb48;
            return true;
        }

        if (bitsPerPixel == 64
            && formatName.Contains("Prgba64", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Prgba64;
            return true;
        }

        if (bitsPerPixel == 64
            && formatName.Contains("Rgba64", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Rgba64;
            return true;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Cmyk32", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Cmyk32;
            return true;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Gray32Float", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Gray32Float;
            return true;
        }

        if (bitsPerPixel == 128
            && formatName.Contains("Prgba128Float", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Prgba128Float;
            return true;
        }

        if (bitsPerPixel == 128
            && formatName.Contains("Rgba128Float", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Rgba128Float;
            return true;
        }

        if (bitsPerPixel == 128
            && formatName.Contains("Rgb128Float", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Rgb128Float;
            return true;
        }

        if (bitsPerPixel == 1
            && formatName.Contains("Indexed1", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Indexed1;
            return true;
        }

        if (bitsPerPixel == 2
            && formatName.Contains("Indexed2", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Indexed2;
            return true;
        }

        if (bitsPerPixel == 4
            && formatName.Contains("Indexed4", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Indexed4;
            return true;
        }

        if (bitsPerPixel == 8
            && formatName.Contains("Indexed8", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = PixelDataFormat.Indexed8;
            return true;
        }

        return false;
    }

    private static bool TryReadPalette(object imageSource, out Pbgra32Color[] palette)
    {
        palette = Array.Empty<Pbgra32Color>();
        if (!TryGetPropertyValue(imageSource, "Palette", out var paletteValue)
            || paletteValue == null
            || !TryGetPropertyValue(paletteValue, "Colors", out var colorsValue)
            || colorsValue == null)
        {
            return false;
        }

        var colors = new List<Pbgra32Color>(256);
        if (colorsValue is System.Collections.IEnumerable enumerable)
        {
            foreach (var colorValue in enumerable)
            {
                if (colors.Count >= 256)
                {
                    break;
                }

                if (colorValue != null && TryReadColor(colorValue, out var color))
                {
                    colors.Add(color);
                }
            }
        }
        else if (TryReadIntProperty(colorsValue, "Count", out var count) && count > 0)
        {
            var getColor = FindIndexer(colorsValue.GetType());
            if (getColor == null)
            {
                return false;
            }

            for (var i = 0; i < count && colors.Count < 256; i++)
            {
                var colorValue = getColor(colorsValue, i);
                if (colorValue != null && TryReadColor(colorValue, out var color))
                {
                    colors.Add(color);
                }
            }
        }

        if (colors.Count == 0)
        {
            return false;
        }

        palette = colors.ToArray();
        return true;
    }

    private static bool TryReadColor(object colorValue, out Pbgra32Color color)
    {
        color = default;
        if (!TryReadByteProperty(colorValue, "A", out var alpha)
            || !TryReadByteProperty(colorValue, "R", out var red)
            || !TryReadByteProperty(colorValue, "G", out var green)
            || !TryReadByteProperty(colorValue, "B", out var blue))
        {
            return false;
        }

        color = Pbgra32Color.FromStraightArgb(alpha, red, green, blue);
        return true;
    }

    private static bool TryReadIntProperty(object instance, string propertyName, out int value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        switch (propertyValue)
        {
            case int intValue:
                value = intValue;
                return true;
            case uint uintValue when uintValue <= int.MaxValue:
                value = (int)uintValue;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadByteProperty(object instance, string propertyName, out byte value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        switch (propertyValue)
        {
            case byte byteValue:
                value = byteValue;
                return true;
            case int intValue when intValue is >= byte.MinValue and <= byte.MaxValue:
                value = (byte)intValue;
                return true;
            case uint uintValue when uintValue <= byte.MaxValue:
                value = (byte)uintValue;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadDoubleProperty(object instance, string propertyName, out double value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        switch (propertyValue)
        {
            case double doubleValue:
                value = doubleValue;
                return true;
            case float floatValue:
                value = floatValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, MemberFlags);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            value = null;
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }

    private static Func<object, int, object?>? FindIndexer(Type type)
    {
        foreach (var property in type.GetProperties(MemberFlags))
        {
            var parameters = property.GetIndexParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
            {
                return (instance, index) => property.GetValue(instance, new object[] { index });
            }
        }

        return null;
    }
}
