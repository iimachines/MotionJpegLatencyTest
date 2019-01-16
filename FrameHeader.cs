using System.Runtime.InteropServices;

namespace MotionJpegLatencyTest
{
    [StructLayout(LayoutKind.Explicit)]
    public struct FrameHeader
    {
        [FieldOffset(0 * 8)]
        public double FrameId;

        [FieldOffset(1 * 8)]
        public double FrameTime;

        [FieldOffset(2 * 8)]
        public double BandWidth;

        [FieldOffset(3 * 8)]
        public double FrameRate;

        [FieldOffset(4 * 8)]
        public double RenderDuration;

        [FieldOffset(5 * 8)]
        public double TransmitDuration;

        [FieldOffset(6 * 8)]
        public double CompressDuration;

        [FieldOffset(7 * 8)]
        public double FrameDuration;

        public const int MessageSize = 8 * 8;
    }
}