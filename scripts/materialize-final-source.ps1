$ErrorActionPreference = 'Stop'

$root = (Resolve-Path '.').Path
Remove-Item 'bin','obj','artifacts','BugHuntHarness' -Recurse -Force -ErrorAction SilentlyContinue

$buildParts = @(Get-ChildItem '.build-overlay/part_*.b64' -File | Sort-Object Name)
if ($buildParts.Count -ne 8) { throw "Expected 8 build overlay parts, found $($buildParts.Count)." }
$buildBase64 = (($buildParts | ForEach-Object { Get-Content $_.FullName -Raw }) -join '') -replace '\s',''
$buildBytes = [Convert]::FromBase64String($buildBase64)
if ($buildBytes.Length -lt 10000) { throw "Build overlay is unexpectedly small: $($buildBytes.Length) bytes." }
if ($buildBytes[0] -ne 0x50 -or $buildBytes[1] -ne 0x4B) { throw 'Build overlay has no ZIP signature.' }
$buildZip = Join-Path $env:RUNNER_TEMP 'built-source-overlay.zip'
[IO.File]::WriteAllBytes($buildZip,$buildBytes)
Expand-Archive -LiteralPath $buildZip -DestinationPath $root -Force

$fixParts = @(Get-ChildItem '.bughunt-fixes/part_*.b64' -File | Sort-Object Name)
if ($fixParts.Count -ne 8) { throw "Expected 8 fix parts, found $($fixParts.Count)." }
$fixBase64 = (($fixParts | ForEach-Object { Get-Content $_.FullName -Raw }) -join '') -replace '\s',''
if ($fixBase64.Length -lt 10000) { throw "BugHunt fix payload is unexpectedly small: $($fixBase64.Length) characters." }
$fixCompressed = [Convert]::FromBase64String($fixBase64)
if ($fixCompressed.Length -lt 5000) { throw "Compressed BugHunt patch is unexpectedly small: $($fixCompressed.Length) bytes." }
$fullPatch = Join-Path $env:RUNNER_TEMP 'bughunt-full.patch'
$input = [IO.MemoryStream]::new($fixCompressed)
try {
  $gzip = [IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
  try {
    $output = [IO.File]::Create($fullPatch)
    try { $gzip.CopyTo($output) } finally { $output.Dispose() }
  } finally { $gzip.Dispose() }
} finally { $input.Dispose() }
if (-not (Test-Path $fullPatch -PathType Leaf) -or (Get-Item $fullPatch).Length -lt 10000) { throw 'Decompressed BugHunt patch is missing or unexpectedly small.' }
$patchText = [IO.File]::ReadAllText($fullPatch)
$requiredPatchTargets = @(
  'App.xaml.cs','MainWindow.xaml.cs','NewDicomMerger.csproj',
  'Services/BatchReportGenerator.cs','Services/BatchReportWriter.cs','Services/DicomDirWriter.cs',
  'Services/DicomScanner.cs','Services/DiffusionBValueHelper.cs','Services/FrameMerger.cs',
  'Services/FrameSplitter.cs','Services/NiftiConverter.cs','Services/SeriesDeidentifier.cs','Services/SevenZipHelper.cs'
)
foreach ($target in $requiredPatchTargets) {
  $normalized = $target -replace '\\','/'
  if ($patchText -notmatch [regex]::Escape($normalized)) { throw "BugHunt patch does not contain required target: $target" }
}

$textFiles = @(
  'App.xaml.cs','MainWindow.xaml.cs','NewDicomMerger.csproj',
  'Services/BatchReportGenerator.cs','Services/BatchReportWriter.cs','Services/DicomDirWriter.cs',
  'Services/DicomScanner.cs','Services/DiffusionBValueHelper.cs','Services/FrameMerger.cs',
  'Services/FrameSplitter.cs','Services/NiftiConverter.cs','Services/SeriesDeidentifier.cs','Services/SevenZipHelper.cs'
)
foreach ($file in $textFiles) {
  if (-not (Test-Path $file -PathType Leaf)) { throw "Missing source file after build overlay reconstruction: $file" }
  $content = [IO.File]::ReadAllText((Resolve-Path $file)) -replace "`r`n","`n" -replace "`r","`n"
  [IO.File]::WriteAllText((Resolve-Path $file),$content,[Text.UTF8Encoding]::new($false))
}

New-Item -ItemType Directory -Force 'BugHuntHarness' | Out-Null
[IO.File]::WriteAllText((Join-Path $root 'BugHuntHarness/BugHuntHarness.csproj'),'',[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $root 'BugHuntHarness/Program.cs'),'',[Text.UTF8Encoding]::new($false))

$sections = [regex]::Split($patchText,'(?m)(?=^diff -ruN )') | Where-Object { $_ -and $_ -notmatch '^diff -ruN .*Services/FrameMerger\.cs' }
$patchWithoutFrame = Join-Path $env:RUNNER_TEMP 'bughunt-without-frame.patch'
[IO.File]::WriteAllText($patchWithoutFrame,($sections -join ''),[Text.UTF8Encoding]::new($false))
git apply --check --whitespace=nowarn $patchWithoutFrame
if ($LASTEXITCODE -ne 0) { throw 'BugHunt patch validation failed.' }
git apply --whitespace=nowarn $patchWithoutFrame
if ($LASTEXITCODE -ne 0) { throw 'BugHunt patch application failed.' }

$expectedFrameHash = '6caa2ac2a163d21599b47b99322b70574b6fc3244390b665764a992b6fe2e82e'
$frameApplied = $false
foreach ($directory in @('.bughunt-frame-v2','.bughunt-frame')) {
  if (-not (Test-Path $directory -PathType Container)) { continue }
  $parts = @(Get-ChildItem "$directory/part_*.b64" -File | Sort-Object Name)
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
    Copy-Item $candidatePath 'Services/FrameMerger.cs' -Force
    $frameApplied = $true
    break
  }
}
if (-not $frameApplied) { throw 'Final FrameMerger payload with expected SHA-256 was not found.' }

