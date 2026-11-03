using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Nimbus;

/// <summary>
/// The Nimbus panel: a borderless, rounded, shadowed window (380 x 620) that opens just above the tray icon and
/// hosts the shared web screens (../web) in WebView2. Mirrors the Mac popover in Sources/App.swift, including the
/// page bridge (ready, save, bar, locate, login, quit, notify, notifyPermission) and the wx.* callbacks.
/// </summary>
internal sealed class PopupForm : Form
{
    // Served from the bundled "web" folder. A made-up host on the reserved .example TLD:
    // Microsoft advises against .local (it can add a delay to every navigation).
    private const string Host = "nimbus.example";
    private const string Origin = "https://" + Host + "/";
    private const string StartPage = Origin + "index.html";

    private static readonly Size PanelDip = new(380, 620);
    private const int GapDip = 12;              // space between the panel and the taskbar / screen edge (see PanelLayout)
    private const int ReopenGuardMs = 600;      // a tray click that closed the panel must not reopen it
    private const int QuitFadeMs = 180;

    private const int WS_EX_TOOLWINDOW = 0x00000080;  // no taskbar button, no Alt+Tab entry
    private const int CS_DROPSHADOW = 0x00020000;     // shadow on Windows 10 (Windows 11 draws its own)

    // Esc from the page (a sheet closes first; the panel closes only when nothing was open), like the Mac key monitor.
    private const string EscScript =
        "addEventListener('keydown',function(e){if(e.key!=='Escape')return;var u=false;" +
        "try{u=!!(window.WxUI&&WxUI.escape());}catch(x){}" +
        "if(!u)chrome.webview.postMessage({a:'esc'});},true);";

    private readonly WebView2 web;
    private readonly Theme theme;
    private readonly Action onQuit;
    private Point anchor;
    private bool hasAnchor;
    private bool ready;
    private bool quitting;
    private long lastHiddenAt;
    private string? bootScriptId;
    private double fitZoom = 1.0;   // below 1 only when the screen is too small for the whole panel

    /// <summary>The page asked for a new tray icon picture or number (also carries the theme setting).</summary>
    public event Action<BarInfo>? BarChanged;

    /// <summary>A rain alert from the page: show it as a Windows notification.</summary>
    public event Action<string, string>? NoticeRequested;

    /// <summary>"Open at login" changed from inside the page.</summary>
    public event Action? LoginChanged;

    /// <summary>Raised once the page has loaded.</summary>
    public event Action? PageReady;

    public bool IsReady => ready;

    /// <summary>Keeps the panel open when it loses focus (automated test only).</summary>
    public bool StayOpen { get; set; }

