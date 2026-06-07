using System;
using System.Windows;
using System.Windows.Media;

namespace GHOSTWing
{
    public partial class StatsOverlayWindow : Window
    {
        public StatsOverlayWindow()
        {
            InitializeComponent();
        }

        public void UpdateSettings(System.Windows.Media.Color color, double size, int x, int y)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            txtVerticalPull.Foreground = brush;
            txtDelay.Foreground = brush;

            txtVerticalPull.FontSize = size;
            txtDelay.FontSize = size;
            lblVP.FontSize = size;
            lblDL.FontSize = size;

            this.Left = x;
            this.Top = y;
        }

        public void UpdateStats(double verticalPull, int delayMs)
        {
            txtVerticalPull.Text = $"{verticalPull:F2}";
            txtDelay.Text = $"{delayMs}ms";
        }
    }
}
