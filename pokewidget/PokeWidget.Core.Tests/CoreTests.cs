using PokeWidget.Core;
using Xunit;

namespace PokeWidget.Core.Tests;

/// <summary>Random à valeur fixe, pour rendre les captures déterministes.</summary>
internal sealed class FixedRandom(double value) : Random
{
    public override double NextDouble() => value;
}

public class RngAndSpawnTests
{
    [Fact]
    public void Hash_and_rng_match_the_userscript()
    {
        foreach (var v in Vectors.Spawns)
        {
            string key = $"{v.Seed}:{v.Slot}";
            Assert.True(v.Hash == SeededRng.HashString(key), $"hash différent pour '{key}'");
            var rng = SeededRng.FromString(key);
            Assert.Equal(v.D0, rng.NextDouble());
            Assert.Equal(v.D1, rng.NextDouble());
            Assert.Equal(v.D2, rng.NextDouble());
        }
    }

    [Fact]
    public void Spawn_species_match_the_userscript()
    {
        foreach (var v in Vectors.Spawns)
        {
            var s = SpawnService.For(v.Seed, v.Slot, "00000000-0000-4000-8000-000000000001");
            Assert.Equal(v.Id, s.Id);
        }
    }

    [Fact]
    public void Shiny_is_stable_per_player_but_varies_between_players()
    {
        const string firstPlayer = "00000000-0000-4000-8000-000000000001";
        const string secondPlayer = "00000000-0000-4000-8000-000000000002";
        bool foundDifferentShiny = false;
        for (long slot = 0; slot < 10_000; slot++)
        {
            var first = SpawnService.For("shared seed", slot, firstPlayer);
            var firstAgain = SpawnService.For("shared seed", slot, firstPlayer);
            var second = SpawnService.For("shared seed", slot, secondPlayer);
            Assert.Equal(first, firstAgain);
            Assert.Equal(first.Id, second.Id);
            if (first.Shiny != second.Shiny) foundDifferentShiny = true;
        }
        Assert.True(foundDifferentShiny);
    }

    [Fact]
    public void Slots_change_every_30_minutes()
    {
        Assert.Equal(0, SpawnService.SlotAt(1_799_999));
        Assert.Equal(1, SpawnService.SlotAt(1_800_000));
        Assert.Equal(1_800_000, SpawnService.SlotEndMs(0));
    }
}

public class CatchTests
{
    [Fact]
    public void Probability_matches_the_userscript()
    {
        foreach (var v in Vectors.Catch)
            Assert.Equal(v.Prob, CatchMath.Probability(v.Rate, v.Bonus), 12);
    }

    [Theory]
    [InlineData(255, BallType.Poke)] [InlineData(120, BallType.Poke)]
    [InlineData(119, BallType.Super)] [InlineData(46, BallType.Super)]
    [InlineData(45, BallType.Hyper)] [InlineData(3, BallType.Hyper)]
    public void Tier_boundaries(int rate, BallType expected) => Assert.Equal(expected, BallInfo.TierFor(rate));
}

public class GameStateTests
{
    private const long T0 = 1_000_000_000_000;

    [Fact]
    public void Tick_does_nothing_before_one_hour()
    {
        var s = GameState.Fresh(T0);
        Assert.False(s.TickBalls(T0 + GameConfig.BallMs - 1, new FixedRandom(1)));
        Assert.Equal(GameConfig.StartBalls, s.Balls.Poke);
    }

    [Fact]
    public void Tick_catches_up_missed_hours_and_keeps_the_remainder()
    {
        var s = GameState.Fresh(T0);
        Assert.True(s.TickBalls(T0 + 3 * GameConfig.BallMs + 12345, new FixedRandom(1)));
        Assert.Equal(GameConfig.StartBalls + 3, s.Balls.Poke);
        Assert.Equal(T0 + 3 * GameConfig.BallMs, s.LastTick);
    }

