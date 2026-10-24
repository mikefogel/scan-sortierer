using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ScanSortierer.Views;

public partial class DialogFenster : Window
{
    public DialogFenster()
    {
        InitializeComponent();
    }

    public static async Task<bool> Zeige(Window besitzer, string titel, string text, string ja, string? nein)
    {
        var d = new DialogFenster { Title = titel };
        d.TitelText.Text = titel;
        d.InhaltText.Text = text;
        d.JaKnopf.Content = ja;
        d.NeinKnopf.IsVisible = nein != null;
        d.NeinKnopf.Content = nein;
        return await d.ShowDialog<bool>(besitzer);
    }

    private void JaKlick(object? sender, RoutedEventArgs e) => Close(true);
    private void NeinKlick(object? sender, RoutedEventArgs e) => Close(false);
}
