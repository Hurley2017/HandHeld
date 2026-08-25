using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

// Verify wire format: launch_desktop, capture video UDP, print header bytes.
using var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri("ws://127.0.0.1:45320/"), CancellationToken.None);
var launch = Encoding.UTF8.GetBytes("{\"type\":\"launch_desktop\",\"width\":1280,\"height\":720,\"fps\":60,\"bitrate\":8000}");
await ws.SendAsync(launch, WebSocketMessageType.Text, true, CancellationToken.None);
var buffer = new byte[4096];
await ws.ReceiveAsync(buffer, CancellationToken.None);

var udp = new UdpClient(45330);
udp.Client.ReceiveTimeout = 3000;
var sw = System.Diagnostics.Stopwatch.StartNew();
int packets = 0;
while (sw.ElapsedMilliseconds < 5000)
{
    try
    {
        var remote = new IPEndPoint(IPAddress.Any, 0);
        var data = udp.Receive(ref remote);
        packets++;
        if (packets <= 3)
        {
            var headerHex = string.Join(" ", data.Take(14).Select(b => b.ToString("X2")));
            var payloadHex = string.Join(" ", data.Skip(14).Take(16).Select(b => b.ToString("X2")));
            Console.WriteLine($"pkt {packets} len={data.Length}");
            Console.WriteLine($"  header: {headerHex}");
            Console.WriteLine($"  payload: {payloadHex}");
        }
    }
    catch (SocketException) { break; }
}
Console.WriteLine($"total packets in 5s: {packets}");
await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
