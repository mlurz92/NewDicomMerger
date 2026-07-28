using System.IO;
using System.Text;
using System.Text.Json;
using NewDicomMerger.Models;

namespace NewDicomMerger.Services;

/// <summary>
/// Writes a per-run batch summary (one row per processed series) as CSV and JSON,
/// so the outcome of a large batch doesn't only live in the scrollback of the
/// in-app log.
/// </summary>
public static class BatchReportWriter
{
    public static async Task WriteAsync(string outputDir, IReadOnlyList<BatchReportEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0 || string.IsNullOrWhiteSpace(outputDir)) return;
        Directory.CreateDirectory(outputDir);

        string baseName = $"Batch-Report_{DateTime.Now:yyyyMMdd-HHmmss}";

        var csv = new StringBuilder();
        csv.AppendLine("PatientName;SeriesName;Modality;FrameCount;Success;OutputPath;ErrorMessage");
        foreach (var e in entries)
        {
            csv.AppendLine(string.Join(';',
                CsvEscape(e.PatientName),
                CsvEscape(e.SeriesName),
                CsvEscape(e.Modality),
                e.FrameCount.ToString(),
                e.Success.ToString(),
                CsvEscape(e.OutputPath),
                CsvEscape(e.ErrorMessage ?? "")));
        }
        await File.WriteAllTextAsync(Path.Combine(outputDir, baseName + ".csv"), csv.ToString(), Encoding.UTF8, ct);

        string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(outputDir, baseName + ".json"), json, Encoding.UTF8, ct);
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.IndexOfAny([';', '"', '\n', '\r']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
