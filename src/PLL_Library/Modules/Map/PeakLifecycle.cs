using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace PLL.Library;

internal static class PeakLifecycle
{
    internal static int PeakSlot()
    {
        var Result = -1;
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            if (!Map.Slots[i].PeakAttachment) continue;
            if (Result >= 0)
                throw new InvalidOperationException("The published map layout contains multiple Peak attachments.");
            Result = i;
        }
        return Result >= 0
            ? Result
            : throw new InvalidOperationException("The published map layout contains no Peak attachment.");
    }

    internal static Vector3 PeakRespawnPosition()
    {
        var Handler = Singleton<MapHandler>.Instance;
        if (!Handler) throw new InvalidOperationException("PEAK's map handler is unavailable during a Peak jump.");
        if (!Map.HasPublishedLayout)
            return Handler.respawnThePeak
                ? Handler.respawnThePeak.position
                : throw new InvalidOperationException("PEAK's Peak respawn anchor is unavailable during a segment jump.");
        var Respawn = Map.Slots[PeakSlot()].PeakRespawn;
        return Respawn
            ? Respawn.position
            : throw new InvalidOperationException("The Peak respawn anchor is unavailable during a segment jump.");
    }

    internal static PeakHandler? PublishedPeakHandler(IReadOnlyList<MapSlot> Slots)
    {
        PeakHandler? Result = null;
        foreach (var Slot in Slots)
        {
            if (!Slot.PeakAttachment) continue;
            if (Result)
                throw new InvalidOperationException("The published map layout contains multiple Peak handlers.");
            Result = Slot.PeakAttachment!.GetComponent<PeakHandler>()
                ?? throw new InvalidOperationException("The Peak attachment lost its PeakHandler owner.");
        }
        return Result;
    }

    internal static MapHandler.MapSegment[] BuildPublishedSegments(
        IReadOnlyList<MapSlot> Slots,
        IReadOnlyList<MapHandler.MapSegment> NativeSegments,
        HashSet<MapHandler.MapSegment> NativeCatalogSegments,
        MapHandler.MapSegment OriginalVoidSegment,
        bool OriginalVoidInitialized,
        OriginalMapSources.VoidSegmentPublication VoidPublication)
    {
        var FoundOriginalVoid = false;
        foreach (var Segment in NativeSegments)
        {
            if (ReferenceEquals(Segment, OriginalVoidSegment))
            {
                if (FoundOriginalVoid)
                    throw new InvalidOperationException("PEAK's original VOID segment ownership is ambiguous.");
                FoundOriginalVoid = true;
                continue;
            }
            if (!NativeCatalogSegments.Contains(Segment))
                throw new InvalidOperationException("PEAK's original VOID segment ownership is ambiguous.");
        }
        if (FoundOriginalVoid != OriginalVoidInitialized)
            throw new InvalidOperationException(
                "PEAK's Void initialization state does not match its original VOID segment.");
        var AppendOriginalVoid = VoidPublication is OriginalMapSources.VoidSegmentPublication.Retained;
        var Result = new MapHandler.MapSegment[Slots.Count + (AppendOriginalVoid ? 1 : 0)];
        for (var i = 0; i < Slots.Count; i++) Result[i] = Slots[i].Segment;
        if (AppendOriginalVoid) Result[Slots.Count] = OriginalVoidSegment;
        return Result;
    }
    static void ActivatePeakOwnerForEndCutscene()
    {
        var Handler = Singleton<MapHandler>.Instance;
        if (!Handler) throw new InvalidOperationException("PEAK's map handler is unavailable during the end cutscene.");
        if (!Map.HasPublishedLayout)
        {
            Handler.segments[(int)Segment.Caldera].segmentParent.transform.parent.gameObject.SetActive(true);
            return;
        }
        var Index = Handler.currentSegment;
        if (Index >= 0 && Index < Map.Slots.Length && Map.Slots[Index].Source is NadirLayoutSegmentSource
            && !Map.Slots[Index].Released && Map.Slots[Index].Container)
            Map.Slots[Index].Container.gameObject.SetActive(false);
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            if (!Map.Slots[i].PeakAttachment) continue;
            if (Map.Slots[i].Released || !Map.Slots[i].Container)
                throw new InvalidOperationException("The published map layout has no live Peak owner.");
            for (var Current = Map.Slots[i].PeakAttachment; Current && Current != Handler.transform; Current = Current.parent)
                Current.gameObject.SetActive(true);
            return;
        }
        var Peak = Singleton<PeakHandler>._instance
            ?? throw new InvalidOperationException("PEAK's native Peak handler is unavailable during the configured ending.");
        for (var Current = Peak.transform; Current && Current != Handler.transform; Current = Current.parent)
            Current.gameObject.SetActive(true);
    }

    internal static bool ReplaceNativeContainerActivation(
        List<CodeInstruction> Codes,
        bool Active,
        MethodInfo Replacement)
    {
        var Adapter = MapTransitionAdapter.Instance;
        var Found = -1;
        for (var i = 0; i + 9 < Codes.Count; i++)
        {
            if (!Codes[i].Calls(Adapter.MapHandlerSingletonGetter) || !Codes[i + 1].LoadsField(Adapter.SegmentsField)
                || !MapTransitionAdapter.LoadsInt(Codes[i + 2], (int)Segment.Caldera) || Codes[i + 3].opcode != OpCodes.Ldelem_Ref
                || !Codes[i + 4].Calls(Adapter.SegmentParentGetter) || !Codes[i + 5].Calls(Adapter.GameObjectTransformGetter)
                || !Codes[i + 6].Calls(Adapter.TransformParentGetter) || !Codes[i + 7].Calls(Adapter.ComponentGameObjectGetter)
                || !MapTransitionAdapter.LoadsBoolean(Codes[i + 8], out var Value) || Value != Active
                || !Codes[i + 9].Calls(Adapter.GameObjectSetActive)) continue;
            if (Found >= 0)
                throw new AmbiguousMatchException("PEAK's native Caldera-container activation site was ambiguous.");
            Found = i;
        }
        if (Found < 0) return false;
        Codes[Found].opcode = OpCodes.Call;
        Codes[Found].operand = Replacement;
        Codes.RemoveRange(Found + 1, 9);
        return true;
    }

    [HarmonyPatch]
    static class PeakEndCutscenePatch
    {
        static readonly MethodInfo Target = AccessTools.DeclaredMethod(typeof(PeakHandler), "PrepareEndCutscene", Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(PeakHandler).FullName, "PrepareEndCutscene");

        static MethodBase TargetMethod() => Target;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var Replacement = ((Action)ActivatePeakOwnerForEndCutscene).Method;
            if (!ReplaceNativeContainerActivation(Codes, true, Replacement))
                throw new MissingMethodException("PeakHandler.PrepareEndCutscene's native container activation was not found.");
            return Codes;
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.ForceEnablePeak))]
    static class ForceEnablePeakPatch
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            if (!Map.HasPublishedLayout) return true;
            ActivatePeakOwnerForEndCutscene();
            return false;
        }
    }
}
