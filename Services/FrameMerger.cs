using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using NewDicomMerger.Models;

namespace NewDicomMerger.Services;

public sealed class FrameMerger
{
    private readonly Action<string> _log;
    private readonly Action<string>? _warn;

    public FrameMerger(Action<string> log, Action<string>? warn = null)
    {
        _log = log;
        _warn = warn;
    }

    private static class Sop
    {
        public const string EnhancedCT = "1.2.840.10008.5.1.4.1.1.2.1";
        public const string EnhancedMR = "1.2.840.10008.5.1.4.1.1.4.1";
        public const string EnhancedPET = "1.2.840.10008.5.1.4.1.1.130";
        public const string MultiFrameGrayByteSC = "1.2.840.10008.5.1.4.1.1.7.2";
        public const string MultiFrameGrayWordSC = "1.2.840.10008.5.1.4.1.1.7.3";
        public const string SecondaryCaptureImage = "1.2.840.10008.5.1.4.1.1.7";
        public const string UltrasoundMultiFrame = "1.2.840.10008.5.1.4.1.1.6.1";
        public const string XRayAngiographic = "1.2.840.10008.5.1.4.1.1.12.1";
        public const string NuclearMedicine = "1.2.840.10008.5.1.4.1.1.20";
    }

    public void Merge(
        SeriesGroup group,
        string outputPath,
        string patientName,
        string seriesDescription,
        bool anonymize = false,
        bool compressDicom = false,
        CancellationToken ct = default,
        SeriesDeidentifier? anonymizer = null,
        bool formatBrainlabDti = false,
        bool generateAuditReport = true)
    {
        ct.ThrowIfCancellationRequested();

        var files = group.Files;

        if (files.Count == 0)
            throw new ArgumentException("SeriesGroup enthält keine Dateien.", nameof(group));

        var first = files[0];

        bool anyMultiFrame = files.Any(f => f.IsMultiFrame);

        if (anyMultiFrame)
        {
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];

                try
                {
                    var fullFile = DicomFile.Open(
                        file.FilePath,
                        DicomScanner.LegacyFallbackEncoding,
                        readOption: FileReadOption.ReadAll);

                    if (!string.IsNullOrWhiteSpace(patientName))
                        fullFile.Dataset.AddOrUpdate(DicomTag.PatientName, patientName);

                    if (!string.IsNullOrWhiteSpace(seriesDescription))
                        fullFile.Dataset.AddOrUpdate(DicomTag.SeriesDescription, seriesDescription);

                    int? bValMulti = DiffusionBValueHelper.ExtractBValue(fullFile.Dataset);

                    if (formatBrainlabDti &&
                        DiffusionBValueHelper.IsDiffusionOrDtiOrDki(fullFile.Dataset))
                    {
                        DiffusionBValueHelper.ApplyBrainlabDtiFormatting(
                            fullFile.Dataset,
                            bValMulti,
                            _warn,
                            _log);
                    }

                    if (anonymize && anonymizer != null)
                        anonymizer.Anonymize(fullFile.Dataset);

                    string outPath = outputPath;

                    if (files.Count > 1)
                    {
                        string dir = Path.GetDirectoryName(outputPath) ?? "";
                        string fn = Path.GetFileNameWithoutExtension(outputPath);
                        string ext = Path.GetExtension(outputPath);

                        outPath = Path.Combine(dir, $"{fn}_{i + 1}{ext}");
                    }

                    fullFile.Save(outPath);

                    int mfCount = file.NumberOfFrames;

                    _log(
                        $"✓ Multi-Frame Volume " +
                        $"{(files.Count > 1 ? $"{i + 1}/{files.Count} " : "")}" +
                        $"verarbeitet ({mfCount} Frames): {Path.GetFileName(outPath)}");
                }
                catch (Exception ex)
                {
                    _warn?.Invoke(
                        $"Fehler beim Verarbeiten des Multi-Frame Volumes " +
                        $"{Path.GetFileName(file.FilePath)}: {ex.Message}");
                }
            }

