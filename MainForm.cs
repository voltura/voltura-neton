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
    private readonly Label status = new() { AutoSize = true, Text = "Checking…", Font = new Font("Segoe UI", 23, FontStyle.Regular), Margin = new Padding(0, 17, 0, 0) };
    private readonly SwitchGlyph stateGlyph = new() { Margin = new Padding(0, 0, 4, 0), Size = new Size(90, 75) };
    private readonly Label stateDescription = new() { AutoSize = true, MaximumSize = new Size(420, 0), Font = new Font("Segoe UI", 10), ForeColor = AppTheme.Muted, Margin = new Padding(0, 1, 0, 6) };
    private readonly Label detail = new() { AutoSize = true, MaximumSize = new Size(420, 0), Font = new Font("Segoe UI", 9), ForeColor = AppTheme.Muted, Margin = new Padding(0, 10, 0, 7) };
    private readonly Label error = new() { Name = "ErrorMessage", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = AppTheme.Red, Font = new Font("Segoe UI", 9) };
    private readonly ModernButton turnOn = new() { Name = "TurnOn", Text = "Turn on", Accent = AppTheme.Green };
    private readonly ModernButton turnOff = new() { Name = "TurnOff", Text = "Turn off", Accent = AppTheme.Red };
    private readonly ModernButton refresh = new() { Text = "Refresh", Quiet = true };
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
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public MainForm(IAdapterService? service = null, AppSettings? settings = null, bool desktopFeatures = true)
    {
        this.service = service ?? new NetworkAdapters();
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
        ClientSize = new Size(820, 590);
        MinimumSize = new Size(820, 610);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppTheme.Paper;
        ForeColor = AppTheme.Ink;
        adapters.DrawMode = DrawMode.OwnerDrawFixed;
        adapters.ItemHeight = 26;
        adapters.FlatStyle = FlatStyle.Flat;
        adapters.BackColor = AppTheme.Paper;
        adapters.ForeColor = AppTheme.Ink;
        adapters.DrawItem += (_, e) =>
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(selected ? AppTheme.Green : AppTheme.Paper);
            e.Graphics.FillRectangle(fill, e.Bounds);
            string text = e.Index >= 0 && e.Index < adapters.Items.Count ? adapters.Items[e.Index]?.ToString() ?? "" : Selected?.Name ?? "Choose a connection";
            var bounds = Rectangle.Inflate(e.Bounds, -8, 0);
            TextRenderer.DrawText(e.Graphics, text, adapters.Font, bounds, selected ? Color.White : AppTheme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
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
        content.Controls.Add(new Label { Text = "CONNECTION DETAILS", AutoSize = true, Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = AppTheme.Muted, Margin = new Padding(0, 4, 0, 9) });
        content.Controls.Add(adapters);
        content.Controls.Add(detail);
        content.Controls.Add(error);
        content.Controls.Add(new Label { Text = "One click switches all connections.\nThe list above is just for a closer look.", AutoSize = true, ForeColor = AppTheme.Muted, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 12, 0, 0) });
        body.Controls.Add(content, 1, 0);
        root.Controls.Add(body, 0, 1);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 16, 0, 0) };
        var hide = new ModernButton { Text = "Hide to tray", Quiet = true };
        var exit = new ModernButton { Name = "Exit", Text = "Exit NetOn", Quiet = true };
        footer.Controls.AddRange([refresh, hide, exit]);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => ApplyTheme();
        menu.Items.Add("About NetOn", null, (_, _) =>
        {
            Say("NetOn by Voltura AB");
            MessageBox.Show(this, "NetOn by Voltura AB\nVersion 1.0.5\nhttps://voltura.github.io/voltura-neton/\n\nFree for all.", "About NetOn", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        mute.Click += (_, _) => { preferences.MuteAllSounds = !preferences.MuteAllSounds; mute.Checked = preferences.MuteAllSounds; if (desktopFeatures) speech.SetMuted(preferences.MuteAllSounds); SavePreferences(); };
        settingsMenu.DropDownItems.Add(mute);
        var voices = new ToolStripMenuItem("Voice");
        var defaultVoice = new ToolStripMenuItem("Recorded voice (offline)") { Checked = preferences.VoiceId is null or RecordedVoice.Id };
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
                var item = new ToolStripMenuItem(choice.Name) { Checked = preferences.VoiceId == choice.Id };
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
            var choice = new ToolStripMenuItem(theme == "System" ? "Follow Windows" : theme) { Checked = preferences.Theme == theme };
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
        timer.Tick += async (_, _) => { if (preferences.Theme == "System" && AppTheme.Dark != AppTheme.ResolveDark("System")) ApplyTheme(); await RefreshAsync(); };
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
        stateGlyph.State = null;
        stateDescription.Text = enable ? "Bringing your connections back…" : "Taking a little offline time…";
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
        stateDescription.Text = anyEnabled ? "Ready when you are. Switch off for a little offline time." : "A little peace and quiet. Switch on when you're ready.";
        status.ForeColor = anyEnabled ? AppTheme.Positive : AppTheme.Negative;
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
                    entry.Padding = new Padding(8, 6, 8, 6);
                    if (entry.HasDropDownItems) ThemeMenu(entry.DropDown);
                }
            }
        }
        if (tray.ContextMenuStrip is { } menu) ThemeMenu(menu);
        if (IsHandleCreated) { int dark = AppTheme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
        Render();
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); int dark = AppTheme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
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
            menuFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
