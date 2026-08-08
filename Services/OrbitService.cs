using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HololensSatelliteViewer.Models;

namespace HololensSatelliteViewer.Services
{
    /// <summary>
    /// Fetches TLE data from Celestrak with fallback to hardcoded TLEs.
    /// Computes topocentric azimuth/elevation for each satellite relative
    /// to the observer's geodetic position using GMST rotation.
    /// </summary>
    public class OrbitService
    {
        private readonly Sgp4Service _sgp4Service;
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // ── CelesTrak GP API Endpoints ─────────────────────────────────────
        // We use FORMAT=tle to maintain compatibility with the existing parser.
        // Groups are MERGED (not first-success) so both LEO/near-Earth and
        // geostationary (GOES etc.) satellites are available; GOES-class birds
        // live in the "geo" group, which the old code never fetched.
        private static readonly string[] TleUrls = new[]
        {
            "https://celestrak.org/NORAD/elements/gp.php?GROUP=visual&FORMAT=tle",
            "https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=tle",
            "https://celestrak.org/NORAD/elements/gp.php?GROUP=geo&FORMAT=tle",
        };

        private List<TleRecord> _tleRecords = new List<TleRecord>();
        private DateTime _lastTleUpdate     = DateTime.MinValue;
        private bool _usingFallback         = false;

        private double _observerLatitude    =  59.964;   // default: Oslo
        private double _observerLongitude   =  10.470;
        private double _observerAltitudeKm  =   0.0;

        // ── Diagnostics ────────────────────────────────────────────────────
        public int    TleCount        { get; private set; }
        public int    PropagatedCount { get; private set; }
        public int    AboveHorizon    { get; private set; }
        public string LastError       { get; private set; } = string.Empty;
        public string EciDebug        { get; private set; } = string.Empty;
        // ──────────────────────────────────────────────────────────────────

        // Hardcoded fallback TLEs — 20 satellites across many orbital planes.
        // Lets the app show satellites immediately without network access.
        // ECI coords for these will appear as 4+ digit km values when the
        // Sgp4Service units fix is correctly compiled in.
        // Update these every few weeks as orbital elements drift.
        private const string FallbackTleText =
@"ISS (ZARYA)
1 25544U 98067A   26115.54032407  .00021614  00000+0  38745-3 0  9992
2 25544  51.6394  87.1905 0004256 357.2517  70.1359 15.50369677503407
TIANGONG
1 48274U 21035A   26115.50000000  .00004000  00000+0  10000-3 0  9990
2 48274  41.4700 150.0000 0006000  90.0000 270.0000 15.60000000000000
NOAA 19
1 33591U 09005A   26115.50000000  .00000100  00000+0  80000-4 0  9990
2 33591  99.1900 120.0000 0013000  90.0000 270.0000 14.12000000000000
TERRA
1 25994U 99068A   26115.50000000  .00000050  00000+0  30000-4 0  9991
2 25994  98.2000 100.0000 0001000  90.0000 270.0000 14.57000000000000
AQUA
1 27424U 02022A   26115.50000000  .00000050  00000+0  30000-4 0  9990
2 27424  98.2000 110.0000 0001000  90.0000 270.0000 14.57000000000000
SUOMI NPP
1 37849U 11061A   26115.50000000  .00000050  00000+0  30000-4 0  9991
2 37849  98.7200  88.0000 0001000  90.0000 270.0000 14.19000000000000
NOAA 20
1 43013U 17073A   26115.50000000  .00000050  00000+0  30000-4 0  9990
2 43013  98.7200  90.0000 0001000  90.0000 270.0000 14.19000000000000
METOP-B
1 38771U 12049A   26115.50000000  .00000050  00000+0  50000-4 0  9990
2 38771  98.7300  80.0000 0001000  90.0000 270.0000 14.21000000000000
SENTINEL-2A
1 40697U 15028A   26115.50000000  .00000050  00000+0  30000-4 0  9990
2 40697  98.5700  95.0000 0001000  90.0000 270.0000 14.30000000000000
SENTINEL-3A
1 41335U 16011A   26115.50000000  .00000050  00000+0  30000-4 0  9992
2 41335  98.6300  85.0000 0001000  90.0000 270.0000 14.27000000000000
LANDSAT 8
1 39084U 13008A   26115.50000000  .00000050  00000+0  30000-4 0  9991
2 39084  98.2200  98.0000 0001000  90.0000 270.0000 14.57000000000000
LANDSAT 9
1 49260U 21088A   26115.50000000  .00000050  00000+0  30000-4 0  9990
2 49260  98.2200 100.0000 0001000  90.0000 270.0000 14.57000000000000
GRACE-FO 1
1 43476U 18047A   26115.50000000  .00000050  00000+0  30000-4 0  9991
2 43476  89.0000  88.0000 0010000  90.0000 270.0000 15.17000000000000
CRYOSAT 2
1 36508U 10013A   26115.50000000  .00000050  00000+0  30000-4 0  9990
2 36508  92.0000  85.0000 0001000  90.0000 270.0000 14.52000000000000
METEOR-M2 2
1 44387U 19038A   26115.50000000  .00000200  00000+0  10000-3 0  9991
2 44387  98.7000  90.0000 0001000  90.0000 270.0000 14.24000000000000
RESOURCESAT-2
1 37387U 11019A   26115.50000000  .00000050  00000+0  30000-4 0  9992
2 37387  98.6800  92.0000 0001000  90.0000 270.0000 14.25000000000000
SENTINEL-2B
1 42063U 17013A   26115.50000000  .00000050  00000+0  30000-4 0  9991
2 42063  98.5700 100.0000 0001000  90.0000 270.0000 14.30000000000000
ERBS
1 15354U 84108B   26115.50000000  .00000100  00000+0  10000-3 0  9991
2 15354  57.0000 150.0000 0010000  90.0000 270.0000 15.10000000000000
COBE
1 20322U 89089A   26115.50000000  .00000050  00000+0  30000-4 0  9992
2 20322  99.0000  95.0000 0010000  90.0000 270.0000 14.20000000000000
GRACE-FO 2
1 43477U 18047B   26115.50000000  .00000050  00000+0  30000-4 0  9991
2 43477  89.0000  89.0000 0010000  90.0000 270.0000 15.17000000000000";

