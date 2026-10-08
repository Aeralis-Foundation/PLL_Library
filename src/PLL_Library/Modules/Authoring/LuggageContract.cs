using System;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal enum LuggagePlacement : byte
{
    FixedCampfire,
    GeneratedCandidate
}

internal sealed class LuggageContract
{
    readonly LuggagePlacement Placement;
    readonly ComponentBinding<PllLuggageAuthoring> AuthoringBinding;
    readonly ComponentBinding<global::Luggage> LuggageBinding;
    readonly ComponentBinding<PhotonView> ViewBinding;
    readonly ComponentBinding<Animator> AnimatorBinding;
    readonly TransformBinding? FixedOwnerBinding;
    readonly ObjectBinding? ActivateOnOpenBinding;
    readonly ComponentBinding<PllLuggageAuthoring>? ReplacesAuthoredBinding;
    readonly LuggageSpawnGroupContract[] SpawnGroups;
    readonly WeightedLuggageItemContract[] CustomItems;
    readonly PllLuggageContentsMode ContentsMode;
    readonly NativeCompatibility? Compatibility;
    readonly PllLuggageRepeatMode RepeatMode;
    readonly RuntimeAnimatorController? AuthoredAnimatorController;
    readonly RuntimeAnimatorController RuntimeAnimatorController;
    readonly string DisplayName;
    readonly string NativeContentsId;
    readonly string NativeReplacementId;
    readonly float OpenTime;
    readonly float SpawnChance;
    NativeLuggageContents? ResolvedContents;
    bool ExternalResolved;

    LuggageContract(
        LuggagePlacement Placement,
        ComponentBinding<PllLuggageAuthoring> AuthoringBinding,
        ComponentBinding<global::Luggage> LuggageBinding,
        ComponentBinding<PhotonView> ViewBinding,
        ComponentBinding<Animator> AnimatorBinding,
        TransformBinding? FixedOwnerBinding,
        ObjectBinding? ActivateOnOpenBinding,
        ComponentBinding<PllLuggageAuthoring>? ReplacesAuthoredBinding,
        LuggageSpawnGroupContract[] SpawnGroups,
        WeightedLuggageItemContract[] CustomItems,
        PllLuggageContentsMode ContentsMode,
        NativeCompatibility? Compatibility,
        PllLuggageRepeatMode RepeatMode,
        RuntimeAnimatorController? AuthoredAnimatorController,
        RuntimeAnimatorController RuntimeAnimatorController,
        string Id,
        string DisplayName,
        string NativeContentsId,
        string ReplacesNativeId,
        float OpenTime,
        float SpawnChance)
    {
        this.Placement = Placement;
        this.AuthoringBinding = AuthoringBinding;
        this.LuggageBinding = LuggageBinding;
        this.ViewBinding = ViewBinding;
        this.AnimatorBinding = AnimatorBinding;
        this.FixedOwnerBinding = FixedOwnerBinding;
        this.ActivateOnOpenBinding = ActivateOnOpenBinding;
        this.ReplacesAuthoredBinding = ReplacesAuthoredBinding;
        this.SpawnGroups = SpawnGroups;
        this.CustomItems = CustomItems;
        this.ContentsMode = ContentsMode;
        this.Compatibility = Compatibility;
        this.RepeatMode = RepeatMode;
        this.AuthoredAnimatorController = AuthoredAnimatorController;
        this.RuntimeAnimatorController = RuntimeAnimatorController;
        this.Id = Id;
        this.DisplayName = DisplayName;
        this.NativeContentsId = NativeContentsId;
        NativeReplacementId = ReplacesNativeId;
        this.OpenTime = OpenTime;
        this.SpawnChance = SpawnChance;
    }

    internal PllLuggageAuthoring Source => AuthoringBinding.Source;
    internal bool Generated => Placement == LuggagePlacement.GeneratedCandidate;
    internal GameObject Root => Source.gameObject;
    internal string Id { get; }
    internal PllLuggageAuthoring? ReplacesAuthored => ReplacesAuthoredBinding?.Source;
    internal string ReplacesNativeId => NativeReplacementId;
    internal bool HasReplacement => ReplacesAuthoredBinding is not null || ReplacesNativeId.Length != 0;
    internal bool RequiresNativeCatalog => ContentsMode != PllLuggageContentsMode.CustomItems
        || ReplacesNativeId.Length != 0;
    internal NativeLuggageFingerprint? NativeReplacementFingerprint { get; private set; }

