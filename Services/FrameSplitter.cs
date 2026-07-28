using System;
using System.IO;
using System.Linq;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using NewDicomMerger.Models;

namespace NewDicomMerger.Services;

public sealed class FrameSplitter
{
    private readonly Action<string> _log;
    private readonly Action<string>? _warn;

    public FrameSplitter(Action<string> log, Action<string>? warn = null)
    {
        _log = log;
        _warn = warn;
    }

    private static class Sop
    {
        public const string CTImageStorage = "1.2.840.10008.5.1.4.1.1.2";
        public const string MRImageStorage = "1.2.840.10008.5.1.4.1.1.4";
        public const string PETImageStorage = "1.2.840.10008.5.1.4.1.1.128";
        public const string UltrasoundImageStorage = "1.2.840.10008.5.1.4.1.1.6.1";
        public const string XRayAngiographicImageStorage = "1.2.840.10008.5.1.4.1.1.12.1";
        public const string NuclearMedicineImageStorage = "1.2.840.10008.5.1.4.1.1.20";
        public const string SecondaryCaptureImageStorage = "1.2.840.10008.5.1.4.1.1.7";
    }

    public void Split(LoadedDicom file, string outputDirectory, string patientName, string seriesDescription, bool anonymize = false, CancellationToken ct = default, SeriesDeidentifier? anonymizer = null, bool splitByBValue = false, bool formatBrainlabDti = false)
    {
        ct.ThrowIfCancellationRequested();
        var fullFile = DicomFile.Open(file.FilePath, DicomScanner.LegacyFallbackEncoding, readOption: FileReadOption.ReadAll);
        if (fullFile.FileMetaInfo.TransferSyntax.IsEncapsulated)
        {
            var transcoder = new FellowOakDicom.Imaging.Codec.DicomTranscoder(fullFile.FileMetaInfo.TransferSyntax, DicomTransferSyntax.ExplicitVRLittleEndian);
            fullFile = transcoder.Transcode(fullFile);
        }
        var masterDs = fullFile.Dataset;

        if (!string.IsNullOrWhiteSpace(patientName))
            masterDs.AddOrUpdate(DicomTag.PatientName, patientName);
        if (!string.IsNullOrWhiteSpace(seriesDescription))
            masterDs.AddOrUpdate(DicomTag.SeriesDescription, seriesDescription);

        var pixelData = DicomPixelData.Create(masterDs);
        int frameCount = pixelData.NumberOfFrames;

        if (frameCount < 1)
        {
            _warn?.Invoke($"Die Datei {Path.GetFileName(file.FilePath)} enthält keine Frames ({frameCount}) und wird übersprungen.");
            return;
        }

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string newSopClassUid = ChooseSingleFrameSopClass(file.Modality);

        var perFrameSeq = masterDs.Contains(DicomTag.PerFrameFunctionalGroupsSequence) 
            ? masterDs.GetSequence(DicomTag.PerFrameFunctionalGroupsSequence) 
            : null;

        var sharedSeq = masterDs.Contains(DicomTag.SharedFunctionalGroupsSequence)
            ? masterDs.GetSequence(DicomTag.SharedFunctionalGroupsSequence)
            : null;
        var sharedItem = (sharedSeq != null && sharedSeq.Items.Count > 0) ? sharedSeq.Items[0] : null;

        string baseSeriesUid = masterDs.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, DicomUID.Generate().UID);
        string baseSeriesDesc = masterDs.GetSingleValueOrDefault(DicomTag.SeriesDescription, "Serie");

