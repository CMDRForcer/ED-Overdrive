# ED-Overdrive

Einrichtungs-Programm für **Elite Dangerous Odyssey** (DX11): installiert und pflegt
[EDAA](https://github.com/CMDRForcer/EDAA) – Kantenglättung (NVIDIA DLAA, Intel XeSS, TAA) und echte
**DLSS Frame Generation** – samt ReShade, Filter-Set und optional **DLSS 5** (GITC Uplift).

Eine Datei, keine Installation: `ED-Overdrive.exe` aus den [Releases](https://github.com/CMDRForcer/ED-Overdrive/releases)
herunterladen und starten. Die App bringt .NET mit.

## Was mitgeliefert wird und was nicht

| Teil | In der exe | Lizenz |
|---|---|---|
| ReShade 6.8.0 (Add-on-Version, `dxgi.dll`) | ✅ | BSD-3-Clause |
| EDAA (Add-on) | ✅ | MIT |
| NVIDIA Streamline, DLSS (Super Resolution, Frame Generation) | ✅ als Teil von EDAA | NVIDIA RTX SDK / NGX SDK |
| Intel XeSS | ✅ als Teil von EDAA | Intel Simplified Software License |
| GITC Uplift 1.8.0 (DLSS 5) | ✅ | MIT |
| Filter-Shader (Deband, CAS, Vibrance, Fake-HDR, LUT, LocalContrast, Schwarzwert-Fix …) | ❌ werden beim Einrichten aus den Original-Repos der Autoren geladen | MIT, CC0, GPL-3.0 (je Shader) |
| qUINT (MXAO, Bloom, DOF) | ❌ optionaler Download | „All rights reserved“, darf nicht mitgeliefert werden |
| `nvngx_dlssnr.dll` (DLSS 5) | ❌ wählst du selbst aus | NVIDIA, darf nicht weitergegeben werden |

Die Installation von ReShade und EDAA braucht also **kein Internet**; nur die Filter und der Kanal
„Entwicklung“ laden etwas herunter. Alle Lizenztexte stehen in [`licenses/`](licenses) und
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md); bei der Installation legt die App sie zusätzlich in
`ED-Overdrive-Lizenzen` im Spielordner ab.

## Voraussetzungen

- Windows 10/11, 64 Bit
- Elite Dangerous Odyssey (Steam, Epic oder Frontier) im DX11-Modus
- Für DLAA/DLSS: NVIDIA RTX. Für Frame Generation: **RTX 40/50**. XeSS und TAA laufen auf jeder DX11-Karte.
- Für DLSS 5: die passende `nvngx_dlssnr.dll` (RTX 50: NVIDIA-Original; RTX 20/30/40: angepasster Community-Build,
  unsigniert, auf eigenes Risiko)

## So geht's

1. **Elite beenden.** Die App ändert nur bei geschlossenem Spiel etwas und legt vorher immer ein Backup an.
2. **Übersicht:** Spielordner wählen (wird meist gefunden) → *Installieren*. Für einen sauberen Neuanfang
   *Saubere Neuinstallation*: alte Dateien werden nach `%LOCALAPPDATA%\GenFrame\Entfernt` verschoben, nicht gelöscht.
3. **Schnellwahl → Spiel-Setup:** mit einem Klick das ganze Setup wählen
   - **Supersampling 1,25:** DLAA, Ultra-Grafik, DLSS 5 aus – gemessen auf einer RTX 4070 Ti in 1440p:
     70 echte → 140 angezeigte Bilder/s im All, etwa 65 echte im vollen Concourse.
   - **DLSS 5:** Elite rendert mit 0,85, DLSS 5 verbessert das Bild, DLSS skaliert hoch – etwa 67 → 135 Bilder/s.
4. Spiel starten. Im Spiel öffnet **Pos1** das ReShade-Overlay (Reiter „EDAA“ und „GITC Uplift“).

## Seiten der App

| Seite | Wofür |
|---|---|
| Übersicht | Spielordner, Installation, Kanal (Stabil = in der App enthalten, Entwicklung = neuester Build) |
| Schnellwahl | Spiel-Setups und EDAA-Modi (DLAA + FG, nur DLAA, nur FG, aus) |
| Filter | ReShade-Filter-Varianten (Natürlich, Lebendig, Leistung, DLSS 5) |
| Grafik | Elite-Grafikwerte über das Spielmenü hinaus, vier Stufen |
| DLSS 5 | GITC Uplift installieren, DLL wählen, optimieren, entfernen |
| Elite-Check | prüft Einstellungen, die mit EDAA kollidieren (Supersampling, CAS, Checkerboard …) |
| Presets | FG-Preset im NVIDIA-Treiberprofil von Elite (nur auf Knopfdruck, mit Backup) |
| Einstellungen | EDAA-Werte aus `ReShade.ini` |
| Diagnose | packt Logs, Einstellungen und einen Prüfbericht in ein Zip zum Weitergeben (ohne Journale) |
| Backups | alle Backups ansehen und wiederherstellen |

## Hinweise

- Im EDAA-Overlay **„Resolve before the HUD pass“** anlassen. DLSS 5 braucht EDAAs Kantenglättung mit NVIDIA DLSS.
- Nur **ein** Bildlimit nutzen: das von EDAA (knapp unter der Bildwiederholrate, z. B. 140 bei 144 Hz).
- EDHM/3DMigoto wird unterstützt. RenoDX mit Frame Generation gibt ein schwarzes Bild: im Overlay unter „Add-ons“ abschalten.
- Die App schreibt nie in die Registry. Einzige Ausnahme ist die Seite „Presets“, nur auf Knopfdruck und mit Backup.

## Bauen

```
dotnet publish tool/GenFrame.Setup/GenFrame.Setup.csproj -p:PublishProfile=Release
```

Vor dem Bauen lädt `tool/GenFrame.Setup/ThirdParty/fetch-bundle.ps1` das EDAA-Release-Paket und den
ReShade-Installer und prüft beide per SHA-256 (nicht im Git). Ein neues EDAA-Release: Version und Prüfsumme dort
und in `InstallService.BundledEdaa` anpassen. Ein veröffentlichtes Release baut die exe per GitHub Actions
(`.github/workflows/release.yml`) und hängt sie an.