$expectedHashes = [ordered]@{
  'App.xaml.cs' = 'ac0cb274748135530680b379e53038858ffc169cec91dec5581b8f1c8e29bfd7'
  'MainWindow.xaml.cs' = 'fb2d8363a798eed6ba03306e228d70f4aec26e84106c044e63adbe674e9b194c'
  'Services/BatchReportGenerator.cs' = '7221cd7cfc95cc61799765f83e0544e995bc5082ebc5bd4fba51d4b364d0c320'
  'Services/BatchReportWriter.cs' = '4b8f68375f9bcecf09f74ae8975a20130b9a359557dc70c0b95b852a335c77cc'
  'Services/DicomDirWriter.cs' = '9815194992fed6a9278d42c3586b079c931d2546ede729a79b43a68250003654'
  'Services/DicomScanner.cs' = '89a84444d67a122a808f82bea4bbc28791550ed66fb26618c89331dd5e89da75'
  'Services/DiffusionBValueHelper.cs' = '2cb70b7b0b94945406ce2aa22f766842ec4c8d411ab351d05a04121742861828'
  'Services/FrameMerger.cs' = '6caa2ac2a163d21599b47b99322b70574b6fc3244390b665764a992b6fe2e82e'
  'Services/FrameSplitter.cs' = 'dcc2899b5d9fea06a24692910756ee07e5ea3805ff528246f97ea2f8fdcd0590'
  'Services/NiftiConverter.cs' = 'd44d75c52e132ae86020f941c7fbd76041b6cfc3209b8855c94909848dfd7878'
  'Services/SeriesDeidentifier.cs' = 'f17ee2185b3d4ff25698c58375f6fe58bea2e1a5bd32805511345efae6f7f62a'
  'Services/SevenZipHelper.cs' = 'e9d61998b39cc91e06ad53073ad50f048a6126dd41f3d4f2e083afa0a0087f93'
}
foreach ($entry in $expectedHashes.GetEnumerator()) {
  $actual = (Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actual -ne $entry.Value) { throw "Hash mismatch for $($entry.Key): $actual" }
}

