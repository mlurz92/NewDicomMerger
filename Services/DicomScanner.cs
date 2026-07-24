using System.IO;
using System.Text;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using NewDicomMerger.Helpers;
using NewDicomMerger.Models;

namespace NewDicomMerger.Services;

/// <summary>
/// Discovers, loads, validates, repairs, groups, and sorts DICOM files.
/// </summary>
public sealed class DicomScanner
{
    private readonly Action<string> _log;

    public DicomScanner(Action<string> log)
    {
        _log = log;
    }

    /// <summary>
    /// Bugfix: fo-dicom's built-in fallback (<c>FellowOakDicom.DicomEncoding.Default</c>) is
    /// plain ASCII, used whenever a file doesn't declare a SpecificCharacterSet (0008,0005) —
    /// common on older/misconfigured (frequently German) scanners and PACS exports that write
    /// raw 8-bit Latin-1/Windows-1252 bytes without declaring it. .NET's ASCIIEncoding replaces
    /// every byte ≥ 0x80 with '?', which is exactly why umlauts (ä/ö/ü/ß) in PatientName showed
    /// up as "K?hn" instead of "Kühn".
    ///
    /// Uses <see cref="Encoding.Latin1"/> (ISO-8859-1), NOT <c>Encoding.GetEncoding(1252)</c>:
    /// the latter needs the System.Text.Encoding.CodePages package and a registered
    /// CodePagesEncodingProvider, and throws NotSupportedException without it — on plain .NET 8
    /// this app would crash on literally the first file scanned. Encoding.Latin1 is built into
    /// the base class library and needs no extra package. German umlauts (0xC0–0xFF) decode
    /// identically under Latin-1 and Windows-1252 — they only differ in the 0x80–0x9F range
    /// (smart quotes, em-dashes etc.), which isn't relevant to patient-name text.
    /// </summary>
    public static readonly Encoding LegacyFallbackEncoding = Encoding.Latin1;

    // ──────────────── Known file extensions ────────────────

