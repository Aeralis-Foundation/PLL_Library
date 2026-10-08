using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal abstract class LayoutSegmentIdentity
{
    protected LayoutSegmentIdentity(
        string Id, string StableId, string Revision, string SegmentTypeId, string BiomeId)
    {
        this.Id = Id ?? throw new ArgumentNullException(nameof(Id));
        this.StableId = StableId ?? throw new ArgumentNullException(nameof(StableId));
        this.Revision = Revision ?? throw new ArgumentNullException(nameof(Revision));
        this.SegmentTypeId = SegmentTypeId ?? throw new ArgumentNullException(nameof(SegmentTypeId));
        this.BiomeId = BiomeId ?? throw new ArgumentNullException(nameof(BiomeId));
    }

    internal string Id { get; }
    internal string StableId { get; }
    internal string Revision { get; }
    internal string SegmentTypeId { get; }
    internal string BiomeId { get; }
}

internal sealed class NonMetadataLayoutSegmentIdentity : LayoutSegmentIdentity
{
    internal NonMetadataLayoutSegmentIdentity(
        string Id, string StableId, string Revision, string SegmentTypeId, string BiomeId)
        : base(Id, StableId, Revision, SegmentTypeId, BiomeId) { }
}

internal sealed class MetadataLayoutSegmentIdentity : LayoutSegmentIdentity
{
    internal MetadataLayoutSegmentIdentity(
        string Id,
        string StableId,
        string Revision,
        string SegmentTypeId,
        string BiomeId,
        BiomeId MetadataBiome) : base(Id, StableId, Revision, SegmentTypeId, BiomeId)
    {
        if (!MetadataBiome.IsValid)
            throw new ArgumentException("A valid metadata biome ID is required.", nameof(MetadataBiome));
        this.MetadataBiome = MetadataBiome;
    }

    internal BiomeId MetadataBiome { get; }
}

internal interface INativeLayoutSegmentSource
{
    Segment NativeRole { get; }
    Biome.BiomeType NativeBiome { get; }
    bool IsNativeVariant { get; }
}

internal static class LayoutSegmentBehavior
{
    internal static NativeCompatibility? Resolve(LayoutSegmentSource Source)
    {
        if (Source is CustomLayoutSegmentSource Custom)
            return PllBiomeBehaviorCompatibility.Resolve(Custom.Behavior);
        if (Source is INativeLayoutSegmentSource Native)
            return new NativeCompatibility(Native.NativeRole, Native.NativeBiome);
        return null;
    }

    internal static Biome.BiomeType CarrierBiome(LayoutSegmentSource Source) =>
        Resolve(Source)?.Biome ?? global::Biome.BiomeType.Shore;
}

internal interface INativeMapSegmentSource : INativeLayoutSegmentSource
{
    MapHandler.MapSegment AuthoredSegment { get; }
}

internal abstract class LayoutSegmentSource
{
    readonly IReadOnlyList<LayoutPort> ExitPortValues;

    protected LayoutSegmentSource(
        LayoutSegmentIdentity Identity,
        GameObject SegmentParent,
        GameObject? SegmentCampfire,
        GameObject? WallNext,
        GameObject? WallPrevious,
        Transform? ReconnectSpawnPos,
        DayNightProfile? DayNightProfile,
        Transform Container,
        Vector3 ProgressionAxis,
        Pose? Start,
        string? StartConnector,
        IReadOnlyList<LayoutPort> EntryPorts,
        IReadOnlyList<LayoutPort> ExitPorts,
        Pose RootPose,
        Vector3 RootScale)
    {
        this.Identity = Identity ?? throw new ArgumentNullException(nameof(Identity));
        this.SegmentParent = SegmentParent ? SegmentParent : throw new ArgumentNullException(nameof(SegmentParent));
        this.SegmentCampfire = SegmentCampfire;
        this.WallNext = WallNext;
        this.WallPrevious = WallPrevious;
        this.ReconnectSpawnPos = ReconnectSpawnPos;
        this.DayNightProfile = DayNightProfile;
        this.Container = Container ? Container : throw new ArgumentNullException(nameof(Container));
        this.ProgressionAxis = ProgressionAxis;
        this.Start = Start;
        this.StartConnector = StartConnector;
        this.EntryPorts = EntryPorts ?? throw new ArgumentNullException(nameof(EntryPorts));
        ExitPortValues = ExitPorts ?? throw new ArgumentNullException(nameof(ExitPorts));
        this.RootPose = RootPose;
        this.RootScale = RootScale;
    }

