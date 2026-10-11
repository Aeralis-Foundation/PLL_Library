using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace PLL.Library;

internal static class FogRuntime
{
    readonly struct FogSphereSnapshot
    {
        internal FogSphereSnapshot(FogSphere Sphere)
        {
            this.Sphere = Sphere;
            Active = Sphere.gameObject.activeSelf;
            Enable = Sphere.ENABLE;
            Reveal = Sphere.REVEAL_AMOUNT;
        }

        internal FogSphere Sphere { get; }
        internal bool Active { get; }
        internal float Enable { get; }
        internal float Reveal { get; }
    }

    static OrbFogHandler? Owner;
    static OrbFogHandler? BoundOwner;
    static RunEnvironmentConfiguration? BoundConfiguration;
    static FogSphereOrigin[] CapturedOrigins = Array.Empty<FogSphereOrigin>();
    static FogSphereOrigin[] BoundOrigins = Array.Empty<FogSphereOrigin>();
    static FogSphereSnapshot[] Spheres = Array.Empty<FogSphereSnapshot>();
    static GameObject? ProjectionRoot;

    static void ClearBinding()
    {
        BoundOwner = null;
        BoundConfiguration = null;
        BoundOrigins = Array.Empty<FogSphereOrigin>();
    }

    static void PublishBinding(OrbFogHandler Handler, FogSphereOrigin[] Origins, RunEnvironmentConfiguration Configuration)
    {
        BoundOrigins = Origins;
        BoundOwner = Handler;
        BoundConfiguration = Configuration;
    }

    internal static void Capture(MapHandler MapOwner)
    {
        ClearBinding();
        var Handler = Singleton<OrbFogHandler>.Instance;
        if (!Handler || Handler.gameObject.scene != MapOwner.gameObject.scene)
            throw new InvalidOperationException("PEAK's native fog handler does not belong to the map scene.");
        Owner = Handler;
        var InitializedOrigins = Handler.origins;
        CapturedOrigins = InitializedOrigins is { Length: > 0 }
            ? (FogSphereOrigin[])InitializedOrigins.Clone()
            : Handler.transform.root.GetComponentsInChildren<FogSphereOrigin>();
        var Components = Handler.GetComponentsInChildren<FogSphere>(true);
        Spheres = Array.ConvertAll(Components, Component => new FogSphereSnapshot(Component));
    }

