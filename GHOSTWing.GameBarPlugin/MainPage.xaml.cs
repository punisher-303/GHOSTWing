using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using Microsoft.Gaming.XboxGameBar;
using Windows.UI;

namespace GHOSTWing.GameBarPlugin
{
    public sealed partial class MainPage : Page
    {
        private XboxGameBarWidget _widget;
        private bool _isRunning = false;
        private DispatcherTimer _toastTimer;
        private long _lastToastId = 0;

        public MainPage()
        {
            this.InitializeComponent();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _toastTimer.Tick += (s, args) => 
            {
                _toastTimer.Stop();
                ToastContainer.Visibility = Visibility.Collapsed;
            };
        }

        protected override async void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            _widget = e.Parameter as XboxGameBarWidget;
            if (_widget != null)
            {
                _isRunning = true;
                _ = ConnectAndListenToGHOSTWing();

                try
                {
                    // Automatically detect monitor size and expand widget to fill most of the screen
                    var displayInfo = Windows.Graphics.Display.DisplayInformation.GetForCurrentView();
                    double w = displayInfo.ScreenWidthInRawPixels - 100; // Leave a 50px buffer on edges
                    double h = displayInfo.ScreenHeightInRawPixels - 100;
                    
                    if (w > 0 && h > 0)
                    {
                        await _widget.TryResizeWindowAsync(new Windows.Foundation.Size(w, h));
                        await _widget.CenterWindowAsync();
                    }
                }
                catch { }
            }
        }

        private async Task ConnectAndListenToGHOSTWing()
        {
            while (_isRunning)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(".", "GHOSTWingOverlayPipe", PipeDirection.In, PipeOptions.Asynchronous))
                    {
                        await client.ConnectAsync(1000);
                        using (var reader = new StreamReader(client))
                        {
                            while (client.IsConnected && _isRunning)
                            {
                                string line = await reader.ReadLineAsync();
                                if (!string.IsNullOrEmpty(line))
                                {
                                    ProcessPayload(line);
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    await Task.Delay(1000);
                }
            }
        }

        private async void ProcessPayload(string json)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<GHOSTWingPayload>(json, options);

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (data.UseGameBarOverlay)
                        {
                            // Stats Update
                            if (data.StatsEnabled)
                            {
                                StatsContainer.Visibility = Visibility.Visible;
                                txtVP.Text = data.VerticalPull.ToString("0.00");
                                txtDL.Text = data.CycleDelay.ToString() + "ms";
                            }
                            else
                            {
                                StatsContainer.Visibility = Visibility.Collapsed;
                            }

                            // Crosshair Update
                            if (data.CrosshairEnabled)
                            {
                                CrosshairCanvas.Visibility = Visibility.Visible;
                                DrawCrosshair(data);
                            }
                            else
                            {
                                CrosshairCanvas.Visibility = Visibility.Collapsed;
                            }
                        }
                        else
                        {
                            // Hide everything if the user disabled GameBar plugin inside GHOSTWing
                            StatsContainer.Visibility = Visibility.Collapsed;
                            CrosshairCanvas.Visibility = Visibility.Collapsed;
                            ToastContainer.Visibility = Visibility.Collapsed;
                        }

                        // Toast Update
                        if (data.ToastId != 0 && data.ToastId != _lastToastId && !string.IsNullOrEmpty(data.ToastMessage))
                        {
                            _lastToastId = data.ToastId;
                            txtToast.Text = data.ToastMessage;
                            ToastContainer.Visibility = Visibility.Visible;
                            _toastTimer.Stop();
                            _toastTimer.Start();
                        }
                    }
                    catch (Exception ex)
                    {
                        txtToast.Text = "Error: " + ex.Message;
                        ToastContainer.Visibility = Visibility.Visible;
                    }
                });
            }
            catch { }
        }

        private void DrawCrosshair(GHOSTWingPayload data)
        {
            CrosshairCanvas.Children.Clear();
            
            Color color = Colors.Lime; // Default
            if (data.CrosshairColorIndex == 1) color = Colors.Red;
            if (data.CrosshairColorIndex == 3) color = Colors.DodgerBlue;
            if (data.CrosshairColorIndex == 4) color = Colors.Yellow;
            if (data.CrosshairColorIndex == 5) color = Colors.Cyan;
            if (data.CrosshairColorIndex == 6) color = Colors.Magenta;
            if (data.CrosshairColorIndex == 0) color = Colors.White;

            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Opacity = Math.Clamp(data.CrosshairOpacity / 100.0, 0.0, 1.0);

            double cx = 0;
            double cy = 0;

            if (data.CrosshairShapeIndex == 0) // Cross
            {
                Rectangle top = new Rectangle { Width = data.CrosshairThickness, Height = data.CrosshairSize, Fill = brush };
                Canvas.SetLeft(top, cx - data.CrosshairThickness / 2);
                Canvas.SetTop(top, cy - data.CrosshairGap - data.CrosshairSize);

                Rectangle bottom = new Rectangle { Width = data.CrosshairThickness, Height = data.CrosshairSize, Fill = brush };
                Canvas.SetLeft(bottom, cx - data.CrosshairThickness / 2);
                Canvas.SetTop(bottom, cy + data.CrosshairGap);

                Rectangle left = new Rectangle { Width = data.CrosshairSize, Height = data.CrosshairThickness, Fill = brush };
                Canvas.SetLeft(left, cx - data.CrosshairGap - data.CrosshairSize);
                Canvas.SetTop(left, cy - data.CrosshairThickness / 2);

                Rectangle right = new Rectangle { Width = data.CrosshairSize, Height = data.CrosshairThickness, Fill = brush };
                Canvas.SetLeft(right, cx + data.CrosshairGap);
                Canvas.SetTop(right, cy - data.CrosshairThickness / 2);

                CrosshairCanvas.Children.Add(top);
                CrosshairCanvas.Children.Add(bottom);
                CrosshairCanvas.Children.Add(left);
                CrosshairCanvas.Children.Add(right);
            }

            if (data.CrosshairDot)
            {
                Ellipse dot = new Ellipse { Width = data.CrosshairThickness, Height = data.CrosshairThickness, Fill = brush };
                Canvas.SetLeft(dot, cx - data.CrosshairThickness / 2);
                Canvas.SetTop(dot, cy - data.CrosshairThickness / 2);
                CrosshairCanvas.Children.Add(dot);
            }
        }
    }

    public class GHOSTWingPayload
    {
        public float VerticalPull { get; set; }
        public int CycleDelay { get; set; }
        public bool UseGameBarOverlay { get; set; }
        
        public bool StatsEnabled { get; set; }
        public int StatsColorIndex { get; set; }
        public double StatsSize { get; set; }
        public int StatsX { get; set; }
        public int StatsY { get; set; }
        
        public bool CrosshairEnabled { get; set; }
        public int CrosshairShapeIndex { get; set; }
        public int CrosshairColorIndex { get; set; }
        public double CrosshairSize { get; set; }
        public double CrosshairThickness { get; set; }
        public double CrosshairGap { get; set; }
        public double CrosshairOpacity { get; set; }
        public bool CrosshairDot { get; set; }
        public bool CrosshairOutline { get; set; }
        
        public string ToastMessage { get; set; }
        public long ToastId { get; set; }
    }
}
