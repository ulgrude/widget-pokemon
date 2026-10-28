using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PokeWidget.Core;

public sealed record ImportedSettings(string Seed, WidgetPosition Position);
public sealed record ImportedProgress(GameState State, ImportedSettings? Settings);

/// <summary>Export/import de la progression, compatible avec le fichier .json du userscript.</summary>
public static class ProgressFile
{
    private sealed record ExportConfig(string Seed, string Position, string[] Urls);
    private sealed record ExportFile(string App, int Version, string ExportedAt, GameState State, ExportConfig Config);

    public static string Export(GameState state, SettingsModel settings, DateTimeOffset now) =>
        JsonSerializer.Serialize(new ExportFile(
            "poke-widget", 1,
            now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            state,
            new ExportConfig(settings.Seed, PositionKeys.ToKey(settings.Position), Array.Empty<string>())), Json.Options);

    /// <summary>Accepter aussi les anciens exports .json en clair (userscript). À mettre à false pour fermer cette voie d'édition manuelle.</summary>
    public static readonly bool AllowLegacyJsonImport = false;

    /// <summary>Export obfusqué (.pokesave).</summary>
    public static byte[] ExportProtected(GameState state, SettingsModel settings, DateTimeOffset now) =>
        SaveObfuscator.Protect(Export(state, settings, now));

    /// <summary>Importe un fichier .pokesave (obfusqué, vérifié) ou, si autorisé, un ancien export .json.</summary>
    public static ImportedProgress ImportBytes(byte[] data, long nowMs)
    {
        if (data.Length > GameConfig.MaxImportChars) throw new InvalidDataException("fichier trop volumineux");
        if (SaveObfuscator.HasMagic(data)) return Import(SaveObfuscator.Unprotect(data), nowMs);
        if (!AllowLegacyJsonImport) throw new InvalidDataException("format de sauvegarde non reconnu");
        return Import(Encoding.UTF8.GetString(data).TrimStart('\uFEFF'), nowMs);
    }

    /// <summary>On ne fait jamais confiance au fichier importé : tout est borné et validé.</summary>
    public static ImportedProgress Import(string json, long nowMs)
    {
        if (json.Length > GameConfig.MaxImportChars) throw new InvalidDataException("fichier trop volumineux");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("fichier non reconnu");

        var raw = root.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.Object ? st : root;
        if (!raw.TryGetProperty("dex", out _) && !raw.TryGetProperty("balls", out _))
            throw new InvalidDataException("fichier non reconnu");

        var s = GameState.Fresh(nowMs);
        if (raw.TryGetProperty("playerId", out var playerId) && playerId.ValueKind == JsonValueKind.String)
            s.PlayerId = playerId.GetString() ?? "";
        s.EnsurePlayerId();

        if (raw.TryGetProperty("shop", out var sh) && sh.ValueKind == JsonValueKind.Object)
        {
            s.Shop = new ShopState
            {
                BallCapUpgrades = Int(sh, "ballCapUpgrades", 0, 5, 0),
                MissCatchRateUpgrades = Int(sh, "missCatchRateUpgrades", 0, 1, 0),
                ShinyBoostUpgrades = Int(sh, "shinyBoostUpgrades", 0, 1, 0),
                SpentDoublons = Int(sh, "spentDoublons", 0, 1_000_000, 0),
                SpentShinyDoublons = Int(sh, "spentShinyDoublons", 0, 1_000_000, 0),
            };
        }

        var b = raw.TryGetProperty("balls", out var be) ? be : default;
        int maxCap = s.MaxBallCap;
        s.Balls = new BallStock
        {
            Poke = Int(b, "poke", 0, maxCap, 0),
            Super = Int(b, "super", 0, maxCap, 0),
            Hyper = Int(b, "hyper", 0, maxCap, 0),
        };
        if (Num(raw, "lastTick") is double lt) s.LastTick = Math.Min((long)lt, nowMs);
        if (Num(raw, "lastCaptureSlot") is double lc) s.LastCaptureSlot = (long)lc;
        if (Num(raw, "seenSlot") is double ss) s.SeenSlot = (long)ss;

        if (raw.TryGetProperty("dex", out var dex) && dex.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in dex.EnumerateObject())
            {
                if (!int.TryParse(p.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                    || id < 1 || id > GameConfig.MaxPokemonId || p.Value.ValueKind != JsonValueKind.Object) continue;
                int count = Int(p.Value, "count", 1, 1_000_000, 1);
                string name = p.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } ns
                    ? (ns.Length > 60 ? ns[..60] : ns) : "#" + id;
                s.Dex[id] = new DexEntry
                {
                    Name = name,
                    Rate = Int(p.Value, "rate", 1, 255, 45),
                    Count = count,
                    Shiny = Int(p.Value, "shiny", 0, count, 0),
                    Mystery = Int(p.Value, "mystery", 0, count, 0),
                };
            }
        }

        ImportedSettings? cfg = null;
        if (root.TryGetProperty("config", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            string? seed = c.TryGetProperty("seed", out var se) && se.ValueKind == JsonValueKind.String ? se.GetString() : null;
            PositionKeys.TryParse(c.TryGetProperty("position", out var po) && po.ValueKind == JsonValueKind.String ? po.GetString() : null, out var pos);
            cfg = new ImportedSettings(SettingsModel.NormalizeSeed(seed), pos);
        }
        return new ImportedProgress(s, cfg);
    }

    private static double? Num(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

    private static int Int(JsonElement obj, string name, int min, int max, int def) =>
        Num(obj, name) is double d ? (int)Math.Clamp(Math.Floor(d), min, max) : def;
}
