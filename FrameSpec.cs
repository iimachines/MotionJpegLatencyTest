namespace MotionJpegLatencyTest
{
    public class FrameSpec
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Center { get; set; }
        public int Radius { get; set; }

        // seconds per full circle revolution
        public int SpinDurationSec { get; set; }

        // we hope to get 60 frames per second, so subdiv each second into 60 ticks
        public int SpinSubdivisions { get; set; }

        public FrameSpec(int width, int height, int spinDurationSec, int spinSubdivisionsPerSec = 60)
        {
            Width = width;
            Height = height;
            Radius = (Height / 3) | 0;
            Center = (Radius / 2) | 0;
            SpinDurationSec = spinDurationSec;
            SpinSubdivisions = SpinDurationSec * spinSubdivisionsPerSec;
        }
    }
}
