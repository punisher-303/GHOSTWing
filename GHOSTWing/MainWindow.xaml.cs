using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Text.Json;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using System.Windows.Shapes;

namespace GHOSTWing
{
    public partial class MainWindow : Window
    {
        // Hook structs moved to GlobalInputHook.cs

        const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const int VK_LBUTTON = 0x01;
        public const string AppVersion = "1.0.4";
        const string UpdateJsonUrl = "https://raw.githubusercontent.com/punisher-303/GHOSTWing/refs/heads/main/version.json";
        private string downloadUrl = "https://github.com/punisher-303/GHOSTWing/releases"; // Default fallback

        private bool recoilActive = false;
        private Thread? recoilThread;
        private PresetManager presetManager = new PresetManager();
        private SettingsManager settingsManager = new SettingsManager();
        private List<RecoilPreset> allPresets = new List<RecoilPreset>();
        private string listeningFor = ""; // "", "Toggle", "Preset"

        private enum ActivationMode
        {
            RightAndLeft,
            LeftOnly
        }
        private ActivationMode currentActivationMode = ActivationMode.RightAndLeft;

        private NotifyIcon? _notifyIcon;

        private Dictionary<string, string> hotkeyPresetMap = new Dictionary<string, string>();

        // Sub-pixel accumulation for precise recoil
        private double accumulatedX = 0;
        private double accumulatedY = 0;

        // Cached slider values to avoid expensive Dispatcher.Invoke in background thread
        private double _cachedVertical = 0;
        private double _cachedHorizontal = 0;
        private int _cachedDelay = 5;

        private CrosshairWindow? crosshairWindow;

        // ADS Hide feature
        private bool _isInitializing = true;
        private bool _adsHideEnabled = false;
        private Thread? _adsWatchThread;
        private bool _adsWatchRunning = false;
        private const int VK_RBUTTON = 0x02;

        // Jitter State


        // Entitlements
        private EntitlementService entitlementService = new EntitlementService();
        private UserEntitlements? currentEntitlements;


        private bool isCursorVisible = true;
        // Movement Dispatcher (Unifies Recoil + Vision inputs)
        private double _pendingMoveX = 0;
        private double _pendingMoveY = 0;
        private readonly object _moveLock = new object();
        private Thread? _movementThread;
        private bool _movementRunning = false;
        private Thread? _stateThread;
        private bool _stateRunning = false;



        [DllImport("user32.dll")]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("user32.dll")]
        static extern bool GetCursorInfo(out CURSORINFO pci);

        [StructLayout(LayoutKind.Sequential)]
        struct CURSORINFO
        {
            public Int32 cbSize;
            public Int32 flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public Int32 x;
            public Int32 y;
        }

        const Int32 CURSOR_SHOWING = 0x00000001;

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint timeEndPeriod(uint uPeriod);

        const uint WDA_NONE = 0x00000000;
        const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;


        public MainWindow()
        {
            string debugPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHOSTWing", "startup_debug.txt");
            try { 
                string? dir = System.IO.Path.GetDirectoryName(debugPath);
                if (dir != null)
                {
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }
                File.WriteAllText(debugPath, "CONSTRUCTOR BEGUN at " + DateTime.Now.ToString() + "\n"); 
            } catch { }

            try
            {
                InitializeComponent();
                try { File.AppendAllText(debugPath, "InitializeComponent OK\n"); } catch { }

                presetManager.Load();
                RefreshPresetCombo();
                try { File.AppendAllText(debugPath, "Constructor Data Loaded\n"); } catch { }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(debugPath, "CONSTRUCTOR CRASH: " + ex.ToString() + "\n"); } catch { }
                System.Windows.MessageBox.Show("Initialize Error: " + ex.Message + "\n\nTry deleting your AppData\\Roaming\\GHOSTWing folder.");
            }

            sliderVertical.Value = 0;
            sliderHorizontal.Value = 0;

            // Tray icon moved to Loaded for better stability
            
            // Hook up slider value caching
            sliderVertical.ValueChanged += (s, e) => _cachedVertical = sliderVertical.Value;
            sliderHorizontal.ValueChanged += (s, e) => _cachedHorizontal = sliderHorizontal.Value;
            sliderDelay.ValueChanged += (s, e) => _cachedDelay = (int)sliderDelay.Value;



            _movementRunning = true;
            _movementThread = new Thread(MovementDispatcherLoop) { IsBackground = true, Priority = ThreadPriority.Highest };
            _movementThread.Start();

            _stateRunning = true;
            _stateThread = new Thread(StateWatchLoop) { IsBackground = true };
            _stateThread.Start();


            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon();
            try
            {
                // Correct relative path for embedded resources
                var streamInfo = System.Windows.Application.GetResourceStream(new Uri("assets/icon.ico", UriKind.Relative));
                if (streamInfo != null)
                {
                    using (var stream = streamInfo.Stream)
                    {
                        _notifyIcon.Icon = new System.Drawing.Icon(stream);
                        _notifyIcon.Visible = true;
                    }
                }
            }
            catch
            {
                // If icon fails to load, we continue without a tray icon to prevent startup crash
                _notifyIcon.Visible = false;
            }
            _notifyIcon.Text = "GHOSTWing";
            _notifyIcon.DoubleClick += (s, e) => ShowAndRestore();

