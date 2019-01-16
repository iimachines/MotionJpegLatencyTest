using System.Runtime.InteropServices;

namespace MotionJpegLatencyTest
{
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct FrameHeader
    {
        public double FrameId;
        public double FrameTime;
        public double BandWidth;
        public double FrameRate;
        public double RenderDuration;
        public double TransmitDuration;
        public double CompressDuration;
        public double FrameDuration;
        public double SegmentX;
        public double SegmentY;

        public static readonly int MessageSize = Marshal.SizeOf<FrameHeader>();
    }
}