using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PokeWidget.Core;

namespace PokeWidget.Desktop;

public partial class DexWindow : Window
{
    private sealed class CardInfo
    {
        public required Border Card { get; init; }
        public required Image Img { get; init; }
        public required int Id { get; init; }
        public required string Name { get; init; }
        public required bool Shiny { get; init; }
        public bool Loaded { get; set; }
    }

    private static readonly SemaphoreSlim Gate = new(6); // au plus 6 téléchargements de sprites en parallèle
    private readonly SpriteCache _sprites;
    private readonly List<CardInfo> _cards = new();
    private bool _closed;

    public DexWindow(GameState state, SpriteCache sprites)
    {
        InitializeComponent();
        DarkChrome.Apply(this);
        _sprites = sprites;
        Closed += (_, _) => _closed = true;

        var ids = state.Dex.Keys.OrderBy(i => i).ToList();
        int total = state.Dex.Values.Sum(e => e.Count);
        TitleText.Text = $"Pokédex : {ids.Count} / {GameConfig.MaxPokemonId} espèces, {total} captures";

        if (ids.Count == 0)
        {
            EmptyText.Text = "Aucun Pokémon capturé pour le moment. Lance une ball sur celui qui est apparu.";
            EmptyText.Visibility = Visibility.Visible;
            SearchGrid.Visibility = Visibility.Collapsed;
            Scroll.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (int id in ids) AddCard(id, state.Dex[id]);
        Loaded += (_, _) => { Search.Focus(); LoadVisible(); };
    }

    private void AddCard(int id, DexEntry e)
    {
        var img = new Image { Width = 72, Height = 72, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

        var panel = new StackPanel();
        panel.Children.Add(img);
        panel.Children.Add(new TextBlock { Text = $"#{id}", FontSize = 11, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = e.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = $"×{e.Count}" + (e.Shiny > 0 ? $"  ✨×{e.Shiny}" : ""),
            FontSize = 12, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center,
        });

        var card = new Border
        {
            Width = 104, Margin = new Thickness(4), Padding = new Thickness(4, 8, 4, 8),
            CornerRadius = new CornerRadius(10), Child = panel,
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
        };
        Cards.Children.Add(card);
        _cards.Add(new CardInfo { Card = card, Img = img, Id = id, Name = e.Name, Shiny = e.Shiny > 0 });
    }

    // Les sprites ne sont chargés que lorsque la carte approche de la zone visible.
    private void LoadVisible()
    {
        if (_closed) return;
        double h = Scroll.ViewportHeight;
        foreach (var c in _cards)
        {
            if (c.Loaded || c.Card.Visibility != Visibility.Visible) continue;
            double y;
            try { y = c.Card.TransformToAncestor(Scroll).Transform(new Point(0, 0)).Y; }
            catch (InvalidOperationException) { continue; } // pas encore dans l'arbre visuel
            if (y + c.Card.ActualHeight < -150 || y > h + 150) continue;
            c.Loaded = true;
            _ = LoadImageAsync(c);
        }
    }

    private async Task LoadImageAsync(CardInfo c)
    {
        await Gate.WaitAsync();
        try
        {
            string? path = await _sprites.GetFileAsync(SpriteUrls.Pixel(c.Id, c.Shiny));
            if (path is null || _closed) return;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            c.Img.Source = bmp;
        }
        catch (Exception) { c.Loaded = false; } // nouvel essai au prochain défilement
        finally { Gate.Release(); }
    }

    private void Scroll_Changed(object sender, ScrollChangedEventArgs e) => LoadVisible();

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        Hint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        int shown = 0;
        foreach (var c in _cards)
        {
            // hasShiny = la card a un shiny dans le dex (on le déduit de c.Shiny qui indique e.Shiny > 0)
            bool ok = DexSearch.Matches(c.Id, c.Name, c.Shiny, Search.Text);
            c.Card.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            if (ok) shown++;
        }
        NoResult.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(LoadVisible));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
