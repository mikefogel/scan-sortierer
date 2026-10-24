# Spezifikation: Scan-Sortierer (v1.5 Entwurf)

Diese Spezifikation ist die Grundlage für die Umsetzung in Visual Studio Code mit Claude Code.

## 1. Ausgangslage und Ziel
- Briefe werden mit einem Canon-Scanner gescannt. Die Dateien landen als `Scan XYZ.pdf` in einem Ordner auf dem NAS der Fritzbox (als Netzwerkfreigabe eingebunden).
- Eine Datei kann mehrere Briefe enthalten.
- Ziel: Ein ganzer Eingangsordner wird eingelesen und abgearbeitet. Jedes Dokument bekommt Metadaten und einen sinnvollen Dateinamen und wird abgelegt.

## 2. Technik
| Thema | Entscheidung |
|---|---|
| Sprache und UI | C# mit .NET und Avalonia UI (moderne Oberfläche) |
| Plattformen | Windows und Linux, muss auf beiden kompilieren und laufen |
| Texterkennung | Lokal per OCR (Vorschlag: Tesseract, Deutsch), nichts verlässt den Rechner |
| Oberflächensprache | Nur Deutsch |
| Auslieferung | Eigenständiges Paket (self-contained) inkl. Tesseract und deutscher Sprachdaten, keine Zusatzinstallation nötig |
| Entwicklung | Visual Studio Code mit Claude Code |

## 3. Ablauf
1. Eingangsordner wählen. Alle PDFs darin werden der Reihe nach bearbeitet.
2. Die aktuelle Datei wird als Seitenvorschau angezeigt.
3. Das Programm schlägt automatisch Dokumentgrenzen vor (z. B. neuer Briefkopf), der Nutzer kann korrigieren.
4. Der Nutzer wählt die Seiten, die zum Dokument gehören. Seiten lassen sich drehen, löschen (leere Seiten) und umsortieren (falsch eingelegt).
5. Dokumenttyp wählen (Dropdown). Zunächst manuell, keine automatische Typwahl.
6. Die Maske der Vorlage erscheint. Per OCR und Schlüsselwörtern werden so viele Felder wie möglich vorbelegt. Der Nutzer prüft und ergänzt. Pflichtfelder müssen ausgefüllt sein.
7. Der Dateiname wird nach dem Schema der Vorlage vorgeschlagen und kann bearbeitet werden.
8. Speichern: Es werden nur die gewählten Seiten als neue PDF mit Metadaten gespeichert. Die übrigen Seiten bleiben für das nächste Dokument stehen.
9. Sind alle Seiten einer Scan-Datei verteilt, wird das Original in den Ordner `erledigt` verschoben (nicht im Testbetrieb, siehe Abschnitt 8). Der Nutzer löscht diesen Ordner manuell. Danach geht es automatisch zur nächsten Datei, bis der Ordner leer ist.

## 4. Vorlagen (JSON)
- Pro Dokumenttyp eine JSON-Datei, frei erweiterbar vom Nutzer.
- Inhalt je Vorlage:
  - Name (erscheint im Dropdown)
  - Felder mit Name, Typ (`text`, `datum`, `betrag`, `auswahl`), Pflicht ja/nein, bei `auswahl` die Liste der Werte
  - Jedes Dokument hat zusätzlich ein freies Feld „Stichworte/Notiz" (nur Metadaten)
  - Pro Feld: im Dateinamen verwenden ja/nein
  - Erkennungsregeln je Feld (Schlüsselwörter, Muster) für die automatische Vorbelegung
  - Dateinamen-Schema, z. B. `{datum}_{absender}_{typ}`
  - Speicherschema (Ablage): vorläufig flach im Zielordner
- Beispiel für ein Schema-Ergebnis: `2026-10-24_Telekom_Rechnung.pdf`
- Das Datum ist immer das Briefdatum (Datumsformat im Dateinamen: `JJJJ-MM-TT`). Beträge erscheinen nie im Dateinamen, nur in den Metadaten. Beträge sind immer Euro und werden mit Währungszeichen gespeichert und angezeigt (z. B. `49,90 €`). Ist kein Briefdatum erkannt, trägt der Nutzer es ein (Pflichtfeld).
- Fehlende optionale Felder werden im Dateinamen weggelassen, ungültige Zeichen entfernt.

## 5. Metadaten
- Werden ausschließlich in die PDF geschrieben (Standard-Felder wie Titel, Autor, Datum, plus eigene Felder für z. B. Betrag und Dokumenttyp).
- Keine Zusatzdatei neben den PDFs, keine Datenbank, keine Übersicht.

