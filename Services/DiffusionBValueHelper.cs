using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FellowOakDicom;
using NewDicomMerger.Models;

namespace NewDicomMerger.Services;

/// <summary>
/// Helper class for extracting DICOM B-values across various vendor formats (Siemens, GE, Philips, Canon/Toshiba, NEMA)
/// and grouping/splitting series or multi-frame DICOM files for Brainlab DTI/DKI compatibility.
/// </summary>
public static class DiffusionBValueHelper
{
    // Private DICOM tags for B-Value
    private static readonly DicomTag SiemensBValueTag = new(0x0019, 0x100c);
    private static readonly DicomTag GeBValueTag = new(0x0043, 0x1039);
    private static readonly DicomTag GePrivateBTag = new(0x0019, 0x10b4);
    private static readonly DicomTag PhilipsBValueTag1 = new(0x2001, 0x1003);
    private static readonly DicomTag PhilipsBValueTag2 = new(0x2005, 0x10b1);
    private static readonly DicomTag PhilipsBValueTag3 = new(0x2005, 0x1409);

    /// <summary>
    /// Extracts the diffusion B-value from a DICOM dataset using standard and vendor-specific shadow tags.
    /// </summary>
    public static int? ExtractBValue(DicomDataset ds)
    {
        if (ds == null) return null;

        // 1. Standard DICOM Tag (0018,9087)
        if (ds.Contains(DicomTag.DiffusionBValue))
        {
            try
            {
                double val = ds.GetSingleValue<double>(DicomTag.DiffusionBValue);
                return NormalizeBValue((int)Math.Round(val));
            }
            catch
            {
                try
                {
                    string sVal = ds.GetSingleValueOrDefault(DicomTag.DiffusionBValue, "");
                    if (double.TryParse(sVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal))
                        return NormalizeBValue((int)Math.Round(dVal));
                }
                catch { }
            }
        }

        // 2. MR Diffusion Sequence (0018,9117)
        if (ds.Contains(DicomTag.MRDiffusionSequence))
        {
            try
            {
                var seq = ds.GetSequence(DicomTag.MRDiffusionSequence);
                if (seq != null && seq.Items.Count > 0 && seq.Items[0].Contains(DicomTag.DiffusionBValue))
                {
                    double val = seq.Items[0].GetSingleValue<double>(DicomTag.DiffusionBValue);
                    return NormalizeBValue((int)Math.Round(val));
                }
            }
            catch { }
        }

        // 3. Shared Functional Groups Sequence
        if (ds.Contains(DicomTag.SharedFunctionalGroupsSequence))
        {
            try
            {
                var sharedSeq = ds.GetSequence(DicomTag.SharedFunctionalGroupsSequence);
                if (sharedSeq != null && sharedSeq.Items.Count > 0)
                {
                    var item = sharedSeq.Items[0];
                    if (item.Contains(DicomTag.MRDiffusionSequence))
                    {
                        var mrDiff = item.GetSequence(DicomTag.MRDiffusionSequence);
                        if (mrDiff != null && mrDiff.Items.Count > 0 && mrDiff.Items[0].Contains(DicomTag.DiffusionBValue))
                        {
                            double val = mrDiff.Items[0].GetSingleValue<double>(DicomTag.DiffusionBValue);
                            return NormalizeBValue((int)Math.Round(val));
                        }
                    }
                }
            }
            catch { }
        }

        // 4. Siemens Shadow Tag (0019, 100c)
        if (ds.Contains(SiemensBValueTag))
        {
            try
            {
                var vals = ds.GetValues<double>(SiemensBValueTag);
                if (vals != null && vals.Length > 0)
                    return NormalizeBValue((int)Math.Round(vals[0]));
            }
            catch { }

            try
            {
                string sVal = ds.GetSingleValueOrDefault(SiemensBValueTag, "");
                if (!string.IsNullOrWhiteSpace(sVal) && double.TryParse(sVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal))
                    return NormalizeBValue((int)Math.Round(dVal));
            }
            catch { }
        }

        // 5. GE Shadow Tag (0043, 1039)
        if (ds.Contains(GeBValueTag))
        {
            try
            {
                var vals = ds.GetValues<long>(GeBValueTag);
                if (vals != null && vals.Length > 0)
                {
                    long val = vals[0];
                    if (val > 1000000) val %= 100000;
                    return NormalizeBValue((int)val);
                }
            }
            catch
            {
                try
                {
                    string sVal = ds.GetSingleValueOrDefault(GeBValueTag, "");
                    if (double.TryParse(sVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal))
                        return NormalizeBValue((int)Math.Round(dVal % 100000));
                }
                catch { }
            }
        }

        // GE Private (0019, 10b4)
        if (ds.Contains(GePrivateBTag))
        {
            try
            {
                double val = ds.GetSingleValue<double>(GePrivateBTag);
                return NormalizeBValue((int)Math.Round(val));
            }
            catch { }
        }

        // 6. Philips Shadow Tags
        foreach (var pTag in new[] { PhilipsBValueTag1, PhilipsBValueTag2, PhilipsBValueTag3 })
        {
            if (ds.Contains(pTag))
            {
                try
                {
                    double val = ds.GetSingleValue<double>(pTag);
                    return NormalizeBValue((int)Math.Round(val));
                }
                catch
                {
                    try
                    {
                        string sVal = ds.GetSingleValueOrDefault(pTag, "");
                        if (double.TryParse(sVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal))
                            return NormalizeBValue((int)Math.Round(dVal));
                    }
                    catch { }
                }
            }
        }

        // 7. Fallback text parsing in SeriesDescription, ProtocolName, SequenceName
        string seriesDesc = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, "");
        string protocol = ds.GetSingleValueOrDefault(DicomTag.ProtocolName, "");
        string seqName = ds.GetSingleValueOrDefault(DicomTag.SequenceName, "");
        string combinedText = $"{seriesDesc} {protocol} {seqName}";

