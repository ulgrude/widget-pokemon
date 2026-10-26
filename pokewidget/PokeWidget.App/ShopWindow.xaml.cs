using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using PokeWidget.Core;
using XamlAnimatedGif;

namespace PokeWidget.Desktop;

public partial class ShopWindow : Window
{
    private readonly GameRepository _repo;
    private readonly SpriteCache _sprites;
    private readonly SpeciesService _species;
    private readonly Action _onStateChanged;
    private readonly Random _rng = new();
    private bool _busyBuying;

    public ShopWindow(GameRepository repo, SpriteCache sprites, SpeciesService species, Action onStateChanged)
    {
        InitializeComponent();
        DarkChrome.Apply(this);
        _repo = repo;
        _sprites = sprites;
        _species = species;
        _onStateChanged = onStateChanged;

        RefreshUi();
    }

    public void RefreshUi()
    {
        var state = _repo.State;
        int avail = state.AvailableDoublons;
        int availShiny = state.AvailableShinyDoublons;

        DoublonsCountText.Text = $"{avail} doublon{(avail > 1 ? "s" : "")}";
        ShinyDoublonsCountText.Text = $"{availShiny} doublon{(availShiny > 1 ? "s" : "")} chromatique{(availShiny > 1 ? "s" : "")}";

        BuyMysteryBtn.IsEnabled = !_busyBuying && avail >= 10;

        int capLvl = state.Shop.BallCapUpgrades;
        BallCapDesc.Text = $"+10 à la capacité maximale de chaque type de ball (actuel : {state.MaxBallCap}).";
        BallCapStatus.Text = capLvl >= 5 ? "Niveau max atteint (150 balls)" : $"Coût : 5 doublons • {capLvl}/5 achats";
        BuyBallCapBtn.Content = capLvl >= 5 ? "Max" : "Acheter";
        BuyBallCapBtn.IsEnabled = !_busyBuying && capLvl < 5 && avail >= 5;

        int missLvl = state.Shop.MissCatchRateUpgrades;
        MissRateStatus.Text = missLvl >= 1 ? "Amélioration active" : "Coût : 100 doublons • 0/1 achat";
        BuyMissRateBtn.Content = missLvl >= 1 ? "Acheté" : "Acheter";
        BuyMissRateBtn.IsEnabled = !_busyBuying && missLvl < 1 && avail >= 100;

        int shinyLvl = state.Shop.ShinyBoostUpgrades;
        ShinyBoostStatus.Text = shinyLvl >= 1 ? "Amélioration active" : "Coût : 1 doublon chromatique • 0/1 achat";
        BuyShinyBoostBtn.Content = shinyLvl >= 1 ? "Acheté" : "Acheter";
        BuyShinyBoostBtn.IsEnabled = !_busyBuying && shinyLvl < 1 && availShiny >= 1;
    }

    private void BuyBallCap_Click(object sender, RoutedEventArgs e)
    {
        if (_repo.State.BuyBallCap())
        {
            _repo.SaveState();
            RefreshUi();
            _onStateChanged();
        }
    }

    private void BuyMissRate_Click(object sender, RoutedEventArgs e)
    {
        if (_repo.State.BuyMissCatchRate())
        {
            _repo.SaveState();
            RefreshUi();
            _onStateChanged();
        }
    }

    private void BuyShinyBoost_Click(object sender, RoutedEventArgs e)
    {
        if (_repo.State.BuyShinyBoost())
        {
            _repo.SaveState();
            RefreshUi();
            _onStateChanged();
        }
    }

    private async void BuyMystery_Click(object sender, RoutedEventArgs e)
    {
        if (_busyBuying || _repo.State.AvailableDoublons < 10) return;
        _busyBuying = true;
        RefreshUi();

        int id = _rng.Next(1, GameConfig.MaxPokemonId + 1);
        double shinyRate = GameConfig.ShinyRate * (_repo.State.Shop.ShinyBoostUpgrades > 0 ? 1.25 : 1.0);
        bool shiny = _rng.NextDouble() < shinyRate;

        // Pré-chargement des infos d'espèce
        SpeciesInfo? info = null;
        try { info = await _species.GetAsync(id); } catch { }
        info ??= new SpeciesInfo($"#{id}", 45);

        // Achat
        _repo.State.BuyMystery(id, info.Name, info.Rate, shiny);
        _repo.SaveState();
        _onStateChanged();

        // Affichage overlay animation
        RevealOverlay.Visibility = Visibility.Visible;
        RevealHeader.Text = "La Poké Ball s'agite…";
        BallStage.Visibility = Visibility.Visible;
        PokemonResultStage.Visibility = Visibility.Collapsed;
        CloseRevealBtn.Visibility = Visibility.Collapsed;

        // Animation de secousse de la Poké Ball
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.2) };
        foreach (var (pct, deg) in new[] { (0.0, 0.0), (0.15, -18.0), (0.35, 16.0), (0.55, -12.0), (0.75, 10.0), (0.9, -5.0), (1.0, 0.0) })
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(deg, KeyTime.FromPercent(pct)));
        RevealBallRot.BeginAnimation(RotateTransform.AngleProperty, shake);

        await Task.Delay(1300);

        // Révélation du Pokémon
        BallStage.Visibility = Visibility.Collapsed;
        PokemonResultStage.Visibility = Visibility.Visible;
        RevealHeader.Text = shiny ? "✨ Un Pokémon chromatique mystère !" : "Pokémon mystère obtenu !";

        RevealNum.Text = $"#{id}";
        RevealName.Text = info.Name;
        RevealShiny.Visibility = shiny ? Visibility.Visible : Visibility.Collapsed;
        ResultBg.Background = (Brush?)TryFindResource(shiny ? "StageShiny" : "StageNormal")
            ?? (shiny ? new SolidColorBrush(Color.FromArgb(0x52, 0xFF, 0xCB, 0x05)) : new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)));

        var entry = _repo.State.Dex[id];
        RevealBadges.Text = $"×{entry.Count}" + (entry.Shiny > 0 ? $"  ✨×{entry.Shiny}" : "") + $"  🌀×{entry.Mystery}";

        // Chargement du sprite
        try
        {
            var r = await _sprites.GetAsync(id, shiny);
            if (r is not null)
            {
                if (r.Kind == SpriteKind.Animated)
                {
                    RevealSprite.Source = null;
                    AnimationBehavior.SetSourceUri(RevealSprite, new Uri(r.Path, UriKind.Absolute));
                }
                else
                {
                    AnimationBehavior.SetSourceUri(RevealSprite, null);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(r.Path, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    RevealSprite.Source = bmp;
                }
            }
        }
        catch { }

        CloseRevealBtn.Visibility = Visibility.Visible;
        _busyBuying = false;
        RefreshUi();
    }

    private void CloseReveal_Click(object sender, RoutedEventArgs e)
    {
        RevealOverlay.Visibility = Visibility.Collapsed;
        RefreshUi();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (RevealOverlay.Visibility == Visibility.Visible && CloseRevealBtn.Visibility == Visibility.Visible)
            {
                RevealOverlay.Visibility = Visibility.Collapsed;
                RefreshUi();
            }
            else if (RevealOverlay.Visibility != Visibility.Visible)
            {
                Close();
            }
        }
    }
}
