using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace PLL.Library;

internal sealed class SpawnTrackerTransaction
{
    internal static void AttachLazySpawnTrackers(int Index)
    {
        var Handler = Map.Owner;
        if (!Handler) return;
        var Trackers = Handler._spawnTrackers;
        Trackers?.RemoveAll(Tracker => !Tracker);

        var spawners = new List<Component>();
        var seen = new HashSet<int>();
        var Slot = Map.Slots[Index];
        var Occurrence = Slot.Occurrence;
        AddSpawners(Slot.Segment.segmentParent);
        AddSpawners(Slot.Segment.segmentCampfire);
        var Peak = Slot.PeakAttachment;
        AddSpawners(Peak ? Peak.gameObject : null);
        if (Slot.CustomResolved is null && Slot.NativeRole == Segment.Caldera)
        {
            var Statue = ScoutStatue(Slot);
            AddSpawners(Statue ? Statue.gameObject : null);
        }
        if (Slot.Index == Occurrence.BatchIndex)
            foreach (var Supplemental in Occurrence.SupplementalRoots)
                AddSpawners(Supplemental.gameObject);

        if (spawners.Count != 0) PrepareSpawnTrackers(Handler, spawners);

        void AddSpawners(GameObject? root)
        {
            if (!root) return;
            foreach (var spawner in root.GetComponentsInChildren<ISpawner>(true))
            {
                var component = (Component)spawner;
                if (component && Map.CurrentRun!.HookedGeneration.OwnedByOuter(root.transform, component.transform)
                    && seen.Add(component.gameObject.GetInstanceID())) spawners.Add(component);
            }
        }
    }

    internal static Hash128 PrepareGeneratedSpawnTracker(GameObject Spawner)
    {
        if (!Spawner) throw new ArgumentNullException(nameof(Spawner));
        return PrepareSpawnTrackers(
            GeneratedTrackerOwner(), new Component[] { Spawner.transform }, true)!.SpawnerId;
    }

    internal static void PrepareGeneratedSpawnTrackers(List<Component> Spawners)
    {
        if (Spawners.Count == 0) return;
        PrepareSpawnTrackers(GeneratedTrackerOwner(), Spawners, true);
    }

    static MapHandler GeneratedTrackerOwner()
    {
        var Handler = Map.Owner;
        if (!Handler || !Map.HasPublishedLayout)
            throw new InvalidOperationException("Generated item spawners require a published map run.");
        return Handler;
    }

    internal static void RemoveSpawnTracker(Peak.SpawnedItemTracker tracker)
    {
        if (!tracker || !Map.Owner) return;
        var Trackers = Map.Owner!._spawnTrackers;
        if (Trackers is null) return;
        var TrackerId = tracker.GetInstanceID();
        Trackers.RemoveAll(Candidate => Candidate && Candidate.GetInstanceID() == TrackerId);
    }

    internal static void RemoveSpawnTrackers(Transform root)
    {
        if (!root || !Map.Owner) return;
        Map.Owner!._spawnTrackers?.RemoveAll(Tracker =>
            !Tracker || TransformOwnership.Contains(root, Tracker.transform));
    }

    static Peak.SpawnedItemTracker? PrepareSpawnTrackers(
        MapHandler Handler, IReadOnlyList<Component> Spawners,
        bool Generated = false, Func<Peak.SpawnedItemTracker, Hash128>? ScopedId = null)
    {
        var SpawnerIds = new HashSet<int>();
        foreach (var Spawner in Spawners)
        {
            if (Generated && (!Spawner
                || Spawner.gameObject.scene != Handler.gameObject.scene
                || !TransformOwnership.Contains(Handler.transform, Spawner.transform)))
                throw new InvalidOperationException("A generated item spawner is outside the published map run.");
            SpawnerIds.Add(Spawner.gameObject.GetInstanceID());
        }
        var Hierarchy = Handler.GetComponentsInChildren<Peak.SpawnedItemTracker>(true);
        var Buckets = new Dictionary<Hash128, List<Peak.SpawnedItemTracker>>(Hierarchy.Length);
        foreach (var Tracker in Hierarchy)
            if (Tracker && !SpawnerIds.Contains(Tracker.gameObject.GetInstanceID()))
                AddSpawnTrackerBucket(Buckets, Tracker);

        var Trackers = Handler._spawnTrackers;
        HashSet<int>? TrackedIds = null;
        if (Trackers is not null)
        {
            TrackedIds = new HashSet<int>(Trackers.Count);
            foreach (var Tracker in Trackers)
                if (Tracker) TrackedIds.Add(Tracker.GetInstanceID());
        }

        var SavedHistories = SavedSpawnHistories(Handler);
        Peak.SpawnedItemTracker? LastTracker = null;
        foreach (var Spawner in Spawners)
        {
            var Matches = Spawner.GetComponents<Peak.SpawnedItemTracker>();
            if (Generated && Matches.Length > 1)
                throw new InvalidOperationException($"Generated item spawner '{Spawner.name}' has duplicate SpawnedItemTracker components.");
            var Target = Matches.Length == 0
                ? Spawner.gameObject.AddComponent<Peak.SpawnedItemTracker>()
                : Matches[0];
            for (var i = 1; i < Matches.Length; i++)
            {
                var Duplicate = Matches[i];
                if (Trackers is not null)
                {
                    var DuplicateId = Duplicate.GetInstanceID();
                    Trackers.RemoveAll(Candidate => Candidate && Candidate.GetInstanceID() == DuplicateId);
                }
                TrackedIds?.Remove(Duplicate.GetInstanceID());
                UnityEngine.Object.DestroyImmediate(Duplicate);
            }

            Target.Init();
            if (ScopedId is not null) Target.SpawnerId = ScopedId(Target);
            if (!Generated) RequireSpawnTrackerOwner(Handler, Target);
            if (Buckets.TryGetValue(Target.SpawnerId, out var Bucket))
                foreach (var Candidate in Bucket)
                    if (Candidate && !NativeFixtures.SameObject(Candidate.gameObject, Target.gameObject))
                        throw SpawnTrackerCollision(Handler, Target, Candidate);
            AddSpawnTrackerBucket(Buckets, Target);

            if (Trackers is not null && TrackedIds!.Add(Target.GetInstanceID()))
                Trackers.Add(Target);
            ApplySavedSpawnHistory(Target, SavedHistories);
            LastTracker = Target;
        }
        return LastTracker;
    }

