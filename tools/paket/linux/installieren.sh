#!/bin/sh
# Legt einen Startmenü-Eintrag für den Scan-Sortierer an (nur für den aktuellen Nutzer, kein root nötig).
# Das Programm bleibt in diesem Ordner; zum Entfernen deinstallieren.sh ausführen.
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
DATEN="${XDG_DATA_HOME:-$HOME/.local/share}"
mkdir -p "$DATEN/applications" "$DATEN/icons/hicolor/256x256/apps"
cp "$DIR/scan-sortierer.png" "$DATEN/icons/hicolor/256x256/apps/scan-sortierer.png"
cat > "$DATEN/applications/scan-sortierer.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Scan-Sortierer
Comment=Gescannte Briefe trennen, benennen und ablegen
Exec="$DIR/ScanSortierer"
Icon=scan-sortierer
Terminal=false
Categories=Office;Utility;
DESKTOP
update-desktop-database "$DATEN/applications" >/dev/null 2>&1 || true
echo "Startmenü-Eintrag „Scan-Sortierer“ angelegt (Programm: $DIR/ScanSortierer)."
