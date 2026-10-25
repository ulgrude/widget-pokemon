using System.Globalization;
using System.Text.Json;

namespace PokeWidget.Core;

public static class ReleaseVersion
{
    /// <summary>"v1.4.0", "1.4", "2.0.0-beta+abc" → Version à 4 composantes (les suffixes sont ignorés).</summary>
    public static bool TryParse(string? s, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s[0] is 'v' or 'V') s = s[1..];
        int cut = s.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) s = s[..cut];

        var parts = s.Split('.');
        if (parts.Length > 4) return false;
        var n = new int[4];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return false;
        version = new Version(n[0], n[1], n[2], n[3]);
        return true;
    }

    public static bool IsNewer(string? latest, string? installed) =>
        TryParse(latest, out var a) && TryParse(installed, out var b) && a > b;

    public static string Format(Version v) => v.Revision > 0 ? v.ToString(4) : v.ToString(3);
}

public sealed record UpdateStatus(bool Available, string? LatestVersion, string? Url);

/// <summary>
/// Vérification des mises à jour via la dernière Release GitHub (les brouillons et pré-versions sont ignorés par l'API).
/// Au plus une vérification automatique par jour ; en cas d'échec, nouvel essai automatique dans 1 h.
/// </summary>
public sealed class UpdateService
{
    public const string DefaultRepo = "ulgrude/widget-pokemon";
    private const long Day = 24L * 3600 * 1000, Hour = 3600L * 1000;

    private readonly HttpClient _http;
    private readonly GameRepository _repo;
    private readonly Func<long> _now;
    private readonly string _slug;

    public UpdateService(HttpClient http, GameRepository repo, Func<long>? nowMs = null, string repoSlug = DefaultRepo)
    {
        _http = http; _repo = repo; _now = nowMs ?? GameConfig.NowMs; _slug = repoSlug;
    }

    public string ReleasesPageUrl => $"https://github.com/{_slug}/releases/latest";

    public bool ShouldAutoCheck() =>
        _repo.Settings.AutoCheckUpdates && _now() - _repo.Settings.LastUpdateCheckMs >= Day;

    /// <summary>État connu sans réseau (dernière vérification mémorisée). Une version déjà installée n'est plus signalée.</summary>
    public UpdateStatus Known(string installed)
    {
        var s = _repo.Settings;
        bool available = ReleaseVersion.IsNewer(s.LatestKnownVersion, installed);
        return new UpdateStatus(available, s.LatestKnownVersion, available ? SafeUrl(s.LatestReleaseUrl) ?? ReleasesPageUrl : null);
    }

    /// <summary>
    /// Interroge GitHub. force = vérification manuelle (ignore le délai d'un jour).
    /// Lève HttpRequestException / InvalidDataException / JsonException en cas d'échec.
    /// </summary>
    public async Task<UpdateStatus> CheckAsync(string installed, bool force)
    {
        if (!force && !ShouldAutoCheck()) return Known(installed);

        var s = _repo.Settings;
        long now = _now(), previous = s.LastUpdateCheckMs;
        s.LastUpdateCheckMs = now;                 // "réservé" tout de suite
        _repo.SaveSettings();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_slug}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await _http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidDataException("aucune version publiée sur GitHub");
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            string? tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (!ReleaseVersion.TryParse(tag, out var v)) throw new InvalidDataException("numéro de version invalide dans la release");
            string? url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;

            s.LatestKnownVersion = ReleaseVersion.Format(v);
            s.LatestReleaseUrl = SafeUrl(url) ?? ReleasesPageUrl;
            _repo.SaveSettings();
            return Known(installed);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            s.LastUpdateCheckMs = force ? previous : now - Day + Hour; // auto : nouvel essai dans 1 h
            _repo.SaveSettings();
            throw;
        }
    }

    /// <summary>On n'ouvre que des liens https://github.com/... (jamais une URL arbitraire venant d'une réponse réseau).</summary>
    public static string? SafeUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com" ? u.AbsoluteUri : null;

    public static string Describe(Exception e) => e switch
    {
        InvalidDataException => e.Message,
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests } => "limite de requêtes GitHub atteinte, réessaie plus tard",
        HttpRequestException { StatusCode: { } code } => $"erreur HTTP {(int)code}",
        TaskCanceledException => "GitHub ne répond pas",
        JsonException => "réponse de GitHub illisible",
        _ => "GitHub injoignable",
    };
}
