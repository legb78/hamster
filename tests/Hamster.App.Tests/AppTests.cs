using Microsoft.Win32;

namespace Hamster.App.Tests;

static class AppTests
{
    const string TestValue = "HamsterTest";

    public static void Run()
    {
        T.Suite("Reglages et demarrage");

        T.Case("reglages : valeurs par defaut, dossier redirige par HAMSTER_SETTINGS_DIR", () =>
        {
            string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hamster", "settings.json");
            DateTime realStamp = File.Exists(real) ? File.GetLastWriteTimeUtc(real) : default;
            string dir = Path.Combine(Path.GetTempPath(), "hamster-app-tests-" + Guid.NewGuid().ToString("N"));
            string? before = Environment.GetEnvironmentVariable("HAMSTER_SETTINGS_DIR");
            try
            {
                Environment.SetEnvironmentVariable("HAMSTER_SETTINGS_DIR", dir);
                T.Eq(dir, Settings.Directory, "dossier");
                var s = Settings.Load();
                T.Eq(false, s.OnlyWithClaudeDesktop, "OnlyWithClaudeDesktop");
                T.Eq(10, s.ChillDelaySeconds, "ChillDelaySeconds");
                T.Eq(false, s.SoundEnabled, "SoundEnabled");
                s.ChillDelaySeconds = 30;
                s.SoundEnabled = true;
                s.Save();
                T.True(File.Exists(Path.Combine(dir, "settings.json")), "ecrit dans le dossier redirige");
                var again = Settings.Load();
                T.Eq(30, again.ChillDelaySeconds, "relu");
                T.Eq(true, again.SoundEnabled, "relu");

                // un fichier d'avant ce changement garde sa valeur : le defaut ne s'applique qu'aux cles absentes
                File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"OnlyWithClaudeDesktop\": true, \"ChillDelaySeconds\": 0 }");
                var old = Settings.Load();
                T.Eq(true, old.OnlyWithClaudeDesktop, "valeur explicite gardee");
                T.Eq(1, old.ChillDelaySeconds, "delai borne");
            }
            finally
            {
                Environment.SetEnvironmentVariable("HAMSTER_SETTINGS_DIR", before);
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
            T.Eq(realStamp, File.Exists(real) ? File.GetLastWriteTimeUtc(real) : default, "vrai settings.json intact");
        });

        T.Case("lancer au demarrage : HamsterTest ecrit puis retire, la vraie entree intacte", () =>
        {
            string? realBefore = RunValue("Hamster");
            const string exe = @"C:\Program Files\Test Hamster\Hamster.exe";
            var reg = new StartupRegistration(TestValue, exe);
            try
            {
                reg.Disable();
                T.True(!reg.IsEnabled, "absent au depart");
                T.Eq(true, reg.Toggle(), "clic : active");
                T.Eq("\"" + exe + "\"", RunValue(TestValue), "chemin entre guillemets");
                T.Eq(RegistryValueKind.String, RunKind(TestValue), "type REG_SZ");
                T.Eq(false, reg.Toggle(), "second clic : retire");
                T.Eq(null, RunValue(TestValue), "valeur retiree");
            }
            finally
            {
                reg.Disable();
            }
            T.Eq(null, RunValue(TestValue), "rien ne reste");
            T.Eq(realBefore, RunValue("Hamster"), "vraie entree Hamster intacte");
        });

        T.Case("lancer au demarrage : desactive dans le Gestionnaire des taches = decoche, le clic reactive", () =>
        {
            string? realBefore = RunValue("Hamster");
            byte[]? realApprovedBefore = ApprovedValue("Hamster");
            const string exe = @"C:\Program Files\Test Hamster\Hamster.exe";
            var reg = new StartupRegistration(TestValue, exe);
            bool createdKey = false;
            try
            {
                reg.Enable();
                T.True(reg.IsEnabled, "active");

                // la marque que laisse le Gestionnaire des taches, telle qu'on la lit sur ce poste :
                // 0x03, trois octets nuls, puis huit octets qui se lisent comme une date FILETIME
                var disabled = new byte[12];
                disabled[0] = StartupRegistration.ApprovedDisabled;
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(disabled, 4);
                createdKey = WriteApproved(TestValue, disabled);
                T.True(reg.DisabledByTaskManager, "marque lue");
                T.True(reg.CurrentValue != null, "la valeur Run est toujours la");
                T.True(!reg.IsEnabled, "decoche : Explorer ne le lancera pas");

                T.Eq(true, reg.Toggle(), "clic : reactive, ne retire pas");
                T.Eq("\"" + exe + "\"", RunValue(TestValue), "valeur Run");
                T.Eq(null, ApprovedValue(TestValue), "marque levee");
                T.True(reg.IsEnabled, "coche");

                // 0x02 : l'etat d'une entree active dans le Gestionnaire des taches
                WriteApproved(TestValue, new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
                T.True(!reg.DisabledByTaskManager && reg.IsEnabled, "0x02 : active");
                T.Eq(false, reg.Toggle(), "clic suivant : retire");
                T.Eq(null, RunValue(TestValue), "valeur Run retiree");
            }
            finally
            {
                reg.Disable();
                DeleteApproved(TestValue, createdKey);
            }
            T.Eq(null, RunValue(TestValue), "rien ne reste dans Run");
            T.Eq(null, ApprovedValue(TestValue), "rien ne reste dans StartupApproved");
            T.Eq(realBefore, RunValue("Hamster"), "vraie entree Hamster intacte");
            T.True(Same(realApprovedBefore, ApprovedValue("Hamster")), "vraie marque Hamster intacte");
        });

        T.Case("lancer au demarrage : nom de valeur par variable d'environnement", () =>
        {
            string? before = Environment.GetEnvironmentVariable("HAMSTER_RUN_VALUE");
            try
            {
                Environment.SetEnvironmentVariable("HAMSTER_RUN_VALUE", TestValue);
                T.Eq(TestValue, new StartupRegistration(exePath: @"C:\x.exe").ValueName, "nom injecte");
                Environment.SetEnvironmentVariable("HAMSTER_RUN_VALUE", null);
                T.Eq("Hamster", new StartupRegistration(exePath: @"C:\x.exe").ValueName, "nom par defaut");
            }
            finally { Environment.SetEnvironmentVariable("HAMSTER_RUN_VALUE", before); }
        });
    }

    static string? RunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath);
        return key?.GetValue(name) as string;
    }

    static byte[]? ApprovedValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.ApprovedKeyPath);
        return key?.GetValue(name) as byte[];
    }

    /// <summary>Ecrit la marque ; vrai si la cle StartupApproved\Run n'existait pas et a du etre creee.</summary>
    static bool WriteApproved(string name, byte[] value)
    {
        bool existed;
        using (var probe = Registry.CurrentUser.OpenSubKey(StartupRegistration.ApprovedKeyPath)) existed = probe != null;
        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistration.ApprovedKeyPath);
        key.SetValue(name, value, RegistryValueKind.Binary);
        return !existed;
    }

    static void DeleteApproved(string name, bool deleteKeyIfEmpty)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.ApprovedKeyPath, writable: true))
        {
            key?.DeleteValue(name, throwOnMissingValue: false);
            if (key == null || !deleteKeyIfEmpty || key.ValueCount > 0 || key.SubKeyCount > 0) return;
        }
        Registry.CurrentUser.DeleteSubKey(StartupRegistration.ApprovedKeyPath, throwOnMissingSubKey: false);
    }

    static bool Same(byte[]? a, byte[]? b) => a == null ? b == null : b != null && a.AsSpan().SequenceEqual(b);

    static RegistryValueKind? RunKind(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath);
        return key?.GetValueNames().Contains(name) == true ? key.GetValueKind(name) : null;
    }
}
