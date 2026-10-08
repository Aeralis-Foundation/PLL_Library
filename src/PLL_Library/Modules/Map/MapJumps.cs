using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal static class MapJumpTransaction
{
    static readonly MethodInfo PreviousScoutStatueGetter = AccessTools.PropertyGetter(
        typeof(MapHandler), nameof(MapHandler.PreviousScoutStatue))
        ?? throw new MissingMethodException(typeof(MapHandler).FullName, "PreviousScoutStatue.get");
    static readonly MethodInfo RespawnChestBreakMethod = AccessTools.DeclaredMethod(
        typeof(RespawnChest), nameof(RespawnChest.Break), Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(RespawnChest).FullName, nameof(RespawnChest.Break));

    sealed class GloomJumpState
    {
        GloomVoiceRuntime.GloomVoiceClone? Outgoing;

        internal GloomJumpState(MapRun? Run, GloomVoiceRuntime.GloomVoiceClone? Outgoing, int OutgoingIndex)
        {
            this.Run = Run;
            this.Outgoing = Outgoing;
            this.OutgoingIndex = OutgoingIndex;
            Outgoing?.BeginExitDeferral();
        }

        internal int OutgoingIndex { get; }
        internal MapRun? Run { get; }

        internal bool TransitionCommitted { get; private set; }

        internal void MarkTransitionCommitted()
        {
            if (TransitionCommitted)
                throw new InvalidOperationException("A map jump transition was committed more than once.");
            TransitionCommitted = true;
        }

        internal GloomVoiceRuntime.GloomVoiceClone? ReleaseOutgoing()
        {
            var Result = Outgoing;
            if (Result is null) return null;
            Outgoing = null;
            Result.EndExitDeferral();
            return Result;
        }
    }

    static bool IsJumpRole(Segment Requested, Segment Role)
    {
        if (!Map.HasPublishedLayout) return Requested == Role;
        if (Requested == Segment.Peak) return Role == Segment.Peak;
        if (!NavigationRoles.TryDecode(Requested, out var Index)) return false;
        return Index >= 0 && Index < Map.Slots.Length
            && LayoutSegmentBehavior.Resolve(Map.Slots[Index].Source)?.Role == Role;
    }

    static int ResolveJumpIndex(Segment Requested)
    {
        var Index = JumpTargetIndex(Requested);
        if (Map.HasPublishedLayout) Map.Owner!.currentSegment = Index;
        return Index;
    }

    internal static int JumpTargetIndex(Segment Requested)
    {
        var Handler = Zorro.Core.Singleton<MapHandler>.Instance;
        if (!Handler) throw new InvalidOperationException("PEAK's map handler is unavailable during a segment jump.");
        int Index;
        if (!Map.HasPublishedLayout)
        {
            Index = Requested == Segment.Peak
                ? (int)Segment.TheKiln
                : Requested == Segment.Void
                    ? Handler.segments.Length - 1
                    : (int)Requested;
        }
        else if (NavigationRoles.TryDecode(Requested, out Index))
        {
            if (Index >= Map.Slots.Length)
                throw new InvalidOperationException($"Map segment navigation index {Index} is outside the published layout.");
        }
        else if (Requested == Segment.Peak)
        {
            Index = PeakLifecycle.PeakSlot();
        }
        else if (Requested == Segment.Void)
        {
            Index = -1;
            for (var i = 0; i < Handler.segments.Length; i++)
                if (NadirRuntime.IsVoidControllerIndex(Handler, i))
                {
                    if (Index >= 0)
                        throw new InvalidOperationException("PEAK's VOID controller segment is ambiguous.");
                    Index = i;
                }
            if (Index < 0)
                throw new InvalidOperationException("PEAK's VOID controller segment is absent from the published handler.");
        }
        else throw new InvalidOperationException($"PEAK's special segment role {Requested} has no navigation adapter.");
        return Index;
    }

    static void BreakPreviousScoutStatue(RespawnChest? Statue)
    {
        if (Statue) Statue.Break();
    }

    internal static bool PrepareJumpProjection(MapRun Run, int Index)
    {
        if (Index >= Run.Slots.Length || Run.LightVolumes.JumpProjectionPrepared(Run.Slots[Index])) return true;
        var Writer = Run.AcquireWriter();
        try { Plugin.StartRoutine(PrepareJumpProjectionRoutine(Run, Writer, Run.Slots[Index])); }
        catch
        {
            Writer.Dispose();
            throw;
        }
        return false;
    }

    static IEnumerator PrepareJumpProjectionRoutine(MapRun Run, MapRun.FeatureWriter Writer, MapSlot TargetSlot)
    {
        var Generation = Run.Features.BeginOperation();
        var Preparation = FeatureGeneration.RunFeatureRoot(Prepare(), Generation, Writer);
        try
        {
            while (Preparation.MoveNext()) yield return Preparation.Current;
        }
        finally
        {
            try { (Preparation as IDisposable)?.Dispose(); }
            finally { Writer.Dispose(); }
        }
        if (Generation.Failure is { } Failure)
        {
            if (ReferenceEquals(Map.CurrentRun, Run) && Run.Active)
                LazySlotGenerator.AbortLazyGeneration(Run, TargetSlot.Index, "Map jump light preparation failed", Failure);
            ExceptionPropagation.Throw(Failure);
        }
        if (ReferenceEquals(Map.CurrentRun, Run) && Run.Active)
            Run.Network.DeferredOperations.ReplayPendingJumps();

        IEnumerator Prepare()
        {
            Writer.RequireCurrent();
            var Restore = LazySlotGenerator.ActivateLazyGeneration(TargetSlot.Index, ActivateGenerated: true);
            try
            {
                var Light = Run.LightVolumes.NeedsRebake(TargetSlot)
                    ? new LightVolumeRebake(Run.LightVolumes, Run, TargetSlot, true, Writer).Execute()
                    : Run.LightVolumes.PrepareFallbackPage(Run, TargetSlot, Writer.RequireCurrent);
                var Routine = FeatureGeneration.RunFeatureRoot(Light, Generation, Writer);
                try
                {
                    while (Routine.MoveNext()) yield return Routine.Current;
                }
                finally { (Routine as IDisposable)?.Dispose(); }
            }
            finally
            {
                try { Restore?.Invoke(); }
                catch (Exception Cleanup)
                {
                    if (Generation.Failure is { } Failure)
                        throw new AggregateException("Map jump light preparation and activation cleanup both failed.", Failure, Cleanup);
                    throw;
                }
            }
        }
    }

    static void PublishJumpCurrentPresentation()
    {
        var Handler = Map.Owner;
        if (!Handler) return;
        if (MapTransitionCoordinator.SelectCurrentLightProjection(Handler!))
            MapTransitionCoordinator.CommitCurrentOccurrence(Handler!);
    }

    static void InsertJumpCurrentPresentation(List<CodeInstruction> Codes)
    {
        var IsMaster = AccessTools.PropertyGetter(typeof(PhotonNetwork), nameof(PhotonNetwork.IsMasterClient));
        var SegmentParent = MapTransitionAdapter.Instance.SegmentParentGetter;
        var Publish = ((Action)PublishJumpCurrentPresentation).Method;
        var Found = -1;
        for (var i = 0; i + 4 < Codes.Count; i++)
        {
            if (!Codes[i].Calls(IsMaster)
                || Codes[i + 1].opcode != OpCodes.Brfalse && Codes[i + 1].opcode != OpCodes.Brfalse_S
                || !Codes[i + 3].Calls(SegmentParent)
                || Codes[i + 4].operand is not MethodInfo Method
                || Method.Name != nameof(GameObject.GetComponentsInChildren)
                || !Method.IsGenericMethod || Method.GetGenericArguments()[0] != typeof(ISpawner)) continue;
            if (Found >= 0)
                throw new AmbiguousMatchException("MapHandler.JumpToSegmentLogic's current presentation boundary was ambiguous.");
            Found = i;
        }
        if (Found < 0)
            throw new MissingMethodException("MapHandler.JumpToSegmentLogic's current presentation boundary was not found.");
        var Injection = new CodeInstruction(OpCodes.Call, Publish);
        Injection.labels.AddRange(Codes[Found].labels);
        Codes[Found].labels.Clear();
        Codes.Insert(Found, Injection);
    }

    static bool ReplaceOrdinarySegmentSetActive(List<CodeInstruction> Codes)
    {
        var Adapter = MapTransitionAdapter.Instance;
        var Replacement = ((Action<GameObject, bool>)PlacementExecution.SetActiveAndValidate).Method;
        var Activated = false;
        var Deactivated = false;
        for (var i = 2; i < Codes.Count; i++)
        {
            if (!Codes[i].Calls(Adapter.GameObjectSetActive)
                || !MapTransitionAdapter.LoadsBoolean(Codes[i - 1], out var Active)) continue;
            var Direct = Codes[i - 2].Calls(Adapter.SegmentParentGetter)
                || Codes[i - 2].Calls(Adapter.SegmentCampfireGetter);
            var Wall = i >= 3 && Codes[i - 2].Calls(Adapter.GameObjectGetter)
                && (Codes[i - 3].LoadsField(Adapter.WallNextField)
                    || Codes[i - 3].LoadsField(Adapter.WallPreviousField));
            if (!Direct && !Wall) continue;
            Codes[i].opcode = OpCodes.Call;
            Codes[i].operand = Replacement;
            Activated |= Active;
            Deactivated |= !Active;
        }
        return Activated && Deactivated;
    }

    static bool ReplaceJumpPeakRespawn(List<CodeInstruction> Codes)
    {
        var Adapter = MapTransitionAdapter.Instance;
        var Replacement = ((Func<Vector3>)PeakLifecycle.PeakRespawnPosition).Method;
        var Found = -1;
        for (var i = 1; i + 1 < Codes.Count; i++)
        {
            if (!Codes[i - 1].Calls(Adapter.MapHandlerSingletonGetter)
                || !Codes[i].LoadsField(Adapter.RespawnThePeakField)
                || !Codes[i + 1].Calls(Adapter.TransformPositionGetter)) continue;
            if (Found >= 0)
                throw new AmbiguousMatchException("MapHandler.JumpToSegmentLogic's contextual Peak respawn was ambiguous.");
            Found = i - 1;
        }
        if (Found < 0) return false;
        Codes[Found].opcode = OpCodes.Call;
        Codes[Found].operand = Replacement;
        Codes[Found].labels.AddRange(Codes[Found + 1].labels);
        Codes[Found].labels.AddRange(Codes[Found + 2].labels);
        Codes[Found].blocks.AddRange(Codes[Found + 1].blocks);
        Codes[Found].blocks.AddRange(Codes[Found + 2].blocks);
        Codes.RemoveRange(Found + 1, 2);
        return true;
    }

    [HarmonyPatch(typeof(MapHandler), "JumpToSegmentLogic")]
    static class WaterJumpPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Segment __0, HashSet<int> __1, bool __2, bool __3, out GloomJumpState? __state)
        {
            __state = null;
            var Run = Map.CurrentRun;
            if (Run?.Active != true || Run.Transitions.ReplayingJump) return Execute(ref __state);
            var Index = JumpTargetIndex(__0);
            if (Index < Run.Slots.Length && Run.Slots[Index].Released) return false;
            Run.Transitions.PendingJumps.TryGetValue(__0, out var Jump);
            Jump?.Merge(__1, __2, __3);
            if (Index < Run.Slots.Length && Run.IsDeferredSlot(Index))
            {
                LazySlotGenerator.GenerateLazySlot(Index);
                if (!Run.Active) return false;
            }
            if (Jump is not null) return false;
            if (MapTransitionCoordinator.JumpMutationOpen(Run)
                && (Index >= Run.Slots.Length || Run.IsOccurrenceReady(Run.Slots[Index].Occurrence)
                    && PrepareJumpProjection(Run, Index)))
                return Execute(ref __state);
            Run.Transitions.PendingJumps.Add(__0, new MapTransitionCoordinator.QueuedJump(__0, __1, __2, __3));
            return false;

            static bool Execute(ref GloomJumpState? State)
            {
                State = new GloomJumpState(Map.CurrentRun, GloomVoiceRuntime.CaptureCurrentGloomVoiceClone(), Map.Index);
                AuthoringRuntime.BeginTransition();
                return true;
            }
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            InsertJumpCurrentPresentation(Codes);
            var VoidPatched = NadirRuntime.ReplaceVoidSuppression(Codes);
            var ActivationPatched = ReplaceOrdinarySegmentSetActive(Codes);
            var PeakSpawnersPatched = SpawnTrackerTransaction.ReplaceJumpPeakSpawnerAppend(Codes);
            var PeakRespawnPatched = ReplaceJumpPeakRespawn(Codes);
            var NadirGuardPatched = NadirRuntime.ReplaceNadirJumpGuard(Codes);
            var BreakPrevious = ((Action<RespawnChest?>)BreakPreviousScoutStatue).Method;
            var Resolve = ((Func<Segment, int>)ResolveJumpIndex).Method;
            var Role = ((Func<Segment, Segment, bool>)IsJumpRole).Method;
            var JumpProgress = MapTransitionAdapter.Instance.JumpToProgressSegment;
            var ExpandProgress = ((Func<int, int>)ProgressProjection.ExpandedProgressIndex).Method;
            var IndexPatched = false;
            var ProgressPatched = false;
            var PeakPatched = false;
            var PreviousScoutStatuePatched = false;

            for (var i = 1; i < Codes.Count; i++)
            {
                if (!Codes[i - 1].Calls(PreviousScoutStatueGetter)
                    || !Codes[i].Calls(RespawnChestBreakMethod)) continue;
                if (PreviousScoutStatuePatched)
                    throw new AmbiguousMatchException(
                        "MapHandler.JumpToSegmentLogic's previous scout statue break was ambiguous.");
                Codes[i].opcode = OpCodes.Call;
                Codes[i].operand = BreakPrevious;
                PreviousScoutStatuePatched = true;
            }

            for (var i = 0; i + 6 < Codes.Count; i++)
            {
                if (Codes[i].opcode != OpCodes.Ldarg_0
                    || !MapTransitionAdapter.LoadsInt(Codes[i + 1], (int)Segment.Peak)
                    || !MapTransitionAdapter.LoadsLocalOne(Codes[i + 3].opcode)
                    || Codes[i + 4].opcode != OpCodes.Ldc_I4_1
                    || Codes[i + 5].opcode != OpCodes.Sub
                    || !MapTransitionAdapter.StoresLocalOne(Codes[i + 6].opcode)) continue;
                if (IndexPatched)
                    throw new AmbiguousMatchException("MapHandler.JumpToSegmentLogic's logical-index adapter was ambiguous.");
                Codes[i + 1].opcode = OpCodes.Call;
                Codes[i + 1].operand = Resolve;
                Codes[i + 2].opcode = OpCodes.Stloc_1;
                Codes[i + 2].operand = null;
                Codes.RemoveRange(i + 3, 4);
                IndexPatched = true;
                break;
            }

            for (var i = 2; i < Codes.Count; i++)
            {
                if (!Codes[i].Calls(JumpProgress) || !MapTransitionAdapter.LoadsLocalOne(Codes[i - 2].opcode)
                    || Codes[i - 1].opcode != OpCodes.Ldc_R4) continue;
                if (ProgressPatched)
                    throw new AmbiguousMatchException("MapHandler.JumpToSegmentLogic's progress index was ambiguous.");
                Codes.Insert(i - 1, new CodeInstruction(OpCodes.Call, ExpandProgress));
                ProgressPatched = true;
                i++;
            }

            for (var i = 0; i + 2 < Codes.Count; i++)
            {
                if (Codes[i].opcode != OpCodes.Ldarg_0) continue;
                var Branch = Codes[i + 2];
                var Peak = !PeakPatched && MapTransitionAdapter.LoadsInt(Codes[i + 1], (int)Segment.Peak)
                    && (MapTransitionAdapter.IsEqualBranch(Branch.opcode)
                        || MapTransitionAdapter.IsNotEqualBranch(Branch.opcode));
                if (!Peak) continue;
                Codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, Role));
                if (MapTransitionAdapter.IsNotEqualBranch(Branch.opcode))
                    Branch.opcode = Branch.opcode == OpCodes.Bne_Un_S ? OpCodes.Brfalse_S : OpCodes.Brfalse;
                else Branch.opcode = Branch.opcode == OpCodes.Beq_S ? OpCodes.Brtrue_S : OpCodes.Brtrue;
                PeakPatched = true;
                i += 2;
            }

            if (!PreviousScoutStatuePatched)
                throw new MissingMethodException(
                    "MapHandler.JumpToSegmentLogic's previous scout statue break was not found.");
            if (!VoidPatched || !NadirGuardPatched
                || !ActivationPatched || !PeakSpawnersPatched || !PeakRespawnPatched
                || !IndexPatched || !ProgressPatched || !PeakPatched)
                throw new MissingMethodException("MapHandler.JumpToSegmentLogic activation sites or role guards were not found.");
            return Codes;
        }

        [HarmonyPostfix]
        static void Postfix(GloomJumpState? __state)
        {
            if (__state is null) return;
            NadirRuntime.RetainJumpedNadir(__state.OutgoingIndex);
            MapEnvironmentProjection.SyncWater();
            AuthoringRuntime.CommitTransition();
            __state.MarkTransitionCommitted();
            var Outgoing = __state.ReleaseOutgoing();
            var Index = Map.Index;
            if (Outgoing is not null && (Index < Outgoing.FirstSlot || Index > Outgoing.LastSlot))
                GloomVoiceRuntime.DismissGloomVoiceClone(Outgoing);
            Map.CurrentRun?.Quicksave.AdvanceRestoredHostCampfireRetention(Index);
            SpawnTrackerTransaction.ActivateBaseCheckpoint(Index);
            ReleasedSlotRetirement.ReleaseOldZones();
            if (__state.Run is { } Run && ReferenceEquals(Map.CurrentRun, Run))
                NativeContentTools.ObserveReadyCurrent(Run);
        }

        [HarmonyFinalizer]
        static Exception? Finalizer(Exception? __exception, GloomJumpState? __state)
        {
            if (__exception is null || __state is null) return __exception;
            var Failure = __exception;
            try
            {
                __state.ReleaseOutgoing();
                if (!__state.TransitionCommitted && ReferenceEquals(Map.CurrentRun, __state.Run))
                    AuthoringRuntime.AbortTransition();
            }
            catch (Exception Cleanup)
            {
                Failure = new AggregateException(
                    "Map segment transition and visual rollback both failed.", __exception, Cleanup);
            }
            if (__state.Run is { Active: true } Run && ReferenceEquals(Map.CurrentRun, Run))
                try { LazySlotGenerator.AbortLazyGeneration(Run, Map.Index, "Map jump failed", Failure); }
                catch (Exception Cleanup)
                {
                    Failure = new AggregateException("Map jump failure and run cleanup both failed.", Failure, Cleanup);
                }
            return Failure;
        }
    }
}