    internal LayoutSegmentIdentity Identity { get; }
    internal string Id => Identity.Id;
    internal string StableId => Identity.StableId;
    internal string Revision => Identity.Revision;
    internal string SegmentTypeId => Identity.SegmentTypeId;
    internal string BiomeId => Identity.BiomeId;
    internal GameObject SegmentParent { get; }
    internal GameObject? SegmentCampfire { get; }
    internal GameObject? WallNext { get; }
    internal GameObject? WallPrevious { get; }
    internal Transform? ReconnectSpawnPos { get; }
    internal DayNightProfile? DayNightProfile { get; }
    internal Transform Container { get; }
    internal Vector3 ProgressionAxis { get; }
    internal Pose? Start { get; }
    internal string? StartConnector { get; }
    internal IReadOnlyList<LayoutPort> EntryPorts { get; }
    internal virtual IReadOnlyList<LayoutPort> ExitPorts => ExitPortValues;
    internal Pose RootPose { get; }
    internal Vector3 RootScale { get; }
    internal virtual int AmuletOpportunityCoverage => 0;
    internal virtual IReadOnlyList<string> AmuletSourceBindings => Array.Empty<string>();

    internal bool TryGetMetadataBiome(out BiomeId Biome)
    {
        if (Identity is MetadataLayoutSegmentIdentity Metadata)
        {
            Biome = Metadata.MetadataBiome;
            return true;
        }
        Biome = default;
        return false;
    }

    internal Vector3 AuthoredPosition(Pose Pose) =>
        RootPose.position + RootPose.rotation * Vector3.Scale(RootScale, Pose.position);
    internal Quaternion AuthoredRotation(Pose Pose) => RootPose.rotation * Pose.rotation;
}

internal abstract class OrdinaryLayoutSegmentSource : LayoutSegmentSource
{
    protected OrdinaryLayoutSegmentSource(
        LayoutSegmentIdentity Identity,
        GameObject SegmentParent,
        GameObject? SegmentCampfire,
        GameObject? WallNext,
        GameObject? WallPrevious,
        Transform? ReconnectSpawnPos,
        DayNightProfile? DayNightProfile,
        Transform Container,
        Vector3 ProgressionAxis,
        Pose? Start,
        string? StartConnector,
        IReadOnlyList<LayoutPort> EntryPorts,
        IReadOnlyList<LayoutPort> ExitPorts,
        Pose PeakAttachmentPose,
        Pose RootPose,
        Vector3 RootScale) : base(
            Identity, SegmentParent, SegmentCampfire, WallNext, WallPrevious,
            ReconnectSpawnPos, DayNightProfile, Container, ProgressionAxis, Start, StartConnector,
            EntryPorts, ExitPorts, RootPose, RootScale)
    {
        this.PeakAttachmentPose = PeakAttachmentPose;
    }

    internal Pose PeakAttachmentPose { get; }
}

internal sealed class NativeOrdinarySegmentSource : OrdinaryLayoutSegmentSource, INativeMapSegmentSource
{
    IReadOnlyList<LayoutPort>? BoundExitPorts;
    PeakAttachmentSource? BoundPeak;

    internal NativeOrdinarySegmentSource(
        MapHandler Handler,
        string Id,
        string StableId,
        Segment Role,
        MapHandler.MapSegment Segment,
        MountainProgressHandler.ProgressPoint ProgressPoint) : this(
            Handler, Id, StableId, Role, Segment, ProgressPoint,
            Capture(Handler, Id, Role, Segment)) { }

