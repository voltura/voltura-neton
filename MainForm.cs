namespace Voltura.NetOn;

internal sealed class MainForm : Form
{
    private readonly IAdapterService service;
    private readonly AppSettings preferences;
    private readonly InternetSwitch internetSwitch;
    private List<Adapter> snapshot = [];
    private readonly bool desktopFeatures;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Speech speech = new();
    private readonly ComboBox adapters = new() { Name = "AdapterSelector", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, AccessibleName = "Network adapter" };
    private readonly Label status = new() { AutoSize = true, Text = "Reading adapters…", Font = new Font("Segoe UI", 20, FontStyle.Bold), Margin = new Padding(0, 18, 0, 8) };
    private readonly Label detail = new() { AutoSize = true, MaximumSize = new Size(490, 0), Margin = new Padding(0, 0, 0, 12) };
    private readonly Label error = new() { Name = "ErrorMessage", AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.Firebrick };
    private readonly Button turnOn = new() { Name = "TurnOn", Text = "Turn internet ON", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
    private readonly Button turnOff = new() { Name = "TurnOff", Text = "Turn internet OFF", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
    private readonly Button refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem trayToggle = new("Toggle internet access");
    private readonly ToolStripMenuItem copyIp = new("Copy external IP");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly Icon onIcon, offIcon, unknownIcon;
    private Icon? customOn, customOff;
    private bool refreshing, changing, exiting, rebinding, showRequested, hasSnapshot;
    private bool? announcedState;
    private string externalIp = "";
    private Adapter? Selected => adapters.SelectedItem as Adapter;

    public MainForm(IAdapterService? service = null, AppSettings? settings = null, bool desktopFeatures = true)
    {
        this.service = service ?? new NetworkAdapters();
        preferences = settings ?? AppSettings.Load();
        this.desktopFeatures = desktopFeatures;
        internetSwitch = new InternetSwitch(this.service, preferences, () => { if (desktopFeatures) preferences.Save(); });
        onIcon = StatusIcons.Create(true, preferences);
        offIcon = StatusIcons.Create(false, preferences);
        unknownIcon = StatusIcons.Create(null, preferences);
        SuspendLayout();
        Text = "Voltura NetOn";
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(570, 430);
        MinimumSize = new Size(590, 470);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowOnly;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(246, 248, 251);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(24), ColumnCount = 1, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 8; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "ADAPTER DETAILS — ON / OFF CONTROLS ALL ADAPTERS", AutoSize = true, ForeColor = Color.DimGray });
        layout.Controls.Add(adapters);
        layout.Controls.Add(status);
        layout.Controls.Add(detail);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([turnOn, turnOff]);
        layout.Controls.Add(buttons);
        layout.Controls.Add(new Label { Text = "OFF disables all network adapters, including Ethernet, Wi-Fi and Bluetooth. ON restores the adapters enabled before OFF. Left-click the tray icon to toggle internet access.", AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 16, 0, 12) });
        layout.Controls.Add(error);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var hide = new Button { Text = "Hide to tray", AutoSize = true };
        var exit = new Button { Name = "Exit", Text = "Exit NetOn", AutoSize = true };
        footer.Controls.AddRange([refresh, hide, exit]);
        layout.Controls.Add(footer);
        Controls.Add(layout);

