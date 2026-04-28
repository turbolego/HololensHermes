using System;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace HololensSatelliteViewer.Services
{
    /// <summary>
    /// Service for obtaining HoloLens device GPS location.
    /// Used to calculate satellite positions relative to the user.
    /// </summary>
    public class GeolocationService
    {
        private Geolocator _geolocator;
        private Geoposition _lastPosition;

        public GeolocationService()
        {
            _geolocator = new Geolocator
            {
                DesiredAccuracyInMeters = 50,
                ReportInterval = 5000 // Update every 5 seconds
            };
        }

        /// <summary>
        /// Get current GPS position of the HoloLens device.
        /// </summary>
        public async Task<Geoposition> GetCurrentLocationAsync()
        {
            try
            {
                var accessStatus = await Geolocator.RequestAccessAsync();
                if (accessStatus != GeolocationAccessStatus.Allowed)
                {
                    System.Diagnostics.Debug.WriteLine("Location access denied");
                    return _lastPosition;
                }

                var position = await _geolocator.GetGeopositionAsync(
                    maximumAge: TimeSpan.FromSeconds(10),
                    timeout: TimeSpan.FromSeconds(30)
                );

                if (position != null)
                {
                    _lastPosition = position;
                }

                return position;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting location: {ex.Message}");
                return _lastPosition;
            }
        }

        /// <summary>
        /// Get the last known position without waiting for an update.
        /// </summary>
        public Geoposition GetLastKnownLocation()
        {
            return _lastPosition;
        }
    }
}
