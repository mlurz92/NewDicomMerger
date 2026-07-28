using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;
using NewDicomMerger.Models;
using NewDicomMerger.Services;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace NewDicomMerger;

public partial class MainWindow : Window
{


    public static readonly DependencyProperty BgBrushProperty = DependencyProperty.Register(nameof(BgBrush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x0A))));
    public Brush BgBrush { get => (Brush)GetValue(BgBrushProperty); set => SetValue(BgBrushProperty, value); }

    public static readonly DependencyProperty CardBgBrushProperty = DependencyProperty.Register(nameof(CardBgBrush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x10))));
    public Brush CardBgBrush { get => (Brush)GetValue(CardBgBrushProperty); set => SetValue(CardBgBrushProperty, value); }

    public static readonly DependencyProperty CardBorderBrushBrushProperty = DependencyProperty.Register(nameof(CardBorderBrushBrush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x22))));
    public Brush CardBorderBrushBrush { get => (Brush)GetValue(CardBorderBrushBrushProperty); set => SetValue(CardBorderBrushBrushProperty, value); }

    public static readonly DependencyProperty Border1BrushProperty = DependencyProperty.Register(nameof(Border1Brush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2E))));
    public Brush Border1Brush { get => (Brush)GetValue(Border1BrushProperty); set => SetValue(Border1BrushProperty, value); }

    public static readonly DependencyProperty TextPrimaryBrushProperty = DependencyProperty.Register(nameof(TextPrimaryBrush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xEC, 0xED, 0xF0))));
    public Brush TextPrimaryBrush { get => (Brush)GetValue(TextPrimaryBrushProperty); set => SetValue(TextPrimaryBrushProperty, value); }

    public static readonly DependencyProperty TextSecondaryBrushProperty = DependencyProperty.Register(nameof(TextSecondaryBrush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96))));
    public Brush TextSecondaryBrush { get => (Brush)GetValue(TextSecondaryBrushProperty); set => SetValue(TextSecondaryBrushProperty, value); }

    public static readonly DependencyProperty TextMutedBrushProperty = DependencyProperty.Register(nameof(TextMutedBrush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B))));
    public Brush TextMutedBrush { get => (Brush)GetValue(TextMutedBrushProperty); set => SetValue(TextMutedBrushProperty, value); }

    public static readonly DependencyProperty BodyTextBrushProperty = DependencyProperty.Register(nameof(BodyTextBrush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xCC, 0xCD, 0xD2))));
    public Brush BodyTextBrush { get => (Brush)GetValue(BodyTextBrushProperty); set => SetValue(BodyTextBrushProperty, value); }

    public static readonly DependencyProperty TextDimBrushProperty = DependencyProperty.Register(nameof(TextDimBrush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x76))));
    public Brush TextDimBrush { get => (Brush)GetValue(TextDimBrushProperty); set => SetValue(TextDimBrushProperty, value); }

    public static readonly DependencyProperty Surface1BrushProperty = DependencyProperty.Register(nameof(Surface1Brush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14))));
    public Brush Surface1Brush { get => (Brush)GetValue(Surface1BrushProperty); set => SetValue(Surface1BrushProperty, value); }

    public static readonly DependencyProperty InputBgBrushProperty = DependencyProperty.Register(nameof(InputBgBrush), typeof(Brush), typeof(MainWindow), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0D))));
    public Brush InputBgBrush { get => (Brush)GetValue(InputBgBrushProperty); set => SetValue(InputBgBrushProperty, value); }

    private bool _isProcessing;
    private ObservableCollection<ReviewItemViewModel> _reviewItems = new();
    private List<LoadedDicom> _allLoadedFiles = new();
    private List<string> _tempZipFolders = new();
    private List<ProcessingJob> _jobs = new();
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _dashboardTimer;
    private DateTime _startTime;
    private int _totalItems;
    private int _processedCount;


    private LoadedDicom[]? _currentPreviewFiles;
    private double _windowWidth = 0;
    private double _windowCenter = 0;
    private bool _wlInitialized;


    private List<DicomTagEntry> _allTagEntries = new();


    private readonly LruCache<string, DicomVolume> _volumeCache = new(3);


    private DicomVolume? _currentVolume;
    private int _currentAxialFrame = 0;
    private int _isMprRendering = 0;
    private bool _renderRequested = false;


    private string _lastOutputDir = string.Empty;
    private int _lastCreated;
    private int _lastErrors;
    private double _lastElapsedSeconds;


    private static readonly string DefaultTemplate = "{Patient}-{Serie}-{Datum}";


    private System.Collections.Concurrent.ConcurrentBag<BatchReportEntry> _batchReport = new();


    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;


            int trueVal = 1;
            DwmSetWindowAttribute(hwnd, 20, ref trueVal, sizeof(int));


            int backdropType = 2;
            DwmSetWindowAttribute(hwnd, 38, ref backdropType, sizeof(int));
        }
        catch { }
    }


    protected override void OnClosed(EventArgs e)
    {
        _dashboardTimer?.Stop();
        CleanupTempFolders();
        base.OnClosed(e);
    }

    public MainWindow()
    {
        InitializeComponent();
        ReviewGrid.ItemsSource = _reviewItems;
        TxtFilenameTemplate.Text = DefaultTemplate;
        new DicomSetupBuilder().RegisterServices(s => s.AddTransient<IImageManager, WPFImageManager>()).Build();

        ApplyTheme();
    }


    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);


        if (e.Key == Key.F1)
        {
            ToggleShortcutOverlay();
            e.Handled = true;
            return;
        }


        if (e.Key == Key.Escape)
        {
            if (ShortcutOverlay.Visibility == Visibility.Visible)
            { AnimateOut(ShortcutOverlay); e.Handled = true; return; }
            if (TagViewerPanel.Visibility == Visibility.Visible)
            { AnimateOut(TagViewerPanel); e.Handled = true; return; }
            if (_isProcessing && _cts != null && !_cts.IsCancellationRequested)
            { CancelButton_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
        }


        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.O:
                    BrowseButton_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.Return:
                    if (StartProcessButton.Visibility == Visibility.Visible && StartProcessButton.IsEnabled)
                    { StartProcessButton_Click(this, new RoutedEventArgs()); e.Handled = true; }
                    break;
                case Key.A:
                    if (ReviewPanel.Visibility == Visibility.Visible)
                    { SelectAll_Click(this, new RoutedEventArgs()); e.Handled = true; }
                    break;
                case Key.D:
                    if (ReviewPanel.Visibility == Visibility.Visible)
                    { SelectNone_Click(this, new RoutedEventArgs()); e.Handled = true; }
                    break;
                case Key.T:
                    if (ReviewPanel.Visibility == Visibility.Visible && ReviewGrid.SelectedItem is ReviewItemViewModel)
                    { TagViewerButton_Click(this, new RoutedEventArgs()); e.Handled = true; }
                    break;
            }
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (e.Key == Key.S)
            {
                if (ReviewPanel.Visibility == Visibility.Visible)
                { IgnoreScouts_Click(this, new RoutedEventArgs()); e.Handled = true; }
            }
        }


        if (e.Key == Key.F5)
        {
            ClearButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

    }

    private void ToggleShortcutOverlay()
    {
        if (ShortcutOverlay.Visibility == Visibility.Visible)
            AnimateOut(ShortcutOverlay);
        else
            AnimateIn(ShortcutOverlay);
    }

    private void CloseShortcutOverlay_Click(object sender, RoutedEventArgs e) => AnimateOut(ShortcutOverlay);


    private void AnimateIn(FrameworkElement element, double fromY = 30, double durationMs = 350)
    {
        element.Visibility = Visibility.Visible;
        element.Opacity = 0;

        if (element.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform(0, fromY);
            element.RenderTransform = tt;
        }
        else
        {
            tt.Y = fromY;
        }

        var sb = new Storyboard();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fadeIn, element);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));

        var slideIn = new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(durationMs))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(slideIn, element);
        Storyboard.SetTargetProperty(slideIn, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        sb.Children.Add(fadeIn);
        sb.Children.Add(slideIn);
        sb.Begin();
    }

    private void AnimateOut(FrameworkElement element, double toY = -20, double durationMs = 250, Action? onComplete = null)
    {
        if (element.RenderTransform is not TranslateTransform)
            element.RenderTransform = new TranslateTransform(0, 0);

        var sb = new Storyboard();

        var fadeOut = new DoubleAnimation(element.Opacity, 0, TimeSpan.FromMilliseconds(durationMs))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(fadeOut, element);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));

        var slideOut = new DoubleAnimation(0, toY, TimeSpan.FromMilliseconds(durationMs))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(slideOut, element);
        Storyboard.SetTargetProperty(slideOut, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        sb.Children.Add(fadeOut);
        sb.Children.Add(slideOut);
        sb.Completed += (_, _) =>
        {
            element.Visibility = Visibility.Collapsed;
            onComplete?.Invoke();
        };
        sb.Begin();
    }

    private void AnimateFadeIn(FrameworkElement element, double durationMs = 300, double delayMs = 0)
    {
        element.Visibility = Visibility.Visible;
        element.Opacity = 0;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void AnimateStaggeredStatCards()
    {
        StatsRow.Visibility = Visibility.Visible;
        int index = 0;
        foreach (var child in StatsRow.Children)
        {
            if (child is Border border)
            {
                border.Opacity = 0;
                border.RenderTransform = new TranslateTransform(0, 20);

                var sb = new Storyboard();
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                { BeginTime = TimeSpan.FromMilliseconds(index * 120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(fade, border);
                Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));

                var slide = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(300))
                { BeginTime = TimeSpan.FromMilliseconds(index * 120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(slide, border);
                Storyboard.SetTargetProperty(slide, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

                sb.Children.Add(fade);
                sb.Children.Add(slide);
                sb.Begin();
                index++;
            }
        }
    }


    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaxBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();


    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        HandleDrag(e);
        if (e.Effects != DragDropEffects.None)
        {
            GlobalDragOverlay.Visibility = Visibility.Visible;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e) => HandleDrag(e);

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        GlobalDragOverlay.Visibility = Visibility.Collapsed;
    }

    private void HandleDrag(DragEventArgs e)
    {
        if (_isProcessing || !e.Data.GetDataPresent(DataFormats.FileDrop))
        { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        e.Effects = paths.Any(p => Directory.Exists(p) || IsLikelyDicom(p) || p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        GlobalDragOverlay.Visibility = Visibility.Collapsed;
        if (_isProcessing) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _isProcessing = true;
        BrowseButton.IsEnabled = false;
        ClearButton.IsEnabled = false;
        StartProcessButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        StatsRow.Visibility = Visibility.Collapsed;
        _reviewItems.Clear();
        _volumeCache.Clear();
        _currentVolume = null;
        await ScanAndReviewAsync(paths.ToList());
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        var dialog = new OpenFolderDialog { Title = "Ordner mit DICOM Dateien auswählen" };
        if (dialog.ShowDialog() == true)
            await ScanAndReviewAsync(new List<string> { dialog.FolderName });
    }


    private async Task ScanAndReviewAsync(List<string> droppedPaths)
    {
        _isProcessing = true;
        _reviewItems.Clear();
        _jobs.Clear();
        CleanupTempFolders();

        LogTextBox.Text = string.Empty;


        AnimateOut(DropZonePanel, -30, 200);
        CompletionPanel.Visibility = Visibility.Collapsed;
        ReviewPanel.Visibility = Visibility.Collapsed;
        await Task.Delay(220);
        AnimateIn(LogPanel, 20, 300);
        AnimateFadeIn(DashboardPanel, 300, 100);

        ActionBar.Visibility = Visibility.Collapsed;
        StatsRow.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        StartProcessButton.Visibility = Visibility.Collapsed;
        BrowseButton.IsEnabled = false;

        SetProgress(0);
        DashboardStatusText.Text = "Entpacke/Scanne Dateien…";
        DashboardEtaText.Text = "";

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var scanner = new DicomScanner(msg => Dispatcher.Invoke(() => AppendLog(msg)));

            await Task.Run(() =>
            {
                foreach (var path in droppedPaths)
                {
                    ct.ThrowIfCancellationRequested();
                    if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        Dispatcher.Invoke(() => AppendLog($"Entpacke {System.IO.Path.GetFileName(path)}..."));
                        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NewDicomMerger", "TempExtractions", Guid.NewGuid().ToString());
                        Directory.CreateDirectory(tempDir);
                        ZipFile.ExtractToDirectory(path, tempDir, true);

                        string baseName = System.IO.Path.GetFileNameWithoutExtension(path);
                        string outputDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? string.Empty, $"{baseName}_Output");
                        Directory.CreateDirectory(outputDir);

                        _jobs.Add(new ProcessingJob { InputDirectory = tempDir, OutputDirectory = outputDir });
                        _tempZipFolders.Add(tempDir);
                    }
                    else if (Directory.Exists(path))
                    {
                        _jobs.Add(new ProcessingJob { InputDirectory = path, OutputDirectory = path });
                    }
                    else if (IsLikelyDicom(path))
                    {
                        string dir = System.IO.Path.GetDirectoryName(path)!;
                        if (!_jobs.Any(j => j.InputDirectory == dir))
                            _jobs.Add(new ProcessingJob { InputDirectory = dir, OutputDirectory = dir });
                    }
                }

                if (_jobs.Count == 0) return;

                var loadedBag = new System.Collections.Concurrent.ConcurrentBag<LoadedDicom>();
                var result = new MergeResult();

                foreach (var job in _jobs)
                {
                    ct.ThrowIfCancellationRequested();
                    var candidates = scanner.FindCandidates(job.InputDirectory, ct);
                    if (candidates.Count == 0) continue;

                    int total = candidates.Count;
                    int done = 0;


                    Parallel.ForEach(candidates, new ParallelOptions
                    {
                        CancellationToken = ct,
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    }, candidate =>
                    {
                        var ld = scanner.TryLoad(candidate, result, ct);
                        if (ld != null) loadedBag.Add(ld);

                        int completed = Interlocked.Increment(ref done);
                        if (completed % 25 == 0 || completed == total)
                            Dispatcher.Invoke(() => SetProgress(100.0 * completed / total));
                    });
                }

                var allLoaded = loadedBag.ToList();
                _allLoadedFiles = allLoaded;

                bool splitByBValue = Dispatcher.Invoke(() => CheckBrainlabDti.IsChecked == true);

                Dispatcher.Invoke(() => DashboardStatusText.Text = "Gruppiere Serien…");
                var groups = scanner.GroupAndSort(allLoaded, result, splitByBValue, ct);

                Dispatcher.Invoke(() =>
                {
                    PopulateReviewGrid(groups);

                    if (_jobs.Count > 0 && string.IsNullOrEmpty(TxtOutputDir.Text))
                        TxtOutputDir.Text = _jobs[0].OutputDirectory;
                });
            }, ct);

            if (_reviewItems.Count > 0)
            {

                AnimateOut(LogPanel, -20, 200);
                AnimateOut(DashboardPanel, -15, 200);
                await Task.Delay(230);
                AnimateIn(ReviewPanel, 25, 400);
                StartProcessButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                AnimateFadeIn(ActionBar, 300, 200);
                UpdateTemplatePreview();
            }
            else
            {
                AppendLog("Keine gültigen DICOM-Serien gefunden.");
                DashboardPanel.Visibility = Visibility.Collapsed;
                AnimateFadeIn(ActionBar, 300, 100);
                ClearButton.IsEnabled = true;
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("\nSCAN ABGEBROCHEN.");
            DashboardPanel.Visibility = Visibility.Collapsed;
            AnimateFadeIn(ActionBar, 300);
            ClearButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fehler beim Scannen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isProcessing = false;
            BrowseButton.IsEnabled = true;
        }
    }

    private void PopulateReviewGrid(List<SeriesGroup> groups)
    {
        _reviewItems.Clear();

        var groupInfoList = groups.Select(g =>
        {
            var first = g.Files[0];
            var ds = first.File.Dataset;
            string pName = ds.GetSingleValueOrDefault(DicomTag.PatientName, "Unbekannt").Replace("^", "_").Trim();
            string pId = ds.GetSingleValueOrDefault(DicomTag.PatientID, pName).Trim();
            string sName = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, "Serie").Trim();
            string studyUid = g.StudyInstanceUid;
            int seriesNumber = ds.GetSingleValueOrDefault(DicomTag.SeriesNumber, 0);
            int acqNumber = ds.GetSingleValueOrDefault(DicomTag.AcquisitionNumber, 0);

            string sortableDateTime = GetSortableStudyDateTime(ds);
            string seriesTime = GetSortableSeriesTime(ds);

            int? bVal = DiffusionBValueHelper.ExtractBValue(first.Dataset);
            if (bVal.HasValue && !sName.Contains($"b{bVal.Value}", StringComparison.OrdinalIgnoreCase) && !sName.Contains($"b={bVal.Value}", StringComparison.OrdinalIgnoreCase))
            {
                sName = $"{sName}_b{bVal.Value}";
            }

            return new
            {
                Group = g,
                PatientKey = string.IsNullOrEmpty(pId) ? pName.ToLowerInvariant() : pId.ToLowerInvariant(),
                PatientName = pName,
                OriginalSeriesName = sName,
                SeriesName = sName,
                StudyInstanceUid = studyUid,
                SeriesNumber = seriesNumber,
                AcquisitionNumber = acqNumber,
                StudyDateTime = sortableDateTime,
                SeriesTime = seriesTime,
                FirstFile = first,
                BValue = bVal
            };
        }).ToList();


        var byPatient = groupInfoList.GroupBy(x => x.PatientKey);
        var processedItems = new List<(SeriesGroup Group, string PatientName, string SeriesName, LoadedDicom FirstFile, bool IsLocalizer, int? BValue)>();

        foreach (var patientGroup in byPatient)
        {
            var studies = patientGroup.GroupBy(x => x.StudyInstanceUid).ToList();

            HashSet<string> earlierStudyUids = new();
            if (studies.Count > 1)
            {
                var sortedStudies = studies
                    .Select(st => new
                    {
                        StudyUid = st.Key,
                        DateTime = st.Max(item => item.StudyDateTime)
                    })
                    .OrderBy(st => st.DateTime)
                    .ToList();


                for (int i = 0; i < sortedStudies.Count - 1; i++)
                {
                    earlierStudyUids.Add(sortedStudies[i].StudyUid);
                }
            }

            var patientSeriesList = patientGroup.Select(x =>
            {
                string finalName = x.OriginalSeriesName;
                if (earlierStudyUids.Contains(x.StudyInstanceUid))
                {
                    if (!finalName.EndsWith("_VU", StringComparison.OrdinalIgnoreCase))
                        finalName += "_VU";
                }
                return new
                {
                    x.Group,
                    x.PatientName,
                    OriginalSeriesName = x.OriginalSeriesName,
                    SeriesName = finalName,
                    x.StudyInstanceUid,
                    x.SeriesNumber,
                    x.AcquisitionNumber,
                    x.SeriesTime,
                    x.FirstFile,
                    x.BValue
                };
            }).ToList();


            var byStudy = patientSeriesList.GroupBy(x => x.StudyInstanceUid);
            foreach (var studyGroup in byStudy)
            {
                var byName = studyGroup.GroupBy(x => x.SeriesName, StringComparer.OrdinalIgnoreCase);
                foreach (var nameGroup in byName)
                {
                    var list = nameGroup
                        .OrderBy(x => x.SeriesTime)
                        .ThenBy(x => x.SeriesNumber)
                        .ThenBy(x => x.AcquisitionNumber)
                        .ThenBy(x => x.Group.SeriesInstanceUid)
                        .ToList();

                    if (list.Count > 1)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var item = list[i];
                            string disambiguatedName = $"{item.SeriesName}_{i + 1}";
                            bool isLoc = IsLocalizerSeries(item.Group);
                            processedItems.Add((item.Group, item.PatientName, disambiguatedName, item.FirstFile, isLoc, item.BValue));
                        }
                    }
                    else
                    {
                        var item = list[0];
                        bool isLoc = IsLocalizerSeries(item.Group);
                        processedItems.Add((item.Group, item.PatientName, item.SeriesName, item.FirstFile, isLoc, item.BValue));
                    }
                }
            }
        }

        foreach (var (g, pName, sName, first, isLoc, bVal) in processedItems)
        {
            string bValText = bVal.HasValue ? $"b={bVal.Value}" : "";
            _reviewItems.Add(new ReviewItemViewModel
            {
                OriginalGroup = g,
                OriginalPatientName = pName,
                PatientName = pName,
                OriginalSeriesName = sName,
                SeriesName = sName,
                Modality = first.Modality,
                FrameCount = g.TotalFrames,
                BValueText = bValText,
                ExcludedFileCount = g.ExcludedFileCount,
                IsSelected = !isLoc
            });
        }
    }

    private async void CheckBrainlabDti_Click(object sender, RoutedEventArgs e)
    {
        if (_allLoadedFiles == null || _allLoadedFiles.Count == 0) return;

        bool splitByBValue = CheckBrainlabDti.IsChecked == true;
        var result = new MergeResult();
        var scanner = new DicomScanner(msg => Dispatcher.Invoke(() => AppendLog(msg)));
        var groups = await Task.Run(() => scanner.GroupAndSort(_allLoadedFiles, result, splitByBValue));

        PopulateReviewGrid(groups);
    }

    private static string GetSortableStudyDateTime(DicomDataset ds)
    {
        string date = GetFirstNonEmptyTagValue(ds, DicomTag.StudyDate, DicomTag.AcquisitionDate, DicomTag.SeriesDate, DicomTag.ContentDate, DicomTag.InstanceCreationDate);
        string time = GetFirstNonEmptyTagValue(ds, DicomTag.StudyTime, DicomTag.AcquisitionTime, DicomTag.SeriesTime, DicomTag.ContentTime, DicomTag.InstanceCreationTime);

        string cleanDate = System.Text.RegularExpressions.Regex.Replace(date, @"[^\d]", "");
        string cleanTime = System.Text.RegularExpressions.Regex.Replace(time, @"[^\d]", "");

        if (cleanDate.Length < 8) cleanDate = cleanDate.PadRight(8, '0');
        if (cleanTime.Length < 6) cleanTime = cleanTime.PadRight(6, '0');

        return $"{cleanDate}_{cleanTime}";
    }

    private static string GetSortableSeriesTime(DicomDataset ds)
    {
        string time = GetFirstNonEmptyTagValue(ds, DicomTag.AcquisitionTime, DicomTag.SeriesTime, DicomTag.ContentTime, DicomTag.StudyTime);
        string cleanTime = System.Text.RegularExpressions.Regex.Replace(time, @"[^\d]", "");
        if (cleanTime.Length < 6) cleanTime = cleanTime.PadRight(6, '0');
        return cleanTime;
    }

    private static string GetFirstNonEmptyTagValue(DicomDataset ds, params DicomTag[] tags)
    {
        foreach (var tag in tags)
        {
            if (ds != null && ds.Contains(tag))
            {
                try
                {
                    string val = ds.GetSingleValueOrDefault(tag, "").Trim();
                    if (!string.IsNullOrEmpty(val)) return val;
                }
                catch { }
            }
        }
        return "";
    }


    private async void ReviewGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReviewGrid.SelectedItem is ReviewItemViewModel item)
        {
            _currentPreviewFiles = item.OriginalGroup.Files.ToArray();


            string? cacheKey = _currentPreviewFiles.Length > 0 ? _currentPreviewFiles[0].SeriesInstanceUid : null;
            if (string.IsNullOrEmpty(cacheKey) || !_volumeCache.TryGetValue(cacheKey, out _))
            {
                _currentVolume = null;
            }

            PreviewControls.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;

            await LoadVolumeAsync(_currentPreviewFiles);
            UpdateTemplatePreview();
        }
        else
        {
            _currentVolume = null;
            _currentPreviewFiles = null;
            PreviewImage.Source = null;
            PreviewEmptyText.Visibility = Visibility.Visible;
            PreviewControls.Visibility = Visibility.Collapsed;
        }
    }


    private async Task LoadVolumeAsync(LoadedDicom[] files)
    {
        if (files == null || files.Length == 0) return;

        string? cacheKey = files[0].SeriesInstanceUid;
        if (!string.IsNullOrEmpty(cacheKey) && _volumeCache.TryGetValue(cacheKey, out var cachedVolume))
        {
            _currentVolume = cachedVolume;
            ApplyLoadedVolumeToUi(files);
            return;
        }


        PreviewSlider.IsEnabled = false;
        PreviewLoadingText.Text = "Lade 2D-Volumen...";
        PreviewLoadingText.Visibility = Visibility.Visible;
        PreviewEmptyText.Visibility = Visibility.Collapsed;

        try
        {
            var volume = await Task.Run(() => BuildVolume(files));

            if (_currentPreviewFiles == files && volume != null)
            {
                _currentVolume = volume;
                if (!string.IsNullOrEmpty(cacheKey))
                    _volumeCache.Put(cacheKey, volume);

                ApplyLoadedVolumeToUi(files);
            }
        }
        catch
        {
            PreviewLoadingText.Text = "Fehler beim Laden des 2D-Volumens";
            PreviewLoadingText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (_currentPreviewFiles == files)
            {
                PreviewSlider.IsEnabled = true;
                PreviewLoadingText.Visibility = Visibility.Collapsed;
            }
        }
    }


    private static DicomVolume BuildVolume(LoadedDicom[] files)
    {
        int fileCount = files.Length;
        var firstFile = files[0];
        int width = firstFile.Columns;
        int height = firstFile.Rows;

        bool isMultiFrameFile = (fileCount == 1 && firstFile.IsMultiFrame);
        int depth;

        if (isMultiFrameFile)
        {
            var ds = firstFile.File.Dataset;
            depth = ds.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1);
        }
        else
        {
            depth = fileCount;
        }

        if (depth < 1) depth = 1;
        var volume = new DicomVolume(width, height, depth);

        try
        {
            if (firstFile.File.Dataset.Contains(DicomTag.PixelSpacing))
            {
                var ps = firstFile.File.Dataset.GetValues<double>(DicomTag.PixelSpacing);
                if (ps.Length >= 2)
                {
                    volume.PixelSpacingX = ps[0];
                    volume.PixelSpacingY = ps[1];
                }
            }
            volume.SliceSpacing = firstFile.File.Dataset.GetSingleValueOrDefault(DicomTag.SliceThickness, 1.0);
        }
        catch { }

        if (isMultiFrameFile)
        {
            try
            {
                var dicomFile = DicomFile.Open(firstFile.FilePath, Services.DicomScanner.LegacyFallbackEncoding, readOption: FileReadOption.ReadAll);
                if (dicomFile.FileMetaInfo.TransferSyntax.IsEncapsulated)
                {
                    var transcoder = new FellowOakDicom.Imaging.Codec.DicomTranscoder(dicomFile.FileMetaInfo.TransferSyntax, DicomTransferSyntax.ExplicitVRLittleEndian);
                    dicomFile = transcoder.Transcode(dicomFile);
                }

                var pixelData = DicomPixelData.Create(dicomFile.Dataset);
                double rescaleSlope = dicomFile.Dataset.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0);
                double rescaleIntercept = dicomFile.Dataset.GetSingleValueOrDefault(DicomTag.RescaleIntercept, 0.0);
                ushort pixelRep = dicomFile.Dataset.GetSingleValueOrDefault(DicomTag.PixelRepresentation, (ushort)0);
                bool isSigned = (pixelRep == 1);
                int bitsAllocated = pixelData.BitsAllocated;

                System.Threading.Tasks.Parallel.For(0, depth, z =>
                {
                    try
                    {
                        var buffer = pixelData.GetFrame(z);
                        var data = buffer.Data;
                        int baseIdx = z * (height * width);

                        if (bitsAllocated == 16)
                        {
                            if (isSigned)
                            {
                                System.ReadOnlySpan<short> signedSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(data);
                                for (int y = 0; y < height; y++)
                                {
                                    int rowOffset = y * width;
                                    for (int x = 0; x < width; x++)
                                    {
                                        short rawVal = signedSpan[rowOffset + x];
                                        volume.VoxelData[baseIdx + rowOffset + x] = (float)(rawVal * rescaleSlope + rescaleIntercept);
                                    }
                                }
                            }
                            else
                            {
                                System.ReadOnlySpan<ushort> ushortSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(data);
                                for (int y = 0; y < height; y++)
                                {
                                    int rowOffset = y * width;
                                    for (int x = 0; x < width; x++)
                                    {
                                        ushort rawVal = ushortSpan[rowOffset + x];
                                        volume.VoxelData[baseIdx + rowOffset + x] = (float)(rawVal * rescaleSlope + rescaleIntercept);
                                    }
                                }
                            }
                        }
                        else if (bitsAllocated == 8)
                        {
                            for (int y = 0; y < height; y++)
                            {
                                int rowOffset = y * width;
                                for (int x = 0; x < width; x++)
                                {
                                    byte rawVal = data[rowOffset + x];
                                    volume.VoxelData[baseIdx + rowOffset + x] = (float)(rawVal * rescaleSlope + rescaleIntercept);
                                }
                            }
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }
        else
        {

            System.Threading.Tasks.Parallel.For(0, depth, z =>
            {
                try
                {
                    var dicomFile = DicomFile.Open(files[z].FilePath, Services.DicomScanner.LegacyFallbackEncoding, readOption: FileReadOption.ReadAll);
                    if (dicomFile.FileMetaInfo.TransferSyntax.IsEncapsulated)
                    {
                        var transcoder = new FellowOakDicom.Imaging.Codec.DicomTranscoder(dicomFile.FileMetaInfo.TransferSyntax, DicomTransferSyntax.ExplicitVRLittleEndian);
                        dicomFile = transcoder.Transcode(dicomFile);
                    }

                    var pixelData = DicomPixelData.Create(dicomFile.Dataset);
                    var buffer = pixelData.GetFrame(0);
                    var data = buffer.Data;

                    double rescaleSlope = dicomFile.Dataset.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0);
                    double rescaleIntercept = dicomFile.Dataset.GetSingleValueOrDefault(DicomTag.RescaleIntercept, 0.0);
                    ushort pixelRep = dicomFile.Dataset.GetSingleValueOrDefault(DicomTag.PixelRepresentation, (ushort)0);
                    bool isSigned = (pixelRep == 1);

                    int bitsAllocated = pixelData.BitsAllocated;
                    int baseIdx = z * (height * width);

                    if (bitsAllocated == 16)
                    {
                        if (isSigned)
                        {
                            System.ReadOnlySpan<short> signedSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(data);
                            for (int y = 0; y < height; y++)
                            {
                                int rowOffset = y * width;
                                for (int x = 0; x < width; x++)
                                {
                                    short rawVal = signedSpan[rowOffset + x];
                                    volume.VoxelData[baseIdx + rowOffset + x] = (float)(rawVal * rescaleSlope + rescaleIntercept);
                                }
                            }
                        }
                        else
                        {
                            System.ReadOnlySpan<ushort> ushortSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(data);
                            for (int y = 0; y < height; y++)
                            {
                                int rowOffset = y * width;
                                for (int x = 0; x < width; x++)
                                {
                                    ushort rawVal = ushortSpan[rowOffset + x];
                                    volume.VoxelData[baseIdx + rowOffset + x] = (float)(rawVal * rescaleSlope + rescaleIntercept);
                                }
                            }
                        }
                    }
                    else if (bitsAllocated == 8)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            int rowOffset = y * width;
                            for (int x = 0; x < width; x++)
                            {
                                byte rawVal = data[rowOffset + x];
                                volume.VoxelData[baseIdx + rowOffset + x] = (float)(rawVal * rescaleSlope + rescaleIntercept);
                            }
                        }
                    }
                }
                catch { }
            });
        }


        float minV = float.MaxValue, maxV = float.MinValue;
        int step = Math.Max(1, volume.VoxelData.Length / 1000);
        for (int i = 0; i < volume.VoxelData.Length; i += step)
        {
            float val = volume.VoxelData[i];
            if (val < minV) minV = val;
            if (val > maxV) maxV = val;
        }
        volume.MinVoxel = minV < maxV ? minV : 0;
        volume.MaxVoxel = minV < maxV ? maxV : 255;

        return volume;
    }

    private void ApplyLoadedVolumeToUi(LoadedDicom[] files)
    {
        _wlInitialized = false;

        int depth = volDepth();

        if (depth > 1)
        {
            PreviewSlider.Minimum = 0;
            PreviewSlider.Maximum = depth - 1;
            PreviewSlider.SmallChange = 1;
            PreviewSlider.LargeChange = Math.Max(1, depth / 10);
            PreviewSlider.TickFrequency = 1;
            PreviewSlider.Value = depth / 2;
            _currentAxialFrame = (int)PreviewSlider.Value;
            PreviewControls.Visibility = Visibility.Visible;
            if (SliceIndexText != null)
            {
                SliceIndexText.Text = $"BILD: {_currentAxialFrame + 1} / {depth}";
            }
        }
        else
        {
            PreviewSlider.Minimum = 0;
            PreviewSlider.Maximum = 0;
            PreviewSlider.Value = 0;
            _currentAxialFrame = 0;
            PreviewControls.Visibility = Visibility.Collapsed;
            if (SliceIndexText != null)
            {
                SliceIndexText.Text = "BILD: 1 / 1";
            }
        }

        try
        {
            var wlFile = DicomFile.Open(files[0].FilePath, Services.DicomScanner.LegacyFallbackEncoding);
            var dcmImage = new DicomImage(wlFile.Dataset);
            _windowWidth = dcmImage.WindowWidth;
            _windowCenter = dcmImage.WindowCenter;

            if (double.IsNaN(_windowWidth) || _windowWidth <= 0 || double.IsNaN(_windowCenter))
            {
                if (_currentVolume != null && _currentVolume.MaxVoxel > _currentVolume.MinVoxel)
                {
                    _windowWidth = _currentVolume.MaxVoxel - _currentVolume.MinVoxel;
                    _windowCenter = _currentVolume.MinVoxel + _windowWidth / 2.0;
                }
                else
                {
                    _windowWidth = 400;
                    _windowCenter = 40;
                }
            }
            _wlInitialized = true;
        }
        catch
        {
            _windowWidth = 400;
            _windowCenter = 40;
            _wlInitialized = true;
        }

        Trigger2DRender();
    }

    private int volDepth() => _currentVolume?.Depth ?? 0;
    private int volHeight() => _currentVolume?.Height ?? 0;
    private int volWidth() => _currentVolume?.Width ?? 0;


    private void Trigger2DRender()
    {
        if (_currentVolume == null) return;


        if (!_wlInitialized) return;

        if (Interlocked.CompareExchange(ref _isMprRendering, 1, 0) != 0)
        {
            _renderRequested = true;
            return;
        }

        _ = Render2DAsync();
    }

    private async Task Render2DAsync()
    {
        var volume = _currentVolume;
        if (volume == null) { _isMprRendering = 0; return; }

        double ww = _windowWidth;
        double wc = _windowCenter;
        int axial = _currentAxialFrame;

        try
        {
            var bmp2D = await Task.Run(() => Render2DSlice(volume, axial, ww, wc));


            if (_currentVolume == volume)
            {
                PreviewImage.Source = bmp2D;
            }
        }
        catch
        {


        }
        finally
        {
            _isMprRendering = 0;
            if (_renderRequested)
            {
                _renderRequested = false;
                Trigger2DRender();
            }
        }
    }

    private void PreviewSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_currentVolume == null) return;
        _currentAxialFrame = (int)e.NewValue;
        if (SliceIndexText != null)
        {
            SliceIndexText.Text = $"BILD: {_currentAxialFrame + 1} / {volDepth()}";
        }
        Trigger2DRender();
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        MprBorder_PreviewMouseWheel(sender, e);
    }

    private void MprBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_currentVolume == null || volDepth() <= 1) return;

        int direction = e.Delta > 0 ? -1 : 1;
        double next = PreviewSlider.Value + direction;
        next = Math.Clamp(next, PreviewSlider.Minimum, PreviewSlider.Maximum);
        if (next != PreviewSlider.Value)
        {
            PreviewSlider.Value = next;
            e.Handled = true;
        }
    }

    private void ApplyTheme()
    {
        Color bg = Color.FromRgb(0x0A, 0x0A, 0x0C);
        Color cardBg = Color.FromRgb(0x12, 0x12, 0x15);
        Color border = Color.FromRgb(0x20, 0x20, 0x24);
        Color border1 = Color.FromRgb(0x2C, 0x2C, 0x30);
        Color accent = Color.FromRgb(0xEC, 0xEC, 0xF1);
        Color accent2 = Color.FromRgb(0x8E, 0x8E, 0x93);
        Color textPrimary = Color.FromRgb(0xEC, 0xED, 0xF0);
        Color textSecondary = Color.FromRgb(0x8E, 0x8E, 0x96);
        Color textMuted = Color.FromRgb(0x52, 0x52, 0x5B);
        Color bodyText = Color.FromRgb(0xCC, 0xCD, 0xD2);
        Color textDim = Color.FromRgb(0x6E, 0x6E, 0x76);
        Color surface1 = Color.FromRgb(0x1C, 0x1C, 0x1E);
        Color inputBg = Color.FromRgb(0x0A, 0x0A, 0x0D);

        SetThemeBrush("Bg", bg);
        SetThemeBrush("CardBg", cardBg);
        SetThemeBrush("CardBorder", border);
        SetThemeBrush("Border1", border1);
        SetThemeBrush("TextPrimary", textPrimary);
        SetThemeBrush("TextSecondary", textSecondary);
        SetThemeBrush("TextMuted", textMuted);
        SetThemeBrush("BodyText", bodyText);
        SetThemeBrush("TextDim", textDim);
        SetThemeBrush("Surface1", surface1);
        SetThemeBrush("InputBg", inputBg);

        var accentBrush = new SolidColorBrush(accent);
        var accent2Brush = new SolidColorBrush(accent2);
        var cardBgBrush = new SolidColorBrush(cardBg);
        var borderBrush = new SolidColorBrush(border);

        this.Background = System.Windows.Media.Brushes.Transparent;

        MprBorder.Background = cardBgBrush;
        MprBorder.BorderBrush = borderBrush;
        MprHeaderBorder.BorderBrush = borderBrush;
        MprHeaderBorder.Background = new LinearGradientBrush(
            Color.FromRgb(0x0D, 0x0D, 0x10),
            Color.FromRgb(0x11, 0x11, 0x14),
            0
        );

        var headerBrush = new LinearGradientBrush(
            Color.FromRgb(0xEC, 0xEC, 0xF1),
            Color.FromRgb(0x8E, 0x8E, 0x93),
            0
        );
        MprTitleText.Foreground = headerBrush;
        PreviewSlider.Foreground = accent2Brush;
        ViewAxial.BorderBrush = borderBrush;
    }


    private void SetThemeBrush(string key, Color color, bool animate = false)
    {

        Resources[key] = new SolidColorBrush(color);


        var dp = key switch
        {
            "Bg" => BgBrushProperty,
            "CardBg" => CardBgBrushProperty,
            "CardBorder" => CardBorderBrushBrushProperty,
            "Border1" => Border1BrushProperty,
            "TextPrimary" => TextPrimaryBrushProperty,
            "TextSecondary" => TextSecondaryBrushProperty,
            "TextMuted" => TextMutedBrushProperty,
            "BodyText" => BodyTextBrushProperty,
            "TextDim" => TextDimBrushProperty,
            "Surface1" => Surface1BrushProperty,
            "InputBg" => InputBgBrushProperty,
            _ => null
        };
        if (dp != null) SetValue(dp, new SolidColorBrush(color));
    }


    private void TagViewerButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReviewGrid.SelectedItem is not ReviewItemViewModel item) return;

        var file = item.OriginalGroup.Files[0];
        _allTagEntries = new List<DicomTagEntry>();

        foreach (var tag in file.File.Dataset)
        {
            string tagHex = $"({tag.Tag.Group:X4},{tag.Tag.Element:X4})";
            string tagName = tag.Tag.DictionaryEntry?.Name ?? "Private Tag";
            string vr = tag.ValueRepresentation?.Code ?? "??";
            string value;

            try
            {
                if (tag is DicomSequence seq)
                    value = $"[Sequence: {seq.Items.Count} item(s)]";
                else if (tag is DicomFragmentSequence)
                    value = "[Pixel Data Fragment]";
                else if (tag.Tag == DicomTag.PixelData)
                    value = "[Pixel Data]";
                else
                {
                    var vals = file.File.Dataset.GetValues<string>(tag.Tag);
                    value = vals != null && vals.Length > 0 ? string.Join(" \\ ", vals) : "";
                    if (value.Length > 200) value = value[..200] + "…";
                }
            }
            catch
            {
                value = "[nicht lesbar]";
            }

            _allTagEntries.Add(new DicomTagEntry
            {
                TagHex = tagHex,
                TagName = tagName,
                VR = vr,
                Value = value
            });
        }

        TagGrid.ItemsSource = _allTagEntries;
        TagSearchBox.Text = "";
        AnimateIn(TagViewerPanel, 0, 250);
    }

    private void TagSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = TagSearchBox.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(query))
        {
            TagGrid.ItemsSource = _allTagEntries;
            return;
        }

        TagGrid.ItemsSource = _allTagEntries.Where(t =>
            t.TagHex.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            t.TagName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            t.VR.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            t.Value.Contains(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    private void CloseTagViewer_Click(object sender, RoutedEventArgs e)
    {
        AnimateOut(TagViewerPanel, 0, 200);
    }


    private string ResolveTemplate(string template, ReviewItemViewModel item)
    {
        var ds = item.OriginalGroup.Files[0].File.Dataset;

        string date = ds.GetSingleValueOrDefault(DicomTag.StudyDate, "");
        if (date.Length == 8)
            date = $"{date[..4]}-{date[4..6]}-{date[6..8]}";

        string studyId = ds.GetSingleValueOrDefault(DicomTag.StudyID, "");
        string accession = ds.GetSingleValueOrDefault(DicomTag.AccessionNumber, "");

        var result = template
            .Replace("{Patient}", item.PatientName)
            .Replace("{Serie}", item.SeriesName)
            .Replace("{Datum}", date)
            .Replace("{Modalität}", item.Modality)
            .Replace("{Modality}", item.Modality)
            .Replace("{StudyID}", studyId)
            .Replace("{Frames}", item.FrameCount.ToString())
            .Replace("{Accession}", accession);


        return string.Join("_", result.Split(System.IO.Path.GetInvalidFileNameChars()));
    }

    private void UpdateTemplatePreview()
    {

    }

    private void TxtFilenameTemplate_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTemplatePreview();
    }


    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _reviewItems) item.IsSelected = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _reviewItems) item.IsSelected = false;
    }

    private void IgnoreScouts_Click(object sender, RoutedEventArgs e)
    {
        string[] scouts = { "loc", "scout", "survey", "plane", "localizer", "topogram", "phoenix" };
        foreach (var item in _reviewItems)
        {
            string name = item.SeriesName.ToLower();
            if (scouts.Any(s => name.Contains(s))) item.IsSelected = false;
        }
        ShowToast("Scouts wurden abgewählt");
    }

    private void ChangeOutputDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Ausgabeordner wählen" };
        if (dialog.ShowDialog() == true)
            TxtOutputDir.Text = dialog.FolderName;
    }


    private async void StartProcessButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = _reviewItems.Where(i => i.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            MessageBox.Show("Bitte wähle mindestens eine Serie aus.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isProcessing = true;


        AnimateOut(ReviewPanel, -20, 200);
        await Task.Delay(230);
        AnimateIn(LogPanel, 20, 300);
        AnimateFadeIn(DashboardPanel, 300, 100);

        StartProcessButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        BrowseButton.IsEnabled = false;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _startTime = DateTime.Now;
        try { File.Delete(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "process_log.txt")); } catch { }

        string customOutDir = TxtOutputDir.Text.Trim();
        _lastOutputDir = customOutDir;

        try
        {
            await ProcessAutoAsync(ct, selectedItems);

            _lastElapsedSeconds = (DateTime.Now - _startTime).TotalSeconds;


            if (_lastErrors == 0)
            {
                AnimateOut(LogPanel, -20, 200);
                AnimateOut(DashboardPanel, -15, 200);
                await Task.Delay(250);
            }
            else
            {
                AnimateOut(DashboardPanel, -15, 200);
                await Task.Delay(250);
            }
            ShowCompletionPanel();
        }
        catch (OperationCanceledException)
        {
            AppendLog("\nVERARBEITUNG ABGEBROCHEN.");
            DashboardStatusText.Text = "Abgebrochen.";
            DashboardEtaText.Text = "";
            DashboardProgressFill.Width = 0;
            DashboardProgressFill.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        }
        catch (Exception ex)
        {
            _lastElapsedSeconds = (DateTime.Now - _startTime).TotalSeconds;
            _lastErrors = Math.Max(1, _lastErrors + 1);
            AppendLog($"\n✗ Unerwarteter Verarbeitungsfehler: {ex}");
            DashboardStatusText.Text = "Verarbeitung mit Fehler beendet.";
            DashboardEtaText.Text = "Fehler";
            DashboardProgressFill.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            ShowStats(_reviewItems.Count, selectedItems.Count, _lastCreated, _lastErrors);
            ShowCompletionPanel();
        }
        finally
        {
            _isProcessing = false;
            CancelButton.Visibility = Visibility.Collapsed;
            BrowseButton.IsEnabled = true;
            CleanupTempFolders();
        }
    }


    private async Task ProcessAutoAsync(CancellationToken ct, List<ReviewItemViewModel> selectedItems)
    {
        var merger = new FrameMerger(
            msg => Dispatcher.Invoke(() => AppendLog(msg)),
            msg => Dispatcher.Invoke(() => AppendLog($"WARN: {msg}")));

        var splitter = new FrameSplitter(
            msg => Dispatcher.Invoke(() => AppendLog(msg)),
            msg => Dispatcher.Invoke(() => AppendLog($"WARN: {msg}")));

        bool anonymize = CheckAnonymize.IsChecked == true;
        bool compressDicom = CheckCompressDicom.IsChecked == true;
        bool brainlabDti = CheckBrainlabDti.IsChecked == true;
        bool generateAuditReport = CheckBrainlabAuditReport.IsChecked == true;
        string customOutDir = TxtOutputDir.Text.Trim();
        var anonymizer = anonymize ? new SeriesDeidentifier() : null;
        var createdPaths = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var reservationGate = new object();
        var reservedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _batchReport = new System.Collections.Concurrent.ConcurrentBag<BatchReportEntry>();
        _lastCreated = 0;
        _lastErrors = 0;

        StartDashboard(selectedItems.Count, "Verarbeite...");

        bool useTurbo = CheckTurbo.IsChecked == true;
        int maxDegree = useTurbo ? Math.Max(1, Environment.ProcessorCount) : 1;
        string template = string.IsNullOrWhiteSpace(TxtFilenameTemplate.Text) ? DefaultTemplate : TxtFilenameTemplate.Text;

        var currentProc = System.Diagnostics.Process.GetCurrentProcess();
        var prevPriority = currentProc.PriorityClass;
        try
        {
            currentProc.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
        }
        catch
        {
        }

        try
        {
            await Parallel.ForEachAsync(
                selectedItems,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDegree,
                    CancellationToken = ct
                },
                async (item, token) =>
                {
                    string reportOutputPath = string.Empty;

                    try
                    {
                        var group = item.OriginalGroup;
                        if (group.Files.Count == 0)
                            throw new InvalidOperationException("Die ausgewählte Serie enthält keine verarbeitbaren DICOM-Dateien.");

                        string firstFile = group.Files[0].FilePath;
                        var job = _jobs.FirstOrDefault(j => firstFile.StartsWith(j.InputDirectory, StringComparison.OrdinalIgnoreCase));

                        string baseOutDir = string.IsNullOrWhiteSpace(customOutDir)
                            ? job?.OutputDirectory ?? System.IO.Path.GetDirectoryName(firstFile) ?? AppDomain.CurrentDomain.BaseDirectory
                            : customOutDir;

                        string outBaseDir = baseOutDir;
                        if (string.IsNullOrWhiteSpace(customOutDir) && job != null && firstFile.StartsWith(job.InputDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            string fileDir = System.IO.Path.GetDirectoryName(firstFile) ?? job.InputDirectory;
                            if (fileDir.Length > job.InputDirectory.Length)
                            {
                                string relDir = fileDir[job.InputDirectory.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                                if (!string.IsNullOrWhiteSpace(relDir))
                                    outBaseDir = System.IO.Path.Combine(baseOutDir, relDir);
                            }
                        }

                        outBaseDir = System.IO.Path.GetFullPath(outBaseDir);
                        Directory.CreateDirectory(outBaseDir);

                        bool isMultiFrameGroup = group.Files.Any(f => f.IsMultiFrame || f.NumberOfFrames > 1);
                        string safeName = Dispatcher.Invoke(() => ResolveTemplate(template, item));
                        safeName = SanitizePathComponent(safeName, "Serie");

                        if (isMultiFrameGroup)
                        {
                            string seriesOutputDir = ReserveUniqueDirectoryPath(
                                outBaseDir,
                                safeName,
                                reservationGate,
                                reservedOutputPaths);

                            for (int sourceIndex = 0; sourceIndex < group.Files.Count; sourceIndex++)
                            {
                                token.ThrowIfCancellationRequested();
                                LoadedDicom file = group.Files[sourceIndex];
                                string targetSubDir = group.Files.Count == 1
                                    ? seriesOutputDir
                                    : System.IO.Path.Combine(
                                        seriesOutputDir,
                                        $"{sourceIndex + 1:D3}_{SanitizePathComponent(System.IO.Path.GetFileNameWithoutExtension(file.FilePath), "Volume")}");

                                Directory.CreateDirectory(targetSubDir);

                                await Task.Run(
                                    () => splitter.Split(
                                        file,
                                        targetSubDir,
                                        item.PatientName,
                                        item.SeriesName,
                                        anonymize,
                                        token,
                                        anonymizer,
                                        brainlabDti,
                                        brainlabDti),
                                    token);

                                List<string> expectedFiles = GetExpectedSplitOutputPaths(file, targetSubDir, brainlabDti).ToList();
                                if (expectedFiles.Count == 0)
                                    throw new InvalidDataException($"Die Multi-Frame-Datei enthält keine ausgabefähigen Frames: {file.FilePath}");

                                foreach (string expectedFile in expectedFiles.Where(File.Exists))
                                    createdPaths.TryAdd(System.IO.Path.GetFullPath(expectedFile), 0);

                                List<string> missingFiles = expectedFiles.Where(path => !File.Exists(path)).ToList();
                                if (missingFiles.Count > 0)
                                {
                                    throw new IOException(
                                        $"Die Multi-Frame-Aufteilung erzeugte {missingFiles.Count} erwartete Datei(en) nicht vollständig. " +
                                        $"Erster fehlender Pfad: {missingFiles[0]}");
                                }
                            }

                            reportOutputPath = seriesOutputDir;
                            Interlocked.Increment(ref _lastCreated);
                            Dispatcher.Invoke(() => AppendLog($"✓ Multi-Frame-Serie aufgeteilt nach: {System.IO.Path.GetFileName(seriesOutputDir)}"));
                        }
                        else
                        {
                            string outPath = ReserveUniqueFilePath(
                                outBaseDir,
                                $"{safeName}.dcm",
                                reservationGate,
                                reservedOutputPaths);

                            reportOutputPath = outPath;

                            await Task.Run(
                                () => merger.Merge(
                                    group,
                                    outPath,
                                    item.PatientName,
                                    item.SeriesName,
                                    anonymize,
                                    compressDicom,
                                    token,
                                    anonymizer,
                                    brainlabDti,
                                    generateAuditReport),
                                token);

                            if (!File.Exists(outPath))
                                throw new IOException($"Die erwartete Ausgabedatei wurde nicht erzeugt: {outPath}");

                            createdPaths.TryAdd(System.IO.Path.GetFullPath(outPath), 0);

                            if (generateAuditReport)
                            {
                                string auditPath = System.IO.Path.ChangeExtension(outPath, ".brainlab.json");
                                if (File.Exists(auditPath))
                                    createdPaths.TryAdd(System.IO.Path.GetFullPath(auditPath), 0);
                            }

                            Interlocked.Increment(ref _lastCreated);
                            Dispatcher.Invoke(() => AppendLog($"✓ Single-Frame-Serie zusammengeführt nach: {System.IO.Path.GetFileName(outPath)}"));
                        }

                        _batchReport.Add(new BatchReportEntry
                        {
                            PatientName = item.PatientName,
                            SeriesName = item.SeriesName,
                            Modality = item.Modality,
                            FrameCount = item.FrameCount,
                            Success = true,
                            OutputPath = reportOutputPath
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _lastErrors);
                        Dispatcher.Invoke(() => AppendLog($"✗ Fehler bei {item.SeriesName}: {ex.Message}"));
                        _batchReport.Add(new BatchReportEntry
                        {
                            PatientName = item.PatientName,
                            SeriesName = item.SeriesName,
                            Modality = item.Modality,
                            FrameCount = item.FrameCount,
                            Success = false,
                            OutputPath = reportOutputPath,
                            ErrorMessage = ex.Message
                        });
                    }
                    finally
                    {
                        Interlocked.Increment(ref _processedCount);
                    }
                });

            List<string> createdFiles = createdPaths.Keys
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (createdFiles.Count > 0)
            {
                _lastOutputDir = ResolvePostProcessingDirectory(customOutDir, createdFiles);
                int postProcessingErrors = await RunPostProcessingAsync(
                    _lastOutputDir,
                    createdFiles,
                    _lastErrors == 0,
                    ct);

                if (postProcessingErrors > 0)
                    _lastErrors += postProcessingErrors;
            }
            else if (string.IsNullOrWhiteSpace(_lastOutputDir))
            {
                _lastOutputDir = !string.IsNullOrWhiteSpace(customOutDir)
                    ? System.IO.Path.GetFullPath(customOutDir)
                    : _jobs.FirstOrDefault()?.OutputDirectory ?? string.Empty;
            }

            ShowStats(_reviewItems.Count, selectedItems.Count, _lastCreated, _lastErrors);
        }
        finally
        {
            try
            {
                currentProc.PriorityClass = prevPriority;
            }
            catch
            {
            }

            StopDashboard();
        }
    }

    private async Task ProcessMergeAsync(CancellationToken ct, List<ReviewItemViewModel> selectedItems)
    {
        var merger = new FrameMerger(
            msg => Dispatcher.Invoke(() => AppendLog(msg)),
            msg => Dispatcher.Invoke(() => AppendLog($"WARN: {msg}")));

        bool anonymize = CheckAnonymize.IsChecked == true;
        bool compressDicom = CheckCompressDicom.IsChecked == true;
        bool generateAuditReport = CheckBrainlabAuditReport.IsChecked == true;
        string customOutDir = TxtOutputDir.Text.Trim();
        var r = new MergeResult { GroupsFound = selectedItems.Count };


        var anonymizer = anonymize ? new SeriesDeidentifier() : null;
        _batchReport = new System.Collections.Concurrent.ConcurrentBag<BatchReportEntry>();

        StartDashboard(selectedItems.Count, "Zusammenführen...");

        bool useTurbo = CheckTurbo.IsChecked == true;
        int maxDegree = useTurbo ? Environment.ProcessorCount : 1;
        string template = TxtFilenameTemplate.Text;
        if (string.IsNullOrWhiteSpace(template)) template = DefaultTemplate;

        var currentProc = System.Diagnostics.Process.GetCurrentProcess();
        var prevPriority = currentProc.PriorityClass;
        try { currentProc.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal; } catch { }

        try
        {
            await Parallel.ForEachAsync(selectedItems, new ParallelOptions { MaxDegreeOfParallelism = maxDegree, CancellationToken = ct }, async (item, token) =>
            {
                var group = item.OriginalGroup;
                var firstFile = group.Files[0].FilePath;
                var job = _jobs.FirstOrDefault(j => firstFile.StartsWith(j.InputDirectory, StringComparison.OrdinalIgnoreCase));

                string baseOutDir = string.IsNullOrEmpty(customOutDir)
                    ? (job?.OutputDirectory ?? System.IO.Path.GetDirectoryName(firstFile)!)
                    : customOutDir;

                string outBaseDir = baseOutDir;
                if (string.IsNullOrEmpty(customOutDir) && job != null && firstFile.StartsWith(job.InputDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    string fileDir = System.IO.Path.GetDirectoryName(firstFile)!;
                    if (fileDir.Length > job.InputDirectory.Length)
                    {
                        string relDir = fileDir.Substring(job.InputDirectory.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                        outBaseDir = System.IO.Path.Combine(baseOutDir, relDir);
                    }
                }
                Directory.CreateDirectory(outBaseDir);

                string outPath = "";
                try
                {

                    string safeName = Dispatcher.Invoke(() => ResolveTemplate(template, item));
                    outPath = System.IO.Path.Combine(outBaseDir, $"{safeName}.dcm");

                    bool formatBrainlab = Dispatcher.Invoke(() => CheckBrainlabDti.IsChecked == true);
                    await Task.Run(() => merger.Merge(group, outPath, item.PatientName, item.SeriesName, anonymize, compressDicom, token, anonymizer, formatBrainlab, generateAuditReport), token);

                    lock (r) { r.CreatedFiles++; }
                    Dispatcher.Invoke(() => AppendLog($"✓ {System.IO.Path.GetFileName(outPath)}"));
                    _batchReport.Add(new BatchReportEntry
                    {
                        PatientName = item.PatientName,
                        SeriesName = item.SeriesName,
                        Modality = item.Modality,
                        FrameCount = item.FrameCount,
                        Success = true,
                        OutputPath = outPath
                    });
                }
                catch (Exception ex)
                {
                    lock (r) { r.Errors++; }
                    Dispatcher.Invoke(() => AppendLog($"✗ Fehler: {ex.ToString()}"));
                    _batchReport.Add(new BatchReportEntry
                    {
                        PatientName = item.PatientName,
                        SeriesName = item.SeriesName,
                        Modality = item.Modality,
                        FrameCount = item.FrameCount,
                        Success = false,
                        OutputPath = outPath,
                        ErrorMessage = ex.Message
                    });
                }
                finally
                {
                    Interlocked.Increment(ref _processedCount);
                }
            });
        }
        finally
        {
            try { currentProc.PriorityClass = prevPriority; } catch { }
            StopDashboard();
        }

        _lastCreated = r.CreatedFiles;
        _lastErrors = r.Errors;
        if (string.IsNullOrEmpty(_lastOutputDir))
            _lastOutputDir = _jobs.FirstOrDefault()?.OutputDirectory ?? "";


        if (r.CreatedFiles > 0)
        {
            var createdFiles = _batchReport
                .Where(e => e.Success && !string.IsNullOrEmpty(e.OutputPath))
                .Select(e => e.OutputPath!)
                .Distinct()
                .ToList();
            int postProcessingErrors = await RunPostProcessingAsync(_lastOutputDir, CollectAssociatedOutputFiles(createdFiles, generateAuditReport), createdFilesCleanRun: r.Errors == 0, ct);
            r.Errors += postProcessingErrors;
            _lastErrors = r.Errors;
        }

        ShowStats(_reviewItems.Count, selectedItems.Count, r.CreatedFiles, r.Errors);
    }


    private async Task ProcessSplitAsync(CancellationToken ct, List<ReviewItemViewModel> selectedItems)
    {
        var splitter = new FrameSplitter(
            msg => Dispatcher.Invoke(() => AppendLog(msg)),
            msg => Dispatcher.Invoke(() => AppendLog($"WARN: {msg}")));

        bool anonymize = CheckAnonymize.IsChecked == true;
        var anonymizer = anonymize ? new SeriesDeidentifier() : null;
        _batchReport = new System.Collections.Concurrent.ConcurrentBag<BatchReportEntry>();
        string customOutDir = TxtOutputDir.Text.Trim();
        int created = 0;
        int errors = 0;

        StartDashboard(selectedItems.Count, "Aufteilen...");
        bool useTurbo = CheckTurbo.IsChecked == true;
        int maxDegree = useTurbo ? Environment.ProcessorCount : 1;

        var currentProc = System.Diagnostics.Process.GetCurrentProcess();
        var prevPriority = currentProc.PriorityClass;
        try { currentProc.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal; } catch { }

        try
        {
            await Parallel.ForEachAsync(selectedItems, new ParallelOptions { MaxDegreeOfParallelism = maxDegree, CancellationToken = ct }, async (item, token) =>
            {
                var file = item.OriginalGroup.Files[0];
                var firstFilePath = file.FilePath;
                var job = _jobs.FirstOrDefault(j => firstFilePath.StartsWith(j.InputDirectory, StringComparison.OrdinalIgnoreCase));

                string baseOutDir = string.IsNullOrEmpty(customOutDir)
                    ? (job?.OutputDirectory ?? System.IO.Path.GetDirectoryName(firstFilePath)!)
                    : customOutDir;

                string outBaseDir = baseOutDir;
                if (string.IsNullOrEmpty(customOutDir) && job != null && firstFilePath.StartsWith(job.InputDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    string fileDir = System.IO.Path.GetDirectoryName(firstFilePath)!;
                    if (fileDir.Length > job.InputDirectory.Length)
                    {
                        string relDir = fileDir.Substring(job.InputDirectory.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                        outBaseDir = System.IO.Path.Combine(baseOutDir, relDir);
                    }
                }

                string outDir = "";
                try
                {
                    string baseName = System.IO.Path.GetFileNameWithoutExtension(firstFilePath);
                    outDir = System.IO.Path.Combine(outBaseDir, baseName);
                    Directory.CreateDirectory(outDir);

                    bool brainlabDti = Dispatcher.Invoke(() => CheckBrainlabDti.IsChecked == true);
                    await Task.Run(() => splitter.Split(file, outDir, item.PatientName, item.SeriesName, anonymize, token, anonymizer, brainlabDti, brainlabDti), token);
                    Interlocked.Increment(ref created);
                    _batchReport.Add(new BatchReportEntry
                    {
                        PatientName = item.PatientName,
                        SeriesName = item.SeriesName,
                        Modality = item.Modality,
                        FrameCount = item.FrameCount,
                        Success = true,
                        OutputPath = outDir
                    });
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errors);
                    Dispatcher.Invoke(() => AppendLog($"✗ Fehler: {ex.Message}"));
                    _batchReport.Add(new BatchReportEntry
                    {
                        PatientName = item.PatientName,
                        SeriesName = item.SeriesName,
                        Modality = item.Modality,
                        FrameCount = item.FrameCount,
                        Success = false,
                        OutputPath = outDir,
                        ErrorMessage = ex.Message
                    });
                }
                finally
                {
                    Interlocked.Increment(ref _processedCount);
                }
            });
        }
        finally
        {
            try { currentProc.PriorityClass = prevPriority; } catch { }
            StopDashboard();
        }

        _lastCreated = created;
        _lastErrors = errors;
        if (string.IsNullOrEmpty(_lastOutputDir))
            _lastOutputDir = _jobs.FirstOrDefault()?.OutputDirectory ?? "";


        if (created > 0)
        {
            var createdFiles = _batchReport
                .Where(e => e.Success && !string.IsNullOrEmpty(e.OutputPath))
                .Select(e => e.OutputPath!)
                .Distinct()
                .ToList();
            int postProcessingErrors = await RunPostProcessingAsync(_lastOutputDir, CollectAssociatedOutputFiles(createdFiles, false), createdFilesCleanRun: errors == 0, ct);
            errors += postProcessingErrors;
            _lastErrors = errors;
        }

        ShowStats(_reviewItems.Count, selectedItems.Count, created, errors);
    }


    private async Task<int> RunPostProcessingAsync(string outputDir, List<string> createdFiles, bool createdFilesCleanRun, CancellationToken ct)
    {
        int errors = 0;
        outputDir = ResolvePostProcessingDirectory(outputDir, createdFiles);
        Directory.CreateDirectory(outputDir);

        if (CheckExportNiftiDirect.IsChecked == true && createdFiles.Count > 0)
        {
            Dispatcher.Invoke(() =>
            {
                DashboardStatusText.Text = "Erstelle NIfTI-Export (dcm2niix)...";
                DashboardEtaText.Text = "Exportiere...";
            });

            try
            {
                IReadOnlyList<string> niftiFiles = await ExportNiftiDirectAsync(outputDir, ct);
                AddExistingFiles(createdFiles, niftiFiles);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                Dispatcher.Invoke(() => AppendLog($"✗ NIfTI-Export-Fehler: {ex.Message}"));
            }
        }

        if (CheckBatchReport.IsChecked == true)
        {
            Dispatcher.Invoke(() =>
            {
                DashboardStatusText.Text = "Schreibe Batch-Report (CSV/JSON)...";
                DashboardEtaText.Text = "Erstelle...";
            });

            try
            {
                DateTime reportStartUtc = DateTime.UtcNow.AddSeconds(-1);
                var existingReports = Directory
                    .EnumerateFiles(outputDir, "Batch-Report_*.*", SearchOption.TopDirectoryOnly)
                    .Select(System.IO.Path.GetFullPath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                await BatchReportWriter.WriteAsync(outputDir, _batchReport.ToList(), ct);

                List<string> newReports = Directory
                    .EnumerateFiles(outputDir, "Batch-Report_*.*", SearchOption.TopDirectoryOnly)
                    .Select(System.IO.Path.GetFullPath)
                    .Where(path => !existingReports.Contains(path) || File.GetLastWriteTimeUtc(path) >= reportStartUtc)
                    .ToList();

                AddExistingFiles(createdFiles, newReports);
                Dispatcher.Invoke(() => AppendLog($"✓ Batch-Report erstellt ({newReports.Count} Datei(en))"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                Dispatcher.Invoke(() => AppendLog($"✗ Batch-Report-Fehler: {ex.Message}"));
            }
        }

        if (CheckCreateDicomdir.IsChecked == true)
        {
            Dispatcher.Invoke(() =>
            {
                DashboardStatusText.Text = "Generiere DICOMDIR-Index...";
                DashboardEtaText.Text = "Indiziere...";
            });

            try
            {
                int count = await DicomDirWriter.BuildAsync(outputDir, ct);
                string dicomDirPath = System.IO.Path.Combine(outputDir, "DICOMDIR");
                if (count > 0 && File.Exists(dicomDirPath))
                    AddExistingFiles(createdFiles, new[] { dicomDirPath });

                Dispatcher.Invoke(() => AppendLog(
                    count > 0
                        ? $"✓ DICOMDIR erstellt ({count} Datei(en) referenziert)"
                        : "DICOMDIR übersprungen (keine .dcm-Dateien gefunden)"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                Dispatcher.Invoke(() => AppendLog($"✗ DICOMDIR-Fehler: {ex.Message}"));
            }
        }

        if (CheckZipOutput.IsChecked == true && createdFiles.Count > 0)
        {
            if (!createdFilesCleanRun)
            {
                Dispatcher.Invoke(() => AppendLog(
                    "WARN: Mindestens eine Serie war fehlerhaft. Die erfolgreich erzeugten Dateien werden dennoch vollständig archiviert."));
            }

            bool zipSucceeded = await CreateZipArchiveAsync(outputDir, createdFiles, ct);
            if (!zipSucceeded)
                errors++;
        }

        return errors;
    }

    private async Task<bool> CreateZipArchiveAsync(string directoryPath, List<string> createdFiles, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || createdFiles.Count == 0)
        {
            Dispatcher.Invoke(() => AppendLog("✗ ZIP-Erstellung übersprungen: Kein gültiges Zielverzeichnis oder keine erzeugten Dateien vorhanden."));
            return false;
        }

        directoryPath = System.IO.Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(directoryPath);

        List<string> archiveInputs = ExpandArchiveInputs(createdFiles)
            .Where(File.Exists)
            .Select(System.IO.Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (archiveInputs.Count == 0)
        {
            Dispatcher.Invoke(() => AppendLog("✗ ZIP-Erstellung übersprungen: Keine vorhandenen Ausgabedateien gefunden."));
            return false;
        }

        Dispatcher.Invoke(() =>
        {
            DashboardStatusText.Text = $"Packe {archiveInputs.Count} Datei(en) in ZIP Stufe 9 Ultra...";
            DashboardEtaText.Text = "Komprimiere...";
            AppendLog("\n══════════════════════════════════════════════════════════════");
            AppendLog($" ERSTELLE ZIP STUFE 9 ULTRA ({archiveInputs.Count} DATEIEN)");
            AppendLog("══════════════════════════════════════════════════════════════");
        });

        try
        {
            List<string> selectedPatients = _reviewItems
                .Where(i => i.IsSelected)
                .Select(i => i.PatientName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string zipBaseName;
            if (selectedPatients.Count == 1)
            {
                zipBaseName = SanitizePathComponent(selectedPatients[0], "Patient");
            }
            else if (selectedPatients.Count > 1)
            {
                zipBaseName = $"{SanitizePathComponent(selectedPatients[0], "Patient")}_et_al";
            }
            else
            {
                zipBaseName = SanitizePathComponent(System.IO.Path.GetFileName(directoryPath), "Patient");
            }

            string zipPath = System.IO.Path.Combine(directoryPath, $"{zipBaseName}.zip");
            archiveInputs.RemoveAll(path => path.Equals(zipPath, StringComparison.OrdinalIgnoreCase));

            if (archiveInputs.Count == 0)
            {
                Dispatcher.Invoke(() => AppendLog("✗ ZIP-Erstellung abgebrochen: Nach Ausschluss des Zielarchivs verbleiben keine Eingabedateien."));
                return false;
            }

            var szHelper = new SevenZipHelper(
                msg => Dispatcher.Invoke(() => AppendLog(msg)),
                msg => Dispatcher.Invoke(() => AppendLog($"7-Zip WARN: {msg}")));

            bool success = await szHelper.CompressFilesToZipUltraAsync(archiveInputs, zipPath, ct);
            if (!success || !File.Exists(zipPath) || new FileInfo(zipPath).Length == 0)
            {
                string reason = string.IsNullOrWhiteSpace(szHelper.LastError)
                    ? "Das Archiv wurde nicht erzeugt oder ist leer."
                    : szHelper.LastError;
                Dispatcher.Invoke(() => AppendLog($"✗ ZIP-Erstellung fehlgeschlagen: {reason}"));
                return false;
            }

            Dispatcher.Invoke(() =>
            {
                DashboardStatusText.Text = "Bereinige validierte Archivquellen...";
                DashboardEtaText.Text = "Bereinige...";
                AppendLog("Das ZIP-Archiv wurde validiert. Entferne ausschließlich die erfolgreich archivierten Ausgabedateien...");
            });

            int deletedFiles = 0;
            int retainedFiles = 0;
            foreach (string path in archiveInputs)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!File.Exists(path))
                        continue;

                    File.Delete(path);
                    deletedFiles++;
                }
                catch (Exception ex)
                {
                    retainedFiles++;
                    Dispatcher.Invoke(() => AppendLog($"WARN: Archivierte Datei konnte nicht entfernt werden: {path} ({ex.Message})"));
                }
            }

            DeleteEmptyOutputDirectories(directoryPath, archiveInputs);
            _lastOutputDir = directoryPath;

            Dispatcher.Invoke(() =>
            {
                AppendLog(
                    $"✓ ZIP erfolgreich: {archiveInputs.Count} Datei(en) in {System.IO.Path.GetFileName(zipPath)} archiviert, " +
                    $"{deletedFiles} Quelldatei(en) entfernt, {retainedFiles} Quelldatei(en) beibehalten.");
                DashboardStatusText.Text = "ZIP-Erstellung abgeschlossen";
                DashboardEtaText.Text = "Fertig";
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AppendLog($"✗ ZIP-Fehler: {ex.Message}"));
            return false;
        }
    }

    private async Task<IReadOnlyList<string>> ExportNiftiDirectAsync(string directoryPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Das NIfTI-Quellverzeichnis wurde nicht gefunden: {directoryPath}");

        Dispatcher.Invoke(() => AppendLog("Führe NIfTI-Export als Nachverarbeitung aus..."));

        var converter = new NiftiConverter(
            msg => Dispatcher.Invoke(() => AppendLog(msg)),
            msg => Dispatcher.Invoke(() => AppendLog($"NIFTI WARN: {msg}")));

        var settings = new NiftiSettings
        {
            CompressGz = CheckCompressGz.IsChecked == true,
            CreateBidsJson = CheckBidsJson.IsChecked == true,
            AnonymizeBids = CheckAnonymize.IsChecked == true,
            FilenameFormat = string.IsNullOrWhiteSpace(TxtNiftiFormat.Text) ? "%f" : TxtNiftiFormat.Text.Trim()
        };

        string niftiOutDir = CreateUniqueDirectoryPath(directoryPath, "Nifti_Export");
        Directory.CreateDirectory(niftiOutDir);

        try
        {
            int code = await converter.ConvertAsync(directoryPath, niftiOutDir, settings, ct);
            if (code != 0)
                throw new InvalidOperationException($"dcm2niix wurde mit Exit-Code {code} beendet.");

            List<string> generatedFiles = Directory
                .EnumerateFiles(niftiOutDir, "*", SearchOption.AllDirectories)
                .Select(System.IO.Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (generatedFiles.Count == 0)
                throw new InvalidOperationException("dcm2niix meldete Erfolg, erzeugte jedoch keine NIfTI-Ausgabedateien.");

            Dispatcher.Invoke(() => AppendLog(
                $"✓ NIfTI-Export erfolgreich: {generatedFiles.Count} Datei(en) unter {System.IO.Path.GetFileName(niftiOutDir)}"));

            return generatedFiles;
        }
        catch
        {
            try
            {
                if (Directory.Exists(niftiOutDir) && !Directory.EnumerateFileSystemEntries(niftiOutDir).Any())
                    Directory.Delete(niftiOutDir);
            }
            catch
            {
            }

            throw;
        }
    }

    private static string ReserveUniqueDirectoryPath(
        string parentDirectory,
        string preferredName,
        object reservationGate,
        ISet<string> reservedPaths)
    {
        parentDirectory = System.IO.Path.GetFullPath(parentDirectory);
        string safeName = SanitizePathComponent(preferredName, "Serie");

        lock (reservationGate)
        {
            for (int suffix = 0; ; suffix++)
            {
                string candidateName = suffix == 0 ? safeName : $"{safeName}_{suffix + 1}";
                string candidatePath = System.IO.Path.Combine(parentDirectory, candidateName);

                if (reservedPaths.Contains(candidatePath) || Directory.Exists(candidatePath) || File.Exists(candidatePath))
                    continue;

                reservedPaths.Add(candidatePath);
                Directory.CreateDirectory(candidatePath);
                return candidatePath;
            }
        }
    }

    private static string ReserveUniqueFilePath(
        string parentDirectory,
        string preferredFileName,
        object reservationGate,
        ISet<string> reservedPaths)
    {
        parentDirectory = System.IO.Path.GetFullPath(parentDirectory);
        string extension = System.IO.Path.GetExtension(preferredFileName);
        string baseName = SanitizePathComponent(System.IO.Path.GetFileNameWithoutExtension(preferredFileName), "Serie");

        lock (reservationGate)
        {
            for (int suffix = 0; ; suffix++)
            {
                string candidateName = suffix == 0
                    ? $"{baseName}{extension}"
                    : $"{baseName}_{suffix + 1}{extension}";
                string candidatePath = System.IO.Path.Combine(parentDirectory, candidateName);

                if (reservedPaths.Contains(candidatePath) || File.Exists(candidatePath) || Directory.Exists(candidatePath))
                    continue;

                reservedPaths.Add(candidatePath);
                return candidatePath;
            }
        }
    }

    private static string CreateUniqueDirectoryPath(string parentDirectory, string preferredName)
    {
        parentDirectory = System.IO.Path.GetFullPath(parentDirectory);
        string safeName = SanitizePathComponent(preferredName, "Ausgabe");

        for (int suffix = 0; ; suffix++)
        {
            string candidateName = suffix == 0 ? safeName : $"{safeName}_{suffix + 1}";
            string candidatePath = System.IO.Path.Combine(parentDirectory, candidateName);
            if (!Directory.Exists(candidatePath) && !File.Exists(candidatePath))
                return candidatePath;
        }
    }

    private static string SanitizePathComponent(string? value, string fallback)
    {
        string source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        char[] invalidCharacters = System.IO.Path.GetInvalidFileNameChars();
        var result = new System.Text.StringBuilder(source.Length);
        bool previousUnderscore = false;

        foreach (char character in source)
        {
            char outputCharacter = invalidCharacters.Contains(character) || char.IsControl(character) ? '_' : character;
            if (outputCharacter == '_')
            {
                if (previousUnderscore)
                    continue;
                previousUnderscore = true;
            }
            else
            {
                previousUnderscore = false;
            }

            result.Append(outputCharacter);
        }

        string sanitized = result.ToString().Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
            sanitized = fallback;

        return sanitized.Length <= 160 ? sanitized : sanitized[..160].TrimEnd('.', ' ');
    }

    private static IEnumerable<string> GetExpectedSplitOutputPaths(LoadedDicom file, string outputDirectory, bool splitByBValue)
    {
        for (int frameIndex = 0; frameIndex < file.NumberOfFrames; frameIndex++)
        {
            string targetDirectory = outputDirectory;
            if (splitByBValue)
            {
                int? frameBValue = DiffusionBValueHelper.ExtractFrameBValue(file.Dataset, frameIndex);
                if (frameBValue.HasValue)
                    targetDirectory = System.IO.Path.Combine(outputDirectory, $"b{frameBValue.Value}");
            }

            yield return System.IO.Path.Combine(targetDirectory, $"Frame_{frameIndex + 1:D4}.dcm");
        }
    }

    private static string ResolvePostProcessingDirectory(string preferredDirectory, IReadOnlyCollection<string> createdFiles)
    {
        if (!string.IsNullOrWhiteSpace(preferredDirectory))
        {
            string preferredPath = System.IO.Path.GetFullPath(preferredDirectory);
            Directory.CreateDirectory(preferredPath);
            return preferredPath;
        }

        List<string> sourceDirectories = createdFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => File.Exists(path)
                ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))
                : Directory.Exists(path)
                    ? System.IO.Path.GetFullPath(path)
                    : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sourceDirectories.Count == 0)
            throw new InvalidOperationException("Das gemeinsame Ausgabeverzeichnis konnte nicht bestimmt werden.");

        string commonDirectory = sourceDirectories[0];
        string commonRoot = System.IO.Path.GetPathRoot(commonDirectory) ?? string.Empty;

        foreach (string directory in sourceDirectories.Skip(1))
        {
            string root = System.IO.Path.GetPathRoot(directory) ?? string.Empty;
            if (!root.Equals(commonRoot, StringComparison.OrdinalIgnoreCase))
                return sourceDirectories[0];

            while (!IsPathWithinOrEqual(commonDirectory, directory))
            {
                DirectoryInfo? parent = Directory.GetParent(commonDirectory);
                if (parent == null)
                    return sourceDirectories[0];
                commonDirectory = parent.FullName;
            }
        }

        Directory.CreateDirectory(commonDirectory);
        return commonDirectory;
    }

    private static bool IsPathWithinOrEqual(string baseDirectory, string candidatePath)
    {
        string relativePath = System.IO.Path.GetRelativePath(baseDirectory, candidatePath);
        return relativePath == "." ||
               (!System.IO.Path.IsPathRooted(relativePath) &&
                relativePath != ".." &&
                !relativePath.StartsWith($"..{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !relativePath.StartsWith($"..{System.IO.Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static void AddExistingFiles(ICollection<string> destination, IEnumerable<string> paths)
    {
        var existing = destination.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            string fullPath = System.IO.Path.GetFullPath(path);
            if (existing.Add(fullPath))
                destination.Add(fullPath);
        }
    }

    private static List<string> CollectAssociatedOutputFiles(IEnumerable<string> paths, bool includeAuditReports)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (File.Exists(path))
            {
                string fullPath = System.IO.Path.GetFullPath(path);
                result.Add(fullPath);

                if (includeAuditReports)
                {
                    string auditPath = System.IO.Path.ChangeExtension(fullPath, ".brainlab.json");
                    if (File.Exists(auditPath))
                        result.Add(System.IO.Path.GetFullPath(auditPath));
                }
            }
            else if (Directory.Exists(path))
            {
                foreach (string filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    result.Add(System.IO.Path.GetFullPath(filePath));
            }
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> ExpandArchiveInputs(IEnumerable<string> paths)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (File.Exists(path))
            {
                string fullPath = System.IO.Path.GetFullPath(path);
                if (yielded.Add(fullPath))
                    yield return fullPath;
            }
            else if (Directory.Exists(path))
            {
                foreach (string filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    string fullPath = System.IO.Path.GetFullPath(filePath);
                    if (yielded.Add(fullPath))
                        yield return fullPath;
                }
            }
        }
    }

    private static void DeleteEmptyOutputDirectories(string outputRoot, IEnumerable<string> archivedFiles)
    {
        outputRoot = System.IO.Path.GetFullPath(outputRoot);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string? initialDirectory in archivedFiles.Select(System.IO.Path.GetDirectoryName))
        {
            if (string.IsNullOrWhiteSpace(initialDirectory))
                continue;

            string currentDirectory = System.IO.Path.GetFullPath(initialDirectory);
            while (!currentDirectory.Equals(outputRoot, StringComparison.OrdinalIgnoreCase) &&
                   IsPathWithinOrEqual(outputRoot, currentDirectory))
            {
                directories.Add(currentDirectory);
                DirectoryInfo? parent = Directory.GetParent(currentDirectory);
                if (parent == null)
                    break;
                currentDirectory = parent.FullName;
            }
        }

        foreach (string directory in directories.OrderByDescending(path => path.Length))
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch
            {
            }
        }
    }

    private async Task ProcessNiftiAsync(CancellationToken ct)
    {
        var converter = new NiftiConverter(
            msg => Dispatcher.Invoke(() => AppendLog(msg)),
            msg => Dispatcher.Invoke(() => AppendLog($"NIFTI WARN: {msg}")));

        var settings = new NiftiSettings
        {
            CompressGz = CheckCompressGz.IsChecked == true,
            CreateBidsJson = CheckBidsJson.IsChecked == true,
            AnonymizeBids = CheckAnonymize.IsChecked == true,
            FilenameFormat = TxtNiftiFormat.Text
        };

        string customOutDir = TxtOutputDir.Text.Trim();
        int exitCode = 0;
        int totalProcessed = 0;

        await Task.Run(async () =>
        {
            foreach (var job in _jobs)
            {
                ct.ThrowIfCancellationRequested();

                var jobItems = _reviewItems.Where(i => i.OriginalGroup.Files[0].FilePath.StartsWith(job.InputDirectory, StringComparison.OrdinalIgnoreCase)).ToList();
                var selectedJobItems = jobItems.Where(i => i.IsSelected).ToList();

                if (selectedJobItems.Count == 0) continue;

                Dispatcher.Invoke(() =>
                {
                    DashboardStatusText.Text = "Konvertiere zu NIfTI…";
                    DashboardEtaText.Text = $"Ordner: {System.IO.Path.GetFileName(job.InputDirectory)}";
                });

                string baseTargetOut = string.IsNullOrEmpty(customOutDir) ? job.OutputDirectory : customOutDir;

                var groupedByRelDir = selectedJobItems.GroupBy(item =>
                {
                    string firstFile = item.OriginalGroup.Files[0].FilePath;
                    string fileDir = System.IO.Path.GetDirectoryName(firstFile)!;
                    if (fileDir.Length > job.InputDirectory.Length)
                    {
                        return fileDir.Substring(job.InputDirectory.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                    }
                    return "";
                }).ToList();

                foreach (var group in groupedByRelDir)
                {
                    string targetOut = baseTargetOut;
                    if (!string.IsNullOrEmpty(group.Key))
                    {
                        targetOut = System.IO.Path.Combine(targetOut, group.Key);
                    }
                    Directory.CreateDirectory(targetOut);

                    string processDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NewDicomMerger", "TempNifti", Guid.NewGuid().ToString());
                    Directory.CreateDirectory(processDir);
                    _tempZipFolders.Add(processDir);

                    foreach (var item in group)
                    {
                        foreach (var file in item.OriginalGroup.Files)
                        {
                            try { File.Copy(file.FilePath, System.IO.Path.Combine(processDir, System.IO.Path.GetFileName(file.FilePath)), true); }
                            catch { }
                        }
                    }

                    int code = await converter.ConvertAsync(processDir, targetOut, settings, ct);
                    if (code != 0) exitCode = code;
                    totalProcessed += group.Count();
                }
            }
        }, ct);

        Dispatcher.Invoke(() => SetProgress(100));

        _lastCreated = exitCode == 0 ? totalProcessed : 0;
        _lastErrors = exitCode == 0 ? 0 : 1;
        if (string.IsNullOrEmpty(_lastOutputDir))
            _lastOutputDir = _jobs.FirstOrDefault()?.OutputDirectory ?? "";

        if (exitCode == 0)
            ShowStats(_reviewItems.Count, totalProcessed, totalProcessed, 0);
        else
            ShowStats(_reviewItems.Count, totalProcessed, 0, 1);
    }


    private void ShowCompletionPanel()
    {
        CompletionCreatedText.Text = _lastCreated.ToString();
        CompletionErrorsText.Text = _lastErrors.ToString();
        CompletionTimeText.Text = FormatDuration(_lastElapsedSeconds);
        CompletionSeriesText.Text = _reviewItems.Count(i => i.IsSelected).ToString();


        CompletionRingProgress.StrokeDashArray = new DoubleCollection { 0, 100 };

        AnimateIn(CompletionPanel, 30, 400);


        var ringAnim = new DoubleAnimation(0, 78.5, TimeSpan.FromMilliseconds(800))
        { BeginTime = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };


        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var start = DateTime.Now;
        double targetDash = 78.5;
        timer.Tick += (_, _) =>
        {
            double elapsed = (DateTime.Now - start).TotalMilliseconds - 200;
            if (elapsed < 0) return;
            double progress = Math.Min(1.0, elapsed / 800.0);

            progress = 1 - Math.Pow(1 - progress, 3);
            double dashVal = progress * targetDash;
            CompletionRingProgress.StrokeDashArray = new DoubleCollection { dashVal, 100 };

            if (progress >= 1.0)
            {
                timer.Stop();

                CompletionCheckmark.Visibility = Visibility.Visible;
                CompletionCheckmark.RenderTransform = new ScaleTransform(0, 0, 0.5, 0.5);
                CompletionCheckmark.RenderTransformOrigin = new Point(0.5, 0.5);
                var scaleX = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
                { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 } };
                var scaleY = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
                { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 } };

                var sb = new Storyboard();
                Storyboard.SetTarget(scaleX, CompletionCheckmark);
                Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
                Storyboard.SetTarget(scaleY, CompletionCheckmark);
                Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
                sb.Children.Add(scaleX);
                sb.Children.Add(scaleY);
                sb.Begin();
            }
        };
        timer.Start();
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds < 60) return $"{seconds:F1}s";
        int min = (int)(seconds / 60);
        double sec = seconds % 60;
        return $"{min}m {sec:F0}s";
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastOutputDir) && Directory.Exists(_lastOutputDir))
        {
            Process.Start("explorer.exe", _lastOutputDir);
        }
    }

    private void CompletionReset_Click(object sender, RoutedEventArgs e)
    {
        AnimateOut(CompletionPanel, 20, 250, PerformReset);
    }

    private void ShowLogFromCompletion_Click(object sender, RoutedEventArgs e)
    {
        AnimateOut(CompletionPanel, 20, 250, () =>
        {
            DropZonePanel.Visibility = Visibility.Collapsed;
            ReviewPanel.Visibility = Visibility.Collapsed;
            LogPanel.Visibility = Visibility.Visible;
            CopyButton.Visibility = Visibility.Visible;
        });
    }

    private void CloseCompletionPanel_Click(object sender, RoutedEventArgs e)
    {
        AnimateOut(CompletionPanel, 20, 250, () =>
        {
            DropZonePanel.Visibility = Visibility.Collapsed;
            ReviewPanel.Visibility = Visibility.Collapsed;
            LogPanel.Visibility = Visibility.Visible;
            CopyButton.Visibility = Visibility.Visible;
        });
    }


    private void StartDashboard(int totalItems, string statusPrefix)
    {
        _totalItems = totalItems;
        _processedCount = 0;
        _startTime = DateTime.Now;
        DashboardStatusText.Text = statusPrefix;
        DashboardSpeedText.Text = " (0 Serien/s)";
        DashboardEtaText.Text = "Restzeit: Berechne...";
        DashboardProgressFill.Width = 0;

        DashboardPanel.Visibility = Visibility.Visible;

        _dashboardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _dashboardTimer.Tick += (s, e) => UpdateDashboard();
        _dashboardTimer.Start();
    }

    private void UpdateDashboard()
    {
        int count = Interlocked.CompareExchange(ref _processedCount, 0, 0);
        double elapsedSeconds = (DateTime.Now - _startTime).TotalSeconds;
        double speed = elapsedSeconds > 0 ? count / elapsedSeconds : 0;

        DashboardSpeedText.Text = $" ({speed:F1} Serien/s)";

        if (count > 0 && count < _totalItems)
        {
            double remainingSeconds = (_totalItems - count) / speed;
            DashboardEtaText.Text = $"Restzeit: {TimeSpan.FromSeconds(remainingSeconds):mm\\:ss}";
        }
        else if (count == _totalItems)
        {
            DashboardEtaText.Text = "Abgeschlossen";
        }

        double percent = _totalItems > 0 ? (double)count / _totalItems : 0;
        double maxW = DashboardPanel.ActualWidth > 0 ? DashboardPanel.ActualWidth : 400;
        double w = percent * maxW;
        if (w < 0) w = 0;

        var anim = new DoubleAnimation(w, TimeSpan.FromMilliseconds(250))
        { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
        DashboardProgressFill.BeginAnimation(WidthProperty, anim);
    }

    private void StopDashboard()
    {
        _dashboardTimer?.Stop();
        DashboardPanel.Visibility = Visibility.Collapsed;
    }


    private void ShowToast(string message)
    {
        Dispatcher.Invoke(() =>
        {
            ToastText.Text = message;
            ToastPanel.Opacity = 0;

            var ta = new ThicknessAnimation(new Thickness(0, 0, 0, 20), TimeSpan.FromSeconds(0.4)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var oa = new DoubleAnimation(1, TimeSpan.FromSeconds(0.4));

            var sb = new Storyboard();
            sb.Children.Add(ta); sb.Children.Add(oa);
            Storyboard.SetTarget(ta, ToastPanel); Storyboard.SetTargetProperty(ta, new PropertyPath("Margin"));
            Storyboard.SetTarget(oa, ToastPanel); Storyboard.SetTargetProperty(oa, new PropertyPath("Opacity"));

            sb.Completed += async (s, e) =>
            {
                await Task.Delay(3500);
                Dispatcher.Invoke(() =>
                {
                    var taOut = new ThicknessAnimation(new Thickness(0, 0, 0, -80), TimeSpan.FromSeconds(0.4)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    var oaOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.4));
                    var sbOut = new Storyboard();
                    sbOut.Children.Add(taOut); sbOut.Children.Add(oaOut);
                    Storyboard.SetTarget(taOut, ToastPanel); Storyboard.SetTargetProperty(taOut, new PropertyPath("Margin"));
                    Storyboard.SetTarget(oaOut, ToastPanel); Storyboard.SetTargetProperty(oaOut, new PropertyPath("Opacity"));
                    sbOut.Begin();
                });
            };
            sb.Begin();
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            CancelButton.IsEnabled = false;
            CancelButton.Content = "Abbrechen…";
            _cts.Cancel();
        }
    }


    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_reviewItems.Count > 0)
        {
            var result = MessageBox.Show(
                "Es sind noch geladene bzw. nicht verarbeitete Serien vorhanden.\n\nWirklich zurücksetzen und alle Änderungen verwerfen?",
                "Zurücksetzen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;
        }

        PerformReset();
    }

    private void PerformReset()
    {
        LogTextBox.Text = string.Empty;


        DropZonePanel.Visibility = Visibility.Visible;
        DropZonePanel.Opacity = 1;
        if (DropZonePanel.RenderTransform is TranslateTransform tt) tt.Y = 0;

        LogPanel.Visibility = Visibility.Collapsed;
        ReviewPanel.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        DashboardPanel.Visibility = Visibility.Collapsed;
        StatsRow.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        StartProcessButton.Visibility = Visibility.Collapsed;
        CompletionPanel.Visibility = Visibility.Collapsed;

        _reviewItems.Clear();
        _jobs.Clear();
        _wlInitialized = false;
        _volumeCache.Clear();


        _currentVolume = null;
        _currentPreviewFiles = null;
        _isMprRendering = 0;
        _renderRequested = false;

        if (PreviewImage != null) PreviewImage.Source = null;
        if (PreviewEmptyText != null) PreviewEmptyText.Visibility = Visibility.Visible;
        if (PreviewLoadingText != null) PreviewLoadingText.Visibility = Visibility.Collapsed;
        if (PreviewControls != null) PreviewControls.Visibility = Visibility.Collapsed;
        if (SliceIndexText != null) SliceIndexText.Text = "2D-ANSICHT";

        CleanupTempFolders();
    }

    private void CleanupTempFolders()
    {
        foreach (var dir in _tempZipFolders)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }
        _tempZipFolders.Clear();
    }

    private void SetProgress(double percent)
    {
        double maxW = DashboardPanel.ActualWidth > 0 ? DashboardPanel.ActualWidth : 400;
        double w = (percent / 100.0) * maxW;
        if (w < 0) w = 0;

        var anim = new DoubleAnimation(w, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
        DashboardProgressFill.BeginAnimation(WidthProperty, anim);
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText(message + Environment.NewLine);
        LogTextBox.ScrollToEnd();
        try
        {
            File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "process_log.txt"), message + Environment.NewLine);
        }
        catch { }
    }

    private void ShowStats(int loaded, int series, int created, int errors)
    {
        StatLoaded.Text = loaded.ToString();
        StatSeries.Text = series.ToString();
        StatCreated.Text = created.ToString();
        StatErrors.Text = errors.ToString();

        AnimateStaggeredStatCards();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(LogTextBox.Text)) return;
        Clipboard.SetText(LogTextBox.Text);
        CopyButton.Content = "✓ Kopiert!";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { timer.Stop(); CopyButton.Content = "📋 Log kopieren"; };
        timer.Start();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
    }

    private void ModeSlider_SizeChanged(object sender, SizeChangedEventArgs e)
    {
    }

    private static bool IsLikelyDicom(string path)
    {
        if (!File.Exists(path)) return false;
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".dcm" || ext == ".dicom" || ext == ".dic" || ext == ".ima") return true;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 132) return false;
            fs.Seek(128, SeekOrigin.Begin);
            var magic = new byte[4];
            fs.Read(magic, 0, 4);
            return magic[0] == 'D' && magic[1] == 'I' && magic[2] == 'C' && magic[3] == 'M';
        }
        catch { return false; }
    }


    private class DicomVolume
    {
        public float[] VoxelData;
        public int Width;
        public int Height;
        public int Depth;
        public double PixelSpacingX = 1.0;
        public double PixelSpacingY = 1.0;
        public double SliceSpacing = 1.0;
        public float MinVoxel = 0;
        public float MaxVoxel = 255;

        public DicomVolume(int width, int height, int depth)
        {
            Width = width;
            Height = height;
            Depth = depth;
            VoxelData = new float[depth * height * width];
        }

        public float GetVoxel(int z, int y, int x)
        {
            if (z < 0 || z >= Depth || y < 0 || y >= Height || x < 0 || x >= Width) return 0;
            return VoxelData[z * (Height * Width) + y * Width + x];
        }

        public void SetVoxel(int z, int y, int x, float value)
        {
            if (z < 0 || z >= Depth || y < 0 || y >= Height || x < 0 || x >= Width) return;
            VoxelData[z * (Height * Width) + y * Width + x] = value;
        }
    }

    private System.Windows.Media.Imaging.WriteableBitmap Render2DSlice(DicomVolume vol, int sliceIndex, double windowWidth, double windowCenter)
    {
        if (vol == null || vol.Depth <= 0 || vol.Width <= 0 || vol.Height <= 0)
            return new System.Windows.Media.Imaging.WriteableBitmap(1, 1, 96, 96, System.Windows.Media.PixelFormats.Gray8, null);


        sliceIndex = Math.Clamp(sliceIndex, 0, vol.Depth - 1);

        int w = vol.Width;
        int h = vol.Height;

        if (w <= 0 || h <= 0) return new System.Windows.Media.Imaging.WriteableBitmap(1, 1, 96, 96, System.Windows.Media.PixelFormats.Gray8, null);

        var bmp = new System.Windows.Media.Imaging.WriteableBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Gray8, null);
        bmp.Lock();

        unsafe
        {
            byte* pBackBuffer = (byte*)bmp.BackBuffer;
            int stride = bmp.BackBufferStride;

            double minVal = windowCenter - windowWidth / 2.0;
            double maxVal = windowCenter + windowWidth / 2.0;
            double range = maxVal - minVal;
            if (range <= 0) range = 1.0;


            double invRange = 255.0 / range;

            int volWidth = vol.Width;
            int volHeight = vol.Height;
            int sliceArea = volHeight * volWidth;
            float[] voxelData = vol.VoxelData;

            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                byte* pRow = pBackBuffer + y * stride;


                int rowBase = sliceIndex * sliceArea + y * volWidth;
                int vecSize = System.Numerics.Vector<float>.Count;
                var vMin = new System.Numerics.Vector<float>((float)minVal);
                var vInvRange = new System.Numerics.Vector<float>((float)invRange);
                var vZero = System.Numerics.Vector<float>.Zero;
                var v255 = new System.Numerics.Vector<float>(255f);

                int x = 0;
                for (; x <= w - vecSize; x += vecSize)
                {
                    var voxelVec = new System.Numerics.Vector<float>(voxelData, rowBase + x);
                    var intensityVec = System.Numerics.Vector.Max(vZero,
                        System.Numerics.Vector.Min(v255, (voxelVec - vMin) * vInvRange));
                    for (int k = 0; k < vecSize; k++)
                        pRow[x + k] = (byte)intensityVec[k];
                }
                for (; x < w; x++)
                {
                    double intensity = (voxelData[rowBase + x] - minVal) * invRange;
                    if (intensity < 0) intensity = 0;
                    else if (intensity > 255) intensity = 255;
                    pRow[x] = (byte)intensity;
                }
            });
        }

        bmp.AddDirtyRect(new Int32Rect(0, 0, w, h));
        bmp.Unlock();
        bmp.Freeze();
        return bmp;
    }

    private static bool IsLocalizerSeries(SeriesGroup group)
    {
        if (group.Files.Count == 0) return false;

        string[] keywords = { "loc", "scout", "survey", "plane", "localizer", "topogram", "phoenix", "overview" };

        foreach (var file in group.Files)
        {
            var ds = file.File.Dataset;
            string desc = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, "").ToLowerInvariant();
            string proto = ds.GetSingleValueOrDefault(DicomTag.ProtocolName, "").ToLowerInvariant();

            if (keywords.Any(k => desc.Contains(k) || proto.Contains(k)))
                return true;

            if (ds.Contains(DicomTag.ImageType))
            {
                try
                {
                    var imageTypes = ds.GetValues<string>(DicomTag.ImageType);
                    if (imageTypes.Any(t => t.Equals("LOCALIZER", StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
                catch { }
            }
        }

        return false;
    }
}