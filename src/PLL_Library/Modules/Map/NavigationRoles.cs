using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Zorro.Core;

namespace PLL.Library;

internal static class NavigationRoles
{
    static ReadOnlySpan<Segment> SpecialRoles => [Segment.Peak, Segment.Void];
    static readonly MethodInfo CharacterSpawnerNativeRoleConsumerMethod = BindCharacterSpawnerNativeRoleConsumer();
    static readonly MethodInfo BaseCampScoutStatueGetter = AccessTools.PropertyGetter(
        typeof(MapHandler), nameof(MapHandler.BaseCampScoutStatue))
        ?? throw new MissingMethodException(typeof(MapHandler).FullName, "BaseCampScoutStatue.get");
    static readonly MethodInfo RespawnChestSegmentNumberGetter = AccessTools.PropertyGetter(
        typeof(RespawnChest), nameof(RespawnChest.SegmentNumber))
        ?? throw new MissingMethodException(typeof(RespawnChest).FullName, "SegmentNumber.get");
    static readonly MethodInfo CurrentSegmentNumberGetter = AccessTools.PropertyGetter(
        typeof(MapHandler), nameof(MapHandler.CurrentSegmentNumber))
        ?? throw new MissingMethodException(typeof(MapHandler).FullName, "CurrentSegmentNumber.get");

    internal static int MaximumOrdinarySegments =>
        PllLayoutCapacityContract.MaximumOrdinarySegments;

    internal static Segment Encode(int Index)
    {
        if (Index < 0 || Index >= MaximumOrdinarySegments)
            throw new InvalidOperationException(
                $"A map layout cannot encode ordinary segment index {Index} around PEAK's special segment roles.");
        var Value = Index;
        foreach (var SpecialRole in SpecialRoles)
            if (Value >= (int)SpecialRole) Value++;
        return (Segment)Value;
    }

    internal static bool TryDecode(Segment Role, out int Index)
    {
        var Value = (int)Role;
        Index = Value;
        foreach (var SpecialRole in SpecialRoles)
        {
            var SpecialValue = (int)SpecialRole;
            if (Value == SpecialValue)
            {
                Index = -1;
                return false;
            }
            if (Value > SpecialValue) Index--;
        }
        return Index >= 0 && Index < MaximumOrdinarySegments;
    }

    internal static bool TryTargetSlot(Segment Requested, out int Index)
    {
        if (TryDecode(Requested, out Index) && Index < Map.Slots.Length) return true;
        Index = -1;
        return false;
    }

    internal static bool TargetHasNativeRole(Segment Requested, Segment Role) =>
        !Map.HasPublishedLayout
            ? Requested == Role
            : TryTargetSlot(Requested, out var Index)
                && LayoutSegmentBehavior.Resolve(Map.Slots[Index].Source)?.Role == Role;

    internal static Segment CurrentNativeRole(MapHandler Handler)
    {
        var Index = Handler.currentSegment;
        if (Map.HasPublishedLayout && Index >= 0 && Index < Map.Slots.Length)
            return LayoutSegmentBehavior.Resolve(Map.Slots[Index].Source)?.Role
                ?? throw new InvalidOperationException(
                    $"Authored segment '{Map.Slots[Index].Source.Id}' reached a native-only role consumer.");
        if (NadirRuntime.CurrentSegmentIsVoidController(Handler)) return Segment.Void;
        return Map.LogicalSegment(Handler);
    }

    static Segment CurrentNativeRole()
    {
        var Handler = Singleton<MapHandler>.Instance;
        return Handler ? CurrentNativeRole(Handler) : Segment.Beach;
    }

    internal static Segment Previous(Segment Role)
    {
        if (!Map.HasPublishedLayout) return (Segment)((int)Role - 1);
        if (Role == Segment.Void)
            return Map.Slots.Length != 0
                ? Map.Slots[Map.Slots.Length - 1].NavigationRole
                : throw new InvalidOperationException("PEAK's special Nadir has no previous published slot.");
        if (!TryDecode(Role, out var Index) || Index <= 0 || Index >= Map.Slots.Length)
            throw new InvalidOperationException($"Map segment role {Role} has no previous published slot.");
        return Map.Slots[Index - 1].NavigationRole;
    }

