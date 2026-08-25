using System.Net;
using System.Net.Sockets;
using System.Text;

// Raw WS client: launch desktop, then capture the UDP video stream and
// analyze every NAL type that arrives. This is the DEFINITIVE wire test.
var tcp = new TcpClient("127.0.0.1", 45320);
var stream = tcp.GetStream();
var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
var req = $"GET / HTTP/1.1\r\nHost: 127.0.0.1:45320\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n";
await stream.WriteAsync(Encoding.ASCII.GetBytes(req));
await stream.FlushAsync();
var reader = new StreamReader(stream, Encoding.ASCII);
while (true) { var l = await reader.ReadLineAsync(); if (l == null || l.Length == 0) break; }

await SendText(stream, "{\"type\":\"hello\",\"name\":\"wire-test\"}");
var helloJson = await ReadText(stream);
File.AppendAllText("test.log", "hello: " + helloJson + "\n");

var udp = new UdpClient(45330);
var remoteEp = new IPEndPoint(IPAddress.Any, 0);
udp.Client.ReceiveTimeout = 1000;

await SendText(stream, "{\"type\":\"launch_desktop\",\"width\":1920,\"height\":1080,\"fps\":60,\"bitrate\":20000}");
var launchJson = await ReadText(stream);
File.AppendAllText("test.log", "launch: " + launchJson + "\n");

// Analyze NAL types for 6 seconds.
int[] nalTypes = new int[32];
int total = 0;
int totalPkts = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();
while (sw.ElapsedMilliseconds < 6000)
{
    try
    {
        var p = udp.Receive(ref remoteEp);
        totalPkts++;
        if (p.Length >= 14)
        {
            int nalType = p[8] & 0x1F;
            int fragId = (p[9] << 8) | p[10];
            int fragCount = (p[11] << 8) | p[12];
            if (fragId == 0 && nalType < 32) {
                nalTypes[nalType]++;
                total++;
                File.AppendAllText("test.log", $"NAL: type={nalType} fragCount={fragCount} len={p.Length}\n");
            }
        }
    }
    catch (Exception ex)
    {
        File.AppendAllText("test.log", "ex: " + ex.Message + "\n");
    }
}
string summary = $"totalPkts={totalPkts} total NALs (frag0): {total}\nSPS(7)={nalTypes[7]} PPS(8)={nalTypes[8]} IDR(5)={nalTypes[5]} SEI(6)={nalTypes[6]} slice(1)={nalTypes[1]}\n";
File.AppendAllText("test.log", summary);
Console.WriteLine(summary);
tcp.Close();

async Task SendText(NetworkStream s, string text)
{
    var data = Encoding.UTF8.GetBytes(text);
    var mask = new byte[] { 0x11, 0x22, 0x33, 0x44 };
    var frame = new byte[2 + 4 + data.Length];
    frame[0] = 0x81; frame[1] = (byte)(0x80 | data.Length);
    Array.Copy(mask, 0, frame, 2, 4);
    for (int i = 0; i < data.Length; i++) frame[6 + i] = (byte)(data[i] ^ mask[i % 4]);
    await s.WriteAsync(frame); await s.FlushAsync();
}

async Task<string> ReadText(NetworkStream s)
{
    var head = new byte[2];
    int got = 0;
    while (got < 2) { int n = await s.ReadAsync(head, got, 2 - got); if (n == 0) break; got += n; }
    int len = head[1] & 0x7F;
    if (len == 126) { var ext = new byte[2]; await s.ReadAsync(ext); len = (ext[0] << 8) | ext[1]; }
    var payload = new byte[len];
    got = 0;
    while (got < len) { int n = await s.ReadAsync(payload, got, len - got); if (n == 0) break; got += n; }
    return Encoding.UTF8.GetString(payload, 0, got);
}
