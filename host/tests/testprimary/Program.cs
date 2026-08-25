using System;
using System.Runtime.InteropServices;

namespace TestPrimary;

public class Program
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion; public ushort dmDriverVersion; public ushort dmSize; public ushort dmDriverExtra;
        public uint dmFields; public int dmPositionX; public int dmPositionY; public int dmDisplayOrientation;
        public int dmDisplayFixedOutput; public short dmColor; public short dmDuplex; public short dmYResolution;
        public short dmTTOption; public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels; public uint dmBitsPerPel; public uint dmPelsWidth; public uint dmPelsHeight;
        public uint dmDisplayFlags; public uint dmDisplayFrequency; public uint dmICMMethod; public uint dmICMIntent;
        public uint dmMediaType; public uint dmDitherType; public uint dmReserved1; public uint dmReserved2;
        public uint dmPanningWidth; public uint dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll")] public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
    [DllImport("user32.dll")] public static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettingsEx(string? dev, ref DEVMODE dm, IntPtr wnd, uint flags, IntPtr param);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettingsEx(string? dev, IntPtr dm, IntPtr wnd, uint flags, IntPtr param);

    public static void Main()
    {
        // Enumerate all attached displays
        var displays = new System.Collections.Generic.List<string>();
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        uint i = 0;
        while (EnumDisplayDevices(null, i, ref dd, 0))
        {
            if ((dd.StateFlags & 0x1) != 0) // attached
            {
                displays.Add(dd.DeviceName);
            }
            i++;
            dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        }

        string targetPrimary = "\\\\.\\DISPLAY5";
        Console.WriteLine($"Attached displays: {string.Join(", ", displays)}");

        // Enumerate target
        var dmTarget = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        EnumDisplaySettings(targetPrimary, -1, ref dmTarget);
        int offsetX = dmTarget.dmPositionX;
        int offsetY = dmTarget.dmPositionY;

        // Set target primary at (0, 0)
        dmTarget.dmPositionX = 0;
        dmTarget.dmPositionY = 0;
        dmTarget.dmFields = 0x00000020 | 0x00080000 | 0x00100000 | 0x00040000 | 0x00400000;
        int rTarget = ChangeDisplaySettingsEx(targetPrimary, ref dmTarget, IntPtr.Zero, 0x00000001 | 0x00000004 /* CDS_UPDATEREGISTRY | CDS_SET_PRIMARY */, IntPtr.Zero);
        Console.WriteLine($"rTarget ({targetPrimary}): {rTarget}");

        // Shift all other displays by (-offsetX, -offsetY)
        foreach (var disp in displays)
        {
            if (disp.Equals(targetPrimary, StringComparison.OrdinalIgnoreCase)) continue;
            var dmOther = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(disp, -1, ref dmOther))
            {
                dmOther.dmPositionX -= offsetX;
                dmOther.dmPositionY -= offsetY;
                dmOther.dmFields = 0x00000020 | 0x00080000 | 0x00100000 | 0x00040000 | 0x00400000;
                int rOther = ChangeDisplaySettingsEx(disp, ref dmOther, IntPtr.Zero, 0x00000001 /* CDS_UPDATEREGISTRY */, IntPtr.Zero);
                Console.WriteLine($"rOther ({disp}): {rOther}");
            }
        }

        // Commit changes
        int rCommit = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0x00000001 /* CDS_UPDATEREGISTRY */, IntPtr.Zero);
        Console.WriteLine($"rCommit: {rCommit}");
    }
}
