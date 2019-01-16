using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TurboJpegWrapper;

namespace MotionJpegLatencyTest
{
    public class FrameWorker : IDisposable
    {
        private readonly FrameSpec _spec;
        private readonly FrameStats _stats;
        private readonly WebSocket _webSocket;
        private readonly CancellationToken _cancellation;
        private readonly Image _background;
        private readonly JpegCompressor _jpegCompressor;
        private readonly Thread _thread;
        private readonly AutoResetEvent _queued = new AutoResetEvent(false);
        private readonly ConcurrentQueue<FrameRequest> _requests = new ConcurrentQueue<FrameRequest>();
        private byte[] _compressionBuffer;
        private byte[] _sendBuffer = new byte[4096];

        // This should always be zero when sending!
        private volatile int _concurrentSendCount;

        public FrameWorker(FrameSpec spec, string backgroundPath, JpegCompressor jpegCompressor, FrameStats stats, WebSocket webSocket, CancellationToken cancellation)
        {
            _spec = spec;
            _stats = stats;
            _background = backgroundPath == null ? null : Image.FromFile(backgroundPath);
            _jpegCompressor = jpegCompressor;
            _webSocket = webSocket;
            _cancellation = cancellation;
            _thread = new Thread(ThreadEntry) {Priority = ThreadPriority.AboveNormal};
            _thread.Start();
        }

        private async void ThreadEntry()
        {
            var sw = new Stopwatch();

            try
            {
                using (var canvas = new Bitmap(_spec.Width, _spec.Height, PixelFormat.Format24bppRgb))
                using (var jpegFrame = _jpegCompressor.CreateFrameBuffer(_spec.Width, _spec.Height, TJSubsamplingOptions.TJSAMP_420))
                using (var gfx = Graphics.FromImage(canvas))
                {
                    while (!_cancellation.IsCancellationRequested)
                    {
                        _queued.WaitOne();

                        while (_requests.TryDequeue(out var current))
                        {
                            sw.Restart();

                            var previous = current.PreviousFrameRequest ?? FrameRequest.Completed;

                            var frameTimeMs = current.FrameTime.TotalMilliseconds;

                            //previous.Rendered.WaitOne();

                            DebugWriteLine($"DRAW {frameTimeMs:00000.0}ms");

                            Render(gfx, current.FrameTime, current.CircleTime);

                            current.Rendered.Set();

                            //previous.Compressed.WaitOne();

                            var compressedSize = Compress(current, canvas, jpegFrame, ref _compressionBuffer);

                            DebugWriteLine($"JPEG {frameTimeMs:00000.0}ms");

                            current.Compressed.Set();

                            // Make sure to transmit frames in order.
                            previous.Transmitted.WaitOne();

                            DebugWriteLine($"SEND {frameTimeMs:00000.0}ms");

                            await Transmit(current.FrameId, current.FrameTime, _compressionBuffer, compressedSize);

                            current.Transmitted.Set();

                            _stats.AddFrameDuration(sw.Elapsed);

                            DebugWriteLine($"DONE {frameTimeMs:00000.0}ms");
                        }
                    }
                }

            }
            catch (ThreadInterruptedException)
            {
                DebugWriteLine("Worked interrupted");
            }

            catch (Exception ex)
            {
                DebugWriteLine($"Worked failed with {ex}");
            }
        }

        private void DebugWriteLine(string line)
        {
            // Console.WriteLine(line);
        }

        public void Dispose()
        {
            _thread.Interrupt();
            _queued.Set();
            _thread.Join();
        }

        private async Task Transmit(int frameId, Duration frameTime, byte[] compressionBuffer, int compressedSize)
        {
            if (Interlocked.Increment(ref _concurrentSendCount) != 1) 
                throw new InvalidOperationException("Multiple threads trying to send to the websocket at once!");

            try
            {
                var sw = new Stopwatch();
                sw.Start();

#if false
            var blockSize = _sendBuffer.Length;
            for (int offset = 0; offset < compressedSize; offset += blockSize)
            {
                int length = Math.Min(blockSize, compressedSize - offset);
                bool isLast = offset + blockSize >= compressedSize;

                await _webSocket.SendAsync(
                    new ArraySegment<byte>(compressionBuffer, offset, length),
                    WebSocketMessageType.Binary, isLast, _cancellation);
            }
#else
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(compressionBuffer, 0, compressedSize),
                    WebSocketMessageType.Binary, true, _cancellation);

#endif

                _stats.AddTransmitDuration(sw.Elapsed);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentSendCount);
            }
        }

        private int Compress(FrameRequest request, Bitmap canvas, JpegFrame frame, ref byte[] compressionBuffer)
        {
            var sw = new Stopwatch();
            sw.Start();

            var imageOffset = FrameHeader.MessageSize;

            int compressedSize = frame.Compress(ref compressionBuffer, imageOffset, canvas, 90);

            _stats.AddCompressedSize(compressedSize + imageOffset);
            _stats.AddCompressDuration(sw.Elapsed);

            var span = MemoryMarshal.Cast<byte, FrameHeader>
                (new Span<byte>(compressionBuffer, 0, imageOffset));

            span[0] = _stats.Update(request.FrameId, request.FrameTime);

            return compressedSize + imageOffset;
        }

        private void Render(Graphics gfx, Duration frameTime, Duration circleTime)
        {
            var frameTimeMs = frameTime.TotalMilliseconds;

            var sw = new Stopwatch();
            sw.Start();

            var width = _spec.Width;
            var height = _spec.Height;
            var radius = _spec.Radius;
            var center = _spec.Center;

            gfx.ResetTransform();

            gfx.Clear(Color.SkyBlue);

            gfx.TranslateTransform(width * 0.5f, height * 0.5f);

            var frameAngle = (circleTime.TotalSeconds * 360 / _spec.SpinDurationSec) % 360;
            gfx.RotateTransform((float)frameAngle);

            if (_background != null)
            {
                var state = gfx.Save();
                gfx.CompositingMode = CompositingMode.SourceCopy;
                gfx.CompositingQuality = CompositingQuality.HighSpeed;
                gfx.InterpolationMode = InterpolationMode.NearestNeighbor;
                gfx.SmoothingMode = SmoothingMode.None;
                gfx.DrawImage(_background, -width * 0.5f, -height * 0.5f);
                gfx.Restore(state);
            }

            var transform = gfx.Transform;

            gfx.Transform = transform;
            gfx.FillRectangle(Brushes.Black, center, -5, radius, 10);

            _stats.AddRenderDuration(sw.Elapsed);

            gfx.ResetTransform();

            // Draw a bouncing ball
            const float ballRadius = 20;
            gfx.FillEllipse(Brushes.Orange,
             width*0.5f - ballRadius, height - (height - 2*ballRadius) * (float)Math.Abs(Math.Sin((float)(frameTimeMs/1000f))),
             ballRadius*2, ballRadius*2);
        }

        public void PostRequest(FrameRequest request)
        {
            Debug.Assert(_requests.IsEmpty);
            _requests.Enqueue(request);
            _queued.Set();
        }

        public bool IsCompleted => _requests.IsEmpty;
    }
}
