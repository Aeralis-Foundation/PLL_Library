using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class MapTransitionAdapter
{
    internal static readonly MapTransitionAdapter Instance = new();

    MapTransitionAdapter()
    {
        GetCurrentSegment = AccessTools.DeclaredMethod(typeof(MapHandler), nameof(MapHandler.GetCurrentSegment))
            ?? throw new MissingMethodException(typeof(MapHandler).FullName, nameof(MapHandler.GetCurrentSegment));
        MapHandlerSingletonGetter = AccessTools.PropertyGetter(typeof(Zorro.Core.Singleton<MapHandler>), "Instance")
            ?? throw new MissingMethodException(typeof(Zorro.Core.Singleton<MapHandler>).FullName, "Instance.get");
        PeakHandlerSingletonGetter = AccessTools.PropertyGetter(typeof(Zorro.Core.Singleton<PeakHandler>), "Instance")
            ?? throw new MissingMethodException(typeof(Zorro.Core.Singleton<PeakHandler>).FullName, "Instance.get");
        SpawnerAddRange = AccessTools.DeclaredMethod(
            typeof(List<ISpawner>), nameof(List<ISpawner>.AddRange), new[] { typeof(IEnumerable<ISpawner>) })
            ?? throw new MissingMethodException(typeof(List<ISpawner>).FullName, nameof(List<ISpawner>.AddRange));
        SegmentsField = AccessTools.DeclaredField(typeof(MapHandler), nameof(MapHandler.segments))
            ?? throw new MissingFieldException(typeof(MapHandler).FullName, nameof(MapHandler.segments));
        RespawnThePeakField = AccessTools.DeclaredField(typeof(MapHandler), nameof(MapHandler.respawnThePeak))
            ?? throw new MissingFieldException(typeof(MapHandler).FullName, nameof(MapHandler.respawnThePeak));
        SegmentParentGetter = AccessTools.PropertyGetter(
            typeof(MapHandler.MapSegment), nameof(MapHandler.MapSegment.segmentParent))
            ?? throw new MissingMethodException(typeof(MapHandler.MapSegment).FullName, "segmentParent.get");
        SegmentCampfireGetter = AccessTools.PropertyGetter(
            typeof(MapHandler.MapSegment), nameof(MapHandler.MapSegment.segmentCampfire))
            ?? throw new MissingMethodException(typeof(MapHandler.MapSegment).FullName, "segmentCampfire.get");
        WallNextField = AccessTools.DeclaredField(typeof(MapHandler.MapSegment), nameof(MapHandler.MapSegment.wallNext))
            ?? throw new MissingFieldException(typeof(MapHandler.MapSegment).FullName, nameof(MapHandler.MapSegment.wallNext));
        WallPreviousField = AccessTools.DeclaredField(typeof(MapHandler.MapSegment), nameof(MapHandler.MapSegment.wallPrevious))
            ?? throw new MissingFieldException(typeof(MapHandler.MapSegment).FullName, nameof(MapHandler.MapSegment.wallPrevious));
        GameObjectTransformGetter = AccessTools.PropertyGetter(typeof(GameObject), nameof(GameObject.transform))
            ?? throw new MissingMethodException(typeof(GameObject).FullName, "transform.get");
        TransformParentGetter = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.parent))
            ?? throw new MissingMethodException(typeof(Transform).FullName, "parent.get");
        TransformPositionGetter = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position))
            ?? throw new MissingMethodException(typeof(Transform).FullName, "position.get");
        ComponentGameObjectGetter = AccessTools.PropertyGetter(typeof(Component), nameof(Component.gameObject))
            ?? throw new MissingMethodException(typeof(Component).FullName, "gameObject.get");
        GameObjectGetter = AccessTools.PropertyGetter(typeof(GameObject), nameof(GameObject.gameObject))
            ?? throw new MissingMethodException(typeof(GameObject).FullName, "gameObject.get");
        GameObjectSetActive = AccessTools.DeclaredMethod(
            typeof(GameObject), nameof(GameObject.SetActive), new[] { typeof(bool) })
            ?? throw new MissingMethodException(typeof(GameObject).FullName, nameof(GameObject.SetActive));
        NativeActivateCurrentSegmentMethod = AccessTools.DeclaredMethod(
            typeof(MapHandler), "ActivateCurrentSegment", Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(MapHandler).FullName, "ActivateCurrentSegment");
        SetSegmentComplete = AccessTools.DeclaredMethod(
            typeof(MountainProgressHandler), nameof(MountainProgressHandler.SetSegmentComplete),
            new[] { typeof(int) })
            ?? throw new MissingMethodException(typeof(MountainProgressHandler).FullName, nameof(MountainProgressHandler.SetSegmentComplete));
        JumpToProgressSegment = AccessTools.DeclaredMethod(
            typeof(MountainProgressHandler), nameof(MountainProgressHandler.JumpToSegment),
            new[] { typeof(int), typeof(float) })
            ?? throw new MissingMethodException(typeof(MountainProgressHandler).FullName, nameof(MountainProgressHandler.JumpToSegment));
        TransitionMoveNext = BindIterator(
            AccessTools.DeclaredMethod(typeof(MapHandler), nameof(MapHandler.GoToSegment), [typeof(Segment)])
                ?? throw new MissingMethodException(typeof(MapHandler).FullName, nameof(MapHandler.GoToSegment)),
            typeof(MapHandler), out var Factory);
        TransitionFactory = Factory;
        TransitionTitleMoveNext = BindIterator(
            TransitionMoveNext, typeof(MapHandler), out _);
        TransitionRequest = AccessTools.DeclaredField(Factory.DeclaringType!, "s")
            ?? throw new MissingFieldException(Factory.DeclaringType!.FullName, "s");
        if (TransitionRequest.FieldType != typeof(Segment))
            throw new MissingFieldException(
                $"{TransitionRequest.DeclaringType?.FullName}.s must capture {typeof(Segment).FullName}.");
    }

    internal MethodInfo GetCurrentSegment { get; }
    internal MethodInfo MapHandlerSingletonGetter { get; }
    internal MethodInfo PeakHandlerSingletonGetter { get; }
    internal MethodInfo SpawnerAddRange { get; }
    internal FieldInfo SegmentsField { get; }
    internal FieldInfo RespawnThePeakField { get; }
    internal MethodInfo SegmentParentGetter { get; }
    internal MethodInfo SegmentCampfireGetter { get; }
    internal FieldInfo WallNextField { get; }
    internal FieldInfo WallPreviousField { get; }
    internal MethodInfo GameObjectTransformGetter { get; }
    internal MethodInfo TransformParentGetter { get; }
    internal MethodInfo TransformPositionGetter { get; }
    internal MethodInfo ComponentGameObjectGetter { get; }
    internal MethodInfo GameObjectGetter { get; }
    internal MethodInfo GameObjectSetActive { get; }
    internal MethodInfo NativeActivateCurrentSegmentMethod { get; }
    internal MethodInfo TransitionFactory { get; }
    internal MethodInfo TransitionMoveNext { get; }
    internal MethodInfo TransitionTitleMoveNext { get; }
    internal MethodInfo SetSegmentComplete { get; }
    internal MethodInfo JumpToProgressSegment { get; }
    internal FieldInfo TransitionRequest { get; }

    internal static MethodInfo BindIterator(
        MethodInfo Caller, Type Owner,
        out MethodInfo Factory)
    {
        MethodInfo? Selected = null;
        foreach (var Instruction in PatchProcessor.GetOriginalInstructions(Caller))
        {
            if (Instruction.opcode != OpCodes.Call && Instruction.opcode != OpCodes.Callvirt
                || Instruction.operand is not MethodInfo Candidate
                || Candidate.ReturnType != typeof(IEnumerator)
                || Candidate.DeclaringType?.DeclaringType != Owner) continue;
            if (Selected is not null)
                throw new AmbiguousMatchException(
                    $"{Owner.FullName}.{Caller.Name} calls multiple nested iterator factories.");
            Selected = Candidate;
        }
        Factory = Selected ?? throw new MissingMethodException(
            $"{Owner.FullName}.{Caller.Name} has no nested iterator factory.");
        return AccessTools.EnumeratorMoveNext(Factory)
            ?? throw new MissingMethodException(
                $"{Factory.DeclaringType?.FullName}.{Factory.Name} iterator was not found.");
    }

    internal static bool LoadsBoolean(CodeInstruction Instruction, out bool Value)
    {
        if (Instruction.opcode == OpCodes.Ldc_I4_0)
        {
            Value = false;
            return true;
        }
        if (Instruction.opcode == OpCodes.Ldc_I4_1)
        {
            Value = true;
            return true;
        }
        Value = false;
        return false;
    }

    internal static bool LoadsInt(CodeInstruction Instruction, int Value) =>
        Value == -1 && Instruction.opcode == OpCodes.Ldc_I4_M1
        || Value == 0 && Instruction.opcode == OpCodes.Ldc_I4_0
        || Value == 1 && Instruction.opcode == OpCodes.Ldc_I4_1
        || Value == 2 && Instruction.opcode == OpCodes.Ldc_I4_2
        || Value == 3 && Instruction.opcode == OpCodes.Ldc_I4_3
        || Value == 4 && Instruction.opcode == OpCodes.Ldc_I4_4
        || Value == 5 && Instruction.opcode == OpCodes.Ldc_I4_5
        || Value == 6 && Instruction.opcode == OpCodes.Ldc_I4_6
        || Value == 7 && Instruction.opcode == OpCodes.Ldc_I4_7
        || Value == 8 && Instruction.opcode == OpCodes.Ldc_I4_8
        || Instruction.opcode == OpCodes.Ldc_I4 && Instruction.operand is int Integer && Integer == Value
        || Instruction.opcode == OpCodes.Ldc_I4_S && Instruction.operand is sbyte Small && Small == Value;

    internal static bool IsNotEqualBranch(OpCode Opcode) => Opcode == OpCodes.Bne_Un || Opcode == OpCodes.Bne_Un_S;
    internal static bool IsEqualBranch(OpCode Opcode) => Opcode == OpCodes.Beq || Opcode == OpCodes.Beq_S;
    internal static bool LoadsLocalOne(OpCode Opcode) => Opcode == OpCodes.Ldloc_1;
    internal static bool StoresLocalOne(OpCode Opcode) => Opcode == OpCodes.Stloc_1;

    internal static int BranchTargetIndex(IReadOnlyList<CodeInstruction> Codes, object? Operand)
    {
        if (Operand is CodeInstruction Instruction)
            for (var i = 0; i < Codes.Count; i++)
                if (ReferenceEquals(Codes[i], Instruction)) return i;
        if (Operand is Label Label)
            for (var i = 0; i < Codes.Count; i++)
                if (Codes[i].labels.Contains(Label)) return i;
        return -1;
    }
}

