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
    public int OnBackground { get; set; } = AppTheme.Green.ToArgb();
    public int OnForeground { get; set; } = Color.White.ToArgb();
    public int OffBackground { get; set; } = AppTheme.Red.ToArgb();
    public int OffForeground { get; set; } = Color.White.ToArgb();
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltura", "NetOn");
    private static string FilePath => Path.Combine(DirectoryPath, "preferences.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            string old = Path.Combine(DirectoryPath, "settings.json");
            return new AppSettings { AdapterId = File.Exists(old) ? JsonSerializer.Deserialize<Guid?>(File.ReadAllText(old)) : null };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }

    public static bool StartWithWindows
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("Voltura NetOn") is string; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (value) key.SetValue("Voltura NetOn", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("Voltura NetOn", false);
        }
    }
}
