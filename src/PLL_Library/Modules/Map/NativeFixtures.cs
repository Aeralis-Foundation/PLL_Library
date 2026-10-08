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

internal static class NativeFixtures
{

    internal sealed class DetachedFixture
    {
        internal DetachedFixture(Transform Value)
        {
            this.Value = Value;
            Parent = Value.parent;
            Sibling = Value.GetSiblingIndex();
            Position = Value.position;
            Rotation = Value.rotation;
            LocalScale = Value.localScale;
            Active = Value.gameObject.activeSelf;
        }

        internal Transform Value { get; }
        Transform? Parent { get; }
        int Sibling { get; }
        Vector3 Position { get; }
        Quaternion Rotation { get; }
        Vector3 LocalScale { get; }
        bool Active { get; }
        internal bool LightingGeometryDirty => Value
            && (Value.position != Position || Math.Abs(Quaternion.Dot(Value.rotation, Rotation)) < 0.999999f
                || Value.localScale != LocalScale || Value.gameObject.activeSelf != Active);

        internal void Bind(Transform Parent, Vector3 Offset)
        {
            if (!Value) return;
            Value.SetParent(Parent, true);
            Value.SetPositionAndRotation(Position + Offset, Rotation);
        }

        internal void Place(Pose Pose)
        {
            if (Value) Value.SetPositionAndRotation(Pose.position, Pose.rotation);
        }

        internal void Place(Pose SourceAnchor, Pose TargetAnchor)
        {
            if (!Value) return;
            var Rotation = TargetAnchor.rotation * Quaternion.Inverse(SourceAnchor.rotation);
            Value.SetPositionAndRotation(
                TargetAnchor.position + Rotation * (Position - SourceAnchor.position),
                Rotation * this.Rotation);
        }

        internal void Restore()
        {
            if (!Value) return;
            Value.SetParent(Parent, true);
            if (Parent) Value.SetSiblingIndex(Math.Min(Sibling, Parent.childCount - 1));
            Value.SetPositionAndRotation(Position, Rotation);
            Value.localScale = LocalScale;
            Value.gameObject.SetActive(Active);
        }
    }

    internal sealed class StartKit
    {
        readonly Transform DetachedParent;
        readonly Transform PrimarySpawn;
        readonly Pose SourceAnchor;
        readonly Transform[] SpawnPoints;
        readonly Vector3[] SpawnPositions;
        readonly Quaternion[] SpawnRotations;
        readonly RunEnvironmentConfiguration Configuration;
        bool Detached;

        internal StartKit(
            Transform DetachedParent,
            DetachedFixture Plane,
            DetachedFixture Spawns,
            Transform PrimarySpawn,
            RunEnvironmentConfiguration Configuration)
        {
            this.DetachedParent = DetachedParent;
            this.Plane = Plane;
            this.Spawns = Spawns;
            this.Configuration = Configuration;
            this.PrimarySpawn = PrimarySpawn;
            SourceAnchor = new Pose(PrimarySpawn.position, PrimarySpawn.rotation);
            var Components = Spawns.Value.GetComponentsInChildren<SpawnPoint>(true);
            SpawnPoints = Array.ConvertAll(Components, Value => Value.transform);
            SpawnPositions = Array.ConvertAll(SpawnPoints, Value => Value.localPosition);
            SpawnRotations = Array.ConvertAll(SpawnPoints, Value => Value.localRotation);
        }

        internal DetachedFixture Plane { get; }
        internal DetachedFixture Spawns { get; }
        internal bool LightingGeometryDirty => Plane.LightingGeometryDirty;

        internal bool OwnedBy(Transform Root) =>
            TransformOwnership.Contains(Root, Plane.Value)
            || TransformOwnership.Contains(Root, Spawns.Value);

        internal void Detach()
        {
            if (Detached) throw new InvalidOperationException("PEAK's start kit is already detached.");
            try
            {
                Plane.Value.SetParent(DetachedParent, true);
                Spawns.Value.SetParent(DetachedParent, true);
                if (Configuration.NoPlane) Plane.Value.gameObject.SetActive(false);
                Detached = true;
            }
            catch (Exception DetachFailure)
            {
                try { Restore(); }
                catch (Exception RestoreFailure)
                {
                    throw new AggregateException(
                        "Start-kit detachment and restoration both failed.", DetachFailure, RestoreFailure);
                }
                throw;
            }
        }

        internal void Place(MapSlot First, NativePlacement.ResolvedLayoutStart Start)
        {
            if (!string.Equals(First.Source.StartConnector ?? PLLLibrary.StartConnector,
                    PLLLibrary.StartConnector, StringComparison.Ordinal))
                throw new InvalidOperationException($"Segment '{First.Source.Id}' has no compatible Start port.");
            var StartPose = Start.Pose;
            var Target = First.Frame.Pose(new Pose(
                First.Source.AuthoredPosition(StartPose),
                First.Source.AuthoredRotation(StartPose)));
            var Anchors = First.CustomResolved?.AuthoringAnchors;
            if (Anchors)
                Target = new Pose(Anchors.PlayerFormationRoot.transform.position, Anchors.PlayerFormationRoot.transform.rotation);
            if (!Configuration.RelocateStart && !TransformOwnership.SamePose(SourceAnchor, Target))
                throw new InvalidOperationException("Relocate Start is required when the first segment does not use PEAK's Beach start.");
            if (Configuration.RelocateStart) Spawns.Place(SourceAnchor, Target);
            if (Configuration.RelocateStart && Start.NativeFormation is { } NativeStart)
                NativeStart.Place(Spawns.Value, Target);
            if (!TransformOwnership.SamePose(new Pose(PrimarySpawn.position, PrimarySpawn.rotation), Target))
                throw new InvalidOperationException("PEAK's primary SpawnPoint could not be aligned to the authored player formation root.");

            if (!Configuration.RelocatePlane || Configuration.NoPlane) return;
            if (Anchors) Plane.Place(new Pose(Anchors.PlaneAnchor.position, Anchors.PlaneAnchor.rotation));
            else Plane.Place(SourceAnchor, Target);
        }

