using System.Text;
using System.Text.Json;
using PokeWidget.Core;
using Xunit;

namespace PokeWidget.Core.Tests;

public class SaveObfuscatorTests
{
    private const string Sample = """{"balls":{"poke":20},"dex":{"25":{"name":"Pikachu ✨","rate":190,"count":3,"shiny":1}}}""";

    [Fact]
    public void Roundtrip_preserves_the_content_including_unicode()
    {
        Assert.Equal(Sample, SaveObfuscator.Unprotect(SaveObfuscator.Protect(Sample)));
    }

    [Fact]
    public void Output_is_not_readable_and_differs_each_time()
    {
        var a = SaveObfuscator.Protect(Sample);
        var b = SaveObfuscator.Protect(Sample);
        Assert.NotEqual(a, b); // nonce aléatoire
        string text = Encoding.Latin1.GetString(a);
        Assert.DoesNotContain("Pikachu", text);
        Assert.DoesNotContain("balls", text);
        Assert.True(SaveObfuscator.HasMagic(a));
    }

    [Fact]
    public void Any_modified_byte_is_detected()
    {
        var data = SaveObfuscator.Protect(Sample);
        for (int i = 0; i < data.Length; i += Math.Max(1, data.Length / 25))
        {
            var copy = (byte[])data.Clone();
            copy[i] ^= 0x01;
            Assert.Throws<InvalidDataException>(() => SaveObfuscator.Unprotect(copy));
        }
    }

    [Fact]
    public void Truncated_or_foreign_files_are_rejected()
    {
        var data = SaveObfuscator.Protect(Sample);
        Assert.Throws<InvalidDataException>(() => SaveObfuscator.Unprotect(data[..20]));
        Assert.Throws<InvalidDataException>(() => SaveObfuscator.Unprotect(Encoding.UTF8.GetBytes(Sample)));
        Assert.Throws<InvalidDataException>(() => SaveObfuscator.Unprotect(Array.Empty<byte>()));
    }
}

public class ProtectedProgressTests
{
    private const long Now = 1_800_000_000_000;

    [Fact]
    public void Protected_export_roundtrips_through_import()
    {
        var s = GameState.Fresh(Now - 5000);
        s.Balls.Hyper = 9;
        s.Dex[133] = new DexEntry { Name = "Évoli", Rate = 45, Count = 4, Shiny = 1 };
        var cfg = new SettingsModel { Seed = "ma seed", Position = WidgetPosition.TopRight };

        var bytes = ProgressFile.ExportProtected(s, cfg, DateTimeOffset.FromUnixTimeMilliseconds(Now));
        Assert.True(SaveObfuscator.HasMagic(bytes));
        var r = ProgressFile.ImportBytes(bytes, Now);
        Assert.Equal(9, r.State.Balls.Hyper);
        Assert.Equal("Évoli", r.State.Dex[133].Name);
        Assert.Equal("ma seed", r.Settings!.Seed);
        Assert.Equal(WidgetPosition.TopRight, r.Settings.Position);
    }

    [Fact]
    public void Legacy_json_export_behavior()
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFF{\"balls\":{\"poke\":5},\"dex\":{}}");
        if (ProgressFile.AllowLegacyJsonImport)
            Assert.Equal(5, ProgressFile.ImportBytes(bytes, Now).State.Balls.Poke);
        else
            Assert.Throws<InvalidDataException>(() => ProgressFile.ImportBytes(bytes, Now));
    }

    [Fact]
    public void Tampered_protected_export_is_rejected()
    {
        var bytes = ProgressFile.ExportProtected(GameState.Fresh(Now), new SettingsModel(), DateTimeOffset.UtcNow);
        bytes[^1] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => ProgressFile.ImportBytes(bytes, Now));
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        if (ProgressFile.AllowLegacyJsonImport)
            Assert.ThrowsAny<JsonException>(() => ProgressFile.ImportBytes(Encoding.UTF8.GetBytes("pas du json"), Now));
        else
            Assert.Throws<InvalidDataException>(() => ProgressFile.ImportBytes(Encoding.UTF8.GetBytes("pas du json"), Now));
    }
}

public class ProtectedRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pokewidget-prot-" + Guid.NewGuid());
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private AppPaths Paths() => new(_dir, Path.Combine(_dir, "sprites"));

    [Fact]
    public void State_file_on_disk_is_obfuscated()
    {
        var repo = new GameRepository(Paths(), 1000);
        repo.State.Dex[25] = new DexEntry { Name = "Pikachu", Rate = 190, Count = 2 };
        repo.SaveState();
        string raw = Encoding.Latin1.GetString(File.ReadAllBytes(Paths().StatePath));
        Assert.DoesNotContain("Pikachu", raw);
        Assert.DoesNotContain("balls", raw);
        Assert.Equal(2, new GameRepository(Paths(), 0).State.Dex[25].Count);
    }

    [Fact]
    public void Legacy_plain_state_json_is_migrated_then_deleted()
    {
        Directory.CreateDirectory(_dir);
        var legacy = GameState.Fresh(1234);
        legacy.Balls.Hyper = 42;
        legacy.Dex[25] = new DexEntry { Name = "Pikachu", Rate = 190, Count = 2 };
        File.WriteAllText(Paths().LegacyStatePath, JsonSerializer.Serialize(legacy, Json.Options));

        var repo = new GameRepository(Paths(), 9999);
        Assert.Equal(42, repo.State.Balls.Hyper);
        Assert.Equal(1234, repo.State.LastTick);
        Assert.True(File.Exists(Paths().StatePath));
        Assert.False(File.Exists(Paths().LegacyStatePath));
        Assert.DoesNotContain("Pikachu", Encoding.Latin1.GetString(File.ReadAllBytes(Paths().StatePath)));
        Assert.Equal(42, new GameRepository(Paths(), 0).State.Balls.Hyper);
    }

    [Fact]
    public void Hand_edited_state_file_is_set_aside_and_a_fresh_game_starts()
    {
        var repo = new GameRepository(Paths(), 1000);
        repo.State.Balls.Poke = 3;
        repo.SaveState();

        var bytes = File.ReadAllBytes(Paths().StatePath);
        bytes[^1] ^= 0x01; // édition à la main
        File.WriteAllBytes(Paths().StatePath, bytes);

        var again = new GameRepository(Paths(), 5000);
        Assert.Equal(GameConfig.StartBalls, again.State.Balls.Poke);
        Assert.Single(Directory.GetFiles(_dir, "state.dat.corrupt-*"));
    }
}