internal sealed class MapTransitionCoordinator
{
    internal enum QueuedTransitionKind { Campfire, DirectAdvance }

    internal readonly struct TransitionLease
    {
        internal enum State { Idle, Reserved, Active, ReplayScheduled }

        TransitionLease(State Current, int DestinationIndex)
        {
            if ((Current is State.Reserved or State.Active) && DestinationIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(DestinationIndex));
            this.Current = Current;
            this.DestinationIndex = DestinationIndex;
        }

        internal State Current { get; }
        internal int DestinationIndex { get; }
        internal static TransitionLease Idle => default;
        internal static TransitionLease Reserved(int DestinationIndex) => new(State.Reserved, DestinationIndex);
        internal static TransitionLease Active(int DestinationIndex) => new(State.Active, DestinationIndex);
        internal static TransitionLease ReplayScheduled => new(State.ReplayScheduled, -1);
    }

    internal readonly struct QueuedTransition
    {
        internal QueuedTransition(QueuedTransitionKind Kind, int CompletedIndex)
        {
            this.Kind = Kind;
            this.CompletedIndex = CompletedIndex;
        }

        internal QueuedTransitionKind Kind { get; }
        internal int CompletedIndex { get; }
    }

    internal sealed class QueuedJump
    {
        internal QueuedJump(Segment Segment, HashSet<int> Players, bool SendToEveryone, bool UpdateFog)
        {
            this.Segment = Segment;
            this.Players = new HashSet<int>(Players);
            this.SendToEveryone = SendToEveryone;
            this.UpdateFog = UpdateFog;
        }

