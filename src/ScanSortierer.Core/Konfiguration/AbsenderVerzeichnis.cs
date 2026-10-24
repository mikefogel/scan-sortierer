using System.Text.Json;
using System.Text.RegularExpressions;
using ScanSortierer.Core.Ablage;

namespace ScanSortierer.Core.Konfiguration;

public sealed class AbsenderEintrag
{
    /// <summary>Einheitlicher Name, z. B. „Telekom“.</summary>
    public string Name { get; set; } = "";

    /// <summary>Weitere Schreibweisen, die auf <see cref="Name"/> abgebildet werden.</summary>
    public List<string> Schreibweisen { get; set; } = [];
}

/// <summary>Gemerkte Absender mit ihren Schreibweisen (absender.json im Konfigurationsordner).</summary>
public sealed partial class AbsenderVerzeichnis
{
    private readonly string? _pfad;

    public AbsenderVerzeichnis(string? pfad = null, IEnumerable<AbsenderEintrag>? eintraege = null)
    {
        _pfad = pfad;
        if (eintraege != null)
            Eintraege.AddRange(eintraege);
    }

    public List<AbsenderEintrag> Eintraege { get; } = [];

    public static AbsenderVerzeichnis Laden(string pfad, out string? warnung)
    {
        warnung = null;
        var v = new AbsenderVerzeichnis(pfad);
        if (!File.Exists(pfad))
            return v;
        try
        {
            var liste = JsonSerializer.Deserialize<List<AbsenderEintrag>>(File.ReadAllText(pfad), Json.Optionen) ?? [];
            v.Eintraege.AddRange(liste.Where(e => !string.IsNullOrWhiteSpace(e.Name)));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            warnung = $"Die gemerkten Absender ({pfad}) konnten nicht gelesen werden: " +
                      (ex is JsonException je ? Json.FehlerText(je) : ex.Message);
        }
        return v;
    }

    public void Speichern()
    {
        if (_pfad is null)
            return;
        var sortiert = Eintraege.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        SichereDateien.ErsetzeAtomar(_pfad, JsonSerializer.Serialize(sortiert, Json.Optionen));
    }

    public IReadOnlyList<string> Namen() =>
        Eintraege.Select(e => e.Name).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Bildet eine Schreibweise auf den einheitlichen Namen ab (oder liefert den Wert unverändert).</summary>
    public string Normalisiere(string wert)
    {
        var w = Vereinfache(wert);
        if (w.Length == 0)
            return wert.Trim();
        foreach (var e in Eintraege)
        {
            if (Vereinfache(e.Name) == w || e.Schreibweisen.Any(s => Vereinfache(s) == w))
                return e.Name;
        }
        return wert.Trim();
    }

    /// <summary>Merkt sich einen Absender. Eine abweichende erkannte Schreibweise wird zugeordnet.</summary>
    /// <returns>True, wenn sich etwas geändert hat.</returns>
    public bool Merke(string name, string? erkannteSchreibweise = null)
    {
        name = name.Trim();
        if (name.Length == 0)
            return false;
        var geaendert = false;
        var eintrag = Eintraege.FirstOrDefault(e => Vereinfache(e.Name) == Vereinfache(name));
        if (eintrag is null)
        {
            eintrag = new AbsenderEintrag { Name = name };
            Eintraege.Add(eintrag);
            geaendert = true;
        }
        if (!string.IsNullOrWhiteSpace(erkannteSchreibweise))
        {
            var s = erkannteSchreibweise.Trim();
            var vs = Vereinfache(s);
            var bekannt = Eintraege.Any(e => Vereinfache(e.Name) == vs || e.Schreibweisen.Any(x => Vereinfache(x) == vs));
            if (!bekannt)
            {
                eintrag.Schreibweisen.Add(s);
                geaendert = true;
            }
        }
        return geaendert;
    }

    /// <summary>Alle Suchbegriffe: einheitlicher Name und Schreibweisen, jeweils mit dem Zielnamen.</summary>
    public IEnumerable<(string Suchbegriff, string Name)> Suchbegriffe()
    {
        foreach (var e in Eintraege)
        {
            yield return (e.Name, e.Name);
            foreach (var s in e.Schreibweisen)
                yield return (s, e.Name);
        }
    }

    public static string Vereinfache(string s) => Leerraum().Replace(s.Trim(), " ").ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Leerraum();
}