        var menu = new ContextMenuStrip();
        menu.Items.Add("About NetOn", null, (_, _) =>
        {
            Say("NetOn by Voltura AB");
            MessageBox.Show(this, "NetOn by Voltura AB\nVersion 1.0.2 • .NET 10\nhttps://github.com/voltura/voltura-neton\n\nFree for all.", "About NetOn", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        menu.Items.Add("Network adapters…", null, (_, _) => ShowWindow());
        menu.Items.Add(trayToggle);
        var settingsMenu = new ToolStripMenuItem("Settings");
        var startup = new ToolStripMenuItem("Start with Windows") { Checked = desktopFeatures && AppSettings.StartWithWindows };
        startup.Click += (_, _) =>
        {
            try { AppSettings.StartWithWindows = !startup.Checked; startup.Checked = AppSettings.StartWithWindows; Say(startup.Checked ? "NetOn set to start with Windows" : "NetOn will not start with Windows"); }
            catch (Exception ex) { Report(ex); }
        };
        settingsMenu.DropDownItems.Add(startup);
        var colors = new ToolStripMenuItem("Icon colors");
        colors.DropDownItems.Add("ON foreground", null, (_, _) => ChooseColor(true, true));
        colors.DropDownItems.Add("ON background", null, (_, _) => ChooseColor(true, false));
        colors.DropDownItems.Add("OFF foreground", null, (_, _) => ChooseColor(false, true));
        colors.DropDownItems.Add("OFF background", null, (_, _) => ChooseColor(false, false));
        colors.DropDownItems.Add("Reset colors", null, (_, _) =>
        {
            var defaults = new AppSettings();
            preferences.OnBackground = defaults.OnBackground; preferences.OnForeground = defaults.OnForeground;
            preferences.OffBackground = defaults.OffBackground; preferences.OffForeground = defaults.OffForeground;
            SavePreferences(); RebuildIcons(); Say("Colors reset");
        });
        settingsMenu.DropDownItems.Add(colors);
        var mute = new ToolStripMenuItem("Mute all sounds") { Checked = preferences.MuteAllSounds };
        mute.Click += (_, _) => { preferences.MuteAllSounds = !preferences.MuteAllSounds; mute.Checked = preferences.MuteAllSounds; SavePreferences(); };
        settingsMenu.DropDownItems.Add(mute);
        menu.Items.Add(settingsMenu);
        menu.Items.Add(copyIp);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit NetOn", null, (_, _) => ExitApp());
        tray = new NotifyIcon { Icon = unknownIcon, Text = "NetOn — reading adapters", ContextMenuStrip = menu, Visible = desktopFeatures };
        tray.MouseClick += async (_, e) => { if (e.Button == MouseButtons.Left) await ToggleAsync(); };
        trayToggle.Click += async (_, _) => await ToggleAsync();
        copyIp.Click += async (_, _) => await CopyExternalIpAsync();
        turnOn.Click += async (_, _) => await ChangeAsync(true);
        turnOff.Click += async (_, _) => await ChangeAsync(false);
        refresh.Click += async (_, _) => await RefreshAsync();
        hide.Click += (_, _) => Hide();
        exit.Click += (_, _) => ExitApp();
        adapters.SelectedIndexChanged += (_, _) =>
        {
            if (rebinding) return;
            preferences.AdapterId = Selected?.Id;
            SavePreferences();
            error.Text = "";
            Render();
        };
        timer.Tick += async (_, _) => await RefreshAsync();
        FormClosing += (_, e) =>
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        Render();
        ResumeLayout(true);
    }

    // A faithful tray startup; the optional adapter window opens from the menu or a second launch.
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(value && showRequested);

    internal void Start()
    {
        _ = RefreshAsync();
        timer.Start();
        if (desktopFeatures) _ = ReadExternalIpAsync();
    }

    public void RequestShow()
    {
        if (exiting || IsDisposed) return;
        try { BeginInvoke(() => { if (!exiting) ShowWindow(); }); }
        catch (InvalidOperationException) { }
    }

    private void ShowWindow() { showRequested = true; Show(); WindowState = FormWindowState.Normal; Activate(); }

    internal async Task RefreshAsync()
    {
        if (refreshing || changing || exiting || adapters.DroppedDown) return;
        refreshing = true;
        try
        {
            var found = await service.ReadAsync(lifetime.Token);
            if (exiting || IsDisposed || changing) return;
            // The user can select an adapter while a background read is in progress.
            if (!adapters.DroppedDown) ApplySnapshot(found);
            error.Text = "";
            Render();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!exiting) { hasSnapshot = false; Report(ex); Render(); } }
        finally { refreshing = false; }
    }

    private void ApplySnapshot(List<Adapter> found)
    {
        snapshot = found;
        Guid? desired = Selected?.Id ?? preferences.AdapterId;
        if (desired is null) desired = found.FirstOrDefault(a => a.Status == "Connected")?.Id ?? found.FirstOrDefault()?.Id;
        // Don't rebuild the dropdown every timer tick; that steals focus and closes it.
        bool changed = !adapters.Items.Cast<Adapter>().SequenceEqual(found);
        if (changed)
        {
            rebinding = true;
            adapters.BeginUpdate();
            try
            {
                adapters.Items.Clear();
                adapters.Items.AddRange(found.Cast<object>().ToArray());
                adapters.SelectedItem = found.FirstOrDefault(a => a.Id == desired);
            }
            finally { adapters.EndUpdate(); rebinding = false; }
        }
        if (preferences.AdapterId is null && Selected is { } selected) { preferences.AdapterId = selected.Id; SavePreferences(); }
        hasSnapshot = true;
    }

    private async Task ToggleAsync()
    {
        if (!hasSnapshot || snapshot.Count == 0) { ShowWindow(); return; }
        await ChangeAsync(!snapshot.Any(a => a.Enabled));
    }

    internal async Task ChangeAsync(bool enable)
    {
        if (changing || exiting || !hasSnapshot || snapshot.Count == 0) return;
        changing = true;
        Render();
        status.Text = enable ? "Turning on…" : "Turning off…";
        tray.Icon = Icon = unknownIcon;
        tray.Text = enable ? "NetOn — turning internet ON" : "NetOn — turning internet OFF";
        error.Text = "";
        try
        {
            var found = await internetSwitch.SetEnabledAsync(enable, lifetime.Token);
            if (exiting) return;
            ApplySnapshot(found);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!exiting)
            {
                Report(ex);
                try { ApplySnapshot(await service.ReadAsync(lifetime.Token)); }
                catch { hasSnapshot = false; }
            }
        }
        finally { changing = false; if (!exiting && !IsDisposed) Render(); }
    }

