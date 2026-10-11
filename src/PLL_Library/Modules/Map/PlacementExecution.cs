using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Zorro.Core;

namespace PLL.Library;

internal static class PlacementExecution
{
    const int BoundaryLayer = 22;

    internal static void RebaseCondorFlightPositions(Transform Root, Matrix4x4 WorldPoseChange)
    {
        foreach (var Bird in Root.GetComponentsInChildren<Condor>(true))
        {
            Bird._initPos = WorldPoseChange.MultiplyPoint3x4(Bird._initPos);
            Bird._lastPos = WorldPoseChange.MultiplyPoint3x4(Bird._lastPos);
        }
    }

    internal static void RebaseTrapWarningLines(IEnumerable<Transform> Roots)
    {
        var ShooterIds = new HashSet<int>();
        foreach (var Root in Roots)
            foreach (var Shooter in Root.GetComponentsInChildren<ArrowShooter>(true))
            {
                if (!Map.CurrentRun!.HookedGeneration.OwnedByOuter(Root, Shooter.transform)
                    || !ShooterIds.Add(Shooter.GetInstanceID())) continue;
                if (ShooterIds.Count == 1) Physics.SyncTransforms();
                Shooter.pos = Shooter.shooter.position;
                Shooter.forward = Shooter.shooter.forward;
                Shooter.DrawWarningLine();
            }
    }

