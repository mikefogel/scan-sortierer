using System.Diagnostics;
using System.Text;
using ScanSortierer.Core.Pdf;

namespace ScanSortierer.Core.Ocr;

/// <summary>
/// Lokale Texterkennung mit dem Tesseract-Programm. Bevorzugt wird das mitgelieferte Tesseract im Unterordner
/// „tesseract“ des Programmverzeichnisses, sonst ein installiertes Tesseract.
/// </summary>
public sealed class TesseractOcr
{
    public const string Sprache = "deu";

    private readonly string? _programm;
    private readonly string? _tessdata;
    private readonly string? _bibliotheken;
    private readonly SemaphoreSlim _parallel;

    private TesseractOcr(string? programm, string? tessdata, string? bibliotheken, string status, bool verfuegbar)
    {
        _programm = programm;
        _tessdata = tessdata;
        _bibliotheken = bibliotheken;
        Status = status;
        Verfuegbar = verfuegbar;
        _parallel = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
    }

    public bool Verfuegbar { get; }

    /// <summary>Beschreibung für die Oberfläche, z. B. „Tesseract 5.3.4 (mitgeliefert), Sprache: deu“.</summary>
    public string Status { get; }

    public static TesseractOcr Nicht(string grund) => new(null, null, null, grund, false);

