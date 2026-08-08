using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using HololensSatelliteViewer.Services;

namespace VerifySatellites
{
    /// <summary>
    /// End-to-end verification of the satellite app pipeline using the ACTUAL
    /// production code (OrbitService + Sgp4Service + TleParser + Models) and
    /// live CelesTrak TLE data — exactly like the HoloLens would.
    ///
    /// Checks:
    ///  1. The app pipeline: GetLiveSatellitesAsync → renderer top-10
    ///  2. Whether GOES-class geostationary satellites are above the horizon
    ///     from the observer fix (the satellite class the app used to never
    ///     load — GROUP=geo was missing from the TLE sources).
    ///
    /// Usage:
    ///   dotnet run                          (Lommedalen 59.9639707, 10.4698709)
    ///   dotnet run -- &lt;lat&gt; &lt;lon&gt;            (any observer GPS fix)
    /// </summary>
    internal static class Program
    {
        // Lommedalen, Norway (near Oslo) — default observer GPS fix
        private const double DefaultLat = 59.9639707;
        private const double DefaultLon = 10.4698709;

        private static async Task<int> Main(string[] args)
        {
            double lat = DefaultLat;
            double lon = DefaultLon;
            if (args.Length >= 1 && double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                lat = a;
            if (args.Length >= 2 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                lon = b;

            Console.WriteLine($"Observer GPS: {lat:F6}, {lon:F6}");
            Console.WriteLine();

            var orbit = new OrbitService();
            orbit.SetObserverLocation(lat, lon, 0.0);

            Console.WriteLine("── App pipeline (GetLiveSatellitesAsync, production code) ──");
            var live = await orbit.GetLiveSatellitesAsync();
            Console.WriteLine($"TLE records loaded:     {orbit.TleCount}  ({(orbit.LastError == "" ? "live network (visual+stations+geo)" : "fallback? " + orbit.LastError)})");
            Console.WriteLine($"Propagated OK:          {orbit.PropagatedCount}");
            Console.WriteLine($"Above horizon (elev>0): {orbit.AboveHorizon}");
            Console.WriteLine($"Returned (top 60 by elev): {live.Count}");

            Console.WriteLine();
            Console.WriteLine("── What the renderer would draw (top 10 by elevation) ──");
            Console.WriteLine($"{"Name",-14} {"El(deg)",-8} {"Az(deg)",-8} {"Range(km)",-10} {"NORAD",-7}");
            foreach (var s in SatelliteSelection.BestVisible(live, 10))
            {
                Console.WriteLine($"{s.Name,-14} {s.Elevation,8:F1} {s.Azimuth,8:F1} {s.RangeKm,10:F0} {s.NoradId,-7}");
            }

            Console.WriteLine();
            Console.WriteLine("── GOES check (geostationary, GROUP=geo) ──");
            var goes = live.Where(s => s.Name.ToUpperInvariant().Contains("GOES")).ToList();
            if (goes.Count == 0)
                Console.WriteLine("GOES satellites in app data: NONE");
            else
                foreach (var s in goes)
                    Console.WriteLine($"{s.Name}: elev {s.Elevation:F1}°  az {s.Azimuth:F1}°");

            await GoesProbe.RunAsync(lat, lon);

            return 0;
        }
    }
}
