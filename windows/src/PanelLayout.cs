namespace Nimbus;

/// <summary>Where the panel goes: its size for the monitor under the tray icon and its place docked to the taskbar side.</summary>
internal static class PanelLayout
{
    /// <summary>Panel rectangle in physical pixels: 380 x 620 DIP times the monitor scale, docked to the taskbar side.</summary>
    public static Rectangle Compute(Point anchor, Size panelDip, int gapDip, out double fitZoom)
    {
        var screen = Screen.FromPoint(anchor);
        var work = screen.WorkingArea;
        var scale = Native.ScaleAt(anchor);
        var gap = (int)Math.Round(gapDip * scale);
        // The page is laid out for the full DIP size. On a screen too small for it (a laptop at 125% or 150%),
        // shrink the whole panel and zoom the page by the same amount, so nothing is ever squashed or cut off.
        var wantW = panelDip.Width * scale;
        var wantH = panelDip.Height * scale;
        var fit = Math.Min(1.0, Math.Min((work.Width - gap * 2) / wantW, (work.Height - gap * 2) / wantH));
        fitZoom = Math.Max(0.4, fit);
        var w = Math.Max(1, (int)Math.Round(wantW * fitZoom));
        var h = Math.Max(1, (int)Math.Round(wantH * fitZoom));

        var edge = TaskbarEdge(screen);
        // An auto-hidden taskbar does not shrink the working area; leave room for it to slide in.
        if (work == screen.Bounds)
        {
            var reserve = (int)Math.Round(48 * scale);
            work = edge switch
            {
                Native.ABE_LEFT => new Rectangle(work.Left + reserve, work.Top, work.Width - reserve, work.Height),
                Native.ABE_TOP => new Rectangle(work.Left, work.Top + reserve, work.Width, work.Height - reserve),
                Native.ABE_RIGHT => new Rectangle(work.Left, work.Top, work.Width - reserve, work.Height),
                _ => new Rectangle(work.Left, work.Top, work.Width, work.Height - reserve),
            };
        }

        int x, y;
        switch (edge)
        {
            case Native.ABE_LEFT: x = work.Left + gap; y = anchor.Y - h / 2; break;
            case Native.ABE_RIGHT: x = work.Right - gap - w; y = anchor.Y - h / 2; break;
            case Native.ABE_TOP: x = anchor.X - w / 2; y = work.Top + gap; break;
            default: x = anchor.X - w / 2; y = work.Bottom - gap - h; break;
        }
        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - gap - w));
        y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - gap - h));
        return new Rectangle(x, y, w, h);
    }

    /// <summary>
    /// Taskbar side for the screen that was clicked: the side where the working area is cut short.
    /// Falls back to the main taskbar's edge (auto-hide or no taskbar on that screen).
    /// </summary>
    private static uint TaskbarEdge(Screen screen)
    {
        var b = screen.Bounds;
        var w = screen.WorkingArea;
        if (w.Bottom < b.Bottom) return Native.ABE_BOTTOM;
        if (w.Top > b.Top) return Native.ABE_TOP;
        if (w.Left > b.Left) return Native.ABE_LEFT;
        if (w.Right < b.Right) return Native.ABE_RIGHT;
        return Native.TaskbarEdge();
    }
}
