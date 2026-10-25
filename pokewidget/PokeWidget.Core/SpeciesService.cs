using System.Collections.Concurrent;
using System.Text.Json;

namespace PokeWidget.Core;

/// <summary>Nom (FR de préférence) et taux de capture d'une espèce, avec cache persistant (species.json).</summary>
public sealed class SpeciesService(HttpClient http, GameRepository repo)
{
    private readonly ConcurrentDictionary<int, Lazy<Task<SpeciesInfo>>> _inflight = new();

    public async Task<SpeciesInfo> GetAsync(int id)
    {
        lock (repo.SyncRoot) { if (repo.Species.TryGetValue(id, out var cached)) return cached; }
        var lazy = _inflight.GetOrAdd(id, i => new Lazy<Task<SpeciesInfo>>(() => FetchAsync(i)));
        try { return await lazy.Value; }
        finally { _inflight.TryRemove(id, out _); }
    }

    private async Task<SpeciesInfo> FetchAsync(int id)
    {
        using var resp = await http.GetAsync($"https://pokeapi.co/api/v2/pokemon-species/{id}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var info = Parse(doc.RootElement);
        lock (repo.SyncRoot) { repo.Species[id] = info; repo.SaveSpecies(); }
        return info;
    }

    public static SpeciesInfo Parse(JsonElement root)
    {
        if (!root.TryGetProperty("capture_rate", out var cr) || !cr.TryGetInt32(out int rate) || rate < 1 || rate > 255)
            throw new InvalidDataException("capture_rate invalide");

        string? fr = null, en = null;
        if (root.TryGetProperty("names", out var names) && names.ValueKind == JsonValueKind.Array)
            foreach (var n in names.EnumerateArray())
            {
                string? lang = n.TryGetProperty("language", out var l) && l.TryGetProperty("name", out var ln) ? ln.GetString() : null;
                string? name = n.TryGetProperty("name", out var nn) ? nn.GetString() : null;
                if (lang == "fr") fr = name; else if (lang == "en") en = name;
            }
        string fallback = root.TryGetProperty("name", out var raw) ? raw.GetString() ?? "?" : "?";
        return new SpeciesInfo(fr ?? en ?? fallback, rate);
    }
}
