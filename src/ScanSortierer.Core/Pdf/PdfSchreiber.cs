using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using ScanSortierer.Core.Ablage;
using ScanSortierer.Core.Ocr;

namespace ScanSortierer.Core.Pdf;

/// <summary>Eine Seite des neuen Dokuments.</summary>
/// <param name="QuellIndex">Seitennummer in der Scan-Datei (0-basiert).</param>
/// <param name="Drehung">Nutzerdrehung in Grad im Uhrzeigersinn.</param>
public sealed record SeitenAuftrag(int QuellIndex, int Drehung, OcrErgebnis? Ocr);

public sealed class Metadaten
{
    public string Titel { get; init; } = "";
    public string Autor { get; init; } = "";
    public string Betreff { get; init; } = "";
    public string Stichworte { get; init; } = "";
    public DateOnly? Briefdatum { get; init; }

    /// <summary>Eigene Felder (Name → Wert), z. B. „Betrag“ → „49,90 €“.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Eigene { get; init; } = [];
}

/// <summary>Eine geöffnete Scan-Datei (PDFsharp, aus dem Speicher; das Original bleibt unverändert).</summary>
public sealed class PdfQuelle
{
    private readonly byte[] _daten;

    private PdfQuelle(byte[] daten, int seiten)
    {
        _daten = daten;
        SeitenAnzahl = seiten;
    }

    public int SeitenAnzahl { get; }

    public static PdfQuelle Oeffne(byte[] daten)
    {
        try
        {
            using var ms = new MemoryStream(daten, writable: false);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            return new PdfQuelle(daten, doc.PageCount);
        }
        catch (Exception ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfFehler("Die PDF ist passwortgeschützt.", true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PdfFehler("Die PDF ist beschädigt oder wird nicht unterstützt.", innere: ex);
        }
    }

    internal PdfDocument OeffneImport()
    {
        var ms = new MemoryStream(_daten, writable: false);
        return PdfReader.Open(ms, PdfDocumentOpenMode.Import);
    }
}

public static class PdfSchreiber
{
    private const string OcrSchrift = "/ScanSortiererOcr";

    /// <summary>
    /// Speichert die Seiten als neue PDF mit Metadaten. Erst temporär schreiben, wieder einlesen und die Seitenzahl
    /// prüfen, dann umbenennen. Vorhandene Dateien werden nie überschrieben.
    /// </summary>
    /// <returns>Der tatsächlich verwendete Pfad.</returns>
    public static string Speichere(PdfQuelle quelle, IReadOnlyList<SeitenAuftrag> seiten, Metadaten meta,
        string zielOrdner, string dateiname, bool textebene)
    {
        if (seiten.Count == 0)
            throw new ArgumentException("Es sind keine Seiten gewählt.");
        using var quellDoc = quelle.OeffneImport();
        using var neu = new PdfDocument();
        neu.Options.CompressContentStreams = true;
        neu.Options.NoCompression = false;

        foreach (var s in seiten)
        {
            var quellSeite = quellDoc.Pages[s.QuellIndex];
            var originalDrehung = ((quellSeite.Rotate % 360) + 360) % 360;
            var seite = neu.AddPage(quellSeite);
            var gesamt = (originalDrehung + s.Drehung) % 360;
            seite.Rotate = gesamt;
            if (textebene && s.Ocr != null && s.Ocr.Woerter.Count > 0)
                FuegeTextebeneHinzu(neu, seite, s.Ocr, originalDrehung);
        }

        var info = neu.Info;
        info.Title = meta.Titel;
        info.Author = meta.Autor;
        info.Subject = meta.Betreff;
        info.Keywords = meta.Stichworte;
        info.Creator = "Scan-Sortierer";
        if (meta.Briefdatum is { } bd)
            info.CreationDate = bd.ToDateTime(TimeOnly.MinValue);
        info.ModificationDate = DateTime.Now;
        foreach (var (name, wert) in meta.Eigene)
        {
            if (string.IsNullOrWhiteSpace(wert))
                continue;
            info.Elements["/" + PdfName(name)] = new PdfString(wert, PdfStringEncoding.Unicode);
        }

        return SichereDateien.SchreibeNeu(zielOrdner, dateiname,
            temp =>
            {
                using var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write);
                neu.Save(fs);
                fs.Flush(flushToDisk: true);
            },
            temp =>
            {
                using var pruef = PdfReader.Open(temp, PdfDocumentOpenMode.Import);
                if (pruef.PageCount != seiten.Count)
                    throw new IOException($"Prüfung fehlgeschlagen: {pruef.PageCount} statt {seiten.Count} Seiten gelesen.");
            });
    }

