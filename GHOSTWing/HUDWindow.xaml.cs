using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GHOSTWing
{
    public partial class HUDWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private DispatcherTimer _hideTimer;

        public HUDWindow()
        {
            InitializeComponent();
            
            _hideTimer = new DispatcherTimer();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(1300); // 1.3 seconds as requested
            _hideTimer.Tick += (s, e) => HideHUD();
        }

        public void ShowMessage(string message)
        {
            txtHUD.Text = message.ToUpper();
            
            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = (SystemParameters.PrimaryScreenHeight / 2) + 100; // Position below crosshair
            
            this.Topmost = false;
            this.Topmost = true;
            this.Show();

            // Force absolute Topmost via Win32 API to pierce through Fullscreen Emulators
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
            
            if (this.TryFindResource("ShowHUDAnim") is Storyboard sb)
            {
                sb.Begin();
            }
            
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private void HideHUD()
        {
            _hideTimer.Stop();
            if (this.TryFindResource("HideHUDAnim") is Storyboard sb)
            {
                EventHandler? completedHandler = null;
                completedHandler = (s, e) =>
                {
                    sb.Completed -= completedHandler;
                    this.Hide();
                };
                sb.Completed += completedHandler;
                sb.Begin();
            }
            else
            {
                this.Hide();
            }
        }
    }
}
