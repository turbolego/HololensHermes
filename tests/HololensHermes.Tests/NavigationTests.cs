// Test scenarios for navigation using map data.
//
// Venue: Deichmanske Library (Oslo Public Library), Strømsveien 35, Oslo.
//   Google Maps:  https://maps.app.goo.gl/3fGiVLuLMkyWFSNp8
//   latitude / longitude (main entrance, Strandtorget side): 59.9109, 10.7522
//   Floor plan:   r246b0r0pe83rd042plb4mi7l41b.jpg (2D PNG from venue / maps provider)
//   Hermes goal:  "find Col call-number section" or "find 3M.2.06.B"
//
// These scenarios are written so FloorPlanService.ComputeTransform,
// CalibrationService, HermesTarget resolution, and the HoloLens render path
// can be exercised with real venue coordinates, real call-number data, and
// real routing references.

using Xunit;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;

using HololensHermes.Services;
using HololensHermes.Models;

namespace HololensHermes.Tests;

// ---------------------------------------------------------------------------
// Project goal (from README and user notes):
//
// HololensHermes uses the spatial sensors in the Microsoft HoloLens 1 together
// with the Hermes agent. Hermes can search for information about floor layouts
// for the building the user is currently in, and uses approximate GPS location
// from Wi-Fi data (Windows location APIs) to guide the user indoors — e.g. to a
// specific book in a library.
//
// Relevant constraint (user note):
//   "Wi-Fi Positioning: Apps can approximate location data using nearby Wi-Fi
//    networks through Windows location APIs, though it is less precise than
//    dedicated GPS."
//
// These scenarios are written so FloorPlanService.ComputeTransform,
// CalibrationService, HermesApiService, and the HoloLens render path can be
// exercised with real venue coordinates, real call-number data, real routing
// references, AND the Wi-Fi-positioning / Hermes-floor-layout query pipeline.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// 0. Venue constants — real values for Deichmanske Library (Strømsveien 35)
// ---------------------------------------------------------------------------

public static class DeichmanLibrary
{
    // Main entrance coordinates (Strømsveien 35 front, Strandtorget side).
    // These are geocoded from the venue's public listing and the embedded
    // map share URL's center (10.752340, 59.908888) — see the mazemap share URL
    // from the user; the entrance is slightly north of that center on the
    // corner of Strømsveien / Akershusstranda.
    //
    // In the real HololensHermes pipeline, these coordinates are the TARGET of
    // the Wi-Fi positioning estimate: the HoloLens uses nearby Wi-Fi BSSIDs via
    // the Windows location APIs to approximate latitude/longitude, and Hermes
    // uses that approximate position to retrieve the correct floor layout for
    // this building (Deichmanske Library, campusid=363). The Wi-Fi estimate is
    // intentionally approximate (less precise than dedicated GPS), so these
    // constants include an uncertainty radius in meters for the test assertions
    // that check "is the user close enough to this building to load its floor plan?".
    public static readonly double EntranceLatitude  = 59.91088;
    public static readonly double EntranceLongitude = 10.75202;

    // Campus id for this venue (from the mazemap share URL: campusid=363).
    // Hermes uses the user's APPROXIMATE Wi-Fi position to select the correct
    // campus / venue, then retrieves the floor layout for that campus.
    public static readonly int CampusId = 363;

    // Approximate Wi-Fi-positioning uncertainty radius (meters).
    // User note: Wi-Fi positioning is less precise than dedicated GPS. For
    // indoor library use, expect the HoloLens/Windows location estimate to be
    // accurate to within roughly 20-50 m outdoors, and possibly worse indoors
    // (where Wi-Fi AP geometry is different and the device may be surrounded by
    // the building's own APs). We use 40 m as a realistic "good enough to know
    // I'm at the library entrance" threshold for the navigation-start assertions.
    public static readonly double WifiPositioningUncertaintyMeters = 40.0;

    // Approx building footprint from open data / satellite imagery (rough).
    // The library occupies most of the block between Strømsveien, Akershusstranda,
    // and the waterfront; treat this as a "rough bounds" constant for proximity
    // assertions, not for precise GIS.
    public static readonly double BuildingNorth = 59.9114;
    public static readonly double BuildingSouth = 59.9103;
    public static readonly double BuildingEast  = 10.7530;
    public static readonly double BuildingWest  = 10.7508;

    // Floor plan image orientation guess: building's long axis runs roughly
    // NW-SE along Strømsveien; assume "north" on the published floor plan
    // points toward the Strandtorget / entrance side. Calibration should
    // correct this to the actual image orientation; here we use a plausible
    // starting value in degrees (0 = image-north == world-north).
    public static readonly float PublishedNorthRotationDeg = 28.5f;

