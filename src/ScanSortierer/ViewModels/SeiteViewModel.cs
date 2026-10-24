using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ScanSortierer.Core.Ocr;

namespace ScanSortierer.ViewModels;

/// <summary>Eine noch nicht verteilte Seite der aktuellen Scan-Datei.</summary>
public sealed partial class SeiteViewModel : ObservableObject
{
    public SeiteViewModel(int scanIndex)
    {
        ScanIndex = scanIndex;
    }

    /// <summary>Position im Original-Scan (0-basiert).</summary>
    public int ScanIndex { get; }

    /// <summary>Nutzerdrehung in Grad im Uhrzeigersinn.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Untertitel))]
    public partial int Drehung { get; set; }

    [ObservableProperty]
    public partial Bitmap? Vorschau { get; set; }

    /// <summary>Ergebnis der Leerseitenerkennung (Vorschlag).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstLeer), nameof(Untertitel))]
    public partial bool LeerVorschlag { get; set; }

    /// <summary>Vom Nutzer festgelegt (null = Vorschlag gilt).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstLeer), nameof(Untertitel))]
    public partial bool? LeerManuell { get; set; }

    public bool IstLeer => LeerManuell ?? LeerVorschlag;

    [ObservableProperty]
    public partial bool IstAusgewaehlt { get; set; }

    [ObservableProperty]
    public partial int AuswahlNummer { get; set; }

    [ObservableProperty]
    public partial bool IstFokus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstGrenze))]
    public partial bool GrenzeVorschlag { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstGrenze))]
    public partial bool? GrenzeManuell { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstGrenze))]
    public partial bool IstErste { get; set; }

    /// <summary>Vor dieser Seite beginnt ein neues Dokument (wird als Linie angezeigt).</summary>
    public bool IstGrenze => !IstErste && (GrenzeManuell ?? GrenzeVorschlag);

    /// <summary>Beginnt ein Dokument (auch die erste Seite).</summary>
    public bool BeginntDokument => IstErste || (GrenzeManuell ?? GrenzeVorschlag);

    [ObservableProperty]
    public partial OcrErgebnis? Ocr { get; set; }

    [ObservableProperty]
    public partial bool OcrLaeuft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Untertitel))]
    public partial bool AutomatischGedreht { get; set; }

    /// <summary>Laufende Texterkennung (zum Abwarten vor dem Speichern).</summary>
    public Task? OcrAufgabe { get; set; }

    /// <summary>Drehung, für die die Texterkennung zuletzt gestartet wurde.</summary>
    public int? OcrAngefordertFuer { get; set; }

    /// <summary>Wurde bereits versucht, eine schlecht lesbare Seite umzudrehen?</summary>
    public bool DrehversuchErfolgt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VorschauHoehe))]
    public partial double VorschauBreite { get; set; } = 170;

    public double VorschauHoehe => Math.Round(VorschauBreite * 1.414);

    public string Bezeichnung => $"Seite {ScanIndex + 1}";

    public string Untertitel =>
        Bezeichnung
        + (IstLeer ? (LeerManuell == true ? " · leer" : " · leer (Vorschlag)") : "")
        + (AutomatischGedreht ? " · gedreht" : "");
}
