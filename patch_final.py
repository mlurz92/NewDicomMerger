import os
import re

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    cs = f.read()

# 1. Fix PatientName parsing
cs = cs.replace(
    'string pName = first.File.Dataset.GetSingleValueOrDefault(FellowOakDicom.DicomTag.PatientName, "Unbekannt");',
    'string pName = first.File.Dataset.GetSingleValueOrDefault(FellowOakDicom.DicomTag.PatientName, "Unbekannt").Replace("^", "_");'
)

# 2. Fix Slider RenderPreviewAsync flickering and race condition
new_render_preview = '''    private int _currentRenderFrame = -1;

    private async Task RenderPreviewAsync(int frameIndex)
    {
        if (_currentPreviewFiles == null || frameIndex < 0 || frameIndex >= _currentPreviewFiles.Length) return;
        
        _currentRenderFrame = frameIndex;
        var path = _currentPreviewFiles[frameIndex].FilePath;
        
        try
        {
            var bmp = await Task.Run(() => 
            {
                var dicomImage = new FellowOakDicom.Imaging.DicomImage(path);
                var image = dicomImage.RenderImage(0).As<System.Windows.Media.Imaging.WriteableBitmap>();
                image.Freeze();
                return image;
            });

            Dispatcher.Invoke(() => 
            {
                if (_currentRenderFrame == frameIndex)
                {
                    PreviewImage.Source = bmp;
                    PreviewLoadingText.Visibility = Visibility.Collapsed;
                }
            });
        }
        catch
        {
            Dispatcher.Invoke(() => 
            {
                if (_currentRenderFrame == frameIndex)
                {
                    PreviewLoadingText.Visibility = Visibility.Visible;
                    PreviewLoadingText.Text = "Fehler beim Laden";
                }
            });
        }
    }'''

cs = re.sub(r'    private async Task RenderPreviewAsync\(int frameIndex\).*?(?=    private void ThemeBlue_Click)', new_render_preview + '\n\n', cs, flags=re.DOTALL)

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(cs)

print('Patched successfully')
