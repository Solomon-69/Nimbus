using Microsoft.Win32;

namespace Nimbus;

/// <summary>Windows light/dark: reads AppsUseLightTheme and raises Changed when the user flips it.</summary>
internal sealed class Theme : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // Nimbus's own paper colours (web/style.css --bg), used behind the page so there is never a white flash.
    public static readonly Color PaperLight = Color.FromArgb(0xF4, 0xEF, 0xE6);
    public static readonly Color PaperDark = Color.FromArgb(0x1B, 0x18, 0x14);

    public event Action<bool>? SystemDarkChanged;

    /// <summary>"auto", "light" or "dark", as last chosen in Nimbus settings (arrives with every bar message).</summary>
    public string Mode { get; set; } = "auto";

    public bool SystemDark { get; private set; }

    /// <summary>Windows' taskbar colour (SystemUsesLightTheme), which the tray icon follows.</summary>
    public bool TaskbarDark { get; private set; }

    /// <summary>Raised when the taskbar colour flips (the tray icon redraws).</summary>
    public event Action? TaskbarChanged;

    public bool EffectiveDark => Mode switch
    {
        "dark" => true,
        "light" => false,
        _ => SystemDark,
    };

    public Theme()
    {
        SystemDark = ReadSystemDark();
        TaskbarDark = ReadTaskbarDark();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private async void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Theme switches arrive as General (and sometimes Color) preference changes.
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;
        Refresh();
        // The registry value can lag the event a little; look again shortly after.
        await Task.Delay(300);
        Refresh();
    }

    /// <summary>Re-reads the Windows setting and raises the change events if something flipped.</summary>
    public void Refresh()
    {
        var taskbar = ReadTaskbarDark();
        if (taskbar != TaskbarDark)
        {
            TaskbarDark = taskbar;
            TaskbarChanged?.Invoke();
        }
        var dark = ReadSystemDark();
        if (dark == SystemDark) return;
        SystemDark = dark;
        SystemDarkChanged?.Invoke(dark);
    }

    private static bool ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool ReadTaskbarDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is not int light || light == 0;
        }
        catch
        {
            return true;
        }
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}

/// <summary>Tray menu colours that follow Windows light/dark (WinForms menus are always light otherwise).</summary>
internal static class MenuStyle
{
    private static readonly MenuColors LightColors = new(false), DarkColors = new(true);
    private static readonly ToolStripProfessionalRenderer LightRenderer = new(LightColors) { RoundedEdges = true };
    private static readonly ToolStripProfessionalRenderer DarkRenderer = new(DarkColors) { RoundedEdges = true };

    public static void Apply(ContextMenuStrip menu, bool dark)
    {
        var colors = dark ? DarkColors : LightColors;
        menu.Renderer = dark ? DarkRenderer : LightRenderer;
        menu.BackColor = colors.Back;
        foreach (ToolStripItem item in menu.Items) item.ForeColor = colors.Text;
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public Color Back { get; }
        public Color Text { get; }
        private readonly Color hover;
        private readonly Color line;

        public MenuColors(bool dark)
        {
            UseSystemColors = false;
            Back = dark ? Color.FromArgb(0x2B, 0x27, 0x22) : Color.FromArgb(0xFB, 0xF8, 0xF3);
            Text = dark ? Color.FromArgb(0xF3, 0xED, 0xE3) : Color.FromArgb(0x20, 0x1C, 0x17);
            hover = dark ? Color.FromArgb(0x3A, 0x34, 0x2D) : Color.FromArgb(0xEE, 0xE6, 0xDA);
            line = dark ? Color.FromArgb(0x45, 0x3E, 0x36) : Color.FromArgb(0xDD, 0xD3, 0xC6);
        }

        public override Color ToolStripDropDownBackground => Back;
        public override Color MenuBorder => line;
        public override Color MenuItemBorder => hover;
        public override Color MenuItemSelected => hover;
        public override Color MenuItemSelectedGradientBegin => hover;
        public override Color MenuItemSelectedGradientEnd => hover;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color SeparatorDark => line;
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => hover;
        public override Color CheckSelectedBackground => hover;
        public override Color CheckPressedBackground => hover;
    }
}