    internal static int Depth(Transform Value)
    {
        var Depth = 0;
        while (Value.parent)
        {
            Value = Value.parent;
            Depth++;
        }
        return Depth;
    }
    internal static void RememberActive(Dictionary<GameObject, bool> Originals, GameObject Value)
    {
        if (!Originals.ContainsKey(Value)) Originals.Add(Value, Value.activeSelf);
    }
    internal static void SetPlacedActive(GameObject Value, bool Active, bool IsClone, Dictionary<GameObject, bool> Originals)
    {
        if (!IsClone) RememberActive(Originals, Value);
        Value.SetActive(Active);
    }
    internal static void SetActiveAndValidate(GameObject Value, bool Active)
    {
        MapSlot? ActivatedSlot = null;
        if (Active && Map.HasPublishedLayout)
            foreach (var Slot in Map.Slots)
            {
                if (Slot.Released || Slot.Segment.segmentParent != Value) continue;
                ActivatedSlot = Slot;
                if (!Slot.IsClone || !Slot.FeaturesActivated || !Slot.Container
                    || Slot.Container.gameObject.activeSelf) break;
                Value.SetActive(false);
                if (Slot.Segment.segmentCampfire) Slot.Segment.segmentCampfire.SetActive(false);
                Slot.Container.gameObject.SetActive(true);
                var Attachments = new HashSet<int>();
                foreach (var Candidate in Slot.Occurrence.Slots)
                {
                    var Attachment = Candidate.PeakAttachment;
                    if (!Attachment || !Attachments.Add(Attachment.GetInstanceID())
                        || Attachment.gameObject.activeSelf) continue;
                    Attachment.gameObject.SetActive(true);
                }
                break;
            }
        Value.SetActive(Active);
        if (!Active || ActivatedSlot is null) return;
        try { NativeFixtures.ValidateActivatedCustomClone(ActivatedSlot); }
        catch (Exception Exception)
        {
            if (Map.Pending is null && Map.Active)
                LazySlotGenerator.AbortLazyGeneration(Map.Index, "Registered segment activation changed its clone contract", Exception);
            throw;
        }
    }
    internal static void ActivateAndValidate(MapSlot Slot, GameObject Value)
    {
        Value.SetActive(true);
        NativeFixtures.ValidateActivatedCustomClone(Slot);
    }
    internal static void ActivateClonedOccurrence(FeatureOccurrence Occurrence, MapRun.FeatureWriter Writer)
    {
        Writer.RequireCurrent();
        var RandomState = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(Occurrence.ActivationSeed);
            ActivateAndValidate(Occurrence.Slots[0], Occurrence.Container.gameObject);
            var Attachments = new HashSet<int>();
            foreach (var Slot in Occurrence.Slots)
            {
                Writer.RequireCurrent();
                ActivateAndValidate(Slot, Slot.Segment.segmentParent);
                if (Slot.Segment.segmentCampfire)
                {
                    Writer.RequireCurrent();
                    ActivateAndValidate(Slot, Slot.Segment.segmentCampfire);
                }
                var Attachment = Slot.PeakAttachment;
                if (Attachment && !TransformOwnership.Contains(Occurrence.Container, Attachment)
                    && Attachments.Add(Attachment.GetInstanceID()))
                {
                    Writer.RequireCurrent();
                    ActivateAndValidate(Slot, Attachment.gameObject);
                }
                Writer.RequireCurrent();
                Slot.MarkFeaturesActivated();
            }
        }
        finally { UnityEngine.Random.state = RandomState; }
    }
    internal static int ReplaceSetActive(List<CodeInstruction> Codes)
    {
        var Original = AccessTools.DeclaredMethod(
            typeof(GameObject), nameof(GameObject.SetActive), new[] { typeof(bool) })
            ?? throw new MissingMethodException("GameObject.SetActive was not found.");
        var Replacement = ((Action<GameObject, bool>)SetActiveAndValidate).Method;
        var Replaced = 0;
        foreach (var Code in Codes)
        {
            if (!Code.Calls(Original)) continue;
            Code.opcode = OpCodes.Call;
            Code.operand = Replacement;
            Replaced++;
        }
        return Replaced;
    }
    internal static void ValidateTransitions(IReadOnlyList<MapSlot> Slots)
    {
        for (var i = 0; i < Slots.Count; i++)
        {
            if (i + 1 == Slots.Count) continue;
            if (!Slots[i].Segment.segmentCampfire && !Slots[i].PeakAttachment
                && Slots[i].Source is not NadirLayoutSegmentSource
                && Slots[i].ProgressionBoundary == NativeProgressionBoundaryKind.None)
                throw new InvalidOperationException($"The {Slots[i].Source.Id} segment at layout position {i + 1} has no progression boundary to advance the map.");
        }
    }
    internal static MapSlot[] Place(
        IReadOnlyList<LayoutSegmentSource> Selected,
        IEnumerable<LayoutSegmentSource> Catalog,
        int Index,
        int TotalSlots,
        int InitialGenerationSlotCount,
        IReadOnlyList<bool> NadirScoutsHonor,
        PlacementFrame Frame,
        Transform Container,
        IReadOnlyList<NativeFeatureSourceContract.SupplementalRootContract> SupplementalRoots,
        Transform NativeVolcanoParent,
        IReadOnlyDictionary<LayoutSegmentSource, NativeTerrainMapProxy.ExternalSupport> NativeCampfireSurfaces,
        Transform CloneParent,
        GameObject? NativeClone,
        HashSet<int> AuthoredTerrain,
        HashSet<int> AuthoredCampfires,
        OriginalMapSources Originals,
        Dictionary<Transform, Vector3> Positions,
        MapEnvironmentProjection.EnvironmentState Environment,
        List<GameObject> CreatedObjects,
        List<GloomVoiceRuntime.GloomProfileClone> CreatedGloomProfiles,
        out Transform? AttachmentRespawn)
    {
        var Run = Map.CurrentRun
            ?? throw new InvalidOperationException("Map placement has no resolved run configuration.");
        var Configuration = Run.Configuration;
        if (NadirScoutsHonor.Count != TotalSlots)
            throw new InvalidOperationException("Nadir checkpoint requirements do not match the logical slot count.");
        AttachmentRespawn = null;
        var Custom = (Selected[0] as CustomLayoutSegmentSource)?.Registration;
        var IsClone = Custom != null || NativeClone is not null;
        if (Frame.Rotated && (!IsClone || Selected.Count != 1 || Selected[0] is not NadirLayoutSegmentSource))
            throw new InvalidOperationException("Only a cloned native Nadir may own a rotated placement frame.");
        var Offset = Frame.Offset;
        var OriginalRoot = Container.gameObject;
        var AuthoredPosition = Container.position;
        var AuthoredRotation = Container.rotation;
        GameObject? PlacedObject = null;
        ResolvedSegment? CustomResolved = null;
        BiomeAuthoringContract? CustomAuthoring = null;
        (Transform Root, PeakAttachmentSource Source)? PassivePeak = null;
        MeshCollider? NativeCampfireSurfaceCollider = null;
        GloomVoiceRuntime.GloomVoiceClone? GloomVoice = null;

        try
        {
            if (!IsClone)
            {
                RememberActive(Originals.Active, OriginalRoot);
                if (!Positions.ContainsKey(Container)) Positions.Add(Container, Container.position);
                OriginalRoot.SetActive(false);
            }
            if (Custom != null)
            {
                PlacedObject = Custom.InstantiateTemplate();
                PlacedObject.transform.SetParent(CloneParent, true);
            }
            else PlacedObject = NativeClone ?? OriginalRoot;
            var OccurrenceName = Selected[0].Id;
            for (var i = 1; i < Selected.Count; i++) OccurrenceName += $"_{Selected[i].Id}";
            if (IsClone) PlacedObject.name = $"PLL_{Index:D3}_{OccurrenceName}";
            PlacedObject.SetActive(false);
            if (IsClone && FeatureGeneration.NativeCaldera(Selected[0]) && PlacementPlan.IsNativeMountainPair(Selected, 0))
                NativeFixtures.CopyNativeCalderaBiome(Container, PlacedObject);
            var PlacedRoot = PlacedObject.transform;
            var Occurrence = new FeatureOccurrence(
                Run, PlacedRoot,
                Container,
                SupplementalRoots,
                Selected[0] is NativeOrdinarySegmentSource { RequiresNativeVariantSelection: true },
                FeatureGeneration.FeatureSeed(FeatureGeneration.MapSeed(), Index, $"{OccurrenceName}:configuration"),
                FeatureGeneration.FeatureSeed(FeatureGeneration.MapSeed(), Index, $"{OccurrenceName}:generation"),
                FeatureGeneration.FeatureSeed(FeatureGeneration.MapSeed(), Index, $"{PlacedRoot.name}:activation"));

            if (IsClone)
            {
                PhotonCloneIdentity.Reset(PlacedObject);
                if (Custom == null) NativeSpiderPopulation.Prepare(Run, Occurrence);
            }

            if (Custom == null)
                foreach (var Source in Catalog)
                {
                    if ((PlacementPlan.Selected(Selected, Source)
                            && (Source is not PeakAttachmentSource || Selected.Count >= 2
                                && Selected[Selected.Count - 2] is NativeOrdinarySegmentSource { ContextualPeak: { } CatalogContextualPeak }
                                && ReferenceEquals(CatalogContextualPeak, Source)))
                        || !TransformOwnership.Contains(Container, Source.SegmentParent.transform)) continue;
                    var UnusedParent = DeterministicObjectIdentity.ResolveHierarchy(
                        Source.SegmentParent.transform, Container, PlacedRoot);
                    var UnusedCampfire = Source.SegmentCampfire
                        ? DeterministicObjectIdentity.ResolveHierarchy(
                            Source.SegmentCampfire.transform, Container, PlacedRoot)
                        : null;
                    if (UnusedParent) SetPlacedActive(UnusedParent.gameObject, false, IsClone, Originals.Active);
                    if (UnusedCampfire) SetPlacedActive(UnusedCampfire.gameObject, false, IsClone, Originals.Active);
                }

            foreach (var Source in Selected)
            {
                if (Source is not NativeOrdinarySegmentSource { ContextualPeak: { } Peak }
                    || !Peak.BridgeRoot || PlacementPlan.Selected(Selected, Peak)) continue;
                if (PassivePeak.HasValue)
                    throw new InvalidOperationException("A native placement cannot own multiple passive Peak fixtures.");
                PassivePeak = (
                    DeterministicObjectIdentity.ResolveHierarchy(Peak.SegmentParent.transform, Container, PlacedRoot)
                        ?? throw new InvalidOperationException(
                            "The placed native segment is missing its embedded Peak fixture."),
                    Peak);
            }

            foreach (var Source in Selected)
            {
                if (!FeatureGeneration.NativeCaldera(Source)) continue;
                if (!NativeCampfireSurfaces.TryGetValue(Source, out var surface))
                    throw new InvalidOperationException(
                        "The placed native Caldera container lost its authored external campfire support.");
                NativeCampfireSurfaceCollider = NativeFixtures.PreparePassiveVolcanoModel(
                    Source, surface, PlacedRoot, Originals, IsClone, CreatedObjects);
                break;
            }

            Transform? ContextualPeakRoot = null;
            var LastSource = PlacementPlan.LastSource(Selected);
            if (LastSource is PeakAttachmentSource FullPeakSource
                && PlacementPlan.FullPeakOwner(Selected, FullPeakSource) is NativeOrdinarySegmentSource { ContextualPeak: { } FullContextualPeak }
                && ReferenceEquals(FullContextualPeak, FullPeakSource))
            {
                ContextualPeakRoot = DeterministicObjectIdentity.ResolveHierarchy(
                    FullPeakSource.SegmentParent.transform, Container, PlacedRoot)
                    ?? throw new InvalidOperationException(
                        "The placed Peak group is missing its contextual full Peak attachment.");
                if (!IsClone) Originals.CaptureFullPeakPose(ContextualPeakRoot);
            }

            if (Frame.Rotated)
                PlacedRoot.SetPositionAndRotation(Frame.Point(AuthoredPosition), Frame.Rotate(AuthoredRotation));
            else if (IsClone) PlacedRoot.position = AuthoredPosition + Offset;
            else
            {
                var Position = AuthoredPosition + Offset;
                var Translation = Position - PlacedRoot.position;
                PlacedRoot.position = Position;
                RebaseCondorFlightPositions(PlacedRoot, Matrix4x4.Translate(Translation));
            }
            NativeTerrainMapProxy.ExternalSupport? PlacedNativeCampfireSurface = NativeCampfireSurfaceCollider
                ? new NativeTerrainMapProxy.ExternalSupport(NativeCampfireSurfaceCollider, PlacedRoot)
                : null;
            if (Custom == null)
                NativeFogHazards.ConfigureNativeFogHazards(
                    PlacedRoot, IsClone, Originals.Active, Configuration.Environment);
            if (IsClone) GloomVoice = GloomVoiceRuntime.PrepareGloomVoiceClone(Selected, PlacedRoot, Index, CreatedGloomProfiles);
            if (Custom != null)
            {
                CustomResolved = Custom.Resolve(PlacedObject);
                CustomResolved.FinalForce.Stage();
                CustomResolved.FreezePorts();
                NativeFixtures.ValidateCustomClone(Selected[0], CustomResolved, CustomResolved, Offset);
                if (!BiomeAuthoringCatalog.TryGet(Selected[0].Id, out CustomAuthoring))
                    throw new InvalidOperationException($"Registered segment '{Selected[0].Id}' lost its sealed authoring contract.");
                CustomAuthoring.ValidatePlacementRuntime(PlacedRoot);
            }
            Environment.Rebase(PlacedObject, Frame, !IsClone, ContextualPeakRoot);
            var SlotCount = 0;
            for (var i = 0; i < Selected.Count; i++)
                if (Selected[i] is not PeakAttachmentSource) SlotCount++;
            if (SlotCount == 0) throw new InvalidOperationException("A map attachment cannot be placed without a segment.");
            var HasSuccessor = Index + SlotCount < TotalSlots;
            var Slots = new MapSlot[SlotCount];
            var SlotIndex = 0;
            Transform? PeakAttachment = null;
            PeakAttachmentSource? PeakSource = null;
            var ReusedPeakAttachment = false;
            for (var i = 0; i < Selected.Count; i++)
            {
                var Source = Selected[i];
                if (Source is PeakAttachmentSource Peak)
                {
                    if (PeakAttachment || SlotIndex != Slots.Length)
                        throw new InvalidOperationException("Peak must be the final attachment in its placement.");
                    var PeakOwner = PlacementPlan.FullPeakOwner(Selected, Peak);
                    var PeakFrame = PlacementPlan.FullPeakFrame(Selected, Frame, Peak);
                    Transform? ExternalPeakAnchor = null;
                    if (PeakOwner is NativeOrdinarySegmentSource { ContextualPeak: { } ContextualPeak }
                        && ReferenceEquals(ContextualPeak, Peak))
                    {
                        PeakAttachment = ContextualPeakRoot
                            ?? throw new InvalidOperationException(
                                "The placed Peak group lost its contextual full Peak attachment.");
                        PlacementPlan.PlaceFullPeakRoot(Peak, PeakAttachment, PeakFrame);
                        ReusedPeakAttachment = !IsClone;
                    }
                    else
                    {
                        var Boundary = Peak.BridgeOwnerBoundary;
                        ExternalPeakAnchor = NativeFixtures.CreatePoseAnchor(
                            PlacedRoot,
                            NativeFixtures.Rebase(PlacedRoot, PeakFrame.Pose(new Pose(
                                Peak.AuthoredPosition(Boundary),
                                PlacementPlan.PeakOwnerRouteRotation(Peak, Boundary)))),
                            $"PLL_PeakAnchor_{Index:D3}");
                        CreatedObjects.Add(ExternalPeakAnchor.gameObject);
                        PeakAttachment = NativeFixtures.ClonePeakAtAnchor(
                            Peak, ExternalPeakAnchor, PeakFrame, CloneParent, Index, CreatedObjects);
                    }
                    Environment.Rebase(PeakAttachment.gameObject, PeakFrame, ReusedPeakAttachment);
                    Pose LocalRespawnPose;
                    if (ExternalPeakAnchor is not null)
                        LocalRespawnPose = new Pose(
                            ExternalPeakAnchor.localPosition, ExternalPeakAnchor.localRotation);
                    else
                    {
                        var Respawn = Peak.RespawnPose;
                        var LivePeakFrame = PlacementPlan.PeakFrame(Peak, PeakAttachment);
                        LocalRespawnPose = NativeFixtures.Rebase(PlacedRoot, LivePeakFrame.Pose(new Pose(
                            Peak.AuthoredPosition(Respawn),
                            Peak.AuthoredRotation(Respawn))));
                    }
                    AttachmentRespawn = NativeFixtures.CreatePoseAnchor(
                        PlacedRoot,
                        LocalRespawnPose,
                        $"PLL_PeakRespawn_{Index:D3}");
                    CreatedObjects.Add(AttachmentRespawn.gameObject);
                    PeakSource = Peak;
                    if (TransformOwnership.Contains(PlacedRoot, PeakAttachment)) PeakAttachment.gameObject.SetActive(true);
                    continue;
                }
                var SegmentParent = CustomResolved?.SegmentParent.transform
                    ?? DeterministicObjectIdentity.ResolveHierarchy(
                        Source.SegmentParent.transform, Container, PlacedRoot)
                    ?? throw new InvalidOperationException($"The {Source.Id} segment parent is outside its authored container.");
                var SegmentCampfire = CustomResolved?.SegmentCampfire.transform
                    ?? (Source.SegmentCampfire
                        ? DeterministicObjectIdentity.ResolveHierarchy(
                            Source.SegmentCampfire.transform, Container, PlacedRoot)
                        : null);
                if (Source.SegmentCampfire && !SegmentCampfire)
                    throw new InvalidOperationException($"The {Source.Id} campfire is outside its authored container.");
                var NativeGloomCampfireReady = false;
                if (IsClone && ReferenceEquals(Source, Selected[0]) && GloomVoiceRuntime.NativeGloomPair(Selected))
                {
                    NativePeakCampfireContract.ValidatePrebuilt(
                        SegmentCampfire!, "The cloned native Gloom campfire root");
                    NativeGloomCampfireReady = true;
                }
                var ProgressionBoundary = PlacementPlan.ProgressionBoundary(Selected, Source, HasSuccessor);
                var WallNext = Source.WallNext
                    ? DeterministicObjectIdentity.ResolveHierarchy(
                        Source.WallNext.transform, Container, PlacedRoot)?.gameObject
                    : null;
                var WallPrevious = Source.WallPrevious
                    ? DeterministicObjectIdentity.ResolveHierarchy(
                        Source.WallPrevious.transform, Container, PlacedRoot)?.gameObject
                    : null;
                var Reconnect = CustomResolved?.Start;
                if (CustomResolved == null && Source.ReconnectSpawnPos && TransformOwnership.Contains(Container, Source.ReconnectSpawnPos))
                    Reconnect = DeterministicObjectIdentity.ResolveHierarchy(
                        Source.ReconnectSpawnPos, Container, PlacedRoot);
                if (!Reconnect)
                {
                    var Pose = Source.Start
                        ?? (Source.EntryPorts.Count != 0
                            ? Source.EntryPorts[0].Pose
                            : throw new InvalidOperationException($"The {Source.Id} segment has no reconnect pose."));
                    Reconnect = NativeFixtures.CreatePoseAnchor(
                        PlacedRoot,
                        NativeFixtures.Rebase(PlacedRoot, Frame.Pose(new Pose(
                            Source.AuthoredPosition(Pose),
                            Source.AuthoredRotation(Pose)))),
                        $"PLL_Reconnect_{SlotIndex:D3}");
                    CreatedObjects.Add(Reconnect.gameObject);
                }
                var NadirSpawnPoints = Array.Empty<Transform>();
                Transform? NadirGate = null;
                Transform? NadirPeak = null;
                Transform? NadirAmbience = null;
                Peak.ForceHeroTextTrigger? NadirHeroTitle = null;
                Transform? VoidWaterRoot = null;
                if (Source is NadirLayoutSegmentSource Nadir)
                {
                    NadirGate = DeterministicObjectIdentity.ResolveHierarchy(
                        Nadir.Gate
                            ?? throw new InvalidOperationException("PEAK's native Nadir gate binding is unavailable."),
                        Container,
                        PlacedRoot);
                    NadirPeak = DeterministicObjectIdentity.ResolveHierarchy(
                        Nadir.PeakRoot
                            ?? throw new InvalidOperationException("PEAK's native Nadir Peak binding is unavailable."),
                        Container,
                        PlacedRoot);
                    if (!NadirGate || !NadirPeak || !TransformOwnership.Contains(PlacedRoot, NadirPeak)
                        || !NadirGate.IsChildOf(NadirPeak))
                        throw new InvalidOperationException("The placed Nadir gate and Peak fixture ownership is invalid.");
                    var PlacedNadirBridge = DeterministicObjectIdentity.ResolveHierarchy(
                        Nadir.BridgeRoot
                            ?? throw new InvalidOperationException("PEAK's native Nadir bridge binding is unavailable."),
                        Container,
                        PlacedRoot);
                    var ContractNadirBridge = NativeSegmentPorts.CreateNadirBridge(NadirPeak);
                    if (!PlacedNadirBridge || PlacedNadirBridge.GetInstanceID() != ContractNadirBridge.GetInstanceID())
                        throw new InvalidOperationException("The placed Nadir bridge identity is invalid.");
                    NadirAmbience = DeterministicObjectIdentity.ResolveHierarchy(
                        Nadir.AmbienceRoot
                            ?? throw new InvalidOperationException("PEAK's native Nadir ambience binding is unavailable."),
                        Container,
                        PlacedRoot);
                    if (!NadirAmbience || NadirAmbience.parent != PlacedRoot
                        || NadirAmbience.GetComponents<AudioLoop>().Length != 1
                        || NadirAmbience.GetComponentsInChildren<AudioLoop>(true).Length != 1)
                        throw new InvalidOperationException("The placed Nadir AudioLoop ownership is invalid.");
                    var HeroTitleRoot = DeterministicObjectIdentity.ResolveHierarchy(
                        (Nadir.HeroTitle
                            ?? throw new InvalidOperationException(
                                "PEAK's native Nadir hero-title binding is unavailable.")).transform,
                        Container,
                        PlacedRoot);
                    var HeroTitles = PlacedRoot.GetComponentsInChildren<Peak.ForceHeroTextTrigger>(true);
                    if (!HeroTitleRoot || !TransformOwnership.Contains(PlacedRoot, HeroTitleRoot)
                        || HeroTitles.Length != 1 || !HeroTitles[0]
                        || HeroTitles[0].transform.GetInstanceID() != HeroTitleRoot.GetInstanceID()
                        || HeroTitles[0].GetType() != typeof(Peak.ForceHeroTextTrigger)
                        || !HeroTitles[0].enabled || !HeroTitles[0].gameObject.activeSelf)
                        throw new InvalidOperationException(
                            "The placed Nadir hero-title trigger ownership is invalid.");
                    NadirHeroTitle = HeroTitles[0];
                    NadirSpawnPoints = new Transform[Nadir.SpawnPoints.Count];
                    for (var SpawnIndex = 0; SpawnIndex < NadirSpawnPoints.Length; SpawnIndex++)
                        NadirSpawnPoints[SpawnIndex] = DeterministicObjectIdentity.ResolveHierarchy(
                            Nadir.SpawnPoints[SpawnIndex], Container, PlacedRoot)
                            ?? throw new InvalidOperationException("The placed Nadir player formation is incomplete.");
                    var WaterVisual = DeterministicObjectIdentity.ResolveHierarchy(
                        Nadir.VoidWaterVisual
                            ?? throw new InvalidOperationException("PEAK's Void Water surface binding is unavailable."),
                        Container,
                        PlacedRoot);
                    var WaterZone = DeterministicObjectIdentity.ResolveHierarchy(
                        Nadir.VoidWaterZone
                            ?? throw new InvalidOperationException("PEAK's Void Water zone binding is unavailable."),
                        Container,
                        PlacedRoot);
                    if (!WaterVisual || !WaterZone || WaterVisual == WaterZone)
                        throw new InvalidOperationException("The placed Nadir Void Water pair is incomplete.");
                    var WaterOwner = new GameObject($"PLL_VoidWater_{Index + SlotIndex:D3}");
                    WaterOwner.transform.SetParent(PlacedRoot, false);
                    WaterVisual.SetParent(WaterOwner.transform, true);
                    WaterZone.SetParent(WaterOwner.transform, true);
                    WaterOwner.SetActive(false);
                    VoidWaterRoot = WaterOwner.transform;
                }

                var CarrierBiome = LayoutSegmentBehavior.CarrierBiome(Source);
                var Segment = new MapHandler.MapSegment
                {
                    wallNext = WallNext!,
                    wallPrevious = WallPrevious,
                    reconnectSpawnPos = Reconnect,
                    hasVariant = false,
                    isVariant = false
                };
                Segment._biome = CarrierBiome;
                Segment._segmentParent = SegmentParent.gameObject;
                Segment._segmentCampfire = SegmentCampfire ? SegmentCampfire.gameObject : null!;
                Segment._dayNightProfile = GloomVoice is not null && ReferenceEquals(Source, Selected[0])
                    ? GloomVoice.Profile
                    : Source.DayNightProfile!;

                var Boxes = Configuration.Biomes.SpatialFor(Source).Boxes;
                for (var BoxIndex = 0; BoxIndex < Boxes.Count; BoxIndex++)
                {
                    var Box = Boxes[BoxIndex];
                    var Wall = new GameObject($"PLL_Boundary_{BoxIndex:D3}");
                    CreatedObjects.Add(Wall);
                    Wall.SetActive(false);
                    Wall.layer = BoundaryLayer;
                    Wall.transform.SetParent(SegmentParent, false);
                    Wall.transform.localPosition = Box.Center;
                    Wall.transform.localRotation = Box.Rotation;
                    Wall.AddComponent<BoxCollider>().size = Box.Size;
                    Wall.AddComponent<ClimbModifierSurface>().onlySlideDown = true;
                    Wall.SetActive(true);
                }
                SetPlacedActive(SegmentParent.gameObject, true, IsClone, Originals.Active);
                if (SegmentCampfire) SetPlacedActive(SegmentCampfire.gameObject, true, IsClone, Originals.Active);
                Transform? NativeCampfireSupport = null;
                NativeTerrainMapProxy.ExternalSupport? NativeCampfireSurface = null;
                if (FeatureGeneration.NativeCaldera(Source))
                {
                    if (!TransformOwnership.Contains(Container, NativeVolcanoParent))
                        throw new InvalidOperationException(
                            "PEAK's native Caldera and Volcano segment roots do not share an authored container.");
                    NativeCampfireSupport = DeterministicObjectIdentity.ResolveHierarchy(
                        NativeVolcanoParent, Container, PlacedRoot);
                    if (!NativeCampfireSupport)
                        throw new InvalidOperationException(
                            "The placed native Caldera container is missing its authored TheKiln support root.");
                    if (IsClone)
                    {
                        var RandomState = UnityEngine.Random.state;
                        try { NativeFixtures.ClearDeferredFeatureOutput(NativeCampfireSupport.gameObject); }
                        finally { UnityEngine.Random.state = RandomState; }
                    }
                    if (!PlacedNativeCampfireSurface.HasValue)
                        throw new InvalidOperationException(
                            "The placed native Caldera container lost its occurrence-owned campfire surface.");
                    NativeCampfireSurface = PlacedNativeCampfireSurface.Value;
                }
                MapSlot.NadirFacet NadirFacet = Source is NadirLayoutSegmentSource
                    ? new MapSlot.NadirFacet.Placed(
                        NadirGate!, NadirPeak!, NadirAmbience!, NadirHeroTitle!,
                        NadirSpawnPoints, VoidWaterRoot!, NadirScoutsHonor[Index + SlotIndex])
                    : MapSlot.NadirFacet.Absent.Instance;
                var Slot = new MapSlot(
                    Index + SlotIndex,
                    Source,
                    Segment,
                    NadirFacet,

                    Occurrence,
                    ProgressionBoundary,
                    NativeCampfireSupport,
                    NativeCampfireSurface,
                    CustomResolved,
                    CustomAuthoring is { Modifiers: var Modifiers }
                        && (Modifiers.NightCold || Modifiers.MesaHeatstroke)
                        ? new NativeModifierRuntime(Modifiers) : null,
                    Frame,
                    IsClone,
                    !Configuration.Layout.ReplaceGenerated
                        && !IsClone
                        && AuthoredTerrain.Contains(SegmentParent.gameObject.GetInstanceID()),
                    !SegmentCampfire
                        ? FeatureGeneration.NativeCampfireOptional(Source)
                        : NativeGloomCampfireReady
                            || !Configuration.Layout.ReplaceGenerated
                                && !IsClone
                                && AuthoredCampfires.Contains(SegmentCampfire.gameObject.GetInstanceID()));
                Slots[SlotIndex++] = Slot;
            }

            Occurrence.Bind(Slots, CustomResolved is not null
                    ? CustomAuthoring!.BindEntityZones(CustomResolved.Root.Value)
                    : Array.Empty<EntityZoneBinding>(), CustomResolved is not null
                    ? CustomAuthoring!.BindHazardZones(CustomResolved.Root.Value)
                    : Array.Empty<HazardZoneBinding>());
            var AmuletOwner = Slots[Slots.Length - 1];
            if (CustomResolved is not null)
                CustomAuthoring!.ConfigureAmulets(
                    CustomResolved.Root.Value, Run, AmuletOwner);
            else if (Slots[0].Source is NativeOrdinarySegmentSource NativeSource)
                Run.Quicksave.EnrollAmuletSource(AmuletOwner,
                    NativeSource.NadirAmulets.PlacedGenerator(PlacedRoot), NativeSource.NadirAmulets.SourceBindings[0]);
            Occurrence.SupplementalReady = !IsClone || Occurrence.SupplementalRoots.Count == 0;

            if (PassivePeak is { } passive)
                NativeFixtures.BindBridgeStatue(
                    NativeFixtures.PassivePeakBridgeOwner(Slots),
                    NativeFixtures.PreparePassivePeak(
                        passive.Source, passive.Root, PlacedRoot, Index, IsClone, CreatedObjects));

            if (GloomVoice is not null)
            {
                if (Slots.Length != 2 || Slots[0].Index != GloomVoice.FirstSlot
                    || Slots[1].Index != GloomVoice.LastSlot)
                    throw new InvalidOperationException("The cloned native Gloom/Citadel voice manager must own its paired layout slots.");
                Slots[0].BindGloomVoice(GloomVoice);
                Slots[1].BindGloomVoice(GloomVoice);
            }

            Occurrence.ConfigurationReady = Occurrence.SupplementalReady;
            foreach (var Slot in Slots)
                if (!Slot.FeaturesReady)
                {
                    Occurrence.ConfigurationReady = false;
                    break;
                }

            if (PeakAttachment)
            {
                var Owner = Slots[Slots.Length - 1];
                Owner.AttachPeak(PeakAttachment, AttachmentRespawn!, PeakSource!);
                var Statue = NativeFixtures.ValidateBridgeStatue(PeakAttachment, !ReusedPeakAttachment);
                if (ReusedPeakAttachment && Statue.GetComponent<PhotonView>().ViewID <= 0)
                    throw new InvalidOperationException(
                        "The reused Peak Bridge Scout Statue has no native PhotonView identity.");
                NativeFixtures.BindBridgeStatue(Owner, Statue, ReusedPeakAttachment ? Originals : null);
            }

            if (IsClone)
            {
                var RandomState = UnityEngine.Random.state;
                var Cleared = new HashSet<int>();
                using var HeightmapGeneration = OccurrenceHeightmapMeshes.EnterGeneration(Occurrence.Roots);
                try
                {
                    for (var i = 0; i < Slots.Length; i++)
                    {
                        if (!Slots[i].TerrainReady)
                            NativeFixtures.ClearDeferredFeatureOutput(Slots[i].Segment.segmentParent, Cleared);
                        if (!Slots[i].CampfireReady)
                            NativeFixtures.ClearDeferredFeatureOutput(Slots[i].Segment.segmentCampfire, Cleared);
                    }
                    foreach (var Supplemental in Occurrence.SupplementalRoots)
                        NativeFixtures.ClearDeferredFeatureOutput(Supplemental.gameObject, Cleared);
                }
                finally { UnityEngine.Random.state = RandomState; }
            }
            if (IsClone && Index >= InitialGenerationSlotCount)
                for (var i = 0; i < Slots.Length; i++)
                {
                    Slots[i].Segment.segmentParent.SetActive(false);
                    if (Slots[i].Segment.segmentCampfire) Slots[i].Segment.segmentCampfire.SetActive(false);
                    if (Slots[i].PeakAttachment) Slots[i].PeakAttachment!.gameObject.SetActive(false);
                }
            if (!IsClone)
            {
                Run.NativeSpawns.EnrollRetained(Occurrence);
                SetPlacedActive(PlacedObject, true, false, Originals.Active);
                foreach (var Slot in Slots) NativeFixtures.ValidateActivatedCustomClone(Slot);
                RebaseTrapWarningLines(new[] { PlacedRoot });
            }
            return Slots;
        }
        catch
        {
            if (IsClone && PlacedObject)
            {
                OccurrenceHeightmapMeshes.ReleaseNow(PlacedObject.transform);
                UnityEngine.Object.DestroyImmediate(PlacedObject);
            }
            throw;
        }
    }
}
