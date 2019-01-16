using System;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MotionJpegLatencyTest
{
    public class RenderLoop
    {
        public static async Task Run(WebSocket webSocket, CancellationToken cancellation)
        {
            var scaleNom = 2;
            var scaleDen = 3;
            var width = (1920 * scaleNom / scaleDen) | 0;
            var height = (1080 * scaleNom / scaleDen) | 0;
            var frameSpec = new FrameSpec(width, height, 1);

            using (var jpegCompressor = new JpegCompressor())
            {
                // Render, compress, transmit
                const int workerCount = 3;

                var receiveBuffer = new byte[1024 * 4];

                var stats = new FrameStats();

                var workers = Enumerable.Range(0, workerCount)
                    .Select(index => new FrameWorker(frameSpec, "background-small.jpg", jpegCompressor, stats, webSocket, cancellation))
                    .ToArray();

                var requests = Enumerable.Repeat(FrameRequest.Completed, workerCount)
                    .ToArray();

                int workerIndex = 0;

                // Ready to start
                await webSocket.SendJsonAsync("READY", frameSpec, cancellation);

                double lastFrameTimeMS = -1;
                int lastFrameId = -1;

                // Start render loop
                while (!cancellation.IsCancellationRequested && !webSocket.CloseStatus.HasValue) // && lastFrameTimeMS < 250)
                {
                    var message = await webSocket.ReceiveJsonAsync(cancellation, receiveBuffer);
                    if (message == null)
                        break;

                    switch (message["action"].Value<string>())
                    {
                        case "MOUSE":
                            {
                                var k = message.SelectToken("payload.kind").Value<int>();
                                var x = message.SelectToken("payload.posX").Value<double>();
                                var y = message.SelectToken("payload.posY").Value<double>();
                                Console.WriteLine($"Mouse {k} {x} {y}");
                                break;
                            }
                        case "TICK":
                            {
                                var frameId = message.SelectToken("payload.frameId").Value<int>();
                                var frameTimeMS = message.SelectToken("payload.frameTime").Value<double>();
                                var circleTimeMs = message.SelectToken("payload.circleTime").Value<double>();

                                if (frameTimeMS == lastFrameTimeMS)
                                {
                                    Console.WriteLine($"Error: received same incoming frame twice, {frameTimeMS:0000.00}ms#{frameId} after {lastFrameTimeMS:0000.00}ms#{lastFrameId}");
                                }
                                else if (frameTimeMS < lastFrameTimeMS)
                                {
                                    Console.WriteLine($"Error: received on older incoming frame, {frameTimeMS:0000.00}ms#{frameId} after {lastFrameTimeMS:0000.00}ms#{lastFrameId}");
                                }
                                else
                                {
                                    lastFrameTimeMS = frameTimeMS;
                                    lastFrameId = frameId;

                                    var worker = workers[workerIndex];

                                    if (worker.IsCompleted)
                                    {
                                        var request = new FrameRequest(
                                            frameId,
                                            TimeSpan.FromMilliseconds(frameTimeMS),
                                            TimeSpan.FromMilliseconds(circleTimeMs),
                                            requests[(workerCount + workerIndex - 1) % workerCount]);

                                        worker.PostRequest(request);

                                        requests[workerIndex] = request;

                                        workerIndex = (workerIndex + 1) % workerCount;
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Skipping frame {frameTimeMS:0000.0}!");
                                    }

                                }

                                break;
                            }
                    }
                }

                foreach (var worker in workers)
                {
                    worker.Dispose();
                }

                Console.WriteLine("Exiting RenderLoop");
            }
        }

    }
}
