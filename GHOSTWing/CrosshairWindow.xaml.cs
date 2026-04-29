using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GHOSTWing
{
    public partial class CrosshairWindow : Window
    {
        public CrosshairWindow()
        {
            InitializeComponent();
            this.Loaded += CrosshairWindow_Loaded;
        }

        private void CrosshairWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Center on screen
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            this.Left = (screenWidth - this.Width) / 2;
            this.Top = (screenHeight - this.Height) / 2;
        }

        public void UpdateCrosshair(string shape, System.Windows.Media.Color color, double size, double thickness, double gap, double opacity, bool dot, bool outline)
        {
            CrosshairCanvas.Children.Clear();
            CrosshairCanvas.Opacity = opacity / 100.0;

            double cx = CrosshairCanvas.ActualWidth / 2;
            double cy = CrosshairCanvas.ActualHeight / 2;

            if (outline)
            {
                DrawShape(shape, Colors.Black, size, thickness + 2, gap, true);
            }
            DrawShape(shape, color, size, thickness, gap, false);

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
                    Canvas.SetLeft(dotOutline, -dotOutline.Width / 2);
                    Canvas.SetTop(dotOutline, -dotOutline.Height / 2);
                    CrosshairCanvas.Children.Add(dotOutline);
                }

                Ellipse dotShape = new Ellipse
                {
                    Width = thickness + 1,
                    Height = thickness + 1,
                    Fill = new SolidColorBrush(color)
                };
                Canvas.SetLeft(dotShape, -dotShape.Width / 2);
                Canvas.SetTop(dotShape, -dotShape.Height / 2);
                CrosshairCanvas.Children.Add(dotShape);
            }
        }

        private void DrawShape(string shape, System.Windows.Media.Color color, double size, double thickness, double gap, bool isOutline)
        {
            System.Windows.Media.Brush brush = new SolidColorBrush(color);

            if (shape.Contains("Cross"))
            {
                // Top
                AddLine(0, -gap, 0, -gap - size, brush, thickness);
                // Bottom
                AddLine(0, gap, 0, gap + size, brush, thickness);
                // Left
                AddLine(-gap, 0, -gap - size, 0, brush, thickness);
                // Right
                AddLine(gap, 0, gap + size, 0, brush, thickness);
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
                Canvas.SetLeft(circle, -size);
                Canvas.SetTop(circle, -size);
                CrosshairCanvas.Children.Add(circle);
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
                Canvas.SetLeft(dot, -dotSize / 2);
                Canvas.SetTop(dot, -dotSize / 2);
                CrosshairCanvas.Children.Add(dot);
            }
        }

        private void AddLine(double x1, double y1, double x2, double y2, System.Windows.Media.Brush brush, double thickness)
        {
            Line line = new Line
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
            CrosshairCanvas.Children.Add(line);
        }
    }
}
