using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ScanSortierer.Core.Pdf;
using ScanSortierer.ViewModels;

namespace ScanSortierer.Views;

public partial class VorschauFenster : Window
{
    private Bitmap? _bitmap;

    public VorschauFenster()
    {
        InitializeComponent();
    }

    public VorschauFenster(PdfiumDokument pdfium, SeiteViewModel seite) : this()
    {
        Title = $"Vorschau · {seite.Bezeichnung}";
        TitelText.Text = seite.Bezeichnung;
        Zoom.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                Skaliere();
        };
        _ = LadeAsync(pdfium, seite);
    }

    private async Task LadeAsync(PdfiumDokument pdfium, SeiteViewModel seite)
    {
        try
        {
            var bild = await Task.Run(() => pdfium.Rendere(seite.ScanIndex, seite.Drehung, 130));
            _bitmap = Bilder.ZuBitmap(bild);
            Bild.Source = _bitmap;
            Skaliere();
        }
        catch (Exception ex) when (ex is PdfFehler or ObjectDisposedException)
        {
            TitelText.Text = $"{seite.Bezeichnung}: Vorschau nicht möglich ({ex.Message})";
        }
    }

    private void Skaliere()
    {
        if (_bitmap is null)
            return;
        Bild.Width = _bitmap.PixelSize.Width * 0.75 * Zoom.Value;
        Bild.Height = _bitmap.PixelSize.Height * 0.75 * Zoom.Value;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Bild.Source = null;
        _bitmap?.Dispose();
    }
}
