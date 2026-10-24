using System.Globalization;
using System.Text.RegularExpressions;

namespace ScanSortierer.Core.Vorlagen;

/// <summary>Einlesen und Formatieren von Datums- und Betragswerten (deutsches Format).</summary>
public static partial class Werte
{
    private static readonly NumberFormatInfo Zahlformat = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = [3],
        NumberDecimalDigits = 2,
    };

    private static readonly string[] Monate =
        ["januar", "februar", "märz", "april", "mai", "juni", "juli", "august", "september", "oktober", "november", "dezember"];

    public static string FormatiereDatum(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    public static string DatumFuerDateiname(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Liest ein Datum: 14.10.2026, 14.10.26, 2026-10-14, 14. Oktober 2026.</summary>
    public static bool LeseDatum(string? text, out DateOnly datum)
    {
        datum = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var t = text.Trim();
        var m = DatumPunkt().Match(t);
        if (m.Success && m.Index == 0 && m.Length == t.Length)
            return Baue(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value, out datum);
        m = DatumIso().Match(t);
        if (m.Success && m.Length == t.Length)
            return Baue(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, out datum);
        m = DatumText().Match(t);
        if (m.Success && m.Length == t.Length)
            return Baue(m.Groups[3].Value, MonatAusName(m.Groups[2].Value).ToString(CultureInfo.InvariantCulture), m.Groups[1].Value, out datum);
        return false;
    }

    /// <summary>Alle Datumsangaben in einem Text mit Position.</summary>
    public static IEnumerable<(DateOnly Datum, int Index, int Laenge)> FindeDaten(string text)
    {
        foreach (Match m in DatumPunkt().Matches(text))
            if (Baue(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value, out var d))
                yield return (d, m.Index, m.Length);
        foreach (Match m in DatumIso().Matches(text))
            if (Baue(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, out var d))
                yield return (d, m.Index, m.Length);
        foreach (Match m in DatumText().Matches(text))
            if (Baue(m.Groups[3].Value, MonatAusName(m.Groups[2].Value).ToString(CultureInfo.InvariantCulture), m.Groups[1].Value, out var d))
                yield return (d, m.Index, m.Length);
    }

    public static string FormatiereBetrag(decimal betrag) => betrag.ToString("N2", Zahlformat) + " €";

    /// <summary>Liest einen Betrag: 49,90 · 49,90 € · 1.234,56 EUR · 49.90 · 49 €.</summary>
    public static bool LeseBetrag(string? text, out decimal betrag)
    {
        betrag = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var t = text.Trim();
        t = WaehrungZeichen().Replace(t, "").Trim();
        if (t.Length == 0)
            return false;
        // Deutsches Format mit Komma als Dezimaltrenner.
        if (DeutscheZahl().IsMatch(t))
            return decimal.TryParse(t, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Zahlformat, out betrag);
        // Punkt als Dezimaltrenner (z. B. 49.90), aber nicht 1.234 (Tausender).
        if (PunktZahl().IsMatch(t))
            return decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out betrag);
        return false;
    }

    /// <summary>Alle Beträge in einem Text (Format 1.234,56) mit Kennzeichen, ob eine Währung dabeistand.</summary>
    public static IEnumerable<(decimal Betrag, int Index, bool MitWaehrung)> FindeBetraege(string text)
    {
        foreach (Match m in BetragImText().Matches(text))
        {
            var zahl = m.Groups["zahl"].Value.Replace(" ", "");
            if (decimal.TryParse(zahl, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Zahlformat, out var b))
                yield return (b, m.Index, m.Groups["vor"].Success || m.Groups["nach"].Success);
        }
    }

    private static int MonatAusName(string name)
    {
        var n = name.ToLowerInvariant().TrimEnd('.').Replace("ae", "ä");
        for (var i = 0; i < Monate.Length; i++)
            if (Monate[i].StartsWith(n, StringComparison.Ordinal) && n.Length >= 3)
                return i + 1;
        return 0;
    }

    private static bool Baue(string jahr, string monat, string tag, out DateOnly datum)
    {
        datum = default;
        if (!int.TryParse(jahr, out var j) || !int.TryParse(monat, out var mo) || !int.TryParse(tag, out var t))
            return false;
        if (jahr.Length == 2)
            j += j > 70 ? 1900 : 2000;
        if (j < 1900 || j > 2200 || mo < 1 || mo > 12 || t < 1 || t > DateTime.DaysInMonth(j, mo))
            return false;
        datum = new DateOnly(j, mo, t);
        return true;
    }

    [GeneratedRegex(@"(?<!\d)(\d{1,2})\.\s?(\d{1,2})\.\s?(\d{4}|\d{2})(?!\d)")]
    private static partial Regex DatumPunkt();

    [GeneratedRegex(@"(?<!\d)(\d{4})-(\d{2})-(\d{2})(?!\d)")]
    private static partial Regex DatumIso();

    [GeneratedRegex(@"(?<!\d)(\d{1,2})\.?\s*(Januar|Jan|Februar|Feb|März|Maerz|Mär|April|Apr|Mai|Juni|Jun|Juli|Jul|August|Aug|September|Sept|Sep|Oktober|Okt|November|Nov|Dezember|Dez)\.?\s+(\d{4})(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex DatumText();

    [GeneratedRegex(@"(€|EUR|Euro)", RegexOptions.IgnoreCase)]
    private static partial Regex WaehrungZeichen();

    [GeneratedRegex(@"^-?(\d{1,3}(\.\d{3})+|\d+)(,\d{1,2})?$")]
    private static partial Regex DeutscheZahl();

    [GeneratedRegex(@"^-?\d+\.\d{1,2}$")]
    private static partial Regex PunktZahl();

    [GeneratedRegex(@"(?<vor>(€|EUR)\s*)?(?<![\d.,])(?<zahl>-?(\d{1,3}(\.\d{3})+|\d+),\d{2})(?![\d,])(?<nach>\s*(€|EUR|Euro))?", RegexOptions.IgnoreCase)]
    private static partial Regex BetragImText();
}