        internal Segment Segment { get; }
        internal HashSet<int> Players { get; }
        internal bool SendToEveryone { get; private set; }
        internal bool UpdateFog { get; private set; }

        internal void Merge(HashSet<int> Players, bool SendToEveryone, bool UpdateFog)
        {
            this.Players.UnionWith(Players);
            this.SendToEveryone |= SendToEveryone;
            this.UpdateFog |= UpdateFog;
        }
    }

    internal SortedDictionary<Segment, QueuedJump> PendingJumps { get; } = new();
    internal SortedDictionary<int, QueuedTransition> QueuedTransitions { get; } = new();
    internal TransitionLease Lease { get; set; }
    internal bool RunFinished { get; set; }
    internal bool RunFinishRequested { get; set; }
    internal bool ReplayingJump { get; set; }
    internal int RequestedDirectAdvance { get; set; } = -1;
    int ActivatedPresentationIndex = -1;

    internal void ResetState()
    {
        PendingJumps.Clear();
        QueuedTransitions.Clear();
        Lease = TransitionLease.Idle;
        RunFinished = false;
        RunFinishRequested = false;
        ReplayingJump = false;
        RequestedDirectAdvance = -1;
        ActivatedPresentationIndex = -1;
    }

    internal static bool JumpMutationOpen(MapRun Run) =>
        Run.Active && !Run.FeatureMutationActive
        && !Run.HookedGeneration.TransitionBlocked
        && Run.Transitions.Lease.Current == TransitionLease.State.Idle;

