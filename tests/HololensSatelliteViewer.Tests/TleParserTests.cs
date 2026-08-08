using System.Collections.Generic;
using System.Linq;
using HololensSatelliteViewer.Services;
using Xunit;

namespace HololensSatelliteViewer.Tests
{
    /// <summary>
    /// Tests for the pure TLE parser extracted from OrbitService.
    /// Real GOES 16 TLE (NORAD 41866) is used as a fixture so a regression in
    /// geostationary parsing — the satellite class the app used to never load —
    /// fails loudly.
    /// </summary>
    public class TleParserTests
    {
        // Real GOES 16 TLE as served by CelesTrak (3-line format, \r\n).
        private const string Goes16Tle =
            "GOES 16                 \r\n" +
            "1 41866U 16071A   26219.91793109 -.00000092  00000+0  00000+0 0  9999\r\n" +
            "2 41866   0.4595  85.1808 0001203 116.9608 340.0248  1.00270594 35622\r\n";

        [Fact]
        public void Parse_ThreeLineFormat_ParsesNameAndNorad()
        {
            var records = TleParser.Parse(Goes16Tle);

            Assert.Single(records);
            Assert.Equal("GOES 16", records[0].Name);
            Assert.Equal(41866, records[0].NoradId);
            Assert.StartsWith("1 41866", records[0].Line1);
            Assert.StartsWith("2 41866", records[0].Line2);
        }

        [Fact]
        public void Parse_TwoLineFormat_UsesUnknownName()
        {
            var tle =
                "1 41866U 16071A   26219.91793109 -.00000092  00000+0  00000+0 0  9999\r\n" +
                "2 41866   0.4595  85.1808 0001203 116.9608 340.0248  1.00270594 35622\r\n";

            var records = TleParser.Parse(tle);

            Assert.Single(records);
            Assert.Equal("UNKNOWN", records[0].Name);
            Assert.Equal(41866, records[0].NoradId);
        }

        [Fact]
        public void Parse_MultipleSatellites_ParsesAll()
        {
            var tle = Goes16Tle + Goes16Tle.Replace("41866", "43226").Replace("GOES 16", "GOES 17");

            var records = TleParser.Parse(tle);

            Assert.Equal(2, records.Count);
            Assert.Equal(new[] { 41866, 43226 }, records.Select(r => r.NoradId));
        }

        [Fact]
        public void Parse_CommentAndJunkLines_AreSkipped()
        {
            var tle =
                "# CelesTrak GP data\r\n" +
                "some junk line that is not a TLE\r\n" +
                Goes16Tle;

            var records = TleParser.Parse(tle);

            Assert.Single(records);
            Assert.Equal(41866, records[0].NoradId);
        }

        [Fact]
        public void Parse_TruncatedLines_AreRejected()
        {
            // Line1 is only 30 chars — must be skipped, not crash
            var tle =
                "GOES 16\r\n" +
                "1 41866U 16071A   26219.9\r\n" +
                "2 41866   0.4595  85.1808\r\n";

            var records = TleParser.Parse(tle);

            Assert.Empty(records);
        }

        [Fact]
        public void Parse_EmptyText_ReturnsEmpty()
        {
            Assert.Empty(TleParser.Parse(""));
            Assert.Empty(TleParser.Parse("   \r\n\r\n  "));
        }

        [Fact]
        public void Parse_CelestrakGeoGroupSample_IncludesGoesSatellites()
        {
            // Representative fragment of GROUP=geo (the group the app used to
            // never fetch — geostationary satellites were invisible).
            var tle =
                "EWS-G3 (GOES 14)        \r\n" +
                "1 25960U 75050A   26219.58666777  .00000000  00000+0  00000+0 0  9990\r\n" +
                "2 25960   3.1530 124.4750 0015069 303.5553  56.4447  1.00275841 21564\r\n" +
                Goes16Tle;

            var records = TleParser.Parse(tle);

            Assert.Equal(2, records.Count);
            Assert.Contains(records, r => r.Name == "GOES 16" && r.NoradId == 41866);
            Assert.Contains(records, r => r.Name == "EWS-G3 (GOES 14)");
        }
    }
}
