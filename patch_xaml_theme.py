import re

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml', 'r', encoding='utf-8') as f:
    xaml = f.read()

match = re.search(r'<StackPanel[^>]*x:Name="ThemePickers"[^>]*>.*?</StackPanel>', xaml, re.DOTALL)
if match:
    xaml = xaml.replace(match.group(0), '')
    with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml', 'w', encoding='utf-8') as f:
        f.write(xaml)
    print('Replaced')
else:
    print('Not found')
