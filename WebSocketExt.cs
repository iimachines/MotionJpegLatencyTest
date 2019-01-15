using System;
using System.Diagnostics;
using System.Dynamic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace MotionJpegLatencyTest
{
    public static class WebSocketExt
    {
        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        public static Task SendTextAsync(this WebSocket socket, string message, CancellationToken cancellation)
        {
            var data = Encoding.UTF8.GetBytes(message);
            return socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, cancellation);
        }

        public static Task SendJsonAsync(this WebSocket socket, string action, object payload, CancellationToken cancellation)
        {
            var message = new { action, payload };
            var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, serializerSettings));
            return socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, cancellation);
        }

        public static async Task<JObject> ReceiveJsonAsync(this WebSocket webSocket, CancellationToken cancellation, byte[] receiveBuffer = null)
        {
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(receiveBuffer), cancellation);

            Debug.Assert(result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            Debug.Assert(result.MessageType == WebSocketMessageType.Text);

            var message = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);

            return JObject.Parse(message);
        }
    }
}
