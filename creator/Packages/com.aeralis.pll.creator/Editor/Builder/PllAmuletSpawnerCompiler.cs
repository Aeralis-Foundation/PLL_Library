using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal static class PllAmuletSpawnerCompiler
    {
        internal static void Translate(GameObject SourceRoot, GameObject StagedRoot,
            IReadOnlyList<PllAmuletSpawnerAuthoring> Sources, PllBiomeBehavior Behavior)
        {
            foreach (var Source in Sources)
            {
                var Target = PllBuildArtifacts.MapComponent(SourceRoot, StagedRoot, Source, "amulet spawner");
                var Sampler = new GameObject("__PLL_AmuletGenerator");
                Sampler.transform.SetParent(Target.transform, false);
                var Volume = Source.Volume;
                var Height = Volume.Shape == PllZoneShape.Box ? Volume.Size.y : Volume.Radius * 2f;
                Sampler.transform.localPosition = Volume.Center + Vector3.up * (Height * 0.5f);
                Sampler.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var Generator = Sampler.AddComponent<Peak.PropSpawner_AmuletStatues>();
                Generator.statueIndex = PllAmuletSpawnerAuthoring.ResolveCategory(Source.Category, Behavior);
                Generator.area = Volume.Shape == PllZoneShape.Box
                    ? new Vector2(Volume.Size.x, Volume.Size.z) : Vector2.one * (Volume.Radius * 2f);
                Generator.rayNearCutoff = 0f; Generator.rayLength = Height;
                Generator.rayDirectionOffset = Vector3.zero; Generator.raycastPosition = true;
                Generator.nrOfSpawns = 1; Generator.randomSpawns = false;
                Generator.minSpawnCount = 0; Generator.currentSpawns = 0;
                Generator.chanceToUseSpawner = 1f;
                Generator.props = Array.Empty<GameObject>();
                Generator.overrideProps = false; Generator.overridePropsList = null;
                Generator.syncTransforms = true; Generator.layerType = HelperFunctions.LayerType.TerrainMap;
                Generator.modifiers = new List<PropSpawnerMod>
                {
                    new PSM_SetUpRotationToNormal(), new PSM_NormalOffset()
                };
                Generator.constraints = new List<PropSpawnerConstraint>
                {
                    new PSC_Normal(), new PSC_RequiredMaterial(), new PllAmuletVolumeConstraint(Target)
                };
                Generator.postConstraints = new List<PropSpawnerConstraintPost>
                {
                    new PSCP_Custom(), new PSCP_LateConstraintCheck { Constraint = new PSC_Embedded() }
                };
                Generator.postSpawnBehaviors = new List<PostSpawnBehavior>
                {
                    new PSB_FailGenerationIfSpawnFailed { minimumSpawnsToPass = 1 }
                };
                Target.gameObject.AddComponent<Biome>();
            }
        }
    }
}
