using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScanSortierer.Core.Ablage;
using ScanSortierer.Core.Erkennung;
using ScanSortierer.Core.Ocr;
using ScanSortierer.Core.Pdf;
using ScanSortierer.Core.Vorlagen;

namespace ScanSortierer.ViewModels;

/// <summary>Ein rückgängig zu machender Schritt (Speichern oder Löschen von Seiten).</summary>
internal sealed record RueckgaengigEintrag(
    DateiViewModel Datei,
    List<SeiteViewModel> SeitenVorher,
    int GespeichertVorher,
    DateiStatus StatusVorher,
    string Beschreibung)
{
    public string? NeueDatei { get; set; }
    public string? OriginalVerschobenNach { get; set; }
}

public sealed partial class HauptViewModel : ObservableObject
{
    private const double VorschauDpi = 60;
    private const double OcrDpi = 300;

    private readonly Dienste _d;

    public Dienste Dienste => _d;
    private readonly Stack<RueckgaengigEintrag> _rueckgaengig = new();
    private CancellationTokenSource _arbeit = new();
    private CancellationTokenSource _einlesen = new();
    private SeiteViewModel? _letzteGeklickt;
    private bool _auswahlManuell;
    private bool _setztDateiname;
    private bool _setztListenAuswahl;
    private int _ocrOffen;

    public HauptViewModel(Dienste dienste)
    {
        _d = dienste;
        foreach (var w in dienste.Startwarnungen)
            Hinweise.Add(w);
        VorschauGroesse = Math.Clamp(dienste.Einstellungen.VorschauGroesse, 110, 420);
        LadeVorlagen();
        AktualisiereKopf();
    }

    public IDialoge? Dialoge { get; set; }

    public ObservableCollection<DateiViewModel> Dateien { get; } = [];
    public ObservableCollection<SeiteViewModel> Seiten { get; } = [];
    public ObservableCollection<Vorlage> Vorlagen { get; } = [];
    public ObservableCollection<FeldViewModel> Felder { get; } = [];
    public ObservableCollection<string> Hinweise { get; } = [];

    [ObservableProperty]
    public partial DateiViewModel? AktuelleDatei { get; set; }

    /// <summary>Auswahl in der Dateiliste (löst das Öffnen aus).</summary>
    [ObservableProperty]
    public partial DateiViewModel? ListenAuswahl { get; set; }

    [ObservableProperty]
    public partial Vorlage? AktuelleVorlage { get; set; }

    [ObservableProperty]
    public partial string Stichworte { get; set; } = "";

