using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScanSortierer.Core.Vorlagen;

namespace ScanSortierer.ViewModels;

public sealed partial class FeldEditorViewModel : ObservableObject
{
    public static IReadOnlyList<string> Typen { get; } = FeldTyp.Alle;

    public event EventHandler<string?>? Geaendert;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Anzeige))]
    public partial string Bezeichnung { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypAnzeige), nameof(IstAuswahl))]
    public partial string Typ { get; set; } = FeldTyp.Text;

    [ObservableProperty]
    public partial bool Pflicht { get; set; }

    [ObservableProperty]
    public partial bool ImDateinamen { get; set; }

    [ObservableProperty]
    public partial string WerteText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErkennungKurz))]
    public partial string SchluesselwoerterText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErkennungKurz))]
    public partial string MusterText { get; set; } = "";

    public string Anzeige => string.IsNullOrWhiteSpace(Bezeichnung) ? Name : Bezeichnung;
    public string TypAnzeige => FeldTyp.Anzeige(Typ);
    public bool IstAuswahl => Typ == FeldTyp.Auswahl;

    public string ErkennungKurz
    {
        get
        {
            var teile = Zeilen(SchluesselwoerterText).Select(s => $"„{s}“").Concat(Zeilen(MusterText).Select(_ => "Muster")).ToList();
            if (teile.Count == 0)
                return Name == SonderFelder.Absender ? "gemerkte Absender, Briefkopf" : Name == SonderFelder.Datum ? "Briefdatum (automatisch)" : "–";
            var text = string.Join(", ", teile);
            return text.Length > 60 ? text[..60] + " …" : text;
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Anzeige) or nameof(TypAnzeige) or nameof(IstAuswahl) or nameof(ErkennungKurz))
            return;
        Geaendert?.Invoke(this, e.PropertyName);
    }

    public static FeldEditorViewModel Aus(Feld f) => new()
    {
        Name = f.Name,
        Bezeichnung = f.Bezeichnung,
        Typ = f.Typ,
        Pflicht = f.Pflicht,
        ImDateinamen = f.ImDateinamen,
        WerteText = string.Join(Environment.NewLine, f.Werte),
        SchluesselwoerterText = string.Join(Environment.NewLine, f.Schluesselwoerter),
        MusterText = string.Join(Environment.NewLine, f.Muster),
    };

    public Feld ZuFeld() => new()
    {
        Name = Name.Trim(),
        Bezeichnung = Bezeichnung.Trim(),
        Typ = Typ,
        Pflicht = Pflicht || Name.Trim().Equals(SonderFelder.Datum, StringComparison.OrdinalIgnoreCase),
        ImDateinamen = ImDateinamen,
        Werte = Typ == FeldTyp.Auswahl ? Zeilen(WerteText) : [],
        Schluesselwoerter = Zeilen(SchluesselwoerterText),
        Muster = Zeilen(MusterText),
    };

    internal static List<string> Zeilen(string text) =>
        text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
}

public sealed partial class VorlagenEditorViewModel : ObservableObject
{
    private readonly Dienste _d;
    private bool _laedt;
    private bool _jsonWirdGesetzt;
    private string? _pfad;

    public VorlagenEditorViewModel(Dienste dienste)
    {
        _d = dienste;
        LadeListe(null);
    }

    public Func<string, string, Task<bool>>? Frage { get; set; }

    public ObservableCollection<Vorlage> Liste { get; } = [];
    public ObservableCollection<FeldEditorViewModel> Felder { get; } = [];
    public ObservableCollection<string> Dateifehler { get; } = [];
    public bool HatDateifehler => Dateifehler.Count > 0;

