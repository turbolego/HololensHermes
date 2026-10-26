# HololensHermes

<a href="https://get.microsoft.com/installer/download/9nr3z5g9gbj7?referrer=appbadge" target="_self" >
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

[HoloLens Hermès](https://apps.microsoft.com/detail/9nr3z5g9gbj7?hl=en-GB&gl=NO) —
a **Microsoft HoloLens 1** prototype for spatial guidance and a future Hermes
agent interface.

The repository contains a Direct3D holographic rendering scaffold and
platform-neutral venue-selection, calibration, and accessible-guidance logic.
The Telegram/Hermes, camera, voice-input, and response-presentation flows are
not yet wired into the running app. See [AGENT_INTERFACE_PLAN.md](./AGENT_INTERFACE_PLAN.md)
for the implementation plan and acceptance criteria.

---

## Contents

- [Current Status](#current-status)
- [How It Works](#how-it-works)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Build](#build)
- [Deploy to HoloLens](#deploy-to-holoLens)
- [CI / CD](#ci--cd)
- [Privacy Policy](#privacy-policy)

---

## Current Status

The current app is an incomplete prototype, not yet a usable Hermes chat client.
The repository includes:

- HoloLens 1 Direct3D rendering, spatial mapping, floor-plan, and target-marker
  components.
- Platform-neutral venue resolution, multi-point calibration, route validation,
  and accessible guidance logic.
- A speech-synthesis helper and prototype Hermes/Telegram service wrappers.
- Cross-platform tests for the navigation logic, including a deterministic
  end-to-end venue-to-guidance journey.

The running app does not yet load a floor plan or route, send messages to
Hermes, capture/upload photos or voice, receive agent replies, or display
conversation text. Those are planned in
[AGENT_INTERFACE_PLAN.md](./AGENT_INTERFACE_PLAN.md).

---

## How It Works

| Component | Current implementation |
|------|--------|
| **Floor plan** | A renderer can draw a floor-plan image as a texture, but no floor plan is loaded by the running app. |
| **Calibration** | Platform-neutral logic fits a transform from 3+ image/world point pairs; an in-app calibration flow is not connected. |
| **Spatial mapping** | Spatial-mapping service and renderer components exist; an in-app mesh toggle is not connected. |
| **Targets** | Route validation, guidance logic, and an anchor renderer exist; Hermes goal resolution and route loading are not connected. |
| **Compass** | `CompassService` reads heading for renderer transforms; the running app has no loaded floor plan to rotate. |
| **Telegram** | `TelegramService` is an incomplete direct Bot API prototype; it is not connected to the running interaction flow. The production design should use an authenticated relay and keep the bot token off-device. |
| **Hermes** | `HermesApiService` contains prototype goal/floor-plan/feedback calls, but is not connected to the running interaction flow and has no agreed conversational-agent contract yet. |

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
├── tests/HololensHermes.Tests/ # Cross-platform tests for platform-neutral logic
├── AGENT_INTERFACE_PLAN.md     # Remaining work and end-to-end acceptance criteria
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

This prototype requests a coarse Windows location estimate at startup and
stores it in memory; the running app does not currently send it to Hermes.
The Telegram and Hermes service wrappers can make HTTPS requests if called,
but are not connected to the running interaction flow. The Telegram prototype
stores credentials in the Windows Credential Vault and includes its bot token
in Telegram API requests when used. No photo or voice recording/upload flow is
implemented. See the [privacy policy](./privacy/index.html) for the current
behavior and the [agent interface plan](./AGENT_INTERFACE_PLAN.md) for the
privacy work required before adding media transmission.

Published policy: https://turbolego.github.io/HololensHermes/privacy/
