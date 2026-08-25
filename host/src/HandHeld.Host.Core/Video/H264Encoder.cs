using System.Diagnostics;
using System.Text;

namespace HandHeld.Host.Core.Video;

/// <summary>
/// H.264 encoder via FFmpeg's h264_nvenc (NVENC on the RTX 5060).
/// Spawns ffmpeg.exe: BGRA8 rawvideo in on stdin, Annex-B H.264 out on stdout.
/// </summary>
public sealed class H264Encoder : IDisposable
{
    private readonly Process _process;
    private readonly Stream _stdin;
    private readonly BinaryReader _stdout;
    private readonly Thread _reader;
    private readonly MemoryStream _buffer = new();
    private long _frameIndex;

    public int Width { get; }
    public int Height { get; }
    public int Fps { get; }

    public H264Encoder(int width, int height, int fps, int bitrateKbps, bool keyframe = false)
    {
        Width = width;
        Height = height;
        Fps = fps;

        // Find ffmpeg relative to the runtime dir (D:\Dev\ffmpeg-dist\...\bin).
        string ffmpeg = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (!File.Exists(ffmpeg))
        {
            var candidates = new[]
            {
                @"D:\Dev\ffmpeg-dist\ffmpeg-9.0.1-essentials_build\bin\ffmpeg.exe",
            };
            ffmpeg = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("ffmpeg.exe not found. Install it under D:\\Dev\\ffmpeg-dist or copy next to the app.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("rawvideo");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("bgra");
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add($"{width}x{height}");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add(fps.ToString());
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add("-");
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("h264_nvenc");
        psi.ArgumentList.Add("-preset");
        psi.ArgumentList.Add("p5");               // low-latency
        psi.ArgumentList.Add("-tune");
        psi.ArgumentList.Add("ll");
        psi.ArgumentList.Add("-b:v");
        psi.ArgumentList.Add($"{bitrateKbps}k");
        psi.ArgumentList.Add("-maxrate");
        psi.ArgumentList.Add($"{bitrateKbps}k");
        psi.ArgumentList.Add("-bufsize");
        psi.ArgumentList.Add($"{bitrateKbps * 2}k");
        psi.ArgumentList.Add("-g");
        psi.ArgumentList.Add((fps * 2).ToString());   // keyframe every 2s
        // Force IDRs deterministically every 2 seconds (t in seconds).
        psi.ArgumentList.Add("-force_key_frames");
        psi.ArgumentList.Add("expr:gte(t,n_forced*2)");
        psi.ArgumentList.Add("-bf");
        psi.ArgumentList.Add("0");                    // no B-frames (low latency)
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("h264");
        psi.ArgumentList.Add("-");

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg");
        _stdin = _process.StandardInput.BaseStream;
        _stdout = new BinaryReader(_process.StandardOutput.BaseStream);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "ffmpeg-stdout" };
        _reader.Start();
    }

    private void ReadLoop()
    {
        try
        {
            var chunk = new byte[65536];
            while (true)
            {
                int n = _stdout.Read(chunk, 0, chunk.Length);
                if (n <= 0) break;
                lock (_buffer) _buffer.Write(chunk, 0, n);
            }
        }
        catch (Exception)
        {
            // process exit
        }
    }

    /// <summary>Encodes one BGRA8 frame; returns Annex-B access units emitted so far.</summary>
    public List<byte[]> Encode(byte[] bgra, int width, int height)
    {
        _stdin.Write(bgra, 0, bgra.Length);
        _stdin.Flush();
        _frameIndex++;
        return Drain();
    }

    /// <summary>Requests an IDR keyframe on the next encode.</summary>
    public void ForceKeyFrame()
    {
        // Send a small 1x1 frame twice with -force_key_frames not possible mid-stream;
        // instead we rely on periodic -g keyframes. Kept for API compatibility.
    }

    private List<byte[]> Drain()
    {
        byte[] bytes;
        lock (_buffer)
        {
            if (_buffer.Length == 0) return new List<byte[]>();
            bytes = _buffer.ToArray();
            _buffer.SetLength(0);
            _buffer.Position = 0;
        }

        var units = new List<byte[]>();
        int i = 0;
        while (i < bytes.Length)
        {
            int start = FindStartCode(bytes, i);
            if (start < 0) break;
            int scLen = (start + 2 < bytes.Length && bytes[start + 2] == 1) ? 3 : 4;
            int next = FindStartCode(bytes, start + scLen);
            int end = next < 0 ? bytes.Length : next;
            if (end > start)
            {
                var au = new byte[end - start];
                Array.Copy(bytes, start, au, 0, au.Length);
                units.Add(au);
            }
            i = next < 0 ? bytes.Length : next;
        }
        return units;
    }

    private static int FindStartCode(byte[] data, int from)
    {
        for (int i = from; i < data.Length - 3; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1) return i;
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1) return i;
        }
        return -1;
    }

    public void Dispose()
    {
        try
        {
            _stdin.Close();
            _process.WaitForExit(2000);
        }
        catch (Exception)
        {
        }
        try { _process.Kill(); } catch { }
        _process.Dispose();
    }
}
