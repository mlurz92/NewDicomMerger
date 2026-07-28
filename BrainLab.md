# Brainlab Fibertracking DTI/DKI – vollständige technische Dateispezifikation

> **Zweck:** Verbindliche technische Übergabespezifikation für die Programmierung, Prüfung und Freigabe eines Konverters, der DTI-/DKI-Scanner-Daten in für **Brainlab Elements Fibertracking Version 2.0** geeignete DICOM-Datensätze überführt.
>
> **Ziel:** Kein bloßes „Zusammenführen von Bildern“, sondern eine vollständige, geometrisch, semantisch und DICOM-IOD-konforme Rekonstruktion der Diffusionsakquisition einschließlich **b-Werten, individuellen Gradientenrichtungen, optionaler B-Matrizen, Volumen-/Schichtdimensionen, Functional Groups, UIDs, Provenienz und Pixelintegrität**.

---

## 0. Dokumentstatus, Geltungsbereich und Verbindlichkeit

### 0.1 Geltungsbereich

Diese Spezifikation gilt für:

- DTI-Daten aus Single-Frame- oder Enhanced-MR-DICOM-Eingaben,
- Multi-Shell-Diffusionsdaten, die als DKI/HARDI akquiriert wurden,
- herstellerspezifische Scanner-DICOMs mit privaten Diffusionsattributen,
- die Konversion in **Enhanced MR Image Storage**,
- die Aufteilung von Multi-Shell-Daten in Brainlab-kompatible Single-Shell-DTI-Serien,
- die Prüfung vor einem Brainlab-Import,
- die technische Protokollierung und reproduzierbare Fehlerbehandlung.

Nicht Gegenstand ist die mathematische Durchführung von Fibertracking im Brainlab-System selbst.

### 0.2 Normative Priorität

Bei Widersprüchen gilt folgende Reihenfolge:

1. **Brainlab-Dokument 60918-15DE, Rev. 1.1** – gerätespezifische Anforderungen an Fibertracking v2.0.
2. **DICOM PS3.3, mindestens Edition 2016b** – von Brainlab ausdrücklich genannter Konformitätsstand.
3. Aktuelle DICOM-Ausgabe – für zusätzliche Validator-Kompatibilität, soweit keine post-2016-Funktion für die Interpretation zwingend benötigt wird.
4. Hersteller-DICOM-Conformance-Statements und private Tag-Dokumentation.
5. Diese Implementierungsspezifikation.

### 0.3 Schlüsselwörter

| Schlüsselwort | Bedeutung |
|---|---|
| **MUSS** | Ausgabe darf bei Nichterfüllung nicht als kompatibel freigegeben werden. |
| **DARF NICHT** | Harte Ausschlussbedingung. |
| **SOLL** | Brainlab-Empfehlung oder robuste Interoperabilitätsanforderung; Abweichung nur mit dokumentierter Warnung und Testimport. |
| **KANN** | Optional, sofern keine andere Regel verletzt wird. |
| **BLOCKER** | Verarbeitung abbrechen; keine klinisch freigabefähige Ausgabe. |
| **WARNUNG** | Ausgabe möglich, aber nicht ohne dokumentierte Prüfung freigeben. |

### 0.4 Zentrale Grundregel

> Ein technisch lesbares Multi-Frame-DICOM ist **nicht automatisch ein gültiges Enhanced-MR-Diffusionsobjekt**. Eine Datei mit `NumberOfFrames` und zusammenkopierten Pixeln ist unzureichend, wenn die erforderlichen Enhanced-MR-Module, Functional Groups, Dimensionsindizes oder framebezogenen Diffusionsattribute fehlen.

---

## 1. Begriffe und Datenmodell

| Begriff | Definition |
|---|---|
| **Frame** | Ein einzelnes zweidimensionales Bild innerhalb eines Multi-Frame-DICOM-Objekts. |
| **Schicht** | Räumliche Ebene innerhalb eines Volumens, bestimmt durch `ImagePositionPatient` und `ImageOrientationPatient`. |
| **Diffusionsvolumen** | Vollständiger räumlicher Schichtstapel mit identischem b-Wert und identischer Diffusionskodierung. |
| **b0-Volumen** | Volumen ohne relevante Diffusionsgewichtung; Zielwert `b=0`. |
| **Richtung** | Normierter Diffusionsgradient in DICOM-Patientenkoordinaten. |
| **Shell** | Gruppe nicht-nullgewichteter Volumina mit demselben nominalen b-Wert. |
| **DTI** | Typischerweise b0 plus ein Nonzero-Shell, vorzugsweise etwa b=1000 s/mm². |
| **DKI/Multi-Shell** | b0 plus mindestens zwei unterschiedliche Nonzero-Shells. |
| **B-Matrix** | Symmetrische 3×3-Matrix der Richtungs- und Stärkeinformation der Diffusionskodierung. |
| **Stack** | Räumlich zusammengehöriger, gleich orientierter Schichtstapel. |
| **Enhanced MR** | DICOM-SOP-Klasse `1.2.840.10008.5.1.4.1.1.4.1`. |
| **Functional Group** | DICOM-Sequenz zur gemeinsamen oder framebezogenen Beschreibung von Multi-Frame-Eigenschaften. |

---

## 2. Kompatibilitätsprofil – harte Gesamtbedingungen

Eine Ausgabe darf nur als **Brainlab Fibertracking DTI kompatibel** bewertet werden, wenn alle folgenden Bedingungen erfüllt sind:

1. **Kopf vollständig im FOV**; keine relevante kranio-kaudale Abschneidung, kein Wrap-around/Aliasing in das Gehirn.
2. **Mindestens 35 Schichten**.
3. **Keine Schichtlücken**.
4. **PatientPosition = HFS** (`Head First Supine`).
5. **Axiale oder axial-oblique Akquisition**; keine sagittale oder koronare Primärakquisition.
6. **Schichtdicke ≤ 3 mm** als Brainlab-Empfehlung; Überschreitung nicht automatisch reparieren.
7. **Quadratische Matrix**: `Rows == Columns`.
8. **Quadratische Pixel**: `PixelSpacing[0] == PixelSpacing[1]` innerhalb definierter numerischer Toleranz.
9. **Mindestens 6 Diffusionsrichtungen plus mindestens ein zugehöriges b0**.
10. **Empfohlen mindestens 20 Richtungen**.
11. b0 und alle Richtungsvolumina besitzen **identische Geometrie**.
12. b0 und Richtungsvolumina gehören in der finalen Brainlab-Ausgabe zur **gleichen Studie und gleichen Serie**.
13. Eine Brainlab-DTI-Serie enthält **genau einen Nonzero-b-Wert/Shell** plus zugehörige b0-Volumina.
14. Bei Wiederholungen besitzt **jede Diffusionsrichtung dieselbe Wiederholungszahl**.
15. Diffusionsinformationen sind **framebezogen** und nicht nur einmal auf Top-Level abgelegt.
16. Jeder Nonzero-Frame besitzt den richtigen individuellen Gradienten beziehungsweise die richtige B-Matrix.
17. Die Enhanced-MR-IOD-Struktur ist vollständig und validatorfähig.
18. Pixeldaten sind vollständig, numerisch unverändert oder nachvollziehbar transformiert und korrekt beschrieben.
19. Hochauflösende anatomische MR-Daten werden zusätzlich bereitgestellt: gleiche Studie oder Aufnahme innerhalb von maximal 12 Stunden.
20. Vor klinischer Freigabe erfolgen DICOM-IOD-Validierung, Brainlab-Testimport und visuelle Traktografie-Plausibilitätskontrolle.

---

## 3. Brainlab-Akquisitionsbedingungen

### 3.1 Sichtfeld und Abdeckung

#### MUSS

- Gesamter Kopf einschließlich Schädelkalotte und kaudaler Hirnanteile im Bildvolumen.
- Mindestens 35 räumlich eindeutige Schichten.
- Kein systematischer Slice-Dropout durch fehlende Dateien.
- Keine Lücke zwischen benachbarten Schichten.

#### SOLL

- FOV so gewählt, dass kein Aliasing in die intrakranielle Region projiziert wird.
- Volumenabdeckung visuell oder über eine freigegebene Pixel-QA bestätigen.

#### Implementierung

Die vollständige Kopfabdeckung ist **nicht allein aus DICOM-Tags sicher beweisbar**. Der Konverter muss daher:

- die geometrische z-Abdeckung berechnen,
- Anzahl und Lage der Schichten berichten,
- optional Vorschaubilder/Montage erzeugen,
- den Zustand `COVERAGE_NOT_VISUALLY_CONFIRMED` ausgeben, falls keine Pixel-QA erfolgt.

### 3.2 Patientenlagerung

#### MUSS

```text
PatientPosition (0018,0510) = HFS
```

Abweichende Lagerungen dürfen nicht stillschweigend auf `HFS` umgeschrieben werden.

