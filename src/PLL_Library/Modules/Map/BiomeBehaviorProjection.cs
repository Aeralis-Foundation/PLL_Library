using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace PLL.Library;

internal static class BiomeBehaviorProjection
{
    static readonly MethodInfo GetCurrentBiome = AccessTools.DeclaredMethod(
        typeof(MapHandler), nameof(MapHandler.GetCurrentBiome), Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(MapHandler).FullName, nameof(MapHandler.GetCurrentBiome));
    static readonly MethodInfo GetBiomeForSegment = AccessTools.DeclaredMethod(
        typeof(MapHandler), nameof(MapHandler.GetBiomeForSegment), new[] { typeof(int) })
        ?? throw new MissingMethodException(typeof(MapHandler).FullName, nameof(MapHandler.GetBiomeForSegment));
    static readonly MethodInfo MarkBiomeSkipped = AccessTools.DeclaredMethod(
        typeof(MountainProgressHandler), nameof(MountainProgressHandler.MarkBiomeSkipped),
        new[] { typeof(Biome.BiomeType) })
        ?? throw new MissingMethodException(
            typeof(MountainProgressHandler).FullName, nameof(MountainProgressHandler.MarkBiomeSkipped));
    static readonly MethodInfo ClosestCampfireBiome = AccessTools.DeclaredMethod(
        typeof(global::Campfire), nameof(global::Campfire.GetClosestCampfireNextBiome), new[] { typeof(Vector3) })
        ?? throw new MissingMethodException(
            typeof(global::Campfire).FullName, nameof(global::Campfire.GetClosestCampfireNextBiome));

    static bool LoadsInteger(CodeInstruction Instruction) => Instruction.opcode == OpCodes.Ldc_I4
        || Instruction.opcode == OpCodes.Ldc_I4_S || Instruction.opcode == OpCodes.Ldc_I4_0
        || Instruction.opcode == OpCodes.Ldc_I4_1 || Instruction.opcode == OpCodes.Ldc_I4_2
        || Instruction.opcode == OpCodes.Ldc_I4_3 || Instruction.opcode == OpCodes.Ldc_I4_4
        || Instruction.opcode == OpCodes.Ldc_I4_5 || Instruction.opcode == OpCodes.Ldc_I4_6
        || Instruction.opcode == OpCodes.Ldc_I4_7 || Instruction.opcode == OpCodes.Ldc_I4_8
        || Instruction.opcode == OpCodes.Ldc_I4_M1;

    static bool CurrentBehaviorMatchesBiome(Biome.BiomeType Expected)
    {
        var Source = Map.CurrentSource;
        if (Source is not null)
            return LayoutSegmentBehavior.Resolve(Source)?.Biome == Expected;
        var Handler = Singleton<MapHandler>.Instance;
        return Handler && Handler.GetCurrentBiome() == Expected;
    }

    static Biome.BiomeType ClosestBehaviorBiome(Vector3 Position)
    {
        var Role = (Segment)global::Campfire.GetClosestCampfireNextSegment(Position);
        if (Map.HasPublishedLayout && NavigationRoles.TryTargetSlot(Role, out var Index))
            return LayoutSegmentBehavior.Resolve(Map.Slots[Index].Source)?.Biome
                ?? throw new InvalidOperationException(
                    $"Authored segment '{Map.Slots[Index].Source.Id}' reached a native guidebook biome query.");
        return global::Campfire.GetClosestCampfireNextBiome(Position);
    }

    static void MarkRevivalBehaviorSkipped(int RoleValue)
    {
        if (Map.HasPublishedLayout
            && NavigationRoles.TryTargetSlot((Segment)RoleValue, out var Index))
        {
            if (LayoutSegmentBehavior.Resolve(Map.Slots[Index].Source) is { } Compatibility)
                MountainProgressHandler.MarkBiomeSkipped(Compatibility.Biome);
            return;
        }
        MountainProgressHandler.MarkBiomeSkipped(
            MapHandler.GetBiomeForSegment(RoleValue));
    }

