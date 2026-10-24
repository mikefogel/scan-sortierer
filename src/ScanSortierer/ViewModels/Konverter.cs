using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ScanSortierer.ViewModels;

public static class Konverter
{
    private static readonly IBrush Leise = new SolidColorBrush(Color.Parse("#8B919A"));
    private static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#C9CDD3"));
    private static readonly IBrush Rot = new SolidColorBrush(Color.Parse("#E07A82"));

    public static readonly IValueConverter FehlerFarbe = new FuncValueConverter<bool, IBrush>(fehler => fehler ? Rot : Leise);
    public static readonly IValueConverter LeerFarbe = new FuncValueConverter<bool, IBrush>(leer => leer ? Rot : Normal);
}

public static class EditorKonverter
{
    private static readonly IBrush Gruen = new SolidColorBrush(Color.Parse("#4CC38A"));
    private static readonly IBrush Rot = new SolidColorBrush(Color.Parse("#E07A82"));

    public static readonly IValueConverter Haken = new FuncValueConverter<bool, string>(b => b ? "☑" : "☐");
    public static readonly IValueConverter GueltigFarbe = new FuncValueConverter<bool, IBrush>(b => b ? Gruen : Rot);
}
