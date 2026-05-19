using System.IO;
using System.Text;

namespace MdViewer.Shared;

/// <summary>
/// Einfacher dateibasierter Logger.
/// Schreibt Logs in [Programmordner]\Log\log-YYYY-MM-DD.log
/// </summary>
public static class FileLogger
{
    private static readonly string LogDirectory;
    private static readonly object Lock = new();

    static FileLogger()
    {
        var baseDir = Path.GetDirectoryName(Environment.ProcessPath)
                      ?? AppContext.BaseDirectory;
        LogDirectory = Path.Combine(baseDir, "Log");
        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch
        {
            // Log-Verzeichnis konnte nicht erstellt werden
            // dann halt kein Logging – besser als Absturz
        }
    }

    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", message, ex);
    }

    public static void Warning(string message, Exception? ex = null)
    {
        Write("WARN ", message, ex);
    }

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    private static void Write(string level, string message, Exception? ex = null)
    {
        try
        {
            var now = DateTime.Now;
            var fileName = $"log-{now:yyyy-MM-dd}.log";
            var filePath = Path.Combine(LogDirectory, fileName);

            var sb = new StringBuilder();
            sb.Append($"[{now:yyyy-MM-dd HH:mm:ss}] [{level}] ");
            sb.AppendLine(message);

            if (ex != null)
            {
                sb.AppendLine($"  Typ:     {ex.GetType().FullName}");
                sb.AppendLine($"  Message: {ex.Message}");

                if (ex is AggregateException ae)
                {
                    for (int i = 0; i < ae.InnerExceptions.Count; i++)
                    {
                        sb.AppendLine($"  --- Inner Exception {i + 1} ---");
                        sb.AppendLine($"  Typ:     {ae.InnerExceptions[i].GetType().FullName}");
                        sb.AppendLine($"  Message: {ae.InnerExceptions[i].Message}");
                    }
                }

                if (ex.StackTrace != null)
                    sb.AppendLine($"  StackTrace: {ex.StackTrace}");
            }

            lock (Lock)
            {
                File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging-Fehler verschlucken — keine Sekundärfehler
        }
    }
}
