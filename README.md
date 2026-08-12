# HololensHermes

<a href="https://get.microsoft.com/installer/download/9nr3z5g9gbj7?referrer=appbadge" target="_self" >
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

[HoloLens Hermès](https://apps.microsoft.com/detail/9nr3z5g9gbj7?hl=en-GB&gl=NO) —
indoor spatial assistant for **Microsoft HoloLens 1**, connected to **Hermes** via Telegram.

The app uses HoloLens 1's on-device spatial mapping and world-locked anchors to place
2D floor plans and target markers in the real world, and connects to Hermes over Telegram
for goal-directed indoor navigation (e.g. find a book in the library, find a furniture
section in a store).

---

## Contents

- [What You Will See](#what-you-will-see)
- [How It Works](#how-it-works)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Build](#build)
- [Deploy to HoloLens](#deploy-to-holoLens)
- [CI / CD](#ci--cd)
- [Privacy Policy](#privacy-policy)

---

## What You Will See

Put on the HoloLens and launch the app:

- A 2D floor plan overlays the real floor, world-locked and compass-rotated (building
  orientation stays correct as you turn).
- Tap a real surface to ask Hermes "find X" — a pulsing target marker + arrow appears,
  world-locked at the target location on the floor plan.
- The floor plan + target markers persist across sessions via `SpatialAnchorStore`.
- Telegram companion: you can query and report from your phone; Hermes answers with a
  floor-plan hotspot and the HoloLens shows the world-locked target.

---

## How It Works

| Step | Detail |
|------|--------|
| **Floor plan** | 2D floor-plan image (PNG from the venue's website) is loaded as a texture, scaled to real meters by calibration, and rendered as a world-locked quad on the floor |
| **Calibration** | Multi-point (3+): the user walks to known floor-plan points and taps; the app solves the affine transform from image pixels → world meters |
| **Spatial mapping** | `SpatialSurfaceObserver` keeps the room/store mesh up to date; the user can toggle mesh visualization to check coverage |
| **Targets** | Hermes API resolves a goal ("find the philosophy section", "find MALM dresser") to a floor-plan position; the app places a `SpatialAnchor` there and shows a pulsing marker + arrow |
| **Compass** | `CompassService` (IMU magnetometer) rotates the floor plan + arrow so north stays north as the user turns |
| **Telegram** | The app stores the user's Telegram username + password + bot id in the Windows Credential Vault; the bot id (BotFather token) is used to call the Telegram Bot API over HTTPS |
| **Hermes** | `HermesApiService` calls the Hermes backend over HTTPS (goal resolution, floor-plan metadata) using the same `HttpClient` + `Windows.Data.Json` pattern as the existing viewers |

No external render engine — all rendering is custom Direct3D 11 with SharpDX (forked from the HololensSatelliteViewer holographic pipeline).

---

## Project Structure

```
HololensHermes/
├── .github/workflows/
│   ├── dotnet.yml              # CI compile check (Debug + Release)
│   ├── dotnet-desktop.yml      # Signed .appxupload artifact on push
│   └── store-submission.yml    # Full Store pipeline: build, WACK, package, submit
├── Assets/                     # PNG logos/splash at required sizes
├── Common/
│   └── DeviceResources.cs      # Direct3D device management
├── Content/
│   ├── FloorPlanRenderer.cs    # 2D floor-plan quad, world-locked, compass-rotated
│   ├── SpatialMappingRenderer.cs  # Optional mesh visualization
│   ├── AnchorRenderer.cs       # Pulsing target marker + arrow at a POI anchor
│   ├── SpatialInputHandler.cs  # Gesture/click input
│   └── Shaders/                # D3D11 shaders (shared with the viewer scaffold)
├── Models/
│   └── FloorPlan.cs            # Floor plan metadata (image URI, size in meters, north rotation)
├── Services/
│   ├── HermesApiService.cs     # HTTPS client to Hermes backend (goal, floor-plan, feedback)
│   ├── TelegramService.cs      # Telegram Bot API wrapper + Credential Vault storage
│   ├── FloorPlanService.cs     # Floor plan bitmap loading + affine transform solver
│   ├── CalibrationService.cs   # Multi-point calibration (user taps known floor-plan points)
│   ├── SpatialMappingService.cs  # Wraps SpatialSurfaceObserver
│   ├── AnchorStoreService.cs  # Persist SpatialAnchors via SpatialAnchorStore
│   └── CompassService.cs      # IMU magnetometer heading (reused from viewer scaffold)
├── privacy/
│   └── index.html              # Privacy policy (served via GitHub Pages)
├── Properties/
│   └── AssemblyInfo.cs
├── BasicHologramMain.cs        # App lifecycle + holographic frame loop
├── HololensHermes.csproj       # UWP project — .NETCore 5.0, x86
├── HololensHermes_TemporaryKey.pfx  # Dev signing cert
├── Package.appxmanifest        # Identity, capabilities, logos
└── deploy.ps1                  # One-shot deploy to HoloLens over USB
```

---

## Prerequisites

| Requirement | Version / Notes |
|-------------|-----------------|
| Windows | 10 or 11 (64-bit) |
| Visual Studio 2022 | Community (free) or higher — **UWP workload** required |
| Windows 10 SDK | **10.0.19041.0** (included in UWP workload) |
| HoloLens 1 | Developer Mode enabled |
| Cable | Micro-USB to USB-A |
| Hermes backend | Running and reachable from the HoloLens (Wi-Fi or USB RNDIS) |
| Telegram bot | Created via @BotFather; bot token (bot id) provided in-app |

---

## Build

Use MSBuild from Visual Studio 2022. The cross-platform `dotnet build` CLI
cannot resolve Windows XAML targets.

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
```

### Debug build — compile check only

```powershell
& $msbuild HololensHermes.csproj `
    /p:Configuration=Debug `
    /p:Platform=x86 `
    /p:AppxPackageSigningEnabled=false `
    /p:GenerateAppxPackageOnBuild=false `
    /v:minimal
```

### Release build — signed .appxupload (Store-ready)

```powershell
& $msbuild HololensHermes.csproj `
    /t:Publish `
    /p:Configuration=Release `
    /p:Platform=x86 `
    /p:AppxBundle=Never `
    /p:UapAppxPackageBuildMode=StoreUpload `
    /p:AppxPackageDir=AppPackages\ `
    /p:AppxPackageSigningEnabled=true `
    /p:PackageCertificateKeyFile=HololensHermes_TemporaryKey.pfx `
    /p:PackageCertificatePassword=ci `
    /v:minimal
```

Output lands in `AppPackages\HololensHermes_1.0.0.0_x86_Test\`.

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
$pkg = "AppPackages\HololensHermes_1.0.0.0_x86_Test"
& $wadc install `
    -f  "$pkg\HololensHermes_1.0.0.0_x86.appx" `
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
|----------|---------|----------|
| `dotnet.yml` | Push / PR to `main` | Compile check (Debug + Release) |
| `dotnet-desktop.yml` | Push to `main` | Signed `.appxupload` artifact |
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

## Privacy Policy

This app does not collect or transmit personal information except for the
purpose of goal-directed indoor navigation with Hermes. The location, webcam,
and microphone capabilities are used exclusively for HoloLens platform
operation. Telegram credentials (username, password, bot id) are stored
locally in the Windows Credential Vault and are never transmitted except
as needed to call the Telegram Bot API on the user's behalf.

Full policy: https://turbolego.github.io/HololensHermes/privacy/
