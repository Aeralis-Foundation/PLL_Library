<img src="https://raw.githubusercontent.com/Aurora-Aeralis/Assets/refs/heads/main/Notices/PEAKLevelLoader/Use-DirectX-11-Dark-1400_v2.png" alt="Use DirectX 11" align="center">
&nbsp;

[![WAVE_top](https://raw.githubusercontent.com/Aurora-Aeralis/Assets/refs/heads/main/General/WAVE_top-2.png)](https://github.com/Aeralis-Foundation/)
# PLL Library -> PEAK <small>[![Thunderstore Version](https://img.shields.io/thunderstore/v/AeralisFoundation/PLL_Library?style=for-the-badge&logo=thunderstore&label=&labelColor=0B1F2A)](https://thunderstore.io/c/peak/p/AeralisFoundation/PLL_Library/)[![Thunderstore Version](https://img.shields.io/thunderstore/v/AeralisFoundation/PLL_Library?style=for-the-badge&logo=unity&label=&labelColor=0B1F2A)](https://thunderstore.io/c/peak/p/AeralisFoundation/PLL_Library/)[![Thunderstore Version](https://img.shields.io/thunderstore/v/AeralisFoundation/PLL_Library?style=for-the-badge&logo=nuget&label=&labelColor=0B1F2A)](https://thunderstore.io/c/peak/p/AeralisFoundation/PLL_Library/)</small>

<img src="https://gcdn.thunderstore.io/live/repository/icons/AeralisFoundation-PLL_Library-1.6.0.png" alt="logo" align="right" width="256" height="256">

[![Discord](https://img.shields.io/badge/Discord-Community-5865F2?style=for-the-badge&logo=discord&labelColor=0B1F2A)]()

&nbsp;

PLL Library is a PEAK map layout mod and automatic custom biome library. It selects, reorders, repeats, and regenerates PEAK's live `WilIsland` segments. PLL also discovers custom content built with PLL Creator.

[Update](#update) &nbsp;&bull;&nbsp; [Features](#features) &nbsp;&bull;&nbsp; [Install](#install) &nbsp;&bull;&nbsp; [Configuration](#configuration) &nbsp;&bull;&nbsp; [Layouts](#layouts) &nbsp;&bull;&nbsp; [Custom Biomes](#custom-biomes) &nbsp;&bull;&nbsp; [Creator](#creator) &nbsp;&bull;&nbsp; [Build](#build-from-source) &nbsp;&bull;&nbsp; [Support](#support)

&nbsp;

---

## Update
<small>Current package information.</small>
<details open>
<summary><strong>Version 5.9.9</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

- I am just lazy...

&nbsp;

---
</details>

## Features
<small>Runtime behavior.</small>
<details>
<summary><strong>Feature list</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

- I am just lazy...

&nbsp;

---
</details>

---

&nbsp;

---

## Install
<small>Mod manager and manual installation.</small>
<details>
<summary><strong>Install instructions</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

<img src="https://raw.githubusercontent.com/Aurora-Aeralis/Assets/refs/heads/main/Notices/Testing/Mod-Manager-Recommended-Dark-1400.png" alt="Mod manager recommended" align="center">

> **Mod manager**

1. Install `PLL_Library` for every player who will join the lobby.
2. Ensure `BepInEx-BepInExPack_PEAK-5.4.75301`, `AeralisFoundation-Unified_Ray_Tracing-1.0.0` and `AeralisFoundation-PLL_Fog_Resources-1.0.0` are installed.
3. Start PEAK once to generate the configuration file. Every member needs compatible PLL protocols and matching content.

> **Manual**

1. Install BepInExPack PEAK.
2. Place `unifiedraytracing` at the root of the `AeralisFoundation-Unified_Ray_Tracing` mod directory. Gale mounts it at `PEAK/BepInEx/plugins/AeralisFoundation-Unified_Ray_Tracing/unifiedraytracing`.
3. Place `PLL_Library.dll` directly in `PEAK/BepInEx/plugins/` and the resource package's `NativeFog.bundle` at `PEAK/BepInEx/plugins/AeralisFoundation-PLL_Fog_Resources/NativeFog.bundle`. Do not place the resource inside the DLL or biome bundle.
4. Start PEAK once to generate the configuration file. Every online lobby member needs compatible PLL protocols and matching content.

> **Custom content**

Place custom `.pll` files built with PLL Creator directly in:

`PEAK/BepInEx/plugins/PLL_Biomes/`

Only files with the exact lowercase `.pll` extension are discovered.

&nbsp;

---
</details>

## Configuration
<small>Generated BepInEx configuration.</small>
<details>
<summary><strong>Configuration reference</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

Configuration file:
`BepInEx/config/AF/PLL_Library/config.cfg`

| Section / key | Default | Meaning |
|---|---:|---|
| `` | `` |  |
|

### Variation patterns

Each native biome has a selection and a `<Biome> Pattern` string under `[Variations]`. Each custom biome variation group has `Selected` and `Pattern` under its `[Variations.<group>]` section.

Select `Pattern` and enter listed variation names separated by `>`, for example `First variation > Second variation > First variation`. The sequence repeats across successive occurrences of that biome, independently of other biomes and generation order. An empty pattern uses `Random`; inactive patterns are ignored.

### Per-biome spatial controls

Native controls are grouped under `[Boundaries]` and `[Fog]` with biome named keys:

```ini
[Boundaries]
Beach Wall Boxes = 0,10,0,2,20,100,0,0,0;20,10,0,2,20,100,0,0,0
[Fog]
Beach Fog Cutout = 40,220,0.5
```

Each registered custom biome receives a separate file beneath:

`BepInEx/config/AF/PLL_Library/Custom Segments/`

Each file records the loaded bundle's `Version / Content Revision`. Its `Favorites`, `Overwrites`, `Before`, `After`, and `Spawn Chance` values begin with the bundle defaults and remain editable by the user. `Spawn Chance` is an inclusive `0` through `100` percentage used by Generative Map.

&nbsp;

---
</details>

---

&nbsp;

---

## Layouts
<small>Deterministic layout selection and resolution.</small>
<details>
<summary><strong>Layout behavior</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

- I am too lazy...

&nbsp;

---
</details>

<details>
<summary><strong>Peer plugin layout API</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

`PLLLibrary` exposes two startup registration methods.

| API | Contract |
|---|---|
| `PLLLibrary.DeployLayout(LayoutDefinition)` | Registers one named boarding pass layout. Registration does not select it or rewrite configuration. |
| `PLLLibrary.ModifyLayout(LayoutModification)` | Registers one deterministic modification for Default or one named layout. Every registered modification for the selected target applies automatically. |

| Input | Contract |
|---|---|
| `LayoutDefinition` | Requires a valid nondefault `LayoutId`, a trimmed unique display name, a content revision, and at least one public segment token. |
| `LayoutModification` | Requires a canonical lowercase qualified ID, a content revision, a valid target `LayoutId`, and at least one nonnull edit. The target may be `LayoutId.Default` or a registered named layout. |
| `LayoutEdit` | Uses `InsertBefore`, `InsertAfter`, or `Remove`. `Occurrence` is counted from zero among matching ordinary occurrences in the original target sequence. Insert operations require at least one segment token. |

```csharp
using BepInEx;
using PLL.Library;

namespace Example.PLL;

[BepInPlugin("example.pll.layout", "Example PLL Layout", "1.0.0")]
[BepInDependency(PLLLibrary.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class ExampleLayoutPlugin : BaseUnityPlugin
{
    void Awake()
    {
        var Layout = new LayoutDefinition(
            new LayoutId("example/extended-ascent"),
            "Extended Ascent",
            "1",
            new[]
            {
                "Beach", "Tropics", "Roots", "Alpine",
                "Mesa", "Volcano", "Citadel", "Nadir"
            });
        PLLLibrary.DeployLayout(Layout);
    }
}
```

&nbsp;

---
</details>

<details>
<summary><strong>ModifyLayout example</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

This example targets the named `Extended Ascent` layout registered above.

```csharp
using BepInEx;
using PLL.Library;

namespace Example.PLL;

[BepInPlugin("example.pll.modification", "Example PLL Modification", "1.0.0")]
[BepInDependency(PLLLibrary.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class ExampleLayoutModificationPlugin : BaseUnityPlugin
{
    void Awake()
    {
        var Modification = new LayoutModification(
            "example/move-mesa",
            "1",
            new LayoutId("example/extended-ascent"),
            new[]
            {
                LayoutEdit.InsertAfter("Roots", 0, new[] { "Mesa" }),
                LayoutEdit.Remove("Mesa", 0)
            });
        PLLLibrary.ModifyLayout(Modification);
    }
}
```

Both edits resolve against the original `Extended Ascent` sequence. `Remove("Mesa", 0)` removes the original Mesa, not the inserted Mesa. The result is `Beach > Tropics > Roots > Mesa > Alpine > Volcano > Citadel > Nadir`.

&nbsp;

---
</details>

## Custom Biomes
<small>Runtime bundle, authoring, and generation contract.</small>
<details>
<summary><strong>Authoring reference</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

- I am too lazy...

&nbsp;

---
</details>

## Creator
<small>Unity authoring and `.pll` packaging.</small>
<details>
<summary><strong>Creator package</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

- I am too lazy...

&nbsp;

---
</details>

---

&nbsp;

---
## Build from source
<small>Conventional Release artifacts.</small>
<details>
<summary><strong>Building</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

Copy `Config.Build.user.props.template` to `Config.Build.user.props`. Set `PEAKManagedDir` in the copied file to the installed `PEAK_Data\Managed` directory, then run:

```powershell
dotnet build .\PLL_Library.slnx -c Release
```

| Output | Conventional path |
|---|---|
| Runtime DLL | `artifacts/bin/PLL_Library/release/PLL_Library.dll` |
| NuGet package | `artifacts/nuget/PLL_Library.5.9.0.nupkg` |
| Thunderstore package | `artifacts/thunderstore/release/PLL_Library-5.9.0.zip` |
| Creator package | `artifacts/Editor/com.aeralis.pll.creator-5.9.0.tgz` |
| Fog resource package | `artifacts/resources/release/PLL_Fog_Resources-1.0.0.zip` |

&nbsp;

---
</details>

## Support
<small>Diagnostics and validation boundaries.</small>
<details>
<summary><strong>Support and troubleshooting</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

Include the following with a report:

- PLL Library, PEAK, and BepInExPack versions
- complete mod list
- current `BepInEx/LogOutput.log`, and PEAK `Player.log`
- `BepInEx/config/AF/PLL_Library/config.cfg`
- selected terminal layout and text seed
- custom bundle filename, stable IDs, and revisions when custom content is involved
- whether the failure occurs offline, as host, as client, during late join, or during quicksave continuation

| Symptom | Check |
|---|---|
| | |

&nbsp;

---
</details>

---

# Copyright © 2026 Aeralis Foundation
Licensed under the **[Aeralis Foundation's Restrictive License (AFRL)](https://github.com/Aeralis-Foundation/AFRL)**, Version 1.0.

Origin Project: [PLL_Library](https://github.com/Aeralis-Foundation/PLL_Library)  
Project Source: [PLL_Library](https://github.com/Aeralis-Foundation/PLL_Library)  

Permission Contact: aeralis.foundation@outlook.com

---

[![WAVE_bottom](https://raw.githubusercontent.com/Aurora-Aeralis/Assets/refs/heads/main/General/WAVE_bottom-2.png)](https://github.com/Aeralis-Foundation/)