    internal static void Bind(MapSlot[] Slots, RunEnvironmentConfiguration Configuration)
    {
        if (Configuration is null) throw new ArgumentNullException(nameof(Configuration));
        ClearBinding();
        var Handler = Owner;
        if (!Handler)
            throw new InvalidOperationException("PEAK's captured fog handler is no longer available.");
        var Completed = false;
        try
        {
            if (ProjectionRoot) UnityEngine.Object.DestroyImmediate(ProjectionRoot);
            RestoreSpheres(Spheres);

            if (Configuration.Fogless)
            {
                ProjectionRoot = new GameObject("PLL_FoglessWall");
                ProjectionRoot.transform.SetParent(Handler.transform.root, false);
                ProjectionRoot.SetActive(false);
                var EmptyOrigins = Array.Empty<FogSphereOrigin>();
                Handler.origins = EmptyOrigins;
                foreach (var Slot in Slots)
                {
                    Slot.Segment.wallNext = ProjectionRoot;
                    Slot.Segment.wallPrevious = ProjectionRoot;
                }
                if (Handler.sphere)
                {
                    Handler.SetFogOrigin(Map.Index);
                    DisableSpheres(Handler);
                }
                PublishBinding(Handler, EmptyOrigins, Configuration);
                ApplyRisingFogState(Handler);
                Completed = true;
                return;
            }

            ProjectionRoot = new GameObject("PLL_FogOrigins");
            ProjectionRoot.transform.SetParent(Handler.transform.root, false);
            var BoundaryCount = Math.Max(0, Slots.Length - 1);
            var ProjectedOrigins = new FogSphereOrigin[BoundaryCount - (EndsWithPeakAttachmentNadir(Slots) ? 1 : 0)];
            if (Slots.Length == 1)
            {
                var Wall = new GameObject("PLL_WallNext_000");
                Wall.transform.SetParent(ProjectionRoot.transform, false);
                Wall.SetActive(false);
                Slots[0].Segment.wallNext = Wall;
            }
            var NextWalls = new GameObject[BoundaryCount];
            var PreviousWalls = new GameObject[BoundaryCount];
            for (var Index = 0; Index < BoundaryCount; Index++)
            {
                var Template = SelectTemplate(Slots, Index, out var Placed);
                if (Template.gameObject.GetComponents<FogSphereOrigin>().Length != 1)
                    throw new InvalidOperationException($"The {Slots[Index].Source.Id} fog origin is ambiguous.");
                var Clone = UnityEngine.Object.Instantiate(Template.gameObject, ProjectionRoot.transform, true);
                Clone.name = $"PLL_FogOrigin_{Index:D3}";
                var Origin = Clone.GetComponent<FogSphereOrigin>();
                if (Index < ProjectedOrigins.Length) ProjectedOrigins[Index] = Origin;
                NextWalls[Index] = RequireUniqueChild(Clone.transform, "Fogwall_Next");
                PreviousWalls[Index] = RequireUniqueChild(Clone.transform, "Fogwall_Prev");
                if (Slots[Index].PeakAttachment
                    || Slots[Index].Source is NadirLayoutSegmentSource
                    || Slots[Index].Source is NativeOrdinarySegmentSource { ContextualPeak: not null }
                        && !Slots[Index].PeakAttachment)
                {
                    var Exit = Slots[Index].PeakAttachment
                        ? PlacementPlan.PlacedPeakExitPose(Slots[Index]).position
                        : Slots[Index].PlacedExit;
                    var Delta = Exit - NextWalls[Index].transform.position;
                    Clone.transform.position += Delta;
                    var Frame = Slots[Index + 1].Frame;
                    if (Frame.Rotated)
                    {
                        var Pivot = NextWalls[Index].transform.position;
                        Clone.transform.SetPositionAndRotation(
                            Pivot + Frame.Direction(Clone.transform.position - Pivot),
                            Frame.Rotate(Clone.transform.rotation));
                    }
                    Origin.moveOnHeight += Delta.y;
                    Origin.moveOnForward += Delta.z;
                }
                else if (Slots[Index].Source is CustomLayoutSegmentSource)
                {
                    var Anchors = Slots[Index].CustomResolved?.AuthoringAnchors;
                    if (Anchors)
                    {
                        Origin.moveOnHeight = Anchors.FogBeginAnchor.position.y;
                        Origin.moveOnForward = Anchors.FogBeginAnchor.position.z;
                    }
                }
                else
                {
                    if (!Placed) Clone.transform.position += Slots[Index].Offset;
                    Origin.moveOnHeight += Slots[Index].Offset.y;
                    Origin.moveOnForward += Slots[Index].Offset.z;
                }
                Clone.SetActive(true);
            }

            for (var Index = 0; Index < BoundaryCount; Index++)
            {
                if (IsPeakZoneBoundary(Slots, Index))
                {
                    UnityEngine.Object.DestroyImmediate(NextWalls[Index]);
                    UnityEngine.Object.DestroyImmediate(PreviousWalls[Index]);
                    Slots[Index].Segment.wallNext = null!;
                    Slots[Index + 1].Segment.wallPrevious = null!;
                    continue;
                }
                if (Slots[Index].ProgressionBoundary != NativeProgressionBoundaryKind.None)
                {
                    NextWalls[Index].SetActive(false);
                    Slots[Index].Segment.wallNext = null!;
                }
                else Slots[Index].Segment.wallNext = NextWalls[Index];
                Slots[Index + 1].Segment.wallPrevious = PreviousWalls[Index];
            }

            Handler.origins = ProjectedOrigins;
            PublishBinding(Handler, ProjectedOrigins, Configuration);
            if (Handler.sphere) Handler.SetFogOrigin(Map.Index);
            Completed = true;
        }
        finally
        {
            if (!Completed)
            {
                ClearBinding();
                if (ProjectionRoot) UnityEngine.Object.DestroyImmediate(ProjectionRoot);
                Handler.origins = CapturedOrigins;
                ProjectionRoot = null;
            }
        }
    }

