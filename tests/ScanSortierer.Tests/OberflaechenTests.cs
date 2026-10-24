#pragma warning disable CS0618 // Bitmap.Save(string): einfache Bildschirmfotos genügen
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ScanSortierer.Core.Konfiguration;
using ScanSortierer.Core.Pdf;
using ScanSortierer.ViewModels;
using ScanSortierer.Views;

[assembly: AvaloniaTestApplication(typeof(ScanSortierer.Tests.TestApp))]

namespace ScanSortierer.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

public class OberflaechenTests
{
    private static (Dienste Dienste, string Eingang, string Ziel) Vorbereiten(bool testbetrieb)
    {
        var basis = Directory.CreateTempSubdirectory("ss-ui-").FullName;
        var eingang = Path.Combine(basis, "Scans");
        var ziel = Path.Combine(basis, "Dokumente");
        Directory.CreateDirectory(eingang);
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Beispielscans"), "*.pdf"))
            File.Copy(f, Path.Combine(eingang, Path.GetFileName(f)));
        var d = Dienste.Starten(new AppPfade(Path.Combine(basis, ".scan-sortierer")));
        d.Einstellungen.Eingangsordner = eingang;
        d.Einstellungen.Zielordner = ziel;
        d.Einstellungen.Testbetrieb = testbetrieb;
        d.Einstellungen.LetzteVorlage = "Rechnung";
        return (d, eingang, ziel);
    }

