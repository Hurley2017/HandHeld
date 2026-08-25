using System.Diagnostics;
using System.Text.Json;

namespace HandHeld.Host.Core.Launcher;

/// <summary>Launches games and the desktop stream session.</summary>
public static class GameLauncher
{
    /// <summary>
    /// Launches a game by ID. Steam games launch windowed + low via
    /// steam://run/&lt;appid&gt;//&lt;options&gt;/ so the host desktop stays usable
    /// (the stream goes to the phone). Shortcuts launch the .lnk.
    /// </summary>
    public static string? Launch(string gameId, int width, int height)
    {
        try
        {
            if (gameId.StartsWith("steam_", StringComparison.Ordinal))
            {
                var appId = gameId["steam_".Length..];
                // Plain launch — no extra args (Steam would prompt a yes/no dialog otherwise).
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"steam://rungameid/{appId}",
                    UseShellExecute = true,
                });
                return null;
            }

            if (gameId.StartsWith("lnk_", StringComparison.Ordinal))
            {
                var path = FindLnk(gameId);
                if (path == null) return $"Shortcut not found: {gameId}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? "",
                });
                return null;
            }

            return $"Unknown game source: {gameId}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Moves a game's main window onto the virtual display (or the given
    /// device) so it renders there, invisible on physical screens.
    /// Polls for up to 30s since windows appear after Steam/launcher handoff.
    /// </summary>
    public static void MoveGameWindowToDisplay(string gameTitle, string deviceName, int width, int height)
    {
        // Get the target display's desktop coordinates.
        var dm = new DEVMODE { dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, -1, ref dm)) return;
        int targetX = dm.dmPositionX;
        int targetY = dm.dmPositionY;

        var keywords = gameTitle
            .Split(new[] { ' ', ':', '-', '_', '™', '®' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3)
            .ToArray();

        var deadline = DateTime.UtcNow.AddSeconds(35);
        IntPtr lastMovedHwnd = IntPtr.Zero;
        int moveCount = 0;

        while (DateTime.UtcNow < deadline && moveCount < 3)
        {
            var hwnd = FindGameWindow(keywords, gameTitle);
            if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
            {
                // Check if already on the target display
                var rect = new RECT();
                if (GetWindowRect(hwnd, ref rect))
                {
                    bool onTarget = (rect.Left >= targetX - 50 && rect.Left <= targetX + 50) &&
                                    (rect.Top >= targetY - 50 && rect.Top <= targetY + 50);

                    if (!onTarget || lastMovedHwnd != hwnd)
                    {
                        // Restore from minimized/maximized state so SetWindowPos takes full effect
                        ShowWindow(hwnd, SW_RESTORE);

                        // Move and size the game window directly onto the virtual display
                        SetWindowPos(hwnd, IntPtr.Zero, targetX, targetY, width, height,
                            SWP_NOZORDER | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

                        SetForegroundWindow(hwnd);
                        lastMovedHwnd = hwnd;
                        moveCount++;
                    }
                }
            }
            Thread.Sleep(800);
        }
    }

    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "steam", "steamservice", "steamwebhelper", "HandHeld.Host", "cmd", "powershell",
        "devenv", "chrome", "msedge", "firefox", "Discord", "nvcontainer", "NVIDIA Share", "taskmgr"
    };

    private static IntPtr FindGameWindow(string[] keywords, string fullTitle)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            var sb = new System.Text.StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            // Check process name
            GetWindowThreadProcessId(hwnd, out uint pid);
            string procName = "";
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                procName = proc.ProcessName;
            }
            catch { }

            if (IgnoredProcesses.Contains(procName)) return true;

            // Direct full title match
            if (title.Contains(fullTitle, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }

            // Keyword match (e.g. "God", "War", "Batman", "Arkham")
            foreach (var kw in keywords)
            {
                if (title.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    procName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    found = hwnd;
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_RESTORE = 9;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct DEVMODE
    {
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
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
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
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

    /// <summary>Closes a running game by killing its process tree (graceful first).</summary>
    public static void Close(GameInfo? game)
    {
        if (game == null) return;
        try
        {
            if (game.Source == "steam" && game.Id.StartsWith("steam_"))
            {
                var appId = game.Id["steam_".Length..];
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"steam://shutdown/",
                    UseShellExecute = true,
                });
                return;
            }
        }
        catch { }

        // Fallback: kill processes whose main window title or path matches the game.
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.ProcessName.Contains(game.Title.Split(' ')[0], StringComparison.OrdinalIgnoreCase) ||
                        (p.MainWindowTitle.Length > 0 && p.MainWindowTitle.Contains(game.Title, StringComparison.OrdinalIgnoreCase)))
                    {
                        p.Kill(true);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static string? FindLnk(string id)
    {
        var name = id["lnk_".Length..];
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                var found = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories)
                    .FirstOrDefault(f => Path.GetFileName(f) == name);
                if (found != null) return found;
            }
            catch { }
        }
        return null;
    }
}
