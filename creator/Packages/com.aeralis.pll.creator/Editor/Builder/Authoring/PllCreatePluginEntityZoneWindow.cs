using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreatePluginEntityZoneWindow : EditorWindow
    {
        GameObject Parent;
        string Id;
        Vector3 LocalPosition;
        Vector2 Scroll;
        readonly PllAuthoringVolume Volume = new PllAuthoringVolume();
        readonly PllPluginEntityZoneSource Source = new PllPluginEntityZoneSource();

        [MenuItem("PLL/Spawning/Create Plugin Entity Zone", priority = 33)]
        static void Open()
        {
            var Window = GetWindow<PllCreatePluginEntityZoneWindow>("Create Plugin Entity Zone");
            Window.Parent = Selection.activeGameObject;
            Window.Id = "entity-" + Guid.NewGuid().ToString("N");
            Window.Show();
        }

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Parent = (GameObject)EditorGUILayout.ObjectField("Parent", Parent, typeof(GameObject), true);
            Id = EditorGUILayout.TextField("Zone ID", Id);
            LocalPosition = EditorGUILayout.Vector3Field("Parent-local Position", LocalPosition);
            Volume.Draw();
            Source.PluginGuid = EditorGUILayout.TextField("Plugin GUID", Source.PluginGuid);
            Source.FactoryId = EditorGUILayout.TextField("Factory ID", Source.FactoryId);
            Source.Revision = EditorGUILayout.TextField("Factory Revision", Source.Revision);
            Source.Prefab = (GameObject)EditorGUILayout.ObjectField("Code-free Prefab", Source.Prefab, typeof(GameObject), false);
            Source.MaximumInitialCount = EditorGUILayout.IntField("Maximum Initial Count", Source.MaximumInitialCount);
            Source.SpawnChance = EditorGUILayout.Slider("Zone Spawn Chance", Source.SpawnChance, 0f, 1f);
            Source.MaximumSlope = EditorGUILayout.Slider("Maximum Slope (degrees)", Source.MaximumSlope, 0f, 90f);
            Source.MinimumSpacing = EditorGUILayout.FloatField("Minimum Spacing", Source.MinimumSpacing);
            EditorGUILayout.HelpBox("Install the selected plugin factory revision. Use a prefab with only mesh, bone, collider and Rigidbody components. The zone spawns a fixed population and recreates it on Continue. Dynamic actors and bundled plugin scripts are not supported.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Parent))
                if (GUILayout.Button("Create Plugin Entity Zone"))
                {
                    try
                    {
                        PllCreateNativeActorZoneWindow.CreateEntity(Parent, Id, LocalPosition, Volume,
                            new PllPluginEntityZoneSource
                            {
                                PluginGuid = Source.PluginGuid, FactoryId = Source.FactoryId, Revision = Source.Revision,
                                Prefab = Source.Prefab, MaximumInitialCount = Source.MaximumInitialCount,
                                SpawnChance = Source.SpawnChance, MaximumSlope = Source.MaximumSlope,
                                MinimumSpacing = Source.MinimumSpacing
                            });
                        Close();
                    }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            EditorGUILayout.EndScrollView();
        }
    }
}
