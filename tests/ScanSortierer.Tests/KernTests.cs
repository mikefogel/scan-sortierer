using ScanSortierer.Core.Ablage;
using ScanSortierer.Core.Konfiguration;
using ScanSortierer.Core.Vorlagen;

namespace ScanSortierer.Tests;

public class DateinamenTests
{
    private static Vorlage Rechnung() => new()
    {
        Name = "Rechnung",
        Dateiname = "{datum}_{absender}_{typ}",
        Felder =
        [
            new Feld { Name = "absender", Typ = FeldTyp.Auswahl, Pflicht = true },
            new Feld { Name = "datum", Typ = FeldTyp.Datum, Pflicht = true },
            new Feld { Name = "betrag", Typ = FeldTyp.Betrag, ImDateinamen = false },
        ],
    };

    [Fact]
    public void BeispielAusDerSpezifikation()
    {
        var name = Dateinamen.Erzeuge(Rechnung(), new Dictionary<string, string> { ["datum"] = "24.10.2026", ["absender"] = "Telekom" });
        Assert.Equal("2026-10-24_Telekom_Rechnung.pdf", name);
    }

    [Fact]
    public void FehlendeFelderWerdenMitTrennerWeggelassen()
    {
        Assert.Equal("2026-10-24_Rechnung.pdf",
            Dateinamen.Erzeuge(Rechnung(), new Dictionary<string, string> { ["datum"] = "24.10.2026" }));
        Assert.Equal("Telekom_Rechnung.pdf",
            Dateinamen.Erzeuge(Rechnung(), new Dictionary<string, string> { ["absender"] = "Telekom" }));
    }

    [Fact]
    public void UngueltigeZeichenWerdenEntfernt()
    {
        Assert.Equal("2026-10-24_ABC GmbH Co_Rechnung.pdf",
            Dateinamen.Erzeuge(Rechnung(), new Dictionary<string, string> { ["datum"] = "24.10.2026", ["absender"] = "A/B\\C: GmbH? *Co\"" }));
    }

    [Fact]
    public void BetraegeNieImDateinamen()
    {
        var v = Rechnung();
        v.Dateiname = "{datum}_{betrag}_{typ}";
        Assert.Equal("2026-10-24_Rechnung.pdf",
            Dateinamen.Erzeuge(v, new Dictionary<string, string> { ["datum"] = "24.10.2026", ["betrag"] = "49,90 €" }));
    }

    [Fact]
    public void ZaehlerBeiVorhandenerDatei()
    {
        var ordner = Directory.CreateTempSubdirectory("ss-test-").FullName;
        File.WriteAllText(Path.Combine(ordner, "a.pdf"), "alt");
        var p = SichereDateien.SchreibeNeu(ordner, "a.pdf", t => File.WriteAllText(t, "neu"));
        Assert.Equal(Path.Combine(ordner, "a_2.pdf"), p);
        Assert.Equal("alt", File.ReadAllText(Path.Combine(ordner, "a.pdf")));
        var p3 = SichereDateien.SchreibeNeu(ordner, "a.pdf", t => File.WriteAllText(t, "neu3"));
        Assert.Equal(Path.Combine(ordner, "a_3.pdf"), p3);
        Assert.Empty(Directory.GetFiles(ordner, "*.tmp"));
    }

    [Fact]
    public void FehlgeschlagenePruefungHinterlaesstKeineDatei()
    {
        var ordner = Directory.CreateTempSubdirectory("ss-test-").FullName;
        Assert.Throws<IOException>(() => SichereDateien.SchreibeNeu(ordner, "x.pdf",
            t => File.WriteAllText(t, "halb"), _ => throw new IOException("kaputt")));
        Assert.Empty(Directory.GetFiles(ordner));
    }
}

public class WerteTests
{
    [Theory]
    [InlineData("14.10.2026", 2026, 10, 14)]
    [InlineData("4.1.26", 2026, 1, 4)]
    [InlineData("2026-10-14", 2026, 10, 14)]
    [InlineData("21. September 2026", 2026, 9, 21)]
    public void DatumLesen(string text, int j, int m, int t)
    {
        Assert.True(Werte.LeseDatum(text, out var d));
        Assert.Equal(new DateOnly(j, m, t), d);
    }