#### BLOCKER

- `FFS`, `HFP`, `FFP`, sitzende oder unbekannte Lagerung,
- fehlendes `PatientPosition`, sofern es nicht zweifelsfrei aus konsistenten Quellinformationen rekonstruiert und als abgeleitet dokumentiert werden kann.

### 3.3 Orientierung

#### MUSS

- Primär axial oder axial-oblique.
- `ImageOrientationPatient` über alle Volumina identisch innerhalb Toleranz.

#### DARF NICHT

- sagittale oder koronare Akquisition als Brainlab-DTI-Primärserie ausgeben,
- schichtweise wechselnde Orientierung,
- unbemerkte Links-rechts-Spiegelung.

#### Obliquität

Brainlab erlaubt eine Winkelung, warnt aber vor extremen Winkeln wegen möglicher Farbverschiebung der Faserbahnen. Da kein numerischer Grenzwert vorgegeben ist:

- Winkel zur transversalen Patientenebene berechnen und berichten.
- axiale Klassifikation aus der Schichtnormalen ableiten.
- stark oblique Datensätze als `WARN_OBLIQUITY` kennzeichnen.
- keine erfundene harte Brainlab-Grenze in Grad behaupten.

### 3.4 Matrix, Pixel und Voxel

#### MUSS

```text
Rows (0028,0010) == Columns (0028,0011)
PixelSpacing[0] ≈ PixelSpacing[1]
```

Empfohlene numerische Vergleichstoleranz:

```text
abs(pxRow - pxCol) <= max(1e-4 mm, 1e-4 * max(pxRow, pxCol))
```

Diese Toleranz dient nur dem Floating-Point-Vergleich; sie darf keine real nicht-quadratischen Pixel „reparieren“.

#### SOLL

- isotrope Voxel, beispielsweise 2,0 × 2,0 × 2,0 mm³,
- Schichtdicke ≤ 3 mm,
- `SpacingBetweenSlices` konsistent mit der geometrisch aus `ImagePositionPatient` berechneten Distanz.

#### BLOCKER

- Matrix nicht quadratisch,
- Pixel nicht quadratisch,
- variable Matrix, variable Pixelgröße oder variable Schichtdicke innerhalb derselben Ausgabe,
- fehlende Geometrieattribute ohne zweifelsfreie Rekonstruktionsmöglichkeit.

### 3.5 Schichtabstand und Lückenfreiheit

Die geometrische Schichtdistanz muss aus den Patientenkoordinaten berechnet werden, nicht aus `SliceLocation` und nicht ausschließlich aus `InstanceNumber`.

```text
row = IOP[0..2]
col = IOP[3..5]
normal = normalize(cross(row, col))
sliceCoordinate = dot(IPP, normal)
```

Für sortierte Schichten:

```text
delta_i = sliceCoordinate[i+1] - sliceCoordinate[i]
```

#### MUSS

- alle `delta_i` innerhalb Toleranz gleich,
- keine doppelte Position,
- keine fehlende Position,
- Schichtpositionen in jedem Diffusionsvolumen identisch.

Empfohlene Toleranz:

```text
abs(delta_i - medianDelta) <= max(0.01 mm, 0.005 * medianDelta)
```

Zusätzliche Prüfung:

```text
abs(abs(medianDelta) - SpacingBetweenSlices) <= Geometrietoleranz
```

Falls `SpacingBetweenSlices` fehlt, ist die aus `ImagePositionPatient` berechnete Distanz maßgeblich.

---

## 4. Diffusionsakquisition: Richtungen, b0, Shells und Wiederholungen

### 4.1 Mindestzahl der Richtungen

| Status | Anzahl |
|---|---:|
| **Absolutes Brainlab-Minimum** | 6 Richtungen + b0 |
| **Brainlab-Empfehlung** | ≥20 Richtungen + b0 |
| **Für robuste klinische DTI-Praxis** | typischerweise 30–64 Richtungen |

Die Anzahl ist als Zahl **eindeutiger nicht-kollinearer Diffusionskodierungen** zu bestimmen. Identische Wiederholungen dürfen nicht als zusätzliche Richtungen gezählt werden.

### 4.2 b0-Bedingungen

#### MUSS

- mindestens ein b0-Volumen,
- gleiche Geometrie wie alle Nonzero-Volumina,
- `DiffusionBValue = 0`,
- `DiffusionDirectionality = NONE`,
- keine erfundene directional Gradientensequenz.

#### b0-Erkennung

Primär anhand des tatsächlichen b-Werts. Eine interne Toleranz darf verwendet werden, beispielsweise `0 ≤ b ≤ 10`, jedoch:

- Originalwert protokollieren,
- im Ziel nur dann exakt `0` schreiben, wenn die Akquisition semantisch ein b0 ist,
- niedrig gewichtete Nicht-b0-Volumes nicht ohne Prüfung auf b0 normalisieren.

### 4.3 Nonzero-Shell

Eine Brainlab-DTI-Serie darf nur enthalten:

```text
b0 + genau ein nominales Nonzero-Shell
```

Beispiele:

```text
ZULÄSSIG: b0 + b1000
BEDINGT:  b0 + b3000
NICHT ZULÄSSIG IN EINER SERIE: b0 + b1000 + b3000
```

### 4.4 b-Wert etwa 1000 s/mm²

Brainlab empfiehlt für die weiße Substanz einen b-Wert um 1000 s/mm².

- `b≈1000`: bevorzugte Brainlab-DTI-Serie.
- `b3000`: technisch als eigene Serie konvertierbar, aber außerhalb der Brainlab-Empfehlung; nur nach Import- und Traktografie-QA.
- b-Werte dürfen nicht ausschließlich aus dem Seriennamen abgeleitet werden, wenn framebezogene DICOM-/Private-Tags verfügbar sind.

### 4.5 Shell-Clustering

Der Konverter darf tatsächliche b-Werte nicht durch aggressive Rundung verfälschen.

#### Algorithmus

1. Roh-b-Wert je Frame extrahieren.
2. Innerhalb eines Volumens Konsistenz prüfen.
3. Volumen-b-Wert als Median der Schichtwerte bestimmen.
4. Shells anhand enger, konfigurierbarer Toleranz clustern.
5. Tatsächlichen b-Wert pro Frame erhalten.
6. Nominales Shell-Label separat speichern.

Empfohlener Standard:

```text
b < 50:    keine automatische Zusammenfassung außer explizitem b0-Profil
b >= 50:   max(abs(b - clusterMedian)) <= max(20, 0.03 * clusterMedian)
```

Bei unklaren Clustern: **BLOCKER**, keine automatische Vermischung.

### 4.6 Wiederholungen

Wiederholungen sind zulässig, wenn:

- jede Richtung dieselbe Wiederholungszahl besitzt,
- jede Wiederholung vollständig ist,
- Richtungszuordnung und Reihenfolge zweifelsfrei sind,
- Wiederholungen nicht als neue Richtungen gezählt werden.

b0-Wiederholungen dürfen zahlreicher sein als die Richtungswiederholungen, müssen aber eindeutig als b0 erkennbar sein.

---

## 5. DTI versus DKI – verbindliche Trennungslogik

### 5.1 Brainlab Fibertracking ist ein DTI-Zielprofil

Das zugrunde liegende Brainlab-Dokument beschreibt DTI/Fibertracking und unterstützt keine variierenden Nonzero-b-Werte innerhalb einer Aufnahme. Eine echte Multi-Shell-DKI-Serie darf daher **nicht unverändert** als Brainlab-DTI-Serie ausgegeben werden.

### 5.2 Erforderliche DKI-Aufteilung

Für Eingabe:

```text
b0 + b1000 + b3000
```

müssen mindestens folgende getrennte Ausgaben erzeugt werden:

```text
Serie 1: b0 + b1000
Serie 2: b0 + b3000   [optional/experimentell]
```

Jede Ausgabe erhält:

- eigene `SeriesInstanceUID`,
- eigene `SeriesNumber`,
- eindeutige `SeriesDescription`,
- eigene SOP-Instanz(en),
- vollständige Kopie der zugehörigen b0-Frames,
- nur einen Nonzero-Shell.

### 5.3 DKI-Modellierung außerhalb Brainlab

Ein vollständiger DKI-Fit benötigt Multi-Shell-Daten. Die Shell-Trennung für Brainlab ist **keine DKI-Analyse**, sondern eine Zielsystem-Konditionierung. Das Programm muss im Bericht ausdrücklich unterscheiden:

```text
AcquisitionType = MULTI_SHELL_DKI_OR_HARDI
BrainlabOutputType = SINGLE_SHELL_DTI_PROFILE
```

### 5.4 Keine Shell-Vermischung

DARF NICHT:

- b1000- und b3000-Frames in dasselbe Enhanced-MR-Brainlab-Objekt schreiben,
- b-Werte nur auf Top-Level differenzieren,
- b3000-Frames als b1000 umdeklarieren,
- b0 aus einer geometrisch oder phasenkodierungsseitig abweichenden TOPUP-Serie unkontrolliert ergänzen.

