# HololensSatelliteViewer

[![UWP Build](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/dotnet.yml/badge.svg)](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/dotnet.yml)
[![UWP Package](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/dotnet-desktop.yml)
[![Store Submission](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/store-submission.yml/badge.svg)](https://github.com/turbolego/HololensSatelliteViewer/actions/workflows/store-submission.yml)
![Platform x86](https://img.shields.io/badge/platform-x86-blue)
![SDK 10.0.19041](https://img.shields.io/badge/Windows%20SDK-10.0.19041-blue)
![HoloLens 1](https://img.shields.io/badge/HoloLens-1st%20gen-blueviolet)

Real-time satellite tracking and TLE visualisation for **Microsoft HoloLens 1**.
Built with UWP / .NET Core v5.0 and no external game engine.

Satellite positions are computed on device using **SGP4 propagator** from up-to-date
Two-Line Element (TLE) sets fetched from [CelesTrak](https://celestrak.org/).
A software-rasterised 3D view renders the overhead pass with orbital paths overlaid
on the real world.

---

## Contents

- [What You Will See](#what-you-will-see)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Build](#build)
- [Deploy to HoloLens](#deploy-to-holoLens)
- [CI / CD](#ci--cd)
- [Microsoft Store Submission](#microsoft-store-submission)
- [Privacy Policy](#privacy-policy)

---

## What You Will See

When the app launches on HoloLens 1, a 3D globe or orbital view of the night sky
appears in front of you, with satellite positions plotted from current TLE data.
Because black pixels are transparent on HoloLens's see-through display, the
visualisation appears to **float in the real world**.

---

## Project Structure

```
HololensSatelliteViewer/
├── .github/workflows/
│   ├── dotnet.yml              # CI compile check (Debug + Release)
│   ├── dotnet-desktop.yml      # Signed .appxupload artifact on push
│   └── store-submission.yml    # Full Store pipeline: build, WACK, package, submit
├── Assets/                     # PNG logos/splash at required sizes
├── Properties/
│   └── AssemblyInfo.cs
├── scripts/
│   ├── create_cert.ps1         # Regenerate signing certificate
│   └── make_assets.py          # Generate placeholder PNGs
├── services/                   # SGP4 propagator + TLE fetching
├── privacy/
│   └── index.html              # Privacy policy (served via GitHub Pages)
├── .nojekyll                   # Disable Jekyll for GitHub Pages
├── HololensSatelliteViewer.csproj  # UWP project — .NETCore 5.0, x86
├── HololensSatelliteViewer_TemporaryKey.pfx  # Dev signing cert
├── MainPage.xaml / MainPage.xaml.cs       # Main rendering view
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
| Python 3 | 3.6+ — only needed for asset generation |
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
| `dotnet-desktop.yml` | Push to `master` | Signed `.appxupload` + `.appx` artifact |
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

## Microsoft Store Submission

The `.appxupload` from the CI artifact can be uploaded directly to
[Partner Center](https://partner.microsoft.com/dashboard).

Supported architecture: **x86** (HoloLens 1).

---

## Privacy Policy

This app does not collect or transmit personal information.
Full policy: https://turbolego.github.io/HololensSatelliteViewer/privacy/
