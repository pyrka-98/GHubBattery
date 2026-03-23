using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GHubBattery;

/// <summary>
/// Appends battery readings to a rolling JSON-lines log file.
/// Location: %LOCALAPPDATA%\GHubBatteryTray\battery_history.jsonl
///
/// Each line is a JSON object:
///   { "ts": "2026-03-23T09:12:01Z", "device": "PRO X Wireless", "pct": 72, "charging": false }
///
/// The file is capped at MaxLines entries — oldest are pruned on startup.
/// </summary>
public sealed class BatteryHistory
{
    private const int MaxLines = 10_000;

    private readonly string _path;
    private readonly ILogger<BatteryHistory> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public BatteryHistory(ILogger<BatteryHistory> log)
    {
        _log  = log;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHubBatteryTray");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "battery_history.jsonl");
        PruneOldEntries();
    }

    public string FilePath => _path;

    public async Task RecordAsync(DeviceBattery device)
    {
        var entry = JsonSerializer.Serialize(new
        {
            ts       = device.LastUpdated.ToString("o"),
            device   = device.DeviceName,
            pct      = device.Percentage,
            charging = device.IsCharging,
        });

        await _lock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_path, entry + Environment.NewLine);
        }
        catch (Exception ex) { _log.LogWarning("History write failed: {Error}", ex.Message); }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<HistoryEntry>> ReadRecentAsync(int maxEntries = 500)
    {
        if (!File.Exists(_path)) return [];

        await _lock.WaitAsync();
        try
        {
            var lines = await File.ReadAllLinesAsync(_path);
            return lines
                .Reverse()
                .Take(maxEntries)
                .Select(line =>
                {
                    try
                    {
                        var doc = JsonDocument.Parse(line);
                        var r   = doc.RootElement;
                        return new HistoryEntry(
                            DateTime.Parse(r.GetProperty("ts").GetString()!),
                            r.GetProperty("device").GetString() ?? "",
                            r.GetProperty("pct").GetInt32(),
                            r.GetProperty("charging").GetBoolean());
                    }
                    catch { return null; }
                })
                .Where(e => e is not null)
                .Cast<HistoryEntry>()
                .ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning("History read failed: {Error}", ex.Message);
            return [];
        }
        finally { _lock.Release(); }
    }

    private void PruneOldEntries()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var lines = File.ReadAllLines(_path);
            if (lines.Length > MaxLines)
            {
                File.WriteAllLines(_path, lines.TakeLast(MaxLines));
                _log.LogInformation("Pruned battery history to {Max} entries.", MaxLines);
            }
        }
        catch (Exception ex) { _log.LogWarning("History prune failed: {Error}", ex.Message); }
    }
}

public sealed record HistoryEntry(DateTime Timestamp, string DeviceName, int Percentage, bool IsCharging);
