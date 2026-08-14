using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using HololensHermes.Navigation;

namespace HololensHermes.Services
{
    /// <summary>
    /// Adapts Windows location services to the platform-neutral navigation core.
    /// On HoloLens 1 this may be estimated from available network signals; callers
    /// must use the reported uncertainty to select a venue and must still perform
    /// local spatial calibration before drawing an indoor route.
    /// </summary>
    public sealed class LocationService
    {
        private readonly Geolocator _geolocator;

        public LocationService(uint desiredAccuracyMeters = 40)
        {
            _geolocator = new Geolocator
            {
                DesiredAccuracy = PositionAccuracy.Default,
                DesiredAccuracyInMeters = desiredAccuracyMeters,
                MovementThreshold = 5.0
            };
        }

        public async Task<LocationEstimate> GetCurrentEstimateAsync(CancellationToken cancellationToken)
        {
            try
            {
                var access = await Geolocator.RequestAccessAsync();
                if (access != GeolocationAccessStatus.Allowed)
                {
                    return LocationEstimate.Unavailable("Location permission is not granted.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                var position = await _geolocator.GetGeopositionAsync(
                    maximumAge: TimeSpan.FromMinutes(2),
                    timeout: TimeSpan.FromSeconds(15));

                cancellationToken.ThrowIfCancellationRequested();
                var point = position.Coordinate.Point.Position;
                return LocationEstimate.Available(
                    point.Latitude,
                    point.Longitude,
                    position.Coordinate.Accuracy,
                    position.Coordinate.Timestamp);
            }
            catch (UnauthorizedAccessException)
            {
                return LocationEstimate.Unavailable("Location permission is denied.");
            }
            catch (TaskCanceledException)
            {
                return LocationEstimate.Unavailable("Location request timed out.");
            }
            catch (Exception)
            {
                // Do not expose implementation or network details to the wearer.
                return LocationEstimate.Unavailable("Location is temporarily unavailable.");
            }
        }
    }
}