    internal static Segment LogicalRole(Segment Role)
    {
        if (!Map.HasPublishedLayout) return Role;
        for (var i = 0; i < Map.Slots.Length; i++)
        {
            if (Map.Slots[i].Released
                || LayoutSegmentBehavior.Resolve(Map.Slots[i].Source)?.Role != Role) continue;
            return Map.Slots[i].NavigationRole;
        }
        throw new InvalidOperationException($"Mini-run segment {Role} is absent from the map layout.");
    }

    internal static int MiniRunStart(RunSettings.SETTINGTYPE Setting, bool Local)
    {
        var Role = RunSettings.GetValue(Setting, Local);
        if (!Map.HasPublishedLayout) return Role;
        var NavigationRole = LogicalRole((Segment)Role);
        if (!TryDecode(NavigationRole, out var Index))
            throw new InvalidOperationException($"Mini-run segment {Role} has no logical layout index.");
        if (Index != Map.Index) MapHandler.JumpToSegment(NavigationRole);
        return 0;
    }

    internal static void ReplaceBaseCampSegmentQuery(List<CodeInstruction> Codes, string Consumer)
    {
        var Replacement = ((Func<RespawnChest?, Segment>)SpawnTrackerTransaction.BaseCampSegment).Method;
        var Replaced = false;
        for (var i = 1; i < Codes.Count; i++)
        {
            if (!Codes[i - 1].Calls(BaseCampScoutStatueGetter)
                || !Codes[i].Calls(RespawnChestSegmentNumberGetter)) continue;
            if (Replaced)
                throw new AmbiguousMatchException($"{Consumer}'s base-camp segment query was ambiguous.");
            Codes[i].opcode = OpCodes.Call;
            Codes[i].operand = Replacement;
            Replaced = true;
        }
        if (!Replaced)
            throw new MissingMethodException($"{Consumer}'s base-camp segment query was not found.");
    }

    static bool CurrentBehaviorMatches(Segment Expected)
    {
        var Handler = Singleton<MapHandler>.Instance;
        if (!Handler) return false;
        if (!Map.HasPublishedLayout || Map.Owner != Handler)
            return Handler.GetCurrentSegment() == Expected;
        var Index = Handler.currentSegment;
        return Index >= 0 && Index < Map.Slots.Length
            && LayoutSegmentBehavior.Resolve(Map.Slots[Index].Source)?.Role == Expected;
    }

    static bool NativeConsumerMayRun(MethodBase Method)
    {
        if (Map.CurrentSource is not CustomLayoutSegmentSource Custom) return true;
        if (Method.DeclaringType == typeof(Peak.ForceFogShaderHeight)) return false;
        return PllBiomeBehaviorCompatibility.Resolve(Custom.Behavior).HasValue;
    }

    static void ReplaceCharacterSpawnerBehaviorMatch(List<CodeInstruction> Codes)
    {
        var Replacement = ((Func<Segment, bool>)CurrentBehaviorMatches).Method;
        var Replaced = false;
        for (var Index = 1; Index + 2 < Codes.Count; Index++)
        {
            if (!Codes[Index].Calls(MapTransitionAdapter.Instance.GetCurrentSegment)
                || !MapTransitionAdapter.IsEqualBranch(Codes[Index + 2].opcode)
                    && !MapTransitionAdapter.IsNotEqualBranch(Codes[Index + 2].opcode)) continue;
            if (Replaced)
                throw new AmbiguousMatchException(
                    "CharacterSpawner's mini-run behavior comparison was ambiguous.");
            Codes[Index].opcode = OpCodes.Pop;
            Codes[Index].operand = null;
            var Branch = Codes[Index + 2];
            Codes.Insert(Index + 2, new CodeInstruction(OpCodes.Call, Replacement));
            if (MapTransitionAdapter.IsEqualBranch(Branch.opcode))
                Branch.opcode = Branch.opcode == OpCodes.Beq_S ? OpCodes.Brtrue_S : OpCodes.Brtrue;
            else Branch.opcode = Branch.opcode == OpCodes.Bne_Un_S ? OpCodes.Brfalse_S : OpCodes.Brfalse;
            Replaced = true;
            Index += 2;
        }
        if (!Replaced)
            throw new MissingMethodException(
                "CharacterSpawner's mini-run behavior comparison was not found.");
    }

