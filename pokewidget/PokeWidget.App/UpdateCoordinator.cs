using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using PokeWidget.Core;
using Velopack;
using Velopack.Sources;

namespace PokeWidget.Desktop;

/// <summary>
/// Relie UpdateService (Core : détection via l'API GitHub, testée) à l'interface, et à Velopack pour l'installation.
/// - Installé via Velopack (Setup.exe) : "Installer et redémarrer" télécharge (delta si possible) et applique la mise à jour.
/// - Lancé autrement (dotnet run, dossier copié) : on ouvre simplement la page de la release.
/// </summary>
public sealed class UpdateCoordinator
{
    private const string RepoUrl = "https://github.com/" + UpdateService.DefaultRepo;

    private readonly UpdateService _svc;
    private readonly GameRepository _repo;
    private readonly UpdateManager? _vpk;
    private bool _running;

    public string InstalledVersion { get; }
    public UpdateStatus Status { get; private set; }
    public bool CanInstall => _vpk is not null;
    public bool IsInstalling { get; private set; }
    public int InstallProgress { get; private set; }

    /// <summary>État mis à jour (affichage du lien, entrée de menu, progression...).</summary>
    public event Action? Changed;
    /// <summary>Une nouvelle version vient d'être détectée et n'a pas encore été notifiée.</summary>
    public event Action<UpdateStatus>? NewVersionFound;
    /// <summary>L'application va se fermer pour appliquer la mise à jour (le temps de libérer l'icône de la zone de notification).</summary>
    public event Action? Restarting;

    public UpdateCoordinator(HttpClient http, GameRepository repo)
    {
        _repo = repo;
        _svc = new UpdateService(http, repo);
        InstalledVersion = ReadInstalledVersion();
        Status = _svc.Known(InstalledVersion);

        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            _vpk = mgr.IsInstalled ? mgr : null;
        }
        catch (Exception) { _vpk = null; } // pas installé via Velopack
    }

    private static string ReadInstalledVersion()
    {
        string? v = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return ReleaseVersion.TryParse(v, out var parsed) ? ReleaseVersion.Format(parsed) : "0.0.0";
    }

    /// <summary>Vérification manuelle (réglages). Lève une exception en cas d'échec : à décrire avec UpdateService.Describe.</summary>
    public async Task<UpdateStatus> CheckAsync(bool force)
    {
        var st = await _svc.CheckAsync(InstalledVersion, force);
        Apply(st);
        return st;
    }

    /// <summary>Vérification automatique : silencieuse en cas d'échec, au plus une par jour.</summary>
    public async Task AutoCheckAsync()
    {
        if (_running || !_svc.ShouldAutoCheck()) return;
        _running = true;
        try { Apply(await _svc.CheckAsync(InstalledVersion, false)); }
        catch (Exception) { /* nouvel essai dans 1 h */ }
        finally { _running = false; }
    }

    private void Apply(UpdateStatus st)
    {
        Status = st;
        Changed?.Invoke();
        if (st.Available && st.LatestVersion != _repo.Settings.LastNotifiedVersion)
        {
            _repo.Settings.LastNotifiedVersion = st.LatestVersion;
            _repo.SaveSettings();
            NewVersionFound?.Invoke(st);
        }
    }

    /// <summary>Action de l'utilisateur : confirme, puis installe (Velopack) ou ouvre la page de téléchargement.</summary>
    public async Task PromptAndInstallAsync()
    {
        var st = Status;
        if (!st.Available || IsInstalling) return;
        if (!CanInstall) { OpenReleasePage(); return; }

        var answer = MessageBox.Show(
            $"Installer la version {st.LatestVersion} ?\n\nLe widget va redémarrer. Ta progression est conservée.",
            "Mise à jour", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try { await InstallAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show($"La mise à jour automatique a échoué : {ex.Message}\n\nLa page de téléchargement va s'ouvrir.",
                "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenReleasePage();
        }
    }

    private async Task InstallAsync()
    {
        if (_vpk is null) { OpenReleasePage(); return; }
        IsInstalling = true;
        InstallProgress = 0;
        Changed?.Invoke();
        try
        {
            var info = await _vpk.CheckForUpdatesAsync();
            if (info is null) throw new InvalidOperationException("la release publiée ne contient pas de paquet d'installation");

            // Progress<T> rappelle sur le thread de l'interface (créé ici, sur ce thread)
            IProgress<int> progress = new Progress<int>(p => { InstallProgress = p; Changed?.Invoke(); });
            await _vpk.DownloadUpdatesAsync(info, progress.Report);

            Restarting?.Invoke();
            _vpk.ApplyUpdatesAndRestart(info.TargetFullRelease); // quitte l'application, applique, relance
        }
        finally
        {
            IsInstalling = false;
            InstallProgress = 0;
            Changed?.Invoke();
        }
    }

    public void OpenReleasePage()
    {
        try { Process.Start(new ProcessStartInfo(Status.Url ?? _svc.ReleasesPageUrl) { UseShellExecute = true }); }
        catch (Exception) { /* pas de navigateur par défaut : rien à faire */ }
    }
}
