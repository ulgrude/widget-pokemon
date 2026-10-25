using PokeWidget.Core;
using Xunit;

namespace PokeWidget.Core.Tests;

public class MiniIconTests
{
    private const string Seed = "newtab";

    // Premier slot chromatique et premier slot non chromatique pour cette seed
    private static long FindSlot(bool shiny) { for (long s = 0; ; s++) if (SpawnService.For(Seed, s).Shiny == shiny) return s; }

    [Fact]
    public void New_pokemon_wobbles_until_seen()
    {
        long slot = FindSlot(false);
        var s = GameState.Fresh(0);
        var m = MiniIcon.Compute(s, Seed, slot);
        Assert.True(m.IsNew); Assert.False(m.Shine);
        Assert.Equal("Un nouveau Pokémon est apparu", m.Label);

        Assert.True(s.MarkSeen(slot));
        Assert.False(s.MarkSeen(slot)); // déjà vu : rien à sauvegarder
        m = MiniIcon.Compute(s, Seed, slot);
        Assert.False(m.IsNew);
        Assert.Equal("Afficher le widget Pokémon", m.Label);
    }

    [Fact]
    public void Shiny_glows_even_after_being_seen()
    {
        long slot = FindSlot(true);
        var s = GameState.Fresh(0);
        s.MarkSeen(slot);
        var m = MiniIcon.Compute(s, Seed, slot);
        Assert.True(m.Shine); Assert.False(m.IsNew);
        Assert.Equal("Un Pokémon chromatique est là !", m.Label);
    }

    [Fact]
    public void Caught_pokemon_shows_no_alert()
    {
        long slot = FindSlot(true);
        var s = GameState.Fresh(0);
        s.LastCaptureSlot = slot;
        var m = MiniIcon.Compute(s, Seed, slot);
        Assert.False(m.IsNew); Assert.False(m.Shine);
    }

    [Fact]
    public void SeenSlot_survives_export_and_import()
    {
        var s = GameState.Fresh(1000); s.SeenSlot = 123;
        var json = ProgressFile.Export(s, new SettingsModel(), DateTimeOffset.FromUnixTimeMilliseconds(2000));
        Assert.Equal(123, ProgressFile.Import(json, 2000).State.SeenSlot);
    }
}
