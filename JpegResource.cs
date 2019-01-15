using System;
using System.Drawing.Imaging;
using System.Threading;
using TurboJpegWrapper;

namespace MotionJpegLatencyTest
{
    /// <summary>
    /// Wraps a single native resource
    /// </summary>
    public abstract class JpegResource : IDisposable
    {
        private IntPtr _handle;

        protected void SetResourceHandle(IntPtr handle)
        {
            var oldHandle = Interlocked.CompareExchange(ref _handle, handle, IntPtr.Zero);

            if (oldHandle != IntPtr.Zero)
            {
                throw new InvalidOperationException();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Cleanup(true);
        }

        private void Cleanup(bool isDisposing)
        {
            if (_handle == IntPtr.Zero)
                return;

            lock (this)
            {
                var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);

                if (handle != IntPtr.Zero)
                {
                    Free(_handle);
                }
            }
        }

        ~JpegResource()
        {
            Cleanup(false);
        }

        /// <summary>
        /// Get the handle, assuming that you locked this object, to prevent it from being disposed while you obtain the lock.
        /// </summary>
        public IntPtr ObtainHandleUnderLock()
        {
            if (!Monitor.IsEntered(this))
            {
                throw new InvalidOperationException($"{GetType().Name} must be locked before getting its handle");
            }

            return _handle;
        }

        protected abstract void Free(IntPtr handle);

        public static void GetErrorAndThrow()
        {
            throw new TJException(JpegLibrary.tjGetErrorStr());
        }

        public static TJPixelFormats ConvertPixelFormat(PixelFormat pixelFormat)
        {
            if (pixelFormat == PixelFormat.Format24bppRgb)
                return TJPixelFormats.TJPF_BGR;
            if (pixelFormat == PixelFormat.Format8bppIndexed)
                return TJPixelFormats.TJPF_GRAY;
            if (pixelFormat == PixelFormat.Format32bppArgb)
                return TJPixelFormats.TJPF_BGRA;
            throw new NotSupportedException($"Provided pixel format \"{(object)pixelFormat}\" is not supported");
        }
    }
}
