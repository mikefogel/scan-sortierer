using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ScanSortierer.ViewModels;
using ScanSortierer.Views;

namespace ScanSortierer;

public partial class App : Application
{
    /// <summary>Für Tests: vorbereitete Dienste statt der Standardkonfiguration.</summary>
    public static Dienste? VorgegebeneDienste { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dienste = VorgegebeneDienste ?? Dienste.Starten();
            var vm = new HauptViewModel(dienste);
            var fenster = new HauptFenster { DataContext = vm };
            desktop.MainWindow = fenster;
            desktop.ShutdownRequested += (_, _) => vm.Beenden();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
