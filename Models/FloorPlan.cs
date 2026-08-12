namespace HololensHermes.Models
{
    /// <summary>
    /// A floor plan image for a library / store mapped onto real-world meters.
    /// </summary>
    public sealed class FloorPlan
    {
        /// <summary>
        /// URI of the floor plan image (PNG/SVG served by a web endpoint, e.g. the library's site or store's page).
        /// </summary>
        public string ImageUri { get; set; }

        /// <summary>
        /// Real-world width in meters represented by the floor plan image's horizontal extent.
        /// </summary>
        public float RealWorldWidthMeters { get; set; }

        /// <summary>
        /// Real-world height in meters represented by the floor plan image's vertical extent.
        /// </summary>
        public float RealWorldHeightMeters { get; set; }

        /// <summary>
        /// Angle (degrees, clockwise from north) the floor plan's top edge is rotated relative to north.
        /// So if the building's main corridor runs east–west, top edge = 90°.
        /// </summary>
        public float NorthRotationDegrees { get; set; }
    }
}
