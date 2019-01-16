using System;
using System.Drawing.Imaging;

namespace MotionJpegLatencyTest
{
    public static class PixelFormatExt
    {
        public static int GetBytesPerPixel(this PixelFormat pf)
        {
            switch (pf)
            {
                case PixelFormat.Alpha:
                case PixelFormat.PAlpha:
                    return 1;
                case PixelFormat.Format24bppRgb:
                    return 3;
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                case PixelFormat.Format32bppRgb:
                    return 4;
                default:
                    throw new NotSupportedException($"Pixel format ${pf} not supported");
            }
        }
    }
}