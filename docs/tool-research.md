# Recherche für das Einrichtungs-Programm

Stand: 03.11.2026

## 1. Preset-Unlocker (NVIDIA-Treiberprofil, NVAPI DRS)

Quelle: `CustomSettingNames.xml` aus NVIDIA Profile Inspector (Orbmu2k/nvidiaProfileInspector,
Stand 13.10.2026). Werte werden ins **Profil von Elite Dangerous** geschrieben (Anwendung
`EliteDangerous64.exe`), nie ins globale Basisprofil.

| Setting-ID | Name | Werte |
|---|---|---|
| `0x10E41E01` | DLSS – Enable DLL Override | 0 aus, 1 an (neueste installierte DLSS-SR-Version) |
| `0x00634291` | DLSS – Forced Model Preset Profile | 0 N/A, 1 Recommended, 2 Custom |
| `0x10E41DF3` | DLSS – Forced Preset Letter | `0x00FFFFFF` empfohlen, 0x0A J, **0x0B K**, 0x0C L, 0x0D M, 0x0E N, 0x0F O |
| `0x10E41E03` | DLSS-FG – Enable DLL Override | 0 aus, 1 an (neueste installierte DLSS-FG-Version) |
| `0x10E41DF1` | DLSS-FG – Forced Preset Letter | `0x00FFFFFE` empfohlen, 1 A (Default), 2 B (UI Focus), **3 C (Performance Focus)**, 4 D … 0x12 R |
| `0x10E41E06` | DLSS – Enable Streamline Override | 0 aus, 1 an (ab Treiber 616.56) |
| `0x10308298` | DLSS-FG – Forced Mode | 0 N/A, 2 Fixed, 4 Dynamic (Dynamic nur RTX 50) |

Erkenntnisse:
- `sl.log` vom 02.11.: FG-Preset 3 = **C (Performance Focus)**, „Is Latest: No“.
  **Preset B (UI Focus)** könnte für Elites HUD interessant sein → im Test vergleichen.
- Bestätigt: Das Treiber-Override übersteuert `dlss_preset` des Add-ons
  (ini stand auf 10/J, angewendet wurde K).
- **Administratorrechte:** Profile Inspector verlangt `requireAdministrator` (app.manifest).
  Das Tool fordert Admin-Rechte nur für den Schritt „Presets schreiben“ an (eigener Prozess
  mit Elevation), der Rest läuft ohne.
- **Risiko:** Laut Community-Berichten (Guru3D, 2025) setzt die NVIDIA App Overrides für
  „nicht unterstützte“ Spiele beim Start teils zurück. Tool prüft deshalb nach jedem Start
  per `sl.log`, ob die Presets angewendet wurden, und bietet „Erneut anwenden“ an.
- Umsetzung: NVAPI aus C# per P/Invoke (`nvapi64.dll`, `NvAPI_DRS_*`), oder kleines
  C++-Hilfsprogramm. Entscheidung beim Bau.

## 2. Lizenzen der Shader-Quellen

Download-URLs aus der offiziellen Paketliste des ReShade-Installers
(`crosire/reshade-shaders`, Branch `list`, `EffectPackages.ini`).

| Baustein | Paket | Download | Lizenz |
|---|---|---|---|
| Deband | Standard effects | `github.com/crosire/reshade-shaders/archive/slim.zip` | MIT-artig (Niklas Haas), Credit nennen |
| Cinetools LUT | Color effects by prod80 | `github.com/prod80/prod80-ReShade-Repository/archive/master.zip` | MIT |
| LocalContrastCS | Insane-Shaders by Lord of Lunacy | `github.com/LordOfLunacy/Insane-Shaders/archive/master.zip` | CC0 |
| Vibrance, CAS | SweetFX by CeeJay.dk | `github.com/CeeJayDK/SweetFX/archive/master.zip` | MIT |

Ergebnis: Alle Bausteine des Filter-Sets sind frei nutzbar. Das Tool lädt trotzdem aus den
Original-Quellen (wie der ReShade-Installer) und zeigt die Credits an.
Nicht im Set: LumeniteFX (Copyright „All rights reserved“, AGNYA-Lizenz).

## 3. ReShade mit Add-on-Unterstützung

- Aktuell **ReShade 6.8.0** (02.08.2026).
- Download: `https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe`
  (Add-on-Version ist **unsigniert** → Tool prüft SHA-256 gegen bekannte Werte).
- Der Installer enthält am Ende ein Zip-Archiv (Signatur `PK\x03\x04`) mit `ReShade64.dll`.
  Das Tool extrahiert `ReShade64.dll` genauso wie der Installer und kopiert sie als `dxgi.dll`
  in den Spielordner (Quelle: `crosire/reshade`, `setup/MainWindow.xaml.cs`).
- Alternative: Installer mit `--headless --api dxgi` starten. Eigenes Extrahieren ist
  kontrollierter (keine fremden Effekt-Downloads, kein Überschreiben von Einstellungen).
- Bestehende `ReShade.ini` wird nicht ersetzt, nur ergänzt (Backup vorher).
