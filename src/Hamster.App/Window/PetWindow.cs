using static Hamster.App.Window.Native;

namespace Hamster.App.Window;

/// <summary>
/// La fenetre du personnage. Un Form sert uniquement de porteur de HWND et de
/// boucle de messages : rien n'est jamais peint par WinForms, tout passe par
/// UpdateLayeredWindow.
///
/// Le click-through est gratuit ici : pour une fenetre layered en alpha par pixel,
/// Windows fait son hit-test sur l'alpha, donc les pixels transparents laissent
/// passer la souris sans une ligne de code. Pas de moniteur global d'evenements.
/// </summary>
internal sealed class PetWindow : Form
{
    IntPtr _powerNotify = IntPtr.Zero;

    public event Action<bool>? DisplayPowerChanged;
    public event Action? DisplayLayoutChanged;

    public PetWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Text = "HamsterPet";
        // Rien ne doit repeindre : UpdateLayeredWindow est la seule source de pixels.
        SetStyle(ControlStyles.Opaque, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>Un clic sur le hamster ne doit jamais interrompre ce que l'utilisateur tape.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _powerNotify = RegisterPowerSettingNotification(Handle, ref GuidSessionDisplayStatus,
            DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_powerNotify != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_powerNotify);
            _powerNotify = IntPtr.Zero;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_POWERBROADCAST when (int)m.WParam == PBT_POWERSETTINGCHANGE:
            {
                var setting = System.Runtime.InteropServices.Marshal
                    .PtrToStructure<POWERBROADCAST_SETTING>(m.LParam);
                if (setting.PowerSetting == GuidSessionDisplayStatus)
                    DisplayPowerChanged?.Invoke(setting.Data != 0);   // 0 = ecran eteint
                break;
            }
            case 0x007E: // WM_DISPLAYCHANGE
                DisplayLayoutChanged?.Invoke();
                break;
        }
        base.WndProc(ref m);
    }

    public void AssertTopMost() =>
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}
