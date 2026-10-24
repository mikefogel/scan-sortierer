#!/bin/sh
# Baut das Linux-Paket lokal (z. B. auf Fedora). Benötigt: .NET 10 SDK, tesseract, python3, curl.
#   tools/paket/paket-linux.sh [version]
set -e
VERSION="${1:-0.0.0-lokal}"
WURZEL="$(cd "$(dirname "$0")/../.." && pwd)"
AUS="$WURZEL/artefakte"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

TESSDATA_URL="https://github.com/tesseract-ocr/tessdata/raw/4.1.0/deu.traineddata"
TESSDATA_SHA256="896b3b4956503ab9daa10285db330881b2d74b70d889b79262cc534b9ec699a4"
curl -sSfL -o "$TMP/deu.traineddata" "$TESSDATA_URL"
echo "$TESSDATA_SHA256  $TMP/deu.traineddata" | sha256sum -c -

PAKET="$TMP/ScanSortierer"
dotnet publish "$WURZEL/src/ScanSortierer" -c Release -r linux-x64 --self-contained \
  -p:Version="${VERSION%%-*}" -p:InformationalVersion="$VERSION" -o "$PAKET"
python3 "$WURZEL/tools/paket/tesseract_buendeln.py" linux --tessdata "$TMP/deu.traineddata" --ziel "$PAKET/tesseract"
cp "$WURZEL"/tools/paket/linux/* "$WURZEL/tools/paket/LIESMICH.txt" "$PAKET/"
env -u TESSDATA_PREFIX "$PAKET/ScanSortierer" --selbsttest "$WURZEL/tests/Beispielscans/Scan 001.pdf" --erwarte Rechnungsbetrag

mkdir -p "$AUS"
tar -czf "$AUS/ScanSortierer-$VERSION-linux-x64.tar.gz" -C "$TMP" ScanSortierer
echo "Fertig: $AUS/ScanSortierer-$VERSION-linux-x64.tar.gz"