    NativeOrdinarySegmentSource(
        MapHandler Handler,
        string Id,
        string StableId,
        Segment Role,
        MapHandler.MapSegment Segment,
        MountainProgressHandler.ProgressPoint ProgressPoint,
        NativeOrdinaryBindings Bindings) : base(
            CreateIdentity(Id, StableId, Role, Segment),
            Bindings.SegmentParent,
            Bindings.SegmentCampfire,
            Segment.wallNext ? Segment.wallNext : Bindings.RoleSegment.wallNext,
            Segment.wallPrevious ? Segment.wallPrevious : Bindings.RoleSegment.wallPrevious,
            Segment.reconnectSpawnPos ? Segment.reconnectSpawnPos : Bindings.RoleSegment.reconnectSpawnPos,
            Segment._dayNightProfile,
            Bindings.Container,
            Handler.transform.forward.normalized,
            Bindings.Ports.Start,
            Bindings.Ports.Start.HasValue ? PLLLibrary.StartConnector : null,
            Bindings.Ports.Entries,
            Bindings.Ports.Exits,
            Bindings.Ports.Exits.Count == 0 ? default : Bindings.Ports.Exits[0].Pose,
            new Pose(Bindings.Container.position, Bindings.Container.rotation),
            Bindings.Container.lossyScale)
    {
        NativeRole = Role;
        NativeBiome = Segment._biome;
        AuthoredSegment = Segment;
        IsNativeVariant = Segment.isVariant;
        PeakSuccessorEntry = Bindings.PeakSuccessorEntry;
        NadirAmulets = NativeAmuletSourceContract.Capture(Bindings.Container);
        RequiresNativeVariantSelection = NativeVariantSelectionRequired(Bindings.Container);
        NativeProgressPoint = ProgressPoint
            ?? throw new ArgumentNullException(nameof(ProgressPoint));
    }

    static LayoutSegmentIdentity CreateIdentity(
        string Id,
        string StableId,
        Segment Role,
        MapHandler.MapSegment Segment)
    {
        var Revision = $"peak/runtime-role/{(int)Role}";
        var SegmentTypeId = Id switch
        {
            "Gloom" => global::PLL.Library.SegmentId.Gloom.Value,
            "Citadel" => global::PLL.Library.SegmentId.Citadel.Value,
            _ => $"peak/segment-role/{(int)Role}"
        };
        var SourceBiomeId = $"peak/biome-type/{(int)Segment._biome}";
        if (Role == global::Segment.Caldera)
            return new NonMetadataLayoutSegmentIdentity(
                Id, StableId, Revision, SegmentTypeId, SourceBiomeId);
        return new MetadataLayoutSegmentIdentity(
            Id,
            StableId,
            Revision,
            SegmentTypeId,
            SourceBiomeId,
            new BiomeId($"peak/{Id.ToLowerInvariant()}"));
    }

    public Segment NativeRole { get; }
    public Biome.BiomeType NativeBiome { get; }
    public MapHandler.MapSegment AuthoredSegment { get; }
    public bool IsNativeVariant { get; }
    internal LayoutPort? PeakSuccessorEntry { get; }
    internal NativeAmuletSourceContract NadirAmulets { get; }
    internal override int AmuletOpportunityCoverage => NadirAmulets.CoverageBit;
    internal override IReadOnlyList<string> AmuletSourceBindings => NadirAmulets.SourceBindings;
    internal bool RequiresNativeVariantSelection { get; }
    internal MountainProgressHandler.ProgressPoint NativeProgressPoint { get; }
    internal PeakAttachmentSource? ContextualPeak => BoundPeak;
    internal override IReadOnlyList<LayoutPort> ExitPorts => BoundExitPorts ?? base.ExitPorts;

    internal void AttachPeak(PeakAttachmentSource Peak)
    {
        if (BoundPeak is not null || Peak is null)
            throw new InvalidOperationException($"Segment '{Id}' repeated its native Peak attachment binding.");
        BoundPeak = Peak;
        if (base.ExitPorts.Count != 0) return;
        var Face = Peak.BridgeExit;
        var Boundary = Peak.BridgeSuccessorBoundary;
        if (Peak.ExitPorts.Count != 1
            || !string.Equals(Face.Connector, Boundary.Connector, StringComparison.Ordinal)
            || !string.Equals(Face.Connector, Peak.ExitPorts[0].Connector, StringComparison.Ordinal))
            throw new InvalidOperationException($"Segment '{Id}' has no native Peak summit exit.");
        BoundExitPorts = new[]
        {
            new LayoutPort(Face.Connector, new Pose(Peak.ExitPorts[0].Pose.position, Boundary.Pose.rotation))
        };
    }

