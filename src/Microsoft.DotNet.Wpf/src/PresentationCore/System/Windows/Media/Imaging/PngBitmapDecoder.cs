// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using Microsoft.Win32.SafeHandles;
using MS.Internal;
using System.Windows.Media;

namespace System.Windows.Media.Imaging
{
    #region PngBitmapDecoder

    /// <summary>
    /// The built-in Microsoft Png (Bitmap) Decoder.
    /// </summary>
    public sealed class PngBitmapDecoder : BitmapDecoder
    {
        /// <summary>
        /// Don't allow construction of a decoder with no params
        /// </summary>
        private PngBitmapDecoder()
        {
        }

        /// <summary>
        /// Create a PngBitmapDecoder given the Uri
        /// </summary>
        /// <param name="bitmapUri">Uri to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public PngBitmapDecoder(
            Uri bitmapUri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapUri, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatPng)
        {
        }

        /// <summary>
        /// If this decoder cannot handle the bitmap stream, it will throw an exception.
        /// </summary>
        /// <param name="bitmapStream">Stream to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public PngBitmapDecoder(
            Stream bitmapStream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapStream, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatPng)
        {
        }

        internal PngBitmapDecoder(
            BitmapFrame portableFrame,
            Uri baseUri,
            Uri uri,
            Stream stream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(true)
        {
            InitializePortableFrames(baseUri, uri, stream, createOptions, cacheOption, portableFrame);
        }

        /// <summary>
        /// Internal Constructor
        /// </summary>
        internal PngBitmapDecoder(
            SafeMILHandle decoderHandle,
            BitmapDecoder decoder,
            Uri baseUri,
            Uri uri,
            Stream stream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption,
            bool insertInDecoderCache,
            bool originalWritable,
            Stream uriStream,
            UnmanagedMemoryStream unmanagedMemoryStream,
            SafeFileHandle safeFilehandle
            ) : base(decoderHandle, decoder, baseUri, uri, stream, createOptions, cacheOption, insertInDecoderCache, originalWritable, uriStream, unmanagedMemoryStream, safeFilehandle)
        {
        }

        public override BitmapPalette Palette
        {
            get
            {
                if (InternalDecoder == null && _frames != null && _frames.Count > 0)
                {
                    return _frames[0].Palette;
                }

                return base.Palette;
            }
        }

        public override ReadOnlyCollection<ColorContext> ColorContexts
        {
            get
            {
                if (InternalDecoder == null)
                {
                    return null;
                }

                return base.ColorContexts;
            }
        }

        public override BitmapSource Thumbnail
        {
            get
            {
                return InternalDecoder == null ? null : base.Thumbnail;
            }
        }

        public override BitmapMetadata Metadata
        {
            get
            {
                return InternalDecoder == null ? null : base.Metadata;
            }
        }

        public override BitmapSource Preview
        {
            get
            {
                return InternalDecoder == null ? null : base.Preview;
            }
        }

        internal static bool TryCreatePortableFrame(
            Stream stream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption,
            out BitmapFrame frame)
        {
            frame = null;

            if (stream == null || !stream.CanSeek)
            {
                return false;
            }

            long startPosition = stream.Position;

            try
            {
                Span<byte> signature = stackalloc byte[8];
                if (!TryReadExactly(stream, signature))
                {
                    stream.Position = startPosition;
                    return false;
                }

                if (!IsPngSignature(signature))
                {
                    stream.Position = startPosition;
                    return false;
                }

                int width = 0;
                int height = 0;
                byte bitDepth = 0;
                byte colorType = 0;
                byte interlaceMethod = 0;
                List<Color> paletteColors = null;
                byte[] paletteAlpha = null;
                using MemoryStream idat = new MemoryStream();
                Span<byte> chunkTypeBytes = stackalloc byte[4];

                while (true)
                {
                    uint length = ReadUInt32BigEndian(stream);
                    ReadExactly(stream, chunkTypeBytes);
                    string chunkType = new string(new[]
                    {
                        (char)chunkTypeBytes[0],
                        (char)chunkTypeBytes[1],
                        (char)chunkTypeBytes[2],
                        (char)chunkTypeBytes[3]
                    });

                    byte[] chunkData = new byte[length];
                    ReadExactly(stream, chunkData);
                    _ = ReadUInt32BigEndian(stream);

                    switch (chunkType)
                    {
                        case "IHDR":
                            if (chunkData.Length != 13)
                            {
                                throw new FileFormatException(null, SR.Image_CantDealWithStream);
                            }

                            width = BinaryPrimitives.ReadInt32BigEndian(chunkData.AsSpan(0, 4));
                            height = BinaryPrimitives.ReadInt32BigEndian(chunkData.AsSpan(4, 4));
                            bitDepth = chunkData[8];
                            colorType = chunkData[9];
                            byte compressionMethod = chunkData[10];
                            byte filterMethod = chunkData[11];
                            interlaceMethod = chunkData[12];
                            if (width <= 0 || height <= 0 || compressionMethod != 0 || filterMethod != 0 || interlaceMethod != 0)
                            {
                                throw new FileFormatException(null, SR.Image_CantDealWithStream);
                            }

                            break;
                        case "PLTE":
                            paletteColors = ReadPalette(chunkData);
                            break;
                        case "tRNS":
                            paletteAlpha = chunkData;
                            break;
                        case "IDAT":
                            idat.Write(chunkData, 0, chunkData.Length);
                            break;
                        case "IEND":
                            frame = CreateFrame(width, height, bitDepth, colorType, paletteColors, paletteAlpha, idat);
                            if (!frame.IsFrozen && frame.CanFreeze)
                            {
                                frame.Freeze();
                            }

                            return true;
                    }
                }
            }
            catch
            {
                stream.Position = startPosition;
                throw;
            }
        }

        internal static bool TryCreatePortableFrameFromUri(
            Uri uri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption,
            out BitmapFrame frame)
        {
            frame = null;

            if (!TryGetLocalPath(uri, out string localPath))
            {
                return false;
            }

            using FileStream stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return TryCreatePortableFrame(stream, createOptions, cacheOption, out frame);
        }

        private static BitmapFrame CreateFrame(
            int width,
            int height,
            byte bitDepth,
            byte colorType,
            List<Color> paletteColors,
            byte[] paletteAlpha,
            MemoryStream compressedData)
        {
            if (bitDepth != 8)
            {
                throw new NotSupportedException($"Portable PNG decoding does not support {bitDepth}-bit images.");
            }

            int componentCount = GetComponentCount(colorType);
            int sourceStride = checked(width * componentCount);
            int targetStride = checked(width * 4);
            byte[] rawRows = Inflate(compressedData, checked((sourceStride + 1) * height));
            byte[] unfiltered = Unfilter(rawRows, width, height, sourceStride, componentCount);
            byte[] pixels = new byte[checked(targetStride * height)];

            for (int y = 0; y < height; y++)
            {
                int sourceRow = y * sourceStride;
                int targetRow = y * targetStride;
                for (int x = 0; x < width; x++)
                {
                    int sourceOffset = sourceRow + x * componentCount;
                    int targetOffset = targetRow + x * 4;
                    WriteBgraPixel(colorType, unfiltered, sourceOffset, paletteColors, paletteAlpha, pixels, targetOffset);
                }
            }

            BitmapSource source = BitmapSource.Create(
                width,
                height,
                96.0,
                96.0,
                PixelFormats.Bgra32,
                null,
                pixels,
                targetStride);

            return BitmapFrame.Create(source);
        }

        private static int GetComponentCount(byte colorType)
        {
            switch (colorType)
            {
                case 0:
                    return 1;
                case 2:
                    return 3;
                case 3:
                    return 1;
                case 4:
                    return 2;
                case 6:
                    return 4;
                default:
                    throw new NotSupportedException($"Portable PNG decoding does not support color type {colorType}.");
            }
        }

        private static void WriteBgraPixel(
            byte colorType,
            byte[] source,
            int sourceOffset,
            List<Color> paletteColors,
            byte[] paletteAlpha,
            byte[] target,
            int targetOffset)
        {
            byte r;
            byte g;
            byte b;
            byte a = 255;

            switch (colorType)
            {
                case 0:
                    r = g = b = source[sourceOffset];
                    break;
                case 2:
                    r = source[sourceOffset];
                    g = source[sourceOffset + 1];
                    b = source[sourceOffset + 2];
                    break;
                case 3:
                    if (paletteColors == null)
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    int paletteIndex = source[sourceOffset];
                    if (paletteIndex >= paletteColors.Count)
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    Color color = paletteColors[paletteIndex];
                    r = color.R;
                    g = color.G;
                    b = color.B;
                    if (paletteAlpha != null && paletteIndex < paletteAlpha.Length)
                    {
                        a = paletteAlpha[paletteIndex];
                    }

                    break;
                case 4:
                    r = g = b = source[sourceOffset];
                    a = source[sourceOffset + 1];
                    break;
                case 6:
                    r = source[sourceOffset];
                    g = source[sourceOffset + 1];
                    b = source[sourceOffset + 2];
                    a = source[sourceOffset + 3];
                    break;
                default:
                    throw new NotSupportedException($"Portable PNG decoding does not support color type {colorType}.");
            }

            target[targetOffset] = b;
            target[targetOffset + 1] = g;
            target[targetOffset + 2] = r;
            target[targetOffset + 3] = a;
        }

        private static List<Color> ReadPalette(byte[] data)
        {
            if (data.Length == 0 || data.Length % 3 != 0)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            var colors = new List<Color>(data.Length / 3);
            for (int i = 0; i < data.Length; i += 3)
            {
                colors.Add(Color.FromRgb(data[i], data[i + 1], data[i + 2]));
            }

            return colors;
        }

        private static byte[] Inflate(MemoryStream compressedData, int expectedLength)
        {
            compressedData.Position = 0;
            byte[] result = new byte[expectedLength];
            using var zlib = new ZLibStream(compressedData, CompressionMode.Decompress, leaveOpen: true);
            int offset = 0;
            while (offset < result.Length)
            {
                int read = zlib.Read(result, offset, result.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }

            return result;
        }

        private static byte[] Unfilter(byte[] rawRows, int width, int height, int sourceStride, int bytesPerPixel)
        {
            _ = width;
            byte[] result = new byte[checked(sourceStride * height)];
            int rawOffset = 0;
            for (int y = 0; y < height; y++)
            {
                byte filterType = rawRows[rawOffset++];
                int rowOffset = y * sourceStride;
                int previousRowOffset = rowOffset - sourceStride;
                for (int x = 0; x < sourceStride; x++)
                {
                    byte raw = rawRows[rawOffset++];
                    byte left = x >= bytesPerPixel ? result[rowOffset + x - bytesPerPixel] : (byte)0;
                    byte above = y > 0 ? result[previousRowOffset + x] : (byte)0;
                    byte upperLeft = y > 0 && x >= bytesPerPixel ? result[previousRowOffset + x - bytesPerPixel] : (byte)0;
                    result[rowOffset + x] = filterType switch
                    {
                        0 => raw,
                        1 => unchecked((byte)(raw + left)),
                        2 => unchecked((byte)(raw + above)),
                        3 => unchecked((byte)(raw + ((left + above) >> 1))),
                        4 => unchecked((byte)(raw + PaethPredictor(left, above, upperLeft))),
                        _ => throw new FileFormatException(null, SR.Image_CantDealWithStream)
                    };
                }
            }

            return result;
        }

        private static byte PaethPredictor(byte left, byte above, byte upperLeft)
        {
            int p = left + above - upperLeft;
            int pa = Math.Abs(p - left);
            int pb = Math.Abs(p - above);
            int pc = Math.Abs(p - upperLeft);

            if (pa <= pb && pa <= pc)
            {
                return left;
            }

            return pb <= pc ? above : upperLeft;
        }

        private static bool TryGetLocalPath(Uri uri, out string localPath)
        {
            localPath = null;

            if (uri == null)
            {
                return false;
            }

            if (uri.IsAbsoluteUri)
            {
                if (!uri.IsFile)
                {
                    return false;
                }

                localPath = uri.LocalPath;
                return true;
            }

            localPath = uri.OriginalString;
            return !string.IsNullOrEmpty(localPath);
        }

        private static bool IsPngSignature(ReadOnlySpan<byte> signature)
        {
            ReadOnlySpan<byte> expected = [137, 80, 78, 71, 13, 10, 26, 10];
            return signature.SequenceEqual(expected);
        }

        private static uint ReadUInt32BigEndian(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            ReadExactly(stream, buffer);
            return BinaryPrimitives.ReadUInt32BigEndian(buffer);
        }

        private static bool TryReadExactly(Stream stream, Span<byte> buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer.Slice(offset));
                if (read == 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }

        private static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            if (!TryReadExactly(stream, buffer))
            {
                throw new EndOfStreamException();
            }
        }

        #region Internal Abstract

        /// Need to implement this to derive from the "sealed" object
        internal override void SealObject()
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    #endregion
}
