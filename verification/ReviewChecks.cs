using Voltura.NetOn;
using NAudio.Wave;
using System.Reflection;

internal static class ReviewChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    internal static async Task RunAsync()
    {
        CheckSettingsFiles();
        await CheckRecoveryAsync();
        await CheckStaleUiAsync();
        CheckSettingsFailures();
        await CheckCopyAndBrowserAsync();
        CheckErrorLayout();
        CheckAudioShutdown();
        Check(Speech.RenderWave("Internet enabled", "removed-voice").AsSpan(44).SequenceEqual(RecordedVoice.ReadPcm("Internet enabled")),
            "a removed Windows voice falls back to the recorded announcement");
        using var speech = new Speech();
        speech.Dispose(); speech.Dispose(); speech.Say("Internet enabled"); speech.SetMuted(true);
        Check(true, "unused speech resources dispose safely and reject later announcements");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool canceled = false;
        int starts = 0;
        try { await NetworkAdapters.RunPowerShellAsync("throw 'Must not start'", cancellation.Token, _ => { starts++; throw new Exception("Canceled helper was started"); }); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled && starts == 0, "a canceled adapter request never starts its PowerShell helper");
    }

    private static void CheckErrorLayout()
    {
        using var form = new MainForm(new ReviewService(), new AppSettings { MuteAllSounds = true }, false);
        var message = Find<Label>(form, "ErrorMessage");
        message.Text = string.Join("\n", Enumerable.Repeat("Windows could not change this adapter. Refresh and try again.", 30));
        Prepare(form);
        var body = (ScrollableControl)message.Parent!.Parent!;
        int bottom = body.PointToClient(message.PointToScreen(new Point(0, message.Height))).Y;
        Console.WriteLine($"Error layout: bottom={bottom}, viewport={body.ClientSize.Height}, scroll extent={body.DisplayRectangle.Height}, scroll={body.AutoScroll}");
        Check(bottom <= body.ClientSize.Height || (body.AutoScroll && bottom <= body.DisplayRectangle.Bottom),
            "long network errors remain reachable by scrolling without covering the footer");
        static void Prepare(Control control)
        {
            _ = control.Handle;
            control.PerformLayout();
            foreach (Control child in control.Controls) Prepare(child);
        }
    }

    private static async Task CheckCopyAndBrowserAsync()
    {
        int lookups = 0;
        string copied = "";
        var service = new ReviewService { Snapshot = [new Adapter("Ethernet", Guid.NewGuid(), "Test", "Connected", 1)] };
        using var form = new MainForm(service, new AppSettings { MuteAllSounds = true }, false,
            _ => ++lookups == 1 ? Task.FromResult("203.0.113.20") : throw new HttpRequestException("Service unavailable"), value => copied = value);
        _ = form.Handle;
        await form.RefreshAsync();
        await form.ReadExternalIpAsync();
        await form.CopyExternalIpAsync();
        Check(copied == "203.0.113.20" && lookups == 1, "copying the displayed IP succeeds without depending on another network request");
        using var link = new LinkLabel { Text = "Visit the NetOn website" };
        AboutForm.OpenWebsite(link, () => throw new System.ComponentModel.Win32Exception("No default browser"));
        Check(link.Text.Contains("neton.voltura.se"), "a browser-launch failure leaves a usable website address without crashing About");
    }

    private static void CheckAudioShutdown()
    {
        using var failedPlayer = new SilentPlayer { FailDisposal = true };
        using (var speech = new Speech(createOutput: () => failedPlayer))
        {
            speech.SetMuted(false);
            Check(failedPlayer.Started.Wait(2000), "isolated audio worker starts without using a sound device");
            speech.SetMuted(true);
            Check(failedPlayer.Disposed.Wait(2000), "muting survives an audio-driver disposal failure");
            speech.Dispose();
            Check(Worker(speech).Join(2000), "audio-driver cleanup failure does not prevent worker shutdown");
        }
        for (int i = 0; i < 50; i++)
        {
            using var player = new SilentPlayer();
            using var speech = new Speech(createOutput: () => player);
            speech.SetMuted(false);
            if (!player.Started.Wait(2000)) throw new Exception("Audio worker failed to start");
            speech.Dispose();
            if (!Worker(speech).Join(2000)) throw new Exception("Audio shutdown stalled");
        }
        Check(true, "50 active-worker shutdowns safely finish collection/event cleanup without racing Dispose");
        static Thread Worker(Speech speech) => (Thread)typeof(Speech).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(speech)!;
    }

    private sealed class SilentPlayer : IWavePlayer, IWavePosition
    {
        internal readonly ManualResetEventSlim Started = new();
        internal readonly ManualResetEventSlim Disposed = new();
        internal bool FailDisposal;
        public PlaybackState PlaybackState { get; private set; }
        public WaveFormat OutputWaveFormat { get; } = new(22050, 16, 1);
        public float Volume { get; set; } = 1;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public void Init(IWaveProvider provider) { }
        public void Play() { PlaybackState = PlaybackState.Playing; Started.Set(); }
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public long GetPosition() => 0;
        public void Dispose()
        {
            PlaybackState = PlaybackState.Stopped;
            Disposed.Set();
            if (FailDisposal) { FailDisposal = false; throw new InvalidOperationException("Simulated audio driver failure"); }
        }
    }

    private static void CheckSettingsFiles()
    {
        var normalized = AppSettings.Parse("{\"RestoreAdapterIds\":null,\"Theme\":null,\"VoiceId\":\" \"}");
        Check(normalized.RestoreAdapterIds.Count == 0 && normalized.Theme == "System" && normalized.VoiceId is null,
            "null settings values normalize without breaking adapter recovery");
        Guid id = Guid.NewGuid();
        normalized.RestoreAdapterIds = [Guid.Empty, id, id];
        normalized.Normalize();
        Check(normalized.RestoreAdapterIds.SequenceEqual([id]) && normalized.OffRequested, "recovery IDs are valid, distinct, and treated as pending restoration");
        string directory = Path.Combine(Path.GetTempPath(), "NetOn-Review-" + Guid.NewGuid().ToString("N"));
        normalized.SaveToDirectory(directory);
        normalized.MuteAllSounds = true;
        normalized.SaveToDirectory(directory);
        string path = Path.Combine(directory, "preferences.json");
        string original = File.ReadAllText(path);
        bool refused = false;
        using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { normalized.SaveToDirectory(directory); }
            catch (IOException) { refused = true; }
        }
        Check(refused && File.ReadAllText(path) == original && !Directory.EnumerateFiles(directory, "*.tmp").Any(),
            "failed atomic settings save preserves the last valid file and cleans the temporary file");
        File.WriteAllText(path, "{");
        var recovered = AppSettings.LoadFromDirectory(directory);
        Check(recovered.OffRequested && recovered.RestoreAdapterIds.SequenceEqual([id]), "corrupt settings recover adapter restoration data from the backup");
        foreach (string name in new[] { "preferences.json", "preferences.json.bak" }) File.Delete(Path.Combine(directory, name));
        Directory.Delete(directory);
    }

    private static async Task CheckRecoveryAsync()
    {
        var active = new Adapter("Wi-Fi", Guid.NewGuid(), "Test wireless", "Connected", 1);
        var spare = new Adapter("Ethernet", Guid.NewGuid(), "Test wired", "Disabled", 2);
        var service = new ReviewService { Snapshot = [active, spare] };
        var settings = new AppSettings();
        bool failed = false;
        try { await new InternetSwitch(service, settings, () => throw new IOException("Disk full")).SetEnabledAsync(false, CancellationToken.None); }
        catch (IOException) { failed = true; }
        Check(failed && service.SetCalls == 0 && !settings.OffRequested && settings.RestoreAdapterIds.Count == 0,
            "failed OFF persistence rolls back in-memory recovery data before touching adapters");
        await new InternetSwitch(service, settings, () => { }).SetEnabledAsync(true, CancellationToken.None);
        Check(service.SetCalls == 0 && !service.Snapshot.Single(a => a.Id == spare.Id).Enabled, "an already-ON request leaves previously disabled adapters alone");
        var controller = new InternetSwitch(service, settings, () => { });
        await controller.SetEnabledAsync(false, CancellationToken.None);
        failed = false;
        try { await new InternetSwitch(service, settings, () => throw new IOException("Disk full")).SetEnabledAsync(true, CancellationToken.None); }
        catch (IOException) { failed = true; }
        Check(failed && settings.OffRequested && settings.RestoreAdapterIds.SequenceEqual([active.Id]), "failed ON persistence retains recovery information for retry");
        await controller.SetEnabledAsync(true, CancellationToken.None);
        Check(!settings.OffRequested && settings.RestoreAdapterIds.Count == 0, "ON can complete recovery after a persistence failure");
        using var cancellation = new CancellationTokenSource();
        service.ReadAction = () => cancellation.Cancel();
        int calls = service.SetCalls;
        bool canceled = false;
        try { await controller.SetEnabledAsync(false, cancellation.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled && service.SetCalls == calls && !settings.OffRequested, "canceling after discovery does not save OFF state or change adapters");
    }

    private static async Task CheckStaleUiAsync()
    {
        var active = new Adapter("Wi-Fi", Guid.NewGuid(), "Test wireless", "Connected", 1);
        var spare = new Adapter("Ethernet", Guid.NewGuid(), "Test wired", "Disabled", 2);
        var service = new ReviewService { Snapshot = [active, spare] };
        var preferences = new AppSettings { AdapterId = Guid.NewGuid(), MuteAllSounds = true };
        var pendingIp = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        using var form = new MainForm(service, preferences, false, _ => ++reads switch
        {
            1 => pendingIp.Task, 2 => Task.FromResult("203.0.113.2"), _ => Task.FromResult("not an IP")
        });
        _ = form.Handle;
        await form.RefreshAsync();
        var selector = Find<ComboBox>(form, "AdapterSelector");
        Check(selector.SelectedItem is Adapter selected && selected.Id == active.Id, "an unavailable saved adapter falls back to a connected adapter");
        Task oldIp = form.ReadExternalIpAsync();
        await form.ChangeAsync(false);
        await form.ChangeAsync(true);
        pendingIp.SetResult("203.0.113.1");
        await oldIp;
        Check(!Find<Label>(form, "ExternalIp").Text.Contains("203.0.113.1"), "an IP response from before OFF/ON cannot replace the new connection's address");
        await form.ReadExternalIpAsync();
        Check(Find<Label>(form, "ExternalIp").Text.Contains("203.0.113.2"), "IP lookup recovers on the current connection");
        await form.ReadExternalIpAsync();
        Check(!Find<Button>(form, "CopyExternalIp").Enabled && !Find<Label>(form, "ExternalIp").Text.Contains("203.0.113.2"), "an invalid IP response clears the stale address and copy action");

        service.Snapshot = [active, spare with { AdminStatus = 1 }];
        await form.RefreshAsync();
        service.IgnoreId = active.Id;
        await form.ChangeAsync(false);
        string failure = Find<Label>(form, "ErrorMessage").Text;
        await form.RefreshAsync();
        Check(failure.Length > 0 && Find<Label>(form, "ErrorMessage").Text == failure, "background refresh does not erase an actionable toggle error");
        await form.RefreshAsync(clearErrors: true);
        Check(Find<Label>(form, "ErrorMessage").Text.Length == 0, "explicit refresh can dismiss an operation error");
        service.IgnoreId = null;
        service.ReadError = new IOException("Discovery unavailable");
        await form.RefreshAsync();
        service.ReadError = null;
        await form.RefreshAsync();
        Check(Find<Label>(form, "ErrorMessage").Text.Length == 0, "a recovered discovery error clears automatically");

        var scheduleClock = new ManualClock();
        using var staleOff = new MainForm(service, new AppSettings { MuteAllSounds = true }, false, timeProvider: scheduleClock);
        service.Snapshot = [active with { AdminStatus = 2 }];
        _ = staleOff.Handle;
        await staleOff.RefreshAsync();
        service.Snapshot = [active];
        staleOff.ScheduleChange(false, TimeSpan.FromMinutes(10));
        scheduleClock.Advance(TimeSpan.FromMinutes(10));
        await staleOff.RunScheduledChangeAsync();
        Check(service.Snapshot.All(a => !a.Enabled), "scheduled OFF reads fresh state even when the cached view already says OFF");
        service.Snapshot = [active];
        using var staleOn = new MainForm(service, new AppSettings { MuteAllSounds = true }, false, timeProvider: scheduleClock);
        _ = staleOn.Handle;
        await staleOn.RefreshAsync();
        service.Snapshot = [active with { AdminStatus = 2 }];
        staleOn.ScheduleChange(true, TimeSpan.FromMinutes(10));
        scheduleClock.Advance(TimeSpan.FromMinutes(10));
        await staleOn.RunScheduledChangeAsync();
        Check(service.Snapshot.All(a => a.Enabled), "scheduled ON reads fresh state even when the cached view already says ON");
    }

    private static T Find<T>(Control form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    private static void CheckSettingsFailures()
    {
        using var settings = new SettingsForm(new AppSettings(), [], false,
            _ => throw new UnauthorizedAccessException(), () => throw new IOException(), (_, _) => { }, () => { });
        _ = settings.Handle;
        var startup = Find<CheckBox>(settings, "StartupSetting");
        startup.Checked = true;
        Check(!startup.Checked && Find<Label>(settings, "SettingsSavedNotice").Text.Contains("Could not change"), "failed startup setting rolls back the checkbox and reports failure");
        Find<CheckBox>(settings, "MuteSetting").Checked = true;
        Check(Find<Label>(settings, "SettingsSavedNotice").Text.Contains("could not be saved"), "settings window does not claim unsaved changes were saved");
    }

    private sealed class ReviewService : IAdapterService
    {
        internal List<Adapter> Snapshot = [];
        internal Action? ReadAction;
        internal Exception? ReadError;
        internal Guid? IgnoreId;
        internal int SetCalls;
        public Task<List<Adapter>> ReadAsync(CancellationToken token)
        {
            if (ReadError is not null) throw ReadError;
            ReadAction?.Invoke();
            return Task.FromResult(Snapshot.ToList());
        }
        public Task SetEnabledAsync(IReadOnlyCollection<Guid> ids, bool enabled, CancellationToken token)
        {
            SetCalls++;
            Snapshot = Snapshot.Select(a => ids.Contains(a.Id) && a.Id != IgnoreId ? a with { AdminStatus = enabled ? 1 : 2 } : a).ToList();
            return Task.CompletedTask;
        }
    }
}
