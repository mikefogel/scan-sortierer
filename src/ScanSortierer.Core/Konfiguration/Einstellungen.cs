using System.Text.Json;
using ScanSortierer.Core.Ablage;

namespace ScanSortierer.Core.Konfiguration;

public sealed class Einstellungen
{
    public string Eingangsordner { get; set; } = "";
    public string Zielordner { get; set; } = "";

    /// <summary>Ordner für erledigte Originale. Leer bedeutet: Unterordner „erledigt“ im Eingangsordner.</summary>
    public string ErledigtOrdner { get; set; } = "";

    /// <summary>Testbetrieb: Originale werden nie verschoben. Beim ersten Start AN.</summary>
    public bool Testbetrieb { get; set; } = true;

    public bool OcrTextebene { get; set; } = true;
    public bool LeereSeitenVorschlagen { get; set; } = true;
    public string LetzteVorlage { get; set; } = "Rechnung";
    public double VorschauGroesse { get; set; } = 170;

    public string ErledigtOrdnerEffektiv() =>
        !string.IsNullOrWhiteSpace(ErledigtOrdner)
            ? ErledigtOrdner
            : string.IsNullOrWhiteSpace(Eingangsordner) ? "" : Path.Combine(Eingangsordner, "erledigt");

    public string ZielordnerEffektiv() =>
        string.IsNullOrWhiteSpace(Zielordner) ? AppPfade.DokumenteOrdner() : Zielordner;

    public Einstellungen Kopie() => (Einstellungen)MemberwiseClone();

    /// <summary>Lädt die Einstellungen. Bei fehlender Datei Standardwerte, bei defekter Datei Standardwerte und Warnung.</summary>
    public static Einstellungen Laden(string pfad, out string? warnung)
    {
        warnung = null;
        if (!File.Exists(pfad))
            return new Einstellungen { Zielordner = AppPfade.DokumenteOrdner() };
        try
        {
            var e = JsonSerializer.Deserialize<Einstellungen>(File.ReadAllText(pfad), Json.Optionen);
            if (e is null)
                throw new JsonException("leer");
            return e;
        }
        catch (JsonException ex)
        {
            warnung = $"Die Einstellungen ({pfad}) sind fehlerhaft und wurden nicht geladen: {Json.FehlerText(ex)} Es gelten Standardwerte.";
        }
        catch (IOException ex)
        {
            warnung = $"Die Einstellungen konnten nicht gelesen werden: {ex.Message}";
        }
        return new Einstellungen { Zielordner = AppPfade.DokumenteOrdner() };
    }

    public void Speichern(string pfad) =>
        SichereDateien.ErsetzeAtomar(pfad, JsonSerializer.Serialize(this, Json.Optionen));

    /// <summary>Prüft die Ordnerangaben. Liefert eine Fehlermeldung oder null.</summary>
    public string? Pruefe()
    {
        static string Norm(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
        if (!string.IsNullOrWhiteSpace(Eingangsordner) && !string.IsNullOrWhiteSpace(Zielordner)
            && string.Equals(Norm(Eingangsordner), Norm(Zielordner), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return "Eingangs- und Zielordner dürfen nicht gleich sein, sonst würden fertige Dokumente erneut eingelesen.";
        return null;
    }
}
