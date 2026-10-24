using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ScanSortierer.Core.Vorlagen;

namespace ScanSortierer.ViewModels;

/// <summary>Ein Eingabefeld der Dokumentmaske.</summary>
public sealed partial class FeldViewModel : ObservableObject
{
    private bool _setztIntern;

    public FeldViewModel(Feld feld, IEnumerable<string> optionen)
    {
        Feld = feld;
        Optionen = new ObservableCollection<string>(optionen);
    }

    public Feld Feld { get; }
    public string Name => Feld.Name;
    public string Bezeichnung => Feld.AnzeigeName + (Feld.Pflicht ? " *" : "");
    public bool IstAuswahl => Feld.Typ == FeldTyp.Auswahl;
    public bool IstEingabe => !IstAuswahl;
    public ObservableCollection<string> Optionen { get; }

    public string Platzhalter => Feld.Typ switch
    {
        FeldTyp.Datum => "TT.MM.JJJJ",
        FeldTyp.Betrag => "0,00 €",
        _ => Feld.Pflicht ? "Pflichtfeld" : "",
    };

    [ObservableProperty]
    public partial string Wert { get; set; } = "";

    /// <summary>Wert wurde per Texterkennung vorbelegt.</summary>
    [ObservableProperty]
    public partial bool Erkannt { get; set; }

    /// <summary>Der Nutzer hat den Wert selbst geändert (wird dann nicht mehr automatisch überschrieben).</summary>
    public bool Bearbeitet { get; set; }

    /// <summary>
    /// Zuletzt erkannte Schreibweise (bleibt auch nach Änderung durch den Nutzer erhalten, damit sie als
    /// Schreibweise des eingetragenen Absenders gemerkt werden kann).
    /// </summary>
    public string? ErkannteSchreibweise { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatFehler))]
    public partial string? Fehler { get; set; }

    public bool HatFehler => !string.IsNullOrEmpty(Fehler);

    public event EventHandler? WertGeaendert;

    partial void OnWertChanged(string value)
    {
        if (!_setztIntern)
        {
            Bearbeitet = true;
            Erkannt = false;
        }
        Fehler = null;
        WertGeaendert?.Invoke(this, EventArgs.Empty);
    }

    public void SetzeErkannt(string wert, string? rohtext)
    {
        _setztIntern = true;
        try
        {
            Wert = wert;
            Erkannt = true;
            ErkannteSchreibweise = rohtext ?? wert;
        }
        finally
        {
            _setztIntern = false;
        }
    }

    public void SetzeLeer()
    {
        _setztIntern = true;
        try
        {
            Wert = "";
            Erkannt = false;
            ErkannteSchreibweise = null;
            Bearbeitet = false;
        }
        finally
        {
            _setztIntern = false;
        }
    }

    /// <summary>Vereinheitlicht die Eingabe (z. B. 14.10.26 → 14.10.2026, 49,9 → 49,90 €).</summary>
    public void Normalisiere()
    {
        var w = Wert.Trim();
        if (w.Length == 0)
            return;
        string? neu = Feld.Typ switch
        {
            FeldTyp.Datum when Werte.LeseDatum(w, out var d) => Werte.FormatiereDatum(d),
            FeldTyp.Betrag when Werte.LeseBetrag(w, out var b) => Werte.FormatiereBetrag(b),
            _ => null,
        };
        if (neu != null && neu != Wert)
        {
            var erkannt = Erkannt;
            var bearbeitet = Bearbeitet;
            _setztIntern = true;
            try
            {
                Wert = neu;
            }
            finally
            {
                _setztIntern = false;
            }
            Erkannt = erkannt;
            Bearbeitet = bearbeitet;
        }
    }

    /// <summary>Prüft Pflicht und Format. Liefert true, wenn der Wert gültig ist.</summary>
    public bool Pruefe()
    {
        Normalisiere();
        var w = Wert.Trim();
        if (w.Length == 0)
        {
            Fehler = Feld.Pflicht ? "Pflichtfeld – bitte ausfüllen." : null;
            return !Feld.Pflicht;
        }
        Fehler = Feld.Typ switch
        {
            FeldTyp.Datum when !Werte.LeseDatum(w, out _) => "Bitte ein Datum im Format TT.MM.JJJJ eingeben.",
            FeldTyp.Betrag when !Werte.LeseBetrag(w, out _) => "Bitte einen Betrag wie 49,90 € eingeben.",
            _ => null,
        };
        return Fehler is null;
    }
}
