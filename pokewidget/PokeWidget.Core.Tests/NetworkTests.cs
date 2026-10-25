using System.Net;
using PokeWidget.Core;
using Xunit;

namespace PokeWidget.Core.Tests;

internal sealed class StubHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        string url = r.RequestUri!.ToString();
        lock (Requests) Requests.Add(url);
        return Task.FromResult(respond(url));
    }
}

public class SpeciesServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pokewidget-net-" + Guid.NewGuid());
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private const string PikachuJson = """
        {"name":"pikachu","capture_rate":190,"names":[
          {"name":"Pikachu","language":{"name":"en"}},{"name":"Pikachu FR","language":{"name":"fr"}}]}
        """;

    private GameRepository NewRepo() => new(new AppPaths(_dir, Path.Combine(_dir, "sprites")), 0);

    [Fact]
    public async Task Prefers_french_name_caches_and_persists()
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PikachuJson) });
        var repo = NewRepo();
        var svc = new SpeciesService(new HttpClient(h), repo);

        var a = await svc.GetAsync(25);
        var b = await svc.GetAsync(25);
        Assert.Equal(new SpeciesInfo("Pikachu FR", 190), a);
        Assert.Equal(a, b);
        Assert.Single(h.Requests);
        Assert.EndsWith("/pokemon-species/25", h.Requests[0]);
        Assert.Equal(190, NewRepo().Species[25].Rate); // relu depuis species.json
    }

    [Fact]
    public void Falls_back_to_english_then_raw_name()
    {
        var en = SpeciesService.Parse(System.Text.Json.JsonDocument.Parse("""{"name":"x","capture_rate":45,"names":[{"name":"Eng","language":{"name":"en"}}]}""").RootElement);
        var raw = SpeciesService.Parse(System.Text.Json.JsonDocument.Parse("""{"name":"mew","capture_rate":45,"names":[]}""").RootElement);
        Assert.Equal("Eng", en.Name);
        Assert.Equal("mew", raw.Name);
    }

    [Fact]
    public async Task Http_error_throws_and_is_not_cached()
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var svc = new SpeciesService(new HttpClient(h), NewRepo());
        await Assert.ThrowsAsync<HttpRequestException>(() => svc.GetAsync(25));
        await Assert.ThrowsAsync<HttpRequestException>(() => svc.GetAsync(25));
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task Invalid_capture_rate_is_rejected()
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"name":"x","capture_rate":9999,"names":[]}""") });
        await Assert.ThrowsAsync<InvalidDataException>(() => new SpeciesService(new HttpClient(h), NewRepo()).GetAsync(1));
    }
}

public class SpriteCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pokewidget-spr-" + Guid.NewGuid());
    private long _now = 1_000_000;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static HttpResponseMessage Ok(int size = 3) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[size].Select(_ => (byte)7).ToArray()) };

    private SpriteCache NewCache(StubHandler h, long budget = 64L * 1024 * 1024) =>
        new(new HttpClient(h), _dir, () => _now, budget);

    [Fact]
    public async Task Falls_back_along_the_chain_and_remembers_404s()
    {
        var h = new StubHandler(u => u.EndsWith("/animated/25.gif") ? new(HttpStatusCode.NotFound) : u.EndsWith("/pokemon/25.png") ? Ok() : new(HttpStatusCode.NotFound));
        var cache = NewCache(h);

        var r = await cache.GetAsync(25, false);
        Assert.NotNull(r);
        Assert.Equal(SpriteKind.Pixel, r!.Kind);
        Assert.True(File.Exists(r.Path));
        Assert.Equal(2, h.Requests.Count);          // gif (404) puis png

        await cache.GetAsync(25, false);
        Assert.Equal(2, h.Requests.Count);           // 404 mémorisé + fichier en cache : aucune requête

        var fresh = NewCache(h);                     // les 404 survivent à un redémarrage
        await fresh.GetAsync(25, false);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task Memorized_404_expires_after_30_days()
    {
        var h = new StubHandler(u => u.EndsWith("/animated/25.gif") ? new(HttpStatusCode.NotFound) : Ok());
        var cache = NewCache(h);
        await cache.GetAsync(25, false);
        Assert.Single(h.Requests, u => u.EndsWith("animated/25.gif"));

        _now += 30L * 24 * 3600 * 1000 + 1;
        await cache.GetAsync(25, false);
        Assert.Equal(2, h.Requests.Count(u => u.EndsWith("animated/25.gif")));
    }

    [Fact]
    public async Task Server_errors_are_not_memorized()
    {
        var h = new StubHandler(u => u.EndsWith("/animated/25.gif") ? new(HttpStatusCode.InternalServerError) : Ok());
        var cache = NewCache(h);
        Assert.Equal(SpriteKind.Pixel, (await cache.GetAsync(25, false))!.Kind);
        Assert.False(File.Exists(Path.Combine(_dir, "misses.json")));
        await NewCache(h).GetAsync(25, false);
        Assert.Equal(2, h.Requests.Count(u => u.EndsWith("animated/25.gif")));
    }

    [Fact]
    public async Task Returns_null_when_nothing_exists()
    {
        var h = new StubHandler(_ => new(HttpStatusCode.NotFound));
        Assert.Null(await NewCache(h).GetAsync(9999, true));
        Assert.Equal(3, h.Requests.Count);
    }

    [Fact]
    public async Task Evicts_the_oldest_files_over_budget()
    {
        var h = new StubHandler(_ => Ok(100));
        var cache = NewCache(h, budget: 250);
        string?[] p = new string?[3];
        for (int i = 0; i < 3; i++) { _now += 1000; p[i] = await cache.GetFileAsync(SpriteUrls.Pixel(i + 1, false)); }

        Assert.False(File.Exists(p[0]));  // le plus ancien est supprimé
        Assert.True(File.Exists(p[1]));
        Assert.True(File.Exists(p[2]));
    }

    [Fact]
    public async Task Parallel_requests_for_the_same_sprite_download_once()
    {
        var h = new StubHandler(_ => Ok());
        var cache = NewCache(h);
        var url = SpriteUrls.Pixel(7, false);
        var paths = await Task.WhenAll(cache.GetFileAsync(url), cache.GetFileAsync(url), cache.GetFileAsync(url));
        Assert.All(paths, p => Assert.NotNull(p));
        Assert.Single(h.Requests);
    }
}
