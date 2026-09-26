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
    /// <summary>Une entree desactivee dans le Gestionnaire des taches laisse ici une valeur qui la fait sauter.</summary>
    public const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
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

    public bool IsEnabled => CurrentValue != null;

    /// <summary>Ce que fait le clic : active si absent, retire sinon. Rend le nouvel etat.</summary>
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