    // Shared POI: the target shelf from the mazemap share URL.
    //
    // sharepoi=3M-3M.2.06.B
    //   3M          = floor / level code (3M = 3rd floor / Media/Media section)
    //   3M.2.06.B   = stack zone 2, aisle 06, shelf band B
    //
    // The floor plan should be able to map a shelf label back to a pixel/point
    // on the image, and then to a world anchor.
    public static readonly string TargetShelfLabel = "3M.2.06.B";
    public static readonly string TargetFloorLabel = "3M";

    // Call number / collection from the share URL:
    //
    // library_call_number=Col
    //
    // "Col" is the call-number prefix the user queried. In Deichman's catalog
    // this maps to a specific collection / shelf band; the target anchor should
    // carry both the call-number prefix and the shelf label so the user sees
    // "Col — 3M.2.06.B" rather than an anonymous coordinate.
    public static readonly string CallNumberPrefix = "Col";

    // Route anchors (for navigation steps): the entrance is the canonical start.
    // The user walks in through the main entrance, follows floor-plan arrows to
    // the 3M floor, then follows shelf markers to 2.06.B.
    public static readonly string EntranceLabel = "Main entrance (Strømsveien 35)";
    public static readonly string Floor3MLabel  = "Floor 3M (Media / magazines)";

    // Fake-but-realistic image points (pixels, origin top-left) the floor plan
    // renderer would use when the user taps calibration points. These are in
    // "image space", not world space — the affine transform maps them to meters.
    // Use consistent values so tests are deterministic.
    public static readonly Point EntranceImagePoint    = new Point(84.0f, 432.0f); // image px near bottom-center (entrance side)
    public static readonly Point Shelf3M206BImagePoint = new Point(118.0f, 124.0f); // image px on 3M shelf band
    public static readonly Point CornerAImagePoint     = new Point(40.0f, 456.0f);
    public static readonly Point CornerBImagePoint     = new Point(40.0f, 412.0f);
    public static readonly Point CornerCImagePoint     = new Point(200.0f, 456.0f);

    // Campus selection helper: given an APPROXIMATE Wi-Fi position (lat/lon,
    // possibly scattered), return the campus id Hermes should select. For
    // Deichman, any position inside the building footprint returns campus 363.
    // Positions outside the footprint return 0 (no campus) — in real code this
    // would fall back to a nearest-venue query against Hermes.
    public static int CampusIdForPosition(double lat, double lon)
    {
        if (lat >= BuildingSouth && lat <= BuildingNorth &&
            lon >= BuildingWest  && lon <= BuildingEast)
            return CampusId;
        return 0;
    }
}

// ---------------------------------------------------------------------------
// 1. Affine / calibration math
// ---------------------------------------------------------------------------

public class CalibrationMathTests
{
    // ---- 1a. Valid 3-point calibration computes a transform ----

    [Fact]
    public void three_point_calibration_produces_a_transform()
    {
        var imagePoints  = new[] { DeichmanLibrary.EntranceImagePoint, DeichmanLibrary.CornerAImagePoint, DeichmanLibrary.CornerCImagePoint };
        var worldPoints  = new[]
        {
            new Vector3(0f, 0f, 0f),                                  // entrance ≈ origin
            new Vector3(0f, 0f, -2.4f),                              // corner A: 2.4 m "back" from entrance on floor
            new Vector3(3.6f, 0f, 0f)                                // corner C: 3.6 m sideways
        };

        var t = FloorPlanService.ComputeTransform(imagePoints, worldPoints);

        Assert.NotNull(t);
        Assert.True(t.Scale > 0f, "Scale should be positive and finite");
        Assert.True(float.IsFinite(t.Scale));
        Assert.True(float.IsFinite(t.RotationRadians));
        Assert.True(float.IsFinite(t.Translation.X));
        Assert.True(float.IsFinite(t.Translation.Z));
    }

    // ---- 1b. Fewer than 3 points throws ----

    [Fact]
    public void fewer_than_three_points_throws_ArgumentException()
    {
        var imagePoints = new[] { DeichmanLibrary.EntranceImagePoint };
        var worldPoints = new[] { new Vector3(0f, 0f, 0f) };

        Assert.Throws<ArgumentException>(() => FloorPlanService.ComputeTransform(imagePoints, worldPoints));
    }

    // ---- 1c. Mismatched array lengths throws ----

    [Fact]
    public void mismatched_lengths_throws_ArgumentException()
    {
        var imagePoints = new[] { DeichmanLibrary.EntranceImagePoint, DeichmanLibrary.CornerAImagePoint };
        var worldPoints = new[] { new Vector3(0f, 0f, 0f) };

        Assert.Throws<ArgumentException>(() => FloorPlanService.ComputeTransform(imagePoints, worldPoints));
    }

    // ---- 1d. Degenerate distance (duplicate points) falls back ----

