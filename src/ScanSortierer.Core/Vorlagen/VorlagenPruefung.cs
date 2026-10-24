using System.Text.RegularExpressions;

namespace ScanSortierer.Core.Vorlagen;

/// <summary>Prüft Vorlagen und liefert verständliche Fehlermeldungen.</summary>
public static partial class VorlagenPruefung
{
    public static List<string> Pruefe(Vorlage v)
    {
        var fehler = new List<string>();
        if (string.IsNullOrWhiteSpace(v.Name))
            fehler.Add("Der Name der Vorlage fehlt.");
        if (v.Felder is null)
        {
            fehler.Add("Die Liste „felder“ fehlt.");
            return fehler;
        }

        var namen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < v.Felder.Count; i++)
        {
            var f = v.Felder[i];
            if (f is null)
            {
                fehler.Add($"Feld {i + 1} ist leer.");
                continue;
            }
            var bez = string.IsNullOrWhiteSpace(f.Name) ? $"Feld {i + 1}" : $"Feld „{f.Name}“";
            if (string.IsNullOrWhiteSpace(f.Name))
                fehler.Add($"{bez}: Der Name fehlt.");
            else if (!GueltigerFeldname().IsMatch(f.Name))
                fehler.Add($"{bez}: Der Name darf nur Buchstaben, Ziffern und _ enthalten und muss mit einem Buchstaben beginnen.");
            else if (f.Name.Equals(SonderFelder.Typ, StringComparison.OrdinalIgnoreCase)
                     || f.Name.Equals(SonderFelder.Stichworte, StringComparison.OrdinalIgnoreCase))
                fehler.Add($"{bez}: Der Name „{f.Name}“ ist reserviert.");
            else if (!namen.Add(f.Name))
                fehler.Add($"{bez}: Der Name kommt mehrfach vor.");

            if (!FeldTyp.Alle.Contains(f.Typ))
                fehler.Add($"{bez}: Unbekannter Typ „{f.Typ}“ (erlaubt: {string.Join(", ", FeldTyp.Alle)}).");
            if (f.Typ == FeldTyp.Auswahl && !f.IstAbsender && (f.Werte is null || f.Werte.Count(w => !string.IsNullOrWhiteSpace(w)) == 0))
                fehler.Add($"{bez}: Bei Typ „auswahl“ muss die Liste „werte“ mindestens einen Wert enthalten.");
            if (f.Typ == FeldTyp.Betrag && f.ImDateinamen)
                fehler.Add($"{bez}: Beträge erscheinen nie im Dateinamen, bitte „imDateinamen“ auf false setzen.");
            if (string.Equals(f.Name, SonderFelder.Datum, StringComparison.OrdinalIgnoreCase) && f.Typ != FeldTyp.Datum)
                fehler.Add($"{bez}: Das Briefdatum muss den Typ „datum“ haben.");
            foreach (var m in f.Muster ?? [])
            {
                try
                {
                    _ = new Regex(m, RegexOptions.None, TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException ex)
                {
                    fehler.Add($"{bez}: Das Muster „{m}“ ist kein gültiger regulärer Ausdruck ({ex.Message}).");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(v.Dateiname))
        {
            fehler.Add("Das Dateinamen-Schema fehlt.");
        }
        else
        {
            var ohnePlatzhalter = Platzhalter().Replace(v.Dateiname, "");
            if (ohnePlatzhalter.Contains('{') || ohnePlatzhalter.Contains('}'))
                fehler.Add("Dateinamen-Schema: Geschweifte Klammern sind nicht ausgeglichen.");
            foreach (Match m in Platzhalter().Matches(v.Dateiname))
            {
                var name = m.Groups[1].Value;
                if (name.Equals(SonderFelder.Typ, StringComparison.OrdinalIgnoreCase))
                    continue;
                var feld = v.FindeFeld(name);
                if (feld is null)
                    fehler.Add($"Dateinamen-Schema: Der Platzhalter {{{name}}} passt zu keinem Feld.");
                else if (feld.Typ == FeldTyp.Betrag)
                    fehler.Add($"Dateinamen-Schema: Beträge ({{{name}}}) dürfen nicht im Dateinamen stehen.");
            }
        }

        if (v.Ablage is not ("flach" or ""))
            fehler.Add($"Ablage „{v.Ablage}“ wird noch nicht unterstützt (nur „flach“).");
        return fehler;
    }

    public static IEnumerable<string> PlatzhalterIn(string schema) =>
        Platzhalter().Matches(schema ?? "").Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"^\p{L}[\p{L}\p{Nd}_]*$")]
    private static partial Regex GueltigerFeldname();

    [GeneratedRegex(@"\{([^{}]*)\}")]
    internal static partial Regex Platzhalter();
}
