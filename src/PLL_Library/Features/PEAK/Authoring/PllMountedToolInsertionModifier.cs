using System;
using UnityEngine;

namespace PLL.Library;

[Serializable]
public sealed class PllMountedToolInsertionModifier : PSM_PitonNormal
{
    public override void ModifyObject(GameObject Spawned, PropSpawner.SpawnData Placement)
    {
        var Source = Spawned.GetComponent<PllRestPointAuthoring>();
        if (!Source || Source.Placement != PllRestPointPlacement.GeneratedTerrain) return;
        if (Source.Profile != PllRestPointProfile.NativeFragileMountedTool)
            throw new InvalidOperationException("Terrain insertion needs a mounted tool on the selected candidate root.");
        var Normal = Placement.hit.normal;
        if (!PllAuthoringValueContract.CanEncode(Normal.x) || !PllAuthoringValueContract.CanEncode(Normal.y)
            || !PllAuthoringValueContract.CanEncode(Normal.z) || Normal.sqrMagnitude == 0f)
            throw new InvalidOperationException("Mounted tool placement has no finite nonzero terrain normal.");
#if !PLL_CREATOR_SDK
        NativePropSpawnerGeneration.RequireMountedToolHit(Placement.spawnerTransform, Placement.hit);
#endif
        base.ModifyObject(Spawned, Placement);
        Spawned.transform.position += Normal * Source.InsertionOffset;
    }
}
