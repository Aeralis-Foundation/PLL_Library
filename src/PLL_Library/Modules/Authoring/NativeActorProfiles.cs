using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal static class NativeActorProfileTable
{
    internal readonly struct Entry
    {
        internal Entry(string Label, string Segment, string Path, params string[] Prefabs)
        {
            this.Label = Label; this.Segment = Segment; this.Path = Path; this.Prefabs = Prefabs;
        }

        internal string Label { get; }
        internal string Segment { get; }
        internal string Path { get; }
        internal string[] Prefabs { get; }
    }

    internal static Entry Get(PllNativeActorProfile Profile) => Profile switch
    {
        PllNativeActorProfile.Scorpions => new("Scorpions", "Mesa", "Wall/Props/Scorpions", "Scorpion"),
        PllNativeActorProfile.Antlions => new("Antlions", "Mesa", "Platteau/Rocks/Antlion", "AntLion"),
        PllNativeActorProfile.Spiders => new("Spiders", "Roots", "WallProps/Spiders", "SpiderDropper Web Variant"),
        PllNativeActorProfile.Beehives => new("Beehives", "Tropics", "Default/Props_Wall/Behive", "Beehive"),
        PllNativeActorProfile.JellyfishGround => new("Slippery Jellyfish (ground)", "Beach",
            "Default/PlateauProps/Jellies", "SlipperyJellyfish"),
        PllNativeActorProfile.JellyfishWalls => new("Slippery Jellyfish (walls)", "Beach",
            "Default/WallProps/Jellies", "SlipperyJellyfish"),
        PllNativeActorProfile.VenusFlyTraps => new("Venus Fly Traps", "Gloom", "Props/VenusFlyTraps", "VenusFlyTrap"),
        PllNativeActorProfile.GhostFires => new("Ghost Fire Belltowers", "Gloom", "Props/Ghost Fires", "Belltower"),
        PllNativeActorProfile.Cacti => new("Cacti", "Mesa", "Platteau/Props/CactusForest/Big", "Tall Cactus", "Short Cactus"),
        PllNativeActorProfile.CactusBalls => new("Cactus Balls", "Mesa", "Platteau/Props/Cactus",
            "Cactus Ball Big", "Cactus Ball Base"),
        PllNativeActorProfile.Dynamite => new("Dynamite", "Mesa", "Wall/Props/Dynamite", "SingleItemSpawner"),
        PllNativeActorProfile.Mushrooms => new("Shelf, Bounce, Healing and Cloud Mushrooms", "Roots", "ShelfShrooms",
            "ShelfShroom", "BounceShroom", "HealingPuffShroom", "CloudFungus"),
        PllNativeActorProfile.TickPlants => new("Jungle Plants (ticks)", "Tropics", "Default/Props_Wall/Weed",
            "Jungle_SharpPlant", "Jungle_Aleo", "Jungle_MiniBanana"),
        PllNativeActorProfile.HotSpring => new("Hot Spring (capybaras)", "Alpine", "Capybara", "Onsen"),
        PllNativeActorProfile.Oasis => new("Oasis (capybaras)", "Mesa", "Platteau/Rocks/Oasis", "Oasis"),
        PllNativeActorProfile.TickMonsteras => new("Monstera Plants (ticks)", "Tropics", "Default/Props_Wall/Monsteras",
            "Jungle_Monstera", "Jungle_Monstera_Thick"),
        PllNativeActorProfile.Frogs => new("Lily Pad Frogs", "Gloom", "Props/LilypadWithFrog", "LilyPadSpawner"),
        PllNativeActorProfile.JungleVines => new("Berry Vines (jungle)", "Tropics", "Default/Props_Wall/Vines", "JungleVine"),
        PllNativeActorProfile.SwampVines => new("Berry Vines (swamp)", "Gloom", "Props/Vines (1)", "SwampVine"),
        PllNativeActorProfile.Cattails => new("Cattails (glizzies)", "Gloom", "Props/Cattails glizzy",
            "Cattail Bunch Glizzy Variant"),
        _ => throw new ArgumentOutOfRangeException(nameof(Profile), "The Native Actor profile is unsupported.")
    };

    internal static IEnumerable<PllNativeActorProfile> All
    {
        get
        {
            foreach (PllNativeActorProfile Value in Enum.GetValues(typeof(PllNativeActorProfile))) yield return Value;
        }
    }

    internal static ulong Bit(PllNativeActorProfile Profile) => 1UL << (int)Profile;
}

