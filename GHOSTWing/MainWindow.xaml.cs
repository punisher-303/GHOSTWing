using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace GHOSTWing
{
    public partial class MainWindow : Window
    {
        // Hook structs moved to GlobalInputHook.cs

        const uint INPUT_MOUSE = 0;
        const uint MOUSEEVENTF_MOVE = 0x0001;
        const int VK_LBUTTON = 0x01;
        const string AppVersion = "1.0.0";
        const string UpdateJsonUrl = "https://raw.githubusercontent.com/GHOST-404/GHOSTWing/main/version.json"; // Replace with your real URL
        private string downloadUrl = "https://github.com/GHOST-404/GHOSTWing/releases"; // Default fallback

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



        [DllImport("user32.dll")]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        public MainWindow()
        {
            InitializeComponent();

            presetManager.Load();
            RefreshPresetCombo();

            sliderVertical.Value = 0;
            sliderHorizontal.Value = 0;

            // Tray icon moved to Loaded for better stability

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
            CheckForUpdates();
        }

        private async void CheckForUpdates()
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    // Add a user-agent to prevent being blocked by GitHub
                    client.DefaultRequestHeaders.Add("User-Agent", "GHOSTWing-App");
                    string json = await client.GetStringAsync(UpdateJsonUrl);
                    
                    // Simple manual parsing to avoid extra dependencies if possible
                    // Or use System.Text.Json
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        string latestVersion = doc.RootElement.GetProperty("version").GetString() ?? AppVersion;
                        downloadUrl = doc.RootElement.GetProperty("download_url").GetString() ?? downloadUrl;

                        if (IsNewerVersion(latestVersion, AppVersion))
                        {
                            // Update UI for New Version
                            btnUpdateStatusTitle.Text = "UPDATE NOW";
                            btnUpdateStatusTitle.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Red);
                            txtUpdateStatus.Text = "Update available";
                            txtUpdateStatus.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Green);
                            
                            txtBadgeVersion.Text = "v" + latestVersion;
                            txtBadgeVersion.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Green);
                            txtBadgeArrow.Visibility = Visibility.Visible;
                        }
                    }
                }
            }
            catch { /* Ignore network errors */ }
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

        private void UpdateNow_Click(object sender, MouseButtonEventArgs e)
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
                RefreshPresetHotkeys();
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

                    System.Windows.MessageBox.Show(
                        $"Shortcut cleared for preset '{preset.Name}'.",
                        "Keybind Deleted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }

        private void btnDeleteAllShortcuts_Click(object? sender, RoutedEventArgs? e)
        {
            if (presetManager.Presets.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "There are no presets to clear.",
                    "No Presets",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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

            System.Windows.MessageBox.Show(
                "All preset shortcuts have been cleared.",
                "Shortcuts Cleared",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
                    System.Windows.MessageBox.Show($"Shortcut '{shortcut}' is already assigned to another preset.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                recoilThread = new Thread(AutoRecoilLoop) { IsBackground = true };
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

        private void AutoRecoilLoop()
        {
            while (recoilActive)
            {
                bool leftPressed = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                bool rightPressed = (GetAsyncKeyState(0x02) & 0x8000) != 0;

                bool shouldActivate = false;
                if (currentActivationMode == ActivationMode.RightAndLeft)
                    shouldActivate = leftPressed && rightPressed;
                else
                    shouldActivate = leftPressed;

                if (shouldActivate)
                {
                    double vertical = 0;
                    double horizontal = 0;

                    // Read current slider values on the UI thread
                    Dispatcher.Invoke(() =>
                    {
                        vertical = sliderVertical.Value;
                        horizontal = sliderHorizontal.Value;
                    });

                    // Accumulate fractional movement
                    accumulatedX += horizontal;
                    accumulatedY += vertical;

                    int moveX = 0;
                    int moveY = 0;

                    // Only move when magnitude >= 1 in either direction
                    if (accumulatedX >= 1.0)
                        moveX = (int)Math.Floor(accumulatedX);
                    else if (accumulatedX <= -1.0)
                        moveX = (int)Math.Ceiling(accumulatedX);

                    if (accumulatedY >= 1.0)
                        moveY = (int)Math.Floor(accumulatedY);
                    else if (accumulatedY <= -1.0)
                        moveY = (int)Math.Ceiling(accumulatedY);

                    // Remove the part we've actually used
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

                int delay = 5;
                Dispatcher.Invoke(() => { delay = (int)sliderDelay.Value; });
                Thread.Sleep(delay);
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
