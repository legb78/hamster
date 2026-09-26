using System.Diagnostics;
using System.Drawing;
using Hamster.App.Render;
using Hamster.App.State;
using Hamster.Art;
using Microsoft.Win32;

namespace Hamster.App.Window;

/// <summary>Cable la fenetre, l'animation, la balade et les evenements systeme.</summary>
internal sealed class PetController : IDisposable
{
    const int Sprite = SpriteLibrary.Size;

    readonly Settings _settings;
    readonly SpriteLibrary _library;
    readonly Animator _animator;
    readonly PetMotion _motion = new();
    readonly LayeredSurface _surface = new();
    readonly PetWindow _window = new();
    readonly VirtualDesktopFollower _desktops = new();
    readonly System.Windows.Forms.Timer _frameTimer = new();
    readonly System.Windows.Forms.Timer _houseTimer = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();

    Screen _screen = Screen.PrimaryScreen!;
    double _lastTickSeconds;
    int _housekeepingTicks;

    // raisons de suspension, cumulatives
    bool _systemAsleep, _sessionLocked, _displayOff;
    // Claude Desktop ferme : la seule raison qui cache aussi la fenetre
    bool _claudeAbsent;

    // drag
    bool _pointerDown, _dragging;
    Point _pointerDownScreen;
    double _dragGrabOffsetSprite;

    public PetWindow Window => _window;
    public bool Suspended =>
        _settings.Paused || _systemAsleep || _sessionLocked || _displayOff || _claudeAbsent;
    public bool WaitingForClaude => _claudeAbsent;

    /// <summary>Leve quand le hamster apparait (true) ou se cache (false) avec Claude Desktop.</summary>
    public event Action<bool>? ClaudePresenceChanged;

    public PetController(Settings settings)
    {
        _settings = settings;
        _library = new SpriteLibrary();
        _animator = new Animator(_library, Clips.Idle);

        _window.MouseDown += OnMouseDown;
        _window.MouseMove += OnMouseMove;
        _window.MouseUp += OnMouseUp;
        _window.DisplayPowerChanged += on => SetDisplayOff(!on);
        _window.DisplayLayoutChanged += Reclamp;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += (_, _) => Reclamp();

        _frameTimer.Tick += (_, _) => Tick();
        _houseTimer.Interval = 1000;
        _houseTimer.Tick += (_, _) => Housekeeping();
    }

    public void Start()
    {
        // on regarde avant la premiere apparition : lance a l'ouverture de session,
        // le hamster ne doit pas clignoter a l'ecran si Claude Desktop n'est pas la
        _claudeAbsent = !ClaudeDesktopPresent();
        if (!_claudeAbsent) _window.Show();
        RestorePosition();
        ApplyCadence();
        _houseTimer.Start();
        if (!Suspended) _frameTimer.Start();
        _lastTickSeconds = _clock.Elapsed.TotalSeconds;
        Render();
        Diagnostics.Info($"demarre, echelle {_settings.Scale}x, bureaux virtuels {_desktops.Status}" +
            (_claudeAbsent ? ", attend Claude Desktop" : ""));
    }

    // ---- boucle -----------------------------------------------------------

