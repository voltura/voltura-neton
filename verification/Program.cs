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
            using var icon = StatusIcons.Create(true, new AppSettings(), 256);
            using var file = File.Create(args[1]);
            icon.Save(file);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--render-speech")
        {
            RenderSpeech(args[1]);
            return 0;
        }
        CheckLiveAdapters();
        CheckIcons();
        CheckInstances();
        using var context = new ApplicationContext();
        using var start = new System.Windows.Forms.Timer { Interval = 1 };
        start.Tick += async (_, _) =>
        {
            start.Stop();
            try { await CheckFormAsync(); }
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

    private static void RenderSpeech(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (string text in new[] { "Internet enabled", "Internet disabled" })
        {
            dynamic voice = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!)!;
            dynamic stream = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream")!)!;
            string path = Path.Combine(directory, text.Replace(' ', '-') + ".wav");
            try
            {
                stream.Format.Type = 22; // 22.05 kHz, 16-bit mono.
                stream.Open(path, 3, false);
                voice.AudioOutputStream = stream;
                voice.Speak(Speech.BuildPrompt(text), 8);
                stream.Close();
                using var input = new BinaryReader(File.OpenRead(path));
                input.ReadBytes(12);
                int rate = 0;
                byte[]? samples = null;
                while (input.BaseStream.Position + 8 <= input.BaseStream.Length)
                {
                    string chunk = System.Text.Encoding.ASCII.GetString(input.ReadBytes(4));
                    int size = input.ReadInt32();
                    long next = input.BaseStream.Position + size + (size & 1);
                    if (chunk == "fmt ") { input.ReadInt16(); input.ReadInt16(); rate = input.ReadInt32(); }
                    if (chunk == "data") samples = input.ReadBytes(size);
                    input.BaseStream.Position = next;
                }
                if (samples == null || rate == 0) throw new Exception("Speech output was not a PCM WAV");
                int initialBytes = rate * 2 * 600 / 1000;
                Require(samples.Length > initialBytes && samples.Take(initialBytes).All(b => b == 0), text + " starts with at least 600 ms of audio-stream silence");
                Require(samples.Skip(initialBytes).Any(b => b != 0), text + " is synthesized after the lead-in (without playing audio)");
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(voice);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(stream);
            }
        }
    }

    private static void CheckIcons()
    {
        var settings = new AppSettings();
        using var on = StatusIcons.Draw(true, settings, 64);
        using var off = StatusIcons.Draw(false, settings, 64);
        Require(on.GetPixel(5, 5).ToArgb() == Color.Green.ToArgb(), "ON icon is green");
        Require(off.GetPixel(5, 5).ToArgb() == Color.Red.ToArgb(), "OFF icon is red");
        using var icon = StatusIcons.Create(true, settings);
        using var bitmap = icon.ToBitmap();
        Require(bitmap.GetPixel(5, 5).ToArgb() == Color.Green.ToArgb(), "owned tray icon retains its color after native handle cleanup");
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
        using (var red = form.Icon!.ToBitmap()) Require(red.GetPixel(5, 5).ToArgb() == Color.Red.ToArgb(), "successful OFF switches the displayed icon to red");
        await form.ChangeAsync(true);
        Require(service.Snapshot.Single(a => a.Id == spare.Id).Enabled == false, "ON leaves previously disabled adapters disabled");
        Require(service.LastIds.ToHashSet().SetEquals([ethernet.Id, wifi.Id]), "ON restores exactly the previously enabled adapters");
        Require(Control<Button>(form, "TurnOff").Enabled && !Control<Button>(form, "TurnOn").Enabled, "successful ON switches the controls to allow OFF");
        using (var green = form.Icon!.ToBitmap()) Require(green.GetPixel(5, 5).ToArgb() == Color.Green.ToArgb(), "successful ON switches the displayed icon to green");

        service.IgnoreId = wifi.Id;
        await form.ChangeAsync(false);
        Require(Control<Button>(form, "TurnOff").Enabled, "if Wi-Fi remains enabled, OFF can be retried");
        using (var stillOn = form.Icon!.ToBitmap()) Require(stillOn.GetPixel(5, 5).ToArgb() == Color.Green.ToArgb(), "a partial disable never displays a red OFF icon");
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
