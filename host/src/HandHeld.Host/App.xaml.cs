using System.IO;
using System.Windows;
using HandHeld.Host.Core;
using WpfApplication = System.Windows.Application;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using System.Drawing;

namespace HandHeld.Host;

public partial class App : WpfApplication
{
    private FormsNotifyIcon? _tray;
    private HostWindow? _window;
    private HostCore? _core;
    private System.Drawing.Icon? _ownedTrayIcon;

    /// <summary>The single app icon (from "HandHeld Icon.png" in the repo root).</summary>
    public static System.Windows.Media.ImageSource? IconSource { get; private set; }

    /// <summary>The app icon as a WPF ImageSource loaded from the embedded .ico
    /// (reliable for the taskbar — PNG sources can render blank at 16px).</summary>
    public static System.Windows.Media.ImageSource? TaskbarIcon { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Locate the icon file (repo root, or next to the exe, or D:\Dev copy).
        var candidates = new[]
        {
            @"D:\Projects\HandHeld\HandHeld Icon.png",
            Path.Combine(AppContext.BaseDirectory, "HandHeld Icon.png"),
            @"D:\Dev\handheld-icon.png",
        };
        string? iconPath = candidates.FirstOrDefault(File.Exists);

        // Taskbar icon: the embedded app.ico (in the exe directory).
        var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(icoPath))
        {
            try
            {
                TaskbarIcon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(icoPath, UriKind.Absolute));
            }
            catch
            {
                TaskbarIcon = null; // fall back to IconSource / default
            }
        }

        System.Drawing.Icon trayIcon;
        if (iconPath != null)
        {
            var src = new System.Windows.Media.Imaging.BitmapImage();
            src.BeginInit();
            src.UriSource = new Uri(iconPath, UriKind.Absolute);
            src.EndInit();
            IconSource = src;

            // Tray icon: load the PNG directly into a Bitmap and convert to Icon.
            // Keep the hIcon alive (don't DestroyIcon) — the NotifyIcon owns it.
            using var bmp = new System.Drawing.Bitmap(iconPath);
            trayIcon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
            _ownedTrayIcon = trayIcon;
        }
        else
        {
            using var icon = IconFactory.CreateGamepadIcon(64);
            IconSource = ToBitmapSource(icon);
            trayIcon = (System.Drawing.Icon)icon.Clone();
            _ownedTrayIcon = trayIcon;
        }

        _core = new HostCore();
        _core.Start();

        var menu = new FormsContextMenuStrip();
        menu.Items.Add("Open HandHeld", null, (_, _) => ShowWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _tray = new FormsNotifyIcon
        {
            Icon = trayIcon,
            Text = "HandHeld — " + _core.DisplayName,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

        ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window == null)
        {
            _window = new HostWindow(_core!);
            _window.Closed += (_, _) => _window = null;
        }
        _window.Show();
        _window.Activate();
    }

    private static System.Windows.Media.ImageSource ToBitmapSource(System.Drawing.Icon icon)
    {
        using var bmp = icon.ToBitmap();
        var hBitmap = bmp.GetHbitmap();
        try
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    protected override void OnExit(ExitEventArgs e)
    {
        _core?.Stop();
        _tray?.Dispose();
        _ownedTrayIcon?.Dispose();
        base.OnExit(e);
    }
}
