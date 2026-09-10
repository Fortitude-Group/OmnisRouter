using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// Local router settings (US5): the loopback port the proxy listens on, validated to the allowed range
/// before saving. Applying a changed port restarts the router and re-points connected clients (the
/// caller does that once <see cref="Port"/> comes back on OK). Uses the shared dialog chrome.
/// </summary>
internal sealed class RouterSettingsWindow : Form
{
    private const int DialogWidth = 460;

    private readonly NumericUpDown _port;
    private readonly Label _error;

    public RouterSettingsWindow(int currentPort)
    {
        Port = currentPort;

        DialogChrome.Apply(this, "OmnisRouter — local router settings");

        var grid = DialogChrome.Grid(
            new ColumnStyle(SizeType.AutoSize),
            new ColumnStyle(SizeType.Percent, 100f));

        AddFull(grid, new Label
        {
            Text = "The local router listens on this loopback port. Changing it restarts the router and "
                 + "re-points any connected apps to the new address.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
            MaximumSize = new Size(DialogWidth - 28, 0),
        });

        _port = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 65535,
            Value = Math.Clamp(currentPort, 1, 65535),
            Width = 100,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 0),
        };
        var portTitle = DialogChrome.Title("Port");
        portTitle.Anchor = AnchorStyles.Left;
        portTitle.Margin = new Padding(0, 4, 12, 0);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(portTitle, 0, _row);
        grid.Controls.Add(_port, 1, _row);
        _row++;

        _error = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(DialogWidth - 28, 0), Margin = new Padding(0, 8, 0, 0) };
        AddFull(grid, _error);

        var save = DialogChrome.Button("Save");
        var cancel = DialogChrome.Button("Cancel", DialogResult.Cancel);
        save.Click += OnSave;
        DialogChrome.Compose(this, DialogWidth, grid, DialogChrome.ButtonBar(save, cancel));
        AcceptButton = save;
        CancelButton = cancel;
    }

    /// <summary>The chosen port (valid only when <see cref="Form.ShowDialog()"/> returns OK).</summary>
    public int Port { get; private set; }

    private int _row;

    private void AddFull(TableLayoutPanel grid, Control control)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(control, 0, _row);
        grid.SetColumnSpan(control, 2);
        _row++;
    }

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
}
