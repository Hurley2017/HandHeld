using System;
using System.Runtime.InteropServices;

namespace TestRes;

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

    [DllImport("user32.dll")] public static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
    [DllImport("user32.dll")] public static extern int ChangeDisplaySettingsEx(string? dev, ref DEVMODE dm, IntPtr wnd, uint flags, IntPtr param);

    public static void Main()
    {
        string dev = "\\\\.\\DISPLAY5";
        DEVMODE? best = null;
        int bestScore = int.MaxValue;
        for (int mode = 0; ; mode++)
        {
            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(dev, mode, ref dm)) break;
            int w = (int)dm.dmPelsWidth, h = (int)dm.dmPelsHeight;
            if (w == 0 || h == 0) continue;
            int score = Math.Abs(w - 1920) + Math.Abs(h - 1080);
            if (score < bestScore) { bestScore = score; best = dm; }
            if (w == 1920 && h == 1080 && dm.dmDisplayFrequency == 60) { best = dm; break; }
        }

        if (best != null)
        {
            var target = best.Value;
            Console.WriteLine($"Found mode: {target.dmPelsWidth}x{target.dmPelsHeight} @ {target.dmDisplayFrequency}Hz");
            target.dmFields = 0x00080000 | 0x00100000 | 0x00400000; // DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY
            int r = ChangeDisplaySettingsEx(dev, ref target, IntPtr.Zero, 0x00000001 /* CDS_UPDATEREGISTRY */, IntPtr.Zero);
            Console.WriteLine($"ChangeDisplaySettingsEx result: {r}");
        }
    }
}
