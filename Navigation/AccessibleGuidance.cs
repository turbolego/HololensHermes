using System;

namespace HololensHermes.Navigation
{
    /// <summary>
    /// The user-visible state of holographic wayfinding. States are deliberately
    /// distinct so safety does not depend on a color-only change to a hologram.
    /// </summary>
    public enum GuidanceState
    {
        Unavailable,
        CalibrationRequired,
        Reorient,
        Proceed,
        Approaching,
        WaypointReached,
        Arrived
    }

    /// <summary>Direction expressed redundantly as text, geometry, and optional audio.</summary>
    public enum GuidanceDirection
    {
        None,
        Ahead,
        Left,
        Right,
        TurnAround
    }

    /// <summary>
    /// A renderer-independent accessible guidance decision. Consumers can map
    /// VisualPattern to high-contrast geometry and SpokenPrompt to synthesized
    /// speech without deriving safety-critical directions independently.
    /// </summary>
    public sealed class AccessibleGuidanceInstruction
    {
        internal AccessibleGuidanceInstruction(
            GuidanceState state,
            GuidanceDirection direction,
            NavigationWaypoint waypoint,
            double distanceMeters,
            double turnDegrees,
            string visualPattern,
            string spokenPrompt,
            bool shouldAdvanceWaypoint)
        {
            State = state;
            Direction = direction;
            Waypoint = waypoint;
            DistanceMeters = distanceMeters;
            TurnDegrees = turnDegrees;
            VisualPattern = visualPattern;
            SpokenPrompt = spokenPrompt;
            ShouldAdvanceWaypoint = shouldAdvanceWaypoint;
        }

        public GuidanceState State { get; private set; }
        public GuidanceDirection Direction { get; private set; }
        public NavigationWaypoint Waypoint { get; private set; }
        public double DistanceMeters { get; private set; }
        public double TurnDegrees { get; private set; }
        public string VisualPattern { get; private set; }
        public string SpokenPrompt { get; private set; }
        public bool ShouldAdvanceWaypoint { get; private set; }

        public bool IsMovementSafe
        {
            get
            {
                return State != GuidanceState.Unavailable &&
                       State != GuidanceState.CalibrationRequired;
            }
        }
    }

    /// <summary>
    /// Produces stable, accessible indoor navigation instructions from a route,
    /// a calibrated floor-plan position, and the user's heading. Heading is in
    /// radians where zero points along positive X and positive angles rotate
    /// toward positive Y in floor-plan coordinates.
    /// </summary>
    public static class AccessibleGuidanceEngine
    {
        private const double ArrivalRadiusMeters = 0.75;
        private const double WaypointRadiusMeters = 1.10;
        private const double ApproachingRadiusMeters = 3.0;
        private const double StraightAheadDegrees = 20.0;
        private const double TurnAroundDegrees = 135.0;

