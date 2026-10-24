using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ScanSortierer.ViewModels;

namespace ScanSortierer.Views;

public partial class EinstellungenFenster : Window
{
    public EinstellungenFenster()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is EinstellungenViewModel vm)
        {
            vm.Fertig += (_, _) => Close();
            vm.OrdnerWaehler = async (titel, start) =>
            {
                var optionen = new FolderPickerOpenOptions { Title = titel };
                if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start))
                    optionen.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start);
                var ergebnis = await StorageProvider.OpenFolderPickerAsync(optionen);
                return ergebnis.Count > 0 ? ergebnis[0].TryGetLocalPath() : null;
            };
        }
    }
}
