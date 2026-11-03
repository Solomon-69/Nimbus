using System.Globalization;
using System.Text.Json;

namespace Nimbus;

/// <summary>
/// "Use my location". Plain WinForms has no Windows.Devices.Geolocation, so the page itself asks WebView2
/// (navigator.geolocation, allowed in PopupForm for our own pages only). That gives the coordinates; the place
/// name comes from a free reverse lookup. If Windows location is switched off, an approximate place from the
/// PC's public IP address is used instead, so "Use my location" still works.
/// </summary>
internal static class Locator
{
    /// <summary>Runs in the page: asks for the position and posts the answer back as {a:'geo'}.</summary>
    public const string AskScript =
        "navigator.geolocation.getCurrentPosition(" +
        "function(p){chrome.webview.postMessage({a:'geo',ok:true,lat:p.coords.latitude,lon:p.coords.longitude});}," +
        "function(e){chrome.webview.postMessage({a:'geo',ok:false,code:e.code});}," +
        "{timeout:8000,maximumAge:600000,enableHighAccuracy:false});";

    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Nimbus/0.2 (+weather tray app)");
        return c;
    }

    /// <summary>Name and region for a point, for the place list ("Paris", "Ile-de-France, France").</summary>
    public static async Task<(string Name, string Sub)> NameAsync(double lat, double lon)
    {
        try
        {
            var url = "https://api.bigdatacloud.net/data/reverse-geocode-client?localityLanguage=en" +
                      $"&latitude={lat.ToString("0.####", CultureInfo.InvariantCulture)}" +
                      $"&longitude={lon.ToString("0.####", CultureInfo.InvariantCulture)}";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var r = doc.RootElement;
            var name = First(r, "city", "locality");
            var sub = string.Join(", ", new[] { First(r, "principalSubdivision"), First(r, "countryName") }.Where(s => s.Length > 0));
            return (name.Length > 0 ? name : "Current location", sub);
        }
        catch (Exception ex)
        {
            Store.Log($"Place name lookup failed: {ex.Message}");
            return ("Current location", "");
        }
    }

    /// <summary>Approximate position from the public IP address (city level), or null when it cannot be had.</summary>
    public static async Task<(double Lat, double Lon, string Name, string Sub)?> ByIpAsync()
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipapi.co/json/"));
            var r = doc.RootElement;
            if (!r.TryGetProperty("latitude", out var la) || !r.TryGetProperty("longitude", out var lo) ||
                la.ValueKind != JsonValueKind.Number || lo.ValueKind != JsonValueKind.Number) return null;
            var name = First(r, "city");
            var sub = string.Join(", ", new[] { First(r, "region"), First(r, "country_name") }.Where(s => s.Length > 0));
            return (la.GetDouble(), lo.GetDouble(), name.Length > 0 ? name : "Current location", sub);
        }
        catch (Exception ex)
        {
            Store.Log($"IP location failed: {ex.Message}");
            return null;
        }
    }

    private static string First(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
            if (obj.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                return v.GetString()!.Trim();
        return "";
    }
}
