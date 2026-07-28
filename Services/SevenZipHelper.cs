using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace NewDicomMerger.Services;

public sealed class SevenZipHelper
{
    private const string EmbeddedExecutableResourceName = "NewDicomMerger.Tools.7za.exe";
    private const int CopyBufferSize = 131072;
    private static readonly SemaphoreSlim ExtractionGate = new(1, 1);
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer EntryComparer = StringComparer.OrdinalIgnoreCase;
    private readonly Action<string> _onLog;
    private readonly Action<string> _onError;

    public SevenZipHelper(Action<string> onLog, Action<string> onError)
    {
        _onLog = onLog ?? throw new ArgumentNullException(nameof(onLog));
        _onError = onError ?? throw new ArgumentNullException(nameof(onError));
    }

    public string? LastError { get; private set; }

    public static async Task<string> EnsureExecutableExtractedAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await ExtractionGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            await using Stream resourceStream = assembly.GetManifestResourceStream(EmbeddedExecutableResourceName)
                ?? throw new FileNotFoundException("Eingebettetes 7za.exe wurde nicht in den Ressourcen gefunden.");

            long resourceLength = resourceStream.CanSeek ? resourceStream.Length : 0;
            string toolVersion = resourceLength > 0 ? resourceLength.ToString("X") : "embedded";
            string toolDirectory = Path.Combine(Path.GetTempPath(), "NewDicomMerger", $"Tools_{toolVersion}");
            string executablePath = Path.Combine(toolDirectory, "7za.exe");

            Directory.CreateDirectory(toolDirectory);

            if (File.Exists(executablePath))
            {
                long existingLength = new FileInfo(executablePath).Length;

                if (existingLength > 0 && (resourceLength <= 0 || existingLength == resourceLength))
                    return executablePath;
            }

            string temporaryExecutablePath = Path.Combine(toolDirectory, $"7za.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var outputStream = new FileStream(
                    temporaryExecutablePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    CopyBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await resourceStream.CopyToAsync(outputStream, CopyBufferSize, ct).ConfigureAwait(false);
                    await outputStream.FlushAsync(ct).ConfigureAwait(false);
                }

                if (!File.Exists(temporaryExecutablePath) || new FileInfo(temporaryExecutablePath).Length == 0)
                    throw new IOException("Die extrahierte 7za.exe ist leer.");

                File.Move(temporaryExecutablePath, executablePath, true);

                if (!File.Exists(executablePath) || new FileInfo(executablePath).Length == 0)
                    throw new FileNotFoundException(
                        "7za.exe konnte nicht in das temporäre Werkzeugverzeichnis extrahiert werden.",
                        executablePath);

