using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;
using Draw = System.Drawing;

namespace PokeWidget.Desktop;

/// <summary>Icône de la zone de notification (NotifyIcon WinForms) : seul usage de WinForms dans le projet.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _update = new() { Visible = false };
    private readonly WinForms.ToolStripSeparator _updateSep = new() { Visible = false };

    public TrayIcon(Action showWidget, Action openSettings, Action openDex, Action openShop, Action openHelp,
                    Action toggleCollapsed, Func<bool> isCollapsed, Action quit, Action openUpdate)
    {
        var menu = new WinForms.ContextMenuStrip();
        _update.Click += (_, _) => openUpdate();
        menu.Items.Add(_update);
        menu.Items.Add(_updateSep);
        var toggle = new WinForms.ToolStripMenuItem();
        toggle.Click += (_, _) => toggleCollapsed();
        menu.Items.Add(toggle);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Pokédex", null, (_, _) => openDex());
        menu.Items.Add("Boutique", null, (_, _) => openShop());
        menu.Items.Add("Réglages…", null, (_, _) => openSettings());
        menu.Items.Add("Comment jouer ?", null, (_, _) => openHelp());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => quit());
        menu.Opening += (_, _) => toggle.Text = isCollapsed() ? "Agrandir le widget" : "Réduire le widget";

        _icon = new WinForms.NotifyIcon { Icon = BuildIcon(), Text = "Widget Pokémon", ContextMenuStrip = menu, Visible = true };
        _icon.BalloonTipClicked += (_, _) => openUpdate();
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) showWidget(); };
    }

    // Poké Ball dessinée à la volée : pas de fichier .ico à embarquer.
    private static Draw.Icon BuildIcon()
    {
        using var bmp = new Draw.Bitmap(32, 32);
        using (var g = Draw.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var red = new Draw.SolidBrush(Draw.Color.FromArgb(0xE5, 0x39, 0x35));
            using var white = new Draw.SolidBrush(Draw.Color.FromArgb(0xF4, 0xF4, 0xF4));
            using var dark = new Draw.SolidBrush(Draw.Color.FromArgb(0x15, 0x18, 0x1B));
            using var pen = new Draw.Pen(dark, 3);
            g.FillPie(red, 3, 3, 26, 26, 180, 180);
            g.FillPie(white, 3, 3, 26, 26, 0, 180);
            g.FillRectangle(dark, 3, 14, 26, 4);
            g.DrawEllipse(pen, 3, 3, 26, 26);
            g.FillEllipse(white, 11, 11, 10, 10);
            g.DrawEllipse(pen, 11, 11, 10, 10);
        }
        IntPtr h = bmp.GetHicon();
        try { using var tmp = Draw.Icon.FromHandle(h); return (Draw.Icon)tmp.Clone(); }
        finally { Native.DestroyIcon(h); }
    }

    /// <summary>Entrée de menu "mise à jour disponible" (null pour la masquer).</summary>
    public void SetUpdate(string? text)
    {
        _update.Visible = _updateSep.Visible = text is not null;
        if (text is not null) _update.Text = text;
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(6000, title, text, WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
