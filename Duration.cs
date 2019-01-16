using System;

namespace MotionJpegLatencyTest
{
    /// <summary>
    /// Duration rounds to milliseconds, this struct doesn't
    /// </summary>
    public struct Duration
    {
        private readonly double _ms;

        private Duration(double ms)
        {
            _ms = ms;
        }

        public double TotalMilliseconds => _ms;
        public double TotalSeconds => _ms / 1000;

        public static readonly Duration MinValue = new Duration(double.NegativeInfinity);

        public static Duration FromMilliseconds(double ms)
        {
            return new Duration(ms);
        }

        public static Duration operator +(Duration x, Duration y)
        {
            return new Duration(x._ms + y._ms);
        }

        public static implicit operator Duration(TimeSpan ts)
        {
            var div = ts.Ticks / TimeSpan.TicksPerMillisecond;
            double mod = ts.Ticks % TimeSpan.TicksPerMillisecond;
            var ms = div + (mod / TimeSpan.TicksPerMillisecond);
            return new Duration(ms);
        }

        public static implicit operator TimeSpan(Duration d)
        {
            var ticks = (long)(d._ms * TimeSpan.TicksPerMillisecond);
            return new TimeSpan(ticks);
        }

        public static Duration FromSeconds(double s) => FromMilliseconds(s * 1000);

        public override string ToString() => $"{_ms:00000.000}ms";
    }
}