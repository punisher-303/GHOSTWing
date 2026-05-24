using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace GHOSTWing
{
    public partial class ESPWindow : Window
    {
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00000080;

        public ESPWindow()
        {
            InitializeComponent();
            this.Loaded += ESPWindow_Loaded;
            
            // Full screen overlay
            this.Left = 0;
            this.Top = 0;
            this.Width = SystemParameters.PrimaryScreenWidth;
            this.Height = SystemParameters.PrimaryScreenHeight;
        }

        private void ESPWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Make window click-through
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }

        public void UpdateESP(List<VisionEngine.TargetInfo> targets, AppSettings s)
        {
            EspCanvas.Children.Clear();
            if (!s.EspEnabled) return;

            var brushObj = new System.Windows.Media.BrushConverter().ConvertFromString(s.EspColor);
            var brush = (brushObj as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.Red;
            brush.Opacity = 0.8;
            var pen = new System.Windows.Media.Pen(brush, 1.5 * s.EspSize);

            double centerX = this.Width / 2;
            double centerY = this.Height / 2;

            foreach (var target in targets)
            {
                double x = centerX + target.Delta.X + s.EspXOffset;
                double y = centerY + target.Delta.Y + s.EspYOffset;
                double w = target.Width * s.EspSize;
                double h = target.Height * s.EspSize;

                if (s.EspModeSkeleton && target.Keypoints != null && target.Keypoints.Count == 17)
                {
                    DrawSkeleton(target.Keypoints, brush, 2 * s.EspSize, centerX, centerY, s.EspXOffset, s.EspYOffset);
                }
                else
                {
                    // Draw Box
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = w,
                        Height = h,
                        Stroke = brush,
                        StrokeThickness = 2 * s.EspSize
                    };
                    Canvas.SetLeft(rect, x - w / 2);
                    Canvas.SetTop(rect, y - h / 2);
                    EspCanvas.Children.Add(rect);
                }
            }
        }

        private void DrawSkeleton(List<System.Drawing.PointF> kps, System.Windows.Media.Brush brush, double thickness, double cx, double cy, int ox, int oy)
        {
            // Skeleton Connections (Indices for YOLO-Pose)
            int[,] connections = new int[,] {
                {0, 1}, {0, 2}, {1, 3}, {2, 4}, // Head
                {5, 6}, {5, 7}, {7, 9}, {6, 8}, {8, 10}, // Arms
                {5, 11}, {6, 12}, {11, 12}, // Torso
                {11, 13}, {13, 15}, {12, 14}, {14, 16} // Legs
            };

            for (int i = 0; i < connections.GetLength(0); i++)
            {
                var p1 = kps[connections[i, 0]];
                var p2 = kps[connections[i, 1]];

                var line = new Line
                {
                    X1 = cx + p1.X - (400 / 2) + ox, // FOV offset correction (assuming 400 fov)
                    Y1 = cy + p1.Y - (400 / 2) + oy,
                    X2 = cx + p2.X - (400 / 2) + ox,
                    Y2 = cy + p2.Y - (400 / 2) + oy,
                    Stroke = brush,
                    StrokeThickness = thickness
                };
                // Note: FOV offset correction is needed because KPs are relative to FOV capture center
                EspCanvas.Children.Add(line);
            }
        }
    }
}
