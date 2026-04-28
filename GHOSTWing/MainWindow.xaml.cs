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

namespace GHOSTWing
{
    public partial class MainWindow : Window
    {
        // Hook structs moved to GlobalInputHook.cs

        const uint INPUT_MOUSE = 0;
        const uint MOUSEEVENTF_MOVE = 0x0001;
        const int VK_LBUTTON = 0x01;
        const string AppVersion = "1.0.1";
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



        [DllImport("user32.dll")]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint timeEndPeriod(uint uPeriod);

        const uint WDA_NONE = 0x00000000;
        const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public MainWindow()
        {
            InitializeComponent();

            presetManager.Load();
            RefreshPresetCombo();

            sliderVertical.Value = 0;
            sliderHorizontal.Value = 0;

            // Tray icon moved to Loaded for better stability
            
            // Hook up slider value caching
            sliderVertical.ValueChanged += (s, e) => _cachedVertical = sliderVertical.Value;
            sliderHorizontal.ValueChanged += (s, e) => _cachedHorizontal = sliderHorizontal.Value;
            sliderDelay.ValueChanged += (s, e) => _cachedDelay = (int)sliderDelay.Value;

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
                if (_notifyIcon != null) _notifyIcon.Visible = false;
                System.Windows.Application.Current.Shutdown();
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
            InitializeTrayIcon();
            settingsManager.Load();
            txtToggleShortcut.Text = settingsManager.Settings.ToggleShortcut;

            RefreshPresetHotkeys();

            GlobalInputHook.OnShortcutPressed += GlobalInputHook_OnShortcutPressed;
            GlobalInputHook.Start();

            txtAppVersion.Text = AppVersion;
            txtBadgeVersion.Text = "v" + AppVersion;
            
            // Initialize Streamer Mode (Combined Stealth)
            btnStreamerMode.IsChecked = settingsManager.Settings.IsStreamerMode;
            UpdateStreamerMode(settingsManager.Settings.IsStreamerMode);
            this.ShowInTaskbar = !settingsManager.Settings.IsStreamerMode;

            // Initialize System Info
            txtOSVersion.Text = GetFriendlyOSName();
            txtRuntimeVersion.Text = RuntimeInformation.FrameworkDescription;

            // Set high process priority for consistent performance
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }

            // Initialize cached values from sliders
            _cachedVertical = sliderVertical.Value;
            _cachedHorizontal = sliderHorizontal.Value;
            _cachedDelay = (int)sliderDelay.Value;

            _ = CheckForUpdates(); // Start silent background check on startup
        }

        private async void CheckUpdate_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (btnUpdateStatusTitle.Text == "UPDATE NOW")
                {
                    OpenDownloadLink();
                    return;
                }

