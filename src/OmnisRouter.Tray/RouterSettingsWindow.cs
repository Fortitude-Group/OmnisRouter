using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// Local router settings (US5): the loopback port the proxy listens on. Validated against the allowed
/// range (1024–65535) before saving, so an out-of-range value is reported rather than thrown. Applying
/// a changed port restarts the router and re-points connected clients — the caller (TrayContext) does
/// that once <see cref="Port"/> comes back on OK. Built in code, matching the other windows; no designer
/// files.
/// </summary>
internal sealed class RouterSettingsWindow : Form
{
    private readonly NumericUpDown _port;
    private readonly Label _error;

    public RouterSettingsWindow(int currentPort)
    {
        Port = currentPort;

        Text = "OmnisRouter — local router settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(460, 220);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            AutoSize = false,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var intro = new Label
        {
            Text = "The local router listens on this loopback port. Changing it restarts the router and "
                 + "re-points any connected apps to the new address.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
            MaximumSize = new Size(420, 0),
        };

        var portLabel = new Label { Text = "Port", AutoSize = true, Margin = new Padding(0, 0, 0, 3) };
        _port = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 65535,
            Value = Math.Clamp(currentPort, 1, 65535),
            Width = 120,
            Margin = new Padding(0, 0, 0, 10),
        };

        _error = new Label
        {
            AutoSize = true,
            ForeColor = Color.Firebrick,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 0, 0, 8),
        };

        var save = new Button { Text = "Save", AutoSize = true, Padding = new Padding(14, 5, 14, 5), Margin = new Padding(8, 0, 0, 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(12, 5, 12, 5), DialogResult = DialogResult.Cancel };
        save.Click += OnSave;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0),
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        AddRow(layout, intro);
        AddRow(layout, portLabel);
        AddRow(layout, _port);
        AddRow(layout, _error);
        AddRow(layout, buttons);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
    }

    /// <summary>The chosen port (valid only when <see cref="Form.ShowDialog()"/> returns OK).</summary>
    public int Port { get; private set; }

    private void OnSave(object? sender, EventArgs e)
    {
        var port = (int)_port.Value;
        if (RouterSettings.ValidatePort(port) is { } error)
        {
            _error.Text = error;
            return;
        }

        Port = port;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddRow(TableLayoutPanel layout, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control, 0, layout.RowStyles.Count - 1);
    }
}
