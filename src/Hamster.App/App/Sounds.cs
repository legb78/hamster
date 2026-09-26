using System.Media;
using System.Runtime.InteropServices;

namespace Hamster.App;

/// <summary>
/// Les sons, tous des .wav de Windows (C:\Windows\Media) : rien a embarquer. Jamais en boucle,
/// et coupes des que SHQueryUserNotificationState ne repond pas QUNS_ACCEPTS_NOTIFICATIONS :
/// application plein ecran, mode presentation, session verrouillee ou economiseur d'ecran,
/// d'apres les commentaires de shellapi.h. Le mode "ne pas deranger" de Windows 11 n'y figure
/// pas : non verifie.
/// </summary>
internal sealed class Sounds : IDisposable
{
    public const string Phone = "Windows Ringin.wav";
    public const string Done = "tada.wav";
    public const string Pop = "Windows Navigation Start.wav";

    /// <summary>Pas plus d'un pop de sous-agent toutes les 10 s : un workflow en lance des dizaines.</summary>
    static readonly TimeSpan PopInterval = TimeSpan.FromSeconds(10);

    const int QUNS_ACCEPTS_NOTIFICATIONS = 5;

    [DllImport("shell32.dll")]
    static extern int SHQueryUserNotificationState(out int state);

    readonly Func<bool> _enabled;
    readonly Dictionary<string, SoundPlayer> _players = new(StringComparer.OrdinalIgnoreCase);
    readonly string _media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
    long _lastPopTicks = long.MinValue;

    public Sounds(Func<bool> enabled) => _enabled = enabled;

    public void PlayPhone() => Play(Phone);
    public void PlayDone() => Play(Done);

    public void PlayPop()
    {
        long now = Environment.TickCount64;
        if (_lastPopTicks != long.MinValue && now - _lastPopTicks < PopInterval.TotalMilliseconds) return;
        if (Play(Pop)) _lastPopTicks = now;
    }

    /// <summary>Vrai si le son est parti.</summary>
    bool Play(string file)
    {
        if (!_enabled()) return false;
        int hr = SHQueryUserNotificationState(out int state);
        if (hr < 0 || state != QUNS_ACCEPTS_NOTIFICATIONS)
        {
            Diagnostics.Info($"son {file} coupe (notifications: {(hr < 0 ? "inconnu" : state.ToString())})");
            return false;
        }
        try
        {
            if (!_players.TryGetValue(file, out var player))
            {
                player = new SoundPlayer(Path.Combine(_media, file));
                _players[file] = player;
            }
            // Play part sur un thread du systeme et rend la main tout de suite
            player.Play();
            Diagnostics.Info("son " + file);
            return true;
        }
        catch (Exception e)
        {
            Diagnostics.Warn($"son {file} impossible: {e.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var p in _players.Values) p.Dispose();
        _players.Clear();
    }
}