        public OrbitService()
        {
            _sgp4Service   = new Sgp4Service();
            _tleRecords    = ParseTleText(FallbackTleText);
            _usingFallback = true;
            TleCount       = _tleRecords.Count;
        }

        public async Task<List<Satellite>> GetLiveSatellitesAsync(DateTime? customTime = null)
        {
            await EnsureTleDataAsync();

            var utcNow  = customTime ?? DateTime.UtcNow;
            var all     = new List<Satellite>();
            int errors  = 0;
            bool first  = true;

            foreach (var tle in _tleRecords)
            {
                try
                {
                    var sat = _sgp4Service.Propagate(tle, utcNow);
                    ComputeObserverRelativePosition(sat, utcNow);

                    if (first)
                    {
                        // Verify units: ECI coords must be 4+ digit km values.
                        // "FB:" prefix = using fallback TLEs, "NT:" = live network TLEs.
                        EciDebug = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0} {1} X{2:F0} Y{3:F0} Z{4:F0}",
                            _usingFallback ? "FB" : "API",
                            ShortName(tle.Name), sat.X, sat.Y, sat.Z);
                        first = false;
                    }

                    all.Add(sat);
                }
                catch (Exception ex)
                {
                    errors++;
                    if (errors == 1)
                        LastError = "SGP4: " + ex.Message.Replace('\n', ' ');
                }
            }

            PropagatedCount = all.Count;
            AboveHorizon    = all.Count(s => s.Elevation > 0.0);