    internal static LuggageContract Create(
        Transform Template,
        Transform CampfireRoot,
        PllBiomeBehavior Behavior,
        BiomeId BiomeId,
        PllLuggageAuthoring Source,
        AuthoringCandidateGraph Graph)
    {
        if (!Source)
            throw new ArgumentException($"Biome '{BiomeId}' contains destroyed luggage authoring.", nameof(Source));
        var Root = Source.gameObject;
        var Stable = TransformOwnership.Contains(Template, Source.transform);
        var Generated = Graph.Contains(Root);
        if (Stable == Generated)
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Source.Id}' must be one fixed root or one exact generated candidate root.",
                nameof(Source));

        LuggagePlacement Placement;
        Transform? FixedOwner = null;
        if (Generated) Placement = LuggagePlacement.GeneratedCandidate;
        else
        {
            if (Source.transform == CampfireRoot
                || !TransformOwnership.Contains(CampfireRoot, Source.transform))
                throw new ArgumentException(
                    $"Biome '{BiomeId}' luggage '{Source.Id}' must belong strictly under CampfireRoot.",
                    nameof(Source));
            Placement = LuggagePlacement.FixedCampfire;
            FixedOwner = CampfireRoot;
        }

        if (!PllAuthoringValueContract.IsLocalId(Source.Id)
            || !PllAuthoringValueContract.IsDisplayText(Source.DisplayName))
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage ID or display name is invalid.", nameof(Source));
        if (!PllAuthoringValueContract.IsDefined(Source.ContentsMode)
            || !PllAuthoringValueContract.IsDefined(Source.RepeatMode)
            || !PllAuthoringValueContract.ValidLuggageContents(
                Source.ContentsMode, Source.CustomItems.Count, Source.NativeContentsId))
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Source.Id}' has an invalid contents or repeat mode.", nameof(Source));
        if (!PllAuthoringValueContract.CanEncodeExactly(Source.OpenTime) || Source.OpenTime <= 0f
            || !PllAuthoringValueContract.CanEncodeExactly(Source.SpawnChance)
            || Source.SpawnChance is < 0f or > 1f)
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Source.Id}' has an invalid open time or spawn chance.", nameof(Source));
        BiomeAuthoringContract.RequireToken(
            Source.NativeContentsId, BiomeId, "native luggage contents ID",
            Source.ContentsMode != PllLuggageContentsMode.NativeLuggage);
        BiomeAuthoringContract.RequireToken(
            Source.ReplacesNativeId, BiomeId, "native luggage replacement ID", true);
        if (Source.ReplacesAuthored && Source.ReplacesNativeId.Length != 0)
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Source.Id}' cannot replace authored and native luggage together.",
                nameof(Source));
        if (ReferenceEquals(Source.ReplacesAuthored, Source))
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Source.Id}' cannot replace itself.", nameof(Source));
        var Compatibility = PllBiomeBehaviorCompatibility.Resolve(Behavior);
        if (Source.ContentsMode == PllLuggageContentsMode.NativeProgression
            && Compatibility is null)
            throw new ArgumentException(
                $"Authored biome '{BiomeId}' luggage '{Source.Id}' has no native progression contents.",
                nameof(Source));
        if (Generated && (!Root.activeSelf || !Source.SpawnChance.Equals(1f)
                || Source.ReplacesAuthored || Source.ReplacesNativeId.Length != 0)
            || !Generated && (Root.activeSelf || !TransformOwnership.ParentDiscoverable(FixedOwner!, Source.transform)
                || TransformOwnership.HasPropSpawnerInPath(FixedOwner!, Source.transform)
                || TransformOwnership.HasConfigurationBranch(FixedOwner!, Source.transform)))
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Source.Id}' violates its placement ownership contract.",
                nameof(Source));
        if (!Source.enabled || Source.GetComponents<PllLuggageAuthoring>().Length != 1)
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Source.Id}' must own one enabled luggage authoring component.",
                nameof(Source));

        var Luggage = Root.GetComponents<global::Luggage>();
        var Views = Root.GetComponents<PhotonView>();
        var Animators = Root.GetComponents<Animator>();
        if (Luggage.Length != 1 || !Luggage[0] || Luggage[0].GetType() != typeof(global::Luggage)
            || !Luggage[0].enabled || Luggage[0].IsOpen
            || Views.Length != 1 || !Views[0] || !Views[0].enabled || Views[0].ViewID != 0
            || Animators.Length != 1 || !Animators[0] || !Animators[0].enabled
            || !Animators[0].runtimeAnimatorController
            || Source.AnimatorController
                && Animators[0].runtimeAnimatorController != Source.AnimatorController
            || Root.GetComponentsInChildren<global::Luggage>(true).Length != 1
            || Root.GetComponentsInChildren<PhotonView>(true).Length != 1
            || Root.GetComponentsInChildren<Animator>(true).Length != 1
            || Root.GetComponentsInChildren<Item>(true).Length != 0
            || Root.GetComponentsInChildren<global::Campfire>(true).Length != 0
            || Root.GetComponentsInChildren<RespawnChest>(true).Length != 0)
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Source.Id}' does not match its staged PEAK interaction shape.",
                nameof(Source));

        var ActivateOnOpen = Source.ActivateOnOpen;
        var SpawnGroups = LuggageSpawnGroupContract.CreateAll(
            Source, Root.transform, ActivateOnOpen ? ActivateOnOpen.transform : null, BiomeId);
        var CustomItems = WeightedLuggageItemContract.CreateAll(Source, BiomeId);
        if (!ValidRootPresentation(Root.transform, ActivateOnOpen ? ActivateOnOpen.transform : null)
            || !ValidActivateOnOpen(Source, ActivateOnOpen)
            || !PllStagedLuggageShapeContract.Matches(
                Luggage[0], Source, static Group => Group.percentageOdds))
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Source.Id}' has invalid presentation, open activation, or staged spawn groups.",
                nameof(Source));

        ComponentBinding<PllLuggageAuthoring>? ReplacesAuthoredBinding = null;
        if (Source.ReplacesAuthored)
        {
            if (!Stable || !TransformOwnership.Contains(Template, Source.ReplacesAuthored.transform))
                throw new ArgumentException(
                    $"Biome '{BiomeId}' luggage '{Source.Id}' has a foreign or generated authored replacement target.",
                    nameof(Source));
            ReplacesAuthoredBinding = ComponentBinding<PllLuggageAuthoring>.Create(
                Template, Source.ReplacesAuthored);
        }
        var BindingRoot = Generated ? Source.transform : Template;
        return new LuggageContract(
            Placement,
            ComponentBinding<PllLuggageAuthoring>.Create(BindingRoot, Source),
            ComponentBinding<global::Luggage>.Create(BindingRoot, Luggage[0]),
            ComponentBinding<PhotonView>.Create(BindingRoot, Views[0]),
            ComponentBinding<Animator>.Create(BindingRoot, Animators[0]),
            FixedOwner is null ? null : TransformBinding.Create(Template, FixedOwner),
            ActivateOnOpen ? ObjectBinding.Create(BindingRoot, ActivateOnOpen) : null,
            ReplacesAuthoredBinding, SpawnGroups, CustomItems,
            Source.ContentsMode, Compatibility, Source.RepeatMode,
            Source.AnimatorController, Animators[0].runtimeAnimatorController,
            Source.Id, Source.DisplayName, Source.NativeContentsId, Source.ReplacesNativeId,
            Source.OpenTime, Source.SpawnChance);
    }

    internal void ValidateAuthoredReplacement(
        IReadOnlyDictionary<PllLuggageAuthoring, LuggageContract> Contracts,
        BiomeId BiomeId)
    {
        if (ReplacesAuthoredBinding is null) return;
        if (!Contracts.TryGetValue(ReplacesAuthoredBinding.Source, out var Target)
            || Target.Generated || Target.HasReplacement)
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Id}' has an invalid authored replacement target.",
                "source");
    }

    internal void ResolveExternal(
        ItemDatabaseCatalog Items,
        NativeLuggageCatalog? Native,
        BiomeId BiomeId)
    {
        ClearExternal();
        try
        {
            var ResolvedItems = new HashSet<GameObject>();
            foreach (var Item in CustomItems)
            {
                Item.ResolveExternal(Items, BiomeId, Id);
                if (!ResolvedItems.Add(Item.ResolveRuntime(BiomeId, Id)))
                    throw new InvalidOperationException(
                        $"Biome '{BiomeId}' luggage '{Id}' custom items resolve to one ItemDatabase identity.");
            }

            var Contents = ContentsMode switch
            {
                PllLuggageContentsMode.NativeProgression =>
                    (Native ?? throw MissingCatalog(BiomeId))
                        .ResolveProgression($"Biome '{BiomeId}' luggage '{Id}' progression contents"),
                PllLuggageContentsMode.NativeLuggage =>
                    (Native ?? throw MissingCatalog(BiomeId))
                        .ResolveContents(NativeContentsId,
                            $"Biome '{BiomeId}' luggage '{Id}' native contents"),
                PllLuggageContentsMode.CustomItems => null,
                _ => throw new InvalidOperationException(
                    $"Biome '{BiomeId}' luggage '{Id}' contents mode is outside the sealed model.")
            };
            if (ContentsMode == PllLuggageContentsMode.NativeLuggage
                && Contents is { HeightBased: true } && Compatibility is null)
                throw new InvalidOperationException(
                    $"Authored biome '{BiomeId}' luggage '{Id}' cannot use HeightBased native luggage contents.");
            ResolvedContents = Contents;
            NativeReplacementFingerprint = ReplacesNativeId.Length == 0
                ? null
                : (Native ?? throw MissingCatalog(BiomeId))
                    .Resolve(ReplacesNativeId,
                        $"Biome '{BiomeId}' luggage '{Id}' native replacement").Fingerprint;
            ExternalResolved = true;
        }
        catch
        {
            ClearExternal();
            throw;
        }

        InvalidOperationException MissingCatalog(BiomeId IdValue) => new(
            $"Biome '{IdValue}' luggage '{Id}' cannot resolve native contents before PEAK's canonical luggage catalog exists.");
    }

    internal void ClearExternal()
    {
        foreach (var Item in CustomItems) Item.ClearExternal();
        ResolvedContents = null;
        NativeReplacementFingerprint = null;
        ExternalResolved = false;
    }

    internal ResolvedLuggage Resolve(Transform Template, BiomeId BiomeId)
    {
        if (Generated)
            throw new InvalidOperationException(
                $"Biome '{BiomeId}' generated luggage '{Id}' cannot resolve as a fixed root.");
        return ResolveCore(Template, null, BiomeId);
    }

    internal ResolvedLuggage ResolveGenerated(
        PllLuggageAuthoring Runtime,
        BiomeId BiomeId)
    {
        if (!Generated || !Runtime)
            throw new InvalidOperationException(
                $"Biome '{BiomeId}' generated luggage '{Id}' has an invalid runtime root.");
        return ResolveCore(Runtime.transform, Runtime, BiomeId);
    }

    ResolvedLuggage ResolveCore(
        Transform BindingRoot,
        PllLuggageAuthoring? Expected,
        BiomeId BiomeId)
    {
        if (!ExternalResolved)
            throw new InvalidOperationException(
                $"Biome '{BiomeId}' luggage '{Id}' external contents were not sealed.");
        var Value = AuthoringBinding.Resolve(BindingRoot, BiomeId, "luggage authoring");
        if (Expected is not null && !ReferenceEquals(Value, Expected))
            throw new InvalidOperationException(
                $"Biome '{BiomeId}' generated luggage '{Id}' changed its runtime binding.");
        var Native = LuggageBinding.Resolve(BindingRoot, BiomeId, "PEAK luggage");
        var View = ViewBinding.Resolve(BindingRoot, BiomeId, "luggage PhotonView");
        var Animator = AnimatorBinding.Resolve(BindingRoot, BiomeId, "luggage Animator");
        var FixedOwner = FixedOwnerBinding?.ResolveValidated(
            BindingRoot, BiomeId.Value, "luggage feature root");
        var ActivateOnOpen = ActivateOnOpenBinding?.Resolve(
            BindingRoot, BiomeId, "luggage open presentation");
        var ReplacesAuthored = ReplacesAuthoredBinding?.Resolve(
            BindingRoot, BiomeId, "luggage authored replacement");
        ValidateResolved(
            Value, Native, View, Animator, FixedOwner,
            ActivateOnOpen, ReplacesAuthored, BiomeId);
        var Items = new ResolvedLuggageItem[CustomItems.Length];
        for (var Index = 0; Index < Items.Length; Index++)
            Items[Index] = CustomItems[Index].Resolve(BiomeId, Id);
        return new ResolvedLuggage(
            Value, Native, Value.gameObject, Generated, SpawnChance,
            ReplacesAuthored, Items, ResolvedContents,
            NativeReplacementFingerprint);
    }

    internal void Validate(
        Transform Template,
        BiomeId BiomeId,
        AuthoringCandidateGraph Graph)
    {
        var BindingRoot = Generated ? Source.transform : Template;
        AuthoringBinding.Validate(BindingRoot, BiomeId, "luggage authoring");
        LuggageBinding.Validate(BindingRoot, BiomeId, "PEAK luggage");
        ViewBinding.Validate(BindingRoot, BiomeId, "luggage PhotonView");
        AnimatorBinding.Validate(BindingRoot, BiomeId, "luggage Animator");
        FixedOwnerBinding?.Validate(Template, BiomeId.Value, "luggage feature root");
        ActivateOnOpenBinding?.Validate(BindingRoot, BiomeId, "luggage open presentation");
        ReplacesAuthoredBinding?.Validate(Template, BiomeId, "luggage authored replacement");
        if (Graph.Contains(Source.gameObject) != Generated || ViewBinding.Source.ViewID != 0)
            throw new InvalidOperationException(
                $"Biome '{BiomeId}' luggage '{Id}' changed its placement or source PhotonView before sealing.");
        ValidateResolved(
            Source, LuggageBinding.Source, ViewBinding.Source,
            AnimatorBinding.Source, FixedOwnerBinding?.Source,
            ActivateOnOpenBinding?.Source, ReplacesAuthoredBinding?.Source, BiomeId);
    }

    void ValidateResolved(
        PllLuggageAuthoring Value,
        global::Luggage Native,
        PhotonView View,
        Animator Animator,
        Transform? FixedOwner,
        GameObject? ActivateOnOpen,
        PllLuggageAuthoring? ReplacesAuthored,
        BiomeId BiomeId)
    {
        if (!Value.enabled || Value.GetComponents<PllLuggageAuthoring>().Length != 1
            || Value.Id != Id || Value.DisplayName != DisplayName
            || Value.ContentsMode != ContentsMode || Value.RepeatMode != RepeatMode
            || Value.NativeContentsId != NativeContentsId
            || Value.ReplacesNativeId != ReplacesNativeId
            || Value.ReplacesAuthored != ReplacesAuthored
            || !Value.OpenTime.Equals(OpenTime) || !Value.SpawnChance.Equals(SpawnChance)
            || Value.AnimatorController != AuthoredAnimatorController
            || Value.ActivateOnOpen != ActivateOnOpen
            || Value.SpawnGroups.Count != SpawnGroups.Length
            || Value.CustomItems.Count != CustomItems.Length
            || Value.gameObject.activeSelf != Generated
            || Native.gameObject != Value.gameObject || View.gameObject != Value.gameObject
            || Animator.gameObject != Value.gameObject || !Native.enabled || !View.enabled
            || !Animator.enabled || Animator.runtimeAnimatorController != RuntimeAnimatorController
            || Value.gameObject.GetComponentsInChildren<global::Luggage>(true).Length != 1
            || Value.gameObject.GetComponentsInChildren<PhotonView>(true).Length != 1
            || Value.gameObject.GetComponentsInChildren<Animator>(true).Length != 1
            || Value.gameObject.GetComponentsInChildren<Item>(true).Length != 0
            || Value.gameObject.GetComponentsInChildren<global::Campfire>(true).Length != 0
            || Value.gameObject.GetComponentsInChildren<RespawnChest>(true).Length != 0
            || !ValidPlacement(Value.transform, FixedOwner)
            || !ValidRootPresentation(Value.transform, ActivateOnOpen ? ActivateOnOpen.transform : null)
            || !ValidActivateOnOpen(Value, ActivateOnOpen)
            || !PllStagedLuggageShapeContract.Matches(
                Native, Value, static Group => Group.percentageOdds))
            throw new InvalidOperationException(
                $"Biome '{BiomeId}' luggage '{Id}' does not match its registered contract.");
        for (var Index = 0; Index < SpawnGroups.Length; Index++)
            SpawnGroups[Index].Validate(
                Value.SpawnGroups[Index], Value.transform,
                ActivateOnOpen ? ActivateOnOpen.transform : null, BiomeId, Id);
        for (var Index = 0; Index < CustomItems.Length; Index++)
            CustomItems[Index].Validate(Value.CustomItems[Index], BiomeId, Id);
    }

    internal void WriteManifest(BinaryWriter Writer) => WriteManifest(Writer, true);
    internal void WriteSourceManifest(BinaryWriter Writer) => WriteManifest(Writer, false);

    void WriteManifest(BinaryWriter Writer, bool External)
    {
        Writer.Write((byte)Placement);
        AuthoringBinding.WriteManifest(Writer);
        LuggageBinding.WriteManifest(Writer);
        ViewBinding.WriteManifest(Writer);
        AnimatorBinding.WriteManifest(Writer);
        Writer.Write(FixedOwnerBinding is not null);
        FixedOwnerBinding?.WriteManifest(Writer);

        Writer.Write(Id);
        Writer.Write(DisplayName);
        Writer.Write(SpawnGroups.Length);
        foreach (var Group in SpawnGroups) Group.WriteManifest(Writer);
        Writer.Write(AuthoredAnimatorController is not null);
        if (AuthoredAnimatorController is not null) Writer.Write(AuthoredAnimatorController.name);
        Writer.Write(RuntimeAnimatorController.name);
        ManifestEncoding.WriteScalar(Writer, OpenTime);
        Writer.Write(ActivateOnOpenBinding is not null);
        ActivateOnOpenBinding?.WriteManifest(Writer);
        Writer.Write((byte)ContentsMode);
        Writer.Write(CustomItems.Length);
        foreach (var Item in CustomItems) Item.WriteManifest(Writer);
        Writer.Write(NativeContentsId);
        Writer.Write((byte)RepeatMode);
        ManifestEncoding.WriteScalar(Writer, SpawnChance);
        Writer.Write(ReplacesAuthoredBinding is not null);
        ReplacesAuthoredBinding?.WriteManifest(Writer);
        Writer.Write(ReplacesNativeId);

        Writer.Write(External);
        if (!External) return;
        if (!ExternalResolved)
            throw new InvalidOperationException(
                $"Luggage '{Id}' external contents were not sealed.");
        Writer.Write(ResolvedContents is not null);
        ResolvedContents?.Fingerprint.WriteManifest(Writer);
        Writer.Write(NativeReplacementFingerprint is not null);
        NativeReplacementFingerprint?.WriteManifest(Writer);
    }

    bool ValidPlacement(Transform Root, Transform? FixedOwner)
    {
        if (Generated)
            return FixedOwner is null && Root.gameObject.activeSelf
                && SpawnChance.Equals(1f)
                && ReplacesAuthoredBinding is null && ReplacesNativeId.Length == 0;
        if (!FixedOwner || Root.gameObject.activeSelf
            || !TransformOwnership.ParentDiscoverable(FixedOwner, Root)
            || TransformOwnership.HasPropSpawnerInPath(FixedOwner, Root)
            || TransformOwnership.HasConfigurationBranch(FixedOwner, Root)) return false;
        return Placement == LuggagePlacement.FixedCampfire;
    }

    static bool ValidRootPresentation(Transform Root, Transform? Excluded)
    {
        var RendererFound = false;
        foreach (var Renderer in Root.GetComponentsInChildren<Renderer>(true))
            if (Renderer && Renderer.enabled
                && (Excluded is null || !TransformOwnership.Contains(Excluded, Renderer.transform))
                && FinalForceAuthoringContract.ActiveBelow(Renderer.transform, Root))
            {
                RendererFound = true;
                break;
            }
        if (!RendererFound) return false;
        foreach (var Collider in Root.GetComponentsInChildren<Collider>(true))
            if (Collider && Collider.enabled && !Collider.isTrigger
                && (Excluded is null || !TransformOwnership.Contains(Excluded, Collider.transform))
                && FinalForceAuthoringContract.ActiveBelow(Collider.transform, Root)) return true;
        return false;
    }

    static bool ValidActivateOnOpen(
        PllLuggageAuthoring Source,
        GameObject? ActivateOnOpen)
    {
        if (!ActivateOnOpen) return true;
        var Root = Source.transform;
        var Activate = ActivateOnOpen.transform;
        if (Activate == Root || !TransformOwnership.Contains(Root, Activate)
            || ActivateOnOpen.activeSelf || !Activate.parent
            || !FinalForceAuthoringContract.ActiveBelow(Activate.parent, Root)) return false;
        foreach (var Group in Source.SpawnGroups)
            foreach (var Point in Group.Points)
                if (Point && TransformOwnership.Overlaps(Activate, Point)) return false;
        foreach (var Component in ActivateOnOpen.GetComponentsInChildren<Component>(true))
        {
            if (!Component) return false;
            if (Component is Transform or MeshFilter or Renderer or Light or LODGroup) continue;
            if (Component is ParticleSystem Particle)
            {
                if (Particle.main.loop) return false;
                continue;
            }
            if (Component is AudioSource Audio)
            {
                if (!Audio.clip || Audio.loop) return false;
                continue;
            }
            return false;
        }
        return true;
    }



}

