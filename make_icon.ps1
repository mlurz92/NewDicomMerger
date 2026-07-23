Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile('C:\Users\marku\.gemini\antigravity-ide\brain\192d9657-577b-40be-bea3-34dc35f192a0\app_icon_1781585736746.png')
$iconStream = [System.IO.File]::Create('c:\Users\marku\Desktop\NewDicomMerger\icon.ico')
[System.Drawing.Icon]::FromHandle($img.GetHicon()).Save($iconStream)
$iconStream.Close()
$img.Dispose()
