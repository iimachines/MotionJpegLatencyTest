/*
var nextFrameTime = Duration.FromSeconds(sendingFrameIndex * 1.0 / framesPerSecond);

if (timelineStopwatch.Elapsed >= nextFrameTime)
{
Console.WriteLine($"Skipping frame {sendingFrameIndex}!");
sendingFrameIndex = (int)Math.Ceiling(timelineStopwatch.Elapsed.TotalSeconds * framesPerSecond);
}
else
{
int spinWaitCycles = 1;

while (timelineStopwatch.Elapsed < nextFrameTime)
{
    Thread.SpinWait(spinWaitCycles);
    spinWaitCycles = Math.Max(1024, spinWaitCycles * 2);
}
}*/

/*
var buffer = new byte[1024 * 4];
var cancel = new CancellationTokenSource();

do
{
    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

    switch (result.MessageType)
    {
        case WebSocketMessageType.Text:
            switch (Encoding.UTF8.GetString(buffer, 0, result.Count))
            {
                case "START":
                    break;
            }
            break;

        case WebSocketMessageType.Close:
            break;
    }

} while (!cancel.IsCancellationRequested);

    await webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);

    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
}
await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
*/