internal sealed class NativeActorProfileSources
{
    readonly NativeSegmentCatalog Catalog;
    readonly Dictionary<PllNativeActorProfile, NativeActorProfileSource> Captured = new();

    internal NativeActorProfileSources(NativeSegmentCatalog Catalog) =>
        this.Catalog = Catalog ?? throw new ArgumentNullException(nameof(Catalog));

    internal NativeActorProfileSource Require(PllNativeActorProfile Profile)
    {
        if (!Captured.TryGetValue(Profile, out var Source))
            Captured.Add(Profile, Source = NativeActorProfileSource.Capture(Catalog, Profile));
        return Source;
    }

    internal void WriteManifest(BinaryWriter Writer, ulong Mask)
    {
        Writer.Write("PLL/NativeActorProfiles/v1"); Writer.Write(Mask);
        foreach (var Profile in NativeActorProfileTable.All)
            if ((Mask & NativeActorProfileTable.Bit(Profile)) != 0) Require(Profile).WriteManifest(Writer);
    }
}

internal sealed class NativeActorProfileSource
{
    static readonly MethodInfo CloneMethod = typeof(object).GetMethod(
        "MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly HashSet<string> MapBoundConstraints = new(StringComparer.Ordinal)
    {
        nameof(PSC_Height), nameof(PSC_RequiredMaterial), nameof(PSC_VolumeLight)
    };

    readonly byte[] Fingerprint;
    readonly int ViewCount;

    NativeActorProfileSource(PllNativeActorProfile Profile, PropSpawner Source, Transform SegmentRoot)
    {
        this.Profile = Profile;
        Props = (GameObject[])NativePrefabContracts.EffectiveProps(Source)!.Clone();
        RelativeRotation = Quaternion.Inverse(SegmentRoot.rotation) * Source.transform.rotation;
        Raycast = Source.raycastPosition; RayLength = Source.rayLength; RayOffset = Source.rayDirectionOffset;
        RayNearCutoff = Source.rayNearCutoff; Layer = Source.layerType; SyncTransforms = Source.syncTransforms;
        VanillaCount = Source.nrOfSpawns;
        Modifiers = Clone(Source.modifiers);
        Constraints = Clone(Source.constraints);
        PostConstraints = Clone(Source.postConstraints);
        foreach (var Prop in Props) ViewCount = Math.Max(ViewCount, Prop.GetComponentsInChildren<PhotonView>(true).Length);
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        {
            Writer.Write((int)Profile);
            Writer.Write(DeterministicObjectIdentity.RelativeComponentPath(Source, SegmentRoot, 0));
            Writer.Write(Props.Length);
            foreach (var Prop in Props)
            {
                var Graph = DeterministicObjectIdentity.HashObjectHierarchy(Prop.transform, "PLL/NativeActorProfile/v1");
                Writer.Write(Prop.name); Writer.Write(Graph.Length); Writer.Write(Graph);
            }
            Writer.Write(Raycast); ManifestEncoding.WriteScalar(Writer, RayLength);
            ManifestEncoding.WriteVector(Writer, RayOffset); ManifestEncoding.WriteScalar(Writer, RayNearCutoff);
            ManifestEncoding.WriteVector(Writer, RelativeRotation.eulerAngles);
            Writer.Write((int)Layer); Writer.Write(SyncTransforms); Writer.Write(ViewCount);
            foreach (var Value in Modifiers) Writer.Write(Value.GetType().FullName ?? "");
            Writer.Write('|');
            foreach (var Value in Constraints) Writer.Write(Value.GetType().FullName ?? "");
            Writer.Write('|');
            foreach (var Value in PostConstraints) Writer.Write(Value.GetType().FullName ?? "");
        }
        Fingerprint = Stream.ToArray();
    }

    internal PllNativeActorProfile Profile { get; }
    internal GameObject[] Props { get; }
    internal Quaternion RelativeRotation { get; }
    internal bool Raycast { get; }
    internal float RayLength { get; }
    internal Vector3 RayOffset { get; }
    internal float RayNearCutoff { get; }
    internal HelperFunctions.LayerType Layer { get; }
    internal bool SyncTransforms { get; }
    internal int VanillaCount { get; }
    readonly List<PropSpawnerMod> Modifiers;
    readonly List<PropSpawnerConstraint> Constraints;
    readonly List<PropSpawnerConstraintPost> PostConstraints;

    internal bool NeedsTriggerRelay
    {
        get
        {
            foreach (var Prop in Props)
                if ((Prop.GetComponent<SlipperyJellyfish>() || Prop.GetComponent<TriggerEvent>()) && !Prop.GetComponent<PhotonView>())
                    return true;
            return false;
        }
    }

    internal bool HasMobs
    {
        get
        {
            foreach (var Prop in Props) if (Prop.GetComponentInChildren<Mob>(true)) return true;
            return false;
        }
    }

    internal void AdmitInitialCount(int Count)
    {
        if (ViewCount == 0) return;
        var Capacity = (PhotonNetwork.MAX_VIEW_IDS - 1) / ViewCount;
        if (Count > Capacity)
            throw new InvalidOperationException(
                $"{NativeActorProfileTable.Get(Profile).Label} initial count {Count} exceeds the native PhotonView capacity {Capacity}.");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint);
    }

