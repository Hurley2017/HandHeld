using System.Net;
using System.Net.Sockets;
using System.Text;

// Send a burst of UDP to the phone's receiver port — if the app is bound and
// this arrives, the network path is fine and the problem is the host's sender.
var udp = new UdpClient();
var target = new IPEndPoint(IPAddress.Parse("192.168.0.161"), 45330);
byte[] payload = new byte[1200];
for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 251);
for (int i = 0; i < 100; i++)
{
    udp.Send(payload, payload.Length, target);
    Thread.Sleep(20);
}
Console.WriteLine("sent 100 UDP packets to 192.168.0.161:45330");
