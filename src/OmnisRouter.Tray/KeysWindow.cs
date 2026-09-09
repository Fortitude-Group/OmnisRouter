using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// Provider key management (US2): lists Anthropic, OpenAI, Gemini and OpenRouter, whether each has a
/// key set, and lets the user add or remove one. A stored key value is never shown back (FR-009).
/// Disabled until the router reports Running (contracts/router-management.md — readiness gates key
/// management). Built in code, matching SetupWindow's style; no designer files.
/// </summary>
internal sealed class KeysWindow : Form
{
    private static readonly (string Provider, string Display)[] Providers =
    [
        ("anthropic", "Anthropic"),
        ("openai", "OpenAI"),
        ("gemini", "Gemini"),
        ("openrouter", "OpenRouter"),
    ];

    private readonly RouterController _router;
    private readonly Label _notReady;
    private readonly Label _error;
    private readonly List<ProviderRow> _rows = [];

    public KeysWindow(RouterController router)
    {
        _router = router;

        Text = "OmnisRouter provider keys";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(660, 420);

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
            Text = "Paste an API key for each provider you want the router to use. Keys are held only "
                 + "in the router's encrypted store and are never shown again.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
            MaximumSize = new Size(600, 0),
        };

        _notReady = new Label
        {
            Text = "Start the router first — actions are disabled until it is ready.",
            AutoSize = true,
            ForeColor = Color.Firebrick,
            Margin = new Padding(0, 0, 0, 10),
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        foreach (var (provider, display) in Providers)
        {
            AddProviderRow(grid, provider, display);
        }

        _error = new Label
        {
            AutoSize = true,
            ForeColor = Color.Firebrick,
            MaximumSize = new Size(600, 0),
            Margin = new Padding(0, 0, 0, 8),
        };

        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(12, 5, 12, 5) };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0),
        };
        buttons.Controls.Add(close);

        AddRow(layout, intro);
        AddRow(layout, _notReady);
        AddRow(layout, grid);
        AddRow(layout, _error);
        // A stretch row here takes up the slack and pins the buttons to the bottom, so the panel's
        // 18px padding stays as a clear margin beneath them rather than the buttons touching the edge.
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        AddRow(layout, buttons);

        Controls.Add(layout);
        CancelButton = close;

        _router.StatusChanged += OnRouterStatusChanged;
        FormClosed += (_, _) => _router.StatusChanged -= OnRouterStatusChanged;
        Load += async (_, _) => await RefreshAsync();

        ApplyGate();
    }

    private void AddProviderRow(TableLayoutPanel grid, string provider, string display)
    {
        var nameLabel = new Label
        {
            Text = display,
            AutoSize = true,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Margin = new Padding(0, 6, 6, 0),
        };
        var statusLabel = new Label { Text = "—", AutoSize = true, Margin = new Padding(0, 8, 6, 0) };
        var keyBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            PlaceholderText = "Paste key",
            MinimumSize = new Size(0, 27),
            Margin = new Padding(0, 4, 6, 4),
        };
        var saveButton = new Button { Text = "Save", AutoSize = true, Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 3, 6, 3) };
        var removeButton = new Button { Text = "Remove", AutoSize = true, Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 3, 0, 3) };

        var row = grid.RowStyles.Count;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(nameLabel, 0, row);
        grid.Controls.Add(statusLabel, 1, row);
        grid.Controls.Add(keyBox, 2, row);
        grid.Controls.Add(saveButton, 3, row);
        grid.Controls.Add(removeButton, 4, row);

        var providerRow = new ProviderRow(provider, statusLabel, keyBox, saveButton, removeButton);
        _rows.Add(providerRow);

        saveButton.Click += async (_, _) => await OnSaveAsync(providerRow);
        removeButton.Click += async (_, _) => await OnRemoveAsync(providerRow);
    }

    private bool IsReady => _router.ManagementClient is not null && _router.Status.State == RouterProcessState.Running;

    private void OnRouterStatusChanged(object? sender, RouterStatus status)
    {
        ApplyGate();
        if (status.State == RouterProcessState.Running)
        {
            _ = RefreshAsync();
        }
    }

    private void ApplyGate()
    {
        var ready = IsReady;
        _notReady.Visible = !ready;
        foreach (var row in _rows)
        {
            row.KeyBox.Enabled = ready;
            row.SaveButton.Enabled = ready;
            row.RemoveButton.Enabled = ready && row.KeyId is not null;
        }
    }

    private async Task RefreshAsync()
    {
        if (!IsReady)
        {
            ApplyGate();
            return;
        }

        try
        {
            var keys = await _router.ManagementClient!.ListKeysAsync().ConfigureAwait(true);
            foreach (var row in _rows)
            {
                var match = keys.FirstOrDefault(k => string.Equals(k.Provider, row.Provider, StringComparison.OrdinalIgnoreCase));
                row.KeyId = match?.Id;
                row.StatusLabel.Text = match is null ? "No key" : "Key set";
            }

            _error.Text = "";
        }
        catch (HttpRequestException ex)
        {
            _error.Text = ex.Message;
        }

        ApplyGate();
    }

    private async Task OnSaveAsync(ProviderRow row)
    {
        var key = row.KeyBox.Text.Trim();
        if (key.Length == 0)
        {
            _error.Text = "Paste a key first.";
            return;
        }

        try
        {
            var label = $"{row.Provider} ({Environment.MachineName})";
            await _router.ManagementClient!.CreateKeyAsync(row.Provider, label, key).ConfigureAwait(true);
            row.KeyBox.Clear();
            _error.Text = "";
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (HttpRequestException ex)
        {
            _error.Text = ex.Message;
        }
    }

    private async Task OnRemoveAsync(ProviderRow row)
    {
        if (row.KeyId is not { } id)
        {
            return;
        }

        try
        {
            await _router.ManagementClient!.DeleteKeyAsync(id).ConfigureAwait(true);
            _error.Text = "";
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (HttpRequestException ex)
        {
            _error.Text = ex.Message;
        }
    }

    private static void AddRow(TableLayoutPanel layout, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control, 0, layout.RowStyles.Count - 1);
    }

    private sealed class ProviderRow(string provider, Label statusLabel, TextBox keyBox, Button saveButton, Button removeButton)
    {
        public string Provider { get; } = provider;

        public Label StatusLabel { get; } = statusLabel;

        public TextBox KeyBox { get; } = keyBox;

        public Button SaveButton { get; } = saveButton;

        public Button RemoveButton { get; } = removeButton;

        public string? KeyId { get; set; }
    }
}
