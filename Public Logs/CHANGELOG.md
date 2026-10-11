# Changelog

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [5.9.4]
<details open>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- Initial FakeItem synchronization identifies scene roots by their unique names, not their positions in Unity's scene root array.

</details>

---

## [5.9.3]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- Native actor sealing accepts compatible Photon prefab pools, including PEAKLib's wrapper, without modifying their resource caches.
- Room settings retain the startup biome schema when a bundle fails live compatibility sealing.
- Variation settings own their IDs, titles and default weights without retaining authoring objects from rejected bundles.
- Food and Content Spawners resolve names within their selected item or prop kind.
- Native prop lists can reference the same prefab as ItemDatabase.
- Effect zones, effects, visuals and sound emitters can bind while their deferred biome roots are inactive.
- Particle fingerprints exclude ParticleSystemRenderer's runtime bounds, which Unity does not preserve when cloning.

</details>

---

## [5.9.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- Startup patches bind concrete material and bridge overrides instead of abstract or broader base methods.
- Creator adds shared placement constraints after constructing every translated spawner, including native Content Spawner children.
- Creator and runtime share amulet selection limits, so a biome cannot build with multiple simultaneously selected sources.
- Native texture mappings use primitive bundle fields so texture only references survive loading in PEAK.
- Food and Content Spawner weighted lists use primitive bundle fields for the same Editor to player serialization boundary.

</details>

---

## [5.9.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- Photon transport stays active while loading through room codes, Steam lobbies and invites, including clients still joining with an assigned room.
- Room code clients can confirm their Steam lobby membership before starting the island.
- Ordinary hooked props under a custom biome root are no longer rejected as managed fixtures.
- Network and image cleanup releases owned resources when an earlier operation fails. 
- Invalid collision vertices and rope skin directions are rejected before native construction.
- Creator detects invalid amulet settings and routes supported native authoring tools correctly.

### Changed

- Removed duplicate scans, sorting, unused state and manual fixed-target bindings where typed access is available. Open serialized-value and RPC discovery remain supported.
- Simplified configuration, setup, inspector and multiplayer messages without removing required usage information.

</details>

---

## [5.9.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Added

- Food Spawner attaches native regional pools or custom weighted item lists to authored and generated custom props.
- Content Spawner places weighted native items and prop families in bounded biome zones.
- Creator native content browser can append selectors directly to Food and Content Spawner lists.
- Native Material authoring resolves named PEAK materials and independent texture property mappings in game without bundling native shader or texture copies.
- Creator validates native material slot ownership and preserves reference settings through staged bundle serialization.

</details>

---

## [5.8.3]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- Creator accepts unset optional Shader references on Material only visuals while still rejecting missing serialized asset references.

</details>

---

## [5.8.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Added

- Optional biome-root `NativeModifiers` with independent Night Cold and Mesa Heatstroke settings.

### Fixed

- Night cold no longer depends on a fixed progress index.
- Custom Mesa heat preserves native shade and item protections without stacking native heat volumes.
- Shared cleanup failure capture replaces duplicate library implementations. 
- Creator package trust uses a bound delegate instead of repeated reflective invocation. 
- Creator diagnostics discover authoring components in one hierarchy pass. 
- Configuration parsing and environment/property lookups avoid redundant work.

</details>

---

## [5.8.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.8.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.7.8]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.7.7]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.7.6]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.7.5]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.7.4]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.7.3]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.7.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.7.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.7.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.6.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.6.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.6.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.5.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Added

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.4.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.4.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Added

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.3.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.3.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.2.4]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.2.3]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.2.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.2.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.2.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.1.4]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.1.3]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.1.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.1.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.1.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.0.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

### Removed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [5.0.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

</details>

---
## [4.1.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Removed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [4.0.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [4.0.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [3.7.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [3.6.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [3.5.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [3.4.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [3.3.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

### Removed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [3.2.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

### Removed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [3.0.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

### Removed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [3.0.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

### Removed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [2.0.8]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

### Removed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [2.0.7]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [2.0.6]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [2.0.5]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [2.0.4]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [2.0.3]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [2.0.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [2.0.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [2.0.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Removed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---
## [1.30.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.30.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.29.7]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.29.6]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.29.5]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.29.4]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.29.3]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.29.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.29.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.29.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.28.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to collapse</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.28.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;


### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.27.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;


### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.26.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.25.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.24.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.23.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.22.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;


### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.21.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.20.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.19.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.18.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.18.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.17.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.17.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.17.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.16.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.16.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.16.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.15.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.15.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.15.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.14.3]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.14.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.14.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.14.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.13.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.13.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.12.3]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.12.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.12.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.12.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.11.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.10.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.10.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.10.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.9.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.9.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.8.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.8.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.7.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

&nbsp;
</details>

---

## [1.7.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- https://knowyourmeme.com/memes/im-tired-boss

### Changed

- https://knowyourmeme.com/memes/im-tired-boss

### Fixed

- https://knowyourmeme.com/memes/im-tired-boss

### Removed

- Legacy `.pllbundle` ingress, public/manual content registration, and `PLLBundleManifest` support.

&nbsp;
</details>

---

## [1.6.2]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- Boarding-pass layout controls were updated.
- PLL Creator's Unity Editor integration was patched.
- Future-segment colliders are temporarily disabled during terrain processing and restored afterward.

&nbsp;
</details>

---

## [1.6.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- Creator setup now isolates Unity's stock `TutorialInfo` scripts into an Editor-only named assembly before importing PEAK's `Assembly-CSharp` provider.
- Remaining project `Assembly-CSharp` conflicts now identify the source scripts that require a named assembly definition.
- Creator setup now replaces its pending transaction record through a real backup path accepted by Unity's Windows Mono runtime.

