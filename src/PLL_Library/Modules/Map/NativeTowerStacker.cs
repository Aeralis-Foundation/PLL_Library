using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal static class NativeTowerStacker
{
    enum OutputState
    {
        Retained,
        Empty,
        Started,
        Completed
    }

    sealed class Tower
    {
        readonly Transform Root;
        readonly GameObject[] Parts;
        readonly List<GameObject> Stack;
        readonly int StackHeight;
        readonly Vector3 Direction;
        readonly float Offset;
        OutputState State;

        internal Tower(Peak.TowerStacker StackerValue, Transform RootValue)
        {
            Stacker = StackerValue;
            Root = RootValue;
            Parts = RequireParts(StackerValue).ToArray();
            Stack = StackerValue.stack
                ?? throw new InvalidOperationException("A TowerStacker stack is null.");
            StackHeight = StackerValue.stackHeight;
            Direction = StackerValue.direction;
            Offset = StackerValue.offset;
            if (Stack.Count == StackHeight)
            {
                ValidateOutput(Stack);
                State = OutputState.Retained;
                return;
            }
            if (Stack.Count != 0)
                throw new InvalidOperationException(
                    $"A TowerStacker contains {Stack.Count} of {StackHeight} layers.");
            State = OutputState.Empty;
        }

        internal Peak.TowerStacker Stacker { get; }
        internal bool RequiresGeneration => State != OutputState.Retained;
        internal IReadOnlyList<GameObject> PartPlan => Parts;
        internal int Height => StackHeight;
        internal Vector3 LayerDirection => Direction;
        internal float LayerOffset => Offset;

        internal void Start()
        {
            if (State != OutputState.Empty)
                throw new InvalidOperationException("A TowerStacker generation started more than once.");
            ValidateInputs();
            if (Stack.Count != 0)
                throw new InvalidOperationException("A TowerStacker gained foreign output before generation.");
            for (var Index = Parts.Length - 1; Index > 0; Index--)
            {
                var Other = UnityEngine.Random.Range(0, Index + 1);
                (Parts[Index], Parts[Other]) = (Parts[Other], Parts[Index]);
            }
            State = OutputState.Started;
        }

        internal void Add(GameObject Output)
        {
            if (State != OutputState.Started)
                throw new InvalidOperationException("TowerStacker output arrived outside generation.");
            if (!Output || !TransformOwnership.Contains(Root, Output.transform))
                throw new InvalidOperationException("TowerStacker output lies outside its feature root.");
            Stack.Add(Output);
        }

        internal void Complete()
        {
            if (State != OutputState.Started)
                throw new InvalidOperationException("A TowerStacker completed without an active generation.");
            State = OutputState.Completed;
        }

        internal void ValidateCompleted()
        {
            if (State == OutputState.Empty)
            {
                RequireExecutionOwner();
                if (Stack.Count != 0)
                    throw new InvalidOperationException("An inactive TowerStacker gained foreign output.");
                return;
            }
            if (State == OutputState.Started)
                throw new InvalidOperationException("A TowerStacker generation did not complete.");
            RequireExecutionOwner();
            ValidateOutput(Stack);
        }

        internal void ClearGenerated()
        {
            if (State is OutputState.Retained or OutputState.Empty) return;
            RequireExecutionOwner();
            ValidateOwnedOutput(Stack);
            Stacker.Clear();
            State = OutputState.Empty;
        }

        internal void RequireExecutionOwner()
        {
            if (!Stacker || !Root || !TransformOwnership.Contains(Root, Stacker.transform))
                throw new InvalidOperationException("A TowerStacker left its feature root.");
            if (!ReferenceEquals(Stacker.stack, Stack))
                throw new InvalidOperationException("A TowerStacker changed its output owner.");
        }

        void ValidateInputs()
        {
            RequireExecutionOwner();
            var CurrentParts = Stacker.parts
                ?? throw new InvalidOperationException("A TowerStacker parts catalog is null.");
            if (Stacker.stackHeight != StackHeight || !Stacker.direction.Equals(Direction)
                || !Stacker.offset.Equals(Offset)
                || !UnityValueContract.Finite(Stacker.transform.position)
                || !UnityValueContract.Finite(Stacker.direction)
                || !float.IsFinite(Stacker.offset) || CurrentParts.Count != Parts.Length)
                throw new InvalidOperationException("A TowerStacker generation plan changed before execution.");
            for (var Index = 0; Index < Parts.Length; Index++)
                if (!ReferenceEquals(CurrentParts[Index], Parts[Index]))
                    throw new InvalidOperationException(
                        $"A TowerStacker part at index {Index} changed before execution.");
        }

        void ValidateOutput(IReadOnlyList<GameObject> Output)
        {
            if (Output.Count != StackHeight)
                throw new InvalidOperationException(
                    $"A TowerStacker generated {Output.Count} of {StackHeight} layers.");
            ValidateOwnedOutput(Output);
        }

        void ValidateOwnedOutput(IReadOnlyList<GameObject> Output)
        {
            var OutputIds = new HashSet<int>();
            for (var Index = 0; Index < Output.Count; Index++)
            {
                var Value = Output[Index];
                if (!Value || !TransformOwnership.Contains(Root, Value.transform))
                    throw new InvalidOperationException(
                        $"TowerStacker layer {Index} lies outside its feature root.");
                if (!OutputIds.Add(Value.GetInstanceID()))
                    throw new InvalidOperationException("A TowerStacker contains duplicate output.");
            }
        }

        static List<GameObject> RequireParts(Peak.TowerStacker Stacker)
        {
            if (Stacker.stackHeight < 0)
                throw new InvalidOperationException("A TowerStacker stackHeight is negative.");
            if (!UnityValueContract.Finite(Stacker.transform.position)
                || !UnityValueContract.Finite(Stacker.direction)
                || !float.IsFinite(Stacker.offset))
                throw new InvalidOperationException("A TowerStacker generation input is not finite.");
            var Source = Stacker.parts
                ?? throw new InvalidOperationException("A TowerStacker parts catalog is null.");
            if (Stacker.stackHeight > 0 && Source.Count == 0)
                throw new InvalidOperationException("A TowerStacker with layers has no parts.");
            for (var Index = 0; Index < Source.Count; Index++)
            {
                var Part = Source[Index];
                if (!Part)
                    throw new InvalidOperationException(
                        $"A TowerStacker part at index {Index} is missing.");
                if (Part.GetComponentInChildren<Peak.TowerStacker>(true))
                    throw new InvalidOperationException(
                        $"A TowerStacker part at index {Index} contains a nested TowerStacker.");
            }
            return Source;
        }

    }

    internal sealed class Generation
    {
        readonly Dictionary<Peak.TowerStacker, Tower> Towers = new();

        internal Generation(NativeFeatureGeneration.FeatureRoots Roots)
        {
            foreach (var Root in Roots)
                foreach (var Stacker in Root.GetComponentsInChildren<Peak.TowerStacker>(true))
                {
                    if (!Stacker)
                        throw new InvalidOperationException("A TowerStacker component is missing.");
                    if (!Towers.TryAdd(Stacker, new Tower(Stacker, Root)))
                        throw new InvalidOperationException(
                            "A TowerStacker belongs to multiple feature roots.");
                }
        }

        internal bool RequiresGeneration(Peak.TowerStacker Stacker) =>
            Require(Stacker).RequiresGeneration;

        internal IEnumerator CreateExecution(
            Peak.TowerStacker Stacker, bool Immediate, Action RequireCurrent)
        {
            var Tower = Require(Stacker);
            if (Tower.RequiresGeneration)
                return Regenerate(Tower, RequireCurrent, Immediate);
            Tower.ValidateCompleted();
            return Retain();
        }

        Tower Require(Peak.TowerStacker Stacker) =>
            Towers.TryGetValue(Stacker, out var Tower)
                ? Tower
                : throw new InvalidOperationException(
                    "A foreign TowerStacker entered native feature generation.");

        internal void ValidateCompleted()
        {
            foreach (var Tower in Towers.Values) Tower.ValidateCompleted();
        }

        internal void ClearGenerated()
        {
            var Failures = new List<Exception>();
            foreach (var Tower in Towers.Values)
                try { Tower.ClearGenerated(); }
                catch (Exception Failure) { Failures.Add(Failure); }
            if (Failures.Count == 1) ExceptionPropagation.Throw(Failures[0]);
            if (Failures.Count != 0)
                throw new AggregateException("TowerStacker output cleanup failed.", Failures);
        }
    }

    sealed class GenerationScope : IDisposable
    {
        readonly NativeFeatureGeneration.FeatureRoots Roots;
        readonly Func<bool> CancellationRequested;
        readonly Action RequireGenerationCurrent;

        internal GenerationScope(
            Generation GenerationValue, NativeFeatureGeneration.FeatureRoots RootsValue,
            Func<bool> IsCancellationRequested, Action RequireCurrentGeneration)
        {
            Generation = GenerationValue;
            Roots = RootsValue;
            CancellationRequested = IsCancellationRequested;
            RequireGenerationCurrent = RequireCurrentGeneration;
        }

        internal Generation Generation { get; }
        internal bool Owns(Transform Candidate) => Roots.DeepestRoot(Candidate, out _) is not null;

        internal void RequireCurrent()
        {
            RequireGenerationCurrent();
            if (CancellationRequested()) throw MapRun.FeatureWriter.Cancelled();
        }

        public void Dispose()
        {
            if (ActiveScope is null) return;
            if (!ReferenceEquals(ActiveScope, this))
                throw new InvalidOperationException(
                    "Native TowerStacker generation lost its active scope during cleanup.");
            ActiveScope = null;
        }
    }

    static GenerationScope? ActiveScope;

    internal static IDisposable EnterGeneration(
        Generation Generation, NativeFeatureGeneration.FeatureRoots Roots,
        Func<bool> IsCancellationRequested, Action RequireCurrentGeneration)
    {
        if (ActiveScope is not null)
            throw new InvalidOperationException("Native TowerStacker generation was entered more than once.");
        var Scope = new GenerationScope(
            Generation, Roots, IsCancellationRequested, RequireCurrentGeneration);
        ActiveScope = Scope;
        return Scope;
    }

    static bool TryCreateExecution(
        Peak.TowerStacker Stacker, bool Immediate,
        out GenerationScope Scope, out IEnumerator Execution)
    {
        Scope = ActiveScope!;
        Execution = null!;
        if (Scope is null || !Scope.Owns(Stacker.transform)) return false;
        Scope.RequireCurrent();
        Execution = Scope.Generation.CreateExecution(
            Stacker, Immediate, Scope.RequireCurrent);
        return true;
    }

    static IEnumerator Retain()
    {
        yield break;
    }

    static IEnumerator Regenerate(Tower Tower, Action RequireCurrent, bool Immediate)
    {
        void RequireExecutionCurrent()
        {
            RequireCurrent();
            Tower.RequireExecutionOwner();
        }

        RequireCurrent();
        Tower.Start();
        var Stacker = Tower.Stacker;
        for (var Index = 0; Index < Tower.Height; Index++)
        {
            RequireExecutionCurrent();
            var Part = Tower.PartPlan[Index % Tower.PartPlan.Count];
            using var Reservation = NativeFeatureGeneration.BeginNativeSpawn(Part);
            var Output = UnityEngine.Object.Instantiate(Part,
                Stacker.transform.position + Tower.LayerDirection * Tower.LayerOffset * Index,
                Stacker.transform.rotation * Part.transform.localRotation
                    * Quaternion.AngleAxis(UnityEngine.Random.value * 360f, Vector3.up),
                Stacker.transform);
            if (!Output)
                throw new InvalidOperationException(
                    $"TowerStacker layer {Index} could not be instantiated.");
            var Transferred = false;
            try
            {
                RequireExecutionCurrent();
                Reservation?.Complete(Output);
                PhotonCloneIdentity.Reset(Output);
                Tower.Add(Output);
                Transferred = true;
                foreach (var Step in Output.GetComponentsInChildren<LevelGenStep>())
                {
                    if (!Step) continue;
                    RequireExecutionCurrent();
                    if (Immediate)
                    {
                        Step.ExecuteImmediately();
                        continue;
                    }
                    var Nested = Step.Execute()
                        ?? throw new InvalidOperationException(
                            $"TowerStacker layer {Index} step {Step.GetType().FullName} returned no iterator.");
                    try
                    {
                        while (Nested.MoveNext())
                        {
                            RequireExecutionCurrent();
                            yield return null;
                            RequireExecutionCurrent();
                        }
                    }
                    finally
                    {
                        (Nested as IDisposable)?.Dispose();
                    }
                }
            }
            finally
            {
                if (!Transferred && Output) NativeFeatureGeneration.DestroyNativeOutput(Output);
            }
            yield return null;
        }
        RequireExecutionCurrent();
        Tower.Complete();
    }

    [HarmonyPatch(typeof(Peak.TowerStacker), nameof(Peak.TowerStacker.Execute))]
    static class ExecutePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.TowerStacker __instance, ref IEnumerator __result)
        {
            if (!TryCreateExecution(__instance, false, out _, out var Execution)) return true;
            __result = Execution;
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.TowerStacker), nameof(Peak.TowerStacker.ExecuteImmediately))]
    static class ExecuteImmediatelyPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Peak.TowerStacker __instance)
        {
            if (!TryCreateExecution(__instance, true, out var Scope, out var Execution)) return true;
            try
            {
                while (Execution.MoveNext()) Scope.RequireCurrent();
            }
            finally
            {
                (Execution as IDisposable)?.Dispose();
            }
            return false;
        }
    }
}
