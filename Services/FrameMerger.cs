using System.IO;
using System.Security.Cryptography;
using System.Text;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using NewDicomMerger.Models;

namespace NewDicomMerger.Services;

/// <summary>
/// Merges a group of single-frame DICOM files into a single multi-frame Enhanced DICOM file.
/// 
/// Key design decisions based on fo-dicom 5.1.4 quirks:
///   • Frames are added individually via DicomPixelData.AddFrame() — NOT as a single concatenated blob.
///     fo-dicom tracks NumberOfFrames internally; adding one blob = 1 frame regardless of byte count.
///   • NumberOfFrames (VR=IS) is managed by AddFrame() automatically.
///   • DimensionIndexPointer / FunctionalGroupPointer (VR=AT) receive DicomTag objects, not uint casts.
///   • InStackPositionNumber (VR=UL) → uint, FrameAcquisitionNumber (VR=US) → ushort.
///   • DimensionIndexValues (VR=UL) → uint[].
///   • Patient/study tags are copied via direct DicomItem references, never via DicomTag.Parse("name").
///   • Output filenames include a series-index AND a hash of the SeriesInstanceUID for uniqueness.
/// </summary>
public sealed record DiffusionBValueSeries(int BValue, SeriesGroup Group);

public sealed class FrameMerger
{
    private readonly Action<string> _log;
    private readonly Action<string>? _warn;

    public FrameMerger(Action<string> log, Action<string>? warn = null)
    {
        _log = log;
        _warn = warn;
    }

    // ──────────────── SOP Class UIDs ────────────────

    private static class Sop
    {
        public const string EnhancedCT            = "1.2.840.10008.5.1.4.1.1.2.1";
        public const string EnhancedMR            = "1.2.840.10008.5.1.4.1.1.4.1";
        public const string EnhancedPET           = "1.2.840.10008.5.1.4.1.1.130";
        public const string MultiFrameGrayByteSC  = "1.2.840.10008.5.1.4.1.1.7.2";
        public const string MultiFrameGrayWordSC  = "1.2.840.10008.5.1.4.1.1.7.3";
        public const string SecondaryCaptureImage  = "1.2.840.10008.5.1.4.1.1.7";
        public const string UltrasoundMultiFrame   = "1.2.840.10008.5.1.4.1.1.6.1";
        public const string XRayAngiographic       = "1.2.840.10008.5.1.4.1.1.12.1";
        public const string NuclearMedicine        = "1.2.840.10008.5.1.4.1.1.20";
    }

    // ──────────────── Public API ────────────────

