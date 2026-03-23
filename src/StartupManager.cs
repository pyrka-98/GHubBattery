using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace GHubBattery;

/// <summary>
/// Manages the "run at Windows startup" registry entry.
///
/// Writes to HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// so no admin rights are required.
/// </summary>
public sealed class StartupManager
{
    private const string RegKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegName = "GHubBatteryTray";

    private readonly ILogger<StartupManager> _log;

    public StartupManager(ILogger<StartupManager> log) => _log = log;

    /// <summary>Returns true if the startup entry exists and points to this executable.</summary>
    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: false);
            var value = key?.GetValue(RegName) as string;
            return string.Equals(value, ExePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Could not read startup registry key: {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>Adds or updates the startup registry entry.</summary>
    public void Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)
                         ?? throw new InvalidOperationException("Could not open Run key for writing.");
            key.SetValue(RegName, ExePath());
            _log.LogInformation("Startup enabled.");
        }
        catch (Exception ex)
        {
            _log.LogError("Failed to enable startup: {Error}", ex.Message);
        }
    }

    /// <summary>Removes the startup registry entry if it exists.</summary>
    public void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
            key?.DeleteValue(RegName, throwOnMissingValue: false);
            _log.LogInformation("Startup disabled.");
        }
        catch (Exception ex)
        {
            _log.LogError("Failed to disable startup: {Error}", ex.Message);
        }
    }

    private static string ExePath() =>
        $"\"{System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName}\"";
}
