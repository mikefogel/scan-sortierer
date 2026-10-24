using System.Text.RegularExpressions;
using ScanSortierer.Core.Konfiguration;
using ScanSortierer.Core.Ocr;
using ScanSortierer.Core.Vorlagen;

namespace ScanSortierer.Core.Erkennung;

/// <summary>Angaben zu einer Seite für die Grenzerkennung.</summary>
/// <param name="ScanIndex">Position im Original-Scan (0-basiert). Beim beidseitigen Scan sind ungerade Positionen Rückseiten.</param>
public sealed record SeitenMerkmale(int ScanIndex, bool Leer, OcrErgebnis? Ocr);

/// <summary>
/// Schlägt Dokumentgrenzen vor: Eine Seite beginnt ein neues Dokument, wenn sie wie die erste Seite eines Briefs
/// aussieht (Anrede, „Seite 1 von“, Datum und Anschrift im Kopf, bekannter Absender oben).
/// </summary>
public static partial class DokumentGrenzen
{
    public const int Schwelle = 3;

    /// <summary>Liefert für jede Seite, ob sie (vermutlich) ein neues Dokument beginnt.</summary>
    public static bool[] Vorschlag(IReadOnlyList<SeitenMerkmale> seiten, AbsenderVerzeichnis? absender = null)
    {
        var ergebnis = new bool[seiten.Count];
        var erste = true;
        SeitenMerkmale? vorherige = null;
        for (var i = 0; i < seiten.Count; i++)
        {
            var s = seiten[i];
            if (s.Leer)
                continue;
            if (erste)
            {
                ergebnis[i] = true;
                erste = false;
            }
            else
            {
                ergebnis[i] = Punkte(s, vorherige, absender) >= Schwelle;
            }
            vorherige = s;
        }
        return ergebnis;
    }

    public static int Punkte(SeitenMerkmale s, SeitenMerkmale? vorherige, AbsenderVerzeichnis? absender)
    {
        if (s.Ocr is null)
            return 0;
        var punkte = 0;
        var text = s.Ocr.Text;
        if (SeiteEins().IsMatch(text))
            punkte += 3;
        if (FolgeSeite().IsMatch(text))
            punkte -= 4;
        if (Anrede().IsMatch(text))
            punkte += 2;

        var kopf = s.Ocr.Zeilen.Where(z => z.ObenRelativ < 0.45).Select(z => z.Text).ToList();
        if (kopf.Any(z => Werte.FindeDaten(z).Any()))
            punkte += 1;
        if (s.Ocr.Zeilen.Where(z => z.ObenRelativ < 0.35).Any(z => Postleitzahl().IsMatch(z.Text)))
            punkte += 1;
        if (absender != null)
        {
            var oben = string.Join("\n", s.Ocr.Zeilen.Where(z => z.ObenRelativ < 0.25).Select(z => AbsenderVerzeichnis.Vereinfache(z.Text)));
            if (absender.Suchbegriffe().Any(b => b.Suchbegriff.Trim().Length >= 3 && oben.Contains(AbsenderVerzeichnis.Vereinfache(b.Suchbegriff), StringComparison.Ordinal)))
                punkte += 1;
        }

        // Die vorherige Seite kündigt eine Fortsetzung an („Seite 1 von 2“).
        if (vorherige?.Ocr != null)
        {
            var m = SeiteXvonY().Match(vorherige.Ocr.Text);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var x) && int.TryParse(m.Groups[2].Value, out var y) && x < y)
                punkte -= 4;
        }

        // Beim beidseitigen Scan beginnt ein neuer Brief fast immer auf einer Vorderseite.
        if (s.ScanIndex % 2 == 1)
            punkte -= 1;
        return punkte;
    }

    [GeneratedRegex(@"\b(Seite|Blatt)\s*1\s*(von|/)\s*\d+", RegexOptions.IgnoreCase)]
    private static partial Regex SeiteEins();

    [GeneratedRegex(@"\b(Seite|Blatt)\s*([2-9]|\d{2,})\s*(von|/)\s*\d+", RegexOptions.IgnoreCase)]
    private static partial Regex FolgeSeite();

    [GeneratedRegex(@"\b(?:Seite|Blatt)\s*(\d+)\s*(?:von|/)\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SeiteXvonY();

    [GeneratedRegex(@"^\s*(Sehr geehrte|Guten Tag|Hallo|Liebe[rs]?\s)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex Anrede();

    [GeneratedRegex(@"\b\d{5}\s+\p{Lu}\p{Ll}+")]
    private static partial Regex Postleitzahl();
}
