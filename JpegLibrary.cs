using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TurboJpegWrapper;

namespace MotionJpegLatencyTest
{
    public static class JpegLibrary
    {
        public static readonly Dictionary<TJPixelFormats, int> PixelSizes = new Dictionary<TJPixelFormats, int>()
        {
            {
                TJPixelFormats.TJPF_RGB,
                3
            },
            {
                TJPixelFormats.TJPF_BGR,
                3
            },
            {
                TJPixelFormats.TJPF_RGBX,
                4
            },
            {
                TJPixelFormats.TJPF_BGRX,
                4
            },
            {
                TJPixelFormats.TJPF_XBGR,
                4
            },
            {
                TJPixelFormats.TJPF_XRGB,
                4
            },
            {
                TJPixelFormats.TJPF_GRAY,
                1
            },
            {
                TJPixelFormats.TJPF_RGBA,
                4
            },
            {
                TJPixelFormats.TJPF_BGRA,
                4
            },
            {
                TJPixelFormats.TJPF_ABGR,
                4
            },
            {
                TJPixelFormats.TJPF_ARGB,
                4
            },
            {
                TJPixelFormats.TJPF_CMYK,
                4
            }
        };
        public static readonly Dictionary<TJSubsamplingOptions, Size> MCUSizes = new Dictionary<TJSubsamplingOptions, Size>()
        {
            {
                TJSubsamplingOptions.TJSAMP_GRAY,
                new Size(8, 8)
            },
            {
                TJSubsamplingOptions.TJSAMP_444,
                new Size(8, 8)
            },
            {
                TJSubsamplingOptions.TJSAMP_422,
                new Size(16, 8)
            },
            {
                TJSubsamplingOptions.TJSAMP_420,
                new Size(16, 16)
            },
            {
                TJSubsamplingOptions.TJSAMP_440,
                new Size(8, 16)
            },
            {
                TJSubsamplingOptions.TJSAMP_411,
                new Size(32, 8)
            }
        };
        private const string UnmanagedLibrary = "turbojpeg";

        public static bool LibraryFound { get; private set; } = true;

        public static int TJPAD(int width)
        {
            return width + 3 & -4;
        }

        public static int TJSCALED(int dimension, (int num, int denom) scalingFactor)
        {
            return (dimension * scalingFactor.num + scalingFactor.denom - 1) / scalingFactor.denom;
        }

        [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr tjInitCompress();

        [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)]
        public static extern int tjCompress2(IntPtr handle, IntPtr srcBuf, int width, int pitch, int height, int pixelFormat, ref IntPtr jpegBuf, ref ulong jpegSize, int jpegSubsamp, int jpegQual, int flags);

        [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)]
        public static extern long tjBufSize(int width, int height, int jpegSubsamp);