    internal static bool CanTransition(Segment Target, int Current) =>
        !Map.Active
            ? (int)Target > Current
            : Current >= 0 && Current + 1 < Map.Slots.Length
                && Target == Map.Slots[Current + 1].NavigationRole;

    static IEnumerator WrapTransitionGeneration(IEnumerator Original) =>
        Map.Active ? LazySlotGenerator.WaitForTransitionGeneration(Original) : Original;

    internal static int RunTerminalIndex() => Map.Slots.Length - 1;

    static void ActivateTransitionSegment(MapHandler Handler)
    {
        var Run = Map.CurrentRun;
        var PublishedOwner = Run is { Active: true } && Run.Owns(Handler);
        if (PublishedOwner)
        {
            var Index = Handler.currentSegment;
            if (Index >= 0 && Index < Run!.Slots.Length)
            {
                var Slot = Run.Slots[Index];
                if (!Slot.Released && Slot.Source is NadirLayoutSegmentSource)
                {
                    if (!Slot.Container)
                        throw new InvalidOperationException("The transitioning Nadir slot lost its container.");
                    Slot.Container.gameObject.SetActive(true);
                }
            }
        }
        Handler.ActivateCurrentSegment();
        if (!PublishedOwner) return;
        Campfire.Bind(Handler.currentSegment);
        if (SelectCurrentLightProjection(Handler)) CommitCurrentOccurrence(Handler);
    }

    internal static void CommitCurrentOccurrence(MapHandler Handler)
    {
        var Run = Map.CurrentRun!;
        var Index = Handler.currentSegment;
        NadirRuntime.ReconcileVoidController(Run, Index);
        Sounds.Commit(Run, Index);
        GloomVoiceRuntime.PublishCurrentGloomVoiceClone();
        Run.Peak.Campfire.ReconcileCurrentOutputs();
    }

    internal static bool SelectCurrentLightProjection(MapHandler Handler)
    {
        if (Map.CurrentRun is not { Active: true } Run || !Run.Owns(Handler)) return false;
        var Index = Handler.currentSegment;
        if (Index < 0 || Index >= Run.Slots.Length || Run.Slots[Index].Released) return false;
        LazySlotGenerator.RequestCurrentLightRebake(Run, Index);
        if (Run.SelectedActiveSlot is not { } Slot || !Run.IsOccurrenceReady(Slot.Occurrence)
            || !Run.LightVolumes.ProjectionPrepared(Slot)) return false;
        Run.LightVolumes.SelectProjection(Run, Index);
        if (Run.Transitions.ActivatedPresentationIndex == Index) return false;
        NadirRuntime.PublishInSpace(Run, Index);
        FogCutoutProjection.SelectProjection(Run.Slots[Index]);
        SpecialDayProjection.Reconcile();
        Run.Transitions.ActivatedPresentationIndex = Index;
        return true;
    }
    static void RetainNearbyTrackedViews(List<PhotonView> Views)
    {
        var Characters = PlayerHandler.GetAllPlayerCharacters();
        for (var i = Views.Count - 1; i >= 0; i--)
        {
            var View = Views[i];
            if (!View)
            {
                Views.RemoveAt(i);
                continue;
            }
            var Retain = false;
            foreach (var Character in Characters)
            {
                if (!Character || !(Vector3.Distance(View.transform.position, Character.Center) < 60f)) continue;
                Retain = true;
                break;
            }
            if (!Retain) Views.RemoveAt(i);
        }
    }

