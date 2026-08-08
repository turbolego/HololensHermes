# VerifySatellites

End-to-end verifier for the satellite HoloLens app pipeline against **live
[CelesTrak](https://celestrak.org/) TLE data** — exercises the actual
production source files (linked via `<Compile Include>`), not copies.

Runs `OrbitService.GetLiveSatellitesAsync` (fetches the `visual` + `stations`
+ `geo` groups, SGP4 propagation, above-horizon filter) and the renderer's
top-10-by-elevation selection, then prints what the HoloLens dome would draw —
plus a GOES visibility probe for geostationary satellites.

## Usage

```bash
dotnet run                      # default: Lommedalen, Norway (59.9639707, 10.4698709)
dotnet run -- <lat> <lon>       # any observer GPS fix
```

Example:

```bash
dotnet run -- 59.9139 10.7522   # Oslo
```

## Output

- TLE records loaded (live `visual+stations+geo` merged, deduped by NORAD id)
- Propagated count, above-horizon count
- Top 10 by elevation — exactly what the renderer draws
- GOES satellites present in the app's data (geostationary birds were
  previously invisible: the app used to fetch only `visual` + `stations`)
- GOES probe: for GOES 2 (decommissioned — expect "NO TLE DATA") and the
  active GOES 16/17/18/19, propagates the real TLE through the production
  `Sgp4Service` + `OrbitService` ENU math (via reflection on the private
  `ComputeObserverRelativePosition`) and reports elevation/azimuth/range

## Notes

- Requires .NET 8 SDK.
- Live data: TLEs refresh from CelesTrak every few hours; satellite positions
  move continuously.
- The top-10-by-elevation ranking pins the fix that geostationary satellites
  must not be range-ranked (a GEO bird at ~35,000 km would always lose to a
  nearby LEO object in a range sort, even when high in the sky).
