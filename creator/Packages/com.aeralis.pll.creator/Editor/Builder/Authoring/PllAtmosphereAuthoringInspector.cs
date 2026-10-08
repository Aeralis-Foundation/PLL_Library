using System;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace PLL.Creator.Editor
{
    [CustomEditor(typeof(PllAtmosphereAuthoring))]
    internal sealed class PllAtmosphereAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(PllAuthoringCommands.Property(
                serializedObject,
                "SkyboxMaterialValue"));
            EditorGUILayout.PropertyField(PllAuthoringCommands.Property(
                serializedObject,
                "FogTextureValue"));
            serializedObject.ApplyModifiedProperties();

            var Atmosphere = (PllAtmosphereAuthoring)target;
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            try
            {
                if (Stage == null)
                    throw new InvalidOperationException(
                        "Open the biome root in Prefab Mode to edit atmosphere authoring.");
                PllAtmosphereAuthoringValidator.ValidateComponent(
                    Stage.prefabContentsRoot,
                    Atmosphere,
                    "Atmosphere authoring");
                EditorGUILayout.HelpBox(
                    "PLL applies these references at runtime without modifying the assets.",
                    MessageType.Info);
            }
            catch (InvalidOperationException Failure)
            {
                EditorGUILayout.HelpBox(Failure.Message, MessageType.Error);
            }
        }
    }
}