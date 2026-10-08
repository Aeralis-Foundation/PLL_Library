using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PLL.Library;

internal sealed class RegisteredSegment
{
    sealed class RespawnBinding
    {
        readonly TransformBinding ChestBinding;
        readonly TransformBinding SkeletonBinding;

        RespawnBinding(
            RespawnChest SourceValue,
            GameObject SkeletonValue,
            TransformBinding ChestBinding,
            TransformBinding SkeletonBinding)
        {
            Source = SourceValue;
            Skeleton = SkeletonValue;
            this.ChestBinding = ChestBinding;
            this.SkeletonBinding = SkeletonBinding;
        }

        internal RespawnChest Source { get; }
        internal GameObject Skeleton { get; }

        internal static RespawnBinding? CreateOptional(Transform TemplateRoot, Transform FeatureRoot, string Name)
        {
            var Chest = NativePrefabContracts.ValidateOptionalRespawn(FeatureRoot, $"custom {Name}");
            if (!Chest) return null;
            var SkeletonValue = Chest.skeleton;
            return new RespawnBinding(
                Chest,
                SkeletonValue,
                TransformBinding.Create(TemplateRoot, Chest.transform),
                TransformBinding.Create(TemplateRoot, SkeletonValue.transform));
        }

        internal void Validate(Transform TemplateRoot, Transform FeatureRoot, string Id, string Name)
        {
            if (!Source || !Skeleton)
                throw new InvalidOperationException($"Registered segment '{Id}' contains a destroyed {Name} RespawnChest reference.");
            ChestBinding.Validate(TemplateRoot, Id, $"{Name} respawn chest");
            SkeletonBinding.Validate(TemplateRoot, Id, $"{Name} respawn chest skeleton");
            RespawnChest? Chest;
            try
            {
                Chest = NativePrefabContracts.ValidateOptionalRespawn(FeatureRoot, $"custom {Name}");
            }
            catch (ArgumentException Exception)
            {
                throw new InvalidOperationException(
                    $"Registered segment '{Id}' changed its {Name} RespawnChest contract before sealing.", Exception);
            }
            if (!ReferenceEquals(Chest, Source)
                || Source.transform != ChestBinding.Source
                || !ReferenceEquals(Source.skeleton, Skeleton)
                || Skeleton.transform != SkeletonBinding.Source
                || !TransformOwnership.Contains(FeatureRoot, Skeleton.transform))
                throw new InvalidOperationException($"Registered segment '{Id}' changed its {Name} RespawnChest contract before sealing.");
        }

        internal RespawnChest Resolve(Transform TemplateRoot, Transform FeatureRoot, string Id, string Name)
        {
            var ChestTransform = ChestBinding.ResolveValidated(TemplateRoot, Id, $"{Name} respawn chest");
            var SkeletonTransform = SkeletonBinding.ResolveValidated(TemplateRoot, Id, $"{Name} respawn chest skeleton");
            RespawnChest? Chest;
            try
            {
                Chest = NativePrefabContracts.ValidateOptionalRespawn(FeatureRoot, $"custom {Name}", false);
            }
            catch (ArgumentException Exception)
            {
                throw new InvalidOperationException(
                    $"Registered segment '{Id}' clone changed its {Name} RespawnChest contract.", Exception);
            }
            if (!Chest || !ReferenceEquals(Chest.transform, ChestTransform)
                || !Chest.skeleton
                || !ReferenceEquals(Chest.skeleton.transform, SkeletonTransform)
                || !TransformOwnership.Contains(FeatureRoot, SkeletonTransform)
                || (FeatureRoot.gameObject.activeInHierarchy
                    && !ReferenceEquals(FeatureRoot.GetComponentInChildren<RespawnChest>(), Chest)))
                throw new InvalidOperationException($"Registered segment '{Id}' clone changed its {Name} RespawnChest contract.");
            return Chest;
        }

        internal void WriteManifest(BinaryWriter Writer)
        {
            ChestBinding.WriteManifest(Writer);
            SkeletonBinding.WriteManifest(Writer);
        }
    }

    sealed class AuthoringAnchorsBinding
    {
        readonly PllBiomePrefabAnchors SourceValue;
        readonly TransformBinding Component;
        readonly TransformBinding FloorRoot;
        readonly TransformBinding CampfireRoot;
        readonly TransformBinding? StatueRoot;
        readonly TransformBinding StartAnchor;
        readonly TransformBinding PlayerFormationRoot;
        readonly TransformBinding PlaneAnchor;
        readonly TransformBinding FogBeginAnchor;
        readonly TransformBinding FogCutoffAnchor;

        AuthoringAnchorsBinding(
            PllBiomePrefabAnchors SourceValue,
            TransformBinding Component,
            TransformBinding FloorRoot,
            TransformBinding CampfireRoot,
            TransformBinding? StatueRoot,
            TransformBinding StartAnchor,
            TransformBinding PlayerFormationRoot,
            TransformBinding PlaneAnchor,
            TransformBinding FogBeginAnchor,
            TransformBinding FogCutoffAnchor)
        {
            this.SourceValue = SourceValue;
            this.Component = Component;
            this.FloorRoot = FloorRoot;
            this.CampfireRoot = CampfireRoot;
            this.StatueRoot = StatueRoot;
            this.StartAnchor = StartAnchor;
            this.PlayerFormationRoot = PlayerFormationRoot;
            this.PlaneAnchor = PlaneAnchor;
            this.FogBeginAnchor = FogBeginAnchor;
            this.FogCutoffAnchor = FogCutoffAnchor;
        }

        internal static AuthoringAnchorsBinding Create(
            Transform TemplateRoot,
            PllBiomePrefabAnchors SourceValue,
            Transform SegmentParentValue,
            Transform SegmentCampfireValue,
            Transform StartValue)
        {
            if (!SourceValue) throw new ArgumentException("The custom segment authoring anchors were destroyed.", "definition");
            var Components = TemplateRoot.GetComponentsInChildren<PllBiomePrefabAnchors>(true);
            if (Components.Length != 1 || !ReferenceEquals(Components[0], SourceValue))
                throw new ArgumentException("The template must own exactly the supplied PllBiomePrefabAnchors.", "definition");
            var Statue = SourceValue.StatueRoot;
            if (!SourceValue.FloorRoot || !SourceValue.CampfireRoot || Statue is not null && !Statue
                || !SourceValue.StartAnchor || !SourceValue.PlayerFormationRoot || !SourceValue.PlaneAnchor
                || !SourceValue.FogBeginAnchor || !SourceValue.FogCutoffAnchor)
                throw new ArgumentException("The custom segment authoring anchors contain a destroyed reference.", "definition");
            if (!ReferenceEquals(SourceValue.FloorRoot.transform, SegmentParentValue)
                || !ReferenceEquals(SourceValue.CampfireRoot.transform, SegmentCampfireValue)
                || !ReferenceEquals(SourceValue.StartAnchor, StartValue))
                throw new ArgumentException("The authored floor, campfire, and Start anchors must match the runtime and Start port exactly.", "definition");
            if (!TransformOwnership.SamePose(SourceValue.PlayerFormationRoot.transform, StartValue))
                throw new ArgumentException("The authored player formation root must match the Start anchor position and rotation.", "definition");
            RequireOwned(TemplateRoot, SourceValue.transform, "anchor component");
            RequireOwned(TemplateRoot, SegmentParentValue, "floor root");
            RequireOwned(TemplateRoot, SegmentCampfireValue, "campfire root");
            RequireOwned(TemplateRoot, StartValue, "Start anchor");
            RequireOwned(TemplateRoot, SourceValue.PlayerFormationRoot.transform, "player formation root");
            RequireOwned(TemplateRoot, SourceValue.PlaneAnchor, "plane anchor");
            RequireOwned(TemplateRoot, SourceValue.FogBeginAnchor, "fog begin anchor");
            RequireOwned(TemplateRoot, SourceValue.FogCutoffAnchor, "fog cutoff anchor");
            if (Statue)
            {
                RequireOwned(TemplateRoot, Statue.transform, "statue root");
                if (!TransformOwnership.Contains(SegmentParentValue, Statue.transform) && !TransformOwnership.Contains(SegmentCampfireValue, Statue.transform))
                    throw new ArgumentException("The authored statue root must belong to a segment feature root.", "definition");
            }
            return new AuthoringAnchorsBinding(
                SourceValue,
                TransformBinding.Create(TemplateRoot, SourceValue.transform),
                TransformBinding.Create(TemplateRoot, SegmentParentValue),
                TransformBinding.Create(TemplateRoot, SegmentCampfireValue),
                Statue ? TransformBinding.Create(TemplateRoot, Statue.transform) : null,
                TransformBinding.Create(TemplateRoot, StartValue),
                TransformBinding.Create(TemplateRoot, SourceValue.PlayerFormationRoot.transform),
                TransformBinding.Create(TemplateRoot, SourceValue.PlaneAnchor),
                TransformBinding.Create(TemplateRoot, SourceValue.FogBeginAnchor),
                TransformBinding.Create(TemplateRoot, SourceValue.FogCutoffAnchor));
        }

