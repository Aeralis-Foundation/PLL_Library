using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class FinalForceOccurrence
{
    readonly FinalForceFeature[] features;

    internal FinalForceOccurrence(FinalForceFeature[] features)
    {
        this.features = features;
    }

    internal int FeatureCount => features.Length;
    internal FinalForceFeature FeatureAt(int Index) => features[Index];
    internal bool IsSelected(Transform Source)
    {
        foreach (var Feature in features)
            if (TransformOwnership.Contains(Feature.Root, Source)) return Feature.IsSelected(Source);
        return false;
    }

    internal void Stage()
    {
        foreach (var feature in features) feature.Stage();
    }

    internal void Stage(long epoch)
    {
        foreach (var feature in features)
            if (NativeFeatureGeneration.IsCurrent(feature.Root, epoch)) feature.Stage();
    }

    internal void Prepare(long epoch)
    {
        foreach (var feature in features)
            if (NativeFeatureGeneration.IsCurrent(feature.Root, epoch)) feature.Prepare(epoch);
    }

    internal void Complete(long epoch)
    {
        foreach (var feature in features)
            if (NativeFeatureGeneration.IsCurrent(feature.Root, epoch)) feature.Complete(epoch);
    }

    internal void Rollback()
    {
        Exception? failure = null;
        for (var i = features.Length - 1; i >= 0; i--)
            try { features[i].Rollback(); }
            catch (Exception exception) { failure ??= exception; }
        if (failure is not null) ExceptionPropagation.Throw(failure);
    }

    internal void WriteNetworkIdentity(GeneratedNetworkContract.ContractWriter Writer)
    {
        Writer.Write(features.Length);
        foreach (var feature in features) feature.WriteNetworkIdentity(Writer);
    }
}

internal sealed class FinalForceFeature
{
    readonly int index;
    readonly FinalGenerationTarget[] finals;
    readonly ForceSpawnTarget[] forces;
    PllFinalGenerationDriver? driver;

    internal FinalForceFeature(
        int index,
        Transform root,
        FinalGenerationTarget[] finals,
        ForceSpawnTarget[] forces)
    {
        this.index = index;
        Root = root;
        this.finals = finals;
        this.forces = forces;
    }

    internal Transform Root { get; }
    internal int Index => index;
    internal int FinalCount => finals.Length;
    internal int ForceCount => forces.Length;
    internal FinalGenerationTarget FinalAt(int Index) => finals[Index];
    internal ForceSpawnTarget ForceAt(int Index) => forces[Index];
    internal bool IsSelected(Transform Source)
    {
        foreach (var Final in finals)
            if (TransformOwnership.Contains(Final.Root.transform, Source))
                return Final.Prepared && Selected(Source, Root, Final.Root.transform);
        return Selected(Source, Root, null);
    }

    internal void Stage()
    {
        foreach (var final in finals) final.Stage();
    }

    internal void Prepare(long epoch)
    {
        if (!Root || !NativeFeatureGeneration.IsCurrent(Root, epoch))
            throw new InvalidOperationException("Final/force preparation occurred outside its native generation epoch.");
        Stage();
        var selectedFinals = new bool[finals.Length];
        for (var i = 0; i < finals.Length; i++)
            selectedFinals[i] = Selected(finals[i].Root.transform, Root, finals[i].Root.transform);
        foreach (var force in forces)
        {
            var selected = force.FinalIndex < 0
                ? Selected(force.Root.transform, Root, null)
                : selectedFinals[force.FinalIndex]
                    && Selected(force.Root.transform, finals[force.FinalIndex].Root.transform,
                        finals[force.FinalIndex].Root.transform);
            force.Prepare(selected);
        }

        var entries = new List<FinalGenerationEntry>();
        for (var i = 0; i < finals.Length; i++)
        {
            if (!selectedFinals[i]) continue;
            var translated = new List<LevelGenStep>();
            foreach (var force in forces)
                if (force.FinalIndex == i && force.TryGetFinalStep(out var Step)) translated.Add(Step);
            entries.Add(finals[i].Prepare(translated));
        }

        if (entries.Count == 0)
        {
            RemoveDriver();
            return;
        }
        var drivers = Root.GetComponents<PllFinalGenerationDriver>();
        if (drivers.Length > 1)
            throw new InvalidOperationException("A feature root contains more than one PLL final-generation driver.");
        driver = drivers.Length == 1 ? drivers[0] : Root.gameObject.AddComponent<PllFinalGenerationDriver>();
        if (!driver || driver.transform != Root)
            throw new InvalidOperationException("The PLL final-generation driver was not installed directly on its feature root.");
        driver.Prepare(Root, epoch, entries.ToArray());

        var captured = Root.GetComponentsInChildren<ILevelGenFinalizer>();
        var driverCount = 0;
        foreach (var finalizer in captured)
            if (ReferenceEquals(finalizer, driver)) driverCount++;
        if (driverCount != 1)
            throw new InvalidOperationException(
                $"Feature root {(index == 0 ? "FloorRoot" : "CampfireRoot")} did not expose exactly one PLL finalizer.");
    }

