using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using GHubBattery;

using var mutex = new System.Threading.Mutex(true, "GHubBatteryTray_SingleInstance", out bool isFirst);
if (!isFirst)
{
    MessageBox.Show("G HUB Battery Tray is already running.\nCheck your system tray.",
        "Already running", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

using var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
#if DEBUG
    b.SetMinimumLevel(LogLevel.Debug);
#else
    b.SetMinimumLevel(LogLevel.Information);
#endif
});

var appLog     = loggerFactory.CreateLogger("App");
var settings   = new AppSettings(loggerFactory.CreateLogger<AppSettings>());
settings.Load();

var crashLog   = new CrashLogger(loggerFactory.CreateLogger<CrashLogger>());
crashLog.Register();

var parser     = new BatteryParser(loggerFactory.CreateLogger<BatteryParser>());
var store      = new BatteryStateStore(loggerFactory.CreateLogger<BatteryStateStore>());
var startup    = new StartupManager(loggerFactory.CreateLogger<StartupManager>());
var history    = new BatteryHistory(loggerFactory.CreateLogger<BatteryHistory>());
var connector  = new GHubConnector(loggerFactory.CreateLogger<GHubConnector>());
var tray       = new TrayManager(store, startup, settings, history, loggerFactory.CreateLogger<TrayManager>());
var updater    = new UpdateChecker(loggerFactory.CreateLogger<UpdateChecker>());

connector.MessageReceived += raw =>
{
    var devices = parser.Parse(raw);
    if (devices.Count > 0) store.Update(devices);
};

connector.Connected += isConnected => tray.SetConnectionStatus(isConnected);

var cts    = new CancellationTokenSource();
var wsTask = Task.Run(async () =>
{
    try   { await connector.RunAsync(cts.Token); }
    catch (OperationCanceledException) { }
    catch (Exception ex) { crashLog.Write("Connector crashed", ex); }
    finally { await connector.DisposeAsync(); }
});

// Update check — fires once 10s after launch
_ = updater.RunAsync(
    (version, url) => tray.ShowUpdateNotification(version, url),
    cts.Token);

appLog.LogInformation("G HUB Battery Tray started. Log: {Path}", crashLog.FilePath);
appLog.LogInformation("Battery history: {Path}", history.FilePath);

Application.Run(new ApplicationContext());

appLog.LogInformation("Shutting down...");
cts.Cancel();
tray.Dispose();

try { await wsTask.WaitAsync(TimeSpan.FromSeconds(3)); }
catch { }

appLog.LogInformation("Stopped.");