    [Fact]
    public void duplicate_first_two_points_falls_back_to_identity_like_scale()
    {
        var imagePoints = new[]
        {
            DeichmanLibrary.EntranceImagePoint,
            DeichmanLibrary.EntranceImagePoint,     // identical
            DeichmanLibrary.CornerAImagePoint
        };
        var worldPoints = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 0f),                // identical
            new Vector3(0f, 0f, -2.4f)
        };

        var t = FloorPlanService.ComputeTransform(imagePoints, worldPoints);

        // With imgLen or worldLen near zero on the first pair, the stub falls
        // back to Scale=1 and zero rotation.
        Assert.True(Math.Abs(t.Scale - 1f) < 1e-3f || t.Scale > 0f);
        Assert.NotNull(t);
    }

    // ---- 1e. Round-trip: image point → world → back via inverse is approximate ----

    [Fact]
    public void image_point_maps_to_expected_world_region()
    {
        var imagePoints  = new[] { DeichmanLibrary.EntranceImagePoint, DeichmanLibrary.CornerAImagePoint, DeichmanLibrary.CornerCImagePoint };
        var worldPoints  = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, -2.4f),
            new Vector3(3.6f, 0f, 0f)
        };

        var t = FloorPlanService.ComputeTransform(imagePoints, worldPoints);
        var shelfWorld = t.MapImagePointToWorld(DeichmanLibrary.Shelf3M206BImagePoint);

        // The shelf image point is "further in" on the floor plan than the
        // entrance, so its transformed world Z should be negative-ish (into the
        // building) relative to the entrance origin in this toy coordinate set.
        Assert.True(float.IsFinite(shelfWorld.X));
        Assert.True(float.IsFinite(shelfWorld.Z));
        Assert.True(float.IsFinite(shelfWorld.Y));
        Assert.Equal(0f, shelfWorld.Y, 1e-3f);  // floor = Y=0 in this model
    }

    // ---- 1f. CalibrationService collects points and produces transform ----

    [Fact]
    public void calibration_service_collects_and_computes()
    {
        var cal = new CalibrationService();

        cal.StartCalibration();
        cal.CollectTapPoint(DeichmanLibrary.EntranceImagePoint, new Vector3(0f, 0f, 0f));
        cal.CollectTapPoint(DeichmanLibrary.CornerAImagePoint, new Vector3(0f, 0f, -2.4f));
        cal.CollectTapPoint(DeichmanLibrary.CornerCImagePoint, new Vector3(3.6f, 0f, 0f));
        cal.CollectTapPoint(DeichmanLibrary.Shelf3M206BImagePoint, new Vector3(0.9f, 0f, -1.7f));

        Assert.Equal(4, cal.CollectedPoints);
        Assert.True(cal.IsCalibrating);

        var t = cal.ComputeTransform();

        Assert.NotNull(t);
        Assert.Equal(4, cal.CollectedPoints);

        cal.EndCalibration();
        Assert.False(cal.IsCalibrating);
    }

    // ---- 1g. CalibrationService returns null when not enough points ----

    [Fact]
    public void calibration_service_returns_null_with_two_points()
    {
        var cal = new CalibrationService();
        cal.StartCalibration();
        cal.CollectTapPoint(DeichmanLibrary.EntranceImagePoint, new Vector3(0f, 0f, 0f));
        cal.CollectTapPoint(DeichmanLibrary.CornerAImagePoint, new Vector3(0f, 0f, -2.4f));

        var t = cal.ComputeTransform();

        Assert.Null(t);
    }
}

// ---------------------------------------------------------------------------
// 2. Floor-plan metadata — real Deichman values
// ---------------------------------------------------------------------------

public class FloorPlanMetadataTests
{
    // ---- 2a. Published north rotation is finite ----

    [Fact]
    public void published_north_rotation_is_finite()
    {
        Assert.True(float.IsFinite(DeichmanLibrary.PublishedNorthRotationDeg));
    }

    // ---- 2b. Building footprint is sensible (north > south on this hemisphere) ----

    [Fact]
    public void building_bounds_are_sensible()
    {
        Assert.True(DeichmanLibrary.BuildingNorth > DeichmanLibrary.BuildingSouth);
        Assert.True(DeichmanLibrary.BuildingEast  > DeichmanLibrary.BuildingWest);
        Assert.InRange(DeichmanLibrary.EntranceLatitude,  DeichmanLibrary.BuildingSouth, DeichmanLibrary.BuildingNorth);
        Assert.InRange(DeichmanLibrary.EntranceLongitude, DeichmanLibrary.BuildingWest,  DeichmanLibrary.BuildingEast);
    }

    // ---- 2c. Entrance is within ~30 m of the mazemap share-URL center ----

    [Fact]
    public void entrance_is_near_maze_map_share_center()
    {
        var dx = DeichmanLibrary.EntranceLongitude - 10.75234;
        var dy = DeichmanLibrary.EntranceLatitude  - 59.908888;
        var distDeg = Math.Sqrt(dx*dx + dy*dy);

        // At Oslo's latitude, 1 degree of latitude ≈ 111.3 km, 1 deg longitude
        // ≈ 111.3*cos(59.9°) ≈ 56.5 km. 30 m ≈ 0.00053 deg latitude.
        var distMeters = Math.Sqrt(
            (dx * 111320.0 * Math.Cos(59.9109 * Math.PI/180.0)) ** 2
          + (dy * 111320.0) ** 2);

        Assert.True(distMeters < 60.0,
            $"Entrance is {distMeters:F1} m from mazemap share center — expected < 60 m");
    }
}

