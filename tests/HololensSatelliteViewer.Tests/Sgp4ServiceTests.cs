using System;
using HololensSatelliteViewer.Models;
using HololensSatelliteViewer.Services;
using Xunit;

namespace HololensSatelliteViewer.Tests
{
    /// <summary>
    /// SGP4 propagation sanity checks using the REAL GOES 16 TLE. Verifies the
    /// SGP4 implementation produces sane geostationary positions (altitude
    /// ~35,786 km, sub-satellite point near the equator) — the class of
    /// satellite the app previously never even loaded.
    /// </summary>
    public class Sgp4ServiceTests
    {
        private static readonly TleRecord Goes16 = new TleRecord
        {
            Name = "GOES 16",
            NoradId = 41866,
            Line1 = "1 41866U 16071A   26219.91793109 -.00000092  00000+0  00000+0 0  9999",
            Line2 = "2 41866   0.4595  85.1808 0001203 116.9608 340.0248  1.00270594 35622"
        };

        [Fact]
        public void Propagate_Goes16_ProducesGeostationaryAltitude()
        {
            var sgp4 = new Sgp4Service();
            var sat = sgp4.Propagate(Goes16, DateTime.UtcNow);

            // Geostationary altitude ≈ 35,786 km; allow generous drift slack
            Assert.InRange(sat.AltitudeKm, 35000, 36500);
        }

        [Fact]
        public void Propagate_Goes16_StaysNearEquator()
        {
            var sgp4 = new Sgp4Service();
            var sat = sgp4.Propagate(Goes16, DateTime.UtcNow);

            // GOES 16 inclination ~0.46° — sub-satellite latitude must hug 0°
            Assert.InRange(sat.Latitude, -2.0, 2.0);
        }

        [Fact]
        public void Propagate_ProducesFiniteEciCoordinates()
        {
            var sgp4 = new Sgp4Service();
            var sat = sgp4.Propagate(Goes16, DateTime.UtcNow);

            Assert.False(double.IsNaN(sat.X));
            Assert.False(double.IsNaN(sat.Y));
            Assert.False(double.IsNaN(sat.Z));
            // ECI position must be a few Earth radii out (~42,164 km for GEO)
            var r = Math.Sqrt(sat.X * sat.X + sat.Y * sat.Y + sat.Z * sat.Z);
            Assert.InRange(r, 40000, 45000);
        }
    }
}