    /// <summary>Sucht Tesseract und prüft, ob die deutschen Sprachdaten vorhanden sind.</summary>
    public static TesseractOcr Finde(string? programmVerzeichnis = null)
    {
        programmVerzeichnis ??= AppContext.BaseDirectory;
        var exe = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        var mitgeliefert = Path.Combine(programmVerzeichnis, "tesseract");

        string? programm = null, tessdata = null, libs = null;
        var herkunft = "";
        if (File.Exists(Path.Combine(mitgeliefert, exe)))
        {
            programm = Path.Combine(mitgeliefert, exe);
            if (Directory.Exists(Path.Combine(mitgeliefert, "tessdata")))
                tessdata = Path.Combine(mitgeliefert, "tessdata");
            if (Directory.Exists(Path.Combine(mitgeliefert, "lib")))
                libs = Path.Combine(mitgeliefert, "lib");
            herkunft = "mitgeliefert";
        }
        else
        {
            programm = SucheImPfad(exe);
            if (programm is null && OperatingSystem.IsWindows())
            {
                foreach (var basis in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                {
                    var p = Path.Combine(basis, "Tesseract-OCR", exe);
                    if (File.Exists(p))
                    {
                        programm = p;
                        break;
                    }
                }
            }
            herkunft = "installiert";
        }

        if (programm is null)
            return Nicht("Tesseract wurde nicht gefunden. Die Texterkennung ist abgeschaltet.");

        var probe = new TesseractOcr(programm, tessdata, libs, "", true);
        try
        {
            var (code, ausgabe, fehler) = probe.Starte(["--list-langs"], null, TimeSpan.FromSeconds(20), CancellationToken.None).GetAwaiter().GetResult();
            var sprachen = (ausgabe + "\n" + fehler).Split('\n').Select(s => s.Trim()).ToList();
            if (code != 0 && sprachen.All(s => s != Sprache))
                return Nicht($"Tesseract ({programm}) lässt sich nicht starten: {fehler.Trim()}");
            if (!sprachen.Contains(Sprache))
                return Nicht($"Tesseract ist vorhanden, aber die deutschen Sprachdaten ({Sprache}) fehlen. Die Texterkennung ist abgeschaltet.");
            var (_, version, versionFehler) = probe.Starte(["--version"], null, TimeSpan.FromSeconds(20), CancellationToken.None).GetAwaiter().GetResult();
            var ersteZeile = (version + versionFehler).Split('\n').FirstOrDefault(s => s.StartsWith("tesseract", StringComparison.OrdinalIgnoreCase))?.Trim() ?? "Tesseract";
            return new TesseractOcr(programm, tessdata, libs, $"{ersteZeile} ({herkunft}), Sprache: Deutsch", true);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or TimeoutException)
        {
            return Nicht($"Tesseract ({programm}) lässt sich nicht starten: {ex.Message}");
        }
    }

    /// <summary>Erkennt den Text eines gerenderten Seitenbilds.</summary>
    public async Task<OcrErgebnis> ErkenneAsync(RasterBild bild, int drehung, CancellationToken abbruch)
    {
        if (!Verfuegbar)
            throw new InvalidOperationException(Status);
        var png = bild.AlsGrauPng();
        // Zwischendatei im System-Temp-Ordner (nie im Programmverzeichnis), wird sofort wieder entfernt.
        var tempOrdner = Path.Combine(Path.GetTempPath(), "scan-sortierer");
        Directory.CreateDirectory(tempOrdner);
        var datei = Path.Combine(tempOrdner, $"{Guid.NewGuid():N}.png");
        await _parallel.WaitAsync(abbruch).ConfigureAwait(false);
        try
        {
            await File.WriteAllBytesAsync(datei, png, abbruch).ConfigureAwait(false);
            var dpi = (int)Math.Round(bild.Dpi);
            var (code, ausgabe, fehler) = await Starte(
                [datei, "stdout", "-l", Sprache, "--psm", "3", "--dpi", dpi.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    // TSV direkt per Variable statt Konfigurationsdatei „tsv“ (die im tessdata-Ordner fehlen kann).
                    "-c", "tessedit_create_txt=0", "-c", "tessedit_create_tsv=1"],
                null, TimeSpan.FromMinutes(3), abbruch).ConfigureAwait(false);
            if (code != 0)
                throw new IOException($"Tesseract meldet einen Fehler ({code}): {fehler.Trim()}");
            return OcrErgebnis.AusTsv(ausgabe, bild.Breite, bild.Hoehe, bild.Dpi, drehung);
        }
        finally
        {
            _parallel.Release();
            try
            {
                File.Delete(datei);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task<(int Code, string Ausgabe, string Fehler)> Starte(string[] argumente, string? eingabe, TimeSpan zeitlimit, CancellationToken abbruch)
    {
        var psi = new ProcessStartInfo(_programm!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = eingabe != null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (_tessdata != null)
        {
            psi.ArgumentList.Add("--tessdata-dir");
            psi.ArgumentList.Add(_tessdata);
        }
        foreach (var a in argumente)
            psi.ArgumentList.Add(a);
        psi.Environment["OMP_THREAD_LIMIT"] = "1";
        if (_tessdata != null)
            psi.Environment["TESSDATA_PREFIX"] = _tessdata;
        if (_bibliotheken != null && OperatingSystem.IsLinux())
        {
            var alt = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            psi.Environment["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(alt) ? _bibliotheken : _bibliotheken + ":" + alt;
        }

        using var p = Process.Start(psi) ?? throw new IOException("Tesseract konnte nicht gestartet werden.");
        var ausgabeTask = p.StandardOutput.ReadToEndAsync(abbruch);
        var fehlerTask = p.StandardError.ReadToEndAsync(abbruch);
        if (eingabe != null)
        {
            await p.StandardInput.WriteAsync(eingabe).ConfigureAwait(false);
            p.StandardInput.Close();
        }
        using var zeit = CancellationTokenSource.CreateLinkedTokenSource(abbruch);
        zeit.CancelAfter(zeitlimit);
        try
        {
            await p.WaitForExitAsync(zeit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            if (abbruch.IsCancellationRequested)
                throw;
            throw new TimeoutException("Tesseract hat zu lange gebraucht.");
        }
        return (p.ExitCode, await ausgabeTask.ConfigureAwait(false), await fehlerTask.ConfigureAwait(false));
    }

    private static string? SucheImPfad(string exe)
    {
        var pfad = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var ordner in pfad.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(ordner.Trim('"'), exe);
                if (File.Exists(p))
                    return p;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }
}