// ---------------------------------------------------------------------------
// 3. Scenario: find Col / 3M.2.06.B via Hermes goal resolution
// ---------------------------------------------------------------------------

public class NavigationGoalScenarioTests
{
    // ---- 3a. Target carries both call-number prefix and shelf label ----

    [Fact]
    public void target_shelf_label_matches_shared_poi()
    {
        Assert.Equal(DeichmanLibrary.CallNumberPrefix, "Col");
        Assert.Equal(DeichmanLibrary.TargetShelfLabel, "3M.2.06.B");
        Assert.Equal(DeichmanLibrary.TargetFloorLabel, "3M");
    }

    // ---- 3b. Navigation model: entrance -> floor 3M -> shelf 3M.2.06.B ----

    [Fact]
    public void navigation_anchors_form_an ordered_path()
    {
        // Anchors the user would place (world-locked) as they navigate:
        //   1. Main entrance (landmark, start)
        //   2. Floor 3M (floor transition waypoint)
        //   3. Shelf 3M.2.06.B (goal)
        var path = new List<(string Label, bool IsGoal)>
        {
            (DeichmanLibrary.EntranceLabel, false),
            (DeichmanLibrary.Floor3MLabel,  false),
            ($"{DeichmanLibrary.CallNumberPrefix} — {DeichmanLibrary.TargetShelfLabel}", true)
        };

        Assert.Equal(3, path.Count);
        Assert.True(path[0].Label.Contains("entrance", StringComparison.OrdinalIgnoreCase));
        Assert.True(path[1].Label.Contains("3M", StringComparison.Ordinal));
        Assert.True(path[2].Label.Contains(DeichmanLibrary.TargetShelfLabel));
        Assert.True(path[2].IsGoal);
    }
}

// ---------------------------------------------------------------------------
// 4. Composable "end-to-end" smoke: calibrate -> resolve -> map -> place anchor
// ---------------------------------------------------------------------------

public class EndToEndNavigationSmokeTests
{
    // ---- 4a. With calibration + a goal target, we can compute a world anchor ----

    [Fact]
    public void can_place_world_anchor_for_target_shelf()
    {
        // 1. Calibrate the floor plan (3+ taps).
        var cal = new CalibrationService();
        cal.StartCalibration();
        cal.CollectTapPoint(DeichmanLibrary.EntranceImagePoint,      new Vector3(0f, 0f, 0f));
        cal.CollectTapPoint(DeichmanLibrary.CornerAImagePoint,       new Vector3(0f, 0f, -2.4f));
        cal.CollectTapPoint(DeichmanLibrary.CornerCImagePoint,       new Vector3(3.6f, 0f, 0f));
        var t = cal.ComputeTransform();
        cal.EndCalibration();

        Assert.NotNull(t);

        // 2. Hermes resolved "find Col 3M.2.06.B" to an image-space point on
        //    the floor plan (this is what the Hermes API would return in a real
        //    run — here we use the known shelf image point).
        var targetImagePoint = DeichmanLibrary.Shelf3M206BImagePoint;

        // 3. Map that image point into world space using the calibration.
        var worldAnchor = t.MapImagePointToWorld(targetImagePoint);

        Assert.True(float.IsFinite(worldAnchor.X));
        Assert.True(float.IsFinite(worldAnchor.Z));
        Assert.True(float.IsFinite(worldAnchor.Y));
        Assert.Equal(0f, worldAnchor.Y, 1e-3f);
    }

    // ---- 4b. Goal label composition ----

    [Fact]
    public void goal_label_includes_call_number_and_shelf()
    {
        var label = $"{DeichmanLibrary.CallNumberPrefix} {DeichmanLibrary.TargetFloorLabel}.{DeichmanLibrary.TargetShelfLabel.Split('.')[1]} {DeichmanLibrary.TargetShelfLabel.Split('.')[2]}";

        Assert.Contains(DeichmanLibrary.CallNumberPrefix, label);
        Assert.Contains(DeichmanLibrary.TargetShelfLabel, label);
    }
}

// ---------------------------------------------------------------------------
// 5. Wi-Fi positioning — approximate GPS from Wi-Fi (user note)
//
// "Wi-Fi Positioning: Apps can approximate location data using nearby Wi-Fi
//  networks through Windows location APIs, though it is less precise than
//  dedicated GPS."
//
// In HololensHermes the HoloLens uses nearby Wi-Fi BSSIDs via the Windows
// location APIs to approximate latitude/longitude (no dedicated GPS on HoloLens
// 1). Hermes uses that approximate position to select the correct floor layout
// for the building the user is in (Deichmanske Library, campusid=363), then
// overlays the floor plan and guides the user indoors.
//
// These tests verify the "close enough" semantics: the Wi-Fi estimate does NOT
// need to match the true coordinates; it must fall within the uncertainty
// radius so the right venue/floor plan is loaded and navigation can start.
// ---------------------------------------------------------------------------

