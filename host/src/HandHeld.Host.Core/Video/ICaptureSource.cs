namespace HandHeld.Host.Core.Video;

/// <summary>Common surface for display capture sources (DXGI or GDI).</summary>
public interface ICaptureSource : IDisposable
{
    int Width { get; }
    int Height { get; }
    bool TryCopyFrame(byte[] buffer);
}
