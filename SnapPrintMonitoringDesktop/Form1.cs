namespace SnapPrintMonitoringDesktop
{
    public partial class Form1 : Form
    {
        private readonly CancellationTokenSource monitoringCancellation = new();
        private bool monitoringStopped;
        private IReadOnlyList<PrinterReading> latest = Array.Empty<PrinterReading>();
        private readonly TextBox printerStatusDisplay = new()
        {
            Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Text = "Automatic printer monitoring is starting..."
        };
        private string localReport = "Waiting for Windows printer readings.";
        private string publishReport = "Waiting to publish.";

        public Form1()
        {
            InitializeComponent();

        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                await Task.WhenAll(PollAsync(monitoringCancellation.Token), PublishAsync(monitoringCancellation.Token));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Monitor stopped: {ex}"); }
            finally { monitoringStopped = true; monitoringCancellation.Dispose(); }
        }

        private async Task PollAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { latest = await Task.Run(WindowsPrinterMonitor.Read, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    latest = WindowsPrinterMonitor.QueueNames.Select(name =>
                        new PrinterReading(name, "UNKNOWN", $"Windows query failed: {ex.Message}")).ToArray();
                }
                if (token.IsCancellationRequested) return;
                localReport = $"Local check: {DateTime.Now:T}" + Environment.NewLine +
                    string.Join(Environment.NewLine, latest.Select(r => $"{r.Queue}: {r.State}\r\n{r.Detail}"));
                RefreshReport();
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
        }

        private async Task PublishAsync(CancellationToken token)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var publisher = new PrinterPublisher(client);
            while (!token.IsCancellationRequested)
            {
                await publisher.PublishAsync(latest, token);
                publishReport = publisher.LastResult;
                if (token.IsCancellationRequested) return;
                RefreshReport();
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
        }

        private void RefreshReport()
        {
            if (!IsDisposed && !monitoringCancellation.IsCancellationRequested)
                printerStatusDisplay.Text = localReport + Environment.NewLine + publishReport;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!monitoringStopped) monitoringCancellation.Cancel();
            base.OnFormClosed(e);
        }
    }
}

