// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.IO;
using MS.Internal;
using Microsoft.Win32.SafeHandles;
using System.Windows.Media;

namespace System.Windows.Media.Imaging
{
    #region BmpBitmapDecoder

    /// <summary>
    /// The built-in Microsoft Bmp (Bitmap) Decoder.
    /// </summary>
    public sealed class BmpBitmapDecoder : BitmapDecoder
    {
        /// <summary>
        /// Don't allow construction of a decoder with no params
        /// </summary>
        private BmpBitmapDecoder()
        {
        }

        /// <summary>
        /// Create a BmpBitmapDecoder given the Uri
        /// </summary>
        /// <param name="bitmapUri">Uri to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public BmpBitmapDecoder(
            Uri bitmapUri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapUri, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatBmp)
        {
        }

        /// <summary>
        /// If this decoder cannot handle the bitmap stream, it will throw an exception.
        /// </summary>
        /// <param name="bitmapStream">Stream to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public BmpBitmapDecoder(
            Stream bitmapStream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapStream, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatBmp)
        {
        }

        internal BmpBitmapDecoder(
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
        internal BmpBitmapDecoder(
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
                Span<byte> fileHeader = stackalloc byte[14];
                if (!TryReadExactly(stream, fileHeader))
                {
                    stream.Position = startPosition;
                    return false;
                }

                if (fileHeader[0] != (byte)'B' || fileHeader[1] != (byte)'M')
                {
                    stream.Position = startPosition;
                    return false;
                }

                uint pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(fileHeader.Slice(10, 4));
                uint dibHeaderSize = ReadUInt32(stream);
                if (dibHeaderSize < 40)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                int width = ReadInt32(stream);
                int signedHeight = ReadInt32(stream);
                ushort planes = ReadUInt16(stream);
                ushort bitsPerPixel = ReadUInt16(stream);
                uint compression = ReadUInt32(stream);
                _ = ReadUInt32(stream);
                int pixelsPerMeterX = ReadInt32(stream);
                int pixelsPerMeterY = ReadInt32(stream);
                _ = ReadUInt32(stream);
                _ = ReadUInt32(stream);

                if (width <= 0 || signedHeight == 0 || planes != 1 || compression != 0)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                PixelFormat pixelFormat = bitsPerPixel switch
                {
                    24 => PixelFormats.Bgr24,
                    32 => PixelFormats.Bgra32,
                    _ => throw new NotSupportedException($"Portable BMP decoding does not support {bitsPerPixel}-bit BI_RGB bitmaps.")
                };

                long extraHeaderBytes = dibHeaderSize - 40;
                if (extraHeaderBytes > 0)
                {
                    stream.Seek(extraHeaderBytes, SeekOrigin.Current);
                }

                int height = Math.Abs(signedHeight);
                bool topDown = signedHeight < 0;
                int sourceStride = checked(((width * bitsPerPixel) + 31) / 32 * 4);
                int targetStride = checked(((width * pixelFormat.BitsPerPixel) + 7) / 8);
                byte[] pixels = new byte[checked(targetStride * height)];
                byte[] sourceRow = new byte[sourceStride];

                stream.Position = startPosition + pixelOffset;
                for (int fileRow = 0; fileRow < height; fileRow++)
                {
                    ReadExactly(stream, sourceRow);
                    int targetRow = topDown ? fileRow : height - 1 - fileRow;
                    Buffer.BlockCopy(sourceRow, 0, pixels, targetRow * targetStride, targetStride);
                }

                BitmapSource source = BitmapSource.Create(
                    width,
                    height,
                    PixelsPerMeterToDpi(pixelsPerMeterX),
                    PixelsPerMeterToDpi(pixelsPerMeterY),
                    pixelFormat,
                    null,
                    pixels,
                    targetStride);

                frame = BitmapFrame.Create(source);
                if (!frame.IsFrozen && frame.CanFreeze)
                {
                    frame.Freeze();
                }

                return true;
            }
            catch
            {
                stream.Position = startPosition;
                throw;
            }
        }

        private static double PixelsPerMeterToDpi(int pixelsPerMeter)
        {
            return pixelsPerMeter > 0 ? pixelsPerMeter / 39.37007874015748 : 96.0;
        }

        private static ushort ReadUInt16(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[sizeof(ushort)];
            ReadExactly(stream, buffer);
            return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
        }

        private static uint ReadUInt32(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            ReadExactly(stream, buffer);
            return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        }

        private static int ReadInt32(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            ReadExactly(stream, buffer);
            return BinaryPrimitives.ReadInt32LittleEndian(buffer);
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