    void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _lastTickSeconds, 0.25);
        _lastTickSeconds = now;

        _animator.Advance(dt);
        if (!_dragging)
        {
            _motion.Tick(dt);
            _animator.SetBackground(_motion.Mode == MotionMode.Walking ? Clips.Walk : Clips.Idle);
        }

        ApplyCadence();
        Render();
    }

    void ApplyCadence()
    {
        // on suit le clip courant, borne 6..12 Hz : inutile de reveiller le CPU
        // douze fois par seconde pour une animation qui tourne a cinq images
        int fps = Math.Clamp(_animator.Current.Fps, 6, 12);
        int interval = 1000 / fps;
        if (_frameTimer.Interval != interval) _frameTimer.Interval = interval;
    }

    void Render()
    {
        int scale = _settings.Scale;
        int side = Sprite * scale;

        // a 1x la ligne de debug est plus large que le sprite : on elargit la
        // surface pour elle et on recentre le personnage dedans, plutot que de
        // tronquer le texte
        string? debug = _settings.DebugOverlay ? BuildDebugLine() : null;
        // la marge est genereuse : MeasureString sur une surface hors ecran et sur
        // la DIB ne rendent pas exactement la meme largeur, et le texte se fait rogner
        int debugWidth = debug == null ? 0 : (int)Math.Ceiling(MeasureDebug(debug).Width) + 16;
        int width = Math.Max(side, debugWidth);
        int offsetX = (width - side) / 2;

        _surface.Ensure(width, side);
        _surface.Clear();
        BlitScaled(_surface.Pixels, width, _animator.Frame(_motion.Facing < 0), scale, offsetX);
        if (debug != null) DrawDebugOverlay(debug);

        var (centerX, top) = WindowAnchor();
        _surface.Present(_window.Handle, centerX - width / 2, top, _settings.Alpha);
    }

    static void BlitScaled(Span<uint> dst, int dstWidth, uint[] src, int scale, int offsetX)
    {
        for (int y = 0; y < Sprite; y++)
        for (int x = 0; x < Sprite; x++)
        {
            uint c = src[y * Sprite + x];
            if (c == 0) continue;
            int bx = offsetX + x * scale, by = y * scale;
            for (int sy = 0; sy < scale; sy++)
            {
                int row = (by + sy) * dstWidth + bx;
                for (int sx = 0; sx < scale; sx++) dst[row + sx] = c;
            }
        }
    }

    string BuildDebugLine() =>
        $"{_animator.Current.Name} {_animator.FrameIndex}/{_animator.Current.FrameCount} " +
        $"{_motion.Mode} x={_motion.X:F0} {_settings.Scale}x " +
        $"vd:{_desktops.Status}{(Suspended ? " PAUSE" : "")}";

    static readonly Font DebugFont = new("Consolas", 9f);
    static readonly Bitmap MeasureSurface = new(1, 1);

    static SizeF MeasureDebug(string text)
    {
        using var g = Graphics.FromImage(MeasureSurface);
        return g.MeasureString(text, DebugFont);
    }

    void DrawDebugOverlay(string line)
    {
        try
        {
            using var g = Graphics.FromImage(_surface.GdiView);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            var size = g.MeasureString(line, DebugFont);
            using var back = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            g.FillRectangle(back, 0, 0, size.Width + 4, size.Height + 2);
            g.DrawString(line, DebugFont, Brushes.White, 2, 1);
        }
        catch (Exception e) { Diagnostics.Warn("overlay: " + e.Message); }
    }

    // ---- geometrie ---------------------------------------------------------

    /// <summary>Centre horizontal du personnage et haut de la fenetre, en pixels ecran.</summary>
    (int CenterX, int Top) WindowAnchor()
    {
        int scale = _settings.Scale;
        var area = _screen.WorkingArea;
        // le bas de la fenetre est cale sur le bas de la zone de travail : le
        // personnage marche donc au-dessus de la barre des taches, jamais dessus
        return (area.Left + (int)Math.Round(_motion.X) * scale, area.Bottom - Sprite * scale);
    }

    void UpdateBounds()
    {
        int scale = _settings.Scale;
        double halfSprite = Sprite / 2.0;
        double widthInSprite = _screen.WorkingArea.Width / (double)scale;
        _motion.SetBounds(halfSprite * 0.55, Math.Max(halfSprite * 0.55, widthInSprite - halfSprite * 0.55));
    }

    void Reclamp()
    {
        var center = new Point(WindowAnchor().CenterX, _screen.WorkingArea.Bottom - 2);
        _screen = Screen.FromPoint(center);
        UpdateBounds();
        Render();
        Diagnostics.Info($"ecran: {_screen.DeviceName} {_screen.WorkingArea}");
    }

    void RestorePosition()
    {
        _screen = Screen.PrimaryScreen!;
        if (_settings.AnchorX is int ax && _settings.AnchorY is int ay)
        {
            var p = new Point(ax, ay);
            foreach (var s in Screen.AllScreens)
                if (s.WorkingArea.Contains(p)) { _screen = s; break; }
            UpdateBounds();
            _motion.PlaceAt((ax - _screen.WorkingArea.Left) / (double)_settings.Scale);
        }
        else
        {
            UpdateBounds();
            _motion.PlaceAt(_screen.WorkingArea.Width / (double)_settings.Scale * 0.5);
        }
    }

    void SavePosition()
    {
        var area = _screen.WorkingArea;
        _settings.AnchorX = area.Left + (int)Math.Round(_motion.X) * _settings.Scale;
        _settings.AnchorY = area.Bottom - 2;
        _settings.Save();
    }

    // ---- entretien ---------------------------------------------------------

    void Housekeeping()
    {
        _housekeepingTicks++;

        // Claude Desktop : un coup d'oeil toutes les deux secondes. C'est une
        // enumeration de processus, pas un evenement : Windows n'en offre pas
        // sans activer l'audit des processus, qui est un reglage de securite
        if (_housekeepingTicks % 2 == 1)
        {
            // un marqueur ajoute a la main dans settings.json compte sans redemarrer
            _settings.RefreshMarkersFromDisk();
            SetClaudeAbsent(!ClaudeDesktopPresent());
        }

        // certaines applications s'imposent en topmost et nous passent devant :
        // on se replace regulierement, ca coute un appel toutes les deux secondes
        if (_housekeepingTicks % 2 == 0 && !Suspended) _window.AssertTopMost();

        if (!Suspended && _desktops.EnsureOnCurrentDesktop(_window.Handle))
        {
            _window.AssertTopMost();
            Render();
        }
    }

    // ---- veille, verrouillage, extinction d'ecran --------------------------

    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) SetAsleep(true);
        else if (e.Mode == PowerModes.Resume) SetAsleep(false);
    }

    void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect)
            SetLocked(true);
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
            SetLocked(false);
    }

    void SetAsleep(bool value) { if (_systemAsleep != value) { _systemAsleep = value; ApplySuspension("veille"); } }
    void SetLocked(bool value) { if (_sessionLocked != value) { _sessionLocked = value; ApplySuspension("verrouillage"); } }
    void SetDisplayOff(bool value) { if (_displayOff != value) { _displayOff = value; ApplySuspension("ecran"); } }

    // ---- Claude Desktop ----------------------------------------------------

    bool ClaudeDesktopPresent() =>
        !_settings.OnlyWithClaudeDesktop || ClaudeDesktop.IsRunning(_settings.ClaudeDesktopPathMarkers);

    void SetClaudeAbsent(bool value)
    {
        if (_claudeAbsent == value) return;
        _claudeAbsent = value;
        if (value)
        {
            CancelDrag();
            _window.Hide();
        }
        else
        {
            _window.Show();
            // ApplySuspension ne repeint pas si une autre raison (Pause) tient
            // encore : sans ca il reapparaitrait sans premier plan garanti
            _window.AssertTopMost();
            Render();
        }
        ApplySuspension("claude desktop");
        ClaudePresenceChanged?.Invoke(!value);
    }

    public void SetOnlyWithClaudeDesktop(bool value)
    {
        _settings.OnlyWithClaudeDesktop = value;
        _settings.Save();
        SetClaudeAbsent(!ClaudeDesktopPresent());
    }

    public void SetUserPaused(bool value)
    {
        _settings.Paused = value;
        _settings.Save();
        ApplySuspension("menu");
    }

    void ApplySuspension(string reason)
    {
        if (Suspended)
        {
            _frameTimer.Stop();
            Diagnostics.Info($"suspendu ({reason})");
        }
        else
        {
            _lastTickSeconds = _clock.Elapsed.TotalSeconds;
            _frameTimer.Start();
            _window.AssertTopMost();
            Render();
            Diagnostics.Info($"repris ({reason})");
        }
    }

    // ---- souris ------------------------------------------------------------

    void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _pointerDown = true;
        _dragging = false;
        _pointerDownScreen = Cursor.Position;
        _dragGrabOffsetSprite = _motion.X - ScreenXToSprite(Cursor.Position.X);
        _window.Capture = true;
    }

    void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_pointerDown) return;
        var now = Cursor.Position;
        if (!_dragging &&
            (Math.Abs(now.X - _pointerDownScreen.X) > 3 || Math.Abs(now.Y - _pointerDownScreen.Y) > 3))
            _dragging = true;
        if (!_dragging) return;

        _screen = Screen.FromPoint(now);
        UpdateBounds();
        _motion.PlaceAt(ScreenXToSprite(now.X) + _dragGrabOffsetSprite);
        Render();
    }

    void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            ShowMenu();
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        _window.Capture = false;
        _pointerDown = false;
        if (_dragging)
        {
            _dragging = false;
            _motion.Settle(_motion.X);
            SavePosition();
        }
        else
        {
            // reaction courte, et surtout il ne se retourne pas vers le curseur
            _animator.Play(Clips.React);
            ApplyCadence();
        }
    }

    /// <summary>Lache un drag en cours, par exemple quand la fenetre se cache sous le curseur.</summary>
    void CancelDrag()
    {
        if (!_pointerDown) return;
        _window.Capture = false;
        _pointerDown = false;
        if (_dragging)
        {
            _dragging = false;
            _motion.Settle(_motion.X);
            SavePosition();
        }
    }

    double ScreenXToSprite(int screenX) => (screenX - _screen.WorkingArea.Left) / (double)_settings.Scale;

    // ---- menu --------------------------------------------------------------

    public ContextMenuStrip? Menu { get; set; }

    void ShowMenu()
    {
        if (Menu == null) return;
        // sans ca, un menu ouvert depuis une fenetre WS_EX_NOACTIVATE ne se ferme
        // pas quand on clique ailleurs
        Native.SetForegroundWindow(_window.Handle);
        Menu.Show(Cursor.Position);
    }

    public void ApplyScale(int scale)
    {
        _settings.Scale = Math.Clamp(scale, 1, 3);
        UpdateBounds();
        SavePosition();
        Render();
    }

    public void ApplyOpacity(int percent)
    {
        _settings.OpacityPercent = Math.Clamp(percent, 20, 100);
        _settings.Save();
        Render();
    }

    public void ToggleDebugOverlay()
    {
        _settings.DebugOverlay = !_settings.DebugOverlay;
        _settings.Save();
        Render();
    }

    /// <summary>Le ramene sur l'ecran du curseur, sous le pointeur.</summary>
    public void BringHere()
    {
        _screen = Screen.FromPoint(Cursor.Position);
        UpdateBounds();
        _motion.Settle(ScreenXToSprite(Cursor.Position.X));
        _window.AssertTopMost();
        SavePosition();
        Render();
        Diagnostics.Info("rappele sur " + _screen.DeviceName);
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _frameTimer.Dispose();
        _houseTimer.Dispose();
        _surface.Dispose();
        _window.Dispose();
    }
}
