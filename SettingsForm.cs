using System.Runtime.InteropServices;

namespace Voltura.NetOn;

internal sealed class SettingsForm : Form
{
    private sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
    private readonly AppSettings preferences;
    private readonly Label savedNotice = new() { Name = "SettingsSavedNotice", Text = "Changes are saved automatically.", AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 12) };
    private bool saveFailed;
    private bool startupFailed;
    private string saveFailureMessage = "Changes could not be saved. Try again.";
    private bool syncing;
    private bool startupState;
    private ThemedCheckBox? startupSetting, muteSetting;
    private ThemedComboBox? voiceSetting, themeSetting;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal SettingsForm(AppSettings preferences, IReadOnlyList<Speech.VoiceChoice> voices, bool startup,
        Action<bool> setStartup, Action changed, Action<bool, bool> chooseColor, Action resetColors)
    {
        this.preferences = preferences;
        SuspendLayout();
        Text = "NetOn settings";
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(540, 560); MinimumSize = new Size(520, 490);
        Icon = StatusIcons.Create(true, new AppSettings());
        ShowInTaskbar = false; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var scroll = new Panel { Name = "SettingsScroll", Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        var layout = new TableLayoutPanel { Name = "SettingsContent", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, ColumnCount = 1, RowCount = 9 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Settings", AutoSize = true, Font = new Font("Segoe UI", 20, FontStyle.Bold), Margin = new Padding(0, 0, 0, 16) });
        var start = startupSetting = new ThemedCheckBox { Name = "StartupSetting", Text = "Start with Windows", Checked = startup, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        startupState = startup;
        bool restoringStartup = false;
        start.CheckedChanged += (_, _) =>
        {
            if (restoringStartup || syncing) return;
            try { setStartup(start.Checked); startupState = start.Checked; startupFailed = false; UpdateSavedNotice(); }
            catch (Exception)
            {
                restoringStartup = true; start.Checked = startupState; restoringStartup = false;
                startupFailed = true;
                UpdateSavedNotice();
            }
        };
        void Persist()
        {
            try
            {
                changed();
                saveFailed = false;
                UpdateSavedNotice();
            }
            catch (Exception) { ReportSaveFailure(); }
        }
        layout.Controls.Add(start);
        var mute = muteSetting = new ThemedCheckBox { Name = "MuteSetting", Text = "Mute all sounds", Checked = preferences.MuteAllSounds, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        mute.CheckedChanged += (_, _) => { if (syncing) return; preferences.MuteAllSounds = mute.Checked; Persist(); };
        layout.Controls.Add(mute);
        layout.Controls.Add(new Label { Text = "Voice", AutoSize = true, Margin = new Padding(0, 0, 0, 5) });
        var voice = voiceSetting = new ThemedComboBox { Name = "VoiceSetting", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16) };
        voice.Items.Add(new Choice(RecordedVoice.Id, "Recorded voice - English"));
        foreach (var item in voices) voice.Items.Add(new Choice(item.Id, item.Name));
        voice.SelectedItem = voice.Items.Cast<Choice>().FirstOrDefault(v => v.Id == (preferences.VoiceId ?? RecordedVoice.Id)) ?? voice.Items[0];
        voice.SelectedIndexChanged += (_, _) => { if (syncing) return; preferences.VoiceId = ((Choice)voice.SelectedItem!).Id; Persist(); };
        layout.Controls.Add(voice);
        layout.Controls.Add(new Label { Text = "Appearance", AutoSize = true, Margin = new Padding(0, 0, 0, 5) });
        var theme = themeSetting = new ThemedComboBox { Name = "ThemeSetting", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16) };
        theme.Items.AddRange([new Choice("System", "Follow Windows"), new Choice("Light", "Light"), new Choice("Dark", "Dark")]);
        theme.SelectedItem = theme.Items.Cast<Choice>().FirstOrDefault(t => t.Id == preferences.Theme) ?? theme.Items[0];
        theme.SelectedIndexChanged += (_, _) => { if (syncing) return; preferences.Theme = ((Choice)theme.SelectedItem!).Id; Persist(); ApplyTheme(); };
        layout.Controls.Add(theme);
        layout.Controls.Add(new Label { Text = "Icon colors", AutoSize = true, Margin = new Padding(0, 0, 0, 5) });
        var colors = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = true };
        foreach (var (title, enabled, foreground) in new[] { ("ON background", true, false), ("ON foreground", true, true), ("OFF background", false, false), ("OFF foreground", false, true) })
        {
            var button = new ModernButton { Text = title, Quiet = true, Size = new Size(150, 40), MinimumSize = Size.Empty };
            button.Click += (_, _) => chooseColor(enabled, foreground);
            colors.Controls.Add(button);
        }
        var reset = new ModernButton { Text = "Reset colors", Quiet = true, Size = new Size(150, 40), MinimumSize = Size.Empty };
        reset.Click += (_, _) => resetColors(); colors.Controls.Add(reset);
        layout.Controls.Add(colors);
        var footer = new TableLayoutPanel { Name = "SettingsFooter", AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.Controls.Add(savedNotice);
        var close = new ModernButton { Name = "SettingsDone", Text = "Done", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        footer.Controls.Add(close); AcceptButton = CancelButton = close;
        scroll.Controls.Add(layout);
        root.Controls.Add(scroll, 0, 0); root.Controls.Add(footer, 0, 1);
        Controls.Add(root); ApplyTheme(); ResumeLayout(true);
    }

    internal void ApplyTheme()
    {
        SyncFromPreferences();
        AppTheme.Set(preferences.Theme);
        void Paint(Control control)
        {
            control.BackColor = control is ComboBox ? AppTheme.Surface : AppTheme.Paper;
            control.ForeColor = AppTheme.Ink;
            foreach (Control child in control.Controls) Paint(child);
            control.Invalidate();
        }
        Paint(this);
        savedNotice.ForeColor = saveFailed || startupFailed ? AppTheme.Negative : AppTheme.Muted;
        if (IsHandleCreated) { int dark = AppTheme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
    }
    internal void SyncFromPreferences(bool? startup = null)
    {
        if (IsDisposed || syncing) return;
        syncing = true;
        try
        {
            if (startup.HasValue && startupSetting is not null) { startupState = startup.Value; startupSetting.Checked = startup.Value; }
            if (muteSetting is not null) muteSetting.Checked = preferences.MuteAllSounds;
            if (voiceSetting is not null) voiceSetting.SelectedItem = voiceSetting.Items.Cast<Choice>().FirstOrDefault(v => v.Id == (preferences.VoiceId ?? RecordedVoice.Id)) ?? voiceSetting.Items[0];
            if (themeSetting is not null) themeSetting.SelectedItem = themeSetting.Items.Cast<Choice>().FirstOrDefault(t => t.Id == preferences.Theme) ?? themeSetting.Items[0];
        }
        finally { syncing = false; }
    }
    internal void ReportSaveFailure(string message = "Changes could not be saved. Try again.")
    {
        saveFailed = true;
        saveFailureMessage = message;
        UpdateSavedNotice();
    }
    private void UpdateSavedNotice()
    {
        savedNotice.Text = saveFailed ? saveFailureMessage : startupFailed ? "Could not change Start with Windows." : "Changes are saved automatically.";
        savedNotice.ForeColor = saveFailed || startupFailed ? AppTheme.Negative : AppTheme.Muted;
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyTheme(); }
    protected override void Dispose(bool disposing) { if (disposing) Icon?.Dispose(); base.Dispose(disposing); }
}