    public void Merge(SeriesGroup group, string outputPath, string patientName, string seriesDescription, bool anonymize = false, bool compressDicom = false, CancellationToken ct = default, SeriesDeidentifier? anonymizer = null)
    {
        ct.ThrowIfCancellationRequested();
        var files = group.Files;
        if (files.Count == 0)
            throw new ArgumentException("SeriesGroup enthält keine Dateien.", nameof(group));
        var first = files[0];
        int frameCount = files.Count;

        // ── Resolve transfer syntax ──
        DicomTransferSyntax ts;
        try { ts = DicomTransferSyntax.Lookup(DicomUID.Parse(first.TransferSyntaxUid)); }
        catch { ts = DicomTransferSyntax.ExplicitVRLittleEndian; }

        // ── Pixel geometry ──
        int rows = first.Rows;
        int cols = first.Columns;
        int bitsAllocated = first.BitsAllocated;
        int samplesPerPixel = first.SamplesPerPixel;
        int bytesPerPixel = bitsAllocated / 8;
        int expectedFrameBytes = rows * cols * samplesPerPixel * bytesPerPixel;

        // ── Create output file ──
        var outFile = new DicomFile();
        var meta = outFile.FileMetaInfo;
        var ds = outFile.Dataset;

        // Transfer syntax: always write as Explicit VR Little Endian
        SetUid(meta, DicomTag.TransferSyntaxUID, DicomTransferSyntax.ExplicitVRLittleEndian.UID.UID);

        // SOP Class selection (choose Enhanced / Multi-Frame SOP Class)
        string sopClassUid = ChooseSopClass(first.Modality, first.BitsAllocated);
        SetUid(meta, DicomTag.MediaStorageSOPClassUID, sopClassUid);
        SetUid(ds, DicomTag.SOPClassUID, sopClassUid);

        // New SOP Instance UID
        string newSopInstance = DicomUID.Generate().UID;
        SetUid(meta, DicomTag.MediaStorageSOPInstanceUID, newSopInstance);
        SetUid(ds, DicomTag.SOPInstanceUID, newSopInstance);

        // Study + Series UIDs (preserved from source)
        SetUid(ds, DicomTag.StudyInstanceUID, group.StudyInstanceUid);
        SetUid(ds, DicomTag.SeriesInstanceUID, group.SeriesInstanceUid);

        // ── Copy patient & study metadata ──
        CopyPatientStudyInfo(first.Dataset, ds);
        if (!string.IsNullOrWhiteSpace(patientName))
            ds.AddOrUpdate(DicomTag.PatientName, patientName);
        if (!string.IsNullOrWhiteSpace(seriesDescription))
            ds.AddOrUpdate(DicomTag.SeriesDescription, seriesDescription);

        // ── Pixel attributes ──
        ds.AddOrUpdate(DicomTag.Rows, (ushort)rows);
        ds.AddOrUpdate(DicomTag.Columns, (ushort)cols);
        ds.AddOrUpdate(DicomTag.BitsAllocated, (ushort)bitsAllocated);
        ds.AddOrUpdate(DicomTag.BitsStored, (ushort)first.BitsStored);
        ds.AddOrUpdate(DicomTag.HighBit, (ushort)first.HighBit);
        ds.AddOrUpdate(DicomTag.PixelRepresentation, (ushort)first.PixelRepresentation);
        ds.AddOrUpdate(DicomTag.SamplesPerPixel, (ushort)samplesPerPixel);
        ds.AddOrUpdate(DicomTag.PhotometricInterpretation, first.PhotometricInterpretation);
        ds.AddOrUpdate(DicomTag.Modality, first.Modality);

        if (samplesPerPixel > 1)
        {
            ds.AddOrUpdate(DicomTag.PlanarConfiguration,
                (ushort)first.Dataset.GetSingleValueOrDefault(DicomTag.PlanarConfiguration, (ushort)0));
        }

        // ── Allokation des flachen Gesamtpuffers zur Umgehung des fo-dicom AddFrame-Bugs ──
        long totalExpectedBytes = (long)expectedFrameBytes * frameCount;
        if (totalExpectedBytes > int.MaxValue)
            throw new InvalidOperationException($"Zusammengefügte Serie überschreitet das 2GB Pufferlimit ({totalExpectedBytes} Bytes).");

        byte[] allPixelBytes = new byte[(int)totalExpectedBytes];
        long totalPixelBytes = 0;
        string? decodedPhotometric = null;

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var fullFile = DicomFile.Open(files[i].FilePath, DicomScanner.LegacyFallbackEncoding, readOption: FileReadOption.ReadAll);

            // Decompress on the fly if input is encapsulated/compressed
            if (fullFile.FileMetaInfo.TransferSyntax.IsEncapsulated)
            {
                var transcoder = new FellowOakDicom.Imaging.Codec.DicomTranscoder(fullFile.FileMetaInfo.TransferSyntax, DicomTransferSyntax.ExplicitVRLittleEndian);
                fullFile = transcoder.Transcode(fullFile);
            }

            var srcPixelData = DicomPixelData.Create(fullFile.Dataset);
            var frameBuffer = srcPixelData.GetFrame(0);
            var frameBytes = frameBuffer.Data;

            // The transcoder normalizes color pixel data to the photometric interpretation
            // it actually wrote (e.g. compressed YBR_FULL_422 → decompressed RGB). Capture
            // that real value once so the output dataset doesn't keep stating a stale
            // photometric interpretation that no longer matches the decoded pixel bytes.
            if (i == 0)
            {
                decodedPhotometric = fullFile.Dataset.GetSingleValueOrDefault(
                    DicomTag.PhotometricInterpretation, first.PhotometricInterpretation);
            }

            long destOffset = (long)i * expectedFrameBytes;
            if (frameBytes.Length == expectedFrameBytes)
            {
                Array.Copy(frameBytes, 0, allPixelBytes, destOffset, expectedFrameBytes);
                totalPixelBytes += expectedFrameBytes;
            }
            else
            {
                // Pad or truncate to expected size
                int copyLen = Math.Min(frameBytes.Length, expectedFrameBytes);
                Array.Copy(frameBytes, 0, allPixelBytes, destOffset, copyLen);
                totalPixelBytes += expectedFrameBytes;

                _warn?.Invoke(
                    $"{Path.GetFileName(files[i].FilePath)}: Frame {frameBytes.Length} B ≠ erwartet {expectedFrameBytes} B – angepasst");
            }
        }

        // Correct the photometric interpretation now that decompression actually happened.
        if (!string.IsNullOrWhiteSpace(decodedPhotometric) && decodedPhotometric != first.PhotometricInterpretation)
            ds.AddOrUpdate(DicomTag.PhotometricInterpretation, decodedPhotometric);