            return;
        }

        if (files.Count == 1)
        {
            var file = files[0];

            var fullFile = DicomFile.Open(
                file.FilePath,
                DicomScanner.LegacyFallbackEncoding,
                readOption: FileReadOption.ReadAll);

            if (fullFile.FileMetaInfo.TransferSyntax.IsEncapsulated)
            {
                var transcoder =
                    new FellowOakDicom.Imaging.Codec.DicomTranscoder(
                        fullFile.FileMetaInfo.TransferSyntax,
                        DicomTransferSyntax.ExplicitVRLittleEndian);

                fullFile = transcoder.Transcode(fullFile);
            }

            if (!string.IsNullOrWhiteSpace(patientName))
                fullFile.Dataset.AddOrUpdate(DicomTag.PatientName, patientName);

            if (!string.IsNullOrWhiteSpace(seriesDescription))
                fullFile.Dataset.AddOrUpdate(DicomTag.SeriesDescription, seriesDescription);

            int? bValSingle = DiffusionBValueHelper.ExtractBValue(fullFile.Dataset);

            if (formatBrainlabDti &&
                DiffusionBValueHelper.IsDiffusionOrDtiOrDki(fullFile.Dataset))
            {
                DiffusionBValueHelper.ApplyBrainlabDtiFormatting(
                    fullFile.Dataset,
                    bValSingle,
                    _warn,
                    _log);
            }

            if (anonymize && anonymizer != null)
                anonymizer.Anonymize(fullFile.Dataset);

            if (compressDicom)
            {
                try
                {
                    var transcoder =
                        new FellowOakDicom.Imaging.Codec.DicomTranscoder(
                            fullFile.FileMetaInfo.TransferSyntax,
                            DicomTransferSyntax.JPEGProcess14SV1);

                    fullFile = transcoder.Transcode(fullFile);
                }
                catch
                {
                }
            }

            fullFile.Save(outputPath);

            _log(
                $"✓ Einzelbild-Serie verarbeitet (1 Frame): " +
                $"{Path.GetFileName(outputPath)}");

            return;
        }

        int frameCount = files.Count;

        DicomTransferSyntax ts;

        try
        {
            ts = DicomTransferSyntax.Lookup(
                DicomUID.Parse(first.TransferSyntaxUid));
        }
        catch
        {
            ts = DicomTransferSyntax.ExplicitVRLittleEndian;
        }

        int rows = first.Rows;
        int cols = first.Columns;
        int bitsAllocated = first.BitsAllocated;
        int samplesPerPixel = first.SamplesPerPixel;
        int bytesPerPixel = bitsAllocated / 8;
        int expectedFrameBytes =
            rows *
            cols *
            samplesPerPixel *
            bytesPerPixel;

        var outFile = new DicomFile();
        var meta = outFile.FileMetaInfo;
        var ds = outFile.Dataset;

        SetUid(
            meta,
            DicomTag.TransferSyntaxUID,
            DicomTransferSyntax.ExplicitVRLittleEndian.UID.UID);

        meta.AddOrUpdate(
            DicomTag.ImplementationClassUID,
            DicomImplementation.ClassUID);

        meta.AddOrUpdate(
            DicomTag.ImplementationVersionName,
            DicomImplementation.Version);

        string sopClassUid =
            ChooseSopClass(first.Modality, first.BitsAllocated);

        SetUid(
            meta,
            DicomTag.MediaStorageSOPClassUID,
            sopClassUid);

        SetUid(
            ds,
            DicomTag.SOPClassUID,
            sopClassUid);

        string newSopInstance = DicomUID.Generate().UID;

        SetUid(
            meta,
            DicomTag.MediaStorageSOPInstanceUID,
            newSopInstance);

        SetUid(
            ds,
            DicomTag.SOPInstanceUID,
            newSopInstance);

        SetUid(
            ds,
            DicomTag.StudyInstanceUID,
            group.StudyInstanceUid);

        SetUid(
            ds,
            DicomTag.SeriesInstanceUID,
            group.SeriesInstanceUid);

        CopyPatientStudyInfo(first.Dataset, ds);

        if (!string.IsNullOrWhiteSpace(patientName))
            ds.AddOrUpdate(DicomTag.PatientName, patientName);

        if (!string.IsNullOrWhiteSpace(seriesDescription))
            ds.AddOrUpdate(DicomTag.SeriesDescription, seriesDescription);

        int? bVal =
            DiffusionBValueHelper.ExtractBValue(first.Dataset);

        if (bVal.HasValue)
        {
            DiffusionBValueHelper.SafeAddBValueTag(
                ds,
                DicomTag.DiffusionBValue,
                bVal.Value);
        }

        if (formatBrainlabDti &&
            DiffusionBValueHelper.IsDiffusionOrDtiOrDki(first.Dataset))
        {
            DiffusionBValueHelper.ApplyBrainlabDtiFormatting(
                ds,
                bVal,
                _warn,
                _log);
        }

        ds.AddOrUpdate(
            DicomTag.Rows,
            (ushort)rows);

        ds.AddOrUpdate(
            DicomTag.Columns,
            (ushort)cols);

        ds.AddOrUpdate(
            DicomTag.BitsAllocated,
            (ushort)bitsAllocated);

        ds.AddOrUpdate(
            DicomTag.BitsStored,
            (ushort)first.BitsStored);

        ds.AddOrUpdate(
            DicomTag.HighBit,
            (ushort)first.HighBit);

        ds.AddOrUpdate(
            DicomTag.PixelRepresentation,
            (ushort)first.PixelRepresentation);

        ds.AddOrUpdate(
            DicomTag.SamplesPerPixel,
            (ushort)samplesPerPixel);

        ds.AddOrUpdate(
            DicomTag.PhotometricInterpretation,
            first.PhotometricInterpretation);

        ds.AddOrUpdate(
            DicomTag.Modality,
            first.Modality);

        if (samplesPerPixel > 1)
        {
            ds.AddOrUpdate(
                DicomTag.PlanarConfiguration,
                (ushort)first.Dataset.GetSingleValueOrDefault(
                    DicomTag.PlanarConfiguration,
                    (ushort)0));
        }

        long totalExpectedBytes =
            (long)expectedFrameBytes *
            frameCount;

        if (totalExpectedBytes > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"Zusammengefügte Serie überschreitet das 2GB Pufferlimit " +
                $"({totalExpectedBytes} Bytes).");
        }

        byte[] allPixelBytes =
            new byte[(int)totalExpectedBytes];

        long totalPixelBytes = 0;
        string? decodedPhotometric = null;

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var fullFile = DicomFile.Open(
                files[i].FilePath,
                DicomScanner.LegacyFallbackEncoding,
                readOption: FileReadOption.ReadAll);

            if (fullFile.FileMetaInfo.TransferSyntax.IsEncapsulated)
            {
                var transcoder =
                    new FellowOakDicom.Imaging.Codec.DicomTranscoder(
                        fullFile.FileMetaInfo.TransferSyntax,
                        DicomTransferSyntax.ExplicitVRLittleEndian);

                fullFile = transcoder.Transcode(fullFile);
            }

            var srcPixelData =
                DicomPixelData.Create(fullFile.Dataset);

            decodedPhotometric ??=
                fullFile.Dataset.GetSingleValueOrDefault(
                    DicomTag.PhotometricInterpretation,
                    first.PhotometricInterpretation);

            var frameBuffer =
                srcPixelData.GetFrame(0);

            byte[] frameBytes =
                frameBuffer.Data;

            if (frameBytes.Length != expectedFrameBytes)
            {
                _warn?.Invoke(
                    $"Frame {i + 1} Byte-Länge unterscheidet sich: " +
                    $"erwartet {expectedFrameBytes}, " +
                    $"erhalten {frameBytes.Length}");
            }

            Buffer.BlockCopy(
                frameBytes,
                0,
                allPixelBytes,
                (int)totalPixelBytes,
                frameBytes.Length);

            totalPixelBytes += frameBytes.Length;
        }

        if (decodedPhotometric != null)
        {
            ds.AddOrUpdate(
                DicomTag.PhotometricInterpretation,
                decodedPhotometric);
        }

        var pixelData =
            DicomPixelData.Create(ds, true);

        IByteBuffer flatBuffer =
            new MemoryByteBuffer(allPixelBytes);

        for (int i = 0; i < frameCount; i++)
        {
            var rangeBuffer =
                new RangeByteBuffer(
                    flatBuffer,
                    (long)i * expectedFrameBytes,
                    expectedFrameBytes);

            pixelData.AddFrame(rangeBuffer);
        }

        BuildSharedAndPerFrameFunctionalGroups(
            files,
            ds,
            sopClassUid);

        string? dirPath =
            Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrEmpty(dirPath))
            Directory.CreateDirectory(dirPath);

        if (anonymize && anonymizer != null)
            anonymizer.Anonymize(ds);

        if (compressDicom)
        {
            try
            {
                var transcoder =
                    new FellowOakDicom.Imaging.Codec.DicomTranscoder(
                        DicomTransferSyntax.ExplicitVRLittleEndian,
                        DicomTransferSyntax.JPEGProcess14SV1);

                outFile =
                    transcoder.Transcode(outFile);
            }
            catch (Exception ex)
            {
                _warn?.Invoke(
                    $"Lossless Kompression fehlgeschlagen, " +
                    $"speichere unkomprimiert: {ex.Message}");
            }
        }

        outFile.Save(outputPath);

        _log(
            $"✓ Erstellt: {Path.GetFileName(outputPath)} " +
            $"({frameCount} Slices, " +
            $"{totalPixelBytes / 1048576.0:F1} MB)");

        if (generateAuditReport)
        {
            GenerateAuditReport(
                group,
                outputPath,
                newSopInstance,
                rows,
                cols);
        }
    }

    private void GenerateAuditReport(
        SeriesGroup group,
        string outputPath,
        string sopInstanceUid,
        int rows,
        int cols)
    {
        try
        {
            var report = new BrainlabAuditReport
            {
                SourceStudyInstanceUID =
                    group.StudyInstanceUid,

                SourceSeriesInstanceUIDs =
                    new List<string>
                    {
                        group.SeriesInstanceUid
                    },

                OutputSeriesInstanceUID =
                    group.SeriesInstanceUid,

                OutputSOPInstanceUIDs =
                    new List<string>
                    {
                        sopInstanceUid
                    },

                NominalShellBValue =
                    group.Files
                        .Select(
                            f =>
                                DiffusionBValueHelper.ExtractBValue(
                                    f.Dataset) ?? 0)
                        .FirstOrDefault(
                            b => b > 0),

                Matrix =
                    new[]
                    {
                        rows,
                        cols
                    },

                FrameCount =
                    group.Files.Count,

                PatientPosition =
                    group.Files[0].Dataset.GetSingleValueOrDefault(
                        DicomTag.PatientPosition,
                        "HFS")
            };

            var iop =
                TryGetDoubles(
                    group.Files[0].Dataset,
                    DicomTag.ImageOrientationPatient,
                    6);

            if (iop != null)
            {
                report.ObliquityDegrees =
                    BatchReportGenerator.CalculateObliquityDegrees(iop);

                report.OrientationClass =
                    report.ObliquityDegrees > 15.0
                        ? "AXIAL_HIGH_OBLIQUITY"
                        : "AXIAL_OBLIQUE";
            }

            var ps =
                TryGetDoubles(
                    group.Files[0].Dataset,
                    DicomTag.PixelSpacing,
                    2);

            if (ps != null && ps.Length >= 2)
            {
                report.PixelSpacingMm =
                    new[]
                    {
                        ps[0],
                        ps[1]
                    };
            }

            double thickness =
                group.Files[0].Dataset.GetSingleValueOrDefault(
                    DicomTag.SliceThickness,
                    0.0);

            report.SliceThicknessMm =
                thickness;

            report.SliceSpacingMm =
                group.Files[0].Dataset.GetSingleValueOrDefault(
                    DicomTag.SpacingBetweenSlices,
                    thickness);

            var bVals =
                group.Files
                    .Select(
                        f =>
                            (double)(
                                DiffusionBValueHelper.ExtractBValue(
                                    f.Dataset) ?? 0))
                    .ToList();

            if (bVals.Count > 0)
            {
                report.ActualBValueRange =
                    new[]
                    {
                        bVals.Min(),
                        bVals.Max()
                    };

                report.B0VolumeCount =
                    bVals.Count(b => b <= 10);

                report.DirectionCount =
                    bVals.Count(b => b > 10);
            }

            report.OutputFileHashes[
                    Path.GetFileName(outputPath)] =
                BatchReportGenerator.ComputeSha256(
                    outputPath);

            BatchReportGenerator.SaveReport(
                report,
                outputPath);
        }
        catch
        {
        }
    }

    private static void CopyPatientStudyInfo(
        DicomDataset src,
        DicomDataset ds)
    {
        CopyTag(src, ds, DicomTag.PatientName);
        CopyTag(src, ds, DicomTag.PatientID);
        CopyTag(src, ds, DicomTag.PatientBirthDate);
        CopyTag(src, ds, DicomTag.PatientSex);
        CopyTag(src, ds, DicomTag.PatientAge);
        CopyTag(src, ds, DicomTag.PatientWeight);

        CopyTag(src, ds, DicomTag.StudyDate);
        CopyTag(src, ds, DicomTag.StudyTime);
        CopyTag(src, ds, DicomTag.AccessionNumber);
        CopyTag(src, ds, DicomTag.StudyID);
        CopyTag(src, ds, DicomTag.ReferringPhysicianName);
        CopyTag(src, ds, DicomTag.StudyDescription);

        CopyTag(src, ds, DicomTag.SeriesDate);
        CopyTag(src, ds, DicomTag.SeriesTime);
        CopyTag(src, ds, DicomTag.SeriesNumber);
        CopyTag(src, ds, DicomTag.PatientPosition);
        CopyTag(src, ds, DicomTag.BodyPartExamined);

        CopyTag(src, ds, DicomTag.Manufacturer);
        CopyTag(src, ds, DicomTag.InstitutionName);
        CopyTag(src, ds, DicomTag.StationName);
        CopyTag(src, ds, DicomTag.ManufacturerModelName);
        CopyTag(src, ds, DicomTag.SoftwareVersions);
        CopyTag(src, ds, DicomTag.DeviceSerialNumber);
        CopyTag(src, ds, DicomTag.ProtocolName);

        CopyTag(src, ds, DicomTag.FrameOfReferenceUID);
        CopyTag(src, ds, DicomTag.ImageType);

        double? spacing =
            ComputeSliceSpacing(src, ds);
    }

    private static double? ComputeSliceSpacing(
        DicomDataset src,
        DicomDataset ds)
    {
        if (src.Contains(DicomTag.SliceThickness))
        {
            try
            {
                double t =
                    src.GetSingleValue<double>(
                        DicomTag.SliceThickness);

                if (t > 0)
                {
                    ds.AddOrUpdate(
                        DicomTag.SpacingBetweenSlices,
                        t);

                    ds.AddOrUpdate(
                        DicomTag.SliceThickness,
                        t);

                    return t;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private void BuildSharedAndPerFrameFunctionalGroups(
        List<LoadedDicom> files,
        DicomDataset target,
        string sopClassUid)
    {
        int count = files.Count;

        double defaultSpacing =
            target.GetSingleValueOrDefault(
                DicomTag.SpacingBetweenSlices,
                1.0);

        string dimOrgUid =
            DicomUID.Generate().UID;

        var dimOrganizationSeq =
            new DicomSequence(
                DicomTag.DimensionOrganizationSequence,
                new DicomDataset(
                    new DicomUniqueIdentifier(
                        DicomTag.DimensionOrganizationUID,
                        dimOrgUid)));

        target.AddOrUpdate(
            dimOrganizationSeq);

        var dimIndexSeq =
            new DicomSequence(
                DicomTag.DimensionIndexSequence,
                new DicomDataset(
                    new DicomAttributeTag(
                        DicomTag.DimensionIndexPointer,
                        DicomTag.InStackPositionNumber),
                    new DicomAttributeTag(
                        DicomTag.FunctionalGroupPointer,
                        DicomTag.FrameContentSequence),
                    new DicomUniqueIdentifier(
                        DicomTag.DimensionOrganizationUID,
                        dimOrgUid)),
                new DicomDataset(
                    new DicomAttributeTag(
                        DicomTag.DimensionIndexPointer,
                        DicomTag.FrameAcquisitionNumber),
                    new DicomAttributeTag(
                        DicomTag.FunctionalGroupPointer,
                        DicomTag.FrameContentSequence),
                    new DicomUniqueIdentifier(
                        DicomTag.DimensionOrganizationUID,
                        dimOrgUid)));

        target.AddOrUpdate(
            dimIndexSeq);

        var orientations =
            new List<double[]>();

        var pixelSpacings =
            new List<double[]?>();

        var thicknesses =
            new List<double>();

        var sharedFG =
            new List<DicomSequence>();

        var perFrameFGLists =
            new List<List<DicomSequence>>();

        for (int i = 0; i < count; i++)
        {
            var s =
                files[i].Dataset;

            orientations.Add(
                TryGetDoubles(
                    s,
                    DicomTag.ImageOrientationPatient,
                    6)
                ??
                new[]
                {
                    1.0,
                    0,
                    0,
                    0,
                    1.0,
                    0
                });

            pixelSpacings.Add(
                TryGetDoubles(
                    s,
                    DicomTag.PixelSpacing,
                    2));

            thicknesses.Add(
                s.GetSingleValueOrDefault(
                    DicomTag.SliceThickness,
                    defaultSpacing));

            perFrameFGLists.Add(
                new List<DicomSequence>());
        }

        bool orientationConsistent =
            orientations.All(
                o =>
                    Math.Abs(
                        o[0] -
                        orientations[0][0]) < 1e-4 &&
                    Math.Abs(
                        o[1] -
                        orientations[0][1]) < 1e-4 &&
                    Math.Abs(
                        o[2] -
                        orientations[0][2]) < 1e-4 &&
                    Math.Abs(
                        o[3] -
                        orientations[0][3]) < 1e-4 &&
                    Math.Abs(
                        o[4] -
                        orientations[0][4]) < 1e-4 &&
                    Math.Abs(
                        o[5] -
                        orientations[0][5]) < 1e-4);

        bool spacingsConsistent =
            pixelSpacings.All(
                ps =>
                    ps != null &&
                    pixelSpacings[0] != null &&
                    Math.Abs(
                        ps[0] -
                        pixelSpacings[0]![0]) < 1e-4 &&
                    Math.Abs(
                        ps[1] -
                        pixelSpacings[0]![1]) < 1e-4);

        bool thicknessesConsistent =
            thicknesses.All(
                t =>
                    Math.Abs(
                        t -
                        thicknesses[0]) < 1e-4);

        bool measuresConsistent =
            spacingsConsistent &&
            thicknessesConsistent;

        var sharedItem =
            new DicomDataset();

        if (orientationConsistent)
        {
            var planeOri =
                new DicomDataset();

            planeOri.AddOrUpdate(
                DicomTag.ImageOrientationPatient,
                orientations[0]);

            sharedItem.Add(
                new DicomSequence(
                    DicomTag.PlaneOrientationSequence,
                    planeOri));
        }

        if (measuresConsistent)
        {
            var pixelMeasures =
                new DicomDataset();

            var ps =
                pixelSpacings[0] ??
                new[]
                {
                    1.0,
                    1.0
                };

            pixelMeasures.AddOrUpdate(
                DicomTag.PixelSpacing,
                ps[0],
                ps[1]);

            pixelMeasures.AddOrUpdate(
                DicomTag.SliceThickness,
                thicknesses[0]);

            pixelMeasures.AddOrUpdate(
                DicomTag.SpacingBetweenSlices,
                target.GetSingleValueOrDefault(
                    DicomTag.SpacingBetweenSlices,
                    thicknesses[0]));

            sharedItem.Add(
                new DicomSequence(
                    DicomTag.PixelMeasuresSequence,
                    pixelMeasures));
        }

        var regionItem =
            new DicomDataset();

        regionItem.AddOrUpdate(
            DicomTag.CodeValue,
            "69536005");

        regionItem.AddOrUpdate(
            DicomTag.CodingSchemeDesignator,
            "SCT");

        regionItem.AddOrUpdate(
            DicomTag.CodeMeaning,
            "Head");

        regionItem.AddOrUpdate(
            DicomTag.ContextGroupExtensionFlag,
            "N");

        regionItem.AddOrUpdate(
            DicomTag.ContextIdentifier,
            "4028");

        var frameAnatomyItem =
            new DicomDataset();

        frameAnatomyItem.AddOrUpdate(
            DicomTag.FrameLaterality,
            "U");

        frameAnatomyItem.AddOrUpdate(
            new DicomSequence(
                DicomTag.AnatomicRegionSequence,
                regionItem));

        sharedItem.Add(
            new DicomSequence(
                DicomTag.FrameAnatomySequence,
                frameAnatomyItem));

        bool isDiffusion =
            DiffusionBValueHelper.IsDiffusionOrDtiOrDki(
                files[0].Dataset);

        var frameTypeItem =
            new DicomDataset();

        if (isDiffusion)
        {
            frameTypeItem.AddOrUpdate(
                DicomTag.FrameType,
                "ORIGINAL",
                "PRIMARY",
                "DIFFUSION",
                "NONE");

            frameTypeItem.AddOrUpdate(
                DicomTag.PixelPresentation,
                "MONOCHROME");

            frameTypeItem.AddOrUpdate(
                DicomTag.VolumetricProperties,
                "VOLUME");

            frameTypeItem.AddOrUpdate(
                DicomTag.VolumeBasedCalculationTechnique,
                "NONE");

            frameTypeItem.AddOrUpdate(
                DicomTag.ComplexImageComponent,
                "MAGNITUDE");

            frameTypeItem.AddOrUpdate(
                DicomTag.AcquisitionContrast,
                "DIFFUSION");
        }
        else
        {
            frameTypeItem.AddOrUpdate(
                DicomTag.FrameType,
                "ORIGINAL",
                "PRIMARY",
                "OTHER",
                "NONE");

            frameTypeItem.AddOrUpdate(
                DicomTag.PixelPresentation,
                "MONOCHROME");

            frameTypeItem.AddOrUpdate(
                DicomTag.VolumetricProperties,
                "VOLUME");

            frameTypeItem.AddOrUpdate(
                DicomTag.VolumeBasedCalculationTechnique,
                "NONE");

            frameTypeItem.AddOrUpdate(
                DicomTag.ComplexImageComponent,
                "MAGNITUDE");

            frameTypeItem.AddOrUpdate(
                DicomTag.AcquisitionContrast,
                "UNKNOWN");
        }

        sharedItem.Add(
            new DicomSequence(
                DicomTag.MRImageFrameTypeSequence,
                frameTypeItem));

        var pvtItem =
            new DicomDataset();

        pvtItem.AddOrUpdate(
            DicomTag.RescaleIntercept,
            "0");

        pvtItem.AddOrUpdate(
            DicomTag.RescaleSlope,
            "1");

        pvtItem.AddOrUpdate(
            DicomTag.RescaleType,
            "US");

        sharedItem.Add(
            new DicomSequence(
                DicomTag.PixelValueTransformationSequence,
                pvtItem));

        foreach (var item in sharedFG)
            sharedItem.AddOrUpdate(item);

        target.AddOrUpdate(
            new DicomSequence(
                DicomTag.SharedFunctionalGroupsSequence,
                sharedItem));

        double[] mainOri =
            orientations[0];

        double nx =
            mainOri[1] * mainOri[5] -
            mainOri[2] * mainOri[4];

        double ny =
            mainOri[2] * mainOri[3] -
            mainOri[0] * mainOri[5];

        double nz =
            mainOri[0] * mainOri[4] -
            mainOri[1] * mainOri[3];

        double norm =
            Math.Sqrt(
                nx * nx +
                ny * ny +
                nz * nz);

        if (norm > 1e-6)
        {
            nx /= norm;
            ny /= norm;
            nz /= norm;
        }

        var sliceCoords =
            new HashSet<double>();

        for (int i = 0; i < files.Count; i++)
        {
            var ipp =
                TryGetDoubles(
                    files[i].Dataset,
                    DicomTag.ImagePositionPatient,
                    3);

            if (ipp != null &&
                ipp.Length >= 3)
            {
                double proj =
                    ipp[0] * nx +
                    ipp[1] * ny +
                    ipp[2] * nz;

                sliceCoords.Add(
                    Math.Round(proj, 2));
            }
        }

        int nSlices =
            sliceCoords.Count > 0 &&
            files.Count % sliceCoords.Count == 0
                ? sliceCoords.Count
                : files.Count;

        var perFrameSeq =
            new DicomSequence(
                DicomTag.PerFrameFunctionalGroupsSequence);

        for (int i = 0; i < files.Count; i++)
        {
            var srcDs =
                files[i].Dataset;

            var frameItem =
                new DicomDataset();

            uint inStackPos =
                (uint)((i % nSlices) + 1);

            uint volumeIdx =
                (uint)((i / nSlices) + 1);

            var planePos =
                new DicomDataset();

            var ipp =
                TryGetDoubles(
                    srcDs,
                    DicomTag.ImagePositionPatient,
                    3);

            if (ipp != null)
            {
                planePos.AddOrUpdate(
                    DicomTag.ImagePositionPatient,
                    ipp[0],
                    ipp[1],
                    ipp[2]);
            }
            else
            {
                planePos.AddOrUpdate(
                    DicomTag.ImagePositionPatient,
                    0.0,
                    0.0,
                    i * defaultSpacing);
            }

            frameItem.Add(
                new DicomSequence(
                    DicomTag.PlanePositionSequence,
                    planePos));

            if (!orientationConsistent)
            {
                var planeOri =
                    new DicomDataset();

                planeOri.AddOrUpdate(
                    DicomTag.ImageOrientationPatient,
                    orientations[i]);

                frameItem.Add(
                    new DicomSequence(
                        DicomTag.PlaneOrientationSequence,
                        planeOri));
            }

            var frameContent =
                new DicomDataset();

            frameContent.AddOrUpdate(
                DicomTag.StackID,
                "1");

            frameContent.AddOrUpdate(
                DicomTag.InStackPositionNumber,
                inStackPos);

            frameContent.AddOrUpdate(
                DicomTag.FrameAcquisitionNumber,
                (ushort)Math.Min(
                    volumeIdx,
                    ushort.MaxValue));

            frameContent.AddOrUpdate(
                DicomTag.DimensionIndexValues,
                inStackPos,
                volumeIdx);

            frameContent.AddOrUpdate(
                DicomTag.FrameReferenceDateTime,
                DateTime.UtcNow.ToString(
                    "yyyyMMddHHmmss.ffffff"));

            frameItem.Add(
                new DicomSequence(
                    DicomTag.FrameContentSequence,
                    frameContent));

            DiffusionBValueHelper.PopulateMrDiffusionMacro(
                frameItem,
                srcDs);

            if (!measuresConsistent)
            {
                var pixelMeasures =
                    new DicomDataset();

                var ps =
                    pixelSpacings[i];

                if (ps != null)
                {
                    pixelMeasures.AddOrUpdate(
                        DicomTag.PixelSpacing,
                        ps[0],
                        ps[1]);
                }

                pixelMeasures.AddOrUpdate(
                    DicomTag.SliceThickness,
                    thicknesses[i]);

                pixelMeasures.AddOrUpdate(
                    DicomTag.SpacingBetweenSlices,
                    target.GetSingleValueOrDefault(
                        DicomTag.SpacingBetweenSlices,
                        thicknesses[i]));

                frameItem.Add(
                    new DicomSequence(
                        DicomTag.PixelMeasuresSequence,
                        pixelMeasures));
            }

            foreach (var seq in perFrameFGLists[i])
                frameItem.Add(seq);

            perFrameSeq.Items.Add(
                frameItem);
        }

        target.AddOrUpdate(
            perFrameSeq);
    }

    private static double[]? TryGetDoubles(
        DicomDataset ds,
        DicomTag tag,
        int expectedLength)
    {
        if (!ds.Contains(tag))
            return null;

        try
        {
            var values =
                ds.GetValues<double>(tag);

            return values is { Length: > 0 } &&
                   values.Length == expectedLength
                ? values
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyTag(
        DicomDataset src,
        DicomDataset dest,
        DicomTag tag)
    {
        if (!src.Contains(tag))
            return;

        try
        {
            dest.AddOrUpdate(
                src.GetDicomItem<DicomItem>(tag));
        }
        catch
        {
        }
    }

    private static string ChooseSopClass(
        string modality,
        int bitsAllocated)
    {
        return modality.ToUpperInvariant() switch
        {
            "CT" => Sop.EnhancedCT,
            "MR" => Sop.EnhancedMR,
            "PT" => Sop.EnhancedPET,
            "US" => Sop.UltrasoundMultiFrame,
            "XA" => Sop.XRayAngiographic,
            "NM" => Sop.NuclearMedicine,
            _ => bitsAllocated <= 8
                ? Sop.MultiFrameGrayByteSC
                : Sop.MultiFrameGrayWordSC
        };
    }

    private static void SetUid(
        DicomDataset ds,
        DicomTag tag,
        string uidValue)
    {
        DicomScanner.SafeSetUid(
            ds,
            tag,
            uidValue);
    }
}