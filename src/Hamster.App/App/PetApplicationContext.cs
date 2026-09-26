using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using Hamster.App.Window;
using Hamster.Art;

namespace Hamster.App;

/// <summary>Cycle de vie : reglages, controleur, icone de notification, menu.</summary>
internal sealed class PetApplicationContext : ApplicationContext
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

    readonly Settings _settings;
    readonly PetController _controller;
    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu;
    readonly RegisteredWaitHandle _quitWait;
    IntPtr _iconHandle;

    // NotifyIcon n'expose pas l'ouverture de son menu. Sa methode privee fait le
    // SetForegroundWindow sans lequel le menu ne se ferme pas quand on clique ailleurs
    static readonly MethodInfo? ShowTrayMenu = typeof(NotifyIcon).GetMethod("ShowContextMenu",
        BindingFlags.Instance | BindingFlags.NonPublic);

    ToolStripMenuItem _pauseItem = null!;
    ToolStripMenuItem _claudeItem = null!;
    ToolStripMenuItem _debugItem = null!;
    readonly List<(ToolStripMenuItem Item, int Value)> _scaleItems = new();
    readonly List<(ToolStripMenuItem Item, int Value)> _opacityItems = new();

    public PetApplicationContext(EventWaitHandle quit)
    {
        _settings = Settings.Load();
        _controller = new PetController(_settings);
        _menu = BuildMenu();
        _controller.Menu = _menu;

        _tray = new NotifyIcon
        {
            Icon = BuildTrayIcon(),
            Text = "Hamster",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _tray.MouseClick += OnTrayClick;
        _settings.Unreadable += ShowUnreadable;

        _controller.ClaudePresenceChanged += _ => UpdateTrayText();
        _controller.Start();
        UpdateTrayText();
        if (_settings.LoadError != null) ShowUnreadable(_settings.LoadError);
        if (ShowTrayMenu == null) Diagnostics.Warn("NotifyIcon.ShowContextMenu introuvable, repli sur Menu.Show");

        // le rappel arrive sur un thread du pool : on repasse sur le thread UI, ou
        // vivent la boucle de messages et l'icone. La fenetre a son handle depuis Start
        var window = _controller.Window;
        _quitWait = ThreadPool.RegisterWaitForSingleObject(quit, (_, _) =>
        {
            try { window.BeginInvoke(new Action(ExitThreadCore)); }
            catch (Exception e) { Diagnostics.Warn("arret propre impossible: " + e.Message); }
        }, null, Timeout.Infinite, executeOnlyOnce: true);
    }

    void OnTrayClick(object? sender, MouseEventArgs e)
    {
        // le clic droit ouvre deja le menu ; le gauche, celui qu'on tente en premier,
        // ne faisait rien
        if (e.Button != MouseButtons.Left) return;
        if (ShowTrayMenu != null) ShowTrayMenu.Invoke(_tray, null);
        else _menu.Show(Cursor.Position);
    }

    // stderr n'est visible nulle part pour un WinExe lance a l'ouverture de session :
    // une notification Windows est le seul moyen de dire que le fichier est casse
    void ShowUnreadable(string error)
    {
        string detail = error.Length > 120 ? error[..120] + "..." : error;
        _tray.ShowBalloonTip(8000, "Hamster : settings.json illisible",
            "Ta version est gardee dans settings.json.bad. " + detail, ToolTipIcon.Warning);
    }

    // l'icone reste dans la zone de notification pendant l'attente : c'est le seul
    // moyen de quitter ou de changer de mode tant que le hamster est cache
    void UpdateTrayText() =>
        _tray.Text = _controller.WaitingForClaude ? "Hamster - attend Claude Desktop" : "Hamster";

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };

        _pauseItem = new ToolStripMenuItem("Pause", null, (_, _) =>
            _controller.SetUserPaused(!_settings.Paused)) { CheckOnClick = false };
        menu.Items.Add(_pauseItem);

        _claudeItem = new ToolStripMenuItem("Seulement avec Claude Desktop", null, (_, _) =>
            _controller.SetOnlyWithClaudeDesktop(!_settings.OnlyWithClaudeDesktop)) { CheckOnClick = false };
        menu.Items.Add(_claudeItem);
        menu.Items.Add(new ToolStripSeparator());

        var size = new ToolStripMenuItem("Taille");
        foreach (int s in new[] { 1, 2, 3 })
        {
            var item = new ToolStripMenuItem($"{s}x", null, (_, _) => _controller.ApplyScale(s));
            _scaleItems.Add((item, s));
            size.DropDownItems.Add(item);
        }
        menu.Items.Add(size);

        var opacity = new ToolStripMenuItem("Opacite");
        foreach (int p in new[] { 100, 75, 50 })
        {
            var item = new ToolStripMenuItem($"{p} %", null, (_, _) => _controller.ApplyOpacity(p));
            _opacityItems.Add((item, p));
            opacity.DropDownItems.Add(item);
        }
        menu.Items.Add(opacity);

        menu.Items.Add(new ToolStripMenuItem("Ramene-le ici", null, (_, _) => _controller.BringHere()));
        menu.Items.Add(new ToolStripSeparator());

        _debugItem = new ToolStripMenuItem("Overlay de debug", null,
            (_, _) => _controller.ToggleDebugOverlay());
        menu.Items.Add(_debugItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quitter", null, (_, _) => ExitThreadCore()));

        menu.Opening += (_, _) => SyncMenuState();
        return menu;
    }

    void SyncMenuState()
    {
        _pauseItem.Checked = _settings.Paused;
        _claudeItem.Checked = _settings.OnlyWithClaudeDesktop;
        _debugItem.Checked = _settings.DebugOverlay;
        foreach (var (item, value) in _scaleItems) item.Checked = _settings.Scale == value;
        foreach (var (item, value) in _opacityItems) item.Checked = _settings.OpacityPercent == value;
    }

    Icon BuildTrayIcon()
    {
        var mini = PixelCanvas.Downscale2x(HamsterSprite.Render(Pose.Default));
        const int n = PixelCanvas.Size / 2;
        using var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        unsafe
        {
            uint* p = (uint*)data.Scan0;
            int stride = data.Stride / 4;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                p[y * stride + x] = Palette.Argb[mini[y * n + x]];
        }
        bmp.UnlockBits(data);
        _iconHandle = bmp.GetHicon();
        return Icon.FromHandle(_iconHandle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _quitWait.Unregister(null);
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            _controller.Dispose();
            if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        }
        base.Dispose(disposing);
    }
}
