using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HandHeld.Host.Core.Launcher;

public sealed record GameInfo(string Id, string Title, string Source, string? ExePath = null);

/// <summary>Scans Steam libraries + registered apps + known exe locations for games.</summary>
public static class GameScanner
{
    /// <summary>Last scan result (for lookups without rescanning).</summary>
    public static IReadOnlyList<GameInfo> Games { get; private set; } = Array.Empty<GameInfo>();

    /// <summary>All steamapps folders found (used for librarycache icon lookup).</summary>
    public static List<string> SteamAppFolders { get; } = new();

    public static List<GameInfo> Scan()
    {
        var games = new List<GameInfo>();
        games.AddRange(ScanSteam());
        games.AddRange(ScanStartMenuShortcuts());
        var result = games
            .GroupBy(g => g.Id)
            .Select(g => g.First())
            .OrderBy(g => g.Title)
            .ToList();
        Games = result;
        return result;
    }

    /// <summary>Steam: reads libraryfolders.vdf, then appmanifest_*.acf for installed app names.</summary>
    private static List<GameInfo> ScanSteam()
    {
        var result = new List<GameInfo>();
        var steamPath = RegistryHelpers.GetSteamPath();
        if (steamPath == null) return result;

        var libraryFolders = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFolders)) return result;

        var folders = new List<string> { steamPath };
        SteamAppFolders.Clear();
        SteamAppFolders.Add(Path.Combine(steamPath, "steamapps"));
        try
        {
            foreach (var line in File.ReadAllLines(libraryFolders))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, "\"path\"\\s+\"([^\"]+)\"");
                if (m.Success)
                {
                    var p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(p))
                    {
                        folders.Add(Path.Combine(p, "steamapps"));
                        SteamAppFolders.Add(Path.Combine(p, "steamapps"));
                    }
                }
            }
        }
        catch { }

        foreach (var folder in folders)
        {
            var manifests = Directory.GetFiles(folder, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
            foreach (var manifest in manifests)
            {
                try
                {
                    var text = File.ReadAllText(manifest);
                    var appId = System.Text.RegularExpressions.Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                    var name = System.Text.RegularExpressions.Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    // Skip non-game Steam entries (Steamworks redist, Spacewar, etc.).
                    if (SteamNonGameApps.Contains(appId)) continue;
                    if (appId.Length > 0 && name.Length > 0)
                    {
                        result.Add(new GameInfo($"steam_{appId}", name, "steam",
                            Path.Combine(folder, $"appmanifest_{appId}.acf")));
                    }
                }
                catch { }
            }
        }
        return result;
    }

    private static readonly HashSet<string> SteamNonGameApps = new(StringComparer.Ordinal)
    {
        "228980", // Steamworks Common Redistributables
        "480",    // Spacewar
        "250900", // SteamVR
    };

    /// <summary>Registered shortcuts from the Start Menu, minus known non-game apps.</summary>
    private static List<GameInfo> ScanStartMenuShortcuts()
    {
        var result = new List<GameInfo>();
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var roots = new[] { startMenu, programs };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                var lnks = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories);
                foreach (var lnk in lnks.Take(500))
                {
                    var title = Path.GetFileNameWithoutExtension(lnk);
                    if (title.Length == 0 || title.StartsWith("Uninstall")) continue;
                    if (NonGameShortcuts.Contains(title)) continue;
                    result.Add(new GameInfo($"lnk_{Path.GetFileName(lnk)}", title, "shortcut", lnk));
                }
            }
            catch { }
        }
        return result;
    }

    private static readonly HashSet<string> NonGameShortcuts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Administrative Tools", "Command Prompt", "Control Panel", "Discord", "File Explorer",
        "LiveCaptions", "Magnify", "Narrator", "On-Screen Keyboard", "OneDrive",
        "Run", "Steam", "Task Manager TMOG", "VoiceAccess", "Windows Terminal",
        "Notepad", "Calculator", "Paint", "Snipping Tool", "Settings", "Microsoft Edge",
        "Chrome", "Firefox", "WhatsApp", "Spotify", "IDLE (Python 3.14 64-bit)",
        "Python 3.14 (64-bit)", "Python 3.14 Manuals (64-bit)", "Python 3.14 Module Docs (64-bit)",
        "Recycle Bin", "Getting Started", "Windows Update", "Internet Explorer",
    };
}

internal static class RegistryHelpers
{
    public static string? GetSteamPath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var path = key?.GetValue("InstallPath") as string;
            return path != null && Directory.Exists(path) ? path : null;
        }
        catch { return null; }
    }
}
