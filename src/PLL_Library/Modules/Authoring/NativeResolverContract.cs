using System;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeResolverContract
{
    static readonly string[] ReservedChildNames =
        { "__PLL_WaterZone", "__PLL_NativeWeather", "__PLL_NativeSporeCloud" };
    readonly ComponentBinding<PLLNativeResolver> Component;
    readonly ObjectBinding Root;
    readonly ResolverAlternative Variant;

    NativeResolverContract(
        ComponentBinding<PLLNativeResolver> Component,
        ObjectBinding Root,
        ResolverAlternative Variant)
    {
        this.Component = Component;
        this.Root = Root;
        this.Variant = Variant;
    }

    internal int NativeSourceMask => Variant.NativeSourceMask;
    internal bool IsFlow => Variant is FlowAlternative;
    internal NativeFlowContract? FlowSource => (Variant as FlowAlternative)?.Contract;

    internal static NativeResolverContract[] CreateAll(
        Transform Template,
        BiomeId Id,
        AuthoringCandidateGraph Graph,
        IReadOnlyList<EffectAuthoringContract> Effects,
        Dictionary<PllFluidDefinition, FluidDefinitionContract> Definitions,
        IReadOnlyDictionary<PLLNativeResolver, SurfaceTextureAuthoringContract> SurfaceTextures)
    {
        foreach (var Candidate in Graph.Ordered)
            foreach (var Source in Candidate.GetComponentsInChildren<PLLNativeResolver>(true))
                if (Source.Form != PllNativeResolverForm.Source
                    || Source.Kind is not (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid))
                    throw new ArgumentException($"Biome '{Id}' places an unsupported native resolver inside a generated candidate.", nameof(Template));
        var Sources = Template.GetComponentsInChildren<PLLNativeResolver>(true);
        var Ordered = new List<(string Key, PLLNativeResolver Value)>(Sources.Length);
        foreach (var Source in Sources)
            Ordered.Add((DeterministicObjectIdentity.RelativeComponentPath(Source, Template, 0), Source));
        Ordered.Sort((Left, Right) => StringComparer.Ordinal.Compare(Left.Key, Right.Key));
        var Result = new NativeResolverContract[Ordered.Count];
        var WeatherCount = 0;
        for (var i = 0; i < Result.Length; i++)
        {
            Result[i] = Create(Template, Id, Effects, Ordered[i].Value, Definitions, SurfaceTextures);
            if (Result[i].Variant.ReservesWeatherSingleton) WeatherCount++;
        }
        if (WeatherCount > 1)
            throw new ArgumentException($"Biome '{Id}' contains more than one native weather resolver.", nameof(Template));
        for (var i = 0; i < Result.Length; i++)
            for (var j = i + 1; j < Result.Length; j++)
                if (TransformOwnership.Contains(Result[i].Root.Source.transform, Result[j].Root.Source.transform)
                    || TransformOwnership.Contains(Result[j].Root.Source.transform, Result[i].Root.Source.transform))
                    throw new ArgumentException($"Biome '{Id}' native resolver roots cannot overlap.", nameof(Template));
        return Result;
    }

    internal static GeneratedNativeFlowContract[] CreateGenerated(AuthoringCandidateGraph Graph, BiomeId Id,
        Dictionary<PllFluidDefinition, FluidDefinitionContract> Definitions,
        IReadOnlyDictionary<PLLNativeResolver, SurfaceTextureAuthoringContract> SurfaceTextures)
    {
        var Result = new List<GeneratedNativeFlowContract>();
        var ContainsFlows = new Dictionary<GameObject, bool>();
        ValidateChildPlacement(Graph.SourceRoot);
        foreach (var Candidate in Graph.Ordered) ValidateChildPlacement(Candidate);
        foreach (var Candidate in Graph.Ordered)
        {
            var Sources = Candidate.GetComponentsInChildren<PLLNativeResolver>(true);
            if (Sources.Length == 0) continue;
            if (Candidate.GetComponentsInChildren<LavaRiver>(true).Length != 0
                || Candidate.GetComponentsInChildren<WaterFallConfig>(true).Length != 0)
                throw new ArgumentException($"Biome '{Id}' flow candidate contains a second native flow condition.");
            var Ordered = new List<(string Path, NativeFlowContract Contract)>(Sources.Length);
            foreach (var Source in Sources)
            {
                if (Source.Form != PllNativeResolverForm.Source)
                    throw new ArgumentException($"Biome '{Id}' generated native resolvers must use Source form.");
                ValidateSourceHeader(Source, Id, true);
                Ordered.Add((DeterministicObjectIdentity.RelativeComponentPath(Source, Candidate.transform, 0),
                    NativeFlowContract.Capture(Candidate.transform, Id, Source, Definitions, SurfaceTextures)));
            }
            Ordered.Sort((Left, Right) => StringComparer.Ordinal.Compare(Left.Path, Right.Path));
            var Contracts = new NativeFlowContract[Ordered.Count];
            for (var i = 0; i < Contracts.Length; i++) Contracts[i] = Ordered[i].Contract;
            Result.Add(new GeneratedNativeFlowContract(Candidate, Contracts));
        }
        return Result.ToArray();

        bool HasFlow(GameObject Candidate)
        {
            if (ContainsFlows.TryGetValue(Candidate, out var Result)) return Result;
            foreach (var Source in Candidate.GetComponentsInChildren<PLLNativeResolver>(true))
                if (Source.Form == PllNativeResolverForm.Source) return ContainsFlows[Candidate] = true;
            foreach (var Child in Graph.CandidatesFrom(Candidate))
                if (HasFlow(Child)) return ContainsFlows[Candidate] = true;
            return ContainsFlows[Candidate] = false;
        }

        void ValidateChildPlacement(GameObject Owner)
        {
            foreach (var Spawner in Owner.GetComponentsInChildren<PropSpawner>(true))
                if (NativePrefabContracts.EffectiveProps(Spawner) is { } Candidates)
                    foreach (var Candidate in Candidates)
                    {
                        var Child = Candidate.GetComponent<PllChildPropAuthoring>();
                        if (!Child || Child.Timing != PllChildPropTiming.Immediate || !HasFlow(Candidate)) continue;
                        if (Spawner.modifiers is not { Count: > 0 }
                            || Spawner.modifiers[Spawner.modifiers.Count - 1] is not PSM_ChildSpawners)
                            throw new ArgumentException($"Biome '{Id}' immediate child flow must run after the parent's final placement modifier.");
                    }
        }
    }

    internal NativeFlowRuntime? ResolveFlow(Transform RuntimeRoot, BiomeId Id, GeneratedGeometryBudget Budget,
        FinalForceOccurrence? Selection = null)
    {
        if (Variant is not FlowAlternative Flow) return null;
        var Source = Component.Resolve(RuntimeRoot, Id, "native flow");
        return (Selection?.IsSelected(Source.transform) ?? Source.gameObject.activeInHierarchy)
            ? Flow.Contract.Resolve(RuntimeRoot, Id, Budget, RuntimeRoot) : null;
    }

    internal ResolvedNativeResolver Resolve(Transform RuntimeRoot, BiomeId Id)
    {
        var Authoring = Component.Resolve(RuntimeRoot, Id, "native resolver");
        var ResolvedRoot = Root.Resolve(RuntimeRoot, Id, "native resolver root");
        ValidateResolvedHeader(Authoring, ResolvedRoot, Id);
        return Variant.Resolve(RuntimeRoot, Authoring, ResolvedRoot, Id);
    }

    internal void Validate(Transform Template, BiomeId Id, IReadOnlyList<EffectAuthoringContract> Effects)
    {
        Component.Validate(Template, Id, "native resolver");
        Root.Validate(Template, Id, "native resolver root");
        var Source = Component.Source;
        ValidateSourceHeader(Source, Id, false);
        if (Source.Root != Root.Source || Source.Kind != Variant.Kind || !Variant.MatchesMetadata(Source))
            throw Invalid(Id, Source, "changed its translated metadata", false);
        Variant.ValidateBindings(Template, Id);
        Variant.ValidateMetadata(Source, Id, Effects, false);
        Variant.ValidateTranslatedSource(Source, Root.Source, Id, false);
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Component.WriteManifest(Writer);
        Root.WriteManifest(Writer);
        Variant.WriteManifest(Writer);
    }

    static NativeResolverContract Create(
        Transform Template,
        BiomeId Id,
        IReadOnlyList<EffectAuthoringContract> Effects,
        PLLNativeResolver Source,
        Dictionary<PllFluidDefinition, FluidDefinitionContract> Definitions,
        IReadOnlyDictionary<PLLNativeResolver, SurfaceTextureAuthoringContract> SurfaceTextures)
    {
        ValidateSourceHeader(Source, Id, true);
        var Variant = ResolverAlternative.Capture(Template, Id, Effects, Source, Definitions, SurfaceTextures);
        return new NativeResolverContract(
            ComponentBinding<PLLNativeResolver>.Create(Template, Source),
            ObjectBinding.Create(Template, Source.Root),
            Variant);
    }

    void ValidateResolvedHeader(PLLNativeResolver Source, GameObject ResolvedRoot, BiomeId Id)
    {
        if (Source.Root != ResolvedRoot || Source.Kind != Variant.Kind
            || !Source.Volume.Equals(Variant.Volume)
            || !Source.MovementModifierShallow.Equals(Variant.MovementShallow)
            || !Source.MovementModifierDeep.Equals(Variant.MovementDeep)
            || !Source.AnimModifierShallow.Equals(Variant.AnimationShallow)
            || !Source.AnimModifierDeep.Equals(Variant.AnimationDeep))
            throw new InvalidOperationException($"Biome '{Id}' changed a native resolver after sealing.");
    }

    static void ValidateSourceHeader(PLLNativeResolver Source, BiomeId Id, bool Argument)
    {
        if (!Source || !Source.enabled || !Source.Root || Source.Root != Source.gameObject
            || Source.GetComponents<PLLNativeResolver>().Length != 1
            || Source.Form is not (PllNativeResolverForm.Zone or PllNativeResolverForm.Source)
            || Source.Form == PllNativeResolverForm.Source
                && Source.Kind is not (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid)
            || Source.Form != PllNativeResolverForm.Source
                && TransformOwnership.HasPropSpawnerInPath(Source.transform.root, Source.transform))
            throw Invalid(Id, Source, "root, kind, or ownership is invalid", Argument);
        if (Source.Form == PllNativeResolverForm.Zone
            && !(Source.Kind == PLLNativeResolverKind.CustomFluid && Source.FluidDefinition
                && Source.FluidDefinition.Behavior == PllFluidBehavior.VisualOnly))
            ZoneVolumeContract.Validate(Source.Volume, Id, $"native resolver '{Source.name}'", Argument);
        if (!FiniteNonnegative(Source.MovementModifierShallow)
            || !FiniteNonnegative(Source.MovementModifierDeep)
            || !FiniteNonnegative(Source.AnimModifierShallow)
            || !FiniteNonnegative(Source.AnimModifierDeep))
            throw Invalid(Id, Source, "water movement or animation modifier is invalid", Argument);
    }

    static void ValidateReservedChildren(
        PLLNativeResolver Source,
        GameObject Owner,
        string ExpectedName,
        BiomeId Id,
        bool Argument)
    {
        foreach (var Name in ReservedChildNames)
        {
            var Count = 0;
            for (var i = 0; i < Owner.transform.childCount; i++)
                if (string.Equals(Owner.transform.GetChild(i).name, Name, StringComparison.Ordinal)) Count++;
            if (Count != (Name == ExpectedName ? 1 : 0))
                throw Invalid(Id, Source, "has an invalid reserved translated-child shape", Argument);
        }
    }

    static int FindPresentation(IReadOnlyList<EffectAuthoringContract> Effects, PllEffectAuthoring? Source)
    {
        if (!Source) return -1;
        for (var i = 0; i < Effects.Count; i++)
            if (ReferenceEquals(Effects[i].Source, Source)) return i;
        return -1;
    }

    static GameObject ExactChild(Transform Root, string Name, BiomeId Id)
    {
        GameObject? Result = null;
        for (var i = 0; i < Root.childCount; i++)
        {
            var Child = Root.GetChild(i).gameObject;
            if (!string.Equals(Child.name, Name, StringComparison.Ordinal)) continue;
            if (Result) throw new ArgumentException($"Biome '{Id}' contains duplicate '{Name}' translations.", nameof(Root));
            Result = Child;
        }
        return Result ? Result : throw new ArgumentException($"Biome '{Id}' is missing translated child '{Name}'.", nameof(Root));
    }

    static Exception Invalid(BiomeId Id, PLLNativeResolver? Source, string Reason, bool Argument) => Argument
        ? new ArgumentException($"Biome '{Id}' native resolver '{(Source ? Source.name : "destroyed")}' {Reason}.", "source")
        : new InvalidOperationException($"Biome '{Id}' native resolver '{(Source ? Source.name : "destroyed")}' {Reason}.");

    static bool FiniteNonnegative(float Value) => Value >= 0f && ManifestEncoding.CanEncode(Value);

    abstract class ResolverAlternative
    {
        protected ResolverAlternative(PLLNativeResolverKind Kind, PLLNativeResolver Source, LocalTransformFingerprint TranslatedTransform)
        {
            this.Kind = Kind;
            Volume = Source.Volume;
            MovementShallow = Source.MovementModifierShallow;
            MovementDeep = Source.MovementModifierDeep;
            AnimationShallow = Source.AnimModifierShallow;
            AnimationDeep = Source.AnimModifierDeep;
            this.TranslatedTransform = TranslatedTransform;
        }

        internal PLLNativeResolverKind Kind { get; }
        internal PllZoneVolume Volume { get; }
        internal float MovementShallow { get; }
        internal float MovementDeep { get; }
        internal float AnimationShallow { get; }
        internal float AnimationDeep { get; }
        protected LocalTransformFingerprint TranslatedTransform { get; }
        internal abstract int PresentationEffectIndex { get; }
        internal abstract int NativeSourceMask { get; }
        internal virtual bool ReservesWeatherSingleton => false;

        internal static ResolverAlternative Capture(
            Transform Template,
            BiomeId Id,
            IReadOnlyList<EffectAuthoringContract> Effects,
            PLLNativeResolver Source,
            Dictionary<PllFluidDefinition, FluidDefinitionContract> Definitions,
            IReadOnlyDictionary<PLLNativeResolver, SurfaceTextureAuthoringContract> SurfaceTextures)
        {
            var Kind = Source.Kind;
            if (Source.Form == PllNativeResolverForm.Source || Kind is PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid)
                return new FlowAlternative(Source, NativeFlowContract.Capture(Template, Id, Source, Definitions, SurfaceTextures));
            return Kind switch
            {
                PLLNativeResolverKind.Water => WaterAlternative.CaptureWater(Template, Id, Source),
                PLLNativeResolverKind.Rain => WeatherAlternative.CaptureWeather(Template, Id, Effects, Source, Kind),
                PLLNativeResolverKind.Wind => WeatherAlternative.CaptureWeather(Template, Id, Effects, Source, Kind),
                PLLNativeResolverKind.Snow => WeatherAlternative.CaptureWeather(Template, Id, Effects, Source, Kind),
                PLLNativeResolverKind.SporeCloud => SporeCloudAlternative.CaptureSporeCloud(Template, Id, Effects, Source),
                _ => throw Invalid(Id, Source, "root, kind, or ownership is invalid", true)
            };
        }

        internal abstract bool MatchesMetadata(PLLNativeResolver Source);
        internal abstract void ValidateBindings(Transform Template, BiomeId Id);
        internal abstract void ValidateMetadata(
            PLLNativeResolver Source,
            BiomeId Id,
            IReadOnlyList<EffectAuthoringContract> Effects,
            bool Argument);
        protected abstract void ValidateTranslatedShape(
            PLLNativeResolver Source,
            GameObject Owner,
            BiomeId Id,
            bool Argument);
        internal abstract ResolvedNativeResolver Resolve(
            Transform RuntimeRoot,
            PLLNativeResolver Source,
            GameObject ResolvedRoot,
            BiomeId Id);

        internal void ValidateTranslatedSource(
            PLLNativeResolver Source,
            GameObject Owner,
            BiomeId Id,
            bool Argument)
        {
            ValidateReservedChildren(Source, Owner, ExpectedChildName, Id, Argument);
            ValidateTranslatedShape(Source, Owner, Id, Argument);
        }

        internal void WriteManifest(BinaryWriter Writer)
        {
            Writer.Write((byte)Kind);
            ZoneVolumeContract.WriteManifest(Writer, Volume);
            ManifestEncoding.WriteScalar(Writer, MovementShallow);
            ManifestEncoding.WriteScalar(Writer, MovementDeep);
            ManifestEncoding.WriteScalar(Writer, AnimationShallow);
            ManifestEncoding.WriteScalar(Writer, AnimationDeep);
            Writer.Write(PresentationEffectIndex);
            WriteBindings(Writer);
            TranslatedTransform.WriteManifest(Writer);
            WriteSurfaceManifest(Writer);
        }

        protected abstract string ExpectedChildName { get; }
        protected abstract void WriteBindings(BinaryWriter Writer);
        protected virtual void WriteSurfaceManifest(BinaryWriter Writer) => Writer.Write(false);
    }

    sealed class FlowAlternative : ResolverAlternative
    {
        internal readonly NativeFlowContract Contract;
        internal FlowAlternative(PLLNativeResolver Source, NativeFlowContract Contract)
            : base(Source.Kind, Source, LocalTransformFingerprint.Create(Source.transform)) => this.Contract = Contract;
        internal override int PresentationEffectIndex => -1;
        internal override int NativeSourceMask => Contract.NativeSourceMask;
        protected override string ExpectedChildName => string.Empty;
        internal override bool MatchesMetadata(PLLNativeResolver Source) => Contract.MatchesMetadata(Source);
        internal override void ValidateBindings(Transform Template, BiomeId Id) => Contract.ValidateBindings(Template, Id);
        internal override void ValidateMetadata(PLLNativeResolver Source, BiomeId Id,
            IReadOnlyList<EffectAuthoringContract> Effects, bool Argument)
        { }
        protected override void ValidateTranslatedShape(PLLNativeResolver Source, GameObject Owner, BiomeId Id, bool Argument) { }
        internal override ResolvedNativeResolver Resolve(Transform RuntimeRoot,
            PLLNativeResolver Source, GameObject ResolvedRoot, BiomeId Id) =>
            throw new InvalidOperationException("Native flows are resolved under their source lifetime owner.");
        protected override void WriteBindings(BinaryWriter Writer) => Contract.WriteManifest(Writer);
    }

    sealed class WaterAlternative : ResolverAlternative
    {
        readonly ComponentBinding<WaterZone> Zone;
        readonly ComponentBinding<MeshRenderer> Plane;
        readonly WaterSurface Surface;

        WaterAlternative(
            PLLNativeResolver Source,
            ComponentBinding<WaterZone> Zone,
            ComponentBinding<MeshRenderer> Plane,
            WaterSurface Surface,
            LocalTransformFingerprint TranslatedTransform)
            : base(PLLNativeResolverKind.Water, Source, TranslatedTransform)
        {
            this.Zone = Zone;
            this.Plane = Plane;
            this.Surface = Surface;
        }

        internal override int PresentationEffectIndex => -1;
        internal override int NativeSourceMask => 0;
        protected override string ExpectedChildName => "__PLL_WaterZone";

        internal static WaterAlternative CaptureWater(Transform Template, BiomeId Id, PLLNativeResolver Source)
        {
            ValidateWaterMetadata(Source, Id, true);
            var Child = ExactChild(Source.Root.transform, "__PLL_WaterZone", Id);
            var Zones = Child.GetComponents<WaterZone>();
            if (Zones.Length != 1)
                throw Invalid(Id, Source, "translated WaterZone shape is invalid", true);
            if (Zones[0].waterPlane is not MeshRenderer SurfaceRenderer)
                throw Invalid(Id, Source, "translated WaterZone surface is not a MeshRenderer", true);
            var Result = new WaterAlternative(
                Source,
                ComponentBinding<WaterZone>.Create(Template, Zones[0]),
                ComponentBinding<MeshRenderer>.Create(Template, SurfaceRenderer),
                WaterSurface.Capture(Source),
                LocalTransformFingerprint.Create(Child.transform));
            Result.ValidateTranslatedSource(Source, Source.Root, Id, true);
            return Result;
        }

        internal override bool MatchesMetadata(PLLNativeResolver Source) => !Source.Presentation
            && Surface.Matches(Source, Plane.Source);

        internal override void ValidateBindings(Transform Template, BiomeId Id)
        {
            Zone.Validate(Template, Id, "translated WaterZone");
            Plane.Validate(Template, Id, "translated water surface");
        }

        internal override void ValidateMetadata(
            PLLNativeResolver Source,
            BiomeId Id,
            IReadOnlyList<EffectAuthoringContract> Effects,
            bool Argument) => ValidateWaterMetadata(Source, Id, Argument);

        protected override void ValidateTranslatedShape(
            PLLNativeResolver Source,
            GameObject Owner,
            BiomeId Id,
            bool Argument)
        {
            var WaterZone = Zone.Source;
            var SurfaceRenderer = Plane.Source;
            var Bounds = NativeResolverVolume.WorldBounds(Owner.transform, Volume);
            TranslatedTransform.Validate(WaterZone.transform, Id, "translated WaterZone", Argument);
            if (Owner.GetComponentsInChildren<WaterZone>(true).Length != 1
                || WaterZone.transform.parent != Owner.transform || !WaterZone.enabled || !WaterZone.gameObject.activeSelf
                || WaterZone.GetComponents<Component>().Length != 2 || !WaterZone.transform.position.Equals(Bounds.center)
                || WaterZone.transform.rotation != Quaternion.identity || WaterZone.waterPlane != SurfaceRenderer
                || !WaterZone.waterDepth.Equals(Bounds.size.y) || !WaterZone.zoneBounds.Equals(Bounds)
                || !WaterZone.movementModifierShallow.Equals(MovementShallow)
                || !WaterZone.movementModifierDeep.Equals(MovementDeep)
                || !WaterZone.animModShallow.Equals(AnimationShallow)
                || !WaterZone.animModDeep.Equals(AnimationDeep))
                throw Invalid(Id, Source, "translated WaterZone shape is invalid", Argument);
            Surface.Validate(Source, Owner, SurfaceRenderer, Volume, Id, Argument);
        }

        internal override ResolvedNativeResolver Resolve(
            Transform RuntimeRoot,
            PLLNativeResolver Source,
            GameObject ResolvedRoot,
            BiomeId Id)
        {
            var WaterZone = Zone.Resolve(RuntimeRoot, Id, "translated WaterZone");
            var SurfaceRenderer = Plane.Resolve(RuntimeRoot, Id, "translated water surface");
            var Bounds = NativeResolverVolume.WorldBounds(ResolvedRoot.transform, Volume);
            if (Source.Presentation)
                throw new InvalidOperationException($"Biome '{Id}' changed a native resolver after sealing.");
            TranslatedTransform.Validate(WaterZone.transform, Id, "translated WaterZone");
            if (!WaterZone.enabled || !WaterZone.gameObject.activeSelf
                || WaterZone.GetComponents<Component>().Length != 2 || WaterZone.waterPlane != SurfaceRenderer
                || !WaterZone.transform.position.Equals(Bounds.center) || !WaterZone.waterDepth.Equals(Bounds.size.y)
                || !WaterZone.movementModifierShallow.Equals(MovementShallow)
                || !WaterZone.movementModifierDeep.Equals(MovementDeep)
                || !WaterZone.animModShallow.Equals(AnimationShallow)
                || !WaterZone.animModDeep.Equals(AnimationDeep))
                throw new InvalidOperationException($"Biome '{Id}' changed its translated WaterZone after sealing.");
            Surface.Validate(Source, ResolvedRoot, SurfaceRenderer, Volume, Id, false);
            return ResolvedNativeResolver.CreateWater(
                ResolvedRoot, Volume, WaterZone, SurfaceRenderer,
                MovementShallow, MovementDeep, AnimationShallow, AnimationDeep);
        }

        protected override void WriteBindings(BinaryWriter Writer)
        {
            Writer.Write(true); Zone.WriteManifest(Writer);
            Writer.Write(true); Plane.WriteManifest(Writer);
            Writer.Write(false); Writer.Write(false); Writer.Write(false); Writer.Write(false); Writer.Write(false);
        }

        protected override void WriteSurfaceManifest(BinaryWriter Writer) => Surface.WriteManifest(Writer);

        static void ValidateWaterMetadata(PLLNativeResolver Source, BiomeId Id, bool Argument)
        {
            if (Source.Volume.Shape != PllZoneShape.Box || Source.Presentation
                || (Source.SurfaceRenderer ? 1 : 0) + (Source.GeneratedSurfaceMaterial ? 1 : 0) != 1)
                throw Invalid(Id, Source, "Water requires one Box volume and exactly one creator-owned surface mode", Argument);
            if (Vector3.Angle(Source.transform.TransformVector(Vector3.up).normalized, Vector3.up) > 0.01f)
                throw Invalid(Id, Source, "Water resolver must keep its positive Y axis upright", Argument);
        }

        abstract class WaterSurface
        {
            internal static WaterSurface Capture(PLLNativeResolver Source) => Source.SurfaceRenderer
                ? new AuthoredSurface()
                : new GeneratedSurface(Source.GeneratedSurfaceMaterial!);

            internal abstract bool Matches(PLLNativeResolver Source, MeshRenderer Plane);
            internal abstract void Validate(
                PLLNativeResolver Source,
                GameObject Owner,
                MeshRenderer Plane,
                PllZoneVolume Volume,
                BiomeId Id,
                bool Argument);
            internal abstract void WriteManifest(BinaryWriter Writer);

            sealed class AuthoredSurface : WaterSurface
            {
                internal override bool Matches(PLLNativeResolver Source, MeshRenderer Plane) =>
                    Source.SurfaceRenderer == Plane && !Source.GeneratedSurfaceMaterial;

                internal override void Validate(
                    PLLNativeResolver Source,
                    GameObject Owner,
                    MeshRenderer Plane,
                    PllZoneVolume Volume,
                    BiomeId Id,
                    bool Argument)
                {
                    if (!Owner || !Plane || !TransformOwnership.Contains(Owner.transform, Plane.transform))
                        throw Invalid(Id, Source, "translated water surface is unowned", Argument);
                    var Filter = Plane.GetComponent<MeshFilter>();
                    var Materials = Plane.sharedMaterials;
                    if (Source.SurfaceRenderer != Plane || Source.GeneratedSurfaceMaterial
                        || Plane.transform == Owner.transform || !Filter || !Filter.sharedMesh
                        || Materials.Length == 0 || Array.Exists(Materials, Value => !Value))
                        throw Invalid(Id, Source, "authored water surface shape is invalid", Argument);
                }

                internal override void WriteManifest(BinaryWriter Writer) => Writer.Write(false);
            }

            sealed class GeneratedSurface : WaterSurface
            {
                readonly Material Material;

                internal GeneratedSurface(Material Material)
                {
                    this.Material = Material;
                }

                internal override bool Matches(PLLNativeResolver Source, MeshRenderer Plane) =>
                    !Source.SurfaceRenderer && Source.GeneratedSurfaceMaterial == Material;

                internal override void Validate(
                    PLLNativeResolver Source,
                    GameObject Owner,
                    MeshRenderer Plane,
                    PllZoneVolume Volume,
                    BiomeId Id,
                    bool Argument)
                {
                    if (!Owner || !Plane || !TransformOwnership.Contains(Owner.transform, Plane.transform))
                        throw Invalid(Id, Source, "translated water surface is unowned", Argument);
                    var MeshFilter = Plane.GetComponent<MeshFilter>();
                    var Mesh = MeshFilter ? MeshFilter.sharedMesh : null;
                    var Materials = Plane.sharedMaterials;
                    var HalfX = Volume.Size.x * 0.5f;
                    var HalfZ = Volume.Size.z * 0.5f;
                    var ExpectedVertices = new[]
                    {
                        new Vector3(-HalfX, 0f, -HalfZ), new Vector3(-HalfX, 0f, HalfZ),
                        new Vector3(HalfX, 0f, HalfZ), new Vector3(HalfX, 0f, -HalfZ)
                    };
                    if (Source.SurfaceRenderer || Source.GeneratedSurfaceMaterial != Material
                        || Plane.name != "__PLL_WaterSurface" || Plane.transform.parent != Owner.transform
                        || Plane.GetComponents<Component>().Length != 3 || !MeshFilter || !Mesh
                        || Plane.GetComponent<Collider>()
                        || Plane.transform.localPosition != Volume.Center + Vector3.up * (Volume.Size.y * 0.5f)
                        || Plane.transform.localRotation != Quaternion.identity || Plane.transform.localScale != Vector3.one
                        || Materials.Length != 1 || Materials[0] != Material
                        || !Same(Mesh.vertices, ExpectedVertices)
                        || !Same(Mesh.normals, new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up })
                        || !Same(Mesh.uv, new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right })
                        || !Same(Mesh.triangles, new[] { 0, 1, 2, 0, 2, 3 }))
                        throw Invalid(Id, Source, "generated water surface shape is invalid", Argument);
                }

                internal override void WriteManifest(BinaryWriter Writer)
                {
                    Writer.Write(true);
                    if (!Material)
                        throw new InvalidOperationException("A native resolver generated-surface material was destroyed before manifest sealing.");
                    Writer.Write(Material.name ?? string.Empty);
                    Writer.Write(Material.shader ? Material.shader.name ?? string.Empty : string.Empty);
                }
            }
        }
    }

    sealed class WeatherAlternative : ResolverAlternative
    {
        readonly ComponentBinding<PhotonView> View;
        readonly ComponentBinding<WindChillZone> Zone;
        readonly ComponentBinding<PllEffectAuthoring> Presentation;
        readonly int PresentationIndex;

        WeatherAlternative(
            PLLNativeResolverKind Kind,
            PLLNativeResolver Source,
            ComponentBinding<PhotonView> View,
            ComponentBinding<WindChillZone> Zone,
            ComponentBinding<PllEffectAuthoring> Presentation,
            int PresentationIndex,
            LocalTransformFingerprint TranslatedTransform)
            : base(Kind, Source, TranslatedTransform)
        {
            this.View = View;
            this.Zone = Zone;
            this.Presentation = Presentation;
            this.PresentationIndex = PresentationIndex;
        }

        internal override int PresentationEffectIndex => PresentationIndex;
        internal override int NativeSourceMask => 1 << (int)Kind;
        internal override bool ReservesWeatherSingleton => true;
        protected override string ExpectedChildName => "__PLL_NativeWeather";

        internal static WeatherAlternative CaptureWeather(
            Transform Template,
            BiomeId Id,
            IReadOnlyList<EffectAuthoringContract> Effects,
            PLLNativeResolver Source,
            PLLNativeResolverKind Kind)
        {
            var PresentationIndex = FindPresentation(Effects, Source.Presentation);
            ValidateWeatherMetadata(Source, Id, Effects, PresentationIndex, true);
            var Child = ExactChild(Source.Root.transform, "__PLL_NativeWeather", Id);
            var Views = Child.GetComponents<PhotonView>();
            var Zones = Child.GetComponents<WindChillZone>();
            if (Views.Length != 1 || Zones.Length != 1)
                throw Invalid(Id, Source, "translated native-weather shape is invalid", true);
            var Result = new WeatherAlternative(
                Kind, Source,
                ComponentBinding<PhotonView>.Create(Template, Views[0]),
                ComponentBinding<WindChillZone>.Create(Template, Zones[0]),
                ComponentBinding<PllEffectAuthoring>.Create(Template, Source.Presentation!),
                PresentationIndex,
                LocalTransformFingerprint.Create(Child.transform));
            Result.ValidateTranslatedSource(Source, Source.Root, Id, true);
            return Result;
        }

        internal override bool MatchesMetadata(PLLNativeResolver Source) =>
            !Source.SurfaceRenderer && !Source.GeneratedSurfaceMaterial && Source.Presentation == Presentation.Source;

        internal override void ValidateBindings(Transform Template, BiomeId Id)
        {
            View.Validate(Template, Id, "translated weather PhotonView");
            Zone.Validate(Template, Id, "translated WindChillZone");
            Presentation.Validate(Template, Id, "native resolver presentation");
        }

        internal override void ValidateMetadata(
            PLLNativeResolver Source,
            BiomeId Id,
            IReadOnlyList<EffectAuthoringContract> Effects,
            bool Argument) => ValidateWeatherMetadata(Source, Id, Effects, PresentationIndex, Argument);

        protected override void ValidateTranslatedShape(
            PLLNativeResolver Source,
            GameObject Owner,
            BiomeId Id,
            bool Argument)
        {
            var WindZone = Zone.Source;
            var PhotonView = View.Source;
            var Bounds = NativeResolverVolume.WorldBounds(Owner.transform, Volume);
            TranslatedTransform.Validate(WindZone.transform, Id, "translated native weather", Argument);
            if (Owner.GetComponentsInChildren<WindChillZone>(true).Length != 1
                || WindZone.transform.parent != Owner.transform || WindZone.gameObject.activeSelf || !WindZone.enabled
                || WindZone.GetComponents<Component>().Length != 3 || !PhotonView.enabled
                || PhotonView.gameObject != WindZone.gameObject || PhotonView.ViewID != 0
                || !WindZone.transform.position.Equals(Bounds.center)
                || WindZone.transform.rotation != Quaternion.identity || !WindZone.windZoneBounds.Equals(Bounds))
                throw Invalid(Id, Source, "translated native-weather shape is invalid", Argument);
        }

        internal override ResolvedNativeResolver Resolve(
            Transform RuntimeRoot,
            PLLNativeResolver Source,
            GameObject ResolvedRoot,
            BiomeId Id)
        {
            var PhotonView = View.Resolve(RuntimeRoot, Id, "translated weather PhotonView");
            var WindZone = Zone.Resolve(RuntimeRoot, Id, "translated WindChillZone");
            var ResolvedPresentation = Presentation.Resolve(RuntimeRoot, Id, "native resolver presentation");
            if (Source.SurfaceRenderer || Source.GeneratedSurfaceMaterial || Source.Presentation != ResolvedPresentation)
                throw new InvalidOperationException($"Biome '{Id}' changed a native resolver after sealing.");
            TranslatedTransform.Validate(WindZone.transform, Id, "translated native weather");
            if (WindZone.gameObject.activeSelf || !WindZone.enabled || !PhotonView.enabled
                || PhotonView.gameObject != WindZone.gameObject || PhotonView.ViewID <= 0
                || !ReferenceEquals(PhotonNetwork.GetPhotonView(PhotonView.ViewID), PhotonView)
                || WindZone.GetComponents<Component>().Length != 3)
                throw new InvalidOperationException($"Biome '{Id}' changed its translated native-weather shape after sealing.");
            return ResolvedNativeResolver.CreateWeather(
                ResolvedRoot, Kind, Volume, PresentationIndex, PhotonView, WindZone);
        }

        protected override void WriteBindings(BinaryWriter Writer)
        {
            Writer.Write(false); Writer.Write(false);
            Writer.Write(true); View.WriteManifest(Writer);
            Writer.Write(true); Zone.WriteManifest(Writer);
            Writer.Write(false); Writer.Write(false);
            Writer.Write(true); Presentation.WriteManifest(Writer);
        }

        static void ValidateWeatherMetadata(
            PLLNativeResolver Source,
            BiomeId Id,
            IReadOnlyList<EffectAuthoringContract> Effects,
            int PresentationIndex,
            bool Argument)
        {
            if (Source.SurfaceRenderer || Source.GeneratedSurfaceMaterial || PresentationIndex < 0
                || PresentationIndex >= Effects.Count || Effects[PresentationIndex].Source.Mode != PllEffectMode.Zone)
                throw Invalid(Id, Source, "native weather and SporeCloud require one creator-owned Zone presentation", Argument);
            if (Source.Volume.Shape != PllZoneShape.Box)
                throw Invalid(Id, Source, "native weather requires a Box volume", Argument);
        }
    }

    sealed class SporeCloudAlternative : ResolverAlternative
    {
        readonly ComponentBinding<WindAffectedStatusEmitter> Emitter;
        readonly ComponentBinding<DisableBasedOnRunSettings> Setting;
        readonly ComponentBinding<PllEffectAuthoring> Presentation;
        readonly int PresentationIndex;

        SporeCloudAlternative(
            PLLNativeResolver Source,
            ComponentBinding<WindAffectedStatusEmitter> Emitter,
            ComponentBinding<DisableBasedOnRunSettings> Setting,
            ComponentBinding<PllEffectAuthoring> Presentation,
            int PresentationIndex,
            LocalTransformFingerprint TranslatedTransform)
            : base(PLLNativeResolverKind.SporeCloud, Source, TranslatedTransform)
        {
            this.Emitter = Emitter;
            this.Setting = Setting;
            this.Presentation = Presentation;
            this.PresentationIndex = PresentationIndex;
        }

        internal override int PresentationEffectIndex => PresentationIndex;
        internal override int NativeSourceMask => 1 << (int)PLLNativeResolverKind.SporeCloud;
        protected override string ExpectedChildName => "__PLL_NativeSporeCloud";

        internal static SporeCloudAlternative CaptureSporeCloud(
            Transform Template,
            BiomeId Id,
            IReadOnlyList<EffectAuthoringContract> Effects,
            PLLNativeResolver Source)
        {
            var PresentationIndex = FindPresentation(Effects, Source.Presentation);
            ValidateSporeCloudMetadata(Source, Id, Effects, PresentationIndex, true);
            var Child = ExactChild(Source.Root.transform, "__PLL_NativeSporeCloud", Id);
            var Emitters = Child.GetComponents<WindAffectedStatusEmitter>();
            var Settings = Child.GetComponents<DisableBasedOnRunSettings>();
            if (Emitters.Length != 1 || Settings.Length != 1)
                throw Invalid(Id, Source, "translated SporeCloud shape is invalid", true);
            var Result = new SporeCloudAlternative(
                Source,
                ComponentBinding<WindAffectedStatusEmitter>.Create(Template, Emitters[0]),
                ComponentBinding<DisableBasedOnRunSettings>.Create(Template, Settings[0]),
                ComponentBinding<PllEffectAuthoring>.Create(Template, Source.Presentation!),
                PresentationIndex,
                LocalTransformFingerprint.Create(Child.transform));
            Result.ValidateTranslatedSource(Source, Source.Root, Id, true);
            return Result;
        }

        internal override bool MatchesMetadata(PLLNativeResolver Source) =>
            !Source.SurfaceRenderer && !Source.GeneratedSurfaceMaterial && Source.Presentation == Presentation.Source;

        internal override void ValidateBindings(Transform Template, BiomeId Id)
        {
            Emitter.Validate(Template, Id, "translated SporeCloud emitter");
            Setting.Validate(Template, Id, "translated SporeCloud run setting");
            Presentation.Validate(Template, Id, "native resolver presentation");
        }

        internal override void ValidateMetadata(
            PLLNativeResolver Source,
            BiomeId Id,
            IReadOnlyList<EffectAuthoringContract> Effects,
            bool Argument) => ValidateSporeCloudMetadata(Source, Id, Effects, PresentationIndex, Argument);

        protected override void ValidateTranslatedShape(
            PLLNativeResolver Source,
            GameObject Owner,
            BiomeId Id,
            bool Argument)
        {
            var StatusEmitter = Emitter.Source;
            var RunSetting = Setting.Source;
            TranslatedTransform.Validate(StatusEmitter.transform, Id, "translated SporeCloud", Argument);
            if (Owner.GetComponentsInChildren<WindAffectedStatusEmitter>(true).Length != 1
                || Owner.GetComponentsInChildren<DisableBasedOnRunSettings>(true).Length != 1
                || StatusEmitter.transform.parent != Owner.transform || StatusEmitter.gameObject.activeSelf
                || !StatusEmitter.enabled || !RunSetting.enabled || RunSetting.gameObject != StatusEmitter.gameObject
                || StatusEmitter.GetComponents<Component>().Length != 3 || StatusEmitter.GetComponent<PhotonView>()
                || StatusEmitter.GetComponent<Collider>() || StatusEmitter.transform.localPosition != Volume.Center
                || StatusEmitter.transform.localRotation != Quaternion.identity
                || StatusEmitter.transform.localScale != Vector3.one)
                throw Invalid(Id, Source, "translated SporeCloud shape is invalid", Argument);
        }

        internal override ResolvedNativeResolver Resolve(
            Transform RuntimeRoot,
            PLLNativeResolver Source,
            GameObject ResolvedRoot,
            BiomeId Id)
        {
            var StatusEmitter = Emitter.Resolve(RuntimeRoot, Id, "translated SporeCloud emitter");
            var RunSetting = Setting.Resolve(RuntimeRoot, Id, "translated SporeCloud run setting");
            var ResolvedPresentation = Presentation.Resolve(RuntimeRoot, Id, "native resolver presentation");
            if (Source.SurfaceRenderer || Source.GeneratedSurfaceMaterial || Source.Presentation != ResolvedPresentation)
                throw new InvalidOperationException($"Biome '{Id}' changed a native resolver after sealing.");
            TranslatedTransform.Validate(StatusEmitter.transform, Id, "translated SporeCloud");
            if (StatusEmitter.gameObject.activeSelf || !StatusEmitter.enabled || !RunSetting.enabled
                || RunSetting.gameObject != StatusEmitter.gameObject
                || StatusEmitter.GetComponents<Component>().Length != 3 || StatusEmitter.GetComponent<PhotonView>()
                || StatusEmitter.GetComponent<Collider>())
                throw new InvalidOperationException($"Biome '{Id}' changed its translated SporeCloud shape after sealing.");
            return ResolvedNativeResolver.CreateSporeCloud(
                ResolvedRoot, Volume, PresentationIndex, StatusEmitter, RunSetting);
        }

        protected override void WriteBindings(BinaryWriter Writer)
        {
            Writer.Write(false); Writer.Write(false); Writer.Write(false); Writer.Write(false);
            Writer.Write(true); Emitter.WriteManifest(Writer);
            Writer.Write(true); Setting.WriteManifest(Writer);
            Writer.Write(true); Presentation.WriteManifest(Writer);
        }

        static void ValidateSporeCloudMetadata(
            PLLNativeResolver Source,
            BiomeId Id,
            IReadOnlyList<EffectAuthoringContract> Effects,
            int PresentationIndex,
            bool Argument)
        {
            if (Source.SurfaceRenderer || Source.GeneratedSurfaceMaterial || PresentationIndex < 0
                || PresentationIndex >= Effects.Count || Effects[PresentationIndex].Source.Mode != PllEffectMode.Zone)
                throw Invalid(Id, Source, "native weather and SporeCloud require one creator-owned Zone presentation", Argument);
            if (Source.Volume.Shape != PllZoneShape.Sphere || Effects[PresentationIndex].Source.Duration != 0f
                || Effects[PresentationIndex].Root.Source == Source.Root
                || !TransformOwnership.Contains(Source.Root.transform, Effects[PresentationIndex].Root.Source.transform))
                throw Invalid(Id, Source, "SporeCloud requires a strict owned duration-zero Sphere presentation", Argument);
            _ = NativeResolverVolume.WorldSphereRadius(Source.Root.transform, Source.Volume);
        }
    }

    static bool Same<T>(T[] Left, T[] Right) where T : IEquatable<T>
    {
        if (Left.Length != Right.Length) return false;
        for (var i = 0; i < Left.Length; i++) if (!Left[i].Equals(Right[i])) return false;
        return true;
    }
}

