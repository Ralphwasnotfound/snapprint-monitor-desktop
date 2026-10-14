namespace SnapPrintMonitoringDesktop
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            if (args.Length == 1 && args[0] == "--test-notification") { WindowsPaperNotifier.RunStandaloneTest(); return; }
            if (args.Length == 1 && args[0] == "--release-smoke-check")
            {
                // Exercise packaged WinForms/WPF/server dependencies without inventory, API binding or printing.
                try
                {
                    using var host = new Form { ClientSize = new Size(1040, 700), ShowInTaskbar = false,
                        StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
                    host.Controls.Add(new PrinterDashboard()); host.Show(); Application.DoEvents();
                    using var image = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
                    host.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    _ = typeof(System.Printing.PrintQueue).Assembly.FullName;
                    _ = typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.FullName;
                    host.Close(); Environment.ExitCode = 0;
                }
                catch { Environment.ExitCode = 1; }
                return;
            }
            Application.Run(new Form1());
        }
    }
}
