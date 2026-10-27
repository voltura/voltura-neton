using System.Runtime.InteropServices;
namespace Voltura.NetOn;

internal sealed class MainForm : Form
{
    private readonly IAdapterService service;
    private readonly AppSettings preferences;
    private readonly InternetSwitch internetSwitch;
    private List<Adapter> snapshot = [];
    private readonly bool desktopFeatures;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Speech speech;
    private readonly Font menuFont = new("Segoe UI", 10);
    private readonly ThemedComboBox adapters = new() { Name = "AdapterSelector", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, AccessibleName = "Network adapter" };
    private readonly TableLayoutPanel connectionDetails = new() { Name = "ConnectionDetails", AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
    private readonly ModernButton detailsToggle = new() { Name = "DetailsToggle", Text = "Show connection details", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Quiet = true, Size = new Size(230, 36), MinimumSize = Size.Empty, Margin = new Padding(0, 6, 0, 6) };
    private readonly Func<CancellationToken, Task<string>> externalIpLookup;
    private readonly Label status = new() { AutoSize = true, Text = "Checking…", Font = new Font("Segoe UI", 23, FontStyle.Regular), Margin = new Padding(0, 17, 0, 0) };
    private readonly SwitchGlyph stateGlyph = new() { Name = "MainSwitch", Margin = new Padding(0, 0, 4, 0), Size = new Size(90, 75) };
    private readonly Label stateDescription = new() { AutoSize = true, MaximumSize = new Size(420, 0), Font = new Font("Segoe UI", 10), ForeColor = AppTheme.Muted, Margin = new Padding(0, 1, 0, 6) };
    private readonly Label detail = new() { AutoSize = true, MaximumSize = new Size(420, 0), Font = new Font("Segoe UI", 9), ForeColor = AppTheme.Muted, Margin = new Padding(0, 10, 0, 7) };
    private readonly Label error = new() { Name = "ErrorMessage", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = AppTheme.Red, Font = new Font("Segoe UI", 9) };
    private readonly ModernButton turnOn = new() { Name = "TurnOn", Text = "Turn on", Accent = AppTheme.Green };
    private readonly ModernButton turnOff = new() { Name = "TurnOff", Text = "Turn off", Accent = AppTheme.Red };
    private readonly ModernButton refresh = new() { Text = "Refresh", Quiet = true };
    private readonly Label externalIpLabel = new() { Name = "ExternalIp", AutoSize = true, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 7, 6, 0) };
    private readonly CopyButton copyIpButton = new() { Name = "CopyExternalIp", Margin = Padding.Empty, Enabled = false };
    private readonly ToolTip hints = new();
    private SettingsForm? settingsWindow;
    private bool readingIp;
    private DateTimeOffset nextIpRead;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem trayToggle = new("Toggle internet access");
    private readonly NetworkSchedule schedule = new();
    private readonly ToolStripMenuItem scheduleSummary = new("No scheduled change") { Enabled = false };
    private readonly ToolStripMenuItem cancelSchedule = new("Cancel scheduled change") { Enabled = false };
    private readonly ToolStripMenuItem copyIp = new("Copy external IP");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly Icon onIcon, offIcon, unknownIcon;
    private Icon? customOn, customOff;
    private bool refreshing, changing, exiting, rebinding, showRequested, hasSnapshot;
    private int operationVersion;
    private bool? announcedState;
    private string externalIp = "";
    private Adapter? Selected => adapters.SelectedItem as Adapter;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public MainForm(IAdapterService? service = null, AppSettings? settings = null, bool desktopFeatures = true, Func<CancellationToken, Task<string>>? externalIpLookup = null)
    {
        this.service = service ?? new NetworkAdapters();
        this.externalIpLookup = externalIpLookup ?? LookupExternalIpAsync;
        preferences = settings ?? AppSettings.Load();
        AppTheme.Set(preferences.Theme);
        speech = new Speech(() => preferences.VoiceId);
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
        ClientSize = new Size(820, preferences.ShowConnectionDetails ? 680 : 590);
        MinimumSize = new Size(820, 610);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppTheme.Paper;
        ForeColor = AppTheme.Ink;
        adapters.FlatStyle = FlatStyle.Flat;
        adapters.BackColor = AppTheme.Surface;
        adapters.ForeColor = AppTheme.Ink;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var mark = new PictureBox { Image = StatusIcons.Draw(true, preferences, 128), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(48, 48), Margin = new Padding(0, 0, 10, 0) };
        mark.Disposed += (_, _) => mark.Image?.Dispose();
        heading.Controls.Add(mark);
        heading.Controls.Add(new Label { Text = "NetOn", AutoSize = true, Font = new Font("Segoe UI", 25, FontStyle.Bold), Margin = new Padding(0, 0, 15, 0) });
        heading.Controls.Add(new Label { Text = "by Voltura", AutoSize = true, ForeColor = AppTheme.Muted, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 20, 0, 0) });
        root.Controls.Add(heading, 0, 0);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        body.Controls.Add(new ArtworkPanel(), 0, 0);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Margin = new Padding(0, 3, 0, 0) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 9; row++) content.RowStyles.Add(new RowStyle(row == 8 ? SizeType.Percent : SizeType.AutoSize, row == 8 ? 100 : 0));
        content.Controls.Add(new Label { Text = "YOUR INTERNET CONNECTION", ForeColor = AppTheme.Muted, Font = new Font("Segoe UI", 8, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 2, 0, 5) });
        var stateRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        stateRow.Controls.Add(stateGlyph);
        stateRow.Controls.Add(status);
        content.Controls.Add(stateRow);
        content.Controls.Add(stateDescription);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 6, 0, 18) };
        buttons.Controls.AddRange([turnOn, turnOff]);
        content.Controls.Add(buttons);
        var ipRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        ipRow.Controls.AddRange([externalIpLabel, copyIpButton]);
        content.Controls.Add(ipRow);
        content.Controls.Add(detailsToggle);
        connectionDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connectionDetails.Controls.Add(adapters);
        connectionDetails.Controls.Add(detail);
        connectionDetails.Visible = preferences.ShowConnectionDetails;
        detailsToggle.Text = preferences.ShowConnectionDetails ? "Hide connection details" : "Show connection details";
        detailsToggle.Click += (_, _) => SetConnectionDetails(!preferences.ShowConnectionDetails);
        content.Controls.Add(connectionDetails);
        hints.SetToolTip(copyIpButton, "Copy external IP address");
        content.Controls.Add(error);
        content.Controls.Add(new Label { Text = "Switches all network connections.", AutoSize = true, ForeColor = AppTheme.Muted, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 12, 0, 0) });
        body.Controls.Add(content, 1, 0);
        root.Controls.Add(body, 0, 1);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 16, 0, 0) };
        var hide = new ModernButton { Text = "Hide to tray", Quiet = true };
        var exit = new ModernButton { Name = "Exit", Text = "Exit NetOn", Quiet = true };
        var settingsButton = new ModernButton { Name = "Settings", Text = "Settings", Quiet = true };
        settingsButton.Click += (_, _) => ShowSettings();
        footer.Controls.AddRange([refresh, hide, settingsButton, exit]);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => ApplyTheme();
        menu.Items.Add("About NetOn", null, (_, _) =>
        {
            Say("NetOn by Voltura AB");
            using var about = new AboutForm();
            about.ShowDialog(this);
        });
        menu.Items.Add("Network adapters…", null, (_, _) => { SetConnectionDetails(true); ShowWindow(); });
        menu.Items.Add(trayToggle);
        var scheduleMenu = new ToolStripMenuItem("Schedule");
        scheduleMenu.DropDownItems.Add(scheduleSummary);
        scheduleMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (bool enable in new[] { true, false })
        {
            var action = new ToolStripMenuItem($"Turn internet {(enable ? "on" : "off")} after");
            foreach (var option in NetworkSchedule.Options)
                action.DropDownItems.Add(option.Label, null, (_, _) => ScheduleChange(enable, option.Delay));
            scheduleMenu.DropDownItems.Add(action);
        }
        cancelSchedule.Click += (_, _) => { schedule.Cancel(); Render(); };
        scheduleMenu.DropDownItems.Add(new ToolStripSeparator());
        scheduleMenu.DropDownItems.Add(cancelSchedule);
        scheduleMenu.DropDownItems.Add(new ToolStripSeparator());
        scheduleMenu.DropDownItems.Add(new ToolStripMenuItem("Runs while NetOn is open") { Enabled = false });
        menu.Items.Add(scheduleMenu);
        var settingsMenu = new ToolStripMenuItem("Settings");
        var startup = new ToolStripMenuItem("Start with Windows") { Tag = "startup", Checked = desktopFeatures && AppSettings.StartWithWindows };
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
        settingsMenu.DropDownItems.Insert(0, new ToolStripMenuItem("Open settings…", null, (_, _) => ShowSettings()));
        settingsMenu.DropDownItems.Add(colors);
        var mute = new ToolStripMenuItem("Mute all sounds") { Tag = "mute", Checked = preferences.MuteAllSounds };
        mute.Click += (_, _) => { preferences.MuteAllSounds = !preferences.MuteAllSounds; mute.Checked = preferences.MuteAllSounds; if (desktopFeatures) speech.SetMuted(preferences.MuteAllSounds); SavePreferences(); };
        settingsMenu.DropDownItems.Add(mute);
        var voices = new ToolStripMenuItem("Voice");
        var defaultVoice = new ToolStripMenuItem("Recorded voice - English") { Tag = "voice:" + RecordedVoice.Id, Checked = preferences.VoiceId is null or RecordedVoice.Id };
        voices.DropDownItems.Add(defaultVoice);
        defaultVoice.Click += (_, _) =>
        {
            preferences.VoiceId = null;
            foreach (ToolStripMenuItem item in voices.DropDownItems) item.Checked = item == defaultVoice;
            SavePreferences(); Say("NetOn by Voltura AB");
        };
        if (desktopFeatures)
        {
            foreach (var choice in Speech.ListVoices())
            {
                var item = new ToolStripMenuItem(choice.Name) { Tag = "voice:" + choice.Id, Checked = preferences.VoiceId == choice.Id };
                item.Click += (_, _) =>
                {
                    preferences.VoiceId = choice.Id;
                    foreach (ToolStripMenuItem option in voices.DropDownItems) option.Checked = option == item;
                    SavePreferences(); Say("NetOn by Voltura AB");
                };
                voices.DropDownItems.Add(item);
            }
        }
        settingsMenu.DropDownItems.Add(voices);
        var themeMenu = new ToolStripMenuItem("Appearance");
        foreach (string theme in new[] { "System", "Light", "Dark" })
        {
            var choice = new ToolStripMenuItem(theme == "System" ? "Follow Windows" : theme) { Tag = "theme:" + theme, Checked = preferences.Theme == theme };
            choice.Click += (_, _) =>
            {
                preferences.Theme = theme;
                foreach (ToolStripMenuItem option in themeMenu.DropDownItems) option.Checked = option == choice;
                SavePreferences(); ApplyTheme();
            };
            themeMenu.DropDownItems.Add(choice);
        }
        settingsMenu.DropDownItems.Add(themeMenu);
        menu.Items.Add(settingsMenu);
        menu.Items.Add(copyIp);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit NetOn", null, (_, _) => ExitApp());
        tray = new NotifyIcon { Icon = unknownIcon, Text = "NetOn — reading adapters", ContextMenuStrip = menu, Visible = desktopFeatures };
        tray.MouseClick += async (_, e) => { if (e.Button == MouseButtons.Left) await ToggleAsync(); };
        trayToggle.Click += async (_, _) => await ToggleAsync();
        copyIp.Click += async (_, _) => await CopyExternalIpAsync();
        copyIpButton.Click += async (_, _) => await CopyExternalIpAsync();
        stateGlyph.Click += async (_, _) => await ToggleAsync();
        turnOn.Click += async (_, _) => await ChangeAsync(true);
        turnOff.Click += async (_, _) => await ChangeAsync(false);
        refresh.Click += async (_, _) => { await RefreshAsync(); if (desktopFeatures) await ReadExternalIpAsync(); };
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
        timer.Tick += async (_, _) => { if (preferences.Theme == "System" && AppTheme.Dark != AppTheme.ResolveDark("System")) ApplyTheme(); await RunScheduledChangeAsync(); await RefreshAsync(); };
        FormClosing += (_, e) =>
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        Render();
        ApplyTheme();
        ResumeLayout(true);
    }

    // A faithful tray startup; the optional adapter window opens from the menu or a second launch.
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(value && showRequested);

    internal void Start()
    {
        _ = RefreshAsync();
        timer.Start();
        if (desktopFeatures) { speech.SetMuted(preferences.MuteAllSounds); _ = ReadExternalIpAsync(); }
    }

    public void RequestShow()
    {
        if (exiting || IsDisposed) return;
        try { BeginInvoke(() => { if (!exiting) ShowWindow(); }); }
        catch (InvalidOperationException) { }
    }
    public void RequestExit()
    {
        if (exiting || IsDisposed) return;
        try { BeginInvoke(ExitApp); } catch (InvalidOperationException) { }
    }

    internal void SetConnectionDetails(bool visible)
    {
        preferences.ShowConnectionDetails = visible;
        connectionDetails.Visible = visible;
        detailsToggle.Text = visible ? "Hide connection details" : "Show connection details";
        detailsToggle.AccessibleDescription = visible ? "Connection details expanded" : "Connection details collapsed";
        if (visible && ClientSize.Height < 680 * DeviceDpi / 96)
        {
            ClientSize = new Size(ClientSize.Width, 680 * DeviceDpi / 96);
            var screen = Screen.FromControl(this).WorkingArea;
            if (Bottom > screen.Bottom) Top = Math.Max(screen.Top, screen.Bottom - Height);
        }
        SavePreferences();
    }

    private void ShowWindow() { showRequested = true; Show(); WindowState = FormWindowState.Normal; Activate(); }

    internal async Task RefreshAsync()
    {
        if (refreshing || changing || exiting || adapters.DroppedDown) return;
        refreshing = true;
        int readVersion = operationVersion;
        try
        {
            var found = await service.ReadAsync(lifetime.Token);
            if (exiting || IsDisposed || changing || readVersion != operationVersion) return;
            // The user can select an adapter while a background read is in progress.
            if (!adapters.DroppedDown) ApplySnapshot(found);
            error.Text = "";
            Render();
            if (desktopFeatures && snapshot.Any(a => a.Enabled) && DateTimeOffset.UtcNow >= nextIpRead) _ = ReadExternalIpAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!exiting && readVersion == operationVersion) { hasSnapshot = false; Report(ex); Render(); } }
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
        operationVersion++;
        Render();
        status.Text = enable ? "Turning on…" : "Turning off…";
        tray.Icon = Icon = unknownIcon;
        stateGlyph.State = null;
        stateDescription.Text = enable ? "Enabling network adapters…" : "Disabling network adapters…";
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
        finally
        {
            changing = false;
            if (!exiting && !IsDisposed)
            {
                Render();
                if (desktopFeatures && snapshot.Any(a => a.Enabled)) _ = ReadExternalIpAsync();
            }
        }
    }

    internal void ScheduleChange(bool enable, TimeSpan delay, DateTimeOffset? now = null)
    {
        schedule.Set(enable, delay, now ?? DateTimeOffset.UtcNow);
        Render();
    }

    internal async Task RunScheduledChangeAsync(DateTimeOffset? now = null)
    {
        if (changing || exiting || !hasSnapshot || snapshot.Count == 0) return;
        if (schedule.TakeDue(now ?? DateTimeOffset.UtcNow) is not bool enable) return;
        bool alreadySet = enable ? snapshot.Any(a => a.Enabled) && !preferences.OffRequested : snapshot.All(a => !a.Enabled);
        if (alreadySet) { Render(); return; }
        await ChangeAsync(enable);
    }

    private void Render()
    {
        scheduleSummary.Text = schedule.Describe(DateTimeOffset.UtcNow);
        cancelSchedule.Enabled = schedule.Due is not null;
        stateGlyph.Enabled = !changing && hasSnapshot && snapshot.Count > 0;
        adapters.Enabled = !changing;
        refresh.Enabled = !changing;
        bool anyEnabled = snapshot.Any(a => a.Enabled);
        bool known = hasSnapshot && snapshot.Count > 0;
        if (known && !anyEnabled) externalIp = "";
        externalIpLabel.Text = "External IP: " + (!known ? "unavailable" : !anyEnabled ? "unavailable while off" : externalIp.Length > 0 ? externalIp : readingIp ? "checking…" : "unavailable");
        copyIpButton.Enabled = !changing && known && anyEnabled && externalIp.Length > 0;
        copyIp.Enabled = !changing && known && anyEnabled;
        turnOn.Enabled = !changing && known && (!anyEnabled || preferences.OffRequested);
        turnOff.Enabled = !changing && known && anyEnabled;
        trayToggle.Enabled = !changing && known;
        if (changing) return;
        bool? enabled = known ? anyEnabled : null;
        stateGlyph.State = enabled;
        Icon icon = enabled switch { true => customOn ?? onIcon, false => customOff ?? offIcon, null => unknownIcon };
        tray.Icon = Icon = icon;
        if (!known)
        {
            status.Text = "Checking…";
            stateDescription.Text = "Refresh to check your connection.";
            status.ForeColor = Color.DimGray;
            detail.Text = hasSnapshot ? "Choose an adapter above. If none are listed, connect one and refresh." : "Refresh to read the adapter status.";
            tray.Text = "NetOn — status unavailable";
            return;
        }
        status.Text = anyEnabled ? "Internet is on" : "Internet is off";
        stateDescription.Text = anyEnabled ? "Network access is allowed." : "All network adapters are disabled.";
        if (schedule.Due is not null) stateDescription.Text += "\nScheduled: " + scheduleSummary.Text;
        status.ForeColor = anyEnabled ? AppTheme.Positive : AppTheme.Negative;
        detail.Text = Selected is { } adapter ? $"{adapter.Description}\n{adapter.Name}: {adapter.Status}\nEnabled adapters: {snapshot.Count(a => a.Enabled)} of {snapshot.Count}" : $"Enabled adapters: {snapshot.Count(a => a.Enabled)} of {snapshot.Count}";
        trayToggle.Text = $"Turn internet {(anyEnabled ? "OFF" : "ON")}";
        string tip = $"NetOn: Internet {(anyEnabled ? "ON" : "OFF")}";
        if (schedule.Due is not null) tip += "\n" + scheduleSummary.Text;
        else if (anyEnabled && externalIp.Length > 0) tip += "\n" + externalIp;
        tray.Text = tip.Length > 63 ? tip[..63] : tip;
        if (announcedState != anyEnabled)
        {
            Say(anyEnabled ? "Internet enabled" : "Internet disabled");
            announcedState = anyEnabled;
        }
    }

    private void Say(string text) { if (desktopFeatures && !preferences.MuteAllSounds && !exiting) speech.Say(text); }
    internal void ApplyTheme()
    {
        AppTheme.Set(preferences.Theme);
        void PaintControls(Control control)
        {
            control.BackColor = control is ComboBox ? AppTheme.Surface : AppTheme.Paper;
            control.ForeColor = control is Label label && label.Font.Size <= 11 ? AppTheme.Muted : AppTheme.Ink;
            foreach (Control child in control.Controls) PaintControls(child);
            control.Invalidate();
        }
        PaintControls(this);
        error.ForeColor = AppTheme.Negative;
        void ThemeMenu(ToolStrip strip)
        {
            strip.Renderer = new ThemedMenuRenderer(); strip.BackColor = AppTheme.Surface; strip.ForeColor = AppTheme.Ink;
            strip.Font = menuFont;
            foreach (ToolStripItem item in strip.Items)
            {
                item.ForeColor = AppTheme.Ink;
                if (item is ToolStripMenuItem entry)
                {
                    if (entry.Tag is string key)
                    {
                        if (key == "startup") entry.Checked = desktopFeatures && AppSettings.StartWithWindows;
                        else if (key == "mute") entry.Checked = preferences.MuteAllSounds;
                        else if (key.StartsWith("voice:")) entry.Checked = key[6..] == (preferences.VoiceId ?? RecordedVoice.Id);
                        else if (key.StartsWith("theme:")) entry.Checked = key[6..] == preferences.Theme;
                    }
                    entry.Padding = new Padding(8, 6, 8, 6);
                    if (entry.HasDropDownItems) ThemeMenu(entry.DropDown);
                }
            }
        }
        if (tray.ContextMenuStrip is { } menu) ThemeMenu(menu);
        if (IsHandleCreated) { int dark = AppTheme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
        settingsWindow?.ApplyTheme();
        Render();
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); int dark = AppTheme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
    private void Report(Exception ex) { if (exiting || IsDisposed) return; error.Text = ex.Message; }
    private void SavePreferences() { if (!desktopFeatures) return; try { preferences.Save(); } catch (Exception ex) { Report(ex); } }

    private void ShowSettings()
    {
        if (settingsWindow is { IsDisposed: false }) { settingsWindow.Activate(); return; }
        using var settings = new SettingsForm(preferences, desktopFeatures ? Speech.ListVoices() : [],
            desktopFeatures && AppSettings.StartWithWindows,
            enabled =>
            {
                if (!desktopFeatures) return;
                try { AppSettings.StartWithWindows = enabled; Say(enabled ? "NetOn set to start with Windows" : "NetOn will not start with Windows"); }
                catch (Exception ex) { Report(ex); }
            },
            () => { if (desktopFeatures) speech.SetMuted(preferences.MuteAllSounds); SavePreferences(); RebuildIcons(); ApplyTheme(); },
            ChooseColor,
            () =>
            {
                var defaults = new AppSettings();
                preferences.OnBackground = defaults.OnBackground; preferences.OnForeground = defaults.OnForeground;
                preferences.OffBackground = defaults.OffBackground; preferences.OffForeground = defaults.OffForeground;
                SavePreferences(); RebuildIcons(); Say("Colors reset");
            });
        settingsWindow = settings;
        try { settings.ShowDialog(this); }
        finally { settingsWindow = null; }
        ApplyTheme();
    }

    private void ChooseColor(bool enabled, bool foreground)
    {
        Say(foreground ? "Please select foreground color" : "Please select background color");
        int argb = enabled ? (foreground ? preferences.OnForeground : preferences.OnBackground) : (foreground ? preferences.OffForeground : preferences.OffBackground);
        using var dialog = new ColorDialog { Color = Color.FromArgb(argb), FullOpen = false, SolidColorOnly = true };
        if (dialog.ShowDialog(Form.ActiveForm ?? this) != DialogResult.OK) return;
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

    internal async Task ReadExternalIpAsync()
    {
        if (readingIp || exiting || !snapshot.Any(a => a.Enabled)) return;
        readingIp = true;
        nextIpRead = DateTimeOffset.UtcNow.AddMinutes(1);
        Render();
        try
        {
            string value = (await externalIpLookup(lifetime.Token)).Trim();
            if (System.Net.IPAddress.TryParse(value, out _) && !exiting && snapshot.Any(a => a.Enabled)) { externalIp = value; Render(); }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { externalIp = ""; }
        finally { readingIp = false; if (!exiting && !IsDisposed) Render(); }
    }

    private static async Task<string> LookupExternalIpAsync(CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        return await client.GetStringAsync("https://api.ipify.org", token);
    }

    private async Task CopyExternalIpAsync()
    {
        copyIp.Enabled = false;
        try
        {
            await ReadExternalIpAsync();
            if (exiting) return;
            if (externalIp.Length == 0) error.Text = "External IP is unavailable while offline.";
            else { Clipboard.SetText(externalIp); hints.SetToolTip(copyIpButton, "Copied"); }
        }
        catch (Exception ex) { Report(ex); }
        finally { if (!exiting) Render(); }
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
            menuFont.Dispose(); hints.Dispose();
        }
        base.Dispose(disposing);
    }
}
