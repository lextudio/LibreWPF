using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using MediaImageSource = System.Windows.Media.ImageSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfBitmapSourceImageAdapter : IWpfImageSourceAdapter
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal enum ReflectedPixelFormatKind
    {
        Pbgra32,
        Bgra32,
        Bgr32,
        Bgr101010,
        Bgr24,
        Rgb24,
        BlackWhite,
        Gray2,
        Gray4,
        Gray8,
        Gray16,
        Bgr555,
        Bgr565,
        Rgb48,
        Rgba64,
        Prgba64,
        Cmyk32,
        Gray32Float,
        Rgb128Float,
        Rgba128Float,
        Prgba128Float,
        Indexed1,
        Indexed2,
        Indexed4,
        Indexed8
    }

    private readonly record struct PbgraColor(byte B, byte G, byte R, byte A);

    public MediaImageSource? AdaptImageSource(object? imageSource)
    {
        if (imageSource == null)
        {
            return null;
        }

        if (imageSource is MediaImageSource mediaImageSource)
        {
            return mediaImageSource;
        }

        if (!TryReadIntProperty(imageSource, "PixelWidth", out var width)
            || !TryReadIntProperty(imageSource, "PixelHeight", out var height)
            || width <= 0
            || height <= 0
            || !TryCopyPixelsAsPbgra32(imageSource, width, height, out var pixels, out var stride))
        {
            return null;
        }

        var dpiX = TryReadDoubleProperty(imageSource, "DpiX", out var readDpiX) ? readDpiX : 96;
        var dpiY = TryReadDoubleProperty(imageSource, "DpiY", out var readDpiY) ? readDpiY : 96;
        var bitmap = new WriteableBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32, palette: null);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.WritePixels(
                new Int32Rect(0, 0, width, height),
                handle.AddrOfPinnedObject(),
                pixels.Length,
                stride);
        }
        finally
        {
            handle.Free();
        }

        return bitmap;
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

        if (width <= 0
            || height <= 0
            || !TryReadPixelFormat(imageSource, out var formatKind, out var bitsPerPixel))
        {
            return false;
        }

        var palette = Array.Empty<PbgraColor>();
        if (RequiresPalette(formatKind)
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

        var sourceStride = checked((width * bitsPerPixel + 7) / 8);
        var sourcePixels = new byte[checked(sourceStride * height)];

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

        stride = checked(width * 4);
        pixels = ConvertToPbgra32(sourcePixels, width, height, sourceStride, formatKind, palette);
        return true;
    }

    internal static bool TryReadPixelFormat(
        object imageSource,
        out ReflectedPixelFormatKind formatKind,
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
                formatKind = ReflectedPixelFormatKind.Pbgra32;
                return true;
            }

            return false;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Pbgra32", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Pbgra32;
            return true;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Bgra32", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Bgra32;
            return true;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Bgr101010", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Bgr101010;
            return true;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Bgr32", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Bgr32;
            return true;
        }

        if (bitsPerPixel == 24
            && formatName.Contains("Bgr24", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Bgr24;
            return true;
        }

        if (bitsPerPixel == 24
            && formatName.Contains("Rgb24", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Rgb24;
            return true;
        }

        if (bitsPerPixel == 1
            && formatName.Contains("BlackWhite", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.BlackWhite;
            return true;
        }

        if (bitsPerPixel == 2
            && formatName.Contains("Gray2", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Gray2;
            return true;
        }

        if (bitsPerPixel == 4
            && formatName.Contains("Gray4", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Gray4;
            return true;
        }

        if (bitsPerPixel == 8
            && formatName.Contains("Gray8", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Gray8;
            return true;
        }

        if (bitsPerPixel == 16
            && formatName.Contains("Gray16", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Gray16;
            return true;
        }

        if (bitsPerPixel == 16
            && formatName.Contains("Bgr555", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Bgr555;
            return true;
        }

        if (bitsPerPixel == 16
            && formatName.Contains("Bgr565", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Bgr565;
            return true;
        }

        if (bitsPerPixel == 48
            && formatName.Contains("Rgb48", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Rgb48;
            return true;
        }

        if (bitsPerPixel == 64
            && formatName.Contains("Prgba64", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Prgba64;
            return true;
        }

        if (bitsPerPixel == 64
            && formatName.Contains("Rgba64", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Rgba64;
            return true;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Cmyk32", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Cmyk32;
            return true;
        }

        if (bitsPerPixel == 32
            && formatName.Contains("Gray32Float", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Gray32Float;
            return true;
        }

        if (bitsPerPixel == 128
            && formatName.Contains("Prgba128Float", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Prgba128Float;
            return true;
        }

        if (bitsPerPixel == 128
            && formatName.Contains("Rgba128Float", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Rgba128Float;
            return true;
        }

        if (bitsPerPixel == 128
            && formatName.Contains("Rgb128Float", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Rgb128Float;
            return true;
        }

        if (bitsPerPixel == 1
            && formatName.Contains("Indexed1", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Indexed1;
            return true;
        }

        if (bitsPerPixel == 2
            && formatName.Contains("Indexed2", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Indexed2;
            return true;
        }

        if (bitsPerPixel == 4
            && formatName.Contains("Indexed4", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Indexed4;
            return true;
        }

        if (bitsPerPixel == 8
            && formatName.Contains("Indexed8", StringComparison.OrdinalIgnoreCase))
        {
            formatKind = ReflectedPixelFormatKind.Indexed8;
            return true;
        }

        return false;
    }

    private static bool RequiresPalette(ReflectedPixelFormatKind formatKind)
    {
        return formatKind is ReflectedPixelFormatKind.Indexed1
            or ReflectedPixelFormatKind.Indexed2
            or ReflectedPixelFormatKind.Indexed4
            or ReflectedPixelFormatKind.Indexed8;
    }

    private static byte[] ConvertToPbgra32(
        byte[] source,
        int width,
        int height,
        int sourceStride,
        ReflectedPixelFormatKind formatKind,
        PbgraColor[] palette)
    {
        var destinationStride = checked(width * 4);
        var destination = new byte[checked(destinationStride * height)];

        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * sourceStride;
            var destinationRow = y * destinationStride;

            for (var x = 0; x < width; x++)
            {
                var destinationOffset = destinationRow + x * 4;
                switch (formatKind)
                {
                    case ReflectedPixelFormatKind.Pbgra32:
                    {
                        var sourceOffset = sourceRow + x * 4;
                        destination[destinationOffset] = source[sourceOffset];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset + 2];
                        destination[destinationOffset + 3] = source[sourceOffset + 3];
                        break;
                    }

                    case ReflectedPixelFormatKind.Bgra32:
                    {
                        var sourceOffset = sourceRow + x * 4;
                        var alpha = source[sourceOffset + 3];
                        destination[destinationOffset] = Premultiply(source[sourceOffset], alpha);
                        destination[destinationOffset + 1] = Premultiply(source[sourceOffset + 1], alpha);
                        destination[destinationOffset + 2] = Premultiply(source[sourceOffset + 2], alpha);
                        destination[destinationOffset + 3] = alpha;
                        break;
                    }

                    case ReflectedPixelFormatKind.Bgr32:
                    {
                        var sourceOffset = sourceRow + x * 4;
                        destination[destinationOffset] = source[sourceOffset];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset + 2];
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Bgr101010:
                    {
                        var sourceOffset = sourceRow + x * 4;
                        var value = ReadUInt32(source, sourceOffset);
                        destination[destinationOffset] = Scale10BitChannel((int)(value & 0x3ff));
                        destination[destinationOffset + 1] = Scale10BitChannel((int)((value >> 10) & 0x3ff));
                        destination[destinationOffset + 2] = Scale10BitChannel((int)((value >> 20) & 0x3ff));
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Bgr24:
                    {
                        var sourceOffset = sourceRow + x * 3;
                        destination[destinationOffset] = source[sourceOffset];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset + 2];
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Rgb24:
                    {
                        var sourceOffset = sourceRow + x * 3;
                        destination[destinationOffset] = source[sourceOffset + 2];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset];
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Gray8:
                    {
                        var gray = source[sourceRow + x];
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.BlackWhite:
                    {
                        var gray = ExpandIndexedGray(ReadPackedValue(source, sourceRow, x, 1), 1);
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Gray2:
                    {
                        var gray = ExpandIndexedGray(ReadPackedValue(source, sourceRow, x, 2), 3);
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Gray4:
                    {
                        var gray = ExpandIndexedGray(ReadPackedValue(source, sourceRow, x, 4), 15);
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Gray16:
                    {
                        var sourceOffset = sourceRow + x * 2;
                        var value = source[sourceOffset] | (source[sourceOffset + 1] << 8);
                        var gray = (byte)((value + 128) / 257);
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Bgr555:
                    {
                        var sourceOffset = sourceRow + x * 2;
                        var value = source[sourceOffset] | (source[sourceOffset + 1] << 8);
                        destination[destinationOffset] = Expand5BitChannel(value & 0x1f);
                        destination[destinationOffset + 1] = Expand5BitChannel((value >> 5) & 0x1f);
                        destination[destinationOffset + 2] = Expand5BitChannel((value >> 10) & 0x1f);
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Bgr565:
                    {
                        var sourceOffset = sourceRow + x * 2;
                        var value = source[sourceOffset] | (source[sourceOffset + 1] << 8);
                        destination[destinationOffset] = Expand5BitChannel(value & 0x1f);
                        destination[destinationOffset + 1] = Expand6BitChannel((value >> 5) & 0x3f);
                        destination[destinationOffset + 2] = Expand5BitChannel((value >> 11) & 0x1f);
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Rgb48:
                    {
                        var sourceOffset = sourceRow + x * 6;
                        var red = ReadUInt16(source, sourceOffset);
                        var green = ReadUInt16(source, sourceOffset + 2);
                        var blue = ReadUInt16(source, sourceOffset + 4);
                        destination[destinationOffset] = Scale16BitChannel(blue);
                        destination[destinationOffset + 1] = Scale16BitChannel(green);
                        destination[destinationOffset + 2] = Scale16BitChannel(red);
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Rgba64:
                    {
                        var sourceOffset = sourceRow + x * 8;
                        var red = ReadUInt16(source, sourceOffset);
                        var green = ReadUInt16(source, sourceOffset + 2);
                        var blue = ReadUInt16(source, sourceOffset + 4);
                        var alpha = ReadUInt16(source, sourceOffset + 6);
                        destination[destinationOffset] = Premultiply16BitChannel(blue, alpha);
                        destination[destinationOffset + 1] = Premultiply16BitChannel(green, alpha);
                        destination[destinationOffset + 2] = Premultiply16BitChannel(red, alpha);
                        destination[destinationOffset + 3] = Scale16BitChannel(alpha);
                        break;
                    }

                    case ReflectedPixelFormatKind.Prgba64:
                    {
                        var sourceOffset = sourceRow + x * 8;
                        var red = ReadUInt16(source, sourceOffset);
                        var green = ReadUInt16(source, sourceOffset + 2);
                        var blue = ReadUInt16(source, sourceOffset + 4);
                        var alpha = ReadUInt16(source, sourceOffset + 6);
                        destination[destinationOffset] = Scale16BitChannel(blue);
                        destination[destinationOffset + 1] = Scale16BitChannel(green);
                        destination[destinationOffset + 2] = Scale16BitChannel(red);
                        destination[destinationOffset + 3] = Scale16BitChannel(alpha);
                        break;
                    }

                    case ReflectedPixelFormatKind.Cmyk32:
                    {
                        var sourceOffset = sourceRow + x * 4;
                        var cyan = source[sourceOffset];
                        var magenta = source[sourceOffset + 1];
                        var yellow = source[sourceOffset + 2];
                        var black = source[sourceOffset + 3];
                        destination[destinationOffset] = ConvertCmykChannel(yellow, black);
                        destination[destinationOffset + 1] = ConvertCmykChannel(magenta, black);
                        destination[destinationOffset + 2] = ConvertCmykChannel(cyan, black);
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Gray32Float:
                    {
                        var gray = ScRgbToSrgbByte(ReadSingle(source, sourceRow + x * 4));
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Rgb128Float:
                    {
                        var sourceOffset = sourceRow + x * 16;
                        var red = ScRgbToSrgbByte(ReadSingle(source, sourceOffset));
                        var green = ScRgbToSrgbByte(ReadSingle(source, sourceOffset + 4));
                        var blue = ScRgbToSrgbByte(ReadSingle(source, sourceOffset + 8));
                        destination[destinationOffset] = blue;
                        destination[destinationOffset + 1] = green;
                        destination[destinationOffset + 2] = red;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case ReflectedPixelFormatKind.Rgba128Float:
                    {
                        var sourceOffset = sourceRow + x * 16;
                        var alpha = ScRgbAlphaToByte(ReadSingle(source, sourceOffset + 12));
                        destination[destinationOffset] = Premultiply(
                            ScRgbToSrgbByte(ReadSingle(source, sourceOffset + 8)),
                            alpha);
                        destination[destinationOffset + 1] = Premultiply(
                            ScRgbToSrgbByte(ReadSingle(source, sourceOffset + 4)),
                            alpha);
                        destination[destinationOffset + 2] = Premultiply(
                            ScRgbToSrgbByte(ReadSingle(source, sourceOffset)),
                            alpha);
                        destination[destinationOffset + 3] = alpha;
                        break;
                    }

                    case ReflectedPixelFormatKind.Prgba128Float:
                    {
                        var sourceOffset = sourceRow + x * 16;
                        var alphaValue = Clamp01(ReadSingle(source, sourceOffset + 12));
                        var alpha = ScRgbAlphaToByte(alphaValue);
                        destination[destinationOffset] = Premultiply(
                            ScRgbToSrgbByte(UnpremultiplyScRgb(ReadSingle(source, sourceOffset + 8), alphaValue)),
                            alpha);
                        destination[destinationOffset + 1] = Premultiply(
                            ScRgbToSrgbByte(UnpremultiplyScRgb(ReadSingle(source, sourceOffset + 4), alphaValue)),
                            alpha);
                        destination[destinationOffset + 2] = Premultiply(
                            ScRgbToSrgbByte(UnpremultiplyScRgb(ReadSingle(source, sourceOffset), alphaValue)),
                            alpha);
                        destination[destinationOffset + 3] = alpha;
                        break;
                    }

                    case ReflectedPixelFormatKind.Indexed1:
                    {
                        CopyPaletteColor(destination, destinationOffset, palette, ReadPackedValue(source, sourceRow, x, 1));
                        break;
                    }

                    case ReflectedPixelFormatKind.Indexed2:
                    {
                        CopyPaletteColor(destination, destinationOffset, palette, ReadPackedValue(source, sourceRow, x, 2));
                        break;
                    }

                    case ReflectedPixelFormatKind.Indexed4:
                    {
                        CopyPaletteColor(destination, destinationOffset, palette, ReadPackedValue(source, sourceRow, x, 4));
                        break;
                    }

                    case ReflectedPixelFormatKind.Indexed8:
                    {
                        CopyPaletteColor(destination, destinationOffset, palette, source[sourceRow + x]);
                        break;
                    }
                }
            }
        }

        return destination;
    }

    private static byte Premultiply(byte color, byte alpha)
    {
        return (byte)((color * alpha + 127) / 255);
    }

    private static int ReadPackedValue(byte[] source, int rowOffset, int x, int bitsPerPixel)
    {
        var bitOffset = x * bitsPerPixel;
        var packed = source[rowOffset + bitOffset / 8];
        var shift = 8 - bitsPerPixel - bitOffset % 8;
        return (packed >> shift) & ((1 << bitsPerPixel) - 1);
    }

    private static byte ExpandIndexedGray(int value, int maxValue)
    {
        return (byte)((value * 255 + maxValue / 2) / maxValue);
    }

    private static void CopyPaletteColor(byte[] destination, int destinationOffset, PbgraColor[] palette, int index)
    {
        var color = index < palette.Length ? palette[index] : default;
        destination[destinationOffset] = color.B;
        destination[destinationOffset + 1] = color.G;
        destination[destinationOffset + 2] = color.R;
        destination[destinationOffset + 3] = color.A;
    }

    private static byte Expand5BitChannel(int value)
    {
        return (byte)((value << 3) | (value >> 2));
    }

    private static byte Expand6BitChannel(int value)
    {
        return (byte)((value << 2) | (value >> 4));
    }

    private static byte Scale10BitChannel(int value)
    {
        return (byte)((value * 255 + 511) / 1023);
    }

    private static int ReadUInt16(byte[] source, int offset)
    {
        return source[offset] | (source[offset + 1] << 8);
    }

    private static uint ReadUInt32(byte[] source, int offset)
    {
        return (uint)(source[offset]
            | (source[offset + 1] << 8)
            | (source[offset + 2] << 16)
            | (source[offset + 3] << 24));
    }

    private static byte Scale16BitChannel(int value)
    {
        return (byte)((value + 128) / 257);
    }

    private static byte Premultiply16BitChannel(int color, int alpha)
    {
        var premultiplied = (int)(((long)color * alpha + 32767) / 65535);
        return Scale16BitChannel(premultiplied);
    }

    private static byte ConvertCmykChannel(byte colorant, byte black)
    {
        return (byte)(((255 - colorant) * (255 - black) + 127) / 255);
    }

    private static float ReadSingle(byte[] source, int offset)
    {
        return BitConverter.ToSingle(source, offset);
    }

    private static byte ScRgbToSrgbByte(float value)
    {
        var clamped = Clamp01(value);
        var encoded = clamped <= 0.0031308f
            ? 12.92f * clamped
            : 1.055f * MathF.Pow(clamped, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
    }

    private static byte ScRgbAlphaToByte(float alpha)
    {
        return (byte)Math.Clamp((int)MathF.Round(Clamp01(alpha) * 255f), 0, 255);
    }

    private static float UnpremultiplyScRgb(float value, float alpha)
    {
        return alpha <= 0f ? 0f : value / alpha;
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value))
        {
            return 0f;
        }

        return Math.Clamp(value, 0f, 1f);
    }

    private static bool TryReadPalette(object imageSource, out PbgraColor[] palette)
    {
        palette = Array.Empty<PbgraColor>();
        if (!TryGetPropertyValue(imageSource, "Palette", out var paletteValue)
            || paletteValue == null
            || !TryGetPropertyValue(paletteValue, "Colors", out var colorsValue)
            || colorsValue == null)
        {
            return false;
        }

        var colors = new List<PbgraColor>(256);
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

    private static bool TryReadColor(object colorValue, out PbgraColor color)
    {
        color = default;
        if (!TryReadByteProperty(colorValue, "A", out var alpha)
            || !TryReadByteProperty(colorValue, "R", out var red)
            || !TryReadByteProperty(colorValue, "G", out var green)
            || !TryReadByteProperty(colorValue, "B", out var blue))
        {
            return false;
        }

        color = new PbgraColor(
            Premultiply(blue, alpha),
            Premultiply(green, alpha),
            Premultiply(red, alpha),
            alpha);
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
