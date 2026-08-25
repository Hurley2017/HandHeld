using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HandHeld.Host.Core.Audio;

/// <summary>WASAPI loopback capture of the default output device (system audio).</summary>
public sealed class LoopbackCapture : IDisposable
{
    private readonly WasapiCapture _capture;
    private readonly WaveFormat _format;

    public WaveFormat Format => _format;

    /// <summary>Raised with PCM float stereo samples as they arrive.</summary>
    public event Action<byte[]>? DataAvailable;

    public LoopbackCapture()
    {
        var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _capture = new WasapiLoopbackCapture(device);
        _format = _capture.WaveFormat;
        _capture.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded > 0)
            {
                var data = new byte[e.BytesRecorded];
                Array.Copy(e.Buffer, data, e.BytesRecorded);
                DataAvailable?.Invoke(data);
            }
        };
    }

    public void Start() => _capture.StartRecording();
    public void Stop() => _capture.StopRecording();

    public void Dispose() => _capture.Dispose();
}
