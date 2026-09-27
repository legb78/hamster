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
    readonly ActivityHub _hub;
    readonly StartupRegistration _startup = new();
    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu;
    readonly RegisteredWaitHandle _quitWait;
    IntPtr _iconHandle;

    // NotifyIcon n'expose pas l'ouverture de son menu. Sa methode privee fait le
    // SetForegroundWindow sans lequel le menu ne se ferme pas quand on clique ailleurs
    static readonly MethodInfo? ShowTrayMenu = typeof(NotifyIcon).GetMethod("ShowContextMenu",
        BindingFlags.Instance | BindingFlags.NonPublic);

    ToolStripMenuItem _waitingItem = null!;
    ToolStripSeparator _waitingSeparator = null!;
    ToolStripMenuItem _pauseItem = null!;
    ToolStripMenuItem _claudeItem = null!;
    ToolStripMenuItem _startupItem = null!;
    ToolStripMenuItem _soundItem = null!;
    ToolStripMenuItem _debugItem = null!;
    readonly List<(ToolStripMenuItem Item, int Value)> _chillItems = new();
    // sans effet visible tant que le hamster est cache : grises pendant l'attente
    readonly List<ToolStripItem> _petOnlyItems = new();
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
        // MouseUp et pas MouseClick : WinForms ne leve pas MouseClick au second
        // relachement d'un double-clic, et le menu ouvert au premier se refermerait
        _tray.MouseUp += OnTrayClick;
        _settings.Unreadable += ShowUnreadable;

        _controller.ClaudePresenceChanged += _ => UpdateTrayText();
        _controller.Start();
        UpdateTrayText();

        // apres Start : la fenetre a son handle, le hub peut y renvoyer ses lots
        _hub = new ActivityHub(_controller.Window);
        _controller.Attach(_hub);
        _hub.Start();
        if (_settings.LoadError != null) ShowUnreadable(_settings.LoadError, _settings.LoadBackedUp);
        if (ShowTrayMenu == null) Diagnostics.Warn("NotifyIcon.ShowContextMenu introuvable, repli sur Menu.Show");

        // le rappel arrive sur un thread du pool : on repasse sur le thread UI, ou
        // vivent la boucle de messages et l'icone. La fenetre a son handle depuis Start
        var window = _controller.Window;
        _quitWait = ThreadPool.RegisterWaitForSingleObject(quit, (_, _) =>
        {
            Program.ExitReason = "signal d'arret (script)";
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
    void ShowUnreadable(string error, bool backedUp)
    {
        string detail = error.Length > 120 ? error[..120] + "..." : error;
        string where = backedUp
            ? "Ta version est gardee dans settings.json.bad. "
            : "Copie de secours impossible : corrige le fichier avant de toucher au menu. ";
        _tray.ShowBalloonTip(8000, "Hamster : settings.json illisible", where + detail, ToolTipIcon.Warning);
    }

    // l'icone reste dans la zone de notification pendant l'attente : c'est le seul
    // moyen de quitter ou de changer de mode tant que le hamster est cache
    void UpdateTrayText() =>
        _tray.Text = _controller.WaitingForClaude ? "Hamster - attend Claude Desktop" : "Hamster";

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };

        // sans ca, un menu ouvert pendant l'attente laisse croire que le hamster est
        // quelque part et que ses reglages ne marchent pas
        _waitingItem = new ToolStripMenuItem("En attente de Claude Desktop") { Enabled = false };
        _waitingSeparator = new ToolStripSeparator();
        menu.Items.Add(_waitingItem);
        menu.Items.Add(_waitingSeparator);

        _pauseItem = new ToolStripMenuItem("Pause", null, (_, _) =>
            _controller.SetUserPaused(!_settings.Paused)) { CheckOnClick = false };
        menu.Items.Add(_pauseItem);

        _claudeItem = new ToolStripMenuItem("Seulement avec Claude Desktop", null, (_, _) =>
            _controller.SetOnlyWithClaudeDesktop(!_settings.OnlyWithClaudeDesktop)) { CheckOnClick = false };
        menu.Items.Add(_claudeItem);

        // la valeur Run n'est ecrite ou retiree qu'ici, au clic : jamais au lancement
        _startupItem = new ToolStripMenuItem("Lancer au demarrage", null, (_, _) => ToggleStartup()) { CheckOnClick = false };
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());

        var size = new ToolStripMenuItem("Taille");
        foreach (int s in new[] { 1, 2, 3 })
        {
            var item = new ToolStripMenuItem($"{s}x", null, (_, _) => _controller.ApplyScale(s));
            _scaleItems.Add((item, s));
            size.DropDownItems.Add(item);
        }
        menu.Items.Add(size);
        _petOnlyItems.Add(size);

        var opacity = new ToolStripMenuItem("Opacite");
        foreach (int p in new[] { 100, 75, 50 })
        {
            var item = new ToolStripMenuItem($"{p} %", null, (_, _) => _controller.ApplyOpacity(p));
            _opacityItems.Add((item, p));
            opacity.DropDownItems.Add(item);
        }
        menu.Items.Add(opacity);
        _petOnlyItems.Add(opacity);

        var chill = new ToolStripMenuItem("Delai avant le mode chill");
        foreach (int d in new[] { 5, 10, 30, 60 })
        {
            var item = new ToolStripMenuItem($"{d} s", null, (_, _) => _controller.ApplyChillDelay(d));
            _chillItems.Add((item, d));
            chill.DropDownItems.Add(item);
        }
        menu.Items.Add(chill);

        _soundItem = new ToolStripMenuItem("Son", null, (_, _) => _controller.SetSound(!_settings.SoundEnabled)) { CheckOnClick = false };
        menu.Items.Add(_soundItem);

        var bringHere = new ToolStripMenuItem("Ramene-le ici", null, (_, _) => _controller.BringHere());
        menu.Items.Add(bringHere);
        _petOnlyItems.Add(bringHere);
        menu.Items.Add(new ToolStripSeparator());

        _debugItem = new ToolStripMenuItem("Overlay de debug", null,
            (_, _) => _controller.ToggleDebugOverlay());
        menu.Items.Add(_debugItem);
        _petOnlyItems.Add(_debugItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quitter", null, (_, _) =>
        {
            Program.ExitReason = "menu Quitter";
            ExitThreadCore();
        }));

        menu.Opening += (_, _) => SyncMenuState();
        return menu;
    }

    void SyncMenuState()
    {
        bool waiting = _controller.WaitingForClaude;
        _waitingItem.Visible = waiting;
        _waitingSeparator.Visible = waiting;
        foreach (var item in _petOnlyItems) item.Enabled = !waiting;

        _pauseItem.Checked = _settings.Paused;
        _claudeItem.Checked = _settings.OnlyWithClaudeDesktop;
        _soundItem.Checked = _settings.SoundEnabled;
        _debugItem.Checked = _settings.DebugOverlay;
        // relu a chaque ouverture : l'entree peut avoir ete retiree (uninstall.ps1) ou
        // desactivee (Gestionnaire des taches) ailleurs
        try { _startupItem.Checked = _startup.IsEnabled; }
        catch (Exception e) { Diagnostics.Warn("lecture de la cle Run: " + e.Message); }
        foreach (var (item, value) in _scaleItems) item.Checked = _settings.Scale == value;
        foreach (var (item, value) in _opacityItems) item.Checked = _settings.OpacityPercent == value;
        foreach (var (item, value) in _chillItems) item.Checked = _settings.ChillDelaySeconds == value;
    }

    void ToggleStartup()
    {
        try
        {
            bool on = _startup.Toggle();
            Diagnostics.Info($"lancer au demarrage: {(on ? "active" : "retire")} ({_startup.ValueName} = {_startup.CurrentValue ?? "absent"}" +
                (_startup.DisabledByTaskManager ? ", desactive dans le Gestionnaire des taches" : "") + ")");
        }
        catch (Exception e)
        {
            Diagnostics.Warn("cle Run non modifiee: " + e.Message);
            _tray.ShowBalloonTip(6000, "Hamster", "Lancement au demarrage non modifie : " + e.Message, ToolTipIcon.Warning);
        }
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
            // les watchers d'abord : plus aucun lot ne doit viser une fenetre en cours de destruction
            _hub.Dispose();
            _controller.Dispose();
            if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        }
        base.Dispose(disposing);
    }
}
