using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateHookedGenerationWindow : EditorWindow
    {
        GameObject Target;
        bool GenerateOnLoad = true;
        string EmitHook = "";

        [MenuItem("PLL/Spawning/Add Hooked Generation to Selected Source", priority = 30)]
        static void Open()
        {
            var Window = GetWindow<PllCreateHookedGenerationWindow>("Add Hooked Generation");
            Window.Target = Selection.activeGameObject;
            Window.Show();
        }

        void OnGUI()
        {
            Target = (GameObject)EditorGUILayout.ObjectField("Source", Target, typeof(GameObject), true);
            GenerateOnLoad = EditorGUILayout.Toggle("Generate on Load", GenerateOnLoad);
            EmitHook = EditorGUILayout.TextField("Emit Hook", EmitHook);
            EditorGUILayout.HelpBox("Choose a configured spawner or source group. Candidate prefabs must be saved assets, not children of a spawner. Disable Generate on Load to trigger generation from an Effect Zone when players enter or leave.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Target || !PllAuthoringValueContract.IsHook(EmitHook)))
                if (GUILayout.Button("Add Hooked Generation"))
                {
                    try { Create(Target, GenerateOnLoad, EmitHook); Close(); }
                    catch (Exception Failure) when (Failure is InvalidOperationException || Failure is ArgumentException)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        internal static PllHookedPropGenerationAuthoring Create(GameObject Target,
            bool GenerateOnLoad, string EmitHook)
        {
            const string Action = "Add Hooked Generation";
            if (!PllAuthoringValueContract.IsHook(EmitHook))
                throw new InvalidOperationException("Emit Hook must contain one valid hook.");
            var Spawner = Target ? Target.GetComponent<PropSpawner>() : null;
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(
                Spawner && Target.transform.parent ? Target.transform.parent.gameObject : Target, Action);
            if (!Owner.IsBiomeRoot)
                throw new InvalidOperationException("Hooked Generation requires a stable source in the current biome prefab.");
            var Nested = PrefabUtility.GetNearestPrefabInstanceRoot(Target);
            if (Nested && Nested != Owner.StageRoot)
                throw new InvalidOperationException("Open the nested prefab source before adding Hooked Generation.");
            var Unstable = PllHookedPropGenerationSourceContract.UnstableRoots(Owner.StageRoot.transform);
            PllHookedPropGenerationSourceContract.ValidateSelection(Owner.StageRoot.transform,
                Owner.Floor, Owner.Campfire, Target.transform, Unstable);
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Marker = Undo.AddComponent<PllHookedPropGenerationAuthoring>(Target);
                using (var Serialized = new SerializedObject(Marker))
                {
                    PllAuthoringCommands.Property(Serialized, "GenerateOnLoadValue").boolValue = GenerateOnLoad;
                    PllAuthoringCommands.Property(Serialized, "EmitHookValue").stringValue = EmitHook;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Marker;
            });
        }
    }
}