                await CheckForUpdates();
            }
            catch { }
        }

        private async Task CheckForUpdates()
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
            GlobalInputHook.Stop();
            GlobalInputHook.OnShortcutPressed -= GlobalInputHook_OnShortcutPressed;
            recoilActive = false;
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
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
                    if (recoilActive) btnStop_Click(null, null);
                    else btnStart_Click(null, null);
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
                    }
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
                var preset = presetManager.Presets.Find(p => p.Name == comboPresets.SelectedItem.ToString());
                if (preset != null)
                {
                    sliderVertical.Value = preset.Vertical;
                    sliderHorizontal.Value = preset.Horizontal;
                    sliderDelay.Value = preset.Delay;
                    txtPresetName.Text = preset.Name;
                    txtShortcutKey.Text = preset.ShortcutKey;
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

        private void btnSetShortcut_Click(object? sender, RoutedEventArgs? e)
        {
            txtShortcutKey.Text = "Listening...";
            listeningFor = "Preset";
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

        private void AssignShortcut(string shortcut)
        {
            if (listeningFor == "Toggle")
            {
                settingsManager.Settings.ToggleShortcut = shortcut;
                settingsManager.Save();
                txtToggleShortcut.Text = shortcut;
            }
            else if (listeningFor == "Preset")
            {
                if (hotkeyPresetMap.ContainsKey(shortcut))
                {
                    ShowNotification($"Key '{shortcut}' already in use!", "Error");
                }
                else
                {
                    txtShortcutKey.Text = shortcut;
                    if (!string.IsNullOrWhiteSpace(txtPresetName.Text))
                    {
                        var preset = presetManager.Presets.Find(p => p.Name == txtPresetName.Text.Trim());
                        if (preset != null)
                        {
                            preset.ShortcutKey = shortcut;
                            presetManager.AddOrUpdatePreset(preset);
                            RefreshPresetHotkeys();
                        }
                    }
                }
            }
            listeningFor = "";
        }

        private void btnSetToggleShortcut_Click(object? sender, RoutedEventArgs? e)
        {
            txtToggleShortcut.Text = "Listening...";
            listeningFor = "Toggle";
        }

        private void btnClearToggleShortcut_Click(object? sender, RoutedEventArgs? e)
        {
            settingsManager.Settings.ToggleShortcut = "";
            settingsManager.Save();
            txtToggleShortcut.Text = "";
        }

        private void btnStart_Click(object? sender, RoutedEventArgs? e)
        {
            if (!recoilActive)
            {
                recoilActive = true;
                recoilThread = new Thread(AutoRecoilLoop) { IsBackground = true, Priority = ThreadPriority.Highest };
                recoilThread.Start();

                btnStart.Content = "Running...";
                btnStart.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50"));
                btnStop.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF3B30"));
            }
        }

        private void btnStop_Click(object? sender, RoutedEventArgs? e)
        {
            recoilActive = false;
            btnStart.Content = "Start";
            btnStart.Background = new SolidColorBrush(Colors.Black);
            btnStop.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF3B30"));
        }

        private void btnModeRightLeft_Click(object? sender, RoutedEventArgs? e)
        {
            currentActivationMode = ActivationMode.RightAndLeft;
            btnModeRightLeft.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50"));
            btnModeLeftOnly.Background = new SolidColorBrush(Colors.Black);
        }

        private void btnModeLeftOnly_Click(object? sender, RoutedEventArgs? e)
        {
            currentActivationMode = ActivationMode.LeftOnly;
            btnModeLeftOnly.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50"));
            btnModeRightLeft.Background = new SolidColorBrush(Colors.Black);
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

        private void UpdateStreamerMode(bool enabled)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                uint affinity = enabled ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
                SetWindowDisplayAffinity(helper.Handle, affinity);
            }
            catch { }
        }

        private void ShowNotification(string message, string type = "Success")
        {
            Dispatcher.BeginInvoke(() =>
            {
                txtNotificationMessage.Text = message;
                
                if (type == "Success")
                {
                    NotificationBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1DB954")); // Green
                    txtNotificationIcon.Text = "✓";
                }
                else if (type == "Warning")
                {
                    NotificationBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFA500")); // Orange
                    txtNotificationIcon.Text = "⚠";
                }
                else if (type == "Error")
                {
                    NotificationBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D32F2F")); // Red
                    txtNotificationIcon.Text = "✕";
                }

                var sb = (System.Windows.Media.Animation.Storyboard)this.Resources["ShowNotificationAnim"];
                sb.Stop(); // Reset if already playing
                sb.Begin();
            });
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

                    bool leftPressed = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                    bool rightPressed = (GetAsyncKeyState(0x02) & 0x8000) != 0;

                    bool shouldActivate = false;
                    if (currentActivationMode == ActivationMode.RightAndLeft)
                        shouldActivate = leftPressed && rightPressed;
                    else
                        shouldActivate = leftPressed;

                    if (shouldActivate)
                    {
                        double vertical = _cachedVertical;
                        double horizontal = _cachedHorizontal;

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
                    else
                    {
                        // When not firing, reset so we don't "dump" stored movement later
                        accumulatedX = 0;
                        accumulatedY = 0;
                    }

                    int targetDelay = _cachedDelay;
                    if (targetDelay < 1) targetDelay = 1;
                    
                    // Turbo Busy-Wait: 100% CPU active for perfect precision
                    // This prevents Windows from ever putting the thread to sleep when minimized
                    while (sw.ElapsedMilliseconds < targetDelay)
                    {
                        // Just stay active
                    }
                }
            }
            finally
            {
                // Restore system timer resolution
                timeEndPeriod(1);
            }
        }

        private void MoveCursorRelative(int dx, int dy)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dx = dx;
            inputs[0].mi.dy = dy;
            inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE;
            inputs[0].mi.mouseData = 0;
            inputs[0].mi.time = 0;
            inputs[0].mi.dwExtraInfo = IntPtr.Zero;

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
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