    internal List<PropSpawnerMod> CloneModifiers()
    {
        var Result = Clone(Modifiers);
        Result.Add(new PllSpawnConditionLayers());
        Result.Add(new PllPhysicsPoseSync());
        return Result;
    }

    internal List<PropSpawnerConstraint> CloneConstraints(bool KeepSurfaceTags, bool KeepPatches, bool ReplaceSpacing)
    {
        var Result = new List<PropSpawnerConstraint>();
        foreach (var Value in Clone(Constraints))
        {
            if (MapBoundConstraints.Contains(Value.GetType().Name)) continue;
            if (!KeepSurfaceTags && Value is PSC_SurfaceRestrictions) continue;
            if (!KeepPatches && Value is PSC_Perlin) continue;
            if (ReplaceSpacing && Value is PSC_SameTypeDistance) continue;
            if (Value is PSC_Normal Normal && !Normal.mute)
            {
                Result.Add(new PllSlopeConstraint(Normal.minAngle, Normal.maxAngle));
                continue;
            }
            Result.Add(Value);
        }
        return Result;
    }


    internal static HelperFunctions.LayerType AuthoredLayer(HelperFunctions.LayerType Layer) =>
        Layer is HelperFunctions.LayerType.Map or HelperFunctions.LayerType.Terrain ? HelperFunctions.LayerType.TerrainMap : Layer;

    internal List<PropSpawnerConstraintPost> ClonePostConstraints() => Clone(PostConstraints);

    static List<T> Clone<T>(List<T>? Values) where T : class
    {
        var Result = new List<T>();
        if (Values is null) return Result;
        foreach (var Value in Values)
            if (Value is not null) Result.Add((T)CloneMethod.Invoke(Value, null));
        return Result;
    }

