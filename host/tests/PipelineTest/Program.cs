using System.Diagnostics;
using System.Text;
using HandHeld.Host.Core.Video;

Console.WriteLine("=== HandHeld pipeline test v2 ===");
using var capture = new DesktopCapture();
Console.WriteLine($"Capture: {capture.Width}x{capture.Height} initialized={capture.Initialized}");

using var encoder = new H264Encoder(1280, 720, 60, 8000);
Console.WriteLine($"Encoder: {encoder.Width}x{encoder.Height}");

var frame = new byte[capture.Width * capture.Height * 4];
var sw = Stopwatch.StartNew();
long frames = 0;
long totalEncodeMs = 0;
int totalUnits = 0;
bool gotSps = false;
bool gotIdr = false;
int nal5Count = 0;

while (sw.ElapsedMilliseconds < 5000)
{
    if (!capture.TryCopyFrame(frame))
    {
        Thread.Sleep(2);
        continue;
    }

    var e = Stopwatch.StartNew();
    var units = encoder.Encode(frame, capture.Width, capture.Height);
    e.Stop();
    totalEncodeMs += e.ElapsedMilliseconds;
    totalUnits += units.Count;
    foreach (var u in units)
    {
        if (u.Length < 5) continue;
        int nalType = u[4] & 0x1F;
        if (nalType == 7) gotSps = true;   // SPS
        if (nalType == 5) { gotIdr = true; nal5Count++; }  // IDR
    }
    frames++;
}

sw.Stop();
Console.WriteLine($"Frames: {frames}, Avg encode: {totalEncodeMs / (double)Math.Max(1, frames):F2} ms/frame");
Console.WriteLine($"AUs: {totalUnits} ({totalUnits / (double)Math.Max(1, frames):F1}/frame)");
Console.WriteLine($"SPS seen: {gotSps}, IDR seen: {gotIdr}, IDR count: {nal5Count}");
Console.WriteLine("=== done ===");
