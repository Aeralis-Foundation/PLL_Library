using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PLL.Library;

internal static class DeterministicObjectIdentity
{
    internal static string RelativeComponentPath<T>(T component, Transform root, int rootIndex) where T : Component =>
        CanonicalRelativeComponentPath(rootIndex, RelativeComponentStructuralPath(component, root));

    internal static string CanonicalRelativeComponentPath(int RootIndex, string StructuralPath) =>
        RootIndex.ToString("X8", CultureInfo.InvariantCulture) + ":" + StructuralPath;

    internal static string RelativeComponentStructuralPath<T>(T Component, Transform Root) where T : Component =>
        RelativeComponentStructuralPath(Component, Root, typeof(T));

    internal static string RelativeTypedComponentStructuralPath(Component Component, Transform Root) =>
        RelativeComponentStructuralPath(Component, Root, Component.GetType());

    static string RelativeComponentStructuralPath(Component Component, Transform Root, Type ComponentType)
    {
        var Path = new List<int>();
        var Current = Component.transform;
        while (Current != Root)
        {
            if (!Current.parent)
                throw new InvalidOperationException($"A generated {ComponentType.Name} is outside its feature root.");
            Path.Add(Current.GetSiblingIndex());
            Current = Current.parent;
        }

        var Result = new StringBuilder();
        for (var i = Path.Count - 1; i >= 0; i--)
            Result.Append(Path[i].ToString("X8", CultureInfo.InvariantCulture)).Append('/');
        var Components = Component.gameObject.GetComponents(ComponentType);
        var Index = 0;
        while (Index < Components.Length && Components[Index] != Component) Index++;
        if (Index == Components.Length)
            throw new InvalidOperationException($"A generated {ComponentType.Name} lost its component index.");
        return Result.Append('#').Append(Index.ToString("X8", CultureInfo.InvariantCulture)).ToString();
    }

    internal static Transform? ResolveHierarchy(Transform Value, Transform SourceRoot, Transform TargetRoot)
    {
        var SiblingPath = new List<int>();
        var Current = Value;
        while (Current != SourceRoot)
        {
            if (!Current.parent) return null;
            SiblingPath.Add(Current.GetSiblingIndex());
            Current = Current.parent;
        }

        Current = TargetRoot;
        for (var PathIndex = SiblingPath.Count - 1; PathIndex >= 0; PathIndex--)
        {
            var ChildIndex = SiblingPath[PathIndex];
            if (ChildIndex < 0 || ChildIndex >= Current.childCount) return null;
            Current = Current.GetChild(ChildIndex);
        }
        return Current;
    }

    internal static string GlobalComponentPath<T>(T component) where T : Component
    {
        var name = typeof(T).Name;
        var scene = component.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException($"A {name} is outside every loaded scene.");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            var scenePath = scene.path ?? string.Empty;
            var sceneName = scene.name ?? string.Empty;
            writer.Write(scene.buildIndex);
            writer.Write(scenePath);
            writer.Write(sceneName);
            var occurrence = 0;
            var found = false;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var candidate = SceneManager.GetSceneAt(i);
                if (candidate.buildIndex != scene.buildIndex
                    || candidate.path != scenePath || candidate.name != sceneName) continue;
                if (candidate.handle == scene.handle)
                {
                    found = true;
                    break;
                }
                occurrence++;
            }
            if (!found && (scene.buildIndex != -1 || scenePath.Length != 0 || sceneName != "DontDestroyOnLoad"))
                throw new InvalidOperationException($"A {name} scene could not be canonically identified.");
            writer.Write(occurrence);

            var path = new List<int>();
            var current = component.transform;
            while (current.parent)
            {
                path.Add(current.GetSiblingIndex());
                current = current.parent;
            }
            var roots = scene.GetRootGameObjects();
            var root = Array.FindIndex(roots, value => value.transform == current);
            if (root < 0) throw new InvalidOperationException($"A {name} root could not be canonically identified.");
            writer.Write(root);
            writer.Write(path.Count);
            for (var i = path.Count - 1; i >= 0; i--) writer.Write(path[i]);

