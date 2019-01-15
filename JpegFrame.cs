using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TurboJpegWrapper;

namespace MotionJpegLatencyTest
{
    public sealed class JpegFrame : JpegResource
    {
        private JpegCompressor _compressor;

        public readonly int MaxWidth;
        public readonly int MaxHeight;
        public readonly int MaxBufferSize;

        public readonly TJSubsamplingOptions SubSampling;

        public JpegFrame(JpegCompressor compressor, int maxWidth, int maxHeight, TJSubsamplingOptions subSampling)
        {
            _compressor = compressor;

            checked
            {
                MaxWidth = maxWidth;
                MaxHeight = maxHeight;
                SubSampling = subSampling;
                MaxBufferSize = (int)JpegLibrary.tjBufSize(maxWidth, maxHeight, (int)subSampling);
            }

            var bufferHandle = JpegLibrary.tjAlloc(MaxBufferSize);

            if (bufferHandle == IntPtr.Zero)
            {
                throw new OutOfMemoryException(
                    $"Failed to allocate TurboJPEG buffer of size {maxWidth}x{maxHeight}, subSampling {subSampling}");
            }

            SetResourceHandle(bufferHandle);
        }

        /// <summary>
        /// Compresses the bitmap to an output buffer, returning the length if the compressed data
        /// </summary>
        /// <remarks>
        /// The buffer is re-allocated if too small, and can be larger than the compressed data!
        /// </remarks>
        public int Compress(ref byte[] buffer, int bufferOffset, Bitmap srcImage, int quality)
        {
            PixelFormat pixelFormat = srcImage.PixelFormat;
            int width = srcImage.Width;
            int height = srcImage.Height;
            BitmapData bitmapData = srcImage.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, pixelFormat);
            int stride = bitmapData.Stride;
            IntPtr scan0 = bitmapData.Scan0;
            try
            {
                return Compress(ref buffer, bufferOffset, scan0, stride, width, height, pixelFormat, quality);
            }
            finally
            {
                srcImage.UnlockBits(bitmapData);
            }
        }

        /// <summary>
        /// Compresses the bitmap to an output buffer, returning the length if the compressed data
        /// </summary>
        /// <remarks>
        /// The buffer is re-allocated if too small, and can be larger than the compressed data!
        /// </remarks>
        public int Compress(
            ref byte[] buffer,
            int bufferOffset,
            IntPtr srcPtr,
            int stride,
            int width,
            int height,
            PixelFormat pixelFormat,
            int quality)
        {
            lock (this)
            {
                var bufferHandle = ObtainHandleUnderLock();

                TJPixelFormats srcFormat = ConvertPixelFormat(pixelFormat);
                JpegLibrary.CheckOptionsCompatibilityAndThrow(SubSampling, srcFormat);
                ulong jpegSize = (ulong)MaxBufferSize;

                lock (_compressor)
                {
                    var compressorHandle = _compressor.ObtainHandleUnderLock();

                    if (JpegLibrary.tjCompress2(compressorHandle, srcPtr,
                            width, stride, height, (int)srcFormat, ref bufferHandle, ref jpegSize,
                            (int)SubSampling, quality, (int)(TJFlags.NOREALLOC | TJFlags.FASTDCT)) == -1)
                    {
                        GetErrorAndThrow();
                    }

                    int length = (int)jpegSize;

                    if (buffer == null || buffer.Length < (bufferOffset + length))
                    {
                        Array.Resize(ref buffer, bufferOffset + MaxBufferSize);
                    }

                    Marshal.Copy(bufferHandle, buffer, bufferOffset, length);

                    return length;
                }
            }
        }

        protected override void Free(IntPtr handle)
        {
            _compressor = null;
            JpegLibrary.tjFree(handle);
        }
    }
}
