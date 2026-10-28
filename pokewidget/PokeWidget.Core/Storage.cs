using System.Text;
using System.Text.Json;

namespace PokeWidget.Core;

public sealed class AppPaths
{
    public string DataDir { get; }
    public string SpritesDir { get; }
    /// <summary>Progression : fichier obfusqué (voir SaveObfuscator).</summary>
    public string StatePath => Path.Combine(DataDir, "state.dat");
    /// <summary>Ancien format (JSON en clair) : migré puis supprimé au premier lancement.</summary>
    public string LegacyStatePath => Path.Combine(DataDir, "state.json");
    public string SettingsPath => Path.Combine(DataDir, "settings.json");
    public string SpeciesPath => Path.Combine(DataDir, "species.json");

    public AppPaths(string dataDir, string spritesDir) { DataDir = dataDir; SpritesDir = spritesDir; }

    /// <summary>%AppData%\PokeWidget (progression) et %LocalAppData%\PokeWidgetCache\sprites (cache jetable, hors du dossier d'installation Velopack).</summary>
    public static AppPaths Default() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PokeWidget"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokeWidgetCache", "sprites"));
}

public static class JsonFile
{
    // Seul un contenu illisible met le fichier de côté. Une erreur d'accès (fichier verrouillé...) est propagée :
    // mieux vaut un échec visible qu'une progression silencieusement remplacée par une partie vierge.
    private static void SetAside(string path)
    {
        try { File.Move(path, path + ".corrupt-" + DateTime.UtcNow.Ticks, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* non bloquant */ }
    }

    /// <summary>Lit un JSON. Absent → fallback. Illisible → mis de côté (.corrupt-...) puis fallback.</summary>
    public static T Load<T>(string path, Func<T> fallback)
    {
        if (!File.Exists(path)) return fallback();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), Json.Options) ?? fallback();
        }
        catch (JsonException)
        {
            SetAside(path);
            return fallback();
        }
    }

    /// <summary>Même chose pour un fichier obfusqué : une modification à la main est détectée comme une corruption.</summary>
    public static T LoadProtected<T>(string path, Func<T> fallback)
    {
        if (!File.Exists(path)) return fallback();
        try
        {
            string json = SaveObfuscator.Unprotect(File.ReadAllBytes(path));
            return JsonSerializer.Deserialize<T>(json, Json.Options) ?? fallback();
        }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            SetAside(path);
            return fallback();
        }
    }

    /// <summary>Écriture atomique : fichier temporaire puis remplacement, pour ne jamais laisser un fichier à moitié écrit.</summary>
    public static void SaveAtomic<T>(string path, T value) =>
        WriteAtomic(path, new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(value, Json.Options)));

    public static void SaveProtectedAtomic<T>(string path, T value) =>
        WriteAtomic(path, SaveObfuscator.Protect(JsonSerializer.Serialize(value, Json.Options)));

    private static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, true);
    }
}

/// <summary>Point d'accès unique à la progression, aux réglages et au cache des espèces.</summary>
public sealed class GameRepository
{
    public AppPaths Paths { get; }
    public object SyncRoot { get; } = new();
    public GameState State { get; private set; }
    public SettingsModel Settings { get; private set; }
    public Dictionary<int, SpeciesInfo> Species { get; private set; }

    public GameRepository(AppPaths paths, long nowMs)
    {
        Paths = paths;
        bool migrateLegacy = !File.Exists(paths.StatePath) && File.Exists(paths.LegacyStatePath);
        State = migrateLegacy
            ? JsonFile.Load(paths.LegacyStatePath, () => GameState.Fresh(nowMs))
            : JsonFile.LoadProtected(paths.StatePath, () => GameState.Fresh(nowMs));
        bool playerIdChanged = State.EnsurePlayerId();
        Settings = JsonFile.Load(paths.SettingsPath, () => new SettingsModel());
        Species = JsonFile.Load(paths.SpeciesPath, () => new Dictionary<int, SpeciesInfo>());
        Settings.Seed = SettingsModel.NormalizeSeed(Settings.Seed);

        // Première fois, migration, ou fichier mis de côté : on (ré)écrit la sauvegarde obfusquée.
        if (!File.Exists(paths.StatePath) || playerIdChanged) SaveState();
        if (migrateLegacy)
        {
            try { File.Delete(paths.LegacyStatePath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* non bloquant */ }
        }
    }

    public void SaveState() { lock (SyncRoot) JsonFile.SaveProtectedAtomic(Paths.StatePath, State); }
    public void SaveSettings() { lock (SyncRoot) JsonFile.SaveAtomic(Paths.SettingsPath, Settings); }
    public void SaveSpecies() { lock (SyncRoot) JsonFile.SaveAtomic(Paths.SpeciesPath, Species); }

    public void ReplaceState(GameState s) { lock (SyncRoot) { State = s; SaveState(); } }
}
