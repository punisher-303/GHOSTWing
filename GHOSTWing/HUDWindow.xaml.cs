using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GHOSTWing
{
    public partial class HUDWindow : Window
    {
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
            
            // Center on screen
            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = (SystemParameters.PrimaryScreenHeight / 2) + 100; // Position below crosshair
            
            this.Show();
            
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
