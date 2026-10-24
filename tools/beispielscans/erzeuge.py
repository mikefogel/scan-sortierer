#!/usr/bin/env python3
"""Erzeugt Beispiel-Scans (reine Bild-PDFs) zum Testen des Scan-Sortierers.

Benötigt ImageMagick (magick) und img2pdf. Aufruf:
    python3 tools/beispielscans/erzeuge.py <Zielordner>
"""
import os
import subprocess
import sys
import tempfile

BREITE, HOEHE = 1654, 2339  # A4 bei 200 dpi
FONT = "Liberation-Sans"

BRIEFE = {
    "telekom1": [
        (130, 140, 46, "Telekom Deutschland GmbH"),
        (130, 200, 28, "Landgrabenweg 151 · 53227 Bonn"),
        (130, 420, 30, "Herrn Michael Muster"),
        (130, 465, 30, "Musterstraße 12"),
        (130, 510, 30, "12345 Musterstadt"),
        (1050, 640, 30, "Bonn, 14.10.2026"),
        (130, 800, 40, "Ihre Rechnung für Oktober 2026"),
        (130, 900, 30, "Rechnungsnummer: 4711-0815-26"),
        (130, 950, 30, "Kundennummer: 123 456 789"),
        (130, 1060, 30, "Sehr geehrter Herr Muster,"),
        (130, 1120, 30, "hiermit erhalten Sie Ihre Rechnung für Ihren Mobilfunkvertrag."),
        (130, 1250, 30, "Grundgebühr MagentaMobil M                      41,93 €"),
        (130, 1300, 30, "Umsatzsteuer 19 %                                   7,97 €"),
        (130, 1380, 34, "Rechnungsbetrag                                     49,90 €"),
        (130, 1500, 30, "Der Betrag wird am 20.10.2026 von Ihrem Konto abgebucht."),
        (1300, 2200, 26, "Seite 1 von 2"),
    ],
    "telekom2": [
        (130, 200, 34, "Einzelverbindungsnachweis"),
        (130, 300, 28, "01.10.2026  Inland     12 min    0,00 €"),
        (130, 350, 28, "05.10.2026  Inland      4 min    0,00 €"),
        (130, 400, 28, "09.10.2026  SMS                  0,00 €"),
        (130, 600, 30, "Mit freundlichen Grüßen"),
        (130, 650, 30, "Ihre Telekom"),
        (1300, 2200, 26, "Seite 2 von 2"),
    ],
    "aok": [
        (130, 140, 46, "AOK Bayern - Die Gesundheitskasse"),
        (130, 200, 28, "Carl-Wery-Straße 28 · 81739 München"),
        (130, 420, 30, "Herrn Michael Muster"),
        (130, 465, 30, "Musterstraße 12"),
        (130, 510, 30, "12345 Musterstadt"),
        (1050, 640, 30, "München, 02.10.2026"),
        (130, 800, 40, "Beitragsbescheid 2027"),
        (130, 900, 30, "Versichertennummer: A123456789"),
        (130, 1060, 30, "Sehr geehrter Herr Muster,"),
        (130, 1120, 30, "ab dem 01.01.2027 beträgt Ihr monatlicher Beitrag 512,40 €."),
        (130, 1300, 30, "Mit freundlichen Grüßen"),
        (130, 1350, 30, "Ihre AOK Bayern"),
    ],
    "stadtwerke": [
        (130, 140, 46, "Stadtwerke Musterstadt GmbH"),
        (130, 200, 28, "Am Kraftwerk 1 · 12345 Musterstadt"),
        (130, 420, 30, "Herrn Michael Muster"),
        (130, 465, 30, "Musterstraße 12"),
        (130, 510, 30, "12345 Musterstadt"),
        (1050, 640, 30, "Musterstadt, 21. September 2026"),
        (130, 800, 40, "Jahresabrechnung Strom 2025/2026"),
        (130, 900, 30, "Rechnungsnr. SW-2026-99812"),
        (130, 1060, 30, "Sehr geehrter Herr Muster,"),
        (130, 1120, 30, "Ihre Jahresabrechnung ergibt eine Nachzahlung."),
        (130, 1250, 34, "Gesamtbetrag                              1.234,56 EUR"),
        (130, 1400, 30, "Mit freundlichen Grüßen"),
    ],
}


def seite(textzeilen, ziel, drehen=0):
    cmd = ["magick", "-size", f"{BREITE}x{HOEHE}", "xc:white", "-font", FONT, "-fill", "black"]
    for x, y, groesse, text in textzeilen:
        cmd += ["-pointsize", str(groesse), "-annotate", f"+{x}+{y}", text.replace("%", "%%")]
    cmd += ["-attenuate", "0.3", "+noise", "Gaussian", "-colorspace", "Gray"]
    if drehen:
        cmd += ["-rotate", str(drehen)]
    cmd += ["-quality", "70", ziel]
    subprocess.run(cmd, check=True)


def leer(ziel):
    # Leere Rückseite mit leichtem Scanner-Rauschen und einem kleinen Fleck.
    subprocess.run(["magick", "-size", f"{BREITE}x{HOEHE}", "xc:white", "-attenuate", "0.3",
                    "+noise", "Gaussian", "-fill", "gray70", "-draw", "circle 800,1200 803,1203",
                    "-colorspace", "Gray", "-quality", "70", ziel], check=True)


def pdf(seiten, ziel):
    with tempfile.TemporaryDirectory() as tmp:
        bilder = []
        for i, (art, drehen) in enumerate(seiten):
            pfad = os.path.join(tmp, f"{i:03}.jpg")
            if art == "leer":
                leer(pfad)
            else:
                seite(BRIEFE[art], pfad, drehen)
            bilder.append(pfad)
        subprocess.run(["img2pdf", "--imgsize", "200dpi", "-o", ziel] + bilder, check=True)


def main():
    ziel = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(ziel, exist_ok=True)
    # Beidseitig gescannt: Vorderseite, Rückseite, ...
    pdf([("telekom1", 0), ("leer", 0), ("telekom2", 0), ("leer", 0)], os.path.join(ziel, "Scan 001.pdf"))
    pdf([("aok", 0), ("leer", 0), ("stadtwerke", 180), ("leer", 0)], os.path.join(ziel, "Scan 002.pdf"))
    with open(os.path.join(ziel, "Scan 003.pdf"), "wb") as f:
        f.write(b"%PDF-1.4\n% defekte Datei zum Testen\n1 0 obj\n<<")


if __name__ == "__main__":
    main()