---

## 6. Hochauflösende anatomische Referenzdaten

Zusätzlich zur DTI-Ausgabe MUSS ein hochauflösender anatomischer MR-Datensatz bereitstehen.

### 6.1 Zeitliche Zuordnung

- gleiche Studie wie die DTI-Daten, **oder**
- Aufnahme maximal 12 Stunden von der DTI-Studie entfernt.

### 6.2 Technische Mindestbedingungen

- eindeutige Patientenidentität,
- korrekte Study-/Series-/Frame-of-Reference-Zuordnung,
- keine beschädigten UIDs,
- vollständige Kopfabdeckung,
- für Registrierung geeignete Bildqualität.

### 6.3 Konverterverhalten

Wenn keine anatomische Serie im Eingabepaket vorhanden ist:

```text
WARN_ANATOMY_MISSING
BrainlabPackageComplete = false
DTIFileConversionPossible = true
ClinicalPackageRelease = false
```

---

## 7. DICOM-Zielcontainer

### 7.1 Bevorzugte SOP-Klasse

```text
Enhanced MR Image Storage
SOP Class UID (0008,0016) = 1.2.840.10008.5.1.4.1.1.4.1
```

### 7.2 Transfer Syntax

Bevorzugt:

```text
Explicit VR Little Endian
Transfer Syntax UID = 1.2.840.10008.1.2.1
```

Andere Transfer Syntaxes nur nach nachgewiesenem Brainlab-Support.

### 7.3 Multi-Frame-Ausgabe

Mögliche Struktur:

- ein Enhanced-MR-Objekt pro Brainlab-Shell-Serie, oder
- DICOM-Concatenation bei Größen-/Implementierungsgrenzen.

Concatenations nur nach explizitem Brainlab-Test. Ein einzelnes Objekt ist für die erste Interoperabilitätsvalidierung vorzuziehen.

### 7.4 DICOM-Datei-Meta-Information

MUSS korrekt enthalten:

- `FileMetaInformationVersion (0002,0001)`,
- `MediaStorageSOPClassUID (0002,0002)`,
- `MediaStorageSOPInstanceUID (0002,0003)`,
- `TransferSyntaxUID (0002,0010)`,
- `ImplementationClassUID (0002,0012)`,
- `ImplementationVersionName (0002,0013)`.

Meta-SOP-UIDs müssen exakt zu Dataset-SOP-UIDs passen.

---

## 8. 16-Bit-Pixelformat

Brainlab fordert Speicherung im 16-Bit-DICOM-Format.

### 8.1 Minimal sichere Interpretation

```text
BitsAllocated (0028,0100) = 16
```

### 8.2 Konsistente Pixelattribute

Zusätzlich MUSS gelten:

```text
SamplesPerPixel (0028,0002) = 1
PhotometricInterpretation (0028,0004) = MONOCHROME2
BitsStored (0028,0101) <= 16
HighBit (0028,0102) = BitsStored - 1
PixelRepresentation (0028,0103) = 0 oder 1 entsprechend Quelle
```

### 8.3 Kein blindes Umschreiben auf 16/16

`BitsStored=12` darf nicht lediglich durch Headeränderung auf `16` gesetzt werden, wenn:

- Padding-/Sign-Bits nicht geprüft wurden,
- signierte Werte vorliegen,
- Intensitätswerte verschoben oder skaliert würden.

Zwei zulässige Strategien:

1. **Bitgenaue Erhaltung**: `BitsAllocated=16`, ursprüngliches `BitsStored`, korrekter `HighBit`.
2. **Numerisch äquivalente Normalisierung auf 16/16**: nur mit kontrollierter Dekodierung und erneuter Kodierung, ohne Änderung des physikalischen Pixelwerts; Rescale-Semantik korrekt aktualisieren.

### 8.4 PixelData-Integrität

Vor Ausgabe MUSS geprüft werden:

```text
expectedBytes = Rows * Columns * SamplesPerPixel * bytesPerSample * NumberOfFrames
```

- unkomprimierte PixelData-Länge exakt erwartbar,
- DICOM-Padding auf gerade Value Length korrekt,
- keine fehlenden oder zusätzlichen Frames,
- Hash/CRC je Quellframe und nach Wiederdekodierung optional vergleichen,
- keine Fensterung in PixelData einrechnen,
- keine lossy Kompression für klinische Standardausgabe.

---

## 9. Enhanced-MR-IOD – erforderliche Grundstruktur

### 9.1 Top-Level-Pflichtbereiche

Mindestens korrekt abzubilden:

- Patient Module,
- General Study Module,
- General Series Module,
- Frame of Reference Module,
- General Equipment Module,
- Enhanced General Equipment Module,
- Enhanced MR Image Module,
- Image Pixel Module,
- Multi-frame Functional Groups Module,
- Multi-frame Dimension Module,
- SOP Common Module.

### 9.2 Functional-Group-Container

```text
Shared Functional Groups Sequence    (5200,9229)
Per-Frame Functional Groups Sequence (5200,9230)
```

#### MUSS

- Shared Sequence: genau ein Item.
- Per-Frame Sequence: genau ein Item pro Frame.
- Anzahl Per-Frame-Items exakt gleich `NumberOfFrames`.
- Eigenschaften nur Shared ablegen, wenn sie wirklich für alle Frames identisch sind.
- Framebezogene Diffusionsattribute nicht top-level als Ersatz speichern.

---

## 10. Obligatorische und bedingte Functional Groups

### 10.1 Pixel Measures Macro – obligatorisch

```text
Pixel Measures Sequence (0028,9110)
  Pixel Spacing         (0028,0030)
  Slice Thickness       (0018,0050)
  Spacing Between Slices(0018,0088) [wenn verfügbar/semantisch gültig]
```

Shared, sofern identisch.

### 10.2 Plane Position (Patient) – obligatorisch

```text
Plane Position Sequence (0020,9113)
  Image Position Patient (0020,0032)
```

Typischerweise per frame.

### 10.3 Plane Orientation (Patient) – obligatorisch

```text
Plane Orientation Sequence (0020,9116)
  Image Orientation Patient (0020,0037)
```

Shared, sofern identisch.

### 10.4 Frame Content – obligatorisch und immer per frame

```text
Frame Content Sequence      (0020,9111)
  Stack ID                   (0020,9056)
  In-Stack Position Number   (0020,9057)
  Frame Acquisition Number   (0020,9156)
  Dimension Index Values     (0020,9157)
```

### 10.5 Frame Anatomy – obligatorisch

```text
Frame Anatomy Sequence (0020,9071)
  Frame Laterality      (0020,9072) [falls zutreffend]
  Anatomic Region Sequence (0008,2218)
```

Für Kopf-DTI geeignete, standardisierte anatomische Kodierung verwenden. Keine frei erfundenen Codewerte.

### 10.6 Pixel Value Transformation – bei MONOCHROME2 erforderlich

```text
Pixel Value Transformation Sequence (0028,9145)
  Rescale Intercept (0028,1052)
  Rescale Slope     (0028,1053)
  Rescale Type      (0028,1054)
```

Top-Level-Rescale allein ersetzt das Functional Group Macro im Enhanced MR nicht.

### 10.7 MR Image Frame Type – obligatorisch

```text
MR Image Frame Type Sequence (0018,9226)
  Frame Type             (0008,9007)
  Pixel Presentation     (0008,9205)
  Volumetric Properties  (0008,9206)
  Volume Based Calculation Technique (0008,9207)
  Complex Image Component(0008,9208)
  Acquisition Contrast   (0008,9209)
```

Für unveränderte DWI-Magnitude-Originalframes typischerweise sinngemäß:

```text
FrameType = ORIGINAL\PRIMARY\DIFFUSION\NONE
PixelPresentation = MONOCHROME
VolumetricProperties = VOLUME
ComplexImageComponent = MAGNITUDE
AcquisitionContrast = DIFFUSION
```

Die exakten Defined Terms müssen gegen die verwendete DICOM-Edition validiert werden.

### 10.8 Weitere MR-Makros

Bei `ORIGINAL` oder `MIXED` sind je nach IOD-Bedingung insbesondere zu berücksichtigen:

- MR Timing and Related Parameters `(0018,9112)`,
- MR FOV/Geometry `(0018,9125)`,
- MR Echo `(0018,9114)`,
- MR Modifier `(0018,9115)`,
- MR Imaging Modifier,
- MR Receive Coil `(0018,9042)`,
- MR Transmit Coil `(0018,9049)`,
- MR Averages `(0018,9119)`.

Der Konverter darf diese Makros nicht pauschal weglassen, wenn die IOD-Bedingung erfüllt ist.

---

## 11. MR Diffusion Macro – zentrale Pflichtstruktur

### 11.1 Sequenz