&nbsp;
</details>

---

## [1.6.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- An installable hidden `PLL Creator` Unity Package Manager package with automatic local PEAK discovery and no generated files under `Assets`.
- `PLLFinalGeneration` for deferred final-phase roots containing the supported exact `PropSpawner` capability.
- `PLLForceSpawner` for exact-name native-prop families with occurrence-owned generation and cleanup.

### Changed

- Creator setup now installs from the released `.tgz`; `bootstrap.ps1` and the generated creator project were removed from the user workflow.
- The boarding-pass layout row uses native dropdown item visuals, an auto-hiding scrollbar, compact padding, and a left-hand Order checkbox.
- Native first-segment placement derives the four-player formation from authored structural Terrain/Map mesh support instead of generated prop output.

### Fixed

- Fog patches no longer publish stale origins while a new flight is still preparing its fog binding.
- Plane and player relocation no longer select generated or airborne prop colliders in Roots, Tropics, or other native starts.

### Unsupported

- Item-domain `PLLForceSpawner` names are rejected because PEAK detaches room-owned item output; native props containing another `ISpawner` are also rejected.

&nbsp;
</details>

---

## [1.5.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Changed

- Boarding-pass layout controls now occupy PEAK's native white ascent panel and use its custom-run selector styling.
- Creator bootstrap accepts installed Unity patch releases from PEAK's detected major.minor editor stream.

### Fixed

- Offline quicksave continuation transfers saved layout selection directly into the active flight snapshot instead of routing it through resettable terminal UI state.
- The layout dropdown and Order toggle no longer overlap or extend beneath the boarding-pass border.

&nbsp;
</details>

---

## [1.5.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- A native scrolling boarding-pass layout dropdown and adjacent per-flight Layout Order toggle.
- Deterministic Generative Map layouts over native and eligible custom biomes.
- Per-custom-biome `Favorites`, `Overwrites`, `Before`, `After`, and `Spawn Chance` configuration files.
- Exact-`BiomeId` custom configuration lookup APIs.
- A separate Unity creator SDK and `.pll` packaging project for the current pre-authored biome schema.

### Changed

- Layout mode now resolves in the order registered/order toggle, Generative Map, InfiniteMap, then PEAK's live base order.
- Cloned native segments run surviving active configuration steps under a per-occurrence seed before PEAK's generation steps.
- The host configuration, layout identity, quicksave identity, and generation epoch include Generative Map and effective custom-biome configuration.

### Fixed

- Future flights no longer inherit the preceding flight's Layout Order toggle override.
- Reused native generation no longer leaves nearby generated Photon views untracked or duplicates stale per-occurrence output.
- Creator publication restores the prior bundle and report together when either destination cannot be replaced.

&nbsp;
</details>

---

## [1.4.1]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Fixed

- The layout selector now uses PEAK's text-bearing custom-options style and occupies the boarding pass's native footer instead of failing on the icon-only run button.
- Configuration schema replacement is staged and atomically committed; an unchanged configuration is no longer rewritten during startup.

&nbsp;
</details>

---

## [1.4.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- Host-selectable registered layouts on PEAK's boarding pass.
- `DeployLayout` and deterministic `ModifyLayout` APIs.
- `Forceload` and `ForceloadElement` inactive world-root preparation APIs.
- `PllContentRegistry`, bundle-element definitions, and stable segment, element, and layout identities.
- Deterministic `Overwrites`, `Before`, `After`, and `Favorites` layout resolution.
- `Prerequisites / Guarantee Campfire Loot`.
- `Patches / Patch bridges`.

### Changed

- Layout edits and InfiniteMap expansion now resolve metadata once over the final logical sequence.
- The host synchronizes selected layout identity, layout-registry revision, campfire-loot behavior, and bridge-patch behavior.
- Legacy PLL quicksaves use their original layout-revision serialization; a legacy revision rejection leaves the save intact.
- Runtime compatibility checks bind to the capabilities used instead of an independent PEAK member inventory.
- The public README now documents installation, configuration, layout registration, inert content loading, and custom-biome contracts.

### Fixed

- Cloned Caldera containers no longer retain duplicate generated The Kiln output.
- Regenerated Beach bridges retain PEAK's non-damaging collision callback behavior.
- Required campfire roots regenerate when their player-scaled Campfire food output is incomplete.
- Custom segment IDs cannot collide with published native segment IDs.
- `Peak` cannot appear before later layout tokens.
- Unselected named layouts no longer abort the selected map because their live content is unavailable.

&nbsp;
</details>

---

## [1.3.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- Custom biome API *(Will be made automatic in next release)*.

### Changed

- The default `LayoutOrder`.
- `Peak` is documented and enforced as the native attachment that must directly follow `Volcano`.
- Custom segments.
- PLL quick saves.

---

&nbsp;
</details>

---

## [1.2.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;

### Added

- Lazy Features.
- Ambiance Correction.
- Sound Correction.
- Begin Index for Infinite Map.
- No Plane.

### Changed

- Prop Generation.
- Object Relocation.
- Spawn and Plane.

### Removed

- Lazy Baking.

---

&nbsp;
</details>

---

## [1.1.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;


### Added

- Remove Old Optimization.
- Winnable.
- Peak at end.
- Water follows Beach.
- Relocate Plane.
- Relocate Start.
- Lazy Generation.
- Fogless.
- No Water.

### Changed

- Prop Generation.
- Beach Segment.

---

&nbsp;
</details>

---

## [1.0.0]
<details>
<summary><strong>Version Details</strong> <small><em>&rarr; click to expand</em></small></summary>

---
&nbsp;


### Added

- Initial Aeralis Foundation: PEAKLevelLoader Library.

&nbsp;
</details>

---
