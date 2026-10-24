using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScanSortierer.Core.Konfiguration;

namespace ScanSortierer.ViewModels;

public sealed partial class AbsenderZeile : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>Schreibweisen, durch Semikolon getrennt.</summary>
    [ObservableProperty]
    public partial string Schreibweisen { get; set; } = "";
}

public sealed partial class EinstellungenViewModel : ObservableObject
{
    private readonly Dienste _d;

    public EinstellungenViewModel(Dienste dienste)
    {
        _d = dienste;
        var e = dienste.Einstellungen;
        Eingangsordner = e.Eingangsordner;
        Zielordner = e.ZielordnerEffektiv();
        ErledigtOrdner = e.ErledigtOrdner;
        Testbetrieb = e.Testbetrieb;
        OcrTextebene = e.OcrTextebene;
        LeereSeitenVorschlagen = e.LeereSeitenVorschlagen;
        foreach (var a in dienste.Absender.Eintraege.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            Absender.Add(new AbsenderZeile { Name = a.Name, Schreibweisen = string.Join("; ", a.Schreibweisen) });
        Protokoll = dienste.Protokoll.LeseEnde();
        if (Protokoll.Length == 0)
            Protokoll = "(noch keine Einträge)";
    }

    public Func<string, string?, Task<string?>>? OrdnerWaehler { get; set; }
    public event EventHandler? Fertig;

    public string Konfigurationsordner => _d.Pfade.Basis;
    public string Version => $"Scan-Sortierer {Dienste.Version}";
    public string OcrStatus => _d.Ocr.Status;
    public string ErledigtPlatzhalter => "leer = Unterordner „erledigt“ im Eingangsordner";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZeigeAllgemein), nameof(ZeigeAbsender), nameof(ZeigeProtokoll), nameof(ZeigeUeber))]
    public partial int Bereich { get; set; }

    public bool ZeigeAllgemein => Bereich == 0;
    public bool ZeigeAbsender => Bereich == 1;
    public bool ZeigeProtokoll => Bereich == 2;
    public bool ZeigeUeber => Bereich == 3;

    [ObservableProperty]
    public partial string Eingangsordner { get; set; } = "";

    [ObservableProperty]
    public partial string Zielordner { get; set; } = "";

    [ObservableProperty]
    public partial string ErledigtOrdner { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestbetriebText))]
    public partial bool Testbetrieb { get; set; }

    public string TestbetriebText => Testbetrieb
        ? "Testbetrieb aktiv: Originale werden nicht verschoben, alle Dateien werden bei jedem Durchlauf erneut angeboten."
        : "Normalbetrieb: Vollständig verteilte Originale werden in den Ordner für erledigte Originale verschoben (nie gelöscht).";

    [ObservableProperty]
    public partial bool OcrTextebene { get; set; }

    [ObservableProperty]
    public partial bool LeereSeitenVorschlagen { get; set; }

    [ObservableProperty]
    public partial string? Fehler { get; set; }

    public ObservableCollection<AbsenderZeile> Absender { get; } = [];

    public string Protokoll { get; }

    [RelayCommand]
    private async Task EingangWaehlenAsync()
    {
        if (OrdnerWaehler != null && await OrdnerWaehler("Eingangsordner wählen", Eingangsordner) is { } p)
            Eingangsordner = p;
    }

    [RelayCommand]
    private async Task ZielWaehlenAsync()
    {
        if (OrdnerWaehler != null && await OrdnerWaehler("Zielordner für fertige Dokumente wählen", Zielordner) is { } p)
            Zielordner = p;
    }

    [RelayCommand]
    private async Task ErledigtWaehlenAsync()
    {
        if (OrdnerWaehler != null && await OrdnerWaehler("Ordner für erledigte Originale wählen", ErledigtOrdner) is { } p)
            ErledigtOrdner = p;
    }

    [RelayCommand]
    private void AbsenderHinzufuegen() => Absender.Add(new AbsenderZeile());

    [RelayCommand]
    private void AbsenderEntfernen(AbsenderZeile zeile) => Absender.Remove(zeile);

    [RelayCommand]
    private void OrdnerOeffnen() => Oeffne(_d.Pfade.Basis);

    [RelayCommand]
    private void ProtokollOeffnen()
    {
        if (File.Exists(_d.Pfade.Protokoll))
            Oeffne(_d.Pfade.Protokoll);
    }

    private static void Oeffne(string pfad)
    {
        try
        {
            Process.Start(new ProcessStartInfo(pfad) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    [RelayCommand]
    private void Speichern()
    {
        var e = _d.Einstellungen.Kopie();
        e.Eingangsordner = Eingangsordner.Trim();
        e.Zielordner = Zielordner.Trim();
        e.ErledigtOrdner = ErledigtOrdner.Trim();
        e.Testbetrieb = Testbetrieb;
        e.OcrTextebene = OcrTextebene;
        e.LeereSeitenVorschlagen = LeereSeitenVorschlagen;
        if (e.Pruefe() is { } fehler)
        {
            Fehler = fehler;
            Bereich = 0;
            return;
        }
        var doppelt = Absender.Where(a => a.Name.Trim().Length > 0)
            .GroupBy(a => AbsenderVerzeichnis.Vereinfache(a.Name)).FirstOrDefault(g => g.Count() > 1);
        if (doppelt != null)
        {
            Fehler = $"Der Absender „{doppelt.First().Name.Trim()}“ ist doppelt eingetragen.";
            Bereich = 1;
            return;
        }
        try
        {
            e.Speichern(_d.Pfade.Einstellungen);
            _d.Einstellungen = e;
            _d.Absender.Eintraege.Clear();
            foreach (var a in Absender.Where(a => a.Name.Trim().Length > 0))
            {
                _d.Absender.Eintraege.Add(new AbsenderEintrag
                {
                    Name = a.Name.Trim(),
                    Schreibweisen = a.Schreibweisen.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList(),
                });
            }
            _d.Absender.Speichern();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fehler = $"Speichern fehlgeschlagen: {ex.Message}";
            return;
        }
        Fertig?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Abbrechen() => Fertig?.Invoke(this, EventArgs.Empty);
}
