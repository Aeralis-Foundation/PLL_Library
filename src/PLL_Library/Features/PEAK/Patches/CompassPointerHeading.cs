using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

[HarmonyPatch(typeof(CompassPointer), nameof(CompassPointer.UpdateHeading))]
internal static class CompassPointerHeadingPatch
{
    static readonly MethodInfo NativeLookRotation =
        ((Func<Vector3, Vector3, Quaternion>)Quaternion.LookRotation).Method;
    static readonly MethodInfo RotationSetter = AccessTools.PropertySetter(
        typeof(Transform), nameof(Transform.rotation))
        ?? throw new MissingMethodException(typeof(Transform).FullName, "rotation.set");

    static Quaternion LookRotation(
        Vector3 Forward,
        Vector3 Up,
        CompassPointer Pointer) =>
        Forward == Vector3.zero
            ? Pointer.needle.rotation
            : Quaternion.LookRotation(Forward, Up);

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> Instructions)
    {
        var Codes = new List<CodeInstruction>(Instructions);
        var Replacement = ((Func<Vector3, Vector3, CompassPointer, Quaternion>)LookRotation).Method;
        var Match = -1;
        for (var Index = 0; Index + 1 < Codes.Count; Index++)
        {
            if (!Codes[Index].Calls(NativeLookRotation)) continue;
            if (Match >= 0)
                throw new AmbiguousMatchException(
                    "CompassPointer.UpdateHeading's look rotation was ambiguous.");
            if (!Codes[Index + 1].Calls(RotationSetter))
                throw new MissingMethodException(
                    "CompassPointer.UpdateHeading's look rotation assignment was not found.");
            Match = Index;
        }
        if (Match < 0)
            throw new MissingMethodException(
                "CompassPointer.UpdateHeading's look rotation was not found.");

        var LoadPointer = new CodeInstruction(OpCodes.Ldarg_0);
        LoadPointer.labels.AddRange(Codes[Match].labels);
        Codes[Match].labels.Clear();
        LoadPointer.blocks.AddRange(Codes[Match].blocks);
        Codes[Match].blocks.Clear();
        Codes.Insert(Match, LoadPointer);
        Codes[Match + 1].opcode = OpCodes.Call;
        Codes[Match + 1].operand = Replacement;
        return Codes;
    }
}
