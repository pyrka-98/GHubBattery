using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace GHubBattery;

/// <summary>
/// Thread-safe store of deviceId -> DeviceBattery.
/// Supports multiple devices — all are tracked and surfaced via All().
/// The tray shows the device with the lowest battery by default.
/// DeviceUpdated fires only when a value actually changes.
/// </summary>
public sealed class BatteryStateStore
{
    private readonly ConcurrentDictionary<string, DeviceBattery> _state = new();
    private readonly ILogger<BatteryStateStore> _log;

    public event Action<DeviceBattery>? DeviceUpdated;

    public BatteryStateStore(ILogger<BatteryStateStore> log) => _log = log;

    public void Update(IReadOnlyList<DeviceBattery> devices)
    {
        foreach (var d in devices)
        {
            var prev = _state.GetValueOrDefault(d.DeviceId);
            _state[d.DeviceId] = d;

            if (prev?.Percentage != d.Percentage || prev?.IsCharging != d.IsCharging)
            {
                _log.LogInformation("Battery update: {Device}", d);
                DeviceUpdated?.Invoke(d);
            }
        }
    }

    /// <summary>All tracked devices with battery support, sorted by name.</summary>
    public IReadOnlyList<DeviceBattery> All() =>
        [.. _state.Values.Where(d => d.Percentage >= 0).OrderBy(d => d.DeviceName)];

    /// <summary>Device with the lowest battery % (shown in tray icon).</summary>
    public DeviceBattery? Worst()
    {
        var valid = _state.Values.Where(d => d.Percentage >= 0).ToList();
        return valid.Count > 0 ? valid.MinBy(d => d.Percentage) : null;
    }

    /// <summary>Lowest battery % across all devices, or null if none tracked.</summary>
    public int? LowestBattery() => Worst()?.Percentage;
}
