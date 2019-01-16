using System;
using System.Collections.Generic;

namespace MotionJpegLatencyTest
{
    public sealed class DisposableList<T> : List<T>, IDisposable where T : IDisposable
    {
        public void Dispose()
        {
            var items = ToArray();

            Clear();

            foreach (var item in items)
            {
                item?.Dispose();
            }
        }
    }
}