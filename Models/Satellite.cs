using System;

namespace HololensSatelliteViewer.Models
{
    public class Satellite
    {
        public string Name { get; set; }
        public int NoradId { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double AltitudeKm { get; set; }

        public double Azimuth { get; set; }
        public double Elevation { get; set; }
        public double RangeKm { get; set; }

        public double VelocityKmPerSec { get; set; }

        public DateTime Timestamp { get; set; }

        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}