    static MethodInfo BindCharacterSpawnerNativeRoleConsumer()
    {
        var Update = AccessTools.DeclaredMethod(
            typeof(CharacterSpawner), nameof(CharacterSpawner.Update), Type.EmptyTypes)
            ?? throw new MissingMethodException(
                typeof(CharacterSpawner).FullName, nameof(CharacterSpawner.Update));
        MethodInfo? Target = null;
        foreach (var Instruction in PatchProcessor.GetOriginalInstructions(Update))
        {
            if (Instruction.opcode != OpCodes.Call && Instruction.opcode != OpCodes.Callvirt
                || Instruction.operand is not MethodInfo Candidate
                || Candidate.DeclaringType != typeof(CharacterSpawner) || Candidate.IsStatic
                || Candidate.ReturnType != typeof(void) || Candidate.GetParameters().Length != 0) continue;
            if (Target is not null)
                throw new AmbiguousMatchException(
                    "CharacterSpawner.Update calls multiple instance host consumers.");
            Target = Candidate;
        }
        return Target ?? throw new MissingMethodException(
            "CharacterSpawner.Update has no instance host consumer.");
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.GetCurrentSegment))]
    static class CurrentRolePatch
    {
        [HarmonyPostfix]
        static void Postfix(MapHandler __instance, ref Segment __result)
        {
            if (Map.HasPublishedLayout && Map.Owner == __instance) __result = Map.LogicalSegment(__instance);
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.CurrentSegmentNumber), MethodType.Getter)]
    static class CurrentSegmentNumberPatch
    {
        [HarmonyPostfix]
        static void Postfix(ref Segment __result)
        {
            var Handler = Map.Owner;
            if (Map.HasPublishedLayout && Handler) __result = Map.LogicalSegment(Handler);
        }
    }

    [HarmonyPatch(typeof(CharacterSpawner), "ScoutsWereRevivedAtCurrentBaseCamp", MethodType.Getter)]
    static class CustomRevivalBehaviorPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (!Map.HasPublishedLayout || Map.CurrentSource is not CustomLayoutSegmentSource Custom)
                return true;
            var Revived = MapHandler.BaseCampHasRevived;
            __result = PllBiomeBehaviorCompatibility.Resolve(Custom.Behavior)?.Role == Segment.TheKiln
                ? Revived && !LavaRising.LavaActive(Segment.TheKiln)
                : Revived;
            return false;
        }
    }

    [HarmonyPatch]
    static class NativeRoleConsumersPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(LavaRising), "Update", Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(LavaRising).FullName, "Update");
            yield return CharacterSpawnerNativeRoleConsumerMethod;
            yield return AccessTools.DeclaredMethod(typeof(Peak.ForceFogShaderHeight), "Update", Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(Peak.ForceFogShaderHeight).FullName, "Update");
            yield return GloomVoiceRuntime.UpdateMethod;
        }

        [HarmonyPrefix]
        static bool Prefix(MethodBase __originalMethod) =>
            __originalMethod == CharacterSpawnerNativeRoleConsumerMethod
            || NativeConsumerMayRun(__originalMethod);

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> Instructions,
            MethodBase __originalMethod)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            if (__originalMethod == CharacterSpawnerNativeRoleConsumerMethod)
            {
                ReplaceBaseCampSegmentQuery(Codes, "CharacterSpawner host update");
                ReplaceCharacterSpawnerBehaviorMatch(Codes);
                return Codes;
            }
            var Native = ((Func<MapHandler, Segment>)CurrentNativeRole).Method;
            var NativeStatic = ((Func<Segment>)CurrentNativeRole).Method;
            var Replaced = false;
            foreach (var Instruction in Codes)
            {
                if (Instruction.Calls(MapTransitionAdapter.Instance.GetCurrentSegment))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Native;
                    Replaced = true;
                }
                else if (Instruction.Calls(CurrentSegmentNumberGetter))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = NativeStatic;
                    Replaced = true;
                }
            }
            if (!Replaced)
                throw new MissingMethodException($"{__originalMethod.DeclaringType?.FullName}'s native-role query was not found.");
            return Codes;
        }
    }
}
