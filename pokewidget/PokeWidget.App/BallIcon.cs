using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PokeWidget.Desktop;

/// <summary>Icône de ball dessinée en vectoriel : moitié haute colorée, bande sombre, moitié basse blanche, bouton central.</summary>
public sealed class BallIcon : Grid
{
    private static readonly Color Dark = Color.FromRgb(0x15, 0x18, 0x1B);
    private static readonly Color White = Color.FromRgb(0xF4, 0xF4, 0xF4);

    public static readonly DependencyProperty TopColorProperty = DependencyProperty.Register(
        nameof(TopColor), typeof(Color), typeof(BallIcon),
        new PropertyMetadata(Color.FromRgb(0xE5, 0x39, 0x35), (d, _) => ((BallIcon)d).Rebuild()));

    public Color TopColor { get => (Color)GetValue(TopColorProperty); set => SetValue(TopColorProperty, value); }

    private readonly Ellipse _body = new() { Stroke = new SolidColorBrush(Dark), StrokeThickness = 2 };

    public BallIcon()
    {
        Width = Height = 24;
        var center = new Ellipse
        {
            Width = 8, Height = 8, StrokeThickness = 2,
            Fill = new SolidColorBrush(White), Stroke = new SolidColorBrush(Dark),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(_body);
        Children.Add(center);
        Rebuild();
    }

    private void Rebuild()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(TopColor, 0));
        b.GradientStops.Add(new GradientStop(TopColor, 0.44));
        b.GradientStops.Add(new GradientStop(Dark, 0.44));
        b.GradientStops.Add(new GradientStop(Dark, 0.56));
        b.GradientStops.Add(new GradientStop(White, 0.56));
        b.GradientStops.Add(new GradientStop(White, 1));
        _body.Fill = b;
    }
}
