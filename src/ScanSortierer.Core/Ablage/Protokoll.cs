using System.Globalization;

namespace ScanSortierer.Core.Ablage;

/// <summary>Protokolldatei im Konfigurationsordner: was wann gespeichert, verschoben oder gelöscht wurde.</summary>
public sealed class Protokoll
{
    private readonly object _sperre = new();

    public Protokoll(string pfad)
    {
        Pfad = pfad;
    }

    public string Pfad { get; }

    public void Schreibe(string aktion, string text)
    {
        var zeile = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}  {aktion,-12} {text}{Environment.NewLine}";
        lock (_sperre)
        {
            try
            {
                File.AppendAllText(Pfad, zeile);
            }
            catch (IOException)
            {
                // Das Protokoll darf die Arbeit nie blockieren.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public string LeseEnde(int maxZeilen = 1000)
    {
        lock (_sperre)
        {
            if (!File.Exists(Pfad))
                return "";
            var zeilen = File.ReadAllLines(Pfad);
            return string.Join(Environment.NewLine, zeilen.Skip(Math.Max(0, zeilen.Length - maxZeilen)));
        }
    }
}
