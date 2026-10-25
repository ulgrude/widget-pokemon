namespace PokeWidget.Core;

/// <summary>Réglages de gameplay (équivalent de CFG dans le userscript).</summary>
public static class GameConfig
{
    public const string DefaultSeed = "youtube.com/@golbfr";
    public const long SlotMs = 30L * 60 * 1000;   // un nouveau Pokémon toutes les 30 min
    public const long BallMs = 60L * 60 * 1000;   // une Poké Ball par heure
    public const int MaxPokemonId = 1025;
    public const double ShinyRate = 1.0 / 64;
    public const int StartBalls = 20;
    public const int BallCap = 100;
    public const double BonusChance = 0.05;
    public const int MaxImportChars = 5_000_000;

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
