using System.Windows;
using System.Windows.Interop;

namespace PokeWidget.Desktop;

internal static class DarkChrome
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>Barre de titre sombre (Windows 10 20H1+ / 11). Sans effet sur les versions plus anciennes.</summary>
    public static void Apply(Window w) => w.SourceInitialized += (_, _) =>
    {
        int on = 1;
        try { Native.DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)); }
        catch (Exception) { /* cosmétique */ }
    };
}
