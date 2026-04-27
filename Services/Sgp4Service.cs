using System;
using HololensSatelliteViewer.Models;

namespace HololensSatelliteViewer.Services
{
    public class Sgp4Service
    {
        private const double TwoPi = 2.0 * Math.PI;
        private const double MinutesPerDay = 1440.0;
        private const double EarthRadiusKm = 6378.137;
        private const double Xke = 0.0743669161;
        private const double J2 = 0.00108262998905;

        public Satellite Propagate(TleRecord tle, DateTime utcTime)
        {
            var elements = ParseTle(tle.Line1, tle.Line2);
            var minutesSinceEpoch = (utcTime - elements.Epoch).TotalMinutes;

            var n = elements.MeanMotion * TwoPi / MinutesPerDay;
            var a = Math.Pow(Xke / n, 2.0 / 3.0);
            var delta = 1.5 * J2 * (3.0 * elements.CosI * elements.CosI - 1.0) / Math.Pow(1.0 - elements.Eccentricity * elements.Eccentricity, 1.5);
            var nPrime = n / (1.0 + delta);
            var aPrime = a * (1.0 - delta);

            var M = elements.MeanAnomaly + nPrime * minutesSinceEpoch;
            M = M % TwoPi;

            var E = SolveKepler(M, elements.Eccentricity);

            var cosE = Math.Cos(E);
            var sinE = Math.Sin(E);

            var nu = 2.0 * Math.Atan2(Math.Sqrt(1.0 + elements.Eccentricity) * sinE, Math.Sqrt(1.0 - elements.Eccentricity) * (cosE - elements.Eccentricity));

            var r = aPrime * (1.0 - elements.Eccentricity * cosE);

            var u = elements.ArgumentOfPerigee + nu;
            var cosU = Math.Cos(u);
            var sinU = Math.Sin(u);

            var x = r * cosU;
            var y = r * sinU;

            var cosRaan = Math.Cos(elements.Raan);
            var sinRaan = Math.Sin(elements.Raan);
            var cosI = elements.CosI;
            var sinI = Math.Sin(Math.Acos(cosI));

            var xEci = x * cosRaan - y * cosI * sinRaan;
            var yEci = x * sinRaan + y * cosI * cosRaan;
            var zEci = y * sinI;

            var lat = Math.Atan2(zEci, Math.Sqrt(xEci * xEci + yEci * yEci));
            var lon = Math.Atan2(yEci, xEci);

            var altitudeKm = Math.Sqrt(xEci * xEci + yEci * yEci + zEci * zEci) - EarthRadiusKm;

            var velocity = Math.Sqrt(398600.4418 / r);

            return new Satellite
            {
                Name = tle.Name,
                NoradId = tle.NoradId,
                Latitude = lat * 180.0 / Math.PI,
                Longitude = lon * 180.0 / Math.PI,
                AltitudeKm = altitudeKm,
                VelocityKmPerSec = velocity,
                Timestamp = utcTime,
                X = xEci,
                Y = yEci,
                Z = zEci
            };
        }

        private double SolveKepler(double M, double e)
        {
            var E = M;
            for (int i = 0; i < 10; i++)
            {
                var dE = (M - E + e * Math.Sin(E)) / (1.0 - e * Math.Cos(E));
                E += dE;
                if (Math.Abs(dE) < 1e-8)
                {
                    break;
                }
            }
            return E;
        }

        private OrbitalElements ParseTle(string line1, string line2)
        {
            var epochYear = int.Parse(line1.Substring(18, 2));
            var epochDay = double.Parse(line1.Substring(20, 12));

            var fullYear = epochYear < 57 ? 2000 + epochYear : 1900 + epochYear;
            var epoch = new DateTime(fullYear, 1, 1).AddDays(epochDay - 1.0);

            var inclinationDeg = double.Parse(line2.Substring(8, 8).Trim());
            var raanDeg = double.Parse(line2.Substring(17, 8).Trim());
            var eccentricity = double.Parse("0." + line2.Substring(26, 7).Trim());
            var argumentOfPerigeeDeg = double.Parse(line2.Substring(34, 8).Trim());
            var meanAnomalyDeg = double.Parse(line2.Substring(43, 8).Trim());
            var meanMotion = double.Parse(line2.Substring(52, 11).Trim());

            return new OrbitalElements
            {
                Epoch = epoch,
                Inclination = inclinationDeg * Math.PI / 180.0,
                Raan = raanDeg * Math.PI / 180.0,
                Eccentricity = eccentricity,
                ArgumentOfPerigee = argumentOfPerigeeDeg * Math.PI / 180.0,
                MeanAnomaly = meanAnomalyDeg * Math.PI / 180.0,
                MeanMotion = meanMotion,
                CosI = Math.Cos(inclinationDeg * Math.PI / 180.0)
            };
        }

        private class OrbitalElements
        {
            public DateTime Epoch { get; set; }
            public double Inclination { get; set; }
            public double Raan { get; set; }
            public double Eccentricity { get; set; }
            public double ArgumentOfPerigee { get; set; }
            public double MeanAnomaly { get; set; }
            public double MeanMotion { get; set; }
            public double CosI { get; set; }
        }
    }
}
