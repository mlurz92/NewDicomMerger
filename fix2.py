import os

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    cs = f.read()

cs = cs.replace('.AddFellowOakDicom().', '.')
cs = cs.replace('DashboardDashboard', 'Dashboard')

with open('c:/Users/marku/Desktop/NewDicomMerger/MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(cs)

print("Fixed double replacement and fo-dicom.")
