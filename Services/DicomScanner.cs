using System.IO;
using System.Text;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using NewDicomMerger.Helpers;
using NewDicomMerger.Models;

namespace NewDicomMerger.Services;

public sealed class DicomScanner
{
    private readonly Action<string> _log;

    public DicomScanner(Action<string> log)
    {
        _log = log;
    }

    public static readonly Encoding LegacyFallbackEncoding = Encoding.Latin1;

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

    public List<string> FindCandidates(string folderPath, CancellationToken ct = default)
    {
        var result = new List<string>();

        foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string ext = Path.GetExtension(file).ToLowerInvariant();

            if (DicomExtensions.Contains(ext))
            {
                result.Add(file);
                continue;
            }
            if (IgnoredExtensions.Contains(ext)) continue;

            var fi = new FileInfo(file);
            if (fi.Length < 132) continue;

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

    public LoadedDicom? TryLoad(string filePath, MergeResult result, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

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

        string tsUid = GetOrRepairUid(meta, DicomTag.TransferSyntaxUID,
            DicomTransferSyntax.ExplicitVRLittleEndian.UID.UID);

        const string fallbackSopClass = "1.2.840.10008.5.1.4.1.1.7";
        string sopClassUid = GetOrRepairUid(ds, DicomTag.SOPClassUID, fallbackSopClass);
        string sopInstanceUid = GetOrRepairUid(ds, DicomTag.SOPInstanceUID, DicomUID.Generate().UID);
        string studyUid = GetOrRepairUid(ds, DicomTag.StudyInstanceUID, DicomUID.Generate().UID);
        string seriesUid = GetOrRepairUid(ds, DicomTag.SeriesInstanceUID, DicomUID.Generate().UID);

        if (!meta.Contains(DicomTag.MediaStorageSOPClassUID))
            SafeSetUid(meta, DicomTag.MediaStorageSOPClassUID, sopClassUid);
        if (!meta.Contains(DicomTag.MediaStorageSOPInstanceUID))
            SafeSetUid(meta, DicomTag.MediaStorageSOPInstanceUID, sopInstanceUid);

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
            if (files.Sum(f => f.NumberOfFrames) < 1) continue;

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
            if (consistent.Sum(f => f.NumberOfFrames) < 1) continue;

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
                if (g.TotalFrames < 1)
                {
                    string shortUid = g.SeriesInstanceUid.Length > 12
                        ? g.SeriesInstanceUid[..12] + "…"
                        : g.SeriesInstanceUid;
                    AddWarning(result, $"Serie {shortUid}: B-Wert-Gruppe übersprungen (keine Frames).");
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

    private static List<LoadedDicom> SortGeometrically(List<LoadedDicom> files)
    {
        var withZ = files.Select(f => new { File = f, Z = GetImagePositionZ(f.Dataset) }).ToList();
        if (withZ.All(x => x.Z.HasValue))
            return withZ.OrderBy(x => x.Z!.Value).Select(x => x.File).ToList();

        var withSl = files.Select(f => new
        {
            File = f,
            Loc = f.Dataset.GetSingleValueOrDefault<double?>(DicomTag.SliceLocation, null)
        }).ToList();
        if (withSl.All(x => x.Loc.HasValue))
            return withSl.OrderBy(x => x.Loc!.Value).Select(x => x.File).ToList();

        if (files.Any(f => f.InstanceNumber > 0))
            return files
                .OrderBy(f => f.InstanceNumber)
                .ThenBy(f => f.FilePath, new NaturalStringComparer())
                .ToList();

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

    private static void AddWarning(MergeResult result, string message)
    {
        lock (result) { result.Warnings.Add(message); }
    }

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
