using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class NativeSpiderPopulation
{
    sealed class Population
    {
        internal Population(MapRun Run, FeatureOccurrence Occurrence, SpiderManager Manager)
        {
            this.Run = Run;
            this.Occurrence = Occurrence;
            this.Manager = Manager;
        }

        internal MapRun Run { get; }
        internal FeatureOccurrence Occurrence { get; }
        internal SpiderManager Manager { get; }
    }

    static readonly ConditionalWeakTable<Transform, Population> Roots = new();
    static readonly ConditionalWeakTable<SpiderManager, Population> Managers = new();
    static readonly ConditionalWeakTable<Spider, Population> Registrations = new();

    internal static void Prepare(MapRun Run, FeatureOccurrence Occurrence)
    {
        var Found = Occurrence.Container.GetComponentsInChildren<SpiderManager>(true);
        if (Found.Length == 0) return;
        if (Found.Length != 1)
            throw new InvalidOperationException(
                "A cloned native spider occurrence must own exactly one SpiderManager.");
        var Population = new Population(Run, Occurrence, Found[0]);
        Population.Manager.spiders.Clear();
        Roots.Add(Occurrence.Container, Population);
        Managers.Add(Population.Manager, Population);
    }

    static SpiderManager RegistrationManager(Spider Spider)
    {
        for (var Parent = Spider.transform; Parent; Parent = Parent.parent)
            if (Roots.TryGetValue(Parent, out var Population))
            {
                Registrations.Add(Spider, Population);
                return Population.Manager;
            }
        return SpiderManager.instance;
    }

    [HarmonyPatch(typeof(SpiderManager), nameof(SpiderManager.Awake))]
    static class ManagerAwakePatch
    {
        [HarmonyPrefix]
        static bool Prefix(SpiderManager __instance) => !Managers.TryGetValue(__instance, out _);
    }

    [HarmonyPatch(typeof(SpiderManager), nameof(SpiderManager.Update))]
    static class ManagerUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(SpiderManager __instance) =>
            Managers.TryGetValue(__instance, out var Population)
                ? Population.Run.NativeSpawns.Ready(Population.Occurrence)
                : NativeSpawnAdmission.Ready(__instance);
    }

    [HarmonyPatch(typeof(Spider), nameof(Spider.Start))]
    static class SpiderStartPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            if (Codes.Count < 3 || Codes[0].opcode != OpCodes.Ldsfld
                || Codes[0].operand is not FieldInfo Field
                || Field.DeclaringType != typeof(SpiderManager)
                || Field.Name != nameof(SpiderManager.instance)
                || Codes[1].opcode != OpCodes.Ldarg_0
                || Codes[2].opcode != OpCodes.Callvirt
                || Codes[2].operand is not MethodInfo Register
                || Register.DeclaringType != typeof(SpiderManager)
                || Register.Name != nameof(SpiderManager.Register))
                throw new MissingMethodException("Spider.Start no longer begins with native manager registration.");
            Codes[0].opcode = OpCodes.Ldarg_0;
            Codes[0].operand = null;
            Codes.Insert(1, new CodeInstruction(OpCodes.Call,
                ((Func<Spider, SpiderManager>)RegistrationManager).Method));
            return Codes;
        }
    }

    [HarmonyPatch(typeof(Spider), nameof(Spider.OnDestroy))]
    static class SpiderDestroyPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Spider __instance)
        {
            if (!Registrations.TryGetValue(__instance, out var Population)) return true;
            Registrations.Remove(__instance);
            Population.Manager.Unregister(__instance);
            return false;
        }
    }
}
