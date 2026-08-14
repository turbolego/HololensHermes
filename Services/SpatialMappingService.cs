using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Perception.Spatial;
using Windows.Perception.Spatial.Surfaces;

namespace HololensHermes.Services
{
    /// <summary>
    /// Owns the HoloLens spatial-surface observer. Surface meshes are delivered
    /// to the renderer in the same spatial coordinate system used for anchors.
    /// </summary>
    public sealed class SpatialMappingService : IDisposable
    {
        private SpatialSurfaceObserver _observer;
        private readonly HashSet<Guid> _knownSurfaceIds = new HashSet<Guid>();
        private bool _disposed;

        public SpatialSurfaceObserver Observer { get { return _observer; } }

        public void Start()
        {
            if (_observer != null || _disposed)
                return;

            _observer = new SpatialSurfaceObserver();
        }

        public IReadOnlyList<Guid> GetSurfaceIds()
        {
            return new List<Guid>(_knownSurfaceIds);
        }

        /// <summary>
        /// Updates the observed surfaces for an explicit HoloLens world-space
        /// bounding volume. The renderer may then request individual meshes from
        /// the returned SpatialSurfaceInfo objects.
        /// </summary>
        public Task UpdateAsync(SpatialBoundingBox boundingBox)
        {
            if (_observer == null || _disposed)
                return Task.CompletedTask;

            _observer.SetBoundingVolume(boundingBox);
            var observedSurfaces = _observer.GetObservedSurfaces();
            _knownSurfaceIds.Clear();
            foreach (var surface in observedSurfaces)
            {
                _knownSurfaceIds.Add(surface.Key);
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _observer = null;
            _knownSurfaceIds.Clear();
            _disposed = true;
        }
    }
}