## 6. Ablage
- Zielordner vorläufig: Benutzer-Dokumente-Ordner, flach, ohne Unterordner. Die Sortierung übernimmt der Nutzer später selbst.
- Zielordner in den Einstellungen änderbar.

## 7. Konfiguration und Absender merken
- Alle Konfiguration liegt im Home-Verzeichnis des Nutzers in einem eigenen Programmverzeichnis (Name z. B. `.scan-sortierer`, wird beim ersten Start angelegt). Windows: Benutzerordner, Linux: `~`.
- Dort liegen: Einstellungen (Eingangs- und Zielordner), die JSON-Vorlagen und die gemerkten Absender.
- Beim ersten Start werden Beispielvorlagen (z. B. Rechnung) dorthin kopiert.
- Das Programmverzeichnis bleibt unverändert, dort werden weder Zwischendaten noch Konfiguration abgelegt.
- Das Programm merkt sich verwendete Absender und schlägt sie als Auswahl vor.

## 8. Testbetrieb und Sicherheit
- **Testbetrieb** (in den Einstellungen schaltbar, beim ersten Start standardmäßig AN): Originale werden nie gelöscht und nie verschoben. Fertige Dateien werden normal gespeichert. Es wird nicht gemerkt, was schon verarbeitet wurde: Jeder Durchlauf bietet alle Dateien erneut an, damit Tests beliebig wiederholbar sind.
- Im Normalbetrieb wird ein Original erst in den Ordner `erledigt` verschoben, wenn alle Seiten verteilt sind und die neuen Dateien erfolgreich geschrieben und wieder gelesen wurden (Seitenzahl stimmt). Endgültig gelöscht wird nie vom Programm.
- Existiert der Zieldateiname schon, wird nie überschrieben, sondern ein Zähler angehängt (`_2`).
- Schreiben erst in eine temporäre Datei, dann umbenennen, damit bei Abbruch keine halben Dateien entstehen.
- Das Original bleibt bis zum Schluss unverändert, bei Programmabbruch geht nichts verloren.
- Das Programm wird erst gestartet, wenn der Scanner fertig ist. Eine Prüfung auf noch laufende Scans ist nicht nötig.

## 9. Weitere Funktionen
1. **Rückgängig:** Letzter Speichervorgang kann zurückgenommen werden (neue Datei wird entfernt, Original kommt aus `erledigt` zurück).
2. **Defekte oder passwortgeschützte PDFs** werden übersprungen und mit Hinweis angezeigt.
3. **Absender-Schreibweisen:** Mehrere Schreibweisen lassen sich auf einen Namen abbilden (z. B. „Telekom Deutschland GmbH" → „Telekom").
4. **Protokolldatei** im Konfigurationsordner: was wann gespeichert oder gelöscht wurde.
5. **Durchsuchbare PDF:** OCR-Text wird als unsichtbare Textebene in die gespeicherte PDF eingebettet.
6. **Beidseitiger Scan:** Der Scanner scannt im Durchlauf beidseitig (Canon-Bürogerät). Leere Rückseiten werden automatisch erkannt und zum Löschen vorgeschlagen (Nutzer bestätigt). Die Seiten bleiben in Scan-Reihenfolge (Vorderseite, Rückseite, ...).

## 10. Nicht im Umfang
- Online-KI oder Cloud-Dienste.
- Automatische Typwahl (kann später folgen).
- Verwaltungsübersicht oder Suche über Dokumente.
- Prüfung auf noch laufende Scans.

## 11. Vorlageneditor
- Vorlagen lassen sich im Programm erstellen und bearbeiten (Felder, Typen, Pflicht, Auswahllisten, Erkennungsregeln, Dateinamen-Schema), gespeichert als JSON im Konfigurationsordner.
- Das JSON bleibt von Hand editierbar. Fehlerhafte Dateien werden mit verständlicher Fehlermeldung abgelehnt, ohne das Programm zu stören.

## 12. Oberflächenentwürfe
Siehe Bilder in `docs/mockups/` (Hauptfenster, Vorlageneditor, Einstellungen). Sie sind Ideen für Aufbau und Stil, keine Pixelvorgabe. Dunkles Design, später ggf. auch hell.

## 13. Offene Punkte
- Details zur Erkennung von Dokumentgrenzen werden im Prototyp ausprobiert.