    private static readonly HashSet<string> DicomExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".dcm", ".dicom", ".dic", ".ima" };

    private static readonly HashSet<string> IgnoredExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".log", ".xml", ".json", ".csv", ".tsv",
            ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif", ".webp", ".svg", ".ico",
            ".exe", ".dll", ".sys", ".com", ".msi", ".appx",
            ".zip", ".7z", ".tar", ".gz", ".rar", ".bz2", ".xz", ".cab",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".rtf",
            ".db", ".sqlite", ".mdb", ".accdb", ".ldf", ".mdf",
            ".mp3", ".mp4", ".avi", ".mkv", ".wav", ".flac", ".mov", ".wmv",
            ".html", ".htm", ".css", ".js", ".ts", ".jsx", ".tsx",
            ".py", ".cs", ".java", ".cpp", ".h", ".rb", ".go", ".rs", ".swift",
            ".md", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
            ".bat", ".cmd", ".sh", ".ps1", ".psm1",
            ".sln", ".csproj", ".fsproj", ".vbproj",
            ".nupkg", ".snupkg"
        };

    // ──────────────── File Discovery ────────────────

    /// <summary>
    /// Finds all DICOM candidate files in a folder (non-recursive).
    /// First checks known extensions, then probes extensionless files for the DICM preamble.
    /// </summary>
    public List<string> FindCandidates(string folderPath, CancellationToken ct = default)
    {
        var result = new List<string>();

        // Single directory pass: classify each file by extension, only probing
        // the DICM magic bytes for extensionless/unknown-extension files.
        foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string ext = Path.GetExtension(file).ToLowerInvariant();

            if (DicomExtensions.Contains(ext))
            {
                result.Add(file);
                continue;
            }
            if (IgnoredExtensions.Contains(ext)) continue; // skip known non-DICOM

            var fi = new FileInfo(file);
            if (fi.Length < 132) continue; // too small for DICOM preamble

            if (HasDicomPreamble(file))
                result.Add(file);
        }

        return result.OrderBy(p => p, new NaturalStringComparer()).ToList();
    }

    private static bool HasDicomPreamble(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            if (fs.Length < 132) return false;
            fs.Seek(128, SeekOrigin.Begin);
            Span<byte> magic = stackalloc byte[4];
            int read = fs.Read(magic);
            return read == 4
                && magic[0] == 'D' && magic[1] == 'I'
                && magic[2] == 'C' && magic[3] == 'M';
        }
        catch { return false; }
    }

    // ──────────────── Loading & Repair ────────────────

    /// <summary>
    /// Attempts to load a single DICOM file, repair missing tags, and validate pixel data.
    /// Returns null (with optional warning) if the file is unusable or not an image.
    /// </summary>
    public LoadedDicom? TryLoad(string filePath, MergeResult result, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // ── Open without pixel-data to save memory and load instantly ──
        DicomFile dicomFile;
        try
        {
            dicomFile = DicomFile.Open(filePath, LegacyFallbackEncoding, readOption: FileReadOption.SkipLargeTags);
        }
        catch (Exception ex)
        {
            AddWarning(result, $"{Path.GetFileName(filePath)}: Nicht lesbar – {ex.Message}");
            return null;
        }

        if (dicomFile.Dataset == null)
        {
            AddWarning(result, $"{Path.GetFileName(filePath)}: Kein Dataset vorhanden");
            return null;
        }

        var ds = dicomFile.Dataset;
        var meta = dicomFile.FileMetaInfo;

        // ── Repair / ensure UIDs ──
        string tsUid = GetOrRepairUid(meta, DicomTag.TransferSyntaxUID,
            DicomTransferSyntax.ExplicitVRLittleEndian.UID.UID);

        const string fallbackSopClass = "1.2.840.10008.5.1.4.1.1.7"; // Secondary Capture
        string sopClassUid = GetOrRepairUid(ds, DicomTag.SOPClassUID, fallbackSopClass);
        string sopInstanceUid = GetOrRepairUid(ds, DicomTag.SOPInstanceUID, DicomUID.Generate().UID);
        string studyUid = GetOrRepairUid(ds, DicomTag.StudyInstanceUID, DicomUID.Generate().UID);
        string seriesUid = GetOrRepairUid(ds, DicomTag.SeriesInstanceUID, DicomUID.Generate().UID);

        if (!meta.Contains(DicomTag.MediaStorageSOPClassUID))
            SafeSetUid(meta, DicomTag.MediaStorageSOPClassUID, sopClassUid);
        if (!meta.Contains(DicomTag.MediaStorageSOPInstanceUID))
            SafeSetUid(meta, DicomTag.MediaStorageSOPInstanceUID, sopInstanceUid);



        // ── Validate pixel attributes ──
        int rows = ds.GetSingleValueOrDefault(DicomTag.Rows, (ushort)0);
        int columns = ds.GetSingleValueOrDefault(DicomTag.Columns, (ushort)0);
        int bitsAllocated = ds.GetSingleValueOrDefault(DicomTag.BitsAllocated, (ushort)0);
        int bitsStored = ds.GetSingleValueOrDefault(DicomTag.BitsStored, (ushort)bitsAllocated);
        int highBit = ds.GetSingleValueOrDefault(DicomTag.HighBit,
            (ushort)(bitsStored > 0 ? bitsStored - 1 : 0));
        int pixelRep = ds.GetSingleValueOrDefault(DicomTag.PixelRepresentation, (ushort)0);
        int samplesPerPixel = ds.GetSingleValueOrDefault(DicomTag.SamplesPerPixel, (ushort)1);

        if (rows <= 0 || columns <= 0)
        {
            AddWarning(result, $"{Path.GetFileName(filePath)}: Ungültige Bildgröße {columns}×{rows}");
            return null;
        }
        if (bitsAllocated is not (8 or 16 or 32))
        {
            AddWarning(result, $"{Path.GetFileName(filePath)}: BitsAllocated={bitsAllocated} nicht unterstützt");
            return null;
        }

        int numberOfFrames = 1;
        if (ds.Contains(DicomTag.NumberOfFrames))
        {
            try { numberOfFrames = ds.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1); }
            catch
            {
                try
                {
                    string str = ds.GetString(DicomTag.NumberOfFrames);
                    if (int.TryParse(str?.Trim(), out int val) && val > 0)
                        numberOfFrames = val;
                }
                catch { }
            }
        }

        int instanceNumber = ds.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0);
        string modality = ds.GetSingleValueOrDefault(DicomTag.Modality, "OT");
        string photometric = ds.GetSingleValueOrDefault(
            DicomTag.PhotometricInterpretation, "MONOCHROME2");

        return new LoadedDicom
        {
            FilePath = filePath,
            File = dicomFile,
            SeriesInstanceUid = seriesUid,
            StudyInstanceUid = studyUid,
            SopInstanceUid = sopInstanceUid,
            SopClassUid = sopClassUid,
            InstanceNumber = instanceNumber,
            Rows = rows,
            Columns = columns,
            BitsAllocated = bitsAllocated,
            BitsStored = bitsStored,
            HighBit = highBit,
            PixelRepresentation = pixelRep,
            SamplesPerPixel = samplesPerPixel,
            TransferSyntaxUid = tsUid,
            Modality = modality,
            PhotometricInterpretation = photometric,
            IsMultiFrame = numberOfFrames > 1,
            NumberOfFrames = numberOfFrames
        };
    }

    // ──────────────── Grouping & Sorting ────────────────

    /// <summary>
    /// Groups loaded DICOMs by Study+Series, validates consistency within each group,
    /// excludes inconsistent files, and sorts each group geometrically.
    /// </summary>
    public List<SeriesGroup> GroupAndSort(List<LoadedDicom> loaded, MergeResult result, bool splitByBValue = false, CancellationToken ct = default)
    {
        var groups = new List<SeriesGroup>();

        var bySeriesKey = loaded
            .GroupBy(d => $"{d.StudyInstanceUid}::{d.SeriesInstanceUid}")
            .ToList();

        foreach (var seriesGroup in bySeriesKey)
        {
            ct.ThrowIfCancellationRequested();
            var files = seriesGroup.ToList();
            if (files.Sum(f => f.NumberOfFrames) < 2) continue;

            // Pixel-property consistency check.
            // Includes BitsStored/PixelRepresentation/TransferSyntax so that mixed
            // signed/unsigned or differently-encoded frames within one series are
            // never silently merged into a single, geometrically inconsistent output.
            var reference = files[0];
            var consistent = files.Where(f =>
                f.Rows == reference.Rows
                && f.Columns == reference.Columns
                && f.BitsAllocated == reference.BitsAllocated
                && f.BitsStored == reference.BitsStored
                && f.PixelRepresentation == reference.PixelRepresentation
                && f.SamplesPerPixel == reference.SamplesPerPixel
                && string.Equals(f.PhotometricInterpretation?.Trim(), reference.PhotometricInterpretation?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.TransferSyntaxUid?.Trim(), reference.TransferSyntaxUid?.Trim(), StringComparison.OrdinalIgnoreCase)
            ).ToList();

            int excluded = files.Count - consistent.Count;
            if (excluded > 0)
            {
                string shortUid = reference.SeriesInstanceUid.Length > 12
                    ? reference.SeriesInstanceUid[..12] + "…"
                    : reference.SeriesInstanceUid;
                AddWarning(result,
                    $"Serie {shortUid}: {excluded} Datei(en) mit inkonsistenten Pixel-Eigenschaften ausgeschlossen.");
                lock (result)
                {
                    result.SkippedFiles += excluded;
                }
            }

            if (consistent.Count == 0) continue;
            if (consistent.Sum(f => f.NumberOfFrames) < 2) continue;

            var sorted = SortGeometrically(consistent);
            groups.Add(new SeriesGroup
            {
                StudyInstanceUid = reference.StudyInstanceUid,
                SeriesInstanceUid = reference.SeriesInstanceUid,
                Modality = reference.Modality,
                Files = sorted,
                ExcludedFileCount = excluded
            });
        }

        if (splitByBValue)
        {
            groups = DiffusionBValueHelper.SplitGroupsByBValue(groups);

            var validGroups = new List<SeriesGroup>();
            foreach (var g in groups)
            {
                if (g.TotalFrames < 2)
                {
                    string shortUid = g.SeriesInstanceUid.Length > 12
                        ? g.SeriesInstanceUid[..12] + "…"
                        : g.SeriesInstanceUid;
                    AddWarning(result, $"Serie {shortUid}: B-Wert-Gruppe übersprungen (nur 1 Datei).");
                    lock (result)
                    {
                        result.SkippedFiles += g.Files.Count;
                    }
                }
                else
                {
                    validGroups.Add(g);
                }
            }
            groups = validGroups;
        }

        return groups;
    }

    // ──────────────── Geometric Sorting ────────────────

    private static List<LoadedDicom> SortGeometrically(List<LoadedDicom> files)
    {
        // 1) ImagePositionPatient Z
        var withZ = files.Select(f => new { File = f, Z = GetImagePositionZ(f.Dataset) }).ToList();
        if (withZ.All(x => x.Z.HasValue))
            return withZ.OrderBy(x => x.Z!.Value).Select(x => x.File).ToList();

        // 2) SliceLocation
        var withSl = files.Select(f => new
        {
            File = f,
            Loc = f.Dataset.GetSingleValueOrDefault<double?>(DicomTag.SliceLocation, null)
        }).ToList();
        if (withSl.All(x => x.Loc.HasValue))
            return withSl.OrderBy(x => x.Loc!.Value).Select(x => x.File).ToList();

        // 3) InstanceNumber
        if (files.Any(f => f.InstanceNumber > 0))
            return files
                .OrderBy(f => f.InstanceNumber)
                .ThenBy(f => f.FilePath, new NaturalStringComparer())
                .ToList();

        // 4) AcquisitionNumber → filename
        return files
            .OrderBy(f => f.Dataset.GetSingleValueOrDefault(DicomTag.AcquisitionNumber, 0))
            .ThenBy(f => f.FilePath, new NaturalStringComparer())
            .ToList();
    }

    private static double? GetImagePositionZ(DicomDataset ds)
    {
        if (!ds.Contains(DicomTag.ImagePositionPatient)) return null;
        try
        {
            var pos = ds.GetValues<double>(DicomTag.ImagePositionPatient);
            return pos is { Length: >= 3 } ? pos[2] : null;
        }
        catch { return null; }
    }

    // ──────────────── Thread-safe result mutation ────────────────

    /// <summary>
    /// TryLoad() is called concurrently from a Parallel.ForEach during scanning;
    /// MergeResult.Warnings is a plain List&lt;string&gt;, so every write must be
    /// serialized to avoid corrupting the list.
    /// </summary>
    private static void AddWarning(MergeResult result, string message)
    {
        lock (result) { result.Warnings.Add(message); }
    }

    // ──────────────── UID Helpers ────────────────

    private static string GetOrRepairUid(DicomDataset ds, DicomTag tag, string fallback)
    {
        string? current = null;
        try
        {
            if (ds.Contains(tag))
            {
                try { current = ds.GetSingleValue<DicomUID>(tag).UID; }
                catch
                {
                    try { current = ds.GetSingleValueOrDefault<string>(tag, ""); }
                    catch { current = ""; }
                }
            }
        }
        catch { current = ""; }

        if (string.IsNullOrWhiteSpace(current))
        {
            SafeSetUid(ds, tag, fallback);
            return fallback;
        }
        return current;
    }

    internal static void SafeSetUid(DicomDataset ds, DicomTag tag, string uidValue)
    {
        if (string.IsNullOrWhiteSpace(uidValue)) return;
        try { ds.AddOrUpdate(tag, DicomUID.Parse(uidValue)); }
        catch
        {
            try { ds.AddOrUpdate(tag, uidValue); }
            catch
            {
                try { ds.Add(new DicomUniqueIdentifier(tag, uidValue)); } catch { }
            }
        }
    }
}