    public PopupForm(Theme theme, Action onQuit)
    {
        this.theme = theme;
        this.onQuit = onQuit;

        Text = "Nimbus";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;   // sizes are computed per monitor in PopupBounds
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = false;

        web = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(web);
        ApplyThemeColors();

        // Resolution or display scale changed (docking a laptop, plugging a monitor): lay out again.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
        {
            if (IsHandleCreated && Visible) BeginInvoke((MethodInvoker)Relayout);
        };

        theme.SystemDarkChanged += _ =>
        {
            // SystemEvents may call back on its own thread; hop to the UI thread.
            if (IsHandleCreated) BeginInvoke((MethodInvoker)ApplyThemeColors);
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            if (!Native.IsWindows11) cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11: system rounded corners, border and shadow on our borderless window.
        Native.SetDwmInt(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
        Native.SetDwmInt(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, theme.EffectiveDark ? 1 : 0);
    }

    // ---------- WebView2 ----------

    /// <summary>Creates WebView2, wires the bridge and loads the screens. Called once at start, while hidden.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            // Force the window handles so WebView2 can attach while the panel is still hidden.
            _ = Handle;
            _ = web.Handle;

            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nimbus", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;

            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            // Whatever gets through (Ctrl+wheel, Ctrl +/-/0) is put straight back to the fit-to-screen zoom.
            web.ZoomFactorChanged += (_, _) => { if (Math.Abs(web.ZoomFactor - fitZoom) > 0.001) web.ZoomFactor = fitZoom; };
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;   // no F5 / Ctrl+R / Ctrl+P inside the panel
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
#if DEBUG
            core.Settings.AreDevToolsEnabled = true;
#else
            core.Settings.AreDevToolsEnabled = false;
#endif

            var webFolder = Path.Combine(AppContext.BaseDirectory, "web");
            if (!File.Exists(Path.Combine(webFolder, "index.html")))
            {
                Store.Log($"Screen files not found at {webFolder}");
                MessageBox.Show("Nimbus could not find its screen files (the \"web\" folder next to Nimbus.exe).\n\n" +
                                "Keep Nimbus.exe and the web folder together, then open Nimbus again.",
                                "Nimbus", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                onQuit();
                return;
            }
            core.SetVirtualHostNameToFolderMapping(Host, webFolder, CoreWebView2HostResourceAccessKind.Deny);

            // "Use my location" asks the page's own geolocation (see Locator); allowed for our pages only.
            core.PermissionRequested += (_, e) =>
                e.State = e.PermissionKind == CoreWebView2PermissionKind.Geolocation &&
                          e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)
                    ? CoreWebView2PermissionState.Allow
                    : CoreWebView2PermissionState.Deny;

            await RegisterBootScriptAsync();
            core.WebMessageReceived += OnWebMessage;
            core.NavigationStarting += (_, e) =>
            {
                // Only the bundled screen files may load; anything else (a stray link) is refused.
                if (!e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationCompleted += (_, e) =>
            {
                ready = e.IsSuccess;
                if (!e.IsSuccess) { Store.Log($"Page failed to load: {e.WebErrorStatus}"); return; }
                ApplyZoom();
                if (Visible) Js("wx.opened()");
                PageReady?.Invoke();
            };
            core.ProcessFailed += (_, e) =>
            {
                Store.Log($"WebView2 process failed: {e.ProcessFailedKind}");
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                    ReloadWithFreshState();
            };

            core.Navigate(StartPage);
        }
        catch (Exception ex)
        {
            Store.Log($"WebView2 start failed: {ex}");
            MessageBox.Show("Nimbus could not start its weather view (Microsoft Edge WebView2).\n\n" + ex.Message,
                            "Nimbus", MessageBoxButtons.OK, MessageBoxIcon.Error);
            onQuit();
        }
    }

    /// <summary>(Re)registers the boot script so a reload gets the current saved state, not the one from start-up.</summary>
    private async Task RegisterBootScriptAsync()
    {
        var core = web.CoreWebView2;
        if (bootScriptId != null) core.RemoveScriptToExecuteOnDocumentCreated(bootScriptId);
        bootScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(BootScript());
    }

    private async void ReloadWithFreshState()
    {
        try
        {
            ready = false;
            await RegisterBootScriptAsync();
            web.CoreWebView2.Navigate(StartPage);
        }
        catch (Exception ex)
        {
            Store.Log($"Reload failed: {ex.Message}");
        }
    }

    /// <summary>Runs before the page's own scripts: hands over the saved places and settings (the engine reads
    /// window.__WX_STORE__ as a JSON string, as in the Mac app) and installs the Esc handler.</summary>
    private static string BootScript() =>
        $"window.__WX_STORE__ = {(Store.LoadState() is { } state ? JsonSerializer.Serialize(state) : "null")};\n" + EscScript;

    /// <summary>Runs JavaScript in the page without waiting for the answer. Safe from any thread.</summary>
    public void Js(string js)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => Js(js))); return; }
        if (web.CoreWebView2 == null) return;
        web.ExecuteScriptAsync(js).ContinueWith(
            t => Store.Log($"Script failed: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>Runs JavaScript and returns its JSON result (used by the automated test).</summary>
    public async Task<string> Eval(string js) => web.CoreWebView2 == null ? "null" : await web.ExecuteScriptAsync(js);

    /// <summary>Tells the page what this PC can do and prefers (units follow the PC's region until the user picks).</summary>
    public void SendHostInfo()
    {
        static string B(bool b) => b ? "true" : "false";
        var (metric, mph) = Store.RegionUnits();
        // Windows needs no permission for notifications or the tray balloon, so canNotify is always true.
        Js($"wx.hostInfo({{platform:'win',canLocate:true,canNotify:true,metric:{B(metric)},mph:{B(mph)},login:{B(Store.LoginEnabled())}}})");
    }

    // ---------- Bridge: page -> app ----------

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var msg = doc.RootElement;
            if (msg.ValueKind != JsonValueKind.Object || Str(msg, "a") is not { } action) return;

            switch (action)
            {
                case "ready":
                    SendHostInfo();
                    break;
                case "save":
                    if (Str(msg, "json") is { } json) Store.SaveState(json);
                    break;
                case "bar":
                    var mode = Str(msg, "theme");
                    theme.Mode = mode is "light" or "dark" ? mode : "auto";
                    ApplyThemeColors();
                    BarChanged?.Invoke(new BarInfo(
                        Str(msg, "text") ?? "", Str(msg, "kind") ?? "cloud", Str(msg, "style") ?? "both",
                        Str(msg, "tip") ?? "Nimbus", msg.TryGetProperty("empty", out var empty) && empty.ValueKind == JsonValueKind.True));
                    break;
                case "locate":
                    Js(Locator.AskScript);
                    break;
                case "geo":
                    var ok = msg.TryGetProperty("ok", out var okProp) && okProp.ValueKind == JsonValueKind.True;
                    var lat = msg.TryGetProperty("lat", out var la) && la.ValueKind == JsonValueKind.Number ? la.GetDouble() : 0;
                    var lon = msg.TryGetProperty("lon", out var lo) && lo.ValueKind == JsonValueKind.Number ? lo.GetDouble() : 0;
                    var code = msg.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
                    _ = FinishLocateAsync(ok, lat, lon, code);
                    break;
                case "login":
                    Store.SetLogin(msg.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True);
                    SendHostInfo();
                    LoginChanged?.Invoke();
                    break;
                case "notifyPermission":
                    SendHostInfo();   // nothing to ask on Windows; the page just needs the "can notify" answer again
                    break;
                case "notify":
                    NoticeRequested?.Invoke(Str(msg, "title") ?? "Nimbus", Str(msg, "body") ?? "");
                    break;
                case "esc":
                    BeginInvoke((MethodInvoker)HidePopup);
                    break;
                case "quit":
                    // Leave the WebView2 event first; quitting disposes the control.
                    BeginInvoke((MethodInvoker)Quit);
                    break;
            }
        }
        catch (JsonException ex)
        {
            Store.Log($"Bad message from page: {ex.Message}");
        }
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Page geolocation answered (or failed): name the place, or fall back to the IP address, then tell the page.</summary>
    private async Task FinishLocateAsync(bool ok, double lat, double lon, int code)
    {
        string json;
        if (ok)
        {
            var (name, sub) = await Locator.NameAsync(lat, lon);
            json = JsonSerializer.Serialize(new { ok = true, lat, lon, name, sub });
        }
        else
        {
            Store.Log($"Browser location failed (code {code}); trying the IP address");
            json = await Locator.ByIpAsync() is { } ip
                ? JsonSerializer.Serialize(new { ok = true, lat = ip.Lat, lon = ip.Lon, name = ip.Name, sub = ip.Sub })
                : JsonSerializer.Serialize(new { ok = false, reason = code == 1 ? "denied" : "failed" });   // 1 = permission denied
        }
        Js($"wx.location({json})");
    }

    // ---------- Theme ----------

    /// <summary>Paper colour behind the page and the window frame, from the Appearance setting (or Windows when Auto).</summary>
    private void ApplyThemeColors()
    {
        var paper = theme.EffectiveDark ? Theme.PaperDark : Theme.PaperLight;
        BackColor = paper;
        web.DefaultBackgroundColor = paper;
        if (IsHandleCreated)
            Native.SetDwmInt(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, theme.EffectiveDark ? 1 : 0);
    }

    // ---------- Showing and hiding ----------

    /// <summary>Left-click on the tray icon.</summary>
    public void Toggle(Point at)
    {
        if (Visible) { HidePopup(); return; }
        // Clicking the tray icon while open first deactivates (and hides) the panel; don't bring it straight back.
        if (Environment.TickCount64 - lastHiddenAt < ReopenGuardMs) return;
        ShowPopup(at);
    }

    /// <summary>Opens the panel above the tray. Without a point it opens at the tray corner of the main screen.</summary>
    public void ShowPopup(Point? at = null)
    {
        if (quitting) return;
        if (at.HasValue) { anchor = at.Value; hasAnchor = true; }
        else if (!hasAnchor)
        {
            var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            anchor = new Point(work.Right - 1, work.Bottom - 1);
        }

        theme.Refresh();   // in case a theme change event was missed while hidden
        var wasVisible = Visible;
        Relayout();
        if (!Visible) Show();
        Activate();
        Native.SetForegroundWindow(Handle);
        web.Focus();
        if (ActiveForm != this)
        {
            // Opened from outside (second launch): the focus grant can arrive a moment later.
            BeginInvoke((MethodInvoker)(() =>
            {
                if (!Visible) return;
                Native.SetForegroundWindow(Handle);
                Activate();
                web.Focus();
            }));
        }
        if (ready && !wasVisible) Js("wx.opened()");
    }

    public void HidePopup()
    {
        if (!Visible) return;
        Hide();
        lastHiddenAt = Environment.TickCount64;
        Js("wx.closed()");
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!quitting && !StayOpen) HidePopup();   // click outside = close, like the Mac popover
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // We size the panel ourselves per monitor; stop WinForms from rescaling it a second time.
        e.Cancel = true;
        base.OnDpiChanged(e);
        if (Visible) BeginInvoke((MethodInvoker)Relayout);
    }

    /// <summary>Puts the panel at its right size and place for the monitor it is on, and zooms the page to match.</summary>
    private void Relayout()
    {
        Bounds = PanelLayout.Compute(anchor, PanelDip, GapDip, out fitZoom);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (web.CoreWebView2 == null) return;
        try { if (Math.Abs(web.ZoomFactor - fitZoom) > 0.001) web.ZoomFactor = fitZoom; }
        catch (Exception ex) { Store.Log($"Zoom failed: {ex.Message}"); }
    }

    // ---------- Quit ----------

    /// <summary>Fades the panel's content out, hides it, then exits (the page's Quit button or the tray menu).</summary>
    public async void Quit()
    {
        if (quitting) return;
        quitting = true;
        try
        {
            if (Visible && ready && web.CoreWebView2 != null)
            {
                await web.ExecuteScriptAsync(
                    $"document.documentElement.style.transition='opacity {QuitFadeMs}ms ease';" +
                    "document.documentElement.style.opacity='0';");
                await Task.Delay(QuitFadeMs + 20);
            }
        }
        catch (Exception ex)
        {
            Store.Log($"Quit fade skipped: {ex.Message}");
        }
        Hide();
        onQuit();
    }
}
