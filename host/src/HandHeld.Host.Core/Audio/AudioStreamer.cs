using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace HandHeld.Host.Core.Audio;

/// <summary>
/// Encodes system audio (WASAPI loopback, float PCM) to AAC via FFmpeg and
/// sends ADTS frames over UDP. Runs its own ffmpeg subprocess.
/// </summary>
public sealed class AudioStreamer : IDisposable
{
    public const int Port = 45340;

    private readonly IPEndPoint _remote;
    private readonly CancellationTokenSource _cts = new();
    private readonly UdpClient _udp = new();
    private Process? _ffmpeg;
    private Thread? _reader;
    private LoopbackCapture? _capture;
    private Thread? _loop;
    private int _packetCount;

    public AudioStreamer(IPEndPoint remote) => _remote = remote;

    public void Start()
    {
        string ffmpeg = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (!File.Exists(ffmpeg))
        {
            var candidates = new[] { @"D:\Dev\ffmpeg-dist\ffmpeg-9.0.1-essentials_build\bin\ffmpeg.exe" };
            ffmpeg = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("ffmpeg.exe not found.");
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
        // Input: float32 stereo from stdin. Output: AAC ADTS to stdout.
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("f32le");
        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add("48000");
        psi.ArgumentList.Add("-ac");
        psi.ArgumentList.Add("2");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add("-");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add("128k");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("adts");
        psi.ArgumentList.Add("-");

        _ffmpeg = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg for audio");
        _reader = new Thread(ReadStdout) { IsBackground = true, Name = "audio-ffmpeg" };
        _reader.Start();

        _capture = new LoopbackCapture();
        _capture.DataAvailable += OnAudio;
        _capture.Start();

        _loop = new Thread(() =>
        {
            while (!_cts.IsCancellationRequested)
            {
                Thread.Sleep(1000);
                if (_packetCount > 0)
                {
                    _packetCount = 0;
                }
            }
        }) { IsBackground = true };
        _loop.Start();
    }

    private void OnAudio(byte[] pcm)
    {
        if (_ffmpeg == null || _ffmpeg.HasExited) return;
        try
        {
            _ffmpeg.StandardInput.BaseStream.Write(pcm, 0, pcm.Length);
            _ffmpeg.StandardInput.BaseStream.Flush();
        }
        catch (Exception)
        {
            // ffmpeg exited
        }
    }

    private void ReadStdout()
    {
        try
        {
            var chunk = new byte[4096];
            while (true)
            {
                int n = _ffmpeg!.StandardOutput.BaseStream.Read(chunk, 0, chunk.Length);
                if (n <= 0) break;
                // Send each ADTS frame as one datagram: find sync 0xFFF.
                int i = 0;
                while (i < n)
                {
                    if (chunk[i] == 0xFF && (chunk[i + 1] & 0xF0) == 0xF0)
                    {
                        int frameLen = ((chunk[i + 3] & 0x03) << 11) | (chunk[i + 4] << 3) | (chunk[i + 5] >> 5);
                        if (frameLen <= 0 || i + frameLen > n) break;
                        var packet = new byte[4 + frameLen];
                        packet[0] = 0; // tag
                        long ts = Environment.TickCount64 & 0xFFFFFF;
                        packet[1] = (byte)(ts >> 16);
                        packet[2] = (byte)(ts >> 8);
                        packet[3] = (byte)ts;
                        Array.Copy(chunk, i, packet, 4, frameLen);
                        _udp.Send(packet, packet.Length, _remote);
                        i += frameLen;
                    }
                    else i++;
                }
            }
        }
        catch (Exception)
        {
            // exit
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _capture?.Stop(); } catch { }
        _capture?.Dispose();
        try { _ffmpeg?.StandardInput.Close(); } catch { }
        try { _ffmpeg?.Kill(); } catch { }
        _ffmpeg?.Dispose();
        _udp.Dispose();
    }
}
