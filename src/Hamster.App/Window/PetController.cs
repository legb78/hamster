using System.Diagnostics;
using System.Drawing;
using Hamster.Activity;
using Hamster.App.Render;
using Hamster.App.State;
using Hamster.Art;
using Microsoft.Win32;

namespace Hamster.App.Window;

/// <summary>
/// Cable la fenetre, l'animation, la balade, les minis et les evenements systeme. Le
/// principal et ses minis partagent UNE fenetre layered : la surface s'elargit pour
/// l'orbite, et Windows fait le hit-test sur l'alpha de l'ensemble.
/// </summary>
internal sealed class PetController : IDisposable
{
    const int Sprite = SpriteLibrary.Size;
    const int MiniSprite = SpriteLibrary.MiniSize;
    /// <summary>Id de survol et de clic du principal ; les minis ont le leur.</summary>
    const string MainId = "";
    /// <summary>Montee et descente du principal quand les minis arrivent ou partent, en px sprite par seconde.</summary>
    const double LiftSpeed = 90;
    /// <summary>Glissade qui ramene l'orbite dans l'ecran, en px sprite par seconde.</summary>
    const double NudgeSpeed = 40;
    /// <summary>Longueur maximale d'une etiquette : une description de sous-agent peut etre longue.</summary>
    const int LabelMaxChars = 48;

    readonly Settings _settings;
    readonly SpriteLibrary _library;
    readonly Animator _animator;
    readonly PetDirector _director;
    readonly MiniCrowd _crowd;
    readonly Compositor _comp = new();
    readonly Sounds _sounds;
    readonly WaitingBell _bell = new();
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
    string? _pressTarget;

    // activite
    ActivityHub? _hub;
    ActivitySnapshot _snapshot = new(PetState.Idle, null, null, null, false, Array.Empty<MiniInfo>(), 0);
    double _hubStartedAt = double.NaN;
    double _lift;
    string? _hover;
    Layout? _layout;
    /// <summary>
    /// Derniere position connue du pointeur : celle du dernier message souris, ou celle du
    /// curseur des qu'il bouge. Sonder le curseur a chaque frame couvre le cas ou c'est le
    /// hamster qui passe sous un curseur immobile.
    /// </summary>
    Point _pointer = new(int.MinValue, int.MinValue);
    Point _lastPolled = new(int.MinValue, int.MinValue);

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
        _director = new PetDirector(_library.Has);
        _crowd = new MiniCrowd(_library);
        _sounds = new Sounds(() => _settings.SoundEnabled);

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

    /// <summary>Branche la source d'activite. Son Changed arrive sur le thread UI.</summary>
    public void Attach(ActivityHub hub)
    {
        _hub = hub;
        _hubStartedAt = _clock.Elapsed.TotalSeconds;
        hub.Changed += OnSnapshot;
    }

    // ---- activite ----------------------------------------------------------

    void OnSnapshot(ActivitySnapshot previous, ActivitySnapshot next)
    {
        double now = _clock.Elapsed.TotalSeconds;
        bool quiet = WaitingBell.Quiet(Suspended, _hubStartedAt, now);
        _snapshot = next;
        var born = _crowd.Sync(next.Minis, next.MinisOverflow, now, animate: !Suspended);
        // mis a jour meme en silence : une attente vue pendant l'amorcage ne sonne pas apres
        var joined = _bell.Update(next);
        if (joined.Count > 0)
            Diagnostics.Info("nouvelle attente: " + string.Join(", ", joined.Select(id => WaitingName(next, id)))
                + (quiet ? " (sans sonnerie)" : ""));

        if (!quiet)
        {
            // une conversation de plus attend, principale ou mini : les memes regles qu'avant
            // (Son coche, notifications acceptees), mais plus seulement pour la principale
            if (WaitingBell.Rings(joined, quiet)) _sounds.PlayPhone();
            if (next.State == PetState.Celebrating && previous.State != PetState.Celebrating) _sounds.PlayDone();
            if (born.Any(m => m.Kind == "subagent")) _sounds.PlayPop();
        }

        // un changement se voit tout de suite, sans attendre la frame suivante
        bool changed = previous.State != next.State || previous.Tool != next.Tool
                       || previous.MainSessionId != next.MainSessionId || !SameMinis(previous.Minis, next.Minis);
        if (changed && !Suspended) Tick();
    }

