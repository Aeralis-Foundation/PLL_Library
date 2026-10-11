using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using static PLL.Creator.Editor.PllBuildArtifacts;
using static PLL.Creator.Editor.PllBundleBuilder;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal sealed class PllHookedPropGenerationCompiler
    {
        internal const string CatalogField = "BundleGeneratedPropAssetCatalog";
        internal const string PrefabsField = "BundleGeneratedPropPrefabs";
        internal const string MeshesField = "BundleGeneratedPropMeshes";
        internal const string MaterialsField = "BundleGeneratedPropMaterials";
        internal const string PhysicsMaterialsField = "BundleGeneratedPropPhysicsMaterials";
        internal readonly PllHookedPropGenerationAuthoring[] Sources;
        readonly PrefabGraph Graph;
        readonly PrefabNode[] Candidates;
        readonly Dictionary<GameObject, GeneratedPropAssetCatalogCodec.Entry> SourceAssets = new Dictionary<GameObject, GeneratedPropAssetCatalogCodec.Entry>();

        internal PllHookedPropGenerationCompiler(
            PllBiomeDefinition Definition, PrefabGraph Graph, IReadOnlyList<PllEffectZoneAuthoring> EffectZones)
        {
            this.Graph = Graph;
            Sources = DiscoverGraphComponents<PllHookedPropGenerationAuthoring>(Graph);
            if (Sources.Length > 256)
                throw new InvalidOperationException("Hooked generation exceeds 256 source targets.");
            var ActionOwners = new Dictionary<PllHookedPropGenerationAuthoring, PllEffectZoneAuthoring>();
            foreach (var Zone in EffectZones)
                PllHookedPropGenerationSourceContract.ValidateActions(Zone,
                    !TransformOwnership.Contains(Definition.RootPrefab.transform, Zone.transform), Sources, ActionOwners);
            var Reachable = new HashSet<string>(StringComparer.Ordinal);
            var Pending = new Queue<PrefabNode>();
            var UnstableRoots = Sources.Length == 0 ? Array.Empty<Transform>()
                : PllHookedPropGenerationSourceContract.UnstableRoots(Definition);
            foreach (var Source in Sources)
            {
                if (!TransformOwnership.Contains(Definition.RootPrefab.transform, Source.transform))
                    throw new InvalidOperationException("Hooked generation requires stable biome source targets.");
                PllHookedPropGenerationSourceContract.Validate(Definition, Source, false, UnstableRoots);
                if (!PllAuthoringValueContract.IsHook(Source.EmitHook))
                    throw new InvalidOperationException("Hooked generation requires a valid Emit Hook.");
                foreach (var Edge in Graph.ByPath[Graph.RootPath].Edges)
                    if (Edge.Spawner && TransformOwnership.Contains(Source.transform, Edge.Spawner.transform)) Add(Edge.TargetPath);
            }
            while (Pending.Count != 0)
            {
                var Node = Pending.Dequeue();
                PllHookedPropGenerationSourceContract.ValidateCandidate(Node.SourceRoot.transform);
                foreach (var Edge in Node.Edges) Add(Edge.TargetPath);
            }
            Candidates = Array.FindAll(Graph.Nodes, Node => Reachable.Contains(Node.SourcePath));
            if (Sources.Length != 0)
            {
                SourceAssets.Add(Definition.RootPrefab, Identity(Definition.RootPrefab));
                foreach (var Node in Candidates) SourceAssets.Add(Node.SourceRoot, Identity(Node.SourceRoot));
            }

            void Add(string Path)
            {
                if (Reachable.Add(Path)) Pending.Enqueue(Graph.ByPath[Path]);
            }
        }

        internal void Translate(GameObject StagedRoot)
        {
            foreach (var Source in Sources)
            {
                var Marker = MapComponent(Graph.ByPath[Graph.RootPath].SourceRoot,
                    StagedRoot, Source, "Hooked generation source");
                if (Marker.GetComponent<PropSpawner>()) Marker.gameObject.AddComponent<PropGrouper>();
            }
        }

        internal CompletedCatalog Seal(PllBiomeDefinition Staged, IReadOnlyDictionary<string, string> StagedPaths)
        {
            if (Sources.Length == 0)
            {
                using (var Empty = new SerializedObject(Staged))
                {
                    Property(Empty, CatalogField).arraySize = 0;
                    Property(Empty, PrefabsField).arraySize = 0;
                    Property(Empty, MeshesField).arraySize = 0;
                    Property(Empty, MaterialsField).arraySize = 0;
                    Property(Empty, PhysicsMaterialsField).arraySize = 0;
                    Empty.ApplyModifiedPropertiesWithoutUndo();
                }
                return CompletedCatalog.Empty;
            }
            var Prefabs = new List<(GeneratedPropAssetCatalogCodec.Entry Entry, GameObject Asset)>();
            Prefabs.Add((SourceAssets[Graph.ByPath[Graph.RootPath].SourceRoot], Staged.RootPrefab));
            foreach (var Source in SourceAssets)
            {
                var Current = Identity(Source.Key);
                if (GeneratedPropAssetCatalogCodec.Compare(Current, Source.Value) != 0
                    || Current.DependencyHash != Source.Value.DependencyHash)
                    throw new InvalidOperationException("Hooked generation source assets changed while staging.");
            }
            var Meshes = new HashSet<Mesh>();
            var Materials = new HashSet<Material>();
            var PhysicsMaterials = new HashSet<PhysicsMaterial>();
            var UnstableRoots = PllHookedPropGenerationSourceContract.UnstableRoots(Staged);
            foreach (var Source in Sources)
            {
                var Marker = MapComponent(Graph.ByPath[Graph.RootPath].SourceRoot,
                    Staged.RootPrefab, Source, "Hooked generation source");
                PllHookedPropGenerationSourceContract.Validate(Staged, Marker, true, UnstableRoots);
                AddGeometry(Marker.transform);
            }
            foreach (var Node in Candidates)
            {
                var Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StagedPaths[Node.SourcePath]);
                if (!Prefab) throw new InvalidOperationException("A staged Hooked candidate is missing.");
                Prefabs.Add((SourceAssets[Node.SourceRoot], Prefab));
                AddGeometry(Prefab.transform);
            }
            Prefabs.Sort((Left, Right) => GeneratedPropAssetCatalogCodec.Compare(Left.Entry, Right.Entry));
            using var Serialized = new SerializedObject(Staged);
            var PrefabTable = WriteTable(Prefabs, PrefabsField);
            var MeshTable = WriteTable(Ordered(Meshes), MeshesField);
            var MaterialTable = WriteTable(Ordered(Materials), MaterialsField);
            var PhysicsMaterialTable = WriteTable(Ordered(PhysicsMaterials), PhysicsMaterialsField);
            var Tables = new GeneratedPropAssetCatalogCodec.Tables(PrefabTable.Entries,
                MeshTable.Entries, MaterialTable.Entries, PhysicsMaterialTable.Entries);
            var Bytes = GeneratedPropAssetCatalogCodec.Encode(Tables);
            var Catalog = Property(Serialized, CatalogField);
            Catalog.arraySize = Bytes.Length;
            for (var i = 0; i < Bytes.Length; i++) Catalog.GetArrayElementAtIndex(i).intValue = Bytes[i];
            Serialized.ApplyModifiedPropertiesWithoutUndo();
            return new CompletedCatalog(Bytes, PrefabTable.Assets,
                MeshTable.Assets, MaterialTable.Assets, PhysicsMaterialTable.Assets);

            void AddGeometry(Transform Root)
            {
                foreach (var Component in Root.GetComponentsInChildren<Component>(true))
                {
                    if (Component is MeshFilter Filter && Filter.sharedMesh) Meshes.Add(Filter.sharedMesh);
                    if (Component is RockMaterialSwapper Swapper && Swapper.mat) Materials.Add(Swapper.mat);
                    if (Component is Renderer Renderer)
                    {
                        if (Renderer is SkinnedMeshRenderer Skinned && Skinned.sharedMesh) Meshes.Add(Skinned.sharedMesh);
                        foreach (var Material in Renderer.sharedMaterials) if (Material) Materials.Add(Material);
                    }
                    if (Component is Collider Collider)
                    {
                        if (Collider is MeshCollider MeshCollider && MeshCollider.sharedMesh) Meshes.Add(MeshCollider.sharedMesh);
                        if (Collider.sharedMaterial) PhysicsMaterials.Add(Collider.sharedMaterial);
                    }
                }
            }

            (GeneratedPropAssetCatalogCodec.Entry[] Entries, T[] Assets) WriteTable<T>(
                List<(GeneratedPropAssetCatalogCodec.Entry Entry, T Asset)> Values, string Field) where T : UnityEngine.Object
            {
                var PropertyValue = Property(Serialized, Field);
                PropertyValue.arraySize = Values.Count;
                var Entries = new GeneratedPropAssetCatalogCodec.Entry[Values.Count];
                var Assets = new T[Values.Count];
                for (var i = 0; i < Values.Count; i++)
                {
                    Entries[i] = Values[i].Entry;
                    Assets[i] = Values[i].Asset;
                    PropertyValue.GetArrayElementAtIndex(i).objectReferenceValue = Assets[i];
                }
                return (Entries, Assets);
            }
        }

        internal sealed class CompletedCatalog
        {
            internal static readonly CompletedCatalog Empty = new CompletedCatalog(Array.Empty<byte>(),
                Array.Empty<GameObject>(), Array.Empty<Mesh>(), Array.Empty<Material>(), Array.Empty<PhysicsMaterial>());

            internal CompletedCatalog(byte[] Bytes, GameObject[] Prefabs, Mesh[] Meshes,
                Material[] Materials, PhysicsMaterial[] PhysicsMaterials)
            {
                this.Bytes = Bytes; this.Prefabs = Array.AsReadOnly(Prefabs);
                this.Meshes = Array.AsReadOnly(Meshes); this.Materials = Array.AsReadOnly(Materials);
                this.PhysicsMaterials = Array.AsReadOnly(PhysicsMaterials);
            }

            internal ReadOnlyMemory<byte> Bytes { get; }
            internal IReadOnlyList<GameObject> Prefabs { get; }
            internal IReadOnlyList<Mesh> Meshes { get; }
            internal IReadOnlyList<Material> Materials { get; }
            internal IReadOnlyList<PhysicsMaterial> PhysicsMaterials { get; }
        }

        static List<(GeneratedPropAssetCatalogCodec.Entry Entry, T Asset)> Ordered<T>(HashSet<T> Assets) where T : UnityEngine.Object
        {
            var Result = new List<(GeneratedPropAssetCatalogCodec.Entry, T)>(Assets.Count);
            foreach (var Asset in Assets) Result.Add((Identity(Asset), Asset));
            Result.Sort((Left, Right) => GeneratedPropAssetCatalogCodec.Compare(Left.Item1, Right.Item1));
            return Result;
        }

        static GeneratedPropAssetCatalogCodec.Entry Identity(UnityEngine.Object Asset)
        {
            if (!EditorUtility.IsPersistent(Asset)
                || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(Asset, out string Guid, out long LocalId))
                throw new InvalidOperationException("Hooked generation references a nonpersistent asset.");
            return new GeneratedPropAssetCatalogCodec.Entry(Guid, LocalId,
                AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(Asset)).ToString());
        }
    }
}
