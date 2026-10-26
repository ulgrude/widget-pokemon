using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PokeWidget.Core;
using XamlAnimatedGif;

namespace PokeWidget.Desktop;

internal sealed record CurrentPokemon(Spawn Spawn, SpeciesInfo Info);

public partial class WidgetWindow : Window
{
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xCB, 0x05);

    private readonly GameRepository _repo;
    private readonly SpeciesService _species;
    private readonly SpriteCache _sprites;
    private readonly UpdateCoordinator _updates;
    private int _ticks;
    private readonly Random _rng = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _msgTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private readonly Dictionary<BallType, (Button Btn, TextBlock Count, TextBlock Pct)> _balls = new();

    private CurrentPokemon? _current;
    private long? _loadingSlot;
    private long _lastFailMs;
    private bool _busy;

    // Icône réduite
    private readonly DropShadowEffect _shadowFx = new() { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.35 };
    private readonly DropShadowEffect _glowFx = new() { Color = Gold, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.5 };
    private bool _wobbling, _glowing;
    private MenuItem _collapseItem = null!;
    private readonly Dictionary<DisplayMode, MenuItem> _modeItems = new();
    private readonly Dictionary<WidgetPosition, MenuItem> _posItems = new();

    private GameState State => _repo.State;
    private SettingsModel Settings => _repo.Settings;

    public WidgetWindow(GameRepository repo, SpeciesService species, SpriteCache sprites, UpdateCoordinator updates)
    {
        _repo = repo; _species = species; _sprites = sprites; _updates = updates;
        InitializeComponent();
        BuildBallButtons();
        BuildContextMenu();

        SizeChanged += (_, _) => PositionWindow();
        SystemParameters.StaticPropertyChanged += (_, e) => { if (e.PropertyName == nameof(SystemParameters.WorkArea)) PositionWindow(); };
        // Au cas où "Afficher le bureau" (Win+D) réduirait la fenêtre : on la remet en place.
        StateChanged += (_, _) => { if (WindowState != WindowState.Normal) WindowState = WindowState.Normal; };
        _msgTimer.Tick += (_, _) => { _msgTimer.Stop(); Msg.Text = ""; };
        _tick.Tick += (_, _) => OnTick();

        // Rattrapage des balls dès le lancement
        if (State.TickBalls(GameConfig.NowMs(), _rng)) _repo.SaveState();

        _updates.Changed += RefreshUpdateUi;
        RefreshUpdateUi();
        ApplyCollapsed();
        Render();
        _ = LoadSpawnAsync();
        _tick.Start();
    }

    /* ---------------------- Fenêtre : style, épinglage bureau ---------------------- */

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        ApplyMode();
        PositionWindow();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Mode "sur le bureau" : à chaque changement de position, on force la fenêtre tout en bas
        // de l'empilement (donc au-dessus des icônes du bureau, sous toutes les autres fenêtres).
        if (msg == Native.WM_WINDOWPOSCHANGING && Settings.Mode == DisplayMode.Desktop)
        {
            var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
            pos.hwndInsertAfter = Native.HWND_BOTTOM;
            pos.flags &= ~Native.SWP_NOZORDER;
            Marshal.StructureToPtr(pos, lParam, false);
        }
        return IntPtr.Zero;
    }

    private void ApplyMode()
    {
        bool desktop = Settings.Mode == DisplayMode.Desktop;
        Topmost = !desktop;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (desktop && hwnd != IntPtr.Zero)
            Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void PositionWindow()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var wa = SystemParameters.WorkArea;
        const double edge = 4; // + 12 de marge intégrée à la fenêtre (ombre) = 16 px du bord, comme le userscript
        bool right = Settings.Position is WidgetPosition.TopRight or WidgetPosition.BottomRight;
        bool bottom = Settings.Position is WidgetPosition.BottomLeft or WidgetPosition.BottomRight;
        Left = right ? wa.Right - ActualWidth - edge : wa.Left + edge;
        Top = bottom ? wa.Bottom - ActualHeight - edge : wa.Top + edge;
    }

    /* ------------------------------ Menu contextuel ------------------------------ */

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        _collapseItem = new MenuItem();
        _collapseItem.Click += (_, _) => SetCollapsed(!Settings.Collapsed);
        menu.Items.Add(_collapseItem);
        menu.Items.Add(new Separator());
        foreach (var (label, open) in new (string, Action)[] { ("Pokédex", (Action)OpenDex), ("Boutique", (Action)OpenShop), ("Réglages…", (Action)OpenSettings), ("Comment jouer ?", (Action)OpenHelp) })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => open();
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());

        foreach (var (mode, label) in new[] { (DisplayMode.Desktop, "Sur le bureau (sous les fenêtres)"), (DisplayMode.Topmost, "Toujours au-dessus") })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { Settings.Mode = mode; _repo.SaveSettings(); ApplyMode(); };
            _modeItems[mode] = item; menu.Items.Add(item);
        }

        var posMenu = new MenuItem { Header = "Position" };
        var labels = new Dictionary<WidgetPosition, string>
        {
            [WidgetPosition.TopLeft] = "Haut gauche", [WidgetPosition.TopRight] = "Haut droite",
            [WidgetPosition.BottomLeft] = "Bas gauche", [WidgetPosition.BottomRight] = "Bas droite",
        };
        foreach (var (pos, label) in labels)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { Settings.Position = pos; _repo.SaveSettings(); PositionWindow(); };
            _posItems[pos] = item; posMenu.Items.Add(item);
        }
        menu.Items.Add(posMenu);
        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "Quitter" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);

        menu.Opened += (_, _) =>
        {
            _collapseItem.Header = Settings.Collapsed ? "Agrandir" : "Réduire";
            foreach (var (m, item) in _modeItems) item.IsChecked = Settings.Mode == m;
            foreach (var (p, item) in _posItems) item.IsChecked = Settings.Position == p;
        };
        FullCard.ContextMenu = menu;
        MiniHost.ContextMenu = menu;
    }

    /* --------------------------- Fenêtres secondaires --------------------------- */

    private DexWindow? _dexWin;
    private ShopWindow? _shopWin;
    private HelpWindow? _helpWin;
    private SettingsWindow? _settingsWin;

    // Une seule fenêtre de chaque type : si elle est déjà ouverte, on la ramène devant.
    private static void ShowSingle<T>(ref T? slot, Func<T> create, Action<T> onClosed) where T : Window
    {
        if (slot is not null)
        {
            if (slot.WindowState == WindowState.Minimized) slot.WindowState = WindowState.Normal;
            slot.Activate();
            return;
        }
        var w = create();
        w.Closed += (_, _) => onClosed(w);
        slot = w;
        w.Show();
        w.Activate();
    }

    public void OpenDex() => ShowSingle(ref _dexWin, () => new DexWindow(State, _sprites), _ => _dexWin = null);
    public void OpenShop() => ShowSingle(ref _shopWin, () => new ShopWindow(_repo, _sprites, _species, () => { Render(); _shopWin?.RefreshUi(); }), _ => _shopWin = null);
    public void OpenHelp() => ShowSingle(ref _helpWin, () => new HelpWindow(), _ => _helpWin = null);
    public void OpenSettings() => ShowSingle(ref _settingsWin, () => new SettingsWindow(_repo, _updates, OnSettingsChanged), _ => _settingsWin = null);

    public bool IsCollapsed => Settings.Collapsed;
    public void ToggleCollapsed() => SetCollapsed(!Settings.Collapsed);
    public void ExpandWidget() { if (Settings.Collapsed) SetCollapsed(false); }

    private void OnSettingsChanged(bool reloadPokemon)
    {
        PositionWindow();
        ApplyMode();
        if (reloadPokemon)
        {
            _current = null;
            _loadingSlot = null;
            Sprite.Visibility = Visibility.Collapsed;
            _ = LoadSpawnAsync();
        }
        Render();
    }

    private void Help_Click(object sender, RoutedEventArgs e) => OpenHelp();
    private void Dex_Click(object sender, RoutedEventArgs e) => OpenDex();
    private void Shop_Click(object sender, RoutedEventArgs e) => OpenShop();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void RefreshUpdateUi()
    {
        var st = _updates.Status;
        if (_updates.IsInstalling)
        {
            UpdateLink.Visibility = Visibility.Visible;
            UpdateRun.Text = $"⬇ Téléchargement… {_updates.InstallProgress} %";
            return;
        }
        UpdateLink.Visibility = st.Available ? Visibility.Visible : Visibility.Collapsed;
        if (st.Available) UpdateRun.Text = $"⬆ Mise à jour {st.LatestVersion} disponible";
    }

    private async void Update_Click(object sender, RoutedEventArgs e) => await _updates.PromptAndInstallAsync();

    /* ------------------------------ Boutons de balls ------------------------------ */

    private static Color BallColor(BallType t) => t switch
    {
        BallType.Poke => Color.FromRgb(0xE5, 0x39, 0x35),
        BallType.Super => Color.FromRgb(0x1E, 0x88, 0xE5),
        _ => Color.FromRgb(0xF9, 0xC6, 0x2B),
    };

    private void BuildBallButtons()
    {
        var style = (Style)FindResource("BallButton");
        foreach (var t in Enum.GetValues<BallType>())
        {
            var count = new TextBlock { Text = "0", FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            var pct = new TextBlock { FontSize = 11, Opacity = 0.65, MinHeight = 15, HorizontalAlignment = HorizontalAlignment.Center };
            var panel = new StackPanel();
            panel.Children.Add(new BallIcon { TopColor = BallColor(t), HorizontalAlignment = HorizontalAlignment.Center });
            panel.Children.Add(count);
            panel.Children.Add(pct);

            var btn = new Button { Style = style, Content = panel, Margin = new Thickness(3, 0, 3, 0), ToolTip = BallInfo.Label(t) };
            AutomationProperties.SetName(btn, BallInfo.Label(t));
            var type = t;
            btn.Click += (_, _) => OnBallClick(type);
            BallsPanel.Children.Add(btn);
            _balls[t] = (btn, count, pct);
        }
    }

    /* ----------------------------------- Jeu ----------------------------------- */

    private async void OnBallClick(BallType type)
    {
        var cur = _current;
        if (_busy || cur is null || State.IsCaught(cur.Spawn.Slot) || State.Balls[type] <= 0) return;

        _busy = true;
        var outcome = State.Throw(type, cur.Spawn, cur.Info, _rng); // la ball est consommée tout de suite...
        _repo.SaveState();
        PlayShake();
        Render();

        await Task.Delay(700);                                        // ...mais le résultat s'affiche après l'animation
        _busy = false;
        if (outcome == ThrowOutcome.Caught) Say(cur.Spawn.Shiny ? "✨ Chromatique capturé !" : $"{cur.Info.Name} capturé !");
        else if (outcome == ThrowOutcome.Missed) Say("Raté, il est toujours là.");
        Render();
    }

    private void OnTick()
    {
        long now = GameConfig.NowMs();
        bool ballsChanged = State.TickBalls(now, _rng);
        if (ballsChanged) _repo.SaveState();

        long slot = SpawnService.SlotAt(now);
        if (_current is null || _current.Spawn.Slot != slot)
            if (_lastFailMs == 0 || now - _lastFailMs > 10_000) _ = LoadSpawnAsync();

        if (_ticks < 4) _ticks++; else _ = _updates.AutoCheckAsync(); // ~4 s après le lancement, puis au plus une fois par jour

        MarkSeenIfVisible(slot);
        if (ballsChanged) Render(); else { RenderStatus(); RenderMini(); }
    }

    // Le slot est "vu" dès que le widget est affiché en grand.
    private void MarkSeenIfVisible(long slot)
    {
        if (!Settings.Collapsed && IsVisible && State.MarkSeen(slot)) _repo.SaveState();
    }

    private async Task LoadSpawnAsync()
    {
        long slot = SpawnService.SlotAt(GameConfig.NowMs());
        if (_loadingSlot == slot) return;
        _loadingSlot = slot;
        string seed = Settings.Seed;
        var spawn = SpawnService.For(seed, slot, State.Shop.ShinyBoostUpgrades > 0);
        try
        {
            var info = await _species.GetAsync(spawn.Id);
            if (SpawnService.SlotAt(GameConfig.NowMs()) != slot || seed != Settings.Seed) { _loadingSlot = null; return; }
            _current = new CurrentPokemon(spawn, info);
            _lastFailMs = 0;
            Render();
            await ShowSpriteAsync(spawn);
        }
        catch (Exception)
        {
            _lastFailMs = GameConfig.NowMs();
            _current = null;
            _loadingSlot = null;
        }
        Render();
    }

    /* --------------------------------- Affichage --------------------------------- */

    private bool IsCaughtShown() => !_busy && _current is not null && State.IsCaught(_current.Spawn.Slot);

    private void Render()
    {
        bool caught = IsCaughtShown();
        double effRate = _current is null ? 45 : State.GetEffectiveRate(_current.Info.Rate, _current.Spawn.Slot);
        foreach (var (type, ui) in _balls)
        {
            ui.Count.Text = State.Balls[type].ToString();
            double? pct = _current is null ? null : CatchMath.Probability(effRate, BallInfo.Bonus(type)) * 100;
            ui.Pct.Text = pct is null ? "" : pct < 1 ? "<1 %" : $"{(int)Math.Round(pct.Value, MidpointRounding.AwayFromZero)} %";
            ui.Btn.IsEnabled = !_busy && _current is not null && !caught && State.Balls[type] > 0;
        }

        if (_current is null)
        {
            NameBall.Visibility = Visibility.Collapsed;
            NameText.Text = _lastFailMs != 0 ? "PokéAPI injoignable" : "Chargement…";
            MetaText.Text = "";
            Sprite.Visibility = Visibility.Collapsed;
        }
        else
        {
            bool inDex = State.Dex.ContainsKey(_current.Spawn.Id);
            NameBall.Visibility = inDex ? Visibility.Visible : Visibility.Collapsed;
            NameText.Text = (_current.Spawn.Shiny ? "✨ " : "") + _current.Info.Name;
            string bonusRate = effRate > _current.Info.Rate ? $" (+{(State.MissedCount * 0.02).ToString("0.00", CultureInfo.InvariantCulture)})" : "";
            MetaText.Text = $"Taux de capture : {_current.Info.Rate}{bonusRate}";
            Sprite.Opacity = caught ? 0.22 : 1;
            StageBg.Background = (Brush)FindResource(_current.Spawn.Shiny ? "StageShiny" : "StageNormal");
        }
        RenderStatus();
        RenderMini();
    }

    private static string Fmt(long ms)
    {
        int s = (int)Math.Max(0, Math.Ceiling(ms / 1000.0));
        return $"{s / 60:D2}:{s % 60:D2}";
    }

    private void RenderStatus()
    {
        long now = GameConfig.NowMs();
        long left = SpawnService.SlotEndMs(SpawnService.SlotAt(now)) - now;
        StatusText.Text = _current is null
            ? (_lastFailMs != 0 ? "Nouvelle tentative…" : "")
            : IsCaughtShown() ? $"Capturé. Prochain Pokémon dans {Fmt(left)}" : $"Il reste {Fmt(left)} pour l'attraper";
    }

    private async Task ShowSpriteAsync(Spawn spawn)
    {
        var r = await _sprites.GetAsync(spawn.Id, spawn.Shiny);
        if (_current is null || _current.Spawn != spawn) return; // le Pokémon a changé entre-temps
        if (r is null) { Sprite.Visibility = Visibility.Collapsed; return; }

        RenderOptions.SetBitmapScalingMode(Sprite, r.Kind == SpriteKind.Artwork ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        if (r.Kind == SpriteKind.Animated)
        {
            Sprite.Source = null;
            AnimationBehavior.SetSourceUri(Sprite, new Uri(r.Path, UriKind.Absolute));
        }
        else
        {
            AnimationBehavior.SetSourceUri(Sprite, null);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // libère le fichier (éviction du cache possible)
            bmp.UriSource = new Uri(r.Path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            Sprite.Source = bmp;
        }
        Sprite.Visibility = Visibility.Visible;
    }

    private void Say(string text)
    {
        Msg.Text = text;
        _msgTimer.Stop();
        _msgTimer.Start();
    }

    private void PlayShake()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.7) };
        foreach (var (pct, deg) in new[] { (0.0, 0.0), (0.15, -14.0), (0.35, 12.0), (0.55, -9.0), (0.75, 6.0), (1.0, 0.0) })
            a.KeyFrames.Add(new LinearDoubleKeyFrame(deg, KeyTime.FromPercent(pct)));
        ShakeRot.BeginAnimation(RotateTransform.AngleProperty, a);
    }

    /* ------------------------------- Icône réduite ------------------------------- */

    private void SetCollapsed(bool v)
    {
        Settings.Collapsed = v;
        _repo.SaveSettings();
        ApplyCollapsed();
        MarkSeenIfVisible(SpawnService.SlotAt(GameConfig.NowMs()));
        Render();
    }

    private void ApplyCollapsed()
    {
        FullCard.Visibility = Settings.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        MiniHost.Visibility = Settings.Collapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderMini()
    {
        var m = MiniIcon.Compute(State, Settings.Seed, SpawnService.SlotAt(GameConfig.NowMs()));
        bool alert = m.IsNew || m.Shine;
        Badge.Visibility = alert ? Visibility.Visible : Visibility.Collapsed;
        Badge.Fill = new SolidColorBrush(m.Shine ? Gold : Color.FromRgb(0xFF, 0x52, 0x52));
        MiniDisc.Stroke = new SolidColorBrush(m.Shine ? Gold : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        MiniDisc.Effect = m.Shine ? _glowFx : _shadowFx;
        MiniHost.Opacity = alert || MiniHost.IsMouseOver ? 1 : 0.8;
        MiniBtn.ToolTip = m.Label;
        AutomationProperties.SetName(MiniBtn, m.Label);

        bool anim = SystemParameters.ClientAreaAnimation && Settings.Collapsed;
        SetWobble(m.IsNew && anim);
        SetGlow(m.Shine && anim);
    }

    private void SetWobble(bool on)
    {
        if (on == _wobbling) return;
        _wobbling = on;
        if (!on) { MiniRot.BeginAnimation(RotateTransform.AngleProperty, null); return; }
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(2.6), RepeatBehavior = RepeatBehavior.Forever };
        foreach (var (pct, deg) in new[] { (0.0, 0.0), (0.55, 0.0), (0.62, -18.0), (0.70, 16.0), (0.78, -11.0), (0.86, 6.0), (1.0, 0.0) })
            a.KeyFrames.Add(new LinearDoubleKeyFrame(deg, KeyTime.FromPercent(pct)));
        MiniRot.BeginAnimation(RotateTransform.AngleProperty, a);
    }

    private void SetGlow(bool on)
    {
        if (on == _glowing) return;
        _glowing = on;
        if (!on)
        {
            _glowFx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            _glowFx.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            return;
        }
        DoubleAnimation Pulse(double from, double to) => new(from, to, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        _glowFx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, Pulse(6, 18));
        _glowFx.BeginAnimation(DropShadowEffect.OpacityProperty, Pulse(0.5, 0.95));
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => SetCollapsed(true);
    private void Expand_Click(object sender, RoutedEventArgs e) => SetCollapsed(false);
    private void Mini_Hover(object sender, System.Windows.Input.MouseEventArgs e) => RenderMini();
}