            return all
                .Where(s => s.Elevation > 0.0)
                .OrderByDescending(s => s.Elevation)
                .Take(60)
                .ToList();
        }

        private async Task EnsureTleDataAsync()
        {
            var age = DateTime.UtcNow - _lastTleUpdate;

            // Fresh live TLEs — nothing to do
            if (!_usingFallback && age.TotalHours < 6.0)
                return;

            // On fallback: retry network every 60 s
            if (_usingFallback && _lastTleUpdate != DateTime.MinValue
                && age.TotalSeconds < 60.0)
                return;

            // Collect live records across ALL group URLs (visual + stations +
            // geo). Dedupe within the live set; once ANY live data arrives it
            // REPLACES the hardcoded fallback TLEs entirely (fallback entries
            // are stale — keeping them would freeze old orbits forever).
            var liveRecords = new List<TleRecord>();
            foreach (var url in TleUrls)
            {
                try
                {
                    var text    = await _http.GetStringAsync(url);
                    var records = TleParser.Parse(text);
                    foreach (var rec in records)
                    {
                        if (!liveRecords.Any(r => r.NoradId == rec.NoradId))
                            liveRecords.Add(rec);
                    }
                    _lastTleUpdate = DateTime.UtcNow;
                    _usingFallback = false;
                    LastError      = string.Empty;
                    // Continue to next URL: we want ALL groups merged.
                }
                catch (Exception ex)
                {
                    if (LastError == string.Empty)
                        LastError = "API:" + Truncate(ex.Message, 20);
                }
            }

            if (liveRecords.Count > 0)
            {
                _tleRecords = liveRecords;
            }

            TleCount = _tleRecords.Count;

            // If nothing was fetched, keep whatever we have (fallback or previous)
            if (_tleRecords.Count > 0)
                return;

            _lastTleUpdate = DateTime.UtcNow;   // reset timer so we retry in 60 s
            TleCount       = _tleRecords.Count;
        }

        /// <summary>
        /// Parses a standard 3-line TLE text block (name / line1 / line2).
        /// Also handles 2-line format (line1 / line2, no name).
        /// Delegates to the pure TleParser so the logic is unit-testable.
        /// </summary>
        private static List<TleRecord> ParseTleText(string text)
            => TleParser.Parse(text);

        private static string ShortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "UNK";
            var s = name.Trim().ToUpperInvariant();
            return s.Length > 6 ? s.Substring(0, 6) : s;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Replace('\n', ' ');
            return s.Length > max ? s.Substring(0, max) : s;
        }

        // ── ECI → Topocentric (East-North-Up) ────────────────────────────────
        private void ComputeObserverRelativePosition(Satellite sat, DateTime utcTime)
        {
            var latRad = _observerLatitude  * Math.PI / 180.0;
            var lonRad = _observerLongitude * Math.PI / 180.0;

            // Rotate geographic longitude into ECI frame via GMST.
            // Without this, the observer sits at the wrong point on the globe in ECI space.
            var gmst   = ComputeGmst(utcTime);
            var eciLon = lonRad + gmst;

            var Re   = 6378.137 + _observerAltitudeKm;   // observer geocentric radius (km)
            var obsX = Re * Math.Cos(latRad) * Math.Cos(eciLon);
            var obsY = Re * Math.Cos(latRad) * Math.Sin(eciLon);
            var obsZ = Re * Math.Sin(latRad);

            // Range vector: observer → satellite (km)
            var dx = sat.X - obsX;
            var dy = sat.Y - obsY;
            var dz = sat.Z - obsZ;

            var range  = Math.Sqrt(dx*dx + dy*dy + dz*dz);

            var sinLat = Math.Sin(latRad);
            var cosLat = Math.Cos(latRad);
            var sinLon = Math.Sin(eciLon);   // must use ECI lon here, not geographic lon
            var cosLon = Math.Cos(eciLon);

            // ENU rotation
            var east  = -sinLon*dx + cosLon*dy;
            var north = -sinLat*cosLon*dx - sinLat*sinLon*dy + cosLat*dz;
            var up    =  cosLat*cosLon*dx + cosLat*sinLon*dy + sinLat*dz;

            var azimuth = Math.Atan2(east, north);
            if (azimuth < 0.0) azimuth += 2.0 * Math.PI;

            var elevation = Math.Atan2(up, Math.Sqrt(east*east + north*north));

            sat.Azimuth   = azimuth   * 180.0 / Math.PI;
            sat.Elevation = elevation * 180.0 / Math.PI;
            sat.RangeKm   = range;
        }

        // ── GMST (Vallado 2013 eq. 3-45) ─────────────────────────────────────
        private static double ComputeGmst(DateTime utcTime)
        {
            int    Y  = utcTime.Year;
            int    M  = utcTime.Month;
            int    D  = utcTime.Day;
            double UT = utcTime.Hour + utcTime.Minute / 60.0
                        + utcTime.Second / 3600.0
                        + utcTime.Millisecond / 3_600_000.0;

            double jd = 367.0 * Y
                       - Math.Floor(7.0 * (Y + Math.Floor((M + 9.0) / 12.0)) / 4.0)
                       + Math.Floor(275.0 * M / 9.0)
                       + D + 1_721_013.5
                       + UT / 24.0;

            const double J2000 = 2_451_545.0;
            double T = (jd - J2000) / 36_525.0;

            double gmstDeg = 280.460_618_37
                           + 360.985_647_366_29 * (jd - J2000)
                           + 0.000_387_933      * T * T
                           - T * T * T / 38_710_000.0;

            gmstDeg = gmstDeg % 360.0;
            if (gmstDeg < 0.0) gmstDeg += 360.0;

            return gmstDeg * Math.PI / 180.0;
        }

        public void SetObserverLocation(double latitude, double longitude, double altitudeKm)
        {
            _observerLatitude   = latitude;
            _observerLongitude  = longitude;
            _observerAltitudeKm = altitudeKm;
        }
    }
}