    internal static void SetOutgoingTransitionActive(GameObject Value, bool IsActive)
    {
        if (IsActive)
            throw new InvalidOperationException("MapHandler.GoToSegment's outgoing segment deactivation became an activation.");
        if (NadirRuntime.TryDeactivateOutgoingTransition(Value)) return;
        PlacementExecution.SetActiveAndValidate(Value, false);
    }

    static bool ReplaceTransitionNativeRoleGuards(List<CodeInstruction> Codes)
    {
        var Adapter = MapTransitionAdapter.Instance;
        var SetFogOrigin = AccessTools.DeclaredMethod(
            typeof(OrbFogHandler), nameof(OrbFogHandler.SetFogOrigin), new[] { typeof(int) })
            ?? throw new MissingMethodException(typeof(OrbFogHandler).FullName, nameof(OrbFogHandler.SetFogOrigin));
        var Replacement = ((Func<Segment, Segment, bool>)NavigationRoles.TargetHasNativeRole).Method;
        var WallPatched = false;
        var FogPatched = false;
        for (var i = 0; i + 3 < Codes.Count; i++)
        {
            if (!Codes[i + 1].LoadsField(Adapter.TransitionRequest)
                || !MapTransitionAdapter.LoadsInt(Codes[i + 2], (int)Segment.TheKiln)
                || !MapTransitionAdapter.IsEqualBranch(Codes[i + 3].opcode)
                    && !MapTransitionAdapter.IsNotEqualBranch(Codes[i + 3].opcode)) continue;
            var End = MapTransitionAdapter.BranchTargetIndex(Codes, Codes[i + 3].operand);
            if (End <= i + 3) continue;
            var Wall = false;
            var Fog = false;
            for (var j = i + 4; j < End; j++)
            {
                Wall |= Codes[j].LoadsField(Adapter.WallPreviousField);
                Fog |= Codes[j].Calls(SetFogOrigin);
            }
            if (Wall)
            {
                var ActivatesWall = false;
                for (var j = i + 4; j < End; j++) ActivatesWall |= Codes[j].Calls(Adapter.GameObjectSetActive);
                Wall &= ActivatesWall;
            }
            if (!Wall && !Fog) continue;
            if (Wall && WallPatched || Fog && FogPatched || Wall && Fog)
                throw new AmbiguousMatchException("MapHandler.GoToSegment's native terminal-role guards were ambiguous.");
            var Branch = Codes[i + 3];
            Codes.Insert(i + 3, new CodeInstruction(OpCodes.Call, Replacement));
            if (MapTransitionAdapter.IsNotEqualBranch(Branch.opcode))
                Branch.opcode = Branch.opcode == OpCodes.Bne_Un_S ? OpCodes.Brfalse_S : OpCodes.Brfalse;
            else Branch.opcode = Branch.opcode == OpCodes.Beq_S ? OpCodes.Brtrue_S : OpCodes.Brtrue;
            WallPatched |= Wall;
            FogPatched |= Fog;
            i += 3;
        }
        return WallPatched && FogPatched;
    }

    static int ReplaceOutgoingTransitionDeactivation(List<CodeInstruction> Codes)
    {
        var Adapter = MapTransitionAdapter.Instance;
        var Replacement = ((Action<GameObject, bool>)SetOutgoingTransitionActive).Method;
        var Replaced = 0;
        for (var i = 2; i < Codes.Count; i++)
        {
            if (!Codes[i].Calls(Adapter.GameObjectSetActive)
                || !MapTransitionAdapter.LoadsInt(Codes[i - 1], 0)
                || !Codes[i - 2].Calls(Adapter.SegmentParentGetter)) continue;
            Codes[i].opcode = OpCodes.Call;
            Codes[i].operand = Replacement;
            Replaced++;
        }
        return Replaced;
    }