    static FogSphereOrigin SelectTemplate(IReadOnlyList<MapSlot> Slots, int Index, out bool Placed)
    {
        var Slot = Slots[Index];
        if (Slot.Source is CustomLayoutSegmentSource)
        {
            Placed = true;
            return Slot.CustomResolved?.Runtime.FogOrigin
                ?? throw new InvalidOperationException(
                    $"Custom segment '{Slot.Source.Id}' has no resolved clone-local fog origin.");
        }
        if (IsInternalCompositeBoundary(Slots, Index))
        {
            var NextSlot = Slots[Index + 1];
            var InternalTemplate = FogOriginAncestor(Slot.Segment.wallNext);
            if (FogOriginContract.IsNativeTemplate(InternalTemplate, true)
                && RequireUniqueChild(InternalTemplate!.transform, "Fogwall_Next")
                    == Slot.Segment.wallNext
                && RequireUniqueChild(InternalTemplate.transform, "Fogwall_Prev")
                    == NextSlot.Segment.wallPrevious)
            {
                Placed = true;
                return InternalTemplate;
            }
            InternalTemplate = FogOriginAncestor(Slot.Source.WallNext);
            if (FogOriginContract.IsNativeTemplate(InternalTemplate, true)
                && RequireUniqueChild(InternalTemplate!.transform, "Fogwall_Next")
                    == Slot.Source.WallNext
                && RequireUniqueChild(InternalTemplate.transform, "Fogwall_Prev")
                    == NextSlot.Source.WallPrevious)
            {
                Placed = false;
                return InternalTemplate;
            }
            throw new InvalidOperationException(
                $"The {Slot.Source.Id}/{NextSlot.Source.Id} internal fog boundary is invalid.");
        }
        var Template = FogOriginAncestor(Slot.Segment.wallNext);
        if (FogOriginContract.IsNativeTemplate(Template, false))
        {
            Placed = true;
            return Template!;
        }
        Template = FogOriginAncestor(Slot.Source.WallNext);
        if (FogOriginContract.IsNativeTemplate(Template, false))
        {
            Placed = false;
            return Template!;
        }
        if (UsesVisualFog(Slot))
        {
            Placed = false;
            return RequireEnabledNativeTemplate();
        }
        throw new InvalidOperationException($"The {Slot.Source.Id} fog origin was not found.");
    }

    static bool IsInternalCompositeBoundary(IReadOnlyList<MapSlot> Slots, int Index) =>
        Index + 1 < Slots.Count
        && ReferenceEquals(Slots[Index].Occurrence, Slots[Index + 1].Occurrence)
        && Slots[Index].Container == Slots[Index + 1].Container
        && NativeSegmentCatalog.IsCompositePair(Slots[Index].Source, Slots[Index + 1].Source);

    static bool UsesVisualFog(MapSlot Slot) =>
        Slot.PeakAttachment || Slot.Source is NadirLayoutSegmentSource
        || Slot.Source is NativeOrdinarySegmentSource { ContextualPeak: not null }
            && !Slot.PeakAttachment;

    static bool EndsWithPeakAttachmentNadir(IReadOnlyList<MapSlot> Slots) =>
        Slots.Count >= 2 && Slots[Slots.Count - 2].PeakAttachment
        && Slots[Slots.Count - 1].Source is NadirLayoutSegmentSource;

