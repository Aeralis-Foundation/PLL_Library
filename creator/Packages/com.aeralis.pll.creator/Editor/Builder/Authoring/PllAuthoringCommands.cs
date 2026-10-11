using System;
using System.Collections.Generic;
using System.Linq;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PLL.Creator.Editor
{
    internal static class PllAuthoringCommands
    {
        const int MenuPriority = 20;

        [MenuItem("PLL/Spawning/Add Final Generation to Selected Source", priority = MenuPriority + 6)]
        static void AddFinalGenerationFromMenu()
        {
            try { AddFinalGeneration(Selection.activeGameObject); }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }

        internal static PLLFinalGeneration AddFinalGeneration(GameObject Target)
        {
            const string Action = "Add Final Generation";
            var Spawner = Target ? Target.GetComponent<PropSpawner>() : null;
            var Owner = RequireFeatureTarget<PLLFinalGeneration>(
                Spawner && Target.transform.parent ? Target.transform.parent.gameObject : Target, Action);
            if (!Owner.IsBiomeRoot)
                throw new InvalidOperationException("Final Generation requires a biome-owned source.");
            if (Spawner)
            {
                var Nested = PrefabUtility.GetNearestPrefabInstanceRoot(Target);
                if (Nested && Nested != Owner.StageRoot)
                    throw new InvalidOperationException("Open the spawner's nested prefab source before adding Final Generation.");
                if (!Target.activeSelf || Target.GetComponent<PLLFinalGeneration>())
                    throw new InvalidOperationException("The selected spawner must be active without Final Generation.");
            }
            foreach (var Marker in Owner.StageRoot.GetComponentsInChildren<PLLFinalGeneration>(true))
                if (TransformOwnership.Overlaps(Target.transform, Marker.transform))
                    throw new InvalidOperationException("Final Generation roots cannot overlap.");
            if (Target.GetComponentsInChildren<PLLCriticalGeneration>(true).Length != 0
                || Target.GetComponentInParent<PLLCriticalGeneration>(true))
                throw new InvalidOperationException("Final Generation cannot overlap Critical Generation.");
            return Create(Action, () =>
            {
                if (Spawner)
                {
                    var Container = new GameObject("Final Generation");
                    Undo.RegisterCreatedObjectUndo(Container, Action);
                    Container.transform.SetParent(Target.transform.parent, false);
                    Container.transform.SetSiblingIndex(Target.transform.GetSiblingIndex());
                    Undo.SetTransformParent(Target.transform, Container.transform, Action);
                    Target = Container;
                }
                return Undo.AddComponent<PLLFinalGeneration>(Target);
            });
        }

        internal static EditablePrefabOwner RequireFeatureTarget<T>(
            GameObject Target, string Action) where T : Component
        {
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Target, Action);
            if (Target.GetComponent<T>())
                throw new InvalidOperationException("The selected source already owns " + typeof(T).Name + ".");
            if (!PllSourceIdentityValidator.DiscoverableWhenConfigured(
                    Owner.StageRoot.transform, Target.transform,
                    PllSourceIdentityValidator.VariationEntryRoots(
                        Owner.StageRoot.GetComponentsInChildren<PllVariationAuthoring>(true))))
                throw new InvalidOperationException("The selected source needs active ancestors or ancestors configured as variation entries.");
            return Owner;
        }

        internal static T Create<T>(string Action, Func<T> Mutation,
            Action Validate = null) where T : Component
        {
            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            try
            {
                var Source = Mutation();
                if (Validate == null) PllSourceIdentityValidator.ValidateCreatorComponent(Source, Action);
                else Validate();
                EditorUtility.SetDirty(Source);
                EditorSceneManager.MarkSceneDirty(Source.gameObject.scene);
                Undo.CollapseUndoOperations(Group);
                Selection.activeObject = Source;
                EditorGUIUtility.PingObject(Source);
                return Source;
            }
            catch
            {
                Undo.RevertAllDownToGroup(Group);
                throw;
            }
        }

        internal static void SetReferences<T>(SerializedObject Serialized,
            string Name, IReadOnlyList<T> Values) where T : Object
        {
            var Property = PllAuthoringCommands.Property(Serialized, Name);
            Property.arraySize = Values.Count;
            for (var Index = 0; Index < Values.Count; Index++)
                Property.GetArrayElementAtIndex(Index).objectReferenceValue = Values[Index];
        }

        internal static void DrawReferences<T>(string Label,
            List<T> Values, bool SceneObjects) where T : Object
        {
            for (var Index = 0; Index < Values.Count; Index++)
            {
                EditorGUILayout.BeginHorizontal();
                Values[Index] = (T)EditorGUILayout.ObjectField(
                    Label + " " + (Index + 1), Values[Index], typeof(T), SceneObjects);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    Values.RemoveAt(Index);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add " + Label)) Values.Add(null);
        }

        [MenuItem("PLL/Visuals/Add Custom Visual to Selected Particles", priority = MenuPriority)]
        static void MakeCustomVisualFromMenu() => Execute("Make Custom Visual", MakeCustomVisual);

        [MenuItem("PLL/Audio/Add Biome Stinger to Selected Audio Source", priority = MenuPriority + 1)]
        static void MakeBiomeStingerFromMenu() => Execute("Make Biome Stinger", MakeBiomeStinger);

        [MenuItem("PLL/Geometry/Make Selected Geometry Climbable", priority = MenuPriority + 3)]
        static void MakeSelectedGeometryClimbableFromMenu()
        {
            try
            {
                MakeSelectedGeometryClimbable();
            }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog(
                    "PLL Creator", Failure.Message, "Close");
            }
        }

        [MenuItem("PLL/Geometry/Add Boundary Box", priority = MenuPriority + 4)]
        static void AddBoundaryBoxFromMenu()
        {
            try { AddBoundaryBox(Selection.activeGameObject); }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }

        internal static BoxCollider AddBoundaryBox(GameObject Target)
        {
            const string Action = "Add Boundary Box";
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Target, Action);
            if (!Owner.IsBiomeRoot || !TransformOwnership.Contains(Owner.Floor, Target.transform))
                throw new InvalidOperationException("A boundary box requires FloorRoot or an editable child below it.");
            var Layer = LayerMask.NameToLayer("InvisWall");
            if (Layer != 22)
                throw new InvalidOperationException("InvisWall must occupy layer 22. Configure PLL Creator.");
            return Create(Action, () =>
            {
                var Box = CreateChild(Target.transform, "Boundary Box");
                Undo.RegisterCreatedObjectUndo(Box, Action);
                Box.layer = Layer;
                return Undo.AddComponent<BoxCollider>(Box);
            });
        }

        internal static PllCampfireAuthoring CreateCampfireForNewBiome(
            GameObject Model, GameObject CampfireRoot)
        {
            RequireCampfireModelPresentation(Model);
            RequireCampfireOwner(Model, CampfireRoot);
            return BuildCampfire(Model);
        }

        static PllCampfireAuthoring BuildCampfire(GameObject Model)
        {
            var Wrapper = CreateWrapper(Model, "Campfire");
            var Logs = CreateChild(Wrapper.transform, "Logs").transform;
            SetParentAndReset(Model.transform, Logs);
            var Fire = CreateParticle(Wrapper.transform, "FireParticles", true, false);
            var SmokeOff = CreateParticle(Wrapper.transform, "SmokeParticlesOff", true, true);
            var SmokeLit = CreateParticle(Wrapper.transform, "SmokeParticlesLit", false, true);
            var Component = Wrapper.AddComponent<PllCampfireAuthoring>();

            var Serialized = new SerializedObject(Component);
            SetReference(Serialized, "root", Wrapper);
            SetReference(Serialized, "modelRoot", Model);
            SetReference(Serialized, "fireParticles", Fire);
            SetReference(Serialized, "smokeParticlesOff", SmokeOff);
            SetReference(Serialized, "smokeParticlesLit", SmokeLit);
            SetReference(Serialized, "logRoot", Logs);
            SetReference(Serialized, "enableWhenLit", Fire.gameObject);
            SetReference(Serialized, "disableWhenLit", SmokeOff.gameObject);
            SetReference(Serialized, "loop", null);
            Serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(Component);
            return Component;
        }

        internal static void SetReference(SerializedObject Serialized, string Name, Object Value)
        {
            Property(Serialized, Name).objectReferenceValue = Value;
        }

        internal static SerializedProperty Property(SerializedObject Serialized, string Name)
        {
            var Property = Serialized.FindProperty(Name);
            if (Property == null)
                throw new InvalidOperationException("Serialized field '" + Name + "' is unavailable on '"
                    + Serialized.targetObject.GetType().FullName + "'. Rebuild PLL Creator from the matching runtime source.");
            return Property;
        }

        static void Execute(string Name, Action Command)
        {
            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Name);
            try
            {
                Command();
                Undo.CollapseUndoOperations(Group);
            }
            catch (InvalidOperationException Failure)
            {
                Undo.RevertAllDownToGroup(Group);
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
            catch
            {
                Undo.RevertAllDownToGroup(Group);
                throw;
            }
        }

        static void MakeCustomVisual()
        {
            var Root = RequireSelection("Make Custom Visual");
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            PllPresentationAuthoringValidator.ValidateCustomVisualSelection(
                Stage.prefabContentsRoot, Root, "Make Custom Visual");
            var Component = AddUnique<PllCustomVisualAuthoring>(Root);
            Complete(Component, Root);
        }

        static void MakeBiomeStinger()
        {
            var Selected = RequireSelection("Make Biome Stinger");
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            PllBiomeStingerValidation.ValidateDraft(Stage.prefabContentsRoot, Selected);
            var Component = AddUnique<PllBiomeStingerAuthoring>(Selected);
            Complete(Component, Selected);
        }

        internal static void MakeSelectedGeometryClimbable()
        {
            const string Action = "Make Selected Geometry Climbable";
            var SelectedObjects = Selection.objects;
            if (SelectedObjects == null || SelectedObjects.Length != 1
                || !(SelectedObjects[0] is GameObject Selected))
                throw new InvalidOperationException(
                    Action + " requires exactly one GameObject selection.");
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(
                Selected, Action);
            var Colliders = PllSourceIdentityValidator.DiscoverComponents<
                Collider>(Selected);
            var ChangedTargets = new List<GameObject>();
            var CollidersByTarget = new Dictionary<
                GameObject, List<Collider>>();
            (GameObject Target, Mesh SharedMesh)[] MeshTargets = null;
            if (Colliders.Length == 0)
            {
                MeshTargets = PllClimbSurfaceAuthoringValidator
                    .ValidateMeshColliderFallback(Selected, Owner, Action);
                foreach (var Target in MeshTargets)
                {
                    ChangedTargets.Add(Target.Target);
                    CollidersByTarget.Add(
                        Target.Target, new List<Collider>());
                }
            }
            foreach (var Collider in Colliders)
            {
                var Context = PllClimbSurfaceAuthoringValidator
                    .ClimbTargetContext(
                        Action, Owner, Collider.transform);
                PllSourceIdentityValidator.RequireEditableTarget(
                    Owner, Collider.transform, Context);
                ValidateClimbCollider(Collider, Context);
                PllClimbSurfaceAuthoringValidator.RequireStaticTarget(
                    Collider.transform, Owner, Context);
                if (PllClimbSurfaceAuthoringValidator.IsClimbLayer(
                        Collider.gameObject.layer)) continue;
                List<Collider> TargetColliders;
                if (!CollidersByTarget.TryGetValue(
                        Collider.gameObject, out TargetColliders))
                {
                    TargetColliders = new List<Collider>();
                    CollidersByTarget.Add(
                        Collider.gameObject, TargetColliders);
                    ChangedTargets.Add(Collider.gameObject);
                }
                TargetColliders.Add(Collider);
            }
            if (ChangedTargets.Count == 0) return;
            PllClimbSurfaceAuthoringValidator.ValidateLayerChanges(
                Owner, ChangedTargets, CollidersByTarget);
            var RecordedTargets = ChangedTargets.ToArray();

            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            try
            {
                Undo.RecordObjects(RecordedTargets, Action);
                foreach (var Target in RecordedTargets)
                    if (!PllClimbSurfaceAuthoringValidator.IsClimbLayer(
                            Target.layer))
                        Target.layer = PllClimbSurfaceAuthoringValidator.MapLayer;
                if (MeshTargets != null)
                    foreach (var Target in MeshTargets)
                        PllClimbSurfaceAuthoringValidator.ConfigureStaticMeshCollider(
                            Undo.AddComponent<MeshCollider>(Target.Target), Target.SharedMesh);
                Undo.CollapseUndoOperations(Group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(Group);
                throw;
            }
        }

        static void ValidateClimbCollider(
            Collider Value, string Context)
        {
            if (!Value)
                throw new InvalidOperationException(
                    Context + " contains a destroyed Collider.");
            if (!Value.enabled)
                throw new InvalidOperationException(
                    Context + " contains a disabled Collider.");
            if (Value.isTrigger)
                throw new InvalidOperationException(
                    Context + " contains a trigger Collider.");
            if (!PllClimbSurfaceAuthoringValidator.IsSupportedColliderType(
                    Value))
                throw new InvalidOperationException(
                    Context + " contains unsupported Collider type '"
                        + Value.GetType().FullName + "'.");
            if (!PllClimbSurfaceAuthoringValidator
                    .HasRequiredColliderData(Value))
                throw new InvalidOperationException(
                    Context + (Value is MeshCollider
                        ? " contains a MeshCollider without a shared mesh."
                        : " contains a TerrainCollider without terrain data."));
        }

        static T AddUnique<T>(GameObject Target) where T : Component
        {
            if (Target.GetComponent<T>())
                throw new InvalidOperationException("'" + Target.name + "' already owns " + typeof(T).Name
                    + "; PLL did not overwrite it.");
            return Undo.AddComponent<T>(Target);
        }

        static GameObject RequireSelection(string Action)
        {
            var Selected = Selection.activeGameObject;
            if (!Selected) throw new InvalidOperationException(Action + " requires one active GameObject selection.");
            RequireEditable(Selected, Action);
            return Selected;
        }

        static void RequireEditable(GameObject Value, string Action)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(Action + " is unavailable while entering or running Play Mode.");
            if (EditorUtility.IsPersistent(Value))
                throw new InvalidOperationException("Open prefab '" + Value.name
                    + "' in Prefab Mode before using " + Action + ".");
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (Stage == null || Value != Stage.prefabContentsRoot
                && !Value.transform.IsChildOf(Stage.prefabContentsRoot.transform))
                throw new InvalidOperationException(Action
                    + " requires an object in the current Prefab Mode hierarchy.");
        }

        static void RequireCampfireModelPresentation(GameObject Model)
        {
            if (!Model || Model.GetComponentInParent<PllCampfireAuthoring>(true)
                || Model.GetComponentsInChildren<PllCampfireAuthoring>(true).Length != 0)
                throw new InvalidOperationException("The selected model must be outside every existing PllCampfireAuthoring root.");
            if (!Model.GetComponentsInChildren<MeshRenderer>(true).Any(Value => Value && Value.enabled
                    && TransformOwnership.DiscoverableWhenActivated(Model.transform, Value.transform)))
                throw new InvalidOperationException("The selected campfire model needs an active-relative enabled MeshRenderer.");
            if (!Model.GetComponentsInChildren<Collider>(true).Any(Value => Value && Value.enabled && !Value.isTrigger
                    && TransformOwnership.DiscoverableWhenActivated(Model.transform, Value.transform)))
                throw new InvalidOperationException("The selected campfire model needs an active-relative enabled non-trigger Collider.");
        }

        static void RequireCampfireOwner(GameObject Model, GameObject CampfireRoot)
        {
            if (!CampfireRoot || Model == CampfireRoot || !Model.transform.IsChildOf(CampfireRoot.transform))
                throw new InvalidOperationException("The selected campfire model must be a strict descendant of the current biome's Campfire Root.");
        }

        static GameObject CreateWrapper(GameObject Model, string Suffix)
        {
            var Parent = Model.transform.parent;
            var Wrapper = new GameObject(UniqueSiblingName(Parent, Model.name + Suffix));
            if (Parent) Wrapper.transform.SetParent(Parent, false);
            Wrapper.transform.SetSiblingIndex(Model.transform.GetSiblingIndex());
            Wrapper.transform.localPosition = Model.transform.localPosition;
            Wrapper.transform.localRotation = Model.transform.localRotation;
            Wrapper.transform.localScale = Model.transform.localScale;
            return Wrapper;
        }

        static void SetParentAndReset(Transform Value, Transform Parent)
        {
            Value.SetParent(Parent, false);
            Value.localPosition = Vector3.zero;
            Value.localRotation = Quaternion.identity;
            Value.localScale = Vector3.one;
        }

        static ParticleSystem CreateParticle(
            Transform Parent, string Name, bool PlayOnAwake, bool Active)
        {
            var Value = CreateChild(Parent, Name);
            var Particle = Value.AddComponent<ParticleSystem>();
            var Main = Particle.main;
            Main.playOnAwake = PlayOnAwake;
            Particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Value.SetActive(Active);
            return Particle;
        }

        static GameObject CreateChild(Transform Parent, string Name)
        {
            var Value = new GameObject(UniqueSiblingName(Parent, Name));
            Value.transform.SetParent(Parent, false);
            return Value;
        }

        static string UniqueSiblingName(Transform Parent, string Requested)
        {
            var Used = new HashSet<string>(StringComparer.Ordinal);
            if (Parent)
                for (var i = 0; i < Parent.childCount; i++) Used.Add(Parent.GetChild(i).name);
            if (!Used.Contains(Requested)) return Requested;
            for (var Suffix = 2; ; Suffix++)
            {
                var Candidate = Requested + Suffix;
                if (!Used.Contains(Candidate)) return Candidate;
            }
        }

        static void Complete(Component Component, GameObject Selected)
        {
            PllSourceIdentityValidator.ValidateCreatorComponent(Component, Component.GetType().Name);
            EditorUtility.SetDirty(Component);
            Selection.activeGameObject = Selected;
            EditorGUIUtility.PingObject(Component);
        }
    }
}
