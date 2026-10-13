using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Peak;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace PLL.Library;

internal static class LightVolumeHarmonyAdapter
{
    static LightVolumeRun? ActiveRun;
    static LightVolumeBakeOperation? ActiveBake;

    internal static void Shutdown()
    {
        if (ActiveBake is not null || ActiveRun is not null)
            throw new InvalidOperationException(
                "The ray-tracing resource bundle cannot unload while a map run is active.");
        UnifiedRayTracingResources.Shutdown();
    }
    internal static void BindRun(LightVolumeRun Run)
    {
        if (ActiveRun is not null)
            throw new InvalidOperationException("Another map run still owns the canonical light volume.");
        ActiveRun = Run;
    }

    internal static void UnbindRun(LightVolumeRun Run)
    {
        if (ReferenceEquals(ActiveRun, Run)) ActiveRun = null;
    }

    internal static void BindBake(LightVolumeRun Run, LightVolumeBakeOperation Bake)
    {
        if (!ReferenceEquals(ActiveRun, Run))
            throw new InvalidOperationException("The canonical light volume belongs to another map run.");
        if (ActiveBake is not null)
            throw new InvalidOperationException("A light-volume bake is already active.");
        ActiveBake = Bake;
    }

    internal static void UnbindBake(LightVolumeBakeOperation Bake)
    {
        if (ReferenceEquals(ActiveBake, Bake)) ActiveBake = null;
    }

    static CommandBuffer TrackAuxiliaryCommandBuffer(CommandBuffer Buffer) =>
        ActiveBake?.TrackCommandBuffer(Buffer, RequiresFence: false) ?? Buffer;

    static void ExecuteOwnedAuxiliaryCommandBuffer(CommandBuffer Buffer)
    {
        if (ActiveBake is { } Operation)
            Operation.ExecuteCommandBuffer(Buffer, RequiresFence: false);
        else Graphics.ExecuteCommandBuffer(Buffer);
    }

    static IEnumerable<CodeInstruction> OwnCommandBuffer(
        IEnumerable<CodeInstruction> Instructions,
        MethodInfo TrackReplacement,
        MethodInfo ExecuteReplacement,
        string Owner)
    {
        var Constructor = AccessTools.Constructor(typeof(CommandBuffer), Type.EmptyTypes)
            ?? throw new MissingMethodException("CommandBuffer's constructor was not found.");
        var Execute = ((Action<CommandBuffer>)Graphics.ExecuteCommandBuffer).Method;
        var ConstructionCount = 0;
        var ExecuteCount = 0;
        foreach (var Instruction in Instructions)
        {
            if (Instruction.Calls(Execute))
            {
                Instruction.opcode = OpCodes.Call;
                Instruction.operand = ExecuteReplacement;
                ExecuteCount++;
            }
            yield return Instruction;
            if (Instruction.opcode != OpCodes.Newobj || !Equals(Instruction.operand, Constructor)) continue;
            yield return new CodeInstruction(OpCodes.Call, TrackReplacement);
            ConstructionCount++;
        }
        if (ConstructionCount != 1 || ExecuteCount != 1)
            throw new MissingMethodException($"{Owner} command-buffer shape changed.");
    }
    static void IterateBakeLights(
        global::LightVolume Volume,
        Func<BakedVolumeLight, bool> IsValid,
        Action<BakedVolumeLight> Action)
    {
        if (ActiveBake is { } Operation)
        {
            Operation.IterateLights(Volume, IsValid, Action);
            return;
        }
        Volume.IterateComponents(IsValid, Action);
    }

    static void IterateBakeRenderers(
        global::LightVolume Volume,
        Action<MeshRenderer> Action)
    {
        if (ActiveBake is { } Operation)
        {
            Operation.IterateRenderers(Volume, Action);
            return;
        }
        Volume.IterateRenderers(Action);
    }

    static Delegate? CombineMeshCleanup(Delegate? Existing, Delegate? Added) =>
        ActiveBake is { } Bake && Added?.Target is ComputeRayTracingAccelStruct Acceleration
            && Bake.OwnsAcceleration(Acceleration) ? Existing : Delegate.Combine(Existing, Added);