```text
MR Diffusion Sequence (0018,9117)
```

- pro Frame genau ein Item,
- bei originalen Diffusionsframes verpflichtend,
- in Per-Frame Functional Groups, sofern b-Wert/Richtung zwischen Frames variieren.

### 11.2 b-Wert

```text
Diffusion b-value (0018,9087)
```

- Einheit gemäß DICOM: s/mm²,
- tatsächlicher framebezogener Wert,
- nicht nur nominaler Serienname,
- keine Shell-übergreifende Top-Level-Angabe.

### 11.3 Directionality

```text
Diffusion Directionality (0018,9075)
```

Zulässige relevante Werte:

- `NONE` für b0,
- `DIRECTIONAL` bei explizitem Gradientenvektor,
- `BMATRIX` bei vollständiger B-Matrix,
- `ISOTROPIC` nur für tatsächlich isotrop diffusionsgewichtete/abgeleitete Frames, nicht für normale Richtungs-DWI.

### 11.4 b0-Frame

```text
MR Diffusion Sequence
  Diffusion b-value = 0
  Diffusion Directionality = NONE
```

DARF NICHT:

- `DIRECTIONAL` mit `0\0\0`,
- zufälligen Gradienten aus einer Nachbaraufnahme übernehmen,
- eine B-Matrix aus einem Nonzero-Frame kopieren.

### 11.5 Richtungsframe – DIRECTIONAL-Profil

```text
MR Diffusion Sequence
  Diffusion b-value = <actual>
  Diffusion Directionality = DIRECTIONAL
  Diffusion Gradient Direction Sequence (0018,9076)
    Diffusion Gradient Orientation (0018,9089) = gx\gy\gz
```

### 11.6 Richtungsframe – BMATRIX-Profil

Bevorzugt, wenn eine valide vollständige B-Matrix vorhanden ist:

```text
MR Diffusion Sequence
  Diffusion b-value = <actual>
  Diffusion Directionality = BMATRIX
  Diffusion Gradient Direction Sequence (0018,9076) [zulässig und empfohlen]
    Diffusion Gradient Orientation (0018,9089) = gx\gy\gz
  Diffusion b-matrix Sequence (0018,9601)
    Diffusion b-value XX (0018,9602)
    Diffusion b-value XY (0018,9603)
    Diffusion b-value XZ (0018,9604)
    Diffusion b-value YY (0018,9605)
    Diffusion b-value YZ (0018,9606)
    Diffusion b-value ZZ (0018,9607)
```

### 11.7 Ein-Item-Regel

Folgende Sequenzen enthalten je Frame maximal/genau ein Item entsprechend DICOM-Typ:

- MR Diffusion Sequence,
- Diffusion Gradient Direction Sequence,
- Diffusion b-matrix Sequence.

---

## 12. Gradientensystem und Koordinaten

### 12.1 DICOM-Patientenkoordinaten

`DiffusionGradientOrientation (0018,9089)` bezieht sich auf das Patientensystem:

- X: rechts → links,
- Y: anterior → posterior,
- Z: Fuß → Kopf.

Dies entspricht DICOM-LPS-Konvention.

### 12.2 Normierung

Für Richtungsvektor `g`:

```text
norm = sqrt(gx² + gy² + gz²)
```

#### MUSS

- `norm > 0`,
- Vektor auf Einheitslänge normalisieren, wenn nur numerisches Rundungsrauschen vorliegt,
- Rohwert und normalisierten Wert im Report speichern.

Empfohlene Akzeptanz:

```text
0.98 <= norm <= 1.02  -> normalisierbar
außerhalb             -> BLOCKER, sofern keine B-Matrix die Richtung zweifelsfrei rekonstruiert
```

### 12.3 Keine unkontrollierte Achsenkonversion

DARF NICHT:

- FSL-bvecs direkt in DICOM schreiben,
- RAS-Vektoren ohne RAS→LPS-Transformation übernehmen,
- Scanner-Koordinaten als Patientenkoordinaten deklarieren,
- pauschal X oder Y negieren, nur weil Daten von Siemens/GE/Philips stammen.

### 12.4 Transformation bei Resampling

Bei räumlicher Rotation mit Rotationsmatrix `R`:

```text
g_new = R * g_old
B_new = R * B_old * R^T
```

Danach:

- Gradient neu normieren,
- B-Matrix auf Symmetrie prüfen,
- neue IOP/IPP verwenden,
- Pixel-/Geometrie-Metadaten aktualisieren,
- Ausgabe als `DERIVED` kennzeichnen.

### 12.5 Vorzeichenäquivalenz

Für lineare Diffusionskodierung sind `g` und `-g` bezüglich `g gᵀ` physikalisch äquivalent. Trotzdem MUSS der gespeicherte Scannervektor unverändert beibehalten werden, sofern keine dokumentierte Transformation erfolgt.

---

## 13. B-Matrix – mathematische Prüfung

### 13.1 Aufbau

Aus sechs unabhängigen Elementen:

```text
B = [ Bxx Bxy Bxz
      Bxy Byy Byz
      Bxz Byz Bzz ]
```

### 13.2 Pflichtprüfungen

1. Alle Werte endlich, keine NaN/Infinity.
2. Matrix symmetrisch.
3. `trace(B)` plausibel zum b-Wert.
4. Eigenwerte numerisch plausibel; keine grob negative Diffusionssensitivität.
5. Haupteigenvektor parallel oder antiparallel zum Gradienten.

Empfohlene Toleranzen:

```text
abs(trace(B) - b) <= max(30, 0.05 * b)
minEigenvalue >= -max(5, 0.01 * b)
abs(dot(normalize(g), principalEigenvector(B))) >= 0.98
```

### 13.3 Keine unkritische Einheitenskalierung

Wenn die private B-Matrix numerisch bereits eine Spur um 1000 beziehungsweise 3000 aufweist, darf keine zusätzliche Multiplikation oder Division um 1000 erfolgen. Maßgeblich ist die numerische Konsistenz mit `DiffusionBValue` und der Herstellerkonformität.

### 13.4 Rekonstruktion aus Gradient

Wenn keine B-Matrix vorhanden, aber ein sicherer linearer Gradient `g` und b-Wert `b`:

```text
B = b * (g * g^T)
```

Diese Rekonstruktion ist nur zulässig, wenn:

- lineare Diffusionskodierung zweifelsfrei vorliegt,
- keine tensor-valued/bipolare komplexe Kodierung mit abweichender B-Matrix vorliegt,
- Herkunft im Report dokumentiert wird.

---

## 14. Multi-Frame-Dimensionen und Frame-Reihenfolge

### 14.1 Dimension Organization

MUSS enthalten:

```text
Dimension Organization Sequence (0020,9221)
  Dimension Organization UID    (0020,9164)

Dimension Index Sequence        (0020,9222)
```

### 14.2 Minimales robustes 4D-Dimensionsmodell

Empfohlene Dimensionen:

1. räumliche Position über `InStackPositionNumber (0020,9057)`,
2. Diffusionsvolumen über `FrameAcquisitionNumber (0020,9156)`.

`FrameAcquisitionNumber` ist pro Volumen konstant und beginnt bei 1.

### 14.3 In-Stack-Nummerierung

Für jedes Volumen neu:

```text
Volumen 1: 1, 2, ..., nSlices
Volumen 2: 1, 2, ..., nSlices
...
```

DARF NICHT:

```text
1, 2, 3, ..., NumberOfFrames
```

über alle Volumina fortlaufend als alleinige In-Stack-Nummerierung.

### 14.4 Frame Acquisition Number

```text
b0 volume 1      -> 1
b0 volume 2      -> 2
...
direction 1     -> k
...
```

Alle Schichten desselben Volumens tragen dieselbe `FrameAcquisitionNumber`.

### 14.5 Dimension Index Values

Pro Frame zwei Werte in der Reihenfolge der Dimension Index Sequence:

```text
[InStackPositionOrdinal, VolumeOrdinal]
```

Jedes Tupel muss innerhalb der Dimension Organization eindeutig sein.

### 14.6 Physische Frame-Reihenfolge

Empfohlen:

```text
for volume in orderedVolumes:
    for slice in orderedSlices:
        appendFrame(volume, slice)
```

Der Parser darf sich später nicht ausschließlich auf die physische Reihenfolge verlassen; Functional Groups und Dimensionen müssen vollständig sein.

### 14.7 Sortierkriterien

Schichten:

```text
sliceCoordinate = dot(IPP, sliceNormal)
```

Volumina:

1. ursprüngliche Acquisition Number/Frame Acquisition Number,
2. AcquisitionDateTime,
3. b-Wert,
4. Gradient-/B-Matrix-Signatur,
5. stabiler Quellindex als letzter Tie-Breaker.

Keine Sortierung ausschließlich nach Dateiname oder `InstanceNumber`.

---

## 15. Serien-, Studien- und UID-Regeln