    static NativeOrdinaryBindings Capture(
        MapHandler Handler, string Id, Segment Role, MapHandler.MapSegment Segment)
    {
        var SegmentParent = Segment._segmentParent;
        if (!SegmentParent)
            throw new InvalidOperationException($"PEAK's native {Id} segment root was not found.");
        var SegmentCampfire = Segment._segmentCampfire;
        var Container = SegmentParent.transform.parent ?? SegmentParent.transform;
        if (string.Equals(Id, "Roots", StringComparison.Ordinal))
            NativeRootsSourceContract.Normalize(SegmentParent.transform);
        var RoleSegment = Handler.segments[(int)Role]
            ?? throw new InvalidOperationException($"PEAK's native {Role} role was not found.");
        return new NativeOrdinaryBindings(
            SegmentParent, SegmentCampfire, Container, RoleSegment,
            NativeSegmentPorts.Create(Handler, Role, Segment, Container),
            Role == global::Segment.Caldera && !Segment.isVariant
                ? NativeSegmentPorts.CreateCalderaPeakEntry(Container, SegmentParent.transform)
                : null);
    }

    readonly struct NativeOrdinaryBindings
    {
        internal NativeOrdinaryBindings(
            GameObject SegmentParent,
            GameObject? SegmentCampfire,
            Transform Container,
            MapHandler.MapSegment RoleSegment,
            NativePortProfile Ports,
            LayoutPort? PeakSuccessorEntry)
        {
            this.SegmentParent = SegmentParent;
            this.SegmentCampfire = SegmentCampfire;
            this.Container = Container;
            this.RoleSegment = RoleSegment;
            this.Ports = Ports;
            this.PeakSuccessorEntry = PeakSuccessorEntry;
        }

        internal GameObject SegmentParent { get; }
        internal GameObject? SegmentCampfire { get; }
        internal Transform Container { get; }
        internal MapHandler.MapSegment RoleSegment { get; }
        internal NativePortProfile Ports { get; }
        internal LayoutPort? PeakSuccessorEntry { get; }
    }

    static bool NativeVariantSelectionRequired(Transform Container)
    {
        var Variants = Container.GetComponentsInChildren<VariantObject>(true);
        var Parent = Container.parent;
        if (Variants.Length == 0 || !Parent
            || Container.GetComponentsInChildren<VariantObjectSelector>(true).Length != 0) return false;
        var Selectors = Parent.GetComponents<VariantObjectSelector>();
        if (Selectors.Length != 1 || Selectors[0].GetType() != typeof(VariantObjectSelector)) return false;
        var Domain = Parent.GetComponentsInChildren<VariantObject>(true);
        if (Domain.Length != Variants.Length) return false;
        foreach (var Variant in Domain)
            if (!Variant || Variant.transform == Container || !Variant.transform.IsChildOf(Container)) return false;
        return true;
    }
}

internal sealed class CustomLayoutSegmentSource : OrdinaryLayoutSegmentSource
{
    internal CustomLayoutSegmentSource(RegisteredSegment Registration) : base(
        new MetadataLayoutSegmentIdentity(
            Registration.Id, Registration.Id, Registration.Revision,
            Registration.SegmentTypeId, Registration.BiomeId, new BiomeId(Registration.BiomeId)),
        Registration.SegmentParent.Source.gameObject,
        Registration.SegmentCampfire.Source.gameObject,
        Registration.WallNext?.Source.gameObject,
        Registration.WallPrevious?.Source.gameObject,
        Registration.Start.Source,
        BiomeAuthoringCatalog.TryGet(Registration.Id, out var Authoring)
            ? Authoring.EffectiveDayNightProfile
            : Registration.DayNightProfile,
        Registration.Template.transform,
        Registration.Template.transform.forward.normalized,
        Registration.Start.LocalPose,
        Registration.Start.Connector,
        new[] { Port(Registration.Entry) },
        new[] { Port(Registration.Exit) },
        Port(Registration.Exit).Pose,
        Registration.TemplatePose,
        Registration.TemplateScale)
    {
        this.Registration = Registration ?? throw new ArgumentNullException(nameof(Registration));
    }

