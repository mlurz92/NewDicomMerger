$ErrorActionPreference = 'Stop'

$root = (Resolve-Path '.').Path
$parts = @(Get-ChildItem '.final-source-overlay/part_*.b64' -File | Sort-Object Name)
if ($parts.Count -ne 2) { throw "Expected 2 final source overlay parts, found $($parts.Count)." }

$base64 = (($parts | ForEach-Object { Get-Content $_.FullName -Raw }) -join '') -replace '\s', ''
$bytes = [Convert]::FromBase64String($base64)
if ($bytes.Length -lt 10000) { throw "Final source overlay is unexpectedly small: $($bytes.Length) bytes." }
if ($bytes[0] -ne 0x50 -or $bytes[1] -ne 0x4B) { throw 'Final source overlay has no ZIP signature.' }

$zipPath = Join-Path $env:RUNNER_TEMP 'final-source-overlay.zip'
$extractPath = Join-Path $env:RUNNER_TEMP 'final-source-overlay'
Remove-Item $extractPath -Recurse -Force -ErrorAction SilentlyContinue
[IO.File]::WriteAllBytes($zipPath, $bytes)
Expand-Archive -LiteralPath $zipPath -DestinationPath $extractPath -Force

$productionFiles = @(
  '.gitattributes',
  'App.xaml',
  'App.xaml.cs',
  'BrainLab.md',
  'Helpers/NaturalSort.cs',
  'MainWindow.xaml',
  'MainWindow.xaml.cs',
  'Models/DicomModels.cs',
  'Models/DicomTagEntry.cs',
  'Models/ReviewItemViewModel.cs',
  'NewDicomMerger.csproj',
  'README.md',
  'Services/BatchReportGenerator.cs',
  'Services/BatchReportWriter.cs',
  'Services/DicomDirWriter.cs',
  'Services/DicomScanner.cs',
  'Services/DiffusionBValueHelper.cs',
  'Services/FrameMerger.cs',
  'Services/FrameSplitter.cs',
  'Services/LruCache.cs',
  'Services/NiftiConverter.cs',
  'Services/SeriesDeidentifier.cs',
  'Services/SevenZipHelper.cs',
  'Tools/7za.exe',
  'Tools/dcm2niix.exe',
  'app.manifest',
  'app_icon.ico',
  'icon.ico',
  'make_icon.ps1',
  'project_context_map.md',
  'project_swarm_matrix.md'
)

foreach ($relativePath in $productionFiles) {
  $source = Join-Path $extractPath $relativePath
  if (-not (Test-Path $source -PathType Leaf)) { throw "Missing final source file: $relativePath" }
  $destination = Join-Path $root $relativePath
  $destinationDirectory = Split-Path $destination -Parent
  if ($destinationDirectory) { New-Item -ItemType Directory -Force $destinationDirectory | Out-Null }
  Copy-Item -LiteralPath $source -Destination $destination -Force
}

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

$temporaryPaths = @(
  '.bughunt-fixes',
  '.bughunt-frame',
  '.bughunt-frame-v2',
  '.bughunt-hotfix',
  '.build-overlay',
  '.build-overlay-supplement',
  '.final-source-overlay',
  '.bughunt-trigger',
  '.final-corrections-trigger',
  '.final-pr-trigger',
  '.final-pr-trigger-2',
  '.final-pr-trigger-3',
  '.final-pr-trigger-4',
  '.net10-pr-trigger',
  '.net10-single-exe-trigger',
  '.github/workflows/net10-bughunt.yml',
  '.github/workflows/net10-final-corrections.yml',
  '.github/workflows/net10-final-pr-validation.yml',
  '.github/workflows/net10-single-exe-build.yml',
  '.github/workflows/net10-single-exe.yml',
  '.github/workflows/apply-final-source.yml',
  'scripts/apply-final-corrections.ps1',
  'scripts/materialize-final-source.ps1'
)

foreach ($path in $temporaryPaths) {
  Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force '.github/workflows' | Out-Null
$mainWorkflow = @'
name: .NET 10 Build

on:
  push:
    branches:
      - main
      - agent/final-corrections-pr-base
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
[IO.File]::WriteAllText((Join-Path $root '.github/workflows/net10-build.yml'), $mainWorkflow, [Text.UTF8Encoding]::new($false))

$remainingPayloads = @(Get-ChildItem -Force | Where-Object { $_.Name -match '^\.(bughunt|build-overlay|final-source)' })
if ($remainingPayloads.Count -ne 0) { throw "Temporary payloads remain: $($remainingPayloads.Name -join ', ')" }

Write-Host 'Final production source materialized and verified.'
