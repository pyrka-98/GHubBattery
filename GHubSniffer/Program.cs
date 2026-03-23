using System.Net.WebSockets;
using System.Text;

var subscribePaths = new[]
{
    "/devices/state/changed",
    "/battery",
    "/battery/state",
    "/battery/state/changed",
    "/devices/battery/state/changed",
    "/devices/dev00000000/battery",
    "/devices/dev00000000/battery/state/changed",
    "/devices/dev00000000/state/changed",
    "/devices/dev00000000/telemetry",
    "/telemetry",
    "/devices/telemetry",
    "/status",
    "/devices/status",
    "/devices/dev00000000/info",
    "/settings",
};

Console.WriteLine("Subscribing to all paths simultaneously and listening for 15 seconds...");
Console.WriteLine("Move your headset, adjust volume, or just wait — anything that might trigger a push.");
Console.WriteLine();

var tasks = subscribePaths.Select(path => ListenOnPath(path)).ToArray();
await Task.WhenAll(tasks);

Console.WriteLine();
Console.WriteLine("Done. Any hits saved to subscribe_hits.json");

async Task ListenOnPath(string path)
{
    try
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "file://");
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.AddSubProtocol("json");
        await ws.ConnectAsync(new Uri("ws://localhost:9010"), CancellationToken.None);

        // Drain OPTIONS
        var buffer = new byte[32 * 1024];
        await ws.ReceiveAsync(buffer, CancellationToken.None);

        // Subscribe
        var sub = System.Text.Json.JsonSerializer.Serialize(new
        {
            msgId  = Guid.NewGuid().ToString("N"),
            verb   = "SUBSCRIBE",
            origin = "GHubSniffer",
            path,
        });
        await ws.SendAsync(Encoding.UTF8.GetBytes(sub), WebSocketMessageType.Text, true, CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var sb = new StringBuilder();

        while (!cts.Token.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            try
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, cts.Token);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                var raw = sb.ToString();

                // Skip boring subscription ACKs with no payload
                if (raw.Contains("NO_SUCH_PATH") || raw.Contains("INVALID_ARG"))
                    continue;

                var doc  = System.Text.Json.JsonDocument.Parse(raw);
                var pretty = System.Text.Json.JsonSerializer.Serialize(
                    doc.RootElement,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"*** HIT on {path} ***");
                Console.ResetColor();
                Console.WriteLine(pretty);
                Console.WriteLine();

                await File.AppendAllTextAsync("subscribe_hits.json",
                    $"// PATH: {path}\n{pretty}\n\n");
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }

        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
        catch { }
    }
    catch { }
}