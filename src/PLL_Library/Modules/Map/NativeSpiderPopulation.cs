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
        if (Roots.TryGetValue(Occurrence.Container, out _)) return;
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
        if (AuthoredPopulation(Spider) is { } Authored)
        {
            Registrations.Add(Spider, Authored);
            return Authored.Manager;
        }
        return SpiderManager.instance ? SpiderManager.instance : FallbackManager();
    }

    static Population? AuthoredPopulation(Spider Spider)
    {
        var Run = Map.CurrentRun;
        if (Run is null) return null;
        foreach (var Slot in Run.Slots)
        {
            var Occurrence = Slot?.Occurrence;
            if (Occurrence is null || !Occurrence.Container || !Spider.transform.IsChildOf(Occurrence.Container)) continue;
            if (Roots.TryGetValue(Occurrence.Container, out var Existing)) return Existing;
            var Host = new GameObject("PLL_SpiderManager");
            Host.SetActive(false);
            Host.transform.SetParent(Occurrence.Container, false);
            var Population = new Population(Run, Occurrence, Host.AddComponent<SpiderManager>());
            Roots.Add(Occurrence.Container, Population);
            Managers.Add(Population.Manager, Population);
            Host.SetActive(true);
            return Population;
        }
        return null;
    }

    static SpiderManager? fallbackManager;

    // A spider outside any map run with no PEAK manager in the scene: give it one rather than a null reference.
    static SpiderManager FallbackManager()
    {
        if (fallbackManager) return fallbackManager!;
        fallbackManager = new GameObject("PLL_FallbackSpiderManager").AddComponent<SpiderManager>();
        Plugin.PLLLog.LogInfo("No PEAK spider manager in this scene; PLL added one for its spiders.");
        return fallbackManager;
    }

    static readonly RaycastHit[] GroundHits = new RaycastHit[32];

    [HarmonyPatch(typeof(Spider), nameof(Spider.GetHit))]
    static class SpiderGroundPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Spider __instance, ref RaycastHit __result)
        {
            if (!Registrations.TryGetValue(__instance, out _) || !__instance.startPoint) return true;
            var Start = __instance.startPoint.position;
            var Count = Physics.SphereCastNonAlloc(Start, __instance.castRadius, Vector3.down, GroundHits,
                __instance.castDistance, HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysicalExceptDefault),
                QueryTriggerInteraction.Ignore);
            var Nearest = -1;
            for (var Index = 0; Index < Count; Index++)
            {
                var Hit = GroundHits[Index];
                if (!Hit.collider || Hit.distance <= 0f || Hit.collider.transform.IsChildOf(__instance.transform)) continue;
                if (Nearest < 0 || Hit.distance < GroundHits[Nearest].distance) Nearest = Index;
            }
            __result = Nearest >= 0 && Vector3.Distance(Start, GroundHits[Nearest].point) >= __instance.minRaycastDistance
                ? GroundHits[Nearest] : default;
            return false;
        }
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