    static string WaitingName(ActivitySnapshot s, string id)
    {
        string label = id == s.MainSessionId ? s.MainLabel ?? "?" : s.Minis.FirstOrDefault(m => m.Id == id)?.Label ?? "?";
        return $"{label} (session {(id.Length > 8 ? id[..8] : id)}{(id == s.MainSessionId ? ", principale" : "")})";
    }

    static bool SameMinis(IReadOnlyList<MiniInfo> a, IReadOnlyList<MiniInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Id != b[i].Id || a[i].State != b[i].State) return false;
        return true;
    }

    // ---- boucle -----------------------------------------------------------

    void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _lastTickSeconds, 0.25);
        _lastTickSeconds = now;

        _director.Update(_snapshot.State, _snapshot.Tool, now, _settings.ChillDelaySeconds);
        string clip = _director.Clip;
        if (!_dragging)
        {
            _motion.Tick(dt, _director.Movement, _director.WalkSpeed);
            if (_director.Movement == Movement.Roam && _motion.Mode == MotionMode.Walking) clip = Clips.Walk;
            KeepOrbitOnScreen(dt);
        }
        _animator.Show(clip);
        _animator.Advance(dt);
        _crowd.Advance(dt, now);
        UpdateLift(dt);
        var cursor = Cursor.Position;
        if (cursor != _lastPolled) { _lastPolled = cursor; _pointer = cursor; }
        UpdateHover(_pointer);

        ApplyCadence();
        Render();
    }

    void ApplyCadence()
    {
        // on suit le clip le plus rapide a l'ecran, borne 6..12 Hz : inutile de reveiller le
        // CPU douze fois par seconde pour une animation qui tourne a cinq images
        int fps = _animator.Current.Fps;
        foreach (var m in _crowd.Minis)
        {
            if (m.ShowsBody) fps = Math.Max(fps, m.Body.Current.Fps);
            if (m.Fx != null) fps = Math.Max(fps, m.Fx.Current.Fps);
        }
        if (Math.Abs(_lift - LiftTarget) > 0.01) fps = 12;
        int interval = 1000 / Math.Clamp(fps, 6, 12);
        if (_frameTimer.Interval != interval) _frameTimer.Interval = interval;
    }

    double LiftTarget => _crowd.Any ? Orbit.Lift : 0;

    void UpdateLift(double dt)
    {
        double target = LiftTarget;
        double step = LiftSpeed * dt;
        _lift = _lift < target ? Math.Min(target, _lift + step) : Math.Max(target, _lift - step);
    }

    /// <summary>Avec des minis, l'orbite doit tenir dans l'ecran : le principal glisse un peu vers l'interieur.</summary>
    void KeepOrbitOnScreen(double dt)
    {
        if (!_crowd.Any) return;
        double width = _screen.WorkingArea.Width / (double)_settings.Scale;
        double min = Orbit.HalfWidth, max = width - Orbit.HalfWidth;
        if (max < min) return;
        double x = _motion.X;
        double target = Math.Clamp(x, min, max);
        if (target != x) _motion.PlaceAt(x + Math.Clamp(target - x, -NudgeSpeed * dt, NudgeSpeed * dt));
    }

    // ---- rendu -------------------------------------------------------------

    /// <summary>Un element dessine, garde pour le hit-test : position en pixels du tampon sprite.</summary>
    readonly record struct Drawn(string Id, int X, int Y, int Size, byte[] Frame, bool Mirror, bool Front);

    readonly record struct LabelBox(string Owner, string Text, Rectangle Rect, bool Alert);

    /// <summary>Ce qui a ete dessine a la derniere frame, pour savoir ce qu'il y a sous la souris.</summary>
    sealed class Layout
    {
        public int Left, Top, Width, Height, Scale, OriginX, OriginY;
        public readonly List<Drawn> Items = new();
        public readonly List<LabelBox> Labels = new();
    }

    /// <summary>Feet : pieds a l'image courante, rebond compris ; Bob : ce rebond, que les etiquettes traitent a part.</summary>
    readonly record struct MiniPlace(Mini Mini, int X, int Feet, int Bob, bool Front, int Facing);

    void Render()
    {
        int s = _settings.Scale;
        double now = _clock.Elapsed.TotalSeconds;
        var layout = new Layout { Scale = s };

        // ---- tampon sprite : principal au centre, orbite autour de ses pieds ----
        int extent = Math.Max(_crowd.Any ? Orbit.Lift : 0, (int)Math.Ceiling(_lift));
        string? badge = _crowd.Overflow > 0 ? "+" + _crowd.Overflow : null;
        int badgeWidth = badge == null ? 0 : PixelFont.Measure(badge) + 4;
        int halfW = _crowd.Any ? Math.Max(Orbit.HalfWidth, badge == null ? 0 : Orbit.BadgeX + badgeWidth + 1) : Sprite / 2;
        int w = 2 * halfW, h = Sprite + extent;
        _comp.Reset(w, h);

        int mainLeft = halfW - Sprite / 2;
        int mainTop = h - Sprite - (int)Math.Round(_lift);
        int feetRow = mainTop + SpriteLibrary.Baseline;

        var places = new List<MiniPlace>();
        foreach (var m in _crowd.Minis)
        {
            double a = _crowd.AngleOf(m, now);
            var (dx, dy) = Orbit.At(a);
            double sin = Math.Sin(a);
            int bob = Orbit.BobAt(now, m.BobSeed);
            // de face ils vont vers la droite, de dos vers la gauche : le regard suit la marche
            places.Add(new MiniPlace(m, halfW + dx, feetRow + dy + bob, bob, sin > 0, sin >= 0 ? 1 : -1));
        }
        // du plus loin au plus proche
        places.Sort((a, b) => a.Feet.CompareTo(b.Feet));

        foreach (var p in places) if (!p.Front) DrawMini(p, layout);

        var mainClip = _animator.Current;
        bool mainMirror = _motion.Facing < 0 && mainClip.Mirrorable;
        var mainFrame = _animator.Frame;
        _comp.Blit(mainFrame, Sprite, mainLeft, mainTop, _library.PaletteFor(0), mainMirror,
            markFace: mainClip.Face(_animator.FrameIndex));
        layout.Items.Add(new Drawn(MainId, mainLeft, mainTop, Sprite, mainFrame, mainMirror, false));

        foreach (var p in places) if (p.Front) DrawMini(p, layout);
        if (badge != null) DrawBadge(badge, halfW + Orbit.BadgeX, feetRow - Orbit.BadgeAboveFeet, badgeWidth);

        // ---- etiquettes et overlay, en pixels ecran ----
        int spriteW = w * s, spriteH = h * s;
        // le tampon agrandi est centre sur le principal : les bords de l'ecran, dans ses
        // coordonnees, tiennent donc sans connaitre encore la taille de la fenetre
        var (labelMinX, labelMaxX) = LabelLayout.ScreenBounds(_screen.WorkingArea, CenterScreenX(), spriteW);
        var labels = BuildLabels(places, mainLeft, mainTop, s,
            LabelLayout.FaceRect(mainClip, mainMirror, mainLeft, mainTop, s),
            LabelLayout.HeadRect(mainClip, mainMirror, mainLeft, mainTop, s),
            labelMinX, labelMaxX, spriteH);
        int minX = 0, maxX = spriteW, minY = 0;
        foreach (var l in labels)
        {
            minX = Math.Min(minX, l.Rect.Left);
            maxX = Math.Max(maxX, l.Rect.Right);
            minY = Math.Min(minY, l.Rect.Top);
        }
        int half = Math.Max(spriteW / 2, Math.Max(spriteW / 2 - minX, maxX - spriteW / 2));
        int topExtra = -minY;

        string? debug = _settings.DebugOverlay ? BuildDebugText() : null;
        SizeF debugSize = default;
        if (debug != null)
        {
            // la marge est genereuse : MeasureString sur une surface hors ecran et sur
            // la DIB ne rendent pas exactement la meme largeur, et le texte se fait rogner
            debugSize = Measure(debug, DebugFont);
            half = Math.Max(half, ((int)Math.Ceiling(debugSize.Width) + 16 + 1) / 2);
            topExtra += (int)Math.Ceiling(debugSize.Height) + 4;
        }

        int dibW = 2 * half, dibH = spriteH + topExtra;
        int ox = half - spriteW / 2, oy = topExtra;
        _surface.Ensure(dibW, dibH);
        _surface.Clear();
        _comp.UpscaleInto(_surface.Pixels, dibW, ox, oy, s);

        foreach (var l in labels)
            layout.Labels.Add(l with { Rect = new Rectangle(l.Rect.X + ox, l.Rect.Y + oy, l.Rect.Width, l.Rect.Height) });
        if (layout.Labels.Count > 0 || debug != null) DrawText(layout.Labels, debug);

        int left = CenterScreenX() - half;
        int top = _screen.WorkingArea.Bottom - dibH;
        _surface.Present(_window.Handle, left, top, _settings.Alpha);

        layout.Left = left;
        layout.Top = top;
        layout.Width = dibW;
        layout.Height = dibH;
        layout.OriginX = ox;
        layout.OriginY = oy;
        _layout = layout;
    }

    void DrawMini(MiniPlace p, Layout layout)
    {
        var m = p.Mini;
        int x = p.X - MiniSprite / 2;
        int y = p.Feet - SpriteLibrary.MiniBaseline;
        if (m.ShowsBody)
        {
            bool mirror = p.Facing < 0 && m.Body.Current.Mirrorable;
            var frame = m.Body.MiniFrame;
            _comp.Blit(frame, MiniSprite, x, y, _library.PaletteFor(m.Hue), mirror, avoidFace: p.Front);
            layout.Items.Add(new Drawn(m.Id, x, y, MiniSprite, frame, mirror, p.Front));
        }
        // fumee et etincelles par-dessus le mini, du meme cote du principal que lui
        if (m.Fx != null)
            _comp.Blit(m.Fx.MiniFrame, MiniSprite, x, y, _library.PaletteFor(0), false, avoidFace: p.Front);
    }

    /// <summary>Pastille "+N" : police 3x5, fond sombre aux coins arrondis.</summary>
    void DrawBadge(string text, int x, int y, int width)
    {
        var palette = _library.PaletteFor(0);
        uint back = palette[Palette.Outline], ink = palette[Palette.EyeShine];
        const int height = PixelFont.GlyphHeight + 4;
        _comp.Fill(x + 1, y, width - 2, height, back);
        _comp.Fill(x, y + 1, width, height - 2, back);
        PixelFont.Draw(text, x + 2, y + 2, (px, py) => _comp.Fill(px, py, 1, 1, ink));
    }

    // ---- etiquettes --------------------------------------------------------

    /// <summary>
    /// Etiquettes a afficher, en pixels ecran relatifs au tampon sprite agrandi. Une conversation
    /// en attente porte en permanence son nom, qu'elle soit la principale ou un mini : c'est
    /// celle qui a besoin de toi. Au survol, celle du hamster survole. Toutes restent entre
    /// minX et maxX (le bord de l'ecran) ; celles des minis evitent le visage du principal, sa
    /// tete (bulle, telephone) quand la place le permet, et les etiquettes deja posees
    /// (LabelLayout.MiniLabel).
    /// </summary>
    List<LabelBox> BuildLabels(List<MiniPlace> places, int mainLeft, int mainTop, int s, Rectangle? face, Rectangle? head,
        int minX, int maxX, int maxY)
    {
        var result = new List<LabelBox>();
        var placed = new List<Rectangle>();
        int mainCenter = (mainLeft + Sprite / 2) * s;
        int mainHead = (mainTop + _animator.Current.TopRow) * s;

        bool mainWaiting = _snapshot.State == PetState.WaitingUser;
        string? mainText = _hover == MainId ? MainHoverText() : mainWaiting ? _snapshot.MainLabel ?? "Claude Code" : null;
        if (mainText != null)
        {
            mainText = Fit(mainText);
            var rect = LabelLayout.KeepInside(LabelLayout.Above(mainCenter, mainHead, LabelSize(mainText)), minX, maxX);
            result.Add(new LabelBox(MainId, mainText, rect, mainWaiting));
            placed.Add(rect);
        }

        // du plus proche au plus loin, dans un ordre qui ne depend ni du survol ni du rebond :
        // survoler une etiquette ne doit pas la deplacer, sinon elle fuirait sous le curseur, et
        // deux minis a la meme profondeur echangeraient leurs places au rythme du sautillement
        foreach (var p in places.OrderByDescending(x => x.Feet - x.Bob).ThenBy(x => x.Mini.Id, StringComparer.Ordinal))
        {
            var m = p.Mini;
            if (!m.ShowsBody) continue;
            bool hovered = _hover == m.Id;
            bool waiting = m.Kind == "session" && m.State == PetState.WaitingUser;
            if (!hovered && !waiting) continue;
            // en attente, le meme texte survole ou non : plus large, l'etiquette pourrait changer
            // de place (bord de l'ecran, visage) et quitter le curseur, qui la ferait revenir
            string text = Fit(waiting ? m.Label : MiniHoverText(m));
            var (shown, reserved) = LabelLayout.MiniLabel(p.X, p.Feet - p.Bob, p.Bob, m.Body.Current.TopRow, LabelSize(text), s,
                face, head, placed, minX, maxX, maxY);
            result.Add(new LabelBox(m.Id, text, shown, m.State == PetState.WaitingUser));
            placed.Add(reserved);
        }
        return result;
    }

    static string Fit(string text) => text.Length > LabelMaxChars ? text[..(LabelMaxChars - 3)] + "..." : text;

    /// <summary>Taille de l'etiquette, cadre compris, en pixels ecran : lisible a 1x comme a 3x.</summary>
    Size LabelSize(string text)
    {
        var size = MeasureLabel(text);
        return new Size((int)Math.Ceiling(size.Width) + 10, (int)Math.Ceiling(size.Height) + 4);
    }

    string MainHoverText()
    {
        var st = _snapshot;
        if (st.MainSessionId == null) return "Hamster - " + StateText(PetState.Idle);
        string text = (st.MainLabel ?? "session") + " - " + StateText(st.State);
        return st.State == PetState.Working && st.Tool != null ? text + " (" + st.Tool + ")" : text;
    }

    static string MiniHoverText(Mini m) =>
        m.Kind == "subagent" ? m.Label : m.Label + " - " + StateText(m.State);

    static string StateText(PetState state) => state switch
    {
        PetState.Working => "travaille",
        PetState.Error => "erreur",
        PetState.WaitingUser => "attend ta reponse",
        PetState.Celebrating => "a fini",
        _ => "au repos",
    };

    static readonly Font DebugFont = new("Consolas", 9f);
    static readonly Bitmap MeasureSurface = new(1, 1);
    Font? _labelFont;
    uint _labelDpi;

    /// <summary>Police des etiquettes, a la taille du DPI de l'ecran de la fenetre.</summary>
    Font LabelFont()
    {
        uint dpi = 96;
        try { dpi = Native.GetDpiForWindow(_window.Handle); } catch { }
        if (dpi == 0) dpi = 96;
        if (_labelFont == null || dpi != _labelDpi)
        {
            _labelFont?.Dispose();
            _labelFont = new Font("Segoe UI", 12f * dpi / 96f, FontStyle.Bold, GraphicsUnit.Pixel);
            _labelDpi = dpi;
        }
        return _labelFont;
    }

    readonly Dictionary<string, SizeF> _labelSizes = new(StringComparer.Ordinal);
    Font? _labelSizesFont;

    /// <summary>
    /// Mesure d'une etiquette, gardee : celle de l'attente reste affichee a chaque frame
    /// pendant toute l'attente, inutile de la remesurer six fois par seconde.
    /// </summary>
    SizeF MeasureLabel(string text)
    {
        var font = LabelFont();
        if (!ReferenceEquals(font, _labelSizesFont) || _labelSizes.Count > 64)
        {
            _labelSizes.Clear();
            _labelSizesFont = font;
        }
        if (!_labelSizes.TryGetValue(text, out var size)) _labelSizes[text] = size = Measure(text, font);
        return size;
    }

    static SizeF Measure(string text, Font font)
    {
        using var g = Graphics.FromImage(MeasureSurface);
        return g.MeasureString(text, font);
    }

    void DrawText(List<LabelBox> labels, string? debug)
    {
        try
        {
            using var g = Graphics.FromImage(_surface.GdiView);
            if (labels.Count > 0)
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                var font = LabelFont();
                using var back = new SolidBrush(Color.FromArgb(255, 0x1C, 0x1A, 0x1A));
                using var normal = new Pen(Color.FromArgb(255, 0x8A, 0x86, 0x84));
                using var alert = new Pen(Color.FromArgb(255, 0xF5, 0xC5, 0x42));
                foreach (var l in labels)
                {
                    var r = l.Rect;
                    g.FillRectangle(back, r);
                    g.DrawRectangle(l.Alert ? alert : normal, r.X, r.Y, r.Width - 1, r.Height - 1);
                    g.DrawString(l.Text, font, Brushes.White, r.X + 5, r.Y + 2);
                }
            }
            if (debug != null)
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                var size = g.MeasureString(debug, DebugFont);
                using var back = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
                g.FillRectangle(back, 0, 0, size.Width + 4, size.Height + 2);
                g.DrawString(debug, DebugFont, Brushes.White, 2, 1);
            }
        }
        catch (Exception e) { Diagnostics.Warn("texte: " + e.Message); }
    }

    string BuildDebugText()
    {
        var st = _snapshot;
        string line1 = $"{_animator.Current.Name} {_animator.FrameIndex}/{_animator.Current.FrameCount} " +
                       $"{_motion.Mode} x={_motion.X:F0} {_settings.Scale}x " +
                       $"vd:{_desktops.Status}{(Suspended ? " PAUSE" : "")}";
        string line2 = $"etat {st.State} ({_director.Phase}) outil {st.Tool ?? "-"} " +
                       $"minis {st.Minis.Count}+{st.MinisOverflow} lift {_lift:F0}";
        string line3 = "transcripts: " + Truncate(_hub?.TranscriptStatus ?? "-", 72);
        string line4 = "hook: " + Truncate(_hub?.HookStatus ?? "-", 72);
        return line1 + "\n" + line2 + "\n" + line3 + "\n" + line4;
    }

    static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";

    // ---- survol et hit-test --------------------------------------------------

    /// <summary>Ce qu'il y a sous ce point ecran : MainId, l'id d'un mini, ou null.</summary>
    string? HitTest(Point screen)
    {
        var l = _layout;
        if (l == null || !_window.Visible) return null;
        int px = screen.X - l.Left, py = screen.Y - l.Top;
        if (px < 0 || py < 0 || px >= l.Width || py >= l.Height) return null;

        for (int i = l.Labels.Count - 1; i >= 0; i--)
            if (l.Labels[i].Rect.Contains(px, py)) return l.Labels[i].Owner;

        int bx = FloorDiv(px - l.OriginX, l.Scale), by = FloorDiv(py - l.OriginY, l.Scale);
        if (bx < 0 || by < 0 || bx >= _comp.Width || by >= _comp.Height) return null;
        bool face = _comp.Face[by * _comp.Width + bx] != 0;
        for (int i = l.Items.Count - 1; i >= 0; i--)
        {
            var it = l.Items[i];
            int lx = bx - it.X, ly = by - it.Y;
            if (lx < 0 || ly < 0 || lx >= it.Size || ly >= it.Size) continue;
            // un mini de devant ne peint pas sur le visage : ce pixel-la est au principal
            if (it.Front && face) continue;
            if (it.Frame[ly * it.Size + (it.Mirror ? it.Size - 1 - lx : lx)] != 0) return it.Id;
        }
        return null;
    }

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    /// <summary>Met a jour le hamster survole ; vrai s'il a change.</summary>
    bool UpdateHover(Point screen)
    {
        string? hover = _dragging || Suspended ? null : HitTest(screen);
        if (hover == _hover) return false;
        _hover = hover;
        Diagnostics.Info("survol: " + (hover == null ? "rien" : hover == MainId ? "principal"
            : "mini " + (hover.Length > 8 ? hover[..8] : hover)));
        return true;
    }

    // ---- geometrie ---------------------------------------------------------

    /// <summary>Centre horizontal du principal, en pixels ecran.</summary>
    int CenterScreenX() => _screen.WorkingArea.Left + (int)Math.Round(_motion.X) * _settings.Scale;

    void UpdateBounds()
    {
        int scale = _settings.Scale;
        double halfSprite = Sprite / 2.0;
        double widthInSprite = _screen.WorkingArea.Width / (double)scale;
        _motion.SetBounds(halfSprite * 0.55, Math.Max(halfSprite * 0.55, widthInSprite - halfSprite * 0.55));
    }

    void Reclamp()
    {
        var center = new Point(CenterScreenX(), _screen.WorkingArea.Bottom - 2);
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

        // les garde-fous du modele (delais, fin des transitoires) avancent avec l'horloge,
        // meme sans nouvel evenement
        _hub?.Refresh();

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
            _hover = null;
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
        // la cible est la ou le clic a eu lieu, d'apres le message lui-meme
        _pointer = _window.PointToScreen(e.Location);
        _pressTarget = HitTest(_pointer);
        _dragGrabOffsetSprite = _motion.X - ScreenXToSprite(Cursor.Position.X);
        _window.Capture = true;
    }

    void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_pointerDown)
        {
            // survol : l'etiquette apparait sans attendre la frame suivante
            _pointer = _window.PointToScreen(e.Location);
            if (UpdateHover(_pointer) && !Suspended) Render();
            return;
        }
        var now = Cursor.Position;
        if (!_dragging &&
            (Math.Abs(now.X - _pointerDownScreen.X) > 3 || Math.Abs(now.Y - _pointerDownScreen.Y) > 3))
        {
            _dragging = true;
            _hover = null;
        }
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
        else if (_pressTarget is { } id && id != MainId)
        {
            // clic sur un mini : sa propre petite reaction, le principal ne bouge pas
            _crowd.React(id);
            ApplyCadence();
        }
        else
        {
            // reaction courte, et surtout il ne se retourne pas vers le curseur
            _animator.PlayOnce(Clips.React);
            ApplyCadence();
        }
        _pressTarget = null;
    }

    /// <summary>Lache un drag en cours, par exemple quand la fenetre se cache sous le curseur.</summary>
    void CancelDrag()
    {
        if (!_pointerDown) return;
        _window.Capture = false;
        _pointerDown = false;
        _pressTarget = null;
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

    public void ApplyChillDelay(int seconds)
    {
        _settings.ChillDelaySeconds = Math.Clamp(seconds, 1, 3600);
        _settings.Save();
        Diagnostics.Info($"delai avant le mode chill: {_settings.ChillDelaySeconds} s");
    }

    public void SetSound(bool enabled)
    {
        _settings.SoundEnabled = enabled;
        _settings.Save();
        Diagnostics.Info("son " + (enabled ? "active" : "coupe"));
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
        if (_hub != null) _hub.Changed -= OnSnapshot;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _frameTimer.Dispose();
        _houseTimer.Dispose();
        _sounds.Dispose();
        _labelFont?.Dispose();
        _surface.Dispose();
        _window.Dispose();
    }
}
