using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using PokeWidget.Core;

namespace PokeWidget.Desktop;

public partial class SettingsWindow : Window
{
    private readonly GameRepository _repo;
    private readonly UpdateCoordinator _updates;
    private readonly Action<bool> _onChanged; // paramètre : true si le Pokémon courant doit être rechargé (seed / import)
    private readonly Dictionary<WidgetPosition, ToggleButton> _posButtons = new();
    private readonly Dictionary<DisplayMode, ToggleButton> _modeButtons = new();
    private WidgetPosition _position;
    private DisplayMode _mode;

    public SettingsWindow(GameRepository repo, UpdateCoordinator updates, Action<bool> onChanged)
    {
        InitializeComponent();
        DarkChrome.Apply(this);
        MaxHeight = SystemParameters.WorkArea.Height;
        _repo = repo; _updates = updates; _onChanged = onChanged;

        var s = repo.Settings;
        _position = s.Position; _mode = s.Mode;
        SeedBox.Text = s.Seed;
        StartupCheck.IsChecked = s.RunAtStartup;
        AutoCheck.IsChecked = s.AutoCheckUpdates;
        ShowUpdateStatus(updates.Status);

        var positions = new (WidgetPosition P, string Label)[]
        {
            (WidgetPosition.TopLeft, "Haut gauche"), (WidgetPosition.TopRight, "Haut droite"),
            (WidgetPosition.BottomLeft, "Bas gauche"), (WidgetPosition.BottomRight, "Bas droite"),
        };
        foreach (var (p, label) in positions)
        {
            var b = Choice(label, p == _position);
            b.Click += (_, _) => { _position = p; Refresh(_posButtons, p); };
            _posButtons[p] = b; PosGrid.Children.Add(b);
        }
        foreach (var (m, label) in new[] { (DisplayMode.Desktop, "Sur le bureau"), (DisplayMode.Topmost, "Toujours au-dessus") })
        {
            var b = Choice(label, m == _mode);
            b.Click += (_, _) => { _mode = m; Refresh(_modeButtons, m); };
            _modeButtons[m] = b; ModeGrid.Children.Add(b);
        }
        Loaded += (_, _) => { SeedBox.Focus(); SeedBox.SelectAll(); };
    }

    private ToggleButton Choice(string label, bool on) =>
        new() { Content = label, IsChecked = on, Style = (Style)FindResource("PkChoice"), Margin = new Thickness(0, 0, 8, 8) };

    private static void Refresh<T>(Dictionary<T, ToggleButton> buttons, T selected) where T : notnull
    {
        foreach (var (k, b) in buttons) b.IsChecked = EqualityComparer<T>.Default.Equals(k, selected);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _repo.Settings;
        string seed = SettingsModel.NormalizeSeed(SeedBox.Text);
        bool seedChanged = seed != s.Seed;
        s.Seed = seed; s.Position = _position; s.Mode = _mode;
        s.RunAtStartup = StartupCheck.IsChecked == true;
        s.AutoCheckUpdates = AutoCheck.IsChecked == true;
        _repo.SaveSettings();

        try { StartupRegistration.Set(s.RunAtStartup); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            MessageBox.Show(this, "Impossible de modifier le lancement avec Windows : " + ex.Message, "Réglages", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _onChanged(seedChanged);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Discord_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://discord.gg/6PtyhKa7GA") { UseShellExecute = true }); }
        catch (Exception) { /* pas bloquant */ }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Sauvegarde Poké-widget (*.pokesave)|*.pokesave", DefaultExt = ".pokesave",
            FileName = $"pokedex-{DateTime.Now:yyyy-MM-dd}.pokesave",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, ProgressFile.ExportProtected(_repo.State, _repo.Settings, DateTimeOffset.UtcNow));
            BackupMsg.Text = "Export enregistré : " + dlg.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BackupMsg.Text = "Export impossible : " + ex.Message;
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Sauvegarde Poké-widget (*.pokesave;*.json)|*.pokesave;*.json|Tous les fichiers|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dlg.FileName).Length > GameConfig.MaxImportChars) throw new InvalidDataException("fichier trop volumineux");
            var r = ProgressFile.ImportBytes(File.ReadAllBytes(dlg.FileName), GameConfig.NowMs());

            int species = r.State.Dex.Count;
            string msg = "Remplacer ta progression actuelle par celle du fichier ?\n\n" +
                         $"• {species} espèce{(species > 1 ? "s" : "")} dans le Pokédex\n" +
                         $"• Poké {r.State.Balls.Poke} / Super {r.State.Balls.Super} / Hyper {r.State.Balls.Hyper}" +
                         (r.Settings is null ? "" : "\n• Seed et position restaurées aussi");
            if (MessageBox.Show(this, msg, "Importer la progression", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            _repo.ReplaceState(r.State);
            if (r.Settings is { } cfg)
            {
                _repo.Settings.Seed = cfg.Seed;
                _repo.Settings.Position = cfg.Position;
                _repo.SaveSettings();
            }
            _onChanged(true);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            BackupMsg.Text = "Import impossible : " + ex.Message;
        }
    }

    private void ShowUpdateStatus(UpdateStatus st)
    {
        if (st.Available)
        {
            UpdateMsg.Text = $"Version {_updates.InstalledVersion} installée, {st.LatestVersion} disponible.";
            DownloadBtn.Content = _updates.CanInstall ? $"Installer la version {st.LatestVersion} et redémarrer" : $"Télécharger la version {st.LatestVersion}";
            DownloadBtn.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateMsg.Text = $"Version installée : {_updates.InstalledVersion}.";
            DownloadBtn.Visibility = Visibility.Collapsed;
        }
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        CheckBtn.IsEnabled = false;
        UpdateMsg.Text = "Vérification…";
        try
        {
            var st = await _updates.CheckAsync(force: true);
            ShowUpdateStatus(st);
            if (!st.Available) UpdateMsg.Text = $"À jour (version {_updates.InstalledVersion}).";
        }
        catch (Exception ex)
        {
            UpdateMsg.Text = "Vérification impossible : " + UpdateService.Describe(ex);
        }
        finally { CheckBtn.IsEnabled = true; }
    }

    private async void Download_Click(object sender, RoutedEventArgs e) => await _updates.PromptAndInstallAsync();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
