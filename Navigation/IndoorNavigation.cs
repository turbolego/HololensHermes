using System;
using System.Collections.Generic;

namespace HololensHermes.Navigation
{
    /// <summary>
    /// A WGS-84 latitude/longitude coordinate. Coordinates are deliberately kept
    /// separate from HoloLens world coordinates: outdoor positioning only selects
    /// a venue, while spatial mapping and calibration perform indoor placement.
    /// </summary>
    public sealed class GeoCoordinate
    {
        public GeoCoordinate(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        public double Latitude { get; private set; }
        public double Longitude { get; private set; }

        public bool IsValid
        {
            get
            {
                return !double.IsNaN(Latitude) && !double.IsInfinity(Latitude) &&
                       !double.IsNaN(Longitude) && !double.IsInfinity(Longitude) &&
                       Latitude >= -90.0 && Latitude <= 90.0 &&
                       Longitude >= -180.0 && Longitude <= 180.0;
            }
        }
    }

    /// <summary>
    /// A location estimate supplied by the Windows location service. Accuracy is
    /// an uncertainty radius in metres, not a claim that the user is at the exact
    /// coordinate. The navigation flow must request calibration before placing a
    /// target whenever this estimate selected the venue.
    /// </summary>
    public sealed class LocationEstimate
    {
        private LocationEstimate()
        {
        }

        public GeoCoordinate Coordinate { get; private set; }
        public double AccuracyMeters { get; private set; }
        public DateTimeOffset TimestampUtc { get; private set; }
        public string Error { get; private set; }

        public bool IsAvailable
        {
            get { return Coordinate != null && Coordinate.IsValid && string.IsNullOrEmpty(Error); }
        }

        public static LocationEstimate Available(
            double latitude,
            double longitude,
            double accuracyMeters,
            DateTimeOffset timestampUtc)
        {
            if (double.IsNaN(accuracyMeters) || double.IsInfinity(accuracyMeters) || accuracyMeters < 0.0)
                throw new ArgumentOutOfRangeException("accuracyMeters");

            var coordinate = new GeoCoordinate(latitude, longitude);
            if (!coordinate.IsValid)
                throw new ArgumentOutOfRangeException("latitude", "A valid WGS-84 coordinate is required.");

            return new LocationEstimate
            {
                Coordinate = coordinate,
                AccuracyMeters = accuracyMeters,
                TimestampUtc = timestampUtc.ToUniversalTime()
            };
        }

        public static LocationEstimate Unavailable(string error)
        {
            return new LocationEstimate
            {
                Error = string.IsNullOrWhiteSpace(error) ? "location unavailable" : error,
                TimestampUtc = DateTimeOffset.UtcNow
            };
        }

        public bool IsUsable(double maximumAccuracyMeters, TimeSpan maximumAge, DateTimeOffset nowUtc)
        {
            if (!IsAvailable || AccuracyMeters > maximumAccuracyMeters || maximumAge < TimeSpan.Zero)
                return false;

            var age = nowUtc.ToUniversalTime() - TimestampUtc;
            return age >= TimeSpan.Zero && age <= maximumAge;
        }
    }

    public static class GeoMath
    {
        private const double EarthRadiusMeters = 6371008.8;

        /// <summary>
        /// Computes great-circle distance using the haversine formula. This stays
        /// accurate for both small indoor-venue proximity checks and wider venue
        /// disambiguation, unlike a latitude-only approximation.
        /// </summary>
        public static double DistanceMeters(GeoCoordinate first, GeoCoordinate second)
        {
            if (first == null) throw new ArgumentNullException("first");
            if (second == null) throw new ArgumentNullException("second");
            if (!first.IsValid || !second.IsValid) throw new ArgumentException("Both coordinates must be valid.");

            var latitudeDelta = DegreesToRadians(second.Latitude - first.Latitude);
            var longitudeDelta = DegreesToRadians(second.Longitude - first.Longitude);
            var latitude1 = DegreesToRadians(first.Latitude);
            var latitude2 = DegreesToRadians(second.Latitude);

            var sinLatitude = Math.Sin(latitudeDelta / 2.0);
            var sinLongitude = Math.Sin(longitudeDelta / 2.0);
            var a = sinLatitude * sinLatitude +
                    Math.Cos(latitude1) * Math.Cos(latitude2) * sinLongitude * sinLongitude;
            a = Math.Min(1.0, Math.Max(0.0, a));
            return EarthRadiusMeters * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }

        internal static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }

    /// <summary>
    /// An axis-aligned WGS-84 venue footprint. It intentionally represents a
    /// coarse building boundary: it is suitable for venue selection from Wi-Fi,
    /// but never substitutes for HoloLens spatial anchors indoors.
    /// </summary>
    public sealed class VenueFootprint
    {
        public VenueFootprint(
            string id,
            string displayName,
            double northLatitude,
            double southLatitude,
            double eastLongitude,
            double westLongitude)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A venue id is required.", "id");
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A venue name is required.", "displayName");
            if (northLatitude < southLatitude) throw new ArgumentException("North latitude must be greater than south latitude.");
            if (eastLongitude < westLongitude) throw new ArgumentException("East longitude must be greater than west longitude.");

