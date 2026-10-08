using System;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal static class PllAtmosphereAuthoringCommand
    {
        const string MenuPath = "PLL/Visuals/Add Atmosphere to Selected Biome";

        [MenuItem(MenuPath, priority = 22)]
        static void Open()
        {
            try
            {
                var Root = RequireSelectedBiomeRoot();
                var Existing = Root.GetComponent<PllAtmosphereAuthoring>();
                if (Existing)
                {
                    Selection.activeObject = Existing;
                    EditorGUIUtility.PingObject(Existing);
                    return;
                }
                PllAtmosphereAuthoringWindow.Open(Root);
            }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }

        internal static PllAtmosphereAuthoring Create(
            GameObject Root,
            Material SkyboxMaterial,
            Texture2D FogTexture)
        {
            RequireCurrentBiomeRoot(Root);
            if (Root.GetComponentsInChildren<PllAtmosphereAuthoring>(true).Length != 0)
                throw new InvalidOperationException(
                    "The selected biome already owns PllAtmosphereAuthoring; PLL did not overwrite it.");
            PllAtmosphereAuthoringValidator.ValidateAssets(
                SkyboxMaterial,
                FogTexture,
                "The selected biome atmosphere");

            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Atmosphere");
            try
            {
                var Atmosphere = Undo.AddComponent<PllAtmosphereAuthoring>(Root);
                var Serialized = new SerializedObject(Atmosphere);
                PllAuthoringCommands.Property(Serialized, "SkyboxMaterialValue").objectReferenceValue = SkyboxMaterial;
                PllAuthoringCommands.Property(Serialized, "FogTextureValue").objectReferenceValue = FogTexture;
                Serialized.ApplyModifiedProperties();
                PllAtmosphereAuthoringValidator.ValidateComponent(
                    Root,
                    Atmosphere,
                    "The selected biome atmosphere");
                EditorUtility.SetDirty(Atmosphere);
                Selection.activeObject = Atmosphere;
                EditorGUIUtility.PingObject(Atmosphere);
                Undo.CollapseUndoOperations(Group);
                return Atmosphere;
            }
            catch
            {
                Undo.RevertAllDownToGroup(Group);
                throw;
            }
        }

        internal static void RequireCurrentBiomeRoot(GameObject Root)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "Add Atmosphere is unavailable while entering or running Play Mode.");
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!Root || Stage == null || Stage.prefabContentsRoot != Root)
                throw new InvalidOperationException(
                    "Select the exact biome root in its current Prefab Mode stage before adding atmosphere.");
            var Runtimes = Root.GetComponentsInChildren<CustomSegmentRuntime>(true);
            var Anchors = Root.GetComponentsInChildren<PllBiomePrefabAnchors>(true);
            if (Runtimes.Length != 1 || Runtimes[0].gameObject != Root
                || Anchors.Length != 1)
                throw new InvalidOperationException(
                    "The selected Prefab Mode root is not one complete PLL biome root.");
            var Links = Root.GetComponentsInChildren<PllBiomeRoot>(true);
            if (Links.Length > 1 || Links.Length == 1 && Links[0].gameObject != Root)
                throw new InvalidOperationException(
                    "The selected Prefab Mode root has an invalid PllBiomeRoot binding.");
        }

        static GameObject RequireSelectedBiomeRoot()
        {
            var Root = Selection.activeGameObject;
            RequireCurrentBiomeRoot(Root);
            return Root;
        }
    }

    internal sealed class PllAtmosphereAuthoringWindow : EditorWindow
    {
        GameObject Root = null;
        Material SkyboxMaterial = null;
        Texture2D FogTexture = null;

        internal static void Open(GameObject Root)
        {
            var Window = GetWindow<PllAtmosphereAuthoringWindow>(true, "Add Atmosphere");
            Window.minSize = new Vector2(420f, 142f);
            Window.Root = Root;
            Window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Biome Root", Root ? Root.name : "Unavailable");
            SkyboxMaterial = (Material)EditorGUILayout.ObjectField(
                "Skybox Material",
                SkyboxMaterial,
                typeof(Material),
                false);
            FogTexture = (Texture2D)EditorGUILayout.ObjectField(
                "Fog Texture",
                FogTexture,
                typeof(Texture2D),
                false);
            EditorGUILayout.HelpBox(
                "Assign at least one persistent project or package asset. PLL references the assets without modifying them.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!Root || !SkyboxMaterial && !FogTexture))
                if (GUILayout.Button("Add Atmosphere")) Add();
        }

        void Add()
        {
            try
            {
                PllAtmosphereAuthoringCommand.Create(Root, SkyboxMaterial, FogTexture);
                Close();
            }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }
    }
}
