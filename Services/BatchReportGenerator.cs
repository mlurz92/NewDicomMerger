using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewDicomMerger.Services;

public sealed class BrainlabAuditReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("converterVersion")]
    public string ConverterVersion { get; set; } = "1.0.0";

    [JsonPropertyName("sourceStudyInstanceUID")]
    public string SourceStudyInstanceUID { get; set; } = string.Empty;

    [JsonPropertyName("sourceSeriesInstanceUIDs")]
    public List<string> SourceSeriesInstanceUIDs { get; set; } = new();

    [JsonPropertyName("outputSeriesInstanceUID")]
    public string OutputSeriesInstanceUID { get; set; } = string.Empty;

    [JsonPropertyName("outputSOPInstanceUIDs")]
    public List<string> OutputSOPInstanceUIDs { get; set; } = new();

    [JsonPropertyName("targetProfile")]
    public string TargetProfile { get; set; } = "Brainlab Elements Fibertracking 2.0";

    [JsonPropertyName("nominalShellBValue")]
    public int NominalShellBValue { get; set; }

    [JsonPropertyName("actualBValueRange")]
    public double[] ActualBValueRange { get; set; } = Array.Empty<double>();

    [JsonPropertyName("b0VolumeCount")]
    public int B0VolumeCount { get; set; }

    [JsonPropertyName("directionCount")]
    public int DirectionCount { get; set; }

    [JsonPropertyName("repetitionCountPerDirection")]
    public int RepetitionCountPerDirection { get; set; } = 1;

    [JsonPropertyName("volumeCount")]
    public int VolumeCount { get; set; }

    [JsonPropertyName("sliceCountPerVolume")]
    public int SliceCountPerVolume { get; set; }

    [JsonPropertyName("frameCount")]
    public int FrameCount { get; set; }

    [JsonPropertyName("matrix")]
    public int[] Matrix { get; set; } = Array.Empty<int>();

    [JsonPropertyName("pixelSpacingMm")]
    public double[] PixelSpacingMm { get; set; } = Array.Empty<double>();

    [JsonPropertyName("sliceThicknessMm")]
    public double SliceThicknessMm { get; set; }

    [JsonPropertyName("sliceSpacingMm")]
    public double SliceSpacingMm { get; set; }

    [JsonPropertyName("patientPosition")]
    public string PatientPosition { get; set; } = "HFS";

    [JsonPropertyName("orientationClass")]
    public string OrientationClass { get; set; } = "AXIAL_OBLIQUE";

    [JsonPropertyName("obliquityDegrees")]
    public double ObliquityDegrees { get; set; }

    [JsonPropertyName("gradientCoordinateSystem")]
    public string GradientCoordinateSystem { get; set; } = "DICOM_LPS_PATIENT";

    [JsonPropertyName("diffusionMetadataSource")]
    public string DiffusionMetadataSource { get; set; } = "DICOM_PUBLIC_OR_PRIVATE_TAGS";

    [JsonPropertyName("pixelDataModified")]
    public bool PixelDataModified { get; set; } = false;

    [JsonPropertyName("dicomValidatorPassed")]
    public bool DicomValidatorPassed { get; set; } = true;

    [JsonPropertyName("brainlabImportTested")]
    public bool BrainlabImportTested { get; set; } = false;

    [JsonPropertyName("blockers")]
    public List<string> Blockers { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("fileHashes")]
    public Dictionary<string, string> FileHashes { get; set; } = new();

    [JsonPropertyName("outputFileHashes")]
    public Dictionary<string, string> OutputFileHashes { get; set; } = new();
}

public static class BatchReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    public static string ComputeSha256(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;
        try
        {
            using var stream = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    public static double CalculateObliquityDegrees(double[] iop)
    {
        if (iop == null || iop.Length < 6) return 0.0;

        double rX = iop[0], rY = iop[1], rZ = iop[2];
        double cX = iop[3], cY = iop[4], cZ = iop[5];

        double nX = rY * cZ - rZ * cY;
        double nY = rZ * cX - rX * cZ;
        double nZ = rX * cY - rY * cX;

        double norm = Math.Sqrt(nX * nX + nY * nY + nZ * nZ);
        if (norm < 1e-6) return 0.0;

        nZ = Math.Abs(nZ / norm);
        if (nZ > 1.0) nZ = 1.0;

        double rad = Math.Acos(nZ);
        return Math.Round(rad * (180.0 / Math.PI), 2);
    }

    public static void SaveReport(BrainlabAuditReport report, string outputPath)
    {
        try
        {
            string dir = Path.GetDirectoryName(outputPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string jsonPath = Path.ChangeExtension(outputPath, ".brainlab.json");
            string json = JsonSerializer.Serialize(report, JsonOptions);
            File.WriteAllText(jsonPath, json);
        }
        catch { }
    }
}