        internal void Validate(
            Transform TemplateRoot,
            string Id,
            Transform SegmentParentValue,
            Transform SegmentCampfireValue,
            Transform StartValue)
        {
            var Resolved = Resolve(TemplateRoot, Id, SegmentParentValue, SegmentCampfireValue, StartValue);
            if (!ReferenceEquals(Resolved, SourceValue))
                throw new InvalidOperationException($"Registered segment '{Id}' replaced its authoring anchors before sealing.");
        }

        internal PllBiomePrefabAnchors Resolve(
            Transform TemplateRoot,
            string Id,
            Transform SegmentParentValue,
            Transform SegmentCampfireValue,
            Transform StartValue)
        {
            var ComponentTransform = Component.ResolveValidated(TemplateRoot, Id, "authoring anchor component");
            var Components = TemplateRoot.GetComponentsInChildren<PllBiomePrefabAnchors>(true);
            var LocalComponents = ComponentTransform.GetComponents<PllBiomePrefabAnchors>();
            if (Components.Length != 1 || LocalComponents.Length != 1
                || !ReferenceEquals(Components[0], LocalComponents[0]))
                throw new InvalidOperationException($"Registered segment '{Id}' changed its authoring anchor component contract.");
            var Anchors = LocalComponents[0];
            var Floor = FloorRoot.ResolveValidated(TemplateRoot, Id, "authored floor root");
            var Campfire = CampfireRoot.ResolveValidated(TemplateRoot, Id, "authored campfire root");
            var Statue = StatueRoot?.ResolveValidated(TemplateRoot, Id, "authored statue root");
            var AuthoredStart = StartAnchor.ResolveValidated(TemplateRoot, Id, "authored Start anchor");
            var Formation = PlayerFormationRoot.ResolveValidated(TemplateRoot, Id, "authored player formation root");
            var Plane = PlaneAnchor.ResolveValidated(TemplateRoot, Id, "authored plane anchor");
            var FogBegin = FogBeginAnchor.ResolveValidated(TemplateRoot, Id, "authored fog begin anchor");
            var FogCutoff = FogCutoffAnchor.ResolveValidated(TemplateRoot, Id, "authored fog cutoff anchor");
            if (!Anchors || !Anchors.FloorRoot || !Anchors.CampfireRoot || !Anchors.StartAnchor
                || !Anchors.PlayerFormationRoot || !Anchors.PlaneAnchor || !Anchors.FogBeginAnchor
                || !Anchors.FogCutoffAnchor
                || !ReferenceEquals(Anchors.FloorRoot.transform, Floor)
                || !ReferenceEquals(Anchors.CampfireRoot.transform, Campfire)
                || !ReferenceEquals(Anchors.StartAnchor, AuthoredStart)
                || !ReferenceEquals(Anchors.PlayerFormationRoot.transform, Formation)
                || !ReferenceEquals(Anchors.PlaneAnchor, Plane)
                || !ReferenceEquals(Anchors.FogBeginAnchor, FogBegin)
                || !ReferenceEquals(Anchors.FogCutoffAnchor, FogCutoff)
                || !ReferenceEquals(Floor, SegmentParentValue)
                || !ReferenceEquals(Campfire, SegmentCampfireValue)
                || !ReferenceEquals(AuthoredStart, StartValue)
                || !TransformOwnership.SamePose(Formation, AuthoredStart)
                || (Statue is null) != ReferenceEquals(Anchors.StatueRoot, null)
                || Statue is not null && (!Anchors.StatueRoot
                    || !ReferenceEquals(Anchors.StatueRoot!.transform, Statue))
                || !StableOwned(TemplateRoot, ComponentTransform) || !StableOwned(TemplateRoot, Floor)
                || !StableOwned(TemplateRoot, Campfire) || !StableOwned(TemplateRoot, AuthoredStart)
                || !StableOwned(TemplateRoot, Formation) || !StableOwned(TemplateRoot, Plane)
                || !StableOwned(TemplateRoot, FogBegin) || !StableOwned(TemplateRoot, FogCutoff)
                || Statue is not null && (!StableOwned(TemplateRoot, Statue)
                    || !TransformOwnership.Contains(SegmentParentValue, Statue) && !TransformOwnership.Contains(SegmentCampfireValue, Statue)))
                throw new InvalidOperationException($"Registered segment '{Id}' changed its exact authoring anchor bindings.");
            return Anchors;
        }

        internal void WriteManifest(BinaryWriter Writer)
        {
            Component.WriteManifest(Writer);
            FloorRoot.WriteManifest(Writer); CampfireRoot.WriteManifest(Writer);
            Writer.Write(StatueRoot is not null); StatueRoot?.WriteManifest(Writer);
            StartAnchor.WriteManifest(Writer); PlayerFormationRoot.WriteManifest(Writer);
            PlaneAnchor.WriteManifest(Writer); FogBeginAnchor.WriteManifest(Writer); FogCutoffAnchor.WriteManifest(Writer);
        }

        static void RequireOwned(Transform TemplateRoot, Transform Value, string Name)
        {
            if (!StableOwned(TemplateRoot, Value))
                throw new ArgumentException($"The authored {Name} must belong to the template outside every PropSpawner hierarchy.", "definition");
        }

        static bool StableOwned(Transform TemplateRoot, Transform Value)
        {
            if (!TransformOwnership.Contains(TemplateRoot, Value)) return false;
            for (var Current = Value; Current != TemplateRoot; Current = Current.parent)
                if (Current.GetComponent<PropSpawner>()) return false;
            return true;
        }
    }

