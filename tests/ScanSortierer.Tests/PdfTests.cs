using ScanSortierer.Core.Erkennung;
using ScanSortierer.Core.Konfiguration;
using ScanSortierer.Core.Ocr;
using ScanSortierer.Core.Pdf;
using ScanSortierer.Core.Vorlagen;

namespace ScanSortierer.Tests;

public static class Beispiele
{
    public static string Pfad(string name) => Path.Combine(AppContext.BaseDirectory, "Beispielscans", name);

    private static readonly Lazy<TesseractOcr> Ocr = new(() => TesseractOcr.Finde());

    public static TesseractOcr Tesseract()
    {
        var t = Ocr.Value;
        if (!t.Verfuegbar)
            Assert.Skip("Tesseract mit deutschen Sprachdaten ist nicht verfügbar: " + t.Status);
        return t;
    }
}

public class PdfTests
{
    [Fact]
    public void RendernUndLeerseitenErkennen()
    {
        using var doc = PdfiumDokument.Oeffne(File.ReadAllBytes(Beispiele.Pfad("Scan 001.pdf")));
        Assert.Equal(4, doc.SeitenAnzahl);
        var leer = Enumerable.Range(0, 4).Select(i => Leerseiten.IstLeer(doc.Rendere(i, 0, 40))).ToArray();
        Assert.Equal([false, true, false, true], leer);
    }

    [Fact]
    public void DrehungTauschtBreiteUndHoehe()
    {
        using var doc = PdfiumDokument.Oeffne(File.ReadAllBytes(Beispiele.Pfad("Scan 001.pdf")));
        var normal = doc.Rendere(0, 0, 36);
        var gedreht = doc.Rendere(0, 90, 36);
        Assert.Equal(normal.Breite, gedreht.Hoehe);
        Assert.Equal(normal.Hoehe, gedreht.Breite);
    }

    [Fact]
    public void DefekteDateiWirdErkannt()
    {
        var daten = File.ReadAllBytes(Beispiele.Pfad("Scan 003.pdf"));
        Assert.Throws<PdfFehler>(() => PdfiumDokument.Oeffne(daten));
        Assert.Throws<PdfFehler>(() => PdfQuelle.Oeffne(daten));
    }

    [Fact]
    public void SpeichernMitDrehungUndMetadaten()
    {
        var daten = File.ReadAllBytes(Beispiele.Pfad("Scan 002.pdf"));
        var quelle = PdfQuelle.Oeffne(daten);
        var ziel = Directory.CreateTempSubdirectory("ss-ziel-").FullName;
        var meta = new Metadaten
        {
            Titel = "2026-09-21_Stadtwerke_Rechnung",
            Autor = "Stadtwerke",
            Betreff = "Rechnung",
            Stichworte = "Strom Jahresabrechnung",
            Briefdatum = new DateOnly(2026, 9, 21),
            Eigene = [new("Betrag", "1.234,56 €"), new("Dokumenttyp", "Rechnung")],
        };
        var pfad = PdfSchreiber.Speichere(quelle, [new SeitenAuftrag(2, 180, null)], meta, ziel, "test.pdf", textebene: true);
        var info = PdfSchreiber.LeseInfo(pfad);
        Assert.Equal("1", info["Seiten"]);
        Assert.Equal("1.234,56 €", info["Betrag"]);
        Assert.Equal("Stadtwerke", info["Author"]);

        // Die gespeicherte Seite steht jetzt aufrecht: oben ist Text (Briefkopf), unten leer.
        using var neu = PdfiumDokument.Oeffne(File.ReadAllBytes(pfad));
        var bild = neu.Rendere(0, 0, 40);
        Assert.True(DunkelAnteilBereich(bild, 0.03, 0.12) > DunkelAnteilBereich(bild, 0.85, 0.95));
    }

    private static double DunkelAnteilBereich(RasterBild bild, double von, double bis)
    {
        var grau = bild.Graustufen();
        int y0 = (int)(bild.Hoehe * von), y1 = (int)(bild.Hoehe * bis), dunkel = 0, gesamt = 0;
        for (var y = y0; y < y1; y++)
            for (var x = 0; x < bild.Breite; x++, gesamt++)
                if (grau[y * bild.Breite + x] < 150)
                    dunkel++;
        return (double)dunkel / gesamt;
    }
}

public class OcrTests
{
    private static Vorlage Rechnung()
    {
        var ordner = Directory.CreateTempSubdirectory("ss-vorlagen-").FullName;
        VorlagenSpeicher.BeispieleKopieren(ordner);
        return new VorlagenSpeicher(ordner).LadeAlle().Vorlagen.First(v => v.Name == "Rechnung");
    }

