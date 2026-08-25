using System.Diagnostics;
using System.Net;

namespace HandHeld.Host.Core.Video;

/// <summary>Runs the capture → encode → send loop for one client session.</summary>
public sealed class StreamSession : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly int _bitrateKbps;
    private readonly IPEndPoint _videoRemote;
    private readonly Action<StreamStats> _onStats;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;

    public StreamSession(
        int width, int height, int fps, int bitrateKbps,
        IPEndPoint videoRemote, Action<StreamStats> onStats)
    {
        _width = width;
        _height = height;
        _fps = fps;
        _bitrateKbps = bitrateKbps;
        _videoRemote = videoRemote;
        _onStats = onStats;
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "StreamSession" };
        _thread.Start();
    }

    private static readonly object LogLock = new();
    private static void Log(string msg)
    {
        try
        {
            lock (LogLock)
            {
                File.AppendAllText(@"D:\Projects\HandHeld\artifacts\host-stream.log",
                    $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {msg}\n");
            }
        }
        catch { }
    }

    private void Loop()
    {
        Log("session loop start");
        try
        {
            LoopBody();
        }
        catch (Exception ex)
        {
            // The stream thread must never take down the host.
            Log($"session ended: {ex}");
            Debug.WriteLine($"[stream] session ended: {ex}");
        }
        Log("session loop end");
    }

    private void LoopBody()
    {
        // Capture the virtual display by device name (GDI works on IddCx
        // outputs where DXGI Duplication fails); fall back to DXGI primary.
        string? virtualDevice = DisplayManager.FindVirtualDisplayDeviceName();
        Log($"virtual display: {(virtualDevice ?? "none")}");
        ICaptureSource? capture = null;
        if (virtualDevice != null)
        {
            try
            {
                capture = new GdiDisplayCapture(virtualDevice);
                Log($"gdi capture ok: {capture.Width}x{capture.Height}");
            }
            catch (Exception ex)
            {
                Log($"virtual gdi capture failed ({ex.Message}); using primary");
                capture = null;
            }
        }
        if (capture == null)
        {
            try
            {
                capture = new DesktopCapture(); // DXGI primary
                Log($"primary capture ok: {capture.Width}x{capture.Height}");
            }
            catch (Exception ex)
            {
                Log($"primary capture failed: {ex.Message}");
                return; // no capture source — nothing to stream
            }
        }

        using (capture)
        // Use the capture's ACTUAL size for the encoder — the virtual display
        // may be a different resolution than the client requested.
        using (var encoder = new H264Encoder(capture.Width, capture.Height, _fps, _bitrateKbps))
        using (var sender = new VideoSender(_videoRemote))
        {
            Log($"encoder+ sender ready, streaming to {_videoRemote}");
            // Always start with a keyframe so a freshly-connecting client can
            // decode immediately (no waiting for the periodic GOP boundary).
            _forceKeyframe = true;
            LoopInner(capture, encoder, sender);
        }
    }

    private void LoopInner(ICaptureSource capture, H264Encoder encoder, VideoSender sender)
    {
        var frame = new byte[capture.Width * capture.Height * 4];
        var stopwatch = Stopwatch.StartNew();
        long frameCount = 0;
        long lastStat = 0;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (!capture.TryCopyFrame(frame))
                {
                    Thread.Sleep(1);
                    continue;
                }

                var sw = Stopwatch.StartNew();
                var accessUnits = encoder.Encode(frame, capture.Width, capture.Height);
                var encodeMs = sw.Elapsed.TotalMilliseconds;

                bool keyframe = _forceKeyframe || (frameCount % (_fps * 2)) == 0;
                _forceKeyframe = false;

                foreach (var au in accessUnits)
                {
                    sender.SendAccessUnit(au, stopwatch.ElapsedMilliseconds, keyframe);
                }

                frameCount++;
                long now = stopwatch.ElapsedMilliseconds;
                if (now - lastStat >= 1000)
                {
                    var stats = new StreamStats
                    {
                        Fps = (double)frameCount * 1000 / now,
                        BitrateKbps = _bitrateKbps,
                        EncodeMs = encodeMs,
                    };
                    _onStats(stats);
                    lastStat = now;
                }

                // Frame pacing: sleep the remainder of the frame slot.
                int frameMs = 1000 / _fps;
                long elapsed = sw.ElapsedMilliseconds;
                if (elapsed < frameMs) Thread.Sleep((int)(frameMs - elapsed));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[stream] {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    public void Stop() => _cts.Cancel();

    private volatile bool _forceKeyframe;
    public void ForceKeyframe() => _forceKeyframe = true;

    public void Dispose()
    {
        Log("session DISPOSE called");
        Stop();
        _thread?.Join(2000);
        _cts.Dispose();
    }
}

public sealed record StreamStats
{
    public double Fps { get; init; }
    public int BitrateKbps { get; init; }
    public double EncodeMs { get; init; }
}
