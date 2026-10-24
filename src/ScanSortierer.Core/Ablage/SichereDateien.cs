namespace ScanSortierer.Core.Ablage;

/// <summary>
/// Dateioperationen nach den Sicherheitsregeln: nie überschreiben, erst temporär schreiben, dann umbenennen.
/// </summary>
public static class SichereDateien
{
    /// <summary>
    /// Schreibt eine neue Datei in <paramref name="zielOrdner"/>. Existiert der Name schon, wird ein Zähler
    /// angehängt (<c>_2</c>, <c>_3</c>, ...). Es wird nie eine bestehende Datei überschrieben.
    /// </summary>
    /// <param name="schreibe">Schreibt den Inhalt in die übergebene temporäre Datei.</param>
    /// <param name="pruefe">Prüft die temporäre Datei vor dem Umbenennen (wirft bei Fehler).</param>
    /// <returns>Der tatsächlich verwendete Pfad.</returns>
    public static string SchreibeNeu(string zielOrdner, string dateiname, Action<string> schreibe, Action<string>? pruefe = null)
    {
        Directory.CreateDirectory(zielOrdner);
        var temp = Path.Combine(zielOrdner, $".scan-sortierer-{Guid.NewGuid():N}.tmp");
        try
        {
            schreibe(temp);
            pruefe?.Invoke(temp);
            return VerschiebeOhneUeberschreiben(temp, zielOrdner, dateiname);
        }
        finally
        {
            // Nur unsere eigene, nicht umbenannte Zwischendatei wird entfernt.
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    /// <summary>Verschiebt eine Datei in einen Ordner, ohne etwas zu überschreiben (Zähler bei Namensgleichheit).</summary>
    public static string VerschiebeEindeutig(string quelle, string zielOrdner)
    {
        Directory.CreateDirectory(zielOrdner);
        return VerschiebeOhneUeberschreiben(quelle, zielOrdner, Path.GetFileName(quelle));
    }

    /// <summary>Verschiebt eine Datei an einen bestimmten Pfad zurück, ohne zu überschreiben.</summary>
    public static string VerschiebeZurueck(string quelle, string zielPfad)
    {
        var ordner = Path.GetDirectoryName(zielPfad)!;
        Directory.CreateDirectory(ordner);
        return VerschiebeOhneUeberschreiben(quelle, ordner, Path.GetFileName(zielPfad));
    }

    /// <summary>
    /// Ersetzt eine Konfigurationsdatei atomar: erst in eine temporäre Datei schreiben, dann umbenennen.
    /// </summary>
    public static void ErsetzeAtomar(string pfad, string inhalt)
    {
        var ordner = Path.GetDirectoryName(Path.GetFullPath(pfad))!;
        Directory.CreateDirectory(ordner);
        var temp = Path.Combine(ordner, $".{Path.GetFileName(pfad)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, inhalt, new System.Text.UTF8Encoding(false));
            File.Move(temp, pfad, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    /// <summary>Liefert einen freien Namen: name.pdf, name_2.pdf, name_3.pdf, ...</summary>
    public static string NameMitZaehler(string dateiname, int nummer)
    {
        if (nummer <= 1)
            return dateiname;
        var stamm = Path.GetFileNameWithoutExtension(dateiname);
        var endung = Path.GetExtension(dateiname);
        return $"{stamm}_{nummer}{endung}";
    }

    private static string VerschiebeOhneUeberschreiben(string quelle, string zielOrdner, string dateiname)
    {
        for (var nummer = 1; nummer < 10_000; nummer++)
        {
            var ziel = Path.Combine(zielOrdner, NameMitZaehler(dateiname, nummer));
            if (File.Exists(ziel) || Directory.Exists(ziel))
                continue;
            try
            {
                File.Move(quelle, ziel, overwrite: false);
                return ziel;
            }
            catch (IOException) when (File.Exists(ziel))
            {
                // Zwischenzeitlich angelegt: nächsten Zähler versuchen.
            }
        }
        throw new IOException($"Kein freier Dateiname für „{dateiname}“ in {zielOrdner} gefunden.");
    }
}
