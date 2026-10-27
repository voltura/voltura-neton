using System.Reflection;
using Voltura.NetOn;

internal static class Program
{
    private static int failures;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--dpi-unaware")) SetThreadDpiAwarenessContext((IntPtr)(-1));
        Application.SetHighDpiMode(args.Contains("--dpi-unaware") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 2 && args[0] == "--write-icon")
        {
            using var icon = StatusIcons.Create(true, new AppSettings());
            using var file = File.Create(args[1]);
            icon.Save(file);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--render-speech")
        {
            RenderSpeech(args[1]);
            return 0;
        }
        bool renderUi = args.Length >= 2 && args[0] == "--render-ui";
        if (!renderUi) { CheckLiveAdapters(); CheckIcons(); CheckInstances(); CheckPronunciation(); }
        using var context = new ApplicationContext();
        using var start = new System.Windows.Forms.Timer { Interval = 1 };
        start.Tick += async (_, _) =>
        {
            start.Stop();
            try { if (renderUi) await RenderUiAsync(args[1]); else { await CheckPowerShellAsync(); CheckSchedules(); await CheckFormAsync(); } }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL: " + ex); }
            finally { context.ExitThread(); }
        };
        start.Start();
        Application.Run(context);
        Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED (no network adapters changed; no windows shown)." : $"FAILED: {failures}");
        return failures == 0 ? 0 : 1;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void CheckLiveAdapters()
    {
        var found = NetworkAdapters.Read();
        Require(found.Count > 0 && found.All(a => a.Id != Guid.Empty && a.Name.Length > 0), "native discovery returns identified hardware adapters");
        foreach (var adapter in found) Console.WriteLine($"  {adapter.Name}: {adapter.Status}, enabled={adapter.Enabled}, {adapter.Id:D}");
        Require(found.Select(a => a.Id).Distinct().Count() == found.Count, "no duplicate filter interfaces in adapter list");
    }

    private static void CheckPronunciation()
    {
        foreach (string phrase in RecordedVoice.Phrases.Keys)
        {
            byte[] pcm = RecordedVoice.ReadPcm(phrase) ?? throw new Exception("Missing natural voice clip: " + phrase);
            byte[] wave = Speech.RenderWave(phrase, RecordedVoice.Id);
            Require(pcm.Length > Speech.SampleRate && wave.AsSpan(44).SequenceEqual(pcm), "offline recorded voice is used for: " + phrase);
        }
        var source = new AnnouncementAudio();
        var idle = new byte[200];
        Require(source.Read(idle, 0, idle.Length) == idle.Length, "audio output stays open while idle");
        byte[] clip = RecordedVoice.ReadPcm("Internet enabled")!;
        source.Play(clip);
        var collected = new List<byte>();
        var block = new byte[512];
        while (source.EndPosition is null)
        {
            Array.Fill(block, (byte)99);
            if (source.Read(block, 6, 500) != 500 || block[0] != 99 || block[511] != 99) throw new Exception("stream overwrote requested buffer bounds");
            collected.AddRange(block.AsSpan(6, 500).ToArray());
        }
        Require(collected.Take(clip.Length).SequenceEqual(clip), "stream delivers first through last recording sample unchanged across buffer boundaries");
        Require(source.EndPosition == 200 + clip.Length, "completion tracks the final sample for hardware playback confirmation");
        source.Play(clip);
        source.Clear();
        source.Read(idle, 0, idle.Length);
        Require(source.EndPosition is null && idle[0] == 1 && idle[1] == 0, "mute clears queued speech and leaves only inaudible idle samples");
        string markup = Speech.PronunciationMarkup("NetOn by Voltura AB & friends", true);
        var document = System.Xml.Linq.XDocument.Parse("<root>" + markup + "</root>");
        Require(document.Descendants("pron").Count() == 2, "company name and AB each have an explicit pronunciation");
        Require(document.Root!.Value.Contains("VolturaAB & friends"), "speech text remains safely escaped around the company name");
        Require(Speech.PronunciationMarkup("Voltura AB", false) == "Voltuura, Aabe", "non-English voices receive the requested spoken company name");
    }

    private static void RenderSpeech(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (string text in new[] { "Internet enabled", "Internet disabled", "NetOn by Voltura AB" })
        {
            byte[] wave = Speech.RenderWave(text);
            File.WriteAllBytes(Path.Combine(directory, text.Replace(' ', '-') + ".wav"), wave);
            using var input = new BinaryReader(new MemoryStream(wave));
            Require(System.Text.Encoding.ASCII.GetString(input.ReadBytes(4)) == "RIFF", "speech is rendered as a WAV stream");
            input.BaseStream.Position = 24;
            int rate = input.ReadInt32();
            Require(rate == Speech.SampleRate, "recording has the expected PCM sample rate");
            Require(wave.AsSpan(44).SequenceEqual(RecordedVoice.ReadPcm(text)), "entire original recording preserved without beep or trimming");

        }
    }
    private static void CheckIcons()
    {
        var settings = new AppSettings();
        using var on = StatusIcons.Draw(true, settings, 64);
        using var off = StatusIcons.Draw(false, settings, 64);
        Require(IsStateColor(on, true), "ON icon is green");
        Require(IsStateColor(off, false), "OFF icon is red");
        Require(on.GetPixel(0, 0).A == 0 && off.GetPixel(0, 0).A == 0, "toggle icons have transparent corners");
        using var icon = StatusIcons.Create(true, settings);
        using var bitmap = icon.ToBitmap();
        Require(IsStateColor(bitmap, true), "multi-resolution tray icon retains its color");
        foreach (int size in new[] { 16, 20, 24, 32, 48, 64, 128, 256 })
        {
            using var smallOn = StatusIcons.Draw(true, settings, size);
            using var smallOff = StatusIcons.Draw(false, settings, size);
            Require(IsStateColor(smallOn, true) && IsStateColor(smallOff, false), $"green/red toggle colors remain distinct at {size}px");
        }
    }

    private static bool IsStateColor(Bitmap image, bool enabled)
    {
        Color sample = image.GetPixel((int)(image.Width * (enabled ? .2 : .8)), image.Height / 2);
        return sample.A > 200 && (enabled ? sample.G > sample.R * 1.3 && sample.G > sample.B * 1.15 : sample.R > sample.G * 1.3 && sample.R > sample.B * 1.3);
    }

    private static async Task RenderUiAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var service = new FakeService { Snapshot = [new Adapter("Ethernet", Guid.NewGuid(), "Intel Ethernet Controller", "Connected", 1), new Adapter("Wi-Fi", Guid.NewGuid(), "Wireless network adapter", "Disconnected", 1)] };
        var previewSettings = new AppSettings { MuteAllSounds = true, Theme = "Light" };
        using var form = new MainForm(service, previewSettings, false, _ => Task.FromResult("203.0.113.5"));
        _ = form.Handle;
        void Prepare(System.Windows.Forms.Control control) { _ = control.Handle; control.PerformLayout(); foreach (System.Windows.Forms.Control child in control.Controls) Prepare(child); }
        Prepare(form);
        await form.RefreshAsync();
        await form.ReadExternalIpAsync();
        Prepare(form);
        var adapterControl = Control<ComboBox>(form, "AdapterSelector");
        Require(adapterControl.Width > 150 && adapterControl.Height > 20 && adapterControl.SelectedIndex >= 0, "themed dropdown has a usable size and a selected adapter");
        Console.WriteLine($"Dropdown: {adapterControl.Bounds}, selected={adapterControl.SelectedItem}");
        using (var on = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(on, new Rectangle(Point.Empty, form.Size)); on.Save(Path.Combine(directory, "neton-app-on.png")); }
        previewSettings.Theme = "Dark"; form.ApplyTheme(); Prepare(form);
        using (var dark = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(dark, new Rectangle(Point.Empty, form.Size)); dark.Save(Path.Combine(directory, "neton-app-dark.png")); }
        Require(form.BackColor == AppTheme.Paper && AppTheme.Dark, "dark theme applies to the app window");
        var contextMenu = (NotifyIcon)typeof(MainForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var menu = contextMenu.ContextMenuStrip!; _ = menu.Handle; menu.PerformLayout(); menu.Size = menu.PreferredSize;
        using (var menuImage = new Bitmap(menu.Width, menu.Height)) { menu.DrawToBitmap(menuImage, menu.ClientRectangle); menuImage.Save(Path.Combine(directory, "neton-menu-dark.png")); }
        var settingsMenu = ((ToolStripMenuItem)menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "Settings")).DropDown;
        _ = settingsMenu.Handle; settingsMenu.PerformLayout(); settingsMenu.Size = settingsMenu.PreferredSize;
        using (var menuImage = new Bitmap(settingsMenu.Width, settingsMenu.Height)) { settingsMenu.DrawToBitmap(menuImage, settingsMenu.ClientRectangle); menuImage.Save(Path.Combine(directory, "neton-settings-menu-dark.png")); }
        previewSettings.Theme = "Light"; form.ApplyTheme(); Prepare(form);
        Require(!AppTheme.Dark, "light theme applies independently of the Windows setting");
        await form.ChangeAsync(false);
        Prepare(form);
        using (var off = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(off, new Rectangle(Point.Empty, form.Size)); off.Save(Path.Combine(directory, "neton-app-off.png")); }

        foreach (string theme in new[] { "Light", "Dark" })
        {
            AppTheme.Set(theme);
            using var about = new AboutForm(); Prepare(about);
            using (var image = new Bitmap(about.Width, about.Height)) { about.DrawToBitmap(image, new Rectangle(Point.Empty, about.Size)); image.Save(Path.Combine(directory, $"neton-about-{theme.ToLowerInvariant()}.png")); }
            using var settings = new SettingsForm(new AppSettings { Theme = theme, MuteAllSounds = true }, [], false, _ => { }, () => { }, (_, _) => { }, () => { }); Prepare(settings);
            CheckSettingsLayout(settings);
            using (var image = new Bitmap(settings.Width, settings.Height)) { settings.DrawToBitmap(image, new Rectangle(Point.Empty, settings.Size)); image.Save(Path.Combine(directory, $"neton-settings-{theme.ToLowerInvariant()}.png")); }
        }
        previewSettings.Theme = "Dark"; form.ApplyTheme(); form.SetConnectionDetails(true); Prepare(form);
        var details = Control<Label>(form, "AdapterDetails");
        Console.WriteLine($"Detail gap={details.Top - adapterControl.Bottom}, margin={details.Margin}, dpi={form.DeviceDpi}, detail={details.Bounds}, adapter={adapterControl.Bounds}");
        Require(details.Top - adapterControl.Bottom >= 18 * form.DeviceDpi / 96, "adapter description has DPI-scaled space below the dropdown");
        using (var expanded = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(expanded, new Rectangle(Point.Empty, form.Size)); expanded.Save(Path.Combine(directory, "neton-details-expanded.png")); }
        var selector = Control<ThemedComboBox>(form, "AdapterSelector");
        using (var face = new Bitmap(selector.Width, selector.Height)) { using var g = Graphics.FromImage(face); selector.PaintFace(g); face.Save(Path.Combine(directory, "neton-dropdown-face.png")); }
        using (var rows = new Bitmap(selector.Width, selector.ItemHeight * selector.Items.Count))
        {
            using var g = Graphics.FromImage(rows);
            for (int i = 0; i < selector.Items.Count; i++) typeof(ThemedComboBox).GetMethod("OnDrawItem", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(selector, [new DrawItemEventArgs(g, selector.Font, new Rectangle(0, i * selector.ItemHeight, selector.Width, selector.ItemHeight), i, i == selector.SelectedIndex ? DrawItemState.Selected : DrawItemState.None)]);
            rows.Save(Path.Combine(directory, "neton-dropdown-rows.png"));
        }
        using var onIcon = StatusIcons.Draw(true, new AppSettings(), 128);
        using var offIcon = StatusIcons.Draw(false, new AppSettings(), 128);
        onIcon.Save(Path.Combine(directory, "icon-on.png")); offIcon.Save(Path.Combine(directory, "icon-off.png"));
        Console.WriteLine($"Rendered hidden app previews at {form.ClientSize.Width}×{form.ClientSize.Height}, DPI={form.DeviceDpi}");
    }

    private static void CheckInstances()
    {
        string name = "Local\\NetOn.Verification." + Guid.NewGuid();
        using var primary = new SingleInstance(name);
        Require(primary.IsPrimary, "first instance owns the mutex");
        using var activated = new ManualResetEventSlim();
        primary.Listen(() => activated.Set());
        var other = new Thread(() =>
        {
            using var secondary = new SingleInstance(name);
            if (secondary.IsPrimary) throw new Exception("Second instance acquired the mutex");
            secondary.RequestActivation();
        });
        other.Start(); other.Join();
        Require(activated.Wait(2000), "second instance signals the first to reopen, without a dialog");
        string abandonedName = name + ".Abandoned";
        Mutex? abandoned = null;
        var owner = new Thread(() => abandoned = new Mutex(true, abandonedName));
        owner.Start(); owner.Join();
        using (abandoned)
        using (var recovered = new SingleInstance(abandonedName))
            Require(recovered.IsPrimary, "app can restart after an abandoned instance");
    }

    private static T Control<T>(MainForm form, string name) where T : System.Windows.Forms.Control => (T)form.Controls.Find(name, true).Single();

    private static async Task CheckFormAsync()
    {
        var ethernet = new Adapter("Ethernet", Guid.NewGuid(), "Test Ethernet", "Connected", 1);
        var wifi = new Adapter("Wi-Fi", Guid.NewGuid(), "Test Wi-Fi", "Disconnected", 1);
        var spare = new Adapter("Spare", Guid.NewGuid(), "Previously disabled", "Disabled", 2);
        var service = new FakeService { Snapshot = [ethernet, wifi, spare] };
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"MuteAllSounds\":true,\"ShowConnectionDetails\":true}")!;
        using var form = new MainForm(service, settings, desktopFeatures: false, externalIpLookup: _ => Task.FromResult("203.0.113.5"));
        _ = form.Handle;
        await form.RefreshAsync();
        var selector = Control<ComboBox>(form, "AdapterSelector");
        Require(!form.Visible, "startup remains in the tray without opening a window");
        Require(selector.Items.Count == 3 && ((Adapter)selector.SelectedItem!).Id == ethernet.Id, "connected adapter is selected initially");
        Require(Control<Button>(form, "TurnOff").Enabled, "OFF control enables after successful discovery");
        Require(Control<Button>(form, "DetailsToggle").Text == "Show connection details", "connection details are collapsed by default");
        int collapsedWidth = form.Width;
        form.SetConnectionDetails(true);
        Require(form.Width == collapsedWidth, "expanding details keeps the main window width unchanged");
        Require(Control<Button>(form, "DetailsToggle").Text == "Hide connection details", "details toggle expands connection details");
        form.SetConnectionDetails(false);
        Require(Control<Button>(form, "DetailsToggle").Text == "Show connection details", "details toggle collapses connection details");
        Require(Control<Button>(form, "Settings").Parent!.Controls.IndexOf(Control<Button>(form, "Settings")) < Control<Button>(form, "Exit").Parent!.Controls.IndexOf(Control<Button>(form, "Exit")), "Settings appears before Exit in the main window");
        var menuTray = (NotifyIcon)typeof(MainForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        Require(menuTray.ContextMenuStrip!.Items[0].Text == "Open NetOn" && menuTray.ContextMenuStrip.Items.OfType<ToolStripSeparator>().Count() == 4, "tray menu names the main window and separates its action groups");
        Require(menuTray.Text.Contains("Click to turn internet OFF"), "ON tooltip includes the click action");
        var internetItem = menuTray.ContextMenuStrip.Items.Cast<ToolStripItem>().Single(i => i.Text == "Internet");
        Require(internetItem.ToolTipText == "Turn Internet connection Off" && internetItem.Image is Bitmap greenSwitch && IsStateColor(greenSwitch, true), "Internet menu row shows green switch with OFF action tooltip");
        CheckControlPainting(form);
        var footer = Control<TableLayoutPanel>(form, "MainFooter");
        var first = footer.Controls.Cast<System.Windows.Forms.Control>().First();
        var last = Control<Button>(form, "Exit");
        Require(Math.Abs(first.Left - (footer.ClientSize.Width - last.Right)) <= 1, "footer buttons have equal left and right spacing");
        var adapterSelector = Control<ThemedComboBox>(form, "AdapterSelector");
        Require(adapterSelector.Width < adapterSelector.Parent!.Width, "adapter selector fits connection names instead of filling the column");
        var longAdapter = new Adapter("Bluetooth Network Connection", Guid.NewGuid(), "Test Bluetooth", "Disconnected", 1);
        adapterSelector.Items.Add(longAdapter);
        adapterSelector.Parent.PerformLayout();
        int nameWidth = TextRenderer.MeasureText(longAdapter.Name, adapterSelector.Font).Width;
        Require(adapterSelector.Width >= nameWidth + 24 * adapterSelector.DeviceDpi / 96 && adapterSelector.Width < adapterSelector.Parent.Width, "long connection name fits without making the selector fill the window");
        adapterSelector.Items.Remove(longAdapter);
        adapterSelector.Parent.PerformLayout();
        var detailsButton = Control<Button>(form, "DetailsToggle");
        Require(detailsButton.Width >= TextRenderer.MeasureText(detailsButton.Text, detailsButton.Font).Width, "details expander fits its full label at the current DPI");
        await form.ReadExternalIpAsync();
        Require(Control<Label>(form, "ExternalIp").Text.Contains("203.0.113.5") && Control<Button>(form, "CopyExternalIp").Enabled, "external IP and copy button are available in the main window");
        var glyph = Control<SwitchGlyph>(form, "MainSwitch");
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(glyph, [EventArgs.Empty]);
        Require(service.Snapshot.All(a => !a.Enabled) && glyph.AccessibleName == "Turn internet on", "main switch click turns all adapters OFF and updates its accessible action");
        Require(menuTray.Text.Contains("Click to turn internet ON"), "OFF tooltip includes the click action");
        Require(internetItem.ToolTipText == "Turn Internet connection On" && internetItem.Image is Bitmap redSwitch && IsStateColor(redSwitch, false), "Internet menu row shows red switch with ON action tooltip");
        Require(!Control<Button>(form, "CopyExternalIp").Enabled && !Control<Label>(form, "ExternalIp").Text.Contains("203.0.113.5"), "OFF hides stale IP and disables copying");
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(glyph, [EventArgs.Empty]);
        Require(service.Snapshot.Where(a => a.Enabled).Select(a => a.Id).ToHashSet().SetEquals([ethernet.Id, wifi.Id]), "main switch click restores exactly the previously enabled adapters");

        var oldRead = new TaskCompletionSource<List<Adapter>>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ReadGate = oldRead;
        Task staleRefresh = form.RefreshAsync();
        service.ReadGate = null;
        await form.ChangeAsync(false);
        oldRead.SetResult([ethernet, wifi, spare]);
        await staleRefresh;
        Require(Control<Button>(form, "TurnOn").Enabled && !Control<Button>(form, "TurnOff").Enabled, "late refresh cannot overwrite a completed tray toggle with old state");
        await form.ChangeAsync(true);


        service.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task refresh = form.RefreshAsync();
        Require(selector.Enabled, "dropdown remains usable during a slow background refresh");
        selector.SelectedIndex = 1;
        service.ReadGate.SetResult([ethernet, wifi, spare]);
        await refresh;
        Require(((Adapter)selector.SelectedItem!).Id == wifi.Id, "background refresh preserves the user's new selection");
        service.ReadGate = null;

        service.ReadError = new IOException("Simulated discovery failure");
        await form.RefreshAsync();
        Require(selector.Enabled && !Control<Button>(form, "TurnOff").Enabled, "discovery errors leave selection and Exit usable without stale toggle controls");
        service.ReadError = null;
        await form.RefreshAsync();
        Require(Control<Button>(form, "TurnOff").Enabled, "refresh recovers controls after an earlier discovery failure");

        await form.ChangeAsync(false);
        Require(service.LastIds.ToHashSet().SetEquals([ethernet.Id, wifi.Id]), "OFF targets both Ethernet and Wi-Fi regardless of the dropdown selection");
        Require(service.Snapshot.All(a => !a.Enabled), "OFF disables every enabled adapter");
        Require(Control<Button>(form, "TurnOn").Enabled && !Control<Button>(form, "TurnOff").Enabled, "successful OFF switches the controls to allow ON");
        using (var red = form.Icon!.ToBitmap()) Require(IsStateColor(red, false), "successful OFF switches the displayed icon to red");
        await form.ChangeAsync(true);
        Require(service.Snapshot.Single(a => a.Id == spare.Id).Enabled == false, "ON leaves previously disabled adapters disabled");
        Require(service.LastIds.ToHashSet().SetEquals([ethernet.Id, wifi.Id]), "ON restores exactly the previously enabled adapters");
        Require(Control<Button>(form, "TurnOff").Enabled && !Control<Button>(form, "TurnOn").Enabled, "successful ON switches the controls to allow OFF");
        using (var green = form.Icon!.ToBitmap()) Require(IsStateColor(green, true), "successful ON switches the displayed icon to green");

        var scheduledAt = DateTimeOffset.UtcNow;
        form.ScheduleChange(false, TimeSpan.FromMinutes(10), scheduledAt);
        await form.RunScheduledChangeAsync(scheduledAt.AddMinutes(9));
        Require(service.Snapshot.Any(a => a.Enabled), "scheduled OFF does not run early");
        await form.RunScheduledChangeAsync(scheduledAt.AddMinutes(10));
        Require(service.Snapshot.All(a => !a.Enabled), "scheduled OFF uses the same global adapter switch");
        form.ScheduleChange(true, TimeSpan.FromMinutes(30), scheduledAt);
        await form.RunScheduledChangeAsync(scheduledAt.AddHours(1));
        Require(service.Snapshot.Where(a => a.Enabled).Select(a => a.Id).ToHashSet().SetEquals([ethernet.Id, wifi.Id]), "overdue ON restores only the connections saved before OFF");
        service.LastIds = [];
        await form.RunScheduledChangeAsync(scheduledAt.AddDays(1));
        Require(service.LastIds.Count == 0, "scheduled change executes only once");
        var scheduleTray = (NotifyIcon)typeof(MainForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var scheduleMenu = (ToolStripMenuItem)scheduleTray.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(i => i.Text == "Schedule");
        Require(((ToolStripMenuItem)scheduleMenu.DropDownItems[2]).DropDownItems.Count == 7 && ((ToolStripMenuItem)scheduleMenu.DropDownItems[3]).DropDownItems.Count == 7, "tray menu offers all seven delays for both ON and OFF");
        form.ScheduleChange(false, TimeSpan.FromHours(1), scheduledAt);
        scheduleMenu.DropDownItems.Cast<ToolStripItem>().Single(i => i.Text == "Cancel scheduled change").PerformClick();
        await form.RunScheduledChangeAsync(scheduledAt.AddHours(2));
        Require(service.LastIds.Count == 0, "canceling a scheduled change prevents execution");
        service.IgnoreId = wifi.Id;
        await form.ChangeAsync(false);
        Require(Control<Button>(form, "TurnOff").Enabled, "if Wi-Fi remains enabled, OFF can be retried");
        using (var stillOn = form.Icon!.ToBitmap()) Require(IsStateColor(stillOn, true), "a partial disable never displays a red OFF icon");
        Require(Control<Label>(form, "ErrorMessage").Text.Contains("still enabled"), "partial disable explains that internet is not OFF");
        service.IgnoreId = null;
        await form.ChangeAsync(true);

        service.SetGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task change = form.ChangeAsync(false);
        Require(service.LastIds.ToHashSet().SetEquals([ethernet.Id, wifi.Id]) && !service.LastEnabled, "pending OFF includes all enabled adapter GUIDs");
        Require(!selector.Enabled && Control<Button>(form, "Exit").Enabled, "Exit remains enabled while an adapter change is pending");
        var tray = (NotifyIcon)typeof(MainForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        tray.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(i => i.Text == "Exit NetOn").PerformClick();
        await change;
        Require(form.IsDisposed, "context-menu Exit closes the app even during a pending operation");

        var slowRead = new FakeService { ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var closing = new MainForm(slowRead, new AppSettings { MuteAllSounds = true }, false);
        _ = closing.Handle;
        Task pending = closing.RefreshAsync();
        closing.ExitApp();
        await pending;
        Require(closing.IsDisposed, "Exit also cancels a pending refresh without waiting for a timeout");

        var persisted = new AppSettings();
        var restartService = new FakeService { Snapshot = [ethernet, wifi, spare] };
        var firstSession = new InternetSwitch(restartService, persisted, () => { });
        await firstSession.SetEnabledAsync(false, CancellationToken.None);
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(persisted))!;
        await new InternetSwitch(restartService, reloaded, () => { }).SetEnabledAsync(true, CancellationToken.None);
        Require(restartService.Snapshot.Where(a => a.Enabled).Select(a => a.Id).ToHashSet().SetEquals([ethernet.Id, wifi.Id]), "ON restores the remembered connections after restarting while OFF");

        var fresh = new FakeService { Snapshot = [ethernet, wifi, spare] };
        bool refused = false;
        try { await new InternetSwitch(fresh, new(), () => throw new IOException("Cannot save recovery state")).SetEnabledAsync(false, CancellationToken.None); }
        catch (IOException) { refused = true; }
        Require(refused && fresh.LastIds.Count == 0, "OFF refuses to change adapters if recovery information cannot be saved");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);

    private static void CheckControlPainting(MainForm form)
    {
        var selector = Control<ThemedComboBox>(form, "AdapterSelector");
        void Prepare(System.Windows.Forms.Control control) { _ = control.Handle; foreach (System.Windows.Forms.Control child in control.Controls) Prepare(child); }
        Prepare(form);
        Font original = selector.Font;
        foreach (float size in new[] { 9f, 10f, 14f })
        {
            using var font = new Font("Segoe UI", size);
            selector.Font = font;
            int closed = (int)SendMessage(selector.Handle, 0x154, (IntPtr)(-1), IntPtr.Zero);
            int row = (int)SendMessage(selector.Handle, 0x154, IntPtr.Zero, IntPtr.Zero);
            Require(closed >= selector.Font.Height + 8 && row >= selector.Font.Height + 8 && selector.Height >= closed,
                $"native dropdown face/rows fit {size}pt text at {selector.DeviceDpi} DPI ({closed}/{row}px)");
            selector.Font = original;
        }
        foreach (string theme in new[] { "Dark", "Light" })
        {
            AppTheme.Set(theme);
            var button = Control<ModernButton>(form, "TurnOff");
            button.BackColor = AppTheme.Paper;
            foreach (bool enabled in new[] { true, false })
            {
                button.Enabled = enabled;
                using var image = new Bitmap(button.Width, button.Height);
                using var graphics = Graphics.FromImage(image);
                graphics.Clear(Color.Magenta);
                typeof(ModernButton).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [new PaintEventArgs(graphics, button.ClientRectangle)]);
                Require(image.GetPixel(0, 0).ToArgb() == AppTheme.Paper.ToArgb() && image.GetPixel(image.Width - 1, image.Height - 1).ToArgb() == AppTheme.Paper.ToArgb(), $"{theme} button corners match the parent, enabled={enabled}");
            }
            using var about = new AboutForm();
            _ = about.Handle;
            Require(about.BackColor == AppTheme.Paper && about.ForeColor == AppTheme.Ink && about.AcceptButton is ModernButton && about.CancelButton == about.AcceptButton, $"{theme} About window uses app colors and keyboard dismissal");
            var preferences = new AppSettings { Theme = theme };
            int changed = 0;
            using var settings = new SettingsForm(preferences, [new Speech.VoiceChoice("test", "Test voice")], false, _ => { }, () => { AppTheme.Set(preferences.Theme); changed++; }, (_, _) => { }, () => { });
            Prepare(settings);
            CheckSettingsLayout(settings, false);
            var voice = (ComboBox)settings.Controls.Find("VoiceSetting", true).Single();
            var appearance = (ComboBox)settings.Controls.Find("ThemeSetting", true).Single();
            Require(settings.BackColor == AppTheme.Paper && voice.SelectedItem!.ToString() == "Recorded voice - English", $"{theme} Settings window is themed and labels recorded English voice correctly");
            voice.SelectedIndex = 1;
            ((CheckBox)settings.Controls.Find("MuteSetting", true).Single()).Checked = true;
            appearance.SelectedIndex = theme == "Dark" ? 1 : 2;
            Require(preferences.VoiceId == "test" && preferences.MuteAllSounds && preferences.Theme != theme && changed == 3, "Settings edits update the same preferences and apply immediately");

        }
        using var checkMenu = new ContextMenuStrip { ImageScalingSize = new Size(80, 40) };
        var checkItem = new ToolStripMenuItem("Mute all sounds") { Checked = true };
        checkMenu.Items.Add(checkItem);
        using var tickImage = new Bitmap(100, 60);
        using (var g = Graphics.FromImage(tickImage))
        {
            g.Clear(Color.Transparent);
            var args = new ToolStripItemImageRenderEventArgs(g, checkItem, new Rectangle(0, 0, 80, 40));
            typeof(ThemedMenuRenderer).GetMethod("OnRenderItemCheck", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(new ThemedMenuRenderer(), [args]);
        }
        var points = new List<Point>();
        for (int x = 0; x < tickImage.Width; x++) for (int y = 0; y < tickImage.Height; y++) if (tickImage.GetPixel(x, y).A > 100) points.Add(new Point(x, y));
        int tickWidth = points.Max(p => p.X) - points.Min(p => p.X);
        Require(tickWidth <= 16 * checkMenu.DeviceDpi / 96f && Math.Abs((points.Max(p => p.X) + points.Min(p => p.X)) / 2f - 40) < 5, "menu tick stays compact and centered in a wide image column");
        form.ApplyTheme();
    }

    private static void CheckSettingsLayout(SettingsForm settings, bool paintStates = true)
    {
        void Prepare(System.Windows.Forms.Control c) { _ = c.Handle; c.PerformLayout(); foreach (System.Windows.Forms.Control child in c.Controls) Prepare(child); }
        foreach (Size size in new[] { settings.ClientSize, new Size((int)(520 * settings.DeviceDpi / 96f), (int)(460 * settings.DeviceDpi / 96f)) })
        {
            Size original = settings.ClientSize;
            settings.ClientSize = size;
            Prepare(settings);
            var notice = settings.Controls.Find("SettingsSavedNotice", true).Single();
            var done = settings.Controls.Find("SettingsDone", true).Single();
            var footer = notice.Parent!;
            Require(notice.Height >= notice.GetPreferredSize(new Size(notice.Width, 0)).Height && notice.Bottom <= done.Top && footer.ClientRectangle.Contains(done.Bounds), $"settings notice and Done fit without overlap at {settings.DeviceDpi} DPI, height {settings.ClientSize.Height}");
            settings.ClientSize = original;
            Prepare(settings);
        }
        if (!paintStates) return;
        foreach (string name in new[] { "StartupSetting", "MuteSetting" })
        {
            var check = (ThemedCheckBox)settings.Controls.Find(name, true).Single();
            bool original = check.Checked;
            foreach (bool value in new[] { false, true })
            {
                check.Checked = value;
                using var image = new Bitmap(check.Width, check.Height);
                check.DrawToBitmap(image, check.ClientRectangle);
                int x = (int)(4 * check.DeviceDpi / 96f), y = check.Height / 2;
                Color expected = value ? AppTheme.Green : AppTheme.Surface;
                int matching = 0;
                for (int px = 0; px < Math.Min(image.Width, (int)Math.Ceiling(18 * check.DeviceDpi / 96f)); px++)
                for (int py = 0; py < image.Height; py++)
                    if (image.GetPixel(px, py).ToArgb() == expected.ToArgb()) matching++;
                Require(matching > 20 * check.DeviceDpi / 96f, $"{name} uses theme colors, checked={value}");
            }
            check.Checked = original;
        }
    }

    private static void CheckSchedules()
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = new NetworkSchedule();
        Require(NetworkSchedule.Options.Select(o => o.Delay.TotalMinutes).SequenceEqual(new double[] { 10, 30, 60, 120, 240, 480, 1440 }), "schedule intervals match the requested list");
        foreach (var option in NetworkSchedule.Options)
        foreach (bool enable in new[] { true, false })
        {
            schedule.Set(enable, option.Delay, now);
            Require(schedule.TakeDue(now + option.Delay - TimeSpan.FromSeconds(1)) is null && schedule.TakeDue(now + option.Delay) == enable && schedule.TakeDue(now.AddDays(2)) is null, $"{option.Label} {(enable ? "ON" : "OFF")} fires at its deadline only once");
        }
        schedule.Set(false, TimeSpan.FromHours(1), now);
        schedule.Set(true, TimeSpan.FromMinutes(10), now);
        Require(schedule.TakeDue(now.AddMinutes(10)) == true, "new schedule replaces the previous schedule");
        schedule.Set(false, TimeSpan.FromHours(24), now);
        Require(schedule.TakeDue(now.AddHours(26)) == false, "schedule uses elapsed time and handles sleep past the deadline");
    }

    private static async Task CheckPowerShellAsync()
    {
        var id = Guid.NewGuid();
        string stub = $"function Get-NetAdapter {{ [CmdletBinding()]param([switch]$IncludeHidden); Write-Progress -Activity 'Loading' -Status 'Modules'; [pscustomobject]@{{InterfaceGuid='{id:D}'}} }}; " +
            "function Enable-NetAdapter { [CmdletBinding()]param([Parameter(ValueFromPipeline=$true)]$InputObject,[switch]$Confirm); process { Write-Progress -Activity 'Enabling' -Status 'Complete' } }; ";
        var success = await NetworkAdapters.RunPowerShellAsync(stub + NetworkAdapters.BuildCommand([id], true), CancellationToken.None);
        Require(success.ExitCode == 0 && string.IsNullOrWhiteSpace(success.Error), "real PowerShell runner suppresses progress without changing any adapter");
        string failStub = stub.Replace("Write-Progress -Activity 'Enabling' -Status 'Complete'", "throw 'Adapter access denied'");
        var failure = await NetworkAdapters.RunPowerShellAsync(failStub + NetworkAdapters.BuildCommand([id], true), CancellationToken.None);
        Require(failure.ExitCode != 0 && failure.Error.Contains("Adapter access denied") && !failure.Error.Contains("CLIXML"), "real PowerShell failures return plain actionable text");
        var enabled = new Adapter("Test", id, "Test", "Connected", 1);
        await NetworkAdapters.ConfirmChangeAsync([id], true, failure, () => [enabled], CancellationToken.None);
        Require(true, "confirmed adapter state takes precedence over a helper error");
        bool rejected = false;
        try { await NetworkAdapters.ConfirmChangeAsync([id], true, failure, () => [enabled with { AdminStatus = 2 }], CancellationToken.None); }
        catch (InvalidOperationException ex) { rejected = ex.Message.Contains("Adapter access denied"); }
        Require(rejected, "failed adapter state still reports a genuine helper error");
        const string progress = "#< CLIXML\n<Objs xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"><Obj S=\"progress\"><TN><T>System.Management.Automation.PSCustomObject</T></TN></Obj></Objs>";
        Require(!NetworkAdapters.FailureMessage(progress).Contains("<"), "serialized progress cannot leak into the UI");
    }

    private sealed class FakeService : IAdapterService
    {
        public List<Adapter> Snapshot = [];
        public TaskCompletionSource<List<Adapter>>? ReadGate;
        public TaskCompletionSource? SetGate;
        public Exception? ReadError;
        public List<Guid> LastIds = [];
        public Guid? IgnoreId;
        public bool LastEnabled;
        public async Task<List<Adapter>> ReadAsync(CancellationToken token)
        {
            if (ReadError != null) throw ReadError;
            return ReadGate != null ? await ReadGate.Task.WaitAsync(token) : Snapshot;
        }
        public async Task SetEnabledAsync(IReadOnlyCollection<Guid> ids, bool enabled, CancellationToken token)
        {
            LastIds = ids.ToList(); LastEnabled = enabled;
            if (SetGate != null) await SetGate.Task.WaitAsync(token);
            Snapshot = Snapshot.Select(a => ids.Contains(a.Id) && a.Id != IgnoreId ? a with { AdminStatus = enabled ? 1 : 2, Status = enabled ? "Disconnected" : "Disabled" } : a).ToList();
        }
    }
}