                return executablePath;
            }
            finally
            {
                DeleteFileQuietly(temporaryExecutablePath);
            }
        }
        finally
        {
            ExtractionGate.Release();
        }
    }

    public async Task<bool> CompressToZipUltraAsync(
        string sourceDirectoryOrFile,
        string destinationZipPath,
        CancellationToken ct = default)
    {
        LastError = null;

        try
        {
            ct.ThrowIfCancellationRequested();

            string sourcePath = NormalizeExistingPath(sourceDirectoryOrFile);
            string destinationPath = NormalizeDestinationPath(destinationZipPath);

            IReadOnlyList<ArchiveManifestEntry> manifest = Directory.Exists(sourcePath)
                ? BuildDirectoryManifest(sourcePath, destinationPath)
                : BuildSingleFileManifest(sourcePath, destinationPath);

            return await CompressManifestAsync(manifest, destinationPath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail($"ZIP-Erstellung fehlgeschlagen: {ex.Message}");
        }
    }

    public async Task<bool> CompressFilesToZipUltraAsync(
        IEnumerable<string> paths,
        string destinationZipPath,
        CancellationToken ct = default)
    {
        LastError = null;

        try
        {
            ct.ThrowIfCancellationRequested();

            if (paths == null)
                throw new ArgumentNullException(nameof(paths));

            string destinationPath = NormalizeDestinationPath(destinationZipPath);
            IReadOnlyList<ArchiveManifestEntry> manifest = BuildExplicitManifest(paths, destinationPath);

            return await CompressManifestAsync(manifest, destinationPath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail($"ZIP-Erstellung fehlgeschlagen: {ex.Message}");
        }
    }

    private async Task<bool> CompressManifestAsync(
        IReadOnlyList<ArchiveManifestEntry> manifest,
        string destinationZipPath,
        CancellationToken ct)
    {
        if (manifest.Count == 0)
            return Fail("ZIP-Erstellung abgebrochen: Keine archivierungsfähigen Dateien gefunden.");

        ct.ThrowIfCancellationRequested();

        string destinationDirectory = Path.GetDirectoryName(destinationZipPath)
            ?? throw new InvalidOperationException(
                "Das Zielverzeichnis des ZIP-Archivs konnte nicht bestimmt werden.");

        Directory.CreateDirectory(destinationDirectory);

        string temporaryArchivePath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileNameWithoutExtension(destinationZipPath)}.{Guid.NewGuid():N}.tmp.zip");

        DeleteFileQuietly(temporaryArchivePath);

        try
        {
            bool sevenZipSucceeded = false;
            string sevenZipDetails = string.Empty;

            try
            {
                string executablePath = await EnsureExecutableExtractedAsync(ct).ConfigureAwait(false);

                ProcessResult result = await RunSevenZipCreateAsync(
                        executablePath,
                        manifest,
                        temporaryArchivePath,
                        ct)
                    .ConfigureAwait(false);

                sevenZipSucceeded =
                    result.ExitCode is 0 or 1 &&
                    File.Exists(temporaryArchivePath) &&
                    new FileInfo(temporaryArchivePath).Length > 0;

                sevenZipDetails = BuildProcessDetails(result);

                if (!sevenZipSucceeded)
                {
                    _onError(
                        $"7-Zip konnte das Archiv nicht vollständig erstellen. {sevenZipDetails}".Trim());

                    DeleteFileQuietly(temporaryArchivePath);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sevenZipDetails = ex.Message;
                _onError($"7-Zip-Ausführung fehlgeschlagen: {ex.Message}");
                DeleteFileQuietly(temporaryArchivePath);
            }

            bool usedFallback = !sevenZipSucceeded;

            if (sevenZipSucceeded)
            {
                try
                {
                    await ValidateArchiveAsync(temporaryArchivePath, manifest, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    usedFallback = true;

                    sevenZipDetails = string.IsNullOrWhiteSpace(sevenZipDetails)
                        ? ex.Message
                        : $"{sevenZipDetails} | Archivvalidierung: {ex.Message}";

                    _onError(
                        $"Das von 7-Zip erzeugte Archiv war unvollständig oder ungültig: {ex.Message}");

                    DeleteFileQuietly(temporaryArchivePath);
                }
            }

            if (usedFallback)
            {
                await CreateFallbackArchiveAsync(manifest, temporaryArchivePath, ct)
                    .ConfigureAwait(false);

                await ValidateArchiveAsync(temporaryArchivePath, manifest, ct)
                    .ConfigureAwait(false);
            }

            CommitArchive(temporaryArchivePath, destinationZipPath);

            await ValidateArchiveAsync(destinationZipPath, manifest, ct)
                .ConfigureAwait(false);

            long archiveBytes = new FileInfo(destinationZipPath).Length;
            string mode = usedFallback ? "ZIP-Fallback" : "7-Zip Ultra (Stufe 9)";

            _onLog(
                $"✓ {mode} erfolgreich erstellt und validiert: " +
                $"{Path.GetFileName(destinationZipPath)} " +
                $"({manifest.Count} Datei(en), {FormatBytes(archiveBytes)})");

            if (usedFallback && !string.IsNullOrWhiteSpace(sevenZipDetails))
                _onLog($"7-Zip-Hinweis: {sevenZipDetails}");

            LastError = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            DeleteFileQuietly(temporaryArchivePath);
            throw;
        }
        catch (Exception ex)
        {
            DeleteFileQuietly(temporaryArchivePath);

            return Fail(
                $"ZIP-Erstellung oder Archivvalidierung fehlgeschlagen: {ex.Message}");
        }
    }

    private async Task<ProcessResult> RunSevenZipCreateAsync(
        string executablePath,
        IReadOnlyList<ArchiveManifestEntry> manifest,
        string temporaryArchivePath,
        CancellationToken ct)
    {
        string workingDirectory = GetManifestBaseDirectory(manifest);
        string listFilePath = Path.Combine(
            Path.GetTempPath(),
            $"NewDicomMerger_7z_{Guid.NewGuid():N}.lst");

        try
        {
            string[] listEntries = manifest
                .Select(item => Path.GetRelativePath(workingDirectory, item.SourcePath))
                .Select(ValidateRelativeInputPath)
                .Select(path => $".{Path.DirectorySeparatorChar}{path}")
                .ToArray();

            await File.WriteAllLinesAsync(
                    listFilePath,
                    listEntries,
                    new UTF8Encoding(false),
                    ct)
                .ConfigureAwait(false);

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("a");
            startInfo.ArgumentList.Add("-tzip");
            startInfo.ArgumentList.Add("-mx=9");
            startInfo.ArgumentList.Add("-mm=Deflate");
            startInfo.ArgumentList.Add("-mfb=258");
            startInfo.ArgumentList.Add("-mpass=15");
            startInfo.ArgumentList.Add("-mmt=on");
            startInfo.ArgumentList.Add("-scsUTF-8");
            startInfo.ArgumentList.Add("-sccUTF-8");
            startInfo.ArgumentList.Add("-bd");
            startInfo.ArgumentList.Add("-bb1");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add(temporaryArchivePath);
            startInfo.ArgumentList.Add($"@{listFilePath}");

            _onLog(
                $"Führe 7-Zip-Kompression Stufe 9 Ultra für {manifest.Count} Datei(en) aus...");

            using var process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
                throw new InvalidOperationException(
                    "7-Zip-Prozess konnte nicht gestartet werden.");

            using CancellationTokenRegistration registration = ct.Register(
                static state =>
                {
                    if (state is not Process runningProcess)
                        return;

                    try
                    {
                        if (!runningProcess.HasExited)
                            runningProcess.Kill(true);
                    }
                    catch
                    {
                    }
                },
                process);

            Task<string> standardOutputTask =
                process.StandardOutput.ReadToEndAsync(ct);

            Task<string> standardErrorTask =
                process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            string standardOutput =
                await standardOutputTask.ConfigureAwait(false);

            string standardError =
                await standardErrorTask.ConfigureAwait(false);

            return new ProcessResult(
                process.ExitCode,
                standardOutput,
                standardError);
        }
        finally
        {
            DeleteFileQuietly(listFilePath);
        }
    }

    private static async Task CreateFallbackArchiveAsync(
        IReadOnlyList<ArchiveManifestEntry> manifest,
        string temporaryArchivePath,
        CancellationToken ct)
    {
        DeleteFileQuietly(temporaryArchivePath);

        await using var archiveStream = new FileStream(
            temporaryArchivePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var archive = new ZipArchive(
            archiveStream,
            ZipArchiveMode.Create,
            false,
            Encoding.UTF8);

        foreach (ArchiveManifestEntry item in manifest)
        {
            ct.ThrowIfCancellationRequested();

            var sourceInfo = new FileInfo(item.SourcePath);

            ZipArchiveEntry entry = archive.CreateEntry(
                item.EntryName,
                CompressionLevel.SmallestSize);

            entry.LastWriteTime = ToValidZipTimestamp(sourceInfo.LastWriteTime);

            await using Stream entryStream = entry.Open();

            await using var sourceStream = new FileStream(
                item.SourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await sourceStream.CopyToAsync(
                    entryStream,
                    CopyBufferSize,
                    ct)
                .ConfigureAwait(false);
        }
    }

    private static async Task ValidateArchiveAsync(
        string archivePath,
        IReadOnlyList<ArchiveManifestEntry> manifest,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(archivePath))
            throw new FileNotFoundException(
                "Das erzeugte ZIP-Archiv wurde nicht gefunden.",
                archivePath);

        if (new FileInfo(archivePath).Length == 0)
            throw new InvalidDataException(
                "Das erzeugte ZIP-Archiv ist leer.");

        using ZipArchive archive = ZipFile.OpenRead(archivePath);

        var actualEntries =
            new Dictionary<string, ZipArchiveEntry>(EntryComparer);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            string normalizedName =
                NormalizeArchiveEntryName(entry.FullName);

            if (string.IsNullOrEmpty(normalizedName) ||
                normalizedName.EndsWith('/'))
            {
                continue;
            }

            EnsureSafeArchiveEntryName(normalizedName);

            if (!actualEntries.TryAdd(normalizedName, entry))
            {
                throw new InvalidDataException(
                    $"Das ZIP-Archiv enthält den Eintrag mehrfach: {normalizedName}");
            }
        }

        if (actualEntries.Count != manifest.Count)
        {
            throw new InvalidDataException(
                $"Das ZIP-Archiv enthält {actualEntries.Count} Datei(en), " +
                $"erwartet wurden {manifest.Count}.");
        }

        foreach (ArchiveManifestEntry expected in manifest)
        {
            ct.ThrowIfCancellationRequested();

            if (!actualEntries.TryGetValue(
                    expected.EntryName,
                    out ZipArchiveEntry? actual))
            {
                throw new InvalidDataException(
                    $"Der erwartete ZIP-Eintrag fehlt: {expected.EntryName}");
            }

            var currentSourceInfo = new FileInfo(expected.SourcePath);
            long currentSourceLength = currentSourceInfo.Length;

            if (currentSourceLength != expected.Length ||
                currentSourceInfo.LastWriteTimeUtc != expected.LastWriteTimeUtc)
            {
                throw new IOException(
                    $"Die Quelldatei wurde während der Archivierung verändert: " +
                    $"{expected.SourcePath}");
            }

            if (actual.Length != expected.Length)
            {
                throw new InvalidDataException(
                    $"Größenabweichung im ZIP-Eintrag {expected.EntryName}: " +
                    $"erwartet {expected.Length} Byte, " +
                    $"enthalten {actual.Length} Byte.");
            }

            await using Stream entryStream = actual.Open();

            await entryStream.CopyToAsync(
                    Stream.Null,
                    CopyBufferSize,
                    ct)
                .ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<ArchiveManifestEntry> BuildDirectoryManifest(
        string sourceDirectory,
        string destinationZipPath)
    {
        string normalizedDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(sourceDirectory));

        var manifest =
            new Dictionary<string, ArchiveManifestEntry>(EntryComparer);

        foreach (string filePath in EnumerateFiles(normalizedDirectory))
        {
            if (PathComparer.Equals(filePath, destinationZipPath))
                continue;

            string entryName = NormalizeAndValidateEntryName(
                Path.GetRelativePath(normalizedDirectory, filePath));

            AddManifestEntry(manifest, filePath, entryName);
        }

        return FinalizeManifest(manifest);
    }

    private static IReadOnlyList<ArchiveManifestEntry> BuildSingleFileManifest(
        string sourceFile,
        string destinationZipPath)
    {
        string normalizedFile = Path.GetFullPath(sourceFile);

        if (PathComparer.Equals(normalizedFile, destinationZipPath))
        {
            throw new InvalidOperationException(
                "Quelldatei und Zielarchiv dürfen nicht identisch sein.");
        }

        string entryName = NormalizeAndValidateEntryName(
            Path.GetFileName(normalizedFile));

        return new[]
        {
            CreateManifestEntry(normalizedFile, entryName)
        };
    }

    private static IReadOnlyList<ArchiveManifestEntry> BuildExplicitManifest(
        IEnumerable<string> paths,
        string destinationZipPath)
    {
        List<string> normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Where(path =>
                !PathComparer.Equals(
                    Path.TrimEndingDirectorySeparator(path),
                    destinationZipPath))
            .Distinct(PathComparer)
            .ToList();

        if (normalizedPaths.Count == 0)
        {
            throw new InvalidOperationException(
                "Keine gültigen Dateien oder Verzeichnisse für die ZIP-Erstellung gefunden.");
        }

        List<string> anchorDirectories = normalizedPaths
            .Select(path =>
                File.Exists(path)
                    ? Path.GetDirectoryName(path)!
                    : Directory
                          .GetParent(Path.TrimEndingDirectorySeparator(path))
                          ?.FullName
                      ?? Path.GetPathRoot(path)!)
            .ToList();

        string commonBaseDirectory =
            GetCommonBaseDirectory(anchorDirectories);

        var manifest =
            new Dictionary<string, ArchiveManifestEntry>(EntryComparer);

        foreach (string path in normalizedPaths)
        {
            if (File.Exists(path))
            {
                if (PathComparer.Equals(path, destinationZipPath))
                    continue;

                string entryName = NormalizeAndValidateEntryName(
                    Path.GetRelativePath(commonBaseDirectory, path));

                AddManifestEntry(manifest, path, entryName);
                continue;
            }

            foreach (string filePath in EnumerateFiles(path))
            {
                if (PathComparer.Equals(filePath, destinationZipPath))
                    continue;

                string entryName = NormalizeAndValidateEntryName(
                    Path.GetRelativePath(commonBaseDirectory, filePath));

                AddManifestEntry(manifest, filePath, entryName);
            }
        }

        return FinalizeManifest(manifest);
    }

    private static IEnumerable<string> EnumerateFiles(
        string directoryPath)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchType = MatchType.Simple,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        return Directory
            .EnumerateFiles(directoryPath, "*", options)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, PathComparer);
    }

    private static void AddManifestEntry(
        IDictionary<string, ArchiveManifestEntry> manifest,
        string sourcePath,
        string entryName)
    {
        ArchiveManifestEntry candidate =
            CreateManifestEntry(sourcePath, entryName);

        if (manifest.TryGetValue(
                candidate.EntryName,
                out ArchiveManifestEntry? existing))
        {
            if (PathComparer.Equals(
                    existing.SourcePath,
                    candidate.SourcePath))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Mehrere Quelldateien würden denselben ZIP-Eintrag erzeugen: " +
                $"{candidate.EntryName}");
        }

        manifest.Add(candidate.EntryName, candidate);
    }

    private static ArchiveManifestEntry CreateManifestEntry(
        string sourcePath,
        string entryName)
    {
        string normalizedSourcePath = Path.GetFullPath(sourcePath);
        var info = new FileInfo(normalizedSourcePath);

        if (!info.Exists)
        {
            throw new FileNotFoundException(
                "Eine zu archivierende Quelldatei wurde nicht gefunden.",
                normalizedSourcePath);
        }

        return new ArchiveManifestEntry(
            normalizedSourcePath,
            entryName,
            info.Length,
            info.LastWriteTimeUtc);
    }

    private static IReadOnlyList<ArchiveManifestEntry> FinalizeManifest(
        IDictionary<string, ArchiveManifestEntry> manifest)
    {
        if (manifest.Count == 0)
        {
            throw new InvalidOperationException(
                "Die ausgewählten Quellen enthalten keine archivierungsfähigen Dateien.");
        }

        return manifest.Values
            .OrderBy(item => item.EntryName, EntryComparer)
            .ToArray();
    }

    private static string GetManifestBaseDirectory(
        IReadOnlyList<ArchiveManifestEntry> manifest)
    {
        if (manifest.Count == 0)
        {
            throw new InvalidOperationException(
                "Für die ZIP-Erstellung ist kein Archivmanifest vorhanden.");
        }

        string firstEntryPath = manifest[0]
            .EntryName
            .Replace('/', Path.DirectorySeparatorChar);

        string[] firstSegments = firstEntryPath.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        if (firstSegments.Length == 0)
        {
            throw new InvalidOperationException(
                "Der erste ZIP-Eintragsname ist ungültig.");
        }

        DirectoryInfo? baseDirectory =
            new FileInfo(manifest[0].SourcePath).Directory;

        for (int i = 1; i < firstSegments.Length; i++)
        {
            baseDirectory = baseDirectory?.Parent;

            if (baseDirectory == null)
            {
                throw new InvalidOperationException(
                    "Das Basisverzeichnis des ZIP-Manifests konnte nicht bestimmt werden.");
            }
        }

        string normalizedBaseDirectory =
            Path.TrimEndingDirectorySeparator(baseDirectory.FullName);

        foreach (ArchiveManifestEntry item in manifest)
        {
            string relativePath = item.EntryName.Replace(
                '/',
                Path.DirectorySeparatorChar);

            string reconstructedPath = Path.GetFullPath(
                Path.Combine(normalizedBaseDirectory, relativePath));

            if (!PathComparer.Equals(
                    reconstructedPath,
                    item.SourcePath))
            {
                throw new InvalidOperationException(
                    $"Der ZIP-Eintragsname lässt sich nicht eindeutig auf die " +
                    $"Quelldatei abbilden: {item.EntryName}");
            }
        }

        return normalizedBaseDirectory;
    }

    private static string GetCommonBaseDirectory(
        IReadOnlyList<string> directories)
    {
        if (directories.Count == 0)
        {
            throw new InvalidOperationException(
                "Für die ZIP-Erstellung konnte kein gemeinsames Basisverzeichnis bestimmt werden.");
        }

        string commonDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(directories[0]));

        string commonRoot = Path.GetPathRoot(commonDirectory)
            ?? throw new InvalidOperationException(
                "Das Wurzelverzeichnis konnte nicht bestimmt werden.");

        for (int i = 1; i < directories.Count; i++)
        {
            string candidate = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(directories[i]));

            string candidateRoot = Path.GetPathRoot(candidate)
                ?? throw new InvalidOperationException(
                    "Das Wurzelverzeichnis einer Quelle konnte nicht bestimmt werden.");

            if (!PathComparer.Equals(commonRoot, candidateRoot))
            {
                throw new InvalidOperationException(
                    "Dateien von unterschiedlichen Laufwerken können nicht gemeinsam " +
                    "in diesem ZIP-Vorgang verarbeitet werden.");
            }

            while (!IsPathWithinOrEqual(commonDirectory, candidate))
            {
                DirectoryInfo? parent =
                    Directory.GetParent(commonDirectory);

                if (parent == null)
                {
                    throw new InvalidOperationException(
                        "Für die ausgewählten Quellen konnte kein gemeinsames " +
                        "Basisverzeichnis bestimmt werden.");
                }

                commonDirectory =
                    Path.TrimEndingDirectorySeparator(parent.FullName);
            }
        }

        return commonDirectory;
    }

    private static bool IsPathWithinOrEqual(
        string baseDirectory,
        string candidatePath)
    {
        string relativePath =
            Path.GetRelativePath(baseDirectory, candidatePath);

        if (relativePath == ".")
            return true;

        return
            !Path.IsPathRooted(relativePath) &&
            relativePath != ".." &&
            !relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) &&
            !relativePath.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal);
    }

    private static string NormalizeExistingPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Der Quellpfad darf nicht leer sein.",
                nameof(path));
        }

        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath) &&
            !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Die angegebene ZIP-Quelle wurde nicht gefunden.",
                fullPath);
        }

        return Directory.Exists(fullPath)
            ? Path.TrimEndingDirectorySeparator(fullPath)
            : fullPath;
    }

    private static string NormalizeDestinationPath(
        string destinationZipPath)
    {
        if (string.IsNullOrWhiteSpace(destinationZipPath))
        {
            throw new ArgumentException(
                "Der Zielpfad des ZIP-Archivs darf nicht leer sein.",
                nameof(destinationZipPath));
        }

        string fullPath = Path.GetFullPath(destinationZipPath);

        if (Directory.Exists(fullPath))
        {
            throw new IOException(
                "Der Zielpfad des ZIP-Archivs verweist auf ein Verzeichnis.");
        }

        return fullPath;
    }

    private static string ValidateRelativeInputPath(
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath == ".")
        {
            throw new InvalidOperationException(
                "Ein relativer Eingabepfad für 7-Zip ist ungültig.");
        }

        if (Path.IsPathRooted(relativePath) ||
            relativePath == ".." ||
            relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            relativePath.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            relativePath.Contains('\r') ||
            relativePath.Contains('\n'))
        {
            throw new InvalidOperationException(
                $"Ein Eingabepfad liegt außerhalb des gemeinsamen " +
                $"Basisverzeichnisses: {relativePath}");
        }

        return relativePath;
    }

    private static string NormalizeAndValidateEntryName(
        string entryName)
    {
        string normalized =
            NormalizeArchiveEntryName(entryName);

        EnsureSafeArchiveEntryName(normalized);

        return normalized;
    }

    private static string NormalizeArchiveEntryName(
        string entryName)
    {
        string normalized =
            entryName.Replace('\\', '/').TrimStart('/');

        while (normalized.StartsWith(
                   "./",
                   StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static void EnsureSafeArchiveEntryName(
        string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) ||
            entryName.Contains('\r') ||
            entryName.Contains('\n') ||
            entryName.StartsWith("../", StringComparison.Ordinal) ||
            entryName.Equals("..", StringComparison.Ordinal) ||
            entryName.Contains("/../", StringComparison.Ordinal) ||
            Path.IsPathRooted(entryName))
        {
            throw new InvalidDataException(
                $"Unsicherer oder ungültiger ZIP-Eintragsname: {entryName}");
        }
    }

    private static void CommitArchive(
        string temporaryArchivePath,
        string destinationZipPath)
    {
        if (!File.Exists(temporaryArchivePath))
        {
            throw new FileNotFoundException(
                "Das validierte temporäre ZIP-Archiv wurde nicht gefunden.",
                temporaryArchivePath);
        }

        if (File.Exists(destinationZipPath))
        {
            try
            {
                File.Replace(
                    temporaryArchivePath,
                    destinationZipPath,
                    null,
                    true);

                return;
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        File.Move(
            temporaryArchivePath,
            destinationZipPath,
            true);
    }

    private bool Fail(string message)
    {
        LastError = message;
        _onError(message);
        return false;
    }

    private static string BuildProcessDetails(
        ProcessResult result)
    {
        string output = result.StandardOutput.Trim();
        string error = result.StandardError.Trim();

        string details = string.Join(
            " | ",
            new[]
            {
                error,
                output
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.IsNullOrWhiteSpace(details)
            ? $"Exit-Code {result.ExitCode}."
            : $"Exit-Code {result.ExitCode}: {details}";
    }

    private static DateTimeOffset ToValidZipTimestamp(
        DateTime timestamp)
    {
        DateTime localTimestamp =
            timestamp.Kind == DateTimeKind.Utc
                ? timestamp.ToLocalTime()
                : timestamp;

        DateTime minimum =
            new(1980, 1, 1, 0, 0, 0, DateTimeKind.Local);

        DateTime maximum =
            new(2107, 12, 31, 23, 59, 58, DateTimeKind.Local);

        if (localTimestamp < minimum)
            localTimestamp = minimum;
        else if (localTimestamp > maximum)
            localTimestamp = maximum;

        return new DateTimeOffset(localTimestamp);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        };

        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 &&
               unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{value:F2} {units[unitIndex]}";
    }

    private static void DeleteFileQuietly(
        string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record ArchiveManifestEntry(
        string SourcePath,
        string EntryName,
        long Length,
        DateTime LastWriteTimeUtc);

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}