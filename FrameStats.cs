using System;

namespace MotionJpegLatencyTest
{
    public class FrameStats
    {
        private double _frameRateWindowTimeMs;
        private int _frameCount;
        private long _byteCount;

        private TimeSpan _renderDurations;
        private TimeSpan _compressDuration;
        private TimeSpan _transmitDuration;
        private TimeSpan _frameDuration;

        public void Update(double frameTimeMs)
        {
            lock (this)
            {
                if (++_frameCount == 30)
                {
                    var dt = (frameTimeMs - _frameRateWindowTimeMs) / 1000.0;
                    FrameRate = _frameCount / dt;
                    BandWidth = (_byteCount / dt) * 10 / 1e6; // roughly estimate 10 bits per byte for transmission

                    double fc = _frameCount;

                    RenderDuration = _renderDurations.TotalMilliseconds / fc;
                    CompressDuration = _compressDuration.TotalMilliseconds / fc;
                    TransmitDuration = _transmitDuration.TotalMilliseconds / fc;
                    FrameDuration = _frameDuration.TotalMilliseconds / fc;

                    _frameDuration = _renderDurations = _compressDuration = _transmitDuration = default;

                    _frameRateWindowTimeMs = frameTimeMs;
                    _frameCount = 0;
                    _byteCount = 0;
                }

            }
        }

        public double FrameRate { get; private set; }

        public double BandWidth { get; private set; }

        public double RenderDuration { get; private set; }
        public double CompressDuration { get; private set; }
        public double TransmitDuration { get; private set; }
        public double FrameDuration { get; private set; }

        private void Add(ref TimeSpan field, in TimeSpan value)
        {
            lock (this)
            {
                field += value;
            }
        }

        public void AddCompressedSize(long compressedSize)
        {
            lock (this)
            {
                _byteCount += compressedSize;
            }
        }

        public void AddRenderDuration(in TimeSpan ts) => Add(ref _renderDurations, ts);
        public void AddCompressDuration (in TimeSpan ts) => Add(ref _compressDuration, ts);
        public void AddTransmitDuration(in TimeSpan ts) => Add(ref _transmitDuration, ts);
        public void AddFrameDuration(in TimeSpan ts) => Add(ref _frameDuration, ts);
    }
}