internal sealed class LuggageSpawnGroupContract
{
    readonly TransformBinding[] Points;

    LuggageSpawnGroupContract(int Weight, TransformBinding[] Points)
    {
        this.Weight = Weight;
        this.Points = Points;
    }

    internal int Weight { get; }

    internal static LuggageSpawnGroupContract[] CreateAll(
        PllLuggageAuthoring Source,
        Transform Root,
        Transform? ActivateOnOpen,
        BiomeId BiomeId)
    {
        if (Source.SpawnGroups.Count == 0)
            throw new ArgumentException(
                $"Biome '{BiomeId}' luggage '{Source.Id}' has no spawn groups.", "source");
        var Result = new LuggageSpawnGroupContract[Source.SpawnGroups.Count];
        long WeightSum = 0;
        for (var Index = 0; Index < Result.Length; Index++)
        {
            var Group = Source.SpawnGroups[Index];
            if (Group is null || Group.Weight < 1 || Group.Points.Count == 0)
                throw new ArgumentException(
                    $"Biome '{BiomeId}' luggage '{Source.Id}' has an invalid spawn group.", "source");
            WeightSum += Group.Weight;
            if (WeightSum > int.MaxValue)
                throw new ArgumentException(
                    $"Biome '{BiomeId}' luggage '{Source.Id}' spawn-group weight exceeds Int32 capacity.",
                    "source");
            var Points = new TransformBinding[Group.Points.Count];
            var Seen = new HashSet<Transform>();
            for (var PointIndex = 0; PointIndex < Points.Length; PointIndex++)
            {
                var Point = Group.Points[PointIndex];
                if (!Point || !Seen.Add(Point) || Point == Root
                    || !TransformOwnership.Contains(Root, Point)
                    || !FinalForceAuthoringContract.ActiveBelow(Point, Root)
                    || TransformOwnership.HasPropSpawnerInPath(Root, Point)
                    || ActivateOnOpen is not null
                        && TransformOwnership.Overlaps(ActivateOnOpen, Point))
                    throw new ArgumentException(
                        $"Biome '{BiomeId}' luggage '{Source.Id}' has a missing, duplicate, inactive, or unowned spawn point.",
                        "source");
                Points[PointIndex] = TransformBinding.Create(Root, Point);
            }
            Result[Index] = new LuggageSpawnGroupContract(Group.Weight, Points);
        }
        return Result;
    }

