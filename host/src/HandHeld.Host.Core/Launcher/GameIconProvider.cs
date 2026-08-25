using System.Drawing;

namespace HandHeld.Host.Core.Launcher;

/// <summary>Finds a game's icon (Steam library header image, or the app's exe icon).</summary>
public static class GameIconProvider
{
    /// <summary>Returns the icon as a base64 PNG (for the JSON games list), or null.</summary>
    public static string? GetIconPng(GameInfo game)
    {
        try
        {
            using var icon = GetIcon(game);
            if (icon == null) return null;
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the icon for a game, or null when none found.</summary>
    public static Icon? GetIcon(GameInfo game)
    {
        try
        {
            if (game.Source == "steam" && game.Id.StartsWith("steam_"))
            {
                var appId = game.Id["steam_".Length..];
                foreach (var steamApps in GameScanner.SteamAppFolders)
                {
                    var header = Path.Combine(steamApps, "librarycache", appId, "header.jpg");
                    if (File.Exists(header))
                    {
                        return IconFromFile(header);
                    }
                    var logo = Path.Combine(steamApps, "librarycache", appId, "logo.png");
                    if (File.Exists(logo))
                    {
                        return IconFromFile(logo);
                    }
                }
            }

            if (game.Source == "shortcut" && game.ExePath != null && File.Exists(game.ExePath))
            {
                var icon = Icon.ExtractAssociatedIcon(game.ExePath);
                return icon != null ? (Icon)icon.Clone() : null;
            }
        }
        catch
        {
            // no icon available — caller falls back to the default.
        }
        return null;
    }

    private static Icon IconFromFile(string path)
    {
        using var bmp = new Bitmap(path);
        var hIcon = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
