using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Voltura.NetOn;

internal static class AppLaunch
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    internal static ProcessStartInfo Elevated(string processPath, string assemblyPath)
    {
        var start = new ProcessStartInfo(processPath) { UseShellExecute = true, Verb = "runas" };
        if (IsFrameworkHost(processPath)) start.ArgumentList.Add(assemblyPath);
        return start;
    }

    internal static string StartupCommand(string processPath, string assemblyPath) =>
        $"\"{processPath}\"" + (IsFrameworkHost(processPath) ? $" \"{assemblyPath}\"" : "");

    private static bool IsFrameworkHost(string processPath) => Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    internal static int Elevate(Func<ProcessStartInfo, Process?>? start = null, Action<Exception>? report = null)
    {
        try
        {
            using var process = (start ?? Process.Start)(Elevated(Environment.ProcessPath!, typeof(AppLaunch).Assembly.Location));
            return 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return 0; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            (report ?? ShowFailure)(ex);
            return 1;
        }
    }

    private static void ShowFailure(Exception error)
    {
        AppTheme.Set(AppSettings.Load().Theme);
        using var font = new Font("Segoe UI", 10);
        using var form = new Form
        {
            Text = "NetOn could not start", Font = font,
            AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(480, 260), MinimumSize = new Size(460, 240),
            StartPosition = FormStartPosition.CenterScreen, ShowIcon = false,
            MaximizeBox = false, MinimizeBox = false, BackColor = AppTheme.Paper, ForeColor = AppTheme.Ink
        };
        form.HandleCreated += (_, _) => { int dark = AppTheme.Dark ? 1 : 0; DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(24) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty, BackColor = AppTheme.Paper };
        body.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(408, 0), BackColor = AppTheme.Paper, ForeColor = AppTheme.Ink, Text = "Windows could not grant NetOn permission to switch network connections.\n\n" + error.Message });
        var close = new ModernButton { Text = "Close", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, BackColor = AppTheme.Paper, Margin = new Padding(0, 12, 0, 0) };
        root.Controls.Add(body, 0, 0); root.Controls.Add(close, 0, 1);
        form.Controls.Add(root); form.AcceptButton = form.CancelButton = close;
        form.ShowDialog();
    }
}
