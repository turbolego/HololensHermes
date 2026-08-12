// Helper: approximate geodesic distance (lat/lon degrees → meters).
// Used by the Wi-Fi positioning tests to verify "close enough" semantics.
// At Oslo latitude 1 degree latitude ≈ 111320 m; longitude is compressed by
// cos(latitude). This is a flat-earth approximation good enough for the
// < 100 m scale of Wi-Fi-positioning assertions.
public static double GeodesicDistanceMeters(double lat1, double lon1, double lat2, double lon2)
{
    var dx = (lon2 - lon1) * 111320.0 * Math.Cos((lat1 + lat2) / 2.0 * Math.PI / 180.0);
    var dy = (lat2 - lat1) * 111320.0;
    return Math.Sqrt(dx * dx + dy * dy);
}