    [ObservableProperty]
    public partial Vorlage? Auswahl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Ueberschrift))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Dateiname { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatFeld))]
    public partial FeldEditorViewModel? AktuellesFeld { get; set; }

    public bool HatFeld => AktuellesFeld != null;

    [ObservableProperty]
    public partial string JsonText { get; set; } = "";

    [ObservableProperty]
    public partial bool JsonBearbeitet { get; set; }

    [ObservableProperty]
    public partial string PruefText { get; set; } = "";

    [ObservableProperty]
    public partial bool Gueltig { get; set; }

    [ObservableProperty]
    public partial bool Geaendert { get; set; }

    [ObservableProperty]
    public partial string Pfad { get; set; } = "";

    [ObservableProperty]
    public partial bool HatVorlage { get; set; }

    public string Ueberschrift => string.IsNullOrWhiteSpace(Name) ? "Neue Vorlage" : $"Vorlage: {Name}";

    private void LadeListe(string? auswaehlen)
    {
        var (vorlagen, fehler) = _d.Vorlagen.LadeAlle();
        Liste.Clear();
        foreach (var v in vorlagen)
            Liste.Add(v);
        Dateifehler.Clear();
        foreach (var f in fehler)
            Dateifehler.Add(f);
        OnPropertyChanged(nameof(HatDateifehler));
        var ziel = Liste.FirstOrDefault(v => v.Name == auswaehlen) ?? Liste.FirstOrDefault();
        _laedt = true;
        Auswahl = ziel;
        _laedt = false;
        Lade(ziel);
    }

    partial void OnAuswahlChanged(Vorlage? oldValue, Vorlage? newValue)
    {
        if (_laedt)
            return;
        _ = WechselnAsync(oldValue, newValue);
    }

    private async Task WechselnAsync(Vorlage? alt, Vorlage? neu)
    {
        if (Geaendert && Frage != null && !await Frage("Ungespeicherte Änderungen", "Die Änderungen an der aktuellen Vorlage gehen verloren. Trotzdem wechseln?"))
        {
            _laedt = true;
            Auswahl = alt;
            _laedt = false;
            return;
        }
        Lade(neu);
    }

    private void Lade(Vorlage? v)
    {
        _laedt = true;
        try
        {
            foreach (var f in Felder)
                f.Geaendert -= FeldGeaendert;
            Felder.Clear();
            HatVorlage = v != null;
            _pfad = v?.Pfad;
            Pfad = v?.Pfad ?? "(noch nicht gespeichert)";
            Name = v?.Name ?? "";
            Dateiname = v?.Dateiname ?? "{datum}_{absender}_{typ}";
            foreach (var f in v?.Felder ?? [])
                FuegeHinzu(FeldEditorViewModel.Aus(f));
            AktuellesFeld = Felder.FirstOrDefault();
        }
        finally
        {
            _laedt = false;
        }
        Geaendert = false;
        Aktualisiere();
    }

    private void FuegeHinzu(FeldEditorViewModel f)
    {
        f.Geaendert += FeldGeaendert;
        Felder.Add(f);
    }

    private string _letzterName = "";

    partial void OnAktuellesFeldChanged(FeldEditorViewModel? value) => _letzterName = value?.Name ?? "";

    private void FeldGeaendert(object? sender, string? eigenschaft)
    {
        if (_laedt || sender is not FeldEditorViewModel f)
            return;
        if (eigenschaft == nameof(FeldEditorViewModel.ImDateinamen))
        {
            if (f.ImDateinamen && !VorlagenPruefung.PlatzhalterIn(Dateiname).Contains(f.Name, StringComparer.OrdinalIgnoreCase) && f.Name.Length > 0)
                Dateiname = Dateiname.Length == 0 ? $"{{{f.Name}}}" : $"{Dateiname}_{{{f.Name}}}";
            else if (!f.ImDateinamen)
                Dateiname = EntfernePlatzhalter(Dateiname, f.Name);
        }
        else if (eigenschaft == nameof(FeldEditorViewModel.Name) && f == AktuellesFeld)
        {
            if (_letzterName.Length > 0 && f.Name.Length > 0)
                Dateiname = Dateiname.Replace($"{{{_letzterName}}}", $"{{{f.Name}}}", StringComparison.OrdinalIgnoreCase);
            _letzterName = f.Name;
        }
        else if (eigenschaft == nameof(FeldEditorViewModel.Typ) && f.Typ == FeldTyp.Betrag && f.ImDateinamen)
        {
            f.ImDateinamen = false;
        }
        Geaendert = true;
        Aktualisiere();
    }

    private static string EntfernePlatzhalter(string schema, string name)
    {
        var p = Regex.Escape($"{{{name}}}");
        var s = Regex.Replace(schema, $@"[_\- ]?{p}", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, $@"{p}[_\- ]?", "", RegexOptions.IgnoreCase);
        return s.Trim('_', '-', ' ');
    }

    partial void OnNameChanged(string value) => Bearbeitet();
    partial void OnDateinameChanged(string value) => Bearbeitet();

    private void Bearbeitet()
    {
        if (_laedt)
            return;
        Geaendert = true;
        Aktualisiere();
    }

    partial void OnJsonTextChanged(string value)
    {
        if (!_jsonWirdGesetzt)
            JsonBearbeitet = true;
    }

    private Vorlage BaueVorlage() => new()
    {
        Name = Name.Trim(),
        Dateiname = Dateiname.Trim(),
        Felder = Felder.Select(f => f.ZuFeld()).ToList(),
        Pfad = _pfad,
    };

    private void Aktualisiere()
    {
        if (!HatVorlage)
        {
            PruefText = "";
            SetzeJson("");
            return;
        }
        var v = BaueVorlage();
        var fehler = VorlagenPruefung.Pruefe(v);
        if (fehler.Count == 0 && Liste.Any(x => x.Pfad != _pfad && string.Equals(x.Name, v.Name, StringComparison.CurrentCultureIgnoreCase)))
            fehler.Add($"Es gibt schon eine Vorlage mit dem Namen „{v.Name}“.");
        Gueltig = fehler.Count == 0;
        PruefText = Gueltig ? "✓ Vorlage ist gültig" : string.Join(Environment.NewLine, fehler.Select(f => "• " + f));
        SetzeJson(VorlagenSpeicher.ZuJson(v));
    }

    private void SetzeJson(string json)
    {
        _jsonWirdGesetzt = true;
        JsonText = json;
        JsonBearbeitet = false;
        _jsonWirdGesetzt = false;
    }

    [RelayCommand]
    private void JsonUebernehmen()
    {
        var (v, fehler) = VorlagenSpeicher.AusJson(JsonText);
        if (v is null)
        {
            Gueltig = false;
            PruefText = "JSON nicht übernommen:" + Environment.NewLine + string.Join(Environment.NewLine, fehler.Select(f => "• " + f));
            return;
        }
        v.Pfad = _pfad;
        var pfad = Pfad;
        Lade(v);
        Pfad = pfad;
        Geaendert = true;
    }

    [RelayCommand]
    private async Task NeueVorlageAsync()
    {
        if (Geaendert && Frage != null && !await Frage("Ungespeicherte Änderungen", "Die Änderungen an der aktuellen Vorlage gehen verloren. Trotzdem eine neue Vorlage anlegen?"))
            return;
        _laedt = true;
        Auswahl = null;
        _laedt = false;
        Lade(new Vorlage
        {
            Name = "",
            Dateiname = "{datum}_{absender}_{typ}",
            Felder =
            [
                new Feld { Name = SonderFelder.Absender, Bezeichnung = "Absender", Typ = FeldTyp.Auswahl, Pflicht = true, ImDateinamen = true },
                new Feld { Name = SonderFelder.Datum, Bezeichnung = "Briefdatum", Typ = FeldTyp.Datum, Pflicht = true, ImDateinamen = true },
            ],
        });
        Pfad = "(noch nicht gespeichert)";
        Geaendert = true;
    }

    [RelayCommand]
    private void FeldHinzufuegen()
    {
        var n = 1;
        while (Felder.Any(f => f.Name == $"feld{n}"))
            n++;
        var f = new FeldEditorViewModel { Name = $"feld{n}", Bezeichnung = $"Feld {n}", Typ = FeldTyp.Text };
        FuegeHinzu(f);
        AktuellesFeld = f;
        Geaendert = true;
        Aktualisiere();
    }

    [RelayCommand]
    private void FeldEntfernen()
    {
        if (AktuellesFeld is not { } f)
            return;
        var i = Felder.IndexOf(f);
        f.Geaendert -= FeldGeaendert;
        Felder.Remove(f);
        Dateiname = EntfernePlatzhalter(Dateiname, f.Name);
        AktuellesFeld = Felder.Count == 0 ? null : Felder[Math.Min(i, Felder.Count - 1)];
        Geaendert = true;
        Aktualisiere();
    }

    [RelayCommand]
    private void FeldHoch() => Verschiebe(-1);

    [RelayCommand]
    private void FeldRunter() => Verschiebe(1);

    private void Verschiebe(int r)
    {
        if (AktuellesFeld is not { } f)
            return;
        var i = Felder.IndexOf(f);
        var z = i + r;
        if (z < 0 || z >= Felder.Count)
            return;
        Felder.Move(i, z);
        AktuellesFeld = f;
        Geaendert = true;
        Aktualisiere();
    }

    [RelayCommand]
    private void Speichern()
    {
        Aktualisiere();
        if (!Gueltig)
            return;
        var v = BaueVorlage();
        try
        {
            _d.Vorlagen.Speichern(v);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            PruefText = "Speichern fehlgeschlagen: " + ex.Message;
            Gueltig = false;
            return;
        }
        Geaendert = false;
        LadeListe(v.Name);
        PruefText = $"✓ Gespeichert: {v.Pfad}";
    }

    [RelayCommand]
    private void Verwerfen()
    {
        var name = Auswahl?.Name;
        Geaendert = false;
        LadeListe(name);
    }

    [RelayCommand]
    private async Task LoeschenAsync()
    {
        if (_pfad is null || !File.Exists(_pfad))
        {
            Verwerfen();
            return;
        }
        if (Frage != null && !await Frage("Vorlage löschen", $"Die Vorlage „{Name}“ ({Path.GetFileName(_pfad)}) wirklich löschen? Bereits gespeicherte Dokumente bleiben unverändert."))
            return;
        try
        {
            File.Delete(_pfad);
            _d.Protokoll.Schreibe("VORLAGE", $"{_pfad} gelöscht");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PruefText = "Löschen fehlgeschlagen: " + ex.Message;
            return;
        }
        Geaendert = false;
        LadeListe(null);
    }
}
