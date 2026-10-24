using System.Text;
using ScanSortierer.Core.Erkennung;
using ScanSortierer.Core.Ocr;
using ScanSortierer.Core.Pdf;

namespace ScanSortierer;

/// <summary>
/// Prüft ohne Oberfläche, ob PDF-Anzeige und Texterkennung im installierten Paket funktionieren:
/// <c>ScanSortierer --selbsttest [datei.pdf] [--erwarte Wort] [--bericht bericht.txt]</c>
/// </summary>
internal static class Selbsttest
{
    public static int Ausfuehren(string[] args)
    {
        var bericht = new StringBuilder();
        string? pdf = null, erwartet = null, berichtDatei = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--erwarte" when i + 1 < args.Length: erwartet = args[++i]; break;
                case "--bericht" when i + 1 < args.Length: berichtDatei = args[++i]; break;
                default: pdf = args[i]; break;
            }
        }

        var ok = true;
        try
        {
            bericht.AppendLine($"Scan-Sortierer {Dienste.Version} – Selbsttest");
            bericht.AppendLine($"Programmverzeichnis: {AppContext.BaseDirectory}");
            var ocr = TesseractOcr.Finde();
            bericht.AppendLine($"Texterkennung: {ocr.Status}");
            ok &= ocr.Verfuegbar;
            if (pdf != null)
            {
                var daten = File.ReadAllBytes(pdf);
                PdfQuelle.Oeffne(daten);
                using var doc = PdfiumDokument.Oeffne(daten);
                bericht.AppendLine($"PDF: {pdf}, {doc.SeitenAnzahl} Seiten");
                var bild = doc.Rendere(0, 0, 300);
                bericht.AppendLine($"Seite 1 gerendert: {bild.Breite}×{bild.Hoehe}, leer: {Leerseiten.IstLeer(bild)}");
                if (ocr.Verfuegbar)
                {
                    var text = ocr.ErkenneAsync(bild, 0, CancellationToken.None).GetAwaiter().GetResult().Text;
                    bericht.AppendLine("Erkannter Text (Anfang): " + text.Replace('\n', ' ')[..Math.Min(200, text.Length)]);
                    if (erwartet != null && !text.Contains(erwartet, StringComparison.OrdinalIgnoreCase))
                    {
                        bericht.AppendLine($"FEHLER: „{erwartet}“ wurde nicht erkannt.");
                        ok = false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            bericht.AppendLine("FEHLER: " + ex);
            ok = false;
        }
        bericht.AppendLine(ok ? "ERGEBNIS: OK" : "ERGEBNIS: FEHLER");
        Console.Out.Write(bericht.ToString());
        Console.Out.Flush();
        if (berichtDatei != null)
            File.WriteAllText(berichtDatei, bericht.ToString());
        return ok ? 0 : 1;
    }
}
