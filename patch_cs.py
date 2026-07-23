import os
import re

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    cs = f.read()

# Remove theme pickers
pattern = r'    private void ThemeBlue_Click.*?ApplyTheme\(Color a1, Color a2, Color a3\)\s*\{\s*this\.Resources\["Accent1"\] = new SolidColorBrush\(a1\);\s*this\.Resources\["Accent2"\] = new SolidColorBrush\(a2\);\s*this\.Resources\["Accent3"\] = new SolidColorBrush\(a3\);\s*\}'
cs = re.sub(pattern, '', cs, flags=re.DOTALL)

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(cs)

print('CS patched')
