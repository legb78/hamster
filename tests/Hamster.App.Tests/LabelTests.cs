using System.Drawing;
using Hamster.Activity;
using Hamster.App.Render;
using Hamster.App.State;
using Hamster.Art;

namespace Hamster.App.Tests;

static class LabelTests
{
    static MiniInfo Mini(string id, PetState state, string kind = "session") =>
        new(id, kind, "projet-" + id, id, state, DateTimeOffset.UnixEpoch);

    static ActivitySnapshot Snap(string? main, PetState state, int overflow = 0, params MiniInfo[] minis) =>
        new(state, main, main == null ? null : "projet-" + main, null, main != null, minis, overflow);

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
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "b revient en attente");
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
            // sans debordement, une absence veut dire fin
            T.Eq("", Joined(bell, Snap("a", PetState.Working)), "b partie");
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "nouvelle attente de b");
        });

        T.Case("absente alors que des sous-agents debordent : l'attente est finie, la suivante sonne", () =>
        {
            var bell = new WaitingBell();
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "b attend");
            var subs = Enumerable.Range(1, 6).Select(i => Mini("s" + i, PetState.Working, "subagent")).ToArray();
            // une attente passerait devant les sous-agents : absente, b ne l'est plus
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 2, subs)), "b absente, sous-agents en trop");
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 2, subs.Take(5).Prepend(Mini("b", PetState.WaitingUser)).ToArray())),
                "nouvelle attente de b : sonne");
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
            var r = LabelLayout.Place(wanted, new Rectangle(0, 0, 50, 50), new Rectangle(0, 0, 60, 50), new[] { new Rectangle(0, 200, 50, 20) }, -10000, 10000, 10000);
            T.Eq(wanted, r, "pas bougee");
            T.Eq(wanted, LabelLayout.Place(wanted, new Rectangle(0, 0, 50, 50), null, Array.Empty<Rectangle>(), -10000, 10000, 10000, sweep: 3), "rebond pris en compte, meme place");
        });

        T.Case("mini a gauche, principal colle au bord : l'etiquette ne part pas de l'autre cote", () =>
        {
            // cas vu a l'ecran : a 2x, principal a 116 px sprite du bord gauche, mini de devant a
            // 135 degres, etiquette de 345 px. A gauche du visage il n'y a pas la place ; a droite,
            // elle semblait appartenir au mini d'en face
            const int s = 2;
            var main = Shared.Library[Clips.Phone];
            var g = Geometry();
            var face = LabelLayout.FaceRect(main, false, g.MainLeft, g.MainTop, s)!.Value;
            var head = LabelLayout.HeadRect(main, false, g.MainLeft, g.MainTop, s);
            var (dx, dy) = Orbit.At(135 * Math.PI / 180);
            var (r, _) = LabelLayout.MiniLabel(g.HalfW + dx, g.Feet + dy, 0, main.TopRow, new Size(345, 21), s,
                face, head, Array.Empty<Rectangle>(), 0, 100000, g.H * s);
            T.True(!r.IntersectsWith(face), "pas sur le visage");
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
                var face = LabelLayout.FaceRect(main, mirror, g.MainLeft, g.MainTop, s)!.Value;
                var head = LabelLayout.HeadRect(main, mirror, g.MainLeft, g.MainTop, s);
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
                        var (r, reserved) = LabelLayout.MiniLabel(g.HalfW + dx, g.Feet + dy, bob, phone.TopRow, size, s, face, head, placed, minX, maxX, g.H * s);
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
                var face = LabelLayout.FaceRect(main, mirror, g.MainLeft, g.MainTop, s)!.Value;
                var head = LabelLayout.HeadRect(main, mirror, g.MainLeft, g.MainTop, s)!.Value;
                T.True(head.Contains(face), "la tete contient le visage");
                var placed = new[] { LabelLayout.Above(g.MainCenter * s, (g.MainTop + main.TopRow) * s, size) };
                for (int deg = 0; deg < 360; deg += 2)
                {
                    var (dx, dy) = Orbit.At(deg * Math.PI / 180);
                    var (r, _) = LabelLayout.MiniLabel(g.HalfW + dx, g.Feet + dy, 0, phone.TopRow, size, s, face, head, placed, -100000, 100000, g.H * s);
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
                var face = LabelLayout.FaceRect(main, mirror, g.MainLeft, g.MainTop, s)!.Value;
                var head = LabelLayout.HeadRect(main, mirror, g.MainLeft, g.MainTop, s)!.Value;
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
                    var (r, reserved) = LabelLayout.MiniLabel(g.HalfW + dx, g.Feet + dy, 0, phone.TopRow, size, s, face, head, placed, minX, maxX, g.H * s);
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
    }

    /// <summary>Placement de PetController.Render, orbite pleine : le principal monte de Orbit.Lift, en pixels sprite.</summary>
    static (int HalfW, int H, int MainLeft, int MainTop, int MainCenter, int Feet) Geometry()
    {
        int halfW = Orbit.HalfWidth, h = SpriteLibrary.Size + Orbit.Lift;
        int mainLeft = halfW - SpriteLibrary.Size / 2, mainTop = h - SpriteLibrary.Size - Orbit.Lift;
        return (halfW, h, mainLeft, mainTop, mainLeft + SpriteLibrary.Size / 2, mainTop + SpriteLibrary.Baseline);
    }
}
