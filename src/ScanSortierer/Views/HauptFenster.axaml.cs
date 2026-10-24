using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ScanSortierer.ViewModels;

namespace ScanSortierer.Views;

public partial class HauptFenster : Window, IDialoge
{
    private SeiteViewModel? _ziehSeite;
    private Point _ziehStart;
    private bool _zieht;
    private bool _gestartet;

    public HauptFenster()
    {
        InitializeComponent();
        AddHandler(PointerMovedEvent, FensterPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, FensterPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, FensterKeyDown, RoutingStrategies.Bubble);
    }

    private HauptViewModel? Vm => DataContext as HauptViewModel;

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_gestartet || Vm is not { } vm)
            return;
        _gestartet = true;
        vm.Dialoge = this;
        await vm.StartenAsync();
    }

    // ------------------------------------------------------------ Seiten anklicken und ziehen

    private void SeiteTapped(object? sender, TappedEventArgs e)
    {
        if (_zieht || (sender as Control)?.DataContext is not SeiteViewModel seite)
            return;
        Vm?.SeiteGeklickt(seite, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    private void SeiteDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SeiteViewModel seite)
            Vorschau(seite);
    }

    private void SeitePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SeiteViewModel seite)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _ziehSeite = seite;
        _ziehStart = e.GetPosition(this);
        _zieht = false;
    }

    private void FensterPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_ziehSeite is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var p = e.GetPosition(this);
        if (!_zieht && (Math.Abs(p.X - _ziehStart.X) > 12 || Math.Abs(p.Y - _ziehStart.Y) > 12))
        {
            _zieht = true;
            Cursor = new Cursor(StandardCursorType.DragMove);
        }
    }

    private void FensterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var seite = _ziehSeite;
        var zog = _zieht;
        _ziehSeite = null;
        if (!zog)
            return;
        Cursor = Cursor.Default;
        if (this.InputHitTest(e.GetPosition(this)) is Visual ziel)
        {
            var c = ziel as Control ?? ziel.FindAncestorOfType<Control>();
            if (c?.DataContext is SeiteViewModel zielSeite && seite != null)
                Vm?.VerschiebeAuf(seite, zielSeite);
        }
        // Tapped kommt nach dem Loslassen; erst danach zurücksetzen.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _zieht = false);
    }

    private void FeldVerlassen(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FeldViewModel feld)
            feld.Normalisiere();
    }

    private void FensterKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || e.Handled)
            return;
        var inText = FocusManager?.GetFocusedElement() is TextBox;
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.SpeichernUndWeiterCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control) && !inText)
        {
            vm.RueckgaengigCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !inText)
        {
            vm.LoeschenCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------ IDialoge

    public async Task<string?> OrdnerWaehlenAsync(string titel, string? start)
    {
        var optionen = new FolderPickerOpenOptions { Title = titel, AllowMultiple = false };
        if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start))
            optionen.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start);
        var ergebnis = await StorageProvider.OpenFolderPickerAsync(optionen);
        return ergebnis.Count > 0 ? ergebnis[0].TryGetLocalPath() : null;
    }

    public Task MeldungAsync(string titel, string text) => DialogFenster.Zeige(this, titel, text, "OK", null);

    public Task<bool> FrageAsync(string titel, string text, string ja = "Ja", string nein = "Abbrechen") =>
        DialogFenster.Zeige(this, titel, text, ja, nein);

    public async Task EinstellungenAsync()
    {
        if (Vm is not { } vm)
            return;
        var f = new EinstellungenFenster { DataContext = new EinstellungenViewModel(vm.Dienste) };
        await f.ShowDialog(this);
    }

    public async Task VorlagenEditorAsync()
    {
        if (Vm is not { } vm)
            return;
        var f = new VorlagenEditorFenster { DataContext = new VorlagenEditorViewModel(vm.Dienste) };
        await f.ShowDialog(this);
    }

    public void Vorschau(SeiteViewModel seite)
    {
        if (Vm?.AktuelleDatei?.Pdfium is not { } pdfium)
            return;
        new VorschauFenster(pdfium, seite).Show(this);
    }
}