    readonly struct CloneState
    {
        internal CloneState(
            GameObject RootValue,
            CustomSegmentRuntime RuntimeValue,
            GameObject SegmentParentValue,
            GameObject SegmentCampfireValue,
            FogSphereOrigin FogOriginValue,
            PllFogAuthoring FogAuthoringValue,
            FogCutoutZone FogCutoutValue,
            MountainProgressHandler.ProgressPoint ProgressPointValue,
            RespawnChest? CampfireRespawnChestValue,
            GameObject? CampfireRespawnSkeletonValue,
            RespawnChest? SegmentRespawnChestValue,
            GameObject? SegmentRespawnSkeletonValue,
            PllBiomePrefabAnchors? AuthoringAnchorsValue,
            FinalForceOccurrence FinalForceValue,
            Transform WallNextValue,
            Transform WallPreviousValue,
            Transform EntryValue,
            Transform ExitValue,
            Transform StartValue,
            SegmentPort EntryPortValue,
            SegmentPort ExitPortValue,
            SegmentPort StartPortValue)
        {
            Root = RootValue;
            Runtime = RuntimeValue;
            SegmentParent = SegmentParentValue;
            SegmentCampfire = SegmentCampfireValue;
            FogOrigin = FogOriginValue;
            FogAuthoring = FogAuthoringValue;
            FogCutout = FogCutoutValue;
            ProgressPoint = ProgressPointValue;
            CampfireRespawnChest = CampfireRespawnChestValue;
            CampfireRespawnSkeleton = CampfireRespawnSkeletonValue;
            SegmentRespawnChest = SegmentRespawnChestValue;
            SegmentRespawnSkeleton = SegmentRespawnSkeletonValue;
            AuthoringAnchors = AuthoringAnchorsValue;
            FinalForce = FinalForceValue;
            WallNext = WallNextValue;
            WallPrevious = WallPreviousValue;
            Entry = EntryValue;
            Exit = ExitValue;
            Start = StartValue;
            EntryPort = EntryPortValue;
            ExitPort = ExitPortValue;
            StartPort = StartPortValue;
        }

        internal GameObject Root { get; }
        internal CustomSegmentRuntime Runtime { get; }
        internal GameObject SegmentParent { get; }
        internal GameObject SegmentCampfire { get; }
        internal FogSphereOrigin FogOrigin { get; }
        internal PllFogAuthoring FogAuthoring { get; }
        internal FogCutoutZone FogCutout { get; }
        internal MountainProgressHandler.ProgressPoint ProgressPoint { get; }
        internal RespawnChest? CampfireRespawnChest { get; }
        internal GameObject? CampfireRespawnSkeleton { get; }
        internal RespawnChest? SegmentRespawnChest { get; }
        internal GameObject? SegmentRespawnSkeleton { get; }
        internal PllBiomePrefabAnchors? AuthoringAnchors { get; }
        internal FinalForceOccurrence FinalForce { get; }
        internal Transform WallNext { get; }
        internal Transform WallPrevious { get; }
        internal Transform Entry { get; }
        internal Transform Exit { get; }
        internal Transform Start { get; }
        internal SegmentPort EntryPort { get; }
        internal SegmentPort ExitPort { get; }
        internal SegmentPort StartPort { get; }

        internal ResolvedSegment CreateResolved() => new(
            Root,
            Runtime,
            SegmentParent,
            SegmentCampfire,
            CampfireRespawnChest,
            SegmentRespawnChest,
            AuthoringAnchors,
            FinalForce,
            Entry,
            Exit,
            Start,
            EntryPort,
            ExitPort,
            StartPort);
    }

    sealed class CloneContract
    {
        readonly FogSphereOrigin FogOrigin;
        readonly PllFogAuthoring FogAuthoring;
        readonly FogCutoutZone FogCutout;
        readonly MountainProgressHandler.ProgressPoint ProgressPoint;
        readonly GameObject? CampfireRespawnSkeleton;
        readonly GameObject? SegmentRespawnSkeleton;
        readonly Transform WallNext;
        readonly Transform WallPrevious;

        internal CloneContract(ResolvedSegment ResolvedValue, CloneState State)
        {
            Resolved = ResolvedValue;
            FogOrigin = State.FogOrigin;
            FogAuthoring = State.FogAuthoring;
            FogCutout = State.FogCutout;
            ProgressPoint = State.ProgressPoint;
            CampfireRespawnSkeleton = State.CampfireRespawnSkeleton;
            SegmentRespawnSkeleton = State.SegmentRespawnSkeleton;
            WallNext = State.WallNext;
            WallPrevious = State.WallPrevious;
        }

        internal ResolvedSegment Resolved { get; }

        internal bool Matches(CloneState Current)
        {
            var Expected = Resolved;
            return Expected.Root.Value && Expected.Runtime && Expected.SegmentParent
                && Expected.SegmentCampfire && Expected.Entry && Expected.Exit && Expected.Start
                && Expected.EntryPort && Expected.ExitPort && Expected.StartPort
                && FogOrigin && FogAuthoring && FogCutout && ProgressPoint is { } && ProgressPoint.transform
                && WallNext && WallPrevious
                && ReferenceEquals(Expected.Root.Value.gameObject, Current.Root)
                && ReferenceEquals(Expected.Runtime, Current.Runtime)
                && ReferenceEquals(Expected.SegmentParent, Current.SegmentParent)
                && ReferenceEquals(Expected.SegmentCampfire, Current.SegmentCampfire)
                && ReferenceEquals(FogOrigin, Current.FogOrigin)
                && ReferenceEquals(FogAuthoring, Current.FogAuthoring)
                && ReferenceEquals(FogCutout, Current.FogCutout)
                && ReferenceEquals(ProgressPoint, Current.ProgressPoint)
                && OptionalMatches(Expected.CampfireRespawnChest, Current.CampfireRespawnChest)
                && OptionalMatches(CampfireRespawnSkeleton, Current.CampfireRespawnSkeleton)
                && OptionalMatches(Expected.SegmentRespawnChest, Current.SegmentRespawnChest)
                && OptionalMatches(SegmentRespawnSkeleton, Current.SegmentRespawnSkeleton)
                && OptionalMatches(Expected.AuthoringAnchors, Current.AuthoringAnchors)
                && ReferenceEquals(Expected.FinalForce, Current.FinalForce)
                && ReferenceEquals(WallNext, Current.WallNext)
                && ReferenceEquals(WallPrevious, Current.WallPrevious)
                && ReferenceEquals(Expected.Entry, Current.Entry)
                && ReferenceEquals(Expected.Exit, Current.Exit)
                && ReferenceEquals(Expected.Start, Current.Start)
                && ReferenceEquals(Expected.EntryPort, Current.EntryPort)
                && ReferenceEquals(Expected.ExitPort, Current.ExitPort)
                && ReferenceEquals(Expected.StartPort, Current.StartPort);
        }

        static bool OptionalMatches(UnityEngine.Object? Expected, UnityEngine.Object? Current)
        {
            if (ReferenceEquals(Expected, null)) return ReferenceEquals(Current, null);
            return Expected && Current && ReferenceEquals(Expected, Current);
        }
    }

    readonly SegmentPort[] Ports;
    readonly ConditionalWeakTable<GameObject, CloneContract> CloneContracts = new();
    readonly CustomSegmentRuntime RuntimeValue;
    readonly FogSphereOrigin FogOriginComponent;
    readonly ComponentBinding<PllFogAuthoring> FogAuthoringBinding;
    readonly RespawnBinding? CampfireRespawn;
    readonly RespawnBinding? SegmentRespawn;
    readonly AuthoringAnchorsBinding? AuthoringAnchorsValue;
    readonly bool FogOriginEnabled;
    readonly float FogOriginSize;
    readonly bool FogOriginDisablesFog;
    readonly bool FogCutoutEnabled;
    readonly float FogCutoutMin;
    readonly float FogCutoutMax;
    readonly float FogCutoutAmount;
    readonly string? ProgressTitle;
    readonly AudioClip? ProgressClip;
    readonly string? ProgressClipName;
    readonly ACHIEVEMENTTYPE ProgressAchievement;
    readonly Biome.BiomeType ProgressBiome;
    readonly Pose TemplatePoseValue;
    readonly Vector3 TemplateScaleValue;

