using System;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal static class PllEntityZoneCommands
    {
        [MenuItem("PLL/Spawning/Make Entity Zone", priority = 30)]
        static void MakeEntityZone()
        {
            try
            {
                var Stage = PrefabStageUtility.GetCurrentPrefabStage();
                var Selected = Selection.activeGameObject;
                if (!Stage || Selection.gameObjects.Length != 1 || !Selected
                    || !Selected.transform.IsChildOf(Stage.prefabContentsRoot.transform))
                    throw new InvalidOperationException("Select one empty object under FloorRoot or CampfireRoot in Prefab Mode.");
                var Link = Stage.prefabContentsRoot.GetComponent<PllBiomeRoot>();
                var Definition = Link ? Link.Definition : null;
                var Anchors = Stage.prefabContentsRoot.GetComponent<PllBiomePrefabAnchors>();
                if (!Definition || !Anchors || !Anchors.FloorRoot || !Anchors.CampfireRoot
                    || Selected == Anchors.FloorRoot || Selected == Anchors.CampfireRoot
                    || !Selected.activeSelf || Selected.GetComponents<Component>().Length != 1 || Selected.transform.childCount != 0)
                    throw new InvalidOperationException("Select an empty object in a PLL biome prefab.");
                var Id = "entity-" + Guid.NewGuid().ToString("N");
                PllActorZoneAuthoringValidator.ValidateNewSourceParent(Stage.prefabContentsRoot,
                    Anchors.FloorRoot.transform, Anchors.CampfireRoot.transform, Selected.transform, Id);
                PllAuthoringCommands.Create("Make Entity Zone", () =>
                {
                    var Component = Undo.AddComponent<PllEntityZoneAuthoring>(Selected);
                    using (var Serialized = new SerializedObject(Component))
                    {
                        PllAuthoringCommands.Property(Serialized, "IdValue").stringValue = Id;
                        var Volume = PllAuthoringCommands.Property(Serialized, "VolumeValue");
                        Volume.FindPropertyRelative("shape").intValue = (int)PllZoneShape.Box;
                        Volume.FindPropertyRelative("size").vector3Value = new Vector3(20f, 10f, 20f);
                        Serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    return Component;
                });
            }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }
    }
}