    internal static byte[] AttachHookedSpawnTrackers(HookedPropTarget Target, int RequestIndex, int Revision, out string[] Ids)
    {
        var Spawners = new List<Component>();
        foreach (var Spawner in Target.Root.GetComponentsInChildren<ISpawner>(true)) Spawners.Add((Component)Spawner);
        if (Spawners.Count != 0)
            PrepareSpawnTrackers(GeneratedTrackerOwner(), Spawners, true, Tracker =>
            {
                var NativeId = Tracker.SpawnerId;
                var Id = Hash128.Compute("PLL/HookedPropGenerationSpawnedItemTracker/v1");
                Id.Append(RequestIndex); Id.Append(Target.Contract.StableTargetKey); Id.Append(Revision); Id.Append(ref NativeId);
                return Id;
            });
        var Trackers = new List<(string Path, string Id)>();
        foreach (var Tracker in Target.Root.GetComponentsInChildren<Peak.SpawnedItemTracker>(true))
            Trackers.Add((DeterministicObjectIdentity.CanonicalRelativeComponentPath(0,
                DeterministicObjectIdentity.RelativeTypedComponentStructuralPath(Tracker, Target.Root)), Tracker.SpawnerId.ToString()));
        Trackers.Sort((Left, Right) => StringComparer.Ordinal.Compare(Left.Path, Right.Path));
        Ids = new string[Trackers.Count];
        for (var i = 0; i < Ids.Length; i++) Ids[i] = Trackers[i].Id;
        Array.Sort(Ids, StringComparer.Ordinal);
        for (var i = 1; i < Ids.Length; i++)
            if (Ids[i] == Ids[i - 1]) throw new InvalidOperationException("Hooked generated trackers have duplicate identities.");
        using var Stream = new System.IO.MemoryStream();
        using (var Writer = new System.IO.BinaryWriter(Stream, new System.Text.UTF8Encoding(false, true), true))
        {
            Writer.Write("PLL/HookedPropGenerationSpawnedItemTrackerSet/v1"); Writer.Write(Trackers.Count);
            foreach (var Tracker in Trackers) { Writer.Write(Tracker.Path); Writer.Write(Tracker.Id); }
        }
        using var Sha = System.Security.Cryptography.SHA256.Create();
        return Sha.ComputeHash(Stream.GetBuffer(), 0, checked((int)Stream.Length));
    }

    static void RequireSpawnTrackerOwner(MapHandler handler, Peak.SpawnedItemTracker tracker)
    {
        if (!TransformOwnership.Contains(handler.transform, tracker.transform))
            throw new InvalidOperationException("A generated SpawnedItemTracker is outside its feature root.");
    }

    static void AddSpawnTrackerBucket(
        Dictionary<Hash128, List<Peak.SpawnedItemTracker>> buckets,
        Peak.SpawnedItemTracker tracker)
    {
        var id = tracker.SpawnerId;
        if (!buckets.TryGetValue(id, out var bucket))
        {
            bucket = new List<Peak.SpawnedItemTracker>();
            buckets.Add(id, bucket);
        }
        bucket.Add(tracker);
    }

    static InvalidOperationException SpawnTrackerCollision(
        MapHandler handler,
        Peak.SpawnedItemTracker tracker,
        Peak.SpawnedItemTracker candidate) =>
        new($"Generated SpawnedItemTracker "
            + $"'{DeterministicObjectIdentity.RelativeComponentPath(tracker, handler.transform, 0)}' collides with "
            + $"'{DeterministicObjectIdentity.RelativeComponentPath(candidate, handler.transform, 0)}' "
            + $"at path-and-world-matrix ID {tracker.SpawnerId}.");

