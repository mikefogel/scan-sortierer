using System.Globalization;
using System.Text;

namespace ScanSortierer.Core.Ocr;

public sealed record OcrWort(string Text, int Links, int Oben, int Breite, int Hoehe, double Konfidenz, int Block, int Absatz, int Zeile);

public sealed record OcrZeile(string Text, double ObenRelativ, double LinksRelativ);

/// <summary>Ergebnis der Texterkennung für eine Seite.</summary>
public sealed class OcrErgebnis
{
    public OcrErgebnis(IReadOnlyList<OcrWort> woerter, int bildBreite, int bildHoehe, double dpi, int drehung)
    {
        Woerter = woerter;
        BildBreite = bildBreite;
        BildHoehe = bildHoehe;
        Dpi = dpi;
        Drehung = drehung;
        Zeilen = BaueZeilen();
        Text = string.Join("\n", Zeilen.Select(z => z.Text));
    }

    public IReadOnlyList<OcrWort> Woerter { get; }
    public int BildBreite { get; }
    public int BildHoehe { get; }
    public double Dpi { get; }

    /// <summary>Nutzerdrehung (Grad, im Uhrzeigersinn), mit der die Seite für die Erkennung gerendert wurde.</summary>
    public int Drehung { get; }

    public IReadOnlyList<OcrZeile> Zeilen { get; }
    public string Text { get; }

    /// <summary>Anzahl sicher erkannter Wörter (Maß für die Lesbarkeit, z. B. um verdrehte Seiten zu erkennen).</summary>
    public int GuteWoerter => Woerter.Count(w => w.Konfidenz >= 70 && w.Text.Count(char.IsLetterOrDigit) >= 2);

    private List<OcrZeile> BaueZeilen()
    {
        var zeilen = new List<OcrZeile>();
        foreach (var gruppe in Woerter.GroupBy(w => (w.Block, w.Absatz, w.Zeile)))
        {
            var ws = gruppe.OrderBy(w => w.Links).ToList();
            var sb = new StringBuilder();
            var mittlereZeichenbreite = ws.Sum(w => w.Breite) / (double)Math.Max(1, ws.Sum(w => w.Text.Length));
            for (var i = 0; i < ws.Count; i++)
            {
                if (i > 0)
                {
                    var luecke = ws[i].Links - (ws[i - 1].Links + ws[i - 1].Breite);
                    // Große Lücken (Tabellenspalten) als doppeltes Leerzeichen kennzeichnen.
                    sb.Append(luecke > mittlereZeichenbreite * 3 ? "  " : " ");
                }
                sb.Append(ws[i].Text);
            }
            var oben = ws.Min(w => w.Oben);
            var links = ws.Min(w => w.Links);
            zeilen.Add(new OcrZeile(sb.ToString(), BildHoehe == 0 ? 0 : (double)oben / BildHoehe, BildBreite == 0 ? 0 : (double)links / BildBreite));
        }
        // Lesereihenfolge: von oben nach unten, bei gleicher Höhe von links nach rechts.
        return zeilen.OrderBy(z => Math.Round(z.ObenRelativ * 200)).ThenBy(z => z.LinksRelativ).ToList();
    }

    /// <summary>Liest die TSV-Ausgabe von Tesseract.</summary>
    public static OcrErgebnis AusTsv(string tsv, int bildBreite, int bildHoehe, double dpi, int drehung)
    {
        var woerter = new List<OcrWort>();
        foreach (var zeile in tsv.Split('\n'))
        {
            var t = zeile.TrimEnd('\r').Split('\t');
            if (t.Length < 12 || t[0] != "5")
                continue;
            var text = t[11].Trim();
            if (text.Length == 0)
                continue;
            if (!int.TryParse(t[2], out var block) || !int.TryParse(t[3], out var absatz) || !int.TryParse(t[4], out var z)
                || !int.TryParse(t[6], out var links) || !int.TryParse(t[7], out var oben)
                || !int.TryParse(t[8], out var breite) || !int.TryParse(t[9], out var hoehe))
                continue;
            double.TryParse(t[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var konf);
            woerter.Add(new OcrWort(text, links, oben, breite, hoehe, konf, block, absatz, z));
        }
        return new OcrErgebnis(woerter, bildBreite, bildHoehe, dpi, drehung);
    }
}