        internal void Restore()
        {
            Detached = false;
            Spawns.Restore();
            for (var i = 0; i < SpawnPoints.Length; i++)
                if (SpawnPoints[i])
                {
                    SpawnPoints[i].localPosition = SpawnPositions[i];
                    SpawnPoints[i].localRotation = SpawnRotations[i];
                }
            Plane.Restore();
        }

    }

    internal static void FindStartKitRoots(
        LayoutSegmentSource Beach,
        out Transform Plane,
        out Transform SpawnRoot,
        out Transform Anchor)
    {
        Transform? FoundPlane = null;
        foreach (var Candidate in Beach.SegmentParent.GetComponentsInChildren<Transform>(true))
        {
            if (!Candidate.name.Equals("crashed plane", StringComparison.OrdinalIgnoreCase)) continue;
            if (Candidate.GetComponents<EnablingSubstep>().Length != 1)
                throw new InvalidOperationException("PEAK's Beach crashed-plane root does not own its native enabling component.");
            if (FoundPlane) throw new InvalidOperationException("The Beach segment contains more than one crashed plane.");
            FoundPlane = Candidate;
        }
        if (!FoundPlane) throw new InvalidOperationException("The Beach crashed plane was not found.");

        var FoundAnchor = Beach.ReconnectSpawnPos;
        var FoundSpawnRoot = FoundAnchor ? FoundAnchor.parent : null;
        var SpawnRootActive = FoundSpawnRoot
            && FoundSpawnRoot.gameObject.activeInHierarchy;
        if (!FoundAnchor || !FoundSpawnRoot || !SpawnRootActive || FoundSpawnRoot.childCount != 4)
            throw new InvalidOperationException("PEAK's active four-player start group was not found.");
        var Spawns = FoundSpawnRoot.GetComponentsInChildren<SpawnPoint>(true);
        if (Spawns.Length != 4)
            throw new InvalidOperationException("PEAK's active four-player start group does not own exactly four spawn points.");
        var Found = new bool[Spawns.Length];
        for (var i = 0; i < FoundSpawnRoot.childCount; i++)
        {
            var Child = FoundSpawnRoot.GetChild(i);
            var Components = Child.GetComponents<SpawnPoint>();
            if (Components.Length != 1) throw new InvalidOperationException(
                "PEAK's active four-player start group does not directly own each spawn point.");
            var Spawn = Components[0];
            var ChildActive = Child.gameObject.activeInHierarchy;
            if (!Spawn.enabled || !ChildActive || !Spawn.startPassedOut
                || Spawn.index < 1 || Spawn.index > Found.Length || Found[Spawn.index - 1])
                throw new InvalidOperationException("PEAK's active four-player start formation is invalid.");
            Found[Spawn.index - 1] = true;
            if (Spawn.index == 1 && Child != FoundAnchor)
                throw new InvalidOperationException("PEAK's primary start anchor does not own SpawnPoint index 1.");
        }
        for (var i = 0; i < Found.Length; i++)
            if (!Found[i]) throw new InvalidOperationException("PEAK's active start formation is incomplete.");
        Plane = FoundPlane;
        SpawnRoot = FoundSpawnRoot;
        Anchor = FoundAnchor;
    }

    internal static StartKit CaptureStartKit(
        Transform Parent,
        Transform Plane,
        Transform SpawnRoot,
        Transform Anchor,
        RunEnvironmentConfiguration Configuration) =>
        new(Parent, new DetachedFixture(Plane), new DetachedFixture(SpawnRoot), Anchor, Configuration);

