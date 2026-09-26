namespace Hamster.App.State;

internal enum MotionMode { Standing, Walking }

/// <summary>
/// La balade. La position est tenue en pixels SPRITE, pas en pixels ecran : c'est
/// ce qui garantit que le personnage ne se deplace que par pas entiers de pixels
/// du sprite, quel que soit le facteur d'echelle. Sinon la grille du pixel art
/// glisse sous le dessin et tout scintille.
/// </summary>
internal sealed class PetMotion
{
    const double SpeedSpritePxPerSecond = 13.0;

    readonly Random _rng = new();

    /// <summary>Position du centre du personnage, en pixels sprite depuis le bord gauche de l'ecran.</summary>
    public double X { get; private set; }
    public int Facing { get; private set; } = 1;   // 1 = vers la droite
    public MotionMode Mode { get; private set; } = MotionMode.Standing;

    double _untilNextDecision;
    double _minX, _maxX;
    Movement _movement = Movement.Roam;

    public void SetBounds(double minX, double maxX)
    {
        _minX = minX;
        _maxX = Math.Max(minX, maxX);
        X = Math.Clamp(X, _minX, _maxX);
    }

    public void PlaceAt(double x) => X = Math.Clamp(x, _minX, _maxX);

    /// <summary>Apres un drag : il repart de la, sans repartir en sens inverse par surprise.</summary>
    public void Settle(double x)
    {
        X = Math.Clamp(x, _minX, _maxX);
        Mode = MotionMode.Standing;
        _untilNextDecision = 1.0 + _rng.NextDouble();
    }

    /// <summary>
    /// Avance d'un pas. Le directeur impose le mode : immobile pendant le travail et
    /// l'attente, marche pendant la petite balade du repos, libre avant le mode chill.
    /// </summary>
    public void Tick(double seconds, Movement movement, double speedFactor = 1.0)
    {
        if (movement != _movement)
        {
            _movement = movement;
            if (movement == Movement.Walk) StartWalking();
            // la balade libre reprend apres un petit temps, pas au milieu d'un pas
            else if (movement == Movement.Roam) { Mode = MotionMode.Standing; _untilNextDecision = 1.0 + _rng.NextDouble(); }
        }

        switch (movement)
        {
            case Movement.Hold:
                Mode = MotionMode.Standing;
                return;
            case Movement.Roam:
                _untilNextDecision -= seconds;
                if (_untilNextDecision <= 0) Decide();
                break;
            default:
                Mode = MotionMode.Walking;
                break;
        }

        if (Mode != MotionMode.Walking) return;

        X += Facing * SpeedSpritePxPerSecond * speedFactor * seconds;
        if (X <= _minX) { X = _minX; Facing = 1; }
        else if (X >= _maxX) { X = _maxX; Facing = -1; }
    }

    void Decide()
    {
        // 60 % de pauses : un pet qui marche sans arret fait un pendule, c'est fatigant
        if (_rng.NextDouble() < 0.6)
        {
            Mode = MotionMode.Standing;
            _untilNextDecision = 2.0 + _rng.NextDouble() * 4.0;
        }
        else
        {
            _untilNextDecision = 1.0 + _rng.NextDouble() * 2.5;
            StartWalking();
        }
    }

    void StartWalking()
    {
        Mode = MotionMode.Walking;
        bool nearLeft = X - _minX < 40;
        bool nearRight = _maxX - X < 40;
        if (nearLeft) Facing = 1;
        else if (nearRight) Facing = -1;
        else if (_rng.NextDouble() < 0.35) Facing = -Facing;
    }
}
