using System;
using Windows.Devices.Sensors;

namespace HololensSatelliteViewer.Services
{
    /// <summary>
    /// Wraps the HoloLens 1 compass sensor (Windows.Devices.Sensors.Compass).
    /// Provides magnetic heading (0–360°, 0=North, 90=East) for rotating
    /// the satellite dome so it tracks the user's physical orientation.
    /// </summary>
    public sealed class CompassService : IDisposable
    {
        private Compass _compass;
        private bool _disposed;

        /// <summary>Latest magnetic heading in degrees (0–360). 0 if compass unavailable.</summary>
        public float CurrentHeadingDegrees { get; private set; }

        /// <summary>Raised when the heading changes (on a background thread).</summary>
        public event EventHandler<float> HeadingChanged;

        /// <summary>
        /// Initializes the compass sensor. No-op if the sensor is unavailable.
        /// Call once at app startup.
        /// </summary>
        public void Initialize()
        {
            if (_disposed) return;

            _compass = Compass.GetDefault();
            if (_compass == null)
            {
                return;
            }

            // Set report interval — use minimum supported, cap at ~60 Hz
            uint minInterval = _compass.MinimumReportInterval;
            _compass.ReportInterval = minInterval > 16 ? minInterval : 16;

            _compass.ReadingChanged += OnReadingChanged;
        }

        private void OnReadingChanged(Compass sender, CompassReadingChangedEventArgs args)
        {
            CompassReading reading = args.Reading;
            double? magneticHeading = reading.HeadingMagneticNorth;

            if (magneticHeading.HasValue)
            {
                float heading = (float)magneticHeading.Value;
                // Normalize to [0, 360)
                heading = (heading + 360f) % 360f;
                CurrentHeadingDegrees = heading;
                HeadingChanged?.Invoke(this, heading);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_compass != null)
            {
                _compass.ReadingChanged -= OnReadingChanged;
                _compass.ReportInterval = 0;
                _compass = null;
            }
        }
    }
}
