using System.Globalization;

namespace PokeWidget.Core;

public readonly record struct Spawn(long Slot, int Id, bool Shiny);
public sealed record SpeciesInfo(string Name, int Rate);

public static class SpawnService
{
    public static long SlotAt(long nowMs) => (long)Math.Floor(nowMs / (double)GameConfig.SlotMs);
    public static long SlotEndMs(long slot) => (slot + 1) * GameConfig.SlotMs;

    /// <summary>Même seed + même slot => même espèce ; le shiny est propre à l'identifiant du joueur.</summary>
    public static Spawn For(string seed, long slot, string playerId, bool shinyBoost = false)
    {
        string slotKey = seed + ":" + slot.ToString(CultureInfo.InvariantCulture);
        var pokemonRng = SeededRng.FromString(slotKey);
        int id = 1 + (int)Math.Floor(pokemonRng.NextDouble() * GameConfig.MaxPokemonId);
        var shinyRng = SeededRng.FromString(slotKey + ":" + playerId);
        double rate = GameConfig.ShinyRate * (shinyBoost ? 1.25 : 1.0);
        bool shiny = shinyRng.NextDouble() < rate;
        return new Spawn(slot, id, shiny);
    }
}
