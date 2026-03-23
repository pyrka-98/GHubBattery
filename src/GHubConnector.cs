using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GHubBattery;

/// <summary>
/// Maintains a WebSocket connection to G HUB's local server.
/// Subscribes to /battery/state/changed and /devices/state/changed.
/// Sends GET /devices/list on connect to seed device names immediately.
/// Runs a polling fallback every 60s to recover from missed pushes (e.g. after sleep/wake).
/// Auto-reconnects with exponential back-off when the socket drops.
/// </summary>
public sealed class GHubConnector : IAsyncDisposable
{
    private const string WsUri  = "ws://localhost:9010";
    private const string Origin = "GHubBatteryTray/1.0";

    private static readonly string[] SubscribePaths =
    [
        "/battery/state/changed",
        "/devices/state/changed",
    ];

    private static readonly TimeSpan ReadTimeout  = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan[] BackOffSteps =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    private readonly ILogger<GHubConnector> _log;
    private ClientWebSocket? _ws;
    private int  _reconnectAttempts;
    private bool _disposed;

    public event Action<string>? MessageReceived;
    public event Action<bool>?   Connected;

    public GHubConnector(ILogger<GHubConnector> log) => _log = log;

    public async Task SendGetBatteryAsync(CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        // Re-subscribe to battery path — G HUB will re-send current state
        var msg   = new { msgId = Guid.NewGuid().ToString("N"), verb = "SUBSCRIBE", origin = Origin, path = "/battery/state/changed" };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        _log.LogDebug("Sent re-SUBSCRIBE -> /battery/state/changed");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Polling timer fires every 60s — sends GET /devices/list even if no push arrived
        using var pollTimer = new System.Timers.Timer(PollInterval.TotalMilliseconds);
        pollTimer.Elapsed += async (_, _) =>
    {
        try
        {
            await SendGetDeviceListAsync(ct);
            await SendGetBatteryAsync(ct);
        }
        catch { }
    };
        pollTimer.Start();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceiveAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogWarning("G HUB connection lost: {Error}", ex.Message);
            }

            Connected?.Invoke(false);
            var delay = BackOffSteps[Math.Min(_reconnectAttempts, BackOffSteps.Length - 1)];
            _reconnectAttempts++;
            _log.LogInformation("Reconnecting in {Delay}s (attempt {N})...", delay.TotalSeconds, _reconnectAttempts);
            await Task.Delay(delay, ct);
        }
    }

    private async Task ConnectAndReceiveAsync(CancellationToken ct)
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Origin", "file://");
        _ws.Options.SetRequestHeader("Pragma", "no-cache");
        _ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        _ws.Options.AddSubProtocol("json");
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        _log.LogInformation("Connecting to {Uri}...", WsUri);
        await _ws.ConnectAsync(new Uri(WsUri), ct);
        _reconnectAttempts = 0;
        _log.LogInformation("Connected to G HUB.");
        Connected?.Invoke(true);

        await SendSubscribeAsync(ct);
        await SendGetDeviceListAsync(ct);
        await ReceiveLoopAsync(ct);
    }

    private async Task SendSubscribeAsync(CancellationToken ct)
    {
        foreach (var path in SubscribePaths)
        {
            var msg   = new { msgId = Guid.NewGuid().ToString("N"), verb = "SUBSCRIBE", origin = Origin, path };
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));
            await _ws!.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            _log.LogDebug("Sent SUBSCRIBE -> {Path}", path);
        }
    }

    public async Task SendGetDeviceListAsync(CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var msg   = new { msgId = Guid.NewGuid().ToString("N"), verb = "GET", origin = Origin, path = "/devices/list" };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        _log.LogDebug("Sent GET -> /devices/list");
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var sb     = new StringBuilder();

        while (_ws!.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            sb.Clear();
            WebSocketReceiveResult result;
            do
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ReadTimeout);
                try
                {
                    result = await _ws.ReceiveAsync(buffer, timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _log.LogWarning("No message from G HUB for {T}s; reconnecting.", ReadTimeout.TotalSeconds);
                    throw new TimeoutException("G HUB receive timeout");
                }

                if (result.MessageType == WebSocketMessageType.Close) return;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            try { MessageReceived?.Invoke(sb.ToString()); }
            catch (Exception ex) { _log.LogWarning("MessageReceived handler threw: {Error}", ex.Message); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ws is not null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None);
            }
            catch { }
            _ws.Dispose();
        }
    }
}
