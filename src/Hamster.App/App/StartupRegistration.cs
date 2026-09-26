using Microsoft.Win32;

namespace Hamster.App;

/// <summary>
/// "Lancer au demarrage" : une valeur de HKCU\...\Run, sans droit administrateur. Elle n'est
/// ecrite ou retiree que par le clic sur le menu, jamais au lancement. Le nom de la valeur est
/// injectable (constructeur, ou variable HAMSTER_RUN_VALUE) pour que les tests travaillent
/// sur "HamsterTest" et ne touchent jamais a la vraie entree.
/// </summary>
internal sealed class StartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    /// <summary>
    /// Ce que le Gestionnaire des taches retient de chaque entree Run : une valeur binaire de
    /// meme nom, non documentee. Sur le poste de developpement, 12 octets dont le premier vaut
    /// 0x02 pour une entree active et 0x03 pour une entree desactivee (suivi, le plus souvent,
    /// de ce qui se lit comme une date FILETIME) ; aucune autre valeur n'y a ete vue. Absente,
    /// l'entree est active.
    /// </summary>
    public const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    /// <summary>Premier octet d'une entree desactivee dans le Gestionnaire des taches.</summary>
    public const byte ApprovedDisabled = 0x03;
    public const string DefaultValueName = "Hamster";

    public StartupRegistration(string? valueName = null, string? exePath = null)
    {
        var env = Environment.GetEnvironmentVariable("HAMSTER_RUN_VALUE");
        ValueName = valueName ?? (string.IsNullOrWhiteSpace(env) ? DefaultValueName : env);
        ExePath = exePath ?? Environment.ProcessPath ?? throw new InvalidOperationException("chemin de l'exe inconnu");
    }

    public string ValueName { get; }
    public string ExePath { get; }
    /// <summary>La commande ecrite : le chemin de l'exe entre guillemets, qui peut contenir des espaces.</summary>
    public string Command => "\"" + ExePath + "\"";

    /// <summary>Valeur actuelle, null si absente.</summary>
    public string? CurrentValue
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) as string;
        }
    }

    /// <summary>
    /// Vrai si le Gestionnaire des taches a desactive l'entree : Explorer saute alors la valeur
    /// Run a l'ouverture de session. Seul le 0x03 observe compte : une autre valeur, qu'on n'a
    /// jamais vue, n'est pas prise pour une desactivation.
    /// </summary>
    public bool DisabledByTaskManager
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } flags && flags[0] == ApprovedDisabled;
        }
    }

    /// <summary>Le hamster se lancera a l'ouverture de session : la valeur Run existe et n'est pas desactivee.</summary>
    public bool IsEnabled => CurrentValue != null && !DisabledByTaskManager;

    /// <summary>
    /// Ce que fait le clic : active si le demarrage n'aurait pas lieu (valeur absente, ou
    /// desactivee dans le Gestionnaire des taches), retire sinon. Rend le nouvel etat.
    /// </summary>
    public bool Toggle()
    {
        if (IsEnabled) Disable();
        else Enable();
        return IsEnabled;
    }

    public void Enable()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        // cocher la case, c'est vouloir le demarrage : on leve une desactivation faite dans le
        // Gestionnaire des taches, comme le faisait install.ps1
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
