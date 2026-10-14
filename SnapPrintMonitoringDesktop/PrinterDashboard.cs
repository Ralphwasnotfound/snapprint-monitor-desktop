namespace SnapPrintMonitoringDesktop;

internal sealed class PrinterDashboard : UserControl
{
    private static readonly Color Ink = Color.FromArgb(28, 39, 56);
    private static readonly Color Muted = Color.FromArgb(99, 111, 128);
    private readonly PrinterCard canon = new("Canon MG3000 series", "CANON");
    private readonly PrinterCard epson = new("EPSON L3210 Series", "EPSON");
    private readonly PrintActivityView currentPrinting = new(true);
    private readonly PrintActivityView printHistory = new(false);
    internal void UpdateJobs(IReadOnlyList<PrintHistoryEntry> entries, string? error) { currentPrinting.Update(entries,error); printHistory.Update(entries,error); }
    private readonly Label serverLabel;
    private readonly Label endpointLabel;
    private readonly Label updatedLabel;
    private readonly Label discoveryLabel;
    private readonly Button diagnosticsButton;
    private Form? diagnosticsWindow;
    private TextBox? diagnosticsText;
    private PrinterSnapshot snapshot = PrinterSnapshot.Initial;
    private string notificationDetail = "Windows notifications not initialized.";
    internal event Action? NotificationTestRequested;
    private string serverDetail = "Local server starting...";
    internal IReadOnlyList<PrinterCard> Cards => [canon, epson];
    internal event Action<string, InventoryAdjustment>? InventoryAdjustmentRequested;

