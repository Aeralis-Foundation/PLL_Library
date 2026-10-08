using System;
using System.Collections.Generic;
using System.Linq;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    sealed class ActorZoneSemanticIdentity
    {
        readonly PrefabBinding Binding;
        readonly string Id;
        readonly PllZoneVolume Volume;
        readonly Pose[] Poses;
        readonly Vector3[] Scales;
        readonly RecipeIdentity Recipe;

        internal ActorZoneSemanticIdentity(PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths, LevelGenStep Source,
            AssetIdentityCatalog Assets, string Field)
        {
            var Values = Fields(Source);
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, Field);
            Id = Values.Id; Volume = Values.Volume;
            Recipe = Values.Source switch
            {
                PllBeetleZoneSource Beetles => new BeetleIdentity(Beetles, Assets, Field),
                PllMushroomZombieZoneSource Zombies => new ZombieIdentity(Zombies),
                PllCondorZoneSource Condor => new CondorIdentity(Condor, Assets, Field),
                PllPluginEntityZoneSource Plugin => new PluginIdentity(Plugin, Assets, Field),
                PllAuthoredEntityZoneSource Entity => new AuthoredEntityIdentity(Graph, StagedPaths, Entity),
                PllAuthoredHazardZoneSource Hazard => new AuthoredHazardIdentity(Graph, StagedPaths, Hazard),
                PllTornadoZoneSource Tornado => new TornadoIdentity(Tornado),
                PllTumbleweedZoneSource Tumbleweed => new TumbleweedIdentity(Tumbleweed),
                _ => throw new InvalidOperationException("The validated actor-zone source is unsupported.")
            };
            var Root = Graph.ByPath[Graph.RootPath].SourceRoot.transform;
            var Depth = 0;
            for (var Current = Source.transform; Current != Root; Current = Current.parent) Depth++;
            Poses = new Pose[Depth]; Scales = new Vector3[Depth];
            var Target = Source.transform;
            for (var Index = Depth - 1; Index >= 0; Index--, Target = Target.parent)
            {
                Poses[Index] = new Pose(Target.localPosition, Target.localRotation);
                Scales[Index] = Target.localScale;
            }
        }

        internal void Require(LevelGenStep Actual, PllBiomePrefabAnchors Anchors,
            DefinitionBindingResolver Resolver, string Context, string Field)
        {
            Binding.Require(Actual, Resolver, Context, Field);
            var Values = Fields(Actual);
            if (Values.Id != Id || !Values.Volume.Equals(Volume))
                throw DefinitionSemanticIdentity.Changed(Context, Field + ".identityOrVolume");
            if (PllActorZoneSourceContract.CompiledFailure(Anchors.transform,
                Anchors.FloorRoot.transform, Anchors.CampfireRoot.transform, Actual, Values.Output) is { } Failure)
                throw new InvalidOperationException(Failure + " " + Context + " '" + Field + "'.");
            var Target = Actual.transform;
            for (var Index = Poses.Length - 1; Index >= 0; Index--, Target = Target.parent)
                if (!Target.localPosition.Equals(Poses[Index].position)
                    || !Target.localRotation.Equals(Poses[Index].rotation) || !Target.localScale.Equals(Scales[Index]))
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".transformPath");
            Recipe.Require(Values.Source, Resolver, Context, Field + ".source");
            using (var Serialized = new SerializedObject(Actual))
            {
                if (PllAuthoringCommands.Property(Serialized, "BundleShape").intValue != (int)Volume.Shape
                    || !PllAuthoringCommands.Property(Serialized, "BundleCenter").vector3Value.Equals(Volume.Center)
                    || !PllAuthoringCommands.Property(Serialized, "BundleSize").vector3Value.Equals(Volume.Size)
                    || !PllAuthoringCommands.Property(Serialized, "BundleRadius").floatValue.Equals(Volume.Radius))
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".compiledVolume");
                var Compiled = PllAuthoringCommands.Property(Serialized, "BundleSourceValue").managedReferenceValue;
                if (!ReferenceEquals(Compiled, Values.Source))
                    Recipe.Require(Compiled, Resolver, Context, Field + ".compiledSource");
            }
        }

        static (string Id, PllZoneVolume Volume, Transform Output, object Source) Fields(LevelGenStep Zone) => Zone switch
        {
            PllEntityZoneAuthoring Entity => (Entity.Id, Entity.Volume, Entity.OutputRoot, Entity.Source),
            PllHazardZoneAuthoring Hazard => (Hazard.Id, Hazard.Volume, Hazard.OutputRoot, Hazard.Source),
            _ => throw new InvalidOperationException("The actor-zone component is unsupported.")
        };

        abstract class RecipeIdentity
        {
            internal abstract void Require(object Actual, DefinitionBindingResolver Resolver, string Context, string Field);
        }

        abstract class GroundIdentity : RecipeIdentity
        {
            readonly int Count;
            readonly float Chance;
            readonly float Slope;
            readonly float Spacing;

            protected GroundIdentity(int Count, float Chance, float Slope, float Spacing)
            {
                this.Count = Count; this.Chance = Chance; this.Slope = Slope; this.Spacing = Spacing;
            }

            protected void RequireGround(int Count, float Chance, float Slope, float Spacing, string Context, string Field)
            {
                if (Count != this.Count || !Chance.Equals(this.Chance)
                    || !Slope.Equals(this.Slope) || !Spacing.Equals(this.Spacing))
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".groundPlacement");
            }
        }

        sealed class BeetleIdentity : GroundIdentity
        {
            readonly AppearanceIdentity Appearance;
            internal BeetleIdentity(PllBeetleZoneSource Source, AssetIdentityCatalog Assets, string Field)
                : base(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing)
                => Appearance = AppearanceIdentity.Capture(Source.Appearance, Assets, Field);
            internal override void Require(object Actual, DefinitionBindingResolver Resolver, string Context, string Field)
            {
                if (Actual is not PllBeetleZoneSource Source) throw DefinitionSemanticIdentity.Changed(Context, Field + ".kind");
                RequireGround(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing, Context, Field);
                AppearanceIdentity.RequireOptional(Appearance, Source.Appearance, Resolver, Context, Field);
            }
        }

        sealed class ZombieIdentity : GroundIdentity
        {
            readonly bool CullRandomly;
            internal ZombieIdentity(PllMushroomZombieZoneSource Source)
                : base(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing)
                => CullRandomly = Source.CullRandomly;
            internal override void Require(object Actual, DefinitionBindingResolver Resolver, string Context, string Field)
            {
                if (Actual is not PllMushroomZombieZoneSource Source) throw DefinitionSemanticIdentity.Changed(Context, Field + ".kind");
                RequireGround(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing, Context, Field);
                if (Source.CullRandomly != CullRandomly) throw DefinitionSemanticIdentity.Changed(Context, Field + ".cullRandomly");
            }
        }

        sealed class CondorIdentity : RecipeIdentity
        {
            readonly float Chance;
            readonly Vector3 Nest;
            readonly AppearanceIdentity Appearance;
            internal CondorIdentity(PllCondorZoneSource Source, AssetIdentityCatalog Assets, string Field)
            {
                Chance = Source.SpawnChance; Nest = Source.NestPosition;
                Appearance = AppearanceIdentity.Capture(Source.Appearance, Assets, Field);
            }
            internal override void Require(object Actual, DefinitionBindingResolver Resolver, string Context, string Field)
            {
                if (Actual is not PllCondorZoneSource Source) throw DefinitionSemanticIdentity.Changed(Context, Field + ".kind");
                if (!Source.SpawnChance.Equals(Chance) || !Source.NestPosition.Equals(Nest))
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".condorPlacement");
                AppearanceIdentity.RequireOptional(Appearance, Source.Appearance, Resolver, Context, Field);
            }
        }

        sealed class PluginIdentity : GroundIdentity
        {
            readonly string PluginGuid;
            readonly string FactoryId;
            readonly string Revision;
            readonly AssetBinding Prefab;
            internal PluginIdentity(PllPluginEntityZoneSource Source, AssetIdentityCatalog Assets, string Field)
                : base(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing)
            {
                PluginGuid = Source.PluginGuid; FactoryId = Source.FactoryId; Revision = Source.Revision;
                Prefab = AssetBinding.Capture(Source.Prefab, Assets, Field + ".prefab");
            }
            internal override void Require(object Actual, DefinitionBindingResolver Resolver, string Context, string Field)
            {
                if (Actual is not PllPluginEntityZoneSource Source) throw DefinitionSemanticIdentity.Changed(Context, Field + ".kind");
                RequireGround(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing, Context, Field);
                if (Source.PluginGuid != PluginGuid || Source.FactoryId != FactoryId || Source.Revision != Revision)
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".factoryIdentity");
                Prefab.Require(Source.Prefab, Resolver, Context, Field + ".prefab");
            }
        }

        sealed class AuthoredEntityIdentity : GroundIdentity
        {
            readonly PrefabBinding Prefab;
            internal AuthoredEntityIdentity(PrefabGraph Graph,
                IReadOnlyDictionary<string, string> StagedPaths, PllAuthoredEntityZoneSource Source)
                : base(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing)
                => Prefab = PrefabBinding.CaptureNode(Graph, StagedPaths,
                    Graph.ByPath[AssetDatabase.GetAssetPath(Source.Prefab)]);
            internal override void Require(object Actual, DefinitionBindingResolver Resolver, string Context, string Field)
            {
                if (Actual is not PllAuthoredEntityZoneSource Source) throw DefinitionSemanticIdentity.Changed(Context, Field + ".kind");
                RequireGround(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing, Context, Field);
                Prefab.Require(Source.Prefab, Resolver, Context, Field + ".prefab");
            }
        }

        sealed class AuthoredHazardIdentity : GroundIdentity
        {
            readonly PrefabBinding Prefab;
            internal AuthoredHazardIdentity(PrefabGraph Graph,
                IReadOnlyDictionary<string, string> StagedPaths, PllAuthoredHazardZoneSource Source)
                : base(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing)
                => Prefab = PrefabBinding.CaptureNode(Graph, StagedPaths,
                    Graph.ByPath[AssetDatabase.GetAssetPath(Source.Prefab)]);
            internal override void Require(object Actual, DefinitionBindingResolver Resolver, string Context, string Field)
            {
                if (Actual is not PllAuthoredHazardZoneSource Source) throw DefinitionSemanticIdentity.Changed(Context, Field + ".kind");
                RequireGround(Source.MaximumInitialCount, Source.SpawnChance, Source.MaximumSlope, Source.MinimumSpacing, Context, Field);
                Prefab.Require(Source.Prefab, Resolver, Context, Field + ".prefab");
            }
        }

        abstract class PointsIdentity : RecipeIdentity
        {
            readonly int MaximumAlive;
            readonly Vector3[] Points;
            protected PointsIdentity(int MaximumAlive, Vector3[] Points)
            {
                this.MaximumAlive = MaximumAlive; this.Points = (Vector3[])Points.Clone();
            }
            protected void RequirePoints(int MaximumAlive, Vector3[] Points, string Context, string Field)
            {
                if (MaximumAlive != this.MaximumAlive || Points == null || !this.Points.SequenceEqual(Points))
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".populationOrPoints");
            }
        }

        sealed class TornadoIdentity : PointsIdentity
        {
            internal TornadoIdentity(PllTornadoZoneSource Source) : base(Source.MaximumAlive, Source.Points) { }
            internal override void Require(object Actual, DefinitionBindingResolver Resolver, string Context, string Field)
            {
                if (Actual is not PllTornadoZoneSource Source) throw DefinitionSemanticIdentity.Changed(Context, Field + ".kind");
                RequirePoints(Source.MaximumAlive, Source.Points, Context, Field);
            }
        }

        sealed class TumbleweedIdentity : PointsIdentity
        {
            readonly Vector3 Offset;
            internal TumbleweedIdentity(PllTumbleweedZoneSource Source) : base(Source.MaximumAlive, Source.Positions)
                => Offset = Source.SpawnOffset;
            internal override void Require(object Actual, DefinitionBindingResolver Resolver, string Context, string Field)
            {
                if (Actual is not PllTumbleweedZoneSource Source) throw DefinitionSemanticIdentity.Changed(Context, Field + ".kind");
                RequirePoints(Source.MaximumAlive, Source.Positions, Context, Field);
                if (!Source.SpawnOffset.Equals(Offset)) throw DefinitionSemanticIdentity.Changed(Context, Field + ".spawnOffset");
            }
        }

        sealed class AppearanceIdentity
        {
            readonly RendererIdentity[] Renderers;
            AppearanceIdentity(PllNativeEntityAppearance Source, AssetIdentityCatalog Assets, string Field)
            {
                Renderers = new RendererIdentity[Source.Renderers.Length];
                for (var Index = 0; Index < Renderers.Length; Index++)
                    Renderers[Index] = new RendererIdentity(Source.Renderers[Index], Assets, Field + ".renderers[" + Index + "]");
            }
            internal static AppearanceIdentity Capture(PllNativeEntityAppearance Source, AssetIdentityCatalog Assets, string Field)
                => Source == null ? null : new AppearanceIdentity(Source, Assets, Field);
            internal static void RequireOptional(AppearanceIdentity Expected, PllNativeEntityAppearance Actual,
                DefinitionBindingResolver Resolver, string Context, string Field)
            {
                if (Expected == null)
                {
                    if (Actual != null) throw DefinitionSemanticIdentity.Changed(Context, Field + ".appearance");
                    return;
                }
                if (Actual == null || Actual.Renderers == null || Actual.Renderers.Length != Expected.Renderers.Length)
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".appearance.renderers");
                for (var Index = 0; Index < Expected.Renderers.Length; Index++)
                    Expected.Renderers[Index].Require(Actual.Renderers[Index], Resolver, Context, Field + ".appearance.renderers[" + Index + "]");
            }
        }

        sealed class RendererIdentity
        {
            readonly string Path;
            readonly AssetBinding Mesh;
            readonly AssetBinding[] Materials;
            readonly string[] Bones;
            readonly Bounds Bounds;
            internal RendererIdentity(PllNativeEntityRenderer Source, AssetIdentityCatalog Assets, string Field)
            {
                Path = Source.RendererPath; Mesh = AssetBinding.Capture(Source.Mesh, Assets, Field + ".mesh");
                Bones = (string[])Source.BonePaths.Clone(); Bounds = Source.LocalBounds;
                Materials = new AssetBinding[Source.Materials.Length];
                for (var Index = 0; Index < Materials.Length; Index++)
                    Materials[Index] = AssetBinding.Capture(Source.Materials[Index], Assets, Field + ".materials[" + Index + "]");
            }
            internal void Require(PllNativeEntityRenderer Actual, DefinitionBindingResolver Resolver, string Context, string Field)
            {
                if (Actual == null || Actual.RendererPath != Path || !Actual.LocalBounds.Equals(Bounds)
                    || Actual.BonePaths == null || !Bones.SequenceEqual(Actual.BonePaths)
                    || Actual.Materials == null || Actual.Materials.Length != Materials.Length)
                    throw DefinitionSemanticIdentity.Changed(Context, Field);
                Mesh.Require(Actual.Mesh, Resolver, Context, Field + ".mesh");
                for (var Index = 0; Index < Materials.Length; Index++)
                    Materials[Index].Require(Actual.Materials[Index], Resolver, Context, Field + ".materials[" + Index + "]");
            }
        }
    }
}
