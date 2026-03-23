using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace GHubBattery;

/// <summary>
/// Owns the Windows system-tray NotifyIcon.
/// Wires battery state -> icon, tooltip, context menu, and balloon notifications.
/// Respects AppSettings for thresholds, notification toggle, and multi-device display.
/// </summary>
public sealed class TrayManager : IDisposable
{
    private const string AppName = "G HUB Battery";

    private readonly NotifyIcon           _tray;
    private readonly BatteryStateStore    _store;
    private readonly StartupManager       _startup;
    private readonly AppSettings          _settings;
    private readonly BatteryHistory       _history;
    private readonly ILogger<TrayManager> _log;

    private readonly HashSet<string> _notifiedLow = [];
    private Icon? _currentIcon;
    private bool  _disposed;

    public TrayManager(
        BatteryStateStore store,
        StartupManager startup,
        AppSettings settings,
        BatteryHistory history,
        ILogger<TrayManager> log)
    {
        _store    = store;
        _startup  = startup;
        _settings = settings;
        _history  = history;
        _log      = log;

        _tray = new NotifyIcon { Text = AppName, Visible = true };
        RefreshIcon(-1, false);
        BuildContextMenu();

        _store.DeviceUpdated += OnDeviceUpdated;
    }

    public void SetConnectionStatus(bool connected)
    {
        InvokeOnUiThread(() =>
        {
            if (!connected)
            {
                RefreshIcon(-1, false);
                _tray.Text = $"{AppName} — G HUB offline";
            }
            else
            {
                _tray.Text = AppName;
            }
        });
    }

