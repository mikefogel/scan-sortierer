using System.Text.RegularExpressions;
using ScanSortierer.Core.Konfiguration;
using ScanSortierer.Core.Ocr;
using ScanSortierer.Core.Vorlagen;

namespace ScanSortierer.Core.Erkennung;

/// <summary>Ein erkannter Feldwert; <see cref="Rohtext"/> ist die Fundstelle (z. B. abweichende Absender-Schreibweise).</summary>
public sealed record ErkannterWert(string Wert, string? Rohtext = null);

/// <summary>Belegt Felder einer Vorlage aus dem OCR-Text vor (Schlüsselwörter, Muster, typische Briefangaben).</summary>
public static partial class FeldErkennung
{
    public static Dictionary<string, ErkannterWert> Erkenne(Vorlage vorlage, IReadOnlyList<OcrErgebnis> seiten, AbsenderVerzeichnis absender, DateOnly? heute = null)
    {
        var ergebnis = new Dictionary<string, ErkannterWert>(StringComparer.OrdinalIgnoreCase);
        if (seiten.Count == 0)
            return ergebnis;
        var zeilen = seiten.SelectMany((s, i) => s.Zeilen.Select(z => (Seite: i, Zeile: z))).ToList();
        var stichtag = heute ?? DateOnly.FromDateTime(DateTime.Today);

        foreach (var feld in vorlage.Felder)
        {
            ErkannterWert? wert = null;
            try
            {
                wert = feld.Typ switch
                {
                    FeldTyp.Datum => ErkenneDatum(feld, zeilen, stichtag),
                    FeldTyp.Betrag => ErkenneBetrag(feld, zeilen),
                    _ when feld.IstAbsender => ErkenneAbsender(feld, seiten[0], zeilen, absender),
                    FeldTyp.Auswahl => ErkenneAuswahl(feld, zeilen),
                    _ => ErkenneText(feld, zeilen),
                };
            }
            catch (RegexMatchTimeoutException)
            {
            }
            if (wert != null && !string.IsNullOrWhiteSpace(wert.Wert))
                ergebnis[feld.Name] = wert;
        }
        return ergebnis;
    }

    private static ErkannterWert? ErkenneDatum(Feld feld, List<(int Seite, OcrZeile Zeile)> zeilen, DateOnly heute)
    {
        bool Plausibel(DateOnly d) => d.Year >= 1950 && d <= heute.AddDays(60);

        // 1. Muster und Schlüsselwörter der Vorlage.
        foreach (var treffer in TrefferNachSchluessel(feld, zeilen))
        {
            var d = Werte.FindeDaten(treffer).Select(x => x.Datum).Where(Plausibel).FirstOrDefault();
            if (d != default)
                return new ErkannterWert(Werte.FormatiereDatum(d));
        }
        if (string.Equals(feld.Name, SonderFelder.Datum, StringComparison.OrdinalIgnoreCase))
        {
            var ersteSeite = zeilen.Where(z => z.Seite == 0).ToList();
            // 2. Typische Briefzeile „Ort, (den) 14.10.2026“.
            foreach (var (_, z) in ersteSeite)
            {
                var m = OrtUndDatum().Match(z.Text);
                if (!m.Success)
                    continue;
                var d = Werte.FindeDaten(z.Text[m.Index..]).Select(x => x.Datum).Where(Plausibel).FirstOrDefault();
                if (d != default)
                    return new ErkannterWert(Werte.FormatiereDatum(d));
            }
            // 3. Erstes Datum im oberen Teil der ersten Seite (Briefkopf).
            foreach (var (_, z) in ersteSeite.Where(z => z.Zeile.ObenRelativ < 0.45))
            {
                var d = Werte.FindeDaten(z.Text).Select(x => x.Datum).Where(Plausibel).FirstOrDefault();
                if (d != default)
                    return new ErkannterWert(Werte.FormatiereDatum(d));
            }
            // 4. Sonst das späteste plausible Datum der ersten Seite.
            var alle = ersteSeite.SelectMany(z => Werte.FindeDaten(z.Zeile.Text)).Select(x => x.Datum).Where(Plausibel).ToList();
            if (alle.Count > 0)
                return new ErkannterWert(Werte.FormatiereDatum(alle.Max()));
        }
        return null;
    }

    private static ErkannterWert? ErkenneBetrag(Feld feld, List<(int Seite, OcrZeile Zeile)> zeilen)
    {
        foreach (var treffer in TrefferNachSchluessel(feld, zeilen))
        {
            var b = Werte.FindeBetraege(treffer).FirstOrDefault();
            if (b != default)
                return new ErkannterWert(Werte.FormatiereBetrag(b.Betrag));
        }
        // Ohne Schlüsselwort: größter Betrag mit Währungsangabe.
        var kandidaten = zeilen.SelectMany(z => Werte.FindeBetraege(z.Zeile.Text)).Where(b => b.MitWaehrung).ToList();
        if (kandidaten.Count > 0)
            return new ErkannterWert(Werte.FormatiereBetrag(kandidaten.Max(b => b.Betrag)));
        return null;
    }

