using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Voltura.NetOn;

internal sealed class AboutForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public AboutForm()
    {
        SuspendLayout();
        Text = "About NetOn";
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(410, 260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Paper;
        ForeColor = AppTheme.Ink;
        Icon = StatusIcons.Create(true, new AppSettings());

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        var title = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var mark = new PictureBox { Image = StatusIcons.Draw(true, new AppSettings(), 128), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(44, 44), Margin = new Padding(0, 0, 10, 0) };
        mark.Disposed += (_, _) => mark.Image?.Dispose();
        title.Controls.Add(mark);
        title.Controls.Add(new Label { Text = "NetOn", AutoSize = true, Font = new Font("Segoe UI", 22, FontStyle.Bold), Margin = Padding.Empty });
        layout.Controls.Add(title);
        string version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "";
        layout.Controls.Add(new Label { Text = $"By Voltura AB · Version {version}", AutoSize = true, ForeColor = AppTheme.Muted, Margin = new Padding(0, 10, 0, 10) });
        var website = new LinkLabel { Text = "Visit the NetOn website", AutoSize = true, LinkColor = AppTheme.Positive, ActiveLinkColor = AppTheme.Positive, VisitedLinkColor = AppTheme.Positive, Margin = new Padding(0, 0, 0, 8) };
        website.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://voltura.github.io/voltura-neton/") { UseShellExecute = true });
        layout.Controls.Add(website);
        layout.Controls.Add(new Label { Text = "Free for all.", AutoSize = true, ForeColor = AppTheme.Muted });
        var close = new ModernButton { Name = "AboutClose", Text = "OK", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        layout.Controls.Add(close);
        AcceptButton = CancelButton = close;
        Controls.Add(layout);
        ResumeLayout(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = AppTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Icon?.Dispose();
        base.Dispose(disposing);
    }
}
