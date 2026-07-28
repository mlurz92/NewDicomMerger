import os
import re

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    cs = f.read()

# 1. Add using
cs = cs.replace('using NewDicomMerger.Services;', 'using NewDicomMerger.Services;\nusing FellowOakDicom.Imaging;\nusing System.Threading;\nusing System.Windows.Threading;')

# 2. Add Init to MainWindow
init_code = '''
        InitializeComponent();
        ReviewGrid.ItemsSource = _reviewItems;
        new FellowOakDicom.DicomSetupBuilder()
            .RegisterServices(s => s.AddFellowOakDicom().AddImageManager<WPFImageManager>())
            .Build();
'''
cs = cs.replace('        InitializeComponent();\n        ReviewGrid.ItemsSource = _reviewItems;', init_code)

# 3. Add Dashboard state
dashboard_state = '''    private DispatcherTimer _dashboardTimer;
    private DateTime _startTime;
    private int _totalItems;
    private int _processedCount;
'''
cs = cs.replace('    private CancellationTokenSource? _cts;', '    private CancellationTokenSource? _cts;\n' + dashboard_state)

# 4. Preview and Theme methods
preview_methods = '''
    // ──────────────── Preview & Themes ────────────────

    private LoadedDicom[] _currentPreviewFiles;

    private async void ReviewGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReviewGrid.SelectedItem is ReviewItemViewModel item)
        {
            _currentPreviewFiles = item.OriginalGroup.Files.ToArray();
            if (_currentPreviewFiles.Length > 1)
            {
                PreviewControls.Visibility = Visibility.Visible;
                PreviewSlider.Maximum = _currentPreviewFiles.Length - 1;
                PreviewSlider.Value = _currentPreviewFiles.Length / 2;
                PreviewFrameText.Text = $"Schicht: {PreviewSlider.Value + 1}/{_currentPreviewFiles.Length}";
            }
            else
            {
                PreviewControls.Visibility = Visibility.Collapsed;
                PreviewSlider.Value = 0;
            }
            PreviewEmptyText.Visibility = Visibility.Collapsed;
            await RenderPreviewAsync((int)PreviewSlider.Value);
        }
        else
        {
            PreviewImage.Source = null;
            PreviewEmptyText.Visibility = Visibility.Visible;
            PreviewControls.Visibility = Visibility.Collapsed;
        }
    }

    private async void PreviewSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_currentPreviewFiles == null) return;
        int frame = (int)e.NewValue;
        PreviewFrameText.Text = $"Schicht: {frame + 1}/{_currentPreviewFiles.Length}";
        await RenderPreviewAsync(frame);
    }

    private async Task RenderPreviewAsync(int frameIndex)
    {
        if (_currentPreviewFiles == null || frameIndex < 0 || frameIndex >= _currentPreviewFiles.Length) return;
        PreviewLoadingText.Visibility = Visibility.Visible;
        PreviewImage.Source = null;

        var path = _currentPreviewFiles[frameIndex].FilePath;
        try
        {
            await Task.Run(() => 
            {
                var dicomImage = new DicomImage(path);
                var bmp = dicomImage.RenderImage(0).As<System.Windows.Media.Imaging.WriteableBitmap>();
                bmp.Freeze();
                Dispatcher.Invoke(() => 
                {
                    PreviewImage.Source = bmp;
                    PreviewLoadingText.Visibility = Visibility.Collapsed;
                });
            });
        }
        catch
        {
            Dispatcher.Invoke(() => PreviewLoadingText.Text = "Fehler beim Laden");
        }
    }

    private void ThemeBlue_Click(object sender, RoutedEventArgs e) => ApplyTheme(Color.FromRgb(0x00, 0xD2, 0xFF), Color.FromRgb(0x3A, 0x7B, 0xD5), Color.FromRgb(0x8B, 0x5C, 0xF6));
    private void ThemePink_Click(object sender, RoutedEventArgs e) => ApplyTheme(Color.FromRgb(0xFF, 0x00, 0x7F), Color.FromRgb(0xF4, 0x3F, 0x5E), Color.FromRgb(0xFF, 0x00, 0xFF));
    private void ThemeGreen_Click(object sender, RoutedEventArgs e) => ApplyTheme(Color.FromRgb(0x10, 0xB9, 0x81), Color.FromRgb(0x05, 0x96, 0x69), Color.FromRgb(0x34, 0xD3, 0x99));
    private void ThemePurple_Click(object sender, RoutedEventArgs e) => ApplyTheme(Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x6D, 0x28, 0xD9), Color.FromRgb(0xC0, 0x84, 0xFC));

    private void ApplyTheme(Color a1, Color a2, Color a3)
    {
        this.Resources["Accent1"] = new SolidColorBrush(a1);
        this.Resources["Accent2"] = new SolidColorBrush(a2);
        this.Resources["Accent3"] = new SolidColorBrush(a3);
    }
'''

cs = cs.replace('    // ──────────────── Smart Selection Handlers ────────────────', preview_methods + '\n    // ──────────────── Smart Selection Handlers ────────────────')


# 5. Dashboard Methods
dashboard_methods = '''
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
        ProgressPanel.Visibility = Visibility.Collapsed; // Hide old

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
            DashboardEtaText.Text = $"Restzeit: {TimeSpan.FromSeconds(remainingSeconds):mm\\\\:ss}";
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
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        DashboardProgressFill.BeginAnimation(WidthProperty, anim);
    }

    private void StopDashboard()
    {
        _dashboardTimer?.Stop();
        DashboardPanel.Visibility = Visibility.Collapsed;
    }
'''