    internal void Validate(
        PllLuggageSpawnGroup Group,
        Transform LuggageRoot,
        Transform? ActivateOnOpen,
        BiomeId BiomeId,
        string Id)
    {
        if (Group is null || Group.Weight != Weight || Group.Points.Count != Points.Length)
            throw new InvalidOperationException(
                $"Biome '{BiomeId}' luggage '{Id}' changed its spawn-group shape.");
        var Seen = new HashSet<Transform>();
        for (var Index = 0; Index < Points.Length; Index++)
        {
            var Point = Points[Index].ResolveValidated(
                LuggageRoot, BiomeId.Value, "luggage spawn point");
            if (Group.Points[Index] != Point || !Seen.Add(Point)
                || Point == LuggageRoot
                || !FinalForceAuthoringContract.ActiveBelow(Point, LuggageRoot)
                || TransformOwnership.HasPropSpawnerInPath(LuggageRoot, Point)
                || ActivateOnOpen is not null
                    && TransformOwnership.Overlaps(ActivateOnOpen, Point))
                throw new InvalidOperationException(
                    $"Biome '{BiomeId}' luggage '{Id}' changed its spawn points.");
        }
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Weight);
        Writer.Write(Points.Length);
        foreach (var Point in Points) Point.WriteManifest(Writer);
    }
}

