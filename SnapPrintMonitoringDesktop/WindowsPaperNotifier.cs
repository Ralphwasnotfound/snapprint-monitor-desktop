using System.ComponentModel;

namespace SnapPrintMonitoringDesktop;

/// <summary>Supported WinForms fallback for this app's unregistered toast environment.</summary>
internal sealed class WindowsPaperNotifier : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly System.Windows.Forms.Timer spacing = new() { Interval = 15000 };
    private readonly Queue<PaperNotification> pending = new();
    private bool waiting;
    private bool testShowing;
    private bool disposed;
    internal string Status { get; private set; } = "Windows tray balloon notifications enabled; Windows notification settings may suppress display.";
    internal event Action<string>? StatusChanged;
    internal WindowsPaperNotifier(Action? openDashboard = null)
    {
        icon = new NotifyIcon { Text = "Print and Snap - paper estimates", Icon = SystemIcons.Application };
        icon.DoubleClick += (_,_) => openDashboard?.Invoke();
        icon.BalloonTipClicked += (_,_) => openDashboard?.Invoke();
        spacing.Tick += (_,_) => { spacing.Stop(); waiting = false; testShowing = false; ShowNext(); };
    }
    internal void Enqueue(PaperNotification notification)
    {
        if (disposed) return;
        if (notification.Kind == PaperNotificationKind.Test && (testShowing || pending.Any(n => n.Kind == PaperNotificationKind.Test))) return;
        pending.Enqueue(notification);
        ShowNext();
    }
    internal void UpdateInventory(IReadOnlyList<PaperInventory> inventories)
    {
        var refreshed = pending.Select(n => PaperAlertPolicy.RefreshPending(n, inventories)).OfType<PaperNotification>().ToArray();
        pending.Clear();
        foreach (var notification in refreshed) pending.Enqueue(notification);
    }
    private void ShowNext()
    {
        if (disposed || waiting || pending.Count == 0) return;
        var notification = pending.Dequeue();
        testShowing = notification.Kind == PaperNotificationKind.Test;
        try
        {
            icon.Visible = true;
            icon.ShowBalloonTip(10000, notification.Title, notification.Body, notification.Kind switch
                { PaperNotificationKind.Empty => ToolTipIcon.Error, PaperNotificationKind.Low => ToolTipIcon.Warning, _ => ToolTipIcon.Info });
            Status = "Notification requested via Windows tray balloon. Windows controls visibility; delivery is not confirmed.";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            Status = "Windows notification request failed (" + ex.GetType().Name + "). Dashboard warnings remain available; use Test notification to retry.";
        }
        // Serialize Canon/Epson requests so consecutive saves do not immediately replace each other's balloons.
        waiting = true;
        spacing.Start();
        StatusChanged?.Invoke(Status);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; pending.Clear(); spacing.Stop(); spacing.Dispose(); icon.Visible = false; icon.Dispose();
    }
    internal static void RunStandaloneTest()
    {
        // This path never creates the main form, inventory service, HTTP server, or printer monitor.
        using var context = new ApplicationContext();
        using var notifier = new WindowsPaperNotifier();
        using var lifetime = new System.Windows.Forms.Timer { Interval = 20000 };
        lifetime.Tick += (_,_) => { lifetime.Stop(); context.ExitThread(); };
        notifier.Enqueue(PaperNotification.Test); lifetime.Start();
        Application.Run(context);
    }
}
