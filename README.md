# PLL Library

PLL Library is a PEAK mod for map layouts and custom biomes. It rebuilds PEAK's island (`WilIsland`) from a list of
segments that you choose. It can reorder, repeat and remove PEAK's own biomes, and it can insert custom biomes made
with **PLL Creator**, a Unity package that ships with this repository.

This guide covers the whole workflow:

1. [How the pieces fit together](#how-the-pieces-fit-together)
2. [Installing PLL to play](#installing-pll-to-play)
3. [Where everything lives in PEAK's folder](#where-everything-lives-in-peaks-folder)
4. [Choosing the map: layouts and configuration](#choosing-the-map-layouts-and-configuration)
5. [Setting up PLL Creator in Unity](#setting-up-pll-creator-in-unity)
6. [Making your first biome](#making-your-first-biome)
7. [Building the biome: geometry, campfire, fog and markers](#building-the-biome-geometry-campfire-fog-and-markers)
8. [Adding content: props, actors, hazards and effects](#adding-content-props-actors-hazards-and-effects)
9. [Building, installing and testing a .pll](#building-installing-and-testing-a-pll)
10. [Playing with friends](#playing-with-friends)
11. [For plugin developers: the layout API](#for-plugin-developers-the-layout-api)
12. [Building PLL from source](#building-pll-from-source)
13. [Troubleshooting](#troubleshooting)

This guide is written for **PLL Library 6.1.0**. Features new in 6.1.0 are marked _(6.1.0)_.

---

## How the pieces fit together

| Piece | What it is | Who needs it |
| --- | --- | --- |
| `PLL_Library.dll` | The BepInEx plugin that builds the map in game | Every player |
| PLL Fog Resources (`NativeFog.bundle`) | PEAK's fog shader, packaged for PLL | Every player |
| Unified Ray Tracing (`unifiedraytracing`) | A runtime dependency of PLL | Every player |
| `.pll` biome files | Custom biomes, built with PLL Creator | Every player, if the map uses them |
| PLL Creator (`com.aeralis.pll.creator-<version>.tgz`) | Unity editor package for making biomes | Biome authors only |

The flow is: **make a biome in Unity → build it to a `.pll` file → drop it in `BepInEx/plugins/PLL_Biomes/` → put its
ID in the layout → play.**

---

## Installing PLL to play

> **PEAK must run on DirectX 11.** PLL's fog only works on Direct3D 11. On Vulkan or D3D12 the fog zones are skipped
> (with one warning) and the map still loads.

### With a mod manager (recommended)

1. Install `PLL_Library` for **every** player who will join the lobby.
2. Make sure these dependencies are installed too:
   - `BepInEx-BepInExPack_PEAK-5.4.75301`
   - `AeralisFoundation-Unified_Ray_Tracing-1.0.0`
   - `AeralisFoundation-PLL_Fog_Resources-1.0.0`
3. Start PEAK once so PLL writes its configuration file, then close it.

### Manually

1. Install BepInExPack for PEAK.
2. Put `unifiedraytracing` in its own folder:
   `PEAK/BepInEx/plugins/AeralisFoundation-Unified_Ray_Tracing/unifiedraytracing`
3. Put `PLL_Library.dll` directly in `PEAK/BepInEx/plugins/`.
4. Put `NativeFog.bundle` exactly here (PLL does not search anywhere else):
   `PEAK/BepInEx/plugins/AeralisFoundation-PLL_Fog_Resources/NativeFog.bundle`
5. Start PEAK once so PLL writes its configuration file.

### Adding custom biomes

Put each `.pll` file (and, if you have it, its `.pll.report.json`) in:

```text
PEAK/BepInEx/plugins/PLL_Biomes/
```

Only files ending in exactly lowercase `.pll` are loaded. Then add the biome's ID to your layout
(see [Choosing the map](#choosing-the-map-layouts-and-configuration)).

---

## Where everything lives in PEAK's folder

```text
PEAK/
├── PEAK_Data/Managed/                      ← PEAK's assemblies (PLL Creator and source builds read these)
└── BepInEx/
    ├── LogOutput.log                       ← BepInEx log: PLL's messages, bundle results, errors
    ├── plugins/
    │   ├── PLL_Library.dll
    │   ├── AeralisFoundation-PLL_Fog_Resources/
    │   │   └── NativeFog.bundle
    │   ├── AeralisFoundation-Unified_Ray_Tracing/
    │   │   └── unifiedraytracing
    │   ├── PLL_Biomes/                     ← custom biomes
    │   │   ├── my-biome.pll
    │   │   └── my-biome.pll.report.json    ← build report written by PLL Creator
    │   └── PLL_ContentTools/               ← created only when PLL Creator sends capture or preview requests
    └── config/
        └── AF/
            └── PLL_Library/
                ├── config.cfg              ← layout, map and performance settings
                └── Custom Segments/
                    └── <namespace>-<name>-<hash>.cfg   ← one file per loaded custom biome
```

With a mod manager (Gale, r2modman and so on) the same layout sits inside the manager's **profile** folder instead of
PEAK's install folder. When PLL Creator asks for a "game profile", choose the folder that contains `BepInEx`.

---

## Choosing the map: layouts and configuration

You can change everything below in game: open the pause menu or **Settings**, then the **PLL** tab. It has three tabs:

- **Map:** the route, available biomes, seed and map options.
- **PLL Configuration:** the other settings, with descriptions.
- **FAQ & Issues:** versions, loaded bundles, warnings and errors.

You can also edit `BepInEx/config/AF/PLL_Library/config.cfg` by hand. Do that with PEAK closed, or use **Reload** in
the PLL page afterwards.

### The route (`[Map]`)

| Setting | Default | What it does |
| --- | --- | --- |
| `LayoutOrder` | `Beach > Tropics > Roots > Alpine > Mesa > Volcano > Citadel > Nadir` | The segments in order, separated by `>`. Use PEAK's biome names or a custom biome's ID. Volcano and Citadel include their lower zones. |
| `DoOrder` | `true` | Use `LayoutOrder`. |
| `Generative Map` | `false` | Let PLL choose an order from the loaded biomes using the map seed. Turn `DoOrder` off to use it. |
| `InfiniteMap` | `false` | Repeat segments (settings under `[Fields]`). |
| `Seed` | empty | Empty = a random seed each run. Type any text to reuse the same map. |
| `Replace Generated` | `true` | Rebuilds PEAK's already-generated props using the selected seed. |
| `Light Map Minimum` | `25` | Maximum light map size per segment, in MiB (this player only). |

Example: put a custom biome with the ID `mymaps/catacombs` between Tropics and Volcano:

```ini
LayoutOrder = Beach > Tropics > mymaps/catacombs > Volcano > Citadel > Nadir
DoOrder = true
```

### Repeating segments (`[Fields]`, used when `InfiniteMap = true`)

| Setting | What it does |
| --- | --- |
| `InfiniteMap: Repeatable` | Segments that can repeat, separated by `>` (up to 255). |
| `InfiniteMap: Chances` | A weight for each repeatable entry, in the same order (0 excludes it). |
| `InfiniteMap: Scale` | How many segments in total (1–255). PEAK supports at most 254 ordinary zones. |
| `InfiniteMap: Begin Index` | Where to insert them, counting from 0. `-1` = at the end, before a final Nadir. |
| `InfiniteMap: Remove Old` | Use only the repeatable segments and drop the default layout. |

### Other sections

| Section | Highlights |
| --- | --- |
| `[Behaviors]` | `Winnable`, `Peak at end`, `Gemless Nadir` (Nadir without Scout's Honor), and when Beach water and Nadir's Void Water turn on. |
| `[Prerequisites]` | `Relocate Plane` and `Relocate Start` move the crashed plane and the four spawn points to the first biome. `Valid Nadir` requires Scout's Honor only when the earlier segments can provide all four amulets. |
| `[Optimizations]` | `Lazy Generation` (props are generated when you get close), `Remove Old` (unloads zones two behind you), `No Sky Props`, and switches that turn off fog, water, rising ghosts, sleep fog or the plane. |
| `[Variations]` | For each PEAK biome: `Random` or `Pattern`. A pattern is a list of variation names separated by `>` that repeats for each copy of that biome. |
| `[Boundaries]` | `<Biome> Wall Boxes`: invisible walls in segment coordinates. Each box is `cx,cy,cz,sx,sy,sz,rx,ry,rz`, and boxes are separated by `;`. |
| `[Fog]` | `<Biome> Fog Cutout`: `min radius,max radius,amount` to thin the visible fog. |
| `[PLL]` | `Get Note` (developer note on the main menu), `Advertise Layout`, `Display Progress` (loading progress bar). |

### Per-biome files (`Custom Segments/*.cfg`)

Each custom biome that loads gets its own file, so players can tune it without rebuilding it:

| Setting | What it does |
| --- | --- |
| `ID` | The biome's stable ID, used in `LayoutOrder`. Don't change it. |
| `Name` | The biome's display name from the bundle. |
| `Favorites`, `Overwrites`, `Before`, `After` | Preferences for Generative Map: neighbours it likes, biomes it may replace, and biomes it should sit right before or after. Separate IDs with `>`. |
| `Spawn Chance` | Generative Map only: a 0–100 chance of being included. |
| `Wall Boxes`, `Fog Cutout` | The same as the native `[Boundaries]` and `[Fog]` settings, for this biome. |
| `Content Revision` | Which version of the biome's content is loaded. |

These start with the values the author built into the bundle.

---

## Setting up PLL Creator in Unity

### Requirements

- **Unity 6000.3.15f1** with **Windows Build Support** (other 6000.3.x versions are accepted, but custom fog shaders
  need exactly 6000.3.15f1).
- A **Universal Render Pipeline (URP)** project. PEAK uses URP 17.3.0.
- An installed copy of PEAK and the **same PLL Library version** as the Creator package.
- Windows, if you want PLL Creator to send capture and preview requests to PEAK.

### Install

1. Create a new Unity 6000.3 URP project for your biomes. Keep it separate from other projects.
2. Open **Window > Package Manager**, click **+**, choose **Install package from tarball…** and pick
   `com.aeralis.pll.creator-<version>.tgz`.
3. Wait for Unity to finish importing and compiling.
4. Run **PLL > Setup > Configure** and select your PEAK folder (the one that contains `PEAK_Data` and
   `UnityPlayer.dll`). Configure then:
   - switches the project's build target to **Windows 64-bit** (Unity recompiles and setup carries on by itself);
   - generates the PLL SDK and PEAK references in `Assets/PLL.Creator.Generated`;
   - names project layers **20 = Terrain**, **21 = Map** and **22 = InvisWall**. If one of those layers already has
     a different name, Configure stops and tells you which one; rename or free it, then run it again.
5. Check **PLL > Setup > Status**. Everything should be ready.

### Updating PLL Creator

1. Back up the project and close PEAK.
2. In Package Manager, remove the old PLL Creator package and install the new `.tgz`.
3. Wait for importing to finish, then run **PLL > Setup > Configure** again and check **PLL > Setup > Status**.

Removing the package doesn't remove `Assets/PLL.Creator.Generated`; Configure replaces it and keeps its script IDs.
Don't delete your biome assets or the project's `Library` folder to update. If a generated script reports a missing
type, restart Unity and run Configure again.

---

## Making your first biome

1. Run **PLL > Biomes > Create Biome**.
2. Fill in the window:

   | Field | Meaning |
   | --- | --- |
   | Output Folder | A folder under `Assets/`. PLL makes a subfolder for the biome in it. |
   | Starter Course | **Flat** (a 64 × 100 m ground) or **Climbing** (a ground, two rest ledges and a plateau). |
   | Biome ID | Lowercase `namespace/name`, for example `mymaps/catacombs`. This is the name used in `LayoutOrder`, so keep it stable. |
   | Display Title | The name shown in game when players arrive. |
   | Entry / Exit Connector | Leave these at the default (`af.pll.library/connector/layout/v1`) so the biome can join PEAK's segments. |
   | Profile To Duplicate | Optional. A PEAK `DayNightProfile` to copy for the sky and lighting. |

3. Click **Create Biome**. PLL creates these assets in the new folder:
   - `<name>.asset`: the **biome definition**. Its ID, title, sounds and generative-map defaults are set here.
   - `<name>.prefab`: the **biome prefab**. Everything players walk on goes in here.
   - `<name>DayNightProfile.asset`: the biome's sky and lighting.
   - A material and meshes for the starter course.
4. Open the prefab and replace the starter course with your own geometry.

**Lighting:** PLL does not rebake lighting. Use **Realtime** lights, light probes or unlit shaders. Baked and Mixed
lights, saved lightmaps, and "Contribute GI" with lightmaps are not supported.

Other biome menu items:

- **PLL > Biomes > Create Day Night Profile:** a new profile, or a copy of the selected one.
- **PLL > Biomes > Validate Selected Biome:** runs the build checks without building.
- **PLL > Biomes > Add Biome Group to Selected Biome:** joins several biomes into one map entry, like PEAK's Volcano
  (which includes Caldera). The "prefix" biomes are generated first, then the owner. Only the owner appears in layouts.
  Prefixes must have Spawn Chance 100 and no layout relationships.

---

## Building the biome: geometry, campfire, fog and markers

### The prefab's structure

```text
<YourBiome>                      CustomSegmentRuntime, PllBiomePrefabAnchors, PllBiomeRoot
├── FloorRoot                    ← all walkable and climbable geometry, props and zones
├── CampfireRoot                 ← the campfire at the end of the biome (and fixed luggage)
│   └── CampfireModel            ← campfire model with PLL's campfire authoring
├── RuntimeMarkers
│   ├── Entry                    ← SegmentPort (Entry): where the previous segment connects
│   ├── Exit                     ← SegmentPort (Exit): where the next segment connects
│   ├── Start                    ← SegmentPort (Start): where players spawn if this biome is first
│   ├── PlayerFormationRoot      ← where the scouts line up at the start
│   ├── PlaneAnchor              ← where the crashed plane goes if this biome is first
│   ├── FogBeginAnchor           ← where the fog starts chasing players
│   ├── FogCutoffAnchor          ← where the fog stops (near the campfire)
│   ├── FogOrigin                ← FogSphereOrigin (size 120)
│   │   ├── Fogwall_Prev
│   │   └── Fogwall_Next
│   ├── FogCutout                ← FogCutoutZone (visible fog thinning)
│   └── ProgressPoint            ← the arrival point that shows the Display Title
└── Presentation                 ← effects, lights and sounds that aren't walkable
```

Keep these names and components. The build checks them.

### Geometry and climbing

- Walkable and climbable surfaces need a **MeshCollider** and must be on layer **20 (Terrain)** or **21 (Map)**. The
  surface inspectors have **Use Terrain Layer (20)** and **Use Map Layer (21)** buttons.
- **PLL > Geometry > Make Selected Geometry Climbable** sets up the selected meshes the way PEAK expects.
- **PLL > Geometry > Add Boundary Box** adds invisible walls (InvisWall layer) to keep players on the course.
- **PLL > Geometry > Edit Route and Rest Ledges** moves, rotates and scales the ports, creates rest ledges, snaps a
  port to the geometry, and matches this Entry's rotation to the previous Exit. **Entry and Exit must use matching
  connector IDs and face the same direction.**
- **PLL > Climbing > Add Rest Point or Hanging Rope to Selected Source** and **Apply Custom Rope Appearance** add
  PEAK's climbing fixtures.

### Where the biome meets its neighbours _(6.1.0)_

PLL joins your biome to the segments before and after it **floor to floor**: PEAK's campfire ledge meets the floor
directly under your **Entry** marker, and the floor under your **Exit** marker meets the next segment's ledge. So:

- Put **Entry** above the floor where players arrive, and **Exit** above the floor where they leave.
- The markers' height doesn't matter (up to 40 m above the floor). Only their position over the floor does.
- The log shows each adjustment, for example `Placement: '<biome>' lined up with '<previous>' at floor level`.

### The campfire

The campfire under `CampfireRoot` ends the biome. Lighting it moves the run on to the next segment, exactly as in
PEAK. From 6.1.0, a Creator campfire whose particles have no material takes the look of PEAK's own campfire in game,
so it no longer shows magenta squares.

### Looks and sound

- **PLL > Visuals > Browse PEAK Materials and Shaders:** use PEAK's own materials. They're previewed in the editor and
  swapped for the real ones in game, so none of PEAK's assets go into your file.
- **PLL > Visuals > Create Layered Surface Material** and **Add Surface Textures to Selected Object:** layered
  terrain-style materials.
- **PLL > Visuals > Apply Environment from Existing Biome** and **Compose Environment Preset:** copy or build sky,
  fog and atmosphere settings.
- **PLL > Visuals > Add Custom Visual to Selected Particles:** a custom visual for a particle system.
- **PLL > Effects > Create Fog Zone in Selected Biome:** a local fog volume.
- **PLL > Audio > Add Biome Stinger to Selected Audio Source:** the sting that plays when players enter.
- In the biome definition: `Ambience Sounds`, `Effect Sounds`, `Enter Sounds` and the biome `Color`.

---

## Adding content: props, actors, hazards and effects

Content goes under `FloorRoot` (or `CampfireRoot` for campfire luggage). Most tools work on the **selected** object,
so select the parent first.

### Props and spawning (PLL > Spawning, PLL > Props)

| Menu item | Use it for |
| --- | --- |
| Create Prop Spawner under Selected Source | Scatter prefabs over the geometry using PEAK's prop spawner. |
| Add Child Props to Selected Source | Spawn each immediate child as a prop. |
| Add Prop Zone to Selected Source | Limit a spawner to a box or sphere. |
| Add Force Spawner to Selected Source | Always place one specific item or prop. |
| Add Critical Generation to Selected Biome | Run a spawner before the ordinary props. |
| Add Final Generation to Selected Source | Run a spawner in PLL's final generation pass, after the ordinary props. Force spawners can be placed inside it. |
| Add Hooked Generation to Selected Source | Generate a spawner later, triggered by an Effect Zone when players enter or leave. |
| Create Luggage in Selected Biome | Suitcases and chests, with PEAK's loot or your own items. |
| Create Amulet Spawner in Selected Biome | An amulet slot in the run's amulet rotation (for Nadir). |
| Add Statue to Selected Model Wrapper | A scout statue (revives) on your own model. |

### Creatures and hazards

| Menu item | Use it for |
| --- | --- |
| Create Native Actor Zone | Place PEAK's own creatures and world actors in a box or sphere. From 6.1.0 the **Vanilla Actor** profile offers 20 of PEAK's spawners: scorpions, antlions, cacti, cactus balls, dynamite, oasis, spiders, mushrooms, beehives, tick plants, berry vines, slippery jellyfish, hot spring, venus fly traps, ghost-fire belltowers, lily-pad frogs and cattails. Each copies PEAK's own spawner when the map loads. The window shows where each one can land (slope, clearance, spacing, which way to aim the zone). |
| Create Entity / Make Entity Zone / Apply Entity Appearance | Zombies, beetles and condors, with PEAK's or your own look. |
| Create Plugin Entity Zone | A fixed population from another plugin's entity factory (that plugin must be installed). |
| Create Hazard | Contact injury and other hazard behaviour on your own objects. _(6.1.0)_ **Caldera Eruption**: PEAK's lava geysers rising from the bottom of a box while a player is over it. |

### Effects (PLL > Effects)

| Menu item | Use it for |
| --- | --- |
| Add Effect to Selected Presentation | Particles, or PEAK's native fire or smoke. |
| Add Effect Zone to Selected Source | A trigger volume that turns effects on or off, and can trigger hooked generation. |
| Add Generation Trigger Zone to Selected Source | Start generation when players reach an area. |
| Add Native Resolver to Selected Source | Use one of PEAK's own sources (for example a spore cloud) with PEAK's mesh, material and collision. |
| Apply Effect Appearance | Restyle an effect. |

### Variations (PLL > Variations > Create Variation Group)

A variation group holds several alternative versions of part of the biome, and one is picked per run. Players can pin
or pattern them in their config (`[Variations.<group>]`: `Selected` and `Pattern`).

### PEAK's own content: Native Content browser

Open **PLL > Native Content > Browser and PEAK Preview** to browse PEAK's items, props and actors and add them to
spawners. To get the catalog:

1. **Select and Inspect Game Profile:** the folder with `BepInEx`, then the installed `PLL_Library.dll`.
2. **Select Installed Bundle:** your built `.pll` in that profile's `PLL_Biomes`.
3. **Queue Native Catalog Export**, then launch PEAK with that profile and start a **new** PLL run (not Continue).
4. **Read Request Result**, then **Import Native Catalog**.

After importing, **Queue Exact Snapshot PEAK Preview** loads your biome in PEAK with a chosen seed so you can look at
it. Requests go through `BepInEx/plugins/PLL_ContentTools/` and expire after the queue lifetime.

---

## Building, installing and testing a .pll

1. **Save the prefab.** Building refuses unsaved prefab changes.
2. Select the **biome definition** (or a `PllBiomeRegistry` to build several biomes into one file, or any object in
   the open biome prefab).
3. Run **PLL > Build > Build .pll** and choose a file name **outside** the Unity project. The name must end in
   lowercase `.pll`.
4. The result window shows the bundle, its `.pll.report.json`, the SHA-256, and the Unity, PEAK and PLL versions it
   was built against. "Gameplay is not tested" just means you still need to play it.
5. Install it from the same window:
   - **Select and Inspect Game Profile**: the folder with `BepInEx`, then the `PLL_Library.dll` in it.
   - Close PEAK, then **Install Current Bundle and Report**. This copies both files into `BepInEx/plugins/PLL_Biomes/`.
6. Add the biome ID to `LayoutOrder` (or turn on Generative Map), start PEAK and start a run.
7. Back in Unity, **Read Recorded Runtime Result** reads PLL's verdict for that exact bundle from `LogOutput.log`
   (`accepted` or a rejection). You can also search the log for `PLL bundle-result/v1 … outcome=accepted`.

Since 6.1.0, each `.pll` gets a bundle name based on the biomes inside it, so several biome files can be installed
together. Files built with older Creators all share one name and only one of them loads, so rebuild them.

---

## Playing with friends

- **Everyone** needs the same PLL Library version, the same fog resources, and the **same `.pll` files** in
  `PLL_Biomes`.
- The host advertises PLL in the Steam lobby (`PllAdmission`). Players without a compatible PLL are told
  "The host has no compatible PLL Library version" and can't join.
- The **host's** layout, seed and variations are used for everyone. Clients don't need matching layout settings.
- Per-player settings (`Light Map Minimum`, `Display Progress`) only affect that player.

---

## For plugin developers: the layout API

Other BepInEx plugins can register layouts at startup. Add a hard dependency on PLL:

| API | What it does |
| --- | --- |
| `PLLLibrary.DeployLayout(LayoutDefinition)` | Registers a named layout. It doesn't select it or change the config. |
| `PLLLibrary.ModifyLayout(LayoutModification)` | Registers an edit to the default layout or a named layout. Every edit for the selected target is applied. |

| Type | Rules |
| --- | --- |
| `LayoutDefinition` | A valid non-default `LayoutId`, a unique display name, a content revision and at least one segment token. |
| `LayoutModification` | A lowercase qualified ID, a content revision, a target `LayoutId` (`LayoutId.Default` or a registered layout) and at least one edit. |
| `LayoutEdit` | `InsertBefore`, `InsertAfter` or `Remove`. `Occurrence` counts from 0 among matching segments in the **original** target sequence. Inserts need at least one segment token. |

```csharp
using BepInEx;
using PLL.Library;

[BepInPlugin("example.pll.layout", "Example PLL Layout", "1.0.0")]
[BepInDependency(PLLLibrary.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class ExampleLayoutPlugin : BaseUnityPlugin
{
    void Awake()
    {
        PLLLibrary.DeployLayout(new LayoutDefinition(
            new LayoutId("example/extended-ascent"),
            "Extended Ascent",
            "1",
            new[] { "Beach", "Tropics", "Roots", "Alpine", "Mesa", "Volcano", "Citadel", "Nadir" }));

        // Move Mesa to straight after Roots. Both edits resolve against the original sequence,
        // so Remove("Mesa", 0) removes the original Mesa, not the inserted one.
        PLLLibrary.ModifyLayout(new LayoutModification(
            "example/move-mesa",
            "1",
            new LayoutId("example/extended-ascent"),
            new[]
            {
                LayoutEdit.InsertAfter("Roots", 0, new[] { "Mesa" }),
                LayoutEdit.Remove("Mesa", 0)
            }));
        // Result: Beach > Tropics > Roots > Mesa > Alpine > Volcano > Citadel > Nadir
    }
}
```

---

## Building PLL from source

1. Copy `Config.Build.user.props.template` to `Config.Build.user.props` and set `PEAKManagedDir` to your
   `PEAK/PEAK_Data/Managed` folder.
2. Build:

   ```powershell
   dotnet build .\PLL_Library.slnx -c Release
   ```

| Output | Path |
| --- | --- |
| Runtime DLL | `artifacts/bin/PLL_Library/release/PLL_Library.dll` |
| NuGet package | `artifacts/nuget/PLL_Library.<version>.nupkg` |
| Thunderstore package | `artifacts/thunderstore/release/PLL_Library-<version>.zip` |
| PLL Creator package | `artifacts/Editor/com.aeralis.pll.creator-<version>.tgz` |
| Fog resource package | `artifacts/resources/release/PLL_Fog_Resources-1.0.0.zip` |

Repository layout:

| Folder | Contents |
| --- | --- |
| `src/PLL_Library/` | The runtime plugin. `Core/` (plugin and public API), `Features/` (PEAK integration, authoring components, networking), `Modules/` (map building, biome registration, spawning, fog, the in-game settings page). |
| `creator/Packages/com.aeralis.pll.creator/` | The Unity editor package: `Editor/Setup` (Configure and Status), `Editor/Builder` (menus, validators, bundle builder). |
| `creator/Sdk/` | Builds the SDK that Configure puts into a Unity project. |
| `resources/` | The fog resource package. |
| `Public Logs/` | The Thunderstore README and the changelog. |

---

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No fog, or a fog warning in the log | PEAK must run on DirectX 11, and `NativeFog.bundle` must be at the exact path above. |
| A custom biome doesn't appear | The file must end in lowercase `.pll` and be directly in `PLL_Biomes`. Its ID must be in `LayoutOrder`, or Generative Map must be on. Search `LogOutput.log` for `bundle-result`. |
| "Unity could not load the AssetBundle" with two biome files | They were built under the same bundle name by an older Creator. Rebuild them with 6.1.0. |
| A friend can't join ("no compatible PLL Library version") | Everyone needs the same PLL version. |
| Different maps for different players, or desyncs | Everyone needs the same `.pll` files. |
| The biome starts too high or low where it meets PEAK's segments | Since 6.1.0 only the Entry and Exit markers' position over the floor matters. Put them over the floor where players arrive and leave. |
| Magenta squares on a Creator campfire | Fixed in 6.1.0 (the campfire copies PEAK's look). |
| Configure fails about project layers | Layers 20, 21 and 22 must be free or already named Terrain, Map and InvisWall. |
| "Unity … is not supported" | Use Unity 6000.3.x (6000.3.15f1 for custom fog shaders). |
| Lighting looks wrong in game | Use Realtime lights, light probes or unlit shaders. Baked lighting isn't supported. |
| A generated script reports a missing type | Restart Unity and run **PLL > Setup > Configure** again. |

When reporting a problem, include:

- the PLL, PEAK and BepInExPack versions, and your full mod list;
- `BepInEx/LogOutput.log` and PEAK's `Player.log`;
- `BepInEx/config/AF/PLL_Library/config.cfg`;
- the layout and text seed;
- for custom content, the `.pll` file name, biome IDs and revisions;
- whether it happens offline, as host, as client, on a late join, or after Continue.

---

# Copyright © 2026 Aeralis Foundation
Licensed under the **[Aeralis Foundation's Restrictive License (AFRL)](https://github.com/Aeralis-Foundation/AFRL)**, Version 1.0.

Origin Project: [PLL_Library](https://github.com/Aeralis-Foundation/PLL_Library)
Project Source: [PLL_Library](https://github.com/Aeralis-Foundation/PLL_Library)

Permission Contact: aeralis.foundation@outlook.com
