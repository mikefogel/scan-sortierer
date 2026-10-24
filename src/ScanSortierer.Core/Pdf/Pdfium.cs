using System.Runtime.InteropServices;

namespace ScanSortierer.Core.Pdf;

/// <summary>
/// Minimale Anbindung an PDFium (aus den Paketen bblanchon.PDFium.*). PDFium ist nicht threadsicher,
/// daher laufen alle Aufrufe unter einer gemeinsamen Sperre.
/// </summary>
internal static class PdfiumNativ
{
    private const string Lib = "pdfium";

    public static readonly object Sperre = new();
    private static bool _initialisiert;

    public static void Init()
    {
        lock (Sperre)
        {
            if (_initialisiert)
                return;
            FPDF_InitLibrary();
            _initialisiert = true;
        }
    }

    [DllImport(Lib)] public static extern void FPDF_InitLibrary();
    [DllImport(Lib)] public static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);
    [DllImport(Lib)] public static extern uint FPDF_GetLastError();
    [DllImport(Lib)] public static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport(Lib)] public static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport(Lib)] public static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [DllImport(Lib)] public static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Lib)] public static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Lib)] public static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Lib)] public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Lib)] public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Lib)] public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Lib)] public static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Lib)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Lib)] public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    public const int FlagAnnotationen = 0x01;
    public const int FlagDrucken = 0x800;
}

public sealed class PdfFehler : Exception
{
    public PdfFehler(string meldung, bool passwortGeschuetzt = false, Exception? innere = null) : base(meldung, innere)
    {
        PasswortGeschuetzt = passwortGeschuetzt;
    }

    public bool PasswortGeschuetzt { get; }
}

/// <summary>Ein mit PDFium geöffnetes Dokument (aus dem Speicher, die Datei bleibt ungesperrt).</summary>
public sealed class PdfiumDokument : IDisposable
{
    private IntPtr _doc;
    private GCHandle _daten;

    private PdfiumDokument(IntPtr doc, GCHandle daten, int seiten)
    {
        _doc = doc;
        _daten = daten;
        SeitenAnzahl = seiten;
    }

    public int SeitenAnzahl { get; }

    public static PdfiumDokument Oeffne(byte[] daten)
    {
        PdfiumNativ.Init();
        var handle = GCHandle.Alloc(daten, GCHandleType.Pinned);
        lock (PdfiumNativ.Sperre)
        {
            var doc = PdfiumNativ.FPDF_LoadMemDocument64(handle.AddrOfPinnedObject(), (nuint)daten.Length, null);
            if (doc == IntPtr.Zero)
            {
                var fehler = PdfiumNativ.FPDF_GetLastError();
                handle.Free();
                throw fehler switch
                {
                    4 => new PdfFehler("Die PDF ist passwortgeschützt.", true),
                    3 => new PdfFehler("Die PDF ist passwortgeschützt.", true),
                    2 => new PdfFehler("Die Datei ist keine gültige PDF oder beschädigt."),
                    _ => new PdfFehler($"Die PDF konnte nicht geöffnet werden (Fehler {fehler})."),
                };
            }
            var seiten = PdfiumNativ.FPDF_GetPageCount(doc);
            if (seiten <= 0)
            {
                PdfiumNativ.FPDF_CloseDocument(doc);
                handle.Free();
                throw new PdfFehler("Die PDF enthält keine Seiten.");
            }
            return new PdfiumDokument(doc, handle, seiten);
        }
    }

    /// <summary>Seitengröße in Punkt, wie angezeigt (mit der Drehung aus der PDF, ohne Nutzerdrehung).</summary>
    public (double Breite, double Hoehe) SeitenGroesse(int index)
    {
        lock (PdfiumNativ.Sperre)
        {
            Pruefe();
            var page = PdfiumNativ.FPDF_LoadPage(_doc, index);
            if (page == IntPtr.Zero)
                throw new PdfFehler($"Seite {index + 1} konnte nicht geladen werden.");
            try
            {
                return (PdfiumNativ.FPDF_GetPageWidthF(page), PdfiumNativ.FPDF_GetPageHeightF(page));
            }
            finally
            {
                PdfiumNativ.FPDF_ClosePage(page);
            }
        }
    }

    /// <summary>Rendert eine Seite als BGRA-Bild.</summary>
    /// <param name="drehung">Zusätzliche Drehung im Uhrzeigersinn in Grad (0, 90, 180, 270).</param>
    /// <param name="dpi">Auflösung. Wird begrenzt, falls <paramref name="maxKante"/> überschritten würde.</param>
    public RasterBild Rendere(int index, int drehung, double dpi, int maxKante = 6000)
    {
        lock (PdfiumNativ.Sperre)
        {
            Pruefe();
            var page = PdfiumNativ.FPDF_LoadPage(_doc, index);
            if (page == IntPtr.Zero)
                throw new PdfFehler($"Seite {index + 1} konnte nicht geladen werden.");
            try
            {
                double wPt = PdfiumNativ.FPDF_GetPageWidthF(page);
                double hPt = PdfiumNativ.FPDF_GetPageHeightF(page);
                var skala = dpi / 72.0;
                var groesste = Math.Max(wPt, hPt) * skala;
                if (groesste > maxKante)
                    skala *= maxKante / groesste;
                var w = Math.Max(1, (int)Math.Round(wPt * skala));
                var h = Math.Max(1, (int)Math.Round(hPt * skala));
                var schritte = ((drehung % 360) + 360) % 360 / 90;
                if (schritte % 2 == 1)
                    (w, h) = (h, w);

                var bmp = PdfiumNativ.FPDFBitmap_Create(w, h, 0);
                if (bmp == IntPtr.Zero)
                    throw new PdfFehler("Zu wenig Speicher zum Anzeigen der Seite.");
                try
                {
                    PdfiumNativ.FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF);
                    PdfiumNativ.FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, schritte, PdfiumNativ.FlagAnnotationen);
                    var stride = PdfiumNativ.FPDFBitmap_GetStride(bmp);
                    var puffer = PdfiumNativ.FPDFBitmap_GetBuffer(bmp);
                    var daten = new byte[w * 4 * h];
                    for (var y = 0; y < h; y++)
                        Marshal.Copy(puffer + y * stride, daten, y * w * 4, w * 4);
                    return new RasterBild(w, h, daten, skala * 72.0);
                }
                finally
                {
                    PdfiumNativ.FPDFBitmap_Destroy(bmp);
                }
            }
            finally
            {
                PdfiumNativ.FPDF_ClosePage(page);
            }
        }
    }

    private void Pruefe() => ObjectDisposedException.ThrowIf(_doc == IntPtr.Zero, this);

    public void Dispose()
    {
        lock (PdfiumNativ.Sperre)
        {
            if (_doc != IntPtr.Zero)
            {
                PdfiumNativ.FPDF_CloseDocument(_doc);
                _doc = IntPtr.Zero;
            }
            if (_daten.IsAllocated)
                _daten.Free();
        }
    }
}