    internal void Complete(long epoch)
    {
        if (driver) driver.VerifyCompleted(Root, epoch);
        foreach (var force in forces) force.Complete();
    }

    internal void Rollback()
    {
        Exception? failure = null;
        RemoveDriver();
        for (var i = forces.Length - 1; i >= 0; i--)
            try { forces[i].Rollback(); }
            catch (Exception exception) { failure ??= exception; }
        for (var i = finals.Length - 1; i >= 0; i--)
            try { finals[i].Rollback(); }
            catch (Exception exception) { failure ??= exception; }
        Stage();
        if (failure is not null) ExceptionPropagation.Throw(failure);
    }

    internal void WriteNetworkIdentity(GeneratedNetworkContract.ContractWriter Writer)
    {
        Writer.Write(index);
        Writer.Write(finals.Length);
        foreach (var final in finals) final.WriteNetworkIdentity(Writer);
        Writer.Write(forces.Length);
        foreach (var force in forces) force.WriteNetworkIdentity(Writer);
    }

    void RemoveDriver()
    {
        if (driver) UnityEngine.Object.DestroyImmediate(driver);
        driver = null;
        if (!Root) return;
        foreach (var extra in Root.GetComponents<PllFinalGenerationDriver>())
            if (extra) UnityEngine.Object.DestroyImmediate(extra);
    }

    static bool Selected(Transform value, Transform root, Transform? held)
    {
        if (!value || !root || value != root && !value.IsChildOf(root)) return false;
        for (var current = value; current && current != root; current = current.parent)
            if (current != held && !current.gameObject.activeSelf) return false;
        return true;
    }
}

internal sealed class FinalGenerationTarget
{
    readonly LevelGenStep[] sourceSteps;
    readonly Peak.PropSpawner_AmuletStatues[] Amulets;
    string[] preparedStepKeys = Array.Empty<string>();
    bool prepared;

    internal FinalGenerationTarget(PLLFinalGeneration MarkerValue, string key, LevelGenStep[] sourceSteps,
        Peak.PropSpawner_AmuletStatues[] Amulets)
    {
        Marker = MarkerValue;
        Root = MarkerValue.gameObject;
        Key = key;
        this.sourceSteps = sourceSteps;
        this.Amulets = Amulets;
    }

    internal PLLFinalGeneration Marker { get; }
    internal GameObject Root { get; }
    internal string Key { get; }
    internal int SourceStepCount => sourceSteps.Length;
    internal LevelGenStep SourceStepAt(int Index) => sourceSteps[Index];
    internal bool Prepared => prepared;

    internal void ValidateContract(GameObject ExpectedRoot, string ExpectedKey)
    {
        if (!Marker || !Root || !ReferenceEquals(Root, ExpectedRoot)
            || !string.Equals(Key, ExpectedKey, StringComparison.Ordinal)
            || Root.activeSelf != prepared)
            throw new InvalidOperationException($"Final root '{ExpectedKey}' changed its resolved occurrence contract.");
    }

    internal void Stage()
    {
        prepared = false;
        preparedStepKeys = Array.Empty<string>();
        if (Root) Root.SetActive(false);
    }

    internal FinalGenerationEntry Prepare(IReadOnlyList<LevelGenStep> translated)
    {
        if (!Root || Root.activeSelf)
            throw new InvalidOperationException($"Final root '{Key}' was not held inactive before native generation.");
        var Steps = new LevelGenStep[sourceSteps.Length + translated.Count];
        Array.Copy(sourceSteps, Steps, sourceSteps.Length);
        for (var i = 0; i < translated.Count; i++) Steps[sourceSteps.Length + i] = translated[i];
        foreach (var step in Steps)
            if (!step || !TransformOwnership.Contains(Root.transform, step.transform)
                || !FinalForceAuthoringContract.ActiveBelow(step.transform, Root.transform, Root.transform))
                throw new InvalidOperationException($"Final root '{Key}' contains an inactive or escaped sealed step.");
        foreach (var step in sourceSteps) StepBinding.ValidateRuntime(step, Key);

        var discovered = Root.GetComponentsInChildren<LevelGenStep>(true);
        if (discovered.Length != Steps.Length)
            throw new InvalidOperationException($"Final root '{Key}' contains an unsealed LevelGenStep.");
        var identities = new HashSet<LevelGenStep>(Steps);
        foreach (var step in discovered)
            if (!step || !identities.Contains(step))
                throw new InvalidOperationException($"Final root '{Key}' changed its sealed LevelGenStep set.");
        preparedStepKeys = new string[Steps.Length];
        for (var i = 0; i < Steps.Length; i++)
            preparedStepKeys[i] = FinalForceAuthoringContract.Key(Root.transform, Steps[i]);
        Array.Sort(preparedStepKeys, Steps, StringComparer.Ordinal);
        prepared = true;
        return new FinalGenerationEntry(Root, Key, Steps, Amulets);
    }