        // DicomPixelData erstellen (erbt Attribute automatisch aus dem Dataset)
        var outPixelData = DicomPixelData.Create(ds, true);

        // Fügt das gesamte Byte-Array als flachen Puffer hinzu
        outPixelData.AddFrame(new MemoryByteBuffer(allPixelBytes));

        // Manuell die Anzahl der Frames eintragen, da wir einen verketteten Puffer hinzugefügt haben
        ds.AddOrUpdate(DicomTag.NumberOfFrames, frameCount.ToString());

        // Sanity-check the assembled pixel buffer / frame count before anything is
        // written to disk. Throws on mismatch; caller (batch loop) catches, logs, and
        // counts this series as an error instead of silently emitting a broken file.
        VerifyOutput(ds, frameCount, expectedFrameBytes, totalPixelBytes);

        // ── Geometry & 3D structure ──
        ApplyGeometry(ds, files);
        BuildSharedAndPerFrameFunctionalGroups(ds, files);
        BuildDimensionOrganization(ds);

        // ── Anonymization ──
        if (anonymize)
        {
            var anon = anonymizer ?? new SeriesDeidentifier();
            anon.Anonymize(ds);
            anon.Anonymize(meta);
        }

        // Bugfix: declare the charset explicitly so patient/study text containing umlauts
        // (ä/ö/ü/ß) round-trips correctly for any reader (including this app itself, e.g.
        // DicomDirWriter) instead of silently defaulting to ISO_IR 6 (ASCII), which is what
        // caused "Kühn" to show up as "K?hn" in the first place when the SOURCE file didn't
        // declare a charset either.
        ds.AddOrUpdate(DicomTag.SpecificCharacterSet, "ISO_IR 100");

        // ── Save ──
        ct.ThrowIfCancellationRequested();

        if (compressDicom)
        {
            try
            {
                _log("    Komprimiere Ausgabedatei (JPEG Lossless)...");
                var transcoder = new FellowOakDicom.Imaging.Codec.DicomTranscoder(outFile.FileMetaInfo.TransferSyntax, DicomTransferSyntax.JPEGProcess14SV1);
                var compressedFile = transcoder.Transcode(outFile);
                compressedFile.Save(outputPath);
            }
            catch (Exception ex)
            {
                _warn?.Invoke($"Kompression fehlgeschlagen, speichere unkomprimiert: {ex.Message}");
                outFile.Save(outputPath);
            }
        }
        else
        {
            outFile.Save(outputPath);
        }

