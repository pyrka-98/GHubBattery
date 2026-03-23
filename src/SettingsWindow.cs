using System.Windows.Forms;
using System.Drawing;

namespace GHubBattery;

/// <summary>
/// Small settings dialog. Opens from the tray context menu.
/// Edits AppSettings and saves on OK.
/// </summary>
public sealed class SettingsWindow : Form
{
    private readonly AppSettings _settings;

    private readonly NumericUpDown _lowThreshold    = new();
    private readonly NumericUpDown _critThreshold   = new();
    private readonly NumericUpDown _pollInterval    = new();
    private readonly CheckBox      _showAllDevices  = new();
    private readonly CheckBox      _notifications   = new();
    private readonly Button        _ok     = new();
    private readonly Button        _cancel = new();

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;

        Text            = "G HUB Battery — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        ClientSize      = new Size(320, 270);
        Font            = new Font("Segoe UI", 9f);
        BackColor       = Color.FromArgb(30, 30, 30);
        ForeColor       = Color.White;

        int y = 16;

        AddLabel("Low battery warning (%)",  16, y);       y += 22;
        Configure(_lowThreshold, 16, y, 1, 99, _settings.LowBatteryThreshold); y += 36;

        AddLabel("Critical battery level (%)", 16, y);     y += 22;
        Configure(_critThreshold, 16, y, 1, 99, _settings.CriticalThreshold); y += 36;

        AddLabel("Poll interval (seconds)", 16, y);        y += 22;
        Configure(_pollInterval, 16, y, 10, 3600, _settings.PollIntervalSeconds); y += 36;

        Configure(_showAllDevices, 16, y, "Show all devices in menu", _settings.ShowAllDevices); y += 28;
        Configure(_notifications,  16, y, "Enable low-battery notifications", _settings.NotificationsEnabled); y += 36;

        _ok.Text     = "OK";
        _ok.Size     = new Size(80, 28);
        _ok.Location = new Point(ClientSize.Width - 188, y);
        _ok.BackColor = Color.FromArgb(0x18, 0x6A, 0xB0);
        _ok.ForeColor = Color.White;
        _ok.FlatStyle = FlatStyle.Flat;
        _ok.FlatAppearance.BorderSize = 0;
        _ok.Click += OnOk;

        _cancel.Text     = "Cancel";
        _cancel.Size     = new Size(80, 28);
        _cancel.Location = new Point(ClientSize.Width - 100, y);
        _cancel.BackColor = Color.FromArgb(60, 60, 60);
        _cancel.ForeColor = Color.White;
        _cancel.FlatStyle = FlatStyle.Flat;
        _cancel.FlatAppearance.BorderSize = 0;
        _cancel.Click += (_, _) => Close();

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
        var lbl = new Label
        {
            Text      = text,
            Location  = new Point(x, y),
            AutoSize  = true,
            ForeColor = Color.FromArgb(180, 180, 180),
        };
        Controls.Add(lbl);
    }

    private void Configure(NumericUpDown ctrl, int x, int y, int min, int max, int value)
    {
        ctrl.Location   = new Point(x, y);
        ctrl.Size       = new Size(100, 24);
        ctrl.Minimum    = min;
        ctrl.Maximum    = max;
        ctrl.Value      = Math.Clamp(value, min, max);
        ctrl.BackColor  = Color.FromArgb(50, 50, 50);
        ctrl.ForeColor  = Color.White;
        ctrl.BorderStyle = BorderStyle.FixedSingle;
    }

    private void Configure(CheckBox ctrl, int x, int y, string text, bool value)
    {
        ctrl.Text      = text;
        ctrl.Location  = new Point(x, y);
        ctrl.AutoSize  = true;
        ctrl.Checked   = value;
        ctrl.ForeColor = Color.White;
    }
}