    static bool IsPeakZoneBoundary(IReadOnlyList<MapSlot> Slots, int Index) =>
        Slots[Index].ProgressionBoundary == NativeProgressionBoundaryKind.PeakBridge
        || Slots[Index].PeakAttachment
        || Slots[Index + 1].PeakAttachment
            && ReferenceEquals(Slots[Index].Occurrence, Slots[Index + 1].Occurrence);

    static FogSphereOrigin RequireEnabledNativeTemplate()
    {
        foreach (var Candidate in CapturedOrigins)
            if (FogOriginContract.IsNativeTemplate(Candidate, false)) return Candidate;
        throw new InvalidOperationException("PEAK's enabled native fog-wall template was not found.");
    }

    static bool SuppressesRisingFog(OrbFogHandler Handler)
    {
        if (NativeBaseMap.OwnsFog(Handler)) return true;
        if (BoundOwner != Handler || !Map.HasPublishedLayout) return false;
        if (BoundConfiguration?.Fogless == true) return true;
        var Index = Handler.currentID;
        if (Index < 0) return false;
        if (Index >= BoundOrigins.Length) return true;
        return Index < Map.Slots.Length
            && Map.Slots[Index].Source is CustomLayoutSegmentSource
                { Registration.FogMode: not PllFogMode.Normal };
    }

    static void ApplyRisingFogState(OrbFogHandler Handler)
    {
        if (NativeBaseMap.ApplyFogless(Handler)) return;
        if (BoundOwner != Handler || !Map.HasPublishedLayout) return;
        if (!SuppressesRisingFog(Handler))
        {
            RestoreSpheres(Spheres);
            return;
        }

        Handler.currentWaitTime = 0f;
        Handler.hasArrived = true;
        Handler.isMoving = false;
        if (Handler.syncCounter < 5f) Handler.syncCounter = 5f;
        foreach (var State in Spheres)
        {
            var Sphere = State.Sphere;
            if (!Sphere) continue;
            Sphere.ENABLE = 0f;
            Sphere.REVEAL_AMOUNT = 0f;
        }
        var CharacterValue = Character.localCharacter;
        if (CharacterValue && CharacterValue.data is { } CharacterData)
            CharacterData.isInFog = false;
        if (Handler.sphere) DisableSpheres(Handler);
    }

    static bool AllowRisingFog(OrbFogHandler Handler)
    {
        if (!SuppressesRisingFog(Handler)) return true;
        ApplyRisingFogState(Handler);
        return false;
    }

    static bool GameplayFogEnabled(float Value, float Expected)
    {
        if (!Mathf.Approximately(Value, Expected)) return false;
        if (NativeBaseMap.FoglessActive) return false;
        if (!BoundOwner || !Map.HasPublishedLayout) return true;
        var Index = BoundOwner.currentID;
        return !SuppressesRisingFog(BoundOwner)
            && (Index < 0 || Index >= Map.Slots.Length - 1
                || Map.Slots[Index].Source is not NadirLayoutSegmentSource);
    }

    static FogSphereOrigin? FogOriginAncestor(GameObject? Wall)
    {
        if (!Wall) return null;
        FogSphereOrigin? Result = null;
        for (var TransformValue = Wall.transform; TransformValue; TransformValue = TransformValue.parent)
        {
            var Origins = TransformValue.GetComponents<FogSphereOrigin>();
            if (Origins.Length > 1 || Origins.Length == 1 && Result)
                throw new InvalidOperationException(
                    $"The fog origin above {Wall.name} is ambiguous.");
            if (Origins.Length == 1) Result = Origins[0];
        }
        return Result;
    }

    static GameObject RequireUniqueChild(Transform Hierarchy, string Name)
    {
        Transform? Result = null;
        foreach (var Candidate in Hierarchy.GetComponentsInChildren<Transform>(true))
        {
            if (Candidate.name != Name) continue;
            if (Result)
                throw new InvalidOperationException(
                    $"The fog hierarchy contains more than one {Name}.");
            Result = Candidate;
        }
        return Result
            ? Result.gameObject
            : throw new InvalidOperationException($"The fog hierarchy contains no {Name}.");
    }

