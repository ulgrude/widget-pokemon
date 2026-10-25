namespace PokeWidget.Core;

public static class PokeHttp
{
    /// <summary>Un seul HttpClient pour toute l'application, avec un User-Agent explicite pour PokéAPI.</summary>
    public static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PokeWidget/1.0 (+https://github.com/ulgrude/widget-pokemon)");
        return c;
    }
}
