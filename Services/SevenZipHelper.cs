using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace NewDicomMerger.Services;

public class SevenZipHelper
{
    private readonly Action<string> _onLog;
    private readonly Action<string> _onError;

    public SevenZipHelper(Action<string> onLog, Action<string> onError)
    {
        _onLog = onLog;
        _onError = onError;
    }

    /// <summary>
    /// Ensures 7za.exe is extracted from embedded resources to %TEMP%\NewDicomMerger\Tools_v1.0\7za.exe.
    /// </summary>
    public static async Task<string> EnsureExecutableExtractedAsync()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), "NewDicomMerger", "Tools_v1.0");
        Directory.CreateDirectory(tempPath);
        string exePath = Path.Combine(tempPath, "7za.exe");

        if (!File.Exists(exePath))
        {
            var assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream("NewDicomMerger.Tools.7za.exe");
            if (stream == null)
            {
                throw new FileNotFoundException("Eingebettetes 7za.exe wurde nicht in den Ressourcen gefunden.");
            }
            try
            {
                using var fileStream = new FileStream(exePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
            }
            catch (IOException)
            {
                // File might be locked by another parallel process
            }

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("7za.exe konnte nicht nach %TEMP% extrahiert werden.", exePath);
            }
        }

        return exePath;
    }

    /// <summary>
    /// Packs a directory or list of files into a .zip archive using 7-Zip Level 9 Ultra (-tzip -mx=9).
    /// </summary>
    public async Task<bool> CompressToZipUltraAsync(string sourceDirectoryOrFile, string destinationZipPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (File.Exists(destinationZipPath))
        {
            try { File.Delete(destinationZipPath); } catch { }
        }

        try
        {
            string exePath = await EnsureExecutableExtractedAsync();

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Command: 7za a -tzip -mx=9 -bd -y -xr!*.zip "dest.zip" "sourceDirectory\*"
            startInfo.ArgumentList.Add("a");
            startInfo.ArgumentList.Add("-tzip");
            startInfo.ArgumentList.Add("-mx=9");
            startInfo.ArgumentList.Add("-bd");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-xr!*.zip");
            startInfo.ArgumentList.Add(destinationZipPath);

            if (Directory.Exists(sourceDirectoryOrFile))
            {
                startInfo.ArgumentList.Add(Path.Combine(sourceDirectoryOrFile, "*"));
            }
            else
            {
                startInfo.ArgumentList.Add(sourceDirectoryOrFile);
            }

            _onLog($"Führe 7-Zip Kompression (Stufe 9-Ultra) aus für {Path.GetFileName(destinationZipPath)}...");

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            using var registration = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); }
                catch { }
            });

            var readOut = process.StandardOutput.ReadToEndAsync(ct);
            var readErr = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);
            string output = await readOut;
            string error = await readErr;

            if (process.ExitCode == 0 && File.Exists(destinationZipPath))
            {
                _onLog($"✓ 7-Zip Ultra (Stufe 9) ZIP erfolgreich erstellt: {Path.GetFileName(destinationZipPath)}");
                return true;
            }
            else
            {
                _onError($"7-Zip beendet mit Exit Code {process.ExitCode}: {error} {output}");
                // Fallback to standard ZipFile if 7za failed
                return FallbackZip(sourceDirectoryOrFile, destinationZipPath);
            }
        }
        catch (Exception ex)
        {
            _onError($"7-Zip Ausführungsfehler: {ex.Message}. Verwende Standard-ZIP-Fallback.");
            return FallbackZip(sourceDirectoryOrFile, destinationZipPath);
        }
    }

    private bool FallbackZip(string sourceDirectoryOrFile, string destinationZipPath)
    {
        try
        {
            if (File.Exists(destinationZipPath)) File.Delete(destinationZipPath);
            string tempZipPath = destinationZipPath + ".tmp";
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

            if (Directory.Exists(sourceDirectoryOrFile))
            {
                using (var archive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
                {
                    var files = Directory.GetFiles(sourceDirectoryOrFile, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (file.Equals(destinationZipPath, StringComparison.OrdinalIgnoreCase) || file.Equals(tempZipPath, StringComparison.OrdinalIgnoreCase) || file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            continue;
                        string relPath = Path.GetRelativePath(sourceDirectoryOrFile, file);
                        archive.CreateEntryFromFile(file, relPath, CompressionLevel.Optimal);
                    }
                }
            }
            else if (File.Exists(sourceDirectoryOrFile))
            {
                using var archive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create);
                archive.CreateEntryFromFile(sourceDirectoryOrFile, Path.GetFileName(sourceDirectoryOrFile), CompressionLevel.Optimal);
            }

            if (File.Exists(tempZipPath))
            {
                File.Move(tempZipPath, destinationZipPath, true);
            }

            _onLog($"✓ ZIP-Archiv (Fallback) erstellt: {Path.GetFileName(destinationZipPath)}");
            return true;
        }
        catch (Exception ex)
        {
            _onError($"ZIP-Fallback Fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Packs an explicit list of files or folders into a .zip archive using 7-Zip Level 9 Ultra (-tzip -mx=9).
    /// </summary>
    public async Task<bool> CompressFilesToZipUltraAsync(IEnumerable<string> paths, string destinationZipPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var validPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (validPaths.Count == 0) return false;

        if (File.Exists(destinationZipPath))
        {
            try { File.Delete(destinationZipPath); } catch { }
        }

        string tempListFile = Path.Combine(Path.GetTempPath(), $"7z_list_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllLinesAsync(tempListFile, validPaths, System.Text.Encoding.UTF8, ct);

            string exePath = await EnsureExecutableExtractedAsync();

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Command: 7za a -tzip -mx=9 -bd -y "dest.zip" "@listfile.txt"
            startInfo.ArgumentList.Add("a");
            startInfo.ArgumentList.Add("-tzip");
            startInfo.ArgumentList.Add("-mx=9");
            startInfo.ArgumentList.Add("-bd");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add(destinationZipPath);
            startInfo.ArgumentList.Add($"@{tempListFile}");

            _onLog($"Führe 7-Zip Kompression (Stufe 9-Ultra) für {validPaths.Count} konvertierte Datei(en) aus...");

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            using var registration = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); }
                catch { }
            });

            var readOut = process.StandardOutput.ReadToEndAsync(ct);
            var readErr = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);
            string output = await readOut;
            string error = await readErr;

            if (process.ExitCode == 0 && File.Exists(destinationZipPath))
            {
                _onLog($"✓ 7-Zip Ultra (Stufe 9) ZIP erfolgreich erstellt ({validPaths.Count} Datei(en)): {Path.GetFileName(destinationZipPath)}");
                return true;
            }
            else
            {
                _onError($"7-Zip beendet mit Exit Code {process.ExitCode}: {error} {output}");
                return FallbackZipFiles(validPaths, destinationZipPath);
            }
        }
        catch (Exception ex)
        {
            _onError($"7-Zip Ausführungsfehler: {ex.Message}. Verwende Standard-ZIP-Fallback.");
            return FallbackZipFiles(validPaths, destinationZipPath);
        }
        finally
        {
            try { if (File.Exists(tempListFile)) File.Delete(tempListFile); } catch { }
        }
    }

    private bool FallbackZipFiles(List<string> paths, string destinationZipPath)
    {
        try
        {
            if (File.Exists(destinationZipPath)) File.Delete(destinationZipPath);

            using var archive = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    archive.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
                }
                else if (Directory.Exists(path))
                {
                    var subFiles = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    string baseDirName = Path.GetFileName(path);
                    foreach (var subFile in subFiles)
                    {
                        string relPath = Path.Combine(baseDirName, Path.GetRelativePath(path, subFile));
                        archive.CreateEntryFromFile(subFile, relPath, CompressionLevel.Optimal);
                    }
                }
            }
            _onLog($"✓ ZIP-Archiv (Fallback) erstellt ({paths.Count} Datei(en)): {Path.GetFileName(destinationZipPath)}");
            return true;
        }
        catch (Exception ex)
        {
            _onError($"ZIP-Fallback Fehlgeschlagen: {ex.Message}");
            return false;
        }
    }
}
