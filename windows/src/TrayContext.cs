using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Nimbus;

/// <summary>
/// The app itself: a tray icon showing the weather (left-click toggles the panel, right-click shows Open /
/// Open at login / Quit) and the hidden-until-needed panel. Mirrors AppDelegate in the Mac app's Sources/App.swift:
/// a 60 second tick, a refresh after wake and when the network returns, rain alerts as notifications.
/// Lives for the whole session; there is no main window.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly Theme theme = new();
    private readonly PopupForm popup;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem loginItem;
    private readonly System.Windows.Forms.Timer tickTimer = new() { Interval = 60_000 };
    private readonly LaunchOptions options;
    private TestHook? testHook;
    private Icon? trayIcon;
    private BarInfo bar = BarInfo.Blank;
    private string drawnKey = "";
    private bool exiting;

    public TrayContext(LaunchOptions options)
    {
        this.options = options;
        Store.RefreshLoginPath();

        popup = new PopupForm(theme, ExitApp);
        popup.BarChanged += OnBar;
        popup.NoticeRequested += OnNotice;
        popup.LoginChanged += () => loginItem!.Checked = Store.LoginEnabled();
        popup.PageReady += OnPageReady;

        var openItem = new ToolStripMenuItem("Open", null, (_, _) => popup.ShowPopup(TrayCenter()));
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        loginItem = new ToolStripMenuItem("Open at login", null, (_, _) =>
        {
            Store.SetLogin(!Store.LoginEnabled());
            loginItem!.Checked = Store.LoginEnabled();
            popup.SendHostInfo();   // the Settings switch follows
        });
        var quitItem = new ToolStripMenuItem("Quit", null, (_, _) =>
        {
            if (popup.Visible) popup.Quit(); else ExitApp();
        });

        menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        menu.Items.AddRange(new ToolStripItem[] { openItem, loginItem, new ToolStripSeparator(), quitItem });
        menu.Opening += (_, _) =>
        {
            loginItem.Checked = Store.LoginEnabled();
            MenuStyle.Apply(menu, theme.SystemDark);
        };

        tray = new NotifyIcon { Text = "Nimbus", ContextMenuStrip = menu };
        DrawTrayIcon();
        tray.Visible = true;
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) popup.Toggle(TrayCenter() ?? Cursor.Position);
        };
        tray.BalloonTipClicked += (_, _) => popup.ShowPopup(TrayCenter());
        theme.TaskbarChanged += () =>
        {
            if (popup.IsHandleCreated) popup.BeginInvoke((System.Windows.Forms.MethodInvoker)(() => DrawTrayIcon(force: true)));
        };

        // Once a minute the page decides whether the forecast is due (5 minutes open, 15 closed).
        tickTimer.Tick += (_, _) => popup.Js("wx.tick()");
        tickTimer.Start();
        // After sleep the network needs a moment before a request can succeed.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;

        // Start WebView2 once the message loop runs, so an early failure can still end the app cleanly.
        Application.Idle += StartOnce;
    }

    private void StartOnce(object? sender, EventArgs e)
    {
        Application.Idle -= StartOnce;
        _ = popup.InitializeAsync();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Task.Delay(3000).ContinueWith(_ => popup.Js("wx.wake()"), TaskScheduler.Default);
    }

    private void OnNetworkChanged(object? sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable) popup.Js("wx.online()");
    }

    /// <summary>Called by the second-instance signal: just open the panel.</summary>
    public void OpenFromOutside() => Post(() => popup.ShowPopup(TrayCenter()));

    /// <summary>Called by "Nimbus.exe --quit".</summary>
    public void QuitFromOutside() => Post(() => { if (popup.Visible) popup.Quit(); else ExitApp(); });

    private void Post(Action a)
    {
        try
        {
            if (!exiting && !popup.IsDisposed && popup.IsHandleCreated) popup.BeginInvoke(a);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Nimbus is shutting down.
        }
    }

    private void OnPageReady()
    {
        popup.PageReady -= OnPageReady;
        if (!options.Test) return;
        // Automated Windows test: keep the panel open when it loses focus and listen for commands (see TestHook).
        popup.StayOpen = true;
        testHook = new TestHook(popup, TrayBounds, () => bar, () => tray.Visible,
            () => popup.ShowPopup(TrayCenter()), () => popup.Quit());
        testHook.Start();
    }

    // ---------- Tray icon ----------

    /// <summary>A bar message arrived: new tooltip every time, new picture only when it really changed.</summary>
    private void OnBar(BarInfo info)
    {
        bar = info;
        tray.Text = info.Tip.Length > 120 ? info.Tip[..120] : info.Tip;
        DrawTrayIcon();
    }

    /// <summary>Weather picture and/or temperature at the exact tray size, white on a dark taskbar and near-black on a light one.</summary>
    private void DrawTrayIcon(bool force = false)
    {
        var key = $"{bar.Key}|{theme.TaskbarDark}|{TraySize()}";
        if (!force && key == drawnKey) return;   // swapping the picture needlessly makes the tray flicker
        drawnKey = key;
        using var bmp = WeatherGlyph.Render(bar, TraySize(), theme.TaskbarDark);
        var handle = bmp.GetHicon();
        var next = (Icon)Icon.FromHandle(handle).Clone();
        Native.DestroyIcon(handle);
        tray.Icon = next;
        trayIcon?.Dispose();
        trayIcon = next;
    }

    private static int TraySize()
    {
        try { return Math.Max(16, Native.GetSystemMetricsForDpi(Native.SM_CXSMICON, Native.GetDpiForSystem())); }
        catch { return SystemInformation.SmallIconSize.Width; }
    }

    /// <summary>Rain alert from the page: a Windows notification from the tray icon.</summary>
    private void OnNotice(string title, string body)
    {
        Store.Log($"notice: {title} / {body}");
        if (tray.Visible) tray.ShowBalloonTip(8000, title, body, ToolTipIcon.None);
    }

    /// <summary>Screen rectangle of the tray icon, or null when it is in the overflow area.</summary>
    private Rectangle? TrayBounds()
    {
        if (!tray.Visible) return null;
        try
        {
            IntPtr hwnd = IntPtr.Zero;
            uint? id = null;
            foreach (var f in typeof(NotifyIcon).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                var v = f.GetValue(tray);
                if (v is NativeWindow nw) hwnd = nw.Handle;
                else if (f.Name.TrimStart('_').Equals("id", StringComparison.OrdinalIgnoreCase) && v != null) id = Convert.ToUInt32(v);
            }
            if (hwnd == IntPtr.Zero || id == null) return null;
            var nid = new Native.NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONIDENTIFIER>(), hWnd = hwnd, uID = id.Value };
            if (Native.Shell_NotifyIconGetRect(ref nid, out var rc) != 0) return null;
            var r = Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom);
            return r.Width > 0 && r.Height > 0 ? r : null;
        }
        catch (Exception ex)
        {
            Store.Log($"Tray position unknown: {ex.Message}");
            return null;
        }
    }

    private Point? TrayCenter() => TrayBounds() is { } r ? new Point(r.Left + r.Width / 2, r.Top + r.Height / 2) : null;

    private void ExitApp()
    {
        if (exiting) return;
        exiting = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        tickTimer.Dispose();
        testHook?.Dispose();
        tray.Visible = false;   // no ghost icon left in the tray
        tray.Dispose();
        trayIcon?.Dispose();
        menu.Dispose();
        theme.Dispose();
        popup.Dispose();
        ExitThread();
    }
}