    private static async Task WarteAuf(Func<bool> bedingung, int sekunden = 120)
    {
        var ende = DateTime.UtcNow.AddSeconds(sekunden);
        while (!bedingung())
        {
            if (DateTime.UtcNow > ende)
                throw new TimeoutException("Bedingung nicht erfüllt.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Bildschirmfoto(HauptFenster fenster, string name)
    {
        var ordner = Environment.GetEnvironmentVariable("SS_SCREENSHOTS") ?? Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(ordner);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        fenster.CaptureRenderedFrame()?.Save(Path.Combine(ordner, name));
    }

    [AvaloniaFact]
    public async Task KompletterDurchlaufImTestbetrieb()
    {
        Beispiele.Tesseract();
        var (d, eingang, ziel) = Vorbereiten(testbetrieb: true);
        var vm = new HauptViewModel(d);
        var fenster = new HauptFenster { DataContext = vm, Width = 1400, Height = 880 };
        fenster.Show();

        await WarteAuf(() => vm.AktuelleDatei?.Name == "Scan 001.pdf" && vm.Seiten.Count == 4 && vm.Seiten.All(s => s.Vorschau != null));
        await WarteAuf(() => vm.Felder.FirstOrDefault(f => f.Name == "betrag")?.Wert == "49,90 €");
        Assert.Equal(3, vm.Dateien.Count);
        await WarteAuf(() => vm.Dateien[2].IstFehlerhaft);
        Assert.Equal("4 Seiten", vm.Dateien[1].Hinweis);
        Assert.Equal([false, true, false, true], vm.Seiten.Select(s => s.IstLeer));
        Assert.Equal([true, false, true, false], vm.Seiten.Select(s => s.IstAusgewaehlt));
        Assert.Equal("14.10.2026", vm.Felder.First(f => f.Name == "datum").Wert);
        Assert.Equal("4711-0815-26", vm.Felder.First(f => f.Name == "rechnungsnummer").Wert);
        Assert.Equal("Telekom Deutschland GmbH", vm.Felder.First(f => f.Name == "absender").Wert);
        Bildschirmfoto(fenster, "01-hauptfenster.png");

        // Absender auf einheitlichen Namen ändern; die erkannte Schreibweise wird gemerkt.
        vm.Felder.First(f => f.Name == "absender").Wert = "Telekom";
        vm.Stichworte = "Mobilfunk Oktober";
        Assert.Equal("2026-10-14_Telekom_Rechnung.pdf", vm.Dateiname);
        await vm.SpeichernUndWeiterCommand.ExecuteAsync(null);
        var gespeichert = Path.Combine(ziel, "2026-10-14_Telekom_Rechnung.pdf");
        Assert.True(File.Exists(gespeichert));
        var info = PdfSchreiber.LeseInfo(gespeichert);
        Assert.Equal("2", info["Seiten"]);
        Assert.Equal("49,90 €", info["Betrag"]);
        Assert.Equal("Telekom", info["Author"]);
        Assert.Equal("Mobilfunk Oktober", info["Keywords"]);
        Assert.Equal("Telekom", d.Absender.Normalisiere("Telekom Deutschland GmbH"));

        // Übrig sind nur die leeren Rückseiten: verwerfen schließt die Datei ab.
        Assert.All(vm.Seiten, s => Assert.True(s.IstLeer && s.IstAusgewaehlt));
        await vm.LoeschenCommand.ExecuteAsync(null);
        await WarteAuf(() => vm.AktuelleDatei?.Name == "Scan 002.pdf" && vm.Seiten.All(s => s.Vorschau != null));
        Assert.True(File.Exists(Path.Combine(eingang, "Scan 001.pdf")), "Testbetrieb: Original bleibt");
        Assert.True(vm.Dateien[0].IstErledigt);
        Assert.True(vm.Dateien[2].IstFehlerhaft, "defekte Datei wird übersprungen");

        // Scan 002: zwei Briefe, der zweite steht auf dem Kopf.
        await WarteAuf(() => vm.Seiten[2].Ocr != null && vm.Seiten[2].AutomatischGedreht);
        await WarteAuf(() => vm.Seiten[2].IstGrenze);
        Assert.Equal([true, false, false, false], vm.Seiten.Select(s => s.IstAusgewaehlt));
        Bildschirmfoto(fenster, "02-zwei-briefe.png");

        vm.AktuelleVorlage = vm.Vorlagen.First(v => v.Name == "Versicherung");
        await WarteAuf(() => vm.Felder.First(f => f.Name == "datum").Wert == "02.10.2026");
        vm.Felder.First(f => f.Name == "absender").Wert = "AOK";
        await vm.SpeichernUndWeiterCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(ziel, "2026-10-02_AOK_Versicherung.pdf")));
        Assert.Equal([false, true, false], vm.Seiten.Select(s => s.IstAusgewaehlt));

        // Rückgängig: neue Datei weg, Seiten wieder da.
        await vm.RueckgaengigCommand.ExecuteAsync(null);
        await WarteAuf(() => vm.Seiten.Count == 4);
        Assert.False(File.Exists(Path.Combine(ziel, "2026-10-02_AOK_Versicherung.pdf")));
        fenster.Close();
    }

    [AvaloniaFact]
    public async Task NormalbetriebVerschiebtOriginalUndRueckgaengigHoltEsZurueck()
    {
        var (d, eingang, ziel) = Vorbereiten(testbetrieb: false);
        d.VorgegebeneOcr = Core.Ocr.TesseractOcr.Nicht("Im Test abgeschaltet.");
        var vm = new HauptViewModel(d);
        var fenster = new HauptFenster { DataContext = vm };
        fenster.Show();
        await WarteAuf(() => vm.AktuelleDatei?.Name == "Scan 001.pdf" && vm.Seiten.All(s => s.Vorschau != null));
        Assert.Contains(vm.Hinweise, h => h.Contains("abgeschaltet"));

        // Ohne OCR: Felder von Hand.
        vm.Felder.First(f => f.Name == "absender").Wert = "Telekom";
        vm.Felder.First(f => f.Name == "datum").Wert = "14.10.26";
        vm.Felder.First(f => f.Name == "betrag").Wert = "49,9";
        vm.Felder.First(f => f.Name == "rechnungsnummer").Wert = "4711";
        await vm.SpeichernUndWeiterCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(ziel, "2026-10-14_Telekom_Rechnung.pdf")));
        Assert.Equal("49,90 €", PdfSchreiber.LeseInfo(Path.Combine(ziel, "2026-10-14_Telekom_Rechnung.pdf"))["Betrag"]);

