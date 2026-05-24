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
        public bool AutoPauseInMenus { get; set; } = false;
        public bool ShowOnScreenHUD { get; set; } = true;
        public bool AdaptiveRecoilEnabled { get; set; } = false;
        public double AiRecoilStrength { get; set; } = 3.0;
        public string ActivationMode { get; set; } = "RightAndLeft";
        public string LastSelectedPreset { get; set; } = "";

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
        public double JitterStrength { get; set; } = 0.5;

        // Tactical Peek Engine
        public bool PeekEnabled { get; set; } = false;
        public bool PeekAutoFire { get; set; } = false;
        public string PeekActivationKey { get; set; } = ""; // No default key
        public string GameCrouchKey { get; set; } = "C";
        public bool PeekModeHold { get; set; } = true; // true = Hold, false = Toggle
        public int PeekShowMs { get; set; } = 350;
        public int PeekHideMs { get; set; } = 400;
        public bool VehicleIntelligenceEnabled { get; set; } = true;

        // Neural Vision AI
        public bool VisionEnabled { get; set; } = false;
        public double VisionConfidence { get; set; } = 0.5;
        public int VisionFov { get; set; } = 400; // Accurate sweet spot
        public double VisionSmoothness { get; set; } = 0.2; // Water-Flow standard
        public int VisionTarget { get; set; } = 1; // 0 = Body, 1 = Head
        public bool ShowVisionFov { get; set; } = false; // User toggle only
        public int VisionActivationMode { get; set; } = 0; // 0 = ADS, 1 = FIRE

        // --- Neuro ESP System ---
        public bool EspEnabled { get; set; } = false;
        public bool EspModeSkeleton { get; set; } = true; // true = Skeleton, false = Box
        public string EspColor { get; set; } = "#FF0000";
        public double EspConfidence { get; set; } = 0.4;
        public double EspSize { get; set; } = 1.0;
        public int EspXOffset { get; set; } = 0;
        public int EspYOffset { get; set; } = 0;

        // --- Precision Calibration System ---
        public bool CalibEnabled { get; set; } = true;
        public double CalibStepSize { get; set; } = 0.05;
        public string CalibUpKey { get; set; } = "";
        public string CalibDownKey { get; set; } = "";
        public string StanceCrouchKey { get; set; } = "LControl";
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