    internal static NativeActorProfileSource Capture(NativeSegmentCatalog Catalog, PllNativeActorProfile Profile)
    {
        var Entry = NativeActorProfileTable.Get(Profile);
        var Root = Catalog.RequireOrdinary(Entry.Segment).SegmentParent.transform;
        var Found = Root.Find(Entry.Path);
        var Spawner = Found ? Found.GetComponent<PropSpawner>() : null;
        if (!Spawner || !Accepts(Spawner, Entry))
        {
            Spawner = null;
            string? Best = null;
            foreach (var Candidate in Root.GetComponentsInChildren<PropSpawner>(true))
            {
                if (!Accepts(Candidate, Entry)) continue;
                var Path = DeterministicObjectIdentity.RelativeComponentPath(Candidate, Root, 0);
                if (Best is null || StringComparer.Ordinal.Compare(Path, Best) < 0) { Best = Path; Spawner = Candidate; }
            }
        }
        if (!Spawner)
            throw new InvalidOperationException(
                $"PEAK's {Entry.Segment} biome has no native spawner for {Entry.Label}. Update PLL_Library.");
        foreach (var Prop in NativePrefabContracts.EffectiveProps(Spawner)!)
        {
            if (Prop.scene.IsValid())
                throw new InvalidOperationException($"PEAK's native {Entry.Label} candidate '{Prop.name}' is a scene object, not a prefab.");
            foreach (var View in Prop.GetComponentsInChildren<PhotonView>(true))
                if (View.ViewID != 0)
                    throw new InvalidOperationException($"PEAK's native {Entry.Label} candidate '{Prop.name}' has an assigned PhotonView.");
        }
        return new NativeActorProfileSource(Profile, Spawner!, Root);
    }

    static bool Accepts(PropSpawner Spawner, NativeActorProfileTable.Entry Entry)
    {
        if (Spawner.GetType() != typeof(PropSpawner)) return false;
        var Props = NativePrefabContracts.EffectiveProps(Spawner);
        if (Props is null || Props.Length == 0) return false;
        foreach (var Prop in Props)
            if (!Prop) return false;
        foreach (var Name in Entry.Prefabs)
            if (Array.FindIndex(Props, Prop => string.Equals(Prop.name, Name, StringComparison.Ordinal)) < 0) return false;
        return true;
    }
}

[Serializable]
internal sealed class PllSlopeConstraint : PropSpawnerConstraint
{
    [SerializeField] float MinimumAngle;
    [SerializeField] float MaximumAngle;

    internal PllSlopeConstraint(float MinimumAngle, float MaximumAngle)
    {
        this.MinimumAngle = MinimumAngle; this.MaximumAngle = MaximumAngle;
    }

    public override bool CheckConstraint(PropSpawner.SpawnData spawnData)
    {
        var Angle = Vector3.Angle(Vector3.up, spawnData.normal);
        var AboveMinimum = MinimumAngle <= 0f ? Angle >= 0f : Angle > MinimumAngle;
        var BelowMaximum = MaximumAngle >= 180f ? Angle <= 180f : Angle < MaximumAngle;
        return AboveMinimum && BelowMaximum;
    }
}

[Serializable]
internal sealed class PllSpawnConditionLayers : PropSpawnerMod
{
    public override void ModifyObject(GameObject spawned, PropSpawner.SpawnData spawnData)
    {
        foreach (var Condition in spawned.GetComponentsInChildren<MultipleGroundPoints>(true))
            Condition.layerType = NativeActorProfileSource.AuthoredLayer(Condition.layerType);
        foreach (var Condition in spawned.GetComponentsInChildren<SpineCheck>(true))
            Condition.layerType = NativeActorProfileSource.AuthoredLayer(Condition.layerType);
    }
}


[Serializable]
internal sealed class PllPhysicsPoseSync : PropSpawnerMod
{
    public override void ModifyObject(GameObject spawned, PropSpawner.SpawnData spawnData)
    {
        foreach (var Body in spawned.GetComponentsInChildren<Rigidbody>(true))
        {
            Body.position = Body.transform.position;
            Body.rotation = Body.transform.rotation;
        }
    }
}

