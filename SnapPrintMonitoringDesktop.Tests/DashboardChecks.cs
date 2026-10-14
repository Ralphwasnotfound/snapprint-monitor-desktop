using System.Drawing;
using System.Windows.Forms;
using SnapPrintMonitoringDesktop;

internal static class DashboardChecks
{
    internal static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { CheckDashboard(); } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("UI checks failed", failure);
    }
    private static void CheckDashboard()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        using var host = new Form { ClientSize = new Size(1040, 660), StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        using var dashboard = new PrinterDashboard();
        host.Controls.Add(dashboard); host.Show(); Application.DoEvents();
        var usb = new UsbEvidence("EXACT_QUEUE_DEVICE_ID", @"USBPRINT\TEST\1", true, 8, 0, "Fixture");
        var evidence = PrinterSignals.Classify(usb, new Dictionary<string,string>(), [], "AVAILABLE", "NORMAL_REPORTED");
        var snapshot = new PrinterSnapshot(DateTimeOffset.UtcNow,
            [new PrinterReading("Canon MG3000 series", "UNKNOWN", "Canon diagnostic fixture") { Evidence = evidence },
             new PrinterReading("EPSON L3210 Series", "UNKNOWN", "Epson diagnostic fixture") { Evidence = evidence with { ConnectionStatus = "USB_NOT_PRESENT", QueueStatus = "OFFLINE" } },
             new PrinterReading("Microsoft Print to PDF", "UNKNOWN", "Virtual printer fixture")], null);
        snapshot = snapshot with { PaperInventories = [
            PaperInventory.Empty("Canon MG3000 series") with { EstimatedSheetsRemaining = 2, SheetsLoaded = 10, TotalEstimatedSheetsAccountedFor = 8, LastInventoryUpdate = DateTimeOffset.UtcNow, SynchronizationStatus = "RECONCILIATION_REQUIRED" },
            PaperInventory.Empty("EPSON L3210 Series") with { EstimatedSheetsRemaining = 0, SheetsLoaded = 5, TotalEstimatedSheetsAccountedFor = 5, LastInventoryUpdate = DateTimeOffset.UtcNow, SynchronizationStatus = "OPERATOR_BASELINE" }] };
        dashboard.SetServerStatus(true, "https://192.168.254.120:5050", "Server running; authentication required; Firebase disabled.");
        dashboard.UpdateSnapshot(snapshot); host.PerformLayout(); dashboard.PerformLayout();
        Check(dashboard.Cards.Count == 2 && dashboard.Cards.All(c => c.PrinterName != "Microsoft Print to PDF"), "Only two dashboard cards");
        Check(snapshot.Printers.Count == 3, "Full discovery snapshot remains unfiltered");
        Check(dashboard.Cards.All(c => c.PhysicalText == "UNKNOWN"), "USB presence never becomes ONLINE");
        Check(dashboard.Cards[0].ConnectionText == "USB present" && dashboard.Cards[1].ConnectionText == "USB not present", "USB states separate");
        Check(dashboard.Cards[1].QueueText == "Offline reported", "Queue offline is separately labeled");
        var output = Path.Combine(Directory.GetCurrentDirectory(), "diagnostics"); Directory.CreateDirectory(output);
        void Render(string name)
        {
            host.PerformLayout(); dashboard.PerformLayout();
            using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
            dashboard.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, name + ".png"));
        }
        Check(dashboard.Cards[0].InventoryText.Contains("LOW PAPER") && dashboard.Cards[0].InventoryText.Contains("Check paper amount"), "Low-paper warning and uncertainty remain separate");
        Check(dashboard.Cards[1].InventoryText.Contains("EMPTY"), "Empty estimate warning visible");
        int notificationTests = 0;
        dashboard.NotificationTestRequested += () => notificationTests++;
        var inventoryBeforeTest = dashboard.Cards[0].InventoryText;
        void FindTestButton(Control parent)
        {
            foreach(Control child in parent.Controls)
                if(child is Button button && button.Text=="Test notification") button.PerformClick(); else FindTestButton(child);
        }
        FindTestButton(dashboard);
        Check(notificationTests==1 && dashboard.Cards[0].InventoryText==inventoryBeforeTest,"Safe notification test raises only a notification request and preserves inventory UI");
        using (var dialog = new InventoryDialog(snapshot.PaperInventories[0]))
        {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000,-20000);
            dialog.Show(); Application.DoEvents();
            IEnumerable<Control> Children(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c)));
            var controls = Children(dialog).ToArray();
            Check(!controls.OfType<ComboBox>().Any(), "No operation selector");
            Check(controls.OfType<NumericUpDown>().Count()==2 && controls.OfType<Button>().Count()==1, "Exactly two fields and one button");
            Check(controls.OfType<Label>().Any(l=>l.Text=="Sheets Loaded") && controls.OfType<Label>().Any(l=>l.Text=="Low Paper Alert At"), "Requested field labels");
            var autoChoice=controls.OfType<CheckBox>().Single();
            Check(!autoChoice.Checked && autoChoice.Text=="Automatically estimate paper used from print jobs","Optional checkbox defaults off");
            autoChoice.Checked=true;
            Check(dialog.Adjustment.AutomaticPaperEstimateRequested==true,"Checkbox saved with settings");
            var numbers = controls.OfType<NumericUpDown>().ToArray(); numbers[0].Value=10; numbers[1].Value=5;
            Check(dialog.Adjustment.Action==InventoryAction.SetCountedTotal && dialog.Adjustment.Sheets==10 && dialog.Adjustment.LowPaperThreshold==5, "Save replaces amount and threshold");
            Check(numbers.All(n=>n.Minimum==0 && n.Maximum==100000), "Nonnegative bounded fields");
            Check(controls.OfType<Button>().Single().Text=="Save Paper Settings", "Requested save label");
            using var image = new Bitmap(dialog.ClientSize.Width, dialog.ClientSize.Height);
            dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output,"manage-paper.png"));
            dialog.Close();
        }
        var jobTabs = dashboard.Controls.OfType<TabControl>().Single();
        Check(jobTabs.TabPages.Count==3 && jobTabs.TabPages[1].Text=="CURRENT PRINTING" && jobTabs.TabPages[2].Text=="PRINT HISTORY", "Separate organized job sections");
        var observed = new PrintHistoryEntry("fixture","Canon MG3000 series",1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,2,6,2,"DUPLEX_LONG_EDGE",16,"Printing — page progress unavailable.",true,false);
        dashboard.UpdateJobs([observed, observed with { Key="epson",Printer="EPSON L3210 Series",Active=false,Status="Unknown outcome" }],null);
        jobTabs.SelectedIndex=1; Application.DoEvents();
        var activeGrid=DescendantsForJobs(jobTabs.TabPages[1]).OfType<DataGridView>().Single();
        Check(activeGrid.Rows.Count==1 && activeGrid.Rows[0].Cells[3].Value?.ToString()=="2", "Current table reports observed Windows pages");
        Check(activeGrid.Rows[0].Cells[5].Value?.ToString()=="Unknown", "Current pages never become physical sheets");
        var currentSummary=DescendantsForJobs(jobTabs.TabPages[1]).OfType<Label>().Single(l=>l.Text.Contains("Sheets Loaded:"));
        Check(currentSummary.Text.Contains("Sheets Loaded: 10") && currentSummary.Text.Contains("Total Estimated Sheets Accounted For: 8") && currentSummary.Text.Contains("EPSON L3210"),"All printers show separate persisted inventory summaries");
        Check(currentSummary.Text.Contains("LOW PAPER") && currentSummary.Text.Contains("PAPER EMPTY") && currentSummary.Text.Contains("Needs Checking"),"Summary threshold and confidence states match inventory");
        Render("current-printing");
        jobTabs.SelectedIndex=2; Application.DoEvents();
        var historyGrid=DescendantsForJobs(jobTabs.TabPages[2]).OfType<DataGridView>().Single();
        Check(historyGrid.Rows.Count==2,"History includes both printers");
        var printerFilter=DescendantsForJobs(jobTabs.TabPages[2]).OfType<ComboBox>().Single();printerFilter.SelectedIndex=2;
        Check(historyGrid.Rows.Count==1 && historyGrid.Rows[0].Cells[1].Value?.ToString()=="EPSON L3210 Series", "History filter isolates Epson");
        var historySummary=DescendantsForJobs(jobTabs.TabPages[2]).OfType<Label>().Single(l=>l.Text.Contains("Sheets Loaded:"));
        Check(historySummary.Text.Contains("Sheets Loaded: 5") && !historySummary.Text.Contains("Canon"),"Selected history printer has its own inventory summary");
        Check(historyGrid.Columns.Cast<DataGridViewColumn>().Any(c=>c.HeaderText=="Copies requested") && historyGrid.Columns.Cast<DataGridViewColumn>().Any(c=>c.HeaderText=="N-up handling"),"Settings are visible in job table, not dashboard cards");
        Render("print-history");
        jobTabs.SelectedIndex=0; Application.DoEvents();
        IEnumerable<Control> DescendantsForJobs(Control parent) => parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(DescendantsForJobs(c)));
        Render("dashboard-normal");
        IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
        var diagnosticsButton = Descendants(dashboard).OfType<Button>().Single(b => b.Text == "Diagnostics");
        diagnosticsButton.PerformClick(); Application.DoEvents();
        var diagnostics = host.OwnedForms.Single();
        var diagnosticsText = diagnostics.Controls.OfType<TextBox>().Single();
        Check(diagnosticsText.Text.Contains("Microsoft Print to PDF") && diagnosticsText.Text.Contains("Canon diagnostic fixture"), "Diagnostics retains every discovered queue and details");
        diagnosticsButton.PerformClick();
        Check(host.OwnedForms.Length == 1, "Diagnostics button reuses its window");
        using (var bitmap = new Bitmap(diagnostics.ClientSize.Width, diagnostics.ClientSize.Height))
        { diagnostics.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "dashboard-diagnostics.png")); }
        host.ClientSize = new Size(884, 651); Render("dashboard-minimum");

        Check(dashboard.Cards.All(card => Descendants(card).Where(c => c is Label or Button).All(label =>
            { var position = card.PointToClient(label.PointToScreen(Point.Empty)); return position.Y >= 0 && position.Y + label.Height <= card.ClientSize.Height && position.X + label.Width <= card.ClientSize.Width; })), "All status labels fit at minimum size");
        Check(dashboard.Cards[0].Right < dashboard.Cards[1].Left && dashboard.Cards.All(c => c.Width > 300), "Cards remain side by side at minimum size");
        host.ClientSize = new Size(1280, 820); Render("dashboard-wide");
        Check(Math.Abs(dashboard.Cards[0].Width-dashboard.Cards[1].Width) <= 1, "Cards share available width");
        dashboard.UpdateSnapshot(new(DateTimeOffset.UtcNow, [], null));
        Check(dashboard.Cards.All(c => c.AvailabilityText == "Not found in Windows" && c.PhysicalText == "UNKNOWN" && c.ConnectionText == "Unknown"), "Missing printers clear prior connection states");
        Check(diagnosticsText.Text.Contains("0 installed queues") && !diagnosticsText.Text.Contains("Canon diagnostic fixture"), "Open diagnostics refreshes without stale readings");
        diagnostics.Close();
        Render("dashboard-missing");
        dashboard.UpdateSnapshot(new(DateTimeOffset.UtcNow, [], "Windows spooler query failed."));
        Check(dashboard.Cards.All(c => c.AvailabilityText == "Discovery unavailable"), "Discovery failure differs from missing printers");
        dashboard.SetServerStatus(false, "", "Server stopped: test configuration error.");
        Render("dashboard-discovery-error");
        using (var adjustment = new InventoryDialog(snapshot.PaperInventories[0]))
        {
            adjustment.StartPosition = FormStartPosition.Manual; adjustment.Location = new Point(-20000,-20000); adjustment.Show(); Application.DoEvents();
            using var bitmap = new Bitmap(adjustment.Width,adjustment.Height);
            adjustment.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(output,"inventory-adjustment.png"));
            Check(adjustment.Adjustment.Action == InventoryAction.SetCountedTotal && adjustment.Adjustment.Sheets == 2 && adjustment.Adjustment.LowPaperThreshold == 3, "Adjustment defaults to counted total and current threshold");
            Check(Descendants(adjustment).Where(c=>c is Button or NumericUpDown or ComboBox).All(c=> adjustment.PointToClient(c.PointToScreen(Point.Empty)).Y+c.Height <= adjustment.ClientSize.Height), "Operator controls fit dialog");
            adjustment.Close();
        }
        Console.WriteLine($"PASS: {checks} UI checks; offscreen previews saved under diagnostics. No live UI interaction or DPI validation performed.");
    }
}