    internal RegisteredSegment Registration { get; }
    internal PllBiomeBehavior Behavior => Registration.Behavior;
    internal override int AmuletOpportunityCoverage => BiomeAuthoringCatalog.TryGet(Id, out var Authoring)
        ? Authoring.AmuletOpportunityCoverage : 0;
    internal override IReadOnlyList<string> AmuletSourceBindings => BiomeAuthoringCatalog.TryGet(Id, out var Authoring)
        ? Authoring.AmuletSourceBindings : Array.Empty<string>();
    internal BiomeId MetadataBiome =>
        ((MetadataLayoutSegmentIdentity)Identity).MetadataBiome;

    static LayoutPort Port(PortBinding Port) => new(Port.Connector, Port.LocalPose);
}

internal sealed class PeakAttachmentSource : LayoutSegmentSource, INativeLayoutSegmentSource
{
    internal PeakAttachmentSource(
        NativeOrdinarySegmentSource Terminal,
        Transform Peak,
        Transform Reconnect,
        MountainProgressHandler.ProgressPoint Summit) : this(
            Terminal,
            Peak,
            Reconnect,
            Summit,
            NativeSegmentPorts.CreatePeakBridge(
                Terminal.Container, Peak, NativeFixtures.ValidateBridgeStatue(Peak, false))) { }

    PeakAttachmentSource(
        NativeOrdinarySegmentSource Terminal,
        Transform Peak,
        Transform Reconnect,
        MountainProgressHandler.ProgressPoint Summit,
        PeakBridgeProfile Bridge) : this(
            Terminal,
            Peak,
            Reconnect,
            Summit,
            Bridge,
            NativeSegmentPorts.CreatePeakExit(
                Terminal.Container, Summit.transform, Bridge.Contact, out var ProgressionAxis),
            ProgressionAxis) { }

    PeakAttachmentSource(
        NativeOrdinarySegmentSource Terminal,
        Transform Peak,
        Transform Reconnect,
        MountainProgressHandler.ProgressPoint Summit,
        PeakBridgeProfile Bridge,
        IReadOnlyList<LayoutPort> ExitPorts,
        Vector3 ProgressionAxis) : base(
            new NonMetadataLayoutSegmentIdentity(
                "Peak", global::PLL.Library.SegmentId.Peak.Value,
                $"{Terminal.Revision}/attachment/{Terminal.BiomeId}",
                $"peak/segment-role/{(int)Segment.Peak}", Terminal.BiomeId),
            Peak.gameObject,
            null,
            null,
            null,
            Reconnect,
            Terminal.DayNightProfile,
            Terminal.Container,
            ProgressionAxis,
            null,
            null,
            NativeSegmentPorts.Empty,
            ExitPorts,
            Terminal.RootPose,
            Terminal.RootScale)
    {
        BridgeRoot = Bridge.Source;
        BridgeExit = Bridge.Exit;
        BridgeContact = Bridge.Contact;
        BridgeOwnerBoundary = Bridge.OwnerBoundary;
        BridgeSuccessorBoundary = Bridge.SuccessorBoundary;
        BridgeCampfire = Bridge.BridgeCampfire;
        BridgeWalkingClearance = Bridge.WalkingClearance;
        Translation = Vector3.zero;
        HashPose = new Pose(
            Terminal.Container.InverseTransformPoint(Peak.position),
            Quaternion.Inverse(Terminal.Container.rotation) * Peak.rotation);
        RespawnPose = NativeSegmentPorts.LocalPose(Terminal.Container, Reconnect);
        NativeProgressPoint = Summit;
        NativeBiome = Terminal.NativeBiome;
        IsNativeVariant = Terminal.IsNativeVariant;
    }

