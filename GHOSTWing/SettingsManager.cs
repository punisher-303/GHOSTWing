using System;
using System.IO;
using System.Text.Json;

namespace GHOSTWing
{
    public class AppSettings
    {
        public string ToggleShortcut { get; set; } = "";
        public bool IsStreamerMode { get; set; } = false;
        
        // System Settings
        public bool RunOnStartup { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool StartMinimized { get; set; } = false;
        public string PriorityClass { get; set; } = "High";
        public double AppOpacity { get; set; } = 1.0;
    }

    public class SettingsManager
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GHOSTWing",
            "settings.json");

        public AppSettings Settings { get; set; } = new AppSettings();

        public void Load()
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
    }
}
