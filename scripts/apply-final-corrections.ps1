$ErrorActionPreference = 'Stop'
Remove-Item 'bin','obj','artifacts','BugHuntHarness' -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force 'artifacts\reports' | Out-Null

$buildParts = @(Get-ChildItem '.build-overlay\part_*.b64' | Sort-Object Name)
if ($buildParts.Count -ne 8) { throw "Expected 8 build overlay parts, found $($buildParts.Count)." }
$buildBase64 = (($buildParts | ForEach-Object { Get-Content $_.FullName -Raw }) -join '') -replace '\s',''
$buildZip = Join-Path $env:RUNNER_TEMP 'built-source-overlay.zip'
[IO.File]::WriteAllBytes($buildZip,[Convert]::FromBase64String($buildBase64))
Expand-Archive -LiteralPath $buildZip -DestinationPath $PWD -Force

$fixParts = @(Get-ChildItem '.bughunt-fixes\part_*.b64' | Sort-Object Name)
if ($fixParts.Count -ne 8) { throw "Expected 8 fix parts, found $($fixParts.Count)." }
$fixBase64 = (($fixParts | ForEach-Object { Get-Content $_.FullName -Raw }) -join '') -replace '\s',''
if ($fixBase64.Length -ne 60876) { throw "Unexpected fix base64 length: $($fixBase64.Length)." }
$fixBase64Hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($fixBase64))).ToLowerInvariant()
if ($fixBase64Hash -ne 'cf5a7265ae39d15b1eb8cd3930c34cd197e68288efb8bd89c27337074d2f0781') { throw "Fix base64 hash mismatch: $fixBase64Hash" }
$fixCompressed = [Convert]::FromBase64String($fixBase64)
$fixCompressedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($fixCompressed)).ToLowerInvariant()
if ($fixCompressedHash -ne '4f89bd40fe3ab1f833a08e54346461f6f8126ce96d515532d6572c5c0d232e1d') { throw "Compressed fix hash mismatch: $fixCompressedHash" }
$fullPatch = Join-Path $env:RUNNER_TEMP 'bughunt-full.patch'
$input = [IO.MemoryStream]::new($fixCompressed)
try {
  $gzip = [IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
  try {
    $output = [IO.File]::Create($fullPatch)
    try { $gzip.CopyTo($output) } finally { $output.Dispose() }
  } finally { $gzip.Dispose() }
} finally { $input.Dispose() }
$fullPatchHash = (Get-FileHash $fullPatch -Algorithm SHA256).Hash.ToLowerInvariant()
if ($fullPatchHash -ne '4decaeea1f0c9d43ad3e51fb386aea25b178800a508c9088ce2285f273f88985') { throw "Full patch hash mismatch: $fullPatchHash" }

$textFiles = @(
  'App.xaml.cs','MainWindow.xaml.cs','NewDicomMerger.csproj',
  'Services\BatchReportGenerator.cs','Services\BatchReportWriter.cs','Services\DicomDirWriter.cs',
  'Services\DicomScanner.cs','Services\DiffusionBValueHelper.cs','Services\FrameMerger.cs',
  'Services\FrameSplitter.cs','Services\NiftiConverter.cs','Services\SeriesDeidentifier.cs','Services\SevenZipHelper.cs'
)
foreach ($file in $textFiles) {
  if (-not (Test-Path $file -PathType Leaf)) { throw "Missing source file: $file" }
  $content = [IO.File]::ReadAllText((Resolve-Path $file)) -replace "`r`n","`n" -replace "`r","`n"
  [IO.File]::WriteAllText((Resolve-Path $file),$content,[Text.UTF8Encoding]::new($false))
}
New-Item -ItemType Directory -Force 'BugHuntHarness' | Out-Null
[IO.File]::WriteAllText((Join-Path $PWD 'BugHuntHarness\BugHuntHarness.csproj'),'',[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $PWD 'BugHuntHarness\Program.cs'),'',[Text.UTF8Encoding]::new($false))

$patchText = [IO.File]::ReadAllText($fullPatch)
$sections = [regex]::Split($patchText,'(?m)(?=^diff -ruN )') | Where-Object { $_ -and $_ -notmatch '^diff -ruN .*Services/FrameMerger\.cs' }
$patchWithoutFrame = Join-Path $env:RUNNER_TEMP 'bughunt-without-frame.patch'
[IO.File]::WriteAllText($patchWithoutFrame,($sections -join ''),[Text.UTF8Encoding]::new($false))
git apply --check --whitespace=nowarn $patchWithoutFrame
if ($LASTEXITCODE -ne 0) { throw 'Final correction patch validation failed.' }
git apply --whitespace=nowarn $patchWithoutFrame
if ($LASTEXITCODE -ne 0) { throw 'Final correction patch application failed.' }

$expectedFrameHash = '6caa2ac2a163d21599b47b99322b70574b6fc3244390b665764a992b6fe2e82e'
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

$required = @(
  'BugHuntHarness\BugHuntHarness.csproj','BugHuntHarness\Program.cs','Services\FrameMerger.cs',
  'Services\SevenZipHelper.cs','Services\SeriesDeidentifier.cs','Services\DicomDirWriter.cs'
)
foreach ($file in $required) {
  if (-not (Test-Path $file -PathType Leaf) -or (Get-Item $file).Length -eq 0) { throw "Corrected file missing or empty: $file" }
}
Get-FileHash $required -Algorithm SHA256 | Format-Table -AutoSize | Out-String | Set-Content 'artifacts\reports\corrected-file-hashes.txt'