internal sealed class WeightedLuggageItemContract
{
    readonly ItemContract Item;

    WeightedLuggageItemContract(
        ItemContract Item,
        int Weight)
    {
        this.Item = Item;
        this.Weight = Weight;
    }

    internal int Weight { get; }

    internal static WeightedLuggageItemContract[] CreateAll(
        PllLuggageAuthoring Source,
        BiomeId BiomeId)
    {
        var Result = new WeightedLuggageItemContract[Source.CustomItems.Count];
        var Seen = new HashSet<GameObject>();
        long WeightSum = 0;
        for (var Index = 0; Index < Result.Length; Index++)
        {
            var Entry = Source.CustomItems[Index];
            if (Entry is null || !Entry.Item || !Seen.Add(Entry.Item) || Entry.Weight < 1)
                throw new ArgumentException(
                    $"Biome '{BiomeId}' luggage '{Source.Id}' has a missing, duplicate, or invalid custom item.",
                    "source");
            WeightSum += Entry.Weight;
            if (WeightSum > int.MaxValue)
                throw new ArgumentException(
                    $"Biome '{BiomeId}' luggage '{Source.Id}' custom-item weight exceeds Int32 capacity.",
                    "source");
            Result[Index] = new WeightedLuggageItemContract(
                ItemContract.Create(Entry.Item, BiomeId, $"luggage '{Source.Id}'"),
                Entry.Weight);
        }
        return Result;
    }

