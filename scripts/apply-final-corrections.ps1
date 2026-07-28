$ErrorActionPreference = 'Stop'
Remove-Item 'bin','obj','artifacts','BugHuntHarness' -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force 'artifacts\reports' | Out-Null

$sourceParts = @(Get-ChildItem '.final-source-overlay\part_*.b64' | Sort-Object Name)
if ($sourceParts.Count -ne 2) { throw "Expected 2 final source overlay parts, found $($sourceParts.Count)." }
$sourceBase64 = (($sourceParts | ForEach-Object { Get-Content $_.FullName -Raw }) -join '') -replace '\s',''
$sourceBytes = [Convert]::FromBase64String($sourceBase64)
if ($sourceBytes.Length -lt 100000) { throw "Final source overlay is unexpectedly small: $($sourceBytes.Length) bytes." }
$sourceZip = Join-Path $env:RUNNER_TEMP 'final-corrected-source.zip'
[IO.File]::WriteAllBytes($sourceZip,$sourceBytes)
$stream = [IO.File]::OpenRead($sourceZip)
try {
  if ($stream.ReadByte() -ne 0x50 -or $stream.ReadByte() -ne 0x4B) { throw 'Final source overlay has no ZIP header.' }
} finally { $stream.Dispose() }
Expand-Archive -LiteralPath $sourceZip -DestinationPath $PWD -Force

$expectedFrameHash = '6caa2ac2a163d21599b47b99322b70574b6fc3244390b665764a992b6fe2e82e'
$currentFrameHash = if (Test-Path 'Services\FrameMerger.cs' -PathType Leaf) { (Get-FileHash 'Services\FrameMerger.cs' -Algorithm SHA256).Hash.ToLowerInvariant() } else { '' }
if ($currentFrameHash -ne $expectedFrameHash) {
  $frameApplied = $false
  foreach ($directory in @('.bughunt-frame-v2','.bughunt-frame')) {
    if (-not (Test-Path $directory -PathType Container)) { continue }
    $parts = @(Get-ChildItem "$directory\part_*.b64" | Sort-Object Name)
    if ($parts.Count -eq 0) { continue }
    $candidateBase64 = (($parts | ForEach-Object { Get-Content $_.FullName -Raw }) -join '') -replace '\s',''
    try { $candidateCompressed = [Convert]::FromBase64String($candidateBase64) } catch { continue }
    $candidatePath = Join-Path $env:RUNNER_TEMP ((Split-Path $directory -Leaf) + '.cs')
    try {
      $candidateInput = [IO.MemoryStream]::new($candidateCompressed)
      try {
        $candidateGzip = [IO.Compression.GZipStream]::new($candidateInput,[IO.Compression.CompressionMode]::Decompress)
        try {
          $candidateOutput = [IO.File]::Create($candidatePath)
          try { $candidateGzip.CopyTo($candidateOutput) } finally { $candidateOutput.Dispose() }
        } finally { $candidateGzip.Dispose() }
      } finally { $candidateInput.Dispose() }
    } catch { continue }
    $candidateHash = (Get-FileHash $candidatePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($candidateHash -eq $expectedFrameHash) {
      Copy-Item $candidatePath 'Services\FrameMerger.cs' -Force
      $frameApplied = $true
      break
    }
  }
  if (-not $frameApplied) { throw 'Final FrameMerger payload with expected SHA-256 was not found.' }
}

$required = @(
  'App.xaml','App.xaml.cs','MainWindow.xaml','MainWindow.xaml.cs','NewDicomMerger.csproj',
  'BugHuntHarness\BugHuntHarness.csproj','BugHuntHarness\Program.cs',
  'Services\BatchReportGenerator.cs','Services\BatchReportWriter.cs','Services\DicomDirWriter.cs',
  'Services\DicomScanner.cs','Services\DiffusionBValueHelper.cs','Services\FrameMerger.cs',
  'Services\FrameSplitter.cs','Services\NiftiConverter.cs','Services\SeriesDeidentifier.cs','Services\SevenZipHelper.cs'
)
foreach ($file in $required) {
  if (-not (Test-Path $file -PathType Leaf) -or (Get-Item $file).Length -eq 0) { throw "Corrected file missing or empty: $file" }
}
$actualFrameHash = (Get-FileHash 'Services\FrameMerger.cs' -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualFrameHash -ne $expectedFrameHash) { throw "Final FrameMerger hash mismatch: $actualFrameHash" }
Get-FileHash $required -Algorithm SHA256 | Format-Table -AutoSize | Out-String | Set-Content 'artifacts\reports\corrected-file-hashes.txt'