    [Fact]
    public void Tick_is_capped_at_100()
    {
        var s = GameState.Fresh(T0);
        s.TickBalls(T0 + 500 * GameConfig.BallMs, new FixedRandom(1));
        Assert.Equal(GameConfig.BallCap, s.Balls.Poke);
    }

    [Fact]
    public void Tick_bonus_balls_depend_on_the_captured_species_tier()
    {
        var s = GameState.Fresh(T0);
        s.Balls.Poke = 0;
        s.Dex[150] = new DexEntry { Name = "Mewtwo", Rate = 3, Count = 2 }; // Hyper Ball
        s.TickBalls(T0 + GameConfig.BallMs, new FixedRandom(0)); // 0 < 5 % : chaque essai réussit
        Assert.Equal(1, s.Balls.Poke);
        Assert.Equal(2, s.Balls.Hyper);
        Assert.Equal(0, s.Balls.Super);
    }

    [Fact]
    public void Throw_catch_updates_dex_and_blocks_a_second_capture()
    {
        var s = GameState.Fresh(T0);
        var spawn = new Spawn(42, 25, true);
        var sp = new SpeciesInfo("Pikachu", 190);
        Assert.Equal(ThrowOutcome.Caught, s.Throw(BallType.Poke, spawn, sp, new FixedRandom(0)));
        Assert.Equal(GameConfig.StartBalls - 1, s.Balls.Poke);
        Assert.Equal(1, s.Dex[25].Count);
        Assert.Equal(1, s.Dex[25].Shiny);
        Assert.Equal(ThrowOutcome.Unavailable, s.Throw(BallType.Poke, spawn, sp, new FixedRandom(0)));
        Assert.Equal(GameConfig.StartBalls - 1, s.Balls.Poke);
    }

    [Fact]
    public void Throw_miss_consumes_a_ball_and_allows_retry()
    {
        var s = GameState.Fresh(T0);
        var spawn = new Spawn(1, 150, false);
        var sp = new SpeciesInfo("Mewtwo", 3);
        Assert.Equal(ThrowOutcome.Missed, s.Throw(BallType.Poke, spawn, sp, new FixedRandom(0.999999)));
        Assert.Equal(GameConfig.StartBalls - 1, s.Balls.Poke);
        Assert.False(s.IsCaught(1));
    }

    [Fact]
    public void Throw_without_balls_is_unavailable()
    {
        var s = GameState.Fresh(T0);
        Assert.Equal(ThrowOutcome.Unavailable, s.Throw(BallType.Hyper, new Spawn(1, 1, false), new SpeciesInfo("X", 45), new FixedRandom(0)));
    }
}

public class ProgressFileTests
{
    private const long Now = 1_800_000_000_000;

    private const string UserscriptExport = """
    {"app":"poke-widget","version":1,"exportedAt":"2026-01-01T00:00:00.000Z",
     "state":{"balls":{"poke":500,"super":-3,"hyper":"x"},"lastTick":99999999999999,"lastCaptureSlot":5,"seenSlot":4,
       "dex":{"25":{"name":"Pikachu","rate":190,"count":3,"shiny":9},"5000":{"name":"X","rate":1,"count":1,"shiny":0},
              "abc":{},"1":{"name":"Bulbizarre","rate":45,"count":0,"shiny":0}}},
     "config":{"seed":"  amis  ","position":"top-left","urls":["https://discord.com/channels/*"]}}
    """;

    [Fact]
    public void Import_accepts_the_userscript_format_and_sanitizes_it()
    {
        var r = ProgressFile.Import(UserscriptExport, Now);
        Assert.Equal(100, r.State.Balls.Poke);
        Assert.Equal(0, r.State.Balls.Super);
        Assert.Equal(0, r.State.Balls.Hyper);
        Assert.Equal(Now, r.State.LastTick);
        Assert.Equal(5, r.State.LastCaptureSlot);
        Assert.Equal(2, r.State.Dex.Count);                 // 5000 et "abc" écartés
        Assert.Equal(3, r.State.Dex[25].Shiny);              // shiny borné à count
        Assert.Equal(1, r.State.Dex[1].Count);               // count minimum 1
        Assert.Equal("amis", r.Settings!.Seed);
        Assert.Equal(WidgetPosition.TopLeft, r.Settings.Position);
        Assert.True(Guid.TryParseExact(r.State.PlayerId, "D", out _));
    }