    RegisteredSegment(
        CustomSegmentDefinition Definition,
        RegisteredBiome BiomeValue,
        TransformBinding SegmentParentValue,
        TransformBinding SegmentCampfireValue,
        TransformBinding RuntimeBindingValue,
        TransformBinding FogOriginValue,
        TransformBinding FogCutoutBindingValue,
        TransformBinding ProgressPointBindingValue,
        RespawnBinding? CampfireRespawn,
        RespawnBinding? SegmentRespawn,
        AuthoringAnchorsBinding? AuthoringAnchorsValue,
        FinalForceAuthoringContract FinalForceValue,
        TransformBinding WallNextValue,
        TransformBinding WallPreviousValue,
        PortBinding EntryValue,
        PortBinding ExitValue,
        PortBinding StartValue,
        SegmentPort[] Ports,
        CustomSegmentRuntime RuntimeValue,
        FogSphereOrigin FogOriginComponent,
        ComponentBinding<PllFogAuthoring> FogAuthoringBinding,
        PllFogMode FogModeValue,
        FogCutoutZone FogCutoutComponent,
        MountainProgressHandler.ProgressPoint ProgressPointComponent)
    {
        Id = Definition.Id;
        Revision = Definition.Revision;
        SegmentTypeId = Definition.SegmentTypeId;
        Biome = BiomeValue;
        Template = Definition.Template;
        SegmentParent = SegmentParentValue;
        SegmentCampfire = SegmentCampfireValue;
        RuntimeBinding = RuntimeBindingValue;
        FogOrigin = FogOriginValue;
        FogCutoutBinding = FogCutoutBindingValue;
        ProgressPointBinding = ProgressPointBindingValue;
        this.CampfireRespawn = CampfireRespawn;
        this.SegmentRespawn = SegmentRespawn;
        this.AuthoringAnchorsValue = AuthoringAnchorsValue;
        FinalForce = FinalForceValue;
        WallNext = WallNextValue;
        WallPrevious = WallPreviousValue;
        FogCutout = FogCutoutComponent;
        ProgressPoint = ProgressPointComponent;
        DayNightProfile = BiomeValue.DayNightProfile;
        Entry = EntryValue;
        Exit = ExitValue;
        Start = StartValue;
        this.Ports = Ports;
        this.RuntimeValue = RuntimeValue;
        this.FogOriginComponent = FogOriginComponent;
        this.FogAuthoringBinding = FogAuthoringBinding;
        FogMode = FogModeValue;
        FogOriginEnabled = FogOriginComponent.enabled;
        FogOriginSize = FogOriginComponent.size;
        FogOriginDisablesFog = FogMode != PllFogMode.Normal;
        FogCutoutEnabled = FogCutoutComponent.enabled;
        FogCutoutMin = FogCutoutComponent.min;
        FogCutoutMax = FogCutoutComponent.max;
        FogCutoutAmount = FogCutoutComponent.amount;
        ProgressTitle = ProgressPointComponent.title;
        ProgressClip = ProgressPointComponent.clip;
        ProgressClipName = ProgressClip ? ProgressClip.name : null;
        ProgressAchievement = ProgressPointComponent.achievement;
        ProgressBiome = ProgressPointComponent.biome;
        TemplatePoseValue = new Pose(Template.transform.localPosition, Template.transform.localRotation);
        TemplateScaleValue = Template.transform.localScale;
    }

    internal string Id { get; }
    internal string Revision { get; }
    internal string SegmentTypeId { get; }
    internal RegisteredBiome Biome { get; }
    internal string BiomeId => Biome.Id;
    internal PllBiomeBehavior Behavior => Biome.Behavior;
    internal GameObject Template { get; }
    internal TransformBinding SegmentParent { get; }
    internal TransformBinding SegmentCampfire { get; }
    internal TransformBinding RuntimeBinding { get; }
    internal TransformBinding FogOrigin { get; }
    internal TransformBinding FogCutoutBinding { get; }
    internal TransformBinding ProgressPointBinding { get; }
    internal RespawnChest? CampfireRespawnChest => CampfireRespawn?.Source;
    internal RespawnChest? SegmentRespawnChest => SegmentRespawn?.Source;
    internal FinalForceAuthoringContract FinalForce { get; }
    internal TransformBinding WallNext { get; }
    internal TransformBinding WallPrevious { get; }
    internal FogCutoutZone FogCutout { get; }
    internal PllFogMode FogMode { get; }
    internal MountainProgressHandler.ProgressPoint ProgressPoint { get; }
    internal DayNightProfile DayNightProfile { get; }
    internal PortBinding Entry { get; }
    internal PortBinding Exit { get; }
    internal PortBinding Start { get; }
    internal Pose TemplatePose => TemplatePoseValue;
    internal Vector3 TemplateScale => TemplateScaleValue;

