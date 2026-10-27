using System.Reflection;
using Voltura.NetOn;

internal static class AdditionalChecks
{
    internal static async Task RunAsync()
    {
        int failures = 0;
        await Case("open About follows an appearance change", AboutThemeAsync);
        await Case("settings checkbox follows a tray mute change", SettingsSyncAsync);
        await Case("open adapter list does not freeze connection status", DropdownRefreshAsync);
        await Case("successful startup retry clears its failure notice", StartupRetryAsync);
        await Case("appearance applies consistently even if saving fails", FailedThemeSaveAsync);
        await Case("opening an existing window preserves maximized state", MaximizedStateAsync);
        await Case("audio device failure does not retry continuously while idle", AudioFailureAsync);
        await Case("scheduling uses elapsed time despite wall-clock corrections", ScheduleClockAsync);
        await Case("framework-host elevation and startup retain the entry assembly", FrameworkHostAsync);
        await Case("single-instance cleanup is safe when called twice", DoubleDisposeAsync);
        await Case("non-cancellation elevation failure is reported without crashing", ElevationFailureAsync);
        await Case("unavailable user registry does not prevent unrelated app controls", RegistryReadAsync);
        if (failures > 0) throw new Exception($"Additional bug regressions failed: {failures}");
        async Task Case(string name, Func<Task> check)
        {
            try { await check(); Console.WriteLine("PASS NEW: " + name); }
            catch (Exception ex) { failures++; Console.WriteLine($"FAIL NEW: {name}: {ex.Message}"); }
        }
    }

    private static Task RegistryReadAsync()
    {
        Require(!AppTheme.ResolveDark("System", () => throw new UnauthorizedAccessException("Theme policy")), "denied theme registry read escaped instead of using the light fallback");
        Require(!AppSettings.ReadStartWithWindows(() => throw new UnauthorizedAccessException("Startup policy")), "denied startup registry read escaped into main-window construction");
        Require(AppTheme.ResolveDark("Dark", () => throw new Exception("Must not read")), "explicit dark mode depended on registry access");
        return Task.CompletedTask;
    }

    private static Task FrameworkHostAsync()
    {
        const string host = @"C:\Program Files\dotnet\dotnet.exe";
        const string assembly = @"C:\NetOn app\Voltura.NetOn.dll";
        var elevated = AppLaunch.Elevated(host, assembly);
        Require(elevated.ArgumentList.SequenceEqual([assembly]), "elevation launched dotnet.exe without the NetOn assembly");
        Require(AppLaunch.StartupCommand(host, assembly) == $"\"{host}\" \"{assembly}\"", "startup registration lost the framework-host argument");
        var apphost = AppLaunch.Elevated(@"C:\NetOn app\Voltura.NetOn.exe", assembly);
        Require(apphost.ArgumentList.Count == 0, "the normal apphost must not receive a DLL argument");
        return Task.CompletedTask;
    }

    private static Task DoubleDisposeAsync()
    {
        var instance = new SingleInstance("Local\\NetOn.AdditionalDispose." + Guid.NewGuid());
        instance.Dispose();
        instance.Dispose();
        return Task.CompletedTask;
    }

    private static Task ElevationFailureAsync()
    {
        Exception? reported = null;
        int status = AppLaunch.Elevate(_ => throw new System.ComponentModel.Win32Exception(5), ex => reported = ex);
        Require(status != 0 && reported is System.ComponentModel.Win32Exception, "access-denied elevation failure was not reported");
        reported = null;
        status = AppLaunch.Elevate(_ => throw new System.ComponentModel.Win32Exception(1223), ex => reported = ex);
        Require(status == 0 && reported is null, "canceling UAC should remain a quiet cancellation");
        return Task.CompletedTask;
    }

    private static Task ScheduleClockAsync()
    {
        var clock = new ManualClock();
        var schedule = new NetworkSchedule(clock);
        schedule.Set(false, TimeSpan.FromMinutes(10));
        clock.Advance(TimeSpan.FromMinutes(1));
        clock.JumpClock(TimeSpan.FromHours(2));
        Require(schedule.TakeDue() is null, "moving the clock forward executed the 10-minute timer after only 1 minute");
        clock.JumpClock(TimeSpan.FromHours(-4));
        clock.Advance(TimeSpan.FromMinutes(9));
        Require(schedule.TakeDue() == false, "moving the clock backward postponed an already elapsed timer");
        return Task.CompletedTask;
    }

    private static Task FailedThemeSaveAsync()
    {
        var preferences = new AppSettings { Theme = "Light", MuteAllSounds = true };
        using var main = new MainForm(new Service(), preferences, false, savePreferences: () => throw new IOException("Disk full"));
        using var settings = main.CreateSettingsForm();
        Find<ComboBox>(settings, "ThemeSetting").SelectedIndex = 2;
        Require(AppTheme.Dark && settings.BackColor == AppTheme.Paper && main.BackColor == AppTheme.Paper,
            "failed persistence left the main window light while settings/global colors became dark");
        Require(Find<Label>(settings, "SettingsSavedNotice").Text.Contains("could not be saved"), "save failure must remain visible");
        return Task.CompletedTask;
    }

