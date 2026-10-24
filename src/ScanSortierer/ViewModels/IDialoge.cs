namespace ScanSortierer.ViewModels;

/// <summary>Dialoge, die das Fenster für die ViewModels bereitstellt.</summary>
public interface IDialoge
{
    Task<string?> OrdnerWaehlenAsync(string titel, string? start);
    Task MeldungAsync(string titel, string text);
    Task<bool> FrageAsync(string titel, string text, string ja = "Ja", string nein = "Abbrechen");
    Task EinstellungenAsync();
    Task VorlagenEditorAsync();
    void Vorschau(SeiteViewModel seite);
}
