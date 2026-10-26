using System;
using System.Collections.Generic;
using HololensHermes.Navigation;
using Xunit;

namespace HololensHermes.Tests
{
    public sealed class AssistantJourneyTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero);

        private static readonly VenueFootprint Venue = new VenueFootprint(
            "venue:library",
            "City Library",
            northLatitude: 59.9114,
            southLatitude: 59.9103,
            eastLongitude: 10.7530,
            westLongitude: 10.7508);

        [Fact]
        public void location_calibration_and_hermes_target_produce_consistent_guidance()
        {
            var location = LocationEstimate.Available(59.91088, 10.75202, 20.0, Now);
            var venue = new VenueResolver(40.0, TimeSpan.FromMinutes(2))
                .Resolve(location, new[] { Venue }, Now);

            Assert.True(venue.IsResolved);
            Assert.Equal("venue:library", venue.Venue.Id);

            var transform = FloorPlanTransform.Create(
                new List<PlanPoint>
                {
                    new PlanPoint(0.0, 0.0),
                    new PlanPoint(100.0, 0.0),
                    new PlanPoint(0.0, 100.0),
                    new PlanPoint(100.0, 100.0)
                },
                new List<WorldPoint>
                {
                    new WorldPoint(3.0, -2.0),
                    new WorldPoint(13.0, -2.0),
                    new WorldPoint(3.0, 8.0),
                    new WorldPoint(13.0, 8.0)
                });

            var hermesTarget = new PlanPoint(80.0, 20.0);
            var worldTarget = transform.Map(hermesTarget);
            Assert.Equal(11.0, worldTarget.X, 8);
            Assert.Equal(0.0, worldTarget.Z, 8);

            var route = new NavigationRoute(new List<NavigationWaypoint>
            {
                new NavigationWaypoint("Returns desk", "Ground", hermesTarget, true)
            });
            var instruction = AccessibleGuidanceEngine.Evaluate(
                route,
                waypointIndex: 0,
                userPosition: new PlanPoint(20.0, 20.0),
                headingRadians: 0.0,
                metersPerPlanUnit: transform.Scale,
                positionalTrackingActive: true,
                calibrationAvailable: true);

            Assert.Equal(GuidanceState.Proceed, instruction.State);
            Assert.Equal(GuidanceDirection.Ahead, instruction.Direction);
            Assert.Equal(6.0, instruction.DistanceMeters, 8);
            Assert.Equal("Returns desk", instruction.Waypoint.Label);
            Assert.Contains("Go ahead", instruction.SpokenPrompt);

            var arrival = AccessibleGuidanceEngine.Evaluate(
                route,
                waypointIndex: 0,
                userPosition: hermesTarget,
                headingRadians: 0.0,
                metersPerPlanUnit: transform.Scale,
                positionalTrackingActive: true,
                calibrationAvailable: true);

            Assert.Equal(GuidanceState.Arrived, arrival.State);
            Assert.False(arrival.ShouldAdvanceWaypoint);
            Assert.Contains("arrived", arrival.SpokenPrompt, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void venue_ambiguity_blocks_the_journey_before_route_guidance()
        {
            var nearbyVenue = new VenueFootprint(
                "venue:nearby",
                "Nearby venue",
                northLatitude: 59.9114,
                southLatitude: 59.9103,
                eastLongitude: 10.7532,
                westLongitude: 10.7510);
            var location = LocationEstimate.Available(59.91088, 10.75210, 40.0, Now);

            var result = new VenueResolver(40.0, TimeSpan.FromMinutes(2))
                .Resolve(location, new[] { Venue, nearbyVenue }, Now);

            Assert.False(result.IsResolved);
            Assert.Equal(VenueResolutionStatus.Ambiguous, result.Status);
        }

        [Fact]
        public void loss_of_tracking_pauses_an_active_route_even_at_the_goal()
        {
            var goal = new PlanPoint(0.2, 0.0);
            var route = new NavigationRoute(new List<NavigationWaypoint>
            {
                new NavigationWaypoint("Information desk", "Ground", goal, true)
            });

            var instruction = AccessibleGuidanceEngine.Evaluate(
                route,
                waypointIndex: 0,
                userPosition: goal,
                headingRadians: 0.0,
                metersPerPlanUnit: 1.0,
                positionalTrackingActive: false,
                calibrationAvailable: true);

            Assert.Equal(GuidanceState.Unavailable, instruction.State);
            Assert.False(instruction.IsMovementSafe);
            Assert.False(instruction.ShouldAdvanceWaypoint);
            Assert.Contains("Stop", instruction.SpokenPrompt);
        }
    }
}
