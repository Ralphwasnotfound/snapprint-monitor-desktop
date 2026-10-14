namespace SnapPrintMonitoringDesktop
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            var layout = new TableLayoutPanel();
            var heading = new Label();
            var subtitle = new Label();
            SuspendLayout();
            layout.SuspendLayout();

            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            heading.Text = "SNAP AND PRINT - PRINTER MONITOR";
            heading.AutoSize = true;
            heading.Dock = DockStyle.Fill;
            heading.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            heading.Margin = new Padding(0, 0, 0, 4);

            subtitle.Text = "Automatic Printer Monitoring";
            subtitle.AutoSize = true;
            subtitle.Dock = DockStyle.Fill;
            subtitle.ForeColor = SystemColors.GrayText;
            subtitle.Margin = new Padding(0, 0, 0, 12);

            printerStatusDisplay.Margin = Padding.Empty;
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(subtitle, 0, 1);
            layout.Controls.Add(printerStatusDisplay, 0, 2);

            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(760, 360);
            MinimumSize = new Size(520, 300);
            Controls.Add(layout);
            Name = "Form1";
            Text = "Snap and Print - Printer Monitor";
            layout.ResumeLayout(false);
            layout.PerformLayout();
            ResumeLayout(false);
        }
    }
}