### 15.1 Beizubehalten

Soweit korrekt und klinisch gewünscht:

- `StudyInstanceUID`,
- `FrameOfReferenceUID`,
- Patient-/Study-Zuordnung,
- Study Date/Time,
- anatomisch relevante Referenzen.

### 15.2 Neu zu erzeugen

Für jede konvertierte Shell-Serie:

- neue `SeriesInstanceUID`,
- neue `SOPInstanceUID` je Objekt,
- gegebenenfalls neue `ConcatenationUID`,
- neue eindeutige `SeriesNumber`,
- eindeutige `SeriesDescription`.

### 15.3 UID-Anforderungen

- gültige numerische DICOM-UID,
- maximal 64 Zeichen,
- keine Kollision innerhalb oder außerhalb des Batches,
- keine UID durch simples Anhängen erzeugen, wenn dadurch 64 Zeichen überschritten werden,
- SOP UID im File Meta Header und Dataset identisch.

### 15.4 Serienbeschreibung

Empfohlenes Schema:

```text
BL_FT_B0_B1000_64DIR
BL_FT_B0_B3000_64DIR
```

Keine alleinige Eignungsentscheidung aufgrund des Namens.

### 15.5 Gleiche Studie und Serie

Brainlab verlangt b0 und Richtungen in derselben Studie und Serie. Deshalb:

- b0-Frames und zugehörige Richtungsframes gemeinsam in **eine** finale `SeriesInstanceUID`,
- Multi-Shell-Ausgaben erhalten getrennte Series UIDs,
- b0-Frames werden für jede Shell-Ausgabe als neue SOP-Instanzkopie eingebettet.

---

## 16. Original versus Derived und Provenienz

### 16.1 Verlustfreies Repacking

Wenn ausschließlich:

- Originalpixel byte-/wertidentisch übernommen,
- keine Interpolation,
- keine Bewegungskorrektur,
- keine Verzerrungskorrektur,
- keine Intensitätstransformation,
- nur DICOM-Struktur und Metadaten standardisiert,

kann scannernahe `ORIGINAL`-Semantik vertretbar sein. Dies muss mit dem eingesetzten Validator und der institutionellen Qualitätsstrategie abgestimmt werden.

### 16.2 Abgeleitete Ausgabe

MUSS als `DERIVED` gekennzeichnet werden bei:

- TOPUP/EDDY,
- Resampling,
- Rotation,
- Interpolation,
- Denoising mit Pixeländerung,
- Gibbs-Ringing-Korrektur,
- Intensitätsnormalisierung,
- Zusammenführung aus bereits abgeleiteten Quellen.

### 16.3 Provenienz

Bei `DERIVED` MUSS enthalten sein:

- `DerivationDescription (0008,2111)`,
- `DerivationImageSequence`/Source Image Referenzen,
- Referenz auf jede Quell-SOP-Instanz oder nachvollziehbare Conversion Source,
- verwendete Software, Version und Parameter,
- Transformationsmatrix beziehungsweise Transformationsbeschreibung,
- Hashes der Quell- und Zieldaten im externen Report.

### 16.4 Keine falsche Herkunft

DARF NICHT:

- `DERIVED` ohne Quellreferenzen,
- `ORIGINAL` nach Resampling,
- Quell-SOP-UIDs kopieren und zugleich neue Pixel erzeugen,
- private Herstellerattribute unbesehen als aktuell gültige Scannerwerte behalten, wenn sie nach Verarbeitung nicht mehr stimmen.

---

## 17. Eingabedaten – allgemeine Voraussetzungen

### 17.1 Zulässige Eingaben

- klassische `MR Image Storage` Single-Frame-DICOMs,
- valide Enhanced-MR-Diffusionsobjekte,
- ZIP-Archive oder Ordnerstrukturen mit vollständigen DICOM-Dateien.

### 17.2 Eingabeselektion

Primär auswählen:

```text
Modality = MR
ImageType enthält ORIGINAL und DIFFUSION, soweit herstellerseitig korrekt
b-Wert-Attribut vorhanden oder sicher extrahierbar
PixelData vorhanden
```

Ausschließen:

- ADC,
- TRACEW,
- FA,
- Color FA,
- Tensor-B0-Maps,
- Secondary Capture,
- Screenshots,
- Localizer/Scout,
- Phoenix-/Protocol-Reports,
- Derived maps,
- TOPUP reverse-PE b0 aus direkter Brainlab-Serie.

### 17.3 Gruppierungsgrenzen

Dateien dürfen nur gemeinsam verarbeitet werden, wenn übereinstimmend:

- Patient/Study-Kontext,
- `StudyInstanceUID`,
- ursprüngliche `SeriesInstanceUID` oder explizit validierte Serienbeziehung,
- `FrameOfReferenceUID`,
- Matrix,
- PixelSpacing,
- Orientierung,
- Schichtpositionen,
- Schichtdicke/-abstand,
- Echo-/Sequenzparameter soweit für eine gemeinsame Akquisition erforderlich.

### 17.4 Vermischung verhindern

BLOCKER bei:

- mehreren Patienten,
- unterschiedlichen Studien ohne explizite Zuordnung,
- unterschiedlichen Frame-of-Reference-UIDs,
- unterschiedlicher Geometrie,
- fehlenden Volumina,
- nicht auflösbaren Dubletten,
- Richtungsmetadaten nur in einem Teil der Dateien.

---

## 18. Multi-Vendor-Extraktionskaskade

### 18.1 Grundsatz

Standard-DICOM-Attribute haben Vorrang. Private Tags sind nur zulässig, wenn Hersteller, Private Creator, VR, VM und Semantik explizit bekannt und validiert sind.

### 18.2 Priorität

1. MR Diffusion Sequence in Per-Frame Functional Groups.
2. MR Diffusion Sequence in Shared Functional Groups.
3. öffentliche Diffusionsattribute im zulässigen IOD-Kontext.
4. herstellerspezifische private Tags mit korrektem Private Creator.
5. Protokoll-/Phoenix-Report als zusätzliche Plausibilisierung.
6. Serienname/Regex nur als Hinweis, niemals alleinige klinische Freigabegrundlage.

### 18.3 Siemens XA/Single-Frame-Profil

Bekannte relevante Attribute:

```text
Private Creator                     (0019,0010) = SIEMENS MR HEADER
B_value                             (0019,100C)
DiffusionDirectionality             (0019,100D)
DiffusionGradientDirection          (0019,100E) VM 3
B_matrix                            (0019,1027) VM 6
```

Mapping:

```text
(0019,100C) -> (0018,9087)
(0019,100E) -> (0018,9089)
(0019,1027) -> (0018,9602..9607)
```

MUSS:

- Private Creator prüfen,
- je Quellbild/Frame auslesen,
- nicht nur ersten Vektor der Serie übernehmen,
- über alle Schichten eines Volumens Gleichheit prüfen,
- B-Matrix gegen Gradient und b-Wert validieren.

### 18.4 GE-/Philips-Fallbacks

Die bestehende Anwendung kann herstellerspezifische b-Wert-Fallbacks unterstützen, beispielsweise GE- und Philips-Private-Tags. Diese dürfen nur aktiviert werden, wenn:

- Hersteller exakt erkannt,
- Private Creator passend,
- Tagtyp und Wertsemantik per Testdatensatz validiert,
- Gradient und B-Matrix ebenfalls extrahiert werden können.

Ein vorhandener b-Wert allein reicht nicht für Fibertracking. Fehlt der individuelle Gradient, ist die Ausgabe **BLOCKIERT**.

### 18.5 Regex-Fallback

Regex aus `SeriesDescription`, `ProtocolName` oder `SequenceName` darf ausschließlich:

- Shell-Verdacht anzeigen,
- Suchfilter unterstützen,
- eine Warnung erzeugen.

DARF NICHT:

- framebezogene b-Werte ersetzen,
- Richtungsvektoren erzeugen,
- eine Brainlab-Freigabe ermöglichen.

---

## 19. Volumenrekonstruktion aus Single-Frame-DICOMs

### 19.1 Frame-Signatur

Für jedes Quellbild erfassen:

```text
StudyInstanceUID
SeriesInstanceUID
FrameOfReferenceUID
AcquisitionNumber
Temporal/Acquisition timestamps
Rows, Columns
PixelSpacing
ImageOrientationPatient
ImagePositionPatient
SliceThickness
SpacingBetweenSlices
b-value
Gradient vector
B-matrix
SOPInstanceUID
Pixel hash
```

### 19.2 Volumenschlüssel

Empfohlen:

```text
VolumeKey =
  StudyInstanceUID
  + SeriesInstanceUID
  + AcquisitionNumber/VolumeOrdinal
  + b-value cluster
  + gradient/B-matrix signature
```

`AcquisitionNumber` allein ist nicht ausreichend, wenn Scanner oder Exporter Werte wiederverwendet.

### 19.3 Vollständigkeitsprüfung

