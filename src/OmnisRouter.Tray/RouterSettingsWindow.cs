using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// Local router settings: the loopback port, and the cache-hygiene defaults (feature 005) — the enabled
/// fixes, whether to report cache-waste to OmnisVigil, and the billing model. These persist with the
/// router settings and apply to the supervised router on its next start; saving restarts it. When an
/// OmnisVigil policy governs the fixes, the fix controls show the policy's set and are not editable, so
/// the window never claims a local edit overrides the policy. Uses the shared dialog chrome.
/// </summary>
internal sealed class RouterSettingsWindow : Form
{
    private const int DialogWidth = 460;

    // (enum name persisted/bound, wire name from the router policy, label shown).
    private static readonly (string Enum, string Wire, string Label)[] FixRows =
    [
        ("LineEnding", "line_ending", "Normalise line endings"),
        ("TrailingWhitespace", "trailing_whitespace", "Strip trailing spaces"),
        ("ToolOrdering", "tool_ordering", "Order tool definitions"),
    ];

    private readonly NumericUpDown _port;
    private readonly CheckBox[] _fixBoxes;
    private readonly CheckBox _emit;
    private readonly ComboBox _billing;
    private readonly Label _error;
    private readonly bool _governed;
    private readonly IReadOnlyList<string> _originalFixes;

    private int _row;

    public RouterSettingsWindow(
        int currentPort,
        IReadOnlyCollection<string> enabledFixes,
        bool emitCacheWaste,
        string billing,
        CacheFixStateInfo? governance)
    {
        Port = currentPort;
        _originalFixes = enabledFixes.ToList();
        _governed = governance?.PolicyOverrides == true;
        EnabledFixes = _originalFixes;
        EmitCacheWaste = emitCacheWaste;
        Billing = string.IsNullOrWhiteSpace(billing) ? "PayAsYouGo" : billing;

        DialogChrome.Apply(this, "OmnisRouter — local router settings");

        var grid = DialogChrome.Grid(
            new ColumnStyle(SizeType.AutoSize),
            new ColumnStyle(SizeType.Percent, 100f));

        AddFull(grid, Note(
            "The local router listens on this loopback port. Changing settings here restarts the router "
            + "and re-points any connected apps."));

        _port = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 65535,
            Value = Math.Clamp(currentPort, 1, 65535),
            Width = 100,
            Anchor = AnchorStyles.Left,
        };
        AddLabelled(grid, "Port", _port);

        // --- Cache hygiene section (feature 005) ---
        AddFull(grid, Title("Cache hygiene"));
        AddFull(grid, Note("Fixes recover cache misses the router can prove safe. Off by default."));

        _fixBoxes = new CheckBox[FixRows.Length];
        var effective = governance?.Effective ?? [];
        for (var i = 0; i < FixRows.Length; i++)
        {
            var (enumName, wire, label) = FixRows[i];
            var box = new CheckBox
            {
                Text = label,
                AutoSize = true,
                // No explicit ForeColor: inherit the dialog's, so the text adapts to the user's Windows
                // theme instead of pinning a colour that can render invisibly (matches SetupWindow).
                Checked = _governed ? effective.Contains(wire) : enabledFixes.Contains(enumName),
                Enabled = !_governed,
                Margin = new Padding(0, 0, 0, 0),
            };
            _fixBoxes[i] = box;
            AddFull(grid, box);
        }

        if (_governed)
        {
            AddFull(grid, Note("These fixes are managed by an OmnisVigil policy, which is in control here."));
        }

        _emit = new CheckBox
        {
            Text = "Report cache-waste to OmnisVigil",
            AutoSize = true,
            Checked = emitCacheWaste,
            Margin = new Padding(0, 8, 0, 0),
        };
        AddFull(grid, _emit);
        AddFull(grid, Note("Only takes effect once an OmnisVigil workspace is connected."));

        _billing = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
            Anchor = AnchorStyles.Left,
        };
        _billing.Items.AddRange(["Pay as you go", "Subscription (shadow estimates)"]);
        _billing.SelectedIndex = string.Equals(Billing, "Subscription", StringComparison.Ordinal) ? 1 : 0;
        AddLabelled(grid, "Billing", _billing);

        AddFull(grid, Note("Saving restarts the router."));

        _error = new Label
        {
            AutoSize = true,
            ForeColor = Color.Firebrick,
            MaximumSize = new Size(DialogWidth - 28, 0),
            Margin = new Padding(0, 8, 0, 0),
        };
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

    /// <summary>The chosen enabled fix set (C# enum names). Unchanged from the original when governed.</summary>
    public IReadOnlyList<string> EnabledFixes { get; private set; }

    /// <summary>Whether to report cache-waste to OmnisVigil.</summary>
    public bool EmitCacheWaste { get; private set; }

    /// <summary>The chosen billing model (<c>PayAsYouGo</c> or <c>Subscription</c>).</summary>
    public string Billing { get; private set; }

    private Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.DimGray,   // matches the secondary-text colour the other dialogs use
        Margin = new Padding(0, 0, 0, 8),
        MaximumSize = new Size(DialogWidth - 28, 0),
    };

    private Label Title(string text)
    {
        var t = DialogChrome.Title(text);
        t.Anchor = AnchorStyles.Left;
        t.Margin = new Padding(0, 10, 0, 4);
        return t;
    }

    private void AddFull(TableLayoutPanel grid, Control control)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(control, 0, _row);
        grid.SetColumnSpan(control, 2);
        _row++;
    }

    private void AddLabelled(TableLayoutPanel grid, string label, Control control)
    {
        var title = DialogChrome.Title(label);
        title.Anchor = AnchorStyles.Left;
        title.Margin = new Padding(0, 4, 12, 0);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(title, 0, _row);
        grid.Controls.Add(control, 1, _row);
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

        // While a policy governs the fixes, the checkboxes are read-only, so keep the persisted local set
        // (it applies again if the policy is later withdrawn). Otherwise take the ticked set.
        if (!_governed)
        {
            EnabledFixes = FixRows.Where((_, i) => _fixBoxes[i].Checked).Select(r => r.Enum).ToList();
        }
        else
        {
            EnabledFixes = _originalFixes;
        }

        EmitCacheWaste = _emit.Checked;
        Billing = _billing.SelectedIndex == 1 ? "Subscription" : "PayAsYouGo";

        DialogResult = DialogResult.OK;
        Close();
    }
}
