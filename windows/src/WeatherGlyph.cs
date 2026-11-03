using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Nimbus;

/// <summary>What the page last asked the tray icon to show (the "bar" message, same as the Mac menu bar item).</summary>
internal sealed record BarInfo(string Text, string Kind, string Style, string Tip, bool Empty)
{
    public static readonly BarInfo Blank = new("", "cloud", "both", "Nimbus", true);

    /// <summary>Same picture and number as another message: the tray icon is only redrawn when this changes.</summary>
    public string Key => $"{Kind}|{Style}|{Text}|{Empty}";
}

/// <summary>
/// Draws the tray icon with System.Drawing: a small weather picture, the temperature, or both, at the exact tray
/// size. Pictures are drawn in a 24-unit box (as the page's own icons are in a 48-unit box) and scaled.
/// </summary>
internal static class WeatherGlyph
{
    private static readonly Color Sun = Color.FromArgb(255, 196, 61);
    private static readonly Color Moon = Color.FromArgb(255, 214, 102);
    private static readonly Color Drop = Color.FromArgb(86, 160, 235);
    private static readonly Color Flake = Color.FromArgb(140, 190, 240);
    private static readonly Color Bolt = Color.FromArgb(255, 200, 40);

    /// <summary>The icon bitmap for a bar message, on a dark or light taskbar, px by px.</summary>
    public static Bitmap Render(BarInfo bar, int px, bool taskbarDark)
    {
        var bmp = new Bitmap(px, px);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // Same rule as the Mac: the picture shows unless the user chose "Temp" and there is a number.
        var number = bar.Text.TrimEnd('C', 'F');
        var showText = bar.Style != "icon" && number.Length > 0 && !bar.Empty;
        var showIcon = bar.Style != "temp" || !showText;
        var ink = taskbarDark ? Color.White : Color.FromArgb(0x1A, 0x1A, 0x1A);

        if (showIcon && showText)
        {
            var iconBox = px * 0.58f;
            Draw(g, bar.Kind, (px - iconBox) / 2f, 0, iconBox, taskbarDark);
            DrawNumber(g, number, new RectangleF(0, iconBox * 0.92f, px, px - iconBox * 0.92f), ink);
        }
        else if (showText) DrawNumber(g, number, new RectangleF(0, 0, px, px), ink);
        else Draw(g, bar.Kind, px * 0.03f, px * 0.03f, px * 0.94f, taskbarDark);
        return bmp;
    }

    /// <summary>Test aid: the icon at 16, 20, 24 and 32 px on a dark and a light taskbar colour, magnified 6x.</summary>
    public static Bitmap Sheet(BarInfo bar)
    {
        int[] sizes = { 16, 20, 24, 32 };
        const int zoom = 6, cell = 40 * zoom;
        var sheet = new Bitmap(cell * sizes.Length, cell * 2);
        using var g = Graphics.FromImage(sheet);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (var row = 0; row < 2; row++)
        {
            var dark = row == 0;
            using var back = new SolidBrush(dark ? Color.FromArgb(0x20, 0x20, 0x20) : Color.FromArgb(0xF3, 0xF3, 0xF3));
            g.FillRectangle(back, 0, row * cell, cell * sizes.Length, cell);
            for (var i = 0; i < sizes.Length; i++)
            {
                using var icon = Render(bar, sizes[i], dark);
                var s = sizes[i] * zoom;
                g.DrawImage(icon, i * cell + (cell - s) / 2, row * cell + (cell - s) / 2, s, s);
            }
        }
        return sheet;
    }

    private static void DrawNumber(Graphics g, string text, RectangleF box, Color ink)
    {
        var size = box.Height * 0.86f;
        using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        // Shrink the type until the whole number ("-12°") fits the width of the icon.
        Font font;
        while (true)
        {
            font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            if (size <= 6f || g.MeasureString(text, font, PointF.Empty, format).Width <= box.Width - 0.5f) break;
            font.Dispose();
            size -= 0.5f;
        }
        using (font)
        using (var brush = new SolidBrush(ink))
            g.DrawString(text, font, brush, box, format);
    }

    // ---------- Pictures (24-unit box) ----------

