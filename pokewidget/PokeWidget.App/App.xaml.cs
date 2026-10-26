using System.Windows;
using PokeWidget.Core;
using Velopack;

namespace PokeWidget.Desktop;

public partial class App : Application
{
    /// <summary>Velopack doit s'exécuter avant tout le reste : il gère les arguments d'installation/mise à jour et peut quitter.</summary>
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private Mutex? _mutex;
    private bool _ownsMutex;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Une seule instance : une deuxième copie quitte immédiatement.
        _mutex = new Mutex(true, @"Local\PokeWidget.SingleInstance", out _ownsMutex);
        if (!_ownsMutex) { Shutdown(); return; }

        var paths = AppPaths.Default();
        var repo = new GameRepository(paths, GameConfig.NowMs());
        var http = PokeHttp.Create();
        var updates = new UpdateCoordinator(http, repo);

        var window = new WidgetWindow(repo, new SpeciesService(http, repo), new SpriteCache(http, paths.SpritesDir), updates);
        MainWindow = window;
        window.Show();

        // Ré-enregistre le chemin de l'exécutable si le lancement avec Windows est activé (au cas où il a été déplacé).
        if (repo.Settings.RunAtStartup)
            try { StartupRegistration.Set(true); } catch (Exception) { /* non bloquant */ }

        _tray = new TrayIcon(window.ExpandWidget, window.OpenSettings, window.OpenDex, window.OpenShop, window.OpenHelp,
                             window.ToggleCollapsed, () => window.IsCollapsed, Shutdown, () => _ = updates.PromptAndInstallAsync());

        void SyncTray() => _tray?.SetUpdate(updates.Status.Available ? $"⬆ Mise à jour {updates.Status.LatestVersion} disponible" : null);
        updates.Changed += SyncTray;
        updates.Restarting += () => _tray?.Dispose(); // pas d'icône fantôme pendant la mise à jour
        SyncTray();
        updates.NewVersionFound += st => _tray?.Notify("Poké-widget", $"La version {st.LatestVersion} est disponible. Clique ici pour la télécharger.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
