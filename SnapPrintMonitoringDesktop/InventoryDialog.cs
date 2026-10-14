namespace SnapPrintMonitoringDesktop;

internal sealed record PaperPreview(bool Valid, int? Remaining, string ActionText, string Error);
internal static class PaperInput
{
    internal static PaperPreview Preview(int? current, InventoryAction action, int amount)
    {
        if (amount is < 0 or > 100000) return new(false, null, "", "Enter a number from 0 to 100,000.");
        if (action == InventoryAction.SetCountedTotal) return new(true, amount, $"Set to {amount:N0} sheets", "");
        if (current is null) return new(false, null, "", "Set Paper Amount first so we know your starting amount.");
        if (amount == 0) return new(false, null, "", "Enter at least 1 sheet.");
        var result = action == InventoryAction.AddRefill ? current.Value + amount : current.Value - amount;
        var text = action == InventoryAction.AddRefill ? $"Add {amount:N0} sheets" : $"Subtract {amount:N0} sheets";
        return result is < 0 or > 100000 ? new(false, null, text, result < 0 ? "You cannot subtract more paper than the current amount." : "The total cannot exceed 100,000 sheets.") : new(true, result, text, "");
    }
}
internal sealed class InventoryDialog : Form
{
    private readonly NumericUpDown sheets = new() { Minimum = 0, Maximum = 100000, Dock = DockStyle.Top, ThousandsSeparator = true };
    private readonly NumericUpDown threshold = new() { Minimum = 0, Maximum = 100000, Dock = DockStyle.Top, ThousandsSeparator = true };
    private readonly CheckBox automatic = new() { Text = "Automatically estimate paper used from print jobs", AutoSize = true };
    private readonly long revision;
    internal InventoryAdjustment Adjustment => new(InventoryAction.SetCountedTotal, (int)sheets.Value, (int)threshold.Value, revision, RequestId, automatic.Checked);
    private Guid RequestId { get; } = Guid.NewGuid();
    internal InventoryDialog(PaperInventory entry)
    {
        revision = entry.Revision;
        Text = "Manage Paper · " + entry.PrinterName;
        ClientSize = new Size(600, 460); MinimumSize = new Size(616, 499);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        Font = new Font("Segoe UI", 10); BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24), ColumnCount = 1 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        void Add(Control control) { control.Margin = new Padding(0, 0, 0, 12); layout.Controls.Add(control); }
        Add(new Label { Text = "Sheets Loaded", AutoSize = true });
        sheets.Value = entry.EstimatedSheetsRemaining ?? 0; Add(sheets);
        Add(new Label { Text = "Low Paper Alert At", AutoSize = true });
        threshold.Value = entry.LowPaperThreshold; Add(threshold);
        Add(new Label { Text = "Saving replaces the current paper estimate.\r\nAlerts include the threshold. Zero sheets means Paper Empty.", AutoSize = true, ForeColor = Color.DimGray });
        automatic.Checked = entry.AutomaticPaperEstimateRequested; Add(automatic);
        Add(new Label { Text = "Automatic Paper Estimate\r\nEstimate paper usage from Windows print jobs. Actual paper usage may differ.\r\nThis preference does not enable deductions without sufficient job information.\r\nCurrent Windows data is insufficient: automatic counting is unavailable.\r\nCheck the actual paper if the number looks incorrect.", AutoSize = true, ForeColor = Color.DimGray });
        var save = new Button { Text = "Save Paper Settings", AutoSize = true };
        save.Click += (_, _) =>
        {
            // Validate typed text as well as spin-button changes before reading Value.
            ValidateChildren();
            DialogResult = DialogResult.OK; Close();
        };
        Add(save); Controls.Add(layout); AcceptButton = save;
    }
}
