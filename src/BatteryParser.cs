using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GHubBattery;

/// <summary>
/// Parses raw JSON messages from the G HUB WebSocket.
///
/// Two message types we handle:
///
/// 1. /battery/state/changed  (verb: BROADCAST)
///    payload: { deviceId, percentage, charging, criticalLevel, fullyCharged }
///    → This is the primary battery data source.
///
/// 2. /devices/list  (verb: GET response)
///    payload: { deviceInfos: [ { id, extendedDisplayName, capabilities.hasBatteryStatus } ] }
///    → Used to seed the device name map so battery messages can show friendly names.
///
/// 3. /devices/state/changed  (verb: BROADCAST)
///    payload: { id, extendedDisplayName, state }
///    → Used to detect device connect/disconnect.
/// </summary>
public sealed class BatteryParser
{
    private readonly ILogger<BatteryParser> _log;

    // deviceId → friendly name, populated from /devices/list and /devices/state/changed
    private readonly Dictionary<string, string> _nameCache = [];

    private static readonly JsonDocumentOptions JsonOpts = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public BatteryParser(ILogger<BatteryParser> log) => _log = log;

    public IReadOnlyList<DeviceBattery> Parse(string raw)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw, JsonOpts); }
        catch (JsonException ex)
        {
            _log.LogWarning("JSON parse error: {Error}", ex.Message);
            return [];
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("path", out var pathEl)) return [];
            var path = pathEl.GetString() ?? "";

            if (!root.TryGetProperty("verb", out var verbEl)) return [];
            var verb = verbEl.GetString() ?? "";

            return path switch
            {
                "/battery/state/changed"  => ParseBatteryBroadcast(root),
                "/devices/list"           => ParseDeviceList(root),
                "/devices/state/changed"  => ParseDeviceStateChanged(root),
                _ => []
            };
        }
    }

    // ── /battery/state/changed ────────────────────────────────────────────────

    private IReadOnlyList<DeviceBattery> ParseBatteryBroadcast(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var p)) return [];

        var deviceId = GetString(p, "deviceId") ?? "";
        if (string.IsNullOrEmpty(deviceId)) return [];

        int percent = -1;
        if (p.TryGetProperty("percentage", out var pctEl) && pctEl.ValueKind == JsonValueKind.Number)
            percent = pctEl.GetInt32();

        bool charging = false;
        if (p.TryGetProperty("charging", out var chgEl) && chgEl.ValueKind == JsonValueKind.True)
            charging = true;

        // Look up friendly name from cache (populated by /devices/list)
        _nameCache.TryGetValue(deviceId, out var name);
        name ??= deviceId;

        _log.LogDebug("Battery broadcast: {Id} {Pct}% charging={Charging}", deviceId, percent, charging);

        return [new DeviceBattery
        {
            DeviceId    = deviceId,
            DeviceName  = name,
            Percentage  = percent,
            IsCharging  = charging,
            LastUpdated = DateTime.UtcNow,
        }];
    }

    // ── /devices/list (GET response) ─────────────────────────────────────────

    private IReadOnlyList<DeviceBattery> ParseDeviceList(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload)) return [];
        if (!payload.TryGetProperty("deviceInfos", out var infos)) return [];
        if (infos.ValueKind != JsonValueKind.Array) return [];

        foreach (var d in infos.EnumerateArray())
        {
            var id   = GetString(d, "id");
            var name = GetString(d, "extendedDisplayName") ?? GetString(d, "displayName");
            if (id is not null && name is not null)
            {
                _nameCache[id] = name;
                _log.LogDebug("Cached device name: {Id} = {Name}", id, name);
            }
        }

        // Return empty — battery values come from /battery/state/changed, not here
        return [];
    }

    // ── /devices/state/changed ────────────────────────────────────────────────

    private IReadOnlyList<DeviceBattery> ParseDeviceStateChanged(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var p)) return [];

        var id   = GetString(p, "id");
        var name = GetString(p, "extendedDisplayName") ?? GetString(p, "displayName");

        if (id is not null && name is not null)
            _nameCache[id] = name;

        // No battery value in this message — just a name cache update
        return [];
    }

    private static string? GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}