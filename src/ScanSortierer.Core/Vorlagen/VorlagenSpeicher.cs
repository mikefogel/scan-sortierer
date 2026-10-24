using System.Text;
using System.Text.Json;
using ScanSortierer.Core.Ablage;
using ScanSortierer.Core.Konfiguration;

namespace ScanSortierer.Core.Vorlagen;

/// <summary>Lädt und speichert die JSON-Vorlagen im Konfigurationsordner.</summary>
public sealed class VorlagenSpeicher
{
    public VorlagenSpeicher(string ordner)
    {
        Ordner = ordner;
    }

    public string Ordner { get; }

    /// <summary>Lädt alle Vorlagen. Fehlerhafte Dateien werden übersprungen und mit Meldung gemeldet.</summary>
    public (List<Vorlage> Vorlagen, List<string> Fehler) LadeAlle()
    {
        var vorlagen = new List<Vorlage>();
        var fehler = new List<string>();
        if (!Directory.Exists(Ordner))
            return (vorlagen, fehler);

        foreach (var datei in Directory.EnumerateFiles(Ordner, "*.json").OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var (vorlage, probleme) = LeseDatei(datei);
            if (vorlage is null)
            {
                fehler.Add($"Vorlage „{Path.GetFileName(datei)}“ wurde nicht geladen: {string.Join(" ", probleme)}");
                continue;
            }
            if (vorlagen.Any(x => string.Equals(x.Name, vorlage.Name, StringComparison.CurrentCultureIgnoreCase)))
            {
                fehler.Add($"Vorlage „{Path.GetFileName(datei)}“ wurde nicht geladen: Der Name „{vorlage.Name}“ ist schon vergeben.");
                continue;
            }
            vorlagen.Add(vorlage);
        }
        vorlagen.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return (vorlagen, fehler);
    }

    public static (Vorlage? Vorlage, List<string> Fehler) LeseDatei(string datei)
    {
        try
        {
            var (v, fehler) = AusJson(File.ReadAllText(datei));
            if (v != null)
                v.Pfad = datei;
            return (v, fehler);
        }
        catch (IOException ex)
        {
            return (null, [$"Datei nicht lesbar ({ex.Message})."]);
        }
        catch (UnauthorizedAccessException ex)
        {
            return (null, [$"Keine Leserechte ({ex.Message})."]);
        }
    }

    /// <summary>Liest eine Vorlage aus JSON und prüft sie.</summary>
    public static (Vorlage? Vorlage, List<string> Fehler) AusJson(string json)
    {
        Vorlage? v;
        try
        {
            v = JsonSerializer.Deserialize<Vorlage>(json, Json.StrengeOptionen);
        }
        catch (JsonException ex)
        {
            return (null, [Json.FehlerText(ex)]);
        }
        if (v is null)
            return (null, ["Die Datei enthält keine Vorlage."]);
        v.Felder ??= [];
        foreach (var f in v.Felder.Where(f => f != null))
        {
            f.Werte ??= [];
            f.Schluesselwoerter ??= [];
            f.Muster ??= [];
            f.Bezeichnung ??= "";
            // Das Briefdatum ist immer Pflicht.
            if (string.Equals(f.Name, SonderFelder.Datum, StringComparison.OrdinalIgnoreCase))
                f.Pflicht = true;
        }
        var fehler = VorlagenPruefung.Pruefe(v);
        return fehler.Count > 0 ? (null, fehler) : (v, fehler);
    }

    public static string ZuJson(Vorlage v) => JsonSerializer.Serialize(v, Json.Optionen);

    /// <summary>Speichert eine Vorlage (atomar). Neue Vorlagen bekommen einen Dateinamen aus ihrem Namen.</summary>
    public string Speichern(Vorlage v)
    {
        var fehler = VorlagenPruefung.Pruefe(v);
        if (fehler.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, fehler));
        Directory.CreateDirectory(Ordner);
        var pfad = v.Pfad;
        if (string.IsNullOrEmpty(pfad))
        {
            var basis = DateinameFuer(v.Name);
            pfad = Path.Combine(Ordner, basis + ".json");
            for (var n = 2; File.Exists(pfad); n++)
                pfad = Path.Combine(Ordner, $"{basis}_{n}.json");
        }
        SichereDateien.ErsetzeAtomar(pfad, ZuJson(v));
        v.Pfad = pfad;
        return pfad;
    }

    public static string DateinameFuer(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            switch (c)
            {
                case 'ä': sb.Append("ae"); break;
                case 'ö': sb.Append("oe"); break;
                case 'ü': sb.Append("ue"); break;
                case 'ß': sb.Append("ss"); break;
                default:
                    if (char.IsAsciiLetterOrDigit(c))
                        sb.Append(c);
                    else if (sb.Length > 0 && sb[^1] != '-')
                        sb.Append('-');
                    break;
            }
        }
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "vorlage" : s;
    }

    /// <summary>Kopiert die mitgelieferten Beispielvorlagen (nur beim ersten Start).</summary>
    public static void BeispieleKopieren(string ordner)
    {
        Directory.CreateDirectory(ordner);
        var asm = typeof(VorlagenSpeicher).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(r => r.StartsWith("Beispielvorlagen.", StringComparison.Ordinal)))
        {
            var ziel = Path.Combine(ordner, res["Beispielvorlagen.".Length..]);
            if (File.Exists(ziel))
                continue;
            using var s = asm.GetManifestResourceStream(res)!;
            using var r = new StreamReader(s, Encoding.UTF8);
            SichereDateien.ErsetzeAtomar(ziel, r.ReadToEnd());
        }
    }
}
