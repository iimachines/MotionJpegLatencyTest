using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TurboJpegWrapper;

namespace MotionJpegLatencyTest
{
    public class FrameWorker : IDisposable
    {
        public class Compressor
        {
            private readonly JpegFrame _jpegFrame;
            private byte[] _compressionBuffer;

            public Compressor(JpegFrame jpegFrame)
            {
                _jpegFrame = jpegFrame;
            }

            public ArraySegment<byte> Compress(IntPtr scan0, int stride, int bufferOffset, PixelFormat pixelFormat, int quality)
            {
                var compressedSize = _jpegFrame.Compress(ref _compressionBuffer, bufferOffset
                    , scan0, stride, _jpegFrame.Width, _jpegFrame.Height, pixelFormat, quality);

                return new ArraySegment<byte>(_compressionBuffer, 0, compressedSize + bufferOffset);
            }
        }

        private readonly FrameSpec _spec;
        private readonly FrameStats _stats;
        private readonly WebSocket _webSocket;
        private readonly CancellationTokenSource _cancellationSource;
        private readonly CancellationToken _cancellationToken;
        private readonly Image _background;
        private readonly JpegCompressor _jpegCompressor;
        private readonly Thread _thread;
        private readonly AutoResetEvent _queued = new AutoResetEvent(false);
        private readonly ConcurrentQueue<FrameRequest> _requests = new ConcurrentQueue<FrameRequest>();
        private byte[] _sendBuffer = new byte[4096];

        public FrameWorker(FrameSpec spec, string backgroundPath, JpegCompressor jpegCompressor, FrameStats stats, WebSocket webSocket)
        {
            _spec = spec;
            _stats = stats;
            _background = backgroundPath == null ? null : Image.FromFile(backgroundPath);
            _jpegCompressor = jpegCompressor;
            _webSocket = webSocket;
            _cancellationSource = new CancellationTokenSource();
            _cancellationToken = _cancellationSource.Token;
            _thread = new Thread(ThreadEntry) { Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        private async void ThreadEntry()
        {
            var sw = new Stopwatch();

            try
            {
                // TODO: We assume at least a quad core processor.
                using (var canvas = new Bitmap(_spec.Width, _spec.Height, PixelFormat.Format24bppRgb))
                using (var jpegFrame1 = _jpegCompressor.CreateFrameBuffer(_spec.Width / 2, _spec.Height / 2, TJSubsamplingOptions.TJSAMP_420))
                using (var jpegFrame2 = _jpegCompressor.CreateFrameBuffer(_spec.Width / 2, _spec.Height / 2, TJSubsamplingOptions.TJSAMP_420))
                using (var jpegFrame3 = _jpegCompressor.CreateFrameBuffer(_spec.Width / 2, _spec.Height / 2, TJSubsamplingOptions.TJSAMP_420))
                using (var jpegFrame4 = _jpegCompressor.CreateFrameBuffer(_spec.Width / 2, _spec.Height / 2, TJSubsamplingOptions.TJSAMP_420))
                using (var gfx = Graphics.FromImage(canvas))
                {
                    var compressors = new[]
                    {
                        new Compressor(jpegFrame1),
                        new Compressor(jpegFrame2),
                        new Compressor(jpegFrame3),
                        new Compressor(jpegFrame4),
                    };

                    while (!_cancellationToken.IsCancellationRequested)
                    {
                        _queued.WaitOne();

                        while (_requests.TryDequeue(out var current))
                        {
                            sw.Restart();

                            var previous = current.PreviousFrameRequest ?? FrameRequest.Completed;

                            var frameId = current.FrameId;

                            //previous.Rendered.WaitOne();

                            DebugWriteLine($"DRAW {frameId:0000}");

                            Render(current, gfx);

                            // Wait before compressing a new picture.
                            // Otherwise we'll use more compression threads than cores.
                            previous.Compressed.WaitOne();

                            DebugWriteLine($"JPEG {frameId:0000}");
                            await Compress(current, canvas, compressors);

                            _stats.AddFrameDuration(sw.Elapsed);

                            DebugWriteLine($"DONE {frameId:0000}");
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
            _cancellationSource.Cancel();
            _thread.Interrupt();
            _queued.Set();
            _thread.Join();
        }

        private Task Transmit(FrameRequest request, ArraySegment<byte> segment)
        {
            var sw = new Stopwatch();
            sw.Start();

            var task = _webSocket.SendDataAsync(segment, _cancellationToken);
            return task.ContinueWith(t => _stats.AddTransmitDuration(sw.Elapsed), _cancellationToken);
        }

        private async Task Compress(FrameRequest request, Bitmap srcImage, Compressor[] compressors, int quality = 90)
        {
            var imageOffset = FrameHeader.MessageSize;

            var header = _stats.Update(request.FrameId, request.FrameTime);

            PixelFormat pixelFormat = srcImage.PixelFormat;
            int width = srcImage.Width;
            int height = srcImage.Height;
            BitmapData bitmapData = srcImage.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, pixelFormat);
            int stride = bitmapData.Stride;
            int bytesPerPixel = pixelFormat.GetBytesPerPixel();
            IntPtr scan0 = bitmapData.Scan0;
            try
            {
                int remainingCompressors = compressors.Length;

                var tasks = compressors
                    .Select((compressor, index) => Task.Run(
                        delegate
                        {
                            var sw = new Stopwatch();
                            sw.Start();

                            var segmentX = (index >> 1) * (_spec.Width / 2);
                            var segmentY = (index & 1) * (_spec.Height / 2);

                            var ptr = scan0.ToInt64() + segmentY * stride + segmentX * bytesPerPixel;

                            var data = compressor.Compress(new IntPtr(ptr), stride, imageOffset, pixelFormat, quality);

                            if (Interlocked.Decrement(ref remainingCompressors) == 0)
                            {
                                // All compression threads are done.
                                request.Compressed.Set();
                            }

                            var span = MemoryMarshal.Cast<byte, FrameHeader>(
                                new Span<byte>(data.Array, data.Offset, data.Count));

                            span[0] = header;
                            span[0].SegmentX = segmentX;
                            span[0].SegmentY = segmentY;

                            _stats.AddCompressedSize(data.Count);
                            _stats.AddCompressDuration(sw.Elapsed);

                            // Make sure the previous frame is transmitted first
                            request.PreviousFrameRequest.Transmitted.WaitOne();

                            // Send this segment
                            return Transmit(request, data);
                        }, _cancellationToken))
                    .ToArray();

                Stopwatch tw = new Stopwatch();
                tw.Start();
                await Task.WhenAll(tasks);
                _stats.AddTransmitDuration(tw.Elapsed);

                request.Transmitted.Set();
            }
            finally
            {
                srcImage.UnlockBits(bitmapData);
            }
        }

        private void Render(FrameRequest request, Graphics gfx)
        {
            Duration frameTime = request.FrameTime;
            Duration circleTime = request.CircleTime;

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
             width * 0.5f - ballRadius, height - (height - 2 * ballRadius) * (float)Math.Abs(Math.Sin((float)(frameTimeMs / 1000f))),
             ballRadius * 2, ballRadius * 2);

            request.Rendered.Set();
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