    /// <summary>Liest die Metadaten einer gespeicherten PDF (für Tests und Kontrolle).</summary>
    public static Dictionary<string, string> LeseInfo(string pfad)
    {
        using var doc = PdfReader.Open(pfad, PdfDocumentOpenMode.Import);
        var d = new Dictionary<string, string>();
        foreach (var key in doc.Info.Elements.Keys)
        {
            var item = doc.Info.Elements[key];
            d[key.TrimStart('/')] = item is PdfString s ? s.Value : item?.ToString() ?? "";
        }
        d["Seiten"] = doc.PageCount.ToString(CultureInfo.InvariantCulture);
        return d;
    }

    /// <summary>Wandelt eine Feldbezeichnung in einen gültigen PDF-Namen (ASCII, ohne Leerzeichen).</summary>
    public static string PdfName(string text)
    {
        var sb = new StringBuilder();
        var gross = true;
        foreach (var c in text)
        {
            var teil = c switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'Ä' => "Ae", 'Ö' => "Oe", 'Ü' => "Ue", 'ß' => "ss",
                _ => char.IsAsciiLetterOrDigit(c) ? c.ToString() : null,
            };
            if (teil is null)
            {
                gross = true;
                continue;
            }
            sb.Append(gross ? char.ToUpperInvariant(teil[0]) + teil[1..] : teil);
            gross = false;
        }
        return sb.Length == 0 ? "Feld" : sb.ToString();
    }

    /// <summary>
    /// Legt den erkannten Text unsichtbar (Textmodus 3) über die Seite, damit die PDF durchsuchbar wird.
    /// Die Wortpositionen stammen aus dem gedrehten Bild und werden in den ungedrehten Seitenraum umgerechnet.
    /// </summary>
    private static void FuegeTextebeneHinzu(PdfDocument doc, PdfPage seite, OcrErgebnis ocr, int originalDrehung)
    {
        var box = seite.EffectiveCropBoxReadOnly;
        if (box.IsZero)
            box = seite.MediaBoxReadOnly;
        double x0 = box.X1, y0 = box.Y1, w = box.Width, h = box.Height;
        var t = (originalDrehung + ocr.Drehung) % 360;
        // Bildpunkte je Punkt (das OCR-Bild zeigt die Seite in Drehung t).
        var anzeigeBreite = t % 180 == 0 ? w : h;
        var s = ocr.BildBreite / anzeigeBreite;
        if (s <= 0 || double.IsNaN(s))
            return;

        // Richtung der Schrift (+a) und „oben“ (-b) im ungedrehten Seitenraum.
        var (dx, dy, ux, uy) = t switch
        {
            90 => (0.0, 1.0, -1.0, 0.0),
            180 => (-1.0, 0.0, 0.0, -1.0),
            270 => (0.0, -1.0, 1.0, 0.0),
            _ => (1.0, 0.0, 0.0, 1.0),
        };

        (double X, double Y) ZuSeite(double a, double b)
        {
            var (u, v) = t switch
            {
                90 => (b, h - a),
                180 => (w - a, h - b),
                270 => (w - b, a),
                _ => (a, b),
            };
            return (x0 + u, y0 + h - v);
        }

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("Q\nBT\n3 Tr\n");
        foreach (var wort in ocr.Woerter)
        {
            var bytes = WinAnsi(wort.Text);
            if (bytes.Length == 0)
                continue;
            var breite1 = Breite(wort.Text);
            if (breite1 <= 0)
                continue;
            var a = wort.Links / s;
            var b = wort.Oben / s;
            var ww = wort.Breite / s;
            var hh = Math.Max(1, wort.Hoehe / s);
            var groesse = hh;
            var (x, y) = ZuSeite(a, b + hh * 0.8);
            var k = ww / breite1;
            sb.Append(CultureInfo.InvariantCulture, $"{OcrSchrift} 1 Tf\n");
            sb.Append(string.Format(inv, "{0:0.####} {1:0.####} {2:0.####} {3:0.####} {4:0.##} {5:0.##} Tm\n",
                dx * k, dy * k, ux * groesse, uy * groesse, x, y));
            sb.Append('(');
            foreach (var by in bytes)
            {
                if (by is (byte)'(' or (byte)')' or (byte)'\\')
                    sb.Append('\\').Append((char)by);
                else if (by < 32 || by > 126)
                    sb.Append('\\').Append(Convert.ToString(by, 8).PadLeft(3, '0'));
                else
                    sb.Append((char)by);
            }
            sb.Append(") Tj\n");
        }
        sb.Append("ET\n");

        // Schrift-Ressource (Standardschrift Helvetica, nicht eingebettet; der Text ist ohnehin unsichtbar).
        var schrift = new PdfDictionary(doc);
        schrift.Elements.SetName("/Type", "/Font");
        schrift.Elements.SetName("/Subtype", "/Type1");
        schrift.Elements.SetName("/BaseFont", "/Helvetica");
        schrift.Elements.SetName("/Encoding", "/WinAnsiEncoding");
        doc.Internals.AddObject(schrift);
        var ressourcen = seite.Elements.GetDictionary("/Resources");
        if (ressourcen is null)
        {
            ressourcen = new PdfDictionary(doc);
            seite.Elements["/Resources"] = ressourcen;
        }
        var schriften = ressourcen.Elements.GetDictionary("/Font");
        if (schriften is null)
        {
            schriften = new PdfDictionary(doc);
            ressourcen.Elements["/Font"] = schriften;
        }
        schriften.Elements[OcrSchrift] = schrift.Reference;

        // Vorhandenen Inhalt in q/Q kapseln, damit dessen Grafikzustand die Textebene nicht verschiebt.
        var vorne = seite.Contents.PrependContent();
        vorne.CreateStream("q\n"u8.ToArray());
        var hinten = seite.Contents.AppendContent();
        hinten.CreateStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    private static readonly Dictionary<char, byte> WinAnsiSonder = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['„'] = 0x84, ['…'] = 0x85, ['‘'] = 0x91, ['’'] = 0x92,
        ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97,
    };

    private static byte[] WinAnsi(string text)
    {
        var bytes = new List<byte>(text.Length);
        foreach (var c in text)
        {
            if (c is >= ' ' and <= '~' or >= ' ' and <= 'ÿ')
                bytes.Add((byte)c);
            else if (WinAnsiSonder.TryGetValue(c, out var b))
                bytes.Add(b);
            else if (!char.IsControl(c))
                bytes.Add((byte)'?');
        }
        return bytes.ToArray();
    }

    // Zeichenbreiten von Helvetica (1/1000 em) für ASCII 32–126.
    private static readonly short[] HelveticaBreiten =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static double Breite(string text)
    {
        double summe = 0;
        foreach (var c in text)
        {
            summe += c switch
            {
                >= ' ' and <= '~' => HelveticaBreiten[c - ' '],
                'Ä' => 667, 'Ö' => 778, 'Ü' => 722, 'ß' => 611,
                _ => 556,
            };
        }
        return summe / 1000.0;
    }
}
