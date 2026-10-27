using System.Reflection;
using Voltura.NetOn;

internal static class Program
{
    private static int failures;
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
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
        bool renderUi = args.Length == 2 && args[0] == "--render-ui";
        if (!renderUi) { CheckLiveAdapters(); CheckIcons(); CheckInstances(); CheckPronunciation(); }
        using var context = new ApplicationContext();
        using var start = new System.Windows.Forms.Timer { Interval = 1 };
        start.Tick += async (_, _) =>
        {
            start.Stop();
            try { if (renderUi) await RenderUiAsync(args[1]); else await CheckFormAsync(); }
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
            Require(rate == Speech.SampleRate, "cue and speech share the expected PCM sample rate");
            int cueBytes = rate * 2 * Speech.CueMilliseconds / 1000;
            var cue = wave.Skip(44).Take(cueBytes).ToArray();
            Require(cue.Take(rate * 2 / 2).Any(b => b != 0), text + " begins with real audio, not discarded digital silence");
            Require(cue.Skip(cueBytes - rate / 5).Any(b => b != 0), "wake-up cue continues until the speech lead-in");
            Require(wave.Skip(44 + cueBytes).Any(b => b != 0), "complete phrase follows in the same WAV stream");
            Require(wave.Length > 44 + cueBytes + rate, "speech has at least half a second of audio after the cue");
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
        using var form = new MainForm(service, new AppSettings { MuteAllSounds = true }, false);
        _ = form.Handle;
        void Prepare(System.Windows.Forms.Control control) { _ = control.Handle; control.PerformLayout(); foreach (System.Windows.Forms.Control child in control.Controls) Prepare(child); }
        Prepare(form);
        await form.RefreshAsync();
        Prepare(form);
        using (var on = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(on, new Rectangle(Point.Empty, form.Size)); on.Save(Path.Combine(directory, "neton-app-on.png")); }
        await form.ChangeAsync(false);
        Prepare(form);
        using (var off = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(off, new Rectangle(Point.Empty, form.Size)); off.Save(Path.Combine(directory, "neton-app-off.png")); }
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
        using var form = new MainForm(service, new AppSettings { MuteAllSounds = true }, desktopFeatures: false);
        _ = form.Handle;
        await form.RefreshAsync();
        var selector = Control<ComboBox>(form, "AdapterSelector");
        Require(!form.Visible, "startup remains in the tray without opening a window");
        Require(selector.Items.Count == 3 && ((Adapter)selector.SelectedItem!).Id == ethernet.Id, "connected adapter is selected initially");
        Require(Control<Button>(form, "TurnOff").Enabled, "OFF control enables after successful discovery");

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
