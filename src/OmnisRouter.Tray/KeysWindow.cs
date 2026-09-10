using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// Provider key management (US2): lists Anthropic, OpenAI, Gemini and OpenRouter, whether each has a
/// key set, and lets the user add or remove one. A stored key value is never shown back (FR-009).
/// Disabled until the router reports Running (contracts/router-management.md). Uses the shared dialog
/// chrome: one flat grid, bold provider titles, a docked button bar, and content-measured sizing.
/// </summary>
internal sealed class KeysWindow : Form
{
    private const int DialogWidth = 600;

    private static readonly (string Provider, string Display)[] Providers =
    [
        ("anthropic", "Anthropic"),
        ("openai", "OpenAI"),
        ("gemini", "Gemini"),
        ("openrouter", "OpenRouter"),
    ];

    private readonly RouterController _router;
    private readonly TableLayoutPanel _grid;
    private readonly Label _notReady;
    private readonly Label _error;
    private readonly List<ProviderRow> _rows = [];
    private int _row;

    public KeysWindow(RouterController router)
    {
        _router = router;

        DialogChrome.Apply(this, "OmnisRouter provider keys");

        _grid = DialogChrome.Grid(
            new ColumnStyle(SizeType.AutoSize),         // provider name (bold)
            new ColumnStyle(SizeType.AutoSize),         // status
            new ColumnStyle(SizeType.Percent, 100f),    // key box
            new ColumnStyle(SizeType.AutoSize),         // save
            new ColumnStyle(SizeType.AutoSize));        // remove

        AddFull(new Label
        {
            Text = "Paste an API key for each provider you want the router to use. Keys are held only "
                 + "in the router's encrypted store and are never shown again.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
            MaximumSize = new Size(DialogWidth - 28, 0),
        });

        _notReady = new Label { Text = "Start the router first. Key actions are disabled until it is ready.", AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(0, 0, 0, 6) };
        AddFull(_notReady);

        foreach (var (provider, display) in Providers)
        {
            AddProviderRow(provider, display);
        }

        _error = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(DialogWidth - 28, 0), Margin = new Padding(0, 6, 0, 0) };
        AddFull(_error);

        var close = DialogChrome.Button("Close", DialogResult.OK);
        DialogChrome.Compose(this, DialogWidth, _grid, DialogChrome.ButtonBar(close));
        CancelButton = close;

        _router.StatusChanged += OnRouterStatusChanged;
        FormClosed += (_, _) => _router.StatusChanged -= OnRouterStatusChanged;
        Load += async (_, _) => await RefreshAsync();

        ApplyGate();
    }

    private void AddProviderRow(string provider, string display)
    {
        var name = DialogChrome.Title(display);
        name.Margin = new Padding(0, 0, 12, 0);
        var status = new Label { Text = "—", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) };
        var keyBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, UseSystemPasswordChar = true, PlaceholderText = "Paste key", Margin = new Padding(0, 0, 8, 0) };
        var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(72, 26), Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 6, 0) };
        var remove = new Button { Text = "Remove", AutoSize = true, MinimumSize = new Size(80, 26), Anchor = AnchorStyles.Right, Margin = new Padding(0) };

        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(name, 0, _row);
        _grid.Controls.Add(status, 1, _row);
        _grid.Controls.Add(keyBox, 2, _row);
        _grid.Controls.Add(save, 3, _row);
        _grid.Controls.Add(remove, 4, _row);
        _row++;

        var row = new ProviderRow(provider, status, keyBox, save, remove);
        _rows.Add(row);
        save.Click += async (_, _) => await OnSaveAsync(row);
        remove.Click += async (_, _) => await OnRemoveAsync(row);
    }

    private void AddFull(Control control)
    {
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(control, 0, _row);
        _grid.SetColumnSpan(control, 5);
        _row++;
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
                row.StatusLabel.ForeColor = match is null ? Color.DimGray : Color.SeaGreen;
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