            Id = id;
            DisplayName = displayName;
            NorthLatitude = northLatitude;
            SouthLatitude = southLatitude;
            EastLongitude = eastLongitude;
            WestLongitude = westLongitude;
        }

        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public double NorthLatitude { get; private set; }
        public double SouthLatitude { get; private set; }
        public double EastLongitude { get; private set; }
        public double WestLongitude { get; private set; }

        public bool Contains(GeoCoordinate coordinate)
        {
            if (coordinate == null || !coordinate.IsValid) return false;
            return coordinate.Latitude >= SouthLatitude && coordinate.Latitude <= NorthLatitude &&
                   coordinate.Longitude >= WestLongitude && coordinate.Longitude <= EastLongitude;
        }

        /// <summary>
        /// Returns zero inside the footprint and a conservative lower-bound
        /// distance to the nearest footprint point outside it.
        /// </summary>
        public double DistanceToMeters(GeoCoordinate coordinate)
        {
            if (coordinate == null) throw new ArgumentNullException("coordinate");
            if (Contains(coordinate)) return 0.0;

            var nearestLatitude = Math.Max(SouthLatitude, Math.Min(NorthLatitude, coordinate.Latitude));
            var nearestLongitude = Math.Max(WestLongitude, Math.Min(EastLongitude, coordinate.Longitude));
            return GeoMath.DistanceMeters(coordinate, new GeoCoordinate(nearestLatitude, nearestLongitude));
        }
    }

    public enum VenueResolutionStatus
    {
        Resolved,
        Unavailable,
        Stale,
        LowAccuracy,
        NoMatchingVenue,
        Ambiguous
    }

    public sealed class VenueResolution
    {
        public VenueResolutionStatus Status { get; internal set; }
        public VenueFootprint Venue { get; internal set; }
        public double DistanceToVenueMeters { get; internal set; }
        public string Message { get; internal set; }

        public bool IsResolved
        {
            get { return Status == VenueResolutionStatus.Resolved && Venue != null; }
        }
    }

    /// <summary>
    /// Resolves Wi-Fi-derived location estimates conservatively. A venue is
    /// selected only if the estimate's uncertainty circle reaches one footprint
    /// and cannot plausibly reach a competing footprint at the same distance.
    /// </summary>
    public sealed class VenueResolver
    {
        public VenueResolver(double maximumAccuracyMeters, TimeSpan maximumAge)
        {
            if (maximumAccuracyMeters <= 0.0) throw new ArgumentOutOfRangeException("maximumAccuracyMeters");
            if (maximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("maximumAge");

            MaximumAccuracyMeters = maximumAccuracyMeters;
            MaximumAge = maximumAge;
        }

        public double MaximumAccuracyMeters { get; private set; }
        public TimeSpan MaximumAge { get; private set; }

        public VenueResolution Resolve(
            LocationEstimate estimate,
            IEnumerable<VenueFootprint> venues,
            DateTimeOffset nowUtc)
        {
            if (estimate == null || !estimate.IsAvailable)
            {
                return Failure(VenueResolutionStatus.Unavailable, "A usable Windows location estimate is required.");
            }

            var age = nowUtc.ToUniversalTime() - estimate.TimestampUtc;
            if (age < TimeSpan.Zero || age > MaximumAge)
            {
                return Failure(VenueResolutionStatus.Stale, "The location estimate is stale; refresh it before selecting a venue.");
            }

            if (estimate.AccuracyMeters > MaximumAccuracyMeters)
            {
                return Failure(VenueResolutionStatus.LowAccuracy, "The location uncertainty is too large to safely select a venue.");
            }

            if (venues == null) throw new ArgumentNullException("venues");

            VenueFootprint bestVenue = null;
            var bestDistance = double.MaxValue;
            var secondDistance = double.MaxValue;

            foreach (var venue in venues)
            {
                if (venue == null) continue;
                var distance = venue.DistanceToMeters(estimate.Coordinate);
                if (distance < bestDistance)
                {
                    secondDistance = bestDistance;
                    bestDistance = distance;
                    bestVenue = venue;
                }
                else if (distance < secondDistance)
                {
                    secondDistance = distance;
                }
            }

            if (bestVenue == null || bestDistance > estimate.AccuracyMeters)
            {
                return Failure(VenueResolutionStatus.NoMatchingVenue, "The Wi-Fi uncertainty circle does not reach a known venue footprint.");
            }

            if (secondDistance <= estimate.AccuracyMeters && Math.Abs(secondDistance - bestDistance) < 5.0)
            {
                return Failure(VenueResolutionStatus.Ambiguous, "The location estimate overlaps multiple nearby venues; ask the user to select one.");
            }

            return new VenueResolution
            {
                Status = VenueResolutionStatus.Resolved,
                Venue = bestVenue,
                DistanceToVenueMeters = bestDistance,
                Message = bestDistance == 0.0
                    ? "Venue selected from the current location estimate."
                    : "Venue selected because its footprint intersects the location uncertainty circle."
            };
        }

        private static VenueResolution Failure(VenueResolutionStatus status, string message)
        {
            return new VenueResolution
            {
                Status = status,
                DistanceToVenueMeters = double.NaN,
                Message = message
            };
        }
    }

