using System;
using System.Collections.Generic;
using System.Linq;
using HololensSatelliteViewer.Models;

namespace HololensSatelliteViewer.Services
{
    /// <summary>
    /// Pure satellite selection logic — extracted from SatelliteRenderer so it
    /// can be unit tested directly (no UWP/SharpDX dependencies).
    /// </summary>
    public static class SatelliteSelection
    {
        /// <summary>
        /// Ranks satellites by elevation (best-visible first) and returns the
        /// top maxCount. Deliberately NOT ranked by range: geostationary
        /// satellites (GOES etc.) are ~35,000+ km away and would ALWAYS lose
        /// to nearby LEO objects in a range sort, even when high in the sky.
        /// </summary>
        public static List<Satellite> BestVisible(
            IEnumerable<Satellite> aboveHorizon, int maxCount)
        {
            return aboveHorizon
                .OrderByDescending(s => s.Elevation)
                .Take(maxCount)
                .ToList();
        }
    }
}