cs = cs.replace('    // ──────────────── UI Helpers ────────────────', dashboard_methods + '\n    // ──────────────── UI Helpers ────────────────')

# 6. Replace ProcessMergeAsync
new_process_merge = '''
    private async Task ProcessMergeAsync(CancellationToken ct, List<ReviewItemViewModel> selectedItems)
    {
        var merger = new FrameMerger(
            msg => Dispatcher.Invoke(() => AppendLog(msg)),
            msg => Dispatcher.Invoke(() => AppendLog($"WARN: {msg}")));

        bool anonymize = CheckAnonymize.IsChecked == true;
        string customOutDir = TxtOutputDir.Text.Trim();
        var r = new MergeResult { GroupsFound = selectedItems.Count };
        
        StartDashboard(selectedItems.Count, "Zusammenführen...");

        bool useTurbo = CheckTurbo.IsChecked == true;
        int maxDegree = useTurbo ? Environment.ProcessorCount : 1;

        try
        {
            await Parallel.ForEachAsync(selectedItems, new ParallelOptions { MaxDegreeOfParallelism = maxDegree, CancellationToken = ct }, async (item, token) =>
            {
                var group = item.OriginalGroup;
                
                var firstFile = group.Files[0].FilePath;
                var job = _jobs.FirstOrDefault(j => firstFile.StartsWith(j.InputDirectory, StringComparison.OrdinalIgnoreCase));
                
                string outBaseDir = string.IsNullOrEmpty(customOutDir) 
                    ? (job?.OutputDirectory ?? Path.GetDirectoryName(firstFile)!) 
                    : customOutDir;

                var firstDs = group.Files[0].File.Dataset;
                firstDs.AddOrUpdate(FellowOakDicom.DicomTag.PatientName, item.PatientName);
                firstDs.AddOrUpdate(FellowOakDicom.DicomTag.SeriesDescription, item.SeriesName);

                try
                {
                    string safeName = string.Join("_", (item.PatientName + "-" + item.SeriesName).Split(Path.GetInvalidFileNameChars()));
                    string outPath = Path.Combine(outBaseDir, $"{safeName}.dcm");
                    
                    await Task.Run(() => merger.Merge(group, outPath, anonymize, token), token);
                    
                    Interlocked.Increment(ref r.CreatedFiles);
                    Dispatcher.Invoke(() => AppendLog($"✓ {Path.GetFileName(outPath)}"));
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref r.Errors);
                    Dispatcher.Invoke(() => AppendLog($"✗ Fehler: {ex.Message}"));
                }
                finally
                {
                    Interlocked.Increment(ref _processedCount);
                }
            });
        }
        finally
        {
            StopDashboard();
        }

        ShowStats(_reviewItems.Count, selectedItems.Count, r.CreatedFiles, r.Errors);
    }
'''

cs = re.sub(r'    private async Task ProcessMergeAsync.*?(?=    private async Task ProcessSplitAsync)', new_process_merge + '\n', cs, flags=re.DOTALL)

# 7. Replace ProcessSplitAsync
new_process_split = '''
    private async Task ProcessSplitAsync(CancellationToken ct, List<ReviewItemViewModel> selectedItems)
    {
        var splitter = new FrameSplitter(
            msg => Dispatcher.Invoke(() => AppendLog(msg)),
            msg => Dispatcher.Invoke(() => AppendLog($"WARN: {msg}")));

        bool anonymize = CheckAnonymize.IsChecked == true;
        string customOutDir = TxtOutputDir.Text.Trim();
        int created = 0;
        int errors = 0;

        StartDashboard(selectedItems.Count, "Aufteilen...");
        bool useTurbo = CheckTurbo.IsChecked == true;
        int maxDegree = useTurbo ? Environment.ProcessorCount : 1;

        try
        {
            await Parallel.ForEachAsync(selectedItems, new ParallelOptions { MaxDegreeOfParallelism = maxDegree, CancellationToken = ct }, async (item, token) =>
            {
                var file = item.OriginalGroup.Files[0];

                var firstFilePath = file.FilePath;
                var job = _jobs.FirstOrDefault(j => firstFilePath.StartsWith(j.InputDirectory, StringComparison.OrdinalIgnoreCase));
                
                string outBaseDir = string.IsNullOrEmpty(customOutDir) 
                    ? (job?.OutputDirectory ?? Path.GetDirectoryName(firstFilePath)!) 
                    : customOutDir;

                try
                {
                    string baseName = Path.GetFileNameWithoutExtension(firstFilePath);
                    string outDir = Path.Combine(outBaseDir, baseName);
                    Directory.CreateDirectory(outDir);
                    
                    await Task.Run(() => splitter.Split(file, outDir, anonymize, token), token);
                    Interlocked.Increment(ref created);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errors);
                    Dispatcher.Invoke(() => AppendLog($"✗ Fehler: {ex.Message}"));
                }
                finally
                {
                    Interlocked.Increment(ref _processedCount);
                }
            });
        }
        finally
        {
            StopDashboard();
        }

        ShowStats(_reviewItems.Count, selectedItems.Count, created, errors);
    }
'''

cs = re.sub(r'    private async Task ProcessSplitAsync.*?(?=    private async Task ProcessNiftiAsync)', new_process_split + '\n', cs, flags=re.DOTALL)

# Let's fix ProcessNiftiAsync to also use the Dashboard
cs = cs.replace('SetProgress(50);', '/* NIfTI has own progress */')
cs = cs.replace('SetProgress(100);', '')

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(cs)

print("Done")