    private void Render()
    {
        adapters.Enabled = !changing;
        refresh.Enabled = !changing;
        bool anyEnabled = snapshot.Any(a => a.Enabled);
        bool known = hasSnapshot && snapshot.Count > 0;
        turnOn.Enabled = !changing && known && (!anyEnabled || preferences.OffRequested);
        turnOff.Enabled = !changing && known && anyEnabled;
        trayToggle.Enabled = !changing && known;
        if (changing) return;
        bool? enabled = known ? anyEnabled : null;
        Icon icon = enabled switch { true => customOn ?? onIcon, false => customOff ?? offIcon, null => unknownIcon };
        tray.Icon = Icon = icon;
        if (!known)
        {
            status.Text = hasSnapshot ? "Select an adapter" : "Status unavailable";
            status.ForeColor = Color.DimGray;
            detail.Text = hasSnapshot ? "Choose an adapter above. If none are listed, connect one and refresh." : "Refresh to read the adapter status.";
            tray.Text = "NetOn — status unavailable";
            return;
        }
        status.Text = anyEnabled ? "Internet ON" : "Internet OFF";
        status.ForeColor = anyEnabled ? Color.Green : Color.Firebrick;
        detail.Text = Selected is { } adapter ? $"{adapter.Description}\n{adapter.Name}: {adapter.Status}\nEnabled adapters: {snapshot.Count(a => a.Enabled)} of {snapshot.Count}" : $"Enabled adapters: {snapshot.Count(a => a.Enabled)} of {snapshot.Count}";
        trayToggle.Text = $"Turn internet {(anyEnabled ? "OFF" : "ON")}";
        string tip = $"NetOn: Internet {(anyEnabled ? "ON" : "OFF")}";
        if (anyEnabled && externalIp.Length > 0) tip += "\n" + externalIp;
        tray.Text = tip.Length > 63 ? tip[..63] : tip;
        if (announcedState != anyEnabled)
        {
            Say(anyEnabled ? "Internet enabled" : "Internet disabled");
            announcedState = anyEnabled;
        }
    }

    private void Say(string text) { if (desktopFeatures && !preferences.MuteAllSounds && !exiting) speech.Say(text); }
    private void Report(Exception ex) { if (exiting || IsDisposed) return; error.Text = ex.Message; }
    private void SavePreferences() { if (!desktopFeatures) return; try { preferences.Save(); } catch (Exception ex) { Report(ex); } }

    private void ChooseColor(bool enabled, bool foreground)
    {
        Say(foreground ? "Please select foreground color" : "Please select background color");
        int argb = enabled ? (foreground ? preferences.OnForeground : preferences.OnBackground) : (foreground ? preferences.OffForeground : preferences.OffBackground);
        using var dialog = new ColorDialog { Color = Color.FromArgb(argb), FullOpen = false, SolidColorOnly = true };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        if (enabled) { if (foreground) preferences.OnForeground = dialog.Color.ToArgb(); else preferences.OnBackground = dialog.Color.ToArgb(); }
        else { if (foreground) preferences.OffForeground = dialog.Color.ToArgb(); else preferences.OffBackground = dialog.Color.ToArgb(); }
        SavePreferences(); RebuildIcons();
    }

    private void RebuildIcons()
    {
        Icon? previousOn = customOn, previousOff = customOff;
        customOn = StatusIcons.Create(true, preferences); customOff = StatusIcons.Create(false, preferences);
        Render(); previousOn?.Dispose(); previousOff?.Dispose();
    }

    private async Task ReadExternalIpAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            string value = (await client.GetStringAsync("https://api.ipify.org", lifetime.Token)).Trim();
            if (System.Net.IPAddress.TryParse(value, out _) && !exiting) { externalIp = value; Render(); }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { /* The app must also work offline. */ }
    }

    private async Task CopyExternalIpAsync()
    {
        copyIp.Enabled = false;
        try
        {
            await ReadExternalIpAsync();
            if (exiting) return;
            if (externalIp.Length == 0) error.Text = "External IP is unavailable while offline.";
            else Clipboard.SetText(externalIp);
        }
        catch (Exception ex) { Report(ex); }
        finally { if (!exiting) copyIp.Enabled = true; }
    }

    internal void ExitApp()
    {
        if (exiting) return;
        exiting = true;
        timer.Stop();
        lifetime.Cancel();
        tray.Visible = false;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            exiting = true; lifetime.Cancel(); timer.Dispose();
            tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
            speech.Dispose(); onIcon.Dispose(); offIcon.Dispose(); unknownIcon.Dispose(); customOn?.Dispose(); customOff?.Dispose();
        }
        base.Dispose(disposing);
    }
}