    [ObservableProperty]
    public partial string Dateiname { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateinameZuruecksetzbar))]
    public partial bool DateinameManuell { get; set; }

    public bool DateinameZuruecksetzbar => DateinameManuell;

    [ObservableProperty]
    public partial string? DateinameFehler { get; set; }

    [ObservableProperty]
    public partial string EingangsordnerAnzeige { get; set; } = "";

    [ObservableProperty]
    public partial bool Testbetrieb { get; set; }

    [ObservableProperty]
    public partial string ZielAnzeige { get; set; } = "";

    [ObservableProperty]
    public partial string DateienTitel { get; set; } = "DATEIEN";

    [ObservableProperty]
    public partial string DokumentTitel { get; set; } = "DOKUMENT";

    [ObservableProperty]
    public partial string StatusLinks { get; set; } = "";

    [ObservableProperty]
    public partial string StatusOcr { get; set; } = "";

    [ObservableProperty]
    public partial string LetzteAktion { get; set; } = "";

    [ObservableProperty]
    public partial bool KannRueckgaengig { get; set; }

    [ObservableProperty]
    public partial string OcrText { get; set; } = "";

    [ObservableProperty]
    public partial bool Beschaeftigt { get; set; }

    [ObservableProperty]
    public partial string LeerText { get; set; } = "";

    [ObservableProperty]
    public partial bool ZeigeSeiten { get; set; }

    [ObservableProperty]
    public partial double VorschauGroesse { get; set; } = 170;

    public bool HatHinweise => Hinweise.Count > 0;

    public int AnzahlAusgewaehlt => Seiten.Count(s => s.IstAusgewaehlt);

    // ------------------------------------------------------------------ Start und Einlesen

    public async Task StartenAsync()
    {
        Hinweise.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HatHinweise));
        OnPropertyChanged(nameof(HatHinweise));
        _d.Ocr = _d.VorgegebeneOcr ?? await Task.Run(() => TesseractOcr.Finde());
        if (!_d.Ocr.Verfuegbar)
            Hinweise.Add(_d.Ocr.Status + " Felder bitte von Hand ausfüllen.");
        await EinlesenAsync();
    }

    [RelayCommand]
    private async Task EinlesenAsync()
    {
        SchliesseAktuelle();
        Dateien.Clear();
        Seiten.Clear();
        AktuelleDatei = null;
        var ordner = _d.Einstellungen.Eingangsordner;
        if (string.IsNullOrWhiteSpace(ordner))
        {
            LeerText = "Bitte oben links den Eingangsordner mit den Scans wählen.";
            ZeigeSeiten = false;
            AktualisiereKopf();
            return;
        }
        if (!Directory.Exists(ordner))
        {
            LeerText = $"Der Eingangsordner „{ordner}“ ist nicht erreichbar. Ist das Netzlaufwerk verbunden?";
            ZeigeSeiten = false;
            AktualisiereKopf();
            return;
        }

        List<string> dateien;
        try
        {
            dateien = await Task.Run(() => Directory.EnumerateFiles(ordner)
                .Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(p).StartsWith('.'))
                .OrderBy(p => Path.GetFileName(p), NatuerlicheSortierung.Instanz)
                .ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LeerText = $"Der Eingangsordner kann nicht gelesen werden: {ex.Message}";
            ZeigeSeiten = false;
            return;
        }
        foreach (var p in dateien)
            Dateien.Add(new DateiViewModel(p));
        AktualisiereKopf();
        if (Dateien.Count == 0)
        {
            LeerText = "Im Eingangsordner liegen keine PDF-Dateien.";
            ZeigeSeiten = false;
            return;
        }
        _einlesen.Cancel();
        _einlesen = new CancellationTokenSource();
        await PruefeDateiAsync(Dateien[0]);
        _ = PruefeDateienAsync(Dateien.Skip(1).ToList(), _einlesen.Token);
        await NaechsteDateiAsync(null);
    }

    /// <summary>Prüft im Hintergrund, ob sich die Dateien öffnen lassen, und zeigt die Seitenzahl an.</summary>
    private async Task PruefeDateienAsync(List<DateiViewModel> dateien, CancellationToken abbruch)
    {
        foreach (var d in dateien)
        {
            if (abbruch.IsCancellationRequested)
                return;
            await PruefeDateiAsync(d);
        }
    }

    private async Task PruefeDateiAsync(DateiViewModel d)
    {
        if (d.Seiten != null || d.Status != DateiStatus.Offen)
            return;
        try
        {
            var seiten = await Task.Run(() =>
            {
                var daten = File.ReadAllBytes(d.Pfad);
                PdfQuelle.Oeffne(daten);
                using var p = PdfiumDokument.Oeffne(daten);
                return p.SeitenAnzahl;
            });
            if (d.Seiten is null && d.Status == DateiStatus.Offen)
                d.Hinweis = seiten == 1 ? "1 Seite" : $"{seiten} Seiten";
        }
        catch (Exception ex) when (ex is PdfFehler or IOException or UnauthorizedAccessException)
        {
            if (d.Status != DateiStatus.Offen || d.Seiten != null)
                return;
            MarkiereFehlerhaft(d, ex);
            AktualisiereKopf();
        }
    }

    private void MarkiereFehlerhaft(DateiViewModel datei, Exception ex)
    {
        datei.Status = DateiStatus.Fehlerhaft;
        datei.Hinweis = ex is PdfFehler { PasswortGeschuetzt: true } ? "passwortgeschützt – übersprungen" : "defekt – übersprungen";
        Hinweise.Add($"„{datei.Name}“ wird übersprungen: {ex.Message}");
        _d.Protokoll.Schreibe("ÜBERSPRUNGEN", $"{datei.Pfad}: {ex.Message}" + (ex.InnerException is { } i ? $" ({i.Message})" : ""));
    }

    [RelayCommand]
    private async Task OrdnerWaehlenAsync()
    {
        if (Dialoge is null)
            return;
        var ordner = await Dialoge.OrdnerWaehlenAsync("Eingangsordner mit den Scans wählen", _d.Einstellungen.Eingangsordner);
        if (ordner is null)
            return;
        var neu = _d.Einstellungen.Kopie();
        neu.Eingangsordner = ordner;
        if (neu.Pruefe() is { } fehler)
        {
            await Dialoge.MeldungAsync("Eingangsordner", fehler);
            return;
        }
        _d.Einstellungen = neu;
        SpeichereEinstellungen();
        await EinlesenAsync();
    }

    [RelayCommand]
    private async Task EinstellungenAsync()
    {
        if (Dialoge is null)
            return;
        var vorher = _d.Einstellungen.Kopie();
        await Dialoge.EinstellungenAsync();
        AktualisiereKopf();
        LadeVorlagenNeu();
        if (vorher.Eingangsordner != _d.Einstellungen.Eingangsordner)
            await EinlesenAsync();
        else
            AktualisiereDateiname();
    }

    [RelayCommand]
    private async Task VorlagenAsync()
    {
        if (Dialoge is null)
            return;
        await Dialoge.VorlagenEditorAsync();
        LadeVorlagenNeu();
    }

    // ------------------------------------------------------------------ Dateien öffnen

    partial void OnListenAuswahlChanged(DateiViewModel? value)
    {
        if (_setztListenAuswahl || value is null || value == AktuelleDatei)
            return;
        _ = OeffneDateiAsync(value);
    }

    private async Task NaechsteDateiAsync(DateiViewModel? nach)
    {
        var start = nach is null ? 0 : Dateien.IndexOf(nach) + 1;
        var reihenfolge = Dateien.Skip(start).Concat(Dateien.Take(start));
        var naechste = reihenfolge.FirstOrDefault(d => d.Status == DateiStatus.Offen && d != nach);
        if (naechste is null)
        {
            SchliesseAktuelle();
            AktuelleDatei = null;
            SetzeListenAuswahl(null);
            Seiten.Clear();
            ZeigeSeiten = false;
            LeerText = Dateien.Any(d => d.Status == DateiStatus.Offen)
                ? "Keine weitere offene Datei."
                : _d.Einstellungen.Testbetrieb
                    ? "Alle Dateien sind bearbeitet. (Testbetrieb: Die Originale liegen unverändert im Eingangsordner.)"
                    : "Alle Dateien sind bearbeitet. Die Originale liegen im Ordner „erledigt“.";
            AktualisiereKopf();
            return;
        }
        await OeffneDateiAsync(naechste);
    }

    private async Task OeffneDateiAsync(DateiViewModel datei)
    {
        if (datei.Status != DateiStatus.Offen)
        {
            SchliesseAktuelle();
            AktuelleDatei = datei;
            SetzeListenAuswahl(datei);
            Seiten.Clear();
            OcrText = "";
            ZeigeSeiten = false;
            LeerText = datei.Status == DateiStatus.Erledigt
                ? $"„{datei.Name}“ ist bereits vollständig bearbeitet."
                : $"„{datei.Name}“ kann nicht gelesen werden ({datei.Hinweis}).";
            AktualisiereStatus();
            return;
        }
        SchliesseAktuelle();
        _arbeit = new CancellationTokenSource();
        var abbruch = _arbeit.Token;
        AktuelleDatei = datei;
        SetzeListenAuswahl(datei);
        Seiten.Clear();
        OcrText = "";
        ZeigeSeiten = false;
        LeerText = $"„{datei.Name}“ wird geöffnet …";

        try
        {
            var (pdfium, quelle) = await Task.Run(() =>
            {
                var daten = File.ReadAllBytes(datei.Pfad);
                var q = PdfQuelle.Oeffne(daten);
                var p = PdfiumDokument.Oeffne(daten);
                return (p, q);
            }, abbruch);
            if (abbruch.IsCancellationRequested || AktuelleDatei != datei)
            {
                pdfium.Dispose();
                return;
            }
            datei.Pdfium = pdfium;
            datei.Quelle = quelle;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is PdfFehler or IOException or UnauthorizedAccessException)
        {
            MarkiereFehlerhaft(datei, ex);
            AktuelleDatei = null;
            await NaechsteDateiAsync(datei);
            return;
        }

        if (datei.Seiten is null)
        {
            datei.SeitenGesamt = datei.Pdfium.SeitenAnzahl;
            datei.Seiten = Enumerable.Range(0, datei.Pdfium.SeitenAnzahl).Select(i => new SeiteViewModel(i)).ToList();
        }
        foreach (var s in datei.Seiten)
        {
            s.VorschauBreite = VorschauGroesse;
            s.IstAusgewaehlt = false;
            s.IstFokus = false;
            Seiten.Add(s);
        }
        ZeigeSeiten = Seiten.Count > 0;
        _auswahlManuell = false;
        _letzteGeklickt = null;
        NeuesDokument();
        AktualisiereGrenzen();
        AutoAuswahl();
        AktualisiereDateiHinweis(datei);
        AktualisiereKopf();
        _ = VerarbeiteDateiAsync(datei, abbruch);
    }

    /// <summary>Vorschauen rendern, leere Seiten erkennen, danach Texterkennung im Hintergrund.</summary>
    private async Task VerarbeiteDateiAsync(DateiViewModel datei, CancellationToken abbruch)
    {
        try
        {
            foreach (var seite in Seiten.ToList())
            {
                if (abbruch.IsCancellationRequested)
                    return;
                await RendereVorschauAsync(datei, seite, erkenneLeer: seite.Ocr is null, abbruch);
            }
            if (abbruch.IsCancellationRequested)
                return;
            AktualisiereGrenzen();
            if (!_auswahlManuell)
                AutoAuswahl();
            AktualisiereDateiHinweis(datei);

            if (!_d.Ocr.Verfuegbar)
                return;
            // Ausgewählte Seiten zuerst, damit die Maske schnell gefüllt wird.
            var reihenfolge = Seiten.Where(s => !s.IstLeer).OrderByDescending(s => s.IstAusgewaehlt).ToList();
            foreach (var seite in reihenfolge)
                StarteOcr(datei, seite, abbruch);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is PdfFehler or ObjectDisposedException)
        {
            if (!abbruch.IsCancellationRequested)
                Hinweise.Add($"Fehler bei der Vorschau von „{datei.Name}“: {ex.Message}");
        }
    }

    private async Task RendereVorschauAsync(DateiViewModel datei, SeiteViewModel seite, bool erkenneLeer, CancellationToken abbruch)
    {
        var pdfium = datei.Pdfium;
        if (pdfium is null)
            return;
        var drehung = seite.Drehung;
        var (bild, leer) = await Task.Run(() =>
        {
            var b = pdfium.Rendere(seite.ScanIndex, drehung, VorschauDpi);
            return (b, Leerseiten.IstLeer(b));
        }, abbruch);
        if (abbruch.IsCancellationRequested || AktuelleDatei != datei)
            return;
        var alt = seite.Vorschau;
        seite.Vorschau = Bilder.ZuBitmap(bild);
        (alt as IDisposable)?.Dispose();
        if (erkenneLeer)
            seite.LeerVorschlag = _d.Einstellungen.LeereSeitenVorschlagen && leer;
    }

    private void StarteOcr(DateiViewModel datei, SeiteViewModel seite, CancellationToken abbruch)
    {
        if (!_d.Ocr.Verfuegbar || seite.OcrAngefordertFuer == seite.Drehung && seite.OcrAufgabe != null)
            return;
        if (seite.Ocr != null && seite.Ocr.Drehung == seite.Drehung)
            return;
        seite.OcrAngefordertFuer = seite.Drehung;
        seite.OcrAufgabe = OcrAsync(datei, seite, seite.Drehung, abbruch);
    }

    private async Task OcrAsync(DateiViewModel datei, SeiteViewModel seite, int drehung, CancellationToken abbruch)
    {
        var pdfium = datei.Pdfium;
        if (pdfium is null)
            return;
        _ocrOffen++;
        AktualisiereOcrStatus();
        seite.OcrLaeuft = true;
        try
        {
            var bild = await Task.Run(() => pdfium.Rendere(seite.ScanIndex, drehung, OcrDpi), abbruch);
            var ergebnis = await _d.Ocr.ErkenneAsync(bild, drehung, abbruch);

            // Schlecht lesbar? Vielleicht steht die Seite auf dem Kopf (falsch eingelegt).
            if (!seite.DrehversuchErfolgt && ergebnis.GuteWoerter < 12 && !seite.IstLeer)
            {
                seite.DrehversuchErfolgt = true;
                var gedreht = (drehung + 180) % 360;
                var bild2 = await Task.Run(() => pdfium.Rendere(seite.ScanIndex, gedreht, OcrDpi), abbruch);
                var ergebnis2 = await _d.Ocr.ErkenneAsync(bild2, gedreht, abbruch);
                if (ergebnis2.GuteWoerter > ergebnis.GuteWoerter * 2 + 5 && seite.Drehung == drehung)
                {
                    ergebnis = ergebnis2;
                    drehung = gedreht;
                    seite.Drehung = gedreht;
                    seite.OcrAngefordertFuer = gedreht;
                    seite.AutomatischGedreht = true;
                    if (AktuelleDatei == datei)
                        await RendereVorschauAsync(datei, seite, erkenneLeer: false, abbruch);
                }
            }
            if (abbruch.IsCancellationRequested || seite.Drehung != drehung)
                return;
            seite.Ocr = ergebnis;
            if (AktuelleDatei != datei)
                return;
            if (seite.IstFokus)
                ZeigeOcrText(seite);
            AktualisiereGrenzen();
            if (!_auswahlManuell)
                AutoAuswahl();
            else if (seite.IstAusgewaehlt)
                FuelleFelder();
            AktualisiereDateiHinweis(datei);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or PdfFehler or ObjectDisposedException or InvalidOperationException)
        {
            if (!abbruch.IsCancellationRequested)
                Hinweise.Add($"Texterkennung für {seite.Bezeichnung} von „{datei.Name}“ fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            seite.OcrLaeuft = false;
            _ocrOffen--;
            AktualisiereOcrStatus();
        }
    }

    private void SchliesseAktuelle()
    {
        _arbeit.Cancel();
        foreach (var s in Seiten)
        {
            // Abgebrochene Erkennungen später neu anstoßen.
            if (s.Ocr is null || s.Ocr.Drehung != s.Drehung)
            {
                s.OcrAufgabe = null;
                s.OcrAngefordertFuer = null;
            }
        }
        AktuelleDatei?.Schliessen();
    }

    public void Beenden()
    {
        SchliesseAktuelle();
        _d.Einstellungen.VorschauGroesse = VorschauGroesse;
        SpeichereEinstellungen();
    }

    // ------------------------------------------------------------------ Seiten: Auswahl, Grenzen

    /// <summary>Klick auf eine Seite: Auswahl umschalten (Umschalt: Bereich).</summary>
    public void SeiteGeklickt(SeiteViewModel seite, bool bereich)
    {
        _auswahlManuell = true;
        if (bereich && _letzteGeklickt != null && Seiten.Contains(_letzteGeklickt))
        {
            int a = Seiten.IndexOf(_letzteGeklickt), b = Seiten.IndexOf(seite);
            for (var i = Math.Min(a, b); i <= Math.Max(a, b); i++)
                Seiten[i].IstAusgewaehlt = true;
        }
        else
        {
            seite.IstAusgewaehlt = !seite.IstAusgewaehlt;
        }
        _letzteGeklickt = seite;
        SetzeFokus(seite);
        NummeriereAuswahl();
        FuelleFelder();
    }

    public void SetzeFokus(SeiteViewModel seite)
    {
        foreach (var s in Seiten)
            s.IstFokus = s == seite;
        ZeigeOcrText(seite);
    }

    private void ZeigeOcrText(SeiteViewModel seite)
    {
        if (seite.Ocr is { } o)
        {
            var text = o.Text.Replace('\n', ' ').Trim();
            OcrText = $"Erkannter Text (OCR) {seite.Bezeichnung}: " + (text.Length == 0 ? "(kein Text)" : $"„{(text.Length > 600 ? text[..600] + " …" : text)}“");
        }
        else
        {
            OcrText = seite.IstLeer ? $"{seite.Bezeichnung}: leere Seite" :
                _d.Ocr.Verfuegbar ? $"{seite.Bezeichnung}: Texterkennung läuft …" : $"{seite.Bezeichnung}: Texterkennung nicht verfügbar";
        }
    }

    [RelayCommand]
    private void GrenzeUmschalten(SeiteViewModel seite)
    {
        seite.GrenzeManuell = !seite.BeginntDokument;
        AktualisiereGrenzen();
        if (!_auswahlManuell)
            AutoAuswahl();
    }

    [RelayCommand]
    private void LeerUmschalten(SeiteViewModel seite)
    {
        seite.LeerManuell = !seite.IstLeer;
        if (!seite.IstLeer && AktuelleDatei is { } datei)
            StarteOcr(datei, seite, _arbeit.Token);
        AktualisiereGrenzen();
        if (!_auswahlManuell)
            AutoAuswahl();
    }

    [RelayCommand]
    private void VorschauOeffnen(SeiteViewModel seite) => Dialoge?.Vorschau(seite);

    [RelayCommand]
    private void DokumentAuswaehlen()
    {
        _auswahlManuell = false;
        AutoAuswahl();
    }

    private void AktualisiereGrenzen()
    {
        var merkmale = Seiten.Select(s => new SeitenMerkmale(s.ScanIndex, s.IstLeer, s.Ocr)).ToList();
        var vorschlag = DokumentGrenzen.Vorschlag(merkmale, _d.Absender);
        for (var i = 0; i < Seiten.Count; i++)
        {
            Seiten[i].IstErste = i == 0;
            Seiten[i].GrenzeVorschlag = vorschlag[i];
        }
    }

    /// <summary>Wählt die (nicht leeren) Seiten des ersten vorgeschlagenen Dokuments.</summary>
    private void AutoAuswahl()
    {
        if (Seiten.Count == 0)
        {
            NummeriereAuswahl();
            return;
        }
        var nurLeer = Seiten.All(s => s.IstLeer);
        var imErsten = true;
        var inhaltGesehen = false;
        foreach (var s in Seiten)
        {
            // Das erste Dokument endet an der nächsten Grenze nach seiner ersten nicht leeren Seite.
            if (inhaltGesehen && s.BeginntDokument && !s.IstLeer)
                imErsten = false;
            if (!s.IstLeer)
                inhaltGesehen = true;
            s.IstAusgewaehlt = nurLeer || (imErsten && !s.IstLeer);
        }
        if (nurLeer)
            StatusLinks = "Nur noch leere Seiten übrig – mit „Löschen“ verwerfen, dann geht es zur nächsten Datei.";
        if (Seiten.FirstOrDefault(s => s.IstAusgewaehlt) is { } erste && !Seiten.Any(s => s.IstFokus))
            SetzeFokus(erste);
        NummeriereAuswahl();
        FuelleFelder();
    }

    private void NummeriereAuswahl()
    {
        var n = 0;
        foreach (var s in Seiten)
            s.AuswahlNummer = s.IstAusgewaehlt ? ++n : 0;
        var ausgewaehlt = Seiten.Where(s => s.IstAusgewaehlt).Select(s => s.ScanIndex + 1).ToList();
        var nummer = (AktuelleDatei?.GespeicherteDokumente ?? 0) + 1;
        DokumentTitel = ausgewaehlt.Count == 0 ? $"DOKUMENT {nummer} · KEINE SEITEN GEWÄHLT" : $"DOKUMENT {nummer} · {SeitenText(ausgewaehlt)}";
        OnPropertyChanged(nameof(AnzahlAusgewaehlt));
        AktualisiereStatus();
    }

    private static string SeitenText(List<int> seiten)
    {
        if (seiten.Count == 1)
            return $"SEITE {seiten[0]}";
        var fortlaufend = seiten.Zip(seiten.Skip(1)).All(p => p.Second == p.First + 1);
        return fortlaufend ? $"SEITEN {seiten[0]}–{seiten[^1]}" : "SEITEN " + string.Join(", ", seiten);
    }

    // ------------------------------------------------------------------ Seiten bearbeiten

    [RelayCommand]
    private Task LinksDrehenAsync() => DreheAsync(-90);

    [RelayCommand]
    private Task RechtsDrehenAsync() => DreheAsync(90);

    private async Task DreheAsync(int grad)
    {
        var datei = AktuelleDatei;
        var gewaehlt = Seiten.Where(s => s.IstAusgewaehlt).ToList();
        if (datei is null || gewaehlt.Count == 0)
        {
            StatusLinks = "Bitte zuerst Seiten auswählen.";
            return;
        }
        var abbruch = _arbeit.Token;
        foreach (var s in gewaehlt)
        {
            s.Drehung = ((s.Drehung + grad) % 360 + 360) % 360;
            s.AutomatischGedreht = false;
            s.DrehversuchErfolgt = true;
        }
        foreach (var s in gewaehlt)
        {
            await RendereVorschauAsync(datei, s, erkenneLeer: false, abbruch);
            if (!s.IstLeer)
                StarteOcr(datei, s, abbruch);
        }
    }

    [RelayCommand]
    private void NachLinks() => Verschiebe(-1);

    [RelayCommand]
    private void NachRechts() => Verschiebe(1);

    private void Verschiebe(int richtung)
    {
        var gewaehlt = Seiten.Where(s => s.IstAusgewaehlt).ToList();
        if (gewaehlt.Count == 0)
        {
            StatusLinks = "Bitte zuerst die Seiten auswählen, die verschoben werden sollen.";
            return;
        }
        var reihenfolge = richtung < 0 ? gewaehlt : Enumerable.Reverse(gewaehlt).ToList();
        foreach (var s in reihenfolge)
        {
            var i = Seiten.IndexOf(s);
            var ziel = i + richtung;
            if (ziel < 0 || ziel >= Seiten.Count || Seiten[ziel].IstAusgewaehlt)
                continue;
            Seiten.Move(i, ziel);
        }
        _auswahlManuell = true;
        UebernehmeReihenfolge();
        AktualisiereGrenzen();
        NummeriereAuswahl();
        FuelleFelder();
    }

    /// <summary>Verschiebt eine Seite per Ziehen an die Position einer anderen.</summary>
    public void VerschiebeAuf(SeiteViewModel seite, SeiteViewModel ziel)
    {
        if (seite == ziel)
            return;
        var von = Seiten.IndexOf(seite);
        var nach = Seiten.IndexOf(ziel);
        if (von < 0 || nach < 0)
            return;
        Seiten.Move(von, nach);
        _auswahlManuell = true;
        UebernehmeReihenfolge();
        AktualisiereGrenzen();
        NummeriereAuswahl();
        FuelleFelder();
    }

    private void UebernehmeReihenfolge()
    {
        if (AktuelleDatei is { } d)
            d.Seiten = [.. Seiten];
    }

    [RelayCommand]
    private void LeereMarkieren()
    {
        _auswahlManuell = true;
        foreach (var s in Seiten)
            s.IstAusgewaehlt = s.IstLeer;
        NummeriereAuswahl();
        FuelleFelder();
        if (!Seiten.Any(s => s.IstLeer))
            StatusLinks = "Keine leeren Seiten erkannt.";
    }

    [RelayCommand]
    private async Task LoeschenAsync()
    {
        var datei = AktuelleDatei;
        var gewaehlt = Seiten.Where(s => s.IstAusgewaehlt).ToList();
        if (datei is null || gewaehlt.Count == 0 || Beschaeftigt)
        {
            StatusLinks = "Bitte zuerst die Seiten auswählen, die verworfen werden sollen.";
            return;
        }
        var nichtLeer = gewaehlt.Count(s => !s.IstLeer);
        if (nichtLeer > 0 && Dialoge != null)
        {
            var ok = await Dialoge.FrageAsync("Seiten verwerfen",
                $"{nichtLeer} der gewählten Seiten {(nichtLeer == 1 ? "ist" : "sind")} nicht leer. Trotzdem verwerfen?\n\n" +
                "Die Seiten werden nur aus der Bearbeitung entfernt; die Original-Datei bleibt unverändert.",
                "Verwerfen");
            if (!ok)
                return;
        }
        var eintrag = new RueckgaengigEintrag(datei, [.. Seiten], datei.GespeicherteDokumente, datei.Status,
            $"{gewaehlt.Count} Seite(n) verworfen");
        foreach (var s in gewaehlt)
            Seiten.Remove(s);
        UebernehmeReihenfolge();
        _d.Protokoll.Schreibe("VERWORFEN", $"{datei.Pfad}: Seite(n) {string.Join(", ", gewaehlt.Select(s => s.ScanIndex + 1))} (Original unverändert)");
        _rueckgaengig.Push(eintrag);
        LetzteAktion = $"Letzte Aktion: {eintrag.Beschreibung}";
        KannRueckgaengig = true;
        await NachVerteilenAsync(datei, eintrag);
    }

    // ------------------------------------------------------------------ Maske und Speichern

    partial void OnAktuelleVorlageChanged(Vorlage? value)
    {
        var alteWerte = Felder.Where(f => f.Bearbeitet && f.Wert.Length > 0).ToDictionary(f => f.Name, f => f.Wert, StringComparer.OrdinalIgnoreCase);
        foreach (var f in Felder)
            f.WertGeaendert -= FeldGeaendert;
        Felder.Clear();
        if (value is null)
        {
            AktualisiereDateiname();
            return;
        }
        if (value.Name != _d.Einstellungen.LetzteVorlage)
        {
            _d.Einstellungen.LetzteVorlage = value.Name;
            SpeichereEinstellungen();
        }
        foreach (var feld in value.Felder)
        {
            var optionen = feld.IstAbsender
                ? feld.Werte.Select(_d.Absender.Normalisiere).Concat(_d.Absender.Namen()).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                : feld.Werte.AsEnumerable();
            var vm = new FeldViewModel(feld, optionen);
            if (alteWerte.TryGetValue(feld.Name, out var alt))
                vm.Wert = alt;
            vm.WertGeaendert += FeldGeaendert;
            Felder.Add(vm);
        }
        FuelleFelder();
    }

    private void FeldGeaendert(object? sender, EventArgs e) => AktualisiereDateiname();

    partial void OnDateinameChanged(string value)
    {
        DateinameFehler = null;
        if (!_setztDateiname)
            DateinameManuell = true;
    }

    partial void OnVorschauGroesseChanged(double value)
    {
        foreach (var s in Seiten)
            s.VorschauBreite = value;
    }

    [RelayCommand]
    private void DateinameZuruecksetzen()
    {
        DateinameManuell = false;
        AktualisiereDateiname();
    }

    private void AktualisiereDateiname()
    {
        if (DateinameManuell)
            return;
        _setztDateiname = true;
        try
        {
            Dateiname = AktuelleVorlage is null ? "" : Dateinamen.Erzeuge(AktuelleVorlage, AktuelleWerte(normalisiert: false));
        }
        finally
        {
            _setztDateiname = false;
        }
    }

    private Dictionary<string, string> AktuelleWerte(bool normalisiert)
    {
        var werte = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Felder)
        {
            var w = f.Wert.Trim();
            if (f.Feld.IstAbsender)
                w = _d.Absender.Normalisiere(w);
            werte[f.Name] = w;
        }
        return werte;
    }

    /// <summary>Belegt die Felder aus der Texterkennung der gewählten Seiten vor (Nutzereingaben bleiben).</summary>
    private void FuelleFelder()
    {
        if (AktuelleVorlage is null)
        {
            AktualisiereDateiname();
            return;
        }
        var ocr = Seiten.Where(s => s.IstAusgewaehlt && s.Ocr != null).Select(s => s.Ocr!).ToList();
        var erkannt = FeldErkennung.Erkenne(AktuelleVorlage, ocr, _d.Absender);
        foreach (var f in Felder)
        {
            if (f.Bearbeitet)
                continue;
            if (erkannt.TryGetValue(f.Name, out var wert))
                f.SetzeErkannt(wert.Wert, wert.Rohtext);
            else if (f.Erkannt || f.Wert.Length > 0)
                f.SetzeLeer();
        }
        AktualisiereDateiname();
    }

    private void NeuesDokument()
    {
        foreach (var f in Felder)
            f.SetzeLeer();
        _setztDateiname = true;
        Stichworte = "";
        DateinameManuell = false;
        _setztDateiname = false;
        AktualisiereDateiname();
    }

    [RelayCommand]
    private async Task SpeichernUndWeiterAsync()
    {
        var datei = AktuelleDatei;
        if (datei?.Quelle is null || Beschaeftigt)
            return;
        var seiten = Seiten.Where(s => s.IstAusgewaehlt).ToList();
        if (seiten.Count == 0)
        {
            StatusLinks = "Bitte die Seiten auswählen, die zum Dokument gehören.";
            return;
        }
        if (AktuelleVorlage is null)
        {
            StatusLinks = "Bitte einen Dokumenttyp wählen.";
            return;
        }
        var gueltig = true;
        foreach (var f in Felder)
            gueltig &= f.Pruefe();
        var name = Dateinamen.NormalisiereEingabe(Dateiname);
        if (name.Length == 0)
        {
            DateinameFehler = "Bitte einen Dateinamen angeben.";
            gueltig = false;
        }
        if (!gueltig)
        {
            StatusLinks = "Bitte die markierten Felder prüfen.";
            return;
        }

        Beschaeftigt = true;
        try
        {
            if (_d.Einstellungen.OcrTextebene && _d.Ocr.Verfuegbar)
            {
                foreach (var s in seiten.Where(s => !s.IstLeer))
                    StarteOcr(datei, s, _arbeit.Token);
                var offen = seiten.Select(s => s.OcrAufgabe).OfType<Task>().Where(t => !t.IsCompleted).ToList();
                if (offen.Count > 0)
                {
                    StatusLinks = "Warte auf die Texterkennung der gewählten Seiten …";
                    await Task.WhenAll(offen);
                }
            }

            var werte = AktuelleWerte(normalisiert: true);
            var vorlage = AktuelleVorlage;
            Werte.LeseDatum(werte.GetValueOrDefault(SonderFelder.Datum), out var briefdatum);
            var eigene = new List<KeyValuePair<string, string>> { new("Dokumenttyp", vorlage.Name) };
            foreach (var f in vorlage.Felder)
            {
                var w = werte.GetValueOrDefault(f.Name, "");
                if (f.Typ == FeldTyp.Datum && Werte.LeseDatum(w, out var d))
                    w = Werte.DatumFuerDateiname(d);
                eigene.Add(new(f.Name == SonderFelder.Datum ? "Briefdatum" : f.AnzeigeName, w));
            }
            eigene.Add(new("Stichworte", Stichworte.Trim()));
            eigene.Add(new("Originaldatei", datei.Name));
            var meta = new Metadaten
            {
                Titel = Path.GetFileNameWithoutExtension(name),
                Autor = werte.GetValueOrDefault(SonderFelder.Absender, ""),
                Betreff = vorlage.Name,
                Stichworte = Stichworte.Trim(),
                Briefdatum = briefdatum == default ? null : briefdatum,
                Eigene = eigene,
            };
            var auftraege = seiten.Select(s => new SeitenAuftrag(s.ScanIndex, s.Drehung, s.Ocr?.Drehung == s.Drehung ? s.Ocr : null)).ToList();
            var ziel = _d.Einstellungen.ZielordnerEffektiv();
            var quelle = datei.Quelle;
            var textebene = _d.Einstellungen.OcrTextebene;

            StatusLinks = "Speichere …";
            string pfad;
            try
            {
                pfad = await Task.Run(() => PdfSchreiber.Speichere(quelle, auftraege, meta, ziel, name, textebene));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                StatusLinks = "Speichern fehlgeschlagen.";
                if (Dialoge != null)
                    await Dialoge.MeldungAsync("Speichern fehlgeschlagen", $"Das Dokument konnte nicht gespeichert werden:\n{ex.Message}\n\nDie Original-Datei ist unverändert.");
                return;
            }

            // Absender merken (mit abweichender erkannter Schreibweise).
            if (Felder.FirstOrDefault(f => f.Feld.IstAbsender) is { } abs && werte.GetValueOrDefault(SonderFelder.Absender) is { Length: > 0 } absName)
            {
                if (_d.Absender.Merke(absName, abs.ErkannteSchreibweise))
                {
                    try
                    {
                        _d.Absender.Speichern();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Hinweise.Add($"Die gemerkten Absender konnten nicht gespeichert werden: {ex.Message}");
                    }
                }
            }

            _d.Protokoll.Schreibe("GESPEICHERT", $"{pfad} ← {datei.Pfad}, Seite(n) {string.Join(", ", seiten.Select(s => s.ScanIndex + 1))}, Typ {vorlage.Name}");
            var eintrag = new RueckgaengigEintrag(datei, [.. Seiten], datei.GespeicherteDokumente, datei.Status, $"„{Path.GetFileName(pfad)}“ gespeichert")
            {
                NeueDatei = pfad,
            };
            _rueckgaengig.Push(eintrag);
            KannRueckgaengig = true;
            LetzteAktion = $"Letzte Aktion: {eintrag.Beschreibung}";
            datei.GespeicherteDokumente++;
            foreach (var s in seiten)
                Seiten.Remove(s);
            UebernehmeReihenfolge();
            StatusLinks = "";
            await NachVerteilenAsync(datei, eintrag);
        }
        finally
        {
            Beschaeftigt = false;
        }
    }

    /// <summary>Nach Speichern oder Verwerfen: weiter im Dokument oder Datei abschließen.</summary>
    private async Task NachVerteilenAsync(DateiViewModel datei, RueckgaengigEintrag eintrag)
    {
        _auswahlManuell = false;
        _letzteGeklickt = null;
        NeuesDokument();
        if (Seiten.Count > 0)
        {
            foreach (var s in Seiten)
                s.IstFokus = false;
            AktualisiereGrenzen();
            AutoAuswahl();
            AktualisiereDateiHinweis(datei);
            return;
        }

        // Alle Seiten verteilt.
        datei.Status = DateiStatus.Erledigt;
        datei.Hinweis = $"{datei.GespeicherteDokumente} Dokument(e) gespeichert";
        SchliesseAktuelle();
        if (_d.Einstellungen.Testbetrieb)
        {
            _d.Protokoll.Schreibe("ERLEDIGT", $"{datei.Pfad} (Testbetrieb: Original bleibt unverändert)");
        }
        else
        {
            var erledigt = _d.Einstellungen.ErledigtOrdnerEffektiv();
            try
            {
                var neu = await Task.Run(() => SichereDateien.VerschiebeEindeutig(datei.Pfad, erledigt));
                eintrag.OriginalVerschobenNach = neu;
                _d.Protokoll.Schreibe("VERSCHOBEN", $"{datei.Pfad} → {neu}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Hinweise.Add($"„{datei.Name}“ ist fertig, konnte aber nicht nach „erledigt“ verschoben werden: {ex.Message}");
            }
        }
        AktualisiereKopf();
        await NaechsteDateiAsync(datei);
    }

    [RelayCommand]
    private async Task UeberspringenAsync()
    {
        if (AktuelleDatei is { } d)
            await NaechsteDateiAsync(d);
    }

    [RelayCommand]
    private async Task RueckgaengigAsync()
    {
        if (Beschaeftigt || !_rueckgaengig.TryPop(out var e))
            return;
        Beschaeftigt = true;
        try
        {
            if (e.NeueDatei != null && File.Exists(e.NeueDatei))
            {
                try
                {
                    File.Delete(e.NeueDatei);
                    _d.Protokoll.Schreibe("ENTFERNT", $"{e.NeueDatei} (rückgängig)");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Hinweise.Add($"„{e.NeueDatei}“ konnte nicht entfernt werden: {ex.Message}");
                }
            }
            if (e.OriginalVerschobenNach != null && File.Exists(e.OriginalVerschobenNach))
            {
                try
                {
                    var zurueck = SichereDateien.VerschiebeZurueck(e.OriginalVerschobenNach, e.Datei.Pfad);
                    _d.Protokoll.Schreibe("ZURÜCK", $"{e.OriginalVerschobenNach} → {zurueck} (rückgängig)");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Hinweise.Add($"Das Original konnte nicht zurückverschoben werden: {ex.Message}");
                }
            }
            if (AktuelleDatei == e.Datei)
                SchliesseAktuelle();
            e.Datei.Seiten = e.SeitenVorher;
            e.Datei.GespeicherteDokumente = e.GespeichertVorher;
            e.Datei.Status = e.StatusVorher;
            LetzteAktion = $"Rückgängig: {e.Beschreibung}";
            KannRueckgaengig = _rueckgaengig.Count > 0;
            AktualisiereKopf();
        }
        finally
        {
            Beschaeftigt = false;
        }
        await OeffneDateiAsync(e.Datei);
    }

    // ------------------------------------------------------------------ Hilfen

    [RelayCommand]
    private void HinweisSchliessen(string hinweis) => Hinweise.Remove(hinweis);

    private void LadeVorlagen()
    {
        var (vorlagen, fehler) = _d.Vorlagen.LadeAlle();
        foreach (var f in fehler)
            Hinweise.Add(f);
        Vorlagen.Clear();
        foreach (var v in vorlagen)
            Vorlagen.Add(v);
        AktuelleVorlage = Vorlagen.FirstOrDefault(v => v.Name == _d.Einstellungen.LetzteVorlage) ?? Vorlagen.FirstOrDefault();
    }

    private void LadeVorlagenNeu()
    {
        var name = AktuelleVorlage?.Name;
        foreach (var h in Hinweise.Where(h => h.StartsWith("Vorlage „", StringComparison.Ordinal)).ToList())
            Hinweise.Remove(h);
        var (vorlagen, fehler) = _d.Vorlagen.LadeAlle();
        foreach (var f in fehler)
            Hinweise.Add(f);
        Vorlagen.Clear();
        foreach (var v in vorlagen)
            Vorlagen.Add(v);
        AktuelleVorlage = Vorlagen.FirstOrDefault(v => v.Name == name) ?? Vorlagen.FirstOrDefault();
    }

    private void SetzeListenAuswahl(DateiViewModel? d)
    {
        _setztListenAuswahl = true;
        ListenAuswahl = d;
        _setztListenAuswahl = false;
    }

    private void AktualisiereKopf()
    {
        var e = _d.Einstellungen;
        EingangsordnerAnzeige = string.IsNullOrWhiteSpace(e.Eingangsordner) ? "Eingangsordner wählen …" : $"Eingangsordner: {e.Eingangsordner}";
        Testbetrieb = e.Testbetrieb;
        ZielAnzeige = $"Ziel: {e.ZielordnerEffektiv()}";
        var offen = Dateien.Count(d => d.Status == DateiStatus.Offen);
        DateienTitel = Dateien.Count == 0 ? "DATEIEN" : $"DATEIEN ({offen} VON {Dateien.Count} OFFEN)";
        AktualisiereStatus();
    }

    private void AktualisiereStatus()
    {
        if (AktuelleDatei is { } d)
            StatusLinks = $"{d.Name} · {Seiten.Count} {(Seiten.Count == 1 ? "Seite" : "Seiten")} übrig · {AnzahlAusgewaehlt} ausgewählt";
        else
            StatusLinks = "";
    }

    private void AktualisiereOcrStatus() =>
        StatusOcr = _ocrOffen > 0 ? $"Texterkennung läuft ({_ocrOffen} {(_ocrOffen == 1 ? "Seite" : "Seiten")}) …" : "";

    private void AktualisiereDateiHinweis(DateiViewModel datei)
    {
        if (datei.Status != DateiStatus.Offen || datei.Seiten is null)
            return;
        var seiten = datei.Seiten;
        var dokumente = seiten.Count(s => s.BeginntDokument && !s.IstLeer);
        var leer = seiten.Count(s => s.IstLeer);
        var teile = new List<string> { $"{datei.SeitenGesamt} Seiten" };
        if (datei.GespeicherteDokumente > 0)
            teile.Add($"{seiten.Count} übrig");
        if (dokumente > 0)
            teile.Add(dokumente == 1 ? "1 Dokument erkannt" : $"{dokumente} Dokumente erkannt");
        if (leer > 0)
            teile.Add($"{leer} leer");
        datei.Hinweis = string.Join(" · ", teile);
    }

    private void SpeichereEinstellungen()
    {
        try
        {
            _d.Einstellungen.Speichern(_d.Pfade.Einstellungen);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Hinweise.Add($"Die Einstellungen konnten nicht gespeichert werden: {ex.Message}");
        }
    }
}

/// <summary>Sortiert „Scan 2“ vor „Scan 10“.</summary>
internal sealed class NatuerlicheSortierung : IComparer<string>
{
    public static readonly NatuerlicheSortierung Instanz = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
            return string.Compare(x, y, StringComparison.Ordinal);
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsDigit(x[i]))
                    i++;
                while (j < y.Length && char.IsDigit(y[j]))
                    j++;
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                var c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (c != 0)
                    return c;
            }
            else
            {
                var c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
                if (c != 0)
                    return c;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
