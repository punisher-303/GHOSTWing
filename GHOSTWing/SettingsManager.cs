using System;
using System.IO;
using System.Text.Json;

namespace GHOSTWing
{
    public class AppSettings
    {
        public string ToggleShortcut { get; set; } = "";
        public string WeaponCycleShortcut { get; set; } = "";
        public string LevelCycleShortcut { get; set; } = "";
        public bool IsStreamerMode { get; set; } = false;
        
        // System Settings
        public bool RunOnStartup { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public string PriorityClass { get; set; } = "High";
        public double AppOpacity { get; set; } = 1.0;

        // Engine & UX
        public bool ShowOnScreenHUD { get; set; } = true;
        public string ActivationMode { get; set; } = "RightAndLeft";
        public string LastSelectedPreset { get; set; } = "";
        
        // Cached Manual Sliders
        public double LastVertical { get; set; } = 0;
        public double LastHorizontal { get; set; } = 0;
        public int LastDelay { get; set; } = 0;

        // Game Specific Calibration
        public double GameVerticalSens { get; set; } = 1.0;
        public double GameADSSens { get; set; } = 50.0;
        public int MouseDpi { get; set; } = 800;

        // Crosshair Settings
        public bool CrosshairEnabled { get; set; } = false;
        public int CrosshairShapeIndex { get; set; } = 0;
        public int CrosshairColorIndex { get; set; } = 1; // Red default
        public double CrosshairSize { get; set; } = 10;
        public double CrosshairThickness { get; set; } = 2;
        public double CrosshairGap { get; set; } = 3;
        public double CrosshairOpacity { get; set; } = 100;
        public bool CrosshairDot { get; set; } = true;
        public bool CrosshairOutline { get; set; } = true;
        public bool HideOnADS { get; set; } = false;



        // Stats Overlay Settings
        public bool StatsOverlayEnabled { get; set; } = false;
        public int StatsOverlayColorIndex { get; set; } = 1; // Default to Red (matching crosshair colors usually)
        public double StatsOverlaySize { get; set; } = 12.0;
        public int StatsOverlayX { get; set; } = 10;
        public int StatsOverlayY { get; set; } = 500;

        // --- Precision Calibration System ---
        public bool CalibEnabled { get; set; } = true;
        public double CalibStepSize { get; set; } = 0.05;
        public string CalibUpKey { get; set; } = "";
        public string CalibDownKey { get; set; } = "";
        public string StanceCrouchKey { get; set; } = "C";
        public string StanceSprintKey { get; set; } = "LShift";
        public double CrouchMultiplier { get; set; } = 0.8;
        public string AttachmentToggleKey { get; set; } = "";
        public bool IsAttachmentActive { get; set; } = false;
        public double GlobalRecoilMultiplier { get; set; } = 1.0;
        public bool ShowCalibNotifications { get; set; } = true;
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