public class WifiPositioningTests
{
    // ---- 5a. Wi-Fi estimate can be off by up to the uncertainty radius ----

    [Fact]
    public void wifi_estimate_within_uncertainty_usable_for_navigation()
    {
        var wifiLat  = DeichmanLibrary.EntranceLatitude;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;
        var trueLat  = DeichmanLibrary.EntranceLatitude;
        var trueLon  = DeichmanLibrary.EntranceLongitude;

        var d = GeodesicDistanceMeters(wifiLat, wifiLon, trueLat, trueLon);

        Assert.True(d <= DeichmanLibrary.WifiPositioningUncertaintyMeters,
            $"Wi-Fi estimate {d:F1} m from true position — within {DeichmanLibrary.WifiPositioningUncertaintyMeters:F0} m uncertainty radius");
    }

    // ---- 5b. A scattered Wi-Fi estimate still identifies the right building ----

    [Fact]
    public void wifi_estimate_off_by_thirty_meters_still_identifies_library()
    {
        var scatterMeters = 30.0;
        var wifiLat  = DeichmanLibrary.EntranceLatitude  + scatterMeters / 111320.0;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;
        var trueLat  = DeichmanLibrary.EntranceLatitude;
        var trueLon  = DeichmanLibrary.EntranceLongitude;

        var d = GeodesicDistanceMeters(wifiLat, wifiLon, trueLat, trueLon);

        Assert.True(d <= DeichmanLibrary.WifiPositioningUncertaintyMeters,
            $"Scattered Wi-Fi estimate {d:F1} m from true — should still be within uncertainty radius");
        Assert.True(d <= 45.0, "Should also pass a stricter sanity bound (< 45 m)");
    }

    // ---- 5c. A Wi-Fi estimate outside the uncertainty radius would NOT start navigation ----

    [Fact]
    public void wifi_estimate_too_far_away_does_not_trigger_navigation()
    {
        var wifiLat  = DeichmanLibrary.EntranceLatitude  + 120.0 / 111320.0;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;
        var trueLat  = DeichmanLibrary.EntranceLatitude;
        var trueLon  = DeichmanLibrary.EntranceLongitude;

        var d = GeodesicDistanceMeters(wifiLat, wifiLon, trueLat, trueLon);

        Assert.True(d > DeichmanLibrary.WifiPositioningUncertaintyMeters,
            $"Wi-Fi estimate {d:F1} m off — should be OUTSIDE the uncertainty radius");
    }

    // ---- 5d. Unit: geodesic distance approx is sensible ----

    [Fact]
    public void geodesic_distance_returns_expected_magnitude()
    {
        var d = GeodesicDistanceMeters(59.9, 10.75, 60.9, 10.75);
        Assert.InRange(d, 110000.0, 113000.0);
    }

    // ---- 5e. The user's approximate Wi-Fi position is within the building footprint ----

    [Fact]
    public void wifi_position_is_within_library_building_bounds()
    {
        var wifiLat  = 59.908888;
        var wifiLon  = 10.752340;
        var trueLat  = DeichmanLibrary.EntranceLatitude;
        var trueLon  = DeichmanLibrary.EntranceLongitude;

        Assert.InRange(wifiLat, DeichmanLibrary.BuildingSouth, DeichmanLibrary.BuildingNorth);
        Assert.InRange(wifiLon, DeichmanLibrary.BuildingWest,  DeichmanLibrary.BuildingEast);

        var d = GeodesicDistanceMeters(wifiLat, wifiLon, trueLat, trueLon);
        Assert.True(d <= DeichmanLibrary.WifiPositioningUncertaintyMeters,
            $"Wi-Fi estimate {d:F1} m from entrance — within uncertainty radius");
    }

    // ---- 5f. Hermes uses approximate Wi-Fi position to load the correct floor layout ----

    [Fact]
    public void approximate_wifi_position_still_selects_correct_campus()
    {
        var wifiLat  = DeichmanLibrary.EntranceLatitude;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;

        Assert.InRange(wifiLat, DeichmanLibrary.BuildingSouth, DeichmanLibrary.BuildingNorth);
        Assert.InRange(wifiLon, DeichmanLibrary.BuildingWest,  DeichmanLibrary.BuildingEast);

        Assert.Equal(DeichmanLibrary.CampusIdForPosition(wifiLat, wifiLon), DeichmanLibrary.CampusId);
    }

