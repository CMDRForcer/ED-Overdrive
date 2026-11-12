# Vendor builds

The app and the add-on can be built for NVIDIA (default) or AMD graphics cards from the
same source. Rule: **every build carries everything that runs on that vendor's cards.**
Vendor-neutral parts are in both. Releases ship the NVIDIA build only.

| Feature | Runs on NVIDIA | Runs on AMD | NVIDIA build | AMD build |
|---|---|---|---|---|
| EDAA TAA, motion vectors, HUD protection, ship detection | yes | yes | yes | yes |
| Intel XeSS | yes | yes | yes | yes |
| NVIDIA DLSS / DLAA | yes | no | yes | no |
| NVIDIA DLSS-G (Streamline) and Reflex | RTX 40/50 | no | yes | no |
| App: DLSS 5 (GITC Uplift) | RTX | no | yes | no |
| App: NVIDIA driver profile presets | yes | no | yes | no |

A new feature gets a row here before it ships.

Building:

- Add-on: `cmake -S edaa -B edaa/build-amd -DEDAA_VENDOR=amd` (default `nvidia`). The AMD
  build defines `EDAA_VENDOR_AMD=1`, leaves out the DLSS and Streamline SDKs and maps a
  saved DLSS method to the EDAA TAA.
- Package: `edaa/tools/bundle.ps1 -Vendor amd` (no NVIDIA DLLs or licenses).
- App: `dotnet publish app/GenFrame.Setup.csproj -p:PublishProfile=Release -p:Vendor=amd`
  (exe `ED-Overdrive-AMD.exe`, red theme, embeds `app/ThirdParty/EDAA-amd.zip`). Delete
  `app/obj` between the two app builds.