    internal static void Release(bool RestoreNative)
    {
        var Handler = Owner;
        var Origins = CapturedOrigins;
        var CapturedSpheres = Spheres;
        var Root = ProjectionRoot;

        ClearBinding();
        Owner = null;
        CapturedOrigins = Array.Empty<FogSphereOrigin>();
        Spheres = Array.Empty<FogSphereSnapshot>();
        ProjectionRoot = null;

        List<Exception>? Failures = null;
        if (Root) CleanupOperations.Capture(ref Failures, () => UnityEngine.Object.DestroyImmediate(Root));
        if (RestoreNative && Handler)
            CleanupOperations.Capture(ref Failures, () => Handler.origins = Origins);
        if (RestoreNative)
            CleanupOperations.Capture(ref Failures, () => RestoreSpheres(CapturedSpheres, RestoreScalars: true));
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Fog projection release failed.", Failures);
    }

    static void DisableSpheres(OrbFogHandler Handler)
    {
        foreach (var State in Spheres)
        {
            var Sphere = State.Sphere;
            if (Sphere && Sphere.gameObject != Handler.gameObject) Sphere.gameObject.SetActive(false);
        }
    }

    static void RestoreSpheres(FogSphereSnapshot[] CapturedSpheres, bool RestoreScalars = false)
    {
        List<Exception>? Failures = null;
        for (var Index = 0; Index < CapturedSpheres.Length; Index++)
            try
            {
                var State = CapturedSpheres[Index];
                if (!State.Sphere) continue;
                if (RestoreScalars)
                {
                    State.Sphere.ENABLE = State.Enable;
                    State.Sphere.REVEAL_AMOUNT = State.Reveal;
                }
                State.Sphere.gameObject.SetActive(State.Active);
            }
            catch (Exception Exception)
            {
                (Failures ??= new()).Add(Exception);
            }
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Fog sphere restoration failed.", Failures);
    }

    static void ApplyBinding(OrbFogHandler Handler)
    {
        if (NativeBaseMap.ApplyFogless(Handler)) return;
        if (BoundOwner != Handler || !Map.HasPublishedLayout) return;
        Handler.origins = BoundOrigins;
        if (Handler.sphere) Handler.SetFogOrigin(Map.Index);
        ApplyRisingFogState(Handler);
    }

    static IEnumerator FollowFogCatchUp(OrbFogHandler Handler, IEnumerator Routine, int OriginIndex)
    {
        while (Handler.currentID == OriginIndex && Routine.MoveNext())
        {
            if (SuppressesRisingFog(Handler)) ApplyRisingFogState(Handler);
            yield return Routine.Current;
        }
        if (Handler.currentID == OriginIndex && SuppressesRisingFog(Handler))
            ApplyRisingFogState(Handler);
    }

    static IEnumerator FollowReveal(OrbFogHandler Handler, IEnumerator Routine, int OriginIndex)
    {
        while (Handler.currentID == OriginIndex && Routine.MoveNext())
        {
            if (SuppressesRisingFog(Handler)) ApplyRisingFogState(Handler);
            yield return Routine.Current;
        }
        if (Handler.currentID != OriginIndex) yield break;
        if (SuppressesRisingFog(Handler))
        {
            ApplyRisingFogState(Handler);
            yield break;
        }
        if (BoundOwner == Handler && Map.Active && Map.Index >= 0 && Map.Index < Map.Slots.Length
            && Map.Slots[Map.Index].NativeRole == Segment.TheKiln && Handler.sphere)
            Handler.SetFogOrigin(Map.Index);
    }

    [HarmonyPatch(typeof(OrbFogHandler), "Start")]
    static class StartPatch
    {
        [HarmonyPostfix]
        static void Postfix(OrbFogHandler __instance) => ApplyBinding(__instance);
    }

