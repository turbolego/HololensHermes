using System;
using System.Collections.Generic;
using HololensHermes.Navigation;
using Xunit;

namespace HololensHermes.Tests
{
    public sealed class GeoMathTests
    {
        [Fact]
        public void distance_between_identical_coordinates_is_zero()
        {
            var coordinate = new GeoCoordinate(59.91088, 10.75202);

            Assert.Equal(0.0, GeoMath.DistanceMeters(coordinate, coordinate), 8);
        }

        [Fact]
        public void distance_for_one_degree_of_latitude_has_expected_magnitude()
        {
            var distance = GeoMath.DistanceMeters(
                new GeoCoordinate(59.9, 10.75),
                new GeoCoordinate(60.9, 10.75));

            Assert.InRange(distance, 111000.0, 112500.0);
        }

        [Fact]
        public void invalid_coordinates_are_rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => LocationEstimate.Available(91.0, 10.0, 20.0, DateTimeOffset.UtcNow));
        }
    }

    public sealed class VenueResolverTests
    {
        private static readonly VenueFootprint Deichman = new VenueFootprint(
            "mazemap:363",
            "Deichman Bjørvika",
            northLatitude: 59.9114,
            southLatitude: 59.9103,
            eastLongitude: 10.7530,
            westLongitude: 10.7508);

        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

        [Fact]
        public void estimate_inside_footprint_resolves_the_venue()
        {
            var estimate = LocationEstimate.Available(59.91088, 10.75202, 25.0, Now);
            var result = Resolver().Resolve(estimate, new[] { Deichman }, Now);

            Assert.True(result.IsResolved);
            Assert.Equal(VenueResolutionStatus.Resolved, result.Status);
            Assert.Equal("mazemap:363", result.Venue.Id);
            Assert.Equal(0.0, result.DistanceToVenueMeters, 8);
        }

        [Fact]
        public void estimate_thirty_metres_outside_footprint_resolves_when_uncertainty_reaches_it()
        {
            var latitudeThirtyMetresSouth = Deichman.SouthLatitude - (30.0 / 111195.0);
            var estimate = LocationEstimate.Available(latitudeThirtyMetresSouth, 10.75202, 40.0, Now);

            var result = Resolver().Resolve(estimate, new[] { Deichman }, Now);

            Assert.True(result.IsResolved);
            Assert.InRange(result.DistanceToVenueMeters, 29.0, 31.0);
        }

        [Fact]
        public void estimate_outside_uncertainty_circle_is_not_resolved()
        {
            var latitudeOneHundredMetresSouth = Deichman.SouthLatitude - (100.0 / 111195.0);
            var estimate = LocationEstimate.Available(latitudeOneHundredMetresSouth, 10.75202, 40.0, Now);

            var result = Resolver().Resolve(estimate, new[] { Deichman }, Now);

            Assert.False(result.IsResolved);
            Assert.Equal(VenueResolutionStatus.NoMatchingVenue, result.Status);
        }

        [Fact]
        public void estimate_with_excessive_accuracy_radius_requires_user_confirmation()
        {
            var estimate = LocationEstimate.Available(59.91088, 10.75202, 75.0, Now);
            var result = Resolver().Resolve(estimate, new[] { Deichman }, Now);

            Assert.False(result.IsResolved);
            Assert.Equal(VenueResolutionStatus.LowAccuracy, result.Status);
        }

        [Fact]
        public void stale_location_is_rejected()
        {
            var estimate = LocationEstimate.Available(59.91088, 10.75202, 25.0, Now - TimeSpan.FromMinutes(3));
            var result = Resolver().Resolve(estimate, new[] { Deichman }, Now);

            Assert.False(result.IsResolved);
            Assert.Equal(VenueResolutionStatus.Stale, result.Status);
        }

        [Fact]
        public void overlapping_candidates_are_reported_as_ambiguous()
        {
            var secondVenue = new VenueFootprint(
                "venue:second",
                "Nearby venue",
                59.9114,
                59.9103,
                10.7532,
                10.7510);
            var estimate = LocationEstimate.Available(59.91088, 10.75210, 40.0, Now);

            var result = Resolver().Resolve(estimate, new[] { Deichman, secondVenue }, Now);

            Assert.False(result.IsResolved);
            Assert.Equal(VenueResolutionStatus.Ambiguous, result.Status);
        }

        [Fact]
        public void unavailable_location_is_not_treated_as_zero_coordinate()
        {
            var result = Resolver().Resolve(LocationEstimate.Unavailable("denied"), new[] { Deichman }, Now);

            Assert.False(result.IsResolved);
            Assert.Equal(VenueResolutionStatus.Unavailable, result.Status);
        }

        private static VenueResolver Resolver()
        {
            return new VenueResolver(40.0, TimeSpan.FromMinutes(2));
        }
    }

    public sealed class FloorPlanTransformTests
    {
        [Fact]
        public void calibration_uses_all_pairs_to_map_a_target_in_world_metres()
        {
            var image = new List<PlanPoint>
            {
                new PlanPoint(0.0, 0.0),
                new PlanPoint(100.0, 0.0),
                new PlanPoint(0.0, 100.0),
                new PlanPoint(100.0, 100.0)
            };
            var world = new List<WorldPoint>
            {
                new WorldPoint(3.0, -2.0),
                new WorldPoint(13.0, -2.0),
                new WorldPoint(3.0, 8.0),
                new WorldPoint(13.0, 8.0)
            };

            var transform = FloorPlanTransform.Create(image, world);
            var target = transform.Map(new PlanPoint(40.0, 70.0));

            Assert.Equal(0.1, transform.Scale, 8);
            Assert.Equal(0.0, transform.RotationRadians, 8);
            Assert.Equal(7.0, target.X, 8);
            Assert.Equal(5.0, target.Z, 8);
        }

        [Fact]
        public void calibration_recovers_rotation_and_translation()
        {
            var image = new List<PlanPoint>
            {
                new PlanPoint(0.0, 0.0),
                new PlanPoint(10.0, 0.0),
                new PlanPoint(0.0, 10.0)
            };
            var world = new List<WorldPoint>
            {
                new WorldPoint(5.0, 9.0),
                new WorldPoint(5.0, 29.0),
                new WorldPoint(-15.0, 9.0)
            };

            var transform = FloorPlanTransform.Create(image, world);
            var target = transform.Map(new PlanPoint(2.0, 3.0));

            Assert.Equal(2.0, transform.Scale, 8);
            Assert.Equal(Math.PI / 2.0, transform.RotationRadians, 8);
            Assert.Equal(-1.0, target.X, 8);
            Assert.Equal(13.0, target.Z, 8);
        }

        [Fact]
        public void coincident_image_calibration_points_are_rejected()
        {
            var image = new List<PlanPoint>
            {
                new PlanPoint(1.0, 1.0),
                new PlanPoint(1.0, 1.0),
                new PlanPoint(1.0, 1.0)
            };
            var world = new List<WorldPoint>
            {
                new WorldPoint(0.0, 0.0),
                new WorldPoint(1.0, 0.0),
                new WorldPoint(0.0, 1.0)
            };

            Assert.Throws<ArgumentException>(() => FloorPlanTransform.Create(image, world));
        }

        [Fact]
        public void calibration_requires_three_matching_pairs()
        {
            Assert.Throws<ArgumentException>(() => FloorPlanTransform.Create(
                new List<PlanPoint> { new PlanPoint(0.0, 0.0), new PlanPoint(1.0, 0.0) },
                new List<WorldPoint> { new WorldPoint(0.0, 0.0), new WorldPoint(1.0, 0.0) }));
        }
    }

    public sealed class NavigationRouteTests
    {
        [Fact]
        public void route_requires_the_final_waypoint_to_be_the_goal()
        {
            var route = new NavigationRoute(new List<NavigationWaypoint>
            {
                new NavigationWaypoint("Main entrance", "L1", new PlanPoint(10.0, 20.0), false),
                new NavigationWaypoint("Lift", "L1", new PlanPoint(40.0, 20.0), false),
                new NavigationWaypoint("Col — 3M.2.06.B", "3M", new PlanPoint(80.0, 40.0), true)
            });

            Assert.Equal(3, route.Waypoints.Count);
            Assert.True(route.Waypoints[2].IsGoal);
            Assert.Equal("Col — 3M.2.06.B", route.Waypoints[2].Label);
        }

        [Fact]
        public void route_rejects_non_goal_final_waypoint()
        {
            Assert.Throws<ArgumentException>(() => new NavigationRoute(new List<NavigationWaypoint>
            {
                new NavigationWaypoint("Main entrance", "L1", new PlanPoint(10.0, 20.0), false)
            }));
        }
    }
}
