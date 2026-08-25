using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HandHeld.Host.Core;

/// <summary>Runtime-drawn HandHeld gamepad icon (no binary asset needed).</summary>
public static class IconFactory
{
    public static Icon CreateGamepadIcon(int size = 64)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(Color.FromArgb(15, 18, 26));
            using var path = RoundedRect(new RectangleF(2, 2, size - 4, size - 4), size / 5f);
            g.FillPath(bg, path);

            using var body = new SolidBrush(Color.FromArgb(88, 166, 255));
            g.FillRectangle(body, size * 0.22f, size * 0.38f, size * 0.56f, size * 0.26f);
            g.FillEllipse(body, size * 0.10f, size * 0.28f, size * 0.24f, size * 0.26f);
            g.FillEllipse(body, size * 0.66f, size * 0.28f, size * 0.24f, size * 0.26f);

            using var white = new SolidBrush(Color.White);
            // D-pad
            g.FillRectangle(white, size * 0.30f, size * 0.42f, size * 0.08f, size * 0.20f);
            g.FillRectangle(white, size * 0.26f, size * 0.46f, size * 0.16f, size * 0.08f);
            // Face buttons
            g.FillEllipse(white, size * 0.50f, size * 0.34f, size * 0.08f, size * 0.08f);
            g.FillEllipse(white, size * 0.58f, size * 0.30f, size * 0.08f, size * 0.08f);
            g.FillEllipse(white, size * 0.66f, size * 0.34f, size * 0.08f, size * 0.08f);
            g.FillEllipse(white, size * 0.58f, size * 0.40f, size * 0.08f, size * 0.08f);
        }

        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