    public Segment NativeRole => Segment.Peak;
    public Biome.BiomeType NativeBiome { get; }
    public bool IsNativeVariant { get; }
    internal Transform BridgeRoot { get; }
    internal LayoutPort BridgeExit { get; }
    internal Pose BridgeContact { get; }
    internal Pose BridgeOwnerBoundary { get; }
    internal LayoutPort BridgeSuccessorBoundary { get; }
    internal Pose BridgeCampfire { get; }
    internal float BridgeWalkingClearance { get; }
    internal Vector3 Translation { get; }
    internal Pose HashPose { get; }
    internal Pose RespawnPose { get; }
    internal MountainProgressHandler.ProgressPoint NativeProgressPoint { get; }

    internal static SpecialDayZone RequireNormalSpecialDayZone(PeakHandler Handler)
    {
        if (!Handler) throw new ArgumentNullException(nameof(Handler));
        var Root = Handler.normalSpecialDay;
        var Zones = Root ? Root.GetComponentsInChildren<SpecialDayZone>(true) : Array.Empty<SpecialDayZone>();
        if (!Root || Root.transform.parent != Handler.transform || Zones.Length != 1 || Zones[0].gameObject != Root)
            throw new InvalidOperationException("PEAK's normal Peak SpecialDayZone binding is ambiguous.");
        return Zones[0];
    }
}

internal sealed class NadirLayoutSegmentSource : LayoutSegmentSource, INativeMapSegmentSource
{
    internal NadirLayoutSegmentSource(MapHandler Handler, Peak.VoidBiome Nadir) : this(
        Handler,
        Nadir,
        Nadir.segment ?? throw new InvalidOperationException(
            "PEAK's native Nadir segment reference is unavailable.")) { }

    NadirLayoutSegmentSource(
        MapHandler Handler, Peak.VoidBiome Nadir, MapHandler.MapSegment Segment) : this(
            Handler,
            Nadir,
            Segment,
            Segment._segmentParent,
            Segment._segmentCampfire) { }

    NadirLayoutSegmentSource(
        MapHandler Handler,
        Peak.VoidBiome Nadir,
        MapHandler.MapSegment Segment,
        GameObject SegmentParent,
        GameObject? SegmentCampfire) : this(
            Handler,
            Nadir,
            Segment,
            SegmentParent,
            SegmentCampfire,
            SegmentParent ? SegmentParent.transform.parent ?? SegmentParent.transform : null!) { }

    NadirLayoutSegmentSource(
        MapHandler Handler,
        Peak.VoidBiome Nadir,
        MapHandler.MapSegment Segment,
        GameObject SegmentParent,
        GameObject? SegmentCampfire,
        Transform Container) : this(
            Handler,
            Nadir,
            Segment,
            SegmentParent,
            SegmentCampfire,
            Container,
            RequireNadirBindings(Nadir, Segment, SegmentParent, SegmentCampfire, Container)) { }

    NadirLayoutSegmentSource(
        MapHandler Handler,
        Peak.VoidBiome Nadir,
        MapHandler.MapSegment Segment,
        GameObject SegmentParent,
        GameObject? SegmentCampfire,
        Transform Container,
        NadirBindings Bindings) : base(
            new NonMetadataLayoutSegmentIdentity(
                "Nadir", global::PLL.Library.SegmentId.Nadir.Value,
                $"peak/runtime-role/{(int)(global::Segment.Void)}", global::PLL.Library.SegmentId.Nadir.Value,
                $"peak/biome-type/{(int)Segment._biome}"),
            SegmentParent,
            SegmentCampfire,
            Segment.wallNext,
            Segment.wallPrevious,
            Segment.reconnectSpawnPos,
            Segment._dayNightProfile,
            Container,
            Handler.transform.forward.normalized,
            Bindings.Ports.Start,
            PLLLibrary.StartConnector,
            Bindings.Ports.Entries,
            Bindings.Ports.Exits,
            new Pose(Container.position, Container.rotation),
            Container.lossyScale)
    {
        NativeBiome = Segment._biome;
        AuthoredSegment = Segment;
        IsNativeVariant = Segment.isVariant;
        HeroTitle = Bindings.HeroTitle;
        SpawnPoints = Bindings.SpawnPoints;
        Gate = Bindings.Gate;
        PeakRoot = Bindings.PeakRoot;
        BridgeRoot = Bindings.BridgeRoot;
        AmbienceRoot = Bindings.AmbienceRoot;
        PeakSuccessorEntry = Bindings.PeakSuccessorEntry;
        VoidWaterVisual = Bindings.VoidWaterVisual;
        VoidWaterZone = Bindings.VoidWaterZone;
    }

