using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateCriticalGenerationWindow : EditorWindow
    {
        GameObject Root;
        GameObject Generator;
        int Order;

        [MenuItem("PLL/Spawning/Add Critical Generation to Selected Biome", priority = 25)]
        static void Open()
        {
            try
            {
                var Selected = Selection.activeGameObject;
                PllAtmosphereAuthoringCommand.RequireCurrentBiomeRoot(Selected);
                var Window = GetWindow<PllCreateCriticalGenerationWindow>("Critical Generation");
                Window.Root = Selected; Window.Generator = null; Window.Order = 0;
                Window.Show();
            }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }

        void OnGUI()
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Biome", Root, typeof(GameObject), true);
            Generator = (GameObject)EditorGUILayout.ObjectField("Generator", Generator, typeof(GameObject), true);
            Order = EditorGUILayout.IntField("Order", Order);
            EditorGUILayout.HelpBox(
                "Choose a configured PropSpawner or Immediate Child Props block under FloorRoot or CampfireRoot. "
                + "This marks its immediate generation as critical without changing placement, candidates or deferred work.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(!Root || !Generator))
                if (GUILayout.Button("Add Critical Generation"))
                {
                    try { Create(Root, Generator, Order); Close(); }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        internal static PLLCriticalGeneration Create(GameObject Root, GameObject Generator, int Order)
        {
            const string Action = "Add Critical Generation";
            PllAtmosphereAuthoringCommand.RequireCurrentBiomeRoot(Root);
            if (!Generator || EditorUtility.IsPersistent(Generator))
                throw new InvalidOperationException(Action + " requires a generator in the editable Prefab Mode hierarchy.");
            var Spawner = Generator.GetComponent<PropSpawner>();
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(
                Spawner && Generator.transform.parent
                    ? Generator.transform.parent.gameObject : Generator, Action);
            if (!Owner.IsBiomeRoot || Owner.StageRoot != Root)
                throw new InvalidOperationException(Action + " requires a generator in the selected biome's Prefab Mode hierarchy.");
            var NestedRoot = PrefabUtility.GetNearestPrefabInstanceRoot(Generator);
            if (NestedRoot && NestedRoot != Root)
                throw new InvalidOperationException("The selected generator belongs to a nested prefab. Open its source before editing it.");
            if (Generator.GetComponent<PLLCriticalGeneration>())
                throw new InvalidOperationException("The selected generator already owns Critical Generation.");
            var PrimarySources = PllCriticalGenerationContract.ValidateScope(
                Root.GetComponent<PllBiomePrefabAnchors>(), Generator.transform, Action, true);
            foreach (var Source in PrimarySources)
                if (Source is PllChildPropAuthoring)
                    PllSourceIdentityValidator.ValidateCreatorComponent(Source, Action);
                else if (Source is PropSpawner Primary)
                {
                    if (Primary.overrideProps || Primary.props == null || Primary.props.Length == 0)
                        throw new InvalidOperationException("The selected PropSpawner requires authored candidate prefabs without an unresolved override.");
                    foreach (var Candidate in Primary.props)
                        PllSourceIdentityValidator.RequirePrefabAsset(Candidate, "Critical Generation candidate");
                }

            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            var Container = Generator;
            if (Spawner)
            {
                var Position = Generator.transform.localPosition;
                var Rotation = Generator.transform.localRotation;
                var Scale = Generator.transform.localScale;
                Container = new GameObject("CriticalGeneration");
                Container.transform.SetParent(Generator.transform.parent, false);
                Container.transform.SetSiblingIndex(Generator.transform.GetSiblingIndex());
                Undo.RegisterCreatedObjectUndo(Container, Action);
                Undo.SetTransformParent(Generator.transform, Container.transform, Action);
                Generator.transform.localPosition = Position;
                Generator.transform.localRotation = Rotation;
                Generator.transform.localScale = Scale;
            }
            var Marker = Undo.AddComponent<PLLCriticalGeneration>(Container);
            Marker.Order = Order;
            EditorUtility.SetDirty(Marker);
            Undo.CollapseUndoOperations(Group);
            Selection.activeGameObject = Container;
            return Marker;
        }
    }
}
