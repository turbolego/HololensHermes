using System.Collections.Generic;
using System.Linq;
using HololensSatelliteViewer.Models;
using HololensSatelliteViewer.Services;
using Xunit;

namespace HololensSatelliteViewer.Tests
{
    /// <summary>
    /// Tests for satellite selection — pinning the fix that geostationary
    /// satellites (GOES etc., ~35,000+ km away) must rank by ELEVATION, not
    /// by range. The old OrderBy(RangeKm) made a GEO bird lose to any nearby
    /// LEO object and never render, even when high in the sky.
    /// </summary>
    public class SatelliteSelectionTests
    {
        private static Satellite Sat(string name, double elev, double rangeKm)
        {
            return new Satellite
            {
                Name = name,
                Elevation = elev,
                RangeKm = rangeKm,
                Azimuth = 90.0
            };
        }

        [Fact]
        public void BestVisible_GoesSatellite_BeatsNearbyLeo()
        {
            // GOES 16 at 23° elevation, 39,000 km away (geostationary)
            // vs a LEO object at 5°, 1,300 km away.
            // The old range-ranking would pick the LEO object every time.
            var sats = new List<Satellite>
            {
                Sat("LEO debris", 5.0, 1300),
                Sat("GOES 16", 23.4, 39000)
            };

            var result = SatelliteSelection.BestVisible(sats, 1);

            var top = Assert.Single(result);
            Assert.Equal("GOES 16", top.Name);
        }

        [Fact]
        public void BestVisible_ReturnsTopByElevation()
        {
            var sats = new List<Satellite>
            {
                Sat("low", 2.0, 900),
                Sat("high", 40.0, 36000),
                Sat("mid", 15.0, 1000)
            };

            var result = SatelliteSelection.BestVisible(sats, 3);

            Assert.Equal(new[] { "high", "mid", "low" }, result.Select(s => s.Name));
        }

        [Fact]
        public void BestVisible_RespectsMaxCount()
        {
            var sats = new List<Satellite>
            {
                Sat("a", 10.0, 900),
                Sat("b", 20.0, 900),
                Sat("c", 30.0, 900)
            };

            var result = SatelliteSelection.BestVisible(sats, 2);

            Assert.Equal(2, result.Count);
            Assert.Equal(new[] { "c", "b" }, result.Select(s => s.Name));
        }

        [Fact]
        public void BestVisible_EmptyInput_ReturnsEmpty()
        {
            Assert.Empty(SatelliteSelection.BestVisible(new List<Satellite>(), 10));
        }

        [Fact]
        public void BestVisible_AllAboveHorizon_AreCandidates()
        {
            // Mixed GEO + LEO: even with 10 slots, the highest-elevation birds
            // win regardless of range (the GOES-16-from-Kristiansand scenario).
            var sats = new List<Satellite>
            {
                Sat("GOES 16", 23.4, 39000),
                Sat("GOES 17", 14.2, 40000),
                Sat("GOES 18", 2.6, 41000),
                Sat("GOES 19", 16.4, 39800),
                Sat("ISS", 12.0, 420),
                Sat("SL-16 R/B", 31.9, 1417),
                Sat("BEIDOU-3 IGSO-1", 74.6, 35963),
                Sat("TDRS 3", 35.6, 37889),
                Sat("COSMOS 2058", 8.0, 871),
                Sat("SDO", 30.9, 38470)
            };

            var result = SatelliteSelection.BestVisible(sats, 10);

            Assert.Equal(10, result.Count);
            Assert.Contains(result, s => s.Name == "GOES 16");
            Assert.Equal("BEIDOU-3 IGSO-1", result[0].Name); // highest elevation first
        }
    }
}