        var match = Regex.Match(combinedText, @"\bb[_-]?(\d{1,5})\b", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int parsedB))
        {
            return NormalizeBValue(parsedB);
        }

        return null;
    }

    /// <summary>
    /// Extracts B-value for frame index i in a multi-frame DICOM dataset.
    /// </summary>
    public static int? ExtractFrameBValue(DicomDataset masterDs, int frameIndex)
    {
        if (masterDs == null) return null;

        if (masterDs.Contains(DicomTag.PerFrameFunctionalGroupsSequence))
        {
            try
            {
                var perFrame = masterDs.GetSequence(DicomTag.PerFrameFunctionalGroupsSequence);
                if (perFrame != null && frameIndex >= 0 && frameIndex < perFrame.Items.Count)
                {
                    var frameItem = perFrame.Items[frameIndex];
                    int? frameB = ExtractBValue(frameItem);
                    if (frameB.HasValue) return frameB;
                }
            }
            catch { }
        }

        return ExtractBValue(masterDs);
    }

    /// <summary>
    /// Normalizes raw B-value numbers (e.g. b=998 -> 1000, b=4 -> 0) to avoid splitting identical clinical B-values.
    /// </summary>
    public static int NormalizeBValue(int rawB)
    {
        if (rawB < 0) return 0;
        if (rawB <= 10) return 0; // b0 threshold

        if (rawB < 500)
        {
            int rem = rawB % 50;
            if (rem < 15) return rawB - rem;
            if (rem > 35) return rawB + (50 - rem);
        }
        else
        {
            int rem = rawB % 100;
            if (rem < 25) return rawB - rem;
            if (rem > 75) return rawB + (100 - rem);
        }

        return rawB;
    }

    /// <summary>
    /// Determines whether a DICOM series/dataset represents a Diffusion, DTI, or DKI sequence.
    /// </summary>
    public static bool IsDiffusionOrDtiOrDki(SeriesGroup group)
    {
        if (group == null || group.Files.Count == 0) return false;
        return IsDiffusionOrDtiOrDki(group.Files[0].Dataset);
    }

    public static bool IsDiffusionOrDtiOrDki(DicomDataset ds)
    {
        if (ds == null) return false;

        string modality = ds.GetSingleValueOrDefault(DicomTag.Modality, "");
        if (!modality.Equals("MR", StringComparison.OrdinalIgnoreCase)) return false;

        if (ExtractBValue(ds).HasValue) return true;

        string desc = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, "").ToLowerInvariant();
        string proto = ds.GetSingleValueOrDefault(DicomTag.ProtocolName, "").ToLowerInvariant();
        string seq = ds.GetSingleValueOrDefault(DicomTag.SequenceName, "").ToLowerInvariant();
        string scanningSeq = ds.Contains(DicomTag.ScanningSequence) ? (ds.GetString(DicomTag.ScanningSequence) ?? "").ToLowerInvariant() : "";

        string[] keywords = ["diff", "dti", "dki", "ep_b", "resolve", "trace", "fa", "adc", "tensor", "kurtosis", "dwi"];
        foreach (var kw in keywords)
        {
            if (desc.Contains(kw) || proto.Contains(kw) || seq.Contains(kw) || scanningSeq.Contains(kw))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Splits a list of SeriesGroup into distinct B-value groups if they represent Diffusion/DTI/DKI sequences with multiple B-values.
    /// </summary>
    public static List<SeriesGroup> SplitGroupsByBValue(List<SeriesGroup> groups)
    {
        var result = new List<SeriesGroup>();

        foreach (var group in groups)
        {
            if (group.Files.Count == 0 || !IsDiffusionOrDtiOrDki(group))
            {
                result.Add(group);
                continue;
            }

            // Single file multi-frame series check
            if (group.Files.Count == 1 && group.Files[0].IsMultiFrame)
            {
                // Multi-frame DICOM files containing multiple B-values will be preserved as a group for FrameMerger/Splitter
                result.Add(group);
                continue;
            }

            // Map files by B-value
            var bMap = new Dictionary<int, List<LoadedDicom>>();
            var unknownBFiles = new List<LoadedDicom>();

            foreach (var file in group.Files)
            {
                int? bVal = ExtractBValue(file.Dataset);
                if (bVal.HasValue)
                {
                    if (!bMap.TryGetValue(bVal.Value, out var list))
                    {
                        list = new List<LoadedDicom>();
                        bMap[bVal.Value] = list;
                    }
                    list.Add(file);
                }
                else
                {
                    unknownBFiles.Add(file);
                }
            }

            // If there's only 0 or 1 distinct B-value, keep original group
            if (bMap.Count <= 1)
            {
                result.Add(group);
                continue;
            }

            // Multiple B-values found -> create separate SeriesGroup for each B-value
            var sortedBValues = bMap.Keys.OrderBy(b => b).ToList();
            int subIndex = 0;

            foreach (var bVal in sortedBValues)
            {
                subIndex++;
                var subFiles = bMap[bVal];

                // Append any unknown B files to the b0 group if present
                if (bVal == 0 && unknownBFiles.Count > 0)
                {
                    subFiles.AddRange(unknownBFiles);
                    unknownBFiles.Clear();
                }

                string newSeriesUid = GenerateDerivedUid(group.SeriesInstanceUid, bVal);
                string bSuffix = $"_b{bVal}";

                foreach (var f in subFiles)
                {
                    string origDesc = f.Dataset.GetSingleValueOrDefault(DicomTag.SeriesDescription, "");
                    if (!origDesc.EndsWith(bSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        string newDesc = string.IsNullOrWhiteSpace(origDesc) ? $"Series{bSuffix}" : $"{origDesc}{bSuffix}";
                        f.Dataset.AddOrUpdate(DicomTag.SeriesDescription, newDesc);
                    }
                }

                result.Add(new SeriesGroup
                {
                    StudyInstanceUid = group.StudyInstanceUid,
                    SeriesInstanceUid = newSeriesUid,
                    Modality = group.Modality,
                    Files = subFiles,
                    ExcludedFileCount = 0
                });
            }

            // If any unknown B files remain, put them in a separate group
            if (unknownBFiles.Count > 0)
            {
                result.Add(new SeriesGroup
                {
                    StudyInstanceUid = group.StudyInstanceUid,
                    SeriesInstanceUid = GenerateDerivedUid(group.SeriesInstanceUid, 99999),
                    Modality = group.Modality,
                    Files = unknownBFiles,
                    ExcludedFileCount = 0
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Generates a valid DICOM SeriesInstanceUID for a split B-value series.
    /// </summary>
    public static string GenerateDerivedUid(string baseUid, int bValue)
    {
        string suffix = $".{bValue}";
        if (!string.IsNullOrEmpty(baseUid) && baseUid.Length + suffix.Length <= 64 && char.IsDigit(baseUid[^1]))
        {
            return baseUid + suffix;
        }

        return DicomUID.Generate().UID;
    }

    /// <summary>
    /// Applies strict Brainlab Fibertracking v2.0 formatting and validation checks to a DICOM dataset.
    /// </summary>
    public static void ApplyBrainlabDtiFormatting(DicomDataset ds, int? bValue = null, Action<string>? warn = null, Action<string>? log = null)
    {
        if (ds == null) return;

        // 1. Patient Position: "Ausschließlich Rückenlage und Kopf voran" (Head First Supine -> HFS)
        ds.AddOrUpdate(DicomTag.PatientPosition, "HFS");

        // 2. Storage: "Ausschließlich im 16-Bit-DICOM-Format"
        ushort bitsAlloc = ds.GetSingleValueOrDefault<ushort>(DicomTag.BitsAllocated, 16);
        if (bitsAlloc != 16)
        {
            ds.AddOrUpdate(DicomTag.BitsAllocated, (ushort)16);
            ds.AddOrUpdate(DicomTag.BitsStored, (ushort)16);
            ds.AddOrUpdate(DicomTag.HighBit, (ushort)15);
            log?.Invoke("    [Brainlab DTI] Speicherformat auf 16-Bit DICOM angepasst.");
        }

        // 3. Diffusion B-Value Tag (0018,9087)
        int? effectiveB = bValue ?? ExtractBValue(ds);
        if (effectiveB.HasValue)
        {
            ds.AddOrUpdate(DicomTag.DiffusionBValue, (double)effectiveB.Value);

            // B-Value Suffix on SeriesDescription
            string origDesc = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, "DTI");
            string bSuffix = $"_b{effectiveB.Value}";
            if (!origDesc.EndsWith(bSuffix, StringComparison.OrdinalIgnoreCase))
            {
                string cleanDesc = Regex.Replace(origDesc, @"_b\d+$", "", RegexOptions.IgnoreCase);
                ds.AddOrUpdate(DicomTag.SeriesDescription, $"{cleanDesc}{bSuffix}");
            }
        }

        // 4. Matrix & Geometry Validation against Brainlab Fibertracking v2.0 Recommendations
        int rows = ds.GetSingleValueOrDefault(DicomTag.Rows, 0);
        int cols = ds.GetSingleValueOrDefault(DicomTag.Columns, 0);
        if (rows > 0 && cols > 0 && rows != cols)
        {
            warn?.Invoke($"[Brainlab DTI Empfehlung] Matrixgröße ist nicht quadratisch: {cols}×{rows} (Gefordert: Quadratische Matrix).");
        }

        if (ds.Contains(DicomTag.PixelSpacing))
        {
            try
            {
                var spacing = ds.GetValues<double>(DicomTag.PixelSpacing);
                if (spacing != null && spacing.Length >= 2 && Math.Abs(spacing[0] - spacing[1]) > 0.0001)
                {
                    warn?.Invoke($"[Brainlab DTI Empfehlung] PixelSpacing ist nicht quadratisch: {spacing[0]:F3}×{spacing[1]:F3} mm.");
                }
            }
            catch { }
        }

        double thickness = ds.GetSingleValueOrDefault(DicomTag.SliceThickness, 0.0);
        if (thickness > 3.05)
        {
            warn?.Invoke($"[Brainlab DTI Empfehlung] Schichtdicke ist {thickness:F1} mm (Empfohlen: ≤ 3.0 mm).");
        }
    }
}
