using System.Windows.Forms;
using System.Drawing;

namespace GHubBattery;

public sealed class SettingsWindow : Form
{
    private readonly AppSettings _settings;

    private readonly NumericUpDown _lowThreshold   = new();
    private readonly NumericUpDown _critThreshold  = new();
    private readonly NumericUpDown _pollInterval   = new();
    private readonly CheckBox      _showAllDevices = new();
    private readonly CheckBox      _notifications  = new();
    private readonly Button        _ok             = new();
    private readonly Button        _cancel         = new();

    private static readonly Color BgColor      = Color.FromArgb(30, 30, 30);
    private static readonly Color InputBg      = Color.FromArgb(50, 50, 50);
    private static readonly Color LabelColor   = Color.FromArgb(180, 180, 180);
    private static readonly Color AccentColor  = Color.FromArgb(24, 106, 176);

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;

        Text            = "G HUB Battery - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        ClientSize      = new Size(420, 420);
        Font            = new Font("Segoe UI", 10f);
        BackColor       = BgColor;
        ForeColor       = Color.White;

        int x     = 24;
        int right = ClientSize.Width - 24;
        int y     = 24;
        int gap   = 14;

        // ── Low battery threshold ──
        AddLabel("Low battery warning (%)", x, y); y += 26;
        AddDescription("Shows a balloon notification when battery drops to this level.", x, y); y += 22;
        Configure(_lowThreshold, x, y, 1, 99, _settings.LowBatteryThreshold); y += 46;

        // ── Critical threshold ──
        AddLabel("Critical battery level (%)", x, y); y += 26;
        AddDescription("Shows an urgent notification at this level.", x, y); y += 22;
        Configure(_critThreshold, x, y, 1, 99, _settings.CriticalThreshold); y += 46;

        // ── Poll interval ──
        AddLabel("Poll interval (seconds)", x, y); y += 26;
        AddDescription("How often to force-refresh battery data from G HUB.", x, y); y += 22;
        Configure(_pollInterval, x, y, 10, 3600, _settings.PollIntervalSeconds); y += 46;

        // ── Checkboxes ──
        Configure(_showAllDevices, x, y, "Show all devices in menu (vs. only the worst)", _settings.ShowAllDevices); y += 32;
        Configure(_notifications,  x, y, "Enable low-battery notifications", _settings.NotificationsEnabled); y += 40;

        // ── Buttons ──
        _ok.Text      = "Save";
        _ok.Size      = new Size(90, 32);
        _ok.Location  = new Point(right - 196, y);
        _ok.BackColor = AccentColor;
        _ok.ForeColor = Color.White;
        _ok.FlatStyle = FlatStyle.Flat;
        _ok.FlatAppearance.BorderSize = 0;
        _ok.Click    += OnOk;

        _cancel.Text      = "Cancel";
        _cancel.Size      = new Size(90, 32);
        _cancel.Location  = new Point(right - 96, y);
        _cancel.BackColor = InputBg;
        _cancel.ForeColor = Color.White;
        _cancel.FlatStyle = FlatStyle.Flat;
        _cancel.FlatAppearance.BorderSize = 0;
        _cancel.Click    += (_, _) => Close();

        Controls.AddRange([_lowThreshold, _critThreshold, _pollInterval,
                           _showAllDevices, _notifications, _ok, _cancel]);

        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        _settings.LowBatteryThreshold  = (int)_lowThreshold.Value;
        _settings.CriticalThreshold    = (int)_critThreshold.Value;
        _settings.PollIntervalSeconds  = (int)_pollInterval.Value;
        _settings.ShowAllDevices       = _showAllDevices.Checked;
        _settings.NotificationsEnabled = _notifications.Checked;
        _settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label
        {
            Text      = text,
            Location  = new Point(x, y),
            AutoSize  = true,
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
        });
    }

    private void AddDescription(string text, int x, int y)
    {
        Controls.Add(new Label
        {
            Text      = text,
            Location  = new Point(x, y),
            Size      = new Size(ClientSize.Width - x * 2, 18),
            Font      = new Font("Segoe UI", 8.5f),
            ForeColor = LabelColor,
        });
    }

    private void Configure(NumericUpDown ctrl, int x, int y, int min, int max, int value)
    {
        ctrl.Location    = new Point(x, y);
        ctrl.Size        = new Size(120, 28);
        ctrl.Minimum     = min;
        ctrl.Maximum     = max;
        ctrl.Value       = Math.Clamp(value, min, max);
        ctrl.BackColor   = InputBg;
        ctrl.ForeColor   = Color.White;
        ctrl.BorderStyle = BorderStyle.FixedSingle;
        ctrl.Font        = new Font("Segoe UI", 10f);
    }

    private void Configure(CheckBox ctrl, int x, int y, string text, bool value)
    {
        ctrl.Text      = text;
        ctrl.Location  = new Point(x, y);
        ctrl.AutoSize  = true;
        ctrl.Checked   = value;
        ctrl.ForeColor = Color.White;
        ctrl.Font      = new Font("Segoe UI", 10f);
    }
}
