using System.ComponentModel;
using System.Runtime.CompilerServices;
using NewDicomMerger.Models;

namespace NewDicomMerger.Models;

public class ReviewItemViewModel : INotifyPropertyChanged
{
    private string _patientName = string.Empty;
    private string _seriesName = string.Empty;
    private bool _isSelected = true;

    public required SeriesGroup OriginalGroup { get; init; }
    
    // Read-only properties for display
    public string OriginalPatientName { get; init; } = string.Empty;
    public string OriginalSeriesName { get; init; } = string.Empty;
    public string Modality { get; init; } = string.Empty;
    public int FrameCount { get; init; }

    /// <summary>Files excluded from this series during grouping due to inconsistent
    /// pixel properties — see SeriesGroup.ExcludedFileCount / DicomScanner.GroupAndSort.</summary>
    public int ExcludedFileCount { get; init; }
    public bool HasWarning => ExcludedFileCount > 0;
    public string StatusText => HasWarning ? $"⚠ {ExcludedFileCount} ausgeschlossen" : "✓ OK";
    public string StatusTooltip => HasWarning
        ? $"{ExcludedFileCount} Datei(en) dieser Serie wurden wegen inkonsistenter Pixel-Eigenschaften " +
          "(Größe, Bit-Tiefe, Vorzeichen, Farbmodell oder Transfersyntax) von der Zusammenführung ausgeschlossen."
        : "Alle gefundenen Dateien dieser Serie sind konsistent und werden vollständig verarbeitet.";

    // Editable properties
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string PatientName
    {
        get => _patientName;
        set
        {
            if (_patientName != value)
            {
                _patientName = value;
                OnPropertyChanged();
            }
        }
    }

    public string SeriesName
    {
        get => _seriesName;
        set
        {
            if (_seriesName != value)
            {
                _seriesName = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
