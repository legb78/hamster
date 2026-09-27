using System.Drawing;
using Hamster.Activity;
using Hamster.App.Render;
using Hamster.App.State;
using Hamster.Art;

namespace Hamster.App.Tests;

static class LabelTests
{
    static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    /// <summary>Mini ; une session au telephone attend depuis T0, ou depuis waitSince secondes apres.</summary>
    static MiniInfo Mini(string id, PetState state, string kind = "session", double? waitSince = null) =>
        new(id, kind, "projet-" + id, id, state, T0)
        {
            WaitSince = waitSince is { } w ? T0.AddSeconds(w) : kind == "session" && state == PetState.WaitingUser ? T0 : null,
        };

    /// <summary>Instantane ; une principale au telephone attend depuis T0.</summary>
    static ActivitySnapshot Snap(string? main, PetState state, int overflow = 0, params MiniInfo[] minis) =>
        new(state, main, main == null ? null : "projet-" + main, null, main != null, minis, overflow)
        {
            MainWaitSince = main != null && state == PetState.WaitingUser ? T0 : null,
        };

    static string Joined(WaitingBell bell, ActivitySnapshot s) => string.Join(",", bell.Update(s));

    public static void Run()
    {
        T.Suite("Sonnerie des attentes");

        T.Case("principale qui se met a attendre : sonne une fois", () =>
        {
            var bell = new WaitingBell();
            T.Eq("", Joined(bell, Snap("a", PetState.Working)), "au travail");
            T.Eq("a", Joined(bell, Snap("a", PetState.WaitingUser)), "attente");
            T.Eq("", Joined(bell, Snap("a", PetState.WaitingUser)), "meme attente, instantane suivant");
        });

        T.Case("seconde conversation en attente, en mini : sonne aussi", () =>
        {
            var bell = new WaitingBell();
            Joined(bell, Snap("a", PetState.WaitingUser));
            T.Eq("b", Joined(bell, Snap("a", PetState.WaitingUser, 0, Mini("b", PetState.WaitingUser))), "mini b");
            T.Eq("", Joined(bell, Snap("a", PetState.WaitingUser, 0, Mini("b", PetState.WaitingUser))), "rien de neuf");
        });

        T.Case("mini deja annonce qui devient principal : pas de seconde sonnerie", () =>
        {
            var bell = new WaitingBell();
            Joined(bell, Snap("a", PetState.WaitingUser, 0, Mini("b", PetState.WaitingUser)));
            // a a eu sa reponse, b prend la place principale
            T.Eq("", Joined(bell, Snap("b", PetState.WaitingUser, 0, Mini("a", PetState.Working))), "b deja annonce");
            T.Eq(1, bell.Waiting.Count, "seul b attend");
        });

        T.Case("attente finie puis revenue : resonne", () =>
        {
            var bell = new WaitingBell();
            Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser)));
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.Working))), "b repond");
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "b rattend");
            T.Eq("", Joined(bell, Snap("a", PetState.Working)), "b oublie");
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser, waitSince: 20))),
                "b revient avec une nouvelle attente");
        });

        T.Case("deux attentes a la fois : une sonnerie, les deux nommees, principale d'abord", () =>
        {
            var bell = new WaitingBell();
            T.Eq("a,c", Joined(bell, Snap("a", PetState.WaitingUser, 0, Mini("s1", PetState.Working, "subagent"), Mini("c", PetState.WaitingUser))), "a et c");
        });

        T.Case("les sous-agents ne comptent pas", () =>
        {
            var bell = new WaitingBell();
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 0, Mini("x", PetState.WaitingUser, "subagent"))), "sous-agent");
        });

        T.Case("coupee par six attentes plus recentes puis revenue : pas de nouvelle sonnerie", () =>
        {
            var bell = new WaitingBell();
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "b attend");
            // les attentes passent en tete des minis : seules six attentes plus recentes coupent b
            var six = Enumerable.Range(1, 6).Select(i => Mini("w" + i, PetState.WaitingUser)).ToArray();
            T.Eq("w1,w2,w3,w4,w5,w6", Joined(bell, Snap("a", PetState.Working, 1, six)), "six nouvelles attentes, b coupee");
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "b revenue : deja annoncee");
            // l'absence seule ne dit rien : c'est le debut de l'attente qui la distingue d'une autre
            T.Eq("", Joined(bell, Snap("a", PetState.Working)), "b partie");
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "b revenue, meme attente : pas de sonnerie");
            T.Eq("", Joined(bell, Snap("a", PetState.Working)), "b repartie");
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser, waitSince: 30))), "nouvelle attente de b");
        });

        T.Case("attente cachee puis revenue : la meme ne resonne pas, une nouvelle sonne", () =>
        {
            // avant : une attente absente de l'instantane, hors six attentes plus recentes, etait
            // tenue pour finie, et resonnait en revenant
            var bell = new WaitingBell();
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "b attend");
            var subs = Enumerable.Range(1, 6).Select(i => Mini("s" + i, PetState.Working, "subagent")).ToArray();
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 2, subs)), "b cachee derriere des sous-agents");
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 2, subs.Take(5).Prepend(Mini("b", PetState.WaitingUser)).ToArray())),
                "b revenue, meme debut : deja annoncee");
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 2, subs)), "cachee de nouveau");
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 2, subs.Take(5).Prepend(Mini("b", PetState.WaitingUser, waitSince: 40)).ToArray())),
                "nouvelle attente de b : sonne");
        });

        T.Case("attente pendant la fete de sa conversation, en mini : deja annoncee, elle ne resonne pas", () =>
        {
            // la fete (Celebrating) passe devant l'attente, qui garde son debut
            var bell = new WaitingBell();
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser, waitSince: 3))), "b attend");
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.Celebrating, waitSince: 3))), "fete de b");
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser, waitSince: 3))), "b reprend son attente");
            T.Eq(1, bell.Waiting.Count, "b toujours tenue pour annoncee");
        });

        T.Case("attente sur le fil d'un sous-agent pendant la fete de sa conversation, huit minis plus recents : ni coupee, ni resonnee", () =>
        {
            // meme chemin que l'app. B attend une autorisation pour son sous-agent ; son fil principal
            // finit un tour a 30 s : 3 s de fete. Avant, le tri ne la classait plus parmi les attentes
            // pendant la fete : les huit sous-agents de A, plus recents, la coupaient de l'instantane,
            // et elle sonnait une seconde fois en revenant
            var t0 = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
            var model = new ActivityModel();
            var bell = new WaitingBell();
            ActivityEvent E(double at, ActivityKind kind, string session, string? agent = null, string? tool = null, string? detail = null) =>
                new(t0.AddSeconds(at), kind, session, agent, @"C:\work\" + session, tool, false, detail);
            var events = new List<ActivityEvent>
            {
                E(-100, ActivityKind.PromptSubmitted, "b"), E(-99, ActivityKind.SubagentActivity, "b", agent: "bsub", detail: "fond"),
                E(0, ActivityKind.PromptSubmitted, "a"), E(1, ActivityKind.AskedUser, "a", tool: "AskUserQuestion", detail: "qa"),
                E(2, ActivityKind.ToolStarted, "b", agent: "bsub", tool: "Bash", detail: "bt"),
                E(3, ActivityKind.NeedsUser, "b", detail: "permission_prompt"),
                E(20, ActivityKind.ToolStarted, "b", tool: "Bash", detail: "bm"), E(21, ActivityKind.ToolFinished, "b", detail: "bm"),
                E(30, ActivityKind.TurnEnded, "b"),
            };
            for (int i = 1; i <= 8; i++) events.Add(E(10 + i, ActivityKind.SubagentActivity, "a", agent: "ag" + i, detail: "n" + i));
            events.Sort((x, y) => x.Time.CompareTo(y.Time));
            int p = 0, ringsB = 0, missing = 0, celebrating = 0;
            for (double t = 0; t <= 40; t += 0.5)
            {
                while (p < events.Count && events[p].Time <= t0.AddSeconds(t)) model.Apply(events[p++]);
                var s = model.Snapshot(t0.AddSeconds(t));
                var joined = bell.Update(s);
                if (WaitingBell.Rings(joined, quiet: false) && joined.Contains("b")) ringsB++;
                if (t < 3.5) continue;
                var b = s.Minis.FirstOrDefault(m => m.Id == "b");
                if (b == null) missing++;
                else if (b.State == PetState.Celebrating) celebrating++;
            }
            T.True(celebrating > 0, "la fete de b passe devant son attente");
            T.Eq(0, missing, "b jamais coupee de l'instantane");
            T.Eq(1, ringsB, "b sonne une fois");
        });

        T.Case("seconde attente d'une session ancienne, 8 sous-agents plus recents : dans l'instantane, et elle sonne", () =>
        {
            // meme chemin que l'app : ActivityModel.Snapshot, puis WaitingBell a chaque instantane
            var t0 = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
            var model = new ActivityModel();
            var bell = new WaitingBell();
            var rang = new List<string>();
            ActivitySnapshot? last = null;
            void Apply(double at, ActivityKind kind, string session, string? agent = null, string? tool = null, string? detail = null)
            {
                model.Apply(new ActivityEvent(t0.AddSeconds(at), kind, session, agent, @"C:\work\" + session, tool, false, detail));
                last = model.Snapshot(t0.AddSeconds(at));
                var joined = bell.Update(last);
                if (WaitingBell.Rings(joined, quiet: false)) rang.AddRange(joined);
            }
            Apply(-100, ActivityKind.PromptSubmitted, "b");
            Apply(0, ActivityKind.PromptSubmitted, "a");
            Apply(1, ActivityKind.AskedUser, "a", tool: "AskUserQuestion", detail: "qa");
            for (int i = 1; i <= 8; i++) Apply(9 + i, ActivityKind.SubagentActivity, "a", agent: "ag" + i, detail: "n" + i);
            Apply(20, ActivityKind.AskedUser, "b", tool: "AskUserQuestion", detail: "qb");
            T.Eq("a", last!.MainSessionId, "a principale, en attente");
            T.True(last.Minis.Any(m => m.Id == "b" && m.State == PetState.WaitingUser), "b dans les minis, au telephone");
            T.Eq("a,b", string.Join(",", rang), "a puis b ont sonne");
        });

        T.Case("attente d'un sous-agent qui dure pendant la fete du principal : une seule sonnerie", () =>
        {
            // la fete (3 s) passe devant l'attente ; elle ne la termine pas
            var t0 = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
            var model = new ActivityModel();
            var bell = new WaitingBell();
            int rings = 0;
            ActivityEvent E(double at, ActivityKind kind, string? agent = null, string? tool = null, string? detail = null) =>
                new(t0.AddSeconds(at), kind, "a", agent, @"C:\work\a", tool, false, detail);
            // l'agent x, lance en fond, demande une autorisation ; le tour du principal finit pendant ce temps
            var events = new Queue<ActivityEvent>(new[]
            {
                E(0, ActivityKind.PromptSubmitted),
                E(1, ActivityKind.ToolStarted, tool: "Agent", detail: "t1"),
                E(2, ActivityKind.SubagentActivity, agent: "x", detail: "fond"),
                E(3, ActivityKind.ToolStarted, agent: "x", tool: "Bash", detail: "xb"),
                E(4, ActivityKind.NeedsUser, detail: "permission_prompt"),
                E(5, ActivityKind.ToolFinished, detail: "t1"),
                E(6, ActivityKind.TurnEnded),
            });
            var states = new List<PetState>();
            for (double t = 0; t <= 20; t += 0.5)
            {
                while (events.Count > 0 && events.Peek().Time <= t0.AddSeconds(t)) model.Apply(events.Dequeue());
                var s = model.Snapshot(t0.AddSeconds(t));
                states.Add(s.State);
                if (WaitingBell.Rings(bell.Update(s), quiet: false)) rings++;
            }
            T.Eq(PetState.WaitingUser, states[8], "attente a 4 s");
            T.True(states.Contains(PetState.Celebrating), "la fete passe devant");
            T.Eq(PetState.WaitingUser, states[^1], "puis l'attente reprend");
            T.Eq(1, rings, "une seule sonnerie");
        });

        T.Case("amorcage : une attente vue pendant les 3 premieres secondes ne sonne ni alors ni apres", () =>
        {
            // meme enchainement que PetController.OnSnapshot : Quiet, Update a chaque instantane, Rings
            var bell = new WaitingBell();
            const double hubAt = 10;
            T.True(WaitingBell.Quiet(false, hubAt, 11), "pendant l'amorcage : silence");
            T.True(!WaitingBell.Rings(bell.Update(Snap("a", PetState.WaitingUser)), WaitingBell.Quiet(false, hubAt, 11)),
                "attente relue au lancement : pas de sonnerie");
            T.True(!WaitingBell.Quiet(false, hubAt, 13), "apres 3 s : plus de silence");
            T.True(!WaitingBell.Rings(bell.Update(Snap("a", PetState.WaitingUser)), WaitingBell.Quiet(false, hubAt, 14)),
                "la meme attente apres l'amorcage : pas de sonnerie");
            T.True(WaitingBell.Rings(bell.Update(Snap("a", PetState.WaitingUser, 0, Mini("b", PetState.WaitingUser))), WaitingBell.Quiet(false, hubAt, 14)),
                "une nouvelle attente apres l'amorcage : sonne");
            T.True(WaitingBell.Quiet(true, hubAt, 100), "suspendu : silence");
            T.True(WaitingBell.Quiet(false, double.NaN, 100), "hub pas encore branche : silence");
        });

        T.Suite("Etiquettes");

        T.Case("bord de l'ecran : l'etiquette est decalee, pas coupee", () =>
        {
            var r = new Rectangle(-30, 10, 100, 20);
            T.Eq(new Rectangle(0, 10, 100, 20), LabelLayout.KeepInside(r, 0, 500), "bord gauche");
            T.Eq(new Rectangle(400, 10, 100, 20), LabelLayout.KeepInside(new Rectangle(450, 10, 100, 20), 0, 500), "bord droit");
            T.Eq(new Rectangle(50, 10, 100, 20), LabelLayout.KeepInside(new Rectangle(50, 10, 100, 20), 0, 500), "deja dedans");
            T.Eq(new Rectangle(0, 10, 100, 20), LabelLayout.KeepInside(r, 0, 60), "plus large que l'ecran : calee a gauche");
        });

        T.Case("bornes des etiquettes : les bords de la zone de travail, dans le repere du tampon centre sur le principal", () =>
        {
            // ecran de gauche d'un double ecran, principal a 40 px de son bord gauche, tampon de 464 px
            var area = new Rectangle(-1920, 0, 1920, 1040);
            var (minX, maxX) = LabelLayout.ScreenBounds(area, area.Left + 40, 464);
            T.Eq(192, minX, "bord gauche : 232 px de demi-tampon moins 40");
            T.Eq(2112, maxX, "bord droit : 1920 + 192");
            T.Eq((0, 1920), LabelLayout.ScreenBounds(new Rectangle(0, 0, 1920, 1040), 960, 1920), "tampon aussi large que l'ecran");
            foreach (int s in new[] { 1, 2, 3 })
            {
                // une etiquette de 300 px centree sur le principal deborderait de l'ecran
                int spriteW = 2 * Orbit.HalfWidth * s, center = area.Left + 40;
                var (lo, hi) = LabelLayout.ScreenBounds(area, center, spriteW);
                var wanted = LabelLayout.Above(spriteW / 2, 20, new Size(300, 21));
                var r = LabelLayout.KeepInside(wanted, lo, hi);
                // le contrat de ScreenBounds : le bord gauche du tampon est a center - spriteW / 2
                int left = r.Left + center - spriteW / 2;
                T.Eq(area.Left, left, $"{s}x : calee sur le bord gauche de l'ecran");
                T.True(left + r.Width <= area.Right, $"{s}x : bord droit");
                T.Eq(wanted.Y, r.Y, "hauteur inchangee");
            }
        });

        T.Case("etiquette de mini sans conflit : reste au-dessus de sa tete", () =>
        {
            var wanted = new Rectangle(500, 100, 80, 20);
            var area = new MainArea(new Rectangle(0, 0, 50, 50), new Rectangle(0, 0, 60, 50), new Rectangle(0, 50, 50, 30));
            var r = LabelLayout.Place(wanted, area, new[] { new Rectangle(0, 200, 50, 20) }, -10000, 10000, 10000);
            T.Eq(wanted, r, "pas bougee");
            T.Eq(wanted, LabelLayout.Place(wanted, area, Array.Empty<Rectangle>(), -10000, 10000, 10000, sweep: 3), "rebond pris en compte, meme place");
        });

        T.Case("mini a gauche, principal colle au bord : l'etiquette ne part pas de l'autre cote", () =>
        {
            // cas vu a l'ecran : a 2x, principal a 116 px sprite du bord gauche, mini de devant a
            // 135 degres, etiquette de 345 px. A gauche du visage il n'y a pas la place ; a droite,
            // elle semblait appartenir au mini d'en face
            const int s = 2;
            var main = Shared.Library[Clips.Phone];
            var g = Geometry();
            var area = Area(main, false, s);
            var (dx, dy) = Orbit.At(135 * Math.PI / 180);
            var (r, _) = LabelLayout.MiniLabel(g.HalfW + dx, g.Feet + dy, 0, main.TopRow, new Size(345, 21), s,
                area, Array.Empty<Rectangle>(), 0, 100000, g.H * s);
            T.True(!r.IntersectsWith(area.Face), "pas sur le visage");
            T.True(r.X + r.Width / 2 < g.HalfW * s, $"du cote du mini : centre {r.X + r.Width / 2}, principal {g.HalfW * s}");
            T.True(r.Bottom <= g.H * s, "dans la fenetre");
        });

        T.Case("etiquette d'un mini qui rebondit : meme place pour un rebond de -1, 0 et +1, tout le tour au quart de degre", () =>
        {
            // avant : un pixel de rebond changeait la place choisie, et l'etiquette sautait de 136 px
            var lib = Shared.Library;
            var phone = lib[Clips.Phone];
            var g = Geometry();
            var size = new Size(345, 21);
            int positions = 0, beside = 0;
            foreach (var main in lib.All.Where(c => c.FaceBounds != null))
            foreach (bool mirror in new[] { false, true })
            foreach (int s in new[] { 1, 2, 3 })
            foreach (bool nearEdge in new[] { false, true })
            {
                int minX = nearEdge ? 0 : -100000, maxX = 100000;
                var area = Area(main, mirror, s);
                var face = area.Face;
                // le principal attend : son etiquette est deja posee
                var placed = new[] { LabelLayout.KeepInside(LabelLayout.Above(g.MainCenter * s, (g.MainTop + main.TopRow) * s, size), minX, maxX) };
                var wanted0 = LabelLayout.Above(0, 0, size);
                for (int q = 0; q < 360 * 4; q++)
                {
                    var (dx, dy) = Orbit.At(q / 4.0 * Math.PI / 180);
                    var shown = new Rectangle[3];
                    for (int bob = -1; bob <= 1; bob++)
                    {
                        // pieds a l'image courante : feet + dy + bob, comme PetController.Render
                        var (r, reserved) = LabelLayout.MiniLabel(g.HalfW + dx, g.Feet + dy, bob, phone.TopRow, size, s, area, placed, minX, maxX, g.H * s);
                        string where = $"{main.Name}{(mirror ? " miroir" : "")} a {q / 4.0} degres, {s}x, rebond {bob}{(nearEdge ? ", bord" : "")}";
                        if (r.IntersectsWith(face)) throw new Exception("visage de " + where);
                        if (r.Bottom > g.H * s) throw new Exception("sous la fenetre : " + where);
                        if (!reserved.Contains(r)) throw new Exception("hors de la zone reservee : " + where);
                        shown[bob + 1] = r;
                    }
                    string at = $"{main.Name}{(mirror ? " miroir" : "")} a {q / 4.0} degres, {s}x{(nearEdge ? ", bord" : "")}";
                    if (shown[0].X != shown[1].X || shown[2].X != shown[1].X)
                        throw new Exception($"place changee par le rebond ({shown[0].X}, {shown[1].X}, {shown[2].X}) : {at}");
                    if (shown[0].Y != shown[1].Y - s || shown[2].Y != shown[1].Y + s)
                        throw new Exception($"Y ne suit pas le seul rebond ({shown[0].Y}, {shown[1].Y}, {shown[2].Y}) : {at}");
                    if (shown[1].X != LabelLayout.KeepInside(wanted0 with { X = (g.HalfW + dx) * s - size.Width / 2 }, minX, maxX).X) beside++;
                    positions++;
                }
            }
            T.Info($"{positions} positions, {beside} ecartees de leur mini");
            T.True(beside > 0, "le cas se presente");
        });

        T.Case("arc arriere : l'etiquette d'un mini ne couvre ni la bulle ni le telephone du principal", () =>
        {
            // principal en attente au milieu de l'ecran, son etiquette posee, un mini fait le tour
            var lib = Shared.Library;
            var phone = lib[Clips.Phone];
            var g = Geometry();
            int back = 0;
            foreach (var main in lib.All.Where(c => c.FaceBounds != null))
            foreach (bool mirror in new[] { false, true })
            foreach (int s in new[] { 1, 2, 3 })
            foreach (var size in new[] { new Size(40, 21), new Size(110, 21), new Size(220, 32), new Size(345, 21) })
            {
                var area = Area(main, mirror, s);
                var head = area.Head;
                T.True(head.Contains(area.Face), "la zone de tete contient le visage de " + main.Name);
                var placed = new[] { LabelLayout.Above(g.MainCenter * s, (g.MainTop + main.TopRow) * s, size) };
                for (int deg = 0; deg < 360; deg += 2)
                {
                    var (dx, dy) = Orbit.At(deg * Math.PI / 180);
                    var (r, _) = LabelLayout.MiniLabel(g.HalfW + dx, g.Feet + dy, 0, phone.TopRow, size, s, area, placed, -100000, 100000, g.H * s);
                    if (r.IntersectsWith(head))
                        throw new Exception($"tete de {main.Name}{(mirror ? " miroir" : "")} couverte a {deg} degres, {s}x, {size.Width} px");
                    if (dy < 0) back++;
                }
            }
            T.Info($"{back} etiquettes de l'arc arriere verifiees");
        });

        T.Case("etiquettes des minis en attente : jamais sur le visage ni l'une sur l'autre, tout le tour", () =>
        {
            var lib = Shared.Library;
            var phone = lib[Clips.Phone];
            var g = Geometry();
            int checkedCount = 0, moved = 0, onHead = 0;
            foreach (var main in lib.All.Where(c => c.FaceBounds != null))
            foreach (bool mirror in new[] { false, true })
            foreach (int s in new[] { 1, 2, 3 })
            foreach (var size in new[] { new Size(40, 21), new Size(110, 21), new Size(220, 32), new Size(345, 21) })
            foreach (bool nearEdge in new[] { false, true })
            for (int deg = 0; deg < 360; deg += 4)
            {
                // le principal au plus pres du bord gauche que permet KeepOrbitOnScreen
                int minX = nearEdge ? 0 : -100000, maxX = 100000;
                var area = Area(main, mirror, s);
                var (face, head) = (area.Face, area.Head);
                var placed = new List<Rectangle>
                {
                    LabelLayout.KeepInside(LabelLayout.Above(g.MainCenter * s, (g.MainTop + main.TopRow) * s, size), minX, maxX),
                };
                // trois minis de session en attente, repartis sur l'orbite comme MiniCrowd ; chacun
                // a son rebond : la zone reservee de chaque etiquette couvre toute son amplitude
                for (int k = 0; k < 3; k++)
                {
                    double a = (deg + 120 * k) * Math.PI / 180;
                    var (dx, dy) = Orbit.At(a);
                    var (r, reserved) = LabelLayout.MiniLabel(g.HalfW + dx, g.Feet + dy, 0, phone.TopRow, size, s, area, placed, minX, maxX, g.H * s);
                    if (r != LabelLayout.Above((g.HalfW + dx) * s, (g.Feet + dy - SpriteLibrary.MiniBaseline + phone.TopRow / 2) * s, size)) moved++;
                    if (r.IntersectsWith(head)) onHead++;
                    string where = $"{main.Name}{(mirror ? " miroir" : "")} a {deg + 120 * k} degres, {s}x, {size.Width} px{(nearEdge ? ", bord" : "")}";
                    if (reserved.IntersectsWith(face)) throw new Exception("visage de " + where);
                    foreach (var other in placed)
                        if (reserved.IntersectsWith(other)) throw new Exception("etiquettes superposees : " + where);
                    if (reserved.Left < minX || reserved.Right > maxX) throw new Exception("hors de l'ecran : " + where);
                    if (reserved.Bottom > g.H * s) throw new Exception("sous la fenetre : " + where);
                    placed.Add(reserved);
                    checkedCount++;
                }
            }
            T.Info($"{checkedCount} etiquettes verifiees, {moved} deplacees, {onHead} sur la tete faute de place (jamais sur le visage)");
            T.True(moved > 0, "le cas se presente");
        });

        T.Case("zone de tete : la meme pour tous les clips, visage, bulle et combine compris, sans les accessoires", () =>
        {
            var lib = Shared.Library;
            var zone = lib.HeadZone;
            int n = SpriteLibrary.Size;
            foreach (var clip in lib.All.Where(c => c.FaceBounds != null))
            foreach (bool mirror in clip.Mirrorable ? new[] { false, true } : new[] { false })
            foreach (int s in new[] { 1, 3 })
            {
                var area = Area(clip, mirror, s);
                T.Eq(Area(lib[Clips.Idle], false, s).Head, area.Head, "zone de " + clip.Name);
                T.True(area.Head.Contains(area.Face), $"visage de {clip.Name}{(mirror ? " miroir" : "")} dans la zone");
            }
            // bulle et combine du telephone dedans ; socle et feux d'artifice, qui ne sont pas du
            // personnage, en partie dehors
            var phone = lib[Clips.Phone].Frames[0];
            bool Inside(int x, int y) => x >= zone.X0 && x <= zone.X1 && y >= zone.Y0 && y <= zone.Y1;
            T.True(phone[15 * n + 19] != 0 && Inside(19, 15), "bulle");
            T.True(phone[80 * n + 97] != 0 && Inside(97, 80), "combine");
            T.True(phone[115 * n + 120] != 0 && !Inside(120, 115), "socle dehors");
            var celebrate = lib[Clips.Celebrate];
            int fireworks = 0;
            foreach (var f in celebrate.Frames)
                for (int y = 0; y <= celebrate.FaceBounds!.Value.Y1; y++)
                for (int x = 0; x < n; x++)
                    if (f[y * n + x] != 0 && !Inside(x, y)) fireworks++;
            T.True(fireworks > 0, "feux d'artifice en partie hors de la zone");
            T.Info($"zone de tete x {zone.X0}..{zone.X1}, y {zone.Y0}..{zone.Y1} ; corps x {lib.BodyZone.X0}..{lib.BodyZone.X1}, y {lib.BodyZone.Y0}..{lib.BodyZone.Y1}");
        });

        T.Case("etiquette d'un mini : la meme place quel que soit le clip du principal, a chaque angle", () =>
        {
            // avant : la tete venait du clip courant, accessoires compris (feux d'artifice, bol,
            // tableau), et l'etiquette sautait jusqu'a 338 px quand le principal changeait de clip.
            // Milieu de l'ecran, sans autre etiquette : la place evite la zone de tete, qui ne
            // depend pas du clip
            var lib = Shared.Library;
            var phone = lib[Clips.Phone];
            var g = Geometry();
            var clips = lib.All.Where(c => c.FaceBounds != null).OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
            int positions = 0;
            foreach (bool left in new[] { false, true })
            foreach (int s in new[] { 1, 2, 3 })
            foreach (var size in new[] { new Size(40, 21), new Size(110, 21), new Size(345, 21) })
            {
                // tourne a gauche, seuls les clips qui le permettent sont en miroir (PetController.Render)
                var areas = clips.Select(c => (c.Name, Area: Area(c, left && c.Mirrorable, s))).ToList();
                for (int deg = 0; deg < 360; deg++)
                {
                    var (dx, dy) = Orbit.At(deg * Math.PI / 180);
                    Rectangle? first = null;
                    foreach (var (name, area) in areas)
                    {
                        var (r, _) = LabelLayout.MiniLabel(g.HalfW + dx, g.Feet + dy, 0, phone.TopRow, size, s, area,
                            Array.Empty<Rectangle>(), -100000, 100000, g.H * s);
                        string where = $"{deg} degres, {s}x, {size.Width} px{(left ? ", tourne a gauche" : "")}";
                        if (r.IntersectsWith(area.Face)) throw new Exception($"visage de {name} : {where}");
                        if (first is not { } f) first = r;
                        else if (r != f) throw new Exception($"{clips[0].Name} {f}, {name} {r} : {where}");
                    }
                    positions++;
                }
            }
            T.Info($"{positions} positions, {clips.Count} clips chacune");
        });

        T.Case("bord de l'ecran : l'etiquette d'un mini reste de son cote du principal, jamais sur son ventre", () =>
        {
            // avant : au bord, 17 a 21 % des etiquettes passaient de l'autre cote du principal, ou
            // tombaient sur son ventre (la place sous l'obstacle). Principal au plus pres du bord
            // que permet KeepOrbitOnScreen, a gauche puis a droite ; son etiquette posee
            var lib = Shared.Library;
            var phone = lib[Clips.Phone];
            var g = Geometry();
            int checkedCount = 0, sides = 0, forced = 0;
            foreach (var main in lib.All.Where(c => c.FaceBounds != null))
            foreach (bool mirror in main.Mirrorable ? new[] { false, true } : new[] { false })
            foreach (int s in new[] { 1, 2, 3 })
            foreach (var size in new[] { new Size(40, 21), new Size(110, 21), new Size(220, 32), new Size(345, 21) })
            foreach (bool leftEdge in new[] { true, false })
            {
                int minX = leftEdge ? 0 : -100000, maxX = leftEdge ? 100000 : 2 * g.HalfW * s;
                var area = Area(main, mirror, s);
                int center = g.MainCenter * s;
                var mainLabel = LabelLayout.KeepInside(LabelLayout.Above(center, (g.MainTop + main.TopRow) * s, size), minX, maxX);
                for (int deg = 0; deg < 360; deg += 2)
                {
                    var (dx, dy) = Orbit.At(deg * Math.PI / 180);
                    var (r, reserved) = LabelLayout.MiniLabel(g.HalfW + dx, g.Feet + dy, 0, phone.TopRow, size, s, area,
                        new[] { mainLabel }, minX, maxX, g.H * s);
                    string where = $"{main.Name}{(mirror ? " miroir" : "")} a {deg} degres, {s}x, {size.Width} px, bord {(leftEdge ? "gauche" : "droit")}";
                    if (reserved.IntersectsWith(area.Face)) throw new Exception("visage de " + where);
                    if (reserved.IntersectsWith(mainLabel)) throw new Exception("sur l'etiquette du principal : " + where);
                    if (reserved.Left < minX || reserved.Right > maxX) throw new Exception("hors de l'ecran : " + where);
                    // arc arriere : le mini est derriere le principal, son etiquette n'a rien a faire sur son corps
                    if (dy < 0 && r.IntersectsWith(area.Body)) throw new Exception("sur le ventre : " + where);
                    checkedCount++;
                    if (Math.Abs(dx) <= 30) continue;
                    sides++;
                    int offset = r.X + r.Width / 2 - center;
                    if (Math.Sign(offset) == Math.Sign(dx) || Math.Abs(offset) <= 20) continue;
                    // de l'autre cote : seulement si l'etiquette, calee sur le bord, n'a pas la place de son cote
                    var wanted = LabelLayout.Above((g.HalfW + dx) * s, 0, size);
                    int inside = LabelLayout.KeepInside(wanted, minX, maxX).X + size.Width / 2 - center;
                    if (Math.Sign(inside) == Math.Sign(dx) && Math.Abs(inside) > 20)
                        throw new Exception($"de l'autre cote ({r}, centre {offset:+#;-#;0} px du principal) : {where}");
                    forced++;
                }
            }
            T.Info($"{checkedCount} etiquettes, {sides} de cote, {forced} de l'autre cote faute de largeur entre le bord et le principal");
        });
    }

    /// <summary>Visage, zone de tete et corps du principal pose comme PetController.Render, orbite pleine.</summary>
    static MainArea Area(RenderClip main, bool mirror, int s)
    {
        var g = Geometry();
        return LabelLayout.AreaOf(main, mirror, Shared.Library, g.MainLeft, g.MainTop, s)!.Value;
    }

    /// <summary>Placement de PetController.Render, orbite pleine : le principal monte de Orbit.Lift, en pixels sprite.</summary>
    static (int HalfW, int H, int MainLeft, int MainTop, int MainCenter, int Feet) Geometry()
    {
        int halfW = Orbit.HalfWidth, h = SpriteLibrary.Size + Orbit.Lift;
        int mainLeft = halfW - SpriteLibrary.Size / 2, mainTop = h - SpriteLibrary.Size - Orbit.Lift;
        return (halfW, h, mainLeft, mainTop, mainLeft + SpriteLibrary.Size / 2, mainTop + SpriteLibrary.Baseline);
    }
}