    [HarmonyPatch(typeof(OrbFogHandler), nameof(OrbFogHandler.SetFogOrigin))]
    static class OriginPatch
    {
        [HarmonyPrefix]
        static void Prefix(OrbFogHandler __instance)
        {
            if (BoundOwner == __instance && Map.HasPublishedLayout)
                __instance.origins = BoundOrigins;
        }

        [HarmonyPostfix]
        static void Postfix(OrbFogHandler __instance) => ApplyRisingFogState(__instance);
    }

    [HarmonyPatch]
    static class MovementPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(OrbFogHandler), "StartMovingRPC")
                ?? throw new MissingMethodException(typeof(OrbFogHandler).FullName, "StartMovingRPC");
            yield return AccessTools.DeclaredMethod(typeof(OrbFogHandler), "WaitToMove")
                ?? throw new MissingMethodException(typeof(OrbFogHandler).FullName, "WaitToMove");
            yield return AccessTools.DeclaredMethod(typeof(OrbFogHandler), "Move")
                ?? throw new MissingMethodException(typeof(OrbFogHandler).FullName, "Move");
        }

        [HarmonyPrefix]
        static bool Prefix(OrbFogHandler __instance) => AllowRisingFog(__instance);
    }

    [HarmonyPatch]
    static class SyncPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(OrbFogHandler), "RPCA_SyncFog")
                ?? throw new MissingMethodException(typeof(OrbFogHandler).FullName, "RPCA_SyncFog");
            yield return AccessTools.DeclaredMethod(typeof(OrbFogHandler), "RPC_InitFog")
                ?? throw new MissingMethodException(typeof(OrbFogHandler).FullName, "RPC_InitFog");
        }

        [HarmonyPostfix]
        static void Postfix(OrbFogHandler __instance) => ApplyRisingFogState(__instance);
    }

    [HarmonyPatch(typeof(OrbFogHandler), "WaitForFogCatchUp")]
    static class CatchUpPatch
    {
        [HarmonyPostfix]
        static void Postfix(OrbFogHandler __instance, ref IEnumerator __result)
        {
            if (BoundOwner == __instance && Map.Active || NativeBaseMap.OwnsFog(__instance))
                __result = FollowFogCatchUp(__instance, __result, __instance.currentID);
        }
    }

    [HarmonyPatch(typeof(OrbFogHandler), nameof(OrbFogHandler.IsFoggingCurrentSegment), MethodType.Getter)]
    static class FoggingQueryPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            var Handler = BoundOwner;
            if (!Handler || !SuppressesRisingFog(Handler)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(OrbFogHandler), "WaitForReveal")]
    static class RevealPatch
    {
        [HarmonyPostfix]
        static void Postfix(OrbFogHandler __instance, ref IEnumerator __result)
        {
            if (BoundOwner == __instance && Map.Active || NativeBaseMap.OwnsFog(__instance))
                __result = FollowReveal(__instance, __result, __instance.currentID);
        }
    }

    [HarmonyPatch(typeof(FogSphere), "SetSharderVars")]
    static class GameplayTailPatch
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Approximately = AccessTools.DeclaredMethod(
                typeof(Mathf), nameof(Mathf.Approximately), new[] { typeof(float), typeof(float) })
                ?? throw new MissingMethodException(
                    typeof(Mathf).FullName, nameof(Mathf.Approximately));
            var Replacement = ((Func<float, float, bool>)GameplayFogEnabled).Method;
            var Replaced = false;
            foreach (var Instruction in Instructions)
            {
                if (Instruction.Calls(Approximately))
                {
                    if (Replaced)
                        throw new AmbiguousMatchException(
                            "FogSphere's gameplay fog condition is ambiguous.");
                    Instruction.opcode = OpCodes.Call;
                    Instruction.operand = Replacement;
                    Replaced = true;
                }
                yield return Instruction;
            }
            if (!Replaced)
                throw new MissingMethodException(
                    "FogSphere's gameplay fog condition was not found.");
        }

        [HarmonyPostfix]
        static void Postfix(FogSphere __instance) => NativeBaseMap.ApplyFogless(__instance);
    }
}
