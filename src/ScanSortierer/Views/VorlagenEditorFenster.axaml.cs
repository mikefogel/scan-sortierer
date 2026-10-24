using Avalonia.Controls;
using ScanSortierer.ViewModels;

namespace ScanSortierer.Views;

public partial class VorlagenEditorFenster : Window
{
    private bool _schliessenBestaetigt;

    public VorlagenEditorFenster()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is VorlagenEditorViewModel vm)
            vm.Frage = (titel, text) => DialogFenster.Zeige(this, titel, text, "Ja", "Abbrechen");
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_schliessenBestaetigt || DataContext is not VorlagenEditorViewModel { Geaendert: true })
            return;
        e.Cancel = true;
        if (await DialogFenster.Zeige(this, "Ungespeicherte Änderungen", "Die Änderungen an der Vorlage wurden nicht gespeichert. Trotzdem schließen?", "Schließen", "Abbrechen"))
        {
            _schliessenBestaetigt = true;
            Close();
        }
    }
}