    [Theory]
    [InlineData("31.02.2026")]
    [InlineData("abc")]
    public void UngueltigesDatum(string text) => Assert.False(Werte.LeseDatum(text, out _));

    [Theory]
    [InlineData("49,90", "49,90 €")]
    [InlineData("49,90 €", "49,90 €")]
    [InlineData("1.234,56 EUR", "1.234,56 €")]
    [InlineData("49.90", "49,90 €")]
    [InlineData("12", "12,00 €")]
    public void BetragLesenUndFormatieren(string text, string erwartet)
    {
        Assert.True(Werte.LeseBetrag(text, out var b));
        Assert.Equal(erwartet, Werte.FormatiereBetrag(b));
    }
}

public class VorlagenTests
{
    [Fact]
    public void BeispielvorlagenSindGueltig()
    {
        var ordner = Directory.CreateTempSubdirectory("ss-vorlagen-").FullName;
        VorlagenSpeicher.BeispieleKopieren(ordner);
        var (vorlagen, fehler) = new VorlagenSpeicher(ordner).LadeAlle();
        Assert.Empty(fehler);
        Assert.Contains(vorlagen, v => v.Name == "Rechnung");
        Assert.True(vorlagen.Count >= 5);
    }

    [Fact]
    public void FehlerhaftesJsonWirdVerstaendlichAbgelehnt()
    {
        var (v, fehler) = VorlagenSpeicher.AusJson("{ \"name\": \"X\", \"felder\": [ { \"name\": \"a\" ] }");
        Assert.Null(v);
        Assert.Contains("Zeile", fehler[0]);
    }

    [Fact]
    public void UnbekannteEigenschaftWirdGemeldet()
    {
        var (v, fehler) = VorlagenSpeicher.AusJson("{ \"name\": \"X\", \"felderr\": [] }");
        Assert.Null(v);
        Assert.Contains("felderr", fehler[0]);
    }

    [Fact]
    public void BetragImDateinamenIstUngueltig()
    {
        var (v, fehler) = VorlagenSpeicher.AusJson("""
            { "name": "X", "dateiname": "{betrag}", "felder": [ { "name": "betrag", "typ": "betrag", "imDateinamen": true } ] }
            """);
        Assert.Null(v);
        Assert.Contains(fehler, f => f.Contains("Beträge"));
    }

    [Fact]
    public void UnbekannterPlatzhalter()
    {
        var (_, fehler) = VorlagenSpeicher.AusJson("""{ "name": "X", "dateiname": "{foo}_{typ}", "felder": [] }""");
        Assert.Contains(fehler, f => f.Contains("{foo}"));
    }

    [Fact]
    public void SpeichernUndLaden()
    {
        var ordner = Directory.CreateTempSubdirectory("ss-vorlagen-").FullName;
        var speicher = new VorlagenSpeicher(ordner);
        var v = new Vorlage { Name = "Behördenbrief", Felder = [new Feld { Name = "datum", Typ = FeldTyp.Datum, Pflicht = true }, new Feld { Name = "absender", Typ = FeldTyp.Auswahl }] };
        var pfad = speicher.Speichern(v);
        Assert.EndsWith("behoerdenbrief.json", pfad);
        var (geladen, fehler) = speicher.LadeAlle();
        Assert.Empty(fehler);
        Assert.Equal("Behördenbrief", Assert.Single(geladen).Name);
    }
}

public class AbsenderTests
{
    [Fact]
    public void SchreibweisenWerdenAbgebildet()
    {
        var a = new AbsenderVerzeichnis(null, [new AbsenderEintrag { Name = "Telekom", Schreibweisen = ["Telekom Deutschland GmbH", "Deutsche Telekom AG"] }]);
        Assert.Equal("Telekom", a.Normalisiere("telekom  deutschland gmbh"));
        Assert.Equal("Vodafone", a.Normalisiere("Vodafone"));
    }

    [Fact]
    public void MerkenMitErkannterSchreibweise()
    {
        var a = new AbsenderVerzeichnis();
        Assert.True(a.Merke("AOK", "AOK Bayern - Die Gesundheitskasse"));
        Assert.Equal("AOK", a.Normalisiere("AOK Bayern - Die Gesundheitskasse"));
        Assert.False(a.Merke("AOK", "AOK Bayern - Die Gesundheitskasse"));
    }
}
