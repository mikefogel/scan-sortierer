using ScanSortierer.Core.Pdf;

namespace ScanSortierer.Core.Erkennung;

/// <summary>Erkennt leere Seiten (z. B. leere Rückseiten beim beidseitigen Scan).</summary>
public static class Leerseiten
{
    /// <summary>Anteil dunkler Bildpunkte (ohne Randbereich), ab dem eine Seite nicht mehr als leer gilt.</summary>
    public const double Schwelle = 0.002;

    public static double DunkelAnteil(RasterBild bild)
    {
        var grau = bild.Graustufen();
        // Ränder ignorieren: dort liegen oft Schatten, Lochungen oder Einzugsspuren.
        var rx = (int)(bild.Breite * 0.06);
        var ry = (int)(bild.Hoehe * 0.05);
        long dunkel = 0, gesamt = 0;
        for (var y = ry; y < bild.Hoehe - ry; y++)
        {
            var zeile = y * bild.Breite;
            for (var x = rx; x < bild.Breite - rx; x++)
            {
                gesamt++;
                if (grau[zeile + x] < 150)
                    dunkel++;
            }
        }
        return gesamt == 0 ? 0 : (double)dunkel / gesamt;
    }

    public static bool IstLeer(RasterBild bild) => DunkelAnteil(bild) < Schwelle;
}
