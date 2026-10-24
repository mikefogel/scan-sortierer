#!/bin/sh
# Entfernt den Startmenü-Eintrag. Konfiguration (~/.scan-sortierer) und Dokumente bleiben erhalten.
DATEN="${XDG_DATA_HOME:-$HOME/.local/share}"
rm -f "$DATEN/applications/scan-sortierer.desktop" "$DATEN/icons/hicolor/256x256/apps/scan-sortierer.png"
echo "Startmenü-Eintrag entfernt. Den Programmordner bitte selbst löschen."