        await vm.LoeschenCommand.ExecuteAsync(null);
        await WarteAuf(() => vm.AktuelleDatei?.Name == "Scan 002.pdf");
        Assert.False(File.Exists(Path.Combine(eingang, "Scan 001.pdf")));
        Assert.True(File.Exists(Path.Combine(eingang, "erledigt", "Scan 001.pdf")));

        await vm.RueckgaengigCommand.ExecuteAsync(null);
        await WarteAuf(() => vm.AktuelleDatei?.Name == "Scan 001.pdf" && vm.Seiten.Count == 2);
        Assert.True(File.Exists(Path.Combine(eingang, "Scan 001.pdf")));
        Assert.False(File.Exists(Path.Combine(eingang, "erledigt", "Scan 001.pdf")));
        Assert.True(File.Exists(Path.Combine(ziel, "2026-10-14_Telekom_Rechnung.pdf")), "nur der letzte Schritt wird zurückgenommen");
        var protokoll = File.ReadAllText(d.Pfade.Protokoll);
        Assert.Contains("VERSCHOBEN", protokoll);
        Assert.Contains("ZURÜCK", protokoll);
        fenster.Close();
    }
}

public class FensterTests
{
    [AvaloniaFact]
    public void EinstellungenUndVorlageneditorLassenSichOeffnen()
    {
        var basis = Directory.CreateTempSubdirectory("ss-ui-").FullName;
        var d = Dienste.Starten(new AppPfade(Path.Combine(basis, ".scan-sortierer")));
        d.Absender.Merke("Telekom", "Telekom Deutschland GmbH");
        d.Absender.Merke("AOK", "AOK Bayern - Die Gesundheitskasse");
        var ordner = Environment.GetEnvironmentVariable("SS_SCREENSHOTS") ?? Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(ordner);

        var einstellungen = new EinstellungenFenster { DataContext = new EinstellungenViewModel(d), Width = 1100, Height = 760 };
        einstellungen.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        einstellungen.CaptureRenderedFrame()?.Save(Path.Combine(ordner, "03-einstellungen.png"));
        ((EinstellungenViewModel)einstellungen.DataContext!).Bereich = 1;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        einstellungen.CaptureRenderedFrame()?.Save(Path.Combine(ordner, "04-absender.png"));
        einstellungen.Close();

        var vm = new VorlagenEditorViewModel(d);
        var editor = new VorlagenEditorFenster { DataContext = vm, Width = 1400, Height = 880 };
        editor.Show();
        vm.Auswahl = vm.Liste.First(v => v.Name == "Rechnung");
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Gueltig, vm.PruefText);
        Assert.Contains("\"rechnungsnummer\"", vm.JsonText);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        editor.CaptureRenderedFrame()?.Save(Path.Combine(ordner, "05-vorlageneditor.png"));

        // Betrag in den Dateinamen ist nicht erlaubt; das Feld wird automatisch herausgenommen.
        var betrag = vm.Felder.First(f => f.Name == "betrag");
        vm.AktuellesFeld = betrag;
        betrag.ImDateinamen = true;
        Assert.False(vm.Gueltig);
        betrag.ImDateinamen = false;
        Assert.True(vm.Gueltig);

        // Fehlerhaftes JSON wird verständlich abgelehnt.
        vm.JsonText = "{ \"name\": \"Rechnung\", ";
        vm.JsonUebernehmenCommand.Execute(null);
        Assert.False(vm.Gueltig);
        Assert.Contains("JSON nicht übernommen", vm.PruefText);
        vm.Geaendert = false;
        editor.Close();
    }
}
