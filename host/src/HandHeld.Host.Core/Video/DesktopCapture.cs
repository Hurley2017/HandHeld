using System.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace HandHeld.Host.Core.Video;

/// <summary>DXGI Desktop Duplication capture — same approach OBS uses.</summary>
public sealed class DesktopCapture : ICaptureSource
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private int _width;
    private int _height;

    public int Width => _width;
    public int Height => _height;
    public bool Initialized => _duplication != null;

    /// <summary>
    /// Captures the display whose GDI device name matches (e.g. "\\.\DISPLAY5"),
    /// or the primary when null. Desktop Duplication is matched by device name,
    /// not raw index — virtual displays are not sequential outputs.
    /// </summary>
    public DesktopCapture(string? deviceName = null)
    {
        _device = D3D11.D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            FeatureLevel.Level_11_0);
        _context = _device.ImmediateContext;
        InitDuplication(deviceName);
    }

    private void InitDuplication(string? deviceName)
    {
        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();

        // Enumerate the adapter's outputs and pick the one matching the target device.
        IDXGIOutput? output = null;
        string? target = deviceName;
        uint idx = 0;
        while (adapter.EnumOutputs(idx, out var candidate) == 0)
        {
            var desc = candidate.Description;
            bool match = target == null
                ? desc.AttachedToDesktop
                : desc.DeviceName?.Equals(target, StringComparison.OrdinalIgnoreCase) == true;
            if (match)
            {
                output = candidate; // keep this one alive — caller owns it
            }
            else
            {
                candidate.Dispose();
            }
            if (match) break;
            idx++;
        }
        if (output == null)
        {
            // Fallback: first attached output.
            if (adapter.EnumOutputs(0, out output) != 0 || output == null)
            {
                throw new InvalidOperationException("No display output available for capture");
            }
        }

        using (output)
        {
            var desc = output.Description;
            if (!desc.AttachedToDesktop)
            {
                throw new InvalidOperationException("Display output is not attached to the desktop");
            }
            _width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
            _height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;
            if (_width <= 0 || _height <= 0)
            {
                throw new InvalidOperationException("Display output has no valid resolution");
            }

            using var output1 = output.QueryInterface<IDXGIOutput1>();
            _duplication = output1.DuplicateOutput(_device);
        }
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
    }

    /// <summary>
    /// Copies the latest desktop frame into <paramref name="dest"/> (BGRA8, width*height*4).
    /// Returns true when a new frame was copied. Throws when the desktop state changes
    /// (resolution/rotation) — caller re-creates the capture.
    /// </summary>
    public bool TryCopyFrame(byte[] dest)
    {
        if (_duplication == null || _staging == null) return false;

        var result = _duplication.AcquireNextFrame(16, out _, out var resource);
        try
        {
            if (result.Failure || resource == null) return false;

            using var texture = resource.QueryInterface<ID3D11Texture2D>();
            _context.CopyResource(_staging, texture);

            var box = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                int rowBytes = _width * 4;
                int needed = rowBytes * _height;
                if (dest.Length < needed) return false;

                if (box.RowPitch == rowBytes)
                {
                    System.Runtime.InteropServices.Marshal.Copy(box.DataPointer, dest, 0, needed);
                }
                else
                {
                    for (int y = 0; y < _height; y++)
                    {
                        var src = IntPtr.Add(box.DataPointer, y * (int)box.RowPitch);
                        System.Runtime.InteropServices.Marshal.Copy(src, dest, y * rowBytes, rowBytes);
                    }
                }
                return true;
            }
            finally
            {
                _context.Unmap(_staging, 0);
            }
        }
        finally
        {
            resource?.Release();
            _duplication.ReleaseFrame();
        }
    }

    public void Dispose()
    {
        _duplication?.Release();
        _staging?.Dispose();
        _context.ClearState();
        _device.Dispose();
    }
}
