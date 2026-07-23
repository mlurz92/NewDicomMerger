using System.IO;
using System.Linq;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using NewDicomMerger.Models;

namespace NewDicomMerger.Services;

/// <summary>
/// Splits a multi-frame DICOM file into multiple single-frame DICOM files.
/// </summary>
public sealed class FrameSplitter
{
    private readonly Action<string> _log;
    private readonly Action<string>? _warn;

    public FrameSplitter(Action<string> log, Action<string>? warn = null)
    {
        _log = log;
        _warn = warn;
    }

    // SOP Class Single-Frame mappings
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

    public void Split(LoadedDicom file, string outputDirectory, string patientName, string seriesDescription, bool anonymize = false, CancellationToken ct = default, SeriesDeidentifier? anonymizer = null)
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

        if (frameCount <= 1)
        {
            _warn?.Invoke($"Die Datei {Path.GetFileName(file.FilePath)} hat nur {frameCount} Frames und wird übersprungen.");
            return;
        }

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // Determine new single-frame SOP Class UID
        string newSopClassUid = ChooseSingleFrameSopClass(file.Modality);

        // Pre-fetch the per-frame sequence if it exists, to extract position data
        var perFrameSeq = masterDs.Contains(DicomTag.PerFrameFunctionalGroupsSequence) 
            ? masterDs.GetSequence(DicomTag.PerFrameFunctionalGroupsSequence) 
            : null;

        var sharedSeq = masterDs.Contains(DicomTag.SharedFunctionalGroupsSequence)
            ? masterDs.GetSequence(DicomTag.SharedFunctionalGroupsSequence)
            : null;
        var sharedItem = (sharedSeq != null && sharedSeq.Items.Count > 0) ? sharedSeq.Items[0] : null;

        for (int i = 0; i < frameCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            // 1. Create a clone of the source dataset to modify
            var frameDs = masterDs.Clone();

            // 2. Remove multi-frame specific tags
            frameDs.Remove(DicomTag.NumberOfFrames);
            frameDs.Remove(DicomTag.PerFrameFunctionalGroupsSequence);
            frameDs.Remove(DicomTag.SharedFunctionalGroupsSequence);
            frameDs.Remove(DicomTag.DimensionOrganizationSequence);
            frameDs.Remove(DicomTag.DimensionIndexSequence);

            // 3. Update SOP Class UID
            DicomScanner.SafeSetUid(frameDs, DicomTag.SOPClassUID, newSopClassUid);
            string newSopInstance = DicomUID.Generate().UID;
            DicomScanner.SafeSetUid(frameDs, DicomTag.SOPInstanceUID, newSopInstance);

            // 4. Update Instance Number
            frameDs.AddOrUpdate(DicomTag.InstanceNumber, i + 1);

            // 5. Try to extract specific frame geometry from PerFrame / Shared Functional Groups
            var frameItem = (perFrameSeq != null && i < perFrameSeq.Items.Count) ? perFrameSeq.Items[i] : null;
            if (frameItem != null || sharedItem != null)
            {
                ExtractGeometryFromFunctionalGroup(frameItem, sharedItem, frameDs);
            }

            // 6. Extract the single frame pixel data
            var frameBuffer = pixelData.GetFrame(i);
            
            // Remove old pixel data, we will recreate it
            frameDs.Remove(DicomTag.PixelData);
            frameDs.Remove(DicomTag.FloatPixelData);
            frameDs.Remove(DicomTag.DoubleFloatPixelData);

            var outPixelData = DicomPixelData.Create(frameDs, true);
            outPixelData.AddFrame(frameBuffer);

            // 7. Save file
            var newFile = new DicomFile(frameDs);
            
            // Update MetaInfo
            var meta = newFile.FileMetaInfo;
            DicomScanner.SafeSetUid(meta, DicomTag.MediaStorageSOPClassUID, newSopClassUid);
            DicomScanner.SafeSetUid(meta, DicomTag.MediaStorageSOPInstanceUID, newSopInstance);
            
            // Anonymize if requested
            if (anonymize)
            {
                var anon = anonymizer ?? new SeriesDeidentifier();
                anon.Anonymize(newFile.Dataset);
                anon.Anonymize(newFile.FileMetaInfo);
            }

            // Bugfix: declare the charset explicitly (see FrameMerger.Merge for the full
            // rationale) so umlauts in patient/study text round-trip correctly instead of
            // silently defaulting to ASCII on the next read.
            frameDs.AddOrUpdate(DicomTag.SpecificCharacterSet, "ISO_IR 100");

            // Generate filename: prefix zeros to sort properly e.g. Frame_0001.dcm
            string fileName = $"Frame_{i + 1:D4}.dcm";
            string outputPath = Path.Combine(outputDirectory, fileName);

            ct.ThrowIfCancellationRequested();
            newFile.Save(outputPath);
        }

        _log($"    {frameCount} Frames erfolgreich aufgeteilt nach: {Path.GetFileName(outputDirectory)}");
    }

    private void ExtractGeometryFromFunctionalGroup(DicomDataset? item, DicomDataset? sharedItem, DicomDataset targetDs)
    {
        // 1. Promote from Shared Functional Groups first
        if (sharedItem != null)
        {
            PromoteFunctionalGroupItems(sharedItem, targetDs);
        }

        // 2. Promote from Per-Frame Functional Groups (overrides Shared values)
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
