// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using MS.Internal;
using Microsoft.Win32.SafeHandles;
using StbImageSharp;

namespace System.Windows.Media.Imaging
{
    #region GifBitmapDecoder

    /// <summary>
    /// The built-in Microsoft Gif (Bitmap) Decoder.
    /// </summary>
    public sealed class GifBitmapDecoder : BitmapDecoder
    {
        /// <summary>
        /// Don't allow construction of a decoder with no params
        /// </summary>
        private GifBitmapDecoder()
        {
        }

        /// <summary>
        /// Create a GifBitmapDecoder given the Uri
        /// </summary>
        /// <param name="bitmapUri">Uri to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public GifBitmapDecoder(
            Uri bitmapUri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapUri, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatGif)
        {
        }

        /// <summary>
        /// If this decoder cannot handle the bitmap stream, it will throw an exception.
        /// </summary>
        /// <param name="bitmapStream">Stream to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public GifBitmapDecoder(
            Stream bitmapStream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapStream, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatGif)
        {
        }

        internal GifBitmapDecoder(
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
        internal GifBitmapDecoder(
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
            UnmanagedMemoryStream unmanagedMemoryStream ,
            SafeFileHandle safeFilehandle
            ) : base(decoderHandle, decoder, baseUri, uri, stream, createOptions, cacheOption, insertInDecoderCache, originalWritable, uriStream, unmanagedMemoryStream, safeFilehandle)
        {
        }

        public override BitmapPalette Palette
        {
            get
            {
                return InternalDecoder == null ? null : base.Palette;
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
                Span<byte> signature = stackalloc byte[6];
                if (!TryReadExactly(stream, signature))
                {
                    stream.Position = startPosition;
                    return false;
                }

                if (!IsGifSignature(signature))
                {
                    stream.Position = startPosition;
                    return false;
                }

                stream.Position = startPosition;
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                int stride = checked(image.Width * 4);
                byte[] pixels = new byte[checked(stride * image.Height)];
                ConvertRgbaToBgra(image.Data, pixels);

                BitmapSource source = BitmapSource.Create(
                    image.Width,
                    image.Height,
                    96.0,
                    96.0,
                    PixelFormats.Bgra32,
                    null,
                    pixels,
                    stride);

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

        private static void ConvertRgbaToBgra(byte[] source, byte[] target)
        {
            for (int sourceOffset = 0; sourceOffset < source.Length; sourceOffset += 4)
            {
                target[sourceOffset + 0] = source[sourceOffset + 2];
                target[sourceOffset + 1] = source[sourceOffset + 1];
                target[sourceOffset + 2] = source[sourceOffset + 0];
                target[sourceOffset + 3] = source[sourceOffset + 3];
            }
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

        private static bool IsGifSignature(ReadOnlySpan<byte> signature)
        {
            return signature.Length >= 6 &&
                signature[0] == (byte)'G' &&
                signature[1] == (byte)'I' &&
                signature[2] == (byte)'F' &&
                signature[3] == (byte)'8' &&
                (signature[4] == (byte)'7' || signature[4] == (byte)'9') &&
                signature[5] == (byte)'a';
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