    static Dictionary<Hash128, List<Peak.SpawnedItemTracker.SpawnRecord>>?
        SavedSpawnHistories(MapHandler Handler)
    {
        var Ingress = Map.Ingress;
        return Ingress.SpawnHistoryOwner
            && Ingress.SpawnHistoryOwner.GetInstanceID() == Handler.GetInstanceID()
            ? Ingress.SavedSpawnHistories : null;
    }

    static void ApplySavedSpawnHistory(
        Peak.SpawnedItemTracker Tracker,
        Dictionary<Hash128, List<Peak.SpawnedItemTracker.SpawnRecord>>? SavedHistories)
    {
        if (!Tracker.HasSpawnHistory && SavedHistories is not null
            && SavedHistories.TryGetValue(Tracker.SpawnerId, out var History))
            Tracker.PopulateHistory(History);
    }

    internal static void ApplySavedSpawnHistories()
    {
        var handler = Singleton<MapHandler>.Instance;
        if (!handler) return;
        var Trackers = handler._spawnTrackers;
        if (Trackers is null) return;
        var Prepared = Map.Pending;
        var scene = handler.gameObject.scene;
        var spawners = new HashSet<int>();
        var retained = new HashSet<int>();
        for (var i = Trackers.Count - 1; i >= 0; i--)
        {
            var Tracker = Trackers[i];
            if (Tracker && Tracker.gameObject.scene == scene && spawners.Add(Tracker.gameObject.GetInstanceID()))
            {
                retained.Add(Tracker.GetInstanceID());
                continue;
            }
            Trackers.RemoveAt(i);
            if (!Tracker || retained.Contains(Tracker.GetInstanceID()) || !OwnsSpawnTracker(handler, Tracker)) continue;
            if (Prepared is { Committed: true, OriginalTrackerIds: { } originalIds }
                && originalIds.Contains(Tracker.GetInstanceID()))
            {
                var TrackerId = Tracker.GetInstanceID();
                if (!Prepared.DeferredTrackerDestruction.Exists(
                        Candidate => Candidate && Candidate.GetInstanceID() == TrackerId))
                    Prepared.DeferredTrackerDestruction.Add(Tracker);
                continue;
            }
            UnityEngine.Object.DestroyImmediate(Tracker);
        }
        var SavedHistories = SavedSpawnHistories(handler);
        foreach (var Tracker in Trackers) ApplySavedSpawnHistory(Tracker, SavedHistories);
    }

