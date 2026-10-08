using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal static class MapPublicationAdapter
{
    [HarmonyPatch(typeof(RunManager), "Start")]
    static class RunStartPatch
    {
        [HarmonyPostfix]
        static void Postfix(ref IEnumerator __result)
        {
            if (!MapHandler.Exists) return;
            __result = MapPublication.BeforeRunStart(__result);
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.InitializeMap))]
    static class InitializeEffectsPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First + 1)]
        static void Prefix()
        {
            WeatherProjection.CaptureWeather();
            FogCutoutProjection.Capture();
            SpecialDayProjection.Capture();
            TerrainSplatProjection.Capture();
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        static void Postfix()
        {
            WeatherProjection.BindWeather();
            TerrainSplatProjection.Bind();
            FogCutoutProjection.Refresh();
            SpecialDayProjection.Refresh();
        }
    }

    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.InitializeMap))]
    [HarmonyPriority(Priority.First)]
    static class InitializeMapPatch
    {
        static void SetInitialSegment(RespawnChest? Statue, Segment Segment)
        {
            if (Statue) Statue.SegmentNumber = Segment;
        }

        static void AddReviveHandler(RespawnChest? Statue, Action<RespawnChest> Handler)
        {
            if (Statue) Statue.ReviveUsed += Handler;
        }

        static void SetActiveIfPresent(GameObject? Value, bool Active)
        {
            if (Value) Value.SetActive(Active);
        }


        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            var SetSegment = AccessTools.DeclaredMethod(typeof(RespawnChest), "set_SegmentNumber")
                ?? throw new MissingMethodException("RespawnChest.SegmentNumber setter was not found.");
            var AddRevive = AccessTools.DeclaredMethod(typeof(RespawnChest), "add_ReviveUsed")
                ?? throw new MissingMethodException("RespawnChest.ReviveUsed adder was not found.");
            var SetActive = AccessTools.DeclaredMethod(typeof(GameObject), nameof(GameObject.SetActive), new[] { typeof(bool) })
                ?? throw new MissingMethodException("GameObject.SetActive was not found.");
            var WallNext = AccessTools.DeclaredField(typeof(MapHandler.MapSegment), nameof(MapHandler.MapSegment.wallNext))
                ?? throw new MissingFieldException("MapHandler.MapSegment.wallNext was not found.");
            var SafeSegment = ((Action<RespawnChest?, Segment>)SetInitialSegment).Method;
            var SafeRevive = ((Action<RespawnChest?, Action<RespawnChest>>)AddReviveHandler).Method;
            var SafeActive = ((Action<GameObject?, bool>)SetActiveIfPresent).Method;
            var SegmentPatched = 0;
            var RevivePatched = 0;
            var WallPatched = 0;

            for (var i = 0; i < Codes.Count; i++)
            {
                if (Codes[i].Calls(SetSegment))
                {
                    Codes[i].opcode = OpCodes.Call;
                    Codes[i].operand = SafeSegment;
                    SegmentPatched++;
                    continue;
                }
                if (Codes[i].Calls(AddRevive))
                {
                    Codes[i].opcode = OpCodes.Call;
                    Codes[i].operand = SafeRevive;
                    RevivePatched++;
                    continue;
                }
                if (!Codes[i].Calls(SetActive)) continue;
                for (var j = Math.Max(0, i - 4); j < i; j++)
                {
                    if (!Equals(Codes[j].operand, WallNext)) continue;
                    Codes[i].opcode = OpCodes.Call;
                    Codes[i].operand = SafeActive;
                    WallPatched++;
                    break;
                }
            }

            if (SegmentPatched != 1 || RevivePatched != 1 || WallPatched != 1)
                throw new MissingMethodException("MapHandler.InitializeMap's first-segment initialization sites were not found.");
            return Codes;
        }

        [HarmonyFinalizer]
        static Exception? Finalizer(Exception? __exception)
            => MapPublication.FinalizeInitializeMap(__exception);
    }

    [HarmonyPatch(typeof(MapHandler), "ActivateWithoutMessageQueue")]
    static class HeavyActivationPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Codes = new List<CodeInstruction>(Instructions);
            if (PlacementExecution.ReplaceSetActive(Codes) == 0)
                throw new MissingMethodException("MapHandler.ActivateWithoutMessageQueue's activation site was not found.");
            return Codes;
        }

        [HarmonyFinalizer]
        static Exception? Finalizer(Exception? __exception)
        {
            if (__exception is not null) PhotonNetwork.IsMessageQueueRunning = true;
            return __exception;
        }
    }
}