Pro Volumen:

- exakt eine Datei pro räumlicher Schichtposition,
- einheitlicher b-Wert,
- einheitlicher Gradient/B-Matrix für Nonzero-Volumen,
- keine wechselnde Orientierung,
- identische Zahl und Lage der Schichten wie Referenzvolumen.

### 19.4 Referenz-Schichtgitter

Das erste valide vollständige b0 oder Nonzero-Volumen definiert das Referenzgitter. Jedes weitere Volumen muss positionsweise matchen.

Empfohlener Positionsvergleich:

```text
abs(sliceCoordinate_current - sliceCoordinate_reference) <= 0.01 mm
```

oder konfigurierbare strengere/lockerere Toleranz mit Bericht.

### 19.5 Dubletten

- gleiche SOPInstanceUID mit unterschiedlichem Inhalt: BLOCKER,
- gleiche Position/Volumen mit identischem Pixelhash: kontrollierte Dublette, standardmäßig BLOCKER oder explizit deduplizierbar,
- gleiche Position/Volumen mit unterschiedlichem Pixelhash: BLOCKER.

---

## 20. Konversionsalgorithmus – verbindlicher Ablauf

### Phase A – Discovery

1. ZIP sicher entpacken; Path Traversal verhindern.
2. Alle Dateien DICOM-basiert erkennen, nicht nur über Erweiterung.
3. File Meta und Dataset parsen.
4. Serien inventarisieren.
5. abgeleitete und irrelevante Serien klassifizieren.

### Phase B – Diffusionsmetadaten

1. b-Wert je Frame extrahieren.
2. Gradient je Frame extrahieren.
3. B-Matrix je Frame extrahieren.
4. Koordinatensystem bestimmen.
5. numerische Validierung durchführen.
6. Provenienz jeder extrahierten Information speichern.

### Phase C – Geometrie

1. IOP validieren und orthonormalisieren nur bei Rundungsfehlern.
2. Schichtnormale berechnen.
3. IPP projizieren.
4. Schichten sortieren.
5. Volumina bilden.
6. Referenzgitter vergleichen.
7. Lücken/Dubletten erkennen.

### Phase D – Shell-Plan

1. b0-Volumina identifizieren.
2. Nonzero-Shells clustern.
3. Richtungszahl pro Shell berechnen.
4. Wiederholungszahl prüfen.
5. Brainlab-Ausgabeplan erzeugen.
6. b0 für jede Shell-Ausgabe zuordnen.

### Phase E – Enhanced-MR-Erzeugung

1. neue UIDs erzeugen.
2. Top-Level-Module schreiben.
3. Shared Functional Groups schreiben.
4. Dimension Organization und Dimension Index Sequence schreiben.
5. Frames volumenweise und schichtweise anordnen.
6. Per-Frame Functional Groups vollständig schreiben.
7. PixelData zusammensetzen.
8. Provenienz schreiben.
9. Datei speichern.

### Phase F – Post-Write-Validation

1. Datei erneut mit unabhängiger Reader-Instanz öffnen.
2. NumberOfFrames prüfen.
3. alle FG-Items zählen.
4. PixelData vollständig dekodieren.
5. b-Werte/Gradienten/B-Matrizen zurücklesen.
6. Positions- und Dimensionsmodell rekonstruieren.
7. Hash-/Wertvergleich durchführen.
8. DICOM-IOD-Validator ausführen.
9. Bericht erzeugen.

---

## 21. Pseudocode der Shell-Konversion

```csharp
InputDataset ScanAndParse(IEnumerable<string> paths)
{
    var frames = ReadAllDicomFrames(paths);
    ValidateSinglePatientStudyContext(frames);
    ExtractGeometry(frames);
    ExtractDiffusionMetadata(frames);
    return BuildVolumes(frames);
}

IReadOnlyList<BrainlabOutputPlan> BuildBrainlabPlans(InputDataset input)
{
    var b0Volumes = input.Volumes.Where(v => IsB0(v.BValue)).ToList();
    if (b0Volumes.Count == 0)
        throw new Blocker("NO_B0_VOLUME");

    var shells = ClusterNonZeroShells(input.Volumes);
    var plans = new List<BrainlabOutputPlan>();

    foreach (var shell in shells)
    {
        ValidateDirectionCount(shell);
        ValidateEqualRepetitions(shell);
        ValidateIdenticalGeometry(b0Volumes, shell.Volumes);

        plans.Add(new BrainlabOutputPlan
        {
            StudyInstanceUid = input.StudyInstanceUid,
            NewSeriesInstanceUid = GenerateDicomUid(),
            Volumes = OrderVolumes(b0Volumes, shell.Volumes),
            NominalBValue = shell.NominalBValue,
            IsPreferred = IsApproximately1000(shell.NominalBValue)
        });
    }

    return plans;
}
```

---

## 22. TOPUP-/EDDY- und Reverse-PE-Daten

### 22.1 Direkter Brainlab-Import

Reverse-PE-b0-Serien dürfen nicht direkt in die Brainlab-DTI-Serie gemischt werden, wenn:

- Phasenkodierrichtung abweicht,
- Geometrie nicht exakt gleich,
- sie nur für Suszeptibilitätskorrektur vorgesehen sind.

### 22.2 Externe Korrektur

Zulässiger Workflow:

```text
Original DICOM
-> dcm2niix/NIfTI
-> TOPUP
-> EDDY
-> rotated b-vectors
-> Rücktransformation in DICOM-LPS
-> neues DERIVED Enhanced MR DICOM
```

### 22.3 Pflicht nach EDDY

- `eddy_rotated_bvecs` verwenden,
- FSL-Koordinaten nicht direkt in DICOM schreiben,
- Rotation in Patientensystem rekonstruieren,
- B-Matrix entsprechend rotieren,
- neue Geometrie vollständig schreiben,
- Quell- und Verarbeitungskette dokumentieren.

### 22.4 Erster Kompatibilitätstest

Für initiale Brainlab-Interoperabilität SOLL zuerst ein verlustfreies Repacking ohne TOPUP/EDDY erzeugt werden. Dadurch werden DICOM- und Zielsystemfehler von Preprocessing-Fehlern getrennt.

---

## 23. Abgeleitete Serien – strikter Ausschluss

Nicht als Fibertracking-Rohdaten verwenden:

| Serie | Grund |
|---|---|
| ADC | bereits abgeleitet, keine individuellen DWI-Richtungen |
| TRACEW | richtungsgemittelt/abgeleitet |
| FA | Tensorergebnis, keine Rohdaten |
| Color FA | RGB-/Secondary-Capture- oder Derived Map |
| TENSOR_B0 | abgeleitete Referenz |
| Screenshots/SC | fehlende MR-Diffusionssemantik |
| Localizer | keine DTI-Akquisition |
| Phoenix-/Protocol-Report | Metadatenquelle, keine Pixel-Rohserie |

Ein Serienname kann variieren; die Entscheidung muss auf `ImageType`, SOP Class, Functional Groups und Diffusionsattributen basieren.

---

## 24. De-Identifizierung und Private Tags

### 24.1 Reihenfolge

Diffusionsmetadaten müssen **vor** Entfernung privater Tags extrahiert werden.

```text
Parse -> Extract -> Validate -> Map to public tags -> De-identify -> Write
```

### 24.2 Whitelist

Private Tags dürfen nur temporär erhalten werden, wenn:

- korrekter Private Creator,
- bekannte Semantik,
- für b-Wert/Gradient/B-Matrix erforderlich.

Nach erfolgreicher Überführung in öffentliche DICOM-Attribute können sie entfernt werden, sofern keine weitere notwendige Information verloren geht.

### 24.3 Rekursive De-Identifizierung

- Sequenzen rekursiv prüfen,
- Referenced SOP UIDs konsistent remappen,
- Study-/Series-/Frame-of-Reference-Beziehungen erhalten,
- eingebrannte Annotationen gesondert prüfen,
- keine diffusionsrelevanten Attribute entfernen.

### 24.4 BLOCKER

Wenn eine Anonymisierung bereits vor Eingang individuelle Gradienten oder B-Matrizen entfernt hat und keine sichere Rekonstruktion möglich ist:

```text
DIFFUSION_METADATA_IRREVERSIBLY_LOST
```

Keine Tensor-/Fibertracking-Ausgabe erzeugen.

---

## 25. Fehler- und Warnklassifikation

### 25.1 Harte Blocker

