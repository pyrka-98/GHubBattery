using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace GHubBattery;

/// <summary>
/// Persists user-configurable settings to the registry.
/// All settings live under HKCU\Software\GHubBatteryTray.
///
/// Settings:
///   LowBatteryThreshold    (int, default 20)  — % that triggers balloon notification
///   CriticalThreshold      (int, default 10)  — % shown as error-level balloon
///   PollIntervalSeconds    (int, default 60)  — how often to force-refresh via GET
///   ShowAllDevices         (bool, default true) — show all devices in menu vs just worst
///   NotificationsEnabled   (bool, default true)
/// </summary>
public sealed class AppSettings
{
    private const string RegKey = @"Software\GHubBatteryTray\Settings";

    public int  LowBatteryThreshold  { get; set; } = 20;
    public int  CriticalThreshold    { get; set; } = 10;
    public int  PollIntervalSeconds  { get; set; } = 60;
    public bool ShowAllDevices       { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;

    private readonly ILogger<AppSettings> _log;

    public AppSettings(ILogger<AppSettings> log) => _log = log;

    public void Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: false);
            if (key is null) return;

            LowBatteryThreshold  = (int)(key.GetValue(nameof(LowBatteryThreshold),  LowBatteryThreshold)  ?? LowBatteryThreshold);
            CriticalThreshold    = (int)(key.GetValue(nameof(CriticalThreshold),    CriticalThreshold)    ?? CriticalThreshold);
            PollIntervalSeconds  = (int)(key.GetValue(nameof(PollIntervalSeconds),  PollIntervalSeconds)  ?? PollIntervalSeconds);
            ShowAllDevices       = (int)(key.GetValue(nameof(ShowAllDevices),       ShowAllDevices ? 1 : 0) ?? 1) == 1;
            NotificationsEnabled = (int)(key.GetValue(nameof(NotificationsEnabled), NotificationsEnabled ? 1 : 0) ?? 1) == 1;
        }
        catch (Exception ex) { _log.LogWarning("Failed to load settings: {Error}", ex.Message); }
    }

    public void Save()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegKey, writable: true);
            key.SetValue(nameof(LowBatteryThreshold),  LowBatteryThreshold,  RegistryValueKind.DWord);
            key.SetValue(nameof(CriticalThreshold),    CriticalThreshold,    RegistryValueKind.DWord);
            key.SetValue(nameof(PollIntervalSeconds),  PollIntervalSeconds,  RegistryValueKind.DWord);
            key.SetValue(nameof(ShowAllDevices),       ShowAllDevices ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(nameof(NotificationsEnabled), NotificationsEnabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex) { _log.LogWarning("Failed to save settings: {Error}", ex.Message); }
    }
}
