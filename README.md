[README.md](https://github.com/user-attachments/files/30356258/README.md)
# 🧠 DICOM Multi-Frame Merger & Brainlab DTI Engine
> **Die ultimative, enzyklopädische Gesamtdokumentation zur Verarbeitung, Zusammenführung (*Merge*), Aufteilung (*Split*), Anonymisierung, NIfTI-Konvertierung und Brainlab-DTI-Konditionierung von DICOM-Bilddaten.**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D4?style=for-the-badge&logo=windows)](https://microsoft.com)
[![Framework](https://img.shields.io/badge/.NET-8.0%20WPF%20(C%23)-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com)
[![DICOM](https://img.shields.io/badge/DICOM-PS3.0%20%7C%20NEMA-00599C?style=for-the-badge)](https://www.dicomstandard.org/)
[![Brainlab](https://img.shields.io/badge/Brainlab-Fibertracking%20v2.0%20Ready-00A0E3?style=for-the-badge)](https://www.brainlab.com)
[![Build](https://img.shields.io/badge/Build-Single--File%20Standalone%20EXE-10B981?style=for-the-badge)](https://github.com)

---

## 📑 Inhaltsverzeichnis

- [1. Systemarchitektur \& Laufzeitumgebung](#1-systemarchitektur--laufzeitumgebung)
- [2. Detaillierte Kern-Modi \& Algorithmen](#2-detaillierte-kern-modi--algorithmen)
  - [Modus 1: Zusammenführen (Merge Engine)](#modus-1-zusammenführen-merge-engine)
  - [Modus 2: Aufteilen (Split Engine)](#modus-2-aufteilen-split-engine)
  - [Modus 3: NIfTI Conversion Engine](#modus-3-nifti-conversion-engine)
- [3. Brainlab DTI \& Fibertracking v2.0 Spezifikation](#3-brainlab-dti--fibertracking-v20-spezifikation)
- [4. Multi-Vendor B-Wert Extraktions-Kaskade](#4-multi-vendor-b-wert-extraktions-kaskade)
- [5. Chronologische Sortierung \& Namens-Disambiguierung](#5-chronologische-sortierung--namens-disambiguierung)
- [6. Anonymisierungs-Engine (PS3.15 Profile)](#6-anonymisierungs-engine-ps315-profile)
- [7. WPF UI, UX, Animationen \& Visual Tokens](#7-wpf-ui-ux-animationen--visual-tokens)
- [8. Erweiterte Export- \& Telemetrie-Systeme](#8-erweiterte-export---telemetrie-systeme)
- [9. Tastatur-Shortcuts \& Hotkey-Matrix](#9-tastatur-shortcuts--hotkey-matrix)
- [10. Quellcode-Architektur \& Klassenverzeichnis](#10-quellcode-architektur--klassenverzeichnis)

---

## 1. Systemarchitektur & Laufzeitumgebung

`NewDicomMerger` ist eine hochspezialisierte, autarke C# / .NET 8 WPF Desktop-Anwendung, die für den kompromisslosen Einsatz in der Radiologie, Neuroradiologie, Neurochirurgie und Medizinphysik entwickelt wurde. Sie transformiert heterogene DICOM-Bilddaten aus unterschiedlichsten Scanner-Generationen (Siemens Healthineers, GE HealthCare, Philips Healthcare, Canon Medical / Toshiba) in perfekt strukturiert aufbereite Daten für Navigations- und Planungssysteme wie **Brainlab iPlan / Elements**, **Brainlab Fibertracking v2.0**, **3D Slicer**, **FSL** oder **SPM**.

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│                             SYSTEM-ARCHITEKTUR                                   │
├──────────────────────────────────────────────────────────────────────────────────┤
│  [ Input: Single/Multi-Frame DICOM / ZIP / Ordner ]                              │
│                         │                                                        │
│                         ▼                                                        │
│  [ DicomScanner: Parallel Tag-Parser & Pixel Geometry Inspector ]                │
│                         │                                                        │
│                         ▼                                                        │
│  [ Multi-Study Chronology Sort (_VU) & Dynamic Series Disambiguation (_1,_2) ]   │
│                         │                                                        │
│                         ▼                                                        │
│  [ Multi-Vendor B-Value Engine (Extract 0018,9087 / Siemens / GE / Philips) ]    │
│                         │                                                        │
│        ┌────────────────┼────────────────┬────────────────┐                      │
│        ▼                ▼                ▼                ▼                      │
│  [ FrameMerger ]  [ FrameSplitter ] [ NiftiConverter ] [ Brainlab DTI Engine ]   │
│  (Flat Memory    (PerFrame Func    (dcm2niix Process  (HFS, 16-Bit, DTI-     │
│   Buffer Array)   Group Splitter)   Wrapper Engine)    Shadow Tag Protect)   │
│        │                │                │                │                      │
│        └────────────────┴────────────────┴────────────────┘                      │
│                         │                                                        │
│                         ▼                                                        │
│  [ Output: Standalone DICOM / NIfTI / ZIP (7-Zip Ultra) / DICOMDIR / Reports ]   │
└──────────────────────────────────────────────────────────────────────────────────┘
```

### Technische Kernkomponenten:
1. **Single-File Native Executable**:
   Das gesamte Projekt kompiliert in eine autarke `.exe` (ca. 187 MB) inklusive der vollständigen .NET 8 Runtime, nativer C++ Transcoder-Libraries (`fo-dicom` Codecs) und eingebetteter Werkzeuge (`dcm2niix.exe`). Es werden keine Administrationsrechte oder externe Frameworks benötigt.
2. **fo-dicom 5.1.4 Integration**:
   Stellt den DICOM-Standard PS3.0 strikt sicher. Behandelt unkomprimierte und komprimierte Transfersyntaxen (Implicit VR Little Endian, Explicit VR Little Endian, JPEG Lossless Process 14 SV1, RLE, JPEG2000).
3. **Puffer-Allokation zur Umgehung von Memory-Leaks**:
   Standardmäßige iterative Aufrufe von `DicomPixelData.AddFrame()` führen bei großen Serien in fo-dicom zu exzessiver Fragmentierung. `FrameMerger` berechnet daher vorab die exakte Byte-Größe des Gesamtvolumens (`rows * cols * samplesPerPixel * bytesPerPixel * frameCount`), reserviert ein flaches `byte[] allPixelBytes` Array im RAM und übergibt dieses als einzelnen `MemoryByteBuffer`. Unterstützt Volumina bis zu 2 GB Puffergröße.
4. **Multi-Core Parallelisierung (`🚀 Turbo`)**:
   Nutzt `Parallel.ForEachAsync` mit skaliertem `MaxDegreeOfParallelism` (entsprechend der physischen/logischen CPU-Kerne des Wirtssystems), um Bildserien simultan zu verarbeiten.

---

## 2. Detaillierte Kern-Modi & Algorithmen

### Modus 1: Zusammenführen (Merge Engine)
Wandelt Hunderte Einzelbild-DICOM-Dateien (*Single-Frame*) einer Serie in ein universelles Multi-Frame DICOM-Volume um.

* **Dynamische SOP-Klassen-Wahl (`ChooseSopClass`)**:
  - **Enhanced MR Image Storage**: `1.2.840.10008.5.1.4.1.1.4.1` (für MRT-Daten)
  - **Enhanced CT Image Storage**: `1.2.840.10008.5.1.4.1.1.2.1` (für CT-Daten)
  - **Multi-Frame Grayscale Word Secondary Capture**: `1.2.840.10008.5.1.4.1.1.7.3` (für 16-Bit Sekundärbilder)
  - **Multi-Frame Grayscale Byte Secondary Capture**: `1.2.840.10008.5.1.4.1.1.7.2` (für 8-Bit Sekundärbilder)
* **Preservation bestehender Multi-Frame-Volumina**:
  Wenn Eingabedateien bereits Multi-Frame-Dateien sind (z. B. 3D-Volumina vom Scanner), werden **alle** Multi-Frame-Dateien der Gruppe nacheinander verarbeitet, anonymisiert und fortlaufend nummeriert (`_1.dcm`, `_2.dcm`) ausgegeben, **ohne dass Dateien oder Frames verworfen werden**.
* **On-the-Fly Dekompression & Farbraum-Normalisierung**:
  Komprimierte Eingabedateien (`IsEncapsulated`) werden automatisch via `DicomTranscoder` nach `ExplicitVRLittleEndian` dekomprimiert. Die `PhotometricInterpretation` wird nach Dekompression geprüft (z. B. `YBR_FULL_422` $\rightarrow$ `RGB`), um korrupte Farbkanäle zu verhindern.
* **Erzeugung Funktioneller Gruppen (PS3.3 C.7.6.16)**:
  - `SharedFunctionalGroupsSequence` (`(5200,9229)`): Nimmt globale Eigenschaften auf, die für den gesamten Stapel identisch sind (`PlaneOrientationSequence`, `PixelMeasuresSequence`).
  - `PerFrameFunctionalGroupsSequence` (`(5200,9230)`): Speichert frame-spezifische Raumkoordinaten (`PlanePositionSequence` mit `ImagePositionPatient`) und Stack-Indizes (`FrameContentSequence` mit `InStackPositionNumber`).

### Modus 2: Aufteilen (Split Engine)
Zerlegt ein Multi-Frame Volume in einzelne Single-Frame DICOM-Dateien (`FrameSplitter.cs`).

* **Single-Frame SOP-Klassen Rückführung**:
  Konvertiert Multi-Frame SOP-Klassen zurück in Standard Single-Frame IODs (`MR Image Storage 1.2.840.10008.5.1.4.1.1.4`, `CT Image Storage 1.2.840.10008.5.1.4.1.1.2`, etc.).
* **Hierarchische Funktionsgruppen-Auflösung**:
  Liest zuerst die `SharedFunctionalGroupsSequence` aus und überträgt globale Werte in das Einzelbild-Dataset. Anschließend liest es den spezifischen Frame-Index aus `PerFrameFunctionalGroupsSequence` aus und überschreibt schichtspezifische Werte (`ImagePositionPatient`, `SliceLocation`). Abschließend werden Multi-Frame-Sequenzen vollständig entfernt.

### Modus 3: NIfTI Conversion Engine
Verwandelt DICOM-Serien direkt in das wissenschaftliche NIfTI-Format (`.nii` / `.nii.gz`) (`NiftiConverter.cs`).

* **Eingebettete dcm2niix Executable**:
  Extrahiert die binär eingebettete Ressource `dcm2niix.exe` beim ersten Aufruf sicher nach `%TEMP%\NewDicomMerger\Tools_v1.0\dcm2niix.exe`.
* **Prozess-Sicherheit**:
  Der Aufruf erfolgt isoliert über `ProcessStartInfo.ArgumentList` (verhindert CLI Command-Injection Angriffsszenarien bei Pfaden mit Sonderzeichen oder Anführungszeichen).
* **BIDS Sidecar JSON**:
  Erzeugt auf Wunsch BIDS-konforme `.json`-Sidecar-Metadaten mit DTI-Parametern (`EchoTime`, `RepetitionTime`, `FlipAngle`, `MagneticFieldStrength`).

---

## 3. Brainlab DTI & Fibertracking v2.0 Compliance

Die Anwendung durchsetzt die offiziellen **Brainlab DTI-Scanempfehlungen (Fibertracking Ver. 2.0)** bis ins kleinste Detail:

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│                     BRAINLAB DTI SPECIFICATION MATRIX                            │
├───────────────────────────────┬──────────────────────────────────────────────────┤
│ DICOM Attribut                │ Brainlab v2.0 Anforderung & Automatische Aktion  │
├───────────────────────────────┼──────────────────────────────────────────────────┤
│ PatientPosition (0018,0510)   │ Strikt "HFS" (Head First Supine / Rückenlage)    │
│ BitsAllocated (0028,0100)     │ Strikt 16-Bit (BitsAllocated=16, HighBit=15)     │
│ DiffusionBValue (0018,9087)   │ NEMA Standard-Tag mit exaktem B-Wert injiziert   │
│ SeriesDescription (0008,103E) │ Suffix _b{bVal} wird zwingend angehängt          │
│ DTI Private Shadow Tags       │ Siemens/GE/Philips/Canon Shadow Tags geschützt   │
│ Matrix-Geometrie              │ Warnung bei non-square Rows != Columns           │
│ Pixel Spacing                 │ Warnung bei non-square PixelSpacing[0] != [1]    │
│ Schichtdicke                  │ Warnung bei SliceThickness > 3.0 mm              │
└───────────────────────────────┴──────────────────────────────────────────────────┘
```

### DTI-Spezifische Parameter & Regeln:
1. **Patientenlagerung (`PatientPosition = "HFS"`)**:
   Ersetzt oder injiziert den Tag `(0018,0510)` garantiert mit dem Wert `"HFS"` (*Head First Supine* / Rückenlage, Kopf voran).
2. **16-Bit DICOM Speicherformat**:
   Stellt sicher, dass das Bild-Layout exakt 16-Bit entspricht (`BitsAllocated = 16`, `BitsStored = 16`, `HighBit = 15`).
3. **DTI-Shadow-Tag Schutz bei Anonymisierung**:
   Bei aktivierter Anonymisierung schützt eine Whitelist herstellerspezifische Diffusions-Shadow-Tags:
   - **Siemens**: Gruppen `0019` & `0029` (enthält `0019,100c` B-Wert, `0019,100e` Gradients, `0019,1027` B-Matrix).
   - **GE**: Gruppen `0019` & `0043` (enthält `0043,1039` B-Wert, `0019,10bb/bc/bd` Gradients).
   - **Philips**: Gruppen `2001` & `2005` (enthält `2001,1003` B-Wert, `2005,10b1` Direction).
4. **Mathematische Validierung & Warnungen**:
   - ⚠️ Warnung, falls Matrix nicht quadratisch ist (`Rows != Columns`).
   - ⚠️ Warnung, falls `PixelSpacing` asymmetrisch ist (`|PixelSpacing[0] - PixelSpacing[1]| > 0.0001`).
   - ⚠️ Warnung, falls Schichtdicke > 3.05 mm ist (`SliceThickness > 3.05`).

---

## 4. Multi-Vendor B-Wert Extraktions-Kaskade

Die Klasse `DiffusionBValueHelper.cs` nutzt eine 7-stufige Kaskade zur B-Wert-Erkennung:

```csharp
// Extraktions-Kaskade in Services/DiffusionBValueHelper.cs:
1. Standard Tag (0018,9087) DiffusionBValue
2. MR Diffusion Sequence (0018,9117)
3. Shared / PerFrame Functional Groups Sequences
4. Siemens Shadow Tag (0019, 100c)
5. GE Shadow Tag (0043, 1039) -> Modulo Math: val > 1000000 ? val % 100000 : val
   GE Private Tag (0019, 10b4)
6. Philips Shadow Tags (2001, 1003), (2005, 10b1), (2005, 1409)
7. Regex Fallback: \bb[_-]?(\d{1,5})\b in SeriesDescription / ProtocolName / SequenceName
```

### B-Wert Normalisierungs-Mathematik (`NormalizeBValue`):
* `rawB <= 10` $\rightarrow$ geglättet auf `0` (b0-Referenzaufnahme)
* `rawB < 500` (Modulo 50):
  - `rem < 15` $\rightarrow$ runden nach unten (`rawB - rem`)
  - `rem > 35` $\rightarrow$ runden nach oben (`rawB + (50 - rem)`)
* `rawB >= 500` (Modulo 100):
  - `rem < 25` $\rightarrow$ runden nach unten (`rawB - rem`)
  - `rem > 75` $\rightarrow$ runden nach oben (`rawB + (100 - rem)`)

### Suffix & Eindeutige Serien-UIDs:
* B-Wert-getrennte Serien erhalten zwingend das Suffix `_b{bVal}` (z. B. `_b0`, `_b1000`).
* Über `GenerateDerivedUid(baseUid, bVal)` wird eine neue `SeriesInstanceUID` generiert (`baseUid.{bVal}`). Überschreitet die UID das DICOM-Limit von 64 Zeichen, wird eine neue UID erzeugt. Die `StudyInstanceUID` bleibt identisch.

---

## 5. Chronologische Sortierung & Namens-Disambiguierung

Der `DicomScanner` implementiert automatische Algorithmen zur Vermeidung von Namenskonflikten:

### 1. Voruntersuchungen (`_VU`-Suffix)
Lädt der Anwender mehrere Studien desselben Patienten, werden diese nach `StudyDate` und `StudyTime` chronologisch geordnet.
* Die zeitlich neueste Studie behält ihren Namen.
* **Sämtliche älteren Studien** erhalten automatisch das Suffix `_VU` (*Voruntersuchung*) an den Seriennamen gehängt (z. B. `T1_MPRAGE_VU`).

### 2. Dynamische Serien (`_1`, `_2`, `_3`)
Gibt es in derselben Studie mehrere Serien mit identischem Namen (z. B. dynamische Kontrastmittel-Passagen), werden diese chronologisch nach `SeriesTime`, `SeriesNumber` und `AcquisitionNumber` sortiert und fortlaufend nummeriert:
`Perfusion_1`, `Perfusion_2`, `Perfusion_3`, ...

---

## 6. Anonymisierungs-Engine (PS3.15 Profile)

`SeriesDeidentifier.cs` implementiert die De-Identifizierung konsistent über den gesamten Batch-Lauf:

* **Patienten-Identität**: `PatientName` $\rightarrow$ `ANONYMOUS^PATIENT`, `PatientID` $\rightarrow$ `ANON-{ShortHash}` (SHA256 der ersten 4 Bytes als 8-stelliger Hex-String), `PatientBirthDate` $\rightarrow$ `19000101`.
* **Entfernte Tags**: Strippt `PatientSex`, `PatientAge`, `PatientWeight`, `PatientSize`, `PatientAddress`, `InstitutionName`, `ReferringPhysicianName`, `PerformingPhysicianName`, `OperatorsName`, `DeviceSerialNumber`, `AccessionNumber`, `StudyID`.
* **Konsistentes UID-Remapping**: Remappt `StudyInstanceUID`, `SeriesInstanceUID` und `FrameOfReferenceUID` über ein `ConcurrentDictionary` (`_uidMap`). Gleiche Quell-UIDs werden im gesamten Batch zu identischen Ziel-UIDs remapped.
* **Tages-Offset Shift**: Verschiebt `StudyDate`, `SeriesDate`, `AcquisitionDate` und `ContentDate` um einen pro Batch-Lauf zufällig gewählten, festen Offset (`_dateShiftDays` zwischen 1 und 365 Tagen). Relativabstände zwischen Aufnahmen bleiben erhalten, absolute Daten werden verschleiert. `StudyTime`, `SeriesTime` etc. werden komplett gelöscht.

---

## 7. WPF UI, UX, Animationen & Visual Tokens

Die Benutzeroberfläche nutzt das **Windows 11 Dark Theme** mit `Segoe UI Variable` und `Cascadia Mono`.

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│                         WPF VISUAL COLOR TOKENS                                  │
├─────────────────────────┬─────────────────────────┬──────────────────────────────┤
│ Token Key               │ Hex-Farbcode            │ Verwendungszweck             │
├─────────────────────────┼─────────────────────────┼──────────────────────────────┤
│ Bg                      │ #0A0A0C                 │ Hauptfenster Hintergrund     │
│ CardBg                  │ #121215                 │ Container-Karten             │
│ CardBorder              │ #202024                 │ Rahmen & Separatoren         │
│ Surface1 / Surface2     │ #1C1C1E / #2C2C2E       │ Eingabefelder & Buttons      │
│ Accent1                 │ #ECECF1                 │ Heller Text & Highlight      │
│ Accent2                 │ #8E8E93                 │ Sekundärer Text & Icons      │
│ Accent3                 │ #3A3A3C                 │ Inaktive Elemente            │
│ Success                 │ #10B981                 │ Erfolgsmeldungen & Badges    │
│ Error                   │ #F43F5E                 │ Fehlermeldungen              │
└─────────────────────────┴─────────────────────────┴──────────────────────────────┘
```

### UI & UX Feinheiten:
* **Custom Titlebar Chrome**: Eigene Titelleiste mit Aero-Buttons (`—`, `☐`, `✕`) und Drag-Move Unterstützung.
* **Mode-Slider Animation**: `ModeSlider` bewegt sich via `TranslateTransform` mit flüssigem `DoubleAnimation` Storyboard hinter den aktiven Tab-Button.
* **DropZone Doppel-Pulsring**: Zwei versetzte, sinus-animierte Ringe (`PulseRing`) signalisieren kontinuierlich die Drag & Drop Bereitschaft.
* **Review DataGrid**:
  - Checkbox-Spalte für gezielte Serienauswahl.
  - Status-Spalte (`✓ OK` oder `⚠ Excluded`).
  - Format-Badges: `📦 Multi-Frame (3D/4D Volume, N Frames)` vs `📄 Single-Frame (N Dateien)`.
  - Editierbare Textfelder für Patientennamen und Serienbeschreibungen mit direkter `INotifyPropertyChanged` Rückkopplung.
* **Interaktive 2D MPR Vorschau**:
  - Schnelle Render-Engine auf Basis von `System.Windows.Media.Imaging.WriteableBitmap`.
  - Slice-Slider mit Tick-Snapping und Schichtanzeige (z. B. `Slice 12/120`).
  - Window/Level Presets (Hirn, Knochen, Weichteil).
  - Mausrad-Navigation (`MprBorder_PreviewMouseWheel`).
* **DICOM Tag Inspector**: Fullscreen-Overlay zur zeilenweisen Anzeige des DICOM-Headers (Tag, VR, Name, Wert) mit Live-Suchfeld (`TagSearchBox`).
* **Completion Overlay**: Erfolgs-Dialog mit rotierendem Fortschrittsring, Checkmark, Laufzeitanzeige und Direkt-Button **`📂 Ausgabe öffnen`**.

---

## 8. Erweiterte Export- & Telemetrie-Systeme

### 📦 Portable 7-Zip Ultra Kompression (`CheckZipOutput`)
Enthält ein integriertes 7-Zip Modul. Nach Abschluss der Konvertierung werden alle Ausgabedateien automatisch in ein `.zip`-Archiv mit **Stufe 9 (Ultra)** gepackt, benannt nach dem Patientennamen (`MUSTERNMANN_MAX.zip`).

### 💿 DICOMDIR Indexer (`DicomDirWriter.cs`)
Scannt den Zielordner und erstellt eine standardkonforme `DICOMDIR`-Datei mit der vollständigen NEMA-Hierarchie (*PATIENT $\rightarrow$ STUDY $\rightarrow$ SERIES $\rightarrow$ IMAGE*) für den Import auf CDs, DVDs oder USB-Sticks.

### 📊 Batch-Berichterstattung (`BatchReportWriter.cs`)
Erzeugt zwei Berichtsdateien im Ausgabeverzeichnis:
* `batch_report.csv`: Tabellarische Übersicht mit Semikolon-Separierung und RFC-4180 Escaping.
* `batch_report.json`: Formatierte JSON-Struktur für automatisierte Skripte.

### 🏷 Namensmuster-Template System (`ResolveTemplate`)
Unterstützt folgende Platzhalter im Dateinamen-Muster:
`{Patient}`, `{Serie}`, `{Datum}`, `{Modalität}`, `{Modality}`, `{StudyID}`, `{Frames}`, `{Accession}`.

---

## 9. Tastatur-Shortcuts & Hotkey-Matrix

| Shortcut | Zielbereich | Ausgelöste Aktion |
| :--- | :--- | :--- |
| <kbd>Ctrl</kbd> + <kbd>O</kbd> | Datei-System | Öffnet den Windows-Ordnerdialog zum Laden von DICOM-Verzeichnissen |
| <kbd>Ctrl</kbd> + <kbd>A</kbd> | Review DataGrid | Wählt alle geladenen Serien in der Tabelle auf einmal aus |
| <kbd>Ctrl</kbd> + <kbd>D</kbd> | Review DataGrid | Hebt die Auswahl für alle Serien in der Tabelle auf |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>S</kbd>| Review DataGrid | Wählt Übersichtsserien (*Scouts, Localizer, Topogramme*) automatisch ab |
| <kbd>Ctrl</kbd> + <kbd>Enter</kbd> | Action Bar | Startet die Verarbeitung aller ausgewählten Serien |
| <kbd>F5</kbd> | Global | Setzt den Anwendungsstatus zurück und leert die Listen |
| <kbd>Esc</kbd> | Global | Bricht laufende Vorgänge ab oder schließt aktive Overlays |

---

## 10. Quellcode-Architektur & Systemstruktur

```
NewDicomMerger/
├── Models/
│   ├── DicomModels.cs              # Core Data Models (LoadedDicom, SeriesGroup, MergeResult, SplitResult)
│   └── ReviewItemViewModel.cs      # WPF Binding ViewModel mit INotifyPropertyChanged & Badges
├── Services/
│   ├── DicomScanner.cs             # Gruppierung, Geometrie-Checks, VU/_1_2 Suffixing
│   ├── FrameMerger.cs              # Multi-Frame Merge Engine, Flat Buffer Array, Functional Groups
│   ├── FrameSplitter.cs            # Multi-Frame Split Engine, Functional Groups Flattening
│   ├── DiffusionBValueHelper.cs    # 7-Stufige Vendor B-Wert-Extraktion & Brainlab DTI Engine
│   ├── SeriesDeidentifier.cs       # DICOM PS3.15 Anonymisierung & DTI Private Tag Protection
│   ├── NiftiConverter.cs           # Process-Wrapper für dcm2niix.exe mit BIDS-Export
│   ├── DicomDirWriter.cs           # ISO/NEMA DICOMDIR Hierarchie-Generator
│   ├── BatchReportWriter.cs        # CSV & JSON Batch-Report Telemetrie
│   └── LruCache.cs                 # O(1) LRU-Cache für 2D-Schnittbild Vorschau
├── MainWindow.xaml                 # Fluent Dark UI Layout, Controls, Animations & Visual Brushes
├── MainWindow.xaml.cs              # UI-Controller, Thread-Marshalling, Async Pipelines & Shortcuts
├── app_icon.ico                    # Eingebettetes Vektor-basiertes Anwendungs-Icon
└── NewDicomMerger.csproj           # C# Project File (PublishSingleFile=true, SelfContained=true)
```

---

*Dokumentations-Release 2.0 — Erstellt mit höchster wissenschaftlicher und technischer Präzision.*
