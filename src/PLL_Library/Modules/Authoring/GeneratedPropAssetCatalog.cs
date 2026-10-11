using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class GeneratedPropAssetCatalog
{
    readonly byte[] CanonicalBytes;
    readonly GameObject[] Prefabs;
    readonly Dictionary<GameObject, int> PrefabOrdinals;
    readonly Dictionary<Mesh, int> MeshOrdinals;
    readonly Dictionary<Material, int> MaterialOrdinals;
    readonly Dictionary<PhysicsMaterial, int> PhysicsMaterialOrdinals;

    GeneratedPropAssetCatalog(
        byte[] CanonicalBytes, GameObject[] Prefabs,
        Dictionary<GameObject, int> PrefabOrdinals, Dictionary<Mesh, int> MeshOrdinals,
        Dictionary<Material, int> MaterialOrdinals, Dictionary<PhysicsMaterial, int> PhysicsMaterialOrdinals)
    {
        this.CanonicalBytes = CanonicalBytes;
        this.Prefabs = Prefabs;
        this.PrefabOrdinals = PrefabOrdinals;
        this.MeshOrdinals = MeshOrdinals;
        this.MaterialOrdinals = MaterialOrdinals;
        this.PhysicsMaterialOrdinals = PhysicsMaterialOrdinals;
    }

    internal GameObject Prefab(int Ordinal) => Prefabs[Ordinal];
    internal int PrefabOrdinal(GameObject Prefab) => PrefabOrdinals.TryGetValue(Prefab, out var Ordinal)
        ? Ordinal : throw new InvalidOperationException("Hooked generation selected an unsealed prefab.");
    internal bool TryMeshOrdinal(Mesh Mesh, out int Ordinal) => MeshOrdinals.TryGetValue(Mesh, out Ordinal);
    internal int MaterialOrdinal(Material Material) => MaterialOrdinals.TryGetValue(Material, out var Ordinal)
        ? Ordinal : throw new InvalidOperationException("Hooked generation produced an unsealed material.");
    internal bool TryPhysicsMaterialOrdinal(PhysicsMaterial Material, out int Ordinal) => PhysicsMaterialOrdinals.TryGetValue(Material, out Ordinal);

    internal void WriteManifest(BinaryWriter Writer) => Writer.Write(CanonicalBytes);

    internal static GeneratedPropAssetCatalog? Create(
        BiomeId Id, byte[]? Bytes, GameObject[]? Prefabs, Mesh[]? Meshes,
        Material[]? Materials, PhysicsMaterial[]? PhysicsMaterials,
        IReadOnlyList<Transform> Roots, AuthoringCandidateGraph Graph,
        out int[][] AllowedPrefabOrdinals)
    {
        AllowedPrefabOrdinals = Array.Empty<int[]>();
        if (Roots.Count == 0)
        {
            if (Bytes is { Length: > 0 } || Prefabs is { Length: > 0 } || Meshes is { Length: > 0 }
                || Materials is { Length: > 0 } || PhysicsMaterials is { Length: > 0 })
                throw new ArgumentException($"Biome '{Id}' has a generated prop catalog without a Hooked generation source.");
            return null;
        }
        if (Bytes is not { Length: > 0 } || Prefabs is null || Meshes is null || Materials is null || PhysicsMaterials is null)
            throw new ArgumentException($"Biome '{Id}' is missing its compiled generated prop asset catalog.");
        var Tables = GeneratedPropAssetCatalogCodec.Decode(Bytes);
        var AllowedPrefabs = new HashSet<GameObject> { Graph.SourceRoot };
        var AllowedMeshes = new HashSet<Mesh>();
        var AllowedMaterials = new HashSet<Material>();
        var AllowedPhysicsMaterials = new HashSet<PhysicsMaterial>();
        var TargetPrefabs = new HashSet<GameObject>[Roots.Count];
        var Pending = new Queue<GameObject>();
        for (var i = 0; i < Roots.Count; i++)
        {
            var Root = Roots[i];
            var Target = TargetPrefabs[i] = new HashSet<GameObject>();
            AddGeometry(Root, false);
            foreach (var Spawner in Root.GetComponentsInChildren<PropSpawner>(true))
                if (NativePrefabContracts.EffectiveProps(Spawner) is { } Candidates)
                    foreach (var Candidate in Candidates) AddPrefab(Candidate, Target);
            while (Pending.Count != 0)
                foreach (var Candidate in Graph.CandidatesFrom(Pending.Dequeue())) AddPrefab(Candidate, Target);
        }
        var PrefabOrdinals = Bind(Prefabs, Tables.Prefabs.Length, AllowedPrefabs, "prefab");
        var MeshOrdinals = Bind(Meshes, Tables.Meshes.Length, AllowedMeshes, "mesh");
        var MaterialOrdinals = Bind(Materials, Tables.Materials.Length, AllowedMaterials, "material");
        var PhysicsMaterialOrdinals = Bind(PhysicsMaterials, Tables.PhysicsMaterials.Length, AllowedPhysicsMaterials, "physics material");
        AllowedPrefabOrdinals = new int[Roots.Count][];
        for (var i = 0; i < TargetPrefabs.Length; i++)
        {
            var Ordinals = AllowedPrefabOrdinals[i] = new int[TargetPrefabs[i].Count];
            var Index = 0;
            foreach (var Prefab in TargetPrefabs[i]) Ordinals[Index++] = PrefabOrdinals[Prefab];
            Array.Sort(Ordinals);
        }
        return new GeneratedPropAssetCatalog((byte[])Bytes.Clone(), (GameObject[])Prefabs.Clone(),
            PrefabOrdinals, MeshOrdinals, MaterialOrdinals, PhysicsMaterialOrdinals);

        void AddPrefab(GameObject Prefab, HashSet<GameObject> Target)
        {
            if (!Target.Add(Prefab)) return;
            if (AllowedPrefabs.Add(Prefab))
            {
                if (!Graph.Contains(Prefab))
                    throw new ArgumentException($"Biome '{Id}' Hooked generation references a foreign prefab.");
                PllHookedPropGenerationSourceContract.ValidateCandidate(Prefab.transform,
                    static Component => Component is IGenConfigStep or Peak.Island.GloomVoiceConfuser);
                AddGeometry(Prefab.transform, true);
            }
            Pending.Enqueue(Prefab);
        }

        void AddGeometry(Transform Root, bool Candidate)
        {
            foreach (var Component in Root.GetComponentsInChildren<Component>(true))
            {
                if (Candidate && (!Component || PllHookedPropGenerationSourceContract.UnsupportedPresentation(Component)))
                    throw new ArgumentException($"Biome '{Id}' Hooked generation references unsupported presentation.");
                if (Component is MeshFilter Filter && Filter.sharedMesh) AllowedMeshes.Add(Filter.sharedMesh);
                if (Component is RockMaterialSwapper Swapper && Swapper.mat) AllowedMaterials.Add(Swapper.mat);
                if (Component is Renderer Renderer)
                {
                    if (Renderer is SkinnedMeshRenderer Skinned && Skinned.sharedMesh) AllowedMeshes.Add(Skinned.sharedMesh);
                    foreach (var Material in Renderer.sharedMaterials)
                        if (Material) AllowedMaterials.Add(Material);
                }
                if (Component is Collider Collider)
                {
                    if (Collider is MeshCollider MeshCollider && MeshCollider.sharedMesh) AllowedMeshes.Add(MeshCollider.sharedMesh);
                    if (Collider.sharedMaterial) AllowedPhysicsMaterials.Add(Collider.sharedMaterial);
                }
            }
        }

        Dictionary<T, int> Bind<T>(T[] Assets, int Count, HashSet<T> Reachable, string Name) where T : UnityEngine.Object
        {
            if (Assets.Length != Count || Assets.Length != Reachable.Count)
                throw new ArgumentException($"Biome '{Id}' generated prop {Name} catalog does not match its reachable assets.");
            var Ordinals = new Dictionary<T, int>(Count);
            for (var i = 0; i < Assets.Length; i++)
                if (!Assets[i] || Assets[i].GetType() != typeof(T) || !Reachable.Contains(Assets[i]) || !Ordinals.TryAdd(Assets[i], i))
                    throw new ArgumentException($"Biome '{Id}' generated prop {Name} catalog contains a missing, duplicate, or foreign asset.");
            return Ordinals;
        }
    }
}