    internal PrinterDashboard()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(246, 248, 251);
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10F);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));
        var title = TextLabel("Print and Snap", 24, FontStyle.Bold, Ink);
        title.Margin = new Padding(0, 0, 0, 2);
        root.Controls.Add(title, 0, 0);
        var subtitle = TextLabel("Printer monitoring", 11, FontStyle.Regular, Muted);
        subtitle.Margin = new Padding(1, 0, 0, 12);
        root.Controls.Add(subtitle, 0, 1);
        var serverRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
        serverRow.ColumnStyles.Add(new(SizeType.AutoSize));
        serverRow.ColumnStyles.Add(new(SizeType.Percent, 100));
        serverLabel = TextLabel("●  Server starting", 9, FontStyle.Bold, Muted);
        serverLabel.Margin = new Padding(0, 0, 16, 0);
        endpointLabel = TextLabel("", 9, FontStyle.Regular, Muted);
        endpointLabel.AutoSize = false; endpointLabel.AutoEllipsis = true; endpointLabel.Dock = DockStyle.Fill;
        serverRow.Controls.Add(serverLabel, 0, 0); serverRow.Controls.Add(endpointLabel, 1, 0);
        root.Controls.Add(serverRow, 0, 2);
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        cards.ColumnStyles.Add(new(SizeType.Percent, 50)); cards.ColumnStyles.Add(new(SizeType.Percent, 50));
        cards.RowStyles.Add(new(SizeType.Percent, 100));
        canon.Margin = new Padding(0, 0, 10, 0); epson.Margin = new Padding(10, 0, 0, 0);
        cards.Controls.Add(canon, 0, 0); cards.Controls.Add(epson, 1, 0);
        root.Controls.Add(cards, 0, 3);
        discoveryLabel = TextLabel("Discovering Windows printers…", 9, FontStyle.Regular, Muted);
        discoveryLabel.Margin = new Padding(0, 14, 0, 10);
        discoveryLabel.MaximumSize = new Size(0, 0);
        root.Controls.Add(discoveryLabel, 0, 4);
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        updatedLabel = TextLabel("Automatic refresh · every 3 seconds", 9, FontStyle.Regular, Muted);
        updatedLabel.Anchor = AnchorStyles.Left;
        diagnosticsButton = new Button { Text = "Diagnostics", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White,
            ForeColor = Ink, Padding = new Padding(14, 5, 14, 5), Cursor = Cursors.Hand, Margin = Padding.Empty };
        diagnosticsButton.FlatAppearance.BorderColor = Color.FromArgb(218, 224, 232);
        diagnosticsButton.Click += (_, _) => OpenDiagnostics();
        footer.ColumnStyles.Add(new(SizeType.AutoSize));
        var testNotification = new Button { Text = "Test notification", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Padding = new Padding(10,5,10,5), Margin = new Padding(0,0,10,0), Cursor = Cursors.Hand };
        testNotification.FlatAppearance.BorderColor = Color.FromArgb(218,224,232);
        testNotification.Click += (_,_) => NotificationTestRequested?.Invoke();
        footer.Controls.Add(updatedLabel, 0, 0); footer.Controls.Add(testNotification, 1, 0); footer.Controls.Add(diagnosticsButton, 2, 0);
        root.Controls.Add(footer, 0, 5);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var statusPage = new TabPage("PRINTER STATUS / PAPER SETTINGS"); statusPage.Controls.Add(root);
        var currentPage = new TabPage("CURRENT PRINTING"); currentPage.Controls.Add(currentPrinting);
        var historyPage = new TabPage("PRINT HISTORY"); historyPage.Controls.Add(printHistory);
        tabs.TabPages.AddRange([statusPage,currentPage,historyPage]); Controls.Add(tabs);
        foreach (var card in Cards) card.AdjustRequested += OpenInventory;
        UpdateSnapshot(snapshot);
    }
    internal static Label TextLabel(string text, float size, FontStyle style, Color color) => new()
    {
        Text = text, AutoSize = true, ForeColor = color, Font = new Font("Segoe UI", size, style), Margin = Padding.Empty,
        UseMnemonic = false
    };
    internal void SetNotificationStatus(string status) { notificationDetail = status; RefreshDiagnostics(); }
    internal void SetServerStatus(bool? running, string endpoint, string detail)
    {
        serverDetail = detail;
        serverLabel.Text = running switch { true => "●  Server running", false => "●  Server stopped", _ => "●  Server starting" };
        serverLabel.ForeColor = running switch { true => Color.FromArgb(35, 115, 83), false => Color.FromArgb(151, 70, 43), _ => Muted };
        endpointLabel.Text = running == true ? endpoint : running == false ? "Open diagnostics for connection details" : "";
        endpointLabel.AccessibleName = "Monitoring server address";
        RefreshDiagnostics();
    }
    internal void UpdateSnapshot(PrinterSnapshot value)
    {
        snapshot = value;
        currentPrinting.UpdatePaper(value.PaperInventories); printHistory.UpdatePaper(value.PaperInventories);
        // Presentation only: the store and HTTP API retain every discovered printer.
        foreach (var card in Cards)
            card.UpdateReading(value.Printers.FirstOrDefault(p => string.Equals(p.Queue, card.PrinterName, StringComparison.OrdinalIgnoreCase)), value.DiscoveryError);
        foreach (var card in Cards)
            card.UpdateInventory(value.PaperInventories.FirstOrDefault(i => string.Equals(i.PrinterName, card.PrinterName, StringComparison.OrdinalIgnoreCase)) ?? PaperInventory.Empty(card.PrinterName));
        discoveryLabel.Text = value.DiscoveryError is not null ? "Printer discovery unavailable. Open diagnostics for details." :
            value.Printers.Count == 0 ? "No installed printers found. Check Windows printer setup." :
            "USB presence and normal queue status do not verify physical readiness.";
        if (value.InventoryError is not null) discoveryLabel.Text = "Inventory storage unavailable. Open diagnostics; counts may be stale.";
        discoveryLabel.ForeColor = value.DiscoveryError is not null || value.InventoryError is not null ? Color.FromArgb(151, 70, 43) : Muted;
        updatedLabel.Text = $"Updated {value.LastUpdated.ToLocalTime():HH:mm:ss}  ·  Automatic refresh";
        RefreshDiagnostics();
    }
    private void OpenInventory(string printer)
    {
        var entry = snapshot.PaperInventories.FirstOrDefault(i => string.Equals(i.PrinterName, printer, StringComparison.OrdinalIgnoreCase)) ?? PaperInventory.Empty(printer);
        using var dialog = new InventoryDialog(entry);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK) InventoryAdjustmentRequested?.Invoke(printer, dialog.Adjustment);
    }
    private void OpenDiagnostics()
    {
        if (diagnosticsWindow is { IsDisposed: false }) { diagnosticsWindow.Activate(); return; }
        diagnosticsText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, BackColor = Color.White, ForeColor = Ink, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10) };
        diagnosticsWindow = new Form { Text = "Print and Snap · Diagnostics", ClientSize = new Size(900, 600), MinimumSize = new Size(500, 320),
            StartPosition = FormStartPosition.CenterParent, Padding = new Padding(20), BackColor = Color.White, ShowInTaskbar = false };
        diagnosticsWindow.Controls.Add(diagnosticsText);
        RefreshDiagnostics();
        diagnosticsWindow.Show(FindForm());
    }
    private void RefreshDiagnostics()
    {
        if (diagnosticsText is null || diagnosticsText.IsDisposed) return;
        var inventoryText = "\r\n\r\nPAPER INVENTORY (operator estimates; automatic deduction disabled)\r\n" +
            (snapshot.InventoryError ?? "Local inventory storage available.") + "\r\n" +
            string.Join("\r\n", snapshot.PaperInventories.Select(i => $"{i.PrinterName}: estimate={i.EstimatedSheetsRemaining?.ToString() ?? "unknown"}; threshold={i.LowPaperThreshold}; confidence={i.Confidence}; sync={i.SynchronizationStatus}; updated={i.LastInventoryUpdate}; autoEnabled={i.AutomaticDeductionEnabled}; {i.AutomaticDeductionStatus}; lastAuto={i.LastAutomaticDeduction}; lastDecision={i.LastUsageDecision}; {i.ReconciliationReason}"));
        var text = serverDetail + "\r\nNotifications: " + notificationDetail + inventoryText + $"\r\n\r\nWindows discovery: {snapshot.LastUpdated.ToLocalTime():G}\r\n" +
            (snapshot.DiscoveryError ?? $"{snapshot.Printers.Count} installed queues discovered.") + "\r\n\r\n" +
            string.Join("\r\n\r\n", snapshot.Printers.Select(p => $"{p.Queue}\r\n{p.Detail}"));
        if (diagnosticsText.Text != text) { var start = diagnosticsText.SelectionStart; diagnosticsText.Text = text; diagnosticsText.SelectionStart = Math.Min(start, text.Length); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) diagnosticsWindow?.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class PrinterCard : Panel
{
    internal string PrinterName { get; }
    internal string PhysicalText => physical.Text;
    internal string ConnectionText => connection.Text;
    internal string QueueText => queue.Text;
    internal string AvailabilityText => availability.Text;
    private readonly Label physical, connection, queue, availability;
    private readonly Label paperCount, paperAlert, paperSync, paperUpdated, paperAutomatic;
    internal event Action<string>? AdjustRequested;
    internal string InventoryText => paperCount.Text + " " + paperAlert.Text + " " + paperSync.Text;
    internal PrinterCard(string name, string brand)
    {
        PrinterName = name;
        Dock = DockStyle.Fill; BackColor = Color.White; Padding = new Padding(16); DoubleBuffered = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 8; i++) layout.RowStyles.Add(new(i == 7 ? SizeType.Percent : SizeType.AutoSize, i == 7 ? 100 : 0));
        var header = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0,0,0,5) };
        header.Controls.Add(new PrinterGlyph { Size = new Size(26, 26), Margin = new Padding(0,0,10,0) });
        var brandLabel = PrinterDashboard.TextLabel(brand, 9, FontStyle.Bold, Color.FromArgb(105,119,138));
        brandLabel.Margin = new Padding(0,5,0,0); header.Controls.Add(brandLabel); layout.Controls.Add(header,0,0);
        var title = PrinterDashboard.TextLabel(name, 16, FontStyle.Bold, Color.FromArgb(28,39,56));
        title.Margin = new Padding(0,0,0,3); title.Dock = DockStyle.Fill; layout.Controls.Add(title,0,1);
        availability = PrinterDashboard.TextLabel("Waiting for discovery", 9, FontStyle.Regular, Color.FromArgb(99,111,128));
        availability.Margin = new Padding(0,0,0,3); layout.Controls.Add(availability,0,2);
        layout.Controls.Add(StatusRow("Physical status", out physical, true),0,3);
        layout.Controls.Add(StatusRow("USB connection", out connection),0,4);
        layout.Controls.Add(StatusRow("Windows queue", out queue),0,5);
        var paper = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Margin = new Padding(0,4,0,0) };
        paper.ColumnStyles.Add(new(SizeType.Percent,100));
        paperCount = PrinterDashboard.TextLabel("Paper Remaining (Estimated): —", 11, FontStyle.Bold, Color.FromArgb(28,39,56));
        paperCount.Margin = new Padding(0,0,0,3); paper.Controls.Add(paperCount,0,0);
        paperAlert = PrinterDashboard.TextLabel("Count needed",9,FontStyle.Bold,Color.FromArgb(151,94,27));
        paper.Controls.Add(paperAlert,0,1);
        paperSync = PrinterDashboard.TextLabel("Operator estimate · unverified",9,FontStyle.Regular,Color.DimGray);
        paper.Controls.Add(paperSync,0,2);
        paperUpdated = PrinterDashboard.TextLabel("Inventory updated: —",8,FontStyle.Regular,Color.DimGray);
        paperUpdated.Margin = new Padding(0,0,0,3); paper.Controls.Add(paperUpdated,0,3);
        paperAutomatic = PrinterDashboard.TextLabel("Last auto: none · unavailable",8,FontStyle.Regular,Color.DimGray);
        paperAutomatic.Margin = new Padding(0,0,0,3); paper.Controls.Add(paperAutomatic,0,4);
        var adjust = new Button { Text = "Manage Paper", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Cursor = Cursors.Hand, Padding = new Padding(8,2,8,2), Margin = Padding.Empty };
        adjust.FlatAppearance.BorderColor = Color.FromArgb(218,224,232);
        adjust.Click += (_,_) => AdjustRequested?.Invoke(PrinterName); paper.Controls.Add(adjust,0,5);
        layout.Controls.Add(paper,0,6);
        Controls.Add(layout);
        AccessibleName = name;
    }
    private static Control StatusRow(string name, out Label value, bool badge = false)
    {
        var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 0, 0, 2) };
        row.ColumnStyles.Add(new(SizeType.Percent, 46));
        row.ColumnStyles.Add(new(SizeType.Percent, 54));
        var label = PrinterDashboard.TextLabel(name, 9, FontStyle.Regular, Color.FromArgb(99, 111, 128));
        label.Anchor = AnchorStyles.Left;
        value = PrinterDashboard.TextLabel("UNKNOWN", 11, FontStyle.Bold, Color.FromArgb(59, 72, 91));
        value.AccessibleName = name;
        value.Anchor = AnchorStyles.Left;
        if (badge) { value.BackColor = Color.FromArgb(239, 242, 246); value.Padding = new Padding(10, 4, 10, 4); }
        row.Controls.Add(label, 0, 0); row.Controls.Add(value, 1, 0);
        return row;
    }
    internal void UpdateReading(PrinterReading? reading, string? discoveryError)
    {
        physical.Text = reading?.PhysicalStatus ?? "UNKNOWN"; // The current classifier never verifies readiness; USB presence is not ONLINE.
        connection.Text = reading?.Evidence?.ConnectionStatus switch { "USB_PRESENT" => "USB present", "USB_NOT_PRESENT" => "USB not present", _ => "Unknown" };
        queue.Text = reading?.Evidence?.QueueStatus switch
        {
            "NORMAL_REPORTED" => "Normal reported", "PRINTING" => "Printing reported", "OFFLINE" => "Offline reported",
            "ERROR" => "Error reported", "WARNING" => "Warning reported", "PAUSED" => "Paused", "NOT READY" => "Not ready reported",
            "CONFLICTING_WINDOWS_REPORTS" => "Conflicting reports", _ => "Unknown"
        };
        availability.Text = discoveryError is not null ? "Discovery unavailable" : reading is null ? "Not found in Windows" : "Installed Windows printer";
        connection.ForeColor = reading?.Evidence?.ConnectionStatus == "USB_NOT_PRESENT" ? Color.FromArgb(151, 94, 27) : Color.FromArgb(59, 72, 91);
        queue.ForeColor = reading?.Evidence?.QueueStatus is "ERROR" or "OFFLINE" or "WARNING" ? Color.FromArgb(151, 94, 27) : Color.FromArgb(59, 72, 91);
    }
    internal void UpdateInventory(PaperInventory entry)
    {
        paperCount.Text = entry.EstimatedSheetsRemaining is int count ? $"Paper Remaining (Estimated): {count:N0}" : "Paper Remaining (Estimated): —";
        paperAlert.Text = entry.EmptyWarning ? "PAPER EMPTY" : entry.LowPaperWarning ? $"LOW PAPER · last estimate (≤{entry.LowPaperThreshold})" : entry.EstimatedSheetsRemaining is null ? "Count needed" : entry.ReconciliationRequired ? "Check paper amount" : $"Low Paper Alert At: {entry.LowPaperThreshold:N0}";
        paperAlert.ForeColor = entry.EmptyWarning ? Color.FromArgb(160,55,45) : entry.LowPaperWarning ? Color.FromArgb(151,94,27) : Color.DimGray;
        paperSync.Text = entry.PersistenceStatus != "AVAILABLE" ? "Storage unavailable · may be stale" : entry.ReconciliationRequired ? "Check paper amount" : "Paper amount entered · estimated";
        paperSync.ForeColor = entry.ReconciliationRequired ? Color.FromArgb(151,94,27) : Color.DimGray;
        paperAutomatic.Text = entry.LastAutomaticDeduction is { } deduction ? $"Last auto: −{deduction.Sheets} · {deduction.At.ToLocalTime():MMM d HH:mm}" : entry.AutomaticPaperEstimateRequested ? "Auto estimate requested · insufficient job data" : "Automatic Paper Estimate: off";
        paperUpdated.Text = entry.LastInventoryUpdate is DateTimeOffset time ? $"Inventory updated: {time.ToLocalTime():MMM d HH:mm}" : "Inventory updated: never";
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(224, 229, 237));
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }
    private sealed class PrinterGlyph : Control
    {
        internal PrinterGlyph() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float scale = Math.Min(Width, Height) / 44f; e.Graphics.ScaleTransform(scale, scale);
            using var pen = new Pen(Color.FromArgb(82, 104, 132), 1.7f);
            using var fill = new SolidBrush(Color.FromArgb(239, 244, 250));
            e.Graphics.FillRectangle(fill, 1, 1, 42, 42);
            e.Graphics.DrawRectangle(pen, 13, 9, 18, 12); e.Graphics.DrawRectangle(pen, 8, 18, 28, 14);
            e.Graphics.FillRectangle(Brushes.White, 13, 26, 18, 11); e.Graphics.DrawRectangle(pen, 13, 26, 18, 11);
            e.Graphics.DrawLine(pen, 17, 30, 27, 30); e.Graphics.DrawLine(pen, 17, 33, 25, 33);
        }
    }
}