    static int ReplaceTransitionSegmentActivations(List<CodeInstruction> Codes)
    {
        var Adapter = MapTransitionAdapter.Instance;
        var Replacement = ((Action<MapHandler>)ActivateTransitionSegment).Method;
        var Replaced = 0;
        foreach (var Code in Codes)
        {
            if (!Code.Calls(Adapter.NativeActivateCurrentSegmentMethod)) continue;
            Code.opcode = OpCodes.Call;
            Code.operand = Replacement;
            Replaced++;
        }
        return Replaced;
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.GoToSegment), typeof(Segment))]
    static class TransitionPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var Adapter = MapTransitionAdapter.Instance;
            var CanTransitionMethod = ((Func<Segment, int, bool>)CanTransition).Method;
            var StartCoroutine = AccessTools.DeclaredMethod(
                typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new[] { typeof(IEnumerator) })
                ?? throw new MissingMethodException(typeof(MonoBehaviour).FullName, nameof(MonoBehaviour.StartCoroutine));
            var WrapGeneration = ((Func<IEnumerator, IEnumerator>)WrapTransitionGeneration).Method;
            var GuardFound = false;
            var TransitionCallFound = false;
            for (var i = 0; i < Codes.Count; i++)
            {
                if (i >= 3 && Codes[i - 3].LoadsField(Adapter.TransitionRequest)
                    && Codes[i - 2].opcode == OpCodes.Ldarg_0
                    && Codes[i - 1].LoadsField(Map.CurrentSegmentField)
                    && (Codes[i].opcode == OpCodes.Bgt || Codes[i].opcode == OpCodes.Bgt_S))
                {
                    if (GuardFound)
                        throw new AmbiguousMatchException("MapHandler.GoToSegment transition guard was ambiguous.");
                    var Target = Codes[i].operand;
                    Codes[i].opcode = OpCodes.Call;
                    Codes[i].operand = CanTransitionMethod;
                    Codes.Insert(++i, new CodeInstruction(OpCodes.Brtrue, Target));
                    GuardFound = true;
                    continue;
                }
                if (i + 1 >= Codes.Count || !Codes[i].Calls(Adapter.TransitionFactory)
                    || !Codes[i + 1].Calls(StartCoroutine)) continue;
                if (TransitionCallFound)
                    throw new AmbiguousMatchException("MapHandler.GoToSegment transition coroutine call was ambiguous.");
                Codes.Insert(++i, new CodeInstruction(OpCodes.Call, WrapGeneration));
                TransitionCallFound = true;
            }
            if (!GuardFound || !TransitionCallFound)
                throw new MissingMethodException(
                    "MapHandler.GoToSegment transition guard or coroutine call was not found.");
            return Codes;
        }
    }
    [HarmonyPatch]
    static class TransitionActivationPatch
    {
        static MethodBase TargetMethod() => MapTransitionAdapter.Instance.TransitionMoveNext;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var Clear = AccessTools.DeclaredMethod(typeof(List<PhotonView>), nameof(List<PhotonView>.Clear))
                ?? throw new MissingMethodException("List<PhotonView>.Clear was not found.");
            var Retain = ((Action<List<PhotonView>>)RetainNearbyTrackedViews).Method;
            var ClearPatched = false;
            for (var i = 0; i < Codes.Count; i++)
            {
                if (!Codes[i].Calls(Clear)) continue;
                if (ClearPatched)
                    throw new AmbiguousMatchException("MapHandler.GoToSegment tracked-view cleanup was ambiguous.");
                Codes[i].opcode = OpCodes.Call;
                Codes[i].operand = Retain;
                ClearPatched = true;
            }
            var PeakSpawnersPatched = SpawnTrackerTransaction.ReplaceTargetPeakSpawnerAppend(Codes);
            var RoleGuardsPatched = ReplaceTransitionNativeRoleGuards(Codes);
            var OutgoingDeactivations = ReplaceOutgoingTransitionDeactivation(Codes);
            var SegmentActivations = ReplaceTransitionSegmentActivations(Codes);
            var OrdinaryActivations = PlacementExecution.ReplaceSetActive(Codes);
            if (OutgoingDeactivations != 1 || SegmentActivations != 2 || OrdinaryActivations == 0)
                throw new MissingMethodException(
                    "MapHandler.GoToSegment's outgoing segment, current-segment, or ordinary activation sites changed.");
            if (!ClearPatched || !PeakSpawnersPatched || !RoleGuardsPatched)
                throw new MissingMethodException(
                    "MapHandler.GoToSegment tracked-view cleanup or native terminal-role adapters were not found.");
            return Codes;
        }
    }
}
