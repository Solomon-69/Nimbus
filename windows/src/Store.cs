using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

namespace Nimbus;

/// <summary>
/// Everything Nimbus keeps on disk, all under %APPDATA%\Nimbus: state.json (places and settings, written by the
/// page) and Nimbus.log. "Open at login" lives in the HKCU Run key, like any per-user startup app.
/// </summary>
internal static class Store
{
    public static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nimbus");
    private static readonly string StatePath = Path.Combine(Folder, "state.json");
    private const int MaxStateBytes = 1_000_000;   // places and settings are a few KB; anything huge is not ours

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Nimbus";

    // ---- Saved state ----

    /// <summary>The saved state exactly as the page wrote it (a JSON text), or null when there is none or it is unreadable.</summary>
    public static string? LoadState()
    {
        try
        {
            if (!File.Exists(StatePath) || new FileInfo(StatePath).Length > MaxStateBytes) return null;
            var text = File.ReadAllText(StatePath);
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? text : null;
        }
        catch (Exception ex)
        {
            Log($"state.json could not be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Writes the state atomically (temp file, then replace) so a crash never leaves half a file.</summary>
    public static void SaveState(string json)
    {
        if (json.Length > MaxStateBytes) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            Directory.CreateDirectory(Folder);
            var temp = StatePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, StatePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log($"Could not write state.json: {ex.Message}");
        }
    }

    // ---- Units follow the PC's region until the user picks their own (same rule as the Mac app) ----

    public static (bool Metric, bool Mph) RegionUnits()
    {
        try
        {
            var region = new RegionInfo(CultureInfo.CurrentCulture.Name);
            var metric = region.IsMetric;
            var mph = !metric || region.TwoLetterISORegionName == "GB";   // US and UK road signs are in miles
            return (metric, mph);
        }
        catch
        {
            return (true, false);
        }
    }

    // ---- Open at login ----

    public static bool LoginEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        catch (Exception ex)
        {
            Log($"Run key could not be read: {ex.Message}");
            return false;
        }
    }

    public static void SetLogin(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (on) key.SetValue(RunValue, $"\"{ExePath()}\"", RegistryValueKind.String);
            else if (key.GetValue(RunValue) != null) key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log($"Open at login change failed: {ex.Message}");
        }
    }

    /// <summary>Keeps the Run entry pointing at this exe after the app folder is moved.</summary>
    public static void RefreshLoginPath()
    {
        if (LoginEnabled()) SetLogin(true);
    }

    private static string ExePath() => Environment.ProcessPath ?? Application.ExecutablePath;

    // ---- Log ----

    /// <summary>Small local log at %APPDATA%\Nimbus\Nimbus.log for troubleshooting. Nothing leaves the PC.</summary>
    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, "Nimbus.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512_000) File.Move(path, path + ".old", overwrite: true);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
