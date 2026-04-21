using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SystemOverlay.Services;

public static class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SystemOverlay");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static Models.AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return new Models.AppSettings();

            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<Models.AppSettings>(json, JsonOptions)
                   ?? new Models.AppSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemOverlay] Settings load failed: {ex.Message}");
            return new Models.AppSettings();
        }
    }

    public static void Save(Models.AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemOverlay] Settings save failed: {ex.Message}");
        }
    }
}
