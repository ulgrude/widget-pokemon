using System.Globalization;

namespace PokeWidget.Core;

public readonly record struct Spawn(long Slot, int Id, bool Shiny);
public sealed record SpeciesInfo(string Name, int Rate);

public static class SpawnService
{
    public static long SlotAt(long nowMs) => (long)Math.Floor(nowMs / (double)GameConfig.SlotMs);
    public static long SlotEndMs(long slot) => (slot + 1) * GameConfig.SlotMs;

    /// <summary>Même seed + même slot => même Pokémon que le userscript.</summary>
    public static Spawn For(string seed, long slot)
    {
        var rng = SeededRng.FromString(seed + ":" + slot.ToString(CultureInfo.InvariantCulture));
        int id = 1 + (int)Math.Floor(rng.NextDouble() * GameConfig.MaxPokemonId);
        bool shiny = rng.NextDouble() < GameConfig.ShinyRate;
        return new Spawn(slot, id, shiny);
    }
}