    [Fact]
    public void Import_rejects_unknown_files()
    {
        Assert.Throws<InvalidDataException>(() => ProgressFile.Import("{\"hello\":1}", Now));
        Assert.Throws<InvalidDataException>(() => ProgressFile.Import("[1,2]", Now));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ProgressFile.Import("pas du json", Now));
    }

    [Fact]
    public void Export_then_import_roundtrips()
    {
        var s = GameState.Fresh(Now - 5000);
        s.Balls.Super = 7;
        s.LastCaptureSlot = 99;
        s.Dex[133] = new DexEntry { Name = "Évoli", Rate = 45, Count = 4, Shiny = 1 };
        var cfg = new SettingsModel { Seed = "ma seed", Position = WidgetPosition.BottomLeft };

        var r = ProgressFile.Import(ProgressFile.Export(s, cfg, DateTimeOffset.FromUnixTimeMilliseconds(Now)), Now);
        Assert.Equal(7, r.State.Balls.Super);
        Assert.Equal(99, r.State.LastCaptureSlot);
        Assert.Equal(s.LastTick, r.State.LastTick);
        Assert.Equal(s.PlayerId, r.State.PlayerId);
        Assert.Equal("Évoli", r.State.Dex[133].Name);
        Assert.Equal(4, r.State.Dex[133].Count);
        Assert.Equal("ma seed", r.Settings!.Seed);
        Assert.Equal(WidgetPosition.BottomLeft, r.Settings.Position);
    }
}

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pokewidget-test-" + Guid.NewGuid());
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Repository_creates_persists_and_reloads()
    {
        var paths = new AppPaths(_dir, Path.Combine(_dir, "sprites"));
        var repo = new GameRepository(paths, 1000);
        Assert.True(File.Exists(paths.StatePath));
        repo.State.Balls.Hyper = 9;
        repo.Settings.Seed = "x";
        repo.Species[25] = new SpeciesInfo("Pikachu", 190);
        repo.SaveState(); repo.SaveSettings(); repo.SaveSpecies();

        var again = new GameRepository(paths, 2000);
        Assert.Equal(9, again.State.Balls.Hyper);
        Assert.Equal(repo.State.PlayerId, again.State.PlayerId);
        Assert.Equal(1000, again.State.LastTick);
        Assert.Equal("x", again.Settings.Seed);
        Assert.Equal(190, again.Species[25].Rate);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Repository_adds_and_persists_a_player_id_for_an_old_save()
    {
        Directory.CreateDirectory(_dir);
        var paths = new AppPaths(_dir, Path.Combine(_dir, "sprites"));
        const string oldSave = """{"balls":{"poke":7,"super":0,"hyper":0},"lastTick":1234,"dex":{},"shop":{}}""";
        File.WriteAllBytes(paths.StatePath, SaveObfuscator.Protect(oldSave));

        var repo = new GameRepository(paths, 2000);
        Assert.True(Guid.TryParseExact(repo.State.PlayerId, "D", out _));
        Assert.Equal(7, repo.State.Balls.Poke);

        var reloaded = new GameRepository(paths, 3000);
        Assert.Equal(repo.State.PlayerId, reloaded.State.PlayerId);
    }

    [Fact]
    public void Corrupted_file_is_set_aside_not_overwritten()
    {
        Directory.CreateDirectory(_dir);
        var paths = new AppPaths(_dir, Path.Combine(_dir, "sprites"));
        File.WriteAllText(paths.StatePath, "{ pas du json");
        var repo = new GameRepository(paths, 5000);
        Assert.Equal(GameConfig.StartBalls, repo.State.Balls.Poke);
        Assert.Single(Directory.GetFiles(_dir, "state.dat.corrupt-*"));
    }
}
