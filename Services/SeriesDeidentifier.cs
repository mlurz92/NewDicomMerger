using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FellowOakDicom;

namespace NewDicomMerger.Services;

/// <summary>
/// Performs de-identification of a DICOM dataset with consistent remapping across
/// an entire batch run (all series processed through the same instance share the
/// same UID-, patient-ID- and date-shift-mapping, so relationships between series
/// of the same original study/patient are preserved without exposing the original
/// identifiers).
///
/// This covers a practical, extended subset of the DICOM PS3.15 Basic Application
/// Level Confidentiality Profile — it is not a certified/complete implementation of
/// the standard's full attribute list (e.g. it does not attempt to strip burned-in
/// pixel annotations).
/// </summary>
public sealed class SeriesDeidentifier
{
    private readonly ConcurrentDictionary<string, string> _uidMap = new();
    private readonly ConcurrentDictionary<string, string> _patientIdMap = new();
    private readonly int _dateShiftDays;

    public SeriesDeidentifier(int? seed = null)
    {
        // One shift offset per batch run: preserves the temporal relationship between
        // a patient's studies/series while still de-identifying the absolute date.
        _dateShiftDays = new Random(seed ?? Environment.TickCount).Next(1, 365);
    }

    public void Anonymize(DicomDataset ds)
    {
        // ── Patient identity ──
        string originalPatientId = ds.GetSingleValueOrDefault(DicomTag.PatientID, "");
        ds.AddOrUpdate(DicomTag.PatientName, "ANONYMOUS^PATIENT");
        ds.AddOrUpdate(DicomTag.PatientID, RemapPatientId(originalPatientId));
        ds.AddOrUpdate(DicomTag.PatientBirthDate, "19000101");

        ds.Remove(DicomTag.PatientSex);
        ds.Remove(DicomTag.PatientAge);
        ds.Remove(DicomTag.PatientWeight);
        ds.Remove(DicomTag.PatientSize);
        ds.Remove(DicomTag.PatientAddress);
        ds.Remove(DicomTag.PatientTelephoneNumbers);
        ds.Remove(DicomTag.EthnicGroup);
        ds.Remove(DicomTag.Occupation);
        ds.Remove(DicomTag.AdditionalPatientHistory);
        ds.Remove(DicomTag.PatientComments);
        ds.Remove(DicomTag.OtherPatientNames);
        ds.Remove(DicomTag.OtherPatientIDsSequence);
        ds.Remove(DicomTag.PatientInsurancePlanCodeSequence);
        ds.Remove(DicomTag.PatientReligiousPreference);
        ds.Remove(DicomTag.PatientMotherBirthName);
        ds.Remove(DicomTag.MilitaryRank);
        ds.Remove(DicomTag.BranchOfService);
        ds.Remove(DicomTag.CountryOfResidence);
        ds.Remove(DicomTag.RegionOfResidence);
        ds.Remove(DicomTag.CurrentPatientLocation);

        // ── Physicians & institutions ──
        ds.Remove(DicomTag.InstitutionName);
        ds.Remove(DicomTag.InstitutionAddress);
        ds.Remove(DicomTag.InstitutionalDepartmentName);
        ds.Remove(DicomTag.ReferringPhysicianName);
        ds.Remove(DicomTag.ReferringPhysicianAddress);
        ds.Remove(DicomTag.ReferringPhysicianTelephoneNumbers);
        ds.Remove(DicomTag.PerformingPhysicianName);
        ds.Remove(DicomTag.NameOfPhysiciansReadingStudy);
        ds.Remove(DicomTag.OperatorsName);
        ds.Remove(DicomTag.RequestingPhysician);
        ds.Remove(DicomTag.PhysiciansOfRecord);
        ds.Remove(DicomTag.StationName);
        ds.Remove(DicomTag.PerformedProcedureStepID);
        ds.Remove(DicomTag.DeviceSerialNumber);

        // ── IDs ──
        ds.Remove(DicomTag.AccessionNumber);
        ds.Remove(DicomTag.StudyID);

        // ── UIDs: remapped consistently instead of merely left untouched, so the
        //    anonymized file set carries no trace of the original identifiers while
        //    still linking series that belonged to the same original study/patient. ──
        RemapUidTag(ds, DicomTag.StudyInstanceUID);
        RemapUidTag(ds, DicomTag.SeriesInstanceUID);
        RemapUidTag(ds, DicomTag.FrameOfReferenceUID);

        // ── Dates: shifted by a consistent per-batch offset rather than deleted,
        //    preserving chronological ordering across a patient's series. Times are
        //    removed outright as they add re-identification risk with little value. ──
        ShiftDateTag(ds, DicomTag.StudyDate);
        ShiftDateTag(ds, DicomTag.SeriesDate);
        ShiftDateTag(ds, DicomTag.AcquisitionDate);
        ShiftDateTag(ds, DicomTag.ContentDate);
        ds.Remove(DicomTag.StudyTime);
        ds.Remove(DicomTag.SeriesTime);
        ds.Remove(DicomTag.AcquisitionTime);
        ds.Remove(DicomTag.ContentTime);
        ds.Remove(DicomTag.StudyDescription);

        // ── Private/vendor tags: not part of any public IOD, frequently carry
        //    device- or site-identifying information, and are unconditionally removed. ──
        ds.Remove(item => item.Tag.IsPrivate);
    }

    private string RemapPatientId(string originalPatientId)
    {
        if (string.IsNullOrWhiteSpace(originalPatientId))
            return "ANON-00000000";

        return _patientIdMap.GetOrAdd(originalPatientId, key => $"ANON-{ShortHash(key)}");
    }

    private void RemapUidTag(DicomDataset ds, DicomTag tag)
    {
        if (!ds.Contains(tag)) return;
        string original = ds.GetSingleValueOrDefault(tag, "");
        if (string.IsNullOrWhiteSpace(original)) return;

        string remapped = _uidMap.GetOrAdd(original, _ => DicomUID.Generate().UID);
        DicomScanner.SafeSetUid(ds, tag, remapped);
    }

    private void ShiftDateTag(DicomDataset ds, DicomTag tag)
    {
        if (!ds.Contains(tag))
            return;

        string raw = ds.GetSingleValueOrDefault(tag, "");
        if (raw.Length == 8
            && DateTime.TryParseExact(raw, "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var date))
        {
            var shifted = date.AddDays(-_dateShiftDays);
            ds.AddOrUpdate(tag, shifted.ToString("yyyyMMdd"));
        }
        else
        {
            ds.Remove(tag);
        }
    }

    private static string ShortHash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(8);
        foreach (byte b in hash[..4])
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
