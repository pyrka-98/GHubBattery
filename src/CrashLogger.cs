using Microsoft.Extensions.Logging;

namespace GHubBattery;

/// <summary>
/// Writes unhandled exceptions to a rolling log file.
/// Location: %LOCALAPPDATA%\GHubBatteryTray\crash.log
/// Keeps the last 500 lines to avoid unbounded growth.
/// Also hooks Application.ThreadException and AppDomain.UnhandledException.
/// </summary>
public sealed class CrashLogger
{
    private const int MaxLines = 500;

    private readonly string _path;
    private readonly ILogger<CrashLogger> _log;

    public CrashLogger(ILogger<CrashLogger> log)
    {
        _log = log;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHubBatteryTray");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "crash.log");
    }

    public string FilePath => _path;

    /// <summary>Call once at startup to register global exception handlers.</summary>
    public void Register()
    {
        System.Windows.Forms.Application.ThreadException += (_, e) =>
            Write("UI thread exception", e.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("Unhandled exception", e.ExceptionObject as Exception);
    }

    public void Write(string context, Exception? ex)
    {
        try
        {
            var lines = new List<string>
            {
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}",
                ex?.ToString() ?? "(null exception)",
                "",
            };

            File.AppendAllLines(_path, lines);
            _log.LogError("{Context}: {Message}", context, ex?.Message);

            // Prune if too large
            var all = File.ReadAllLines(_path);
            if (all.Length > MaxLines)
                File.WriteAllLines(_path, all.TakeLast(MaxLines));
        }
        catch { /* never throw from a crash logger */ }
    }
}
