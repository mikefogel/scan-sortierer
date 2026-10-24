#!/usr/bin/env python3
"""Bündelt Tesseract samt benötigter Bibliotheken und deutscher Sprachdaten für das Programmpaket.

Ergebnis (Ordner --ziel, später <Programmverzeichnis>/tesseract):
    Windows: tesseract.exe + benötigte DLLs, tessdata/deu.traineddata
    Linux:   tesseract + lib/*.so*, tessdata/deu.traineddata

Aufrufe:
    tesseract_buendeln.py windows --installer setup.exe --tessdata deu.traineddata --ziel dist/ScanSortierer/tesseract
    tesseract_buendeln.py linux --tessdata deu.traineddata --ziel dist/ScanSortierer/tesseract
"""
import argparse
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile

# Grundbibliotheken, die jedes Linux-System mitbringt (wie die AppImage-Ausschlussliste).
LINUX_AUSSCHLUSS = re.compile(
    r"^(linux-vdso|ld-linux.*|libc|libm|libdl|libpthread|librt|libresolv|libutil|libgcc_s|libstdc\+\+)\.so"
)


def pe_importe(pfad):
    """Liest die DLL-Namen aus der Importtabelle einer PE-Datei (exe/dll)."""
    with open(pfad, "rb") as f:
        daten = f.read()
    pe = struct.unpack_from("<I", daten, 0x3C)[0]
    if daten[pe:pe + 4] != b"PE\0\0":
        raise ValueError(f"{pfad}: keine PE-Datei")
    abschnitte = struct.unpack_from("<H", daten, pe + 6)[0]
    opt_groesse = struct.unpack_from("<H", daten, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", daten, opt)[0]
    verzeichnisse = opt + (112 if magic == 0x20B else 96)
    import_rva = struct.unpack_from("<I", daten, verzeichnisse + 8)[0]
    if import_rva == 0:
        return []
    tabelle = opt + opt_groesse
    sektionen = []
    for i in range(abschnitte):
        s = tabelle + i * 40
        vgroesse, vadresse, rgroesse, roffset = struct.unpack_from("<IIII", daten, s + 8)
        sektionen.append((vadresse, max(vgroesse, rgroesse), roffset))

    def offset(rva):
        for va, groesse, ro in sektionen:
            if va <= rva < va + groesse:
                return rva - va + ro
        raise ValueError(f"{pfad}: RVA {rva:#x} außerhalb der Abschnitte")

    namen = []
    pos = offset(import_rva)
    while True:
        eintrag = struct.unpack_from("<IIIII", daten, pos)
        if eintrag == (0, 0, 0, 0, 0):
            break
        name_off = offset(eintrag[3])
        ende = daten.index(b"\0", name_off)
        namen.append(daten[name_off:ende].decode("ascii"))
        pos += 20
    return namen


def windows(args):
    with tempfile.TemporaryDirectory() as tmp:
        subprocess.run(["7z", "x", "-y", f"-o{tmp}", args.installer], check=True, stdout=subprocess.DEVNULL)
        vorhanden = {n.lower(): n for n in os.listdir(tmp)}
        if "tesseract.exe" not in vorhanden:
            sys.exit("tesseract.exe nicht im Installer gefunden")
        benoetigt = set()
        offen = ["tesseract.exe"]
        while offen:
            datei = offen.pop()
            for dll in pe_importe(os.path.join(tmp, vorhanden[datei])):
                schluessel = dll.lower()
                # Nur DLLs aus dem Installer; Systembibliotheken (KERNEL32, msvcrt, ...) bringt Windows mit.
                if schluessel in vorhanden and schluessel not in benoetigt:
                    benoetigt.add(schluessel)
                    offen.append(schluessel)
        os.makedirs(args.ziel, exist_ok=True)
        for datei in ["tesseract.exe", *sorted(benoetigt)]:
            shutil.copy2(os.path.join(tmp, vorhanden[datei]), args.ziel)
        if os.path.isdir(os.path.join(tmp, "doc")):
            shutil.copytree(os.path.join(tmp, "doc"), os.path.join(args.ziel, "doc"), dirs_exist_ok=True)
        print(f"Tesseract (Windows) gebündelt: tesseract.exe + {len(benoetigt)} DLLs")
    sprachdaten(args)


def linux(args):
    programm = shutil.which("tesseract")
    if not programm:
        sys.exit("tesseract nicht gefunden (apt install tesseract-ocr)")
    programm = os.path.realpath(programm)
    os.makedirs(os.path.join(args.ziel, "lib"), exist_ok=True)
    shutil.copy2(programm, os.path.join(args.ziel, "tesseract"))
    ausgabe = subprocess.run(["ldd", programm], check=True, capture_output=True, text=True).stdout
    anzahl = 0
    for zeile in ausgabe.splitlines():
        m = re.match(r"\s*(\S+)\s+=>\s+(\S+)", zeile)
        if not m or LINUX_AUSSCHLUSS.match(m.group(1)):
            continue
        if m.group(2) == "not":
            sys.exit(f"Bibliothek fehlt: {zeile.strip()}")
        shutil.copy2(os.path.realpath(m.group(2)), os.path.join(args.ziel, "lib", m.group(1)))
        anzahl += 1
    for lizenz in ["/usr/share/doc/tesseract-ocr/copyright", "/usr/share/licenses/tesseract/LICENSE"]:
        if os.path.exists(lizenz):
            os.makedirs(os.path.join(args.ziel, "doc"), exist_ok=True)
            shutil.copy2(lizenz, os.path.join(args.ziel, "doc", "LICENSE-tesseract"))
            break
    print(f"Tesseract (Linux) gebündelt: {programm} + {anzahl} Bibliotheken")
    sprachdaten(args)


def sprachdaten(args):
    ziel = os.path.join(args.ziel, "tessdata")
    os.makedirs(ziel, exist_ok=True)
    shutil.copy2(args.tessdata, os.path.join(ziel, "deu.traineddata"))
    print("Sprachdaten: deu.traineddata")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("plattform", choices=["windows", "linux"])
    p.add_argument("--installer", help="UB-Mannheim-Installer (nur Windows)")
    p.add_argument("--tessdata", required=True, help="Pfad zu deu.traineddata")
    p.add_argument("--ziel", required=True)
    args = p.parse_args()
    if args.plattform == "windows":
        if not args.installer:
            p.error("--installer fehlt")
        windows(args)
    else:
        linux(args)


if __name__ == "__main__":
    main()
