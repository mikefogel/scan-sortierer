namespace ScanSortierer.Core.Konfiguration;

/// <summary>
/// Pfade im Konfigurationsordner des Nutzers (~/.scan-sortierer bzw. %USERPROFILE%\.scan-sortierer).
/// Das Programmverzeichnis wird nie beschrieben.
/// </summary>
public sealed class AppPfade
{
    public const string OrdnerName = ".scan-sortierer";

    public AppPfade(string basis)
    {
        Basis = basis;
    }

    public string Basis { get; }
    public string Vorlagen => Path.Combine(Basis, "vorlagen");
    public string Einstellungen => Path.Combine(Basis, "einstellungen.json");
    public string Absender => Path.Combine(Basis, "absender.json");
    public string Protokoll => Path.Combine(Basis, "protokoll.log");

    /// <summary>True, wenn der Konfigurationsordner beim Anlegen noch nicht existierte.</summary>
    public bool ErsterStart { get; private set; }

    public static AppPfade Standard()
    {
        var ueberschrieben = Environment.GetEnvironmentVariable("SCAN_SORTIERER_KONFIG");
        if (!string.IsNullOrWhiteSpace(ueberschrieben))
            return new AppPfade(ueberschrieben);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new AppPfade(Path.Combine(home, OrdnerName));
    }

    public void Anlegen()
    {
        ErsterStart = !Directory.Exists(Basis);
        Directory.CreateDirectory(Basis);
        if (!Directory.Exists(Vorlagen))
        {
            Directory.CreateDirectory(Vorlagen);
            global::ScanSortierer.Core.Vorlagen.VorlagenSpeicher.BeispieleKopieren(Vorlagen);
        }
    }

    /// <summary>Dokumente-Ordner des Nutzers (Windows: Eigene Dokumente, Linux: XDG_DOCUMENTS_DIR).</summary>
    public static string DokumenteOrdner()
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_DOCUMENTS_DIR");
        if (string.IsNullOrWhiteSpace(xdg))
        {
            var datei = Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".config"),
                "user-dirs.dirs");
            try
            {
                if (File.Exists(datei))
                {
                    foreach (var zeile in File.ReadAllLines(datei))
                    {
                        var z = zeile.Trim();
                        if (!z.StartsWith("XDG_DOCUMENTS_DIR=", StringComparison.Ordinal))
                            continue;
                        xdg = z["XDG_DOCUMENTS_DIR=".Length..].Trim('"').Replace("$HOME", home);
                        break;
                    }
                }
            }
            catch (IOException)
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(xdg) && Path.GetFullPath(xdg) != Path.GetFullPath(home))
            return xdg;
        foreach (var name in new[] { "Dokumente", "Documents" })
        {
            var p = Path.Combine(home, name);
            if (Directory.Exists(p))
                return p;
        }
        return home;
    }
}