    internal static void CopyNativeCalderaBiome(Transform AuthoredRoot, GameObject Clone)
    {
        var AuthoredBiomes = AuthoredRoot.GetComponentsInParent<Biome>(true);
        if (AuthoredBiomes.Length != 1)
            throw new InvalidOperationException(
                "PEAK's native Caldera/Volcano container must have exactly one authored Biome ancestor.");
        if (Clone.GetComponents<Biome>().Length != 0)
            throw new InvalidOperationException(
                "The cloned native Caldera/Volcano root already contains a Biome component.");
        var AuthoredBiome = AuthoredBiomes[0];
        var PlacedBiome = Clone.AddComponent<Biome>();
        PlacedBiome.mapHandler = AuthoredBiome.mapHandler;
        PlacedBiome.biomeType = AuthoredBiome.biomeType;
    }
    internal static MeshCollider PreparePassiveVolcanoModel(
        LayoutSegmentSource Source,
        NativeTerrainMapProxy.ExternalSupport Support,
        Transform PlacedRoot,
        OriginalMapSources Originals,
        bool IsClone,
        List<GameObject> CreatedObjects)
    {
        var SourceCollider = Support.Source as MeshCollider;
        var Model = SourceCollider ? SourceCollider.transform : null;
        var Fixture = Model ? Model.parent : null;
        var SiblingParent = Source.Container.parent;
        var Scene = Source.Container.gameObject.scene;
        if (!SourceCollider || !Model || !Fixture || !SiblingParent || Fixture.parent != SiblingParent
            || Fixture == Source.Container || Fixture.name != "VolcanoModel" || Model.name != "VolcanoModel"
            || Fixture.gameObject.scene != Scene || Model.gameObject.scene != Scene
            || PlacedRoot.gameObject.scene != Scene || !Fixture.gameObject.activeSelf
            || !Model.gameObject.activeSelf || PlacedRoot.gameObject.activeSelf
            || Model.GetComponents<MeshFilter>().Length != 1
            || Model.GetComponents<MeshRenderer>().Length != 1
            || Model.GetComponents<Collider>().Length != 1
            || Model.GetComponent<MeshCollider>() != SourceCollider)
            throw new InvalidOperationException(
                "PEAK's native VolcanoModel does not match its authored sibling path and combined-mesh ownership.");

        PlacementExecution.RememberActive(Originals.Active, Fixture.gameObject);
        var Clone = UnityEngine.Object.Instantiate(Fixture.gameObject, PlacedRoot, true);
        if (!IsClone) CreatedObjects.Add(Clone);
        try
        {
            Clone.name = Fixture.name;
            Clone.SetActive(false);
            var ClonedModel = DeterministicObjectIdentity.ResolveHierarchy(Model, Fixture, Clone.transform)
                ?? throw new InvalidOperationException("The cloned VolcanoModel lost its combined-mesh child.");
            NormalizePassiveVolcanoModel(Clone, ClonedModel);
            Clone.SetActive(true);
            return ValidatePassiveVolcanoModel(Clone, PlacedRoot);
        }
        catch
        {
            if (!IsClone) CreatedObjects.Remove(Clone);
            if (Clone) UnityEngine.Object.DestroyImmediate(Clone);
            throw;
        }
    }
    internal static void NormalizePassiveVolcanoModel(GameObject Root, Transform Model)
    {
        if (!Model || Model.parent != Root.transform)
            throw new InvalidOperationException("The cloned VolcanoModel has no direct combined-mesh child.");
        var Filters = Model.GetComponents<MeshFilter>();
        var Renderers = Model.GetComponents<MeshRenderer>();
        var Colliders = Model.GetComponents<Collider>();
        if (Filters.Length != 1 || Renderers.Length != 1 || Colliders.Length != 1
            || Colliders[0] is not MeshCollider collider || !Filters[0].sharedMesh
            || collider.sharedMesh != Filters[0].sharedMesh || !HasPassivePeakMaterials(Renderers[0])
            || !collider.enabled || collider.isTrigger || collider.convex || collider.attachedRigidbody)
            throw new InvalidOperationException("The cloned VolcanoModel has no usable combined static mesh.");

        for (var i = Root.transform.childCount - 1; i >= 0; i--)
        {
            var Child = Root.transform.GetChild(i);
            if (Child != Model) UnityEngine.Object.DestroyImmediate(Child.gameObject);
        }
        for (var i = Model.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(Model.GetChild(i).gameObject);

        foreach (var Component in Root.GetComponents<Component>())
        {
            if (!Component)
                throw new InvalidOperationException("The cloned VolcanoModel root contains an unresolved component.");
            if (Component is not Transform) UnityEngine.Object.DestroyImmediate(Component);
        }
        foreach (var Component in Model.GetComponents<Component>())
        {
            if (!Component)
                throw new InvalidOperationException("The cloned VolcanoModel mesh contains an unresolved component.");
            if (Component is not Transform && Component != Filters[0]
                && Component != Renderers[0] && Component != collider)
                UnityEngine.Object.DestroyImmediate(Component);
        }
        Renderers[0].enabled = true;
    }
    internal static MeshCollider ValidatePassiveVolcanoModel(GameObject Root, Transform PlacedRoot)
    {
        if (Root.name != "VolcanoModel" || Root.transform.parent != PlacedRoot
            || Root.scene != PlacedRoot.gameObject.scene || !Root.activeSelf
            || PlacedRoot.gameObject.activeSelf || Root.GetComponents<Component>().Length != 1
            || Root.transform.childCount != 1)
            throw new InvalidOperationException("The passive VolcanoModel must remain an exact static root.");
        var Model = Root.transform.GetChild(0);
        var Filter = Model.GetComponent<MeshFilter>();
        var Renderer = Model.GetComponent<MeshRenderer>();
        var Collider = Model.GetComponent<MeshCollider>();
        if (Model.name != "VolcanoModel" || Model.parent != Root.transform || Model.childCount != 0
            || Model.gameObject.scene != PlacedRoot.gameObject.scene || !Model.gameObject.activeSelf
            || Model.GetComponents<Component>().Length != 4
            || !Filter || !Renderer || !Collider
            || Filter.GetComponents<MeshFilter>().Length != 1
            || Renderer.GetComponents<MeshRenderer>().Length != 1
            || Collider.GetComponents<Collider>().Length != 1
            || !Filter.sharedMesh || Collider.sharedMesh != Filter.sharedMesh
            || !Renderer.enabled || !HasPassivePeakMaterials(Renderer)
            || !Collider.enabled || Collider.isTrigger || Collider.convex || Collider.attachedRigidbody)
            throw new InvalidOperationException("The passive VolcanoModel must own one static mesh, renderer, and collider.");
        return Collider;
    }
    internal static Peak.ScoutStatue PreparePassivePeak(
        PeakAttachmentSource Source,
        Transform EmbeddedPeak,
        Transform PlacedRoot,
        int Index,
        bool IsClone,
        List<GameObject> CreatedObjects)
    {
        if (!EmbeddedPeak || EmbeddedPeak == PlacedRoot || !TransformOwnership.Contains(PlacedRoot, EmbeddedPeak)
            || EmbeddedPeak.gameObject.activeSelf || EmbeddedPeak.gameObject.activeInHierarchy
            || PlacedRoot.gameObject.activeInHierarchy)
            throw new InvalidOperationException("A passive Peak fixture must be staged inactive inside its placed container.");

        var Passive = EmbeddedPeak;
        if (!IsClone)
        {
            var Clone = UnityEngine.Object.Instantiate(EmbeddedPeak.gameObject, PlacedRoot, true);
            CreatedObjects.Add(Clone);
            Passive = Clone.transform;
        }
        if (Passive.gameObject.activeSelf || Passive.gameObject.activeInHierarchy)
            throw new InvalidOperationException("The cloned passive Peak fixture became active before sanitization.");
        Passive.position += PlacedRoot.TransformVector(Source.Translation);
        ExtractPassivePeakCloudRings(Passive, PlacedRoot, CreatedObjects);

        var Owners = ValidatePassivePeakOwners(Passive);
        var Presentation = Owners.NormalZone.transform;
        Presentation.SetParent(PlacedRoot, true);
        Presentation.name = $"PLL_PassivePeakSpecialDay_{Index:D3}";
        CreatedObjects.Add(Presentation.gameObject);

        PhotonCloneIdentity.Reset(Passive.gameObject);
        PrunePassivePeakOwners(Passive, Owners.Handler, Owners.Statue.transform);
        StripPassivePeakComponents(Passive.gameObject, Owners.Statue.transform);
        if (Passive.parent != PlacedRoot) Passive.SetParent(PlacedRoot, true);
        var Statue = ValidatePassivePeak(Passive.gameObject);
        Passive.name = $"PLL_PassivePeak_{Index:D3}";
        Passive.gameObject.SetActive(true);
        if (Passive.gameObject.activeInHierarchy)
            throw new InvalidOperationException("The passive Peak fixture activated outside its staged container lifetime.");
        return Statue;
    }
    internal static void ExtractPassivePeakCloudRings(
        Transform Passive,
        Transform PlacedRoot,
        List<GameObject> CreatedObjects)
    {
        Transform? First = null;
        Transform? Second = null;
        var ChildCount = Passive.childCount;
        for (var i = 0; i < ChildCount; i++)
        {
            var Child = Passive.GetChild(i);
            switch (Child.name)
            {
                case "Cloud Ring (1)":
                    if (First) throw new InvalidOperationException("The passive Peak repeats Cloud Ring (1).");
                    First = Child;
                    break;
                case "Cloud Ring (2)":
                    if (Second) throw new InvalidOperationException("The passive Peak repeats Cloud Ring (2).");
                    Second = Child;
                    break;
            }
        }
        if (!First || !Second)
            throw new InvalidOperationException("The passive Peak must own direct Cloud Ring (1) and Cloud Ring (2) branches.");

        foreach (var Ring in new[] { First!, Second! })
        {
            if (Ring.childCount != 0 || Ring.GetComponents<Component>().Length != 4
                || Ring.GetComponents<ParticleSystem>().Length != 1
                || Ring.GetComponents<Renderer>().Length != 1
                || Ring.GetComponents<ParticleSystemCenter>().Length != 1)
                throw new InvalidOperationException($"The passive Peak {Ring.name} branch is not visual-only.");
            var Active = Ring.gameObject.activeSelf;
            Ring.SetParent(PlacedRoot, true);
            if (Ring.gameObject.activeSelf != Active)
                throw new InvalidOperationException($"The passive Peak {Ring.name} branch changed active state while reparenting.");
            CreatedObjects.Add(Ring.gameObject);
        }
    }
    internal static (PeakHandler Handler, SpecialDayZone NormalZone, Peak.ScoutStatue Statue) ValidatePassivePeakOwners(
        Transform Root)
    {
        var Handlers = Root.GetComponentsInChildren<PeakHandler>(true);
        if (Handlers.Length != 1 || Handlers[0].transform != Root)
            throw new InvalidOperationException("The passive Peak source must own exactly one root PeakHandler.");
        var Handler = Handlers[0];
        var TerminalRoots = new[]
        {
            Handler.endCutscene,
            Handler.endCutsceneFinal,
            Handler.endCutsceneFinalSpecialDay,
            Handler.normalSpecialDay,
            Handler.peakSequence
        };
        var TerminalIds = new HashSet<int>();
        foreach (var Terminal in TerminalRoots)
            if (!Terminal || Terminal.transform.parent != Root || !TerminalIds.Add(Terminal.GetInstanceID()))
                throw new InvalidOperationException("The passive Peak source has invalid terminal branch ownership.");
        if (!Handler.scoutmaster || !Handler.scoutmaster.transform.IsChildOf(Handler.endCutscene.transform)
            || !Handler.flareBox || !Handler.flareBox.transform.IsChildOf(Handler.endCutsceneFinal.transform))
            throw new InvalidOperationException("The passive Peak source has invalid nested cutscene ownership.");

        var NormalZone = PeakAttachmentSource.RequireNormalSpecialDayZone(Handler);
        var NormalRoot = NormalZone.transform;
        if (NormalRoot.name != "SpecialDay Peak" || NormalRoot.GetComponents<Component>().Length != 2
            || NormalRoot.GetComponents<SpecialDayZone>().Length != 1 || !NormalZone.enabled
            || NormalZone.globalZone || !NormalRoot.gameObject.activeSelf || NormalRoot.childCount != 1)
            throw new InvalidOperationException("The passive Peak normal SpecialDay zone has invalid native ownership.");
        var LightRoot = NormalRoot.GetChild(0);
        var Light = LightRoot.GetComponent<Light>();
        var AdditionalLight = LightRoot.GetComponent<UniversalAdditionalLightData>();
        if (LightRoot.name != "Directional Light" || LightRoot.childCount != 0
            || !LightRoot.gameObject.activeSelf || LightRoot.GetComponents<Component>().Length != 3
            || !Light || !AdditionalLight
            || LightRoot.GetComponents<Light>().Length != 1
            || LightRoot.GetComponents<UniversalAdditionalLightData>().Length != 1
            || NormalZone.specialLight != Light)
            throw new InvalidOperationException("The passive Peak normal SpecialDay light has invalid native ownership.");

        return (Handler, NormalZone, ValidateBridgeStatue(Root, false));
    }
    internal static void PrunePassivePeakOwners(Transform Root, PeakHandler Handler, Transform StatueRoot)
    {
        var TerminalRoots = new[]
        {
            Handler.endCutscene,
            Handler.endCutsceneFinal,
            Handler.endCutsceneFinalSpecialDay,
            Handler.peakSequence
        };
        foreach (var Terminal in TerminalRoots) UnityEngine.Object.DestroyImmediate(Terminal);

        var SpawnerOwners = new List<GameObject>();
        var SpawnerOwnerIds = new HashSet<int>();
        foreach (var Spawner in Root.GetComponentsInChildren<Peak.EndgameFlareSpawner>(true))
            if (!TransformOwnership.Contains(StatueRoot, Spawner.transform)
                && SpawnerOwnerIds.Add(Spawner.gameObject.GetInstanceID())) SpawnerOwners.Add(Spawner.gameObject);
        foreach (var Owner in SpawnerOwners) UnityEngine.Object.DestroyImmediate(Owner);
    }
    internal static void StripPassivePeakComponents(GameObject Root, Transform StatueRoot)
    {
        var Retained = new HashSet<Component>();
        var Filters = new Dictionary<Transform, MeshFilter>();
        var Renderers = new Dictionary<Transform, MeshRenderer>();
        var DuplicateFilters = new HashSet<Transform>();
        var DuplicateRenderers = new HashSet<Transform>();
        foreach (var Filter in Root.GetComponentsInChildren<MeshFilter>(true))
            if (!TransformOwnership.Contains(StatueRoot, Filter.transform)
                && !Filters.TryAdd(Filter.transform, Filter)) DuplicateFilters.Add(Filter.transform);
        foreach (var Renderer in Root.GetComponentsInChildren<MeshRenderer>(true))
            if (!TransformOwnership.Contains(StatueRoot, Renderer.transform)
                && !Renderers.TryAdd(Renderer.transform, Renderer)) DuplicateRenderers.Add(Renderer.transform);
        foreach (var Pair in Filters)
        {
            if (DuplicateFilters.Contains(Pair.Key) || DuplicateRenderers.Contains(Pair.Key)
                || !Renderers.TryGetValue(Pair.Key, out var renderer)
                || !Pair.Value.sharedMesh || !HasPassivePeakMaterials(renderer))
                continue;
            Retained.Add(Pair.Value);
            Retained.Add(renderer);
        }

        foreach (var Collider in Root.GetComponentsInChildren<MeshCollider>(true))
            if (!TransformOwnership.Contains(StatueRoot, Collider.transform)
                && Collider.enabled && !Collider.isTrigger && !Collider.convex
                && !Collider.attachedRigidbody && Collider.sharedMesh)
                Retained.Add(Collider);

        foreach (var Group in Root.GetComponentsInChildren<LODGroup>(true))
        {
            if (TransformOwnership.Contains(StatueRoot, Group.transform)) continue;
            var Valid = true;
            var RendererCount = 0;
            foreach (var Lod in Group.GetLODs())
                foreach (var Renderer in Lod.renderers)
                {
                    RendererCount++;
                    if (!Renderer || !Retained.Contains(Renderer)) Valid = false;
                }
            if (Valid && RendererCount != 0) Retained.Add(Group);
        }

        var Components = Root.GetComponentsInChildren<Component>(true);
        foreach (var Component in Components)
            if (!Component)
                throw new InvalidOperationException("The passive Peak source contains an unresolved component.");
        foreach (var Component in Components)
            if (Component is ParticleSystemCenter
                && !TransformOwnership.Contains(StatueRoot, Component.transform))
                UnityEngine.Object.DestroyImmediate(Component);
        foreach (var Component in Components)
            if (Component && Component is not Transform && !Retained.Contains(Component)
                && !TransformOwnership.Contains(StatueRoot, Component.transform))
                UnityEngine.Object.DestroyImmediate(Component);

        var RetainedTransforms = new HashSet<Transform> { Root.transform };
        foreach (var Component in Retained)
        {
            var Current = Component.transform;
            while (Current && RetainedTransforms.Add(Current) && Current != Root.transform) Current = Current.parent;
        }
        var Transforms = Root.GetComponentsInChildren<Transform>(true);
        foreach (var Value in Transforms)
        {
            if (!Value || TransformOwnership.Contains(StatueRoot, Value) || RetainedTransforms.Contains(Value)
                || !Value.parent || !RetainedTransforms.Contains(Value.parent)) continue;
            UnityEngine.Object.DestroyImmediate(Value.gameObject);
        }
    }
    internal static bool HasPassivePeakMaterials(MeshRenderer Renderer)
    {
        var Materials = Renderer.sharedMaterials;
        if (Materials.Length == 0) return false;
        foreach (var Material in Materials)
            if (!Material) return false;
        return true;
    }
    internal static Peak.ScoutStatue ValidatePassivePeak(GameObject Root)
    {
        var Statue = ValidateBridgeStatue(Root.transform, true);
        var StatueRoot = Statue.transform;
        var Bridge = NativeSegmentPorts.CreatePeakBridge(Root.transform, Root.transform, Statue).Source;
        if (Bridge.GetComponents<Component>().Length != 4)
            throw new InvalidOperationException("The passive Peak Bridge must remain an exact static leaf.");

        foreach (var Component in Root.GetComponentsInChildren<Component>(true))
        {
            if (TransformOwnership.Contains(StatueRoot, Component.transform)) continue;
            switch (Component)
            {
                case Transform:
                case MeshFilter:
                case MeshRenderer:
                case LODGroup:
                    break;
                case MeshCollider collider when collider.enabled && !collider.isTrigger && !collider.convex
                    && !collider.attachedRigidbody && collider.sharedMesh:
                    break;
                default:
                    throw new InvalidOperationException("The passive Peak fixture contains a non-static component.");
            }
        }

        foreach (var Filter in Root.GetComponentsInChildren<MeshFilter>(true))
            if (!TransformOwnership.Contains(StatueRoot, Filter.transform)
                && (Filter.GetComponents<MeshFilter>().Length != 1
                    || Filter.GetComponents<MeshRenderer>().Length != 1
                    || !Filter.sharedMesh || !HasPassivePeakMaterials(Filter.GetComponent<MeshRenderer>())))
                throw new InvalidOperationException("The passive Peak fixture contains an invalid static mesh pair.");
        foreach (var Renderer in Root.GetComponentsInChildren<MeshRenderer>(true))
            if (!TransformOwnership.Contains(StatueRoot, Renderer.transform)
                && (Renderer.GetComponents<MeshRenderer>().Length != 1
                    || Renderer.GetComponents<MeshFilter>().Length != 1))
                throw new InvalidOperationException("The passive Peak fixture contains an invalid static mesh pair.");
        return Statue;
    }
    internal static Peak.ScoutStatue ValidateBridgeStatue(Transform Peak, bool RequireUnassignedView, bool ExpectedEnabled = true)
    {
        var Statues = Peak.GetComponentsInChildren<Peak.ScoutStatue>(true);
        if (Statues.Length != 1 || Statues[0].transform.parent != Peak)
            throw new InvalidOperationException("The Peak Bridge must own exactly one direct Scout Statue branch.");
        var Statue = Statues[0];
        var Owner = Statue.gameObject;
        var Views = Owner.GetComponents<PhotonView>();
        var Observed = Views.Length == 1 ? Views[0].ObservedComponents : null;
        if (Owner.name != "AmuletScoutStatue" || !Owner.activeSelf || Statue.enabled != ExpectedEnabled
            || Owner.GetComponents<Component>().Length != 3
            || Owner.GetComponents<Peak.ScoutStatue>().Length != 1
            || Views.Length != 1 || Views[0].Synchronization != ViewSynchronization.UnreliableOnChange
            || Views[0].OwnershipTransfer != OwnershipOption.Fixed
            || Observed is null || Observed.Count != 1 || Observed[0].GetInstanceID() != Statue.GetInstanceID()
            || RequireUnassignedView && Views[0].ViewID != 0 || Owner.transform.childCount != 3)
            throw new InvalidOperationException("The Peak Bridge Scout Statue has invalid native component ownership.");
        var Children = 0;
        for (var i = 0; i < Owner.transform.childCount; i++)
        {
            var Child = Owner.transform.GetChild(i);
            if (!Child.gameObject.activeSelf)
                throw new InvalidOperationException("The Peak Bridge Scout Statue has an inactive native root branch.");
            Children |= Child.name switch
            {
                "Mesh" => 1,
                "StrangeGemSpot" => 2,
                "Amulets" => 4,
                _ => 0
            };
        }
        if (Children != 7)
            throw new InvalidOperationException("The Peak Bridge Scout Statue has invalid native root branches.");
        return Statue;
    }
    internal static MapSlot PassivePeakBridgeOwner(MapSlot[] Slots)
    {
        MapSlot? Owner = null;
        foreach (var Slot in Slots)
        {
            if (Slot.ProgressionBoundary != NativeProgressionBoundaryKind.PeakBridge) continue;
            if (Owner is not null)
                throw new InvalidOperationException("A passive Peak fixture cannot belong to multiple Peak Bridge slots.");
            Owner = Slot;
        }
        return Owner
            ?? throw new InvalidOperationException("A passive Peak fixture has no owning Peak Bridge slot.");
    }
    internal static void BindBridgeStatue(MapSlot Slot, Peak.ScoutStatue Statue, OriginalMapSources? Originals = null)
    {
        if (!Statue || !Statue.enabled || Slot.BridgeStatue is not null
            || !TransformOwnership.Contains(Slot.Container, Statue.transform)
                && (!Slot.PeakAttachment || !TransformOwnership.Contains(Slot.PeakAttachment, Statue.transform)))
            throw new InvalidOperationException("A Peak Bridge slot received an invalid Scout Statue owner.");
        Originals?.CaptureBridgeStatue(Statue);
        Statue.enabled = false;
        Slot.BindBridgeTarget(Statue);
    }
    internal static GameObject InstantiateInactivePrototype(
        GameObject Prototype,
        Transform Parent,
        List<GameObject> CreatedObjects)
    {
        var WasActive = Prototype.activeSelf;
        Prototype.SetActive(false);
        try
        {
            var Clone = UnityEngine.Object.Instantiate(Prototype, Parent, true);
            CreatedObjects.Add(Clone);
            return Clone;
        }
        finally
        {
            if (Prototype) Prototype.SetActive(WasActive);
        }
    }
    internal static Transform ClonePeakAtAnchor(
        PeakAttachmentSource Source,
        Transform Anchor,
        PlacementFrame PeakFrame,
        Transform CloneParent,
        int Index,
        List<GameObject> CreatedObjects)
    {
        var Prototype = Source.SegmentParent;
        var Clone = InstantiateInactivePrototype(Prototype, CloneParent, CreatedObjects);
        Clone.name = $"PLL_Peak_{Index:D3}";
        Clone.transform.rotation = PeakFrame.Rotate(Prototype.transform.rotation);
        Clone.transform.position = Anchor.position
            - Clone.transform.TransformVector(PlacementPlan.PeakBridgeOwnerBoundaryLocalPosition(Source));
        PhotonCloneIdentity.Reset(Clone);
        return Clone.transform;
    }
    internal static PeakHandler ClonePeakEndCutsceneOwner(
        PeakHandler Source,
        Transform CloneParent,
        List<GameObject> CreatedObjects)
    {
        var Prototype = Source.gameObject;
        var Clone = InstantiateInactivePrototype(Prototype, CloneParent, CreatedObjects);
        Clone.name = "PLL_PeakEndCutscene";
        PhotonCloneIdentity.Reset(Clone);
        return Clone.GetComponent<PeakHandler>()
            ?? throw new InvalidOperationException("The retained Peak end-cutscene owner lost its PeakHandler.");
    }
    internal static void ClearDeferredFeatureOutput(GameObject? Root, HashSet<int>? Cleared = null)
    {
        if (!Root || Cleared is not null && !Cleared.Add(Root.GetInstanceID())) return;
        Map.CurrentRun?.ClimbingFixtures.Retire(Root);
        if (Map.CurrentRun is { } Run)
            foreach (var Slot in Run.Slots)
                if (ReferenceEquals(Slot, Slot.Occurrence.Slots[0])) Slot.Occurrence.RetireActors(Root);
        NativeFeatureGeneration.ClearDeferredOutput(Root.transform);
    }
    internal static Transform CreatePoseAnchor(Transform Root, Pose Pose, string Name)
    {
        var Marker = new GameObject(Name);
        Marker.transform.SetParent(Root, false);
        Marker.transform.localPosition = Pose.position;
        Marker.transform.localRotation = Pose.rotation;
        Marker.transform.localScale = Vector3.one;
        return Marker.transform;
    }
    internal static Pose Rebase(Transform Root, Pose World) => new(
        Root.InverseTransformPoint(World.position),
        Quaternion.Inverse(Root.rotation) * World.rotation);
    internal static void ValidateActivatedCustomClone(MapSlot Slot)
    {
        if (Slot.Released
            || Slot.Source is not CustomLayoutSegmentSource { Registration: { } custom }) return;
        var Expected = Slot.CustomResolved
            ?? throw new InvalidOperationException($"Registered segment '{Slot.Source.Id}' clone contract is unavailable.");
        if (!Slot.Container)
            throw new InvalidOperationException($"Registered segment '{Slot.Source.Id}' clone was destroyed.");
        custom.ValidateResolvedClone(Slot.Container.gameObject, Expected);
        ValidateCustomClone(Slot.Source, Expected, Expected, Slot.Offset);
    }
    internal static void ValidateActivatedCustomClones()
    {
        if (Map.Pending is null && !Map.Active) return;
        var Slots = Map.Pending?.Slots ?? Map.Slots;
        foreach (var Slot in Slots) ValidateActivatedCustomClone(Slot);
    }
    internal static void ValidateCustomClone(
        LayoutSegmentSource Source,
        ResolvedSegment Expected,
        ResolvedSegment Current,
        Vector3 Offset)
    {
        var Custom = (Source as CustomLayoutSegmentSource)?.Registration
            ?? throw new InvalidOperationException($"Segment '{Source.Id}' has no registered clone contract.");
        ValidateCustomTransform(Source, Expected.Root, Current.Root.Value, "root");
        ValidateCustomTransform(Source, Expected.SegmentParentState, Current.SegmentParentState.Value, "segment root");
        ValidateCustomTransform(Source, Expected.SegmentCampfireState, Current.SegmentCampfireState.Value, "campfire root");
        ValidateCustomTransform(Source, Expected.EntryState, Current.EntryState.Value, "Entry port");
        ValidateCustomTransform(Source, Expected.ExitState, Current.ExitState.Value, "Exit port");
        ValidateCustomTransform(Source, Expected.StartState, Current.StartState.Value, "Start port");

        var Root = Current.Root.Value;
        var SegmentParent = Current.SegmentParent.transform;
        var SegmentCampfire = Current.SegmentCampfire.transform;
        if (!TransformOwnership.Contains(Root, SegmentParent) || !TransformOwnership.Contains(Root, SegmentCampfire) || TransformOwnership.Overlaps(SegmentParent, SegmentCampfire)
            || !TransformOwnership.Contains(Root, Current.Entry) || !TransformOwnership.Contains(Root, Current.Exit) || !TransformOwnership.Contains(Root, Current.Start)
            || TransformOwnership.Contains(SegmentParent, Current.Entry) || TransformOwnership.Contains(SegmentCampfire, Current.Entry)
            || TransformOwnership.Contains(SegmentParent, Current.Exit) || TransformOwnership.Contains(SegmentCampfire, Current.Exit)
            || TransformOwnership.Contains(SegmentParent, Current.Start) || TransformOwnership.Contains(SegmentCampfire, Current.Start))
            throw new InvalidOperationException($"Registered segment '{Source.Id}' clone changed its root/port ownership.");

        if (Root.GetComponentsInChildren<SegmentPort>(true).Length != 3)
            throw new InvalidOperationException(
                $"Registered segment '{Source.Id}' clone must retain exactly one Entry, Exit, and Start SegmentPort.");
        ValidateCustomPortContract(
            Source, Expected.EntryPort, Current.EntryPort, Current.Entry,
            SegmentPortKind.Entry, Custom.Entry.Connector, "Entry");
        ValidateCustomPortContract(
            Source, Expected.ExitPort, Current.ExitPort, Current.Exit,
            SegmentPortKind.Exit, Custom.Exit.Connector, "Exit");
        ValidateCustomPortContract(
            Source, Expected.StartPort, Current.StartPort, Current.Start,
            SegmentPortKind.Start, Custom.Start.Connector, "Start");
        ValidateCustomPort(Source, Current.Entry, Source.EntryPorts[0].Pose, Offset, "Entry");
        ValidateCustomPort(Source, Current.Exit, Source.ExitPorts[0].Pose, Offset, "Exit");
        ValidateCustomPort(
            Source,
            Current.Start,
            Source.Start ?? throw new InvalidOperationException($"Registered segment '{Source.Id}' has no Start port."),
            Offset,
            "Start");
    }
    internal static void ValidateCustomTransform(
        LayoutSegmentSource Source,
        TransformState Expected,
        Transform Current,
        string Name)
    {
        if (!Current || !SameObject(Expected.Value, Current) || !SameObject(Expected.Parent, Current.parent)
            || (Current.position - Expected.Pose.position).sqrMagnitude > 0.000001f
            || Math.Abs(Quaternion.Dot(
                Current.rotation.normalized,
                Expected.Pose.rotation.normalized)) < 0.999999f
            || !Current.localScale.Equals(Expected.LocalScale)
            || !Current.lossyScale.Equals(Expected.LossyScale))
            throw new InvalidOperationException($"Registered segment '{Source.Id}' clone changed its {Name} transform.");
    }
    internal static void ValidateCustomPortContract(
        LayoutSegmentSource Source,
        SegmentPort Expected,
        SegmentPort Current,
        Transform Value,
        SegmentPortKind Kind,
        string Connector,
        string Name)
    {
        if (!SameObject(Expected, Current) || !SameObject(Current.transform, Value) || !Current.Registered
            || Current.Kind != Kind || !string.Equals(Current.Connector, Connector, StringComparison.Ordinal))
            throw new InvalidOperationException($"Registered segment '{Source.Id}' clone changed its {Name} SegmentPort contract.");
    }
    internal static void ValidateCustomPort(
        LayoutSegmentSource Source,
        Transform Value,
        Pose Pose,
        Vector3 Offset,
        string Name)
    {
        if (!Value
            || (Value.position - Source.AuthoredPosition(Pose) - Offset).sqrMagnitude > 0.000001f
            || Math.Abs(Quaternion.Dot(
                Value.rotation.normalized,
                Source.AuthoredRotation(Pose).normalized)) < 0.999999f)
            throw new InvalidOperationException($"Registered segment '{Source.Id}' clone changed its {Name} port pose.");
    }
    internal static bool SameObject(UnityEngine.Object? Left, UnityEngine.Object? Right) =>
        Left is null ? Right is null : Right is not null && Left && Right
            && Left.GetInstanceID() == Right.GetInstanceID();
}
