using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PLL.Creator.Editor
{
    internal static class PllPresentationDependencySealBuilder
    {
        internal static byte[] Create(
            DefinitionCatalog Catalog,
            IReadOnlyList<PllFogShaderAdapter> FogShaderAdapters, Transform IdentityRoot)
        {
            var Dependencies = new Dictionary<(string Guid, long LocalId, string Type), string>();
            foreach (var Adapter in FogShaderAdapters)
            {
                AddDependency(Adapter.OriginalShader); AddDependency(Adapter.FogShader);
            }
            AddComponents(Catalog.SurfaceTextures); AddComponents(Catalog.BreakableProps);
            AddComponents(Catalog.SwingingProps); AddComponents(Catalog.Vines);
            AddComponents(Catalog.RestPoints); AddComponents(Catalog.HangingRopes);
            foreach (var Visual in Catalog.CustomVisuals)
                foreach (var Component in Visual.Authoring.GetComponentsInChildren<Component>(true))
                {
                    if (!Component || Component is Transform || Component == Visual.Authoring) continue;
                    using var Serialized = new SerializedObject(Component);
                    using var Property = Serialized.GetIterator();
                    while (Property.Next(true))
                    {
                        if (Property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        AddDependency(Property.objectReferenceValue);
                    }
                }
            if (Catalog.Atmosphere)
                PllAtmosphereAuthoringValidator.VisitDependencies(Catalog.Atmosphere, AddDependency);
            foreach (var Zone in Catalog.EntityZones)
                if (Zone.Source is PllPluginEntityZoneSource Plugin)
                {
                    var Path = AssetDatabase.GetAssetPath(Plugin.Prefab);
                    if (!EditorUtility.IsPersistent(Plugin.Prefab)
                        || !Path.StartsWith("Assets/", StringComparison.Ordinal)
                            && !Path.StartsWith("Packages/", StringComparison.Ordinal))
                        throw new InvalidOperationException("Entity prototypes must be imported prefab assets.");
                    AddAssetIdentity(Plugin.Prefab, Path);
                }
                else if (PllNativeEntityAppearanceValueContract.Appearance(Zone.Source) is { } Appearance)
                    foreach (var Renderer in Appearance.Renderers)
                    {
                        AddAppearanceDependency(Renderer.Mesh);
                        foreach (var Material in Renderer.Materials) AddAppearanceDependency(Material);
                    }

            foreach (var Resolver in Catalog.NativeResolvers)
            {
                if (Resolver.Form != PllNativeResolverForm.Source && Resolver.Kind is not (PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid)) continue;
                AddDependency(Resolver.FluidDefinition); AddDependency(Resolver.EmitterModel);
                AddDependency(Resolver.SurfaceRenderer);
                AddDependency(Resolver.GeneratedSurfaceMaterial);
                if (Resolver.SurfaceRenderer)
                {
                    var Filter = Resolver.SurfaceRenderer.GetComponent<MeshFilter>();
                    if (Filter) AddDependency(Filter.sharedMesh);
                    foreach (var Material in Resolver.SurfaceRenderer.sharedMaterials) AddDependency(Material);
                }
                if (Resolver.Form != PllNativeResolverForm.Source) continue;
                var Settings = Resolver.SourceSettings;
                AddDependency(Settings.Material); AddDependency(Settings.PusherSfx);
                AddDependency(Settings.EndRock); AddDependency(Settings.Splash); AddDependency(Settings.SpawnAlongSpline);
                if (Settings.DetailsAlongSpline != null)
                    foreach (var Detail in Settings.DetailsAlongSpline) AddDependency(Detail);
            }
            var DependencyHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            var Entries = new PllDependencySeal.Entry[Dependencies.Count];
            var Index = 0;
            foreach (var Dependency in Dependencies)
            {
                if (!DependencyHashes.TryGetValue(Dependency.Value, out var DependencyHash))
                {
                    DependencyHash = AssetDatabase.GetAssetDependencyHash(Dependency.Value).ToString();
                    DependencyHashes.Add(Dependency.Value, DependencyHash);
                }
                Entries[Index++] = new PllDependencySeal.Entry(
                    Dependency.Key.Guid,
                    Dependency.Key.LocalId,
                    Dependency.Key.Type,
                    DependencyHash);
            }
            return PllDependencySeal.CreateHash(Entries);

            void AddComponents<T>(IReadOnlyList<T> Sources) where T : Component
            {
                foreach (var Source in Sources)
                {
                    using var Serialized = new SerializedObject(Source);
                    using var Property = Serialized.GetIterator();
                    while (Property.Next(true))
                        if (Property.propertyType == SerializedPropertyType.ObjectReference) AddDependency(Property.objectReferenceValue);
                }
            }

            void AddAppearanceDependency(Object Dependency)
            {
                var Path = AssetDatabase.GetAssetPath(Dependency);
                if (!Dependency || !EditorUtility.IsPersistent(Dependency)
                    || !Path.StartsWith("Assets/", StringComparison.Ordinal)
                        && !Path.StartsWith("Packages/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Entity appearance meshes and materials must be imported assets.");
                AddAssetIdentity(Dependency, Path);
            }

            void AddDependency(Object Dependency)
            {
                if (!Dependency || !EditorUtility.IsPersistent(Dependency)) return;
                var DependencyTransform = Dependency is GameObject DependencyObject
                    ? DependencyObject.transform
                    : Dependency is Component DependencyComponent
                        ? DependencyComponent.transform
                        : null;
                if (DependencyTransform && TransformOwnership.Contains(IdentityRoot, DependencyTransform)) return;
                var Path = AssetDatabase.GetAssetPath(Dependency);
                if (!Path.StartsWith("Assets/", StringComparison.Ordinal)
                    && !Path.StartsWith("Packages/", StringComparison.Ordinal)) return;
                AddAssetIdentity(Dependency, Path);
            }

            void AddAssetIdentity(Object Dependency, string Path)
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    Dependency,
                    out var Guid,
                    out long LocalId))
                    throw new InvalidOperationException(
                        "Presentation dependency '" + Path + "' has no stable GUID and local file ID.");
                var Type = Dependency.GetType();
                var TypeName = Type.Assembly.GetName().Name + ":" + (Type.FullName ?? Type.Name);
                Dependencies.TryAdd((Guid, LocalId, TypeName), Path);
            }
        }
    }
}
