using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Perception.Spatial;
using Windows.UI.Input.Spatial;

namespace HololensHermes.Services
{
    /// <summary>
    /// Multi-point calibration: user walks to known floor-plan points and taps.
    ///
    /// Collects pairs of (floor-plan image point, world position at tap).
    /// After 3+ points collected, computes the affine transform mapping
    /// image space → world space (see FloorPlanService.ComputeTransform).
    ///
    /// Call CollectTapPointAsync each time the user taps while in calibration mode.
    /// </summary>
    public sealed class CalibrationService
    {
        private readonly List<Point> _imagePoints = new List<Point>();
        private readonly List<Vector3> _worldPoints = new List<Vector3>();

        /// <summary>
        /// Mode: false = normal operation; true = collecting calibration points.
        /// </summary>
        public bool IsCalibrating { get; private set; }

        /// <summary>
        /// Number of points collected in current calibration session.
        /// </summary>
        public int CollectedPoints => _imagePoints.Count;

        /// <summary>
        /// Begin collecting calibration points.
        /// </summary>
        public void StartCalibration()
        {
            _imagePoints.Clear();
            _worldPoints.Clear();
            IsCalibrating = true;
        }

        /// <summary>
        /// End calibration (save or discard).
        /// </summary>
        public void EndCalibration()
        {
            IsCalibrating = false;
        }

        /// <summary>
        /// Record a tap point: image point (on the floor plan image, in pixels,
        /// origin top-left) and the world position at the time of the tap.
        ///
        /// Use SpatialPointerPose to get the world position of the gaze/at-tap point.
        /// </summary>
        public void CollectTapPoint(Point imagePoint, Vector3 worldPos)
        {
            if (!IsCalibrating) return;
            _imagePoints.Add(imagePoint);
            _worldPoints.Add(worldPos);
        }

        /// <summary>
        /// After collecting 3+ points, compute the transform.
        /// Returns null if not enough points.
        /// </summary>
        public AffineFloorPlanTransform ComputeTransform()
        {
            if (_imagePoints.Count < 3 || _worldPoints.Count < 3)
                return null;
            return FloorPlanService.ComputeTransform(
                _imagePoints.ToArray(),
                _worldPoints.ToArray());
        }
    }
}
