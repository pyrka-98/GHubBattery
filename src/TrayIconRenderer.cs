using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace GHubBattery;

/// <summary>
/// Generates tray icons: a rounded rectangle filled with a battery-level color
/// and a large bold percentage number centered inside it.
///
/// Color coding:
///   >= 50%  -> green
///   20-49%  -> amber
///   < 20%   -> red
///   unknown -> gray
///
/// When charging, a small yellow dot appears in the bottom-right corner.
/// </summary>
public static class TrayIconRenderer
{
    private static readonly Color ColorGood     = Color.FromArgb(0x2E, 0xA8, 0x4E);
    private static readonly Color ColorLow      = Color.FromArgb(0xE0, 0x8C, 0x00);
    private static readonly Color ColorCritical = Color.FromArgb(0xDC, 0x3A, 0x2F);
    private static readonly Color ColorUnknown  = Color.FromArgb(0x66, 0x66, 0x66);
    private static readonly Color ColorText     = Color.White;
    private static readonly Color ColorCharging = Color.FromArgb(0xFF, 0xEE, 0x44);

    public static Icon Render(int percent, bool isCharging = false, int size = 16)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g   = Graphics.FromImage(bmp);
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);
        DrawBadge(g, percent, isCharging, size);
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public static Icon RenderMultiSize(int percent, bool isCharging = false)
    {
        using var ms = new System.IO.MemoryStream();
        WriteIco(ms, percent, isCharging, [16, 32]);
        ms.Position = 0;
        return new Icon(ms);
    }

    private static void DrawBadge(Graphics g, int percent, bool isCharging, int size)
    {
        var bgColor = percent switch
        {
            < 0  => ColorUnknown,
            < 20 => ColorCritical,
            < 50 => ColorLow,
            _    => ColorGood,
        };

        float s  = size;
        float rx = size >= 24 ? 3f : 2f;

        // Filled rounded rectangle background
        using var bgBrush = new SolidBrush(bgColor);
        DrawRoundRectFill(g, bgBrush, 0, 0, s, s, rx);

        // Subtle dark border
        using var borderPen = new Pen(Color.FromArgb(80, 0, 0, 0), 1f);
        DrawRoundRect(g, borderPen, 0.5f, 0.5f, s - 1f, s - 1f, rx);

        // Number label
        var label = percent >= 0 ? percent.ToString() : "?";

        float fontSize = label.Length switch
        {
            1 => size * 0.72f,
            2 => size * 0.58f,
            _ => size * 0.42f,   // "100"
        };

        using var font   = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush  = new SolidBrush(ColorText);
        using var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0));

        var tsz = g.MeasureString(label, font);
        float tx = (s - tsz.Width)  / 2f + 0.5f;
        float ty = (s - tsz.Height) / 2f + 0.5f;

        g.DrawString(label, font, shadow, tx + 0.8f, ty + 0.8f);
        g.DrawString(label, font, brush,  tx,        ty);

        // Charging dot (bottom-right)
        if (isCharging)
        {
            float dotR = Math.Max(2f, size * 0.18f);
            float dotX = s - dotR - 0.5f;
            float dotY = s - dotR - 0.5f;
            using var dotBorder = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            using var dotBrush  = new SolidBrush(ColorCharging);
            g.FillEllipse(dotBorder, dotX - 0.5f, dotY - 0.5f, dotR * 2 + 1, dotR * 2 + 1);
            g.FillEllipse(dotBrush,  dotX,        dotY,        dotR * 2,     dotR * 2);
        }
    }

    private static void DrawRoundRect(Graphics g, Pen pen, float x, float y, float w, float h, float r)
    {
        using var path = RoundRectPath(x, y, w, h, r);
        g.DrawPath(pen, path);
    }

    private static void DrawRoundRectFill(Graphics g, Brush brush, float x, float y, float w, float h, float r)
    {
        using var path = RoundRectPath(x, y, w, h, r);
        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundRectPath(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        float d = r * 2;
        path.AddArc(x,         y,         d, d, 180, 90);
        path.AddArc(x + w - d, y,         d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d,   0, 90);
        path.AddArc(x,         y + h - d, d, d,  90, 90);
        path.CloseFigure();
        return path;
    }

    private static void WriteIco(System.IO.Stream output, int percent, bool isCharging, int[] sizes)
    {
        var images = sizes.Select(sz =>
        {
            using var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
            using var g   = Graphics.FromImage(bmp);
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.Transparent);
            DrawBadge(g, percent, isCharging, sz);
            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using var w = new System.IO.BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)sizes.Length);

        int dataOffset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int sz = sizes[i];
            w.Write((byte)(sz >= 256 ? 0 : sz));
            w.Write((byte)(sz >= 256 ? 0 : sz));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write((uint)images[i].Length);
            w.Write((uint)dataOffset);
            dataOffset += images[i].Length;
        }

        foreach (var img in images)
            w.Write(img);
    }
}
