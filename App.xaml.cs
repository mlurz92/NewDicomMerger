using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace NewDicomMerger;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            ReportFatal(args.Exception, "DispatcherUnhandledException");
            args.Handled = true;
        };

        // Bugfix (found while diagnosing a startup crash): the previous handler only
        // covered exceptions on the UI dispatcher thread. Anything thrown on a
        // ThreadPool thread outside a Task (e.g. inside Parallel.For/Parallel.ForEach)
        // or in an unobserved Task went completely unhandled/unlogged, crashing the
        // process with no diagnostic trail beyond Windows' generic "stopped working".
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ReportFatal(ex, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportFatal(args.Exception, "TaskScheduler.UnobservedTaskException");
            args.SetObserved();
        };
    }

    /// <summary>
    /// Unwraps the full exception chain (TargetInvocationException and similar
    /// wrappers otherwise hide the actual root cause behind a generic message) and
    /// both logs it to a file and shows it to the user, so a crash can actually be
    /// diagnosed instead of only showing "an exception occurred: &lt;wrapper message&gt;".
    /// </summary>
    private static void ReportFatal(Exception ex, string source)
    {
        string details = FormatExceptionChain(ex);

        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{details}\n{new string('=', 80)}\n");
        }
        catch
        {
            // Logging is best-effort; must never prevent the error dialog from showing.
        }

        MessageBox.Show(
            $"Ein unerwarteter Fehler ist aufgetreten ({source}):\n\n{details}\n\nDetails wurden in crash_log.txt gespeichert.",
            "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string FormatExceptionChain(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var current = ex;
        int depth = 0;
        while (current != null)
        {
            sb.AppendLine($"{new string(' ', depth * 2)}[{depth}] {current.GetType().FullName}: {current.Message}");
            sb.AppendLine(current.StackTrace);
            current = current.InnerException;
            depth++;
        }
        return sb.ToString();
    }
}
