using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class CriticalGenerationContract
{
    readonly ComponentBinding<PLLCriticalGeneration> Marker;
    readonly ComponentBinding<LevelGenStep>[] Steps;
    readonly int Order;
    readonly string Key;

    CriticalGenerationContract(
        ComponentBinding<PLLCriticalGeneration> Marker,
        ComponentBinding<LevelGenStep>[] Steps, int Order, string Key)
    {
        this.Marker = Marker; this.Steps = Steps;
        this.Order = Order; this.Key = Key;
    }

    internal static CriticalGenerationContract[] CreateAll(
        PllBiomePrefabAnchors Anchors, BiomeId Id, AuthoringCandidateGraph Graph)
    {
        foreach (var Candidate in Graph.Ordered)
            if (Candidate.GetComponentInChildren<PLLCriticalGeneration>(true))
                throw new ArgumentException($"Biome '{Id}' Critical Generation cannot occur in a candidate prefab.", nameof(Anchors));
        var Sources = Anchors.GetComponentsInChildren<PLLCriticalGeneration>(true);
        var Result = new CriticalGenerationContract[Sources.Length];
        for (var Index = 0; Index < Sources.Length; Index++)
        {
            var Source = Sources[Index];
            var Key = FinalForceAuthoringContract.Key(Anchors.transform, Source);
            var Context = $"Biome '{Id}' Critical Generation '{Key}'";
            PllCriticalGenerationContract.ValidateMarker(Source, Context);
            var SourcesOfSteps = PllCriticalGenerationContract.ValidateScope(
                Anchors, Source.transform, Context, false);
            var Steps = new ComponentBinding<LevelGenStep>[SourcesOfSteps.Length];
            for (var StepIndex = 0; StepIndex < Steps.Length; StepIndex++)
                Steps[StepIndex] = ComponentBinding<LevelGenStep>.Create(
                    Anchors.transform, (LevelGenStep)SourcesOfSteps[StepIndex]);
            Result[Index] = new CriticalGenerationContract(
                ComponentBinding<PLLCriticalGeneration>.Create(Anchors.transform, Source),
                Steps, Source.Order, Key);
        }
        Array.Sort(Result, (Left, Right) =>
        {
            var Order = Left.Order.CompareTo(Right.Order);
            return Order != 0 ? Order : StringComparer.Ordinal.Compare(Left.Key, Right.Key);
        });
        return Result;
    }

    internal void Validate(Transform Template, BiomeId Id)
    {
        Marker.Validate(Template, Id, "Critical Generation marker");
        if (Marker.Source.Order != Order)
            throw new InvalidOperationException($"Biome '{Id}' changed its Critical Generation order before sealing.");
        var Context = $"Biome '{Id}' Critical Generation '{Key}'";
        PllCriticalGenerationContract.ValidateMarker(Marker.Source, Context);
        var CurrentSteps = PllCriticalGenerationContract.ValidateScope(
            Template.GetComponent<PllBiomePrefabAnchors>(), Marker.Source.transform, Context, false);
        if (CurrentSteps.Length != Steps.Length)
            throw new InvalidOperationException($"Biome '{Id}' changed its Critical Generation step membership before sealing.");
        for (var Index = 0; Index < Steps.Length; Index++)
            if (!ReferenceEquals(CurrentSteps[Index], Steps[Index].Source))
                throw new InvalidOperationException($"Biome '{Id}' changed its Critical Generation step membership before sealing.");
        foreach (var Step in Steps) Step.Validate(Template, Id, "Critical Generation step");
    }

    internal void Resolve(Transform Root, BiomeId Id, Dictionary<LevelGenStep, int> Ranks, int Rank)
    {
        Marker.Resolve(Root, Id, "Critical Generation marker");
        foreach (var Step in Steps)
            Ranks.Add(Step.Resolve(Root, Id, "Critical Generation step"), Rank);
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Marker.WriteManifest(Writer); Writer.Write(Order); Writer.Write(Key);
        Writer.Write(Steps.Length);
        foreach (var Step in Steps) Step.WriteManifest(Writer);
    }
}