    internal static RegisteredSegment Create(CustomSegmentDefinition Definition, RegisteredBiome BiomeValue)
    {
        if (!string.Equals(Definition.BiomeId, BiomeValue.Id, StringComparison.Ordinal))
            throw new ArgumentException("The segment biome does not match the registered biome.", nameof(Definition));
        var TemplateObject = Definition.Template;
        var RuntimeValue = Definition.Runtime;
        if (!TemplateObject)
            throw new ArgumentException("The custom segment template was destroyed.", nameof(Definition));
        if (!RuntimeValue)
            throw new ArgumentException("The custom segment runtime was destroyed.", nameof(Definition));
        var SegmentParentObject = RuntimeValue.SegmentParent;
        var SegmentCampfireObject = RuntimeValue.SegmentCampfire;
        var FogOriginComponent = RuntimeValue.FogOrigin;
        var FogCutoutComponent = RuntimeValue.FogCutout;
        var ProgressPointValue = RuntimeValue.ProgressPoint;
        if (!SegmentParentObject || !SegmentCampfireObject || !FogOriginComponent || !FogCutoutComponent
            || ProgressPointValue is null || !ProgressPointValue.transform)
            throw new ArgumentException("The custom segment runtime contains a destroyed reference.", nameof(Definition));
        if (ProgressPointValue.clip is not null && !ProgressPointValue.clip)
            throw new ArgumentException("The custom progress point audio clip was destroyed.", nameof(Definition));
        ValidateFinite(FogOriginComponent.size, "fog origin size");
        ValidateFinite(FogCutoutComponent.min, "fog cutout minimum");
        ValidateFinite(FogCutoutComponent.max, "fog cutout maximum");
        ValidateFinite(FogCutoutComponent.amount, "fog cutout amount");

        var TemplateRoot = TemplateObject.transform;
        if (TemplateObject.scene.IsValid())
            throw new ArgumentException("The segment template must be a persistent prefab, not a scene object.", nameof(Definition));
        if (TemplateRoot.parent)
            throw new ArgumentException("The segment template must be a prefab root.", nameof(Definition));
        if (TemplateObject.activeSelf)
            throw new ArgumentException("The segment template must be inactive so clones can be prepared before Awake.", nameof(Definition));
        if (TemplateObject.GetComponentsInChildren<SpawnPoint>(true).Length != 0)
            throw new ArgumentException("Custom segment prefabs must not contain SpawnPoint components; PLL preserves and relocates PEAK's native four-player formation.", nameof(Definition));
        ValidateFinite(TemplateRoot.localPosition, "template position");
        ValidateFinite(TemplateRoot.localRotation, "template rotation");
        ValidateScale(TemplateRoot.localScale, "template scale");

        var SegmentParentValue = SegmentParentObject.transform;
        var SegmentCampfireValue = SegmentCampfireObject.transform;
        if (!TransformOwnership.Contains(TemplateRoot, SegmentParentValue) || !TransformOwnership.Contains(TemplateRoot, SegmentCampfireValue))
            throw new ArgumentException("The template must own both segment roots.", nameof(Definition));
        if (TransformOwnership.Overlaps(SegmentParentValue, SegmentCampfireValue))
            throw new ArgumentException("The segment and campfire roots must be disjoint.", nameof(Definition));

        NativePrefabContracts.ValidateCampfireRoot(SegmentParentValue, SegmentCampfireValue, "custom campfire root");
        var CampfireRespawn = RespawnBinding.CreateOptional(TemplateRoot, SegmentCampfireValue, "campfire root");
        var SegmentRespawn = RespawnBinding.CreateOptional(TemplateRoot, SegmentParentValue, "segment root");

        if (RuntimeValue.Registered)
            throw new ArgumentException("A CustomSegmentRuntime cannot belong to more than one registered definition.", nameof(Definition));
        var Runtimes = TemplateObject.GetComponentsInChildren<CustomSegmentRuntime>(true);
        if (RuntimeValue.gameObject != TemplateObject || Runtimes.Length != 1 || !ReferenceEquals(Runtimes[0], RuntimeValue))
            throw new ArgumentException("The template root must contain exactly the supplied CustomSegmentRuntime.", nameof(Definition));
        var RuntimeBindingValue = TransformBinding.Create(TemplateRoot, RuntimeValue.transform);

        var Origin = FogOriginComponent.transform;
        if (!TransformOwnership.Contains(TemplateRoot, Origin) || TransformOwnership.Contains(SegmentParentValue, Origin) || TransformOwnership.Contains(SegmentCampfireValue, Origin)
            || TransformOwnership.HasPropSpawnerInPath(TemplateRoot, Origin))
            throw new ArgumentException("The fog origin must be template-owned and outside the feature roots.", nameof(Definition));
        var Origins = Origin.gameObject.GetComponents<FogSphereOrigin>();
        if (Origins.Length != 1 || !ReferenceEquals(Origins[0], FogOriginComponent))
            throw new ArgumentException("The custom fog origin must contain exactly the supplied FogSphereOrigin component.", nameof(Definition));
        var FogAuthoringComponents = TemplateObject.GetComponentsInChildren<PllFogAuthoring>(true);
        if (FogAuthoringComponents.Length != 1)
            throw new ArgumentException("The custom segment must contain exactly one PllFogAuthoring component.", nameof(Definition));
        var FogAuthoringValue = FogAuthoringComponents[0];
        if (!FogAuthoringValue || !FogAuthoringValue.enabled || FogAuthoringValue.gameObject != Origin.gameObject
            || !Enum.IsDefined(typeof(PllFogMode), FogAuthoringValue.Mode))
            throw new ArgumentException(
                "PllFogAuthoring must be enabled with a defined mode on the exact custom fog origin GameObject.",
                nameof(Definition));
        var FogModeValue = FogAuthoringValue.Mode;
        if (FogOriginComponent.disableFog != (FogModeValue != PllFogMode.Normal))
            throw new ArgumentException(
                "The staged FogSphereOrigin.disableFog value must be derived from PllFogAuthoring.Mode.",
                nameof(Definition));
        var FogOriginValue = TransformBinding.Create(TemplateRoot, Origin);
        var WallNextValue = TransformBinding.Create(TemplateRoot, Unique(Origin, "Fogwall_Next"));
        var WallPreviousValue = TransformBinding.Create(TemplateRoot, Unique(Origin, "Fogwall_Prev"));

        var Cutout = FogCutoutComponent.transform;
        var Progress = ProgressPointValue.transform;
        if (!TransformOwnership.Contains(TemplateRoot, Cutout) || TransformOwnership.Contains(SegmentParentValue, Cutout) || TransformOwnership.Contains(SegmentCampfireValue, Cutout)
            || !TransformOwnership.Contains(TemplateRoot, Progress) || TransformOwnership.Contains(SegmentParentValue, Progress) || TransformOwnership.Contains(SegmentCampfireValue, Progress)
            || TransformOwnership.HasPropSpawnerInPath(TemplateRoot, Cutout)
            || TransformOwnership.HasPropSpawnerInPath(TemplateRoot, Progress))
            throw new ArgumentException("The fog cutout and progress point must be template-owned and outside the feature roots.", nameof(Definition));
        var Cutouts = Cutout.gameObject.GetComponents<FogCutoutZone>();
        if (Cutouts.Length != 1 || !ReferenceEquals(Cutouts[0], FogCutoutComponent))
            throw new ArgumentException("The custom fog cutout must contain exactly the supplied FogCutoutZone component.", nameof(Definition));
        var FogCutoutValue = TransformBinding.Create(TemplateRoot, Cutout);
        var ProgressBinding = TransformBinding.Create(TemplateRoot, Progress);

        var Components = TemplateObject.GetComponentsInChildren<SegmentPort>(true);
        if (Components.Length != 3)
            throw new ArgumentException("The template must contain exactly one Entry, Exit, and Start SegmentPort.", nameof(Definition));

        SegmentPort? EntryValue = null, ExitValue = null, StartValue = null;
        foreach (var Port in Components)
        {
            if (Port.Registered)
                throw new ArgumentException("A SegmentPort cannot belong to more than one registered definition.", nameof(Definition));
            if (!TransformOwnership.Contains(TemplateRoot, Port.transform) || TransformOwnership.Contains(SegmentParentValue, Port.transform) || TransformOwnership.Contains(SegmentCampfireValue, Port.transform)
                || TransformOwnership.HasPropSpawnerInPath(TemplateRoot, Port.transform))
                throw new ArgumentException("Segment ports must be template-owned and outside the feature roots.", nameof(Definition));
            SegmentRegistry.ValidateToken(Port.Connector, nameof(SegmentPort.Connector));
            switch (Port.Kind)
            {
                case SegmentPortKind.Entry:
                    if (EntryValue) throw DuplicatePort(Port.Kind);
                    EntryValue = Port;
                    break;
                case SegmentPortKind.Exit:
                    if (ExitValue) throw DuplicatePort(Port.Kind);
                    ExitValue = Port;
                    break;
                case SegmentPortKind.Start:
                    if (StartValue) throw DuplicatePort(Port.Kind);
                    StartValue = Port;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Definition), $"Unknown segment port kind '{Port.Kind}'.");
            }
        }
        if (!EntryValue || !ExitValue || !StartValue)
            throw new ArgumentException("The template must contain exactly one Entry, Exit, and Start SegmentPort.", nameof(Definition));
        if (!string.Equals(StartValue.Connector, PLLLibrary.StartConnector, StringComparison.Ordinal))
            throw new ArgumentException($"The Start port connector must be exactly '{PLLLibrary.StartConnector}'.", nameof(Definition));

        var AuthoringAnchorsValue = Definition.AuthoringAnchors
            ? AuthoringAnchorsBinding.Create(TemplateRoot, Definition.AuthoringAnchors!, SegmentParentValue, SegmentCampfireValue, StartValue.transform)
            : null;
        if (Definition.AuthoringAnchors)
        {
            var StatueRoot = Definition.AuthoringAnchors!.StatueRoot;
            var AnchoredRespawn = StatueRoot
                ? NativePrefabContracts.ValidateOptionalRespawn(StatueRoot.transform, "authored statue root")
                : null;
            var RespawnCount = (CampfireRespawn is null ? 0 : 1) + (SegmentRespawn is null ? 0 : 1);
            if (StatueRoot && (!AnchoredRespawn || RespawnCount != 1
                    || !ReferenceEquals(AnchoredRespawn, CampfireRespawn?.Source)
                    && !ReferenceEquals(AnchoredRespawn, SegmentRespawn?.Source))
                || !StatueRoot && RespawnCount != 0)
                throw new ArgumentException("The authored statue root must identify the exact optional runtime statue.", nameof(Definition));
        }
        var FinalForceValue = FinalForceAuthoringContract.Create(
            TemplateRoot, SegmentParentValue, SegmentCampfireValue, Definition.Id, new BiomeId(BiomeValue.Id));

        return new RegisteredSegment(
            Definition,
            BiomeValue,
            TransformBinding.Create(TemplateRoot, SegmentParentValue),
            TransformBinding.Create(TemplateRoot, SegmentCampfireValue),
            RuntimeBindingValue,
            FogOriginValue,
            FogCutoutValue,
            ProgressBinding,
            CampfireRespawn,
            SegmentRespawn,
            AuthoringAnchorsValue,
            FinalForceValue,
            WallNextValue,
            WallPreviousValue,
            PortBinding.Create(TemplateRoot, EntryValue),
            PortBinding.Create(TemplateRoot, ExitValue),
            PortBinding.Create(TemplateRoot, StartValue),
            Components,
            RuntimeValue,
            FogOriginComponent,
            ComponentBinding<PllFogAuthoring>.Create(TemplateRoot, FogAuthoringValue),
            FogModeValue,
            FogCutoutComponent,
            ProgressPointValue);
    }

    internal void FreezePorts()
    {
        foreach (var Port in Ports) Port.Freeze();
        RuntimeValue.Freeze();
    }

    internal void UnfreezePorts()
    {
        foreach (var Port in Ports) Port.Unfreeze();
        RuntimeValue.Unfreeze();
    }

    internal void ValidateTemplate()
    {
        if (!Template || !DayNightProfile)
            throw new InvalidOperationException($"Registered segment '{Id}' changed before the registry was sealed.");
        var TemplateRoot = Template.transform;
        if (!TemplateRoot || Template.scene.IsValid() || TemplateRoot.parent || Template.activeSelf
            || !TemplateRoot.localPosition.Equals(TemplatePoseValue.position)
            || !TemplateRoot.localRotation.Equals(TemplatePoseValue.rotation)
            || !TemplateRoot.localScale.Equals(TemplateScaleValue))
            throw new InvalidOperationException($"Registered segment '{Id}' changed its template root before sealing.");
        if (!RuntimeValue || !FogOriginComponent || !FogCutout || ProgressPoint is null || !ProgressPoint.transform)
            throw new InvalidOperationException($"Registered segment '{Id}' contains a destroyed runtime reference.");
        if (ProgressClip is not null && !ProgressClip)
            throw new InvalidOperationException($"Registered segment '{Id}' contains a destroyed progress audio clip.");

        SegmentParent.Validate(TemplateRoot, Id, "segment feature root");
        SegmentCampfire.Validate(TemplateRoot, Id, "campfire feature root");
        RuntimeBinding.Validate(TemplateRoot, Id, "runtime");
        FogOrigin.Validate(TemplateRoot, Id, "fog origin");
        FogCutoutBinding.Validate(TemplateRoot, Id, "fog cutout");
        ProgressPointBinding.Validate(TemplateRoot, Id, "progress point");
        WallNext.Validate(TemplateRoot, Id, "next fog wall");
        WallPrevious.Validate(TemplateRoot, Id, "previous fog wall");

        var SegmentParentValue = SegmentParent.Source;
        var SegmentCampfireValue = SegmentCampfire.Source;
        var Origin = FogOrigin.Source;
        var Cutout = FogCutoutBinding.Source;
        var Progress = ProgressPointBinding.Source;
        try
        {
            NativePrefabContracts.ValidateCampfireRoot(SegmentParentValue, SegmentCampfireValue, "custom campfire root");
        }
        catch (ArgumentException Exception)
        {
            throw new InvalidOperationException($"Registered segment '{Id}' changed its operational Campfire contract.", Exception);
        }
        CampfireRespawn?.Validate(TemplateRoot, SegmentCampfireValue, Id, "campfire root");
        SegmentRespawn?.Validate(TemplateRoot, SegmentParentValue, Id, "segment root");
        AuthoringAnchorsValue?.Validate(TemplateRoot, Id, SegmentParentValue, SegmentCampfireValue, Start.Source);
        FinalForce.Validate(TemplateRoot, SegmentParentValue, SegmentCampfireValue);
        var Runtimes = Template.GetComponentsInChildren<CustomSegmentRuntime>(true);
        var Origins = Origin.gameObject.GetComponents<FogSphereOrigin>();
        var Cutouts = Cutout.gameObject.GetComponents<FogCutoutZone>();
        ValidateFogAuthoring(TemplateRoot, FogOriginComponent, true);
        if (!RuntimeValue.Registered || RuntimeValue.gameObject != Template
            || Runtimes.Length != 1 || !ReferenceEquals(Runtimes[0], RuntimeValue)
            || Origins.Length != 1 || !ReferenceEquals(Origins[0], FogOriginComponent)
            || Cutouts.Length != 1 || !ReferenceEquals(Cutouts[0], FogCutout)
            || !ReferenceEquals(RuntimeValue.SegmentParent, SegmentParentValue.gameObject)
            || !ReferenceEquals(RuntimeValue.SegmentCampfire, SegmentCampfireValue.gameObject)
            || !ReferenceEquals(RuntimeValue.FogOrigin, FogOriginComponent)
            || !ReferenceEquals(RuntimeValue.FogCutout, FogCutout)
            || !ReferenceEquals(RuntimeValue.ProgressPoint, ProgressPoint)
            || RuntimeValue.ProgressPoint is null || !RuntimeValue.ProgressPoint.transform
            || RuntimeValue.ProgressPoint.transform != Progress)
            throw new InvalidOperationException($"Registered segment '{Id}' changed its runtime content before sealing.");
        if (!MatchesRuntimeValues(FogOriginComponent, FogCutout, ProgressPoint))
            throw new InvalidOperationException($"Registered segment '{Id}' changed its runtime component values before sealing.");
        if (!TransformOwnership.Contains(TemplateRoot, SegmentParentValue) || !TransformOwnership.Contains(TemplateRoot, SegmentCampfireValue)
            || TransformOwnership.Overlaps(SegmentParentValue, SegmentCampfireValue)
            || !TransformOwnership.Contains(TemplateRoot, Origin) || TransformOwnership.Contains(SegmentParentValue, Origin) || TransformOwnership.Contains(SegmentCampfireValue, Origin)
            || !TransformOwnership.Contains(TemplateRoot, Cutout) || TransformOwnership.Contains(SegmentParentValue, Cutout) || TransformOwnership.Contains(SegmentCampfireValue, Cutout)
            || !TransformOwnership.Contains(TemplateRoot, Progress) || TransformOwnership.Contains(SegmentParentValue, Progress) || TransformOwnership.Contains(SegmentCampfireValue, Progress)
            || TransformOwnership.HasPropSpawnerInPath(TemplateRoot, Origin)
            || TransformOwnership.HasPropSpawnerInPath(TemplateRoot, Cutout)
            || TransformOwnership.HasPropSpawnerInPath(TemplateRoot, Progress)
            || !UniqueMatches(Origin, "Fogwall_Next", WallNext.Source)
            || !UniqueMatches(Origin, "Fogwall_Prev", WallPrevious.Source))
            throw new InvalidOperationException($"Registered segment '{Id}' changed its runtime ownership before sealing.");

        var CurrentPorts = Template.GetComponentsInChildren<SegmentPort>(true);
        if (CurrentPorts.Length != 3 || !SamePorts(CurrentPorts, Ports))
            throw new InvalidOperationException($"Registered segment '{Id}' changed its SegmentPort components before sealing.");
        Entry.Validate(TemplateRoot, Id, SegmentPortKind.Entry, SegmentParentValue, SegmentCampfireValue);
        Exit.Validate(TemplateRoot, Id, SegmentPortKind.Exit, SegmentParentValue, SegmentCampfireValue);
        Start.Validate(TemplateRoot, Id, SegmentPortKind.Start, SegmentParentValue, SegmentCampfireValue);
    }

    internal GameObject InstantiateTemplate()
    {
        ValidateTemplate();
        var Clone = UnityEngine.Object.Instantiate(Template);
        Clone.transform.SetPositionAndRotation(TemplatePoseValue.position, TemplatePoseValue.rotation);
        Clone.transform.localScale = TemplateScaleValue;
        return Clone;
    }

    internal ResolvedSegment Resolve(GameObject Clone)
    {
        if (!Clone) throw new InvalidOperationException($"Registered segment '{Id}' clone was destroyed.");
        if (CloneContracts.TryGetValue(Clone, out var Contract))
        {
            ValidateClone(Clone, Contract);
            return Contract.Resolved;
        }

        var State = ResolveClone(Clone, null);
        var Resolved = State.CreateResolved();
        CloneContracts.Add(Clone, new CloneContract(Resolved, State));
        State.Runtime.Freeze();
        return Resolved;
    }

    internal void ValidateResolvedClone(GameObject Clone, ResolvedSegment Expected)
    {
        if (!Clone) throw new InvalidOperationException($"Registered segment '{Id}' clone was destroyed.");
        if (!CloneContracts.TryGetValue(Clone, out var Contract)
            || !ReferenceEquals(Contract.Resolved, Expected))
            throw new InvalidOperationException($"Registered segment '{Id}' clone contract is unavailable.");
        ValidateClone(Clone, Contract);
    }

    void ValidateClone(GameObject Clone, CloneContract Contract) =>
        _ = ResolveClone(Clone, Contract);

    CloneState ResolveClone(GameObject Clone, CloneContract? Contract)
    {
        var Root = Clone.transform;
        var SegmentParentValue = SegmentParent.ResolveValidated(Root, Id, "segment feature root");
        var SegmentCampfireValue = SegmentCampfire.ResolveValidated(Root, Id, "campfire feature root");
        var RuntimeRoot = RuntimeBinding.ResolveValidated(Root, Id, "runtime");
        var FogOriginValue = FogOrigin.ResolveValidated(Root, Id, "fog origin");
        var FogCutoutValue = FogCutoutBinding.ResolveValidated(Root, Id, "fog cutout");
        var ProgressPointValue = ProgressPointBinding.ResolveValidated(Root, Id, "progress point");
        var ResolvedCampfireRespawn = CampfireRespawn?.Resolve(Root, SegmentCampfireValue, Id, "campfire root");
        var ResolvedCampfireSkeleton = ResolvedCampfireRespawn ? ResolvedCampfireRespawn.skeleton : null;
        var ResolvedSegmentRespawn = SegmentRespawn?.Resolve(Root, SegmentParentValue, Id, "segment root");
        var ResolvedSegmentSkeleton = ResolvedSegmentRespawn ? ResolvedSegmentRespawn.skeleton : null;
        var WallNextValue = WallNext.ResolveValidated(Root, Id, "next fog wall");
        var WallPreviousValue = WallPrevious.ResolveValidated(Root, Id, "previous fog wall");
        var EntryValue = Entry.ResolveValidated(Root, Id, "Entry port");
        var ExitValue = Exit.ResolveValidated(Root, Id, "Exit port");
        var StartValue = Start.ResolveValidated(Root, Id, "Start port");
        var ResolvedAuthoringAnchors = AuthoringAnchorsValue?.Resolve(
            Root, Id, SegmentParentValue, SegmentCampfireValue, StartValue);
        FinalForceOccurrence ResolvedFinalForce;
        if (Contract is null) ResolvedFinalForce = FinalForce.Resolve(Root);
        else
        {
            ResolvedFinalForce = Contract.Resolved.FinalForce;
            FinalForce.ValidateResolved(Root, ResolvedFinalForce);
        }

        var Runtimes = Root.GetComponentsInChildren<CustomSegmentRuntime>(true);
        var RuntimeComponents = RuntimeRoot.GetComponents<CustomSegmentRuntime>();
        var Origins = FogOriginValue.GetComponents<FogSphereOrigin>();
        var Cutouts = FogCutoutValue.GetComponents<FogCutoutZone>();
        if (Runtimes.Length != 1 || RuntimeComponents.Length != 1
            || !ReferenceEquals(Runtimes[0], RuntimeComponents[0])
            || Origins.Length != 1 || Cutouts.Length != 1
            || Root.GetComponentsInChildren<SpawnPoint>(true).Length != 0)
            throw new InvalidOperationException($"Registered segment '{Id}' clone changed its runtime component counts.");
        var ResolvedRuntime = RuntimeComponents[0];
        var ResolvedOrigin = Origins[0];
        var ResolvedFogAuthoring = ValidateFogAuthoring(Root, ResolvedOrigin, false);
        var ResolvedCutout = Cutouts[0];
        var ResolvedProgress = ResolvedRuntime.ProgressPoint;
        if (!ReferenceEquals(ResolvedRuntime.SegmentParent, SegmentParentValue.gameObject)
            || !ReferenceEquals(ResolvedRuntime.SegmentCampfire, SegmentCampfireValue.gameObject)
            || !ReferenceEquals(ResolvedRuntime.FogOrigin, ResolvedOrigin)
            || !ReferenceEquals(ResolvedRuntime.FogCutout, ResolvedCutout)
            || ResolvedProgress is null || !ResolvedProgress.transform
            || ResolvedProgress.transform != ProgressPointValue
            || !MatchesRuntimeValues(ResolvedOrigin, ResolvedCutout, ResolvedProgress))
            throw new InvalidOperationException($"Registered segment '{Id}' clone has invalid runtime content bindings.");

        var State = new CloneState(
            Clone,
            ResolvedRuntime,
            SegmentParentValue.gameObject,
            SegmentCampfireValue.gameObject,
            ResolvedOrigin,
            ResolvedFogAuthoring,
            ResolvedCutout,
            ResolvedProgress,
            ResolvedCampfireRespawn,
            ResolvedCampfireSkeleton,
            ResolvedSegmentRespawn,
            ResolvedSegmentSkeleton,
            ResolvedAuthoringAnchors,
            ResolvedFinalForce,
            WallNextValue,
            WallPreviousValue,
            EntryValue,
            ExitValue,
            StartValue,
            ResolvePort(EntryValue, SegmentPortKind.Entry),
            ResolvePort(ExitValue, SegmentPortKind.Exit),
            ResolvePort(StartValue, SegmentPortKind.Start));
        if (Contract is not null
            && (!ResolvedRuntime.Registered || !Contract.Matches(State)))
            throw new InvalidOperationException($"Registered segment '{Id}' clone replaced its frozen registered content.");
        return State;
    }

    PllFogAuthoring ValidateFogAuthoring(Transform Root, FogSphereOrigin Origin, bool SourceValue)
    {
        var Components = Root.GetComponentsInChildren<PllFogAuthoring>(true);
        var ResolvedBiomeId = new BiomeId(BiomeId);
        PllFogAuthoring Bound;
        if (SourceValue)
        {
            FogAuthoringBinding.Validate(Root, ResolvedBiomeId, "fog authoring");
            Bound = FogAuthoringBinding.Source;
        }
        else Bound = FogAuthoringBinding.Resolve(Root, ResolvedBiomeId, "fog authoring");
        var Local = Origin.gameObject.GetComponents<PllFogAuthoring>();
        if (Components.Length == 1 && Local.Length == 1
            && ReferenceEquals(Components[0], Local[0]) && ReferenceEquals(Components[0], Bound)
            && Bound && Bound.enabled && Bound.gameObject == Origin.gameObject
            && Enum.IsDefined(typeof(PllFogMode), Bound.Mode) && Bound.Mode == FogMode) return Bound;
        throw new InvalidOperationException(SourceValue
            ? $"Registered segment '{Id}' changed its fog authoring contract before sealing."
            : $"Registered segment '{Id}' clone has invalid fog authoring.");
    }

    bool MatchesRuntimeValues(
        FogSphereOrigin Origin,
        FogCutoutZone Cutout,
        MountainProgressHandler.ProgressPoint Progress)
    {
        if (!Origin || !Cutout || Progress is null || !Progress.transform
            || ProgressClip is not null && !ProgressClip) return false;
        return Origin.enabled == FogOriginEnabled
            && Origin.size.Equals(FogOriginSize)
            && Origin.disableFog == FogOriginDisablesFog
            && Cutout.enabled == FogCutoutEnabled
            && Cutout.min.Equals(FogCutoutMin)
            && Cutout.max.Equals(FogCutoutMax)
            && Cutout.amount.Equals(FogCutoutAmount)
            && string.Equals(Progress.title, ProgressTitle, StringComparison.Ordinal)
            && ReferenceEquals(Progress.clip, ProgressClip)
            && (!ProgressClip || string.Equals(ProgressClip.name, ProgressClipName, StringComparison.Ordinal))
            && Progress.achievement == ProgressAchievement
            && Progress.biome == ProgressBiome;
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Id); Writer.Write(Revision); Writer.Write(SegmentTypeId); Writer.Write(BiomeId);
        ManifestEncoding.WritePose(Writer, TemplatePoseValue); ManifestEncoding.WriteVector(Writer, TemplateScaleValue);
        SegmentParent.WriteManifest(Writer); SegmentCampfire.WriteManifest(Writer);
        RuntimeBinding.WriteManifest(Writer);
        FogOrigin.WriteManifest(Writer); FogCutoutBinding.WriteManifest(Writer); ProgressPointBinding.WriteManifest(Writer);
        Writer.Write(CampfireRespawn is not null);
        CampfireRespawn?.WriteManifest(Writer);
        Writer.Write(SegmentRespawn is not null);
        SegmentRespawn?.WriteManifest(Writer);
        Writer.Write(AuthoringAnchorsValue is not null);
        AuthoringAnchorsValue?.WriteManifest(Writer);
        FinalForce.WriteManifest(Writer);
        WallNext.WriteManifest(Writer); WallPrevious.WriteManifest(Writer);
        Writer.Write(FogOriginEnabled); ManifestEncoding.WriteScalar(Writer, FogOriginSize);
        FogAuthoringBinding.WriteManifest(Writer);
        Writer.Write((byte)FogMode); Writer.Write(FogOriginDisablesFog);
        Writer.Write(FogCutoutEnabled); ManifestEncoding.WriteScalar(Writer, FogCutoutMin);
        ManifestEncoding.WriteScalar(Writer, FogCutoutMax); ManifestEncoding.WriteScalar(Writer, FogCutoutAmount);
        WriteNullable(Writer, ProgressTitle); Writer.Write(ProgressClip is not null);
        if (ProgressClip is not null) WriteNullable(Writer, ProgressClipName);
        Writer.Write((int)ProgressAchievement); Writer.Write((int)ProgressBiome);
        Entry.WriteManifest(Writer); Exit.WriteManifest(Writer); Start.WriteManifest(Writer);
    }

    static void WriteNullable(BinaryWriter Writer, string? Value)
    {
        Writer.Write(Value is not null);
        if (Value is not null) Writer.Write(Value);
    }

    static ArgumentException DuplicatePort(SegmentPortKind Kind) =>
        new($"The template contains more than one {Kind} SegmentPort.", "definition");

    SegmentPort ResolvePort(Transform Value, SegmentPortKind Kind) =>
        Value.GetComponent<SegmentPort>()
        ?? throw new InvalidOperationException($"Registered segment '{Id}' clone is missing its {Kind} SegmentPort.");

    static Transform Unique(Transform Root, string Name)
    {
        Transform? Result = null;
        foreach (var Candidate in Root.GetComponentsInChildren<Transform>(true))
        {
            if (Candidate.name != Name) continue;
            if (Result) throw new ArgumentException($"The custom fog origin contains more than one {Name}.", "definition");
            Result = Candidate;
        }
        return Result ? Result : throw new ArgumentException($"The custom fog origin contains no {Name}.", "definition");
    }

    static bool UniqueMatches(Transform Root, string Name, Transform Expected)
    {
        Transform? Result = null;
        foreach (var Candidate in Root.GetComponentsInChildren<Transform>(true))
        {
            if (Candidate.name != Name) continue;
            if (Result) return false;
            Result = Candidate;
        }
        return Result && Result == Expected;
    }

    static bool SamePorts(SegmentPort[] Current, SegmentPort[] Expected)
    {
        foreach (var Port in Expected)
        {
            if (!Port) return false;
            var Found = false;
            foreach (var Candidate in Current)
                if (ReferenceEquals(Candidate, Port))
                {
                    Found = true;
                    break;
                }
            if (!Found) return false;
        }
        return true;
    }

    static void ValidateFinite(Vector3 Value, string Name)
    {
        if (!ManifestEncoding.CanEncode(Value.x) || !ManifestEncoding.CanEncode(Value.y) || !ManifestEncoding.CanEncode(Value.z))
            throw new ArgumentException($"The {Name} must be finite and within the manifest range.", "definition");
    }

    static void ValidateFinite(float Value, string Name)
    {
        if (!ManifestEncoding.CanEncode(Value)) throw new ArgumentException($"The {Name} must be finite and within the manifest range.", "definition");
    }

    static void ValidateFinite(Quaternion Value, string Name)
    {
        if (!ManifestEncoding.CanEncode(Value.x) || !ManifestEncoding.CanEncode(Value.y) || !ManifestEncoding.CanEncode(Value.z) || !ManifestEncoding.CanEncode(Value.w)
            || Value.x == 0f && Value.y == 0f && Value.z == 0f && Value.w == 0f)
            throw new ArgumentException($"The {Name} must be a finite nonzero quaternion within the manifest range.", "definition");
    }

    static void ValidateScale(Vector3 Value, string Name)
    {
        ValidateFinite(Value, Name);
        if (Value.x <= 0f || Value.y <= 0f || Value.z <= 0f)
            throw new ArgumentException($"The {Name} must be positive.", "definition");
    }

}

