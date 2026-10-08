using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class AuthoringCandidateGraph
{
    readonly Dictionary<GameObject, GameObject[]> References;
    readonly ReadOnlyMemory<byte>[] CandidateFingerprints;

    AuthoringCandidateGraph(GameObject Root, GameObject[] ordered, Dictionary<GameObject, GameObject[]> References)
    {
        Ordered = ordered;
        this.References = References;
        CandidateFingerprints = new ReadOnlyMemory<byte>[ordered.Length];
        SourceRoot = Root;
    }

    internal IReadOnlyList<GameObject> Ordered { get; }
    internal GameObject SourceRoot { get; }
    internal bool Contains(GameObject value) => value
        && value != SourceRoot && References.ContainsKey(value);
    internal IReadOnlyList<GameObject> CandidatesFrom(GameObject Root) => References[Root];

    internal ReadOnlyMemory<byte> CaptureFingerprint(int CandidateIndex)
    {
        if (CandidateFingerprints[CandidateIndex].IsEmpty)
            CandidateFingerprints[CandidateIndex] = DeterministicObjectIdentity.HashObjectHierarchy(
                Ordered[CandidateIndex].transform, "PLL/AuthoringCandidate/v1");
        return CandidateFingerprints[CandidateIndex];
    }

    internal static AuthoringCandidateGraph Create(Transform template, BiomeId id)
    {
        if (!template) throw new ArgumentException($"Biome '{id}' candidate graph has no template root.", nameof(template));
        var ordered = new List<GameObject>();
        var pending = new Queue<GameObject>();
        var seen = new HashSet<GameObject> { template.gameObject };
        var references = new Dictionary<GameObject, GameObject[]>();
        var parentSpawners = new List<PropSpawner>();
        EnqueueReferences(template.gameObject);
        while (pending.Count != 0) EnqueueReferences(pending.Dequeue());
        ValidateAcyclic(template.gameObject);
        ValidateChildEdges();
        return new AuthoringCandidateGraph(template.gameObject, ordered.ToArray(), references);

        void EnqueueReferences(GameObject owner)
        {
            var outgoing = new List<GameObject>();
            foreach (var spawner in owner.GetComponentsInChildren<PropSpawner>(true))
            {
                if (!spawner) throw new ArgumentException($"Biome '{id}' candidate graph contains a destroyed PropSpawner.", nameof(template));
                parentSpawners.Add(spawner);
                var Values = NativePrefabContracts.EffectiveProps(spawner);
                if (Values is null) continue;
                foreach (var Value in Values) Enqueue(Value, "PropSpawner", outgoing);
            }
            foreach (var child in owner.GetComponentsInChildren<PllChildPropAuthoring>(true))
            {
                if (!child) throw new ArgumentException($"Biome '{id}' candidate graph contains destroyed child-prop authoring.", nameof(template));
                foreach (var value in child.Props) Enqueue(value, "child-prop authoring", outgoing);
            }
            foreach (var Zone in owner.GetComponentsInChildren<PllEntityZoneAuthoring>(true))
                if (Zone.Source is PllAuthoredEntityZoneSource Authored)
                    Enqueue(Authored.Prefab, "authored Entity Zone", outgoing);
            foreach (var Zone in owner.GetComponentsInChildren<PllHazardZoneAuthoring>(true))
                if (Zone.Source is PllAuthoredHazardZoneSource Authored)
                    Enqueue(Authored.Prefab, "authored Hazard Zone", outgoing);
            references.Add(owner, outgoing.ToArray());
        }

        void Enqueue(GameObject? value, string source, ICollection<GameObject> outgoing)
        {
            if (!value) throw new ArgumentException($"Biome '{id}' {source} contains a missing candidate prefab.", nameof(template));
            outgoing.Add(value);
            if (!seen.Add(value)) return;
            ordered.Add(value);
            pending.Enqueue(value);
        }

        void ValidateAcyclic(GameObject root)
        {
            var states = new Dictionary<GameObject, byte>();
            Visit(root);

            void Visit(GameObject value)
            {
                if (states.TryGetValue(value, out var state))
                {
                    if (state == 1)
                        throw new ArgumentException($"Biome '{id}' reachable candidate graph contains a cycle at '{value.name}'.", nameof(template));
                    return;
                }
                states.Add(value, 1);
                foreach (var candidate in references[value]) Visit(candidate);
                states[value] = 2;
            }
        }

        void ValidateChildEdges()
        {
            foreach (var spawner in parentSpawners)
            {
                var timings = new HashSet<PllChildPropTiming>();
                var Values = NativePrefabContracts.EffectiveProps(spawner);
                if (Values is not null)
                    foreach (var Value in Values)
                    {
                        var Child = Value.GetComponent<PllChildPropAuthoring>();
                        if (Child) timings.Add(Child.Timing);
                    }
                if (timings.Count == 0) continue;
                if (timings.Count != 1)
                    throw new ArgumentException($"Biome '{id}' PropSpawner '{spawner.name}' selects child-prop candidates with conflicting timings.", nameof(template));
                var timing = First(timings);
                var immediate = CountImmediate(spawner.modifiers, out var invalidImmediate);
                var late = CountLate(spawner.postSpawnBehaviors, out var invalidLate);
                if (invalidImmediate || invalidLate
                    || timing == PllChildPropTiming.Immediate && (immediate != 1 || late != 0)
                    || timing == PllChildPropTiming.Late && (immediate != 0 || late != 1)
                    || timing is not (PllChildPropTiming.Immediate or PllChildPropTiming.Late))
                    throw new ArgumentException($"Biome '{id}' PropSpawner '{spawner.name}' has an invalid staged child-spawner mechanism.", nameof(template));
            }
        }

        static PllChildPropTiming First(IEnumerable<PllChildPropTiming> values)
        {
            foreach (var value in values) return value;
            throw new InvalidOperationException("A child-prop timing set was unexpectedly empty.");
        }

        static int CountImmediate(IReadOnlyList<PropSpawnerMod>? values, out bool invalid)
        {
            var count = 0;
            invalid = false;
            if (values is null) return 0;
            foreach (var value in values)
                if (value is PSM_ChildSpawners)
                {
                    count++;
                    invalid |= value.mute;
                }
            return count;
        }

        static int CountLate(IReadOnlyList<PostSpawnBehavior>? values, out bool invalid)
        {
            var count = 0;
            invalid = false;
            if (values is null) return 0;
            foreach (var value in values)
                if (value is PSB_ChildSpawners)
                {
                    count++;
                    invalid |= value.mute;
                }
            return count;
        }
    }
}
