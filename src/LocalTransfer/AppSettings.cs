using System.Text.Json;

namespace LocalTransfer;

internal sealed class AppSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "local-transfer");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public string? UploadFolder { get; set; }
    public bool DarkMode { get; set; }
    public string? Language { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public string ResolveUploadFolder()
    {
        if (!string.IsNullOrWhiteSpace(UploadFolder))
        {
            try { return Path.GetFullPath(UploadFolder); } catch { }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "local-transfer");
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
