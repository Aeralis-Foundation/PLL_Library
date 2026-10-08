using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class ClimbSurfaceAuthoringContract
{
    const int TerrainLayer = 20;
    const int MapLayer = 21;
    const int TerrainMapMask = (1 << TerrainLayer) | (1 << MapLayer);
    static readonly bool ProviderSealed = SealInstalledProvider();
    readonly DefinitionKind Kind;
    readonly AuthoringBindingOwner Owner;
    readonly ComponentBinding<PllClimbSurfaceAuthoring>? ObjectAuthoring;
    readonly ComponentBinding<PllClimbSurfaceMaterialAuthoring>? MaterialAuthoring;
    readonly int MaterialRuleIndex;
    readonly int MaterialRuleCount;
    readonly PhysicsMaterial? Material;
    readonly PhysicsMaterialIdentity MaterialIdentity;
    readonly PllClimbSurfaceMode Mode;
    readonly string Hook;
    readonly float StaminaUsageMultiplier;
    readonly float SpeedMultiplier;
    readonly TargetBinding[] Targets;

    enum DefinitionKind : byte
    {
        Object,
        Material
    }

    ClimbSurfaceAuthoringContract(
        DefinitionKind Kind,
        AuthoringBindingOwner Owner,
        ComponentBinding<PllClimbSurfaceAuthoring>? ObjectAuthoring,
        ComponentBinding<PllClimbSurfaceMaterialAuthoring>? MaterialAuthoring,
        int MaterialRuleIndex,
        int MaterialRuleCount,
        PhysicsMaterial? Material,
        PhysicsMaterialIdentity MaterialIdentity,
        PllClimbSurfaceMode Mode,
        string Hook,
        float StaminaUsageMultiplier,
        float SpeedMultiplier,
        TargetBinding[] Targets)
    {
        this.Kind = Kind;
        this.Owner = Owner;
        this.ObjectAuthoring = ObjectAuthoring;
        this.MaterialAuthoring = MaterialAuthoring;
        this.MaterialRuleIndex = MaterialRuleIndex;
        this.MaterialRuleCount = MaterialRuleCount;
        this.Material = Material;
        this.MaterialIdentity = MaterialIdentity;
        this.Mode = Mode;
        this.Hook = Hook;
        this.StaminaUsageMultiplier = StaminaUsageMultiplier;
        this.SpeedMultiplier = SpeedMultiplier;
        this.Targets = Targets;
    }

    internal static void SealProvider() => _ = ProviderSealed;

    internal static void ValidateRuntimeMultiplier(
        float Value, string Parameter)
    {
        if (!ValidMultiplier(Value))
            throw new ArgumentOutOfRangeException(
                Parameter, Value,
                "A climb multiplier must be finite, nonnegative, and exactly synchronizable.");
    }

    internal static ClimbSurfaceAuthoringContract[] CreateAll(
        Transform Template, Transform Floor, Transform Campfire,
        BiomeId Id, AuthoringCandidateGraph Graph)
    {
        var ObjectEntries = AuthoringBindingOwner.GraphComponents<PllClimbSurfaceAuthoring>(
            Template, Id, Graph, "climb-surface authoring");
        var MaterialEntries = AuthoringBindingOwner.GraphComponents<PllClimbSurfaceMaterialAuthoring>(
            Template, Id, Graph, "climb-surface material authoring");
        if (MaterialEntries.Count > 1 || MaterialEntries.Count == 1
            && (MaterialEntries[0].Owner.Generated
                || MaterialEntries[0].Value.transform != Template))
            throw new ArgumentException(
                $"Biome '{Id}' climb-surface material authoring must be unique and direct on the root.",
                nameof(Template));

        var ObjectsByTarget = new Dictionary<GameObject, int>();
        for (var Index = 0; Index < ObjectEntries.Count; Index++)
        {
            var Entry = ObjectEntries[Index];
            ValidateObjectSource(Entry.Owner, Entry.Value, Floor, Campfire, Id);
            if (!ObjectsByTarget.TryAdd(Entry.Value.gameObject, Index))
                throw Invalid(Id, Entry.Value.gameObject,
                    "contains multiple object rules on one GameObject");
        }

        PllClimbSurfaceMaterialAuthoring? MaterialCarrier = null;
        IReadOnlyList<PllClimbSurfaceMaterialRule> MaterialRules =
            Array.Empty<PllClimbSurfaceMaterialRule>();
        Dictionary<PhysicsMaterial, int>? MaterialIndices = null;
        PhysicsMaterialIdentity[] MaterialIdentities =
            Array.Empty<PhysicsMaterialIdentity>();
        if (MaterialEntries.Count != 0)
        {
            MaterialCarrier = MaterialEntries[0].Value;
            if (!MaterialCarrier.enabled
                || MaterialCarrier.GetComponents<PllClimbSurfaceMaterialAuthoring>().Length != 1)
                throw Invalid(Id, MaterialCarrier.gameObject,
                    "has a disabled or duplicate material-rule carrier");
            MaterialRules = MaterialCarrier.Rules;
            if (MaterialRules.Count == 0)
                throw Invalid(Id, MaterialCarrier.gameObject, "has no material rules");
            MaterialIndices = new Dictionary<PhysicsMaterial, int>(MaterialRules.Count);
            MaterialIdentities = new PhysicsMaterialIdentity[MaterialRules.Count];
            for (var Index = 0; Index < MaterialRules.Count; Index++)
            {
                var Rule = MaterialRules[Index];
                if (Rule is null || !Rule.PhysicsMaterial
                    || !MaterialIndices.TryAdd(Rule.PhysicsMaterial, Index))
                    throw Invalid(Id, MaterialCarrier.gameObject,
                        "contains a missing or duplicate PhysicsMaterial rule");
                ValidateValues(
                    Rule.Mode, Rule.EmitHook, Rule.StaminaUsageMultiplier,
                    Rule.SpeedMultiplier, Id, "material rule");
                MaterialIdentities[Index] = PhysicsMaterialIdentity.Capture(
                    Rule.PhysicsMaterial, Id);
            }
        }

        var TargetDrafts = new List<TargetDraft>();
        var DraftByObject = new Dictionary<GameObject, TargetDraft>();
        AddColliderTargets(Template.gameObject);
        foreach (var Candidate in Graph.Ordered) AddColliderTargets(Candidate);

        void AddColliderTargets(GameObject GraphRoot)
        {
            var GraphOwner = AuthoringBindingOwner.Create(
                Template, Graph, GraphRoot.transform, Id,
                "climb-surface Collider graph");
            var Colliders = GraphRoot.GetComponentsInChildren<Collider>(true);
            var Ordered = new (string Key, Collider Value)[Colliders.Length];
            for (var Index = 0; Index < Ordered.Length; Index++)
            {
                if (!Colliders[Index])
                    throw Invalid(Id, GraphRoot,
                        "contains a destroyed Collider");
                Ordered[Index] = (
                    DeterministicObjectIdentity.RelativeComponentPath(
                        Colliders[Index], GraphOwner.BindingRoot, 0),
                    Colliders[Index]);
            }
            Array.Sort(Ordered, (Left, Right) =>
                StringComparer.Ordinal.Compare(Left.Key, Right.Key));
            foreach (var Entry in Ordered)
            {
                var Collider = Entry.Value;
                if (!Eligible(Collider)
                    || !ValidOwnership(
                        GraphOwner, Collider.transform,
                        Floor, Campfire)) continue;
                if (!DraftByObject.TryGetValue(
                        Collider.gameObject, out var Draft))
                {
                    Draft = new TargetDraft(GraphOwner, Collider.gameObject);
                    DraftByObject.Add(Collider.gameObject, Draft);
                    TargetDrafts.Add(Draft);
                }
                Draft.Colliders.Add(Collider);
            }
        }

        var UsedMaterialRules = new bool[MaterialRules.Count];
        foreach (var Draft in TargetDrafts)
        {
            Draft.ObjectDefinition = ObjectsByTarget.TryGetValue(
                Draft.Target, out var ObjectIndex) ? ObjectIndex : -1;
            var AlwaysObject = Draft.ObjectDefinition >= 0
                && ObjectEntries[Draft.ObjectDefinition].Value.Mode
                    == PllClimbSurfaceMode.Always;
            if (!AlwaysObject && MaterialIndices is not null)
            {
                var ResolvedRule = -1;
                var Matched = 0;
                foreach (var Collider in Draft.Colliders)
                {
                    if (!Collider.sharedMaterial
                        || !MaterialIndices.TryGetValue(
                            Collider.sharedMaterial, out var RuleIndex)) continue;
                    if (ResolvedRule >= 0 && ResolvedRule != RuleIndex)
                        throw Invalid(Id, Draft.Target,
                            "mixes PhysicsMaterial rules on one fallback GameObject");
                    ResolvedRule = RuleIndex;
                    Matched++;
                }
                if (Matched != 0 && Matched != Draft.Colliders.Count)
                    throw Invalid(Id, Draft.Target,
                        "mixes matched and unmatched Colliders on one fallback GameObject");
                Draft.MaterialDefinition = ResolvedRule;
                if (ResolvedRule >= 0) UsedMaterialRules[ResolvedRule] = true;
            }
        }
        for (var Index = 0; Index < UsedMaterialRules.Length; Index++)
            if (!UsedMaterialRules[Index])
                throw Invalid(Id, MaterialCarrier!.gameObject,
                    $"material rule {Index} has no eligible fallback target");

        foreach (var Pair in ObjectsByTarget)
            if (!DraftByObject.ContainsKey(Pair.Key))
                throw Invalid(Id, Pair.Key,
                    "has no enabled non-trigger Collider on Terrain or Map layer");

        var NativeByObject = new Dictionary<GameObject, ClimbModifierSurface>();
        AddNativeSurfaces(Template.gameObject);
        foreach (var Candidate in Graph.Ordered) AddNativeSurfaces(Candidate);

        void AddNativeSurfaces(GameObject GraphRoot)
        {
            foreach (var Surface in GraphRoot.GetComponentsInChildren<ClimbModifierSurface>(true))
                if (!Surface
                    || !NativeByObject.TryAdd(
                        Surface.gameObject, Surface))
                    throw Invalid(Id, GraphRoot,
                        "contains a missing or duplicate native climb surface");
        }

        var BindingByObject = new Dictionary<GameObject, TargetBinding>();
        foreach (var Draft in TargetDrafts)
        {
            if (Draft.ObjectDefinition < 0 && Draft.MaterialDefinition < 0) continue;
            if (!NativeByObject.Remove(Draft.Target, out var Surface))
                throw Invalid(Id, Draft.Target,
                    "has no paired native climb surface");
            BindingByObject.Add(
                Draft.Target, TargetBinding.Create(Draft, Surface, Id));
        }
        if (NativeByObject.Count != 0)
            foreach (var Pair in NativeByObject)
                throw Invalid(Id, Pair.Key,
                    "contains an unowned native climb surface");
        var Contracts = new ClimbSurfaceAuthoringContract[
            ObjectEntries.Count + MaterialRules.Count];
        for (var Index = 0; Index < ObjectEntries.Count; Index++)
        {
            var Entry = ObjectEntries[Index];
            var Source = Entry.Value;
            Contracts[Index] = new ClimbSurfaceAuthoringContract(
                DefinitionKind.Object, Entry.Owner,
                ComponentBinding<PllClimbSurfaceAuthoring>.Create(
                    Entry.Owner.BindingRoot, Source),
                null, -1, 0, null, default,
                Source.Mode, Source.EmitHook, Source.StaminaUsageMultiplier,
                Source.SpeedMultiplier,
                new[] { BindingByObject[Source.gameObject] });
        }
        if (MaterialCarrier is not null)
        {
            var Entry = MaterialEntries[0];
            var MaterialBinding = ComponentBinding<PllClimbSurfaceMaterialAuthoring>.Create(
                Entry.Owner.BindingRoot, MaterialCarrier);
            for (var RuleIndex = 0; RuleIndex < MaterialRules.Count; RuleIndex++)
            {
                var Rule = MaterialRules[RuleIndex];
                var RuleTargets = new List<TargetBinding>();
                foreach (var Draft in TargetDrafts)
                    if (Draft.MaterialDefinition == RuleIndex)
                        RuleTargets.Add(BindingByObject[Draft.Target]);
                Contracts[ObjectEntries.Count + RuleIndex] =
                    new ClimbSurfaceAuthoringContract(
                        DefinitionKind.Material, Entry.Owner, null,
                        MaterialBinding, RuleIndex, MaterialRules.Count,
                        Rule.PhysicsMaterial, MaterialIdentities[RuleIndex],
                        Rule.Mode, Rule.EmitHook,
                        Rule.StaminaUsageMultiplier, Rule.SpeedMultiplier,
                        RuleTargets.ToArray());
            }
        }
        return Contracts;
    }

    internal static void ValidateAll(
        IReadOnlyList<ClimbSurfaceAuthoringContract> Contracts,
        Transform Template, Transform Floor, Transform Campfire,
        BiomeId Id, AuthoringCandidateGraph Graph)
    {
        ClimbSurfaceAuthoringContract[] Current;
        try
        {
            Current = CreateAll(Template, Floor, Campfire, Id, Graph);
        }
        catch (Exception Failure) when (Failure is ArgumentException
            or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Biome '{Id}' changed its climb-surface catalog before sealing.",
                Failure);
        }
        if (Current.Length != Contracts.Count)
            throw Changed(Id, "changed its definition count");
        for (var Index = 0; Index < Current.Length; Index++)
            if (!Contracts[Index].SameSource(Current[Index]))
                throw Changed(Id, "changed component identity or values");
    }

    internal static ResolvedClimbSurfaceCatalog ResolveAll(
        IReadOnlyList<ClimbSurfaceAuthoringContract> Contracts,
        Transform RuntimeRoot, MapSlot Slot, BiomeId Id)
    {
        if (!RuntimeRoot || Slot.Released)
            throw new InvalidOperationException(
                $"Biome '{Id}' climb-surface runtime root is unavailable.");
        var CandidateSurfaces = new List<ClimbModifierSurface>();
        var SeenSurfaces = new HashSet<int>();
        if (Slot.TerrainReady) AddCandidates(Slot.Segment.segmentParent);
        if (Slot.CampfireReady) AddCandidates(Slot.Segment.segmentCampfire);

        PllClimbSurfaceMaterialAuthoring? MaterialCarrier = null;
        var Definitions = new ResolvedClimbSurfaceDefinition[Contracts.Count];
        var TargetDrafts = new List<ResolvedTargetDraft>();
        var TargetIndices = new Dictionary<ClimbModifierSurface, int>();
        for (var DefinitionIndex = 0;
            DefinitionIndex < Contracts.Count; DefinitionIndex++)
        {
            var Contract = Contracts[DefinitionIndex];
            PllClimbSurfaceMaterialRule? RuntimeMaterialRule = null;
            if (Contract.Kind == DefinitionKind.Material)
            {
                MaterialCarrier ??= Contract.MaterialAuthoring!.Resolve(
                    RuntimeRoot, Id, "climb-surface material authoring");
                RuntimeMaterialRule = Contract.ValidateMaterialCarrier(
                    MaterialCarrier, RuntimeRoot, Id);
            }
            Definitions[DefinitionIndex] = new ResolvedClimbSurfaceDefinition(
                Contract.Mode, Contract.Hook, Contract.StaminaUsageMultiplier,
                Contract.SpeedMultiplier);
            foreach (var Target in Contract.Targets)
            {
                if (!Target.Owner.Generated)
                {
                    var Resolved = Target.ResolveStable(RuntimeRoot, Id);
                    Contract.ValidateCarrier(
                        Resolved, RuntimeRoot, RuntimeMaterialRule, Id);
                    AddResolved(Resolved, Contract.Kind, DefinitionIndex);
                    continue;
                }
                foreach (var Candidate in CandidateSurfaces)
                {
                    if (!Target.TryResolveGenerated(Candidate, Id, out var Resolved))
                        continue;
                    Contract.ValidateCarrier(
                        Resolved, RuntimeRoot, RuntimeMaterialRule, Id);
                    AddResolved(Resolved, Contract.Kind, DefinitionIndex);
                }
            }
        }

        var Targets = new ResolvedClimbSurfaceTarget[TargetDrafts.Count];
        for (var Index = 0; Index < Targets.Length; Index++)
        {
            var Draft = TargetDrafts[Index];
            Targets[Index] = new ResolvedClimbSurfaceTarget(
                Draft.Surface, Draft.ObjectDefinition,
                Draft.MaterialDefinition);
        }
        return new ResolvedClimbSurfaceCatalog(Definitions, Targets);

        void AddCandidates(GameObject? FeatureRoot)
        {
            if (!FeatureRoot) return;
            foreach (var Surface in FeatureRoot.GetComponentsInChildren<ClimbModifierSurface>(true))
                if (Surface && SeenSurfaces.Add(Surface.GetInstanceID()))
                    CandidateSurfaces.Add(Surface);
        }

        void AddResolved(
            ResolvedTargetBinding Resolved, DefinitionKind ResolvedKind,
            int DefinitionIndex)
        {
            if (!TargetIndices.TryGetValue(Resolved.Surface, out var TargetIndex))
            {
                TargetIndex = TargetDrafts.Count;
                TargetIndices.Add(Resolved.Surface, TargetIndex);
                TargetDrafts.Add(new ResolvedTargetDraft(Resolved.Surface));
            }
            var Draft = TargetDrafts[TargetIndex];
            if (ResolvedKind == DefinitionKind.Object)
            {
                if (Draft.ObjectDefinition >= 0
                    && Draft.ObjectDefinition != DefinitionIndex)
                    throw new InvalidOperationException(
                        $"Biome '{Id}' runtime climb target has multiple object rules.");
                Draft.ObjectDefinition = DefinitionIndex;
            }
            else
            {
                if (Draft.MaterialDefinition >= 0
                    && Draft.MaterialDefinition != DefinitionIndex)
                    throw new InvalidOperationException(
                        $"Biome '{Id}' runtime climb target has multiple material rules.");
                Draft.MaterialDefinition = DefinitionIndex;
            }
        }
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write((byte)Kind);
        Owner.WriteManifest(Writer);
        Writer.Write(ObjectAuthoring is not null);
        ObjectAuthoring?.WriteManifest(Writer);
        Writer.Write(MaterialAuthoring is not null);
        MaterialAuthoring?.WriteManifest(Writer);
        Writer.Write(MaterialRuleIndex);
        Writer.Write(MaterialRuleCount);
        Writer.Write(Mode == PllClimbSurfaceMode.EmitHook);
        Writer.Write(Hook);
        ManifestEncoding.WriteScalar(Writer, StaminaUsageMultiplier);
        ManifestEncoding.WriteScalar(Writer, SpeedMultiplier);
        Writer.Write(Material is not null);
        if (Material is not null) MaterialIdentity.WriteManifest(Writer);
        Writer.Write(Targets.Length);
        foreach (var Target in Targets) Target.WriteManifest(Writer);
    }

    bool SameSource(ClimbSurfaceAuthoringContract Other)
    {
        if (Kind != Other.Kind
            || !ReferenceEquals(Owner.SourceRoot, Other.Owner.SourceRoot)
            || !ReferenceEquals(ObjectAuthoring?.Source, Other.ObjectAuthoring?.Source)
            || !ReferenceEquals(MaterialAuthoring?.Source, Other.MaterialAuthoring?.Source)
            || MaterialRuleIndex != Other.MaterialRuleIndex
            || MaterialRuleCount != Other.MaterialRuleCount
            || !ReferenceEquals(Material, Other.Material)
            || !MaterialIdentity.Equals(Other.MaterialIdentity)
            || Mode != Other.Mode
            || !string.Equals(Hook, Other.Hook, StringComparison.Ordinal)
            || !StaminaUsageMultiplier.Equals(Other.StaminaUsageMultiplier)
            || !SpeedMultiplier.Equals(Other.SpeedMultiplier)
            || Targets.Length != Other.Targets.Length) return false;
        for (var Index = 0; Index < Targets.Length; Index++)
            if (!Targets[Index].SameSource(Other.Targets[Index])) return false;
        return true;
    }
    PllClimbSurfaceMaterialRule ValidateMaterialCarrier(
        PllClimbSurfaceMaterialAuthoring Carrier,
        Transform RuntimeRoot, BiomeId Id)
    {
        if (!Carrier.enabled
            || Carrier.GetComponents<PllClimbSurfaceMaterialAuthoring>().Length != 1
            || Carrier.transform != RuntimeRoot
            || Carrier.Rules.Count != MaterialRuleCount)
            throw Changed(Id, "changed its runtime material carrier");
        var Rule = Carrier.Rules[MaterialRuleIndex];
        if (Rule is null || !ReferenceEquals(Rule.PhysicsMaterial, Material)
            || !MaterialIdentity.Equals(
                PhysicsMaterialIdentity.Capture(Rule.PhysicsMaterial, Id))
            || Rule.Mode != Mode
            || !string.Equals(Rule.EmitHook, Hook, StringComparison.Ordinal)
            || !Rule.StaminaUsageMultiplier.Equals(StaminaUsageMultiplier)
            || !Rule.SpeedMultiplier.Equals(SpeedMultiplier))
            throw Changed(Id, "changed a runtime material rule");
        ValidateValues(
            Rule.Mode, Rule.EmitHook, Rule.StaminaUsageMultiplier,
            Rule.SpeedMultiplier, Id, "runtime material rule");
        return Rule;
    }

    void ValidateCarrier(
        ResolvedTargetBinding Target,
        Transform RuntimeRoot,
        PllClimbSurfaceMaterialRule? RuntimeMaterialRule,
        BiomeId Id)
    {
        if (Kind == DefinitionKind.Object)
        {
            var CarrierRoot = Target.GeneratedRoot ?? RuntimeRoot;
            var Carrier = ObjectAuthoring!.Resolve(
                CarrierRoot, Id, "climb-surface authoring");
            if (Carrier.gameObject != Target.Surface.gameObject)
                throw Changed(Id, "moved a runtime object carrier from its target");
            ValidateObjectCarrier(Carrier, Id);
            return;
        }
        if (RuntimeMaterialRule is null)
            throw new InvalidOperationException(
                $"Biome '{Id}' runtime material rule was not resolved.");
        foreach (var Collider in Target.Colliders)
            if (!ReferenceEquals(
                    Collider.sharedMaterial,
                    RuntimeMaterialRule.PhysicsMaterial))
                throw Changed(Id, "changed a runtime material target");
    }

    void ValidateObjectCarrier(PllClimbSurfaceAuthoring Carrier, BiomeId Id)
    {
        if (!Carrier.enabled
            || Carrier.GetComponents<PllClimbSurfaceAuthoring>().Length != 1
            || Carrier.Mode != Mode
            || !string.Equals(Carrier.EmitHook, Hook, StringComparison.Ordinal)
            || !Carrier.StaminaUsageMultiplier.Equals(StaminaUsageMultiplier)
            || !Carrier.SpeedMultiplier.Equals(SpeedMultiplier))
            throw Changed(Id, "changed a runtime object rule");
        ValidateValues(
            Carrier.Mode, Carrier.EmitHook,
            Carrier.StaminaUsageMultiplier, Carrier.SpeedMultiplier,
            Id, "runtime object rule");
    }

    static void ValidateObjectSource(
        AuthoringBindingOwner Owner, PllClimbSurfaceAuthoring Source,
        Transform Floor, Transform Campfire, BiomeId Id)
    {
        if (!Source.enabled
            || Source.GetComponents<PllClimbSurfaceAuthoring>().Length != 1
            || !ValidOwnership(
                Owner, Source.transform, Floor, Campfire))
            throw Invalid(Id, Source.gameObject,
                "has invalid ownership or a disabled or duplicate object carrier");
        ValidateValues(
            Source.Mode, Source.EmitHook, Source.StaminaUsageMultiplier,
            Source.SpeedMultiplier, Id, "object rule");
    }

    static void ValidateValues(
        PllClimbSurfaceMode Mode, string Hook,
        float StaminaUsageMultiplier, float SpeedMultiplier,
        BiomeId Id, string Name)
    {
        if (!ValidMultiplier(StaminaUsageMultiplier)
            || !ValidMultiplier(SpeedMultiplier))
            throw new ArgumentException(
                $"Biome '{Id}' climb {Name} multipliers must be finite, nonnegative, and exactly synchronizable.",
                "Template");
        switch (Mode)
        {
            case PllClimbSurfaceMode.Always when Hook.Length == 0:
                return;
            case PllClimbSurfaceMode.EmitHook:
                try
                {
                    HookedPropRuntime.ValidateHook(Hook, nameof(Hook));
                    return;
                }
                catch (ArgumentException Failure)
                {
                    throw new ArgumentException(
                        $"Biome '{Id}' climb {Name} has an invalid emit hook.",
                        "Template", Failure);
                }
            default:
                throw new ArgumentException(
                    $"Biome '{Id}' climb {Name} has an invalid mode or hook pairing.",
                    "Template");
        }
    }

    static bool Eligible(Collider Collider) => Collider && Collider.enabled
        && !Collider.isTrigger
        && Collider.gameObject.layer is TerrainLayer or MapLayer;

    static bool ValidOwnership(
        AuthoringBindingOwner Owner, Transform Value,
        Transform Floor, Transform Campfire)
    {
        if (!Owner.Generated)
            return (TransformOwnership.Contains(Floor, Value)
                    || TransformOwnership.Contains(Campfire, Value))
                && !TransformOwnership.HasPropSpawnerInPath(
                    Owner.BindingRoot, Value);
        if (TransformOwnership.HasPropSpawnerInPath(
                Owner.BindingRoot, Value)) return false;
        for (var Current = Value;
            Current && Current != Owner.BindingRoot; Current = Current.parent)
            if (string.Equals(Current.name, "RuntimeMarkers", StringComparison.Ordinal)
                || string.Equals(Current.name, "Presentation", StringComparison.Ordinal))
                return false;
        return true;
    }

    static bool SealInstalledProvider()
    {
        if (LayerMask.NameToLayer("Terrain") != TerrainLayer
            || LayerMask.NameToLayer("Map") != MapLayer
            || HelperFunctions.terrainMapMask.value != TerrainMapMask)
            throw new InvalidOperationException(
                "PEAK's Terrain and Map layer contract does not match climb-surface authoring.");
        return true;
    }

    static bool ValidMultiplier(float Value) => Value >= 0f
        && ManifestEncoding.CanEncodeExactly(Value);

    static ArgumentException Invalid(
        BiomeId Id, GameObject Target, string Reason) => new(
        $"Biome '{Id}' climb target '{Target.name}' {Reason}.",
        "Template");

    static InvalidOperationException Changed(BiomeId Id, string Reason) =>
        new($"Biome '{Id}' climb-surface catalog {Reason}.");

    sealed class TargetDraft
    {
        internal TargetDraft(AuthoringBindingOwner Owner, GameObject Target)
        {
            this.Owner = Owner;
            this.Target = Target;
        }

        internal AuthoringBindingOwner Owner { get; }
        internal GameObject Target { get; }
        internal List<Collider> Colliders { get; } = new();
        internal int ObjectDefinition { get; set; } = -1;
        internal int MaterialDefinition { get; set; } = -1;
    }

    sealed class TargetBinding
    {
        internal TargetBinding(
            AuthoringBindingOwner Owner,
            ComponentBinding<ClimbModifierSurface> Surface,
            ComponentBinding<Collider>[] Colliders,
            string[] ColliderTypes,
            int Layer)
        {
            this.Owner = Owner;
            this.Surface = Surface;
            this.Colliders = Colliders;
            this.ColliderTypes = ColliderTypes;
            this.Layer = Layer;
        }

        internal AuthoringBindingOwner Owner { get; }
        internal ComponentBinding<ClimbModifierSurface> Surface { get; }
        internal ComponentBinding<Collider>[] Colliders { get; }
        internal string[] ColliderTypes { get; }
        internal int Layer { get; }

        internal static TargetBinding Create(
            TargetDraft Draft, ClimbModifierSurface Surface, BiomeId Id)
        {
            if (!ValidNative(Surface, true))
                throw Invalid(Id, Draft.Target,
                    "has a nonneutral native climb surface");
            var Colliders = new ComponentBinding<Collider>[
                Draft.Colliders.Count];
            var ColliderTypes = new string[Draft.Colliders.Count];
            for (var Index = 0; Index < Colliders.Length; Index++)
            {
                var Collider = Draft.Colliders[Index];
                if (Collider.gameObject != Draft.Target)
                    throw Invalid(Id, Draft.Target,
                        "has foreign Collider coverage");
                Colliders[Index] = ComponentBinding<Collider>.Create(
                    Draft.Owner.BindingRoot, Collider);
                ColliderTypes[Index] = Collider.GetType().FullName
                    ?? Collider.GetType().Name;
            }
            return new TargetBinding(
                Draft.Owner,
                ComponentBinding<ClimbModifierSurface>.Create(
                    Draft.Owner.BindingRoot, Surface),
                Colliders, ColliderTypes, Draft.Target.layer);
        }
        internal ResolvedTargetBinding ResolveStable(
            Transform RuntimeRoot, BiomeId Id)
        {
            if (Owner.Generated)
                throw new InvalidOperationException(
                    $"Biome '{Id}' cannot resolve a generated climb target as stable.");
            return Resolve(RuntimeRoot, null, Id);
        }

        internal bool TryResolveGenerated(
            ClimbModifierSurface Candidate, BiomeId Id,
            out ResolvedTargetBinding Resolved)
        {
            Resolved = default;
            if (!Owner.Generated || !Surface.TryMatchGenerated(
                    Owner, Candidate, Id, "generated climb surface",
                    out var GeneratedRoot)) return false;
            Resolved = Resolve(GeneratedRoot, GeneratedRoot, Id);
            return true;
        }

        internal void WriteManifest(BinaryWriter Writer)
        {
            Owner.WriteManifest(Writer);
            Surface.WriteManifest(Writer);
            Writer.Write(Layer);
            Writer.Write(Colliders.Length);
            for (var Index = 0; Index < Colliders.Length; Index++)
            {
                Colliders[Index].WriteManifest(Writer);
                Writer.Write(ColliderTypes[Index]);
            }
            Writer.Write(false);
            ManifestEncoding.WriteScalar(Writer, 1f);
            ManifestEncoding.WriteScalar(Writer, 0f);
            ManifestEncoding.WriteScalar(Writer, 1f);
            Writer.Write(false);
            Writer.Write((int)default(CharacterAfflictions.STATUSTYPE));
            Writer.Write(false);
            ManifestEncoding.WriteScalar(Writer, 0.5f);
            ManifestEncoding.WriteScalar(Writer, 0.5f);
            Writer.Write(false);
            Writer.Write(false);
            ManifestEncoding.WriteScalar(Writer, 0f);
        }

        internal bool SameSource(TargetBinding Other)
        {
            if (!ReferenceEquals(Owner.SourceRoot, Other.Owner.SourceRoot)
                || !ReferenceEquals(Surface.Source, Other.Surface.Source)
                || Layer != Other.Layer
                || Colliders.Length != Other.Colliders.Length)
                return false;
            for (var Index = 0; Index < Colliders.Length; Index++)
                if (!ReferenceEquals(
                        Colliders[Index].Source,
                        Other.Colliders[Index].Source)
                    || !string.Equals(
                        ColliderTypes[Index], Other.ColliderTypes[Index],
                        StringComparison.Ordinal)) return false;
            return true;
        }

        ResolvedTargetBinding Resolve(
            Transform BindingRoot, Transform? GeneratedRoot, BiomeId Id)
        {
            var ResolvedSurface = Surface.Resolve(
                BindingRoot, Id, "climb surface");
            var ResolvedColliders = new Collider[Colliders.Length];
            for (var Index = 0; Index < ResolvedColliders.Length; Index++)
                ResolvedColliders[Index] = Colliders[Index].Resolve(
                    BindingRoot, Id, "climb-surface Collider");
            if (ResolvedSurface.gameObject.layer != Layer
                || !ValidNative(ResolvedSurface, false)
                || !CollidersMatch(
                    ResolvedSurface, ResolvedColliders,
                    ColliderTypes))
                throw Changed(Id, "changed native target shape at runtime");
            return new ResolvedTargetBinding(
                ResolvedSurface, ResolvedColliders, GeneratedRoot);
        }

        static bool CollidersMatch(
            ClimbModifierSurface Surface,
            IReadOnlyList<Collider> Expected,
            IReadOnlyList<string> ExpectedTypes)
        {
            var Current = Surface.GetComponents<Collider>();
            var Matched = 0;
            foreach (var Collider in Current)
            {
                if (!Eligible(Collider)) continue;
                if (Matched >= Expected.Count
                    || !ReferenceEquals(Collider, Expected[Matched])
                    || Collider.gameObject != Surface.gameObject
                    || !string.Equals(
                        Collider.GetType().FullName,
                        ExpectedTypes[Matched],
                        StringComparison.Ordinal)) return false;
                Matched++;
            }
            return Matched == Expected.Count;
        }

        static bool ValidNative(
            ClimbModifierSurface Surface, bool RequireNeutralMultipliers) =>
            Surface.enabled
            && Surface.gameObject.layer is TerrainLayer or MapLayer
            && Surface.GetComponents<ClimbModifierSurface>().Length == 1
            && !Surface.onlySlideDown
            && Surface.slipModifier.Equals(0f)
            && !Surface.applyStatus
            && Surface.statusType == default
            && !Surface.applyPetrify
            && Surface.statusAmount.Equals(0.5f)
            && Surface.statusCooldown.Equals(0.5f)
            && !Surface.staticClimbCost
            && Surface.onClimbAction is null
            && !Surface.hasAlwaysClimbableRange
            && Surface.alwaysClimbableRange.Equals(0f)
            && ValidMultiplier(Surface.speedMultiplier)
            && ValidMultiplier(Surface.staminaUsageMultiplier)
            && (!RequireNeutralMultipliers
                || Surface.speedMultiplier.Equals(1f)
                && Surface.staminaUsageMultiplier.Equals(1f));
    }

    readonly struct PhysicsMaterialIdentity : IEquatable<PhysicsMaterialIdentity>
    {
        PhysicsMaterialIdentity(
            string Name,
            float DynamicFriction,
            float StaticFriction,
            float Bounciness,
            PhysicsMaterialCombine FrictionCombine,
            PhysicsMaterialCombine BounceCombine)
        {
            this.Name = Name;
            this.DynamicFriction = DynamicFriction;
            this.StaticFriction = StaticFriction;
            this.Bounciness = Bounciness;
            this.FrictionCombine = FrictionCombine;
            this.BounceCombine = BounceCombine;
        }

        readonly string Name;
        readonly float DynamicFriction;
        readonly float StaticFriction;
        readonly float Bounciness;
        readonly PhysicsMaterialCombine FrictionCombine;
        readonly PhysicsMaterialCombine BounceCombine;

        internal static PhysicsMaterialIdentity Capture(
            PhysicsMaterial Material, BiomeId Id)
        {
            if (!Material
                || !float.IsFinite(Material.dynamicFriction)
                || !float.IsFinite(Material.staticFriction)
                || !float.IsFinite(Material.bounciness))
                throw new ArgumentException(
                    $"Biome '{Id}' climb material has invalid physics values.",
                    "Template");
            return new PhysicsMaterialIdentity(
                Material.name ?? string.Empty,
                Material.dynamicFriction, Material.staticFriction,
                Material.bounciness, Material.frictionCombine,
                Material.bounceCombine);
        }

        internal void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write(Name);
            Writer.Write(DynamicFriction);
            Writer.Write(StaticFriction);
            Writer.Write(Bounciness);
            Writer.Write((int)FrictionCombine);
            Writer.Write((int)BounceCombine);
        }

        public bool Equals(PhysicsMaterialIdentity Other) =>
            string.Equals(Name, Other.Name, StringComparison.Ordinal)
            && DynamicFriction.Equals(Other.DynamicFriction)
            && StaticFriction.Equals(Other.StaticFriction)
            && Bounciness.Equals(Other.Bounciness)
            && FrictionCombine == Other.FrictionCombine
            && BounceCombine == Other.BounceCombine;
    }

    sealed class ResolvedTargetDraft
    {
        internal ResolvedTargetDraft(ClimbModifierSurface Surface) =>
            this.Surface = Surface;

        internal ClimbModifierSurface Surface { get; }
        internal int ObjectDefinition { get; set; } = -1;
        internal int MaterialDefinition { get; set; } = -1;
    }
}

