# Scan-Sortierer

Desktop-Programm (Windows/Linux, C# und Avalonia) zum Sortieren gescannter Briefe: Scan-PDFs trennen, per lokaler OCR Metadaten erkennen, in die PDF schreiben und passend benennen.

Die vollständige Spezifikation steht in [docs/spezifikation.md](docs/spezifikation.md), Oberflächenentwürfe in [docs/mockups/](docs/mockups/).

## Installation

Fertige Pakete gibt es unter [Releases](../../releases). Sie enthalten .NET, Tesseract und die deutschen Sprachdaten – es muss nichts zusätzlich installiert werden.

- **Windows:** `ScanSortierer-…-win-x64.zip` entpacken, `ScanSortierer.exe` starten.
- **Linux (z. B. Fedora):**
  ```sh
  tar xzf ScanSortierer-…-linux-x64.tar.gz -C ~/Programme
  ~/Programme/ScanSortierer/installieren.sh   # optional: Startmenü-Eintrag
  ~/Programme/ScanSortierer/ScanSortierer
  ```

Beim ersten Start wird `~/.scan-sortierer` (Windows: `%USERPROFILE%\.scan-sortierer`) angelegt mit `einstellungen.json`, `absender.json`, `protokoll.log` und den Vorlagen in `vorlagen/`. Der **Testbetrieb ist anfangs eingeschaltet**: Originale werden dann nie verschoben.

## Bedienung in Kürze

1. Oben links den Eingangsordner wählen. Alle PDFs darin werden der Reihe nach angeboten; defekte oder passwortgeschützte Dateien werden markiert und übersprungen.
2. Das Programm schlägt das erste Dokument vor (gelb gestrichelte Linie = vermutete Dokumentgrenze, rot gestrichelt = leere Seite). Seiten per Klick (Umschalt-Klick: Bereich) an- und abwählen, drehen, verschieben (Knöpfe oder mit der Maus ziehen) oder verwerfen (Entf). Rechtsklick auf eine Seite: Grenze/leer umschalten, Doppelklick: große Vorschau. Kopfstehende Seiten werden automatisch gedreht.
3. Dokumenttyp wählen. Die Felder werden per Texterkennung vorbelegt („✓ erkannt“), Pflichtfelder sind mit * markiert.
4. **Speichern & weiter** (Strg+S): Die gewählten Seiten werden als neue PDF mit Metadaten und unsichtbarer Textebene in den Zielordner geschrieben. Sind alle Seiten verteilt, geht es zur nächsten Datei; im Normalbetrieb wandert das Original in den Ordner `erledigt`.
5. **Rückgängig** (Strg+Z) nimmt den letzten Schritt zurück (neue Datei entfernen, Original zurückholen).

## Vorlagen

Je Dokumenttyp eine JSON-Datei in `~/.scan-sortierer/vorlagen/`, im Programm unter **Vorlagen** bearbeitbar oder von Hand:

```json
{
  "name": "Rechnung",
  "dateiname": "{datum}_{absender}_{typ}",
  "ablage": "flach",
  "felder": [
    { "name": "absender", "bezeichnung": "Absender", "typ": "auswahl", "pflicht": true, "imDateinamen": true },
    { "name": "datum", "bezeichnung": "Briefdatum", "typ": "datum", "pflicht": true, "imDateinamen": true,
      "schluesselwoerter": ["Rechnungsdatum", "Datum"] },
    { "name": "betrag", "bezeichnung": "Betrag", "typ": "betrag", "pflicht": true, "imDateinamen": false,
      "schluesselwoerter": ["Rechnungsbetrag", "Gesamtbetrag"] },
    { "name": "rechnungsnummer", "bezeichnung": "Rechnungsnummer", "typ": "text", "pflicht": true, "imDateinamen": false,
      "schluesselwoerter": ["Rechnungsnummer", "Rechnungsnr."], "muster": ["RE-(\\d+)"] }
  ]
}
```

- Typen: `text`, `datum`, `betrag`, `auswahl` (mit `werte`). Platzhalter `{typ}` ist der Vorlagenname.
- `datum` ist immer das Briefdatum (Pflicht, im Dateinamen als `JJJJ-MM-TT`). Beträge erscheinen nie im Dateinamen.
- Erkennung: Text hinter einem der `schluesselwoerter` oder die erste Klammergruppe eines `muster` (regulärer Ausdruck).
- Beim Feld `absender` werden gemerkte Absender vorgeschlagen; Schreibweisen lassen sich unter Einstellungen → Absender zuordnen (z. B. „Telekom Deutschland GmbH“ → „Telekom“). Korrigiert man einen erkannten Absender, wird die erkannte Schreibweise automatisch zugeordnet.
- Fehlerhafte Dateien werden beim Start mit Meldung übersprungen.

## Metadaten in der PDF

Titel (= Dateiname), Autor (= Absender), Betreff (= Dokumenttyp), Stichwörter (= Stichworte/Notiz), Erstellungsdatum (= Briefdatum) sowie eigene Felder: `Dokumenttyp`, `Briefdatum`, je ein Eintrag pro Vorlagenfeld (z. B. `Betrag` = `49,90 €`), `Stichworte`, `Originaldatei`.

## Entwicklung

Voraussetzungen: .NET 10 SDK; für die OCR-Tests Tesseract mit deutschen Sprachdaten (Fedora: `sudo dnf install tesseract tesseract-langpack-deu`).

```sh
dotnet build
dotnet test                      # OCR-Tests werden ohne Tesseract übersprungen
dotnet run --project src/ScanSortierer
tools/paket/paket-linux.sh 0.1.0 # Linux-Paket lokal bauen → artefakte/
python3 tools/beispielscans/erzeuge.py <ordner>   # Beispiel-Scans erzeugen (ImageMagick, img2pdf)
```

Aufbau:

- `src/ScanSortierer.Core` – Logik ohne Oberfläche: Konfiguration, Vorlagen, PDF (PDFium zum Anzeigen, PDFsharp zum Schreiben), OCR (Tesseract-Prozess), Erkennung von Feldern, leeren Seiten und Dokumentgrenzen, sicheres Speichern.
- `src/ScanSortierer` – Avalonia-Oberfläche (MVVM). `ScanSortierer --selbsttest <pdf>` prüft ohne Oberfläche, ob PDF-Anzeige und OCR im Paket funktionieren.
- `tests/` – Unit-, OCR- und headless Oberflächentests mit Beispiel-Scans.

Die Pipeline (`.github/workflows/build.yml`) testet auf Linux und Windows, baut die eigenständigen Pakete, prüft sie per Selbsttest (Linux zusätzlich in einem frischen Fedora-Container ohne Tesseract) und legt bei einem Tag `v*` ein GitHub-Release an:

```sh
git tag v0.1.0 && git push origin v0.1.0
```

Verwendete Bibliotheken: Avalonia (MIT), CommunityToolkit.Mvvm (MIT), PDFsharp 6.2 (MIT; neuere Versionen sind AGPL), PDFium über bblanchon.PDFium (BSD/Apache 2.0), Tesseract (Apache 2.0).
