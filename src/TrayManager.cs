using System.Windows.Forms;
using System.Drawing;
using Microsoft.Extensions.Logging;

namespace GHubBattery;

public sealed class TrayManager : IDisposable
{
    private const string AppName = "G HUB Battery";

    private readonly NotifyIcon           _tray;
    private readonly BatteryStateStore    _store;
    private readonly StartupManager       _startup;
    private readonly AppSettings          _settings;
    private readonly BatteryHistory       _history;
    private readonly GHubConnector        _connector;
    private readonly ILogger<TrayManager> _log;

    private readonly HashSet<string> _notifiedLow = [];
    private Icon? _currentIcon;
    private bool  _disposed;

    // Dark theme colors
    private static readonly Color BgColor        = Color.FromArgb(30, 30, 30);
    private static readonly Color TextColor       = Color.FromArgb(220, 220, 220);
    private static readonly Color MutedColor      = Color.FromArgb(130, 130, 130);
    private static readonly Color SeparatorColor  = Color.FromArgb(60, 60, 60);
    private static readonly Color HoverColor      = Color.FromArgb(50, 120, 200);

    public TrayManager(
        BatteryStateStore store,
        StartupManager startup,
        AppSettings settings,
        BatteryHistory history,
        GHubConnector connector,
        ILogger<TrayManager> log)
    {
        _store    = store;
        _startup  = startup;
        _settings = settings;
        _history  = history;
        _connector = connector;
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
                _tray.Text = $"{AppName} - G HUB offline";
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
            _tray.BalloonTipText  = $"G HUB Battery {version} is available.";
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
        _ = _history.RecordAsync(device);

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
        var oldIcon = _currentIcon;
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
        if (all.Count == 0) { _tray.Text = $"{AppName} - no devices"; return; }

        var lines   = all.Select(d => $"{TruncateName(d.DeviceName, 22)}: {d.Percentage}%{(d.IsCharging ? " charging" : "")}");
        var tooltip = $"{AppName}\n" + string.Join("\n", lines);
        _tray.Text  = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.BackColor = BgColor;
        menu.ForeColor = TextColor;
        menu.Renderer  = new DarkMenuRenderer();

        // ── Header ──
        var header = new ToolStripLabel(AppName)
        {
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = MutedColor,
            BackColor = BgColor,
        };
        menu.Items.Add(header);
        menu.Items.Add(MakeSeparator());

        // ── Device rows ──
        var all = _settings.ShowAllDevices ? _store.All() : (_store.Worst() is { } w ? [w] : []);
        if (all.Count == 0)
        {
            menu.Items.Add(new ToolStripLabel("Waiting for G HUB...")
            {
                Enabled   = false,
                ForeColor = MutedColor,
                BackColor = BgColor,
            });
        }
        else
        {
            foreach (var d in all)
            {
                var label = $"{TruncateName(d.DeviceName, 24)}  {d.Percentage}%{(d.IsCharging ? " charging" : "")}{(d.Level == BatteryLevel.Critical ? " !" : "")}";
                var item  = new ToolStripMenuItem(label)
                {
                    Enabled   = false,
                    ForeColor = TextColor,
                    BackColor = BgColor,
                };
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(MakeSeparator());

        // ── Actions ──
        menu.Items.Add(MakeItem("Refresh now", () =>
        {
            _ = Task.Run(async () =>
            {
                await _connector.SendGetBatteryAsync(CancellationToken.None);
                await Task.Delay(500);
                InvokeOnUiThread(() =>
                {
                    RefreshIconFromStore();
                    UpdateTooltip();
                    BuildContextMenu();
                });
            });
        }));

        menu.Items.Add(MakeItem("Open battery history log", () =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_history.FilePath) { UseShellExecute = true }); }
            catch { }
        }));

        menu.Items.Add(MakeItem("Settings...", () =>
        {
            using var win = new SettingsWindow(_settings);
            win.ShowDialog();
            RefreshIconFromStore();
            BuildContextMenu();
        }));

        var startupItem = MakeItem("Run at Windows startup", () => { });
        startupItem.Checked = _startup.IsEnabled();
        startupItem.Click  += (_, _) =>
        {
            if (_startup.IsEnabled()) _startup.Disable(); else _startup.Enable();
            startupItem.Checked = _startup.IsEnabled();
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(MakeSeparator());
        menu.Items.Add(MakeItem("Exit", () => Application.Exit()));

        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    private static ToolStripMenuItem MakeItem(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Color.FromArgb(220, 220, 220),
            BackColor = Color.FromArgb(30, 30, 30),
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static ToolStripSeparator MakeSeparator() => new()
    {
        BackColor = Color.FromArgb(30, 30, 30),
    };

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

// ── Dark theme renderer ───────────────────────────────────────────────────────

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Bg     = Color.FromArgb(30, 30, 30);
    private static readonly Color Hover  = Color.FromArgb(50, 100, 180);
    private static readonly Color Border = Color.FromArgb(60, 60, 60);

    public DarkMenuRenderer() : base(new DarkMenuColors()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Bg);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
        using var brush = new SolidBrush(e.Item.Selected && e.Item.Enabled ? Hover : Bg);
        e.Graphics.FillRectangle(brush, rect);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Color.FromArgb(60, 60, 60));
        e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? Color.FromArgb(220, 220, 220)
            : Color.FromArgb(110, 110, 110);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Bg);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Border);
        e.Graphics.DrawRectangle(pen, new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1));
    }
}

internal sealed class DarkMenuColors : ProfessionalColorTable
{
    private static readonly Color Bg = Color.FromArgb(30, 30, 30);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(50, 100, 180);
    public override Color MenuItemSelectedGradientEnd   => Color.FromArgb(50, 100, 180);
    public override Color MenuItemSelected              => Color.FromArgb(50, 100, 180);
    public override Color MenuBorder                    => Color.FromArgb(60, 60, 60);
    public override Color ToolStripDropDownBackground   => Bg;
    public override Color ImageMarginGradientBegin      => Bg;
    public override Color ImageMarginGradientMiddle     => Bg;
    public override Color ImageMarginGradientEnd        => Bg;
}
