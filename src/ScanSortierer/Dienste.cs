using ScanSortierer.Core.Ablage;
using ScanSortierer.Core.Konfiguration;
using ScanSortierer.Core.Ocr;
using ScanSortierer.Core.Vorlagen;

namespace ScanSortierer;

/// <summary>Gemeinsame Dienste und Konfiguration der Anwendung.</summary>
public sealed class Dienste
{
    private Dienste(AppPfade pfade, Einstellungen einstellungen, AbsenderVerzeichnis absender, List<string> warnungen)
    {
        Pfade = pfade;
        Einstellungen = einstellungen;
        Absender = absender;
        Vorlagen = new VorlagenSpeicher(pfade.Vorlagen);
        Protokoll = new Protokoll(pfade.Protokoll);
        Startwarnungen = warnungen;
    }

    public AppPfade Pfade { get; }
    public Einstellungen Einstellungen { get; set; }
    public AbsenderVerzeichnis Absender { get; }
    public VorlagenSpeicher Vorlagen { get; }
    public Protokoll Protokoll { get; }
    public List<string> Startwarnungen { get; }

    /// <summary>Texterkennung; wird im Hintergrund gesucht und ist bis dahin „nicht verfügbar“.</summary>
    public TesseractOcr Ocr { get; set; } = TesseractOcr.Nicht("Texterkennung wird vorbereitet …");

    /// <summary>Fest vorgegebene Texterkennung (für Tests); sonst wird Tesseract gesucht.</summary>
    public TesseractOcr? VorgegebeneOcr { get; set; }

    public static string Version =>
        typeof(Dienste).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    public static Dienste Starten(AppPfade? pfade = null)
    {
        pfade ??= AppPfade.Standard();
        var warnungen = new List<string>();
        try
        {
            pfade.Anlegen();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnungen.Add($"Der Konfigurationsordner {pfade.Basis} konnte nicht angelegt werden: {ex.Message}");
        }
        var einstellungen = Einstellungen.Laden(pfade.Einstellungen, out var w1);
        if (w1 != null)
            warnungen.Add(w1);
        if (!File.Exists(pfade.Einstellungen))
        {
            try
            {
                einstellungen.Speichern(pfade.Einstellungen);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnungen.Add($"Die Einstellungen konnten nicht gespeichert werden: {ex.Message}");
            }
        }
        var absender = AbsenderVerzeichnis.Laden(pfade.Absender, out var w2);
        if (w2 != null)
            warnungen.Add(w2);
        return new Dienste(pfade, einstellungen, absender, warnungen);
    }
}
