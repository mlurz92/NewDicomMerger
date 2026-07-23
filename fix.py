import os
import re

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    cs = f.read()

# Fix IServiceCollection extension
cs = cs.replace('using System.Windows.Threading;', 'using System.Windows.Threading;\nusing Microsoft.Extensions.DependencyInjection;')

# Fix Nullable warnings
cs = cs.replace('private DispatcherTimer _dashboardTimer;', 'private DispatcherTimer? _dashboardTimer;')
cs = cs.replace('private LoadedDicom[] _currentPreviewFiles;', 'private LoadedDicom[]? _currentPreviewFiles;')

# Replace ProgressPanel with DashboardPanel
cs = cs.replace('ProgressPanel', 'DashboardPanel')

# Replace StatusText with DashboardStatusText
cs = cs.replace('StatusText.Text', 'DashboardStatusText.Text')

# Replace DetailText with DashboardEtaText
cs = cs.replace('DetailText.Text', 'DashboardEtaText.Text')

# Replace ProgressFill.Width with DashboardProgressFill.Width
cs = cs.replace('ProgressFill.Width', 'DashboardProgressFill.Width')
cs = cs.replace('ProgressFill.Background', 'DashboardProgressFill.Background')
cs = cs.replace('ProgressFill.BeginAnimation', 'DashboardProgressFill.BeginAnimation')

# Fix SetProgress to not use PercentText
set_progress = '''    private void SetProgress(double percent)
    {
        double maxW = DashboardPanel.ActualWidth > 0 ? DashboardPanel.ActualWidth : 400; 
        double w = (percent / 100.0) * maxW;
        if (w < 0) w = 0;
        
        var anim = new DoubleAnimation(w, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        DashboardProgressFill.BeginAnimation(WidthProperty, anim);
    }'''

cs = re.sub(r'    private void SetProgress\(double percent\).*?(?=    private void AppendLog)', set_progress + '\n\n', cs, flags=re.DOTALL)

# Fix Interlocked properties in ProcessMergeAsync
cs = cs.replace('Interlocked.Increment(ref r.CreatedFiles);', 'lock(r) { r.CreatedFiles++; }')
cs = cs.replace('Interlocked.Increment(ref r.Errors);', 'lock(r) { r.Errors++; }')

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(cs)

print("Fixed CS file")