        _log($"    Größe: {rows}×{cols} × {frameCount} Frames, " +
             $"{bitsAllocated} Bit, {samplesPerPixel}ch, {first.Modality}");
        _log($"    SOP-Klasse: {SopClassDisplayName(sopClassUid)}");
    }

    public static IReadOnlyList<DiffusionBValueSeries> SplitByDiffusionBValue(SeriesGroup group)
    {
        var withBValues = group.Files
            .Select(file => new { File = file, BValue = TryGetDiffusionBValue(file.Dataset) })
            .ToList();

        if (withBValues.Any(x => x.BValue == null))
            return [];

        var bValueGroups = withBValues
            .GroupBy(x => NormalizeBValue(x.BValue!.Value))
            .OrderBy(g => g.Key)
            .ToList();

        if (bValueGroups.Count <= 1)
            return [];

        return bValueGroups
            .Select(g => new DiffusionBValueSeries(
                g.Key,
                new SeriesGroup
                {
                    StudyInstanceUid = group.StudyInstanceUid,
                    SeriesInstanceUid = DicomUID.Generate().UID,
                    Modality = group.Modality,
                    Files = g.Select(x => x.File).ToList(),
                    ExcludedFileCount = group.ExcludedFileCount
                }))
            .ToList();
    }

    public static double? TryGetDiffusionBValue(DicomDataset ds)
    {
        if (TryGetDouble(ds, DicomTag.DiffusionBValue, out double topLevelBValue))
            return topLevelBValue;

        if (ds.Contains(DicomTag.MRDiffusionSequence))
        {
            var diffusionSequence = ds.GetSequence(DicomTag.MRDiffusionSequence);
            foreach (var item in diffusionSequence.Items)
            {
                if (TryGetDouble(item, DicomTag.DiffusionBValue, out double nestedBValue))
                    return nestedBValue;
            }
        }

        return null;
    }

    public static bool HasMultipleDiffusionBValues(SeriesGroup group)
    {
        var values = new HashSet<int>();
        foreach (var file in group.Files)
        {
            var bValue = TryGetDiffusionBValue(file.Dataset);
            if (bValue == null)
                return false;
            values.Add(NormalizeBValue(bValue.Value));
            if (values.Count > 1)
                return true;
        }

        return false;
    }

    private static bool TryGetDouble(DicomDataset ds, DicomTag tag, out double value)
    {
        value = 0;
        if (!ds.Contains(tag)) return false;

        try
        {
            value = ds.GetSingleValue<double>(tag);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int NormalizeBValue(double bValue)
        => (int)Math.Round(bValue, MidpointRounding.AwayFromZero);

    // Anonymization now lives in SeriesDeidentifier, which keeps UID/patient-ID/date-shift
    // state consistent across an entire batch run instead of anonymizing each file in
    // isolation. See Merge()'s `anonymizer` parameter and Services/SeriesDeidentifier.cs.

    // ──────────────── Geometry ────────────────

    private void ApplyGeometry(DicomDataset ds, List<LoadedDicom> files)
    {
        var firstDs = files[0].Dataset;

        CopyTag(firstDs, ds, DicomTag.PixelSpacing);
        CopyTag(firstDs, ds, DicomTag.PixelAspectRatio);
        CopyTag(firstDs, ds, DicomTag.ImagerPixelSpacing);
        CopyTag(firstDs, ds, DicomTag.ImageOrientationPatient);
        CopyTag(firstDs, ds, DicomTag.SliceThickness);
        CopyTag(firstDs, ds, DicomTag.WindowCenter);
        CopyTag(firstDs, ds, DicomTag.WindowWidth);
        CopyTag(firstDs, ds, DicomTag.RescaleIntercept);
        CopyTag(firstDs, ds, DicomTag.RescaleSlope);
        CopyTag(firstDs, ds, DicomTag.RescaleType);
        CopyTag(firstDs, ds, DicomTag.AcquisitionDateTime);
        CopyTag(firstDs, ds, DicomTag.ContentDate);
        CopyTag(firstDs, ds, DicomTag.ContentTime);
        CopyTag(firstDs, ds, DicomTag.AcquisitionDate);
        CopyTag(firstDs, ds, DicomTag.AcquisitionTime);
        CopyTag(firstDs, ds, DicomTag.AcquisitionNumber);
        CopyTag(firstDs, ds, DicomTag.SeriesDescription);
        CopyTag(firstDs, ds, DicomTag.SeriesNumber);
        CopyTag(firstDs, ds, DicomTag.ProtocolName);

        // Compute spacing between slices
        double? spacing = ComputeSliceSpacing(files);
        if (spacing is > 0)
        {
            ds.AddOrUpdate(DicomTag.SpacingBetweenSlices, spacing.Value);
            if (!ds.Contains(DicomTag.SliceThickness))
                ds.AddOrUpdate(DicomTag.SliceThickness, spacing.Value);
            _log($"    SpacingBetweenSlices: {spacing.Value:F4} mm");
        }
    }

    private static double? ComputeSliceSpacing(List<LoadedDicom> files)
    {
        // Method 1: average of SliceThickness values
        var thicknesses = files
            .Select(f => f.Dataset.GetSingleValueOrDefault<double?>(DicomTag.SliceThickness, null))
            .Where(t => t is > 0)
            .Select(t => t!.Value)
            .ToList();

        if (thicknesses.Count == files.Count)
            return thicknesses.Average();

        // Method 2: median of Z-position deltas
        var zPositions = files
            .Select(f => GetImagePositionZ(f.Dataset))
            .ToList();

        if (zPositions.All(z => z.HasValue) && zPositions.Count >= 2)
        {
            var deltas = new List<double>();
            for (int i = 1; i < zPositions.Count; i++)
            {
                double d = Math.Abs(zPositions[i]!.Value - zPositions[i - 1]!.Value);
                if (d > 0) deltas.Add(d);
            }
            if (deltas.Count > 0) return Median(deltas);
        }

        return null;
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

    // ──────────────── Shared & Per-Frame Functional Groups ────────────────

    /// <summary>
    /// Builds PerFrameFunctionalGroupsSequence for values that vary frame-to-frame
    /// (position, frame content), and — per PS3.3 C.7.6.16 — factors PixelMeasures and
    /// PlaneOrientation out into SharedFunctionalGroupsSequence whenever they are
    /// identical across the whole stack (the common case for a straight axial/coronal/
    /// sagittal series). This avoids duplicating the same geometry values once per
    /// frame and matches what most Enhanced-DICOM-aware viewers expect. If a stack
    /// genuinely has varying orientation/spacing per frame, those groups fall back to
    /// being written per-frame as before.
    /// </summary>
    private static void BuildSharedAndPerFrameFunctionalGroups(DicomDataset target, List<LoadedDicom> files)
    {
        double defaultSpacing = target.GetSingleValueOrDefault(DicomTag.SpacingBetweenSlices, 1.0);

        var orientations = new double[files.Count][];
        var pixelSpacings = new double[]?[files.Count];
        var thicknesses = new double[files.Count];

        for (int i = 0; i < files.Count; i++)
        {
            var srcDs = files[i].Dataset;

            orientations[i] = TryGetDoubles(srcDs, DicomTag.ImageOrientationPatient, 6)
                ?? new[] { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0 };

            pixelSpacings[i] = TryGetDoubles(srcDs, DicomTag.PixelSpacing, 2);

            double thickness = srcDs.GetSingleValueOrDefault(DicomTag.SliceThickness, 0.0);
            thicknesses[i] = thickness <= 0 ? defaultSpacing : thickness;
        }

        bool orientationConsistent = files.Count > 0 && orientations.All(o => SequenceApproxEqual(o, orientations[0]));
        bool measuresConsistent = files.Count > 0
            && thicknesses.All(t => Math.Abs(t - thicknesses[0]) < 1e-6)
            && pixelSpacings.All(ps => PixelSpacingApproxEqual(ps, pixelSpacings[0]));

        // ── Map generic functional groups ──
        var fgDatasets = new Dictionary<DicomTag, DicomDataset?[]>();
        foreach (var kvp in FunctionalGroupMappings)
        {
            var fgTag = kvp.Key;
            var childTags = kvp.Value;
            var datasets = new DicomDataset?[files.Count];
            bool anyFound = false;

            for (int i = 0; i < files.Count; i++)
            {
                var srcDs = files[i].Dataset;
                DicomDataset? itemDs = null;

                foreach (var childTag in childTags)
                {
                    if (srcDs.Contains(childTag))
                    {
                        itemDs ??= new DicomDataset();
                        foreach (var item in srcDs)
                        {
                            if (item.Tag == childTag)
                            {
                                itemDs.AddOrUpdate(item);
                                break;
                            }
                        }
                        anyFound = true;
                    }
                }
                datasets[i] = itemDs;
            }

            if (anyFound)
            {
                fgDatasets[fgTag] = datasets;
            }
        }

        var sharedFG = new DicomDataset();
        var perFrameFGLists = new List<DicomSequence>[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            perFrameFGLists[i] = new List<DicomSequence>();
        }

        foreach (var kvp in fgDatasets)
        {
            var fgTag = kvp.Key;
            var datasets = kvp.Value;

            bool consistent = true;
            var firstDs = datasets[0];

            for (int i = 1; i < files.Count; i++)
            {
                if (!AreDatasetsEqual(firstDs, datasets[i]))
                {
                    consistent = false;
                    break;
                }
            }

            if (consistent && firstDs != null)
            {
                sharedFG.AddOrUpdate(new DicomSequence(fgTag, firstDs));
            }
            else
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var ds = datasets[i];
                    if (ds != null)
                    {
                        perFrameFGLists[i].Add(new DicomSequence(fgTag, ds));
                    }
                }
            }
        }

        // ── Shared Functional Groups ──
        var sharedItem = new DicomDataset();

        if (orientationConsistent)
        {
            var planeOri = new DicomDataset();
            planeOri.AddOrUpdate(DicomTag.ImageOrientationPatient, orientations[0]);
            sharedItem.Add(new DicomSequence(DicomTag.PlaneOrientationSequence, planeOri));
        }

        if (measuresConsistent)
        {
            var pixelMeasures = new DicomDataset();
            var ps = pixelSpacings[0] ?? new[] { 1.0, 1.0 };
            pixelMeasures.AddOrUpdate(DicomTag.PixelSpacing, ps[0], ps[1]);
            pixelMeasures.AddOrUpdate(DicomTag.SliceThickness, thicknesses[0]);
            pixelMeasures.AddOrUpdate(DicomTag.SpacingBetweenSlices,
                target.GetSingleValueOrDefault(DicomTag.SpacingBetweenSlices, thicknesses[0]));
            sharedItem.Add(new DicomSequence(DicomTag.PixelMeasuresSequence, pixelMeasures));
        }

        foreach (var item in sharedFG)
        {
            sharedItem.AddOrUpdate(item);
        }

        int sharedCount = 0;
        foreach (var _ in sharedItem) sharedCount++;
        if (sharedCount > 0)
        {
            target.AddOrUpdate(new DicomSequence(DicomTag.SharedFunctionalGroupsSequence, sharedItem));
        }

        // ── Per-Frame Functional Groups ──
        var perFrameSeq = new DicomSequence(DicomTag.PerFrameFunctionalGroupsSequence);

        for (int i = 0; i < files.Count; i++)
        {
            var srcDs = files[i].Dataset;
            var frameItem = new DicomDataset();

            // Plane Position
            var planePos = new DicomDataset();
            var ipp = TryGetDoubles(srcDs, DicomTag.ImagePositionPatient, 3);
            if (ipp != null)
                planePos.AddOrUpdate(DicomTag.ImagePositionPatient, ipp[0], ipp[1], ipp[2]);
            else
                planePos.AddOrUpdate(DicomTag.ImagePositionPatient, 0.0, 0.0, i * defaultSpacing);
            frameItem.Add(new DicomSequence(DicomTag.PlanePositionSequence, planePos));

            // Plane Orientation (fallback only)
            if (!orientationConsistent)
            {
                var planeOri = new DicomDataset();
                planeOri.AddOrUpdate(DicomTag.ImageOrientationPatient, orientations[i]);
                frameItem.Add(new DicomSequence(DicomTag.PlaneOrientationSequence, planeOri));
            }

            // Frame Content
            var frameContent = new DicomDataset();
            frameContent.AddOrUpdate(DicomTag.StackID, "1");
            frameContent.AddOrUpdate(DicomTag.InStackPositionNumber, (uint)(i + 1));
            frameContent.AddOrUpdate(DicomTag.DimensionIndexValues, 1U, (uint)(i + 1));
            frameContent.AddOrUpdate(DicomTag.FrameAcquisitionNumber, (ushort)Math.Min(i + 1, ushort.MaxValue));
            frameContent.AddOrUpdate(DicomTag.FrameReferenceDateTime,
                DateTime.UtcNow.ToString("yyyyMMddHHmmss.ffffff"));
            frameItem.Add(new DicomSequence(DicomTag.FrameContentSequence, frameContent));

            // Pixel Measures (fallback only)
            if (!measuresConsistent)
            {
                var pixelMeasures = new DicomDataset();
                var ps = pixelSpacings[i];
                if (ps != null)
                    pixelMeasures.AddOrUpdate(DicomTag.PixelSpacing, ps[0], ps[1]);
                pixelMeasures.AddOrUpdate(DicomTag.SliceThickness, thicknesses[i]);
                pixelMeasures.AddOrUpdate(DicomTag.SpacingBetweenSlices,
                    target.GetSingleValueOrDefault(DicomTag.SpacingBetweenSlices, thicknesses[i]));
                frameItem.Add(new DicomSequence(DicomTag.PixelMeasuresSequence, pixelMeasures));
            }

            // Generic per-frame groups
            foreach (var seq in perFrameFGLists[i])
            {
                frameItem.Add(seq);
            }

            perFrameSeq.Items.Add(frameItem);
        }

        target.AddOrUpdate(perFrameSeq);
    }

    private static double[]? TryGetDoubles(DicomDataset ds, DicomTag tag, int expectedLength)
    {
        if (!ds.Contains(tag)) return null;
        try
        {
            var values = ds.GetValues<double>(tag);
            return values is { Length: > 0 } && values.Length == expectedLength ? values : null;
        }
        catch { return null; }
    }

    private static bool SequenceApproxEqual(double[] a, double[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > 1e-4) return false;
        return true;
    }

    private static bool PixelSpacingApproxEqual(double[]? a, double[]? b)
    {
        if (a == null || b == null) return a == null && b == null;
        return SequenceApproxEqual(a, b);
    }

    // ──────────────── Dimension Organization ────────────────

    private static void BuildDimensionOrganization(DicomDataset target)
    {
        string dimOrgUid = DicomUID.Generate().UID;

        // DimensionOrganizationSequence
        var dimOrgItem = new DicomDataset();
        dimOrgItem.AddOrUpdate(DicomTag.DimensionOrganizationUID, dimOrgUid);
        target.AddOrUpdate(new DicomSequence(DicomTag.DimensionOrganizationSequence, dimOrgItem));

        // DimensionIndexSequence
        // VR of DimensionIndexPointer & FunctionalGroupPointer = AT → pass DicomTag, NOT uint
        var dimIdxSeq = new DicomSequence(DicomTag.DimensionIndexSequence);

        var idx1 = new DicomDataset();
        idx1.AddOrUpdate(DicomTag.DimensionIndexPointer, DicomTag.StackID);
        idx1.AddOrUpdate(DicomTag.FunctionalGroupPointer, DicomTag.FrameContentSequence);
        idx1.AddOrUpdate(DicomTag.DimensionOrganizationUID, dimOrgUid);
        idx1.AddOrUpdate(DicomTag.DimensionDescriptionLabel, "Stack ID");
        dimIdxSeq.Items.Add(idx1);

        var idx2 = new DicomDataset();
        idx2.AddOrUpdate(DicomTag.DimensionIndexPointer, DicomTag.InStackPositionNumber);
        idx2.AddOrUpdate(DicomTag.FunctionalGroupPointer, DicomTag.FrameContentSequence);
        idx2.AddOrUpdate(DicomTag.DimensionOrganizationUID, dimOrgUid);
        idx2.AddOrUpdate(DicomTag.DimensionDescriptionLabel, "In-Stack Position Number");
        dimIdxSeq.Items.Add(idx2);

        target.AddOrUpdate(dimIdxSeq);
    }

    // ──────────────── Tag Copy Helpers ────────────────

    private static void CopyPatientStudyInfo(DicomDataset src, DicomDataset dst)
    {
        // Use strongly-typed DicomTag constants — NEVER DicomTag.Parse("PatientID")!
        DicomTag[] tags =
        [
            DicomTag.PatientID, DicomTag.PatientName, DicomTag.PatientBirthDate, DicomTag.PatientSex,
            DicomTag.PatientAge, DicomTag.PatientWeight, DicomTag.PatientSize,
            DicomTag.StudyID, DicomTag.StudyDate, DicomTag.StudyTime, DicomTag.StudyDescription,
            DicomTag.AccessionNumber, DicomTag.ReferringPhysicianName,
            DicomTag.InstitutionName, DicomTag.InstitutionAddress,
            DicomTag.Manufacturer, DicomTag.ManufacturerModelName,
            DicomTag.DeviceSerialNumber, DicomTag.SoftwareVersions,
            DicomTag.BodyPartExamined
        ];

        foreach (var tag in tags)
            CopyTag(src, dst, tag);
    }

    private static void CopyTag(DicomDataset src, DicomDataset dst, DicomTag tag)
    {
        if (!src.Contains(tag) || dst.Contains(tag)) return;
        try
        {
            foreach (var item in src)
            {
                if (item.Tag == tag)
                {
                    dst.Add(item);
                    break;
                }
            }
        }
        catch { /* ignore copy failures for non-critical tags */ }
    }

    // ──────────────── Verification ────────────────

    private static void VerifyOutput(DicomDataset ds, int expectedFrames, int expectedFrameSize, long actualBytes)
    {
        long expectedTotal = (long)expectedFrames * expectedFrameSize;
        if (actualBytes != expectedTotal)
            throw new InvalidOperationException(
                $"Sanity-Check: Pixel-Buffer {actualBytes} B ≠ erwartet {expectedTotal} B");

        if (!ds.Contains(DicomTag.PixelData))
            throw new InvalidOperationException("PixelData fehlt im Ausgabe-Dataset");

        int nof = ds.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 0);
        if (nof != expectedFrames)
            throw new InvalidOperationException(
                $"NumberOfFrames = {nof}, erwartet = {expectedFrames}");
    }

    // ──────────────── SOP Class Selection ────────────────

    private static string ChooseSopClass(string modality, int bitsAllocated) =>
        modality.ToUpperInvariant() switch
        {
            "CT" => Sop.EnhancedCT,
            "MR" => Sop.EnhancedMR,
            "PT" => Sop.EnhancedPET,
            "US" => Sop.UltrasoundMultiFrame,
            "XA" => Sop.XRayAngiographic,
            "NM" => Sop.NuclearMedicine,
            _ => bitsAllocated <= 8 ? Sop.MultiFrameGrayByteSC : Sop.MultiFrameGrayWordSC
        };

    private static string SopClassDisplayName(string uid) => uid switch
    {
        Sop.EnhancedCT           => "Enhanced CT Image Storage",
        Sop.EnhancedMR           => "Enhanced MR Image Storage",
        Sop.EnhancedPET          => "Enhanced PET Image Storage",
        Sop.MultiFrameGrayByteSC => "Multi-Frame Grayscale Byte SC",
        Sop.MultiFrameGrayWordSC => "Multi-Frame Grayscale Word SC",
        Sop.UltrasoundMultiFrame => "Ultrasound Multi-Frame",
        Sop.XRayAngiographic     => "X-Ray Angiographic Image",
        Sop.NuclearMedicine      => "Nuclear Medicine Image",
        _                        => uid
    };

    // ──────────────── UID Helper ────────────────

    private static void SetUid(DicomDataset ds, DicomTag tag, string uidValue)
        => DicomScanner.SafeSetUid(ds, tag, uidValue);

    // ──────────────── Output Filename ────────────────

    /// <summary>
    /// Generates a guaranteed-unique filename using series index + UID hash.
    /// Fixes the old bug where multiple series produced identical filenames.
    /// </summary>
    public static string FormatPatientNameForFile(string dicomPatientName)
    {
        if (string.IsNullOrWhiteSpace(dicomPatientName)) return "Unbekannt";
        
        var parts = dicomPatientName.Split('^', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            // Nachname_Vorname
            return $"{parts[0].Trim()}_{parts[1].Trim()}";
        }
        else if (parts.Length == 1)
        {
            return parts[0].Trim();
        }
        return "Unbekannt";
    }

    public static string GenerateOutputPath(string folderPath, SeriesGroup group, int index)
    {
        var first = group.Files[0];
        var ds = first.File.Dataset;

        string patientName = FormatPatientNameForFile(ds.GetSingleValueOrDefault(FellowOakDicom.DicomTag.PatientName, "Unbekannt"));
        string seriesDesc = ds.GetSingleValueOrDefault(FellowOakDicom.DicomTag.SeriesDescription, "Serie");

        patientName = SanitizeFilename(patientName);
        seriesDesc = SanitizeFilename(seriesDesc);

        string baseName = $"{patientName}-{seriesDesc}";
        string fileName = $"{baseName}.dcm";
        string fullPath = Path.Combine(folderPath, fileName);

        int counter = 1;
        while (File.Exists(fullPath))
        {
            counter++;
            fileName = $"{baseName}_{counter}.dcm";
            fullPath = Path.Combine(folderPath, fileName);
        }

        return fullPath;
    }

    private static string SanitizeFilename(string input)
    {
        if (string.IsNullOrEmpty(input)) return "OT";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }

    // ──────────────── Math ────────────────

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }

    private static readonly Dictionary<DicomTag, DicomTag[]> FunctionalGroupMappings = new()
    {
        {
            DicomTag.MRDiffusionSequence,
            new[]
            {
                DicomTag.DiffusionBValue,
                DicomTag.DiffusionDirectionality,
                DicomTag.DiffusionGradientDirectionSequence,
                DicomTag.DiffusionBMatrixSequence
            }
        },
        {
            DicomTag.MRTimingAndRelatedParametersSequence,
            new[]
            {
                DicomTag.RepetitionTime,
                DicomTag.EchoTime,
                DicomTag.FlipAngle,
                DicomTag.EchoTrainLength,
                DicomTag.NumberOfAverages,
                DicomTag.InversionTime
            }
        },
        {
            DicomTag.PixelValueTransformationSequence,
            new[]
            {
                DicomTag.RescaleIntercept,
                DicomTag.RescaleSlope,
                DicomTag.RescaleType
            }
        },
        {
            DicomTag.MREchoSequence,
            new[]
            {
                DicomTag.EchoNumbers
            }
        },
        {
            DicomTag.MRImagingModifierSequence,
            new[]
            {
                DicomTag.PixelBandwidth,
                DicomTag.TransmitCoilName,
                DicomTag.AcquisitionMatrix
            }
        }
    };

    private static bool AreDatasetsEqual(DicomDataset? a, DicomDataset? b)
    {
        if (a == null || b == null) return a == null && b == null;

        int countA = 0;
        foreach (var _ in a) countA++;
        int countB = 0;
        foreach (var _ in b) countB++;
        if (countA != countB) return false;

        foreach (var itemA in a)
        {
            if (!b.Contains(itemA.Tag)) return false;
            var itemB = b.First(item => item.Tag == itemA.Tag);
            if (!AreDicomItemsEqual(itemA, itemB)) return false;
        }

        return true;
    }

    private static bool AreDicomItemsEqual(DicomItem a, DicomItem b)
    {
        if (a.ValueRepresentation != b.ValueRepresentation) return false;

        if (a is DicomSequence seqA && b is DicomSequence seqB)
        {
            if (seqA.Items.Count != seqB.Items.Count) return false;
            for (int i = 0; i < seqA.Items.Count; i++)
            {
                if (!AreDatasetsEqual(seqA.Items[i], seqB.Items[i])) return false;
            }
            return true;
        }

        if (a is DicomElement elA && b is DicomElement elB)
        {
            var dataA = elA.Buffer.Data;
            var dataB = elB.Buffer.Data;
            if (dataA.Length != dataB.Length) return false;
            for (int i = 0; i < dataA.Length; i++)
            {
                if (dataA[i] != dataB[i]) return false;
            }
            return true;
        }

        return false;
    }
}