| Code | Bedeutung |
|---|---|
| `NO_B0_VOLUME` | kein b0 vorhanden |
| `TOO_FEW_DIRECTIONS` | <6 eindeutige Richtungen |
| `MISSING_GRADIENT` | Nonzero-Volumen ohne individuellen Vektor/B-Matrix |
| `GRADIENT_INCONSISTENT_WITHIN_VOLUME` | schichtweise wechselnde Richtung |
| `MISSING_BVALUE` | b-Wert nicht sicher bestimmbar |
| `MIXED_NONZERO_SHELLS_IN_OUTPUT` | mehrere Nonzero-b-Werte in einer Zielserie |
| `GEOMETRY_MISMATCH` | Matrix/FOV/IOP/IPP/Schichtzahl inkonsistent |
| `NON_SQUARE_MATRIX` | Rows != Columns |
| `NON_SQUARE_PIXELS` | asymmetrisches PixelSpacing |
| `PATIENT_POSITION_NOT_HFS` | Lagerung nicht HFS |
| `NON_AXIAL_ACQUISITION` | sagittal/koronar |
| `SLICE_GAP_OR_MISSING_SLICE` | Lücke oder fehlende Position |
| `PIXELDATA_TRUNCATED` | Pixeldaten unvollständig |
| `UID_COLLISION` | nicht eindeutige UID |
| `ENHANCED_MR_IOD_INVALID` | Validatorfehler in Pflichtmodulen |
| `DIFFUSION_METADATA_IRREVERSIBLY_LOST` | Vektoren/B-Matrizen nicht rekonstruierbar |

### 25.2 Warnungen

| Code | Bedeutung |
|---|---|
| `DIRECTION_COUNT_BELOW_RECOMMENDATION` | 6–19 Richtungen |
| `BVALUE_NOT_AROUND_1000` | beispielsweise b3000 |
| `VOXELS_NOT_ISOTROPIC` | nicht isotrop, sonst geometrisch zulässig |
| `SLICE_THICKNESS_OVER_3MM` | Brainlab-Empfehlung verletzt |
| `HIGH_OBLIQUITY` | starke Winkelung |
| `ANATOMY_MISSING` | anatomische Referenz fehlt |
| `COVERAGE_NOT_VISUALLY_CONFIRMED` | Kopfabdeckung nur geometrisch geprüft |
| `DERIVED_DATASET` | Pixel wurden transformiert |
| `B_MATRIX_RECONSTRUCTED` | B-Matrix aus b und g berechnet |
| `PRIVATE_TAG_MAPPING_USED` | öffentliche Tags aus Herstellerattributen erzeugt |
| `BRAINLAB_IMPORT_NOT_YET_TESTED` | noch kein realer Importtest |

---

## 26. Maschinenlesbarer Batch-Report

Für jede Ausgabe MUSS ein JSON-Report erzeugt werden.

### 26.1 Mindestfelder

```json
{
  "schemaVersion": "1.0",
  "converterVersion": "x.y.z",
  "sourceStudyInstanceUID": "...",
  "sourceSeriesInstanceUIDs": ["..."],
  "outputSeriesInstanceUID": "...",
  "outputSOPInstanceUIDs": ["..."],
  "targetProfile": "Brainlab Elements Fibertracking 2.0",
  "nominalShellBValue": 1000,
  "actualBValueRange": [989, 1006],
  "b0VolumeCount": 10,
  "directionCount": 64,
  "repetitionCountPerDirection": 1,
  "volumeCount": 74,
  "sliceCountPerVolume": 50,
  "frameCount": 3700,
  "matrix": [88, 88],
  "pixelSpacingMm": [2.5, 2.5],
  "sliceThicknessMm": 2.5,
  "sliceSpacingMm": 2.5,
  "patientPosition": "HFS",
  "orientationClass": "AXIAL_OBLIQUE",
  "obliquityDegrees": 11.85,
  "gradientCoordinateSystem": "DICOM_LPS_PATIENT",
  "diffusionMetadataSource": "SIEMENS_PRIVATE_TAGS",
  "pixelDataModified": false,
  "dicomValidatorPassed": true,
  "brainlabImportTested": false,
  "blockers": [],
  "warnings": ["BRAINLAB_IMPORT_NOT_YET_TESTED"]
}
```

### 26.2 Auditierbarkeit

Zusätzlich speichern:

- SHA-256 jeder Quelldatei,
- SHA-256 jeder Zieldatei,
- vollständige Mapping-Tabelle Quelle → Ziel-Frame,
- Quell-SOPInstanceUID je Ziel-Frame,
- verwendete Toleranzen,
- verworfene Dateien mit Grund,
- Extraktionsquelle jedes b-Werts und Gradienten.

---

## 27. DICOM-Validator und technische Endkontrolle

### 27.1 Validator

Mindestens ein unabhängiger IOD-Validator, beispielsweise `dciodvfy` oder gleichwertig.

### 27.2 Pflichtprüfungen

- Enhanced MR SOP Class korrekt,
- alle Type-1-Attribute vorhanden und nicht leer,
- alle zutreffenden Type-1C-Attribute vorhanden,
- Shared/Per-Frame Functional Groups korrekt,
- Per-Frame-Anzahl = NumberOfFrames,
- MR Diffusion Macro vollständig,
- Dimensionsorganisation gültig,
- VR/VM korrekt,
- Sequenzen korrekt verschachtelt,
- keine ungültigen UIDs,
- PixelData dekodierbar.

### 27.3 Round-trip-Test

Nach Schreiben:

1. Datei erneut öffnen.
2. Alle Frames dekodieren.
3. Geometrie rekonstruieren.
4. Shell und Richtungen rekonstruieren.
5. Quell-/Zielgradienten vergleichen.
6. Quell-/Zielpixel vergleichen.
7. Dimensionstupel auf Eindeutigkeit prüfen.

### 27.4 Numerische Akzeptanz

| Prüfung | Ziel |
|---|---|
| Gradientennorm | etwa 1 |
| B-Matrix-Spur | etwa b-Wert |
| Gradient/Haupteigenvektor | `abs(dot) >= 0.98`, bevorzugt nahe 1 |
| Schichtpositionsdifferenz | innerhalb Geometrietoleranz |
| Pixelwertdifferenz bei Repacking | 0 |
| Richtungszahl | exakt erwartet |
| Frames | `volumes × slices` |

---

## 28. Brainlab-Testimport und klinische Plausibilitäts-QA

Ein bestandener DICOM-Validator ersetzt nicht den Brainlab-Testimport.

### 28.1 Importprüfung

- Serie wird als Diffusions-/DTI-Datensatz erkannt.
- b0 und Richtungszahl korrekt erkannt.
- keine Frame-/Schichtverluste.
- keine Fehlermeldung bei Tensorberechnung.
- anatomische Referenz registrierbar.

### 28.2 Bildbasierte QA

- b0 plausibel,
- DWI-Richtungen ohne offensichtliche Sortierfehler,
- FA-Karte anatomisch plausibel,
- Farb-FA-Achsen plausibel,
- keine Links-rechts-Spiegelung,
- Corpus callosum, Pyramidenbahn und Kommissuren plausibel,
- Vergleich mit scannerseitiger FA/Color-FA, sofern vorhanden.

### 28.3 Freigabestatus

```text
CONVERTED_NOT_VALIDATED
DICOM_VALIDATED
BRAINLAB_IMPORT_VALIDATED
CLINICALLY_QA_APPROVED
```

Nur der letzte Status ist eine vollständige klinische Freigabe.

---

## 29. Referenzprofil für die geprüften Siemens-Prisma-XA30-Daten

Dieser Abschnitt ist ein konkretes Implementierungs- und Testprofil für die bereits geprüften Scanner-Originaldaten.

### 29.1 Datensatz A

```text
Scanner: MAGNETOM Prisma Fit 3 T
Software: syngo MR XA30
Sequenz: ep2d_diff_mddw_64_s4
Matrix: 140 x 140
PixelSpacing: 1.57143 x 1.57143 mm
SliceThickness: 1.5 mm
SliceSpacing: 1.5 mm
Slices/Volume: 96
b0 volumes: 10
b3000 directions: 64
Total volumes: 74
Expected frames: 74 * 96 = 7104
PatientPosition: HFS
Orientation: axial-oblique
```

Ziel:

```text
BL_FT_B0_B3000_64DIR
```

Bewertung:

- technisch konvertierbar,
- b3000 außerhalb der bevorzugten Brainlab-b≈1000-Empfehlung,
- nur nach realem Import-/Traktografie-Test freigeben.

### 29.2 Datensatz B

```text
Scanner: MAGNETOM Prisma Fit 3 T
Software: syngo MR XA30
Sequenz: DKI_BIPOLAR_2.5mm_64dir
Matrix: 88 x 88
PixelSpacing: 2.5 x 2.5 mm
SliceThickness: 2.5 mm
SliceSpacing: 2.5 mm
Slices/Volume: 50
b0 volumes: 10
b1000 directions: 64
b3000 directions: 64
Total input volumes: 138
PatientPosition: HFS
Orientation: axial-oblique
```

Ausgabe 1 – Priorität:

```text
b0 + b1000
Volumes: 10 + 64 = 74
Frames: 74 * 50 = 3700
SeriesDescription: BL_FT_B0_B1000_64DIR
```

Ausgabe 2 – optional:

