using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class Campfire
{
    static readonly Dictionary<global::Campfire, int> Slots = new();
    static readonly Dictionary<global::Campfire, Segment> OriginalTargets = new();
    static bool transactionActive;

    readonly struct LightInvocation
    {
        internal enum Kind { Native, ImmediateTransition, DeferredTransition }

        LightInvocation(Kind current, int completedIndex)
        {
            Current = current;
            CompletedIndex = completedIndex;
        }

        internal Kind Current { get; }
        internal int CompletedIndex { get; }
        internal static LightInvocation Native => default;
        internal static LightInvocation Immediate(int completedIndex) =>
            new(Kind.ImmediateTransition, completedIndex);
        internal static LightInvocation Deferred(int completedIndex) =>
            new(Kind.DeferredTransition, completedIndex);
    }

    internal static void BeginTransaction()
    {
        OriginalTargets.Clear();
        transactionActive = true;
    }

    internal static void CommitTransaction()
    {
        OriginalTargets.Clear();
        Slots.Clear();
        transactionActive = false;
    }

    internal static void RollbackTransaction()
    {
        foreach (var target in OriginalTargets)
            if (target.Key) target.Key.advanceToSegment = target.Value;
        OriginalTargets.Clear();
        Slots.Clear();
        transactionActive = false;
    }

    internal static void Bind()
    {
        Slots.Clear();
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            if (Map.Slots[i].Released || Map.Slots[i].Source is NadirLayoutSegmentSource) continue;
            var root = Map.Slots[i].Segment.segmentCampfire;
            if (root) Bind(root, i);
        }
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            if (Map.Slots[i].Released || Map.Slots[i].Source is NadirLayoutSegmentSource) continue;
            var root = Map.Slots[i].Segment.segmentParent;
            if (root) BindMissing(root, i);
        }
        Map.CurrentRun?.Peak.Campfire.Bind();
    }

    internal static void Bind(int index)
    {
        if (index < 0 || index >= Map.Slots.Length || Map.Slots[index].Released
            || Map.Slots[index].Source is NadirLayoutSegmentSource) return;
        var segment = Map.Slots[index].Segment;
        if (segment.segmentCampfire) Bind(segment.segmentCampfire, index);
        if (segment.segmentParent) BindMissing(segment.segmentParent, index);
        Map.CurrentRun?.Peak.Campfire.Bind(index);
    }

    static void Bind(GameObject root, int index)
    {
        foreach (var campfire in root.GetComponentsInChildren<global::Campfire>(true))
            Bind(campfire, index);
    }

    static void BindMissing(GameObject root, int index)
    {
        foreach (var campfire in root.GetComponentsInChildren<global::Campfire>(true))
            if (!Slots.ContainsKey(campfire)) Bind(campfire, index);
    }

    internal static void Bind(global::Campfire campfire, int index)
    {
        if (transactionActive && !OriginalTargets.ContainsKey(campfire))
            OriginalTargets.Add(campfire, campfire.advanceToSegment);
        Slots[campfire] = index;
        campfire.advanceToSegment = index + 1 < Map.Slots.Length
            ? Map.Slots[index + 1].NavigationRole
            : Map.Slots[index].NavigationRole;
    }

    internal static void Unbind(global::Campfire Campfire) => Slots.Remove(Campfire);

    internal static bool TryGetSlot(global::Campfire campfire, out int index)
    {
        if (Slots.TryGetValue(campfire, out index)) return true;
        if (!ReleasedSlotRetirement.TryGetSlot(campfire, out index)) return false;
        Bind(campfire, index);
        return true;
    }

    internal static void SaveOrDeferTransition(global::Campfire campfire)
    {
        if (Map.Active && TryGetSlot(campfire, out var index) && index + 1 == Map.Index) return;
        if (Map.CurrentRun is { Active: true } Run) { Run.HookedGeneration.SaveOrDefer(campfire); return; }
        Peak.Quicksave.SaveNow();
        GUIManager.instance.Quicksave();
    }

    [HarmonyPatch(typeof(global::Campfire), "IsMiniRunEndingCampfire")]
    static class MiniRunPatch
    {
        [HarmonyPostfix]
        static void Postfix(global::Campfire __instance, ref bool __result)
        {
            if (Map.Active && TryGetSlot(__instance, out _)) __result = false;
        }
    }

    [HarmonyPatch(typeof(global::Campfire), "Light_Rpc")]
    static class LightPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var Codes = new List<CodeInstruction>(instructions);
            var Save = ((Action)Peak.Quicksave.SaveNow).Method;
            var Gui = AccessTools.DeclaredField(typeof(GUIManager), nameof(GUIManager.instance))
                ?? throw new MissingFieldException("GUIManager.instance was not found.");
            var Notification = AccessTools.DeclaredMethod(
                typeof(GUIManager), nameof(GUIManager.Quicksave), Type.EmptyTypes)
                ?? throw new MissingMethodException("GUIManager.Quicksave was not found.");
            var Replacement = ((Action<global::Campfire>)SaveOrDeferTransition).Method;
            var Replaced = false;
            for (var i = 0; i + 2 < Codes.Count; i++)
            {
                if (!Codes[i].Calls(Save)) continue;
                if (Replaced)
                    throw new AmbiguousMatchException("Campfire.Light_Rpc's quicksave pair was ambiguous.");
                if (!Codes[i + 1].LoadsField(Gui) || !Codes[i + 2].Calls(Notification))
                    throw new MissingMethodException("Campfire.Light_Rpc's quicksave notification was not found.");
                Codes[i].opcode = OpCodes.Ldarg_0;
                Codes[i].operand = null;
                Codes[i + 1].opcode = OpCodes.Call;
                Codes[i + 1].operand = Replacement;
                Codes[i + 2].opcode = OpCodes.Nop;
                Codes[i + 2].operand = null;
                Replaced = true;
            }
            if (!Replaced) throw new MissingMethodException("Campfire.Light_Rpc's quicksave pair was not found.");
            return Codes;
        }

        [HarmonyPrefix]
        static bool Prefix(global::Campfire __instance, ref bool updateSegment, out LightInvocation __state)
        {
            __state = LightInvocation.Native;
            if (!updateSegment || !Map.Active) return true;
            if (!TryGetSlot(__instance, out var index))
            {
                LazySlotGenerator.AbortLazyGeneration(Map.Index, "Campfire progression lost its owning map slot");
                return false;
            }
            switch (LazySlotGenerator.AdmitCampfireTransition(index))
            {
                case LazySlotGenerator.CampfireTransitionAdmission.Native:
                    __state = LightInvocation.Immediate(index);
                    Bind(__instance, index);
                    if (ReleasedSlotRetirement.IsRunTerminal(index) || Map.Slots[index].PeakAttachment) updateSegment = false;
                    return true;
                case LazySlotGenerator.CampfireTransitionAdmission.Deferred:
                    __state = LightInvocation.Deferred(index);
                    updateSegment = false;
                    return true;
                case LazySlotGenerator.CampfireTransitionAdmission.Stale:
                    if (index >= Map.Index)
                        LazySlotGenerator.AbortLazyGeneration(
                            Map.Index, "Campfire progression came from a map slot that does not own the active transition");
                    return false;
                default:
                    throw new InvalidOperationException("A campfire transition has an unknown admission result.");
            }
        }

        [HarmonyPostfix]
        static void Postfix(global::Campfire __instance, LightInvocation __state)
        {
            if (Map.CurrentRun?.Peak.Campfire.OnCampfireLight(__instance) == true) return;
            var Invocation = __state;
            var Index = Invocation.CompletedIndex;
            if (Invocation.Current == LightInvocation.Kind.Native || !Map.Active) return;
            if (Invocation.Current == LightInvocation.Kind.DeferredTransition)
            {
                if (!Map.Slots[Index].PeakAttachment) LazySlotGenerator.QueueCampfireTransition(Index);
                return;
            }
            if (ReleasedSlotRetirement.IsRunTerminal(Index))
            {
                LazySlotGenerator.OnFinalCampfireLit(Index);
                return;
            }
            if (Map.Slots[Index].PeakAttachment) return;
            if (Map.Index != Index + 1)
            {
                LazySlotGenerator.CancelCampfireTransition(Index);
                MapQuicksave.CancelCampfireQuicksave(Index);
                return;
            }
            ReleasedSlotRetirement.OnCampfireTransition(Index);
        }

        [HarmonyFinalizer]
        static Exception? Finalizer(Exception? __exception, LightInvocation __state)
        {
            var Invocation = __state;
            if (__exception is not null && Invocation.Current == LightInvocation.Kind.ImmediateTransition)
            {
                LazySlotGenerator.CancelCampfireTransition(Invocation.CompletedIndex);
                MapQuicksave.CancelCampfireQuicksave(Invocation.CompletedIndex);
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Campfire_Set_Segment), nameof(Campfire_Set_Segment.ModifyObject))]
    static class GeneratedCampfirePatch
    {
        [HarmonyPostfix]
        static void Postfix(GameObject spawned)
        {
            if (!Map.Active || !spawned) return;
            foreach (var campfire in spawned.GetComponentsInChildren<global::Campfire>(true))
                if (TryGetSlot(campfire, out var index)) Bind(campfire, index);
        }
    }
}