    internal void Rollback()
    {
        Exception? failure = null;
        for (var i = sourceSteps.Length - 1; i >= 0; i--)
            try
            {
                if (sourceSteps[i] && sourceSteps[i] is not Peak.TowerStacker) sourceSteps[i].Clear();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        if (Root) Root.SetActive(false);
        prepared = false;
        preparedStepKeys = Array.Empty<string>();
        if (failure is not null) ExceptionPropagation.Throw(failure);
    }

    internal void WriteNetworkIdentity(GeneratedNetworkContract.ContractWriter Writer)
    {
        Writer.Write(Key);
        Writer.Write(prepared);
        if (!prepared) return;
        Writer.Write(preparedStepKeys.Length);
        foreach (var step in preparedStepKeys) Writer.Write(step);
    }
}

internal readonly struct FinalGenerationEntry
{
    internal FinalGenerationEntry(GameObject root, string key, LevelGenStep[] steps,
        Peak.PropSpawner_AmuletStatues[] Amulets)
    {
        Root = root;
        Key = key;
        Steps = steps;
        this.Amulets = Amulets;
    }

    internal GameObject Root { get; }
    internal string Key { get; }
    internal LevelGenStep[] Steps { get; }
    internal Peak.PropSpawner_AmuletStatues[] Amulets { get; }
}

[DisallowMultipleComponent]
internal sealed class PllFinalGenerationDriver : MonoBehaviour, ILevelGenFinalizer
{
    Transform? owner;
    FinalGenerationEntry[] entries = Array.Empty<FinalGenerationEntry>();
    long epoch;
    bool prepared;
    bool completed;

    internal void Prepare(Transform featureRoot, long runEpoch, FinalGenerationEntry[] ownedEntries)
    {
        if (!featureRoot || transform != featureRoot || ownedEntries.Length == 0)
            throw new InvalidOperationException("A final-generation driver received an invalid owned entry plan.");
        owner = featureRoot;
        epoch = runEpoch;
        entries = ownedEntries;
        prepared = true;
        completed = false;
    }

    public void OnLevelGenFinished()
    {
        if (!prepared || completed || !owner || !NativeFeatureGeneration.IsCurrent(owner, epoch))
            throw new InvalidOperationException("A final-generation callback was duplicate or outside its prepared epoch.");
        foreach (var entry in entries)
        {
            if (!entry.Root || entry.Root.activeSelf)
                throw new InvalidOperationException($"Final root '{entry.Key}' changed before its callback.");
            entry.Root.SetActive(true);
            Physics.SyncTransforms();
            for (var i = 0; i < entry.Steps.Length; i++)
            {
                var step = entry.Steps[i];
                if (!step || !TransformOwnership.Contains(entry.Root.transform, step.transform)
                    || !FinalForceAuthoringContract.ActiveBelow(step.transform, entry.Root.transform))
                    throw new InvalidOperationException($"Final root '{entry.Key}' changed step {i} before execution.");
                step.ExecuteImmediately();
                foreach (var Amulet in entry.Amulets)
                    if (ReferenceEquals(Amulet, step)) Amulet.OnLevelGenFinished();
            }
            if (entry.Steps.Length != 0) Physics.SyncTransforms();
        }
        completed = true;
    }

    internal void VerifyCompleted(Transform featureRoot, long runEpoch)
    {
        if (!prepared || !completed || owner != featureRoot || epoch != runEpoch
            || !NativeFeatureGeneration.IsCurrent(featureRoot, runEpoch))
            throw new InvalidOperationException("PEAK did not complete the prepared final-generation callback exactly once.");
        foreach (var entry in entries)
            if (!entry.Root || !entry.Root.activeSelf)
                throw new InvalidOperationException($"Final root '{entry.Key}' was not retained after finalization.");
    }
}

internal sealed class ForceSpawnTarget
{
    readonly ForceSpawnExecution Execution;

    internal ForceSpawnTarget(
        PLLForceSpawner Marker,
        string Key,
        int FinalIndexValue,
        ForceSpawnResolution Resolution)
    {
        FinalIndex = FinalIndexValue;
        Execution = ForceSpawnExecution.Create(Marker, Key, Resolution);
    }

    internal GameObject Root => Execution.Root;
    internal int FinalIndex { get; }

    internal void ValidateContract(PLLForceSpawner Marker, string Key, int ExpectedFinalIndex)
    {
        if (FinalIndex != ExpectedFinalIndex)
            throw new InvalidOperationException($"Force marker '{Key}' changed its resolved final ownership.");
        Execution.ValidateContract(Marker, Key);
    }

    internal void Prepare(bool Selected) => Execution.Prepare(Selected);
    internal void Complete() => Execution.Complete();
    internal void Rollback() => Execution.Rollback();
    internal bool TryGetFinalStep(out LevelGenStep Step) => Execution.TryGetFinalStep(out Step);
    internal void WriteNetworkIdentity(GeneratedNetworkContract.ContractWriter Writer) => Execution.WriteNetworkIdentity(Writer);
    internal static void ClearCaptures() => ForceSpawnExecution.ClearCaptures();
}
