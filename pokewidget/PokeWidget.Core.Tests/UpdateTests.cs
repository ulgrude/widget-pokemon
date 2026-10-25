using System.Net;
using PokeWidget.Core;
using Xunit;

namespace PokeWidget.Core.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.4.0", "1.3.9", true)]
    [InlineData("1.4.0", "1.4.0", false)]
    [InlineData("1.3.9", "1.4.0", false)]
    [InlineData("1.10.0", "1.9.0", true)]        // comparaison numérique, pas alphabétique
    [InlineData("1.4", "1.4.0", false)]
    [InlineData("1.4.1", "1.4", true)]
    [InlineData("v2.0.0-beta.1", "1.9.9", true)] // suffixes ignorés
    [InlineData("1.0.0+abc123", "1.0.0", false)]
    [InlineData("abc", "1.0.0", false)]
    [InlineData("1.0.0", "garbage", false)]
    [InlineData("1.2.3.4.5", "1.0.0", false)]
    [InlineData("-1.0.0", "0.0.1", false)]
    public void IsNewer(string latest, string installed, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(latest, installed));

    [Fact]
    public void Format_drops_a_zero_revision()
    {
        ReleaseVersion.TryParse("v1.4", out var v);
        Assert.Equal("1.4.0", ReleaseVersion.Format(v));
        ReleaseVersion.TryParse("1.4.0.2", out v);
        Assert.Equal("1.4.0.2", ReleaseVersion.Format(v));
    }

    [Theory]
    [InlineData("https://github.com/ulgrude/widget-pokemon/releases/tag/v1.2.0", true)]
    [InlineData("http://github.com/x/y", false)]
    [InlineData("https://evil.example/github.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(null, false)]
    public void SafeUrl(string? url, bool ok) => Assert.Equal(ok, UpdateService.SafeUrl(url) is not null);
}

public class UpdateServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pokewidget-upd-" + Guid.NewGuid());
    private long _now = 1_800_000_000_000;
    private const long Hour = 3_600_000, Day = 24 * Hour;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static string Release(string tag, string url = "https://github.com/ulgrude/widget-pokemon/releases/tag/v1.2.0") =>
        $$"""{"tag_name":"{{tag}}","html_url":"{{url}}","name":"x","body":"notes"}""";

    private (UpdateService Svc, GameRepository Repo) New(StubHandler h, bool auto = true)
    {
        var repo = new GameRepository(new AppPaths(_dir, Path.Combine(_dir, "sprites")), _now);
        repo.Settings.AutoCheckUpdates = auto;
        return (new UpdateService(new HttpClient(h), repo, () => _now), repo);
    }

    private static StubHandler Json(string body) => new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    [Fact]
    public async Task Newer_release_is_reported_and_remembered()
    {
        var h = Json(Release("v1.2.0"));
        var (svc, repo) = New(h);

        var st = await svc.CheckAsync("1.0.0", force: false);
        Assert.True(st.Available);
        Assert.Equal("1.2.0", st.LatestVersion);
        Assert.Equal("https://github.com/ulgrude/widget-pokemon/releases/tag/v1.2.0", st.Url);
        Assert.Equal("1.2.0", repo.Settings.LatestKnownVersion);
        Assert.EndsWith("/repos/ulgrude/widget-pokemon/releases/latest", h.Requests[0]);
    }

    [Fact]
    public async Task Same_or_older_release_is_not_an_update()
    {
        var (svc, _) = New(Json(Release("v1.0.0")));
        var st = await svc.CheckAsync("1.0.0", false);
        Assert.False(st.Available);
        Assert.Null(st.Url);
        Assert.Equal("1.0.0", st.LatestVersion);
    }

    [Fact]
    public async Task Auto_check_happens_at_most_once_a_day_but_manual_check_always_runs()
    {
        var h = Json(Release("v1.2.0"));
        var (svc, _) = New(h);
        await svc.CheckAsync("1.0.0", false);
        _now += Day - 1;
        var again = await svc.CheckAsync("1.0.0", false);
        Assert.Single(h.Requests);
        Assert.True(again.Available);                      // statut mémorisé, sans réseau

        await svc.CheckAsync("1.0.0", true);
        Assert.Equal(2, h.Requests.Count);

        _now += Day;
        Assert.True(svc.ShouldAutoCheck());
    }

    [Fact]
    public async Task Disabled_auto_check_never_calls_the_network_unless_forced()
    {
        var h = Json(Release("v1.2.0"));
        var (svc, _) = New(h, auto: false);
        Assert.False(svc.ShouldAutoCheck());
        await svc.CheckAsync("1.0.0", false);
        Assert.Empty(h.Requests);
        await svc.CheckAsync("1.0.0", true);
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Failed_auto_check_is_retried_after_one_hour()
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var (svc, _) = New(h);
        await Assert.ThrowsAsync<HttpRequestException>(() => svc.CheckAsync("1.0.0", false));
        Assert.False(svc.ShouldAutoCheck());
        _now += 30 * 60 * 1000;
        Assert.False(svc.ShouldAutoCheck());
        _now += 31 * 60 * 1000;
        Assert.True(svc.ShouldAutoCheck());
    }

    [Fact]
    public async Task Failed_manual_check_does_not_postpone_the_next_auto_check()
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var (svc, repo) = New(h);
        long before = repo.Settings.LastUpdateCheckMs;
        await Assert.ThrowsAsync<HttpRequestException>(() => svc.CheckAsync("1.0.0", true));
        Assert.Equal(before, repo.Settings.LastUpdateCheckMs);
    }

    [Fact]
    public async Task No_release_published_gives_a_clear_error()
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var (svc, _) = New(h);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => svc.CheckAsync("1.0.0", true));
        Assert.Contains("aucune version", UpdateService.Describe(ex));
    }

    [Fact]
    public async Task Rate_limit_is_described()
    {
        var h = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var (svc, _) = New(h);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => svc.CheckAsync("1.0.0", true));
        Assert.Contains("limite", UpdateService.Describe(ex));
    }

    [Fact]
    public async Task Untrusted_release_url_falls_back_to_the_releases_page()
    {
        var (svc, _) = New(Json(Release("v1.2.0", "https://evil.example/download.exe")));
        var st = await svc.CheckAsync("1.0.0", true);
        Assert.Equal("https://github.com/ulgrude/widget-pokemon/releases/latest", st.Url);
    }

    [Fact]
    public async Task Invalid_tag_is_rejected()
    {
        var (svc, _) = New(Json(Release("nightly")));
        await Assert.ThrowsAsync<InvalidDataException>(() => svc.CheckAsync("1.0.0", true));
    }

    [Fact]
    public async Task Known_status_forgets_a_version_that_is_now_installed()
    {
        var (svc, _) = New(Json(Release("v1.2.0")));
        await svc.CheckAsync("1.0.0", true);
        Assert.True(svc.Known("1.0.0").Available);
        Assert.False(svc.Known("1.2.0").Available);
        Assert.False(svc.Known("1.3.0").Available);
    }
}
