using PokeWidget.Core;
using Xunit;

namespace PokeWidget.Core.Tests;

public class ShopTests
{
    private const long T0 = 1_000_000_000_000;

    [Fact]
    public void CalculateEarnedDoublons_counts_duplicates_correctly()
    {
        var s = GameState.Fresh(T0);
        // Pikachu: 3 captures, 1 shiny -> 2 normaux (donc 1 doublon normal), 1 shiny (donc 0 doublon shiny)
        s.Dex[25] = new DexEntry { Name = "Pikachu", Rate = 190, Count = 3, Shiny = 1 };
        // Mew: 1 capture, 0 shiny -> 0 doublon normal, 0 doublon shiny
        s.Dex[151] = new DexEntry { Name = "Mew", Rate = 45, Count = 1, Shiny = 0 };
        // Dracaufeu: 4 captures, 3 shinies -> 1 normal (0 doublon), 3 shinies (2 doublons chromatiques)
        s.Dex[6] = new DexEntry { Name = "Dracaufeu", Rate = 45, Count = 4, Shiny = 3 };

        var (normalDupes, shinyDupes) = s.GetEarnedDoublons();
        Assert.Equal(1, normalDupes);
        Assert.Equal(2, shinyDupes);
        Assert.Equal(1, s.AvailableDoublons);
        Assert.Equal(2, s.AvailableShinyDoublons);
    }

    [Fact]
    public void BuyMystery_deducts_doublons_and_updates_dex()
    {
        var s = GameState.Fresh(T0);
        // 11 normaux -> 10 doublons
        s.Dex[25] = new DexEntry { Name = "Pikachu", Rate = 190, Count = 11, Shiny = 0 };
        Assert.Equal(10, s.AvailableDoublons);

        Assert.True(s.BuyMystery(133, "Évoli", 45, true));
        Assert.Equal(0, s.AvailableDoublons);
        Assert.Equal(10, s.Shop.SpentDoublons);
        Assert.Equal(1, s.Dex[133].Count);
        Assert.Equal(1, s.Dex[133].Shiny);
        Assert.Equal(1, s.Dex[133].Mystery);

        // Insuffisant pour un 2ème achat
        Assert.False(s.BuyMystery(1, "Bulbizarre", 45, false));
    }

    [Fact]
    public void BuyBallCap_increases_capacity_up_to_five_times()
    {
        var s = GameState.Fresh(T0);
        // 31 normaux -> 30 doublons
        s.Dex[25] = new DexEntry { Name = "Pikachu", Rate = 190, Count = 31, Shiny = 0 };

        for (int i = 1; i <= 5; i++)
        {
            Assert.True(s.BuyBallCap());
            Assert.Equal(i, s.Shop.BallCapUpgrades);
            Assert.Equal(100 + i * 10, s.MaxBallCap);
        }

        // 6ème achat refusé (max 5)
        Assert.False(s.BuyBallCap());
        Assert.Equal(5, s.Shop.BallCapUpgrades);
        Assert.Equal(150, s.MaxBallCap);
    }

    [Fact]
    public void MissCatchRate_increases_effective_rate_on_misses()
    {
        var s = GameState.Fresh(T0);
        // 101 normaux -> 100 doublons
        s.Dex[25] = new DexEntry { Name = "Pikachu", Rate = 190, Count = 101, Shiny = 0 };

        Assert.True(s.BuyMissCatchRate());
        Assert.Equal(1, s.Shop.MissCatchRateUpgrades);
        Assert.False(s.BuyMissCatchRate()); // Max 1 achat

        var spawn = new Spawn(10, 150, false);
        var species = new SpeciesInfo("Mewtwo", 3);

        // Rate initial
        Assert.Equal(3.0, s.GetEffectiveRate(species.Rate, spawn.Slot));

        // 1er lancer raté (force un nombre aléatoire élevé pour rater)
        s.Throw(BallType.Poke, spawn, species, new FixedRandom(0.9999));
        Assert.Equal(1, s.MissedCount);
        Assert.Equal(3.02, s.GetEffectiveRate(species.Rate, spawn.Slot), 2);

        // 2ème lancer raté
        s.Throw(BallType.Poke, spawn, species, new FixedRandom(0.9999));
        Assert.Equal(2, s.MissedCount);
        Assert.Equal(3.04, s.GetEffectiveRate(species.Rate, spawn.Slot), 2);

        // Nouveau slot différent -> réinitialise le bonus de rate
        Assert.Equal(3.0, s.GetEffectiveRate(species.Rate, 11));
    }

    [Fact]
    public void ShinyBoost_upgrade_applies_and_limits_to_one()
    {
        var s = GameState.Fresh(T0);
        // 2 shinies -> 1 doublon chromatique
        s.Dex[25] = new DexEntry { Name = "Pikachu", Rate = 190, Count = 2, Shiny = 2 };
        Assert.Equal(1, s.AvailableShinyDoublons);

        Assert.True(s.BuyShinyBoost());
        Assert.Equal(1, s.Shop.ShinyBoostUpgrades);
        Assert.Equal(0, s.AvailableShinyDoublons);
        Assert.False(s.BuyShinyBoost()); // Max 1 achat
    }

    [Fact]
    public void ProgressFile_roundtrips_shop_state()
    {
        var s = GameState.Fresh(T0);
        s.Shop.BallCapUpgrades = 3;
        s.Shop.MissCatchRateUpgrades = 1;
        s.Shop.ShinyBoostUpgrades = 1;
        s.Shop.SpentDoublons = 25;
        s.Shop.SpentShinyDoublons = 1;
        s.Balls.Poke = 130;
        s.Dex[25] = new DexEntry { Name = "Pikachu", Rate = 190, Count = 5, Shiny = 2, Mystery = 1 };

        var cfg = new SettingsModel();
        string json = ProgressFile.Export(s, cfg, DateTimeOffset.UtcNow);
        var imported = ProgressFile.Import(json, T0);

        Assert.Equal(3, imported.State.Shop.BallCapUpgrades);
        Assert.Equal(1, imported.State.Shop.MissCatchRateUpgrades);
        Assert.Equal(1, imported.State.Shop.ShinyBoostUpgrades);
        Assert.Equal(25, imported.State.Shop.SpentDoublons);
        Assert.Equal(1, imported.State.Shop.SpentShinyDoublons);
        Assert.Equal(130, imported.State.Balls.Poke);
        Assert.Equal(1, imported.State.Dex[25].Mystery);
    }
}
