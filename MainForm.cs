using System.Text.Json;

namespace Voltura.NetOn;

internal sealed class MainForm : Form
{
    private readonly ComboBox adapters = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Network adapter" };
    private readonly Label status = new() { AutoSize = true, Text = "Reading adapters…", Font = new Font("Segoe UI", 20, FontStyle.Bold), Margin = new Padding(0, 20, 0, 8) };
    private readonly Label detail = new() { AutoSize = true, MaximumSize = new Size(490, 0), Margin = new Padding(0, 0, 0, 16) };
    private readonly Button turnOn = new() { Text = "Turn internet ON", AutoSize = true, Padding = new Padding(14, 8, 14, 8), Enabled = false };
    private readonly Button turnOff = new() { Text = "Turn internet OFF", AutoSize = true, Padding = new Padding(14, 8, 14, 8), Enabled = false };
    private readonly Button refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem trayToggle = new("Toggle selected adapter");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 5000 };
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltura", "NetOn", "settings.json");
    private Guid? selectedId;
    private bool busy;
    private bool exiting;
    private bool rebinding;
    private Adapter? Selected => adapters.SelectedItem as Adapter;

    public MainForm()
    {
        SuspendLayout();
        Text = "Voltura NetOn";
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 390);
        MinimumSize = new Size(580, 430);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.FromArgb(246, 248, 251);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 7; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "NETWORK ADAPTER", AutoSize = true, ForeColor = Color.DimGray });
        layout.Controls.Add(adapters);
        layout.Controls.Add(status);
        layout.Controls.Add(detail);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([turnOn, turnOff]);
        layout.Controls.Add(buttons);
        layout.Controls.Add(new Label { Text = "Switches the selected adapter. Other active adapters can still provide internet access. Turning it on does not guarantee an internet connection.", AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 16, 0, 12) });
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var hide = new Button { Text = "Hide to tray", AutoSize = true };
        var exit = new Button { Text = "Exit", AutoSize = true };
        footer.Controls.AddRange([refresh, hide, exit]);
        layout.Controls.Add(footer);
        Controls.Add(layout);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open NetOn", null, (_, _) => ShowWindow());
        menu.Items.Add(trayToggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Voltura NetOn", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();
        trayToggle.Click += async (_, _) => { if (Selected is { } adapter) await ChangeAsync(!adapter.Enabled); };
        turnOn.Click += async (_, _) => await ChangeAsync(true);
        turnOff.Click += async (_, _) => await ChangeAsync(false);
        refresh.Click += async (_, _) => await RefreshAsync();
        hide.Click += (_, _) => Hide();
        exit.Click += (_, _) => ExitApp();
        adapters.SelectedIndexChanged += (_, _) =>
        {
            if (rebinding) return;
            selectedId = Selected?.Id;
            SaveSelection();
            Render();
        };
        timer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { await RefreshAsync(); timer.Start(); };
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        try { if (File.Exists(settingsPath)) selectedId = JsonSerializer.Deserialize<Guid?>(File.ReadAllText(settingsPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* Use the active adapter if settings are unavailable. */ }
        ResumeLayout(true);
    }

    private void SaveSelection()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath, JsonSerializer.Serialize(selectedId)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { detail.Text = "The adapter selection could not be saved: " + ex.Message; }
    }

    private async Task RefreshAsync()
    {
        if (busy || exiting || adapters.DroppedDown) return;
        busy = true;
        Render();
        bool loaded = false;
        try { await LoadAdaptersAsync(); loaded = true; }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                rebinding = true;
                try { adapters.DataSource = null; } finally { rebinding = false; }
                status.Text = "Status unavailable";
                detail.Text = ex.Message;
                tray.Text = "Voltura NetOn — status unavailable";
            }
        }
        finally { busy = false; if (!IsDisposed) { adapters.Enabled = refresh.Enabled = true; if (loaded) Render(); } }
    }

    private async Task LoadAdaptersAsync()
    {
        var found = await NetworkAdapters.ReadAsync();
        if (exiting || IsDisposed) return;
        Guid? desired = selectedId ?? found.FirstOrDefault(a => a.Status == "Up")?.Id ?? found.FirstOrDefault()?.Id;
        rebinding = true;
        try { adapters.DataSource = found; adapters.SelectedItem = found.FirstOrDefault(a => a.Id == desired); }
        finally { rebinding = false; }
        selectedId = desired;
    }

    private async Task ChangeAsync(bool enable)
    {
        if (busy || Selected is not { } adapter) return;
        busy = true;
        Render();
        status.Text = enable ? "Turning on…" : "Turning off…";
        try
        {
            await NetworkAdapters.SetEnabledAsync(adapter.Id, enable);
            await LoadAdaptersAsync();
            if (Selected is not { } actual || actual.Enabled != enable)
                throw new InvalidOperationException("Windows has not confirmed the requested adapter state. Refresh and try again.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not change adapter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            try { await LoadAdaptersAsync(); } catch { adapters.DataSource = null; }
        }
        finally { busy = false; if (!IsDisposed) Render(); }
    }

    private void Render()
    {
        adapters.Enabled = refresh.Enabled = !busy;
        turnOn.Enabled = !busy && Selected is { Enabled: false };
        turnOff.Enabled = !busy && Selected is { Enabled: true };
        trayToggle.Enabled = !busy && Selected != null;
        if (busy) return;
        if (Selected is not { } adapter)
        {
            status.Text = "No adapter selected";
            detail.Text = adapters.Items.Count == 0 ? "No physical network adapters were found. Connect an adapter and refresh." : "Select an available adapter above.";
            tray.Text = "Voltura NetOn — no adapter selected";
            return;
        }
        status.Text = adapter.Enabled ? "Adapter ON" : "Adapter OFF";
        status.ForeColor = adapter.Enabled ? Color.FromArgb(22, 125, 77) : Color.FromArgb(154, 57, 45);
        detail.Text = $"{adapter.Description}\nConnection: {adapter.Status}";
        trayToggle.Text = $"Turn {adapter.Name} {(adapter.Enabled ? "OFF" : "ON")}";
        string tooltip = $"NetOn: {adapter.Name} — {(adapter.Enabled ? "ON" : "OFF")}";
        tray.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
        tray.Icon = adapter.Enabled ? SystemIcons.Information : SystemIcons.Warning;
    }

    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void ExitApp() { if (busy) return; exiting = true; Close(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { exiting = true; timer.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        base.Dispose(disposing);
    }
}