    private static ErkannterWert? ErkenneText(Feld feld, List<(int Seite, OcrZeile Zeile)> zeilen)
    {
        foreach (var treffer in TrefferNachSchluessel(feld, zeilen))
        {
            // Bis zur nächsten Spaltenlücke (doppeltes Leerzeichen).
            var wert = treffer.Split("  ", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            wert = wert.Trim(' ', ':', '-', '#', '.', ',');
            if (wert.Length > 80)
                wert = wert[..80].Trim();
            if (wert.Length > 0)
                return new ErkannterWert(wert);
        }
        return null;
    }

    private static ErkannterWert? ErkenneAuswahl(Feld feld, List<(int Seite, OcrZeile Zeile)> zeilen)
    {
        var text = string.Join("\n", zeilen.Select(z => z.Zeile.Text));
        var besterIndex = int.MaxValue;
        string? bester = null;
        foreach (var w in feld.Werte.Where(w => !string.IsNullOrWhiteSpace(w)))
        {
            var i = text.IndexOf(w.Trim(), StringComparison.CurrentCultureIgnoreCase);
            if (i >= 0 && i < besterIndex)
            {
                besterIndex = i;
                bester = w;
            }
        }
        if (bester != null)
            return new ErkannterWert(bester);
        var t = TrefferNachSchluessel(feld, zeilen).FirstOrDefault();
        return t is null ? null : new ErkannterWert(t.Split("  ")[0].Trim(' ', ':', '-'));
    }

    private static ErkannterWert? ErkenneAbsender(Feld feld, OcrErgebnis ersteSeite, List<(int Seite, OcrZeile Zeile)> zeilen, AbsenderVerzeichnis absender)
    {
        // 1. Bekannte Absender und Schreibweisen, bevorzugt weit oben auf der ersten Seite (Briefkopf).
        var kandidaten = absender.Suchbegriffe()
            .Concat(feld.Werte.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => (Suchbegriff: w, Name: absender.Normalisiere(w))))
            .Where(k => k.Suchbegriff.Trim().Length >= 2)
            .OrderByDescending(k => k.Suchbegriff.Length)
            .ToList();
        (double Oben, string Name)? bester = null;
        foreach (var z in ersteSeite.Zeilen)
        {
            var vereinfacht = AbsenderVerzeichnis.Vereinfache(z.Text);
            foreach (var (such, name) in kandidaten)
            {
                var s = AbsenderVerzeichnis.Vereinfache(such);
                if (!EnthaeltWort(vereinfacht, s))
                    continue;
                // Empfängeradresse und Fließtext liegen tiefer; Treffer oben sind wahrscheinlicher der Absender.
                if (bester is null || z.ObenRelativ < bester.Value.Oben)
                    bester = (z.ObenRelativ, name);
                break;
            }
        }
        if (bester != null)
            return new ErkannterWert(bester.Value.Name);

        // 2. Schlüsselwörter der Vorlage.
        foreach (var t in TrefferNachSchluessel(feld, zeilen))
        {
            var w = t.Split("  ")[0].Trim(' ', ':', '-', ',');
            if (w.Length > 1)
                return new ErkannterWert(absender.Normalisiere(w), w);
        }

        // 3. Briefkopf: erste Zeile oben, die nach Firma oder Behörde aussieht.
        foreach (var z in ersteSeite.Zeilen.Where(z => z.ObenRelativ < 0.2))
        {
            var teil = z.Text.Split("  ")[0].Trim();
            if (Firmenzeichen().IsMatch(teil))
            {
                var bereinigt = teil.Split(" · ")[0].Trim(' ', '-', ',', '|');
                return new ErkannterWert(absender.Normalisiere(bereinigt), bereinigt);
            }
        }
        return null;
    }

    /// <summary>Text hinter den Schlüsselwörtern (gleiche Zeile, sonst nächste Zeile) und Treffer der Muster.</summary>
    private static IEnumerable<string> TrefferNachSchluessel(Feld feld, List<(int Seite, OcrZeile Zeile)> zeilen)
    {
        foreach (var muster in feld.Muster.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            Regex re;
            try
            {
                re = new Regex(muster, RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                continue;
            }
            foreach (var (_, z) in zeilen)
            {
                var m = re.Match(z.Text);
                if (m.Success)
                    yield return m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value;
            }
        }
        foreach (var schluessel in feld.Schluesselwoerter.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var re = new Regex(@"(?<!\p{L})" + Regex.Escape(schluessel.Trim()), RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            for (var i = 0; i < zeilen.Count; i++)
            {
                var text = zeilen[i].Zeile.Text;
                var m = re.Match(text);
                if (!m.Success)
                    continue;
                var rest = text[(m.Index + m.Length)..];
                // Wortrest des Schlüsselworts überspringen (z. B. „Rechnungsnummer“ bei Schlüssel „Rechnungsnr“).
                rest = WortRest().Replace(rest, "", 1);
                rest = rest.TrimStart(' ', ':', '.', '#', '-');
                if (rest.Trim().Length > 0)
                    yield return rest.Trim();
                else if (i + 1 < zeilen.Count && zeilen[i + 1].Seite == zeilen[i].Seite)
                    yield return zeilen[i + 1].Zeile.Text.Trim();
            }
        }
    }

    private static bool EnthaeltWort(string text, string wort)
    {
        var i = text.IndexOf(wort, StringComparison.Ordinal);
        while (i >= 0)
        {
            var linksOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            var rechtsOk = i + wort.Length >= text.Length || !char.IsLetterOrDigit(text[i + wort.Length]);
            if (linksOk && rechtsOk)
                return true;
            i = text.IndexOf(wort, i + 1, StringComparison.Ordinal);
        }
        return false;
    }

    [GeneratedRegex(@"^[\p{L}][\p{L}\s\.\-/]{1,40},\s*(den\s+)?\d{1,2}\.", RegexOptions.IgnoreCase)]
    private static partial Regex OrtUndDatum();

    [GeneratedRegex(@"\b(GmbH|AG|KG|mbH|e\.\s?V\.|SE|Bank|Sparkasse|Versicherung|Stadtwerke|Amt|Finanzamt|Krankenkasse|AOK|Gesundheitskasse|Rentenversicherung|Agentur|Verwaltung|Gemeinde|Stadt|Landratsamt|Kasse)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Firmenzeichen();

    [GeneratedRegex(@"^\p{L}*\.?")]
    private static partial Regex WortRest();
}