    public Segment NativeRole => Segment.Void;
    public Biome.BiomeType NativeBiome { get; }
    public MapHandler.MapSegment AuthoredSegment { get; }
    public bool IsNativeVariant { get; }
    internal Transform Gate { get; }
    internal Transform PeakRoot { get; }
    internal Transform BridgeRoot { get; }
    internal Transform AmbienceRoot { get; }
    internal Peak.ForceHeroTextTrigger HeroTitle { get; }
    internal IReadOnlyList<Transform> SpawnPoints { get; }
    internal LayoutPort PeakSuccessorEntry { get; }
    internal Transform VoidWaterVisual { get; }
    internal Transform VoidWaterZone { get; }

    static NadirBindings RequireNadirBindings(
        Peak.VoidBiome Nadir,
        MapHandler.MapSegment Segment,
        GameObject SegmentParent,
        GameObject? SegmentCampfire,
        Transform Container)
    {
        if (!SegmentParent || SegmentCampfire || Segment.wallNext || Segment.wallPrevious || !Segment.reconnectSpawnPos)
            throw new InvalidOperationException("PEAK's native Nadir segment contract is invalid.");
        if (Container.parent != Nadir.transform)
            throw new InvalidOperationException("PEAK's native Nadir container ownership is ambiguous.");
        var HeroTitles = Container.GetComponentsInChildren<Peak.ForceHeroTextTrigger>(true);
        if (HeroTitles.Length != 1)
            throw new InvalidOperationException(
                "PEAK's native Nadir container must own exactly one hero-title trigger.");
        var SpawnPoints = Nadir.spawnPoints;
        if (SpawnPoints is not { Length: 4 })
            throw new InvalidOperationException(
                "PEAK's native Nadir player formation must contain four spawn points.");
        var UniqueSpawnPoints = new HashSet<int>();
        foreach (var SpawnPoint in SpawnPoints)
            if (!SpawnPoint || SpawnPoint.parent != Segment.reconnectSpawnPos
                || !UniqueSpawnPoints.Add(SpawnPoint.GetInstanceID()))
                throw new InvalidOperationException(
                    "PEAK's native Nadir player formation ownership is ambiguous.");
        var Gates = SegmentParent.GetComponentsInChildren<Peak.PeakGatePortal>(true);
        if (Gates.Length != 1)
            throw new InvalidOperationException("PEAK's native Nadir segment must own exactly one gate.");
        var Gate = Gates[0].transform;
        var PeakRoot = FindPeak(SegmentParent.transform);
        if (!Gate.IsChildOf(PeakRoot))
            throw new InvalidOperationException("PEAK's native Nadir gate is outside its Nadir Peak fixture.");
        var BridgeRoot = NativeSegmentPorts.CreateNadirBridge(PeakRoot);
        var AmbienceRoot = FindAmbience(Container, SegmentParent.transform);
        BindVoidWater(SegmentParent.transform, out var VoidWaterVisual, out var VoidWaterZone);
        var Ports = NativeSegmentPorts.CreateNadir(Segment, Container, Gate, out var PeakSuccessorEntry);
        return new NadirBindings(
            HeroTitles[0], (Transform[])SpawnPoints.Clone(), Gate, PeakRoot, BridgeRoot, AmbienceRoot,
            PeakSuccessorEntry, VoidWaterVisual, VoidWaterZone, Ports);
    }

