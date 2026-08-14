using System;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Perception.Spatial;

namespace HololensHermes.Services
{
    /// <summary>
    /// Persists world-locked anchors so calibrated floor plans and resolved
    /// targets can be restored during a later session in the same mapped space.
    /// </summary>
    public sealed class AnchorStoreService
    {
        public async Task<string> CreateAndPersistAnchorIdAsync(
            SpatialCoordinateSystem coordinateSystem,
            Vector3 position)
        {
            if (coordinateSystem == null)
                throw new ArgumentNullException("coordinateSystem");

            var anchor = SpatialAnchor.TryCreateRelativeTo(
                coordinateSystem,
                position,
                Quaternion.Identity);
            if (anchor == null)
                throw new InvalidOperationException("Unable to create a spatial anchor at the requested position.");

            var store = await SpatialAnchorManager.RequestStoreAsync();
            if (store == null)
                throw new InvalidOperationException("No spatial anchor store is available.");

            var anchorId = Guid.NewGuid().ToString("N");
            if (!store.TrySave(anchorId, anchor))
                throw new InvalidOperationException("Unable to persist the spatial anchor.");

            return anchorId;
        }

        public async Task<SpatialAnchor> LoadAnchorByIdAsync(string anchorId)
        {
            if (string.IsNullOrWhiteSpace(anchorId))
                return null;

            var store = await SpatialAnchorManager.RequestStoreAsync();
            return store == null ? null : store.TryGet(anchorId);
        }
    }
}
