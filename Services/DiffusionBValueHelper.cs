using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FellowOakDicom;
using NewDicomMerger.Models;

namespace NewDicomMerger.Services;

public static class DiffusionBValueHelper
{
    public static readonly DicomTag SiemensBValueTag = new DicomTag(0x0019, 0x100c, "SIEMENS MR HEADER");
    public static readonly DicomTag SiemensDirectionalityTag = new DicomTag(0x0019, 0x100d, "SIEMENS MR HEADER");
    public static readonly DicomTag SiemensGradientTag = new DicomTag(0x0019, 0x100e, "SIEMENS MR HEADER");
    public static readonly DicomTag SiemensBMatrixTag = new DicomTag(0x0019, 0x1027, "SIEMENS MR HEADER");

    private static readonly DicomTag GeBValueTag = new DicomTag(0x0043, 0x1039, "GEMS_PARM_01");
    private static readonly DicomTag GePrivateBTag = new DicomTag(0x0019, 0x10b4, "GEMS_ACQU_01");
    private static readonly DicomTag PhilipsBValueTag1 = new DicomTag(0x2001, 0x1003, "PHILIPS MR R5.5/PAR");
    private static readonly DicomTag PhilipsBValueTag2 = new DicomTag(0x2005, 0x10b1, "PHILIPS MR R5.6/PAR");
    private static readonly DicomTag PhilipsBValueTag3 = new DicomTag(0x2005, 0x1409, "PHILIPS MR R5.6/PAR");

    static DiffusionBValueHelper()
    {
        try
        {
            var dict = DicomDictionary.Default;
            RegisterPrivateTag(dict, new DicomTag(0x0019, 0x100c, "SIEMENS MR HEADER"), "Siemens B-Value", DicomVR.FD);
            RegisterPrivateTag(dict, new DicomTag(0x0019, 0x100d, "SIEMENS MR HEADER"), "Siemens Directionality", DicomVR.CS);
            RegisterPrivateTag(dict, new DicomTag(0x0019, 0x100e, "SIEMENS MR HEADER"), "Siemens Gradient", DicomVR.FD);
            RegisterPrivateTag(dict, new DicomTag(0x0019, 0x1027, "SIEMENS MR HEADER"), "Siemens B-Matrix", DicomVR.FD);
            RegisterPrivateTag(dict, new DicomTag(0x0043, 0x1039, "GEMS_PARM_01"), "GE B-Value", DicomVR.IS);
            RegisterPrivateTag(dict, new DicomTag(0x0019, 0x10b4, "GEMS_ACQU_01"), "GE Private B-Value", DicomVR.DS);
            RegisterPrivateTag(dict, new DicomTag(0x2001, 0x1003, "PHILIPS MR R5.5/PAR"), "Philips B-Value 1", DicomVR.FL);
            RegisterPrivateTag(dict, new DicomTag(0x2005, 0x10b1, "PHILIPS MR R5.6/PAR"), "Philips B-Value 2", DicomVR.FL);
            RegisterPrivateTag(dict, new DicomTag(0x2005, 0x1409, "PHILIPS MR R5.6/PAR"), "Philips B-Value 3", DicomVR.FL);
        }
        catch { }
    }

    private static void RegisterPrivateTag(DicomDictionary dict, DicomTag tag, string name, DicomVR vr)
    {
        try
        {
            var entry = new DicomDictionaryEntry(tag, name, name.Replace(" ", ""), DicomVM.VM_1_n, false, vr);
            dict.Add(entry);
        }
        catch { }
    }