internal readonly struct ResolvedTargetBinding
{
    internal ResolvedTargetBinding(
        ClimbModifierSurface Surface, Collider[] Colliders,
        Transform? GeneratedRoot)
    {
        this.Surface = Surface;
        this.Colliders = Colliders;
        this.GeneratedRoot = GeneratedRoot;
    }

    internal ClimbModifierSurface Surface { get; }
    internal Collider[] Colliders { get; }
    internal Transform? GeneratedRoot { get; }
}

internal readonly struct ResolvedClimbSurfaceCatalog
{
    internal ResolvedClimbSurfaceCatalog(
        ResolvedClimbSurfaceDefinition[] Definitions,
        ResolvedClimbSurfaceTarget[] Targets)
    {
        this.Definitions = Definitions;
        this.Targets = Targets;
    }

    internal ResolvedClimbSurfaceDefinition[] Definitions { get; }
    internal ResolvedClimbSurfaceTarget[] Targets { get; }
}

internal readonly struct ResolvedClimbSurfaceDefinition
{
    internal ResolvedClimbSurfaceDefinition(
        PllClimbSurfaceMode Mode, string Hook,
        float StaminaUsageMultiplier, float SpeedMultiplier)
    {
        this.Mode = Mode;
        this.Hook = Hook;
        this.StaminaUsageMultiplier = StaminaUsageMultiplier;
        this.SpeedMultiplier = SpeedMultiplier;
    }

    internal PllClimbSurfaceMode Mode { get; }
    internal string Hook { get; }
    internal float StaminaUsageMultiplier { get; }
    internal float SpeedMultiplier { get; }
}

internal readonly struct ResolvedClimbSurfaceTarget
{
    internal ResolvedClimbSurfaceTarget(
        ClimbModifierSurface Surface,
        int ObjectDefinition, int MaterialDefinition)
    {
        this.Surface = Surface;
        this.ObjectDefinition = ObjectDefinition;
        this.MaterialDefinition = MaterialDefinition;
    }

    internal ClimbModifierSurface Surface { get; }
    internal int ObjectDefinition { get; }
    internal int MaterialDefinition { get; }
}