    public void ShowUpdateNotification(string version, string url)
    {
        InvokeOnUiThread(() =>
        {
            _tray.BalloonTipTitle = "Update available";
            _tray.BalloonTipText  = $"G HUB Battery {version} is available. Click to open.";
            _tray.BalloonTipIcon  = ToolTipIcon.Info;
            _tray.ShowBalloonTip(8000);
            _tray.BalloonTipClicked += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
            };
        });
    }

    private void OnDeviceUpdated(DeviceBattery device)
    {
        _ = _history.RecordAsync(device);   // fire-and-forget

        InvokeOnUiThread(() =>
        {
            RefreshIconFromStore();
            UpdateTooltip();
            BuildContextMenu();
            if (_settings.NotificationsEnabled)
                MaybeSendLowBatteryNotification(device);
        });
    }

    private void RefreshIconFromStore()
    {
        var worst = _store.Worst();
        RefreshIcon(worst?.Percentage ?? -1, worst?.IsCharging ?? false);
    }

    private void RefreshIcon(int percent, bool isCharging)
    {
        var oldIcon   = _currentIcon;
        try
        {
            _currentIcon = TrayIconRenderer.RenderMultiSize(percent, isCharging);
            _tray.Icon   = _currentIcon;
        }
        catch (Exception ex) { _log.LogWarning("Icon render failed: {Error}", ex.Message); }
        finally
        {
            if (oldIcon is not null)
                Task.Delay(200).ContinueWith(_ => oldIcon.Dispose());
        }
    }

    private void UpdateTooltip()
    {
        var all = _store.All();
        if (all.Count == 0) { _tray.Text = $"{AppName} — no devices"; return; }

        var lines   = all.Select(d => $"{TruncateName(d.DeviceName, 22)}: {d.Percentage}%{(d.IsCharging ? " charging" : "")}");
        var tooltip = $"{AppName}\n" + string.Join("\n", lines);
        _tray.Text  = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Renderer = new FlatMenuRenderer();

        // Header
        menu.Items.Add(new ToolStripLabel(AppName)
        {
            Font      = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.SystemColors.GrayText,
        });
        menu.Items.Add(new ToolStripSeparator());

        // Device rows
        var all = _settings.ShowAllDevices ? _store.All() : (_store.Worst() is { } w ? [w] : []);
        if (all.Count == 0)
        {
            menu.Items.Add(new ToolStripLabel("Waiting for G HUB...") { Enabled = false, ForeColor = System.Drawing.SystemColors.GrayText });
        }
        else
        {
            foreach (var d in all)
            {
                var label = $"{TruncateName(d.DeviceName, 30)}   {d.Percentage}%{(d.IsCharging ? "  charging" : "")}{(d.Level == BatteryLevel.Critical ? "  !" : "")}";
                var bmp   = TrayIconRenderer.Render(d.Percentage, d.IsCharging, 16).ToBitmap();
                menu.Items.Add(new ToolStripMenuItem(label, bmp) { Enabled = false });
            }
        }

        menu.Items.Add(new ToolStripSeparator());

        // Refresh
        var refresh = new ToolStripMenuItem("Refresh now");
        refresh.Click += (_, _) => { RefreshIconFromStore(); UpdateTooltip(); BuildContextMenu(); };
        menu.Items.Add(refresh);

        // History file
        var histItem = new ToolStripMenuItem("Open battery history log");
        histItem.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_history.FilePath) { UseShellExecute = true }); }
            catch { }
        };
        menu.Items.Add(histItem);

        // Settings
        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) =>
        {
            using var win = new SettingsWindow(_settings);
            win.ShowDialog();
            RefreshIconFromStore();
            BuildContextMenu();
        };
        menu.Items.Add(settingsItem);

        // Startup toggle
        var startupItem = new ToolStripMenuItem("Run at Windows startup") { Checked = _startup.IsEnabled() };
        startupItem.Click += (_, _) =>
        {
            if (_startup.IsEnabled()) _startup.Disable(); else _startup.Enable();
            startupItem.Checked = _startup.IsEnabled();
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => System.Windows.Forms.Application.Exit();
        menu.Items.Add(exit);

        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    private void MaybeSendLowBatteryNotification(DeviceBattery d)
    {
        if (d.Percentage < 0 || d.IsCharging) return;

        int low  = _settings.LowBatteryThreshold;
        int crit = _settings.CriticalThreshold;

        if (d.Percentage <= low && !_notifiedLow.Contains(d.DeviceId))
        {
            _notifiedLow.Add(d.DeviceId);
            _tray.ShowBalloonTip(5000,
                d.Percentage <= crit ? "Critical battery!" : "Low battery",
                $"{d.DeviceName}: {d.Percentage}%",
                d.Percentage <= crit ? ToolTipIcon.Error : ToolTipIcon.Warning);
        }

        if (d.Percentage > low + 5)
            _notifiedLow.Remove(d.DeviceId);
    }

    private static void InvokeOnUiThread(Action action)
    {
        var ctx = SynchronizationContext.Current;
        if (ctx is not null) ctx.Post(_ => action(), null);
        else action();
    }

    private static string TruncateName(string name, int max) =>
        name.Length > max ? name[..(max - 1)] + "..." : name;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _store.DeviceUpdated -= OnDeviceUpdated;
        _tray.Visible = false;
        _tray.Dispose();
        _currentIcon?.Dispose();
    }
}

internal sealed class FlatMenuRenderer : ToolStripProfessionalRenderer
{
    public FlatMenuRenderer() : base(new FlatMenuColors()) { }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected) { base.OnRenderMenuItemBackground(e); return; }
        var r = new System.Drawing.Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
        using var b = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(40, 120, 200, 255));
        e.Graphics.FillRectangle(b, r);
    }
}

internal sealed class FlatMenuColors : ProfessionalColorTable
{
    public override System.Drawing.Color MenuItemSelectedGradientBegin => System.Drawing.Color.FromArgb(40, 120, 200, 255);
    public override System.Drawing.Color MenuItemSelectedGradientEnd   => System.Drawing.Color.FromArgb(40, 120, 200, 255);
    public override System.Drawing.Color MenuItemSelected              => System.Drawing.Color.FromArgb(40, 120, 200, 255);
    public override System.Drawing.Color MenuBorder                    => System.Drawing.Color.FromArgb(60, 60, 60);
    public override System.Drawing.Color ToolStripDropDownBackground   => System.Drawing.Color.FromArgb(30, 30, 30);
    public override System.Drawing.Color ImageMarginGradientBegin      => System.Drawing.Color.FromArgb(30, 30, 30);
    public override System.Drawing.Color ImageMarginGradientMiddle     => System.Drawing.Color.FromArgb(30, 30, 30);
    public override System.Drawing.Color ImageMarginGradientEnd        => System.Drawing.Color.FromArgb(30, 30, 30);
}