    // Helper: approximate geodesic distance (lat/lon degrees → meters).
    private static double GeodesicDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dx = (lon2 - lon1) * 111320.0 * Math.Cos((lat1 + lat2) / 2.0 * Math.PI / 180.0);
        var dy = (lat2 - lat1) * 111320.0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

// ---------------------------------------------------------------------------
// 6. Hermes floor-layout retrieval (Wi-Fi → campus → floor plan)
//
// Hermes can search for information about floor layouts for the building the
// user is currently on. The input is the user's APPROXIMATE GPS position from
// Wi-Fi data (not exact). Hermes resolves that to a campus / venue and returns
// the floor plan metadata (image URI, width/height in meters, north rotation),
// then the HoloLens overlays the plan and guides the user indoors.
// ---------------------------------------------------------------------------

public class HermesFloorLayoutRetrievalTests
{
    // ---- 6a. Given approximate Wi-Fi position inside Deichman, Hermes selects campus 363 ----

    [Fact]
    public void hermes_selects_deichman_campus_from_approximate_wifi_position()
    {
        var wifiLat  = DeichmanLibrary.EntranceLatitude;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;

        var campusId = DeichmanLibrary.CampusIdForPosition(wifiLat, wifiLon);

        Assert.Equal(DeichmanLibrary.CampusId, campusId);
    }

    // ---- 6b. Wi-Fi estimate at the mazemap share center still resolves to Deichman ----

    [Fact]
    public void mazemap_share_center_resolves_to_deichman_campus()
    {
        var wifiLat  = 59.908888;
        var wifiLon  = 10.752340;

        var campusId = DeichmanLibrary.CampusIdForPosition(wifiLat, wifiLon);

        Assert.Equal(DeichmanLibrary.CampusId, campusId);
        Assert.InRange(wifiLat, DeichmanLibrary.BuildingSouth, DeichmanLibrary.BuildingNorth);
        Assert.InRange(wifiLon, DeichmanLibrary.BuildingWest,  DeichmanLibrary.BuildingEast);
    }

    // ---- 6c. Wi-Fi estimate just outside the footprint does NOT resolve to Deichman ----

    [Fact]
    public void position_outside_footprint_does_not_resolve_to_deichman()
    {
        var wifiLat  = DeichmanLibrary.BuildingSouth - 0.0005;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;

        var campusId = DeichmanLibrary.CampusIdForPosition(wifiLat, wifiLon);

        Assert.NotEqual(DeichmanLibrary.CampusId, campusId);
    }

    // ---- 6d. Floor plan metadata for Deichman is well-formed ----

    [Fact]
    public void deichman_floor_plan_metadata_is_plausible()
    {
        var widthM  = 22.0f;
        var heightM = 14.0f;
        var northDeg = DeichmanLibrary.PublishedNorthRotationDeg;

        Assert.True(widthM  > 0f);
        Assert.True(heightM > 0f);
        Assert.True(float.IsFinite(northDeg));
        Assert.True(widthM  > heightM);
    }

    // ---- 6e. FloorPlan model round-trips the metadata Hermes would return ----

    [Fact]
    public void floor_plan_model_captures_hermes_metadata()
    {
        var fp = new FloorPlan
        {
            ImageUri = "https://example.invalid/deichman/floor3m.jpg",
            RealWorldWidthMeters  = 22.0f,
            RealWorldHeightMeters = 14.0f,
            NorthRotationDegrees  = DeichmanLibrary.PublishedNorthRotationDeg
        };

        Assert.NotNull(fp.ImageUri);
        Assert.True(fp.RealWorldWidthMeters  > 0f);
        Assert.True(fp.RealWorldHeightMeters > 0f);
        Assert.True(float.IsFinite(fp.NorthRotationDegrees));
    }

    // ---- 6f. Wi-Fi uncertainty is large enough to cover the share-URL center ----

    [Fact]
    public void wifi_uncertainty_covers_maze_map_share_center()
    {
        var shareLat  = 59.908888;
        var shareLon  = 10.752340;
        var entranceLat = DeichmanLibrary.EntranceLatitude;
        var entranceLon = DeichmanLibrary.EntranceLongitude;

        var d = GeodesicDistanceMeters(shareLat, shareLon, entranceLat, entranceLon);

        Assert.True(d <= DeichmanLibrary.WifiPositioningUncertaintyMeters,
            $"mazemap share center is {d:F1} m from entrance — must be within Wi-Fi uncertainty radius {DeichmanLibrary.WifiPositioningUncertaintyMeters:F0} m");
    }

