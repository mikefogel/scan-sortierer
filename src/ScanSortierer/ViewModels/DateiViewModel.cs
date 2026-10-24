using CommunityToolkit.Mvvm.ComponentModel;
using ScanSortierer.Core.Pdf;

namespace ScanSortierer.ViewModels;

public enum DateiStatus
{
    Offen,
    Erledigt,
    Fehlerhaft,
}

/// <summary>Eine Scan-Datei im Eingangsordner mit ihrem Bearbeitungsstand.</summary>
public sealed partial class DateiViewModel : ObservableObject
{
    public DateiViewModel(string pfad)
    {
        Pfad = pfad;
    }

    public string Pfad { get; }
    public string Name => Path.GetFileName(Pfad);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IstErledigt), nameof(IstFehlerhaft), nameof(Anzeige))]
    public partial DateiStatus Status { get; set; }

    [ObservableProperty]
    public partial string Hinweis { get; set; } = "";

    public bool IstErledigt => Status == DateiStatus.Erledigt;
    public bool IstFehlerhaft => Status == DateiStatus.Fehlerhaft;

    public string Anzeige => Status switch
    {
        DateiStatus.Erledigt => "✓ " + Name,
        DateiStatus.Fehlerhaft => "⚠ " + Name,
        _ => Name,
    };

    /// <summary>Noch nicht verteilte Seiten (null, solange die Datei nicht geöffnet wurde).</summary>
    public List<SeiteViewModel>? Seiten { get; set; }

    public int SeitenGesamt { get; set; }
    public int GespeicherteDokumente { get; set; }

    /// <summary>Geöffnete Dokumente (nur solange die Datei aktiv ist).</summary>
    public PdfiumDokument? Pdfium { get; set; }

    public PdfQuelle? Quelle { get; set; }

    public void Schliessen()
    {
        Pdfium?.Dispose();
        Pdfium = null;
        Quelle = null;
        if (Seiten is null)
            return;
        foreach (var s in Seiten)
        {
            (s.Vorschau as IDisposable)?.Dispose();
            s.Vorschau = null;
        }
    }
}
