using System.Runtime.InteropServices;

namespace Voltura.NetOn;

internal sealed class SettingsForm : Form
{
    private sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
    private readonly AppSettings preferences;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal SettingsForm(AppSettings preferences, IReadOnlyList<Speech.VoiceChoice> voices, bool startup,
        Action<bool> setStartup, Action changed, Action<bool, bool> chooseColor, Action resetColors)
    {
        this.preferences = preferences;
        SuspendLayout();
        Text = "NetOn settings";
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(540, 500); MinimumSize = new Size(520, 490);
        Icon = StatusIcons.Create(true, new AppSettings());
        ShowInTaskbar = false; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 11 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 11; i++) layout.RowStyles.Add(new RowStyle(i == 9 ? SizeType.Percent : SizeType.AutoSize, i == 9 ? 100 : 0));
        layout.Controls.Add(new Label { Text = "Settings", AutoSize = true, Font = new Font("Segoe UI", 20, FontStyle.Bold), Margin = new Padding(0, 0, 0, 16) });
        var start = new CheckBox { Name = "StartupSetting", Text = "Start with Windows", Checked = startup, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        start.CheckedChanged += (_, _) => setStartup(start.Checked);
        layout.Controls.Add(start);
        var mute = new CheckBox { Name = "MuteSetting", Text = "Mute all sounds", Checked = preferences.MuteAllSounds, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        mute.CheckedChanged += (_, _) => { preferences.MuteAllSounds = mute.Checked; changed(); };
        layout.Controls.Add(mute);
        layout.Controls.Add(new Label { Text = "Voice", AutoSize = true, Margin = new Padding(0, 0, 0, 5) });
        var voice = new ThemedComboBox { Name = "VoiceSetting", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16) };
        voice.Items.Add(new Choice(RecordedVoice.Id, "Recorded voice - English"));
        foreach (var item in voices) voice.Items.Add(new Choice(item.Id, item.Name));
        voice.SelectedItem = voice.Items.Cast<Choice>().FirstOrDefault(v => v.Id == (preferences.VoiceId ?? RecordedVoice.Id)) ?? voice.Items[0];
        voice.SelectedIndexChanged += (_, _) => { preferences.VoiceId = ((Choice)voice.SelectedItem!).Id; changed(); };
        layout.Controls.Add(voice);
        layout.Controls.Add(new Label { Text = "Appearance", AutoSize = true, Margin = new Padding(0, 0, 0, 5) });
        var theme = new ThemedComboBox { Name = "ThemeSetting", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16) };
        theme.Items.AddRange([new Choice("System", "Follow Windows"), new Choice("Light", "Light"), new Choice("Dark", "Dark")]);
        theme.SelectedItem = theme.Items.Cast<Choice>().FirstOrDefault(t => t.Id == preferences.Theme) ?? theme.Items[0];
        theme.SelectedIndexChanged += (_, _) => { preferences.Theme = ((Choice)theme.SelectedItem!).Id; changed(); ApplyTheme(); };
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
        layout.Controls.Add(new Label { Text = "Changes are saved automatically.", AutoSize = true, Margin = new Padding(0, 8, 0, 8) });
        var close = new ModernButton { Text = "Done", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        layout.Controls.Add(close); AcceptButton = CancelButton = close;
        Controls.Add(layout); ApplyTheme(); ResumeLayout(true);
    }

    internal void ApplyTheme()
    {
        AppTheme.Set(preferences.Theme);
        void Paint(Control control)
        {
            control.BackColor = control is ComboBox ? AppTheme.Surface : AppTheme.Paper;
            control.ForeColor = AppTheme.Ink;
            foreach (Control child in control.Controls) Paint(child);
            control.Invalidate();
        }
        Paint(this);
        if (IsHandleCreated) { int dark = AppTheme.Dark ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyTheme(); }
    protected override void Dispose(bool disposing) { if (disposing) Icon?.Dispose(); base.Dispose(disposing); }
}
