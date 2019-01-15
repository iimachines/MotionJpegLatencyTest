using System;
using TurboJpegWrapper;

namespace MotionJpegLatencyTest
{
    public sealed class JpegCompressor : JpegResource
    {
        public JpegCompressor()
        {
            var handle = JpegLibrary.tjInitCompress();
            if (handle == IntPtr.Zero)
            {
                GetErrorAndThrow();
            }

            SetResourceHandle(handle);
        }

        public JpegFrame CreateFrameBuffer(int maxWidth, int maxHeight, TJSubsamplingOptions subSampling)
        {
            return new JpegFrame(this, maxWidth, maxHeight, subSampling);
        }

        protected override void Free(IntPtr handle)
        {
            JpegLibrary.tjDestroy(handle);
        }
    }
}