$productionFiles = @(
  '.gitattributes','App.xaml','App.xaml.cs','BrainLab.md','Helpers/NaturalSort.cs','MainWindow.xaml','MainWindow.xaml.cs',
  'Models/DicomModels.cs','Models/DicomTagEntry.cs','Models/ReviewItemViewModel.cs','NewDicomMerger.csproj','README.md',
  'Services/BatchReportGenerator.cs','Services/BatchReportWriter.cs','Services/DicomDirWriter.cs','Services/DicomScanner.cs',
  'Services/DiffusionBValueHelper.cs','Services/FrameMerger.cs','Services/FrameSplitter.cs','Services/LruCache.cs',
  'Services/NiftiConverter.cs','Services/SeriesDeidentifier.cs','Services/SevenZipHelper.cs','Tools/7za.exe','Tools/dcm2niix.exe',
  'app.manifest','app_icon.ico','icon.ico','make_icon.ps1','project_context_map.md','project_swarm_matrix.md'
)
foreach ($file in $productionFiles) {
  if (-not (Test-Path $file -PathType Leaf) -or (Get-Item $file).Length -eq 0) { throw "Production file missing or empty: $file" }
}

Remove-Item 'BugHuntHarness' -Recurse -Force -ErrorAction SilentlyContinue
$temporaryPaths = @(
  '.bughunt-fixes','.bughunt-frame','.bughunt-frame-v2','.bughunt-hotfix','.build-overlay','.build-overlay-supplement','.final-source-overlay',
  '.bughunt-trigger','.final-corrections-trigger','.final-pr-trigger','.final-pr-trigger-2','.final-pr-trigger-3','.final-pr-trigger-4',
  '.net10-pr-trigger','.net10-single-exe-trigger','.apply-final-source-trigger','.apply-final-source-main-trigger',
  '.github/workflows/net10-bughunt.yml','.github/workflows/net10-final-corrections.yml','.github/workflows/net10-final-pr-validation.yml',
  '.github/workflows/net10-single-exe-build.yml','.github/workflows/net10-single-exe.yml','.github/workflows/apply-final-source.yml',
  '.github/workflows/apply-final-source-main.yml','scripts/apply-final-corrections.ps1','scripts/materialize-final-source.ps1'
)
foreach ($path in $temporaryPaths) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue }

New-Item -ItemType Directory -Force '.github/workflows' | Out-Null
$mainWorkflow = @'
name: .NET 10 Build

on:
  push:
    branches:
      - main
  pull_request:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  build:
    runs-on: windows-latest
    timeout-minutes: 60
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x
      - name: Restore
        shell: pwsh
        run: dotnet restore NewDicomMerger.csproj -r win-x64
      - name: Build
        shell: pwsh
        run: dotnet build NewDicomMerger.csproj -c Release -r win-x64 --no-restore -p:ContinuousIntegrationBuild=true -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest-all -p:TreatWarningsAsErrors=false
      - name: Publish Single EXE
        shell: pwsh
        run: dotnet publish NewDicomMerger.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish
      - name: Validate
        shell: pwsh
        run: |
          $files = @(Get-ChildItem artifacts/publish -File)
          if ($files.Count -ne 1 -or $files[0].Name -ne 'NewDicomMerger.exe') { throw 'Single-EXE validation failed.' }
          $stream = [IO.File]::OpenRead($files[0].FullName)
          try { if ($stream.ReadByte() -ne 0x4D -or $stream.ReadByte() -ne 0x5A) { throw 'Invalid PE header.' } } finally { $stream.Dispose() }
          Get-FileHash $files[0].FullName -Algorithm SHA256 | Format-List
      - uses: actions/upload-artifact@v4
        with:
          name: NewDicomMerger-net10-win-x64-single-exe
          path: artifacts/publish/NewDicomMerger.exe
          if-no-files-found: error
'@
[IO.File]::WriteAllText((Join-Path $root '.github/workflows/net10-build.yml'),$mainWorkflow,[Text.UTF8Encoding]::new($false))

$remainingPayloads = @(Get-ChildItem -Force | Where-Object { $_.Name -match '^\.(bughunt|build-overlay|final-source)' })
if ($remainingPayloads.Count -ne 0) { throw "Temporary payloads remain: $($remainingPayloads.Name -join ', ')" }

Write-Host 'Final production source reconstructed, patched, verified, and cleaned.'
