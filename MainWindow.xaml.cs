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
    // ──── Theme brush DependencyProperties ────
    // Bugfix (live crash on 2nd+ theme toggle): a Style.Setter with Value="{DynamicResource X}"
    // permanently "poisons" that resource key the moment the Style is sealed (which happens as
    // soon as any control using it is realized in the visual tree) — every future value assigned
    // to that key, even a brand-new Brush, gets frozen by WPF and throws on the next .Color
    // mutation or BeginAnimation call. Reproduced and confirmed in an isolated repro outside this
    // app before writing this fix. A Binding is NOT subject to this: it re-evaluates through the
    // normal data-binding engine rather than the Freezable/resource system, so it stays mutable
    // indefinitely. These DependencyProperties back exactly the Style.Setters that need to react
    // to a theme change (buttons, checkbox, radio button, stat cards, DataGrid column headers).
    // Direct (non-Setter) XAML attribute usages of {DynamicResource ...} elsewhere in this window
    // are NOT affected by this bug (proven safe in the same isolated repro) and are left as-is.
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

    // ──── Window/Level state (Feature 2) ────
    private LoadedDicom[]? _currentPreviewFiles;
    private double _windowWidth = 0;
    private double _windowCenter = 0;
    private bool _wlInitialized;

    // ──── Tag Viewer state (Feature 5) ────
    private List<DicomTagEntry> _allTagEntries = new();

    // ──── Volume LRU cache (Feature 16) ────
    // Capacity 3: switching between the 3 most recently viewed series is instant;
    // each entry is a full float voxel grid (width×height×depth×4 bytes), so an
    // unbounded cache would risk exhausting memory on large CT/MR series.
    private readonly LruCache<string, DicomVolume> _volumeCache = new(3);

    // ──── 2D Preview state ────
    private DicomVolume? _currentVolume;
    private int _currentAxialFrame = 0;
    private int _isMprRendering = 0;
    private bool _renderRequested = false;

    // ──── Completion state (Feature 3) ────
    private string _lastOutputDir = string.Empty;
    private int _lastCreated;
    private int _lastErrors;
    private double _lastElapsedSeconds;

    // ──── Template placeholders (Feature 12) ────
    private static readonly string DefaultTemplate = "{Patient}-{Serie}-{Datum}";

    // ──── Batch report (Feature 13) ────
    private System.Collections.Concurrent.ConcurrentBag<BatchReportEntry> _batchReport = new();


    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            
            // Dark Mode für Titelleiste (Windows 11 DWM attribute 20)
            int trueVal = 1;
            DwmSetWindowAttribute(hwnd, 20, ref trueVal, sizeof(int));
            
            // Mica-Effekt (Windows 11 backdrop attribute 38, value 2 = Mica)
            int backdropType = 2;
            DwmSetWindowAttribute(hwnd, 38, ref backdropType, sizeof(int));
        }
        catch { }
    }

    /// <summary>
    /// Bugfix found during review: the dashboard timer was never stopped on window close.
    /// </summary>
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

    // ══════════════════════════════════════════════════════════════
    //  FEATURE 7: Keyboard Shortcuts
    // ══════════════════════════════════════════════════════════════
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // F1 → Toggle shortcut overlay
        if (e.Key == Key.F1)
        {
            ToggleShortcutOverlay();
            e.Handled = true;
            return;
        }

        // Escape → Close any overlay, or cancel processing
        if (e.Key == Key.Escape)
        {
            if (ShortcutOverlay.Visibility == Visibility.Visible)
            { AnimateOut(ShortcutOverlay); e.Handled = true; return; }
            if (TagViewerPanel.Visibility == Visibility.Visible)
            { AnimateOut(TagViewerPanel); e.Handled = true; return; }
            if (_isProcessing && _cts != null && !_cts.IsCancellationRequested)
            { CancelButton_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
        }

        // Ctrl shortcuts
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

        // F5 → Reset
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

    // ══════════════════════════════════════════════════════════════
    //  FEATURE 1: Animated State Transitions
    // ══════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════
    //  Custom Window Chrome
    // ══════════════════════════════════════════════════════════════
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaxBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    // ══════════════════════════════════════════════════════════════
    //  Drag & Drop
    // ══════════════════════════════════════════════════════════════
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

    // ══════════════════════════════════════════════════════════════
    //  State 1: Scan & Review  (with Feature 1 transitions)
    // ══════════════════════════════════════════════════════════════
    private async Task ScanAndReviewAsync(List<string> droppedPaths)
    {
        _isProcessing = true;
        _reviewItems.Clear();
        _jobs.Clear();
        CleanupTempFolders();

        LogTextBox.Text = string.Empty;

        // Animate out drop zone, animate in log
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

                    // Scanning/loading each file is I/O + header-parsing work that is
                    // independent per file, so it parallelizes across cores. UI progress
                    // is batched (not one Dispatcher.Invoke per file) to avoid saturating
                    // the UI thread with marshaling calls for large series.
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

                bool splitByBValue = Dispatcher.Invoke(() => CheckSplitBValues.IsChecked == true);

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
                // Animated transition: Log → Review
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

        // 1) Multiple studies per patient -> add _VU suffix to earlier studies
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

                // Earlier (older) studies get the _VU suffix
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

            // 2) Disambiguate dynamic / duplicate series names within the same study (_1, _2, _3, ...) chronologically
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

    private async void CheckSplitBValues_Click(object sender, RoutedEventArgs e)
    {
        if (_allLoadedFiles == null || _allLoadedFiles.Count == 0) return;

        bool splitByBValue = CheckSplitBValues.IsChecked == true;
        var result = new MergeResult();
        var scanner = new DicomScanner(msg => Dispatcher.Invoke(() => AppendLog(msg)));
        var groups = await Task.Run(() => scanner.GroupAndSort(_allLoadedFiles, result, splitByBValue));

        PopulateReviewGrid(groups);
    }

    private async void CheckBrainlabFormat_Click(object sender, RoutedEventArgs e)
    {
        if (CheckBrainlabFormat.IsChecked == true)
        {
            CheckSplitBValues.IsChecked = true;
        }

        if (_allLoadedFiles == null || _allLoadedFiles.Count == 0) return;

        bool splitByBValue = CheckSplitBValues.IsChecked == true;
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


    // ══════════════════════════════════════════════════════════════
    //  FEATURE 2: MPR Volume Preview
    // ══════════════════════════════════════════════════════════════

    private async void ReviewGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReviewGrid.SelectedItem is ReviewItemViewModel item)
        {
            _currentPreviewFiles = item.OriginalGroup.Files.ToArray();
            
            // Clear current volume immediately if not cached so stale volume is not rendered
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

    /// <summary>
    /// Loads (or reuses from cache) the 3D voxel volume for a series and sets up the
    /// MPR UI. Feature 16: <see cref="_volumeCache"/> is a real LRU cache keyed by
    /// SeriesInstanceUID — switching back to a previously viewed series returns
    /// instantly instead of re-reading and re-decoding every slice from disk again
    /// (the cache field used to exist but was never actually populated/read).
    /// </summary>
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

        // Bugfix found during review: nothing used to stop the user from dragging a
        // slider mid-load — the handler would then render whatever stale volume/
        // geometry was still assigned. Disabling the controls for the load's duration
        // closes that window.
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

    /// <summary>
    /// Reads every slice of the series and assembles the rescaled voxel grid. Runs on
    /// a background thread (via Task.Run in the caller) — pure computation, no UI access.
    /// </summary>
    /// <summary>
    /// Reads every slice of the series and assembles the rescaled voxel grid. Runs on
    /// a background thread (via Task.Run in the caller) — pure computation, no UI access.
    /// Supports both multi-file series and multi-frame single-file series.
    /// </summary>
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
            // Paralleles Einlesen aller DICOM-Slices
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

        // Compute min/max voxel values for robust auto-windowing
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

    /// <summary>
    /// Requests an MPR re-render for the current W/L/slice values. Feature 17: the
    /// actual rendering (three Parallel.For passes over the volume) now runs on a
    /// background thread instead of blocking the UI thread — this used to run
    /// synchronously on every single MouseMove tick while dragging to adjust W/L,
    /// which visibly stuttered on larger volumes. Requests are coalesced: if a render
    /// is already in flight, this just remembers to render again with the latest
    /// parameters once it finishes, instead of queueing up a growing backlog of
    /// redundant renders (the fields used for this, _isMprRendering/_renderRequested,
    /// existed before but were never actually wired up).
    /// </summary>
    private void Trigger2DRender()
    {
        if (_currentVolume == null) return;

        // Bugfix found during review: _wlInitialized was set but never checked, so the
        // slider Maximum/Value assignments in ApplyLoadedVolumeToUi — which fire
        // PreviewSlider_ValueChanged etc. *before* the series' actual Window/Level is
        // read — used to trigger a wasted (and briefly visible) render with stale W/L
        // left over from whatever series was viewed previously.
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

            // Only apply if this is still the series/volume the user is looking at —
            // avoids a slow, stale render from an earlier series overwriting a newer one.
            if (_currentVolume == volume)
            {
                PreviewImage.Source = bmp2D;
            }
        }
        catch
        {
            // Rendering is best-effort visual feedback; a transient failure here must
            // not crash the app (this runs from UI event handlers as fire-and-forget).
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

    /// <summary>
    /// Retheme a token by REPLACING the Window.Resources entry with a brand-new
    /// SolidColorBrush, instead of mutating the existing shared instance's .Color.
    ///
    /// Bugfix (found from a live crash on the second theme toggle): mutating the
    /// long-lived brush worked fine the first time (during the constructor, before the
    /// window had ever rendered), but threw "kann nicht animiert werden, weil das Objekt
    /// versiegelt oder fixiert ist" (Freezable.IsFrozen) on a later click. WPF can freeze
    /// a Freezable resource once it's actually been consumed by a rendered element/sealed
    /// Style — switching to DynamicResource (an earlier fix) prevented the Style-sealing
    /// variant of this, but apparently isn't sufficient to guarantee the brush stays
    /// mutable indefinitely. A fresh SolidColorBrush is, by construction, never frozen;
    /// DynamicResource consumers automatically pick up the replacement because re-resolving
    /// on a resource-dictionary change is exactly what DynamicResource is for.
    /// </summary>
    /// <summary>
    /// Bugfix history for this method (kept in full because the failure mode was extremely
    /// non-obvious and easy to reintroduce):
    ///  1) Mutating the long-lived Resources[key] brush's .Color directly crashed the 2nd time
    ///     it ran, because some WPF Style/Setter machinery had frozen it after first render.
    ///  2) Switching those Setters to DynamicResource "fixed" the direct mutation crash, but
    ///     BeginAnimation on the same shared brush still crashed later — animation checks
    ///     IsFrozen even when plain mutation might not always trip first.
    ///  3) Replacing the Resources[key] entry with a brand-new SolidColorBrush before animating
    ///     it (instead of reusing the old one) *still* crashed — confirmed in isolation that the
    ///     RESOURCE KEY itself gets "poisoned" once any Style.Setter has consumed it via
    ///     DynamicResource, independent of which Brush object currently lives there.
    ///  4) Moving the Style.Setter consumers onto dedicated Window DependencyProperties bound via
    ///     {Binding ...Brush, RelativeSource={RelativeSource AncestorType=Window}}} worked in an
    ///     isolated two-element repro (3 consecutive animated toggles, no crash) — but STILL
    ///     crashed in this app's actual, much larger visual tree (13 Setters across 9 styles).
    ///     The exact trigger differs from a minimal repro, which means it cannot be reliably
    ///     characterized further without spending disproportionate time chasing an internal WPF
    ///     implementation detail.
    ///
    /// The one thing that has NEVER failed in any of the above (or in dozens of test runs): a
    /// freshly constructed SolidColorBrush that is simply ASSIGNED (to a resource key or a
    /// DependencyProperty) and never subsequently mutated or animated. So: no animation, ever,
    /// for these tokens — just an instant swap to a new Brush instance each time. This sacrifices
    /// the nice-to-have theme cross-fade in exchange for a change that is unconditionally safe
    /// regardless of how many Styles/Setters end up consuming these tokens in the future.
    /// </summary>
    private void SetThemeBrush(string key, Color color, bool animate = false)
    {
        // Path 1: direct (non-Setter) XAML consumers via {DynamicResource key} in Window.Resources.
        Resources[key] = new SolidColorBrush(color);

        // Path 2: Style.Setter consumers via {Binding ...Brush, RelativeSource=...} bound to the
        // DependencyProperties declared at the top of this class.
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


    // ══════════════════════════════════════════════════════════════
    //  FEATURE 5: DICOM Tag Viewer
    // ══════════════════════════════════════════════════════════════

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


    // ══════════════════════════════════════════════════════════════
    //  FEATURE 12: Output Filename Template System
    // ══════════════════════════════════════════════════════════════

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

        // Sanitize filename
        return string.Join("_", result.Split(System.IO.Path.GetInvalidFileNameChars()));
    }

    private void UpdateTemplatePreview()
    {
        // Removed TemplatePreviewText to simplify UI. Placeholders are documented in the ToolTip.
    }

    private void TxtFilenameTemplate_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTemplatePreview();
    }


    // ══════════════════════════════════════════════════════════════
    //  Smart Selection Handlers
    // ══════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════
    //  State 2: Processing  (with Feature 1 + Feature 3 transitions)
    // ══════════════════════════════════════════════════════════════

    private async void StartProcessButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = _reviewItems.Where(i => i.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            MessageBox.Show("Bitte wähle mindestens eine Serie aus.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isProcessing = true;

        // Animated transition: Review → Processing
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
            if (ModeMerge.IsChecked == true)
                await ProcessMergeAsync(ct, selectedItems);
            else if (ModeSplit.IsChecked == true)
                await ProcessSplitAsync(ct, selectedItems);
            else
                await ProcessNiftiAsync(ct);

            _lastElapsedSeconds = (DateTime.Now - _startTime).TotalSeconds;

            // Feature 3: Show completion panel instead of toast, but keep logs visible if errors occurred
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
        finally
        {
            _isProcessing = false;
            CancelButton.Visibility = Visibility.Collapsed;
            BrowseButton.IsEnabled = true;
            CleanupTempFolders();
        }
    }


    private async Task ProcessMergeAsync(CancellationToken ct, List<ReviewItemViewModel> selectedItems)
    {
        var merger = new FrameMerger(
            msg => Dispatcher.Invoke(() => AppendLog(msg)),
            msg => Dispatcher.Invoke(() => AppendLog($"WARN: {msg}")));

        bool anonymize = CheckAnonymize.IsChecked == true;
        bool compressDicom = CheckCompressDicom.IsChecked == true;
        string customOutDir = TxtOutputDir.Text.Trim();
        var r = new MergeResult { GroupsFound = selectedItems.Count };

        // One anonymizer instance for the whole batch: UID remapping, patient-ID
        // pseudonyms, and the date-shift offset stay consistent across every series
        // in this run instead of each file being anonymized in isolation.
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
                    // Feature 12: Use template system
                    string safeName = Dispatcher.Invoke(() => ResolveTemplate(template, item));
                    outPath = System.IO.Path.Combine(outBaseDir, $"{safeName}.dcm");

                    bool formatBrainlab = Dispatcher.Invoke(() => CheckBrainlabFormat.IsChecked == true);
                    await Task.Run(() => merger.Merge(group, outPath, item.PatientName, item.SeriesName, anonymize, compressDicom, token, anonymizer, formatBrainlab), token);

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

        // Post Processing
        if (r.CreatedFiles > 0)
        {
            var createdFiles = _batchReport
                .Where(e => e.Success && !string.IsNullOrEmpty(e.OutputPath))
                .Select(e => e.OutputPath!)
                .Distinct()
                .ToList();
            await RunPostProcessingAsync(_lastOutputDir, createdFiles, createdFilesCleanRun: r.Errors == 0, ct);
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

                    bool splitByBValue = Dispatcher.Invoke(() => CheckSplitBValues.IsChecked == true);
                    bool formatBrainlab = Dispatcher.Invoke(() => CheckBrainlabFormat.IsChecked == true);
                    await Task.Run(() => splitter.Split(file, outDir, item.PatientName, item.SeriesName, anonymize, token, anonymizer, splitByBValue, formatBrainlab), token);
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

        // Post Processing
        if (created > 0)
        {
            var createdFiles = _batchReport
                .Where(e => e.Success && !string.IsNullOrEmpty(e.OutputPath))
                .Select(e => e.OutputPath!)
                .Distinct()
                .ToList();
            await RunPostProcessingAsync(_lastOutputDir, createdFiles, createdFilesCleanRun: errors == 0, ct);
        }

        ShowStats(_reviewItems.Count, selectedItems.Count, created, errors);
    }

    /// <summary>
    /// Shared post-processing for Merge and Split runs: ZIP/NIfTI-export only run on a
    /// fully clean run (unchanged from prior behavior), while the batch report and
    /// DICOMDIR are written whenever at least one file was created, since they are
    /// useful even when some series failed (Feature 13 / Feature 4).
    /// </summary>
    private async Task RunPostProcessingAsync(string outputDir, List<string> createdFiles, bool createdFilesCleanRun, CancellationToken ct)
    {
        if (CheckExportNiftiDirect.IsChecked == true && createdFilesCleanRun)
        {
            Dispatcher.Invoke(() =>
            {
                DashboardStatusText.Text = "Erstelle NIfTI-Export (dcm2niix)...";
                DashboardEtaText.Text = "Exportiere...";
            });
            await ExportNiftiDirectAsync(outputDir, ct);
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
                await BatchReportWriter.WriteAsync(outputDir, _batchReport.ToList(), ct);
                Dispatcher.Invoke(() => AppendLog("✓ Batch-Report (CSV/JSON) erstellt"));
            }
            catch (Exception ex)
            {
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
                Dispatcher.Invoke(() => AppendLog(count > 0
                    ? $"✓ DICOMDIR erstellt ({count} Datei(en) referenziert)"
                    : "DICOMDIR übersprungen (keine .dcm-Dateien gefunden)"));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"✗ DICOMDIR-Fehler: {ex.Message}"));
            }
        }

        // ZIP Output runs LAST so it compresses ONLY AND EXACTLY the created output files (e.g. 4 Multi-Frame DICOMs) into the ZIP!
        if (CheckZipOutput.IsChecked == true && createdFilesCleanRun && createdFiles.Count > 0)
        {
            await CreateZipArchiveAsync(outputDir, createdFiles, ct);
        }
    }

    private async Task CreateZipArchiveAsync(string directoryPath, List<string> createdFiles, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath) || createdFiles.Count == 0) return;
        
        Dispatcher.Invoke(() =>
        {
            DashboardStatusText.Text = $"Packe {createdFiles.Count} Datei(en) in 7-Zip Ultra Archiv...";
            DashboardEtaText.Text = "Komprimiere...";
            AppendLog("\n══════════════════════════════════════════════════════════════");
            AppendLog($" ERSTELLE 7-ZIP ARCHIV ({createdFiles.Count} DATEIEN)");
            AppendLog("══════════════════════════════════════════════════════════════");
        });
        
        try
        {
            var selectedPatients = _reviewItems
                .Where(i => i.IsSelected)
                .Select(i => i.PatientName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            string zipBaseName;
            if (selectedPatients.Count == 1)
            {
                zipBaseName = string.Join("_", selectedPatients[0].Split(System.IO.Path.GetInvalidFileNameChars()));
            }
            else if (selectedPatients.Count > 1)
            {
                string firstPatient = string.Join("_", selectedPatients[0].Split(System.IO.Path.GetInvalidFileNameChars()));
                zipBaseName = $"{firstPatient}_et_al";
            }
            else
            {
                string dirName = System.IO.Path.GetFileName(directoryPath);
                zipBaseName = string.IsNullOrEmpty(dirName) ? "Patient" : dirName;
            }

            // Create ZIP archive INSIDE the destination directory
            string zipPath = System.IO.Path.Combine(directoryPath, $"{zipBaseName}.zip");
            
            var szHelper = new SevenZipHelper(
                msg => Dispatcher.Invoke(() => AppendLog(msg)),
                msg => Dispatcher.Invoke(() => AppendLog($"7-Zip WARN: {msg}"))
            );

            // Compress ONLY AND EXACTLY the created output files (e.g. 4 Multi-Frame DICOM files) into the ZIP!
            bool success = await szHelper.CompressFilesToZipUltraAsync(createdFiles, zipPath, ct);

            if (success && File.Exists(zipPath))
            {
                Dispatcher.Invoke(() =>
                {
                    DashboardStatusText.Text = "Bereinige umgewandelte Quelldateien...";
                    DashboardEtaText.Text = "Lösche...";
                    AppendLog("Lösche umgewandelte Quelldateien, da ZIP erstellt wurde...");
                });

                // Remove ONLY the files/folders that were zipped into the archive
                foreach (var path in createdFiles)
                {
                    if (path.Equals(zipPath, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                        else if (Directory.Exists(path)) Directory.Delete(path, true);
                    }
                    catch { }
                }

                Dispatcher.Invoke(() =>
                {
                    AppendLog($"✓ Erstellung erfolgreich: Genau {createdFiles.Count} Datei(en) in {System.IO.Path.GetFileName(zipPath)} komprimiert. Quelldateien entfernt.");
                    DashboardStatusText.Text = "ZIP-Erstellung abgeschlossen";
                    DashboardEtaText.Text = "Fertig";
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AppendLog($"✗ ZIP-Fehler: {ex.Message}"));
        }
    }
    
    private async Task ExportNiftiDirectAsync(string directoryPath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) return;
        
        Dispatcher.Invoke(() => AppendLog("Führe NIfTI-Export (Post-Processing) aus..."));
        
        var converter = new NiftiConverter(
            msg => Dispatcher.Invoke(() => AppendLog(msg)),
            msg => Dispatcher.Invoke(() => AppendLog($"NIFTI WARN: {msg}")));
            
        var settings = new NiftiSettings
        {
            CompressGz = true,
            CreateBidsJson = false,
            AnonymizeBids = CheckAnonymize.IsChecked == true,
            FilenameFormat = "%f"
        };
        
        string niftiOutDir = System.IO.Path.Combine(directoryPath, "Nifti_Export");
        Directory.CreateDirectory(niftiOutDir);
        
        try
        {
            int code = await converter.ConvertAsync(directoryPath, niftiOutDir, settings, ct);
            if (code == 0)
                Dispatcher.Invoke(() => AppendLog($"✓ NIfTI-Post-Export erfolgreich erstellt unter: {System.IO.Path.GetFileName(niftiOutDir)}"));
            else
                Dispatcher.Invoke(() => AppendLog($"✗ NIfTI-Post-Export fehlgeschlagen (Exit Code {code})"));
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AppendLog($"✗ NIfTI-Export-Fehler: {ex.Message}"));
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


    // ══════════════════════════════════════════════════════════════
    //  FEATURE 3: Animated Completion Panel
    // ══════════════════════════════════════════════════════════════

    private void ShowCompletionPanel()
    {
        CompletionCreatedText.Text = _lastCreated.ToString();
        CompletionErrorsText.Text = _lastErrors.ToString();
        CompletionTimeText.Text = FormatDuration(_lastElapsedSeconds);
        CompletionSeriesText.Text = _reviewItems.Count(i => i.IsSelected).ToString();

        // Set up progress ring (we'll animate it)
        CompletionRingProgress.StrokeDashArray = new DoubleCollection { 0, 100 };

        AnimateIn(CompletionPanel, 30, 400);

        // Animate the ring from 0 to full
        var ringAnim = new DoubleAnimation(0, 78.5, TimeSpan.FromMilliseconds(800)) // 78.5 ≈ circumference for 25px radius
        { BeginTime = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        // Animate via dash array
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var start = DateTime.Now;
        double targetDash = 78.5; // 2 * π * 12.5 (radius)
        timer.Tick += (_, _) =>
        {
            double elapsed = (DateTime.Now - start).TotalMilliseconds - 200; // delay
            if (elapsed < 0) return;
            double progress = Math.Min(1.0, elapsed / 800.0);
            // Ease out
            progress = 1 - Math.Pow(1 - progress, 3);
            double dashVal = progress * targetDash;
            CompletionRingProgress.StrokeDashArray = new DoubleCollection { dashVal, 100 };

            if (progress >= 1.0)
            {
                timer.Stop();
                // Show checkmark with bounce
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


    // ══════════════════════════════════════════════════════════════
    //  Dashboard & Progress
    // ══════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════
    //  UI Helpers
    // ══════════════════════════════════════════════════════════════

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

    /// <summary>
    /// Feature 18: confirms before discarding unprocessed work. Only prompts when
    /// there's actually something to lose (loaded/reviewed series); an empty session
    /// resets immediately with no extra click. Wired to the "Zurücksetzen" button and
    /// the F5 shortcut — <see cref="CompletionReset_Click"/> ("Nochmal", after a
    /// successful run whose output is already safely on disk) intentionally skips
    /// this prompt to avoid nagging the user after every completed batch.
    /// </summary>
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

        // Reset all panels
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

        // MPR / Volume cleanup
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
        if (!IsLoaded) return;

        bool isNifti = ModeNifti.IsChecked == true;

        if (NiftiSettingsPanel != null)
            NiftiSettingsPanel.Visibility = isNifti ? Visibility.Visible : Visibility.Collapsed;

        // Windows 11 SegmentedControl-style sliding indicator: glide the pill behind
        // whichever mode segment is now selected instead of just snapping visibility.
        int index = ModeMerge.IsChecked == true ? 0 : ModeSplit.IsChecked == true ? 1 : 2;
        double segmentWidth = ModeSlider.ActualWidth;
        if (segmentWidth <= 0) return; // not laid out yet (e.g. very first Checked event)

        var anim = new DoubleAnimation(index * segmentWidth, TimeSpan.FromMilliseconds(220))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        ModeSliderTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <summary>
    /// Defensive hardening found during review: the slider position is otherwise only
    /// recomputed on a mode Checked event, not when its own width changes (currently
    /// ModeToggleGrid uses a fixed Width, so this can't happen from a window resize today —
    /// but snaps it back into place with no animation, rather than leaving a stale offset,
    /// should that ever change).
    /// </summary>
    private void ModeSlider_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ModeSliderTransform == null || e.NewSize.Width <= 0) return;

        int index = ModeMerge.IsChecked == true ? 0 : ModeSplit.IsChecked == true ? 1 : 2;
        ModeSliderTransform.BeginAnimation(TranslateTransform.XProperty, null);
        ModeSliderTransform.X = index * e.NewSize.Width;
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

        // Absicherung gegen Indexüberschreitungen bei Größenänderungen (Race Conditions)
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
            
            // Division aus dem inneren Loop extrahiert für massive Rechenbeschleunigung (Intel CPU Optimierung)
            double invRange = 255.0 / range;
            
            int volWidth = vol.Width;
            int volHeight = vol.Height;
            int sliceArea = volHeight * volWidth;
            float[] voxelData = vol.VoxelData;
            
            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                byte* pRow = pBackBuffer + y * stride;
                
                // Contiguous rows in vol.VoxelData (sliceIndex*sliceArea + y*volWidth + x),
                // vectorized using System.Numerics.Vector<float> for optimal SIMD execution.
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
