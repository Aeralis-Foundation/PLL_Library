using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using pworld.Scripts.Extensions;
using UnityEngine;

namespace PLL.Library;

[HarmonyPatch(typeof(RopeBoneVisualizer), nameof(RopeBoneVisualizer.RenderInSpool))]
internal static class RopeSpoolFrame
{
    static readonly MethodInfo NativeRotation = ((Func<Vector3, Vector3, Quaternion>)ExtQuaternion.LookRotationPrioUp).Method;
    static readonly MethodInfo SegmentUp = ((Func<Quaternion, Vector3>)ExtQuaternion.GetUp).Method;
    static readonly MethodInfo WorldUp = AccessTools.PropertyGetter(typeof(Vector3), nameof(Vector3.up));
    static readonly MethodInfo Negate = AccessTools.DeclaredMethod(typeof(Vector3), "op_UnaryNegation", new[] { typeof(Vector3) });
    static readonly MethodInfo SegmentAt = AccessTools.PropertyGetter(typeof(List<RopeSyncData.SegmentData>), "Item");
    static readonly MethodInfo Terminal = ((Func<Vector3, Vector3, RopeBoneVisualizer, int, Quaternion>)TerminalRotation).Method;
    static readonly MethodInfo Visible = ((Func<Vector3, Vector3, RopeBoneVisualizer, int, Quaternion>)VisibleRotation).Method;

    static Quaternion TerminalRotation(Vector3 DesiredForward, Vector3 Up, RopeBoneVisualizer Visual, int Index)
    {
        var Forward = Vector3.ProjectOnPlane(DesiredForward, Up).normalized;
        if (Forward != Vector3.zero) return Quaternion.LookRotation(Forward, Up);
        var Previous = Visual.bones[Index - 1].rotation;
        return Quaternion.FromToRotation(Previous * Vector3.up, Up) * Previous;
    }

    static Quaternion VisibleRotation(Vector3 DesiredForward, Vector3 Up, RopeBoneVisualizer Visual, int Index)
    {
        var Forward = Vector3.ProjectOnPlane(DesiredForward, Up).normalized;
        return Quaternion.LookRotation(Forward != Vector3.zero ? Forward : Visual.fullPositionData[Index].rotation * Vector3.forward, Up);
    }

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions, MethodBase Original)
    {
        var Locals = Original.GetMethodBody()!.LocalVariables;
        if (Locals.Count < 4 || Locals[1].LocalType != typeof(int)
            || Locals[2].LocalType != typeof(Transform) || Locals[3].LocalType != typeof(Vector3))
            throw new MissingMethodException("RopeBoneVisualizer.RenderInSpool has a different native bone-index layout.");
        var Codes = new List<CodeInstruction>(Instructions);
        var TerminalIndex = -1; var VisibleIndex = -1;
        for (var i = 0; i < Codes.Count; i++)
        {
            if (!Codes[i].Calls(NativeRotation)) continue;
            if (i >= 4 && Codes[i - 1].Calls(Negate) && Codes[i - 2].opcode == OpCodes.Ldloc_3
                && Codes[i - 3].Calls(WorldUp) && Codes[i - 4].opcode == OpCodes.Ldloc_2 && TerminalIndex < 0)
                TerminalIndex = i;
            else if (i >= 7 && Codes[i - 1].Calls(Negate) && Codes[i - 2].Calls(SegmentUp)
                && Codes[i - 3].opcode == OpCodes.Ldfld
                && Codes[i - 3].operand is FieldInfo Field && Field.DeclaringType == typeof(RopeSyncData.SegmentData)
                && Field.Name == nameof(RopeSyncData.SegmentData.rotation) && Codes[i - 4].Calls(SegmentAt)
                && Codes[i - 5].opcode == OpCodes.Ldloc_1 && Codes[i - 6].opcode == OpCodes.Ldfld
                && Codes[i - 6].operand is FieldInfo Frames && Frames.DeclaringType == typeof(RopeBoneVisualizer)
                && Frames.Name == nameof(RopeBoneVisualizer.fullPositionData) && Codes[i - 7].opcode == OpCodes.Ldarg_0 && VisibleIndex < 0)
                VisibleIndex = i;
            else throw new MissingMethodException("RopeBoneVisualizer.RenderInSpool has a different native rotation callsite.");
        }
        if (TerminalIndex < 0 || VisibleIndex <= TerminalIndex)
            throw new MissingMethodException("RopeBoneVisualizer.RenderInSpool needs its native terminal and visible rotation callsites.");
        Replace(VisibleIndex, Visible); Replace(TerminalIndex, Terminal);
        return Codes;

        void Replace(int Index, MethodInfo Rotate)
        {
            var LoadVisual = new CodeInstruction(OpCodes.Ldarg_0);
            LoadVisual.labels.AddRange(Codes[Index].labels); Codes[Index].labels.Clear();
            LoadVisual.blocks.AddRange(Codes[Index].blocks); Codes[Index].blocks.Clear();
            Codes.InsertRange(Index, new[] { LoadVisual, new CodeInstruction(OpCodes.Ldloc_1) });
            Codes[Index + 2].opcode = OpCodes.Call; Codes[Index + 2].operand = Rotate;
        }
    }
}