    public static double[]? TryGetPrivateTagDoubleArray(DicomDataset ds, ushort group, ushort element)
    {
        if (ds == null) return null;
        try
        {
            var item = ds.GetDicomItem<DicomItem>(new DicomTag(group, element));
            if (item == null) return null;

            if (item is DicomElement elem)
            {
                if (elem is DicomFloatingPointDouble fd) return fd.Get<double[]>();
                if (elem is DicomFloatingPointSingle fl) return fl.Get<float[]>().Select(f => (double)f).ToArray();
                if (elem is DicomDecimalString dsElem) return dsElem.Get<double[]>();
                if (elem is DicomIntegerString isElem) return isElem.Get<long[]>().Select(l => (double)l).ToArray();
                if (elem is DicomUnsignedLong ulElem) return ulElem.Get<uint[]>().Select(u => (double)u).ToArray();
                if (elem is DicomSignedLong slElem) return slElem.Get<int[]>().Select(i => (double)i).ToArray();
                if (elem is DicomOtherByte obElem)
                {
                    var bytes = obElem.Buffer.Data;
                    if (bytes.Length >= 8)
                    {
                        var doubles = new double[bytes.Length / 8];
                        Buffer.BlockCopy(bytes, 0, doubles, 0, doubles.Length * 8);
                        return doubles;
                    }
                }

                string sVal = elem.Get<string>();
                if (!string.IsNullOrWhiteSpace(sVal))
                {
                    var parts = sVal.Split(new[] { '\\', '/', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var list = new List<double>();
                    foreach (var p in parts)
                    {
                        if (double.TryParse(p.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                            list.Add(d);
                    }
                    if (list.Count > 0) return list.ToArray();
                }
            }
        }
        catch { }
        return null;
    }

    public static string? TryGetPrivateTagString(DicomDataset ds, ushort group, ushort element)
    {
        if (ds == null) return null;
        try
        {
            var item = ds.GetDicomItem<DicomItem>(new DicomTag(group, element));
            if (item is DicomElement elem)
            {
                string sVal = elem.Get<string>();
                if (!string.IsNullOrWhiteSpace(sVal)) return sVal.Trim();
            }
        }
        catch { }
        return null;
    }

    public static int? ExtractBValue(DicomDataset ds)
    {
        if (ds == null) return null;

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

        var siemensB = TryGetPrivateTagDoubleArray(ds, 0x0019, 0x100c);
        if (siemensB != null && siemensB.Length > 0)
        {
            return NormalizeBValue((int)Math.Round(siemensB[0]));
        }

        var geB = TryGetPrivateTagDoubleArray(ds, 0x0043, 0x1039);
        if (geB != null && geB.Length > 0)
        {
            long val = (long)Math.Round(geB[0]);
            if (val > 1000000) val %= 100000;
            return NormalizeBValue((int)val);
        }

        var gePrivB = TryGetPrivateTagDoubleArray(ds, 0x0019, 0x10b4);
        if (gePrivB != null && gePrivB.Length > 0)
        {
            return NormalizeBValue((int)Math.Round(gePrivB[0]));
        }

        foreach (var pTag in new[] { (ushort)0x1003, (ushort)0x10b1, (ushort)0x1409 })
        {
            var pB = TryGetPrivateTagDoubleArray(ds, 0x2001, pTag) ?? TryGetPrivateTagDoubleArray(ds, 0x2005, pTag);
            if (pB != null && pB.Length > 0)
            {
                return NormalizeBValue((int)Math.Round(pB[0]));
            }
        }

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

    public static int NormalizeBValue(int rawB)
    {
        if (rawB < 0) return 0;
        if (rawB <= 10) return 0;

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

    public static double[] ReconstructBMatrix(double bValue, double[] gUnit)
    {
        if (gUnit == null || gUnit.Length < 3) return new double[6];
        double gx = gUnit[0], gy = gUnit[1], gz = gUnit[2];
        return new double[]
        {
            bValue * gx * gx,
            bValue * gx * gy,
            bValue * gx * gz,
            bValue * gy * gy,
            bValue * gy * gz,
            bValue * gz * gz
        };
    }

    public static bool ValidateBMatrix(double bValue, double[] g, double[] bMat6, out string error)
    {
        if (bMat6 == null || bMat6.Length < 6)
        {
            error = "B-matrix component array missing or incomplete.";
            return false;
        }

        double bxx = bMat6[0], bxy = bMat6[1], bxz = bMat6[2];
        double byy = bMat6[3], byz = bMat6[4], bzz = bMat6[5];

        double trace = bxx + byy + bzz;
        if (Math.Abs(trace - bValue) > Math.Max(30.0, 0.05 * bValue))
        {
            error = $"B-matrix trace ({trace:F1}) mismatches b-value ({bValue:F1}).";
            return false;
        }

        if (g != null && g.Length >= 3)
        {
            double gNorm = Math.Sqrt(g[0] * g[0] + g[1] * g[1] + g[2] * g[2]);
            if (gNorm > 1e-6)
            {
                double[] v = new double[] { g[0] / gNorm, g[1] / gNorm, g[2] / gNorm };
                for (int iter = 0; iter < 10; iter++)
                {
                    double x = bxx * v[0] + bxy * v[1] + bxz * v[2];
                    double y = bxy * v[0] + byy * v[1] + byz * v[2];
                    double z = bxz * v[0] + byz * v[1] + bzz * v[2];
                    double norm = Math.Sqrt(x * x + y * y + z * z);
                    if (norm > 1e-9) { v[0] = x / norm; v[1] = y / norm; v[2] = z / norm; }
                }

                double dot = Math.Abs((g[0] / gNorm) * v[0] + (g[1] / gNorm) * v[1] + (g[2] / gNorm) * v[2]);
                if (dot < 0.98)
                {
                    error = $"Principal eigenvector dot product with gradient ({dot:F4}) < 0.98.";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }

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

            if (group.Files.Count == 1 && group.Files[0].IsMultiFrame)
            {
                result.Add(group);
                continue;
            }

            var bMap = new Dictionary<int, List<LoadedDicom>>();
            var unknownBFiles = new List<LoadedDicom>();

            foreach (var file in group.Files)
            {
                int? bVal = ExtractBValue(file.Dataset);
                if (bVal.HasValue)
                {
                    int normalizedB = bVal.Value;
                    int matchedCluster = normalizedB;
                    foreach (var key in bMap.Keys)
                    {
                        if (key > 0 && Math.Abs(normalizedB - key) <= Math.Max(20, 0.03 * key))
                        {
                            matchedCluster = key;
                            break;
                        }
                    }

                    if (!bMap.TryGetValue(matchedCluster, out var list))
                    {
                        list = new List<LoadedDicom>();
                        bMap[matchedCluster] = list;
                    }
                    list.Add(file);
                }
                else
                {
                    unknownBFiles.Add(file);
                }
            }

            if (bMap.Count <= 1)
            {
                result.Add(group);
                continue;
            }

            var b0Files = bMap.TryGetValue(0, out var zeroList) ? zeroList : new List<LoadedDicom>();
            if (unknownBFiles.Count > 0)
            {
                b0Files.AddRange(unknownBFiles);
            }

            var nonZeroBValues = bMap.Keys.Where(b => b > 0).OrderBy(b => b).ToList();

            if (nonZeroBValues.Count <= 1)
            {
                result.Add(group);
                continue;
            }

            int shellIndex = 0;
            foreach (var bVal in nonZeroBValues)
            {
                shellIndex++;
                var shellFiles = bMap[bVal];

                var combinedFiles = new List<LoadedDicom>(b0Files.Count + shellFiles.Count);
                combinedFiles.AddRange(b0Files);
                combinedFiles.AddRange(shellFiles);

                string newSeriesUid = GenerateDerivedUid(group.SeriesInstanceUid, bVal);
                string seriesLabel = bVal == 1000 ? "BL_FT_B0_B1000" : $"BL_FT_B0_B{bVal}";

                foreach (var f in combinedFiles)
                {
                    string origDesc = f.Dataset.GetSingleValueOrDefault(DicomTag.SeriesDescription, "DTI");
                    if (!origDesc.Contains(seriesLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        string cleanDesc = Regex.Replace(origDesc, @"_b\d+$|_b0_b\d+$", "", RegexOptions.IgnoreCase);
                        string newDesc = string.IsNullOrWhiteSpace(cleanDesc) ? seriesLabel : $"{cleanDesc}_{seriesLabel}";
                        f.Dataset.AddOrUpdate(DicomTag.SeriesDescription, newDesc);
                    }
                }

                result.Add(new SeriesGroup
                {
                    StudyInstanceUid = group.StudyInstanceUid,
                    SeriesInstanceUid = newSeriesUid,
                    Modality = group.Modality,
                    Files = combinedFiles,
                    ExcludedFileCount = 0
                });
            }
        }

        return result;
    }

    public static string GenerateDerivedUid(string baseUid, int bValue)
    {
        string suffix = $".{bValue}";
        if (!string.IsNullOrEmpty(baseUid) && baseUid.Length + suffix.Length <= 64 && char.IsDigit(baseUid[^1]))
        {
            return baseUid + suffix;
        }

        return DicomUID.Generate().UID;
    }

    public static void SafeAddBValueTag(DicomDataset ds, DicomTag tag, double bValue)
    {
        if (ds == null || tag == null) return;
        try
        {
            ds.AddOrUpdate(tag, bValue);
        }
        catch
        {
            try
            {
                ds.AddOrUpdate(tag, (int)Math.Round(bValue));
            }
            catch
            {
                try
                {
                    ds.AddOrUpdate(tag, Math.Round(bValue).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { }
            }
        }
    }

    public static void PopulateMrDiffusionMacro(DicomDataset targetDs, DicomDataset srcDs, int? overrideBValue = null)
    {
        if (targetDs == null || srcDs == null) return;

        int bValue = overrideBValue ?? ExtractBValue(srcDs) ?? 0;
        string directionality = bValue <= 10 ? "NONE" : "BMATRIX";

        string? siemensDir = TryGetPrivateTagString(srcDs, 0x0019, 0x100d);
        if (bValue > 10 && !string.IsNullOrWhiteSpace(siemensDir))
        {
            directionality = siemensDir;
        }

        SafeAddBValueTag(targetDs, DicomTag.DiffusionBValue, bValue);
        SafeAddBValueTag(targetDs, SiemensBValueTag, bValue);
        targetDs.AddOrUpdate(SiemensDirectionalityTag, directionality);

        double[]? gradient = TryGetPrivateTagDoubleArray(srcDs, 0x0019, 0x100e);
        if (gradient == null && srcDs.Contains(DicomTag.DiffusionGradientOrientation))
        {
            try
            {
                var gVals = srcDs.GetValues<double>(DicomTag.DiffusionGradientOrientation);
                if (gVals is { Length: >= 3 }) gradient = gVals;
            }
            catch { }
        }

        if (gradient != null && gradient.Length >= 3)
        {
            double gNorm = Math.Sqrt(gradient[0] * gradient[0] + gradient[1] * gradient[1] + gradient[2] * gradient[2]);
            if (gNorm > 1e-6)
            {
                gradient[0] /= gNorm;
                gradient[1] /= gNorm;
                gradient[2] /= gNorm;
            }
            targetDs.AddOrUpdate(SiemensGradientTag, gradient[0], gradient[1], gradient[2]);
        }

        double[]? bMatrix = TryGetPrivateTagDoubleArray(srcDs, 0x0019, 0x1027);
        if (bMatrix == null && bValue > 10 && gradient != null && gradient.Length >= 3)
        {
            bMatrix = ReconstructBMatrix((double)bValue, gradient);
        }

        if (bMatrix != null && bMatrix.Length >= 6)
        {
            targetDs.AddOrUpdate(SiemensBMatrixTag, bMatrix[0], bMatrix[1], bMatrix[2], bMatrix[3], bMatrix[4], bMatrix[5]);
        }

        var mrDiffItem = new DicomDataset();
        SafeAddBValueTag(mrDiffItem, DicomTag.DiffusionBValue, bValue);
        mrDiffItem.AddOrUpdate(DicomTag.DiffusionDirectionality, directionality);

        if (bValue > 10)
        {
            if (gradient != null && gradient.Length >= 3)
            {
                var gradItem = new DicomDataset();
                gradItem.AddOrUpdate(DicomTag.DiffusionGradientOrientation, gradient[0], gradient[1], gradient[2]);
                mrDiffItem.Add(new DicomSequence(DicomTag.DiffusionGradientDirectionSequence, gradItem));
            }

            if (bMatrix != null && bMatrix.Length >= 6)
            {
                var bMatItem = new DicomDataset();
                bMatItem.AddOrUpdate(DicomTag.DiffusionBValueXX, bMatrix[0]);
                bMatItem.AddOrUpdate(DicomTag.DiffusionBValueXY, bMatrix[1]);
                bMatItem.AddOrUpdate(DicomTag.DiffusionBValueXZ, bMatrix[2]);
                bMatItem.AddOrUpdate(DicomTag.DiffusionBValueYY, bMatrix[3]);
                bMatItem.AddOrUpdate(DicomTag.DiffusionBValueYZ, bMatrix[4]);
                bMatItem.AddOrUpdate(DicomTag.DiffusionBValueZZ, bMatrix[5]);
                mrDiffItem.Add(new DicomSequence(DicomTag.DiffusionBMatrixSequence, bMatItem));
            }
        }

        targetDs.AddOrUpdate(new DicomSequence(DicomTag.MRDiffusionSequence, mrDiffItem));
    }

    public static void ApplyBrainlabDtiFormatting(DicomDataset ds, int? bValue = null, Action<string>? warn = null, Action<string>? log = null)
    {
        if (ds == null) return;

        ds.AddOrUpdate(DicomTag.PatientPosition, "HFS");

        ushort bitsAlloc = ds.GetSingleValueOrDefault<ushort>(DicomTag.BitsAllocated, 16);
        if (bitsAlloc != 16)
        {
            ds.AddOrUpdate(DicomTag.BitsAllocated, (ushort)16);
            ds.AddOrUpdate(DicomTag.BitsStored, (ushort)16);
            ds.AddOrUpdate(DicomTag.HighBit, (ushort)15);
            ds.AddOrUpdate(DicomTag.PixelRepresentation, (ushort)0);
            log?.Invoke("    [Brainlab DTI] Speicherformat auf 16-Bit Unsigned DICOM angepasst.");
        }

        int? effectiveB = bValue ?? ExtractBValue(ds);
        if (effectiveB.HasValue)
        {
            PopulateMrDiffusionMacro(ds, ds, effectiveB.Value);

            string origDesc = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, "DTI");
            string bSuffix = $"_b{effectiveB.Value}";
            if (!origDesc.EndsWith(bSuffix, StringComparison.OrdinalIgnoreCase))
            {
                string cleanDesc = Regex.Replace(origDesc, @"_b\d+$", "", RegexOptions.IgnoreCase);
                ds.AddOrUpdate(DicomTag.SeriesDescription, $"{cleanDesc}{bSuffix}");
            }
        }

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
