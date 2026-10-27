using System.Text.Json;
using Microsoft.Win32;

namespace Voltura.NetOn;

internal sealed class AppSettings
{
    public Guid? AdapterId { get; set; }
    public List<Guid> RestoreAdapterIds { get; set; } = [];
    public bool OffRequested { get; set; }
    public bool MuteAllSounds { get; set; }
    public string? VoiceId { get; set; }
    public string Theme { get; set; } = "System";
    public int OnBackground { get; set; } = AppTheme.Green.ToArgb();
    public int OnForeground { get; set; } = Color.White.ToArgb();
    public int OffBackground { get; set; } = AppTheme.Red.ToArgb();
    public int OffForeground { get; set; } = Color.White.ToArgb();
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltura", "NetOn");

    internal void Normalize()
    {
        RestoreAdapterIds = (RestoreAdapterIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (RestoreAdapterIds.Count > 0) OffRequested = true;
        if (AdapterId == Guid.Empty) AdapterId = null;
        if (Theme is not ("System" or "Light" or "Dark")) Theme = "System";
        if (string.IsNullOrWhiteSpace(VoiceId)) VoiceId = null;
    }

    internal static AppSettings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? throw new JsonException("Settings are empty.");
        settings.Normalize();
        return settings;
    }

    public static AppSettings Load() => LoadFromDirectory(DirectoryPath);

    internal static AppSettings LoadFromDirectory(string directory)
    {
        foreach (string name in new[] { "preferences.json", "preferences.json.bak" })
        {
            try
            {
                string path = Path.Combine(directory, name);
                if (File.Exists(path)) return Parse(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        try
        {
            string old = Path.Combine(directory, "settings.json");
            return new AppSettings { AdapterId = File.Exists(old) ? JsonSerializer.Deserialize<Guid?>(File.ReadAllText(old)) : null };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save() => SaveToDirectory(DirectoryPath);

    internal void SaveToDirectory(string directory)
    {
        Normalize();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "preferences.json");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                file.Write(data);
                file.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static bool StartWithWindows
    {
        get => ReadStartWithWindows(() => { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("Voltura NetOn"); });
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (value) key.SetValue("Voltura NetOn", AppLaunch.StartupCommand(Environment.ProcessPath!, typeof(AppSettings).Assembly.Location));
            else key.DeleteValue("Voltura NetOn", false);
        }
    }
    internal static bool ReadStartWithWindows(Func<object?> read)
    {
        try { return read() is string; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
}
