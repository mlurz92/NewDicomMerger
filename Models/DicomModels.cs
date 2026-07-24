using FellowOakDicom;

namespace NewDicomMerger.Models;

/// <summary>
/// Represents a successfully loaded, validated, and repaired single-frame DICOM file.
/// </summary>
public sealed class LoadedDicom
{
    public required string FilePath { get; init; }
    public required DicomFile File { get; init; }
    public DicomDataset Dataset => File.Dataset;

    // UIDs
    public required string SeriesInstanceUid { get; init; }
    public required string StudyInstanceUid { get; init; }
    public required string SopInstanceUid { get; init; }
    public required string SopClassUid { get; init; }

    // Instance
    public required int InstanceNumber { get; init; }

    // Pixel geometry
    public required int Rows { get; init; }
    public required int Columns { get; init; }
    public required int BitsAllocated { get; init; }
    public required int BitsStored { get; init; }
    public required int HighBit { get; init; }
    public required int PixelRepresentation { get; init; }
    public required int SamplesPerPixel { get; init; }

    // Codec
    public required string TransferSyntaxUid { get; init; }

    // Metadata
    public required string Modality { get; init; }
    public required string PhotometricInterpretation { get; init; }
    public required bool IsMultiFrame { get; init; }
    
    /// <summary>
    /// Tatsächliche Anzahl der Frames in dieser spezifischen Datei.
    /// (1 bei Single-Frame, >1 bei Multi-Frame).
    /// </summary>
    public required int NumberOfFrames { get; init; }
}

/// <summary>
/// A group of LoadedDicom files that share the same Study + Series UIDs and consistent pixel properties.
/// </summary>
public sealed class SeriesGroup
{
    public required string StudyInstanceUid { get; init; }
    public required string SeriesInstanceUid { get; init; }
    public required string Modality { get; init; }
    public required List<LoadedDicom> Files { get; init; }

    /// <summary>
    /// Number of files originally found for this Study+Series that were excluded
    /// during grouping due to inconsistent pixel properties (Rows/Columns/BitsAllocated/
    /// BitsStored/PixelRepresentation/SamplesPerPixel/PhotometricInterpretation/
    /// TransferSyntax — see DicomScanner.GroupAndSort). Surfaced in the review grid's
    /// status column so the user can see at a glance which series are incomplete.
    /// </summary>
    public int ExcludedFileCount { get; init; }

    /// <summary>
    /// Berechnet die korrekte, absolute Summe aller in dieser Gruppe enthaltenen Frames
    /// über alle Dateien (sowohl Single-Frame als auch Multi-Frame) hinweg.
    /// </summary>
    public int TotalFrames => Files.Sum(f => f.NumberOfFrames);
}

/// <summary>
/// Aggregated result of the merge operation.
/// </summary>
public sealed class MergeResult
{
    public int DiscoveredFiles { get; set; }
    public int LoadedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public int GroupsFound { get; set; }
    public int CreatedFiles { get; set; }
    public int Errors { get; set; }
    public List<string> OutputFiles { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> ErrorMessages { get; } = [];
}

public class ProcessingJob
{
    public required string InputDirectory { get; init; }
    public required string OutputDirectory { get; init; }
}

/// <summary>
/// Aggregated result of the split operation.
/// </summary>
public sealed class SplitResult
{
    public int DiscoveredFiles { get; set; }
    public int MultiFrameFiles { get; set; }
    public int SkippedFiles { get; set; }
    public int CreatedSingleFrames => OutputFiles.Count;
    public int Errors { get; set; }
    public List<string> OutputFiles { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> ErrorMessages { get; } = [];
}

/// <summary>
/// One row of a batch run's CSV/JSON report (see Services/BatchReportWriter.cs).
/// </summary>
public sealed class BatchReportEntry
{
    public required string PatientName { get; init; }
    public required string SeriesName { get; init; }
    public required string Modality { get; init; }
    public required int FrameCount { get; init; }
    public required bool Success { get; init; }
    public string OutputPath { get; init; } = "";
    public string? ErrorMessage { get; init; }
}
