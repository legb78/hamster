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

        T.Case("cachee par le plafond de minis puis revenue : pas de nouvelle sonnerie", () =>
        {
            var bell = new WaitingBell();
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "b attend");
            // six sous-agents recents poussent b hors de l'instantane
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 1)), "b cachee");
            T.Eq("", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "b revenue");
            // sans debordement, une absence veut dire fin
            T.Eq("", Joined(bell, Snap("a", PetState.Working)), "b partie");
            T.Eq("b", Joined(bell, Snap("a", PetState.Working, 0, Mini("b", PetState.WaitingUser))), "nouvelle attente de b");
        });

        T.Case("amorcage : l'attente vue en silence ne sonne pas ensuite", () =>
        {
            // le controleur appelle Update a chaque instantane et ne joue le son qu'apres 3 s :
            // les attentes relues au lancement sont donc deja dans l'ensemble
            var bell = new WaitingBell();
            T.Eq("a", Joined(bell, Snap("a", PetState.WaitingUser)), "vue pendant l'amorcage");
            T.Eq("", Joined(bell, Snap("a", PetState.WaitingUser)), "rien apres l'amorcage");
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

        T.Case("hamster colle au bord gauche : etiquettes dans l'ecran, meme geometrie que le rendu", () =>
        {
            // PetController : le tampon agrandi est centre sur le principal, et une etiquette de
            // 300 px centree sur un principal a 40 px du bord deborderait de 110 px
            var area = new Rectangle(-1920, 0, 1920, 1040);
            foreach (int s in new[] { 1, 2, 3 })
            {
                int spriteW = SpriteLibrary.Size * s;
                int center = area.Left + 40;
                int spriteLeft = center - spriteW / 2;
                var wanted = LabelLayout.Above(spriteW / 2, 20, new Size(300, 21));
                var r = LabelLayout.KeepInside(wanted, area.Left - spriteLeft, area.Right - spriteLeft);
                T.True(r.Left + spriteLeft >= area.Left, $"{s}x : bord gauche {r.Left + spriteLeft}");
                T.True(r.Right + spriteLeft <= area.Right, $"{s}x : bord droit");
                T.Eq(wanted.Y, r.Y, "hauteur inchangee");
            }
        });

        T.Case("etiquette de mini sans conflit : reste au-dessus de sa tete", () =>
        {
            var wanted = new Rectangle(500, 100, 80, 20);
            var r = LabelLayout.Place(wanted, new Rectangle(0, 0, 50, 50), new[] { new Rectangle(0, 200, 50, 20) }, -10000, 10000, 10000);
            T.Eq(wanted, r, "pas bougee");
        });

        T.Case("mini a gauche, principal colle au bord : l'etiquette ne part pas de l'autre cote", () =>
        {
            // cas vu a l'ecran : a 2x, principal a 116 px sprite du bord gauche, mini de devant a
            // 135 degres, etiquette de 345 px. A gauche du visage il n'y a pas la place ; a droite,
            // elle semblait appartenir au mini d'en face
            const int s = 2;
            var main = Shared.Library[Clips.Phone];
            int halfW = Orbit.HalfWidth, h = SpriteLibrary.Size + Orbit.Lift;
            int mainLeft = halfW - SpriteLibrary.Size / 2, mainTop = h - SpriteLibrary.Size - Orbit.Lift;
            var face = LabelLayout.FaceRect(main, false, mainLeft, mainTop, s)!.Value;
            var (dx, dy) = Orbit.At(135 * Math.PI / 180);
            int top = mainTop + SpriteLibrary.Baseline + dy - SpriteLibrary.MiniBaseline + Shared.Library[Clips.Phone].TopRow / 2;
            var wanted = LabelLayout.Above((halfW + dx) * s, top * s, new Size(345, 21));
            var r = LabelLayout.Place(wanted, face, Array.Empty<Rectangle>(), 0, 100000, h * s);
            T.True(!r.IntersectsWith(face), "pas sur le visage");
            T.True(r.X + r.Width / 2 < halfW * s, $"du cote du mini : centre {r.X + r.Width / 2}, principal {halfW * s}");
            T.True(r.Bottom <= h * s, "dans la fenetre");
        });

        T.Case("etiquettes des minis en attente : jamais sur le visage ni l'une sur l'autre, tout le tour", () =>
        {
            var lib = Shared.Library;
            var phone = lib[Clips.Phone];
            int checkedCount = 0, moved = 0;
            foreach (var main in lib.All.Where(c => c.FaceBounds != null))
            foreach (bool mirror in new[] { false, true })
            foreach (int s in new[] { 1, 2, 3 })
            foreach (var size in new[] { new Size(40, 21), new Size(110, 21), new Size(220, 32), new Size(345, 21) })
            foreach (bool nearEdge in new[] { false, true })
            for (int deg = 0; deg < 360; deg += 4)
            {
                // meme placement que PetController.Render, orbite pleine : principal monte de Lift
                int halfW = Orbit.HalfWidth, h = SpriteLibrary.Size + Orbit.Lift;
                int mainLeft = halfW - SpriteLibrary.Size / 2, mainTop = h - SpriteLibrary.Size - Orbit.Lift;
                int feet = mainTop + SpriteLibrary.Baseline;
                // le principal au plus pres du bord gauche que permet KeepOrbitOnScreen
                int minX = nearEdge ? 0 : -100000, maxX = 100000;
                var face = LabelLayout.FaceRect(main, mirror, mainLeft, mainTop, s)!.Value;
                var placed = new List<Rectangle>
                {
                    LabelLayout.KeepInside(LabelLayout.Above((mainLeft + SpriteLibrary.Size / 2) * s, (mainTop + main.TopRow) * s, size), minX, maxX),
                };
                // trois minis de session en attente, repartis sur l'orbite comme MiniCrowd
                for (int k = 0; k < 3; k++)
                {
                    double a = (deg + 120 * k) * Math.PI / 180;
                    var (dx, dy) = Orbit.At(a);
                    int top = feet + dy - SpriteLibrary.MiniBaseline + phone.TopRow / 2;
                    var wanted = LabelLayout.Above((halfW + dx) * s, top * s, size);
                    var r = LabelLayout.Place(wanted, face, placed, minX, maxX, h * s);
                    if (r != wanted) moved++;
                    string where = $"{main.Name}{(mirror ? " miroir" : "")} a {deg + 120 * k} degres, {s}x, {size.Width} px{(nearEdge ? ", bord" : "")}";
                    if (r.IntersectsWith(face)) throw new Exception("visage de " + where);
                    foreach (var other in placed)
                        if (r.IntersectsWith(other)) throw new Exception("etiquettes superposees : " + where);
                    if (r.Left < minX || r.Right > maxX) throw new Exception("hors de l'ecran : " + where);
                    if (r.Bottom > h * s) throw new Exception("sous la fenetre : " + where);
                    placed.Add(r);
                    checkedCount++;
                }
            }
            T.Info($"{checkedCount} etiquettes verifiees, {moved} deplacees");
            T.True(moved > 0, "le cas se presente");
        });
    }
}
