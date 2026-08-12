using System;
using System.Threading.Tasks;
using Windows.Perception.Spatial;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HololensHermes.Services
{
    /// <summary>
    /// Persist a SpatialAnchor to disk + SpatialAnchorStore so it survives app restarts.
    ///
    /// Used to remember target POI locations (book, furniture, aisle marker)
    /// across sessions — the user's target markers stay where they were placed.
    /// </summary>
    public sealed class AnchorStoreService
    {
        private const string AnchorStoreName = "HololensHermes";

        /// <summary>
        /// Create a SpatialAnchor at the given world position and persist its id.
        /// Return the persisted anchor id string.
        /// </summary>
        public async Task<string> CreateAndPersistAnchorIdAsync(SpatialCoordinateSystem coordinateSystem, Vector3 position)
        {
            // Create a stationary anchor at the requested position.
            var anchor = coordinateSystem.CreateAnchor(Quaternion.CreateFromRotationMatrix(
                Matrix4x4.CreateTranslation(position)));
            // Persist via SpatialAnchorStore.
            var store = SpatialAnchorStore.GetDefault();
            if (store == null) throw new InvalidOperationException("No SpatialAnchorStore available");
            await store.RequestAccessAsync();
            var anchorId = anchor.SpatialId.ToString();
            // Store the anchor id + metadata.
            // For now we save the id; full metadata (label, type) stored separately.
            store.Add(anchor);
            // Note: Store.add requires the anchor to be a SpatialAnchor with its id.
            return anchorId;
        }

        /// <summary>
        /// Load an existing anchor by id (or metadata) and return it.
        /// </summary>
        public async Task<SpatialAnchor> LoadAnchorByIdAsync(string anchorId)
        {
            var store = SpatialAnchorStore.GetDefault();
            if (store == null) return null;
            await store.RequestAccessAsync();
            var anchor = store.Get(anchorId);
            return anchor;
        }
    }
}
