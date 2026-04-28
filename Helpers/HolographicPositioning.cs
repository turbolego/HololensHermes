using System;
using System.Numerics;
using Windows.Devices.Geolocation;
using Windows.Perception.Spatial;
using Windows.UI.Xaml.Controls;

namespace HololensSatelliteViewer.Helpers
{
    /// <summary>
    /// Helper class to convert satellite positions from Earth-relative coordinates
    /// to HoloLens world-space holographic coordinates
    /// </summary>
    public class HolographicPositioning
    {
        private SpatialStationaryFrameOfReference _worldOrigin;
        private Geopoint _userLocation;

        public void InitializeWorldOrigin(SpatialCoordinateSystem coordinateSystem)
        {
            _worldOrigin = SpatialLocator.GetDefault()?.CreateStationaryFrameOfReferenceAtCurrentLocation();
        }

        public void UpdateUserLocation(Geopoint location)
        {
            _userLocation = location;
        }

        /// <summary>
        /// Convert satellite lat/lon/altitude to Vector3 position relative to user
        /// </summary>
        public Vector3 SatelliteToWorldPosition(double satLat, double satLon, double satAltKm)
        {
            if (_userLocation == null) return Vector3.Zero;

            // Earth radius in km
            const double earthRadius = 6371.0;

            // Convert to ECEF (Earth-Centered Earth-Fixed) coordinates
            var userEcef = LatLonAltToECEF(
                _userLocation.Position.Latitude,
                _userLocation.Position.Longitude,
                _userLocation.Position.Altitude / 1000.0);

            var satEcef = LatLonAltToECEF(satLat, satLon, satAltKm);

            // Get vector from user to satellite
            var relX = satEcef.X - userEcef.X;
            var relY = satEcef.Y - userEcef.Y;
            var relZ = satEcef.Z - userEcef.Z;

            // Scale down for visualization (1 km = 1 meter in hologram space)
            var scale = 0.001f; // Adjust this for best viewing distance

            return new Vector3(
                (float)(relX * scale),
                (float)(relZ * scale),  // Z becomes up in HoloLens space
                (float)(-relY * scale)  // Y becomes forward
            );
        }

        private Vector3 LatLonAltToECEF(double latDeg, double lonDeg, double altKm)
        {
            var lat = latDeg * Math.PI / 180.0;
            var lon = lonDeg * Math.PI / 180.0;
            var radius = 6371.0 + altKm;

            var x = radius * Math.Cos(lat) * Math.Cos(lon);
            var y = radius * Math.Cos(lat) * Math.Sin(lon);
            var z = radius * Math.Sin(lat);

            return new Vector3((float)x, (float)y, (float)z);
        }
    }
}