    internal void ResolveExternal(
        ItemDatabaseCatalog Catalog,
        BiomeId BiomeId,
        string Id) => Item.ResolveDatabase(Catalog, BiomeId, $"luggage '{Id}'");

    internal ResolvedLuggageItem Resolve(BiomeId BiomeId, string Id) =>
        new(Item.ResolveRuntime(BiomeId, $"luggage '{Id}'"), Weight);

    internal GameObject ResolveRuntime(BiomeId BiomeId, string Id) =>
        Item.ResolveRuntime(BiomeId, $"luggage '{Id}'");

    internal void ClearExternal() => Item.ClearExternal();

    internal void Validate(
        PllLuggageItemEntry Entry,
        BiomeId BiomeId,
        string Id)
    {
        Item.ValidateSource(BiomeId, $"luggage '{Id}'");
        if (Entry is null || Entry.Item != Item.Source || Entry.Weight != Weight)
            throw new InvalidOperationException(
                $"Biome '{BiomeId}' luggage '{Id}' changed its custom items.");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Weight);
        Item.WriteManifest(Writer);
    }
}

internal readonly struct ResolvedLuggageItem
{
    internal ResolvedLuggageItem(GameObject Item, int Weight)
    {
        this.Item = Item;
        this.Weight = Weight;
    }

    internal GameObject Item { get; }
    internal int Weight { get; }
}