    private static void Draw(Graphics g, string kind, float x, float y, float size, bool dark)
    {
        var state = g.Save();
        g.TranslateTransform(x, y);
        g.ScaleTransform(size / 24f, size / 24f);
        var cloud = dark ? Color.FromArgb(232, 238, 246) : Color.FromArgb(118, 138, 164);
        switch (kind)
        {
            case "clear": SunDisc(g, 12, 12, 5, 7.6f, 10.4f); break;
            case "clear-n": MoonDisc(g, 12, 12, 8f); break;
            case "partly": SunDisc(g, 15.5f, 8.5f, 3.8f, 5.8f, 7.8f); Cloud(g, cloud, 0, 4.5f, 0.86f); break;
            case "partly-n": MoonDisc(g, 15.5f, 8.5f, 5.5f); Cloud(g, cloud, 0, 4.5f, 0.86f); break;
            case "rain":
                Cloud(g, cloud, 0.5f, -1.5f, 0.9f);
                Streak(g, Drop, 8f, 18.5f, 6.5f, 22.5f); Streak(g, Drop, 12.5f, 18.5f, 11f, 22.5f); Streak(g, Drop, 17f, 18.5f, 15.5f, 22.5f);
                break;
            case "drizzle":
                Cloud(g, cloud, 0.5f, -1.5f, 0.9f);
                Streak(g, Drop, 9.5f, 19f, 8.5f, 21.5f); Streak(g, Drop, 15f, 19f, 14f, 21.5f);
                break;
            case "snow":
                Cloud(g, cloud, 0.5f, -1.5f, 0.9f);
                using (var b = new SolidBrush(Flake))
                    foreach (var (cx, cy) in new[] { (8f, 20f), (12.5f, 22f), (17f, 20f) }) g.FillEllipse(b, cx - 1.4f, cy - 1.4f, 2.8f, 2.8f);
                break;
            case "thunder":
                Cloud(g, cloud, 0.5f, -2.5f, 0.9f);
                using (var b = new SolidBrush(Bolt))
                    g.FillPolygon(b, new[] { new PointF(13.8f, 14f), new PointF(9.2f, 19.8f), new PointF(12.4f, 19.8f), new PointF(10.4f, 24f), new PointF(16f, 17.6f), new PointF(12.8f, 17.6f), new PointF(14.8f, 14f) });
                break;
            case "fog":
                Cloud(g, cloud, 0.5f, -2f, 0.9f);
                Streak(g, cloud, 5f, 19.5f, 19f, 19.5f); Streak(g, cloud, 8f, 22.5f, 21f, 22.5f);
                break;
            case "wind":
                using (var pen = new Pen(cloud, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, 3f, 9f, 14f, 9f); g.DrawArc(pen, 11.5f, 4f, 5f, 5f, 90, -180);
                    g.DrawLine(pen, 3f, 13.5f, 19f, 13.5f);
                    g.DrawLine(pen, 3f, 18f, 12f, 18f); g.DrawArc(pen, 9.5f, 18f, 5f, 5f, 270, 180);
                }
                break;
            default: Cloud(g, cloud, 0, 1f, 1f); break;
        }
        g.Restore(state);
    }

    private static void Cloud(Graphics g, Color color, float dx, float dy, float k)
    {
        using var path = new GraphicsPath(FillMode.Winding);
        path.AddEllipse(2.5f, 11f, 8f, 8f);
        path.AddEllipse(6f, 5f, 11f, 11f);
        path.AddEllipse(13f, 10f, 9f, 9f);
        path.AddRectangle(new RectangleF(6.5f, 13f, 11f, 6f));
        using var m = new Matrix();
        m.Translate(dx + 12f * (1 - k), dy + 12f * (1 - k));   // scale about the centre of the box
        m.Scale(k, k);
        path.Transform(m);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static void SunDisc(Graphics g, float cx, float cy, float r, float rayFrom, float rayTo)
    {
        using var pen = new Pen(Sun, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            g.DrawLine(pen, cx + (float)Math.Cos(a) * rayFrom, cy + (float)Math.Sin(a) * rayFrom, cx + (float)Math.Cos(a) * rayTo, cy + (float)Math.Sin(a) * rayTo);
        }
        using var brush = new SolidBrush(Sun);
        g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
    }

    private static void MoonDisc(Graphics g, float cx, float cy, float r)
    {
        using var disc = new GraphicsPath();
        disc.AddEllipse(cx - r, cy - r, r * 2, r * 2);
        using var bite = new GraphicsPath();
        bite.AddEllipse(cx + r * 0.35f - r * 0.9f, cy - r * 0.55f - r * 0.9f, r * 1.8f, r * 1.8f);
        using var region = new Region(disc);
        region.Exclude(bite);
        using var brush = new SolidBrush(Moon);
        g.FillRegion(brush, region);
    }

    private static void Streak(Graphics g, Color color, float x1, float y1, float x2, float y2)
    {
        using var pen = new Pen(color, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, x1, y1, x2, y2);
    }
}
