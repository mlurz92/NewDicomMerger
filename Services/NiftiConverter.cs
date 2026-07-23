using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace NewDicomMerger.Services;

public class NiftiSettings
{
    public bool CompressGz { get; set; } = true;
    public bool CreateBidsJson { get; set; } = false;
    public bool AnonymizeBids { get; set; } = false;
    public string FilenameFormat { get; set; } = "%f";
}

public class NiftiConverter
{
    private readonly Action<string> _onLog;
    private readonly Action<string> _onError;

    public NiftiConverter(Action<string> onLog, Action<string> onError)
    {
        _onLog = onLog;
        _onError = onError;
    }

    public async Task<int> ConvertAsync(string inputDir, string outputDir, NiftiSettings settings, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string tempPath = Path.Combine(Path.GetTempPath(), "NewDicomMerger", "Tools_v1.0");
        Directory.CreateDirectory(tempPath);
        string exePath = Path.Combine(tempPath, "dcm2niix.exe");
        
        // Extract embedded dcm2niix.exe if it doesn't exist
        if (!File.Exists(exePath))
        {
            var assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream("NewDicomMerger.Tools.dcm2niix.exe");
            if (stream == null)
            {
                _onError("Konnte eingebettetes dcm2niix.exe nicht finden.");
                throw new FileNotFoundException("dcm2niix.exe embedded resource missing");
            }
            try
            {
                using var fileStream = new FileStream(exePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
            }
            catch (IOException)
            {
                // Another instance might have locked the file while extracting, which is acceptable
                // — BUT only if that other instance actually succeeded. Bugfix found during review:
                // this used to fall through unconditionally, so a real extraction failure (full disk,
                // no write permission in %TEMP%) produced no exe and a confusing Win32Exception later
                // from Process.Start() instead of a clear error naming the actual cause.
            }

            if (!File.Exists(exePath))
            {
                _onError("dcm2niix.exe konnte nicht nach %TEMP% extrahiert werden (z.B. volle Festplatte oder fehlende Schreibrechte).");
                throw new FileNotFoundException("dcm2niix.exe extraction failed", exePath);
            }
        }

        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        string zipArg = settings.CompressGz ? "y" : "n";
        string bidsArg = settings.CreateBidsJson ? "y" : "n";
        string anonArg = settings.AnonymizeBids ? "y" : "n";
        string filenameArg = string.IsNullOrWhiteSpace(settings.FilenameFormat) ? "%f" : settings.FilenameFormat;

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList passes each argument as its own array element (no shell/CLI
        // re-parsing), which closes the command-injection hole that existed when
        // user-controlled paths/filenames were interpolated into a single Arguments
        // string and could break out of quoting (e.g. a folder name containing `"`).
        startInfo.ArgumentList.Add("-z");
        startInfo.ArgumentList.Add(zipArg);
        startInfo.ArgumentList.Add("-b");
        startInfo.ArgumentList.Add(bidsArg);
        startInfo.ArgumentList.Add("-ba");
        startInfo.ArgumentList.Add(anonArg);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(filenameArg);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputDir);
        startInfo.ArgumentList.Add(inputDir);

        _onLog($"Führe dcm2niix aus...");
        
        using var process = new Process { StartInfo = startInfo };
        
        process.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _onLog($"  [dcm2niix] {e.Data}");
        };
        
        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                // dcm2niix uses stderr for some normal output too, but we can treat it as a warning/error log
                _onLog($"  [dcm2niix ERR] {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Register cancellation to kill process
        using var registration = ct.Register(() => 
        {
            try { if (!process.HasExited) process.Kill(true); } 
            catch { /* ignore */ }
        });

        try 
        {
            await process.WaitForExitAsync(ct);
        }
        catch (TaskCanceledException)
        {
            throw; // Let caller handle
        }

        return process.ExitCode;
    }
}