internal readonly struct ResolvedLuggage
{
    internal ResolvedLuggage(
        PllLuggageAuthoring Authoring,
        global::Luggage NativeLuggage,
        GameObject Root,
        bool Generated,
        float SpawnChance,
        PllLuggageAuthoring? ReplacesAuthored,
        ResolvedLuggageItem[] CustomItems,
        NativeLuggageContents? Contents,
        NativeLuggageFingerprint? NativeReplacementFingerprint)
    {
        this.Authoring = Authoring;
        Luggage = NativeLuggage;
        this.Root = Root;
        this.Generated = Generated;
        this.SpawnChance = SpawnChance;
        this.ReplacesAuthored = ReplacesAuthored;
        this.CustomItems = CustomItems;
        this.Contents = Contents;
        this.NativeReplacementFingerprint = NativeReplacementFingerprint;
    }

    internal PllLuggageAuthoring Authoring { get; }
    internal global::Luggage Luggage { get; }
    internal GameObject Root { get; }
    internal bool Generated { get; }
    internal float SpawnChance { get; }
    internal PllLuggageAuthoring? ReplacesAuthored { get; }
    internal ResolvedLuggageItem[] CustomItems { get; }
    internal NativeLuggageContents? Contents { get; }
    internal NativeLuggageFingerprint? NativeReplacementFingerprint { get; }
}
