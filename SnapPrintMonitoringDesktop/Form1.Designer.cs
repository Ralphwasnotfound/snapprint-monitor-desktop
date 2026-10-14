#nullable enable
namespace SnapPrintMonitoringDesktop;

partial class Form1
{
    private System.ComponentModel.IContainer? components = null;
    private PrinterDashboard dashboard = null!;
    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }
    private void InitializeComponent()
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(1040, 660);
        MinimumSize = new Size(900, 690);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(246, 248, 251);
        dashboard = new PrinterDashboard();
        Controls.Add(dashboard);
        Name = "Form1";
        Text = "Print and Snap · Printer Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        ResumeLayout(false);
    }
}