    static void DisposeNativeBakeContext(RayTracingContext Context)
    {
        if (ActiveBake?.OwnsContext(Context) != true) Context.Dispose();
    }

    [HarmonyPatch]
    static class NativeBakeContextDisposePatch
    {
        static readonly MethodBase MoveNext;
        static readonly MethodInfo Dispose, Replacement = ((Action<RayTracingContext>)DisposeNativeBakeContext).Method;

        static NativeBakeContextDisposePatch()
        {
            var Bake = AccessTools.DeclaredMethod(typeof(global::LightVolume), nameof(global::LightVolume.Bake), new[] { typeof(bool) })
                ?? throw new MissingMethodException(typeof(global::LightVolume).FullName, nameof(global::LightVolume.Bake));
            var StateMachine = Bake.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
                ?? throw new MissingMethodException("LightVolume.Bake has no async state machine.");
            MoveNext = AccessTools.DeclaredMethod(StateMachine, nameof(IAsyncStateMachine.MoveNext), Type.EmptyTypes)
                ?? throw new MissingMethodException(StateMachine.FullName, nameof(IAsyncStateMachine.MoveNext));
            Dispose = AccessTools.DeclaredMethod(typeof(RayTracingContext), nameof(RayTracingContext.Dispose), Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(RayTracingContext).FullName, nameof(RayTracingContext.Dispose));
        }

