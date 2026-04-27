using System;
using System;
using System.Collections.Generic;
using Windows.System.Display;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using HololensSatelliteViewer.Services;
using HololensSatelliteViewer.Models;

namespace HololensSatelliteViewer
{
    public sealed partial class MainPage : Page
    {
        private readonly OrbitService _orbitService;
        private readonly DispatcherTimer _fetchTimer;
        private readonly DispatcherTimer _renderTimer;
        private DisplayRequest _displayRequest;

        private List<Satellite> _satellites;
        private double _pulse;
        private bool _isFetching;

        private const double HorizonRadiusPx = 300.0;

        public MainPage()
        {
            InitializeComponent();

            _orbitService = new OrbitService();
            _satellites = new List<Satellite>();

            _fetchTimer = new DispatcherTimer();
            _fetchTimer.Interval = TimeSpan.FromSeconds(2);
            _fetchTimer.Tick += FetchTimerTick;

            _renderTimer = new DispatcherTimer();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(33);
            _renderTimer.Tick += RenderTimerTick;

            Loaded += MainPageLoaded;
            Unloaded += MainPageUnloaded;
        }

        private async void MainPageLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _displayRequest = new DisplayRequest();
                _displayRequest.RequestActive();

                StatusText.Text = "Initializing satellite tracker...";
                await RefreshSatellitesAsync();
                _fetchTimer.Start();
                _renderTimer.Start();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
            }
        }

        private void MainPageUnloaded(object sender, RoutedEventArgs e)
        {
            _fetchTimer.Stop();
            _renderTimer.Stop();

            if (_displayRequest != null)
            {
                _displayRequest.RequestRelease();
            }
        }

        private async void FetchTimerTick(object sender, object e)
        {
            await RefreshSatellitesAsync();
        }

        private void RenderTimerTick(object sender, object e)
        {
            _pulse += 0.15;
            RenderSatellites();
        }

        private async System.Threading.Tasks.Task RefreshSatellitesAsync()
        {
            if (_isFetching)
            {
                return;
            }

            _isFetching = true;

            try
            {
                _satellites = await _orbitService.GetLiveSatellitesAsync();

                if (_satellites.Count > 0)
                {
                    StatusText.Text = "Tracking satellites in real-time";
                }
                else
                {
                    StatusText.Text = "Waiting for visible satellites...";
                }

                CountText.Text = $"Satellites: {_satellites.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message.Substring(0, Math.Min(50, ex.Message.Length));
            }
            finally
            {
                _isFetching = false;
            }
        }

        private void RenderSatellites()
        {
            HologramCanvas.Children.Clear();

            var canvasWidth = HologramCanvas.ActualWidth;
            var canvasHeight = HologramCanvas.ActualHeight;

            if (canvasWidth < 10 || canvasHeight < 10)
            {
                StatusText.Text = "Canvas not ready...";
                return;
            }

            var centerX = canvasWidth * 0.5;
            var centerY = canvasHeight * 0.5;

            DrawHorizonCircle(centerX, centerY);
            DrawCompassMarks(centerX, centerY);

            foreach (var sat in _satellites)
            {
                if (sat.Elevation < 0.0)
                {
                    continue;
                }

                var azimuthRad = sat.Azimuth * Math.PI / 180.0;
                var elevationRad = sat.Elevation * Math.PI / 180.0;

                var normalizedRadius = (90.0 - sat.Elevation) / 90.0;
                var radius = normalizedRadius * HorizonRadiusPx;

                var x = centerX + radius * Math.Sin(azimuthRad);
                var y = centerY - radius * Math.Cos(azimuthRad);

                var pulseOffset = Math.Sin(_pulse + (sat.NoradId % 100) * 0.1) * 1.5;
                var size = 12.0 + pulseOffset + (sat.Elevation / 90.0) * 6.0;

                var brightness = 0.5 + (sat.Elevation / 90.0) * 0.5;
                var colorValue = (byte)(255 * brightness);

                var ellipse = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(Color.FromArgb(255, 255, (byte)(180 * brightness), 0)),
                    Stroke = new SolidColorBrush(Color.FromArgb(200, colorValue, colorValue, 255)),
                    StrokeThickness = 2.0
                };

                Canvas.SetLeft(ellipse, x - size * 0.5);
                Canvas.SetTop(ellipse, y - size * 0.5);

                HologramCanvas.Children.Add(ellipse);

                if (sat.Elevation > 30.0 && _satellites.Count < 15)
                {
                    var label = new TextBlock
                    {
                        Text = sat.Name,
                        Foreground = new SolidColorBrush(Color.FromArgb(200, colorValue, colorValue, 255)),
                        FontSize = 12,
                        FontWeight = Windows.UI.Text.FontWeights.SemiBold
                    };

                    Canvas.SetLeft(label, x + size * 0.7);
                    Canvas.SetTop(label, y - size * 0.5);

                    HologramCanvas.Children.Add(label);
                }
            }
        }

        private void DrawHorizonCircle(double centerX, double centerY)
        {
            var horizon = new Ellipse
            {
                Width = HorizonRadiusPx * 2.0,
                Height = HorizonRadiusPx * 2.0,
                Stroke = new SolidColorBrush(Color.FromArgb(100, 100, 150, 200)),
                StrokeThickness = 2.0
            };

            Canvas.SetLeft(horizon, centerX - HorizonRadiusPx);
            Canvas.SetTop(horizon, centerY - HorizonRadiusPx);

            HologramCanvas.Children.Add(horizon);
        }

        private void DrawCompassMarks(double centerX, double centerY)
        {
            var directions = new[]
            {
                new { Label = "N", Azimuth = 0.0 },
                new { Label = "E", Azimuth = 90.0 },
                new { Label = "S", Azimuth = 180.0 },
                new { Label = "W", Azimuth = 270.0 }
            };

            foreach (var dir in directions)
            {
                var rad = dir.Azimuth * Math.PI / 180.0;
                var x = centerX + (HorizonRadiusPx + 20.0) * Math.Sin(rad);
                var y = centerY - (HorizonRadiusPx + 20.0) * Math.Cos(rad);

                var text = new TextBlock
                {
                    Text = dir.Label,
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 150, 200, 255)),
                    FontSize = 20,
                    FontWeight = Windows.UI.Text.FontWeights.Bold
                };

                Canvas.SetLeft(text, x - 10.0);
                Canvas.SetTop(text, y - 10.0);

                HologramCanvas.Children.Add(text);
            }
        }
    }
}

