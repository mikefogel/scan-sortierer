# Scan-Sortierer

Lies zuerst `docs/spezifikation.md`, sie ist die verbindliche Grundlage. Oberflächenentwürfe liegen in `docs/mockups/` (Ideen, keine Pixelvorgabe).

## Rahmen
- C# mit .NET und Avalonia UI, muss auf Windows und Linux kompilieren und laufen.
- Oberfläche nur Deutsch. Lokale OCR (Tesseract), keine Cloud-Dienste.
- Metadaten ausschließlich in der PDF. Konfiguration, Vorlagen und gemerkte Absender liegen im Home-Verzeichnis des Nutzers (`.scan-sortierer`), nie im Programmverzeichnis.
- Testbetrieb (Standard AN): Originale niemals verschieben oder löschen. Das Programm löscht Originale nie selbst.
- Nie Dateien überschreiben, erst temporär schreiben, dann umbenennen.

## Vorgehen
Mit einem lauffähigen Prototyp beginnen (Ordner einlesen, Vorschau, Seiten wählen/drehen/löschen/umsortieren, Speichern mit Metadaten und Dateiname), danach OCR und Vorlagen mit Erkennungsregeln, dann Vorlageneditor.
