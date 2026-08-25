using System.Runtime.InteropServices;

namespace HandHeld.Host.Core.Video;

/// <summary>Enumerates physical + virtual displays on the host.</summary>
public static class DisplayManager
{
    public sealed record DisplayInfo(int Index, string Name, string DeviceName, bool IsVirtual, int Width, int Height);

    /// <summary>Lists all active displays; virtual ones are flagged (they show only to stream clients).</summary>
    public static List<DisplayInfo> GetDisplays()
    {
        var result = new List<DisplayInfo>();
        var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        uint i = 0;
        while (EnumDisplayDevices(null, i, ref d, 0))
        {
            var isAttached = (d.StateFlags & 0x1) != 0;          // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
            var isPrimary = (d.StateFlags & 0x4) != 0;            // DISPLAY_DEVICE_PRIMARY_DEVICE
            var isMirror = (d.StateFlags & 0x8) != 0;             // DISPLAY_DEVICE_MIRRORING_DRIVER
            var isVirtual = (d.StateFlags & 0x10) != 0;           // DISPLAY_DEVICE_VGA_COMPATIBLE? no — see below

            // A "virtual" display: attached, not the primary, and its device string
            // matches the common virtual driver names (IddSample/IddCx, Virtual Display).
            if (isAttached)
            {
                var name = d.DeviceString ?? d.DeviceName ?? $"Display {i}";
                var virtualHint = name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Idd", StringComparison.OrdinalIgnoreCase)
                    || d.DeviceName.Contains("DISPLAY", StringComparison.OrdinalIgnoreCase) && name.Contains("Mirror", StringComparison.OrdinalIgnoreCase);

                // Fallback heuristic: any attached non-primary display that isn't the
                // standard built-in is treated as virtual-capable.
                bool likelyVirtual = virtualHint || (!isPrimary && !isMirror && name.Contains("Virtual", StringComparison.OrdinalIgnoreCase));

                result.Add(new DisplayInfo((int)i, name, d.DeviceName ?? "", likelyVirtual, 0, 0));
            }
            i++;
            d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        }
        return result;
    }

    /// <summary>Finds the first virtual display index, or -1 if none.</summary>
    public static int FindVirtualDisplayIndex()
    {
        foreach (var disp in GetDisplays())
        {
            if (disp.IsVirtual) return disp.Index;
        }
        return -1;
    }

    /// <summary>Returns the device name ("\\.\DISPLAY5") of the virtual display, or null.</summary>
    public static string? FindVirtualDisplayDeviceName()
    {
        foreach (var disp in GetDisplays())
        {
            if (disp.IsVirtual) return disp.DeviceName;
        }
        return null;
    }

    /// <summary>Makes the given display primary (games open there) — session-only (not persisted).</summary>
    public static bool SetPrimaryDisplay(string deviceName)
    {
        var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, -1 /* ENUM_CURRENT_SETTINGS */, ref dm)) return false;
        dm.dmPositionX = 0;
        dm.dmPositionY = 0;
        dm.dmFields = 0x00000020; // DM_POSITION
        const int CDS_SET_PRIMARY = 0x4;
        var result = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_SET_PRIMARY, IntPtr.Zero);
        return result == 0;
    }

    /// <summary>Gets the current primary display's device name.</summary>
    public static string? GetPrimaryDisplayDeviceName()
    {
        foreach (var disp in GetDisplays())
        {
            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(disp.DeviceName, -1, ref dm) &&
                (dm.dmPositionX == 0 && dm.dmPositionY == 0))
            {
                return disp.DeviceName;
            }
        }
        return null;
    }

    /// <summary>
    /// Sets the virtual display (if present) to the given resolution and returns
    /// true on success. The client's resolution drives the virtual display.
    /// Uses a driver-enumerated mode (hand-built DEVMODEs are rejected).
    /// </summary>
    public static bool SetVirtualDisplayResolution(int width, int height)
    {
        var dev = FindVirtualDisplayDeviceName();
        if (dev == null) return false;

        // Find the closest supported mode (exact match preferred).
        DEVMODE? best = null;
        int bestScore = int.MaxValue;
        for (int mode = 0; ; mode++)
        {
            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(dev, mode, ref dm)) break;
            int w = (int)dm.dmPelsWidth, h = (int)dm.dmPelsHeight;
            if (w == 0 || h == 0) continue;
            int score = Math.Abs(w - width) + Math.Abs(h - height);
            if (score < bestScore) { bestScore = score; best = dm; }
            if (w == width && h == height) break; // exact match
        }
        if (best == null) return false;

        var target = best.Value;
        target.dmFields = 0x00080000 | 0x00100000 | 0x00400000; // DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY
        const int CDS_UPDATEREGISTRY = 0x00000001;
        var result = ChangeDisplaySettingsEx(dev, ref target, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        return result == 0;
    }

    /// <summary>The virtual display device instance ID (VDD from the driver package).</summary>
    private const string VddInstanceId = @"ROOT\DISPLAY\0000";

    /// <summary>Enables or disables the virtual display device via pnputil.
    /// When disabled, the virtual monitor disappears entirely from Windows.
    /// Fire-and-forget: never blocks the caller (pnputil can be slow).</summary>
    public static void SetVirtualDisplayEnabled(bool enabled)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = enabled
                    ? $"/enable-device \"{VddInstanceId}\""
                    : $"/disable-device \"{VddInstanceId}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Never fatal — the host must keep running even if pnputil fails.
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
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

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);
}
