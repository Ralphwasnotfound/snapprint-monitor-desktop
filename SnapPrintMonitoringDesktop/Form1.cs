namespace SnapPrintMonitoringDesktop;

public partial class Form1 : Form
{
    private readonly CancellationTokenSource monitoringCancellation = new();
    private readonly SnapshotStore store = new();
    private readonly LocalMonitoringServer server = new();
    private Task? monitoringTask;
    private PaperInventoryService? inventory;
    private PrintHistoryService? history;
    private readonly WindowsPaperNotifier notifier;
    private Task? adjustmentTask;
    private bool closing;
    private bool cleanupComplete;
    private string serverReport = "Local server starting...";
    public Form1()
    {
        InitializeComponent();
        notifier = new WindowsPaperNotifier(() => { if (!closing) { Show(); WindowState = FormWindowState.Normal; Activate(); } });
        Disposed += (_,_) => notifier.Dispose();
        notifier.StatusChanged += dashboard.SetNotificationStatus;
        dashboard.SetNotificationStatus(notifier.Status);
        dashboard.NotificationTestRequested += () => notifier.Enqueue(PaperNotification.Test);
        dashboard.InventoryAdjustmentRequested += (name, adjustment) =>
        {
            if (adjustmentTask is { IsCompleted: false }) { MessageBox.Show(this, "An inventory save is still running. Wait for it to finish, then reopen the adjustment.", "Paper inventory"); return; }
            adjustmentTask = AdjustInventoryAsync(name, adjustment);
        };
    }
    private async Task AdjustInventoryAsync(string name, InventoryAdjustment adjustment)
    {
        if (closing) return;
        if (inventory is null) { MessageBox.Show(this, "Inventory storage is still starting. Wait, then reopen the adjustment.", "Paper inventory"); return; }
        try
        {
            var notification = await Task.Run(() => inventory.Adjust(name, adjustment, DateTimeOffset.UtcNow));
            if (!closing)
            {
                notifier.UpdateInventory(inventory.Current);
                if (notification is not null) notifier.Enqueue(notification);
            }
        }
        catch (InvalidOperationException ex) { if (!closing) MessageBox.Show(this, ex.Message, "Paper inventory", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        if (!closing) { store.Update(inventory.Attach(store.Current)); RefreshReport(); }
    }
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        inventory = await Task.Run(() => new PaperInventoryService(PaperInventoryService.DefaultPath));
        history = await Task.Run(() => new PrintHistoryService(PrintHistoryService.DefaultPath));
        if (closing) { inventory.Dispose(); history.Dispose(); return; }
        store.Update(inventory.Attach(store.Current));
        monitoringTask = PollAsync(monitoringCancellation.Token);
        try
        {
            var options = MonitorOptions.FromEnvironment();
            await server.StartAsync(options, store, monitoringCancellation.Token);
            if (closing) { await server.DisposeAsync(); return; }
            serverReport = $"Server running: {options.Url}\r\nAuthentication required. Firebase publishing disabled.";
            dashboard.SetServerStatus(true, options.Url, serverReport);
        }
        catch (OperationCanceledException) when (monitoringCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Only known configuration messages are safe to display; other exception text may contain secrets.
            serverReport = ex is MonitorConfigurationException ? $"Server stopped: {ex.Message}" :
                "Server stopped: check address, port conflict, token and certificate configuration. " + ex.GetType().Name;
            dashboard.SetServerStatus(false, "", serverReport);
        }
        RefreshReport();
    }
    private async Task PollAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var snapshot = await Task.Run(() =>
                {
                    var result = new PrinterSnapshot(DateTimeOffset.UtcNow, WindowsPrinterMonitor.Read(), null);
                    var notices = inventory!.Observe(result);
                    history!.Observe(inventory.Attach(result));
                    return (Snapshot: result, Notifications: notices);
                }, token);
                store.Update(inventory!.Attach(snapshot.Snapshot));
                if (!closing)
                {
                    notifier.UpdateInventory(inventory.Current);
                    foreach (var notice in snapshot.Notifications) notifier.Enqueue(notice);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                // Do not keep stale READY/PRINTING states after a failed discovery.
                var failed = new PrinterSnapshot(DateTimeOffset.UtcNow, Array.Empty<PrinterReading>(),
                    $"Windows discovery failed ({ex.GetType().Name}). Check Print Spooler and Windows WMI services.");
                await Task.Run(() => { inventory!.Observe(failed); history!.Observe(failed); });
                store.Update(inventory!.Attach(failed));
            }
            RefreshReport();
            try { await Task.Delay(TimeSpan.FromSeconds(3), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
    }
    private void RefreshReport()
    {
        if (IsDisposed || monitoringCancellation.IsCancellationRequested) return;
        dashboard.UpdateSnapshot(store.Current);
        if(history is not null) dashboard.UpdateJobs(history.Current,history.Error);
    }
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (cleanupComplete) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        monitoringCancellation.Cancel();
        // Stop the listener before exiting; a blocked native WMI call must not hold the UI open indefinitely.
        try
        {
            await server.DisposeAsync();
            if (adjustmentTask is not null) await adjustmentTask;
            if (monitoringTask is not null) await Task.WhenAny(monitoringTask, Task.Delay(5000));
        }
        finally { notifier.Dispose(); inventory?.Dispose(); history?.Dispose(); cleanupComplete = true; Close(); }
    }
}