    private static Task MaximizedStateAsync()
    {
        using var main = new MainForm(new Service(), new AppSettings { MuteAllSounds = true }, false);
        main.WindowState = FormWindowState.Maximized;
        main.RestoreIfMinimized();
        Require(main.WindowState == FormWindowState.Maximized, "bringing the window forward needlessly restored it from maximized");
        main.WindowState = FormWindowState.Minimized;
        main.RestoreIfMinimized();
        Require(main.WindowState == FormWindowState.Maximized, "a maximized window lost its previous state after minimization");
        main.WindowState = FormWindowState.Normal;
        main.RestoreIfMinimized();
        main.WindowState = FormWindowState.Minimized;
        main.RestoreIfMinimized();
        Require(main.WindowState == FormWindowState.Normal, "a minimized window was not restored");
        return Task.CompletedTask;
    }

    private static async Task AudioFailureAsync()
    {
        int attempts = 0;
        using var attempted = new ManualResetEventSlim();
        using var speech = new Speech(createOutput: () => { Interlocked.Increment(ref attempts); attempted.Set(); throw new InvalidOperationException("Audio device unavailable"); });
        speech.SetMuted(false);
        Require(attempted.Wait(2000), "audio failure was not exercised");
        await Task.Delay(800);
        int idleAttempts = Volatile.Read(ref attempts);
        speech.Say("Internet enabled");
        await Task.Delay(400);
        int retryAttempts = Volatile.Read(ref attempts);
        speech.Dispose();
        var worker = (Thread)typeof(Speech).GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(speech)!;
        Require(worker.Join(2000), "failed audio worker did not stop");
        Require(idleAttempts == 1 && retryAttempts == 2, $"audio retry attempts: idle={idleAttempts}, after next request={retryAttempts}");
    }

    private static Task StartupRetryAsync()
    {
        bool fail = true;
        using var settings = new SettingsForm(new AppSettings { Theme = "Light" }, [], false,
            _ => { if (fail) throw new IOException("Startup entry denied"); }, () => { }, (_, _) => { }, () => { });
        var startup = Find<CheckBox>(settings, "StartupSetting");
        startup.Checked = true;
        Require(!startup.Checked, "first failure did not roll back");
        fail = false;
        startup.Checked = true;
        Require(Find<Label>(settings, "SettingsSavedNotice").Text == "Changes are saved automatically.", "the old failure notice remained after the successful retry");
        return Task.CompletedTask;
    }

    private static Task AboutThemeAsync()
    {
        var preferences = new AppSettings { Theme = "Light", MuteAllSounds = true };
        using var main = new MainForm(new Service(), preferences, false);
        using var about = new AboutForm { Owner = main };
        _ = main.Handle; _ = about.Handle;
        preferences.Theme = "Dark";
        main.ApplyTheme();
        Require(about.BackColor == AppTheme.Paper, "About retained its light background after the owner switched to dark");
        return Task.CompletedTask;
    }

    private static Task SettingsSyncAsync()
    {
        var preferences = new AppSettings { Theme = "Light" };
        using var main = new MainForm(new Service(), preferences, false);
        using var settings = new SettingsForm(preferences, [], false, _ => { }, () => { }, (_, _) => { }, () => { });
        typeof(MainForm).GetField("settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, settings);
        _ = main.Handle; _ = settings.Handle;
        var tray = (NotifyIcon)typeof(MainForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var menu = (ToolStripMenuItem)tray.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(i => i.Text == "Settings");
        menu.DropDownItems.Cast<ToolStripItem>().Single(i => i.Text == "Mute all sounds").PerformClick();
        Require(preferences.MuteAllSounds && Find<CheckBox>(settings, "MuteSetting").Checked, "tray changed mute but the open settings checkbox stayed unchecked");
        return Task.CompletedTask;
    }

    private static async Task DropdownRefreshAsync()
    {
        var adapter = new Adapter("Ethernet", Guid.NewGuid(), "Test", "Connected", 1);
        var service = new Service { Snapshot = [adapter] };
        bool open = false;
        using var main = new MainForm(service, new AppSettings { MuteAllSounds = true }, false, adapterListOpen: () => open);
        _ = main.Handle;
        await main.RefreshAsync();
        open = true;
        service.Snapshot = [adapter with { AdminStatus = 2, Status = "Disabled" }];
        await main.RefreshAsync();
        Require(Find<Button>(main, "TurnOn").Enabled && !Find<Button>(main, "TurnOff").Enabled,
            "the cached ON state was retained while the adapter list was open");
    }

    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Service : IAdapterService
    {
        internal List<Adapter> Snapshot = [];
        public Task<List<Adapter>> ReadAsync(CancellationToken token) => Task.FromResult(Snapshot.ToList());
        public Task SetEnabledAsync(IReadOnlyCollection<Guid> ids, bool enabled, CancellationToken token)
        {
            Snapshot = Snapshot.Select(a => ids.Contains(a.Id) ? a with { AdminStatus = enabled ? 1 : 2 } : a).ToList();
            return Task.CompletedTask;
        }
    }
}
