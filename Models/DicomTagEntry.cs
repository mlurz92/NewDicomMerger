namespace NewDicomMerger.Models;

/// <summary>
/// Represents a single DICOM tag entry for display in the Tag Viewer.
/// </summary>
public sealed class DicomTagEntry
{
    public string TagHex { get; init; } = string.Empty;
    public string TagName { get; init; } = string.Empty;
    public string VR { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