    static Transform FindPeak(Transform SegmentRoot)
    {
        Transform? Result = null;
        for (var i = 0; i < SegmentRoot.childCount; i++)
        {
            var Child = SegmentRoot.GetChild(i);
            if (!Child.name.Equals("Nadir Peak", StringComparison.Ordinal)) continue;
            if (Result)
                throw new InvalidOperationException(
                    "PEAK's native Nadir segment owns multiple direct Nadir Peak children.");
            Result = Child;
        }
        return Result
            ? Result
            : throw new InvalidOperationException(
                "PEAK's native Nadir segment has no direct Nadir Peak child.");
    }

    static Transform FindAmbience(Transform Container, Transform SegmentRoot)
    {
        Transform? Result = null;
        for (var i = 0; i < Container.childCount; i++)
        {
            var Child = Container.GetChild(i);
            var Loops = Child.GetComponents<AudioLoop>();
            if (Loops.Length == 0) continue;
            if (Loops.Length != 1 || Child.GetComponentsInChildren<AudioLoop>(true).Length != 1)
                throw new InvalidOperationException(
                    "PEAK's native Nadir ambience owner has an ambiguous AudioLoop graph.");
            if (Result)
                throw new InvalidOperationException(
                    "PEAK's native Nadir container owns multiple direct AudioLoop children.");
            Result = Child;
        }
        if (!Result || Result == SegmentRoot)
            throw new InvalidOperationException(
                "PEAK's native Nadir container must directly own one AudioLoop outside its segment root.");
        return Result;
    }

    static void BindVoidWater(Transform SegmentRoot, out Transform Visual, out Transform Zone)
    {
        Transform? Surface = null;
        foreach (var Candidate in SegmentRoot.GetComponentsInChildren<MeshRenderer>(true))
        {
            var Matches = false;
            foreach (var Material in Candidate.sharedMaterials)
                if (Material && Material.name.Equals("M_Void Water", StringComparison.Ordinal))
                {
                    if (Matches)
                        throw new InvalidOperationException(
                            "PEAK's Void Water renderer repeats its native material.");
                    Matches = true;
                }
            if (!Matches) continue;
            var Parent = Candidate.transform.parent;
            if (!Parent || Surface && Surface != Parent)
                throw new InvalidOperationException("PEAK's Void Water surface ownership is ambiguous.");
            Surface = Parent;
        }
        if (!Surface || !Surface.IsChildOf(SegmentRoot))
            throw new InvalidOperationException("PEAK's Void Water surface is unavailable.");
        Visual = Surface;
        var Zones = SegmentRoot.GetComponentsInChildren<WaterZone>(true);
        if (Zones.Length != 1 || Zones[0].transform == Visual
            || !Visual.parent || Zones[0].transform.parent != Visual.parent)
            throw new InvalidOperationException("PEAK's Void Water zone ownership is ambiguous.");
        Zone = Zones[0].transform;
    }

    readonly struct NadirBindings
    {
        internal NadirBindings(
            Peak.ForceHeroTextTrigger HeroTitle,
            Transform[] SpawnPoints,
            Transform Gate,
            Transform PeakRoot,
            Transform BridgeRoot,
            Transform AmbienceRoot,
            LayoutPort PeakSuccessorEntry,
            Transform VoidWaterVisual,
            Transform VoidWaterZone,
            NativePortProfile Ports)
        {
            this.HeroTitle = HeroTitle;
            this.SpawnPoints = SpawnPoints;
            this.Gate = Gate;
            this.PeakRoot = PeakRoot;
            this.BridgeRoot = BridgeRoot;
            this.AmbienceRoot = AmbienceRoot;
            this.PeakSuccessorEntry = PeakSuccessorEntry;
            this.VoidWaterVisual = VoidWaterVisual;
            this.VoidWaterZone = VoidWaterZone;
            this.Ports = Ports;
        }

        internal Peak.ForceHeroTextTrigger HeroTitle { get; }
        internal Transform[] SpawnPoints { get; }
        internal Transform Gate { get; }
        internal Transform PeakRoot { get; }
        internal Transform BridgeRoot { get; }
        internal Transform AmbienceRoot { get; }
        internal LayoutPort PeakSuccessorEntry { get; }
        internal Transform VoidWaterVisual { get; }
        internal Transform VoidWaterZone { get; }
        internal NativePortProfile Ports { get; }
    }
}