internal sealed class PortBinding
{
    readonly TransformBinding Binding;
    readonly SegmentPort Port;
    readonly SegmentPortKind Kind;
    readonly Pose LocalPoseValue;

    PortBinding(TransformBinding Binding, SegmentPort Port, SegmentPortKind Kind, string ConnectorValue, Pose LocalPoseValue)
    {
        this.Binding = Binding;
        this.Port = Port;
        this.Kind = Kind;
        Connector = ConnectorValue;
        this.LocalPoseValue = LocalPoseValue;
    }

    internal string Connector { get; }
    internal Pose LocalPose => LocalPoseValue;
    internal Transform Source => Binding.Source;
    internal Transform ResolveValidated(Transform TemplateRoot, string Id, string Name) =>
        Binding.ResolveValidated(TemplateRoot, Id, Name);

    internal static PortBinding Create(Transform TemplateRoot, SegmentPort Port)
    {
        var Pose = new Pose(TemplateRoot.InverseTransformPoint(Port.transform.position),
            Quaternion.Inverse(TemplateRoot.rotation) * Port.transform.rotation);
        if (!ManifestEncoding.CanEncode(Pose.position.x) || !ManifestEncoding.CanEncode(Pose.position.y) || !ManifestEncoding.CanEncode(Pose.position.z)
            || !ManifestEncoding.CanEncode(Pose.rotation.x) || !ManifestEncoding.CanEncode(Pose.rotation.y)
            || !ManifestEncoding.CanEncode(Pose.rotation.z) || !ManifestEncoding.CanEncode(Pose.rotation.w)
            || Pose.rotation.x == 0f && Pose.rotation.y == 0f
            && Pose.rotation.z == 0f && Pose.rotation.w == 0f)
            throw new ArgumentException("A segment port pose must be finite, within the manifest range, and have a nonzero rotation.", nameof(Port));
        return new PortBinding(TransformBinding.Create(TemplateRoot, Port.transform), Port, Port.Kind, Port.Connector, Pose);
    }

