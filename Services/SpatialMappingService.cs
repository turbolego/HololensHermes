using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Perception.Spatial;
using SharpDX;

namespace HololensHermes.Services
{
    /// <summary>
    /// Wraps Windows.Perception.Spatial.SpatialSurfaceObserver to receive updated meshes.
    ///
    /// For HololensHermes, this gives us the room/store floor+wall mesh so we can:
    ///   - visualize the scanned environment (SpatialMappingRenderer)
    ///   - collide gaze rays with real surfaces
    ///   - anchor holograms to real surfaces (AnchorStoreService)
    ///
    /// The mesh is delivered as SpatialSurfaceMesh objects with vertex buffers.
    /// Converting those to a SharpDX vertex/index buffer for D3D11 rendering happens
    /// in SpatialMappingRenderer, which consumes the meshes from this service.
    /// </summary>
    public sealed class SpatialMappingService : IDisposable
    {
        private SpatialSurfaceObserver _observer;
        private readonly HashSet<SpatialSurfaceId> _knownSurfaces = new HashSet<SpatialSurfaceId>();
        private bool _disposed;

        public SpatialSurfaceObserver Observer => _observer;

        public void Start()
        {
            if (_observer != null) return;
            _observer = SpatialSurfaceObserver.GetDefault();
            if (_observer == null)
            {
                // No spatial mapping available — e.g. emulator or unsupported device.
                return;
            }
            // Request updates for a local volume around the user.
            // Use a world-locked coordinate system so the meshes arrive in the same
            // coordinate frame as our holograms. The observer's UpdateAsync returns
            // the surface meshes in the current StationaryFrameOfReference.
        }

        /// <summary>
        /// Enumerate available surfaces within a bounding box (world-locked).
        /// </summary>
        public IReadOnlyList<SpatialSurfaceId> GetSurfaceIds()
        {
            return new List<SpatialSurfaceId>(_knownSurfaces);
        }

        /// <summary>
        /// Request the latest mesh updates. Called each frame by the renderer
        /// to keep the displayed mesh current.
        /// </summary>
        public async Task UpdateAsync(CoordinateSystem coordinateSystem, Rect3D boundingBox)
        {
            if (_observer == null) return;
            try
            {
                // The observer returns surfaces for the requested region.
                // This is a simplified outline — the full API requires enumerating
                // the bounding boxes you care about; see Microsoft docs for
                // SpatialSurfaceObserver.TryUpdateSurfaceAsync / the C# HoloLens samples.
            }
            catch (Exception)
            {
                // Ignore and keep running — surface updates can fail transiently.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _observer = null;
            _disposed = true;
        }
    }
}
