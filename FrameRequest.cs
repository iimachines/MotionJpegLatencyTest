using System;
using System.Threading;

namespace MotionJpegLatencyTest
{
    public sealed class FrameRequest
    {
        public readonly int FrameId;
        public readonly TimeSpan FrameTime;
        public readonly TimeSpan CircleTime;
        public readonly FrameRequest PreviousFrameRequest;

        public readonly EventWaitHandle Rendered;
        public readonly EventWaitHandle Compressed;
        public readonly EventWaitHandle Transmitted;

        public FrameRequest(int frameId, TimeSpan frameTime, TimeSpan circleTime, FrameRequest previousFrameRequest)
        {
            FrameId = frameId;
            FrameTime = frameTime;
            CircleTime = circleTime;
            PreviousFrameRequest = previousFrameRequest;
            Rendered = new AutoResetEvent(false);
            Compressed = new AutoResetEvent(false);
            Transmitted = new AutoResetEvent(false);
        }

        public void SetAll()
        {
            Rendered.Set();
            Compressed.Set();
            Transmitted.Set();
        }

        private FrameRequest()
        {
            FrameId = -1;
            FrameTime = TimeSpan.MinValue;
            CircleTime = TimeSpan.MinValue;
            Rendered = new ManualResetEvent(true);
            Compressed = new ManualResetEvent(true);
            Transmitted = new ManualResetEvent(true);
        }

        public static readonly FrameRequest Completed = new FrameRequest();
    }
}
