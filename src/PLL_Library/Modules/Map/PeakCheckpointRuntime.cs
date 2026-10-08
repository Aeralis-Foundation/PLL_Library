using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class PeakCheckpointRuntime
{
    internal enum PeakCheckpointMode : byte { Direct, ScoutsHonor }

    internal sealed class PeakCheckpoint
    {
        int HonorFxInFlightCount;
        internal bool HonorCompletionProjected { get; private set; }

        internal PeakCheckpoint(
            int Index,
            bool HasSuccessor,
            MapSlot.PeakBridgeTarget Target,
            PhotonView View,
            PeakCheckpointMode Mode)
        {
            this.Index = Index;
            this.HasSuccessor = HasSuccessor;
            SuccessorIndex = HasSuccessor ? checked(Index + 1) : -1;
            this.Target = Target ?? throw new ArgumentNullException(nameof(Target));
            this.View = View && ReferenceEquals(View.gameObject, Target.Statue.gameObject)
                ? View
                : throw new InvalidOperationException("A Peak checkpoint Scout Statue has no owned PhotonView.");
            this.Mode = Mode;
        }

        internal int Index { get; }
        internal bool HasSuccessor { get; }
        internal int SuccessorIndex { get; }
        internal MapSlot.PeakBridgeTarget Target { get; }
        internal Peak.ScoutStatue Statue => Target.Statue;
        internal PhotonView View { get; }
        internal PeakCheckpointMode Mode { get; }
        internal bool Ready { get; private set; }
        internal int HonorFxInFlight => HonorFxInFlightCount;
        internal bool HonorFxVisualComplete => Statue.scoutsHonorActivateFX && Statue.scoutsHonorActivateFX.activeSelf;
        internal bool CompletionInteractionReady => Mode == PeakCheckpointMode.Direct
            || Ready && HonorFxInFlightCount == 0 && HonorCompletionProjected;

        internal void BeginHonorFx() => HonorFxInFlightCount = checked(HonorFxInFlightCount + 1);

        internal void EndHonorFx()
        {
            if (HonorFxInFlightCount <= 0)
                throw new InvalidOperationException("A Peak checkpoint completed an unowned Scouts Honor FX routine.");
            HonorFxInFlightCount--;
        }

        internal bool HasFourDistinctAmulets()
        {
            var Submitted = 0;
            foreach (var Type in RequireAmulets())
            {
                if (Type < 0 || Type > 3 || (Submitted & 1 << Type) != 0) return false;
                Submitted |= 1 << Type;
            }
            return Submitted == 15;
        }

        internal void ApplyHonorCompletion()
        {
            if (Mode != PeakCheckpointMode.ScoutsHonor)
                throw new InvalidOperationException("A direct Peak checkpoint received Scouts Honor completion.");
            if (!Ready)
            {
                var Amulets = RequireAmulets();
                for (var i = 0; i < Amulets.Length; i++) Amulets[i] = i;
                Ready = true;
                Statue.OnAmuletsChanged();
            }
            if (!HonorFxVisualComplete) Statue.RPC_InsertAmulet(-2);
            HonorCompletionProjected = true;
        }


        internal void ApplyBestowProjection(PeakBestowLedger Ledger)
        {
            if (Ledger.CheckpointIndex != Index)
                throw new InvalidOperationException("A Peak bestow ledger targeted another checkpoint.");
            var Amulets = RequireAmulets();
            var Changed = false;
            for (var i = 0; i < Amulets.Length; i++)
            {
                var Expected = i < Ledger.Count ? Ledger.CanonicalTypeAt(i) : -1;
                if (Amulets[i] == Expected) continue;
                Amulets[i] = Expected;
                Changed = true;
            }
            if (Ledger.Count == 4) MarkReady();
            if (Changed) Statue.OnAmuletsChanged();
        }

        int[] RequireAmulets()
        {
            var Amulets = Statue.hasAmulets;
            return Amulets is { Length: 4 }
                ? Amulets
                : throw new InvalidOperationException("A Peak checkpoint Scout Statue lost its canonical amulet state.");
        }

        void MarkReady()
        {
            if (Mode != PeakCheckpointMode.ScoutsHonor)
                throw new InvalidOperationException("A direct Peak checkpoint cannot accept Scouts Honor readiness.");
            Ready = true;
        }
    }

    const string InsertAmuletRpc = "RPC_InsertAmulet";
    const int StrangeGemAmuletIndex = 99;

    readonly MapRun Owner;
    readonly Dictionary<Peak.ScoutStatue, PeakCheckpoint> CheckpointIndex = new();
    NativeCampfireCheckpoint? NativeCampfire;
    PeakCheckpoint?[] Checkpoints = Array.Empty<PeakCheckpoint?>();
    bool[] HonorSlots = Array.Empty<bool>();

    static readonly MethodInfo ScoutsHonorRoutineMethod =
        AccessTools.DeclaredMethod(typeof(Peak.ScoutStatue), "ActivateScoutsHonorFXRoutine", Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(Peak.ScoutStatue).FullName, "ActivateScoutsHonorFXRoutine");
    static readonly MethodInfo ScoutsHonorMoveNextMethod =
        AccessTools.EnumeratorMoveNext(ScoutsHonorRoutineMethod)
        ?? throw new MissingMethodException("Peak.ScoutStatue Scouts Honor iterator was not found.");

    internal PeakCheckpointRuntime(MapRun Owner) =>
        this.Owner = Owner ?? throw new ArgumentNullException(nameof(Owner));

    internal bool Unconfigured => NativeCampfire is null && Checkpoints.Length == 0 && CheckpointIndex.Count == 0;
    internal NativeCampfireCheckpoint? ConfiguredCampfire => NativeCampfire;

    internal bool HonorRequired(int Index) =>
        Index >= 0 && Index < HonorSlots.Length && HonorSlots[Index];

    internal void ApplyHonorCompletion(int Index)
    {
        if (!TryGet(Index, out var Checkpoint) || !HonorRequired(Index))
            throw new InvalidOperationException("Scouts Honor completion targeted a non-honor checkpoint.");
        Checkpoint.ApplyHonorCompletion();
    }

    internal bool HasCheckpoint(int Index) =>
        Index >= 0 && Index < Checkpoints.Length && Checkpoints[Index] is not null;

    internal bool TryGetSuccessorIndex(int Index, out int SuccessorIndex)
    {
        SuccessorIndex = -1;
        if (!HasCheckpoint(Index) || !Checkpoints[Index]!.HasSuccessor) return false;
        SuccessorIndex = Checkpoints[Index]!.SuccessorIndex;
        return true;
    }

    internal void Configure(PreparedMap Prepared, NativeCampfireCheckpoint Campfire)
    {
        if (Campfire is null) throw new ArgumentNullException(nameof(Campfire));
        NativeCampfire = Campfire;
        var Slots = Prepared.Slots;
        var ConfiguredCheckpoints = new PeakCheckpoint?[Slots.Length];
        var ConfiguredHonorSlots = new bool[Slots.Length];
        for (var i = 0; i < Slots.Length; i++)
        {
            var Slot = Slots[i];
            var Target = Slot.BridgeTarget;
            if (Target is null) continue;
            var HasSuccessor = i + 1 < Slots.Length;
            if (HasSuccessor && Slot.PeakAttachment
                && Slots[i + 1].Source is not NadirLayoutSegmentSource)
                throw new InvalidOperationException("A full Peak checkpoint requires an immediate Nadir successor.");
            var Statue = Target.Statue;
            var View = Statue.GetComponent<PhotonView>()
                ?? throw new InvalidOperationException("A Peak checkpoint Scout Statue has no PhotonView.");
            if (Statue.hasAmulets is not { Length: 4 })
                throw new InvalidOperationException("A Peak checkpoint Scout Statue must own four canonical amulet slots.");
            var Mode = HasSuccessor && Slots[i + 1].Source is NadirLayoutSegmentSource
                && Slots[i + 1].NadirRequiresScoutsHonor
                    ? PeakCheckpointMode.ScoutsHonor
                    : PeakCheckpointMode.Direct;
            var Checkpoint = new PeakCheckpoint(i, HasSuccessor, Target, View, Mode);
            ConfiguredCheckpoints[i] = Checkpoint;
            ConfiguredHonorSlots[i] = Mode == PeakCheckpointMode.ScoutsHonor;
            CheckpointIndex.Add(Statue, Checkpoint);
        }
        Checkpoints = ConfiguredCheckpoints;
        HonorSlots = ConfiguredHonorSlots;
    }

    internal void EnableBridgeStatues()
    {
        if (!ReferenceEquals(Map.CurrentRun, Owner) || !Owner.Publishing)
            throw new InvalidOperationException("Bridge Scout Statues can only activate for the current published map run.");
        foreach (var Checkpoint in Checkpoints)
        {
            if (Checkpoint is null) continue;
            var Slot = Owner.Slots[Checkpoint.Index];
            var Target = Checkpoint.Target;
            if (Slot.Released || !Target.Root
                || !ReferenceEquals(NativeFixtures.ValidateBridgeStatue(Target.Root, false, false), Target.Statue)
                || !ReferenceEquals(Slot.BridgeTarget, Target))
                throw new InvalidOperationException("A bound Peak Bridge Scout Statue lost its staged owner.");
            RequirePublishedView(Target.Statue, Checkpoint.View);
            Target.Statue.enabled = true;
        }
    }

    internal NativeCampfireCheckpoint RequireCampfire() =>
        NativeCampfire
        ?? throw new InvalidOperationException("The active Peak checkpoint lost PEAK's Beach campfire contract.");

    internal bool TryGet(Peak.ScoutStatue Statue, out PeakCheckpoint Checkpoint)
    {
        Checkpoint = null!;
        return ReferenceEquals(Map.CurrentRun, Owner) && Owner.HasPublishedLayout && Statue
            && CheckpointIndex.TryGetValue(Statue, out Checkpoint)
            && Checkpoint.Index >= 0 && Checkpoint.Index < Owner.Slots.Length
            && ReferenceEquals(Owner.Slots[Checkpoint.Index].BridgeTarget, Checkpoint.Target);
    }

    internal bool TryGet(int Index, out PeakCheckpoint Checkpoint)
    {
        Checkpoint = null!;
        return ReferenceEquals(Map.CurrentRun, Owner) && Owner.HasPublishedLayout && Index >= 0
            && Index < Checkpoints.Length && Checkpoints[Index] is { } Value
            && (Checkpoint = Value) is not null;
    }

    internal bool IsCurrent(PeakCheckpoint Checkpoint) =>
        ReferenceEquals(Map.CurrentRun, Owner) && Owner.Active && Checkpoint.Index == Map.Index
        && Checkpoint.Index >= 0 && Checkpoint.Index < Owner.Slots.Length
        && !Owner.Slots[Checkpoint.Index].Released
        && (!Checkpoint.HasSuccessor || Checkpoint.SuccessorIndex == Checkpoint.Index + 1
            && Checkpoint.SuccessorIndex < Owner.Slots.Length
            && !Owner.Slots[Checkpoint.SuccessorIndex].Released)
        && ReferenceEquals(Owner.Slots[Checkpoint.Index].BridgeTarget, Checkpoint.Target)
        && Checkpoint.Statue && Checkpoint.Statue.enabled && Checkpoint.Statue.gameObject.activeInHierarchy;

    internal bool AdmissionSatisfied(int Index) =>
        TryGet(Index, out var Checkpoint) && IsCurrent(Checkpoint)
        && Checkpoint.CompletionInteractionReady && Owner.Peak.Campfire.EveryoneInRange(Index);

    internal void RecoverAuthority()
    {
        if (!PhotonNetwork.IsMasterClient || !TryGet(Map.Index, out var Checkpoint)
            || Checkpoint.Mode != PeakCheckpointMode.ScoutsHonor || !Checkpoint.Ready
            || Checkpoint.HonorFxInFlight != 0 || Checkpoint.HonorCompletionProjected
            || !Checkpoint.HasFourDistinctAmulets()) return;
        RequirePublishedView(Checkpoint.Statue, Checkpoint.View).RPC(InsertAmuletRpc, RpcTarget.All, -1);
    }

    internal void RetireSlot(int Index)
    {
        if (Index < 0 || Index >= Checkpoints.Length || Checkpoints[Index] is not { } Checkpoint) return;
        CheckpointIndex.Remove(Checkpoint.Statue);
        Checkpoints[Index] = null;
    }

    internal void ReleaseState()
    {
        NativeCampfire = null;
        Checkpoints = Array.Empty<PeakCheckpoint?>();
        HonorSlots = Array.Empty<bool>();
        CheckpointIndex.Clear();
    }

    static bool TryGetCurrent(
        Peak.ScoutStatue Statue,
        out PeakCheckpointRuntime Runtime,
        out PeakCheckpoint Checkpoint)
    {
        Runtime = null!;
        Checkpoint = null!;
        var Run = Map.CurrentRun;
        if (Run is null || !Run.Peak.Checkpoints.TryGet(Statue, out Checkpoint)) return false;
        Runtime = Run.Peak.Checkpoints;
        return true;
    }

    static PhotonView RequirePublishedView(Peak.ScoutStatue Statue, PhotonView? View)
    {
        if (!Statue || !View || !ReferenceEquals(View.gameObject, Statue.gameObject)
            || View.ViewID <= 0 || !ReferenceEquals(PhotonNetwork.GetPhotonView(View.ViewID), View))
            throw new InvalidOperationException("A bound Peak checkpoint Scout Statue has no published PhotonView.");
        return View;
    }

    bool CanReturnStrangeGem(PeakCheckpoint Checkpoint, Character Interactor)
    {
        if (!IsCurrent(Checkpoint) || Checkpoint.Mode != PeakCheckpointMode.ScoutsHonor
            || Checkpoint.Ready || !Interactor) return false;
        var Item = Interactor.data.currentItem;
        return Item && Item.TryGetComponent<Peak.AmuletBase>(out var Amulet)
            && Amulet.amuletIndex == StrangeGemAmuletIndex;
    }

    IEnumerator FollowScoutsHonorCompletion(PeakCheckpoint Checkpoint, IEnumerator Routine)
    {
        var Completed = false;
        try
        {
            while (Routine.MoveNext()) yield return Routine.Current;
            Completed = true;
        }
        finally
        {
            try { (Routine as IDisposable)?.Dispose(); }
            finally { Checkpoint.EndHonorFx(); }
        }
        if (Completed) OnScoutsHonorFxCompleted(Checkpoint);
    }

    void OnScoutsHonorFxCompleted(PeakCheckpoint Checkpoint)
    {
        if (!PhotonNetwork.IsMasterClient || Checkpoint.HonorCompletionProjected) return;
        try
        {
            if (!Checkpoint.Ready || !Checkpoint.HasFourDistinctAmulets())
                throw new InvalidOperationException("Scouts Honor FX completed before the fourth accepted submission.");
            if (!ReferenceEquals(Map.CurrentRun, Owner) || !Owner.Active
                || Checkpoint.Index < 0 || Checkpoint.Index >= Owner.Slots.Length
                || Owner.Slots[Checkpoint.Index].Released
                || !ReferenceEquals(Owner.Slots[Checkpoint.Index].BridgeTarget, Checkpoint.Target)) return;
            if (!Owner.Peak.PublishHonorCompletion(Checkpoint.Index))
                throw new InvalidOperationException("Scouts Honor completion could not be published.");
        }
        catch (Exception Exception)
        {
            LazySlotGenerator.AbortLazyGeneration(
                Checkpoint.Index, "Scouts Honor presentation completion failed", Exception);
        }
    }

    static bool ShouldRunNativeScoutsHonorReward(Peak.ScoutStatue Statue) =>
        !TryGetCurrent(Statue, out _, out var Checkpoint)
        || Checkpoint.Mode != PeakCheckpointMode.ScoutsHonor;

    [HarmonyPatch(typeof(Peak.ScoutStatue), "SpawnGem_Master")]
    static class BridgeStatueGemGenerationPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.ScoutStatue __instance) =>
            !TryGetCurrent(__instance, out var Runtime, out var Checkpoint)
            || Runtime.Owner.Peak.Reward.RetainsStatueGem(Checkpoint.Index);
    }

    [HarmonyPatch(typeof(Peak.ScoutStatue), "RPC_InsertAmulet", typeof(int))]
    static class BridgeStatueInsertAmuletPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.ScoutStatue __instance, int __0)
        {
            if (!TryGetCurrent(__instance, out var Runtime, out var Checkpoint)) return true;
            if (Checkpoint.Mode == PeakCheckpointMode.Direct) return false;
            if (__0 == StrangeGemAmuletIndex)
                return !Checkpoint.Ready && Runtime.IsCurrent(Checkpoint);
            if (__0 == -2) return true;
            if (__0 != -1) return false;
            if (Checkpoint.HonorFxInFlight != 0 || Checkpoint.HonorCompletionProjected) return false;
            return !PhotonNetwork.IsMasterClient
                || Runtime.IsCurrent(Checkpoint) && Checkpoint.Ready
                    && Checkpoint.HasFourDistinctAmulets();
        }
    }

    [HarmonyPatch]
    static class ScoutsHonorGenerationPatch
    {
        static MethodBase TargetMethod() => ScoutsHonorRoutineMethod;

        [HarmonyPostfix]
        static void Postfix(Peak.ScoutStatue __instance, ref IEnumerator __result)
        {
            if (!TryGetCurrent(__instance, out var Runtime, out var Checkpoint)
                || Checkpoint.Mode != PeakCheckpointMode.ScoutsHonor) return;
            if (__result is null)
                throw new InvalidOperationException("Peak.ScoutStatue returned no Scouts Honor generation routine.");
            Checkpoint.BeginHonorFx();
            __result = Runtime.FollowScoutsHonorCompletion(Checkpoint, __result);
        }
    }

    [HarmonyPatch]
    static class ScoutsHonorRewardReplacementPatch
    {
        static MethodBase TargetMethod() => ScoutsHonorMoveNextMethod;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var Iterator = ScoutsHonorMoveNextMethod.DeclaringType
                ?? throw new MissingMemberException("Peak.ScoutStatue Scouts Honor iterator type is unavailable.");
            var OwnerField = AccessTools.DeclaredField(Iterator, "<>4__this");
            if (OwnerField is null || OwnerField.FieldType != typeof(Peak.ScoutStatue))
                throw new MissingFieldException(Iterator.FullName, "<>4__this");
            var IsMaster = AccessTools.PropertyGetter(typeof(PhotonNetwork), nameof(PhotonNetwork.IsMasterClient))
                ?? throw new MissingMethodException(typeof(PhotonNetwork).FullName, "IsMasterClient.get");
            var Replacement = ((Func<Peak.ScoutStatue, bool>)ShouldRunNativeScoutsHonorReward).Method;
            var Found = -1;
            for (var i = 0; i + 1 < Codes.Count; i++)
            {
                if (!Codes[i].Calls(IsMaster)) continue;
                if (Found >= 0)
                    throw new AmbiguousMatchException("Scouts Honor's final master reward gate was ambiguous.");
                if (Codes[i + 1].opcode != OpCodes.Brfalse && Codes[i + 1].opcode != OpCodes.Brfalse_S)
                    throw new MissingMethodException("Scouts Honor's final master reward branch was not found.");
                Found = i;
            }
            if (Found < 0)
                throw new MissingMethodException("Scouts Honor's final master reward gate was not found.");

            var LoadIterator = new CodeInstruction(OpCodes.Ldarg_0);
            LoadIterator.labels.AddRange(Codes[Found].labels);
            Codes[Found].labels.Clear();
            LoadIterator.blocks.AddRange(Codes[Found].blocks);
            Codes[Found].blocks.Clear();
            Codes.InsertRange(Found, new[]
            {
                LoadIterator,
                new CodeInstruction(OpCodes.Ldfld, OwnerField)
            });
            Codes[Found + 2].opcode = OpCodes.Call;
            Codes[Found + 2].operand = Replacement;
            return Codes;
        }
    }

    [HarmonyPatch(typeof(Peak.ScoutStatue), nameof(Peak.ScoutStatue.IsConstantlyInteractable))]
    static class PeakCheckpointInteractiblePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.ScoutStatue __instance, Character interactor, ref bool __result)
        {
            if (!TryGetCurrent(__instance, out var Runtime, out var Checkpoint)) return true;
            if (!Runtime.IsCurrent(Checkpoint))
            {
                __result = false;
                return false;
            }
            __result = Runtime.CanReturnStrangeGem(Checkpoint, interactor)
                || Runtime.Owner.Peak.Bestow.CanBestowPeakAmulet(Checkpoint, interactor)
                || Runtime.Owner.Peak.Reward.CanUse(Checkpoint.Index);
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.ScoutStatue), nameof(Peak.ScoutStatue.Interact_CastFinished))]
    static class PeakCheckpointInteractionPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.ScoutStatue __instance, Character interactor)
        {
            if (!TryGetCurrent(__instance, out var Runtime, out var Checkpoint)) return true;
            if (!Runtime.IsCurrent(Checkpoint)) return false;
            if (Runtime.CanReturnStrangeGem(Checkpoint, interactor)) return true;
            if (Runtime.Owner.Peak.Bestow.CanBestowPeakAmulet(Checkpoint, interactor))
            {
                Runtime.Owner.Peak.Bestow.RequestPeakBestow(Checkpoint, interactor);
                return false;
            }
            if (Runtime.Owner.Peak.Reward.CanUse(Checkpoint.Index))
                Runtime.Owner.Peak.Reward.Interact(Checkpoint.Index, interactor);
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.ScoutStatue), nameof(Peak.ScoutStatue.GetInteractionText))]
    static class PeakCheckpointInteractionTextPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.ScoutStatue __instance, ref string __result)
        {
            if (!TryGetCurrent(__instance, out var Runtime, out var Checkpoint)
                || !Runtime.IsCurrent(Checkpoint)) return true;
            var Interactor = Character.localCharacter;
            if (Interactor && (Runtime.CanReturnStrangeGem(Checkpoint, Interactor)
                    || Runtime.Owner.Peak.Bestow.CanBestowPeakAmulet(Checkpoint, Interactor))) return true;
            if (!Runtime.Owner.Peak.Reward.CanUse(Checkpoint.Index)) return true;
            __result = Runtime.Owner.Peak.Reward.InteractionText(Checkpoint.Index);
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.ScoutStatue), nameof(Peak.ScoutStatue.GetName))]
    static class PeakCheckpointNamePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.ScoutStatue __instance, ref string __result)
        {
            if (!TryGetCurrent(__instance, out var Runtime, out var Checkpoint)
                || !Runtime.IsCurrent(Checkpoint)) return true;
            var Interactor = Character.localCharacter;
            if (Interactor && (Runtime.CanReturnStrangeGem(Checkpoint, Interactor)
                    || Runtime.Owner.Peak.Bestow.CanBestowPeakAmulet(Checkpoint, Interactor))) return true;
            if (!Runtime.Owner.Peak.Reward.CanUse(Checkpoint.Index)) return true;
            __result = Runtime.Owner.Peak.Reward.Name(Checkpoint.Index);
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.ScoutStatue), nameof(Peak.ScoutStatue.GetInteractTime))]
    static class PeakCheckpointInteractTimePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.ScoutStatue __instance, Character interactor, ref float __result)
        {
            if (!TryGetCurrent(__instance, out var Runtime, out var Checkpoint)
                || !Runtime.IsCurrent(Checkpoint)
                || Runtime.CanReturnStrangeGem(Checkpoint, interactor)
                || Runtime.Owner.Peak.Bestow.CanBestowPeakAmulet(Checkpoint, interactor)) return true;
            if (!Runtime.Owner.Peak.Reward.CanUse(Checkpoint.Index)) return true;
            __result = Runtime.Owner.Peak.Reward.InteractTime(Checkpoint.Index, interactor);
            return false;
        }
    }
}
