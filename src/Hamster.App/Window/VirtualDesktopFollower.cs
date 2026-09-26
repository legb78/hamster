using System.Runtime.InteropServices;

namespace Hamster.App.Window;

/// <summary>
/// Suivi des bureaux virtuels.
///
/// macOS a NSWindowCollectionBehavior.canJoinAllSpaces ; Windows n'a aucun
/// equivalent public. Epingler une fenetre sur tous les bureaux passe par
/// IVirtualDesktopManagerInternal, non documentee, dont l'IID change a chaque
/// build de Windows -- exactement le genre de contournement qu'on ne veut pas.
///
/// Le repli n'utilise que l'API publique IVirtualDesktopManager : on regarde si
/// la fenetre est sur le bureau courant, et sinon on la deplace vers le bureau de
/// la fenetre au premier plan (qui est par definition sur le bureau courant).
/// Cout : un appel COM par seconde. Rancon : le personnage arrive avec au plus
/// une seconde de retard apres un changement de bureau.
/// </summary>
internal sealed class VirtualDesktopFollower
{
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktopId);
    }

    static readonly Guid ClsidVirtualDesktopManager = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");

    readonly IVirtualDesktopManager? _manager;
    int _failures;

    public bool Available => _manager != null && _failures < 5;
    public string Status => _manager == null ? "indisponible"
        : _failures >= 5 ? "abandonne" : "actif";

    public VirtualDesktopFollower()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidVirtualDesktopManager);
            _manager = type == null ? null : Activator.CreateInstance(type) as IVirtualDesktopManager;
        }
        catch (Exception e)
        {
            Diagnostics.Warn("bureaux virtuels indisponibles: " + e.Message);
            _manager = null;
        }
    }

    /// <summary>Ramene la fenetre sur le bureau courant si elle n'y est plus. True si un deplacement a eu lieu.</summary>
    public bool EnsureOnCurrentDesktop(IntPtr hwnd)
    {
        if (!Available || hwnd == IntPtr.Zero) return false;
        try
        {
            if (_manager!.IsWindowOnCurrentVirtualDesktop(hwnd, out int onCurrent) != 0) return false;
            if (onCurrent != 0) return false;

            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == hwnd) return false;
            if (_manager.GetWindowDesktopId(fg, out Guid target) != 0 || target == Guid.Empty) return false;
            if (_manager.MoveWindowToDesktop(hwnd, ref target) != 0) { _failures++; return false; }

            _failures = 0;
            return true;
        }
        catch (Exception e)
        {
            _failures++;
            Diagnostics.Warn("suivi de bureau virtuel: " + e.Message);
            return false;
        }
    }
}