internal readonly struct LocalTransformFingerprint
{
    LocalTransformFingerprint(Vector3 Position, Quaternion Rotation, Vector3 Scale)
    {
        this.Position = Position; this.Rotation = Rotation; this.Scale = Scale;
    }

    Vector3 Position { get; }
    Quaternion Rotation { get; }
    Vector3 Scale { get; }

    internal static LocalTransformFingerprint Create(Transform Value)
    {
        if (!Value) throw new ArgumentNullException(nameof(Value));
        return new LocalTransformFingerprint(Value.localPosition, Value.localRotation, Value.localScale);
    }

    internal void Validate(Transform Value, BiomeId Id, string Name, bool Argument = false)
    {
        if (Value && Value.localPosition == Position && Value.localRotation == Rotation && Value.localScale == Scale)
            return;
        var Message = $"Biome '{Id}' changed its {Name} local transform after staging.";
        if (Argument) throw new ArgumentException(Message, "source");
        throw new InvalidOperationException(Message);
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        ManifestEncoding.WriteVector(Writer, Position);
        ManifestEncoding.WriteScalar(Writer, Rotation.x); ManifestEncoding.WriteScalar(Writer, Rotation.y);
        ManifestEncoding.WriteScalar(Writer, Rotation.z); ManifestEncoding.WriteScalar(Writer, Rotation.w);
        ManifestEncoding.WriteVector(Writer, Scale);
    }
}
