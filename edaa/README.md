# EDAA - Temporal Anti-Aliasing for Elite Dangerous

A ReShade add-on that adds TAA (Temporal Anti-Aliasing) to Elite Dangerous
Odyssey (DX11): sub-pixel jitter, motion vectors from the game's camera and
depth, and a TAA resolve inserted into the game's frame before the cockpit
HUD, bloom and tonemapping. NVIDIA DLAA (DLSS at native resolution, RTX
GPUs) or Intel XeSS Native AA (any recent GPU, through a D3D12 bridge) can
replace the EDAA TAA at the same point.

**DLSS Frame Generation (RTX 40/50, added in ED-Overdrive):** the final
image is presented through a D3D12 swap chain driven by NVIDIA Streamline
(DLSS-G 2x, Reflex low latency and frame limiter), with EDAA's depth and
motion vectors and the cockpit HUD kept steady. Turn it on in the overlay
(Alt+R, EDAA, "Frame Generation"), restart the game once, and use its frame
limit (just below the refresh rate, e.g. 140 at 144 Hz) instead of any other
limiter. Needs Windows hardware-accelerated GPU scheduling.

* Original project by Siegfried-Origin: https://github.com/Siegfried-Origin/EDAA
  (website: https://siegfried-origin.github.io/EDAA/); this copy adds DLSS Frame Generation
* How it works, settings, diagnostics: [docs/addon.md](docs/addon.md)
* The game's rendering, reverse-engineered: [docs/reverse-engineering.md](docs/reverse-engineering.md)
* Finding camera matrices in any DX11 game: [docs/finding-camera-matrices.md](docs/finding-camera-matrices.md)
* Motion vectors design: [docs/motion-vectors.md](docs/motion-vectors.md)
* Analysis scripts (RenderDoc): [tools/README.md](tools/README.md)

## Install

The easiest way is **ED-Overdrive** (the app in this repository, see the
[main README](../README.md)): it installs ReShade and EDAA, keeps backups
and sets everything up.

### By hand

Game folder (Steam): `C:\Program Files (x86)\Steam\steamapps\common\Elite Dangerous\Products\elite-dangerous-odyssey-64`

1. Download **ReShade 6.8.0 or newer, "with full add-on support"** from
   https://reshade.me (the normal build does not load add-ons).
2. Run it, select `EliteDangerous64.exe` in the game folder and the API
   **DirectX 10/11/12**. It installs `dxgi.dll` and `ReShade.ini`. Skip the
   effect packages (none are needed).
3. Copy everything from `EDAA-<version>.zip` (attached to each
   [ED-Overdrive release](https://github.com/CMDRForcer/ED-Overdrive/releases))
   next to `dxgi.dll`: `EDAA.addon64`, `nvngx_dlss.dll` (DLAA), `libxess.dll`
   (XeSS), the `sl.*.dll` files and `nvngx_dlssg.dll` (Frame Generation).
4. Set the game's anti-aliasing to off and start the game.

## Uninstall

Close the game and delete the files from step 3. To remove ReShade too,
delete `dxgi.dll`, `ReShade.ini`, `ReShade.log` and the `reshade-shaders`
folder if present. Do not use the ReShade setup's "Uninstall" when EDHM is
installed: it also deletes EDHM's `d3d11.dll`.

## Building

Visual Studio 2022 (C++), CMake 3.20+ and the Windows SDK (fxc):

```
git submodule update --init --depth 1
cmake -S edaa -B edaa/build -G "Visual Studio 17 2022" -A x64
cmake --build edaa/build --config Release
```

For Frame Generation, extract the
[Streamline SDK 2.14.16](https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.16)
to `edaa/third_party/streamline` first. The output is in `edaa/build/Release`.
CI builds the add-on for every push and pull request
([.github/workflows/build.yml](../.github/workflows/build.yml)) and attaches
`EDAA-<version>.zip` to every release.

## Hotkeys

| Keys | Action |
|---|---|
| `Ctrl+Shift+T` | TAA on/off |

Developer builds (`-DEDAA_DEVELOPER=ON`, see [docs/addon.md](docs/addon.md))
add `Ctrl+Shift+J` (jitter alone), `Ctrl+Shift+M` (debug views) and
`Ctrl+Shift+C` (auto capture to `Documents\EDAA-captures`).

## License

EDAA is released under the [MIT License](LICENSE). The third-party
components below keep their own licenses.

## Third-party components

ReShade (BSD 3-Clause), Dear ImGui (MIT), the NVIDIA DLSS SDK (NVIDIA RTX
SDKs license) and the Intel XeSS SDK (Intel Simplified Software License)
are git submodules in `third_party/`; see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). NVIDIA and DLSS are
trademarks of NVIDIA Corporation, Intel and XeSS of Intel Corporation; EDAA
is not sponsored or endorsed by NVIDIA or Intel.
