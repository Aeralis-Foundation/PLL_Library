using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace PLL.Library;

internal sealed class NativeModifierRuntime
{
    readonly (bool NightCold, bool MesaHeatstroke) Settings;
    float HeatElapsedSeconds;

    internal NativeModifierRuntime((bool NightCold, bool MesaHeatstroke) Settings) =>
        this.Settings = Settings;

    static void Apply(CharacterAfflictions Afflictions)
    {
        var Run = Map.CurrentRun;
        var Slot = Run?.ReadyCurrentSlot;
        if (Run is not null && Slot is null) return;
        if (Run is null && !Singleton<MountainProgressHandler>.Instance) return;
        var Modifiers = Slot?.Modifiers;
        var Custom = Slot?.CustomResolved is not null;
        if (Custom && Modifiers is null) return;
        var DayNight = DayNightManager.instance;
        if (!DayNight) return;
        var Heat = Modifiers?.Settings.MesaHeatstroke == true;
        if ((!Custom || Modifiers?.Settings.NightCold == true) && Ascents.isNightCold)
        {
            var Day = DayNight.isDay;
            if (Day < 0.5f)
            {
                Afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Cold,
                    Time.deltaTime * (1f - Day) * Ascents.nightColdRate);
                if (Heat && (!ReferenceEquals(Map.CurrentRun, Run)
                    || !ReferenceEquals(Run!.ReadyCurrentSlot, Slot)
                    || !Afflictions.character.IsLocal
                    || !DayNight || DayNightManager.instance != DayNight)) return;
            }
        }
        if (!Heat) return;
        var Sun = DayNight.sun;
        if (!Sun || Sun.intensity < 5f) return;
        var Center = Afflictions.character.Center;
        var Hit = HelperFunctions.LineCheck(
            Center - Sun.transform.forward * 1000f, Center,
            HelperFunctions.LayerType.AllPhysical, 0f, QueryTriggerInteraction.Ignore);
        if (Hit.transform && Hit.transform.root != Afflictions.character.transform.root) return;
        Modifiers!.HeatElapsedSeconds += Time.deltaTime;
        if (Modifiers.HeatElapsedSeconds <= 0.5f) return;
        Modifiers.HeatElapsedSeconds = 0f;
        Afflictions.AddSunHeat(0.01f);
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.UpdateNormalStatuses))]
    static class NightColdPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var Getter = AccessTools.PropertyGetter(typeof(Ascents), nameof(Ascents.isNightCold));
            var Start = Codes.FindIndex(Instruction => Instruction.Calls(Getter));
            if (Start < 0 || Start + 1 >= Codes.Count
                || Codes.FindIndex(Start + 1, Instruction => Instruction.Calls(Getter)) >= 0
                || Codes[Start + 1].opcode != OpCodes.Brfalse_S
                    && Codes[Start + 1].opcode != OpCodes.Brfalse
                || Codes[Start + 1].operand is not Label Exit)
                throw new InvalidOperationException("The native night cold branch is unsupported.");
            var End = Codes.FindIndex(Start + 2, Instruction => Instruction.labels.Contains(Exit));
            if (End <= Start + 2 || Codes[End - 1].opcode != OpCodes.Pop
                || !Codes[End - 2].Calls(SymbolExtensions.GetMethodInfo(() =>
                    default(CharacterAfflictions)!.AddStatus(default, default,
                        default, default, default, default, default))))
                throw new InvalidOperationException("The native night cold effect is unsupported.");
            Codes[Start].opcode = OpCodes.Ldarg_0;
            Codes[Start].operand = null;
            Codes.RemoveRange(Start + 1, End - Start - 1);
            Codes.Insert(Start + 1, new CodeInstruction(OpCodes.Call,
                SymbolExtensions.GetMethodInfo(() => Apply(null!))));
            return Codes;
        }
    }

    [HarmonyPatch(typeof(HotSun), nameof(HotSun.Update))]
    static class HotSunPatch
    {
        [HarmonyPrefix]
        static bool Prefix() => Map.CurrentRun is not { } Run
            || Run.ReadyCurrentSlot is { CustomResolved: null };
    }
}