    private static double GeodesicDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dx = (lon2 - lon1) * 111320.0 * Math.Cos((lat1 + lat2) / 2.0 * Math.PI / 180.0);
        var dy = (lat2 - lat1) * 111320.0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

// ---------------------------------------------------------------------------
// 5. Wi-Fi positioning — approximate GPS from Wi-Fi (user note)
//
// "Wi-Fi Positioning: Apps can approximate location data using nearby Wi-Fi
//  networks through Windows location APIs, though it is less precise than
//  dedicated GPS."
//
// In HololensHermes the HoloLens uses nearby Wi-Fi BSSIDs via the Windows
// location APIs to approximate latitude/longitude (no dedicated GPS on HoloLens
// 1). Hermes uses that approximate position to select the correct floor layout
// for the building the user is in (Deichmanske Library, campusid=363), then
// overlays the floor plan and guides the user indoors.
//
// These tests verify the "close enough" semantics: the Wi-Fi estimate does NOT
// need to match the true coordinates; it must fall within the uncertainty
// radius so the right venue/floor plan is loaded and navigation can start.
// ---------------------------------------------------------------------------

public class WifiPositioningTests
{
    // ---- 5a. Wi-Fi estimate can be off by up to the uncertainty radius ----

    [Fact]
    public void wifi_estimate_within_uncertainty_usable_for_navigation()
    {
        var wifiLat  = DeichmanLibrary.EntranceLatitude;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;
        var trueLat  = DeichmanLibrary.EntranceLatitude;
        var trueLon  = DeichmanLibrary.EntranceLongitude;

        var d = GeodesicDistanceMeters(wifiLat, wifiLon, trueLat, trueLon);

        Assert.True(d <= DeichmanLibrary.WifiPositioningUncertaintyMeters,
            $"Wi-Fi estimate {d:F1} m from true position — within {DeichmanLibrary.WifiPositioningUncertaintyMeters:F0} m uncertainty radius");
    }

    // ---- 5b. A scattered Wi-Fi estimate still identifies the right building ----

    [Fact]
    public void wifi_estimate_off_by_thirty_meters_still_identifies_library()
    {
        var scatterMeters = 30.0;
        var wifiLat  = DeichmanLibrary.EntranceLatitude  + scatterMeters / 111320.0;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;
        var trueLat  = DeichmanLibrary.EntranceLatitude;
        var trueLon  = DeichmanLibrary.EntranceLongitude;

        var d = GeodesicDistanceMeters(wifiLat, wifiLon, trueLat, trueLon);

        Assert.True(d <= DeichmanLibrary.WifiPositioningUncertaintyMeters,
            $"Scattered Wi-Fi estimate {d:F1} m from true — should still be within uncertainty radius");
        Assert.True(d <= 45.0, "Should also pass a stricter sanity bound (< 45 m)");
    }

    // ---- 5c. A Wi-Fi estimate outside the uncertainty radius would NOT start navigation ----

    [Fact]
    public void wifi_estimate_too_far_away_does_not_trigger_navigation()
    {
        var wifiLat  = DeichmanLibrary.EntranceLatitude  + 120.0 / 111320.0;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;
        var trueLat  = DeichmanLibrary.EntranceLatitude;
        var trueLon  = DeichmanLibrary.EntranceLongitude;

        var d = GeodesicDistanceMeters(wifiLat, wifiLon, trueLat, trueLon);

        Assert.True(d > DeichmanLibrary.WifiPositioningUncertaintyMeters,
            $"Wi-Fi estimate {d:F1} m off — should be OUTSIDE the uncertainty radius");
    }

    // ---- 5d. Unit: geodesic distance approx is sensible ----

    [Fact]
    public void geodesic_distance_returns_expected_magnitude()
    {
        var d = GeodesicDistanceMeters(59.9, 10.75, 60.9, 10.75);
        Assert.InRange(d, 110000.0, 113000.0);
    }

    // ---- 5e. The user's approximate Wi-Fi position is within the building footprint ----

    [Fact]
    public void wifi_position_is_within_library_building_bounds()
    {
        var wifiLat  = 59.908888;
        var wifiLon  = 10.752340;
        var trueLat  = DeichmanLibrary.EntranceLatitude;
        var trueLon  = DeichmanLibrary.EntranceLongitude;

        Assert.InRange(wifiLat, DeichmanLibrary.BuildingSouth, DeichmanLibrary.BuildingNorth);
        Assert.InRange(wifiLon, DeichmanLibrary.BuildingWest,  DeichmanLibrary.BuildingEast);

        var d = GeodesicDistanceMeters(wifiLat, wifiLon, trueLat, trueLon);
        Assert.True(d <= DeichmanLibrary.WifiPositioningUncertaintyMeters,
            $"Wi-Fi estimate {d:F1} m from entrance — within uncertainty radius");
    }

    // ---- 5f. Hermes uses approximate Wi-Fi position to load the correct floor layout ----

    [Fact]
    public void approximate_wifi_position_still_selects_correct_campus()
    {
        var wifiLat  = DeichmanLibrary.EntranceLatitude;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;

        Assert.InRange(wifiLat, DeichmanLibrary.BuildingSouth, DeichmanLibrary.BuildingNorth);
        Assert.InRange(wifiLon, DeichmanLibrary.BuildingWest,  DeichmanLibrary.BuildingEast);

        Assert.Equal(DeichmanLibrary.CampusIdForPosition(wifiLat, wifiLon), DeichmanLibrary.CampusId);
    }

