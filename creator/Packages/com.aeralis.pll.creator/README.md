# PLL Creator 5.9.9

## Requirements

- Unity 6000.3 with Windows Build Support.
- An installed copy of PEAK and matching PLL Library 5.9.9.

## Install and configure

- Add the Creator `.tgz` through Unity Package Manager's **Install package from tarball** command.
- Run **PLL > Setup > Configure** and select the PEAK installation directory.

## Update an existing project

1. Finish any pending setup, back up the project and close PEAK.
2. Remove the old PLL Creator package in Unity Package Manager, then install the new `.tgz`.
3. Wait for Unity to finish importing, then run **PLL > Setup > Configure** again.
4. Wait for setup to finish and check **PLL > Setup > Status**.

Package Manager removes the package, not the SDK and scripts generated under `Assets/PLL.Creator.Generated`. Configure replaces that generated folder together and preserves its script GUIDs now... Do not remove your biome assets or clear the whole project Library to update PLL Creator. If a generated script reports a missing type, then it is compiling against an older or missing SDK. Restart Unity and configure again.