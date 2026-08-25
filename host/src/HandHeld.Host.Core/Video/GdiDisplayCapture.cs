using System.Runtime.InteropServices;

namespace HandHeld.Host.Core.Video;

/// <summary>
/// Captures a display (including virtual IddCx outputs) via GDI BitBlt.
/// Unlike DXGI Desktop Duplication, GDI capture works on virtual displays.
/// </summary>
public sealed class GdiDisplayCapture : ICaptureSource
{
    private readonly IntPtr _hdcScreen;
    private readonly IntPtr _hdcMem;
    private readonly IntPtr _bmp;
    private readonly IntPtr _oldBmp;
    private readonly int _width;
    private readonly int _height;
    private readonly int _x;
    private readonly int _y;

    public int Width => _width;
    public int Height => _height;

    public GdiDisplayCapture(string deviceName)
    {
        var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, -1, ref dm))
            throw new InvalidOperationException($"EnumDisplaySettings failed for {deviceName}");

        _x = dm.dmPositionX;
        _y = dm.dmPositionY;
        _width = (int)dm.dmPelsWidth;
        _height = (int)dm.dmPelsHeight;

        _hdcScreen = CreateDC("DISPLAY", null, null, IntPtr.Zero);
        if (_hdcScreen == IntPtr.Zero)
            throw new InvalidOperationException("CreateDC(DISPLAY) failed");

        _hdcMem = CreateCompatibleDC(_hdcScreen);
        _bmp = CreateCompatibleBitmap(_hdcScreen, _width, _height);
        _oldBmp = SelectObject(_hdcMem, _bmp);
    }

    /// <summary>Copies the current display contents into a BGRA buffer.</summary>
    public bool TryCopyFrame(byte[] buffer)
    {
        if (!BitBlt(_hdcMem, 0, 0, _width, _height, _hdcScreen, _x, _y, 0x00CC0020 /* SRCCOPY */))
            return false;

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = _width,
                biHeight = -_height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            },
        };
        var bits = GetDIBits(_hdcMem, _bmp, 0, (uint)_height, buffer, ref bmi, 0);
        return bits != 0;
    }

    public void Dispose()
    {
        if (_oldBmp != IntPtr.Zero) SelectObject(_hdcMem, _oldBmp);
        if (_bmp != IntPtr.Zero) DeleteObject(_bmp);
        if (_hdcMem != IntPtr.Zero) DeleteDC(_hdcMem);
        if (_hdcScreen != IntPtr.Zero) DeleteDC(_hdcScreen);
    }

    // --- P/Invoke ---
    [StructLayout(LayoutKind.Sequential)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("user32.dll")] private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
}
