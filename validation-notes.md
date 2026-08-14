# Validation Notes

## HoloLens 1 emulator feasibility

This development environment is Ubuntu Linux and does not provide Windows, Visual Studio, Hyper-V, the Windows SDK, or a DirectX 11/WDDM graphics host. The HoloLens 1 emulator therefore cannot be installed or run here.

Microsoft documents that the first-generation emulator is a Hyper-V virtual machine and requires 64-bit Windows 10 Pro, Enterprise, or Education; a 64-bit four-core CPU; at least 8 GB RAM; hardware virtualization and DEP; DirectX 11; and a WDDM 1.2 driver. It is deployed from Visual Studio with the UWP project platform set to x86. The latest listed first-generation image is build 10.0.17763.134, released November 19, 2018.

## Recommended Windows validation

Use a Windows 10 Pro/Enterprise/Education workstation with Hyper-V enabled, Visual Studio 2017 (or VS 2022 plus the first-generation template extension), the UWP workload, and Windows SDK 10.0.19041.0. Deploy the x86 Debug build to the HoloLens Emulator and exercise the simulated room, air-tap input, calibration, spatial mapping visualization, and location-unavailable behavior. A physical HoloLens 1 is still required to validate real Wi-Fi position quality, compass behavior, camera sensing, and spatial-anchor persistence.

## Sources

- https://learn.microsoft.com/en-us/windows/mixed-reality/develop/install-the-tools
- https://learn.microsoft.com/en-us/windows/mixed-reality/develop/advanced-concepts/using-the-hololens-emulator
- https://learn.microsoft.com/en-us/windows/mixed-reality/develop/advanced-concepts/hololens-emulator-archive