    [HarmonyPatch]
    static class CurrentBiomeComparisonPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(
                typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus),
                new[]
                {
                    typeof(CharacterAfflictions.STATUSTYPE), typeof(float), typeof(bool),
                    typeof(bool), typeof(bool), typeof(bool), typeof(bool)
                }) ?? throw new MissingMethodException(
                    typeof(CharacterAfflictions).FullName, nameof(CharacterAfflictions.AddStatus));
            yield return AccessTools.DeclaredMethod(
                typeof(AchievementManager), nameof(AchievementManager.LocalCharacterHitByTrap),
                Type.EmptyTypes) ?? throw new MissingMethodException(
                    typeof(AchievementManager).FullName,
                    nameof(AchievementManager.LocalCharacterHitByTrap));
            yield return AccessTools.DeclaredMethod(
                typeof(Action_ShowBinocularOverlay), "TestLookAtSun", Type.EmptyTypes)
                ?? throw new MissingMethodException(
                    typeof(Action_ShowBinocularOverlay).FullName, "TestLookAtSun");
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> Instructions,
            MethodBase __originalMethod)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var Replacement = ((Func<Biome.BiomeType, bool>)CurrentBehaviorMatchesBiome).Method;
            var Replaced = 0;
            for (var Index = 0; Index + 2 < Codes.Count; Index++)
            {
                if (!Codes[Index].Calls(GetCurrentBiome)
                    || !LoadsInteger(Codes[Index + 1])
                    || !MapTransitionAdapter.IsEqualBranch(Codes[Index + 2].opcode)
                        && !MapTransitionAdapter.IsNotEqualBranch(Codes[Index + 2].opcode)) continue;
                Codes[Index].opcode = OpCodes.Pop;
                Codes[Index].operand = null;
                var Branch = Codes[Index + 2];
                Codes.Insert(Index + 2, new CodeInstruction(OpCodes.Call, Replacement));
                if (MapTransitionAdapter.IsEqualBranch(Branch.opcode))
                    Branch.opcode = Branch.opcode == OpCodes.Beq_S ? OpCodes.Brtrue_S : OpCodes.Brtrue;
                else Branch.opcode = Branch.opcode == OpCodes.Bne_Un_S ? OpCodes.Brfalse_S : OpCodes.Brfalse;
                Replaced++;
                Index += 2;
            }
            var Expected = __originalMethod.DeclaringType == typeof(CharacterAfflictions) ? 3 : 1;
            if (Replaced != Expected)
                throw new MissingMethodException(
                    $"{__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} has {Replaced} "
                    + $"current-biome comparisons; expected {Expected}.");
            return Codes;
        }
    }

    [HarmonyPatch(typeof(Character), "RPCA_ReviveAtPosition")]
    static class RevivalSkipPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var Replacement = ((Action<int>)MarkRevivalBehaviorSkipped).Method;
            var Replaced = false;
            for (var Index = 1; Index < Codes.Count; Index++)
            {
                if (!Codes[Index - 1].Calls(GetBiomeForSegment)
                    || !Codes[Index].Calls(MarkBiomeSkipped)) continue;
                if (Replaced)
                    throw new AmbiguousMatchException(
                        "Character.RPCA_ReviveAtPosition biome skip was ambiguous.");
                Codes[Index - 1].opcode = OpCodes.Nop;
                Codes[Index - 1].operand = null;
                Codes[Index].opcode = OpCodes.Call;
                Codes[Index].operand = Replacement;
                Replaced = true;
            }
            if (!Replaced)
                throw new MissingMethodException(
                    "Character.RPCA_ReviveAtPosition biome skip was not found.");
            return Codes;
        }
    }

    [HarmonyPatch(typeof(CharacterStats), nameof(CharacterStats.Record))]
    static class AuthoredTimelinePatch
    {
        [HarmonyPrefix]
        static bool Prefix(CharacterStats __instance)
        {
            if (Map.CurrentSource is not CustomLayoutSegmentSource
                { Behavior: PllBiomeBehavior.Authored }) return true;
            __instance.tick = 0f;
            __instance.justDied = false;
            __instance.justRevived = false;
            __instance.justPassedOut = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Guidebook), nameof(Guidebook.GuidebookPageValid))]
    static class GuidebookBiomePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Guidebook __instance, Vector3 __0, ref bool __result)
        {
            if (!__instance.requireBiomeType || !Map.HasPublishedLayout) return true;
            var Role = (Segment)global::Campfire.GetClosestCampfireNextSegment(__0);
            if (!NavigationRoles.TryTargetSlot(Role, out var Index)
                || Map.Slots[Index].Source is not CustomLayoutSegmentSource Custom
                || PllBiomeBehaviorCompatibility.Resolve(Custom.Behavior).HasValue) return true;
            __result = false;
            return false;
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var RequiredBiome = AccessTools.DeclaredField(
                typeof(Guidebook), nameof(Guidebook.requiredBiome))
                ?? throw new MissingFieldException(
                    typeof(Guidebook).FullName, nameof(Guidebook.requiredBiome));
            var Replacement = ((Func<Vector3, Biome.BiomeType>)ClosestBehaviorBiome).Method;
            var Replaced = 0;
            for (var Index = 0; Index + 3 < Codes.Count; Index++)
            {
                if (!Codes[Index].Calls(ClosestCampfireBiome)
                    || Codes[Index + 1].opcode != OpCodes.Ldarg_0
                    || !Codes[Index + 2].LoadsField(RequiredBiome)
                    || !MapTransitionAdapter.IsEqualBranch(Codes[Index + 3].opcode)) continue;
                Codes[Index].opcode = OpCodes.Call;
                Codes[Index].operand = Replacement;
                Replaced++;
            }
            if (Replaced != 1)
                throw new MissingMethodException(
                    $"Guidebook.GuidebookPageValid has {Replaced} result biome calls; expected 1.");
            return Codes;
        }
    }
}
