using System;
using System.Threading;

namespace MotionJpegLatencyTest
{
    public sealed class FrameRequest
    {
        public readonly int FrameId;
        public readonly Duration FrameTime;
        public readonly Duration CircleTime;
        public readonly FrameRequest PreviousFrameRequest;

        public readonly EventWaitHandle Rendered;
        public readonly EventWaitHandle Compressed;
        public readonly EventWaitHandle Transmitted;

        public FrameRequest(int frameId, Duration frameTime, Duration circleTime, FrameRequest previousFrameRequest)
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
            FrameTime = Duration.MinValue;
            CircleTime = Duration.MinValue;
            Rendered = new ManualResetEvent(true);
            Compressed = new ManualResetEvent(true);
            Transmitted = new ManualResetEvent(true);
        }

        public static readonly FrameRequest Completed = new FrameRequest();
    }
}