        [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr tjInitDecompress();

        public static int tjDecompressHeader(IntPtr handle, IntPtr jpegBuf, ulong jpegSize, out int width, out int height, out int jpegSubsamp, out int jpegColorspace)
        {
            switch (IntPtr.Size)
            {
                case 4:
                    return tjDecompressHeader3_x86(handle, jpegBuf, (uint)jpegSize, out width, out height, out jpegSubsamp, out jpegColorspace);
                case 8:
                    return tjDecompressHeader3_x64(handle, jpegBuf, jpegSize, out width, out height, out jpegSubsamp, out jpegColorspace);
                default:
                    throw new InvalidOperationException("Invalid platform. Can not find proper function");
            }
        }

        [DllImport("turbojpeg", EntryPoint = "tjDecompressHeader3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int tjDecompressHeader3_x86(IntPtr handle, IntPtr jpegBuf, uint jpegSize, out int width, out int height, out int jpegSubsamp, out int jpegColorspace);

        [DllImport("turbojpeg", EntryPoint = "tjDecompressHeader3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int tjDecompressHeader3_x64(IntPtr handle, IntPtr jpegBuf, ulong jpegSize, out int width, out int height, out int jpegSubsamp, out int jpegColorspace);

        [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr tjGetScalingFactors(out int numscalingfactors);

        public static int tjDecompress(IntPtr handle, IntPtr jpegBuf, ulong jpegSize, IntPtr dstBuf, int width, int pitch, int height, int pixelFormat, int flags)
        {
            switch (IntPtr.Size)
            {
                case 4:
                    return tjDecompress2_x86(handle, jpegBuf, (uint)jpegSize, dstBuf, width, pitch, height, pixelFormat, flags);
                case 8:
                    return tjDecompress2_x64(handle, jpegBuf, jpegSize, dstBuf, width, pitch, height, pixelFormat, flags);
                default:
                    throw new InvalidOperationException("Invalid platform. Can not find proper function");
            }
        }

        [DllImport("turbojpeg", EntryPoint = "tjDecompress2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int tjDecompress2_x86(IntPtr handle, IntPtr jpegBuf, uint jpegSize, IntPtr dstBuf, int width, int pitch, int height, int pixelFormat, int flags);

        [DllImport("turbojpeg", EntryPoint = "tjDecompress2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int tjDecompress2_x64(IntPtr handle, IntPtr jpegBuf, ulong jpegSize, IntPtr dstBuf, int width, int pitch, int height, int pixelFormat, int flags);

        [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr tjAlloc(int bytes);

        [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)]
        public static extern void tjFree(IntPtr buffer);

        [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr tjInitTransform();

        public static int tjTransform(IntPtr handle, IntPtr jpegBuf, ulong jpegSize, int n, IntPtr[] dstBufs, ulong[] dstSizes, IntPtr transforms, int flags)
        {
            uint[] dstSizes1 = new uint[dstSizes.Length];
            for (int index = 0; index < dstSizes.Length; ++index)
                dstSizes1[index] = (uint)dstSizes[index];
            int num;
            switch (IntPtr.Size)
            {
                case 4:
                    num = tjTransform_x86(handle, jpegBuf, (uint)jpegSize, n, dstBufs, dstSizes1, transforms, flags);
                    break;
                case 8:
                    num = tjTransform_x64(handle, jpegBuf, jpegSize, n, dstBufs, dstSizes1, transforms, flags);
                    break;
                default:
                    throw new InvalidOperationException("Invalid platform. Can not find proper function");
            }
            for (int index = 0; index < dstSizes.Length; ++index)
                dstSizes[index] = (ulong)dstSizes1[index];
            return num;
        }

        [DllImport("turbojpeg", EntryPoint = "tjTransform", CallingConvention = CallingConvention.Cdecl)]
        private static extern int tjTransform_x86(IntPtr handle, IntPtr jpegBuf, uint jpegSize, int n, IntPtr[] dstBufs, uint[] dstSizes, IntPtr transforms, int flags);

        [DllImport("turbojpeg", EntryPoint = "tjTransform", CallingConvention = CallingConvention.Cdecl)]
        private static extern int tjTransform_x64(IntPtr handle, IntPtr jpegBuf, ulong jpegSize, int n, IntPtr[] dstBufs, uint[] dstSizes, IntPtr transforms, int flags);

        [DllImport("turbojpeg", CallingConvention = CallingConvention.Cdecl)]
        public static extern int tjDestroy(IntPtr handle);

        [DllImport("turbojpeg", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public static extern string tjGetErrorStr();

        public static void CheckOptionsCompatibilityAndThrow(TJSubsamplingOptions subSamp, TJPixelFormats srcFormat)
        {
            if (srcFormat == TJPixelFormats.TJPF_GRAY && subSamp != TJSubsamplingOptions.TJSAMP_GRAY)
                throw new NotSupportedException(
                    $"Subsampling differ from {(object)TJSubsamplingOptions.TJSAMP_GRAY} for pixel format {(object)TJPixelFormats.TJPF_GRAY} is not supported");
        }
    }
}
