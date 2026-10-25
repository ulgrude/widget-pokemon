using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PokeWidget.Core;

namespace PokeWidget.Desktop;

public partial class HelpWindow : Window
{
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xFF, 0xCB, 0x05));

    public HelpWindow()
    {
        InitializeComponent();
        DarkChrome.Apply(this);

        // Les chiffres sont lus dans GameConfig : l'aide reste exacte si on les modifie.
        int slotMin = (int)(GameConfig.SlotMs / 60_000);
        int ballH = (int)(GameConfig.BallMs / 3_600_000);
        int bonusPct = (int)Math.Round(GameConfig.BonusChance * 100);
        int shinyDen = (int)Math.Round(1 / GameConfig.ShinyRate);

        H("Le Pokémon du moment", first: true);
        P($"Un nouveau Pokémon apparaît toutes les {slotMin} minutes. Il est tiré à partir de la seed : toutes les personnes qui ont la même seed voient le même Pokémon au même moment. La seed se change dans les réglages.");
        P($"Chaque apparition a 1 chance sur {shinyDen} d'être chromatique (✨).");

        H("Capturer");
        Li("Choisis une ball et lance-la : le pourcentage affiché dessous est ta chance de capture.");
        Li("Chaque lancer consomme une ball, qu'il réussisse ou non. Si c'est raté, tu peux réessayer tant qu'il te reste des balls et du temps.");
        Li("Tu ne peux capturer qu'un Pokémon par apparition. Une fois capturé, il s'estompe jusqu'au suivant.");
        Li("Plus le taux de capture du Pokémon est bas, plus il est difficile à attraper.");

        H("Les balls");
        Li("Poké Ball : chance de base.", Ball(BallType.Poke));
        Li("Super Ball : ×1,5 sur la chance de capture.", Ball(BallType.Super));
        Li("Hyper Ball : ×2 sur la chance de capture.", Ball(BallType.Hyper));
        P($"Tu démarres avec {GameConfig.StartBalls} Poké Balls, et tu ne peux pas dépasser {GameConfig.BallCap} balls de chaque type.");

        H("Gagner des balls");
        Li($"+1 Poké Ball toutes les {ballH} heure{(ballH > 1 ? "s" : "")}. Le temps compte même quand l'application est fermée : les balls se rattrapent au lancement.");
        Li($"Bonus : chaque heure, chaque capture de ton Pokédex a {bonusPct} % de chance de te donner une ball supplémentaire. Capturer deux fois la même espèce compte deux fois.");
        Li($"Le type de ball offerte dépend du taux de capture de l'espèce : Poké Ball à partir de 120, Super Ball de 46 à 119, Hyper Ball à 45 ou moins. Les Pokémon difficiles à attraper rapportent donc de meilleures balls.");

        H("Pokédex et sauvegarde");
        P("Le Pokédex liste tes captures, avec une recherche par nom ou numéro. Dans les réglages, tu peux exporter ta progression dans un fichier .pokesave et la réimporter plus tard.");

        H("Réduire le widget");
        P("Le bouton − (ou le clic droit) réduit le widget à une Poké Ball. Elle se balance quand un nouveau Pokémon est apparu, et brille en doré quand un chromatique est là.");
    }

    private void H(string text, bool first = false) => Body.Children.Add(new TextBlock
    {
        Text = text, FontWeight = FontWeights.SemiBold, Foreground = Accent,
        Margin = new Thickness(0, first ? 0 : 16, 0, 4),
    });

    private void P(string text) => Body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });

    private void Li(string text, UIElement? icon = null)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var lead = icon ?? new TextBlock { Text = "•", HorizontalAlignment = HorizontalAlignment.Center };
        if (lead is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Left; fe.VerticalAlignment = VerticalAlignment.Top; fe.Margin = new Thickness(icon is null ? 10 : 0, 0, 0, 0); }
        Grid.SetColumn(lead, 0);
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 1);
        row.Children.Add(lead);
        row.Children.Add(t);
        Body.Children.Add(row);
    }

    private static BallIcon Ball(BallType t) => new()
    {
        Width = 20, Height = 20,
        TopColor = t switch { BallType.Poke => Color.FromRgb(0xE5, 0x39, 0x35), BallType.Super => Color.FromRgb(0x1E, 0x88, 0xE5), _ => Color.FromRgb(0xF9, 0xC6, 0x2B) },
    };

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
