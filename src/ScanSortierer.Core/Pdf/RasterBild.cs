using System.Buffers.Binary;
using System.IO.Compression;

namespace ScanSortierer.Core.Pdf;

/// <summary>Ein gerendertes Bild im Format BGRA (4 Bytes je Pixel, ohne Zeilenauffüllung).</summary>
public sealed class RasterBild
{
    public RasterBild(int breite, int hoehe, byte[] bgra, double dpi)
    {
        Breite = breite;
        Hoehe = hoehe;
        Bgra = bgra;
        Dpi = dpi;
    }

    public int Breite { get; }
    public int Hoehe { get; }
    public byte[] Bgra { get; }
    public double Dpi { get; }

    /// <summary>Graustufen (8 Bit je Pixel).</summary>
    public byte[] Graustufen()
    {
        var grau = new byte[Breite * Hoehe];
        for (int i = 0, j = 0; i < grau.Length; i++, j += 4)
            grau[i] = (byte)((Bgra[j] * 29 + Bgra[j + 1] * 150 + Bgra[j + 2] * 77) >> 8);
        return grau;
    }

    /// <summary>Kodiert das Bild als 8-Bit-Graustufen-PNG (für Tesseract).</summary>
    public byte[] AlsGrauPng()
    {
        var grau = Graustufen();
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, Breite);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], Hoehe);
        ihdr[8] = 8; // Bittiefe
        ihdr[9] = 0; // Graustufen
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        SchreibeChunk(ms, "IHDR", ihdr);

        using (var roh = new MemoryStream())
        {
            using (var z = new ZLibStream(roh, CompressionLevel.Fastest, leaveOpen: true))
            {
                for (var y = 0; y < Hoehe; y++)
                {
                    z.WriteByte(0); // Filter: keiner
                    z.Write(grau, y * Breite, Breite);
                }
            }
            SchreibeChunk(ms, "IDAT", roh.ToArray());
        }
        SchreibeChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void SchreibeChunk(Stream s, string typ, ReadOnlySpan<byte> daten)
    {
        Span<byte> laenge = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(laenge, daten.Length);
        s.Write(laenge);
        var typBytes = System.Text.Encoding.ASCII.GetBytes(typ);
        s.Write(typBytes);
        s.Write(daten);
        var crc = Crc32.Berechne(typBytes, daten);
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static class Crc32
    {
        private static readonly uint[] Tabelle = Erzeuge();

        private static uint[] Erzeuge()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        public static uint Berechne(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            var c = 0xFFFFFFFFu;
            foreach (var x in a)
                c = Tabelle[(c ^ x) & 0xFF] ^ (c >> 8);
            foreach (var x in b)
                c = Tabelle[(c ^ x) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