```text
b0 + b3000
Volumes: 10 + 64 = 74
Frames: 74 * 50 = 3700
SeriesDescription: BL_FT_B0_B3000_64DIR
```

### 29.3 Siemens-Tagmapping

```text
(0019,100C) B_value
(0019,100D) DiffusionDirectionality
(0019,100E) DiffusionGradientDirection
(0019,1027) B_matrix
```

MUSS für jedes einzelne Quellbild gelesen werden. Der erste Quellframe darf niemals stellvertretend für alle 64 Richtungen verwendet werden.

### 29.4 TOPUP-PA

```text
Nur Preprocessing
Nicht direkt in Brainlab-DTI-Serie mischen
```

### 29.5 Ausschlussserien

```text
ADC
TRACEW
FA
ColFA
TENSOR_B0
PhoenixZIPReport
Secondary Capture
```

---

## 30. Automatisierte Testfälle

### 30.1 Positive Tests

1. b0 + 64 × b1000, 50 Schichten, isotrop, HFS.
2. b0 + 20 × b1000, 35 Schichten, lückenlos.
3. mehrere b0-Volumina plus 64 Richtungen.
4. DKI b0+b1000+b3000 → zwei getrennte Ausgaben.
5. Siemens-private Tags → vollständiges öffentliches Diffusionsmakro.
6. Gradient und B-Matrix konsistent.
7. BitsAllocated=16/BitsStored=12 bitgenau erhalten.
8. große Multi-Frame-Datei Round-trip ohne Pixeländerung.

### 30.2 Negative Tests

1. kein b0,
2. nur 5 Richtungen,
3. fehlender Gradient in einer Richtung,
4. nur ein Vektor für alle Richtungen,
5. b1000 und b3000 in einer Zielserie,
6. 34 Schichten,
7. Schichtlücke,
8. unterschiedliche IPP-Gitter,
9. Rows != Columns,
10. PixelSpacing nicht quadratisch,
11. PatientPosition != HFS,
12. sagittale Akquisition,
13. PixelData um ein Frame verkürzt,
14. doppelte SOP UID mit unterschiedlichem Pixelinhalt,
15. Per-Frame FG-Zahl ungleich NumberOfFrames,
16. globale InStackPosition 1…N ohne Wiederholung je Volumen,
17. b0 mit Directionality=DIRECTIONAL,
18. Nonzero-Frame ohne Gradient/B-Matrix,
19. Derived Pixel ohne Derivation Source,
20. Anonymisierung entfernt private Gradienten vor Mapping.

### 30.3 Regressionstests

- exakt erwartete Framezahlen für Datensatz A und B,
- exakt 64 eindeutige Richtungen je Shell,
- b0-Zahl unverändert,
- Hashvergleich Pixelwerte,
- keine UID-Kollision über wiederholte Batchläufe,
- identische Ergebnisse unabhängig von Dateisystemreihenfolge.

---

## 31. Performance- und Robustheitsanforderungen

### 31.1 Determinismus

Gleicher Input und gleiche Konfiguration müssen semantisch identische Ergebnisse liefern. UIDs dürfen neu sein, die Frame-/Volumenreihenfolge und Metadatenzuordnung müssen jedoch deterministisch bleiben.

### 31.2 Speicher

Für große Datensätze:

- Pixelgröße vorab berechnen,
- Überlaufprüfungen mit 64-Bit-Arithmetik,
- keine unkontrollierte parallele Erstellung mehrerer sehr großer Enhanced-MR-Objekte,
- Streaming oder Memory-Mapped-Strategie, wenn Objekt > verfügbarem sicheren RAM.

### 31.3 Abbruch und Transaktionalität

- Ausgabe zunächst in temporäre Datei,
- erst nach erfolgreichem Write/Read/Validate atomar umbenennen,
- bei Fehler keine halbfertige `.dcm` als gültig belassen,
- temporäre Dateien sicher bereinigen.

### 31.4 ZIP-Sicherheit

- kein `../` Path Traversal,
- Größenlimit und Entpackquoten gegen ZIP-Bomben,
- Dateizahl-Limit konfigurierbar,
- keine Ausführung eingebetteter Dateien.

---

## 32. Definition of Done

Die Implementierung ist erst abgeschlossen, wenn alle folgenden Punkte erfüllt sind:

- [ ] Brainlab-Soll-/Muss-Kriterien vollständig implementiert.
- [ ] DKI-Multi-Shell-Trennung korrekt.
- [ ] b0 wird jeder Shell-Ausgabe zugeordnet.
- [ ] individuelle Gradienten je Volumen erhalten.
- [ ] B-Matrizen korrekt gemappt und validiert.
- [ ] DICOM-LPS-Koordinaten korrekt.
- [ ] keine Achsenspiegelung.
- [ ] Enhanced-MR-IOD vollständig.
- [ ] obligatorische Functional Groups vorhanden.
- [ ] MR Diffusion Macro pro Frame.
- [ ] Dimension Organization korrekt.
- [ ] InStackPosition wiederholt je Volumen.
- [ ] FrameAcquisitionNumber eindeutig je Volumen.
- [ ] PixelData bit-/wertidentisch bei Repacking.
- [ ] UIDs gültig und eindeutig.
- [ ] Provenienz vollständig.
- [ ] De-Identifizierung entfernt keine Diffusionsinformationen.
- [ ] unabhängiger DICOM-Validator bestanden.
- [ ] Round-trip-Tests bestanden.
- [ ] Referenzdatensätze A/B bestehen automatisierte Tests.
- [ ] Brainlab-Testimport durchgeführt.
- [ ] FA-/Color-FA-/Traktografie-Plausibilität bestätigt.
- [ ] anatomische Referenzserie vorhanden oder als fehlend blockierend dokumentiert.
- [ ] JSON-/CSV-Batchreport vollständig.

---

## 33. Kompakte Freigabematrix

| Bereich | Freigabebedingung |
|---|---|
| Lagerung | HFS |
| Orientierung | axial/axial-oblique |
| Schichten | ≥35, lückenlos |
| Dicke | bevorzugt ≤3 mm |
| Matrix | quadratisch |
| Pixel | quadratisch |
| Voxel | isotrop empfohlen |
| Richtungen | ≥6 zwingend, ≥20 empfohlen |
| b0 | mindestens 1, identische Geometrie |
| Shells | genau 1 Nonzero-Shell pro Brainlab-Serie |
| b-Wert | etwa 1000 bevorzugt |
| Wiederholungen | je Richtung gleich |
| DICOM | mindestens 2016b-kompatibles MR Diffusion Macro |
| Container | Enhanced MR bevorzugt |
| Pixel | 16-Bit-Container, konsistente BitsStored/HighBit |
| Diffusion | framebezogener b-Wert + Gradient/B-Matrix |
| Dimensionen | Schicht + Volumen eindeutig |
| Anatomie | gleiche Studie oder ≤12 h |
| Validierung | IOD-Validator + Brainlab-Import + visuelle QA |

---

## 34. Primärquellen

1. **Brainlab AG:** *DTI-Scanempfehlungen – Fibertracking Ver. 2.0*, Dokument 60918-15DE, Rev. 1.1.
2. **DICOM PS3.3 – Enhanced MR Image IOD und Functional Group Macros:**  
   https://dicom.nema.org/medical/dicom/current/output/chtml/part03/sect_A.36.html
3. **DICOM PS3.3 C.8.13.5 – Enhanced MR Image Functional Group Macros:**  
   https://dicom.nema.org/medical/dicom/current/output/chtml/part03/sect_C.8.13.5.html
4. **DICOM PS3.3 C.8.13.5.9 – MR Diffusion Macro:**  
   https://dicom.nema.org/medical/dicom/current/output/chtml/part03/sect_C.8.13.5.9.html
5. **DICOM PS3.3 C.7.6.17 – Multi-frame Dimension Module:**  
   https://dicom.nema.org/medical/dicom/current/output/chtml/part03/sect_C.7.6.17.html
6. **DICOM PS3.6 – Data Dictionary:**  
   https://dicom.nema.org/medical/dicom/current/output/html/part06.html
7. Hersteller-DICOM-Conformance-Statement des jeweiligen Scanners und Softwarestands.

---

## 35. Schlussanforderung an die Programmierung

> Die Anwendung darf eine Ausgabe nicht deshalb als Brainlab-kompatibel deklarieren, weil sie sich öffnen lässt oder weil b-Werte im Seriennamen stehen. Kompatibilität setzt voraus, dass **jede räumliche Schicht jedem korrekten Diffusionsvolumen zugeordnet ist, jeder Nonzero-Frame seinen richtigen individuellen Gradient beziehungsweise seine B-Matrix trägt, b0 und Richtungen eine identische Geometrie bilden, jede Zielserie nur einen Nonzero-Shell enthält und das gesamte Enhanced-MR-Objekt als vollständige DICOM-IOD validiert wurde**.
