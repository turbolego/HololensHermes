using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HololensSatelliteViewer.Models;

namespace HololensSatelliteViewer.Services
{
    public class OrbitService
    {
        private readonly TleService _tleService;
        private readonly Sgp4Service _sgp4Service;

        private List<TleRecord> _tleRecords;
        private DateTime _lastTleUpdate;

        private double _observerLatitude = 60.3913;
        private double _observerLongitude = 5.3221;
        private double _observerAltitudeKm = 0.0;

        public OrbitService()
        {
            _tleService = new TleService();
            _sgp4Service = new Sgp4Service();
            _tleRecords = new List<TleRecord>();
            _lastTleUpdate = DateTime.MinValue;
        }

        public async Task<List<Satellite>> GetLiveSatellitesAsync()
        {
            await EnsureTleDataAsync();

            var utcNow = DateTime.UtcNow;
            var satellites = new List<Satellite>();

            foreach (var tle in _tleRecords)
            {
                try
                {
                    var sat = _sgp4Service.Propagate(tle, utcNow);
                    ComputeObserverRelativePosition(sat, utcNow);
                    satellites.Add(sat);
                }
                catch
                {
                }
            }

            return satellites
                .Where(s => s.Elevation > 0.0)
                .OrderByDescending(s => s.Elevation)
                .Take(60)
                .ToList();
        }

        private async Task EnsureTleDataAsync()
        {
            var age = DateTime.UtcNow - _lastTleUpdate;

            if (age.TotalHours < 6.0 && _tleRecords.Count > 0)
            {
                return;
            }

            try
            {
                var stations = await _tleService.DownloadStationTlesAsync();
                var active = await _tleService.DownloadActiveTlesAsync(120);

                _tleRecords = stations
                    .Concat(active)
                    .GroupBy(r => r.NoradId > 0 ? r.NoradId.ToString() : r.Name)
                    .Select(g => g.First())
                    .Take(120)
                    .ToList();

                _lastTleUpdate = DateTime.UtcNow;
            }
            catch
            {
            }
        }

        private void ComputeObserverRelativePosition(Satellite sat, DateTime utcTime)
        {
            var observerX = 0.0;
            var observerY = 0.0;
            var observerZ = 0.0;

            var latRad = _observerLatitude * Math.PI / 180.0;
            var lonRad = _observerLongitude * Math.PI / 180.0;

            var earthRadiusKm = 6378.137;
            var radius = earthRadiusKm + _observerAltitudeKm;

            observerX = radius * Math.Cos(latRad) * Math.Cos(lonRad);
            observerY = radius * Math.Cos(latRad) * Math.Sin(lonRad);
            observerZ = radius * Math.Sin(latRad);

            var dx = sat.X - observerX;
            var dy = sat.Y - observerY;
            var dz = sat.Z - observerZ;

            var range = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            var sinLat = Math.Sin(latRad);
            var cosLat = Math.Cos(latRad);
            var sinLon = Math.Sin(lonRad);
            var cosLon = Math.Cos(lonRad);

            var south = dx * cosLon + dy * sinLon;
            var east = -dx * sinLon + dy * cosLon;
            var zenith = dz * cosLat - (dx * cosLon + dy * sinLon) * sinLat;

            var azimuth = Math.Atan2(east, -south);
            if (azimuth < 0.0)
            {
                azimuth += 2.0 * Math.PI;
            }

            var elevation = Math.Asin(zenith / range);

            sat.Azimuth = azimuth * 180.0 / Math.PI;
            sat.Elevation = elevation * 180.0 / Math.PI;
            sat.RangeKm = range;
        }

        public void SetObserverLocation(double latitude, double longitude, double altitudeKm)
        {
            _observerLatitude = latitude;
            _observerLongitude = longitude;
            _observerAltitudeKm = altitudeKm;
        }
    }
}