            var menuStrip = new System.Windows.Forms.ContextMenuStrip();
            var openItem = new System.Windows.Forms.ToolStripMenuItem("Open GHOSTWing");
            openItem.Click += (s, e) => ShowAndRestore();
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) =>
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                Environment.Exit(0);
            };
            
            menuStrip.Items.Add(openItem);
            menuStrip.Items.Add(exitItem);
            _notifyIcon.ContextMenuStrip = menuStrip;
        }

        private void ShowAndRestore()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string debugPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHOSTWing", "startup_debug.txt");
            try { File.WriteAllText(debugPath, "STARTUP BEGUN at " + DateTime.Now.ToString() + "\n"); } catch { }

            try
            {
                InitializeTrayIcon();
                try { File.AppendAllText(debugPath, "Tray OK\n"); } catch { }
                
                // 1. Load Data
                settingsManager.Load();
                presetManager.Load();
                try { File.AppendAllText(debugPath, "Data Loaded\n"); } catch { }

                // Initial UI Setup
                txtAppVersion.Text = AppVersion;
                txtBadgeVersion.Text = "v" + AppVersion;
                txtUUID.Text = HardwareIdManager.GetDeviceId();
                txtOSVersion.Text = GetFriendlyOSName();
                txtRuntimeVersion.Text = RuntimeInformation.FrameworkDescription;
                txtToggleShortcut.Text = string.IsNullOrEmpty(settingsManager.Settings.ToggleShortcut) ? "None" : settingsManager.Settings.ToggleShortcut;
                
                // Sync Toggle Buttons in Settings
                chkRunOnStartup.IsChecked = settingsManager.Settings.RunOnStartup;
                chkMinimizeToTray.IsChecked = settingsManager.Settings.MinimizeToTray;
                chkStartMinimized.IsChecked = settingsManager.Settings.StartMinimized;


                sliderOpacity.Value = settingsManager.Settings.AppOpacity;
                this.Opacity = settingsManager.Settings.AppOpacity;
                try { File.AppendAllText(debugPath, "UI Setup OK\n"); } catch { }
                
                btnStreamerMode.IsChecked = settingsManager.Settings.IsStreamerMode;
                UpdateStreamerMode(settingsManager.Settings.IsStreamerMode);

                var s = settingsManager.Settings;
                if (s.ActivationMode == "LeftOnly")
                {
                    rbModeLeftOnly.IsChecked = true;
                    currentActivationMode = ActivationMode.LeftOnly;
                }
                else
                {
                    rbModeRightLeft.IsChecked = true;
                    currentActivationMode = ActivationMode.RightAndLeft;
                }



                // 4. Restore Last Selected Preset
                RefreshPresetHotkeys();
                if (!string.IsNullOrEmpty(s.LastSelectedPreset))
                {
                    foreach (var item in comboPresets.Items)
                    {
                        if (item.ToString() == s.LastSelectedPreset)
                        {
                            comboPresets.SelectedItem = item;
                            break;
                        }
                    }
                }
                else if (comboPresets.Items.Count > 0)
                {
                    // If no last selected, auto-load the first one instead of empty
                    comboPresets.SelectedIndex = 0;
                }
                try { File.AppendAllText(debugPath, "Presets OK\n"); } catch { }

                // 5. Restore Performance & Tray Settings
                UpdateProcessPriority();
                if (s.StartMinimized)
                {
                    this.WindowState = WindowState.Minimized;
                    if (s.MinimizeToTray) this.Hide();
                }

                // 6. Restore Crosshair & Intelligent Features
                RestoreCrosshairSettings();
                try { File.AppendAllText(debugPath, "Crosshair OK\n"); } catch { }

                Dispatcher.BeginInvoke(new Action(() => _isInitializing = false), System.Windows.Threading.DispatcherPriority.ContextIdle);

                // 7. Final Startups
                GlobalInputHook.OnShortcutPressed += GlobalInputHook_OnShortcutPressed;
                GlobalInputHook.Start();
                try { File.AppendAllText(debugPath, "Hooks OK\n"); } catch { }

                _cachedVertical = (float)sliderVertical.Value;
                _cachedHorizontal = (float)sliderHorizontal.Value;
                _cachedDelay = (int)sliderDelay.Value;

                _ = CheckForUpdates();

                // 8. Membership & Entitlements
                RefreshEntitlementsAsync();
                
                if (MainTabControl != null) MainTabControl.SelectedIndex = 0;
                
                try { File.AppendAllText(debugPath, "FINISHED\n"); } catch { }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(debugPath, "CRASH: " + ex.ToString() + "\n"); } catch { }
                System.Windows.MessageBox.Show("Startup Error: " + ex.Message);
            }
        }

        private void RestoreCrosshairSettings()
        {
            var s = settingsManager.Settings;
            
            // Restore UI state
            if (s.CrosshairEnabled)
            {
                btnCrosshairEnable.IsChecked = true;
                crosshairWindow = new CrosshairWindow();
                crosshairWindow.Show();
                
                if (s.IsStreamerMode) UpdateStreamerMode(true);
            }

            if (comboCrosshairShape != null) comboCrosshairShape.SelectedIndex = s.CrosshairShapeIndex;
            if (comboCrosshairColor != null) comboCrosshairColor.SelectedIndex = s.CrosshairColorIndex;
            if (sliderCrosshairSize != null) sliderCrosshairSize.Value = s.CrosshairSize;
            if (sliderCrosshairThickness != null) sliderCrosshairThickness.Value = s.CrosshairThickness;
            if (sliderCrosshairGap != null) sliderCrosshairGap.Value = s.CrosshairGap;
            if (sliderCrosshairOpacity != null) sliderCrosshairOpacity.Value = s.CrosshairOpacity;
            if (chkCrosshairDot != null) chkCrosshairDot.IsChecked = s.CrosshairDot;
            if (chkCrosshairOutline != null) chkCrosshairOutline.IsChecked = s.CrosshairOutline;
            if (chkHideOnADS != null) chkHideOnADS.IsChecked = s.HideOnADS;
            
            _adsHideEnabled = s.HideOnADS;

            UpdateCrosshairOverlay();
            RestoreCalibrationSettings();
            
            if (s.CrosshairEnabled && s.HideOnADS) StartAdsWatch();
        }

        private void RestoreCalibrationSettings()
        {
            var s = settingsManager.Settings;
            if (sliderCrouchMult != null) sliderCrouchMult.Value = s.CrouchMultiplier;
            if (sliderCalibStep != null) sliderCalibStep.Value = s.CalibStepSize;
            if (sliderGlobalMult != null) sliderGlobalMult.Value = s.GlobalRecoilMultiplier;
            if (chkAttachActive != null) chkAttachActive.IsChecked = s.IsAttachmentActive;
            if (chkCalibNotifications != null) chkCalibNotifications.IsChecked = s.ShowCalibNotifications;
            if (chkCalibEnabled != null) chkCalibEnabled.IsChecked = true; // Default to enabled per user request

            UpdateHotkeyButtonText(btnCalibCrouchKey, s.StanceCrouchKey);
            UpdateHotkeyButtonText(btnCalibSprintKey, s.StanceSprintKey);
            UpdateHotkeyButtonText(btnCalibAttachKey, s.AttachmentToggleKey);
            UpdateHotkeyButtonText(btnCalibUpKey, s.CalibUpKey);
            UpdateHotkeyButtonText(btnCalibDownKey, s.CalibDownKey);
        }

        private void UpdateHotkeyButtonText(System.Windows.Controls.Button? btn, string key)
        {
            if (btn != null) btn.Content = string.IsNullOrEmpty(key) ? "Record Key" : key.ToUpper();
        }

        private void SaveCrosshairSettings()
        {
            if (_isInitializing || settingsManager == null || comboCrosshairShape == null) return;

            var s = settingsManager.Settings;
            s.CrosshairEnabled = btnCrosshairEnable.IsChecked == true;
            s.CrosshairShapeIndex = comboCrosshairShape.SelectedIndex;
            s.CrosshairColorIndex = comboCrosshairColor.SelectedIndex;
            s.CrosshairSize = sliderCrosshairSize.Value;
            s.CrosshairThickness = sliderCrosshairThickness.Value;
            s.CrosshairGap = sliderCrosshairGap.Value;
            s.CrosshairOpacity = sliderCrosshairOpacity.Value;
            s.CrosshairDot = chkCrosshairDot.IsChecked == true;
            s.CrosshairOutline = chkCrosshairOutline.IsChecked == true;
            s.HideOnADS = chkHideOnADS.IsChecked == true;

            settingsManager.Save();
        }

        private void btnDownloadNow_Click(object sender, RoutedEventArgs e)
        {
            OpenDownloadLink();
            HideUpdateModal();
        }

        private void btnLater_Click(object sender, RoutedEventArgs e)
        {
            HideUpdateModal();
        }

        private void HideUpdateModal()
        {
            var sb = (System.Windows.Media.Animation.Storyboard)this.Resources["HideUpdateModalAnim"];
            sb.Begin();
        }

        private async void CheckUpdate_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (btnUpdateStatusTitle.Text.Contains("UPDATE NOW"))
                {
                    OpenDownloadLink();
                    return;
                }
                
                if (btnUpdateStatusTitle.Text.Contains("LATEST"))
                {
                    ShowNotification("You are using the latest version!", "Success");
                    return;
                }

                await CheckForUpdates(false); // Manual check
            }
            catch { }
        }

        private async Task CheckForUpdates(bool isStartup = true)
        {
            try
            {
                // Animation Start
                btnUpdateStatusTitle.Text = "Checking...";
                btnUpdateStatusTitle.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Gray);
                
                await Task.Delay(1000); // Small delay for the "Animation" feel

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "GHOSTWing-App");
                    string json = await client.GetStringAsync(UpdateJsonUrl);
                    
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        string latestVersion = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? AppVersion : AppVersion;
                        downloadUrl = doc.RootElement.TryGetProperty("download_url", out var d) ? d.GetString() ?? downloadUrl : downloadUrl;
                        
                        // ... (Footer text and color logic remains the same)
                        string footerText = doc.RootElement.TryGetProperty("footer_text", out var f) ? f.GetString() ?? "" : "";
                        string footerColor = doc.RootElement.TryGetProperty("footer_color", out var fc) ? fc.GetString() ?? "" : "";

                        if (!string.IsNullOrEmpty(footerText))
                        {
                            txtMarquee.Text = footerText;
                        }
                        
                        if (!string.IsNullOrEmpty(footerColor))
                        {
                            try {
                                string colorHex = footerColor.ToLower();
                                if (colorHex == "red") colorHex = "#D32F2F";
                                else if (colorHex == "green") colorHex = "#1DB954";
                                
                                txtMarquee.Foreground = (System.Windows.Media.Brush?)new BrushConverter().ConvertFrom(colorHex) ?? txtMarquee.Foreground;
                            } catch { }
                        }

                        if (IsNewerVersion(latestVersion, AppVersion))
                        {
                            btnUpdateStatusTitle.Text = "📥 UPDATE NOW";
                            btnUpdateStatusTitle.Foreground = (System.Windows.Media.Brush?)new BrushConverter().ConvertFrom("#D32F2F") ?? System.Windows.Media.Brushes.Red;
                            txtUpdateStatus.Text = "New version available for download";
                            txtUpdateStatus.Foreground = (System.Windows.Media.Brush?)new BrushConverter().ConvertFrom("#1DB954") ?? System.Windows.Media.Brushes.Green;
                            txtBadgeVersion.Text = "v" + latestVersion + " ↓";
                            txtBadgeVersion.Foreground = (System.Windows.Media.Brush?)new BrushConverter().ConvertFrom("#1DB954") ?? System.Windows.Media.Brushes.Green;
                            txtBadgeArrow.Visibility = Visibility.Collapsed; // We added it to the text directly
                            
                            if (isStartup)
                            {
                                txtCurrentVersionModal.Text = "v" + AppVersion;
                                txtLatestVersionModal.Text = "v" + latestVersion;
                                var sb = (System.Windows.Media.Animation.Storyboard)this.Resources["ShowUpdateModalAnim"];
                                sb.Begin();
                            }
                        }
                        else
                        {
                            btnUpdateStatusTitle.Text = "LATEST";
                            btnUpdateStatusTitle.Foreground = (System.Windows.Media.Brush?)new BrushConverter().ConvertFrom("#1DB954") ?? System.Windows.Media.Brushes.Green;
                            btnUpdateStatusTitle.Cursor = System.Windows.Input.Cursors.Arrow;
                            txtUpdateStatus.Text = "Using Latest version";
                        }
                    }
                }
            }
            catch 
            { 
                btnUpdateStatusTitle.Text = "Check Update";
                btnUpdateStatusTitle.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Gray);
                txtUpdateStatus.Text = "Check failed";
            }
        }

        private bool IsNewerVersion(string latest, string current)
        {
            try
            {
                Version vLatest = new Version(latest);
                Version vCurrent = new Version(current);
                return vLatest > vCurrent;
            }
            catch { return false; }
        }

        private void Title_Click(object sender, MouseButtonEventArgs e)
        {
            OpenDownloadLink();
        }

        private void OpenDownloadLink()
        {
            try
            {
                Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
            }
            catch { }
        }

        private void btnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = presetManager.GetFolderPath();
                if (!System.IO.Directory.Exists(path)) System.IO.Directory.CreateDirectory(path);
                Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Could not open folder: " + ex.Message);
            }
        }

        private void btnImportPreset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
                openFileDialog.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";
                
                if (openFileDialog.ShowDialog() == true)
                {
                    string json = System.IO.File.ReadAllText(openFileDialog.FileName);
                    
                    // Try to deserialize as a list or a single preset
                    List<RecoilPreset>? imported = null;
                    try 
                    {
                        imported = JsonSerializer.Deserialize<List<RecoilPreset>>(json);
                    }
                    catch 
                    {
                        var single = JsonSerializer.Deserialize<RecoilPreset>(json);
                        if (single != null) imported = new List<RecoilPreset> { single };
                    }

                    if (imported != null)
                    {
                        int count = 0;
                        foreach (var p in imported)
                        {
                            if (string.IsNullOrEmpty(p.Name)) continue;
                            presetManager.AddOrUpdatePreset(p);
                            count++;
                        }
                        
                        RefreshPresetCombo(); // Fixed method name
                        RefreshPresetHotkeys();
                        ShowNotification($"Imported {count} presets successfully!");
                    }
                    else
                    {
                        ShowNotification("Invalid preset file format.", "Error");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowNotification("Error importing: " + ex.Message, "Error");
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (settingsManager.Settings.MinimizeToTray)
            {
                e.Cancel = true;
                this.Hide();
                return;
            }

            // --- FULL SHUTDOWN SEQUENCE ---
            recoilActive = false;
            _adsWatchRunning = false;
            
            GlobalInputHook.Stop();
            GlobalInputHook.OnShortcutPressed -= GlobalInputHook_OnShortcutPressed;
            
            if (crosshairWindow != null)
            {
                crosshairWindow.Close();
                crosshairWindow = null;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            // Force process termination to clean up background threads
            Environment.Exit(0);
        }

        private void RefreshPresetHotkeys()
        {
            hotkeyPresetMap.Clear();
            foreach (var preset in presetManager.Presets)
            {
                if (!string.IsNullOrEmpty(preset.ShortcutKey))
                {
                    hotkeyPresetMap[preset.ShortcutKey] = preset.Name;
                }
            }
        }

        private void GlobalInputHook_OnShortcutPressed(string shortcut)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!string.IsNullOrEmpty(listeningFor))
                {
                    // We are setting a shortcut
                    AssignShortcut(shortcut);
                    return;
                }

                // Check for Stop shortcut
                // Check for Toggle shortcut
                if (!string.IsNullOrEmpty(settingsManager.Settings.ToggleShortcut) && shortcut == settingsManager.Settings.ToggleShortcut)
                {
                    string keyInfo = !string.IsNullOrEmpty(shortcut) ? $" [{shortcut}]" : "";
                    if (recoilActive) { StopRecoil(); ShowHUD($"ENGINE: OFF{keyInfo}"); }
                    else { StartRecoil(); ShowHUD($"ENGINE: ON{keyInfo}"); }
                    return;
                }




                // Check for Preset shortcut
                if (hotkeyPresetMap.TryGetValue(shortcut, out string? foundName) && foundName != null)
                {
                    var preset = presetManager.Presets.Find(p => p.Name == foundName);
                    if (preset != null)
                    {
                        sliderVertical.Value = preset.Vertical;
                        sliderHorizontal.Value = preset.Horizontal;
                        sliderDelay.Value = preset.Delay;
                        txtPresetName.Text = preset.Name;
                        txtShortcutKey.Text = preset.ShortcutKey;
                        ShowHUD($"PRESET: {preset.Name}");
                    }
                }

                // 4. Calibration Nudges (LUA Port)
                var s_cal = settingsManager.Settings;
                if (!s_cal.CalibEnabled) return; // Ignore calibration keys if disabled

                if (shortcut == s_cal.CalibUpKey)
                {
                    sliderVertical.Value = Math.Round(Math.Min(sliderVertical.Maximum, sliderVertical.Value + s_cal.CalibStepSize), 2);
                    ShowCalibrationFeedback("Vertical", sliderVertical.Value);
                }
                else if (shortcut == s_cal.CalibDownKey)
                {
                    sliderVertical.Value = Math.Round(Math.Max(sliderVertical.Minimum, sliderVertical.Value - s_cal.CalibStepSize), 2);
                    ShowCalibrationFeedback("Vertical", sliderVertical.Value);
                }
                else if (shortcut == s_cal.AttachmentToggleKey)
                {
                    chkAttachActive.IsChecked = !chkAttachActive.IsChecked;
                    ShowHUD(s_cal.IsAttachmentActive ? "ATTACHMENTS: ON" : "ATTACHMENTS: OFF");
                }
            });
        }

        private void RefreshPresetCombo()
        {
            comboPresets.Items.Clear();
            foreach (var p in presetManager.Presets)
                comboPresets.Items.Add(p.Name);
        }

        private void btnSavePreset_Click(object? sender, RoutedEventArgs? e)
        {
            if (!string.IsNullOrWhiteSpace(txtPresetName.Text))
            {
                var preset = new RecoilPreset
                {
                    Name = txtPresetName.Text.Trim(),
                    Vertical = (float)sliderVertical.Value,
                    Horizontal = (float)sliderHorizontal.Value,
                    Delay = (int)sliderDelay.Value,
                    ShortcutKey = txtShortcutKey.Text
                };

                presetManager.AddOrUpdatePreset(preset);
                RefreshPresetCombo();
                RefreshPresetHotkeys(); // re-register after changes
                ShowNotification($"Preset '{preset.Name}' saved!");
            }
        }

        private void comboPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboPresets.SelectedItem != null)
            {
                string? selectedName = comboPresets.SelectedItem.ToString();
                var preset = presetManager.Presets.Find(p => p.Name == selectedName);
                if (preset != null)
                {
                    sliderVertical.Value = preset.Vertical;
                    sliderHorizontal.Value = preset.Horizontal;
                    sliderDelay.Value = preset.Delay;
                    txtPresetName.Text = preset.Name;
                    txtShortcutKey.Text = preset.ShortcutKey ?? "";
                    
                    if (!_isInitializing && settingsManager != null)
                    {
                        settingsManager.Settings.LastSelectedPreset = selectedName ?? "";
                        settingsManager.Save();
                    }
                }
            }
        }

        private void btnDeletePreset_Click(object? sender, RoutedEventArgs? e)
        {
            if (comboPresets.SelectedItem != null)
            {
                string? selectedName = comboPresets.SelectedItem.ToString();
                if (selectedName != null)
                {
                    presetManager.DeletePreset(selectedName);
                }
                RefreshPresetCombo();
                txtPresetName.Clear();
                txtShortcutKey.Clear();
                RefreshPresetHotkeys();
                ShowNotification($"Preset '{selectedName}' deleted.", "Warning");
            }
        }

        private void AssignShortcut(string shortcut)
        {
            if (listeningFor == "Toggle")
            {
                settingsManager.Settings.ToggleShortcut = shortcut;
                txtToggleShortcut.Text = shortcut;
            }

            else if (listeningFor == "StanceCrouchKey")
            {
                settingsManager.Settings.StanceCrouchKey = shortcut;
                btnCalibCrouchKey.Content = shortcut.ToUpper();
            }
            else if (listeningFor == "StanceSprintKey")
            {
                settingsManager.Settings.StanceSprintKey = shortcut;
                btnCalibSprintKey.Content = shortcut.ToUpper();
            }
            else if (listeningFor == "AttachmentToggleKey")
            {
                settingsManager.Settings.AttachmentToggleKey = shortcut;
                btnCalibAttachKey.Content = shortcut.ToUpper();
            }
            else if (listeningFor == "CalibUpKey")
            {
                settingsManager.Settings.CalibUpKey = shortcut;
                btnCalibUpKey.Content = shortcut.ToUpper();
            }
            else if (listeningFor == "CalibDownKey")
            {
                settingsManager.Settings.CalibDownKey = shortcut;
                btnCalibDownKey.Content = shortcut.ToUpper();
            }
            else
            {
                txtShortcutKey.Text = shortcut;
            }
            
            listeningFor = "";
            settingsManager.Save();
        }

        private void btnDeleteShortcut_Click(object? sender, RoutedEventArgs? e)
        {
            if (!string.IsNullOrWhiteSpace(txtPresetName.Text))
            {
                var preset = presetManager.Presets.Find(p => p.Name == txtPresetName.Text.Trim());
                if (preset != null)
                {
                    preset.ShortcutKey = "";
                    txtShortcutKey.Text = "";
                    presetManager.AddOrUpdatePreset(preset);
                    RefreshPresetHotkeys();

                    ShowNotification($"Shortcut cleared for '{preset.Name}'", "Warning");
                }
            }
        }

        private void btnDeleteAllShortcuts_Click(object? sender, RoutedEventArgs? e)
        {
            if (presetManager.Presets.Count == 0)
            {
                ShowNotification("No presets found to clear.", "Error");
                return;
            }

            var result = System.Windows.MessageBox.Show(
                "This will clear the shortcut key for ALL presets. Continue?",
                "Clear All Shortcuts",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            foreach (var preset in presetManager.Presets)
            {
                preset.ShortcutKey = string.Empty;
            }

            presetManager.Save();
            txtShortcutKey.Text = string.Empty;
            RefreshPresetHotkeys();

            ShowNotification("All shortcuts cleared.", "Warning");
        }

        private void btnSetShortcut_Click(object? sender, RoutedEventArgs? e)
        {
            listeningFor = "Preset";
            txtShortcutKey.Text = "LISTENING...";
        }

        private void Hotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
            {
                btn.Content = "Listening...";
                listeningFor = btn.Name;
            }
        }

        private void btnSetToggleShortcut_Click(object sender, RoutedEventArgs e)
        {
            listeningFor = "Toggle";
            txtToggleShortcut.Text = "PRESS ANY KEY...";
        }



        private void btnRecordKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
            {
                btn.Content = "WAITING...";
                listeningFor = btn.Tag?.ToString() ?? "";
            }
        }

        private void btnClearToggleShortcut_Click(object? sender, RoutedEventArgs? e)
        {
            settingsManager.Settings.ToggleShortcut = "";
            settingsManager.Save();
            txtToggleShortcut.Text = "";
        }

        private void btnRecoilStatus_Click(object sender, RoutedEventArgs e)
        {
            if (btnRecoilStatus.IsChecked == true)
            {
                StartRecoil();
            }
            else
            {
                StopRecoil();
            }
        }

        private void StartRecoil()
        {
            if (!recoilActive)
            {
                recoilActive = true;
                recoilThread = new Thread(AutoRecoilLoop) { IsBackground = true, Priority = ThreadPriority.Highest };
                recoilThread.Start();

                btnRecoilStatus.IsChecked = true;
                txtEngineStatus.Text = "RUNNING";
                txtEngineStatus.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1DB954"));

                ShowHUD("RECOIL ENGINE: ON");
            }
        }

        private void StopRecoil()
        {
            recoilActive = false;
            btnRecoilStatus.IsChecked = false;
            txtEngineStatus.Text = "STOPPED";
            txtEngineStatus.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D32F2F"));

            ShowHUD("RECOIL ENGINE: OFF");
        }



        private void rbModeRightLeft_Checked(object sender, RoutedEventArgs e)
        {
            currentActivationMode = ActivationMode.RightAndLeft;
            if (!_isInitializing && settingsManager != null)
            {
                settingsManager.Settings.ActivationMode = "RightAndLeft";
                settingsManager.Save();
            }
        }

        private void rbModeLeftOnly_Checked(object sender, RoutedEventArgs e)
        {
            currentActivationMode = ActivationMode.LeftOnly;
            if (!_isInitializing && settingsManager != null)
            {
                settingsManager.Settings.ActivationMode = "LeftOnly";
                settingsManager.Save();
            }
        }

        private void NavRecoil_Checked(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null) MainTabControl.SelectedIndex = 0;
        }

        private void NavCrosshair_Checked(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null)
            {
                MainTabControl.SelectedIndex = 1;
                UpdateCrosshairPreview();
            }
        }

        private void NavIntelligence_Checked(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null) MainTabControl.SelectedIndex = 2;
        }

        private void NavAccount_Checked(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null) MainTabControl.SelectedIndex = 5;
        }

        private void NavCart_Checked(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null)
            {
                MainTabControl.SelectedIndex = 6;
                _ = LoadPlansAsync(); // Fire and forget
            }
        }

        private void NavCalibration_Checked(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null) MainTabControl.SelectedIndex = 3;
        }

        private void NavSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null) MainTabControl.SelectedIndex = 4;
        }

        private async void RefreshEntitlementsAsync()
        {
            string uuid = HardwareIdManager.GetDeviceId();
            currentEntitlements = await entitlementService.FetchEntitlements(uuid);
            
            // Update UI on UI Thread
            Dispatcher.Invoke(() => {
                UpdateAccountUI();
                ApplySecurityLocks();
            });
        }

        private List<AppPlan> _availablePlans = new List<AppPlan>();

        private async Task LoadPlansAsync()
        {
            var plans = await entitlementService.FetchPlans();
            if (plans == null || plans.Count == 0) return;
            
            _availablePlans = plans;
            Dispatcher.Invoke(() => UpdatePlanCards(plans));
        }

        private void UpdatePlanCards(List<AppPlan> plans)
        {
            int currentPlanId = currentEntitlements?.PlanId ?? 1;

            foreach (var plan in plans)
            {
                StackPanel? panel = plan.Id switch { 1 => panelFreeFeatures, 2 => panelGoldFeatures, 3 => panelDiamondFeatures, _ => null };
                System.Windows.Controls.Button? buyBtn = plan.Id switch { 1 => btnFreeBuy, 2 => btnGoldBuy, 3 => btnDiamondBuy, _ => null };
                
                if (panel == null) continue;

                panel.Children.Clear();
                foreach (var feat in plan.Features)
                {
                    var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    var icon = new TextBlock 
                    { 
                        Text = feat.Value ? "✓" : "✘", 
                        Foreground = new SolidColorBrush(feat.Value ? Colors.Green : Colors.Red),
                        FontWeight = FontWeights.Bold,
                        Width = 20
                    };
                    var text = new TextBlock 
                    { 
                        Text = feat.Name, 
                        Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(feat.Value ? "#FAFAFA" : "#555555")),
                        FontSize = 11
                    };
                    sp.Children.Add(icon);
                    sp.Children.Add(text);
                    panel.Children.Add(sp);
                }

                // Update Prices and Descriptions
                if (plan.Id == 1) { txtFreePrice.Text = plan.Price; txtFreeDesc.Text = plan.Description; }
                if (plan.Id == 2) { txtGoldPrice.Text = plan.Price; txtGoldDesc.Text = plan.Description; }
                if (plan.Id == 3) { txtDiamondPrice.Text = plan.Price; txtDiamondDesc.Text = plan.Description; }

                // Update Button State based on PlanId (1=FREE, 2=GOLD, 3=DIAMOND)
                if (buyBtn != null)
                {
                    bool isCurrent = plan.Id == currentPlanId;
                    buyBtn.Content = isCurrent ? "CURRENT PLAN" : "UPGRADE NOW";
                    buyBtn.IsEnabled = !isCurrent;
                    buyBtn.Opacity = isCurrent ? 0.5 : 1.0;
                }
            }
        }

        private void btnPlan_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
            {
                int id = btn.Name switch { "btnFreeBuy" => 1, "btnGoldBuy" => 2, "btnDiamondBuy" => 3, _ => 0 };
                var plan = _availablePlans.FirstOrDefault(p => p.Id == id);
                if (plan != null && !string.IsNullOrEmpty(plan.CheckoutUrl))
                {
                    try { Process.Start(new ProcessStartInfo(plan.CheckoutUrl) { UseShellExecute = true }); } catch { }
                }
            }
        }

        private void UpdateAccountUI()
        {
            if (currentEntitlements == null) return;

            txtAccUserName.Text = currentEntitlements.UserName.ToUpper();
            txtAccUUID.Text = "UUID: " + currentEntitlements.Uuid;
            
            // 3-Level Premium Logic
            string tierName = currentEntitlements.PlanId switch
            {
                1 => "FREE USER",
                2 => "GOLD MEMBER",
                3 => "DIAMOND MEMBER",
                _ => "VIP MEMBER"
            };

            string accent = currentEntitlements.PlanId switch
            {
                1 => "#71717A", // Zinc (Free)
                2 => "#F59E0B", // Amber (Gold)
                3 => "#10B981", // Green (Diamond)
                _ => "#10B981"  // Green (VIP)
            };

            txtAccStatusBadge.Text = tierName;
            txtAccStatusText.Text = currentEntitlements.PlanId > 1 ? "ACTIVE" : "FREE";
            txtAccPurchaseDate.Text = currentEntitlements.PurchaseDate.ToString("dd / MM / yyyy");

            if (currentEntitlements.ExpiryDate.HasValue)
            {
                txtAccExpiryDate.Text = currentEntitlements.ExpiryDate.Value.ToString("dd / MM / yyyy");
                var remaining = currentEntitlements.ExpiryDate.Value - DateTime.Now;
                if (remaining.TotalSeconds > 0)
                {
                    int years = remaining.Days / 365;
                    int months = (remaining.Days % 365) / 30;
                    int days = (remaining.Days % 365) % 30;
                    int hours = remaining.Hours;
                    txtAccDaysLeft.Text = $"{years}Y {months}M {days}D {hours}H";
                    txtAccDaysLeft.FontSize = 14;
                }
                else
                {
                    txtAccDaysLeft.Text = "EXPIRED";
                    txtAccDaysLeft.FontSize = 16;
                }
            }
            else
            {
                txtAccExpiryDate.Text = "LIFETIME ACCESS";
                txtAccDaysLeft.Text = "FOREVER";
                txtAccDaysLeft.FontSize = 18;
            }

            var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accent));
            txtAccStatusBadge.Foreground = brush;
            txtAccStatusText.Foreground = brush;
            borderProgressCircle.BorderBrush = brush;
            txtAccDaysLeft.Foreground = brush;
        }

        private void ApplySecurityLocks()
        {
            if (currentEntitlements == null) return;

            // 1. Universal Tab Security (Loops through all keys in SQL)
            foreach (var tab in currentEntitlements.Tabs)
            {
                if (tab.Key == "VipCart") continue; // Always allow purchase tab

                // Find elements by convention: NavKey, overlayKey, badgeSideKey
                Border? overlay = this.FindName("overlay" + tab.Key) as Border;
                Border? badge = this.FindName("badgeSide" + tab.Key) as Border;

                UpdateTabOverlay(tab.Key, overlay, badge);
            }

            // 2. Feature Locking (Keep these specific as they link to different control types)

            UpdateFeatureLock("EngineStatus", btnRecoilStatus, badgeVipEngine);
            UpdateFeatureLock("StreamerMode", btnStreamerMode, badgeVipStreamer);
            UpdateFeatureLock("CrosshairActive", btnCrosshairEnable, badgeVipCrossActive);
            UpdateFeatureLock("StartWithWindows", chkRunOnStartup, badgeVipStartup);
            UpdateFeatureLock("MinimizeToTray", chkMinimizeToTray, null);
            UpdateFeatureLock("StartMinimized", chkStartMinimized, null);
            UpdateFeatureLock("PrecisionCalibration", null, badgeSidePrecisionCalibration);
            
            // 3. Engine Safety (Disable features if not entitled)
            if (currentEntitlements.Features.TryGetValue("PrecisionCalibration", out bool calibAllowed))
            {
                settingsManager.Settings.CalibEnabled = calibAllowed;
            }
        }

        private void UpdateTabOverlay(string key, Border? overlay, Border? badge)
        {
            if (currentEntitlements != null && currentEntitlements.Tabs.TryGetValue(key, out bool allowed))
            {
                bool isMaint = currentEntitlements.Maintenance.ContainsKey(key) && currentEntitlements.Maintenance[key];
                
                if (overlay != null) 
                {
                    overlay.Visibility = (allowed && !isMaint) ? Visibility.Collapsed : Visibility.Visible;
                    if (isMaint)
                    {
                        overlay.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E6F59E0B")); // Solid Orange Warning
                        // ... maintenance logic ...
                    }
                    else
                    {
                        // Premium Blurred Dark Overlay
                        overlay.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#CC09090B"));
                        
                        // Dynamic Tier Message based on key
                        var stack = overlay.Child as StackPanel;
                        if (stack == null && overlay.Child is Grid g) stack = g.Children.OfType<StackPanel>().FirstOrDefault();
                        
                        if (stack != null)
                        {
                            var lockBadge = stack.Children.OfType<Border>().FirstOrDefault();
                            var txts = stack.Children.OfType<TextBlock>().ToList();
                            if (lockBadge != null && lockBadge.Child is TextBlock bt)
                            {
                                bool isDiamond = (key == "NeuralVision" || key == "Intelligence" || key == "PrecisionCalibration");
                                bt.Text = isDiamond ? "DIAMOND FEATURE" : "GOLD FEATURE";
                                lockBadge.Background = isDiamond ? 
                                    new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#06B6D4")) : // Cyan for Diamond
                                    new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")); // Amber for Gold
                            }
                        }
                    }
                }

                if (badge != null)
                {
                    badge.Visibility = (allowed && !isMaint) ? Visibility.Collapsed : Visibility.Visible;
                    if (!allowed || isMaint)
                    {
                        badge.Background = isMaint ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")) : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981"));
                        var grid = badge.Parent as Grid;
                        if (grid != null)
                        {
                            var textBlock = grid.Children.OfType<Border>().FirstOrDefault()?.Child as TextBlock;
                            if (textBlock != null) 
                            {
                                textBlock.Text = isMaint ? "MAINT" : "VIP";
                                textBlock.Foreground = isMaint ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
                            }
                        }
                    }
                }
            }
        }

        private void btnGoToCart_Click(object sender, RoutedEventArgs e)
        {
            // Switch to VIP CART tab (Index 5)
            NavVipCart.IsChecked = true;
            MainTabControl.SelectedIndex = 5;
        }

        private void UpdateTabLock(string key, System.Windows.Controls.RadioButton nav, Border? badge)
        {
            // No longer used for disabling, but kept for legacy or badge logic if needed
            if (currentEntitlements != null && currentEntitlements.Tabs.TryGetValue(key, out bool allowed))
            {
                // nav.IsEnabled = true; // Always allow clicking
            }
        }

        private void sliderCrouchMult_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (settingsManager != null)
            {
                settingsManager.Settings.CrouchMultiplier = e.NewValue;
                settingsManager.Save();
            }
        }

        private void sliderCalibStep_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (settingsManager != null)
            {
                settingsManager.Settings.CalibStepSize = e.NewValue;
                settingsManager.Save();
            }
        }

        private void sliderGlobalMult_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (settingsManager != null)
            {
                settingsManager.Settings.GlobalRecoilMultiplier = e.NewValue;
                settingsManager.Save();
            }
        }

        private void chkAttachActive_Changed(object sender, RoutedEventArgs e)
        {
            if (settingsManager != null)
            {
                settingsManager.Settings.IsAttachmentActive = (chkAttachActive.IsChecked == true);
                settingsManager.Save();
            }
        }

        private void chkCalibNotifications_Changed(object sender, RoutedEventArgs e)
        {
            if (settingsManager != null)
            {
                settingsManager.Settings.ShowCalibNotifications = (chkCalibNotifications.IsChecked == true);
                settingsManager.Save();
            }
        }

        private void chkCalibEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (settingsManager != null && chkCalibEnabled != null)
            {
                settingsManager.Settings.CalibEnabled = (chkCalibEnabled.IsChecked == true);
                settingsManager.Save();
            }
        }



        private void UpdateFeatureLock(string key, System.Windows.Controls.Primitives.ToggleButton? toggle, Border? badge)
        {
            if (currentEntitlements != null && currentEntitlements.Features.TryGetValue(key, out bool allowed))
            {
                bool isMaint = currentEntitlements.Maintenance.ContainsKey(key) && currentEntitlements.Maintenance[key];
                
                // Disable if not allowed OR under maintenance
                if (toggle != null) toggle.IsEnabled = allowed && !isMaint;
                
                if (badge != null)
                {
                    badge.Visibility = (allowed && !isMaint) ? Visibility.Collapsed : Visibility.Visible;
                    if (!allowed || isMaint)
                    {
                        badge.Background = isMaint ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")) : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981"));
                        var textBlock = badge.Child as TextBlock;
                        if (textBlock != null)
                        {
                            textBlock.Text = isMaint ? "UNDER MAINTENANCE" : "VIP ONLY";
                            textBlock.Foreground = isMaint ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
                        }
                    }
                }
                
                if ((!allowed || isMaint) && toggle != null) toggle.IsChecked = false;
            }
        }

        private void EnsureCrosshairWindow()
        {
            if (crosshairWindow == null)
            {
                crosshairWindow = new CrosshairWindow();
                UpdateStreamerMode(settingsManager.Settings.IsStreamerMode);
            }
        }

        private void btnCrosshairEnable_Click(object sender, RoutedEventArgs e)
        {
            UpdateOverlays();
            
            if (btnCrosshairEnable.IsChecked == true)
            {
                if (_adsHideEnabled) StartAdsWatch();
            }
            else
            {
                StopAdsWatch();
            }
            
            SaveCrosshairSettings();
        }

        private void UpdateOverlays()
        {
            var s = settingsManager.Settings;
            bool crosshairEnabled = btnCrosshairEnable.IsChecked == true;
            if (crosshairEnabled)
            {
                EnsureCrosshairWindow();
                crosshairWindow?.Show();
                UpdateCrosshairOverlay();
            }
            else
            {
                // Both disabled, hide/close window
                if (crosshairWindow != null)
                {
                    crosshairWindow.Hide();
                    // We keep it hidden instead of closing to avoid flickering when toggling
                }
            }
        }

        private void chkHideOnADS_Click(object sender, RoutedEventArgs e)
        {
            _adsHideEnabled = chkHideOnADS.IsChecked == true;
            SaveCrosshairSettings();
            if (_adsHideEnabled && btnCrosshairEnable.IsChecked == true)
                StartAdsWatch();
            else
                StopAdsWatch();
        }

        private void StartAdsWatch()
        {
            if (_adsWatchRunning) return;
            _adsWatchRunning = true;
            _adsWatchThread = new Thread(() =>
            {
                bool wasHidden = false;
                while (_adsWatchRunning)
                {
                    bool rightHeld = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
                    if (rightHeld && !wasHidden)
                    {
                        wasHidden = true;
                        Dispatcher.Invoke(() => crosshairWindow?.Hide());
                    }
                    else if (!rightHeld && wasHidden)
                    {
                        wasHidden = false;
                        Dispatcher.Invoke(() =>
                        {
                            if (btnCrosshairEnable.IsChecked == true)
                                crosshairWindow?.Show();
                        });
                    }
                    Thread.Sleep(8); // ~120Hz polling
                }
            }) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
            _adsWatchThread.Start();
        }

        private void StopAdsWatch()
        {
            _adsWatchRunning = false;
        }

        private void CrosshairSetting_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateCrosshairOverlay();
            SaveCrosshairSettings();
        }

        private void CrosshairSetting_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCrosshairOverlay();
            SaveCrosshairSettings();
        }

        private void UpdateCrosshairOverlay()
        {
            UpdateCrosshairPreview();
            
            // If window is null or hidden, and we need it, UpdateOverlays will handle it
            if (crosshairWindow == null || comboCrosshairShape == null ||
                sliderCrosshairSize == null || sliderCrosshairThickness == null || 
                sliderCrosshairGap == null || sliderCrosshairOpacity == null) return;

            if (btnCrosshairEnable.IsChecked == true)
            {
                string shape = (comboCrosshairShape.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Cross";
                System.Windows.Media.Color color = GetSelectedCrosshairColor();
                
                crosshairWindow.UpdateCrosshair(
                    shape,
                    color,
                    sliderCrosshairSize.Value,
                    sliderCrosshairThickness.Value,
                    sliderCrosshairGap.Value,
                    sliderCrosshairOpacity.Value,
                    chkCrosshairDot.IsChecked == true,
                    chkCrosshairOutline.IsChecked == true
                );
            }
            

        }

        private System.Windows.Media.Color GetSelectedCrosshairColor()
        {
            if (comboCrosshairColor == null) return Colors.Green;
            string colorName = (comboCrosshairColor.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Green";
            switch (colorName)
            {
                case "Red": return Colors.Red;
                case "Cyan": return Colors.Cyan;
                case "White": return Colors.White;
                case "Yellow": return Colors.Yellow;
                default: return Colors.Green;
            }
        }

        private void UpdateCrosshairPreview()
        {
            if (CrosshairPreview == null || comboCrosshairShape == null || comboCrosshairColor == null ||
                sliderCrosshairSize == null || sliderCrosshairThickness == null || 
                sliderCrosshairGap == null || sliderCrosshairOpacity == null ||
                chkCrosshairDot == null || chkCrosshairOutline == null) return;
            
            CrosshairPreview.Children.Clear();
            string shape = (comboCrosshairShape.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Cross";
            System.Windows.Media.Color color = GetSelectedCrosshairColor();
            
            DrawCrosshairOnCanvas(
                CrosshairPreview,
                shape,
                color,
                sliderCrosshairSize.Value,
                sliderCrosshairThickness.Value,
                sliderCrosshairGap.Value,
                sliderCrosshairOpacity.Value,
                chkCrosshairDot.IsChecked == true,
                chkCrosshairOutline.IsChecked == true
            );
        }

        private void SettingChanged_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || settingsManager == null) return;
            var s = settingsManager.Settings;


            settingsManager.Save();
        }

        private void DrawCrosshairOnCanvas(Canvas canvas, string shape, System.Windows.Media.Color color, double size, double thickness, double gap, double opacity, bool dot, bool outline)
        {
            canvas.Opacity = opacity / 100.0;
            
            // Re-use logic from CrosshairWindow but draw on the provided canvas
            // We'll use relative positioning around the center of the canvas
            double cx = canvas.Width / 2;
            double cy = canvas.Height / 2;

            if (outline)
            {
                DrawShapeOnCanvas(canvas, shape, Colors.Black, size, thickness + 2, gap, true, cx, cy);
            }
            DrawShapeOnCanvas(canvas, shape, color, size, thickness, gap, false, cx, cy);

            if (dot && shape != "Dot")
            {
                if (outline)
                {
                    Ellipse dotOutline = new Ellipse
                    {
                        Width = thickness + 3,
                        Height = thickness + 3,
                        Fill = System.Windows.Media.Brushes.Black
                    };
                    Canvas.SetLeft(dotOutline, cx - dotOutline.Width / 2);
                    Canvas.SetTop(dotOutline, cy - dotOutline.Height / 2);
                    canvas.Children.Add(dotOutline);
                }

                Ellipse dotShape = new Ellipse
                {
                    Width = thickness + 1,
                    Height = thickness + 1,
                    Fill = new SolidColorBrush(color)
                };
                Canvas.SetLeft(dotShape, cx - dotShape.Width / 2);
                Canvas.SetTop(dotShape, cy - dotShape.Height / 2);
                canvas.Children.Add(dotShape);
            }
        }

        private void DrawShapeOnCanvas(Canvas canvas, string shape, System.Windows.Media.Color color, double size, double thickness, double gap, bool isOutline, double cx, double cy)
        {
            System.Windows.Media.Brush brush = new SolidColorBrush(color);

            if (shape.Contains("Cross"))
            {
                // Top
                AddLineToCanvas(canvas, cx, cy - gap, cx, cy - gap - size, brush, thickness);
                // Bottom
                AddLineToCanvas(canvas, cx, cy + gap, cx, cy + gap + size, brush, thickness);
                // Left
                AddLineToCanvas(canvas, cx - gap, cy, cx - gap - size, cy, brush, thickness);
                // Right
                AddLineToCanvas(canvas, cx + gap, cy, cx + gap + size, cy, brush, thickness);
            }

            if (shape.Contains("Circle"))
            {
                Ellipse circle = new Ellipse
                {
                    Width = size * 2,
                    Height = size * 2,
                    Stroke = brush,
                    StrokeThickness = thickness
                };
                Canvas.SetLeft(circle, cx - size);
                Canvas.SetTop(circle, cy - size);
                canvas.Children.Add(circle);
            }

            if (shape == "Dot")
            {
                double dotSize = Math.Max(thickness + 1, 3);
                if (isOutline) dotSize += 2;

                Ellipse dot = new Ellipse
                {
                    Width = dotSize,
                    Height = dotSize,
                    Fill = brush
                };
                Canvas.SetLeft(dot, cx - dotSize / 2);
                Canvas.SetTop(dot, cy - dotSize / 2);
                canvas.Children.Add(dot);
            }
        }

        private void AddLineToCanvas(Canvas canvas, double x1, double y1, double x2, double y2, System.Windows.Media.Brush brush, double thickness)
        {
            System.Windows.Shapes.Line line = new System.Windows.Shapes.Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(line);
        }

        private void btnHide_Click(object? sender, RoutedEventArgs? e)
        {
            this.Hide();
            _notifyIcon?.ShowBalloonTip(2000, "GHOSTWing", "Running in the background. Double-click the tray icon to restore.", System.Windows.Forms.ToolTipIcon.Info);
        }

        private void btnStreamerMode_Click(object sender, RoutedEventArgs e)
        {
            bool isEnabled = btnStreamerMode.IsChecked ?? false;
            settingsManager.Settings.IsStreamerMode = isEnabled;
            settingsManager.Save();
            
            UpdateStreamerMode(isEnabled);
            this.ShowInTaskbar = !isEnabled;
        }

        private void ShowNotification(string message, string type = "Success")
        {
            Dispatcher.BeginInvoke(() =>
            {
                txtNotificationMessage.Text = message;
                
                if (type == "Success")
                {
                    NotificationBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981")); // Green
                    txtNotificationIcon.Text = "✓";
                }
                else if (type == "Warning")
                {
                    NotificationBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")); // Amber
                    txtNotificationIcon.Text = "⚠";
                }
                else
                {
                    NotificationBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6")); // Blue
                    txtNotificationIcon.Text = "ℹ";
                }
                
                var sb = (System.Windows.Media.Animation.Storyboard)this.Resources["ShowNotificationAnim"];
                sb.Begin();
            });
        }

        private void ShowCalibrationFeedback(string type, double value)
        {
            if (settingsManager.Settings.ShowCalibNotifications)
            {
                ShowHUD($"{type} Calibrated: {value:F2}");
            }
        }

        private bool IsHotkeyDown(string keyName)
        {
            if (string.IsNullOrEmpty(keyName) || keyName == "None") return false;
            try
            {
                if (keyName == "LControl") return (GetAsyncKeyState(0x11) & 0x8000) != 0;
                if (keyName == "LShift") return (GetAsyncKeyState(0x10) & 0x8000) != 0;

                Key key = (Key)Enum.Parse(typeof(Key), keyName);
                int vk = KeyInterop.VirtualKeyFromKey(key);
                return (GetAsyncKeyState(vk) & 0x8000) != 0;
            }
            catch { return false; }
        }

        private string GetFriendlyOSName()
        {
            string desc = RuntimeInformation.OSDescription;
            // Windows 11 often reports as Windows 10 with a build number >= 22000
            if (desc.Contains("Windows 10") || desc.Contains("Windows 10.0"))
            {
                // Simple build number check via OSDescription string
                // Example: "Microsoft Windows 10.0.22631"
                try {
                    string[] parts = desc.Split('.');
                    if (parts.Length >= 3) {
                        if (int.TryParse(parts[2], out int build) && build >= 22000) {
                            return desc.Replace("Windows 10", "Windows 11").Replace("10.0", "11.0");
                        }
                    }
                } catch { }
            }
            return desc;
        }

        private HUDWindow? _hud;

        private void ShowHUD(string message)
        {
            if (!settingsManager.Settings.ShowOnScreenHUD) return;

            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => ShowHUD(message));
                return;
            }

            if (_hud == null) 
            {
                _hud = new HUDWindow();
                UpdateStreamerMode(settingsManager.Settings.IsStreamerMode);
            }
            _hud.ShowMessage(message);
        }

        private void AutoRecoilLoop()
        {
            // Set 1ms timer resolution for high-precision sleep
            timeBeginPeriod(1);
            
            try
            {
                Stopwatch sw = new Stopwatch();

                while (recoilActive)
                {
                    sw.Restart();
                    int targetDelay = _cachedDelay;

                    // 1. Smart Mouse Detection (Anti-Menu Bug)
                    // (State is now handled by StateWatchLoop)

                    // Use high-priority hook states instead of polling GetAsyncKeyState
                    bool leftPressed = GlobalInputHook.IsLeftButtonPressed;
                    bool rightPressed = GlobalInputHook.IsRightButtonPressed;

                    bool shouldActivate = false;
                    if (currentActivationMode == ActivationMode.RightAndLeft)
                        shouldActivate = leftPressed && rightPressed;
                    else
                        shouldActivate = leftPressed;

                    // Only activate if buttons are pressed AND mouse cursor is hidden (in-game)
                    if (shouldActivate && !isCursorVisible)
                    {
                        {
                            // --- MANUAL RECOIL CONFIGURATION ---
                            var s = settingsManager.Settings;
                            double vertical = _cachedVertical;
                            double horizontal = _cachedHorizontal;

                            // Apply Calibration Multipliers
                            if (s.CalibEnabled)
                            {
                                vertical *= s.GlobalRecoilMultiplier;
                                horizontal *= s.GlobalRecoilMultiplier;

                                if (IsHotkeyDown(s.StanceCrouchKey))
                                {
                                    vertical *= s.CrouchMultiplier;
                                    horizontal *= s.CrouchMultiplier;
                                }
                            }

                            // Accumulate fractional movement
                            accumulatedX += horizontal;
                            accumulatedY += vertical;

                            int moveX = (int)accumulatedX;
                            int moveY = (int)accumulatedY;

                            accumulatedX -= moveX;
                            accumulatedY -= moveY;

                            // Only send input if there is real movement
                            if (moveX != 0 || moveY != 0)
                            {
                                MoveCursorRelative(moveX, moveY);
                            }
                        }
                    }
                    else
                    {
                        // Reset trackers when not firing
                        GlobalInputHook.GetAndResetPhysicalDeltas(); // Clear stale user input
                        
                        // When not firing, reset so we don't "dump" stored movement later
                        accumulatedX = 0;
                        accumulatedY = 0;
                    }

                    if (targetDelay < 1) targetDelay = 1;
                    
                    // Hybrid Sleep/Spin: Best of both worlds
                    // Sleep for the majority of the time to save CPU, then spin-wait the last 1ms for micro-precision
                    int sleepTime = targetDelay - 1;
                    if (sleepTime > 0) Thread.Sleep(sleepTime);
                    
                    while (sw.ElapsedMilliseconds < targetDelay)
                    {
                        // Final micro-precision spin
                        Thread.SpinWait(10);
                    }
                }
            }
            finally
            {
                // Restore system timer resolution
                timeEndPeriod(1);
            }
        }

        private void MoveCursorRelative(double dx, double dy)
        {
            lock (_moveLock)
            {
                _pendingMoveX += dx;
                _pendingMoveY += dy;
            }
        }

        private void MovementDispatcherLoop()
        {
            timeBeginPeriod(1);
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.mouseData = 0;
            inputs[0].mi.time = 0;
            inputs[0].mi.dwExtraInfo = IntPtr.Zero;
            inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE;

            while (_movementRunning)
            {
                int ix = 0, iy = 0;
                lock (_moveLock)
                {
                    if (Math.Abs(_pendingMoveX) >= 1.0 || Math.Abs(_pendingMoveY) >= 1.0)
                    {
                        ix = (int)Math.Truncate(_pendingMoveX);
                        iy = (int)Math.Truncate(_pendingMoveY);
                        
                        _pendingMoveX -= ix;
                        _pendingMoveY -= iy;
                    }
                }

                if (ix != 0 || iy != 0)
                {
                    inputs[0].mi.dx = ix;
                    inputs[0].mi.dy = iy;
                    SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                }

                Thread.Sleep(1); // 1000Hz dispatch
            }
            timeEndPeriod(1);
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public MOUSEINPUT mi;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }




        private void sliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (settingsManager == null) return;
            this.Opacity = e.NewValue;
            settingsManager.Settings.AppOpacity = e.NewValue;
            settingsManager.Save();
        }

        private void btnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Are you sure you want to reset all settings to default?", "GHOSTWing Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                settingsManager.Settings = new AppSettings();
                settingsManager.Save();
                System.Windows.MessageBox.Show("Settings reset. Please restart the application for all changes to take effect.", "GHOSTWing", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateStartupRegistration()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)!)
                {
                    if (settingsManager.Settings.RunOnStartup)
                    {
                        key.SetValue("GHOSTWing", $"\"{Process.GetCurrentProcess().MainModule?.FileName}\"");
                    }
                    else
                    {
                        if (key.GetValue("GHOSTWing") != null) key.DeleteValue("GHOSTWing");
                    }
                }
            }
            catch { }
        }

        private void UpdateProcessPriority()
        {
            try
            {
                var priority = settingsManager.Settings.PriorityClass;
                var proc = Process.GetCurrentProcess();
                switch (priority)
                {
                    case "Normal": proc.PriorityClass = ProcessPriorityClass.Normal; break;
                    case "Above Normal": proc.PriorityClass = ProcessPriorityClass.AboveNormal; break;
                    case "High": proc.PriorityClass = ProcessPriorityClass.High; break;
                    case "Realtime (Not Recommended)": proc.PriorityClass = ProcessPriorityClass.RealTime; break;
                    default: proc.PriorityClass = ProcessPriorityClass.High; break;
                }
            }
            catch { }
        }

        private void btnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void btnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
                this.WindowState = WindowState.Maximized;
        }

        private void btnCopyUUID_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(txtUUID.Text);
                ShowNotification("Device ID copied to clipboard!", "Success");
            }
            catch { }
        }

        private void CopyUUID_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(txtUUID.Text);
                ShowNotification("Device ID copied to clipboard!", "Success");
            }
            catch { }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ShowWeaponHUD()
        {
            ShowHUD("AI ENGINE: ADAPTING");
        }

        private void btnRefreshAccount_Click(object sender, RoutedEventArgs e)
        {
            RefreshEntitlementsAsync();
            ShowNotification("Account status synced!", "Success");
        }

        private void btnExtendSub_Click(object sender, RoutedEventArgs e)
        {
            // Open your discord or website
            Process.Start(new ProcessStartInfo("https://github.com/punisher-303/GHOSTWing") { UseShellExecute = true });
        }


        private void StateWatchLoop()
        {
            while (_stateRunning)
            {
                try
                {
                        CURSORINFO ci = new CURSORINFO();
                        ci.cbSize = Marshal.SizeOf(ci);
                        if (GetCursorInfo(out ci))
                        {
                            this.isCursorVisible = (ci.flags == 0x00000001); // 0x1 = CURSOR_SHOWING
                        }
                }
                catch { }
                Thread.Sleep(100); // 10Hz is enough for menu detection
            }
        }



        private void SimulateKey(string keyName, bool down)
        {
            try
            {
                if (string.IsNullOrEmpty(keyName)) return;
                Key key = (Key)Enum.Parse(typeof(Key), keyName);
                int vk = KeyInterop.VirtualKeyFromKey(key);
                
                INPUT input = new INPUT();
                input.type = 1; // INPUT_KEYBOARD
                input.ki.wVk = (ushort)vk;
                input.ki.dwFlags = down ? 0u : 0x0002u; // 0 = Down, 2 = Up
                SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
            }
            catch { }
        }

        private void SimulateMouse(int vKey, bool down)
        {
            INPUT input = new INPUT();
            input.type = 0; // INPUT_MOUSE
            uint flag = 0;
            if (vKey == VK_LBUTTON) flag = down ? 0x0002u : 0x0004u; // LEFTDOWN, LEFTUP
            if (vKey == VK_RBUTTON) flag = down ? 0x0008u : 0x0010u; // RIGHTDOWN, RIGHTUP
            
            input.mi.dwFlags = flag;
            SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        private void UpdateStreamerMode(bool enabled)
        {
            try
            {
                uint affinity = enabled ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
                
                // Protect main window
                var mainHelper = new WindowInteropHelper(this);
                if (mainHelper.Handle != IntPtr.Zero)
                    SetWindowDisplayAffinity(mainHelper.Handle, affinity);

                // Protect crosshair window
                if (crosshairWindow != null)
                {
                    var crosshairHelper = new WindowInteropHelper(crosshairWindow);
                    if (crosshairHelper.Handle != IntPtr.Zero)
                        SetWindowDisplayAffinity(crosshairHelper.Handle, affinity);
                }

                // Protect HUD window
                if (_hud != null)
                {
                    var hudHelper = new WindowInteropHelper(_hud);
                    if (hudHelper.Handle != IntPtr.Zero)
                        SetWindowDisplayAffinity(hudHelper.Handle, affinity);
                }
            }
            catch { }
        }


    }

    public class RecoilPreset
    {
        public string Name { get; set; } = "";
        public float Vertical { get; set; }
        public float Horizontal { get; set; }
        public int Delay { get; set; } = 5;
        public string ShortcutKey { get; set; } = "";
    }
}