    internal void Validate(
        Transform TemplateRoot,
        string Id,
        SegmentPortKind ExpectedKind,
        Transform SegmentParentValue,
        Transform SegmentCampfireValue)
    {
        if (!Port || Kind != ExpectedKind || !Port.Registered || Port.Kind != Kind
            || !string.Equals(Port.Connector, Connector, StringComparison.Ordinal))
            throw new InvalidOperationException($"Registered segment '{Id}' changed its {ExpectedKind} SegmentPort contract before sealing.");
        Binding.Validate(TemplateRoot, Id, $"{ExpectedKind} port");
        var SourceValue = Binding.Source;
        if (Port.transform != SourceValue || TransformOwnership.Contains(SegmentParentValue, SourceValue)
            || TransformOwnership.Contains(SegmentCampfireValue, SourceValue)
            || TransformOwnership.HasPropSpawnerInPath(TemplateRoot, SourceValue))
            throw new InvalidOperationException($"Registered segment '{Id}' changed its {ExpectedKind} SegmentPort ownership before sealing.");
        var Current = new Pose(TemplateRoot.InverseTransformPoint(SourceValue.position),
            Quaternion.Inverse(TemplateRoot.rotation) * SourceValue.rotation);
        if (!Current.position.Equals(LocalPoseValue.position) || !Current.rotation.Equals(LocalPoseValue.rotation))
            throw new InvalidOperationException($"Registered segment '{Id}' changed a port pose before sealing.");
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Binding.WriteManifest(Writer);
        Writer.Write((int)Kind);
        Writer.Write(Connector);
        ManifestEncoding.WritePose(Writer, LocalPoseValue);
    }

}
