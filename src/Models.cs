namespace GHubBattery;

/// <summary>
/// Snapshot of a single device's battery state.
/// </summary>
public sealed class DeviceBattery
{
    public string DeviceId   { get; init; } = "";
    public string DeviceName { get; init; } = "";

    /// <summary>0–100, or -1 when the device does not report battery.</summary>
    public int Percentage { get; init; } = -1;

    public bool IsCharging { get; init; }
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;

    public BatteryLevel Level => Percentage switch
    {
        < 0  => BatteryLevel.Unknown,
        < 20 => BatteryLevel.Critical,
        < 50 => BatteryLevel.Low,
        _    => BatteryLevel.Good,
    };

    public override string ToString() =>
        Percentage < 0
            ? $"{DeviceName}: no battery"
            : $"{DeviceName}: {Percentage}%{(IsCharging ? " (charging)" : "")}";
}

public enum BatteryLevel { Unknown, Critical, Low, Good }
