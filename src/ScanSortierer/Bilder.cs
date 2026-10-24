using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ScanSortierer.Core.Pdf;

namespace ScanSortierer;

internal static class Bilder
{
    public static WriteableBitmap ZuBitmap(RasterBild r)
    {
        var bmp = new WriteableBitmap(new PixelSize(r.Breite, r.Hoehe), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = bmp.Lock();
        for (var y = 0; y < r.Hoehe; y++)
            Marshal.Copy(r.Bgra, y * r.Breite * 4, fb.Address + y * fb.RowBytes, r.Breite * 4);
        return bmp;
    }
}
