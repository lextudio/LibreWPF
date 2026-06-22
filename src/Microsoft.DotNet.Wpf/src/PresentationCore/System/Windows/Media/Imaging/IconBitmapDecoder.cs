// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32.SafeHandles;
using MS.Internal;

namespace System.Windows.Media.Imaging
{
    #region IconBitmapDecoder

    /// <summary>
    /// The built-in Microsoft Icon (Bitmap) Decoder.
    /// </summary>
    public sealed class IconBitmapDecoder : BitmapDecoder
    {
        /// <summary>
        /// Don't allow construction of a decoder with no params
        /// </summary>
        private IconBitmapDecoder()
        {
        }

        /// <summary>
        /// Create a IconBitmapDecoder given the Uri
        /// </summary>
        /// <param name="bitmapUri">Uri to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public IconBitmapDecoder(
            Uri bitmapUri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapUri, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatIco)
        {
        }

        /// <summary>
        /// If this decoder cannot handle the bitmap stream, it will throw an exception.
        /// </summary>
        /// <param name="bitmapStream">Stream to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public IconBitmapDecoder(
            Stream bitmapStream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapStream, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatIco)
        {
        }

        internal IconBitmapDecoder(
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
        internal IconBitmapDecoder(
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
                Span<byte> header = stackalloc byte[6];
                if (!TryReadExactly(stream, header))
                {
                    stream.Position = startPosition;
                    return false;
                }

                ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(0, 2));
                ushort iconType = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(2, 2));
                ushort count = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2));
                if (reserved != 0 || iconType != 1 || count == 0)
                {
                    stream.Position = startPosition;
                    return false;
                }

                if (count > 1024)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                List<IconDirectoryEntry> entries = new List<IconDirectoryEntry>(count);
                for (int i = 0; i < count; i++)
                {
                    entries.Add(ReadDirectoryEntry(stream));
                }

                entries.Sort(CompareDirectoryEntries);
                foreach (IconDirectoryEntry entry in entries)
                {
                    if (entry.BytesInResource == 0)
                    {
                        continue;
                    }

                    if (!TrySeekToImage(stream, startPosition, entry, out long imagePosition))
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    Span<byte> signature = stackalloc byte[8];
                    if (!TryReadExactly(stream, signature))
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    if (!IsPngSignature(signature))
                    {
                        continue;
                    }

                    stream.Position = imagePosition;
                    byte[] imageBytes = new byte[checked((int)entry.BytesInResource)];
                    ReadExactly(stream, imageBytes);

                    using MemoryStream imageStream = new MemoryStream(imageBytes, writable: false);
                    if (!PngBitmapDecoder.TryCreatePortableFrame(imageStream, createOptions, cacheOption, out frame))
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    if (!frame.IsFrozen && frame.CanFreeze)
                    {
                        frame.Freeze();
                    }

                    return true;
                }

                throw new NotSupportedException("Portable ICO decoding currently supports PNG-backed icon images.");
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

        private static IconDirectoryEntry ReadDirectoryEntry(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[16];
            ReadExactly(stream, buffer);

            int width = buffer[0] == 0 ? 256 : buffer[0];
            int height = buffer[1] == 0 ? 256 : buffer[1];
            return new IconDirectoryEntry(
                width,
                height,
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2)),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12, 4)));
        }

        private static int CompareDirectoryEntries(IconDirectoryEntry left, IconDirectoryEntry right)
        {
            int areaComparison = (right.Width * right.Height).CompareTo(left.Width * left.Height);
            if (areaComparison != 0)
            {
                return areaComparison;
            }

            return right.BitCount.CompareTo(left.BitCount);
        }

        private static bool TrySeekToImage(Stream stream, long startPosition, IconDirectoryEntry entry, out long imagePosition)
        {
            imagePosition = checked(startPosition + entry.ImageOffset);
            long imageEnd = checked(imagePosition + entry.BytesInResource);
            if (imagePosition < startPosition || imageEnd < imagePosition)
            {
                return false;
            }

            try
            {
                if (imageEnd > stream.Length)
                {
                    return false;
                }
            }
            catch (NotSupportedException)
            {
            }

            stream.Position = imagePosition;
            return true;
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
            return signature.Length >= 8 &&
                signature[0] == 0x89 &&
                signature[1] == (byte)'P' &&
                signature[2] == (byte)'N' &&
                signature[3] == (byte)'G' &&
                signature[4] == 0x0D &&
                signature[5] == 0x0A &&
                signature[6] == 0x1A &&
                signature[7] == 0x0A;
        }

        private static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            if (!TryReadExactly(stream, buffer))
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }
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

        private readonly struct IconDirectoryEntry
        {
            public IconDirectoryEntry(int width, int height, ushort bitCount, uint bytesInResource, uint imageOffset)
            {
                Width = width;
                Height = height;
                BitCount = bitCount;
                BytesInResource = bytesInResource;
                ImageOffset = imageOffset;
            }

            public int Width { get; }

            public int Height { get; }

            public ushort BitCount { get; }

            public uint BytesInResource { get; }

            public uint ImageOffset { get; }
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
