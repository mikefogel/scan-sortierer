using System.Text;
using System.Text.RegularExpressions;
using ScanSortierer.Core.Vorlagen;

namespace ScanSortierer.Core.Ablage;

/// <summary>Erzeugt Dateinamen nach dem Schema der Vorlage.</summary>
public static partial class Dateinamen
{
    private static readonly HashSet<char> Ungueltig = [.. "<>:\"/\\|?*", .. Path.GetInvalidFileNameChars()];

    private static readonly HashSet<string> ReservierteNamen = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Baut den Dateinamen (mit „.pdf“). Fehlende optionale Felder werden samt Trennzeichen weggelassen,
    /// Datumsfelder als JJJJ-MM-TT geschrieben, Beträge nie verwendet, ungültige Zeichen entfernt.
    /// </summary>
    public static string Erzeuge(Vorlage vorlage, IReadOnlyDictionary<string, string> werte)
    {
        var schema = string.IsNullOrWhiteSpace(vorlage.Dateiname) ? "{datum}_{absender}_{typ}" : vorlage.Dateiname;
        var sb = new StringBuilder();
        var position = 0;
        string? praefix = null;
        var erstesTeil = true;
        foreach (Match m in VorlagenPruefung.Platzhalter().Matches(schema))
        {
            var literal = schema[position..m.Index];
            position = m.Index + m.Length;
            var wert = WertFuer(vorlage, m.Groups[1].Value, werte);
            if (wert.Length == 0)
            {
                // Feld fehlt: Trenner davor entfällt. Nur ein Text am Anfang des Schemas bleibt erhalten.
                if (erstesTeil)
                    praefix ??= literal;
                continue;
            }
            sb.Append(erstesTeil && praefix != null ? praefix : literal);
            sb.Append(wert);
            erstesTeil = false;
        }
        sb.Append(schema[position..]);

        var name = Bereinige(sb.ToString());
        name = MehrfachTrenner().Replace(name, m => m.Value[..1]);
        name = name.Trim(' ', '.', '_', '-');
        if (name.Length == 0)
            name = "Dokument";
        if (ReservierteNamen.Contains(name))
            name = "_" + name;
        if (name.Length > 180)
            name = name[..180].TrimEnd(' ', '.');
        return name + ".pdf";
    }

    /// <summary>Entfernt Zeichen, die in Dateinamen unter Windows oder Linux ungültig sind.</summary>
    public static string Bereinige(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (Ungueltig.Contains(c) || char.IsControl(c))
                continue;
            sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }
        return Leerraum().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Stellt sicher, dass ein vom Nutzer eingegebener Name gültig ist und auf .pdf endet.</summary>
    public static string NormalisiereEingabe(string eingabe)
    {
        var name = eingabe.Trim();
        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        name = Bereinige(name).Trim(' ', '.');
        if (name.Length == 0)
            return "";
        if (ReservierteNamen.Contains(name))
            name = "_" + name;
        return name + ".pdf";
    }

    private static string WertFuer(Vorlage vorlage, string platzhalter, IReadOnlyDictionary<string, string> werte)
    {
        if (platzhalter.Equals(SonderFelder.Typ, StringComparison.OrdinalIgnoreCase))
            return Bereinige(vorlage.Name);
        var feld = vorlage.FindeFeld(platzhalter);
        if (feld is null || !feld.ImDateinamen || feld.Typ == FeldTyp.Betrag)
            return "";
        if (!werte.TryGetValue(feld.Name, out var wert) || string.IsNullOrWhiteSpace(wert))
            return "";
        if (feld.Typ == FeldTyp.Datum)
            return Werte.LeseDatum(wert, out var d) ? Werte.DatumFuerDateiname(d) : "";
        return Bereinige(wert);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Leerraum();

    [GeneratedRegex(@"([_\-])\1+|[_\-]\s+[_\-]")]
    private static partial Regex MehrfachTrenner();
}
