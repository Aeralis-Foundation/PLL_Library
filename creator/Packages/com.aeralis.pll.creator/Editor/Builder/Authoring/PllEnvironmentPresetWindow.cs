using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllEnvironmentPresetWindow : EditorWindow
    {
        GameObject Root;
        PllBiomeDefinition Source;
        PllEnvironmentPreset Preset;

        [MenuItem("PLL/Visuals/Compose Environment Preset", priority = 24)]
        static void Open()
        {
            var Window = GetWindow<PllEnvironmentPresetWindow>("Environment Preset");
            Window.Root = Selection.activeGameObject;
            Window.Source = Selection.activeObject as PllBiomeDefinition;
            Window.Preset = Selection.activeObject as PllEnvironmentPreset;
            Window.Show();
        }

        void OnGUI()
        {
            Source = (PllBiomeDefinition)EditorGUILayout.ObjectField(
                "Authored Source Biome", Source, typeof(PllBiomeDefinition), false);
            using (new EditorGUI.DisabledScope(!Source))
                if (GUILayout.Button("Save Configured Environment Preset"))
                {
                    try
                    {
                        var Path = EditorUtility.SaveFilePanelInProject(
                            "Save Environment Preset", Source.name + " Environment", "asset",
                            "Save a preset referencing this authored biome's current environment.");
                        if (Path.Length != 0) Preset = Create(Source, Path);
                    }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            Preset = (PllEnvironmentPreset)EditorGUILayout.ObjectField(
                "Configured Preset", Preset, typeof(PllEnvironmentPreset), false);
            Root = (GameObject)EditorGUILayout.ObjectField(
                "Target Biome", Root, typeof(GameObject), true);
            EditorGUILayout.HelpBox(
                "Copies the source biome's current lighting profile, color, audio, atmosphere, fog, native visuals, particles, weather and campsite. "
                + "The target's campsite transform, biome identity, behavior and terrain stay unchanged. "
                + "Remove references from other objects to components being replaced. Source assets stay unchanged; you can undo the changes together.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Root || !Preset))
                if (GUILayout.Button("Apply Configured Environment Preset"))
                {
                    try { Apply(Root, Preset); Close(); }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        internal static PllEnvironmentPreset Create(PllBiomeDefinition Source, string Path)
        {
            var FullPath = PllCreatorFileSystem.AssetPathToFullPath(Path);
            if (!Path.StartsWith("Assets/", StringComparison.Ordinal)
                || !Path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                || !AssetDatabase.IsValidFolder(System.IO.Path.GetDirectoryName(Path).Replace('\\', '/'))
                || !PllCreatorFileSystem.Inside(FullPath, Application.dataPath)
                || System.IO.File.Exists(FullPath))
                throw new InvalidOperationException("Choose a new .asset path inside Assets.");
            RequireSource(Source);
            var Preset = CreateInstance<PllEnvironmentPreset>();
            Preset.Configure(Source);
            PllSourceAssetCreation.SaveAsset(Preset, Path);
            Selection.activeObject = Preset;
            return Preset;
        }

        internal static void Apply(GameObject Root, PllEnvironmentPreset Preset)
        {
            const string Action = "Apply Environment Preset";
            PllAtmosphereAuthoringCommand.RequireCurrentBiomeRoot(Root);
            var Link = Root.GetComponent<PllBiomeRoot>();
            if (!Link || !Link.Definition || !Preset || !AssetDatabase.IsMainAsset(Preset))
                throw new InvalidOperationException("Choose a linked target biome and a saved configured preset.");
            if (Link.Definition == Preset.Source)
                throw new InvalidOperationException("Choose a different target biome definition.");
            var Catalog = RequireSource(Preset.Source);
            var Anchors = Root.GetComponentInChildren<PllBiomePrefabAnchors>(true);
            var Runtime = Root.GetComponent<CustomSegmentRuntime>();
            PllSourceIdentityValidator.ResolveEditableOwner(Anchors.FloorRoot, Action);
            var Campfire = Anchors.CampfireRoot;
            if (Runtime.SegmentCampfire != Campfire || !Campfire)
                throw new InvalidOperationException("The target campsite must match its runtime and anchor owner.");
            RequireCompositionTarget(Root, Campfire.transform);
            var RemovedRoots = new List<GameObject> { Campfire };
            foreach (var Envelope in Root.GetComponentsInChildren<PllEnvironmentPresetRoot>(true))
            {
                if (TransformOwnership.Contains(Envelope.transform, Anchors.FloorRoot.transform)
                    || TransformOwnership.Contains(Envelope.transform, Campfire.transform))
                    throw new InvalidOperationException("An environment envelope cannot own terrain or the canonical campsite root.");
                if (!InsideRemoved(Envelope.transform, RemovedRoots))
                    RemovedRoots.Add(Envelope.gameObject);
            }
            foreach (var Visual in Root.GetComponentsInChildren<PllCustomVisualAuthoring>(true))
                if (!InsideRemoved(Visual.transform, RemovedRoots)) RemovedRoots.Add(Visual.gameObject);
            foreach (var Resolver in Root.GetComponentsInChildren<PLLNativeResolver>(true))
                if (IsWeather(Resolver) && !InsideRemoved(Resolver.transform, RemovedRoots))
                    RemovedRoots.Add(Resolver.gameObject);
            var RemovedMetadata = new List<Component>();
            var Atmosphere = Root.GetComponent<PllAtmosphereAuthoring>();
            var NativeVisual = Root.GetComponent<PLLNativeVisualResolver>();
            if (!Catalog.Atmosphere && Atmosphere) RemovedMetadata.Add(Atmosphere);
            if (Catalog.NativeVisualResolvers.Length == 0 && NativeVisual) RemovedMetadata.Add(NativeVisual);
            foreach (var Removed in RemovedRoots)
                RequireCompositionTarget(Root, Removed.transform);
            ValidateReplacementReferences(Root, RemovedRoots, RemovedMetadata, Runtime, Anchors);
            ValidateCampsiteContents(Catalog, Link.Definition.Behavior);
            Transform Presentation = null;
            if (Catalog.CustomVisuals.Length != 0)
                for (var Index = 0; Index < Root.transform.childCount; Index++)
                {
                    var Child = Root.transform.GetChild(Index);
                    if (Child.name != "Presentation") continue;
                    if (Presentation)
                        throw new InvalidOperationException("The target has multiple direct Presentation children.");
                    RequireCompositionTarget(Root, Child);
                    if (!TransformOwnership.DiscoverableWhenActivated(Root.transform, Child)
                        || Child.GetComponents<Component>().Length != 1)
                        throw new InvalidOperationException("Presentation must be an active, unmanaged direct child.");
                    Presentation = Child;
                }
            if (Presentation) ValidateInverseFrames(Presentation, Root.transform);
            foreach (var Resolver in Catalog.NativeResolvers)
                if (IsWeather(Resolver) && !TransformOwnership.Contains(Preset.Source.Anchors.CampfireRoot.transform, Resolver.transform))
                {
                    ValidateInverseFrames(Anchors.FloorRoot.transform, Root.transform);
                    break;
                }
            var Staging = new GameObject("PLL Environment Staging");
            Staging.SetActive(false);
            Staging.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var SourceCampfire = Preset.Source.Anchors.CampfireRoot;
                var ClonedCampfire = Instantiate(SourceCampfire, Staging.transform, false);
                ClonedCampfire.name = Campfire.name;
                ClonedCampfire.transform.SetLocalPositionAndRotation(Campfire.transform.localPosition, Campfire.transform.localRotation);
                ClonedCampfire.transform.localScale = Campfire.transform.localScale;
                GameObject Statue = null;
                if (Preset.Source.Anchors.StatueRoot)
                {
                    var SourceStatues = SourceCampfire.GetComponentsInChildren<PllStatueAuthoring>(true);
                    var ClonedStatues = ClonedCampfire.GetComponentsInChildren<PllStatueAuthoring>(true);
                    for (var Index = 0; Index < SourceStatues.Length; Index++)
                        if (SourceStatues[Index].gameObject == Preset.Source.Anchors.StatueRoot)
                            Statue = ClonedStatues[Index].gameObject;
                }
                var WeatherClones = new List<GameObject>();
                foreach (var Resolver in Catalog.NativeResolvers)
                    if (IsWeather(Resolver) && !TransformOwnership.Contains(SourceCampfire.transform, Resolver.transform))
                        WeatherClones.Add(CloneInBiomeSpace(Resolver.gameObject, Preset.Source.RootPrefab.transform,
                            Anchors.FloorRoot.transform, Root.transform, Staging.transform));
                var ParticleClones = new List<GameObject>();
                foreach (var Visual in Catalog.CustomVisuals)
                    ParticleClones.Add(CloneInBiomeSpace(Visual.Authoring.gameObject, Preset.Source.RootPrefab.transform,
                        Presentation ? Presentation : Root.transform, Root.transform, Staging.transform));
                Undo.IncrementCurrentGroup();
                var Group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(Action);
                try
                {
                    PllApplyEnvironmentWindow.Apply(Root, Catalog);
                    Undo.RecordObject(Runtime, Action);
                    Undo.RecordObject(Anchors, Action);
                    using (var Serialized = new SerializedObject(Runtime))
                    {
                        PllAuthoringCommands.SetReference(Serialized, "segmentCampfire", ClonedCampfire);
                        Serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    using (var Serialized = new SerializedObject(Anchors))
                    {
                        PllAuthoringCommands.SetReference(Serialized, "campfireRoot", ClonedCampfire);
                        PllAuthoringCommands.SetReference(Serialized, "statueRoot", Statue);
                        Serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    ClonedCampfire.transform.SetParent(Campfire.transform.parent, false);
                    ClonedCampfire.transform.SetSiblingIndex(Campfire.transform.GetSiblingIndex());
                    Undo.RegisterCreatedObjectUndo(ClonedCampfire, Action);
                    foreach (var Removed in RemovedRoots) Undo.DestroyObjectImmediate(Removed);
                    foreach (var Removed in RemovedMetadata) Undo.DestroyObjectImmediate(Removed);
                    if (Catalog.CustomVisuals.Length != 0 && !Presentation)
                    {
                        var Created = new GameObject("Presentation");
                        Undo.RegisterCreatedObjectUndo(Created, Action);
                        Created.transform.SetParent(Root.transform, false);
                        Presentation = Created.transform;
                    }
                    foreach (var Clone in WeatherClones)
                    {
                        Clone.transform.SetParent(Anchors.FloorRoot.transform, false);
                        Undo.RegisterCreatedObjectUndo(Clone, Action);
                    }
                    foreach (var Clone in ParticleClones)
                    {
                        Clone.transform.SetParent(Presentation, false);
                        Undo.RegisterCreatedObjectUndo(Clone, Action);
                    }
                    EditorUtility.SetDirty(Runtime);
                    EditorUtility.SetDirty(Anchors);
                    EditorSceneManager.MarkSceneDirty(Root.scene);
                    Undo.CollapseUndoOperations(Group);
                    Selection.activeObject = Link.Definition;
                }
                catch
                {
                    Undo.RevertAllDownToGroup(Group);
                    throw;
                }
            }
            finally { DestroyImmediate(Staging); }
        }

        static DefinitionCatalog RequireSource(PllBiomeDefinition Source)
        {
            if (!Source || !AssetDatabase.IsMainAsset(Source))
                throw new InvalidOperationException("Choose a saved authored biome definition.");
            var Catalog = new PllSourceCatalogValidator().Validate(Source);
            ValidateCloneSource(Source.Anchors.CampfireRoot, Source.RootPrefab);
            foreach (var Resolver in Catalog.NativeResolvers)
                if (IsWeather(Resolver) && !TransformOwnership.Contains(Source.Anchors.CampfireRoot.transform, Resolver.transform))
                    ValidateCloneSource(Resolver.gameObject, Source.RootPrefab);
            foreach (var Visual in Catalog.CustomVisuals) ValidateCloneSource(Visual.Authoring.gameObject, Source.RootPrefab);
            return Catalog;
        }

        static void ValidateCampsiteContents(DefinitionCatalog Catalog, PllBiomeBehavior Behavior)
        {
            var Root = Catalog.Definition.Anchors.CampfireRoot.transform;
            var Candidates = new HashSet<string>(StringComparer.Ordinal);
            var Pending = new Queue<PrefabNode>();
            foreach (var Edge in Catalog.Graph.ByPath[Catalog.Graph.RootPath].Edges)
            {
                var Source = Edge.Spawner ? Edge.Spawner.transform : Edge.ChildProps.transform;
                if (TransformOwnership.Contains(Root, Source) && Candidates.Add(Edge.TargetPath))
                    Pending.Enqueue(Catalog.Graph.ByPath[Edge.TargetPath]);
            }
            while (Pending.Count != 0)
                foreach (var Edge in Pending.Dequeue().Edges)
                    if (Candidates.Add(Edge.TargetPath)) Pending.Enqueue(Catalog.Graph.ByPath[Edge.TargetPath]);
            foreach (var Luggage in Catalog.Luggage)
                if (TransformOwnership.Contains(Root, Luggage.transform) || Candidates.Contains(AssetDatabase.GetAssetPath(Luggage)))
                    PllFixtureAuthoringValidator.ValidateLuggageComponent(Luggage, Behavior, "Preset campsite luggage");
        }

        static bool IsWeather(PLLNativeResolver Source) => Source.Kind is PLLNativeResolverKind.Rain
            or PLLNativeResolverKind.Wind or PLLNativeResolverKind.Snow;

        static void RequireCompositionTarget(GameObject Root, Transform Value)
        {
            var NestedRoot = PrefabUtility.GetNearestPrefabInstanceRoot(Value.gameObject);
            if (EditorUtility.IsPersistent(Value) || Value.gameObject.scene != Root.scene
                || !TransformOwnership.Contains(Root.transform, Value) || NestedRoot && NestedRoot != Root)
                throw new InvalidOperationException("The replaced environment must belong to the current biome, outside nested prefab instances.");
        }

        static Transform ReferenceTransform(UnityEngine.Object Value) => Value is GameObject Object
            ? Object.transform : Value is Component Component ? Component.transform : null;

        static void ValidateCloneSource(GameObject Source, GameObject BiomeRoot)
        {
            foreach (var Component in Source.GetComponentsInChildren<Component>(true))
            {
                if (!Component) throw new InvalidOperationException("The selected environment source contains a missing script.");
                if (Component is Transform) continue;
                if (Component is MonoBehaviour && Component.GetType() != typeof(PropSpawner)
                    && Component.GetType().Assembly != typeof(PllBiomeDefinition).Assembly)
                    throw new InvalidOperationException("The selected environment source contains an executable script outside PLL authoring.");
                using (var Serialized = new SerializedObject(Component))
                {
                    var Property = Serialized.GetIterator();
                    while (Property.Next(true))
                    {
                        if (Property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var Target = ReferenceTransform(Property.objectReferenceValue);
                        if (Target && !TransformOwnership.Contains(Source.transform, Target)
                            && (TransformOwnership.Contains(BiomeRoot.transform, Target) || !EditorUtility.IsPersistent(Target)))
                            throw new InvalidOperationException("The selected environment source references an object outside its copied owner: "
                                + Component.name + "." + Property.propertyPath + ".");
                    }
                }
            }
        }

        static void ValidateReplacementReferences(GameObject Root, IReadOnlyList<GameObject> RemovedRoots,
            IReadOnlyList<Component> RemovedMetadata, CustomSegmentRuntime Runtime, PllBiomePrefabAnchors Anchors)
        {
            foreach (var Component in Root.GetComponentsInChildren<Component>(true))
            {
                if (!Component || Component is Transform) continue;
                if (InsideRemoved(Component.transform, RemovedRoots)) continue;
                using (var Serialized = new SerializedObject(Component))
                {
                    var Property = Serialized.GetIterator();
                    while (Property.Next(true))
                    {
                        if (Property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (Component == Runtime && Property.propertyPath == "segmentCampfire"
                            || Component == Anchors && Property.propertyPath is "campfireRoot" or "statueRoot") continue;
                        var Value = Property.objectReferenceValue;
                        var Target = ReferenceTransform(Value);
                        var RemovedReference = false;
                        foreach (var Removed in RemovedRoots)
                            if (Target && TransformOwnership.Contains(Removed.transform, Target)) { RemovedReference = true; break; }
                        foreach (var Removed in RemovedMetadata)
                            if (Value == Removed) { RemovedReference = true; break; }
                        if (RemovedReference)
                            throw new InvalidOperationException("An object outside the replaced environment references its old owner: "
                                + Component.name + "." + Property.propertyPath + ".");
                    }
                }
            }
        }

        static void ValidateInverseFrames(Transform Parent, Transform Origin)
        {
            for (var Current = Parent; Current != Origin; Current = Current.parent)
                if (!PllAuthoringValueContract.CanEncode(1f / Current.localScale.x)
                    || !PllAuthoringValueContract.CanEncode(1f / Current.localScale.y)
                    || !PllAuthoringValueContract.CanEncode(1f / Current.localScale.z))
                    throw new InvalidOperationException("The target environment parent has a noninvertible scale.");
        }

        static GameObject CloneInBiomeSpace(GameObject Source, Transform SourceOrigin,
            Transform TargetParent, Transform TargetOrigin, Transform Staging)
        {
            var Envelope = new GameObject(Source.name + " Environment");
            Envelope.transform.SetParent(Staging, false);
            Envelope.AddComponent<PllEnvironmentPresetRoot>();
            var Parent = Envelope.transform;
            for (var Current = TargetParent; Current != TargetOrigin; Current = Current.parent)
            {
                if (Current.localScale != Vector3.one)
                {
                    var Scale = new GameObject("Inverse Scale");
                    Scale.transform.SetParent(Parent, false);
                    Scale.transform.localScale = new Vector3(1f / Current.localScale.x, 1f / Current.localScale.y, 1f / Current.localScale.z);
                    Parent = Scale.transform;
                }
                if (Current.localRotation != Quaternion.identity)
                {
                    var Rotation = new GameObject("Inverse Rotation");
                    Rotation.transform.SetParent(Parent, false);
                    Rotation.transform.localRotation = Quaternion.Inverse(Current.localRotation);
                    Parent = Rotation.transform;
                }
                if (Current.localPosition != Vector3.zero)
                {
                    var Position = new GameObject("Inverse Position");
                    Position.transform.SetParent(Parent, false);
                    Position.transform.localPosition = -Current.localPosition;
                    Parent = Position.transform;
                }
            }
            var Ancestors = new Stack<Transform>();
            for (var Current = Source.transform.parent; Current != SourceOrigin; Current = Current.parent)
                Ancestors.Push(Current);
            while (Ancestors.Count != 0)
            {
                var Original = Ancestors.Pop();
                var Frame = new GameObject(Original.name);
                Frame.transform.SetParent(Parent, false);
                Frame.transform.SetLocalPositionAndRotation(Original.localPosition, Original.localRotation);
                Frame.transform.localScale = Original.localScale;
                Parent = Frame.transform;
            }
            var Clone = Instantiate(Source, Parent, false);
            Clone.name = Source.name;
            return Envelope;
        }

        static bool InsideRemoved(Transform Value, IReadOnlyList<GameObject> Roots)
        {
            foreach (var Root in Roots)
                if (TransformOwnership.Contains(Root.transform, Value)) return true;
            return false;
        }
    }
}
