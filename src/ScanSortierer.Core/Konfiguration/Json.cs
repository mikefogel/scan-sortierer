using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ScanSortierer.Core.Konfiguration;

/// <summary>Gemeinsame JSON-Einstellungen: lesbar, von Hand editierbar, Umlaute unverändert.</summary>
public static partial class Json
{
    public static readonly JsonSerializerOptions Optionen = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static readonly JsonSerializerOptions StrengeOptionen = new(Optionen)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Übersetzt einen JSON-Fehler in eine verständliche deutsche Meldung.</summary>
    public static string FehlerText(JsonException ex)
    {
        var ort = ex.LineNumber is { } zeile ? $"Zeile {zeile + 1}" + (ex.BytePositionInLine is { } sp ? $", Spalte {sp + 1}" : "") + ": " : "";
        var meldung = ex.Message;
        if (meldung.Contains("could not be mapped", StringComparison.Ordinal))
        {
            var name = NameInAnfuehrung().Match(meldung);
            return $"{ort}Unbekannte Eigenschaft „{(name.Success ? name.Groups[1].Value : "?")}“.";
        }
        if (meldung.Contains("could not be converted", StringComparison.Ordinal))
            return $"{ort}Ein Wert hat den falschen Typ (z. B. Text statt true/false oder Liste).";
        if (meldung.Contains("is an invalid start of a value", StringComparison.Ordinal)
            || meldung.Contains("is invalid after", StringComparison.Ordinal)
            || meldung.Contains("expected", StringComparison.OrdinalIgnoreCase))
            return $"{ort}Ungültige JSON-Syntax (fehlt ein Komma, eine Klammer oder ein Anführungszeichen?).";
        return $"{ort}Ungültiges JSON ({meldung}).";
    }

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex NameInAnfuehrung();
}
