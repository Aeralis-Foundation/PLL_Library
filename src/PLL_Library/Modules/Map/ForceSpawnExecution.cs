using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal abstract class ForceSpawnExecution
{
    readonly PLLForceSpawner Marker;
    readonly string Key;
    bool IsSelected;

    ForceSpawnExecution(PLLForceSpawner MarkerValue, string KeyValue)
    {
        Marker = MarkerValue;
        Key = KeyValue;
    }

    internal GameObject Root => Marker.gameObject;

    internal void ValidateContract(PLLForceSpawner ExpectedMarker, string ExpectedKey)
    {
        if (!Marker || !ReferenceEquals(Marker, ExpectedMarker)
            || !string.Equals(Key, ExpectedKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"Force marker '{ExpectedKey}' changed its resolved occurrence contract.");
        if (IsSelected) ValidateSelected();
        else RequireUnprepared();
    }

    static bool SameNativeObject(UnityEngine.Object? Left, UnityEngine.Object? Right) =>
        Left && Right && Left.GetInstanceID() == Right.GetInstanceID();

    internal static ForceSpawnExecution Create(
        PLLForceSpawner Marker,
        string Key,
        NativeSpawnResolution Resolution) => Resolution switch
    {
        NativeItemSpawnResolution Item => new ItemExecution(Marker, Key, Item),
        NativePropSpawnResolution Prop => new PropExecution(Marker, Key, Prop),
        _ => throw new InvalidOperationException($"Force marker '{Key}' has an unknown sealed resolution.")
    };

    internal void Prepare(bool Selected)
    {
        if (!Marker || !Root)
            throw new InvalidOperationException($"Force marker '{Key}' was destroyed before preparation.");
        if (!Selected)
        {
            RequireUnprepared();
            IsSelected = false;
            return;
        }
        PrepareSelected();
        IsSelected = true;
    }

    internal void Complete()
    {
        if (!IsSelected)
        {
            RequireUnprepared();
            return;
        }
        CompleteSelected();
    }

    internal void Rollback()
    {
        try { RollbackRuntime(); }
        finally { IsSelected = false; }
    }

    internal void WriteNetworkIdentity(GeneratedNetworkContract.ContractWriter Writer)
    {
        Writer.Write(Key);
        Writer.Write(IsSelected);
        WriteNetworkExecutionIdentity(Writer);
    }

    internal abstract bool TryGetFinalStep(out LevelGenStep Step);
    internal static void ClearCaptures() => PropExecution.ClearCaptureRegistry();

    protected abstract void PrepareSelected();
    protected abstract void CompleteSelected();
    protected abstract void ValidateSelected();
    protected abstract void RollbackRuntime();
    protected abstract void RequireUnprepared();
    protected abstract void WriteNetworkExecutionIdentity(GeneratedNetworkContract.ContractWriter Writer);

    protected void RequireSourceShape()
    {
        if (Root.transform.childCount != 0 || Root.GetComponent<Photon.Pun.PhotonView>()
            || Root.GetComponents<PLLForceSpawner>().Length != 1
            || Root.GetComponents<Component>().Length != 2)
            throw new InvalidOperationException($"Force marker '{Key}' changed before runtime translation.");
    }

    protected InvalidOperationException RetainedRuntimeTranslation() =>
        new($"Nonselected force marker '{Key}' retained a runtime translation.");

    sealed class ItemExecution : ForceSpawnExecution
    {
        readonly NativeItemSpawnResolution Resolution;
        Spawner? ItemSpawner;
        Peak.SpawnedItemTracker? ItemTracker;
        Hash128 ItemTrackerId;

        internal ItemExecution(
            PLLForceSpawner Marker,
            string Key,
            NativeItemSpawnResolution ResolutionValue) : base(Marker, Key)
        {
            Resolution = ResolutionValue;
        }

        internal override bool TryGetFinalStep(out LevelGenStep Step)
        {
            Step = null!;
            return false;
        }

        protected override void PrepareSelected()
        {
            var IsNew = !ItemSpawner;
            if (IsNew) RequireSourceShape();
            var Prefab = Resolution.Prefab();
            if (IsNew)
            {
                var RuntimeSpawner = Root.AddComponent<Spawner>();
                ItemSpawner = RuntimeSpawner;
                RuntimeSpawner.spawnMode = Spawner.SpawnMode.SingleItem;
                RuntimeSpawner.spawnPointMode = Spawner.SpawnPointMode.SingleList;
                RuntimeSpawner.spawnedObjectPrefab = Prefab;
                RuntimeSpawner.spawns = null;
                RuntimeSpawner.spawnPool = default;
                RuntimeSpawner.spawnSpots = new List<Transform> { Root.transform };
                RuntimeSpawner.weightedSpawnSpots = new List<Spawner.WeightedSpawnPointEntry>();
                RuntimeSpawner.spawnUpTowardsTarget = null;
                RuntimeSpawner.spawnTransformIsSpawnerTransform = false;
                RuntimeSpawner.spawnAwayFromUpTarget = false;
                RuntimeSpawner.centerItemsVisually = false;
                RuntimeSpawner.spawnOnStart = true;
                RuntimeSpawner.isKinematic = false;
                RuntimeSpawner.baseSpawnChance = 1f;
                RuntimeSpawner.playersInRoomRequirement = 0;
                RuntimeSpawner.belowAscentRequirement = -1;
                RuntimeSpawner.normalRunsOnly = false;
                RuntimeSpawner.canRepeatSpawns = false;
                RuntimeSpawner.heightBasedSpawnPools = new List<Spawner.HeightBasedSpawnListEntry>();
                RuntimeSpawner.fallbackSpawn = null;
                ItemTracker = Root.AddComponent<Peak.SpawnedItemTracker>();
                ItemTrackerId = SpawnTrackerTransaction.PrepareGeneratedSpawnTracker(Root);
            }
            Validate(Prefab);
        }

        protected override void CompleteSelected() => Validate(Resolution.Prefab());
        protected override void ValidateSelected() => Validate(Resolution.Prefab());

        protected override void RollbackRuntime()
        {
            Exception? Failure = null;
            var Tracker = ItemTracker;
            if (Tracker is not null && Tracker)
            {
                try { SpawnTrackerTransaction.RemoveSpawnTracker(Tracker); }
                catch (Exception ExceptionValue) { Failure = ExceptionValue; }
                try { UnityEngine.Object.DestroyImmediate(Tracker); }
                catch (Exception ExceptionValue) { Failure ??= ExceptionValue; }
            }
            ItemTracker = null;
            var RuntimeSpawner = ItemSpawner;
            if (RuntimeSpawner is not null && RuntimeSpawner)
            {
                try { UnityEngine.Object.DestroyImmediate(RuntimeSpawner); }
                catch (Exception ExceptionValue) { Failure ??= ExceptionValue; }
            }
            ItemSpawner = null;
            ItemTrackerId = default;
            if (Failure is not null) ExceptionPropagation.Throw(Failure);
        }

        protected override void RequireUnprepared()
        {
            if (ItemSpawner || ItemTracker) throw RetainedRuntimeTranslation();
        }

        protected override void WriteNetworkExecutionIdentity(GeneratedNetworkContract.ContractWriter Writer) => Writer.Write(true);

        void Validate(GameObject Prefab)
        {
            var RuntimeSpawner = ItemSpawner;
            var Tracker = ItemTracker;
            if (RuntimeSpawner is null || !RuntimeSpawner || Tracker is null || !Tracker
                || !SameNativeObject(RuntimeSpawner.gameObject, Root) || !SameNativeObject(Tracker.gameObject, Root)
                || Root.transform.childCount != 0 || Root.GetComponents<Spawner>().Length != 1
                || Root.GetComponents<Peak.SpawnedItemTracker>().Length != 1
                || Root.GetComponent<Photon.Pun.PhotonView>()
                || RuntimeSpawner.spawnMode != Spawner.SpawnMode.SingleItem
                || RuntimeSpawner.spawnPointMode != Spawner.SpawnPointMode.SingleList
                || !SameNativeObject(RuntimeSpawner.spawnedObjectPrefab, Prefab) || RuntimeSpawner.spawns
                || RuntimeSpawner.spawnPool != default || RuntimeSpawner.spawnSpots is not { Count: 1 }
                || !SameNativeObject(RuntimeSpawner.spawnSpots[0], Root.transform)
                || RuntimeSpawner.weightedSpawnSpots is not { Count: 0 } || RuntimeSpawner.spawnUpTowardsTarget
                || RuntimeSpawner.spawnTransformIsSpawnerTransform || RuntimeSpawner.spawnAwayFromUpTarget
                || RuntimeSpawner.centerItemsVisually || !RuntimeSpawner.spawnOnStart || RuntimeSpawner.isKinematic
                || RuntimeSpawner.baseSpawnChance != 1f || RuntimeSpawner.playersInRoomRequirement != 0
                || RuntimeSpawner.belowAscentRequirement != -1 || RuntimeSpawner.normalRunsOnly
                || RuntimeSpawner.canRepeatSpawns || RuntimeSpawner.heightBasedSpawnPools is not { Count: 0 }
                || RuntimeSpawner.fallbackSpawn || Tracker.SpawnerId != ItemTrackerId)
                throw new InvalidOperationException($"Forced item marker '{Key}' changed its native Spawner contract.");
        }
    }

    sealed class PropExecution : ForceSpawnExecution
    {
        static readonly Dictionary<int, PropExecution> CaptureTargets = new();
        readonly NativePropSpawnResolution Resolution;
        GameObject[]? Family;
        PropSpawner? PropSpawner;
        GameObject? PropOutput;
        int? CapturedOutputId;
        string? SelectedPropKey;
        Vector3 SelectedPropScale;

        internal PropExecution(
            PLLForceSpawner Marker,
            string Key,
            NativePropSpawnResolution ResolutionValue) : base(Marker, Key)
        {
            Resolution = ResolutionValue;
        }

        internal override bool TryGetFinalStep(out LevelGenStep Step)
        {
            var RuntimeSpawner = PropSpawner;
            if (RuntimeSpawner is not null && RuntimeSpawner)
            {
                Step = RuntimeSpawner;
                return true;
            }
            Step = null!;
            return false;
        }

        protected override void PrepareSelected()
        {
            CapturedOutputId = null;
            SelectedPropKey = null;
            SelectedPropScale = default;
            var IsNew = !PropSpawner;
            if (IsNew) RequireSourceShape();
            var FamilyValue = Family;
            if (FamilyValue is null) Family = FamilyValue = Resolution.PropFamily();
            else Resolution.ValidateFamily(FamilyValue);
            if (IsNew)
            {
                var RuntimeSpawner = Root.AddComponent<PropSpawner>();
                PropSpawner = RuntimeSpawner;
                RuntimeSpawner.area = Vector2.zero;
                RuntimeSpawner.raycastPosition = false;
                RuntimeSpawner.nrOfSpawns = 1;
                RuntimeSpawner.randomSpawns = false;
                RuntimeSpawner.minSpawnCount = 0;
                RuntimeSpawner.chanceToUseSpawner = 1f;
                RuntimeSpawner.currentSpawns = 0;
                RuntimeSpawner.props = FamilyValue;
                RuntimeSpawner.overrideProps = false;
                RuntimeSpawner.overridePropsList = null!;
                RuntimeSpawner.syncTransforms = true;
                RuntimeSpawner.constraints = new List<PropSpawnerConstraint>();
                RuntimeSpawner.postConstraints = new List<PropSpawnerConstraintPost>();
                RuntimeSpawner.postSpawnBehaviors = new List<PostSpawnBehavior>();
                NativePrefabContracts.ValidationConstraints(RuntimeSpawner) = new List<IValidationConstraint>();
                RuntimeSpawner.modifiers = new List<PropSpawnerMod> { new PllSocketTransformModifier() };
            }
            var PreparedSpawner = PropSpawner!;
            PreparedSpawner.currentSpawns = 0;
            Validate(PreparedSpawner, FamilyValue);
            RegisterCapture(PreparedSpawner, this);
        }

        protected override void CompleteSelected()
        {
            var RuntimeSpawner = PropSpawner!;
            Validate(RuntimeSpawner, ValidatedFamily());
            var Output = ValidatedOutput(RuntimeSpawner);
            PropOutput = Output;
            var SpawnerObjects = new HashSet<int>();
            foreach (var SpawnedSpawner in Output.GetComponentsInChildren<ISpawner>(true))
            {
                var SpawnerComponent = (Component)SpawnedSpawner;
                if (!SpawnerComponent) continue;
                var SpawnerObject = SpawnerComponent.gameObject;
                if (SpawnerObjects.Add(SpawnerObject.GetInstanceID()))
                    SpawnTrackerTransaction.PrepareGeneratedSpawnTracker(SpawnerObject);
            }
        }

        protected override void ValidateSelected()
        {
            var RuntimeSpawner = PropSpawner;
            if (RuntimeSpawner is null) throw RetainedRuntimeTranslation();
            Validate(RuntimeSpawner, ValidatedFamily());
            if (!SameNativeObject(PropOutput, ValidatedOutput(RuntimeSpawner)))
                throw new InvalidOperationException(
                    $"Forced native prop '{Resolution.Name}' at '{Key}' replaced its owned output.");
        }

        protected override void RollbackRuntime()
        {
            Exception? Failure = null;
            CapturedOutputId = null;
            SelectedPropKey = null;
            SelectedPropScale = default;
            var Output = PropOutput;
            if (Output is not null && Output)
            {
                try { SpawnTrackerTransaction.RemoveSpawnTrackers(Output.transform); }
                catch (Exception ExceptionValue) { Failure = ExceptionValue; }
            }
            PropOutput = null;
            var RuntimeSpawner = PropSpawner;
            if (RuntimeSpawner is not null && RuntimeSpawner)
            {
                UnregisterCapture(RuntimeSpawner, this);
                try { RuntimeSpawner.Clear(); }
                catch (Exception ExceptionValue) { Failure ??= ExceptionValue; }
                try { UnityEngine.Object.DestroyImmediate(RuntimeSpawner); }
                catch (Exception ExceptionValue) { Failure ??= ExceptionValue; }
            }
            PropSpawner = null;
            if (Failure is not null) ExceptionPropagation.Throw(Failure);
        }

        protected override void RequireUnprepared()
        {
            if (PropSpawner || PropOutput || CapturedOutputId is not null || SelectedPropKey is not null)
                throw RetainedRuntimeTranslation();
        }

        protected override void WriteNetworkExecutionIdentity(GeneratedNetworkContract.ContractWriter Writer)
        {
            Writer.Write(false);
            if (!IsSelected) return;
            Writer.Write(SelectedPropKey
                ?? throw new InvalidOperationException(
                    $"Forced native prop '{Resolution.Name}' has no selected candidate identity."));
        }

        void Validate(PropSpawner RuntimeSpawner, GameObject[] Family)
        {
            if (!RuntimeSpawner || !SameNativeObject(RuntimeSpawner.gameObject, Root)
                || Root.GetComponents<PropSpawner>().Length != 1
                || Root.GetComponent<Photon.Pun.PhotonView>() || RuntimeSpawner.area != Vector2.zero
                || RuntimeSpawner.raycastPosition || RuntimeSpawner.nrOfSpawns != 1 || RuntimeSpawner.randomSpawns
                || RuntimeSpawner.minSpawnCount != 0 || RuntimeSpawner.chanceToUseSpawner != 1f
                || RuntimeSpawner.overrideProps || RuntimeSpawner.overridePropsList || !RuntimeSpawner.syncTransforms
                || RuntimeSpawner.props is null || RuntimeSpawner.props.Length != Family.Length
                || RuntimeSpawner.constraints is not { Count: 0 }
                || RuntimeSpawner.postConstraints is not { Count: 0 }
                || RuntimeSpawner.postSpawnBehaviors is not { Count: 0 }
                || NativePrefabContracts.ValidationConstraints(RuntimeSpawner) is not { Count: 0 }
                || RuntimeSpawner.modifiers is not { Count: 1 }
                || RuntimeSpawner.modifiers[0] is not PllSocketTransformModifier { mute: false })
                throw new InvalidOperationException($"Forced prop marker '{Key}' changed its native PropSpawner contract.");
            for (var i = 0; i < Family.Length; i++)
                if (!SameNativeObject(RuntimeSpawner.props[i], Family[i]))
                    throw new InvalidOperationException(
                        $"Forced prop marker '{Key}' changed its sealed candidate family.");
        }

        GameObject[] ValidatedFamily()
        {
            var FamilyValue = Family
                ?? throw new InvalidOperationException($"Forced prop marker '{Key}' has no prepared candidate family.");
            Resolution.ValidateFamily(FamilyValue);
            return FamilyValue;
        }

        GameObject ValidatedOutput(PropSpawner RuntimeSpawner)
        {
            var Children = RuntimeSpawner.transform.childCount;
            GameObject? Output = null;
            GameObject? Collision = null;
            foreach (var Value in RuntimeSpawner.SpawnedProps)
            {
                if (!Value) continue;
                if (!Output)
                {
                    Output = Value;
                    continue;
                }
                Collision = Value;
                break;
            }
            if (Children != 1 || Output is null || !Output || Collision is not null
                || !SameNativeObject(Output.transform.parent, RuntimeSpawner.transform)
                || !SameNativeObject(RuntimeSpawner.transform.GetChild(0).gameObject, Output)
                || CapturedOutputId != Output.GetInstanceID() || SelectedPropKey is null
                || (Output.transform.position - Root.transform.position).sqrMagnitude > 0.000001f
                || Math.Abs(Quaternion.Dot(Output.transform.rotation.normalized,
                    Root.transform.rotation.normalized)) < 0.999999f
                || !SameScale(Output.transform.localScale, SelectedPropScale))
                throw new InvalidOperationException(
                    $"Forced native prop '{Resolution.Name}' at '{Key}' did not retain one exact-pose owned output.");
            foreach (var View in Output.GetComponentsInChildren<Photon.Pun.PhotonView>(true))
                if (View && View.ViewID != 0)
                    throw new InvalidOperationException(
                        $"Forced native prop '{Resolution.Name}' at '{Key}' retained an assigned PhotonView.");
            return Output;
        }

        void CaptureSpawn(GameObject Prefab, GameObject Output)
        {
            var RuntimeSpawner = PropSpawner;
            if (!IsSelected || RuntimeSpawner is null || !RuntimeSpawner || !Prefab || !Output
                || !NativeFeatureGeneration.IsActiveDescendant(Root.transform))
                throw new InvalidOperationException(
                    $"Forced native prop '{Resolution.Name}' spawned outside its owned generation scope.");
            if (CapturedOutputId is not null)
                throw new InvalidOperationException(
                    $"Forced native prop '{Resolution.Name}' created more than one candidate output.");
            SelectedPropKey = Resolution.StableKey(Prefab);
            SelectedPropScale = Prefab.transform.localScale;
            CapturedOutputId = Output.GetInstanceID();
        }

        static void RegisterCapture(PropSpawner Spawner, PropExecution Execution)
        {
            var SpawnerInstanceId = Spawner.GetInstanceID();
            if (CaptureTargets.TryGetValue(SpawnerInstanceId, out var Current))
            {
                if (ReferenceEquals(Current, Execution)) return;
                throw new InvalidOperationException("A translated force PropSpawner has more than one occurrence owner.");
            }
            CaptureTargets.Add(SpawnerInstanceId, Execution);
        }

        static void UnregisterCapture(PropSpawner Spawner, PropExecution Execution)
        {
            var SpawnerInstanceId = Spawner.GetInstanceID();
            if (CaptureTargets.TryGetValue(SpawnerInstanceId, out var Current) && ReferenceEquals(Current, Execution))
                CaptureTargets.Remove(SpawnerInstanceId);
        }

        static bool SameScale(Vector3 Value, Vector3 Expected) =>
            SameScale(Value.x, Expected.x) && SameScale(Value.y, Expected.y) && SameScale(Value.z, Expected.z);

        static bool SameScale(float Value, float Expected) =>
            Math.Abs(Value - Expected) <= 0.0001f * Math.Max(1f, Math.Abs(Expected));

        internal static void ClearCaptureRegistry() => CaptureTargets.Clear();

        [HarmonyPatch(typeof(HelperFunctions), "SpawnPrefab",
            new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Transform) })]
        static class SpawnCapturePatch
        {
            [HarmonyPostfix]
            static void Postfix(GameObject __0, Transform __3, GameObject __result)
            {
                if (!__3 || !__result || !__3.TryGetComponent<PropSpawner>(out var Spawner)
                    || !CaptureTargets.TryGetValue(Spawner.GetInstanceID(), out var Execution)) return;
                Execution.CaptureSpawn(__0, __result);
            }
        }

        [HarmonyPatch(typeof(PropSpawner), nameof(PropSpawner.Clear))]
        static class ClearCapturePatch
        {
            [HarmonyPostfix]
            static void Postfix(PropSpawner __instance) =>
                CaptureTargets.Remove(__instance.GetInstanceID());
        }
    }
}