        for (int i = 0; i < frameCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var frameDs = masterDs.Clone();

            frameDs.Remove(DicomTag.NumberOfFrames);
            frameDs.Remove(DicomTag.PerFrameFunctionalGroupsSequence);
            frameDs.Remove(DicomTag.SharedFunctionalGroupsSequence);
            frameDs.Remove(DicomTag.DimensionOrganizationSequence);
            frameDs.Remove(DicomTag.DimensionIndexSequence);

            DicomScanner.SafeSetUid(frameDs, DicomTag.SOPClassUID, newSopClassUid);
            string newSopInstance = DicomUID.Generate().UID;
            DicomScanner.SafeSetUid(frameDs, DicomTag.SOPInstanceUID, newSopInstance);

            int? frameB = DiffusionBValueHelper.ExtractFrameBValue(masterDs, i);

            if (frameB.HasValue)
            {
                DiffusionBValueHelper.SafeAddBValueTag(frameDs, DicomTag.DiffusionBValue, frameB.Value);
                if (splitByBValue)
                {
                    string bSeriesUid = DiffusionBValueHelper.GenerateDerivedUid(baseSeriesUid, frameB.Value);
                    DicomScanner.SafeSetUid(frameDs, DicomTag.SeriesInstanceUID, bSeriesUid);
                    string bDesc = baseSeriesDesc.Contains($"_b{frameB.Value}", StringComparison.OrdinalIgnoreCase)
                        ? baseSeriesDesc
                        : $"{baseSeriesDesc}_b{frameB.Value}";
                    frameDs.AddOrUpdate(DicomTag.SeriesDescription, bDesc);
                }
            }

            if (formatBrainlabDti && DiffusionBValueHelper.IsDiffusionOrDtiOrDki(masterDs))
            {
                DiffusionBValueHelper.ApplyBrainlabDtiFormatting(frameDs, frameB, _warn, _log);
            }

            frameDs.AddOrUpdate(DicomTag.InstanceNumber, i + 1);

            var frameItem = (perFrameSeq != null && i < perFrameSeq.Items.Count) ? perFrameSeq.Items[i] : null;
            if (frameItem != null || sharedItem != null)
            {
                ExtractGeometryFromFunctionalGroup(frameItem, sharedItem, frameDs);
            }

            var frameBuffer = pixelData.GetFrame(i);
            
            frameDs.Remove(DicomTag.PixelData);
            frameDs.Remove(DicomTag.FloatPixelData);
            frameDs.Remove(DicomTag.DoubleFloatPixelData);

            var outPixelData = DicomPixelData.Create(frameDs, true);
            outPixelData.AddFrame(frameBuffer);

            var newFile = new DicomFile(frameDs);
            
            var meta = newFile.FileMetaInfo;
            DicomScanner.SafeSetUid(meta, DicomTag.MediaStorageSOPClassUID, newSopClassUid);
            DicomScanner.SafeSetUid(meta, DicomTag.MediaStorageSOPInstanceUID, newSopInstance);
            
            if (anonymize)
            {
                var anon = anonymizer ?? new SeriesDeidentifier();
                anon.Anonymize(newFile.Dataset);
                anon.Anonymize(newFile.FileMetaInfo);
            }

            frameDs.AddOrUpdate(DicomTag.SpecificCharacterSet, "ISO_IR 192");

            string targetFolder = outputDirectory;
            if (splitByBValue && frameB.HasValue)
            {
                targetFolder = Path.Combine(outputDirectory, $"b{frameB.Value}");
                Directory.CreateDirectory(targetFolder);
            }

            string fileName = $"Frame_{i + 1:D4}.dcm";
            string outputPath = Path.Combine(targetFolder, fileName);

            ct.ThrowIfCancellationRequested();
            newFile.Save(outputPath);
        }

        _log($"    {frameCount} Frames erfolgreich aufgeteilt nach: {Path.GetFileName(outputDirectory)}");
    }

    private void ExtractGeometryFromFunctionalGroup(DicomDataset? item, DicomDataset? sharedItem, DicomDataset targetDs)
    {
        if (sharedItem != null)
        {
            PromoteFunctionalGroupItems(sharedItem, targetDs);
        }

        if (item != null)
        {
            PromoteFunctionalGroupItems(item, targetDs);
        }
    }

    private void PromoteFunctionalGroupItems(DicomDataset sourceDs, DicomDataset targetDs)
    {
        foreach (var mainItem in sourceDs)
        {
            if (mainItem is DicomSequence seq)
            {
                if (seq.Items.Count > 0)
                {
                    var subItem = seq.Items[0];
                    foreach (var subSubItem in subItem)
                    {
                        targetDs.AddOrUpdate(subSubItem);
                    }
                }
            }
        }
    }

    private static string ChooseSingleFrameSopClass(string modality) =>
        modality.ToUpperInvariant() switch
        {
            "CT" => Sop.CTImageStorage,
            "MR" => Sop.MRImageStorage,
            "PT" => Sop.PETImageStorage,
            "US" => Sop.UltrasoundImageStorage,
            "XA" => Sop.XRayAngiographicImageStorage,
            "NM" => Sop.NuclearMedicineImageStorage,
            _ => Sop.SecondaryCaptureImageStorage
        };
}
