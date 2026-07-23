using System.IO;
using FellowOakDicom;
using FellowOakDicom.Media;

namespace NewDicomMerger.Services;

/// <summary>
/// Builds a DICOMDIR index file for a folder of DICOM files, so the folder can be
/// burned to a CD/USB stick as a standards-conformant media storage file-set that
/// PACS/viewer software can browse without re-scanning every file.
/// </summary>
public static class DicomDirWriter
{
    /// <summary>
    /// Scans <paramref name="rootDirectory"/> recursively for .dcm files and writes a
    /// DICOMDIR into its root. Returns the number of files referenced, or 0 if none
    /// were found/readable (in which case no DICOMDIR is written).
    /// </summary>
    public static async Task<int> BuildAsync(string rootDirectory, CancellationToken ct = default)
    {
        var dcmFiles = Directory.EnumerateFiles(rootDirectory, "*.dcm", SearchOption.AllDirectories).ToList();
        if (dcmFiles.Count == 0) return 0;

        var dicomDir = new DicomDirectory(true) { FileSetID = "NEWDICOMMERGER" };
        int added = 0;

        foreach (var path in dcmFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var file = await DicomFile.OpenAsync(path, DicomScanner.LegacyFallbackEncoding, readOption: FileReadOption.SkipLargeTags);
                string relative = Path.GetRelativePath(rootDirectory, path).Replace(Path.DirectorySeparatorChar, '\\');
                dicomDir.AddFile(file, relative);
                added++;
            }
            catch
            {
                // Skip files that aren't valid/readable DICOM — DICOMDIR generation
                // is best-effort and must not abort the whole export for one bad file.
            }
        }

        if (added == 0) return 0;

        string dicomDirPath = Path.Combine(rootDirectory, "DICOMDIR");
        await dicomDir.SaveAsync(dicomDirPath);
        return added;
    }
}
