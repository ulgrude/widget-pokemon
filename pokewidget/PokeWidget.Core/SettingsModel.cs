using System.Text.Json.Serialization;

namespace PokeWidget.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WidgetPosition { TopLeft, TopRight, BottomLeft, BottomRight }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DisplayMode { Desktop, Topmost }

public static class PositionKeys
{
    private static readonly (WidgetPosition P, string Key)[] Map =
    {
        (WidgetPosition.TopLeft, "top-left"), (WidgetPosition.TopRight, "top-right"),
        (WidgetPosition.BottomLeft, "bottom-left"), (WidgetPosition.BottomRight, "bottom-right"),
    };

    public static string ToKey(WidgetPosition p) => Map.First(m => m.P == p).Key;

    public static bool TryParse(string? key, out WidgetPosition p)
    {
        foreach (var m in Map) if (m.Key == key) { p = m.P; return true; }
        p = WidgetPosition.BottomRight; return false;
    }
}

public sealed class SettingsModel
{
    public string Seed { get; set; } = GameConfig.DefaultSeed;
    public WidgetPosition Position { get; set; } = WidgetPosition.BottomRight;
    public DisplayMode Mode { get; set; } = DisplayMode.Desktop;
    /// <summary>Widget réduit à une simple Poké Ball.</summary>
    public bool Collapsed { get; set; }
    public bool RunAtStartup { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public long LastUpdateCheckMs { get; set; }
    public string? LatestKnownVersion { get; set; }
    public string? LatestReleaseUrl { get; set; }
    /// <summary>Dernière version pour laquelle une notification a déjà été affichée (une seule fois par version).</summary>
    public string? LastNotifiedVersion { get; set; }

    public static string NormalizeSeed(string? s)
    {
        s = s?.Trim() ?? "";
        if (s.Length == 0) return GameConfig.DefaultSeed;
        return s.Length > 100 ? s[..100] : s;
    }
}
