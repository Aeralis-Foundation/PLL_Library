# PLL Fog Resources

This content only Thunderstore dependency contains PLL's sealed native fog shader bundle. 
* It contains no plugin DLL and has no dependency on PLL Library.

## Installation

PLL Library depends on `AeralisFoundation-PLL_Fog_Resources-1.0.0`.
Place the package root at:

```text
BepInEx/plugins/AeralisFoundation-PLL_Fog_Resources/NativeFog.bundle
```

Keep `NativeFog.bundle` at that exact path. PLL does not scan other directories or fall back to an embedded copy. Missing or changed resources reject fog compatibility before rendering begins.

NuGet installs the library API, not these game resources. A NuGet consumer must also install the Thunderstore resource package, or extract its `NativeFog.bundle` into the directory shown above.

## Compatibility

The bundle is built for these exact inputs:

- Unity `6000.3.15f1`
- PEAK 2.6a
- Universal Render Pipeline  `17.3.0`
- `StandaloneWindows64`