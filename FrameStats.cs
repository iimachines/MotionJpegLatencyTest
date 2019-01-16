namespace MotionJpegLatencyTest
{
    public class FrameStats
    {
        private FrameHeader _header;

        private double _frameRateWindowTimeMs;
        private int _frameCount;
        private long _byteCount;

        private Duration _renderDurations;
        private Duration _compressDuration;
        private Duration _transmitDuration;
        private Duration _frameDuration;

        public FrameHeader Update(long frameId, Duration frameTime)
        {
            lock (this)
            {
                var frameTimeMs = frameTime.TotalMilliseconds;

                _header.FrameId = frameId;
                _header.FrameTime = frameTimeMs;

                if (++_frameCount == 30)
                {
                    var dt = (frameTimeMs - _frameRateWindowTimeMs) / 1000.0;
                    double fc = _frameCount;

                    _header.FrameRate = _frameCount / dt;
                    _header.BandWidth = (_byteCount / dt) * 10 / 1e6; // roughly estimate 10 bits per byte for transmission
                    _header.RenderDuration = _renderDurations.TotalMilliseconds / fc;
                    _header.CompressDuration = _compressDuration.TotalMilliseconds / fc;
                    _header.TransmitDuration = _transmitDuration.TotalMilliseconds / fc;
                    _header.FrameDuration = _frameDuration.TotalMilliseconds / fc;

                    _frameDuration = _renderDurations = _compressDuration = _transmitDuration = default;

                    _frameRateWindowTimeMs = frameTimeMs;
                    _frameCount = 0;
                    _byteCount = 0;
                }

                return _header;
            }
        }

        private void Add(ref Duration field, in Duration value)
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

        public void AddRenderDuration(in Duration ts) => Add(ref _renderDurations, ts);
        public void AddCompressDuration(in Duration ts) => Add(ref _compressDuration, ts);
        public void AddTransmitDuration(in Duration ts) => Add(ref _transmitDuration, ts);
        public void AddFrameDuration(in Duration ts) => Add(ref _frameDuration, ts);
    }
}
