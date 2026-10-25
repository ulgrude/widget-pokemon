using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PokeWidget.Core;

public enum SpriteKind { Animated, Pixel, Artwork }
public sealed record SpriteResult(string Path, SpriteKind Kind);

public static class SpriteUrls
{
    public const string Base = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/";
    private static string S(bool shiny) => shiny ? "shiny/" : "";

    public static string Animated(int id, bool shiny) => $"{Base}versions/generation-v/black-white/animated/{S(shiny)}{id}.gif";
    public static string Pixel(int id, bool shiny) => $"{Base}{S(shiny)}{id}.png";
    public static string Artwork(int id, bool shiny) => $"{Base}other/official-artwork/{S(shiny)}{id}.png";

    /// <summary>Ordre d'essai : GIF animé (gen. 5), PNG, artwork officiel. Index = SpriteKind.</summary>
    public static string[] Chain(int id, bool shiny) => [Animated(id, shiny), Pixel(id, shiny), Artwork(id, shiny)];
}

/// <summary>
/// Cache de sprites sur disque (jetable : on peut supprimer le dossier sans rien perdre).
/// - Les 404 sont mémorisés (misses.json) pour ne pas être redemandés pendant 30 jours.
/// - Les autres erreurs (réseau, 5xx) ne sont PAS mémorisées : on réessaiera.
/// - Éviction du plus ancien quand le budget disque est dépassé.
/// </summary>
public sealed class SpriteCache
{
    private const int MaxFileBytes = 5 * 1024 * 1024;
    private const string MissesFile = "misses.json";

    private readonly HttpClient _http;
    private readonly string _dir;
    private readonly Func<long> _now;
    private readonly long _budgetBytes, _missTtlMs;
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _misses;
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inflight = new();

    public SpriteCache(HttpClient http, string dir, Func<long>? nowMs = null,
        long budgetBytes = 64L * 1024 * 1024, long missTtlMs = 30L * 24 * 3600 * 1000)
    {
        _http = http; _dir = dir; _now = nowMs ?? GameConfig.NowMs; _budgetBytes = budgetBytes; _missTtlMs = missTtlMs;
        _misses = JsonFile.Load(Path.Combine(dir, MissesFile), () => new Dictionary<string, long>());
    }

    /// <summary>Premier sprite disponible de la chaîne (animé, puis PNG, puis artwork), ou null.</summary>
    public async Task<SpriteResult?> GetAsync(int id, bool shiny)
    {
        var chain = SpriteUrls.Chain(id, shiny);
        for (int i = 0; i < chain.Length; i++)
            if (await GetFileAsync(chain[i]) is { } path) return new SpriteResult(path, (SpriteKind)i);
        return null;
    }

    /// <summary>Chemin local du fichier (téléchargé si besoin), ou null s'il n'existe pas / est injoignable.</summary>
    public async Task<string?> GetFileAsync(string url)
    {
        string key = KeyFor(url);
        string path = Path.Combine(_dir, key);

        if (File.Exists(path)) { Touch(path); return path; }

        lock (_lock)
        {
            if (_misses.TryGetValue(key, out long t))
            {
                if (_now() - t < _missTtlMs) return null;
                _misses.Remove(key); SaveMisses();
            }
        }

        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<string?>>(() => DownloadAsync(url, key, path)));
        try { return await lazy.Value; }
        finally { _inflight.TryRemove(key, out _); }
    }

    private async Task<string?> DownloadAsync(string url, string key, string path)
    {
        try
        {
            using var resp = await _http.GetAsync(url);
            if (resp.StatusCode == HttpStatusCode.NotFound) { RecordMiss(key); return null; }
            resp.EnsureSuccessStatusCode();
            byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0 || bytes.Length > MaxFileBytes) return null;

            Directory.CreateDirectory(_dir);
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, path, true);
            Touch(path);
            Evict(path);
            return path;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void RecordMiss(string key) { lock (_lock) { _misses[key] = _now(); SaveMisses(); } }
    private void SaveMisses() { try { JsonFile.SaveAtomic(Path.Combine(_dir, MissesFile), _misses); } catch (IOException) { } }
    private void Touch(string path) { try { File.SetLastWriteTimeUtc(path, DateTimeOffset.FromUnixTimeMilliseconds(_now()).UtcDateTime); } catch (IOException) { } }

    private void Evict(string keep)
    {
        try
        {
            var files = Directory.EnumerateFiles(_dir)
                .Where(f => Path.GetFileName(f) != MissesFile && !f.EndsWith(".tmp"))
                .Select(f => new FileInfo(f)).ToList();
            long total = files.Sum(f => f.Length);
            foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (total <= _budgetBytes) break;
                if (f.FullName == keep) continue;
                total -= f.Length;
                f.Delete();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* non bloquant */ }
    }

    private static string KeyFor(string url)
    {
        if (url.StartsWith(SpriteUrls.Base)) return url[SpriteUrls.Base.Length..].Replace('/', '_');
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))) + Path.GetExtension(new Uri(url).AbsolutePath);
    }
}
