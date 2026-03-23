using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GHubBattery;

/// <summary>
/// Checks for newer releases on GitHub.
/// Compares the current assembly version against the latest tag on:
///   https://api.github.com/repos/{Owner}/{Repo}/releases/latest
///
/// Update OWNER and REPO below to match your GitHub repository.
/// Set the assembly version in GHubBattery.csproj:
///   <Version>1.0.0</Version>
///
/// The check runs once on startup (after a 10s delay so the app is settled)
/// and shows a tray balloon if a newer version is available.
/// </summary>
public sealed class UpdateChecker
{
    // ── Configure these to match your GitHub repo ─────────────────────────────
    private const string Owner = "pyrka-98";
    private const string Repo  = "GHubBattery";
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Version CurrentVersion =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    private readonly ILogger<UpdateChecker> _log;

    public UpdateChecker(ILogger<UpdateChecker> log) => _log = log;

    /// <summary>
    /// Runs the update check in the background.
    /// Invokes onUpdateAvailable(latestVersion, releaseUrl) if a newer version is found.
    /// </summary>
    public Task RunAsync(Action<string, string> onUpdateAvailable, CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                // Wait 10s after startup before checking
                await Task.Delay(TimeSpan.FromSeconds(10), ct);

                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", $"GHubBatteryTray/{CurrentVersion}");
                http.Timeout = TimeSpan.FromSeconds(10);

                var url      = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
                var response = await http.GetStringAsync(url, ct);
                var doc      = JsonDocument.Parse(response);

                var tagName    = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                var releaseUrl = doc.RootElement.GetProperty("html_url").GetString() ?? "";

                // Strip leading 'v' from tag (e.g. "v1.2.0" -> "1.2.0")
                var versionStr = tagName.TrimStart('v');

                if (!Version.TryParse(versionStr, out var latestVersion))
                {
                    _log.LogDebug("Could not parse version from tag: {Tag}", tagName);
                    return;
                }

                if (latestVersion > CurrentVersion)
                {
                    _log.LogInformation("Update available: {Latest} (current: {Current})", latestVersion, CurrentVersion);
                    onUpdateAvailable(latestVersion.ToString(), releaseUrl);
                }
                else
                {
                    _log.LogDebug("Up to date ({Version}).", CurrentVersion);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Update check failing is non-critical — log quietly
                _log.LogDebug("Update check failed: {Error}", ex.Message);
            }
        }, ct);
}