        static MethodBase TargetMethod() => MoveNext;

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Replaced = 0;
            foreach (var Instruction in Instructions)
            {
                if (Instruction.Calls(Dispose))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Replacement;
                    Replaced++;
                }
                yield return Instruction;
            }
            if (Replaced != 1)
                throw new MissingMethodException($"LightVolume.Bake contained {Replaced} context disposal calls instead of 1.");
        }
    }
    [HarmonyPatch(typeof(global::LightVolume), nameof(global::LightVolume.Bake))]
    static class BakePatch
    {
        [HarmonyPrefix]
        static void Prefix(global::LightVolume __instance)
        {
            if (ActiveRun is null) return;
            if (ActiveBake is null || !ActiveBake.ConsumeBakeAuthorization(__instance))
                throw new InvalidOperationException("Another light-volume bake started during map publication.");
        }
    }

    [HarmonyPatch(typeof(RayTracingResources), nameof(RayTracingResources.LoadFromRenderPipelineResources))]
    static class RayTracingResourcesPatch
    {
        [HarmonyPostfix]
        static void Postfix(RayTracingResources __instance, ref bool __result)
        {
            if (__result || ActiveBake?.NeedsComputeResources != true) return;
            UnifiedRayTracingResources.Populate(__instance);
            __result = true;
        }
    }

    [HarmonyPatch(typeof(RayTracingContext), MethodType.Constructor,
        typeof(RayTracingBackend), typeof(RayTracingResources))]
    static class RayTracingContextPatch
    {
        [HarmonyPostfix]
        static void Postfix(RayTracingContext __instance) => ActiveBake?.TryTrackContext(__instance);
    }

    [HarmonyPatch(typeof(ComputeRayTracingBackend),
        nameof(ComputeRayTracingBackend.CreateAccelerationStructure),
        typeof(AccelerationStructureOptions), typeof(ReferenceCounter))]
    static class ComputeAccelerationPatch
    {
        [HarmonyPrefix]
        static bool Prefix(
            ComputeRayTracingBackend __instance,
            AccelerationStructureOptions options,
            ReferenceCounter counter,
            ref IRayTracingAccelStruct __result)
        {
            if (ActiveBake is not { } Operation
                || !Operation.TryCreateComputeAcceleration(
                    __instance, options, counter, out var Created)) return true;
            __result = Created!;
            return false;
        }
    }

    [HarmonyPatch(typeof(ComputeRayTracingAccelStruct),
        nameof(ComputeRayTracingAccelStruct.Build),
        typeof(CommandBuffer), typeof(GraphicsBuffer))]
    static class ComputeAccelerationBuildPatch
    {
        [HarmonyPrefix]
        static void Prefix(ComputeRayTracingAccelStruct __instance) =>
            ActiveBake?.TryRequireTopLevelAllocation(__instance);
    }
    [HarmonyPatch(typeof(global::LightVolume), "Create3DTexture")]
    static class Create3DTexturePatch
    {
        [HarmonyPrefix]
        static void Prefix() => ActiveBake?.RequireVolumeTextureAllocation();

        [HarmonyPostfix]
        static void Postfix(RenderTexture __result) => ActiveBake?.TrackVolumeTexture(__result);
    }

    [HarmonyPatch(typeof(RayTracingHelper), nameof(RayTracingHelper.CreateScratchBufferForBuild))]
    static class BuildScratchPatch
    {
        [HarmonyPrefix]
        static void Prefix() => ActiveBake?.TryRequireBuildScratchAllocation();
    }

    [HarmonyPatch(typeof(RayTracingHelper), nameof(RayTracingHelper.CreateScratchBufferForTrace))]
    static class TraceScratchPatch
    {
        [HarmonyPrefix]
        static void Prefix(uint dispatchWidth, uint dispatchHeight, uint dispatchDepth) =>
            ActiveBake?.TryRequireTraceScratchAllocation(
                dispatchWidth, dispatchHeight, dispatchDepth);
    }

    [HarmonyPatch(typeof(global::LightVolume), nameof(global::LightVolume.BuildLights))]
    static class BuildLightsInventoryPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var GenericIteration = AccessTools.DeclaredMethod(
                    typeof(global::LightVolume), "IterateComponents")
                ?.MakeGenericMethod(typeof(BakedVolumeLight))
                ?? throw new MissingMethodException(typeof(global::LightVolume).FullName, "IterateComponents");
            var Replacement = ((Action<global::LightVolume,
                Func<BakedVolumeLight, bool>, Action<BakedVolumeLight>>)IterateBakeLights).Method;
            var Replaced = 0;
            foreach (var Instruction in Instructions)
            {
                if (Instruction.Calls(GenericIteration))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Replacement;
                    Replaced++;
                }
                yield return Instruction;
            }
            if (Replaced != 1)
                throw new MissingMethodException(
                    $"LightVolume.BuildLights contained {Replaced} supported component iterations instead of 1.");
        }
    }

    [HarmonyPatch(typeof(global::LightVolume), nameof(global::LightVolume.BuildMeshes))]
    static class BuildMeshesInventoryPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Iteration = AccessTools.DeclaredMethod(
                    typeof(global::LightVolume), "IterateRenderers",
                    new[] { typeof(Action<MeshRenderer>) })
                ?? throw new MissingMethodException(typeof(global::LightVolume).FullName, "IterateRenderers");
            var Replacement = ((Action<global::LightVolume,
                Action<MeshRenderer>>)IterateBakeRenderers).Method;
            var Combine = ((Func<Delegate?, Delegate?, Delegate?>)Delegate.Combine).Method;
            var CombineReplacement = ((Func<Delegate?, Delegate?, Delegate?>)CombineMeshCleanup).Method;
            var Replaced = 0;
            var Combined = 0;
            foreach (var Instruction in Instructions)
            {
                if (Instruction.Calls(Iteration))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Replacement;
                    Replaced++;
                }
                else if (Instruction.Calls(Combine))
                {
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = CombineReplacement;
                    Combined++;
                }
                yield return Instruction;
            }
            if (Replaced != 1)
                throw new MissingMethodException(
                    $"LightVolume.BuildMeshes contained {Replaced} supported renderer iterations instead of 1.");
            if (Combined != 2)
                throw new MissingMethodException(
                    $"LightVolume.BuildMeshes contained {Combined} supported cleanup combinations instead of 2.");
        }
    }

    [HarmonyPatch(typeof(global::LightVolume), nameof(global::LightVolume.RunBake))]
    static class RunBakePatch
    {
        [HarmonyPrefix]
        static bool Prefix(
            global::LightVolume __instance, RayTracingContext context,
            RenderTexture target, ref Awaitable __result)
        {
            if (ActiveBake is not { } Operation || ActiveRun?.Owns(__instance) != true) return true;
            __result = Operation.RunBake(context, target);
            return false;
        }
    }

    [HarmonyPatch(typeof(BLASPositionsPool), nameof(BLASPositionsPool.Add),
        new[] { typeof(VertexBufferChunk), typeof(BlockAllocator.Allocation) },
        new[] { ArgumentType.Normal, ArgumentType.Ref })]
    static class PositionCopyCommandBufferPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions) =>
            OwnCommandBuffer(
                Instructions,
                ((Func<CommandBuffer, CommandBuffer>)TrackAuxiliaryCommandBuffer).Method,
                ((Action<CommandBuffer>)ExecuteOwnedAuxiliaryCommandBuffer).Method,
                "BLASPositionsPool.Add");
    }
    [HarmonyPatch(typeof(GraphicsHelpers), nameof(GraphicsHelpers.CopyBuffer),
        typeof(ComputeShader), typeof(GraphicsBuffer), typeof(int),
        typeof(GraphicsBuffer), typeof(int), typeof(int))]
    static class BufferCopyCommandBufferPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions) =>
            OwnCommandBuffer(
                Instructions,
                ((Func<CommandBuffer, CommandBuffer>)TrackAuxiliaryCommandBuffer).Method,
                ((Action<CommandBuffer>)ExecuteOwnedAuxiliaryCommandBuffer).Method,
                "GraphicsHelpers.CopyBuffer");
    }
    [HarmonyPatch(typeof(global::LightVolume), "LoadLightMapData")]
    static class LoadLightMapDataPatch
    {
        [HarmonyPostfix]
        static void Postfix(global::LightVolume __instance)
        {
            if (ActiveBake is { } Operation && ActiveRun?.Owns(__instance) == true)
                Operation.PublishPreviousGlobals();
        }
    }

    [HarmonyPatch(typeof(CompressableLightMap), MethodType.Constructor, typeof(NativeArray<byte>),
        typeof(RenderTextureFormat), typeof(int3), typeof(Vector3), typeof(Vector3))]
    static class ConstructPatch
    {
        [HarmonyPrefix]
        static void Prefix(int3 __2) => ActiveBake?.TryRequireReadableMapAllocation(__2);

        [HarmonyPostfix]
        static void Postfix() => ActiveBake?.PublishPreviousGlobals();
    }

    [HarmonyPatch(typeof(global::LightVolume), nameof(global::LightVolume.SaveTex))]
    static class SaveTexPatch
    {
        [HarmonyPrefix]
        static bool Prefix(
            global::LightVolume __instance, RenderTexture renderTexture,
            ref Awaitable __result)
        {
            if (ActiveBake is not { } Operation || ActiveRun?.Owns(__instance) != true) return true;
            __result = Operation.SaveTex(renderTexture);
            return false;
        }
    }
    [HarmonyPatch(typeof(CompressableLightMap), nameof(CompressableLightMap.Decompress))]
    static class DecompressPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref CompressableLightMap __instance) =>
            !LightVolumeVisualMap.TryDecompress(ref __instance);
    }

    [HarmonyPatch(typeof(global::LightVolume), nameof(global::LightVolume.SamplePosition))]
    static class SamplePositionPatch
    {
        [HarmonyPrefix]
        static bool Prefix(global::LightVolume __instance, Vector3 worldPos, ref Color __result)
        {
            if (ActiveRun is { } Run && Run.Owns(__instance))
            {
                __result = Run.SampleCurrentProjection(worldPos);
                return false;
            }
            return !LightVolumeVisualMap.TrySampleCompressed(__instance, worldPos, out __result);
        }
    }

    [HarmonyPatch(typeof(global::LightVolume), "SetShaderVars")]
    static class SetShaderVarsPatch
    {
        [HarmonyPostfix]
        static void Postfix(global::LightVolume __instance)
        {
            if (ActiveBake is { } Operation && ActiveRun?.Owns(__instance) == true)
            {
                Operation.PublishPreviousGlobals();
                return;
            }
            if (ActiveRun is { } Run && Run.Owns(__instance) && Run.RepublishProjectionGlobals()) return;
            LightVolumeVisualMap.RepublishCompactGlobals(__instance);
        }
    }
}
