using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using HololensSatelliteViewer.Models;
using HololensSatelliteViewer.Services;

namespace VerifySatellites
{
    /// <summary>
    /// Probes whether specific GOES satellites are above the observer's horizon
    /// using the production Sgp4Service + OrbitService ENU math (the private
    /// ComputeObserverRelativePosition is invoked via reflection so the real
    /// production code is exercised, not a copy).
    ///
    /// GOES 2 (NORAD 04278) is the retired 1977 bird — CelesTrak has no TLE for
    /// it ("No GP data found"), which is itself the answer for that satellite.
    /// The active GOES 16/17/18/19 are the living equivalents.
    /// </summary>
    internal static class GoesProbe
    {
        private static readonly (string Name, int NoradId)[] Targets =
        {
            ("GOES 2", 4278),    // retired 1977 — expect "no TLE data"
            ("GOES 16", 41866),
            ("GOES 17", 43226),
            ("GOES 18", 51850),
            ("GOES 19", 60133),
        };

        public static async Task RunAsync(double lat, double lon)
        {
            Console.WriteLine();
            Console.WriteLine("── GOES visibility probe (production Sgp4 + ENU math) ──");
            Console.WriteLine($"Observer: {lat:F4}N {lon:F4}E");
            Console.WriteLine();

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            foreach (var (name, noradId) in Targets)
            {
                try
                {
                    using var resp = await http.GetAsync(
                        $"https://celestrak.org/NORAD/elements/gp.php?CATNR={noradId}&FORMAT=tle");
                    var raw = await resp.Content.ReadAsStringAsync();

                    // Decommissioned / untracked satellites (e.g. GOES 2) return
                    // 404 or "No GP data found" — that IS the answer.
                    if (!resp.IsSuccessStatusCode || raw.Contains("No GP data"))
                    {
                        Console.WriteLine($"{name,-9} (NORAD {noradId}): NO TLE DATA (decommissioned/untracked)");
                        continue;
                    }

                    var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length < 3)
                    {
                        Console.WriteLine($"{name,-9} (NORAD {noradId}): NO TLE DATA (malformed response)");
                        continue;
                    }

                    // 3-line TLE format: name / line1 / line2
                    var tle = new TleRecord
                    {
                        Name = lines[0].Trim(),
                        NoradId = noradId,
                        Line1 = lines[1].Trim(),
                        Line2 = lines[2].Trim()
                    };

                    var sgp4 = new Sgp4Service();
                    var sat = sgp4.Propagate(tle, DateTime.UtcNow);

                    // Call the production ENU math via reflection
                    var orbit = new OrbitService();
                    orbit.SetObserverLocation(lat, lon, 0.0);
                    var method = typeof(OrbitService).GetMethod(
                        "ComputeObserverRelativePosition",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    method.Invoke(orbit, new object[] { sat, DateTime.UtcNow });

                    Console.WriteLine(
                        $"{name,-9} (NORAD {noradId}): elev {sat.Elevation,6:F1}°  az {sat.Azimuth,6:F1}°  " +
                        $"range {sat.RangeKm,8:F0} km  lon {sat.Longitude,7:F1}°  alt {sat.AltitudeKm,7:F0} km  " +
                        $"→ {(sat.Elevation > 0.0 ? "ABOVE HORIZON" : "below horizon")}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{name,-9} (NORAD {noradId}): ERROR {ex.Message}");
                }
            }
        }
    }
}
