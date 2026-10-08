using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Library;

internal static class NadirRuntime
{
    internal static void RestoreSoulFreedStatus(Peak.VoidBiome Controller, int Status) =>
        Controller.SetSoulState(Status);
    static MapHandler.MapSegment? VoidControllerSegment() =>
        Peak.VoidBiome.instance ? Peak.VoidBiome.instance.segment : null;
    internal static bool IsVoidControllerIndex(MapHandler Handler, int Index) =>
        Handler && Index >= 0 && Index < Handler.segments.Length
        && VoidControllerSegment() is { } Nadir && ReferenceEquals(Handler.segments[Index], Nadir);
    internal static bool CurrentSegmentIsVoidController(MapHandler Handler) =>
        Map.HasPublishedLayout && Handler && IsVoidControllerIndex(Handler, Handler.currentSegment);
    internal static bool TryGetVoidControllerSlot(out int Index)
    {
        Index = -1;
        var Segment = VoidControllerSegment();
        if (!Map.HasPublishedLayout || Segment is null) return false;
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            if (!ReferenceEquals(Map.Slots[i].Segment, Segment)) continue;
            if (Index >= 0)
                throw new InvalidOperationException("The VOID controller segment belongs to multiple published slots.");
            Index = i;
        }
        return Index >= 0;
    }
    internal static void ReconcileVoidController(MapRun Run, int Index) =>
        Run.PublicationOwner?.Originals.ReconcileVoidController(Run.Slots, Index);
    internal static void PublishInSpace(MapRun Run, int Index)
    {
        if (Index < 0 || Index >= Run.Slots.Length || !DayNightManager.instance) return;
        var Profile = Run.Slots[Index].Segment._dayNightProfile;
        var Value = 0f;
        if (Profile && Profile.globalStaticShaderFloats is { } Parameters)
            foreach (var Parameter in Parameters)
                if (Parameter is not null && Parameter.parameter == DayNightManager.ShaderParams.InSpace)
                    Value = Parameter.paramValue;
        Shader.SetGlobalFloat(
            DayNightManager.instance.getShaderValue(DayNightManager.ShaderParams.InSpace), Value);
    }
    internal static bool CurrentSegmentIsNadir(MapHandler Handler)
    {
        if (!Map.HasPublishedLayout || !Handler) return false;
        var Index = Handler.currentSegment;
        return Index >= 0 && Index < Map.Slots.Length
            ? Map.Slots[Index].Source is NadirLayoutSegmentSource
            : IsVoidControllerIndex(Handler, Index);
    }

    static bool IsNadirJump(Segment Requested) =>
        Requested == Segment.Void
        || Map.HasPublishedLayout && NavigationRoles.TryTargetSlot(Requested, out var Index)
            && Index >= 0 && Index < Map.Slots.Length && Map.Slots[Index].Source is NadirLayoutSegmentSource;

    internal static bool ReplaceNadirJumpGuard(List<CodeInstruction> Codes)
    {
        var Instance = AccessTools.DeclaredField(typeof(Peak.VoidBiome), nameof(Peak.VoidBiome.instance))
            ?? throw new MissingFieldException(typeof(Peak.VoidBiome).FullName, nameof(Peak.VoidBiome.instance));
        var GetSpawn = AccessTools.DeclaredMethod(
            typeof(Peak.VoidBiome), nameof(Peak.VoidBiome.GetSpawnPosition), new[] { typeof(int) })
            ?? throw new MissingMethodException(typeof(Peak.VoidBiome).FullName, nameof(Peak.VoidBiome.GetSpawnPosition));
        var IsNadir = ((Func<Segment, bool>)IsNadirJump).Method;
        var Found = false;
        for (var i = 0; i + 5 < Codes.Count; i++)
        {
            if (Codes[i].opcode != OpCodes.Ldarg_0
                || !MapTransitionAdapter.LoadsInt(Codes[i + 1], (int)Segment.Void)
                || !MapTransitionAdapter.IsNotEqualBranch(Codes[i + 2].opcode)
                || !Codes[i + 3].LoadsField(Instance)
                || !Codes[i + 5].Calls(GetSpawn)) continue;
            if (Found)
                throw new AmbiguousMatchException("MapHandler.JumpToSegmentLogic's Nadir player formation was ambiguous.");
            Codes[i + 1].opcode = OpCodes.Call;
            Codes[i + 1].operand = IsNadir;
            Codes[i + 2].opcode = Codes[i + 2].opcode == OpCodes.Bne_Un_S
                ? OpCodes.Brfalse_S
                : OpCodes.Brfalse;
            Found = true;
        }
        return Found;
    }

    static bool OwnsVoidController(Peak.VoidBiome Controller)
    {
        var Current = Peak.VoidBiome.instance;
        return Map.HasPublishedLayout && Controller && Current
            && Controller.GetInstanceID() == Current.GetInstanceID();
    }

    [HarmonyPatch(typeof(Peak.VoidBiome), nameof(Peak.VoidBiome.GetSpawnPoint), typeof(int))]
    static class NadirSpawnPointPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.VoidBiome __instance, int __0, ref Transform __result)
        {
            var Handler = Map.Owner;
            if (!Map.Active || !Handler || !OwnsVoidController(__instance)) return true;
            var Index = Handler.currentSegment;
            if (Index < 0 || Index >= Map.Slots.Length) return true;
            var Slot = Map.Slots[Index];
            if (Slot.Released || Slot.PlacedNadir is not { } Nadir) return true;
            __result = Nadir.SpawnPoints[__0 % Nadir.SpawnPoints.Length];
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.VoidBiome), nameof(Peak.VoidBiome.Activate))]
    static class VoidActivationPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.VoidBiome __instance) => !OwnsVoidController(__instance) || !__instance.isActive;
    }

    static void SuppressPublishedLayoutForVoid()
    {
        var Handler = Zorro.Core.Singleton<MapHandler>.Instance;
        if (!Handler) throw new InvalidOperationException("PEAK's map handler is unavailable during Void activation.");
        if (!Map.HasPublishedLayout)
        {
            Handler.segments[(int)Segment.Caldera].segmentParent.transform.parent.gameObject.SetActive(false);
            return;
        }
        foreach (var Slot in Map.Slots)
            if (!Slot.Released && Slot.Container) Slot.Container.gameObject.SetActive(false);
    }
    internal static bool ReplaceVoidSuppression(List<CodeInstruction> Codes)
    {
        var Replacement = ((Action)SuppressPublishedLayoutForVoid).Method;
        return PeakLifecycle.ReplaceNativeContainerActivation(Codes, false, Replacement);
    }

    static bool TryGetTransitionNadirSlot(GameObject SegmentParent, int SlotIndex, out MapSlot NadirSlot)
    {
        NadirSlot = null!;
        if (!Map.HasPublishedLayout || !SegmentParent || SlotIndex < 0 || SlotIndex >= Map.Slots.Length) return false;
        var Candidate = Map.Slots[SlotIndex];
        if (Candidate.Released || Candidate.Source is not NadirLayoutSegmentSource || Candidate.Segment.segmentParent != SegmentParent)
            return false;
        NadirSlot = Candidate;
        return true;
    }

    static void DisableWarpVolume(Volume? Volume)
    {
        if (!Volume) return;
        Volume.weight = 0f;
        Volume.enabled = false;
    }
    static void QuiesceNadirGate(Transform Gate)
    {
        var Portal = Gate.GetComponent<Peak.PeakGatePortal>()
            ?? throw new InvalidOperationException("The transitioning Nadir gate lost its portal.");
        Portal.enabled = false;
        DisableWarpVolume(Portal.warpPost);
        DisableWarpVolume(Portal.warpPost2);
        foreach (var Loop in Gate.GetComponentsInChildren<AudioLoop>(true))
        {
            if (!Loop) continue;
            Loop.enabled = false;
            if (Loop.loop) Loop.loop.Stop();
        }
        foreach (var Source in Gate.GetComponentsInChildren<AudioSource>(true))
            if (Source) Source.Stop();
    }
    static bool TryDeactivateNadir(GameObject Value, int SlotIndex)
    {
        if (!TryGetTransitionNadirSlot(Value, SlotIndex, out var Slot)) return false;
        var Nadir = Slot.PlacedNadir
            ?? throw new InvalidOperationException("The transitioning Nadir slot lost its placed fixture.");
        if (!Slot.Container)
            throw new InvalidOperationException("The transitioning Nadir slot lost its container.");
        Slot.Container.gameObject.SetActive(true);
        Nadir.Peak.SetParent(Slot.Container, true);
        Nadir.Peak.gameObject.SetActive(true);
        QuiesceNadirGate(Nadir.Gate);
        SpecialDayProjection.DeactivatePreservingGlobals(Value);
        Physics.SyncTransforms();
        Nadir.Ambience.gameObject.SetActive(false);
        return true;
    }
    internal static bool TryDeactivateOutgoingTransition(GameObject Value) =>
        TryDeactivateNadir(Value, Map.Index - 1);
    internal static void RetainJumpedNadir(int OutgoingIndex)
    {
        if (!Map.Active || OutgoingIndex < 0 || OutgoingIndex >= Map.Slots.Length
            || OutgoingIndex == Map.Index) return;
        TryDeactivateNadir(Map.Slots[OutgoingIndex].Segment.segmentParent, OutgoingIndex);
    }

    static bool TryGetNadirSlot(Peak.PeakGatePortal Gate, out int Index)
    {
        Index = -1;
        if (!Map.Active || !Gate) return false;
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            var Slot = Map.Slots[i];
            if (Slot.Released || Slot.Source is not NadirLayoutSegmentSource || Slot.NadirGate != Gate.transform) continue;
            if (Index >= 0)
                throw new InvalidOperationException("The active Nadir gate belongs to multiple layout slots.");
            Index = i;
        }
        return Index >= 0;
    }

    static NativeCampfireCheckpoint? NadirCheckpoint(
        Peak.PeakGatePortal Gate, out int Index)
    {
        if (TryGetNadirSlot(Gate, out Index) && CanUseNadirGate(Index) && !ReleasedSlotRetirement.IsRunTerminal(Index))
            return Map.CurrentRun?.Peak.Checkpoints.ConfiguredCampfire;
        return null;
    }

    static bool CanUseNadirGate(int SlotIndex) =>
        SlotIndex == Map.Index && !Map.Slots[SlotIndex].PeakAttachment
        && (ReleasedSlotRetirement.IsRunTerminal(SlotIndex) ? Map.CurrentRun!.Configuration.Terminal.Winnable : ReleasedSlotRetirement.IsNadirAdvance(SlotIndex));
    internal static bool EveryoneAtNadirGate(MapSlot NadirSlot)
    {
        var Checkpoint = Map.CurrentRun!.Peak.Checkpoints.RequireCampfire();
        var Gate = NadirSlot.NadirGate
            ?? throw new InvalidOperationException("The active Nadir segment lost its placed gate.");
        return ProgressProjection.EveryoneAtCheckpoint(Gate.position, Checkpoint.Radius);
    }

    static Segment PeakSuccessorNavigationRole()
    {
        if (Map.Active)
        {
            var CurrentIndex = Map.Index;
            var Checkpoints = Map.CurrentRun!.Peak.Checkpoints;
            if (Checkpoints.TryGetSuccessorIndex(CurrentIndex, out var SuccessorIndex)
                && SuccessorIndex < Map.Slots.Length
                && Map.Slots[SuccessorIndex].Source is NadirLayoutSegmentSource)
                return Map.Slots[SuccessorIndex].NavigationRole;
        }
        return Segment.Void;
    }

    [HarmonyPatch]
    static class NadirWarpPatch
    {
        static readonly MethodInfo Factory = AccessTools.DeclaredMethod(
            typeof(MapHandler), nameof(MapHandler.GoToVoidRoutine), new[] { typeof(bool) })
            ?? throw new MissingMethodException(typeof(MapHandler).FullName, nameof(MapHandler.GoToVoidRoutine));

        static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(Factory)
            ?? throw new MissingMethodException("MapHandler.GoToVoidRoutine iterator was not found.");

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var Jump = AccessTools.DeclaredMethod(
                typeof(MapHandler), nameof(MapHandler.JumpToSegment), new[] { typeof(Segment) })
                ?? throw new MissingMethodException(typeof(MapHandler).FullName, nameof(MapHandler.JumpToSegment));
            var Target = ((Func<Segment>)PeakSuccessorNavigationRole).Method;
            var Found = false;
            for (var i = 1; i < Codes.Count; i++)
            {
                if (!Codes[i].Calls(Jump) || !MapTransitionAdapter.LoadsInt(Codes[i - 1], (int)Segment.Void)) continue;
                if (Found)
                    throw new AmbiguousMatchException("MapHandler.GoToVoidRoutine's Nadir jump target was ambiguous.");
                Codes[i - 1].opcode = OpCodes.Call;
                Codes[i - 1].operand = Target;
                Found = true;
            }
            if (!Found)
                throw new MissingMethodException("MapHandler.GoToVoidRoutine's Nadir jump target was not found.");
            return Codes;
        }
    }

    [HarmonyPatch(typeof(Peak.PeakGatePortal), nameof(Peak.PeakGatePortal.Interact_CastFinished))]
    static class NadirGatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.PeakGatePortal __instance, Character interactor)
        {
            if (!TryGetNadirSlot(__instance, out var Index)) return true;
            if (!CanUseNadirGate(Index)) return false;
            if (ReleasedSlotRetirement.IsRunTerminal(Index)) return true;
            if (Map.CurrentRun!.Transitions.RequestedDirectAdvance == Index) return false;
            var Checkpoint = Map.CurrentRun!.Peak.Checkpoints.RequireCampfire();
            if (!ProgressProjection.EveryoneAtCheckpoint(__instance.transform.position, Checkpoint.Radius))
            {
                __instance.CancelCast(interactor);
                return false;
            }
            __instance.teleportInstance.Play(Vector3.zero);
            __instance.CancelCast(interactor);
            Map.CurrentRun!.Network.DeferredOperations.RequestDirectAdvance(Index);
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.PeakGatePortal), nameof(Peak.PeakGatePortal.IsInteractible))]
    static class NadirGateInteractiblePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.PeakGatePortal __instance, ref bool __result)
        {
            if (!TryGetNadirSlot(__instance, out var Index)) return true;
            __result = CanUseNadirGate(Index);
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.PeakGatePortal), nameof(Peak.PeakGatePortal.GetInteractionText))]
    static class NadirGateInteractionTextPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.PeakGatePortal __instance, ref string __result)
        {
            if (NadirCheckpoint(__instance, out _) is not { } Checkpoint) return true;
            __result = ProgressProjection.CheckpointInteractionText(__instance.transform.position, Checkpoint.Radius);
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.PeakGatePortal), nameof(Peak.PeakGatePortal.GetName))]
    static class NadirGateNamePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.PeakGatePortal __instance, ref string __result)
        {
            if (NadirCheckpoint(__instance, out _) is null) return true;
            __result = LocalizedText.GetText("CAMPFIRE");
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.PeakGatePortal), nameof(Peak.PeakGatePortal.GetInteractTime))]
    static class NadirGateInteractTimePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.PeakGatePortal __instance, ref float __result)
        {
            if (NadirCheckpoint(__instance, out _) is not { } Checkpoint) return true;
            __result = Checkpoint.InteractTime;
            return false;
        }
    }

}
