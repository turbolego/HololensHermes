# HololensSatelliteViewer

[![UWP Build](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/dotnet.yml/badge.svg)](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/dotnet.yml)
[![UWP Package](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/dotnet-desktop.yml)
[![Store Submission](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/store-submission.yml/badge.svg)](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/store-submission.yml)
![Platform x86](https://img.shields.io/badge/platform-x86-blue)
![SDK 10.0.19041](https://img.shields.io/badge/Windows%20SDK-10.0.19041-blue)
![HoloLens 1](https://img.shields.io/badge/HoloLens-1st%20gen-blueviolet)

Real-time satellite tracker for **Microsoft HoloLens 1** built on the UWP platform.

Satellites are rendered as holographic 3D cubes in the dome **above** the user,
positioned from real Two-Line Element (TLE) data fetched from
[CelesTrak](https://celestrak.org/). A debug panel below the GPS location shows
each satellite's name, azimuth, elevation, and relative position.
---

<img width="1408" height="792" alt="2953" src="https://github.com/user-attachments/assets/d827aa66-2a03-4f56-a6e5-423079ef0861" />

---

## Contents

- [What You Will See](#what-you-will-see)
- [How It Works](#how-it-works)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Build](#build)
- [Deploy to HoloLens](#deploy-to-holoLens)
- [CI / CD](#ci--cd)
- [Microsoft Store](#microsoft-store)
- [Privacy Policy](#privacy-policy)

---

## What You Will See

Put on the HoloLens and launch the app:

- Satellites appear as small coloured cubes **above you** in a dome arrangement,
  their positions computed from live TLE orbital data and your GPS location.
  The brightest (closest) 10 satellites are shown.
- A **text panel** floats in front of you below eye level, listing:
  - Your current **GPS coordinates**
  - TLE data stats (loaded, propagated, above horizon)
  - Each visible satellite's **name, azimuth, elevation, and relative X/Z position**
- Because black pixels are transparent on HoloLens's see-through display, the
  scene appears to **float in the real world** — no background, no window frame.

---

## How It Works

| Step | Detail |
|---|---|
| **GPS** | HoloLens `Geolocator` gets the device's current lat/lon/altitude |
| **TLE fetch** | Fetches active satellite Two-Line Elements from CelesTrak every second |
| **Orbit propagation** | SGP4 propagator computes each satellite's topocentric azimuth, elevation, and range from the observer |
| **Sorting** | The 10 closest satellites are selected by range |
| **Rendering** | Direct3D 11 holographic pipeline draws each satellite as a colour-coded cube with a label above it, positioned 0.4 m to the ceiling (with some vertical spread per elevation) |
| **Text panel** | Custom bitmap glyph rendering via a geometry shader animates the info list in 3D at a fixed offset from the user's head position |

No external render engine — all rendering is custom Direct3D 11 with SharpDX.

---

## Project Structure

```
HololensSatelliteViewer/
├── .github/workflows/
│   ├── dotnet.yml              # CI compile check (Debug + Release)
│   ├── dotnet-desktop.yml      # Signed .appxupload artifact on push
│   └── store-submission.yml    # Full Store pipeline: build, WACK, package, submit
├── Assets/                     # PNG logos/splash at required sizes
├── Common/
│   └── DeviceResources.cs      # Direct3D device management
├── Content/
│   ├── SatelliteRenderer.cs    # Holographic satellite cube + text rendering
│   ├── SpatialInputHandler.cs  # Gesture/click input
│   └── SpinningCubeRenderer.cs # Sample cube (from template)
├── Helpers/
│   └── HolographicPositioning.cs  # Lat/lon/alt to world coordinates
├── Models/
│   └── Satellite.cs            # Satellite data model (az/el/range/name)
├── Services/
│   ├── GeolocationService.cs   # HoloLens GPS provider
│   ├── OrbitService.cs         # TLE fetch + topocentric calculations
│   ├── Sgp4Service.cs          # Kepler / simplified SGP4 propagator
│   └── TleService.cs           # CelesTrak HTTP client
├── privacy/
│   └── index.html              # Privacy policy (served via GitHub Pages)
├── properties/
│   └── AssemblyInfo.cs
├── BasicHologramMain.cs        # App lifecycle + holographic frame loop
├── HololensSatelliteViewer.csproj  # UWP project — .NETCore 5.0, x86
├── HololensSatelliteViewer_TemporaryKey.pfx  # Dev signing cert
├── Package.appxmanifest        # Identity, capabilities, logos
└── deploy.ps1                  # One-shot deploy to HoloLens over USB
```

---

## Prerequisites

| Requirement | Version / Notes |
|---|---|
| Windows | 10 or 11 (64-bit) |
| Visual Studio 2022 | Community (free) or higher — **UWP workload** required |
| Windows 10 SDK | **10.0.19041.0** (included in UWP workload) |
| HoloLens 1 | Developer Mode enabled |
| Cable | Micro-USB to USB-A |

---

## Build

Use MSBuild from Visual Studio 2022. The cross-platform `dotnet build` CLI
cannot resolve Windows XAML targets.

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
```

### Debug build — compile check only

```powershell
& $msbuild HololensSatelliteViewer.csproj `
    /p:Configuration=Debug `
    /p:Platform=x86 `
    /p:AppxPackageSigningEnabled=false `
    /p:GenerateAppxPackageOnBuild=false `
    /v:minimal
```

### Release build — signed .appxupload (Store-ready)

```powershell
& $msbuild HololensSatelliteViewer.csproj `
    /t:Publish `
    /p:Configuration=Release `
    /p:Platform=x86 `
    /p:AppxBundle=Never `
    /p:UapAppxPackageBuildMode=StoreUpload `
    /p:AppxPackageDir=AppPackages\ `
    /p:AppxPackageSigningEnabled=true `
    /p:PackageCertificateKeyFile=HololensSatelliteViewer_TemporaryKey.pfx `
    /p:PackageCertificatePassword=ci `
    /v:minimal
```

Output lands in `AppPackages\HololensSatelliteViewer_1.0.0.0_x86_Test\`.

---

## Deploy to HoloLens

### 1. Enable Developer Mode on HoloLens

1. **Start menu → Settings → Update & Security → For developers**
2. Toggle **Use developer features → On**
3. Toggle **Enable Device Portal → On**

### 2. Connect via USB

Connect the HoloLens with a **Micro-USB to USB-A** cable. Windows installs a
**Remote NDIS (RNDIS)** driver — the device becomes reachable at `127.0.0.1`.

### 3. Install with WinAppDeployCmd

```powershell
# Locate the tool
$wadc = (Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin"
    -Recurse -Filter "WinAppDeployCmd.exe" |
    Sort-Object FullName | Select-Object -Last 1).FullName

# Install the .appx + dependencies
$pkg = "AppPackages\HololensSatelliteViewer_1.0.0.0_x86_Test"
& $wadc install `
    -f  "$pkg\HololensSatelliteViewer_1.0.0.0_x86.appx" `
    -ip 127.0.0.1 `
    -d  "$pkg\Dependencies\x86\Microsoft.NET.Native.Framework.1.3.appx" `
    -d  "$pkg\Dependencies\x86\Microsoft.NET.Native.Runtime.1.4.appx" `
    -d  "$pkg\Dependencies\x86\Microsoft.VCLibs.x86.14.00.appx"
```

First-time pairing: the HoloLens shows a 6-digit PIN — add `-pin 123456`.

### Quick re-deploy

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy.ps1
```

---

## CI / CD

Three GitHub Actions workflows run on `windows-2022` runners.

| Workflow | Trigger | Produces |
|---|---|---|
| `dotnet.yml` | Push / PR to `master` | Compile check (Debug + Release) |
| `dotnet-desktop.yml` | Push to `master` | Signed `.appxupload` artifact |
| `store-submission.yml` | Tag `v*.*.*` | `.appxupload` + WACK + optional Store publish |

### `dotnet.yml` — compile check

Builds Debug and Release with signing disabled. Catches build regressions.

### `dotnet-desktop.yml` — signed artifact

Generates a fresh self-signed cert per run, builds a full signed Release
`.appxupload` via the `Publish` target with `UapAppxPackageBuildMode=StoreUpload`,
produces a downloadable artifact, then removes the cert.

### `store-submission.yml` — Store pipeline

Triggered by a `v*.*.*` git tag. Same build as above plus:

1. Windows App Certification Kit (WACK) validation
2. Submission to Partner Center via `microsoft-store-apppublisher` action
   (requires `AZURE_AD_TENANT_ID`, `AZURE_AD_CLIENT_ID`,
   `AZURE_AD_CLIENT_SECRET`, `SELLER_ID` secrets configured in repo)

---

## Microsoft Store

The `.appxupload` from the CI artifact can be uploaded directly to
[Partner Center](https://partner.microsoft.com/dashboard).

Supported architecture: **x86** (HoloLens 1).

Package identity: `Turbolego.HololensSatelliteViewer`
Publisher: `CN=BB1A7F2A-A87C-44C8-8C14-84C6486E7E75`

---

## Privacy Policy

This app does not collect or transmit personal information. The location,
webcam, and microphone capabilities are used exclusively for HoloLens platform
operation — no data leaves the device except for public TLE requests to CelesTrak.

Full policy: https://turbolego.github.io/HololensSatelliteViewer/privacy/