    static bool OwnsSpawnTracker(MapHandler handler, Peak.SpawnedItemTracker tracker) =>
        tracker && tracker.gameObject.scene == handler.gameObject.scene && TransformOwnership.Contains(handler.transform, tracker.transform);
    internal static void CommitSpawnTrackers(PreparedMap Prepared)
    {
        var Failures = new List<Exception>();
        try
        {
            if (!Prepared.Handler) return;
            var Handler = Prepared.Handler;
            var retained = new HashSet<int>();
            var Trackers = Handler._spawnTrackers;
            if (Trackers is not null)
                foreach (var Tracker in Trackers)
                    if (Tracker) retained.Add(Tracker.GetInstanceID());
            foreach (var Tracker in Prepared.DeferredTrackerDestruction)
            {
                if (!Tracker || retained.Contains(Tracker.GetInstanceID()) || !OwnsSpawnTracker(Handler, Tracker)) continue;
                try
                {
                    UnityEngine.Object.DestroyImmediate(Tracker);
                }
                catch (Exception exception)
                {
                    Failures.Add(exception);
                }
            }
        }
        catch (Exception Failure)
        {
            Failures.Add(Failure);
        }
        finally
        {
            Prepared.DeferredTrackerDestruction.Clear();
        }
        if (Failures.Count != 0)
            throw new AggregateException("Committed SpawnedItemTracker cleanup failed.", Failures);
    }
    internal static void RestoreSpawnTrackers(PreparedMap Prepared)
    {
        if (!Prepared.Handler || Prepared.OriginalTrackerIds is null) return;
        var Handler = Prepared.Handler;
        Exception? failure = null;
        foreach (var Tracker in Handler.GetComponentsInChildren<Peak.SpawnedItemTracker>(true))
            if (!Prepared.OriginalTrackerIds.Contains(Tracker.GetInstanceID()))
                try
                {
                    UnityEngine.Object.DestroyImmediate(Tracker);
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
        try
        {
            Handler._spawnTrackers = Prepared.OriginalSpawnTrackers is null
                ? null!
                : new List<Peak.SpawnedItemTracker>(Prepared.OriginalSpawnTrackers);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
        Prepared.DeferredTrackerDestruction.Clear();
        if (failure is not null) ExceptionPropagation.Throw(failure);
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.PopulateSpawnHistories))]
    static class SpawnHistoryPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Dictionary<Hash128, List<Peak.SpawnedItemTracker.SpawnRecord>> spawnRecords)
        {
            var handler = Singleton<MapHandler>.Instance;
            if (!handler) return true;
            Map.Ingress.SpawnHistoryOwner = handler;
            Map.Ingress.SavedSpawnHistories = new Dictionary<Hash128, List<Peak.SpawnedItemTracker.SpawnRecord>>(spawnRecords.Count);
            foreach (var record in spawnRecords)
                Map.Ingress.SavedSpawnHistories.Add(record.Key, new List<Peak.SpawnedItemTracker.SpawnRecord>(record.Value));
            return handler._spawnTrackers is not null;
        }
    }

    [HarmonyPatch(typeof(Peak.SpawnedItemTracker), nameof(Peak.SpawnedItemTracker.TrackSpawnedItems))]
    static class HookedSpawnHistoryMaterializationPatch
    {
        [HarmonyPostfix]
        static void Postfix(Peak.SpawnedItemTracker __instance) =>
            Map.CurrentRun?.HookedGeneration.TrackerMaterialized(__instance);
    }

    static void AddTargetPeakSpawners(List<ISpawner> Spawners, Segment Requested)
    {
        if (!Map.HasPublishedLayout)
        {
            if (Requested == Segment.TheKiln)
                Spawners.AddRange(Zorro.Core.Singleton<PeakHandler>.Instance.gameObject.GetComponentsInChildren<ISpawner>());
            return;
        }
        if (Requested == Segment.Peak || Requested == Segment.Void) return;
        if (!NavigationRoles.TryTargetSlot(Requested, out var Index))
            throw new InvalidOperationException($"Map segment target {Requested} has no published layout slot.");
        var Peak = Map.Slots[Index].PeakAttachment;
        if (Peak && !TransformOwnership.Contains(Map.Slots[Index].Segment.segmentParent.transform, Peak))
            Spawners.AddRange(Peak.GetComponentsInChildren<ISpawner>());
    }

    static ISpawner[] InitialSegmentSpawners(GameObject SegmentParent)
    {
        var Spawners = SegmentParent.GetComponentsInChildren<ISpawner>();
        if (!Map.HasPublishedLayout || Map.Slots.Length == 0) return Spawners;
        var Peak = Map.Slots[0].PeakAttachment;
        if (!Peak || TransformOwnership.Contains(SegmentParent.transform, Peak)) return Spawners;
        var PeakSpawners = Peak.GetComponentsInChildren<ISpawner>();
        if (PeakSpawners.Length == 0) return Spawners;
        var Result = new ISpawner[Spawners.Length + PeakSpawners.Length];
        Array.Copy(Spawners, Result, Spawners.Length);
        Array.Copy(PeakSpawners, 0, Result, Spawners.Length, PeakSpawners.Length);
        return Result;
    }

    static ISpawner[] InitialGlobalSpawners(Transform GlobalParent)
    {
        if (!Map.HasPublishedLayout || Map.Slots.Length == 0)
            return GlobalParent.GetComponentsInChildren<ISpawner>();

        var PrunedRoots = new HashSet<int>();
        foreach (var Slot in Map.Slots)
        {
            if (Slot.Container && Slot.Container != GlobalParent)
                PrunedRoots.Add(Slot.Container.GetInstanceID());
            var Peak = Map.CurrentRun!.Network.ViewBatches.ExternalPeakRoot(Slot);
            if (Peak && Peak != GlobalParent) PrunedRoots.Add(Peak.GetInstanceID());
        }

        var Spawners = new List<ISpawner>();
        var Components = new List<ISpawner>();
        AddComponents(GlobalParent);
        if (GlobalParent.gameObject.activeInHierarchy)
            for (var i = 0; i < GlobalParent.childCount; i++) AddBranch(GlobalParent.GetChild(i));
        return Spawners.ToArray();

        void AddBranch(Transform Root)
        {
            if (!Root.gameObject.activeSelf || PrunedRoots.Contains(Root.GetInstanceID())) return;
            AddComponents(Root);
            for (var i = 0; i < Root.childCount; i++) AddBranch(Root.GetChild(i));
        }

        void AddComponents(Transform Root)
        {
            Components.Clear();
            Root.GetComponents(Components);
            Spawners.AddRange(Components);
        }
    }

    static bool IsSpawnerComponentsCall(CodeInstruction Instruction) =>
        Instruction.operand is MethodInfo Method
        && Method.DeclaringType == typeof(GameObject)
        && Method.Name == nameof(GameObject.GetComponentsInChildren)
        && Method.IsGenericMethod
        && Method.GetGenericArguments() is { Length: 1 } Arguments
        && Arguments[0] == typeof(ISpawner);

    static bool IsComponentSpawnerComponentsCall(CodeInstruction Instruction) =>
        Instruction.operand is MethodInfo Method
        && Method.DeclaringType == typeof(Component)
        && Method.Name == nameof(Component.GetComponentsInChildren)
        && Method.IsGenericMethod
        && Method.GetGenericArguments() is { Length: 1 } Arguments
        && Arguments[0] == typeof(ISpawner);

    internal static bool ReplaceTargetPeakSpawnerAppend(List<CodeInstruction> Codes)
    {
        var Adapter = MapTransitionAdapter.Instance;
        var Replacement = ((Action<List<ISpawner>, Segment>)AddTargetPeakSpawners).Method;
        var Found = -1;
        for (var i = 8; i < Codes.Count; i++)
        {
            if (!Codes[i - 7].LoadsField(Adapter.TransitionRequest)
                || !MapTransitionAdapter.LoadsInt(Codes[i - 6], (int)Segment.TheKiln)
                || !MapTransitionAdapter.IsNotEqualBranch(Codes[i - 5].opcode)
                || !Codes[i - 3].Calls(Adapter.PeakHandlerSingletonGetter)
                || !Codes[i - 2].Calls(Adapter.ComponentGameObjectGetter)
                || !IsSpawnerComponentsCall(Codes[i - 1])
                || !Codes[i].Calls(Adapter.SpawnerAddRange)) continue;
            if (Found >= 0)
                throw new AmbiguousMatchException("MapHandler.GoToSegment's contextual Peak spawner append was ambiguous.");
            Found = i - 8;
        }
        if (Found < 0) return false;
        var RequestOwner = Codes[Found];
        var Request = Codes[Found + 1];
        var List = Codes[Found + 4];
        List.labels.AddRange(RequestOwner.labels);
        RequestOwner.labels.Clear();
        List.blocks.AddRange(RequestOwner.blocks);
        RequestOwner.blocks.Clear();
        Codes.RemoveRange(Found, 9);
        Codes.InsertRange(Found, new[]
        {
            List,
            RequestOwner,
            Request,
            new CodeInstruction(System.Reflection.Emit.OpCodes.Call, Replacement)
        });
        return true;
    }

    internal static bool ReplaceJumpPeakSpawnerAppend(List<CodeInstruction> Codes)
    {
        var Adapter = MapTransitionAdapter.Instance;
        var Replacement = ((Action<List<ISpawner>, Segment>)AddTargetPeakSpawners).Method;
        var Found = -1;
        for (var i = 7; i < Codes.Count; i++)
        {
            if (Codes[i - 7].opcode != System.Reflection.Emit.OpCodes.Ldarg_0
                || !MapTransitionAdapter.LoadsInt(Codes[i - 6], (int)Segment.TheKiln)
                || !MapTransitionAdapter.IsNotEqualBranch(Codes[i - 5].opcode)
                || !Codes[i - 3].Calls(Adapter.PeakHandlerSingletonGetter)
                || !Codes[i - 2].Calls(Adapter.ComponentGameObjectGetter)
                || !IsSpawnerComponentsCall(Codes[i - 1])
                || !Codes[i].Calls(Adapter.SpawnerAddRange)) continue;
            if (Found >= 0)
                throw new AmbiguousMatchException("MapHandler.JumpToSegmentLogic's contextual Peak spawner append was ambiguous.");
            Found = i - 7;
        }
        if (Found < 0) return false;
        var Request = Codes[Found];
        var List = Codes[Found + 3];
        List.labels.AddRange(Request.labels);
        Request.labels.Clear();
        List.blocks.AddRange(Request.blocks);
        Request.blocks.Clear();
        Codes.RemoveRange(Found, 8);
        Codes.InsertRange(Found, new[]
        {
            List,
            Request,
            new CodeInstruction(System.Reflection.Emit.OpCodes.Call, Replacement)
        });
        return true;
    }

    [HarmonyPatch(typeof(MapHandler), "Update")]
    static class InitialPeakSpawnerPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var SegmentReplacement = ((Func<GameObject, ISpawner[]>)InitialSegmentSpawners).Method;
            var GlobalReplacement = ((Func<Transform, ISpawner[]>)InitialGlobalSpawners).Method;
            var GlobalParent = AccessTools.DeclaredField(typeof(MapHandler), nameof(MapHandler.globalParent))
                ?? throw new MissingFieldException(typeof(MapHandler).FullName, nameof(MapHandler.globalParent));
            var SegmentFound = false;
            var GlobalFound = false;
            for (var i = 0; i < Codes.Count; i++)
            {
                if (IsSpawnerComponentsCall(Codes[i]))
                {
                    if (SegmentFound)
                        throw new AmbiguousMatchException("MapHandler.Update's initial segment spawner scan was ambiguous.");
                    Codes[i].opcode = System.Reflection.Emit.OpCodes.Call;
                    Codes[i].operand = SegmentReplacement;
                    SegmentFound = true;
                    continue;
                }
                if (i == 0 || !Codes[i - 1].LoadsField(GlobalParent)
                    || !IsComponentSpawnerComponentsCall(Codes[i])) continue;
                if (GlobalFound)
                    throw new AmbiguousMatchException("MapHandler.Update's global spawner scan was ambiguous.");
                Codes[i].opcode = System.Reflection.Emit.OpCodes.Call;
                Codes[i].operand = GlobalReplacement;
                GlobalFound = true;
            }
            if (!SegmentFound || !GlobalFound)
                throw new MissingMethodException("MapHandler.Update's initial segment or global spawner scan was not found.");
            return Codes;
        }
    }

    static RespawnChest? ScoutStatue(MapSlot Slot)
    {
        if (Slot.CustomResolved is { } Custom)
            return Custom.CampfireRespawnChest ?? Custom.SegmentRespawnChest;
        if (Slot.NativeRole == Segment.Caldera && !Slot.Container) return null;
        var Root = Slot.NativeRole == Segment.Caldera
            ? Slot.Container.gameObject
            : Slot.Segment.segmentCampfire;
        return Root ? Root.GetComponentInChildren<RespawnChest>(true) : null;
    }

    internal static global::Campfire? SlotCampfire(int Index)
    {
        if (Index < 0 || Index >= Map.Slots.Length) return null;
        var Root = Map.Slots[Index].Segment.segmentCampfire;
        return Root ? Root.GetComponentInChildren<global::Campfire>(true) : null;
    }

    internal static int PreviousCampfireSlot(Segment Reached)
    {
        if (!Map.HasPublishedLayout) return (int)Reached - 1;
        int Index;
        if (!NavigationRoles.TryDecode(Reached, out Index) || Index >= Map.Slots.Length)
        {
            if (Reached != Segment.Void) return -1;
            Index = Map.Slots.Length;
        }
        for (Index--; Index >= 0; Index--)
            if (Map.Slots[Index].Segment.segmentCampfire) return Index;
        return -1;
    }

    internal static bool TryResolveBaseCheckpoint(
        int TargetSlot,
        IReadOnlyList<byte>? RestoredPeakStates,
        out int CampfireSlot,
        out GameObject CheckpointRoot,
        out global::Campfire? Campfire)
    {
        var Run = Map.CurrentRun;
        if (Run?.HasPublishedLayout == true)
        {
            var PeakFound = RestoredPeakStates is null
                ? Run.Peak.Campfire.TryGetLitCheckpointForTarget(
                    TargetSlot, out CampfireSlot, out var PeakRoot, out var PeakCampfire)
                : Run.Peak.Campfire.TryGetSavedLitCheckpointForTarget(
                    TargetSlot, RestoredPeakStates, out CampfireSlot, out PeakRoot, out PeakCampfire);
            if (PeakFound)
            {
                CheckpointRoot = PeakRoot.gameObject;
                Campfire = PeakCampfire;
                return true;
            }
        }
        for (CampfireSlot = Math.Min(TargetSlot - 1, Map.Slots.Length - 1);
            CampfireSlot >= 0;
            CampfireSlot--)
        {
            if (Map.Slots[CampfireSlot].Released) continue;
            CheckpointRoot = Map.Slots[CampfireSlot].Segment.segmentCampfire;
            if (!CheckpointRoot) continue;
            Campfire = CheckpointRoot.GetComponentInChildren<global::Campfire>(true);
            return true;
        }
        CampfireSlot = -1;
        CheckpointRoot = null!;
        Campfire = null;
        return false;
    }

    static bool PeakCheckpointProjectionPending(MapRun Run, int TargetSlot) =>
        Run.Peak.Campfire.TryGetCheckpointSlotForTarget(TargetSlot, out var CampfireSlot)
        && !Run.Peak.CompletionReady(CampfireSlot);

    internal static int? BaseCheckpointSlot(int TargetSlot)
    {
        var Run = Map.CurrentRun;
        return Run?.Active == true
            && !PeakCheckpointProjectionPending(Run, TargetSlot)
            && TryResolveBaseCheckpoint(
                TargetSlot, null,
                out var CampfireSlot, out _, out _)
            ? CampfireSlot
            : null;
    }

    internal static global::Campfire? ResolveBaseCheckpoint(int TargetSlot)
    {
        var Run = Map.CurrentRun;
        if (Run?.Active != true
            || PeakCheckpointProjectionPending(Run, TargetSlot)
            || !TryResolveBaseCheckpoint(
                TargetSlot, null,
                out _, out var CheckpointRoot, out var Campfire)) return null;
        return Campfire
            ? Campfire
            : CheckpointRoot.GetComponentInChildren<global::Campfire>(true);
    }

    internal static global::Campfire? ActivateBaseCheckpoint(int TargetSlot)
    {
        var Run = Map.CurrentRun;
        if (Run?.Active != true
            || PeakCheckpointProjectionPending(Run, TargetSlot)
            || !TryResolveBaseCheckpoint(
                TargetSlot, null,
                out var CampfireSlot, out var CheckpointRoot, out var Campfire)) return null;
        var Slot = Run.Slots[CampfireSlot];
        var OrdinaryRoot = Slot.Segment.segmentCampfire;
        if (Run.Peak.CampfireLit(CampfireSlot) && Run.Peak.Campfire.Owns(CampfireSlot)
            && OrdinaryRoot && OrdinaryRoot != CheckpointRoot && OrdinaryRoot.activeSelf)
            PlacementExecution.SetActiveAndValidate(OrdinaryRoot, false);
        var Container = Slot.Occurrence.Container;
        if (Container && TransformOwnership.Contains(Container, CheckpointRoot.transform)
            && !Container.gameObject.activeSelf)
            PlacementExecution.SetActiveAndValidate(Container.gameObject, true);
        var PeakAttachment = Slot.PeakAttachment;
        if (PeakAttachment && TransformOwnership.Contains(PeakAttachment, CheckpointRoot.transform)
            && !PeakAttachment.gameObject.activeSelf)
            PlacementExecution.SetActiveAndValidate(PeakAttachment.gameObject, true);
        if (!CheckpointRoot.activeSelf)
            PlacementExecution.SetActiveAndValidate(CheckpointRoot, true);
        Physics.SyncTransforms();
        Run.Peak.Campfire.ReconcileActivatedCheckpoint(CampfireSlot);
        return Campfire
            ? Campfire
            : CheckpointRoot.GetComponentInChildren<global::Campfire>(true);
    }

    internal static int BaseCampSlotIndex()
    {
        var Index = Math.Min(Map.Index, Map.Slots.Length - 1);
        if (Index <= 0) return Index;
        if (ProgressProjection.PreviousSegmentIsStillBaseCamp())
        {
            for (var Previous = Index - 1; Previous >= 0; Previous--)
                if (SlotCampfire(Previous)) return Previous;
        }
        else if (!Map.Slots[Index].Segment.segmentCampfire
            && BaseCheckpointSlot(Index) is { } CheckpointSlot)
            return CheckpointSlot;
        return Index;
    }

    internal static Segment BaseCampSegment(RespawnChest? NativeStatue)
    {
        if (!Map.HasPublishedLayout) return NativeStatue!.SegmentNumber;
        return Map.Slots[BaseCampSlotIndex()].NavigationRole;
    }

    internal static int SpawnReadinessIndex(Segment Role)
    {
        if (!Map.HasPublishedLayout) return (int)Role;
        if (NavigationRoles.TryDecode(Role, out var Index) && Index < Map.Slots.Length) return Index;
        if (Role == Segment.Void)
        {
            if (NadirRuntime.TryGetVoidControllerSlot(out Index)) return Index;
            var Handler = Map.Owner;
            if (Handler && NadirRuntime.CurrentSegmentIsVoidController(Handler) && Map.Slots.Length != 0)
                return Map.Slots.Length - 1;
        }
        throw new InvalidOperationException($"Spawn segment {Role} has no published layout slot.");
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.LastRevivedSegment), MethodType.Getter)]
    static class LastRevivedSegmentPatch
    {
        [HarmonyPostfix]
        static void Postfix(MapHandler __instance, ref int __result)
        {
            var Handler = Map.Owner;
            if (!Map.HasPublishedLayout || !Peak.VoidBiome.VoidBiomeActive || !Handler
                || Handler.GetInstanceID() != __instance.GetInstanceID()) return;
            var Index = __instance.currentSegment;
            if (Index >= 0 && Index < Map.Slots.Length
                && Map.Slots[Index].Source is NadirLayoutSegmentSource)
                __result = (int)Map.Slots[Index].NavigationRole;
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.BaseCampHasRevived), MethodType.Getter)]
    static class BaseCampRevivedPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (!Map.HasPublishedLayout || !Map.Owner) return true;
            var Revived = SpawnReadinessIndex((Segment)Map.Owner.LastRevivedSegment);
            __result = Revived == BaseCampSlotIndex();
            return false;
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.CurrentScoutStatue), MethodType.Getter)]
    static class CurrentScoutStatuePatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref RespawnChest __result)
        {
            if (!Map.HasPublishedLayout || Map.Index < 0 || Map.Index >= Map.Slots.Length) return true;
            __result = Map.Slots[Map.Index].NativeRole == Segment.TheKiln ? null! : ScoutStatue(Map.Slots[Map.Index])!;
            return false;
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.PreviousScoutStatue), MethodType.Getter)]
    static class PreviousScoutStatuePatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref RespawnChest __result)
        {
            if (!Map.HasPublishedLayout || Map.Index <= 0) return true;
            var Index = Math.Min(Map.Index - 1, Map.Slots.Length - 1);
            __result = ScoutStatue(Map.Slots[Index])!;
            return false;
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.CurrentBaseCampSpawnPoint), MethodType.Getter)]
    static class BaseCampSpawnPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref Transform __result)
        {
            if (!Map.HasPublishedLayout) return true;
            var Handler = Map.Owner;
            if (Handler && NadirRuntime.CurrentSegmentIsNadir(Handler)) return true;
            if (Map.Index == 0)
            {
                if (!Map.CurrentRun!.Configuration.Environment.RelocateStart || !SpawnPoint.LocalSpawnPoint) return true;
                __result = SpawnPoint.LocalSpawnPoint.transform;
                return false;
            }
            var Index = Math.Min(Map.Index, Map.Slots.Length - 1);
            if (Index > 0 && Map.Slots[Index].PeakAttachment && Map.Slots[Index - 1].Released && Map.Slots[Index].PeakRespawn)
            {
                __result = Map.Slots[Index].PeakRespawn!;
                return false;
            }
            var BaseCamp = BaseCampSlotIndex();
            var Campfire = BaseCamp == Index
                ? SlotCampfire(Index)
                : ResolveBaseCheckpoint(Index) ?? SlotCampfire(BaseCamp);
            __result = Campfire ? Campfire.transform : Map.Slots[Index].Segment.reconnectSpawnPos;
            return false;
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.PreviousCampfire), MethodType.Getter)]
    static class PreviousCampfirePatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref global::Campfire __result)
        {
            var Handler = Map.Owner;
            if (!Map.HasPublishedLayout || !Handler) return true;
            if (Map.CurrentRun!.Quicksave.RetainedRestoredHostCampfire is { } Restored)
            {
                __result = Restored;
                return false;
            }
            if (NadirRuntime.CurrentSegmentIsNadir(Handler)) return true;
            __result = ResolveBaseCheckpoint(Map.Index)!;
            return false;
        }
    }

    [HarmonyPatch]
    static class MiniRunJumpPatch
    {
        static MethodBase TargetMethod()
        {
            var routine = AccessTools.DeclaredMethod(typeof(RunManager), "JumpToMiniRunBiomeWhenReady")
                ?? throw new MissingMethodException("RunManager.JumpToMiniRunBiomeWhenReady was not found.");
            return AccessTools.EnumeratorMoveNext(routine)
                ?? throw new MissingMethodException("RunManager.JumpToMiniRunBiomeWhenReady iterator was not found.");
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.DeclaredMethod(typeof(RunSettings), nameof(RunSettings.GetValue),
                new[] { typeof(RunSettings.SETTINGTYPE), typeof(bool) });
            var Replacement = ((Func<RunSettings.SETTINGTYPE, bool, int>)NavigationRoles.MiniRunStart).Method;
            var replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.operand = Replacement;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new MissingMethodException("The mini-run start setting was not found.");
        }
    }

    [HarmonyPatch(typeof(CharacterSpawner), nameof(CharacterSpawner.RPC_MiniRunPlayerSpawn))]
    static class MiniRunSpawnPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref Segment __0)
        {
            __0 = NavigationRoles.LogicalRole(__0);
            return LazySlotGenerator.SlotReady(SpawnReadinessIndex(__0));
        }
    }

    [HarmonyPatch(typeof(CharacterSpawner), "SpawnHostCharacter")]
    static class HostSpawnReadinessPatch
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            var Run = Map.CurrentRun;
            if (Run is null
                || !Run.Quicksave.TryGetRetainedRestoredHostSlots(
                    out var CampfireIndex, out var TargetIndex))
                return LazySlotGenerator.SlotReady(LazySlotGenerator.HostSpawnIndex());
            if (!Run.Active || !Run.Peak.CompletionReady(CampfireIndex)) return false;

            if (!LazySlotGenerator.SlotReady(CampfireIndex))
            {
                if (!Run.Active) return false;
                if (Run.Slots[CampfireIndex].Released)
                    Run.Quicksave.AbortRestoredHostCampfire(
                        $"requires released prior campfire slot {CampfireIndex}.");
                return false;
            }
            if (!Run.Quicksave.ResolveRestoredHostCampfire())
            {
                Run.Quicksave.AbortRestoredHostCampfire(
                    $"requires a missing prior campfire at slot {CampfireIndex}.");
                return false;
            }
            return LazySlotGenerator.SlotReady(TargetIndex);
        }

        [HarmonyPostfix]
        static void Postfix()
        {
            var LocalCharacter = Character.localCharacter;
            if (!LocalCharacter
                || !PlayerHandler.TryGetCharacter(
                    Peak.Network.NetCode.Session.SeatNumber, out var RegisteredCharacter)
                || !ReferenceEquals(LocalCharacter, RegisteredCharacter)) return;
            Map.CurrentRun?.Quicksave.MarkRestoredHostCampfireConsumed();
        }
    }

    [HarmonyPatch]
    static class RemoteSpawnReadinessPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(CharacterSpawner), nameof(CharacterSpawner.RPC_ReconnectingPlayerSpawn))
                ?? throw new MissingMethodException("CharacterSpawner.RPC_ReconnectingPlayerSpawn was not found.");
            yield return AccessTools.DeclaredMethod(typeof(CharacterSpawner), nameof(CharacterSpawner.RPC_NewPlayerSpawn))
                ?? throw new MissingMethodException("CharacterSpawner.RPC_NewPlayerSpawn was not found.");
        }

        [HarmonyPrefix]
        static bool Prefix(
            Segment __0,
            [HarmonyArgument("info")] ref Photon.Pun.PhotonMessageInfo Info,
            MethodBase __originalMethod)
        {
            var Sender = Info.Sender;
            if (__originalMethod.Name == nameof(CharacterSpawner.RPC_ReconnectingPlayerSpawn)
                ? Sender.ActorNumber != Peak.Network.NetCode.Session.HostId
                : !Sender.IsMasterClient) return true;
            var Index = SpawnReadinessIndex(__0);
            var Run = Map.CurrentRun;
            if (Run?.Active == true && PeakCheckpointProjectionPending(Run, Index)) return false;
            if (!LazySlotGenerator.SlotReady(Index)) return false;
            if (Index == 0 || ActivateBaseCheckpoint(Index)) return true;
            LazySlotGenerator.AbortLazyGeneration(
                Index, "Remote spawn base checkpoint is unavailable");
            return false;
        }
    }
}