    /// <summary>A point in floor-plan image coordinates.</summary>
    public struct PlanPoint
    {
        public PlanPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; private set; }
        public double Y { get; private set; }
    }

    /// <summary>A point in HoloLens world X/Z metres. Y is supplied by the renderer.</summary>
    public struct WorldPoint
    {
        public WorldPoint(double x, double z)
        {
            X = x;
            Z = z;
        }

        public double X { get; private set; }
        public double Z { get; private set; }
    }

    /// <summary>
    /// Similarity transform from floor-plan image pixels to local world metres.
    /// The fit uses all supplied calibration pairs, rather than allowing the
    /// first two taps to dominate placement as the earlier scaffold did.
    /// </summary>
    public sealed class FloorPlanTransform
    {
        private const double DegenerateThreshold = 0.0000001;

        private FloorPlanTransform(double scale, double rotationRadians, double translationX, double translationZ)
        {
            Scale = scale;
            RotationRadians = rotationRadians;
            TranslationX = translationX;
            TranslationZ = translationZ;
        }

        public double Scale { get; private set; }
        public double RotationRadians { get; private set; }
        public double TranslationX { get; private set; }
        public double TranslationZ { get; private set; }

        public WorldPoint Map(PlanPoint point)
        {
            var cosine = Math.Cos(RotationRadians);
            var sine = Math.Sin(RotationRadians);
            return new WorldPoint(
                TranslationX + Scale * (cosine * point.X - sine * point.Y),
                TranslationZ + Scale * (sine * point.X + cosine * point.Y));
        }

        public static FloorPlanTransform Create(IList<PlanPoint> imagePoints, IList<WorldPoint> worldPoints)
        {
            if (imagePoints == null) throw new ArgumentNullException("imagePoints");
            if (worldPoints == null) throw new ArgumentNullException("worldPoints");
            if (imagePoints.Count != worldPoints.Count || imagePoints.Count < 3)
                throw new ArgumentException("At least three matching calibration pairs are required.");

            double imageCenterX = 0.0;
            double imageCenterY = 0.0;
            double worldCenterX = 0.0;
            double worldCenterZ = 0.0;
            for (var i = 0; i < imagePoints.Count; i++)
            {
                imageCenterX += imagePoints[i].X;
                imageCenterY += imagePoints[i].Y;
                worldCenterX += worldPoints[i].X;
                worldCenterZ += worldPoints[i].Z;
            }

            imageCenterX /= imagePoints.Count;
            imageCenterY /= imagePoints.Count;
            worldCenterX /= imagePoints.Count;
            worldCenterZ /= imagePoints.Count;

            double dot = 0.0;
            double cross = 0.0;
            double imageEnergy = 0.0;
            for (var i = 0; i < imagePoints.Count; i++)
            {
                var px = imagePoints[i].X - imageCenterX;
                var py = imagePoints[i].Y - imageCenterY;
                var wx = worldPoints[i].X - worldCenterX;
                var wz = worldPoints[i].Z - worldCenterZ;

                dot += px * wx + py * wz;
                cross += px * wz - py * wx;
                imageEnergy += px * px + py * py;
            }

            if (imageEnergy < DegenerateThreshold)
                throw new ArgumentException("Calibration image points must not all be coincident.", "imagePoints");

            var scale = Math.Sqrt(dot * dot + cross * cross) / imageEnergy;
            if (scale < DegenerateThreshold || double.IsNaN(scale) || double.IsInfinity(scale))
                throw new ArgumentException("Calibration world points do not define a usable transform.", "worldPoints");

            var rotation = Math.Atan2(cross, dot);
            var cosine = Math.Cos(rotation);
            var sine = Math.Sin(rotation);
            var translationX = worldCenterX - scale * (cosine * imageCenterX - sine * imageCenterY);
            var translationZ = worldCenterZ - scale * (sine * imageCenterX + cosine * imageCenterY);

            return new FloorPlanTransform(scale, rotation, translationX, translationZ);
        }
    }

    public sealed class NavigationWaypoint
    {
        public NavigationWaypoint(string label, string floorId, PlanPoint location, bool isGoal)
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A waypoint label is required.", "label");
            Label = label;
            FloorId = floorId ?? string.Empty;
            Location = location;
            IsGoal = isGoal;
        }

        public string Label { get; private set; }
        public string FloorId { get; private set; }
        public PlanPoint Location { get; private set; }
        public bool IsGoal { get; private set; }
    }

    public sealed class NavigationRoute
    {
        public NavigationRoute(IList<NavigationWaypoint> waypoints)
        {
            if (waypoints == null || waypoints.Count == 0)
                throw new ArgumentException("A route must contain at least one waypoint.", "waypoints");
            if (!waypoints[waypoints.Count - 1].IsGoal)
                throw new ArgumentException("The final route waypoint must be the navigation goal.", "waypoints");

            Waypoints = new List<NavigationWaypoint>(waypoints).AsReadOnly();
        }

        public IList<NavigationWaypoint> Waypoints { get; private set; }
    }
}