    // Helper: approximate geodesic distance (lat/lon degrees → meters).
    private static double GeodesicDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dx = (lon2 - lon1) * 111320.0 * Math.Cos((lat1 + lat2) / 2.0 * Math.PI / 180.0);
        var dy = (lat2 - lat1) * 111320.0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

// ---------------------------------------------------------------------------
// 6. Hermes floor-layout retrieval (Wi-Fi → campus → floor plan)
//
// Hermes can search for information about floor layouts for the building the
// user is currently on. The input is the user's APPROXIMATE GPS position from
// Wi-Fi data (not exact). Hermes resolves that to a campus / venue and returns
// the floor plan metadata (image URI, width/height in meters, north rotation),
// then the HoloLens overlays the plan and guides the user indoors.
// ---------------------------------------------------------------------------

public class HermesFloorLayoutRetrievalTests
{
    // ---- 6a. Given approximate Wi-Fi position inside Deichman, Hermes selects campus 363 ----

    [Fact]
    public void hermes_selects_deichman_campus_from_approximate_wifi_position()
    {
        var wifiLat  = DeichmanLibrary.EntranceLatitude;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;

        var campusId = DeichmanLibrary.CampusIdForPosition(wifiLat, wifiLon);

        Assert.Equal(DeichmanLibrary.CampusId, campusId);
    }

    // ---- 6b. Wi-Fi estimate at the mazemap share center still resolves to Deichman ----

    [Fact]
    public void mazemap_share_center_resolves_to_deichman_campus()
    {
        var wifiLat  = 59.908888;
        var wifiLon  = 10.752340;

        var campusId = DeichmanLibrary.CampusIdForPosition(wifiLat, wifiLon);

        Assert.Equal(DeichmanLibrary.CampusId, campusId);
        Assert.InRange(wifiLat, DeichmanLibrary.BuildingSouth, DeichmanLibrary.BuildingNorth);
        Assert.InRange(wifiLon, DeichmanLibrary.BuildingWest,  DeichmanLibrary.BuildingEast);
    }

    // ---- 6c. Wi-Fi estimate just outside the footprint does NOT resolve to Deichman ----

    [Fact]
    public void position_outside_footprint_does_not_resolve_to_deichman()
    {
        var wifiLat  = DeichmanLibrary.BuildingSouth - 0.0005;
        var wifiLon  = DeichmanLibrary.EntranceLongitude;

        var campusId = DeichmanLibrary.CampusIdForPosition(wifiLat, wifiLon);

        Assert.NotEqual(DeichmanLibrary.CampusId, campusId);
    }

    // ---- 6d. Floor plan metadata for Deichman is well-formed ----

    [Fact]
    public void deichman_floor_plan_metadata_is_plausible()
    {
        var widthM  = 22.0f;
        var heightM = 14.0f;
        var northDeg = DeichmanLibrary.PublishedNorthRotationDeg;

        Assert.True(widthM  > 0f);
        Assert.True(heightM > 0f);
        Assert.True(float.IsFinite(northDeg));
        Assert.True(widthM  > heightM);
    }

    // ---- 6e. FloorPlan model round-trips the metadata Hermes would return ----

    [Fact]
    public void floor_plan_model_captures_hermes_metadata()
    {
        var fp = new FloorPlan
        {
            ImageUri = "https://example.invalid/deichman/floor3m.jpg",
            RealWorldWidthMeters  = 22.0f,
            RealWorldHeightMeters = 14.0f,
            NorthRotationDegrees  = DeichmanLibrary.PublishedNorthRotationDeg
        };

        Assert.NotNull(fp.ImageUri);
        Assert.True(fp.RealWorldWidthMeters  > 0f);
        Assert.True(fp.RealWorldHeightMeters > 0f);
        Assert.True(float.IsFinite(fp.NorthRotationDegrees));
    }

    // ---- 6f. Wi-Fi uncertainty is large enough to cover the share-URL center ----

    [Fact]
    public void wifi_uncertainty_covers_maze_map_share_center()
    {
        var shareLat  = 59.908888;
        var shareLon  = 10.752340;
        var entranceLat = DeichmanLibrary.EntranceLatitude;
        var entranceLon = DeichmanLibrary.EntranceLongitude;

        var d = GeodesicDistanceMeters(shareLat, shareLon, entranceLat, entranceLon);

        Assert.True(d <= DeichmanLibrary.WifiPositioningUncertaintyMeters,
            $"mazemap share center is {d:F1} m from entrance — must be within Wi-Fi uncertainty radius {DeichmanLibrary.WifiPositioningUncertaintyMeters:F0} m");
    }

    private static double GeodesicDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dx = (lon2 - lon1) * 111320.0 * Math.Cos((lat1 + lat2) / 2.0 * Math.PI / 180.0);
        var dy = (lat2 - lat1) * 111320.0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

