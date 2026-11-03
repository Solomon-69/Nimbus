using System.Text.Json;

namespace Nimbus;

/// <summary>
/// Automated Windows test hook, only alive when Nimbus is started with --test (the CI test does that).
/// The test appends command lines to %APPDATA%\Nimbus\test-commands.txt; this takes the file, runs each line in
/// order and appends one answer line per command to %APPDATA%\Nimbus\test-results.txt: "seq command OK|ERR detail".
/// A first "0 ready OK" line says the page has loaded and commands are being read. Commands:
///   panel | close | toggle | state | wake | tick | quit | wait:MS
///   js:CODE        run JavaScript in the page, the answer is its JSON result
///   shot:PATH      PNG of the panel (and a little of what is around it) as it looks on screen
///   screen:PATH    PNG of the whole primary screen
///   icon:PATH      PNG of the current tray icon at 16 to 32 px on a dark and a light taskbar, magnified
/// </summary>
internal sealed class TestHook : IDisposable
{
    private static readonly string CommandsPath = Path.Combine(Store.Folder, "test-commands.txt");
    private static readonly string ResultsPath = Path.Combine(Store.Folder, "test-results.txt");

    // What the page looks like right now, for the "state" command.
    private const string PageState =
        "JSON.stringify({sheet:window.WxUI?WxUI.state().sheet:null,theme:document.documentElement.getAttribute('data-theme')," +
        "places:WxEngine.S.places.length,loaded:Object.keys(WxEngine.S.models).length,offline:WxEngine.R.offline," +
        "unit:WxEngine.S.settings.unit,speed:WxEngine.S.settings.speed,sky:WxEngine.S.settings.sky," +
        "w:innerWidth,h:innerHeight,hero:(document.querySelector('.hero')||{}).innerText||null})";

    private readonly PopupForm popup;
    private readonly Func<Rectangle?> trayBounds;
    private readonly Func<BarInfo> bar;
    private readonly Func<bool> trayVisible;
    private readonly Action showPanel, quit;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private int seq;
    private bool busy;

    public TestHook(PopupForm popup, Func<Rectangle?> trayBounds, Func<BarInfo> bar, Func<bool> trayVisible, Action showPanel, Action quit)
    {
        this.popup = popup;
        this.trayBounds = trayBounds;
        this.bar = bar;
        this.trayVisible = trayVisible;
        this.showPanel = showPanel;
        this.quit = quit;
    }

    public void Start()
    {
        try
        {
            Directory.CreateDirectory(Store.Folder);
            File.Delete(CommandsPath);
            File.WriteAllText(ResultsPath, "");
        }
        catch (Exception ex) { Store.Log($"Test hook could not reset its files: {ex.Message}"); }
        Answer("ready", true, "");
        timer.Tick += async (_, _) => await PollAsync();
        timer.Start();
    }

    private async Task PollAsync()
    {
        if (busy || !File.Exists(CommandsPath)) return;
        string text;
        try
        {
            using var stream = new FileStream(CommandsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            text = await new StreamReader(stream).ReadToEndAsync();
        }
        catch (IOException) { return; }   // the test is still writing; look again in a moment
        if (!text.EndsWith('\n')) return;   // a half-written line
        try { File.Delete(CommandsPath); } catch (IOException) { return; }

        busy = true;
        try
        {
            foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length > 0 && !line.StartsWith('#')) await RunAsync(line);
            }
        }
        finally { busy = false; }
    }

    private async Task RunAsync(string line)
    {
        var colon = line.IndexOf(':');
        var name = colon < 0 ? line : line[..colon];
        var arg = colon < 0 ? "" : line[(colon + 1)..];
        try
        {
            switch (name)
            {
                case "panel": showPanel(); Answer(name, true, ""); break;
                case "close": popup.HidePopup(); Answer(name, true, ""); break;
                case "toggle": if (popup.Visible) popup.HidePopup(); else showPanel(); Answer(name, true, ""); break;
                case "wake": popup.Js("wx.wake()"); Answer(name, true, ""); break;
                case "tick": popup.Js("wx.tick()"); Answer(name, true, ""); break;
                case "wait": await Task.Delay(Math.Clamp(int.TryParse(arg, out var ms) ? ms : 500, 0, 60_000)); Answer(name, true, ""); break;
                case "quit": Answer(name, true, ""); quit(); break;
                case "js": Answer(name, true, await popup.Eval(arg)); break;
                case "shot": Answer(name, SaveShot(arg), arg); break;
                case "screen": Answer(name, SaveScreen(arg), arg); break;
                case "icon":
                    using (var sheet = WeatherGlyph.Sheet(bar())) sheet.Save(arg, System.Drawing.Imaging.ImageFormat.Png);
                    Answer(name, true, arg);
                    break;
                case "state": Answer(name, true, await StateAsync()); break;
                default: Answer(name, false, "unknown command"); break;
            }
        }
        catch (Exception ex)
        {
            Store.Log($"Test command '{line}' failed: {ex.Message}");
            Answer(name, false, ex.Message);
        }
    }

    private async Task<string> StateAsync()
    {
        var page = await popup.Eval(popup.IsReady ? PageState : "null");
        var b = popup.Bounds;
        var t = trayBounds();
        var info = bar();
        return JsonSerializer.Serialize(new
        {
            visible = popup.Visible,
            ready = popup.IsReady,
            bounds = new[] { b.X, b.Y, b.Width, b.Height },
            screen = new[] { Screen.PrimaryScreen!.Bounds.Width, Screen.PrimaryScreen.Bounds.Height },
            tray = t is { } r ? new[] { r.X, r.Y, r.Width, r.Height } : null,
            trayVisible = trayVisible(),
            bar = new { info.Text, info.Kind, info.Style, info.Empty },
            page = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Deserialize<string>(page) ?? "null"),
        });
    }

    /// <summary>The panel as it is on screen, with a 16 px margin so the taskbar side and window edge show.</summary>
    private bool SaveShot(string path)
    {
        if (!popup.Visible) return false;
        var b = popup.Bounds;
        return Capture(path, Rectangle.Inflate(b, 16, 16));
    }

    private static bool SaveScreen(string path) => Capture(path, SystemInformation.VirtualScreen);

    private static bool Capture(string path, Rectangle area)
    {
        area.Intersect(SystemInformation.VirtualScreen);
        if (area.Width <= 0 || area.Height <= 0) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var bmp = new Bitmap(area.Width, area.Height);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return true;
    }

    private void Answer(string command, bool ok, string detail)
    {
        var line = $"{(command == "ready" ? 0 : ++seq)} {command} {(ok ? "OK" : "ERR")} {detail.Replace('\r', ' ').Replace('\n', ' ')}";
        try { File.AppendAllText(ResultsPath, line + Environment.NewLine); }
        catch (Exception ex) { Store.Log($"Test hook could not write an answer: {ex.Message}"); }
    }

    public void Dispose() => timer.Dispose();
}