        public static AccessibleGuidanceInstruction Evaluate(
            NavigationRoute route,
            int waypointIndex,
            PlanPoint userPosition,
            double headingRadians,
            double metersPerPlanUnit,
            bool positionalTrackingActive,
            bool calibrationAvailable)
        {
            if (route == null) throw new ArgumentNullException("route");
            if (waypointIndex < 0 || waypointIndex >= route.Waypoints.Count)
                throw new ArgumentOutOfRangeException("waypointIndex");
            if (double.IsNaN(metersPerPlanUnit) || double.IsInfinity(metersPerPlanUnit) || metersPerPlanUnit <= 0.0)
                throw new ArgumentOutOfRangeException("metersPerPlanUnit");

            var waypoint = route.Waypoints[waypointIndex];
            if (!positionalTrackingActive)
            {
                return Build(
                    GuidanceState.Unavailable,
                    GuidanceDirection.None,
                    waypoint,
                    double.NaN,
                    0.0,
                    "stop-ring",
                    "Guidance paused. Stop and look around until positional tracking returns.",
                    false);
            }

            if (!calibrationAvailable)
            {
                return Build(
                    GuidanceState.CalibrationRequired,
                    GuidanceDirection.None,
                    waypoint,
                    double.NaN,
                    0.0,
                    "calibration-grid",
                    "Indoor guidance needs calibration. Follow the calibration markers before continuing.",
                    false);
            }

            var deltaX = waypoint.Location.X - userPosition.X;
            var deltaY = waypoint.Location.Y - userPosition.Y;
            var distanceMeters = Math.Sqrt(deltaX * deltaX + deltaY * deltaY) * metersPerPlanUnit;
            var bearingRadians = Math.Atan2(deltaY, deltaX);
            var turnDegrees = NormalizeDegrees((bearingRadians - headingRadians) * 180.0 / Math.PI);

            var arrivalRadius = waypoint.IsGoal ? ArrivalRadiusMeters : WaypointRadiusMeters;
            if (distanceMeters <= arrivalRadius)
            {
                if (waypoint.IsGoal)
                {
                    return Build(
                        GuidanceState.Arrived,
                        GuidanceDirection.None,
                        waypoint,
                        distanceMeters,
                        0.0,
                        "goal-beacon",
                        "You have arrived at " + waypoint.Label + ".",
                        false);
                }

                return Build(
                    GuidanceState.WaypointReached,
                    GuidanceDirection.None,
                    waypoint,
                    distanceMeters,
                    0.0,
                    "waypoint-ring",
                    "Waypoint reached: " + waypoint.Label + ". Preparing the next direction.",
                    true);
            }

            var direction = ToDirection(turnDegrees);
            var state = distanceMeters <= ApproachingRadiusMeters
                ? GuidanceState.Approaching
                : direction == GuidanceDirection.Ahead ? GuidanceState.Proceed : GuidanceState.Reorient;
            var pattern = state == GuidanceState.Approaching
                ? "approach-chevron"
                : direction == GuidanceDirection.Ahead ? "forward-chevron" : "turn-chevron";
            var prompt = BuildMovementPrompt(direction, distanceMeters, waypoint.Label, state == GuidanceState.Approaching);
            return Build(state, direction, waypoint, distanceMeters, turnDegrees, pattern, prompt, false);
        }

        private static AccessibleGuidanceInstruction Build(
            GuidanceState state,
            GuidanceDirection direction,
            NavigationWaypoint waypoint,
            double distanceMeters,
            double turnDegrees,
            string visualPattern,
            string prompt,
            bool shouldAdvanceWaypoint)
        {
            return new AccessibleGuidanceInstruction(
                state,
                direction,
                waypoint,
                distanceMeters,
                turnDegrees,
                visualPattern,
                prompt,
                shouldAdvanceWaypoint);
        }

        private static GuidanceDirection ToDirection(double turnDegrees)
        {
            var magnitude = Math.Abs(turnDegrees);
            if (magnitude <= StraightAheadDegrees)
                return GuidanceDirection.Ahead;
            if (magnitude >= TurnAroundDegrees)
                return GuidanceDirection.TurnAround;
            return turnDegrees < 0.0 ? GuidanceDirection.Left : GuidanceDirection.Right;
        }

        private static string BuildMovementPrompt(
            GuidanceDirection direction,
            double distanceMeters,
            string label,
            bool approaching)
        {
            var roundedDistance = Math.Max(1, (int)Math.Round(distanceMeters, MidpointRounding.AwayFromZero));
            var action = direction == GuidanceDirection.Ahead ? "Go ahead" :
                         direction == GuidanceDirection.Left ? "Turn left" :
                         direction == GuidanceDirection.Right ? "Turn right" :
                         "Turn around";
            var distancePhrase = roundedDistance == 1 ? "1 metre" : roundedDistance + " metres";
            return approaching
                ? action + ". You are approaching " + label + ", about " + distancePhrase + " away."
                : action + " for about " + distancePhrase + " toward " + label + ".";
        }

        private static double NormalizeDegrees(double degrees)
        {
            while (degrees > 180.0) degrees -= 360.0;
            while (degrees <= -180.0) degrees += 360.0;
            return degrees;
        }
    }
}
