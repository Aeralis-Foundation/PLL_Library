using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateStatueWindow : EditorWindow
    {
        GameObject Target;
        GameObject Model;
        GameObject Skeleton;
        Transform SpawnPoint;
        RuntimeAnimatorController Controller;
        string DisplayName = "Scout Statue";
        float OpenTime = 1.5f;
        readonly List<GameObject> Items = new List<GameObject>();

        [MenuItem("PLL/Props/Add Statue to Selected Model Wrapper", priority = 23)]
        static void Open()
        {
            var Window = GetWindow<PllCreateStatueWindow>("Add Statue");
            Window.Target = Selection.activeGameObject;
            Window.Model = null; Window.Skeleton = null; Window.SpawnPoint = null;
            Window.Controller = null; Window.Items.Clear();
            Window.Show();
        }

        Vector2 Scroll;

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Target = (GameObject)EditorGUILayout.ObjectField("Wrapper", Target, typeof(GameObject), true);
            Model = (GameObject)EditorGUILayout.ObjectField("Model Child", Model, typeof(GameObject), true);
            Skeleton = (GameObject)EditorGUILayout.ObjectField("Skeleton Child", Skeleton, typeof(GameObject), true);
            SpawnPoint = (Transform)EditorGUILayout.ObjectField("Spawn Point", SpawnPoint, typeof(Transform), true);
            DisplayName = EditorGUILayout.TextField("Display Name", DisplayName);
            OpenTime = EditorGUILayout.FloatField("Opening Seconds", OpenTime);
            Controller = (RuntimeAnimatorController)EditorGUILayout.ObjectField("Animator Controller", Controller, typeof(RuntimeAnimatorController), false);
            PllAuthoringCommands.DrawReferences("Item", Items, false);
            EditorGUILayout.HelpBox("Choose a model with solid collision. If no skeleton is assigned, PLL uses the model. "
                + "The builder adds PEAK's respawn components and an Animator when needed.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Target || !Model || !SpawnPoint))
                if (GUILayout.Button("Add Statue"))
                {
                    try { Create(Target, Model, Skeleton, SpawnPoint, DisplayName, OpenTime, Controller, Items); Close(); }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            EditorGUILayout.EndScrollView();
        }

        internal static PllStatueAuthoring Create(GameObject Target, GameObject Model,
            GameObject Skeleton, Transform SpawnPoint, string DisplayName, float OpenTime,
            RuntimeAnimatorController Controller, IReadOnlyList<GameObject> Items)
        {
            const string Action = "Add Statue";
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PllStatueAuthoring>(Target, Action);
            if (!Owner.IsBiomeRoot || !Target.activeSelf
                || TransformOwnership.HasConfigurationBranch(Owner.StageRoot.transform, Target.transform))
                throw new InvalidOperationException("Statue requires an unconditional active biome-owned wrapper.");
            var Anchors = Owner.StageRoot.GetComponent<PllBiomePrefabAnchors>();
            if (Anchors.StatueRoot || Owner.StageRoot.GetComponentsInChildren<PllStatueAuthoring>(true).Length != 0)
                throw new InvalidOperationException("The biome already owns a statue.");
            if (!Model || Model == Target || !Model.transform.IsChildOf(Target.transform)
                || !SpawnPoint || SpawnPoint == Target.transform || SpawnPoint.gameObject == Model
                || !SpawnPoint.IsChildOf(Target.transform)
                || Skeleton && (Skeleton == Target || !Skeleton.transform.IsChildOf(Target.transform)))
                throw new InvalidOperationException("Model, spawn point and optional skeleton must be distinct owned children of the wrapper.");
            PllSourceIdentityValidator.RequireEditableTarget(Owner, Model.transform, Action);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, SpawnPoint, Action);
            if (Skeleton) PllSourceIdentityValidator.RequireEditableTarget(Owner, Skeleton.transform, Action);
            if (!TransformOwnership.DiscoverableWhenActivated(Target.transform, Model.transform)
                || !TransformOwnership.DiscoverableWhenActivated(Target.transform, SpawnPoint)
                || Skeleton && !TransformOwnership.DiscoverableWhenActivated(Target.transform, Skeleton.transform))
                throw new InvalidOperationException("Statue children must have active ancestors.");
            if (!PllAuthoringValueContract.IsDisplayText(DisplayName)
                || !PllAuthoringValueContract.CanEncode(OpenTime) || OpenTime <= 0f)
                throw new InvalidOperationException("Statue requires display text and a positive finite opening duration.");
            var Animator = Target.GetComponent<Animator>();
            if (Animator && !Animator.enabled)
                throw new InvalidOperationException("The wrapper's Animator must be enabled.");
            if (Controller || Animator && Animator.runtimeAnimatorController)
                PllFixtureAuthoringValidator.ValidateLuggageAnimatorController(
                    Controller ? Controller : Animator.runtimeAnimatorController, "Statue Animator Controller");
            PllFixtureAuthoringValidator.ValidateItemCatalog(Items, Action);
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Source = Undo.AddComponent<PllStatueAuthoring>(Target);
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.SetReference(Serialized, "modelRoot", Model);
                    PllAuthoringCommands.SetReference(Serialized, "skeletonRoot", Skeleton);
                    PllAuthoringCommands.SetReference(Serialized, "spawnPoint", SpawnPoint);
                    PllAuthoringCommands.SetReference(Serialized, "animatorController", Controller);
                    PllAuthoringCommands.SetReferences(Serialized, "items", Items);
                    PllAuthoringCommands.Property(Serialized, "displayName").stringValue = DisplayName;
                    PllAuthoringCommands.Property(Serialized, "openTime").floatValue = OpenTime;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                Undo.RecordObject(Anchors, Action);
                using (var Serialized = new SerializedObject(Anchors))
                {
                    PllAuthoringCommands.SetReference(Serialized, "statueRoot", Target);
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(Anchors);
                return Source;
            });
        }
    }
}