            var components = component.gameObject.GetComponents<T>();
            var index = Array.IndexOf(components, component);
            if (index < 0) throw new InvalidOperationException($"A {name} component could not be canonically identified.");
            writer.Write(index);
        }
        return Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    internal static byte[] HashPaths(string domain, IReadOnlyList<string> paths)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(3);
            writer.Write(domain);
            writer.Write(paths.Count);
            for (var i = 0; i < paths.Count; i++) writer.Write(paths[i]);
        }
        using var sha = SHA256.Create();
        return sha.ComputeHash(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    internal static byte[] HashObjectHierarchy(Transform root, string domain, int? LayerOverride = null)
    {
        if (!root) throw new ArgumentNullException(nameof(root));
        if (domain is null) throw new ArgumentNullException(nameof(domain));
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(1);
            writer.Write(domain);
            WriteNode(writer, root, root, LayerOverride);
        }
        using var sha = SHA256.Create();
        return sha.ComputeHash(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    static void WriteNode(BinaryWriter writer, Transform value, Transform root, int? LayerOverride)
    {
        writer.Write(value.name ?? string.Empty);
        writer.Write(value.gameObject.activeSelf);
        writer.Write(LayerOverride ?? value.gameObject.layer);
        WriteVector(writer, value.localPosition);
        WriteRotation(writer, value.localRotation);
        WriteVector(writer, value.localScale);

        var components = value.GetComponents<Component>();
        writer.Write(components.Length);
        foreach (var component in components)
        {
            if (!component)
            {
                writer.Write(string.Empty);
                continue;
            }
            writer.Write(StableTypeName(component.GetType()));
            if (component is FakeItem fakeItem)
            {
                writer.Write(fakeItem.itemName ?? string.Empty);
                writer.Write(fakeItem.realItemPrefab ? fakeItem.realItemPrefab.itemID : ushort.MaxValue);
            }
            if (component is Luggage luggage)
            {
                writer.Write(luggage.displayName ?? string.Empty);
                WriteFloat(writer, luggage.timeToOpen);
            }
            if (component is Spawner spawner) WriteSpawner(writer, spawner, root);
            else if (component is SingleItemSpawner single) WriteSingleSpawner(writer, single);
            if (component is MeshFilter meshFilter) WriteMesh(writer, meshFilter.sharedMesh);
            if (component is SkinnedMeshRenderer skinned) WriteMesh(writer, skinned.sharedMesh);
            if (component is MeshCollider meshCollider) WriteMesh(writer, meshCollider.sharedMesh);
            if (component is Renderer renderer) WriteMaterials(writer, renderer);
            if (component is Collider collider) WriteCollider(writer, collider);
            if (component is Rigidbody body)
            {
                writer.Write(body.isKinematic);
                writer.Write(body.useGravity);
                WriteFloat(writer, body.mass);
            }
        }

        writer.Write(value.childCount);
        for (var i = 0; i < value.childCount; i++) WriteNode(writer, value.GetChild(i), root, LayerOverride);
    }

    static void WriteSpawner(BinaryWriter writer, Spawner value, Transform root)
    {
        writer.Write(value.playersInRoomRequirement);
        writer.Write(value.belowAscentRequirement);
        writer.Write(value.normalRunsOnly);
        writer.Write((int)value.spawnMode);
        writer.Write((int)value.spawnPointMode);
        WriteFloat(writer, value.baseSpawnChance);
        WritePrefab(writer, value.spawnedObjectPrefab);
        writer.Write((int)value.spawnPool);
        writer.Write(value.canRepeatSpawns);
        writer.Write(value.spawnTransformIsSpawnerTransform);
        writer.Write(value.spawnAwayFromUpTarget);
        writer.Write(value.centerItemsVisually);
        writer.Write(value.spawnOnStart);
        writer.Write(value.isKinematic);
        WritePrefab(writer, value.fallbackSpawn);
        WriteTransform(writer, value.spawnUpTowardsTarget, root);

        writer.Write(value.spawnSpots?.Count ?? -1);
        if (value.spawnSpots is not null)
            foreach (var spot in value.spawnSpots) WriteTransform(writer, spot, root);
        writer.Write(value.weightedSpawnSpots?.Count ?? -1);
        if (value.weightedSpawnSpots is not null)
            foreach (var entry in value.weightedSpawnSpots)
            {
                writer.Write(entry.weight);
                writer.Write(entry.spawnSpots?.Count ?? -1);
                if (entry.spawnSpots is not null)
                    foreach (var spot in entry.spawnSpots) WriteTransform(writer, spot, root);
            }
        writer.Write(value.heightBasedSpawnPools?.Count ?? -1);
        if (value.heightBasedSpawnPools is null) return;
        foreach (var entry in value.heightBasedSpawnPools)
        {
            writer.Write((int)entry.spawnPool);
            WriteFloat(writer, entry.minimumHeight);
            WriteFloat(writer, entry.minimumZ);
            writer.Write(entry.hasBiomeRequirement);
            writer.Write((int)entry.biomeRequirement);
        }
    }

    static void WriteSingleSpawner(BinaryWriter writer, SingleItemSpawner value)
    {
        writer.Write(value.isKinematic);
        writer.Write(value.playersInRoomRequirement);
        writer.Write(value.belowAscentRequirement);
        WritePrefab(writer, value.prefab);
    }

    static void WritePrefab(BinaryWriter writer, GameObject? value)
    {
        writer.Write((bool)value);
        if (!value) return;
        writer.Write(value.name ?? string.Empty);
        if (value.TryGetComponent<Item>(out var item))
        {
            writer.Write(true);
            writer.Write(item.itemID);
        }
        else writer.Write(false);
        var components = value.GetComponents<Component>();
        writer.Write(components.Length);
        foreach (var component in components)
            writer.Write(component ? StableTypeName(component.GetType()) : string.Empty);
    }

    static void WriteTransform(BinaryWriter writer, Transform? value, Transform root)
    {
        writer.Write((bool)value);
        if (!value) return;
        var path = new List<int>();
        var current = value;
        while (current != root && current.parent)
        {
            path.Add(current.GetSiblingIndex());
            current = current.parent;
        }
        writer.Write(current == root);
        writer.Write(path.Count);
        for (var i = path.Count - 1; i >= 0; i--) writer.Write(path[i]);
        if (current == root) return;
        writer.Write(value.name ?? string.Empty);
        WriteVector(writer, value.position);
        WriteRotation(writer, value.rotation);
    }

    static void WriteMesh(BinaryWriter writer, Mesh? value)
    {
        writer.Write((bool)value);
        if (!value) return;
        writer.Write(value.name ?? string.Empty);
        writer.Write(value.vertexCount);
        writer.Write(value.subMeshCount);
    }

    static void WriteMaterials(BinaryWriter writer, Renderer value)
    {
        writer.Write(value.enabled);
        var Materials = value.sharedMaterials;
        writer.Write(Materials.Length);
        foreach (var Material in Materials)
        {
            writer.Write((bool)Material);
            if (!Material) continue;
            writer.Write(NativeFogRenderRuntime.CanonicalMaterial(Material).name ?? string.Empty);
            var Shader = Material.shader;
            writer.Write(Shader ? NativeFogRenderRuntime.CanonicalShader(Shader).name ?? string.Empty : string.Empty);
        }
    }

    static void WriteCollider(BinaryWriter writer, Collider value)
    {
        writer.Write(value.enabled);
        writer.Write(value.isTrigger);
        writer.Write(value.sharedMaterial ? value.sharedMaterial.name ?? string.Empty : string.Empty);
        if (value is BoxCollider box)
        {
            WriteVector(writer, box.center);
            WriteVector(writer, box.size);
        }
        else if (value is SphereCollider sphere)
        {
            WriteVector(writer, sphere.center);
            WriteFloat(writer, sphere.radius);
        }
        else if (value is CapsuleCollider capsule)
        {
            WriteVector(writer, capsule.center);
            WriteFloat(writer, capsule.radius);
            WriteFloat(writer, capsule.height);
            writer.Write(capsule.direction);
        }
        else if (value is MeshCollider mesh) writer.Write(mesh.convex);
        else if (value is TerrainCollider terrain)
            writer.Write(terrain.terrainData ? terrain.terrainData.name ?? string.Empty : string.Empty);
    }

    static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        WriteFloat(writer, value.x);
        WriteFloat(writer, value.y);
        WriteFloat(writer, value.z);
    }

    static void WriteRotation(BinaryWriter writer, Quaternion value)
    {
        if (value.w < 0f || value.w == 0f && (value.z < 0f
            || value.z == 0f && (value.y < 0f || value.y == 0f && value.x < 0f)))
            value = new Quaternion(-value.x, -value.y, -value.z, -value.w);
        WriteFloat(writer, value.x);
        WriteFloat(writer, value.y);
        WriteFloat(writer, value.z);
        WriteFloat(writer, value.w);
    }

    static void WriteFloat(BinaryWriter writer, float value)
    {
        if (value == 0f) value = 0f;
        writer.Write(float.IsNaN(value) ? int.MaxValue : BitConverter.SingleToInt32Bits(value));
    }

    internal static string StableTypeName(Type Value) =>
        $"{Value.Assembly.GetName().Name}:{Value.FullName ?? Value.Name}";

    internal static bool SameHash(byte[] Left, byte[] Right) => Left.AsSpan().SequenceEqual(Right);
}
