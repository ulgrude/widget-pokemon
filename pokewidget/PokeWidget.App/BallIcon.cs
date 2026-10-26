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

    private readonly Ellipse _body = new();
    private readonly Ellipse _centerRing = new();
    private readonly Ellipse _centerButton = new();

    public BallIcon()
    {
        Width = Height = 24;
        _body.Stroke = new SolidColorBrush(Dark);

        _centerRing.Fill = new SolidColorBrush(Dark);
        _centerRing.HorizontalAlignment = HorizontalAlignment.Center;
        _centerRing.VerticalAlignment = VerticalAlignment.Center;

        _centerButton.Fill = new SolidColorBrush(White);
        _centerButton.Stroke = new SolidColorBrush(Dark);
        _centerButton.HorizontalAlignment = HorizontalAlignment.Center;
        _centerButton.VerticalAlignment = VerticalAlignment.Center;

        Children.Add(_body);
        Children.Add(_centerRing);
        Children.Add(_centerButton);

        Loaded += (_, _) => Rebuild();
        Rebuild();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
    }

    private void Rebuild()
    {
        double w = ActualWidth > 0 ? ActualWidth : (double.IsNaN(Width) ? 24 : Width);
        double h = ActualHeight > 0 ? ActualHeight : (double.IsNaN(Height) ? 24 : Height);
        double size = Math.Max(12, Math.Min(w, h));

        double stroke = Math.Max(1.5, Math.Round(size * 0.04 * 2) / 2);
        _body.StrokeThickness = stroke;

        // Ligne médiane noire fine et proportionnelle (~5% du diamètre)
        double halfLine = Math.Max(0.022, (stroke * 0.7) / size);
        double stop1 = Math.Clamp(0.5 - halfLine, 0.46, 0.485);
        double stop2 = Math.Clamp(0.5 + halfLine, 0.515, 0.54);

        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(TopColor, 0));
        b.GradientStops.Add(new GradientStop(TopColor, stop1));
        b.GradientStops.Add(new GradientStop(Dark, stop1));
        b.GradientStops.Add(new GradientStop(Dark, stop2));
        b.GradientStops.Add(new GradientStop(White, stop2));
        b.GradientStops.Add(new GradientStop(White, 1));
        _body.Fill = b;

        // Bouton central bien proportionné (anneau noir 32% + bouton blanc 23%)
        double ringSize = Math.Round(size * 0.32);
        double buttonSize = Math.Round(size * 0.23);

        _centerRing.Width = _centerRing.Height = ringSize;
        _centerButton.Width = _centerButton.Height = buttonSize;
    }
}