    [Fact]
    public async Task ErkennungUndDurchsuchbarePdf()
    {
        var ocr = Beispiele.Tesseract();
        var daten = File.ReadAllBytes(Beispiele.Pfad("Scan 001.pdf"));
        using var doc = PdfiumDokument.Oeffne(daten);
        var seite1 = await ocr.ErkenneAsync(doc.Rendere(0, 0, 300), 0, TestContext.Current.CancellationToken);
        var seite3 = await ocr.ErkenneAsync(doc.Rendere(2, 0, 300), 0, TestContext.Current.CancellationToken);
        Assert.Contains("Telekom", seite1.Text);

        var absender = new AbsenderVerzeichnis(null, [new AbsenderEintrag { Name = "Telekom", Schreibweisen = ["Telekom Deutschland GmbH"] }]);
        var werte = FeldErkennung.Erkenne(Rechnung(), [seite1, seite3], absender, new DateOnly(2026, 10, 24));
        Assert.Equal("Telekom", werte["absender"].Wert);
        Assert.Equal("14.10.2026", werte["datum"].Wert);
        Assert.Equal("49,90 €", werte["betrag"].Wert);
        Assert.Equal("4711-0815-26", werte["rechnungsnummer"].Wert);

        var grenzen = DokumentGrenzen.Vorschlag([
            new SeitenMerkmale(0, false, seite1), new SeitenMerkmale(1, true, null),
            new SeitenMerkmale(2, false, seite3), new SeitenMerkmale(3, true, null)], absender);
        Assert.Equal([true, false, false, false], grenzen);

        var ziel = Directory.CreateTempSubdirectory("ss-ziel-").FullName;
        var pfad = PdfSchreiber.Speichere(PdfQuelle.Oeffne(daten),
            [new SeitenAuftrag(0, 0, seite1), new SeitenAuftrag(2, 0, seite3)],
            new Metadaten { Titel = "t" }, ziel, "telekom.pdf", textebene: true);
        Assert.Equal("2", PdfSchreiber.LeseInfo(pfad)["Seiten"]);
        var text = PdfText(pfad);
        if (text != null)
            Assert.Contains("Rechnungsbetrag", text);
    }

    [Fact]
    public async Task ZweiBriefeInEinerDateiUndVerdrehteSeite()
    {
        var ocr = Beispiele.Tesseract();
        using var doc = PdfiumDokument.Oeffne(File.ReadAllBytes(Beispiele.Pfad("Scan 002.pdf")));
        var aok = await ocr.ErkenneAsync(doc.Rendere(0, 0, 300), 0, TestContext.Current.CancellationToken);
        var verdreht = await ocr.ErkenneAsync(doc.Rendere(2, 0, 300), 0, TestContext.Current.CancellationToken);
        var aufrecht = await ocr.ErkenneAsync(doc.Rendere(2, 180, 300), 180, TestContext.Current.CancellationToken);
        Assert.True(aufrecht.GuteWoerter > verdreht.GuteWoerter * 3 + 5, $"{aufrecht.GuteWoerter} vs {verdreht.GuteWoerter}");

        var grenzen = DokumentGrenzen.Vorschlag([
            new SeitenMerkmale(0, false, aok), new SeitenMerkmale(1, true, null),
            new SeitenMerkmale(2, false, aufrecht), new SeitenMerkmale(3, true, null)]);
        Assert.Equal([true, false, true, false], grenzen);

        var werte = FeldErkennung.Erkenne(Rechnung(), [aufrecht], new AbsenderVerzeichnis(), new DateOnly(2026, 10, 24));
        Assert.Equal("21.09.2026", werte["datum"].Wert);
        Assert.Equal("1.234,56 €", werte["betrag"].Wert);
        Assert.Equal("SW-2026-99812", werte["rechnungsnummer"].Wert);
        Assert.Equal("Stadtwerke Musterstadt GmbH", werte["absender"].Wert);

        // Textebene einer gedrehten Seite muss lesbar (nicht kopfstehend) extrahierbar sein.
        var ziel = Directory.CreateTempSubdirectory("ss-ziel-").FullName;
        var pfad = PdfSchreiber.Speichere(PdfQuelle.Oeffne(File.ReadAllBytes(Beispiele.Pfad("Scan 002.pdf"))),
            [new SeitenAuftrag(2, 180, aufrecht)], new Metadaten(), ziel, "sw.pdf", textebene: true);
        var text = PdfText(pfad);
        if (text != null)
            Assert.Contains("Jahresabrechnung", text);
    }

    /// <summary>Text mit pdftotext (Poppler) extrahieren, falls installiert.</summary>
    private static string? PdfText(string pfad)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("pdftotext", [pfad, "-"]) { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? text : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
