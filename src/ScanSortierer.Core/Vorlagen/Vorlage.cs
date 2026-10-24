using System.Text.Json.Serialization;

namespace ScanSortierer.Core.Vorlagen;

public static class FeldTyp
{
    public const string Text = "text";
    public const string Datum = "datum";
    public const string Betrag = "betrag";
    public const string Auswahl = "auswahl";

    public static readonly string[] Alle = [Text, Datum, Betrag, Auswahl];

    public static string Anzeige(string typ) => typ switch
    {
        Text => "Text",
        Datum => "Datum",
        Betrag => "Betrag",
        Auswahl => "Auswahl",
        _ => typ,
    };
}

/// <summary>Eine Dokumentvorlage (eine JSON-Datei je Dokumenttyp).</summary>
public sealed class Vorlage
{
    /// <summary>Name im Dropdown, zugleich Wert des Platzhalters {typ}.</summary>
    public string Name { get; set; } = "";

    /// <summary>Dateinamen-Schema, z. B. „{datum}_{absender}_{typ}“.</summary>
    public string Dateiname { get; set; } = "{datum}_{absender}_{typ}";

    /// <summary>Ablage: vorläufig immer „flach“ im Zielordner.</summary>
    public string Ablage { get; set; } = "flach";

    public List<Feld> Felder { get; set; } = [];

    /// <summary>Datei, aus der die Vorlage geladen wurde (wird nicht gespeichert).</summary>
    [JsonIgnore]
    public string? Pfad { get; set; }

    public Feld? FindeFeld(string name) =>
        Felder.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class Feld
{
    /// <summary>Technischer Name, wird als Platzhalter verwendet (z. B. „absender“ → {absender}).</summary>
    public string Name { get; set; } = "";

    /// <summary>Anzeigename in der Maske, z. B. „Briefdatum“. Leer: Name mit großem Anfangsbuchstaben.</summary>
    public string Bezeichnung { get; set; } = "";

    public string Typ { get; set; } = FeldTyp.Text;
    public bool Pflicht { get; set; }
    public bool ImDateinamen { get; set; } = true;

    /// <summary>Werte bei Typ „auswahl“.</summary>
    public List<string> Werte { get; set; } = [];

    /// <summary>Erkennung: Schlüsselwörter, hinter denen der Wert steht (z. B. „Rechnungsnummer“).</summary>
    public List<string> Schluesselwoerter { get; set; } = [];

    /// <summary>Erkennung: reguläre Ausdrücke. Die erste Klammergruppe (oder der ganze Treffer) ist der Wert.</summary>
    public List<string> Muster { get; set; } = [];

    [JsonIgnore]
    public string AnzeigeName => !string.IsNullOrWhiteSpace(Bezeichnung)
        ? Bezeichnung
        : Name.Length == 0 ? "" : char.ToUpperInvariant(Name[0]) + Name[1..];

    [JsonIgnore]
    public bool IstAbsender => string.Equals(Name, SonderFelder.Absender, StringComparison.OrdinalIgnoreCase);
}

public static class SonderFelder
{
    /// <summary>Briefdatum, immer Pflicht.</summary>
    public const string Datum = "datum";

    /// <summary>Absender: Vorschläge aus den gemerkten Absendern.</summary>
    public const string Absender = "absender";

    /// <summary>Platzhalter für den Dokumenttyp (Vorlagenname).</summary>
    public const string Typ = "typ";

    /// <summary>Freies Feld „Stichworte/Notiz“, das jedes Dokument hat (nur Metadaten).</summary>
    public const string Stichworte = "stichworte";
}
