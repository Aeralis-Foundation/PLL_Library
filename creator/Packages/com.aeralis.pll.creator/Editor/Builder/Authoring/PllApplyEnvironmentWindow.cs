using System;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllApplyEnvironmentWindow : EditorWindow
    {
        GameObject Root;
        PllBiomeDefinition Source;

        [MenuItem("PLL/Visuals/Apply Environment from Existing Biome", priority = 23)]
        static void Open()
        {
            var Window = GetWindow<PllApplyEnvironmentWindow>("Apply Existing Environment");
            Window.Root = Selection.activeGameObject;
            Window.Source = null;
            Window.Show();
        }

        void OnGUI()
        {
            Root = (GameObject)EditorGUILayout.ObjectField("Target Biome", Root, typeof(GameObject), true);
            Source = (PllBiomeDefinition)EditorGUILayout.ObjectField("Source Biome Asset", Source, typeof(PllBiomeDefinition), false);
            EditorGUILayout.HelpBox("Applies an existing biome's lighting profile, color, audio catalogs and provided atmosphere, fog and native visual settings in one Undo operation. It does not change biome behavior, clone weather/particles/campsites, or modify source assets. Those remain owned by their current source objects.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Root || !Source))
                if (GUILayout.Button("Apply Existing Environment"))
                {
                    try { Apply(Root, Source); Close(); }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        internal static void Apply(GameObject Root, PllBiomeDefinition Source) => Apply(Root, Source, null);

        internal static void Apply(GameObject Root, DefinitionCatalog Catalog) => Apply(Root, Catalog.Definition, Catalog);

        static void Apply(GameObject Root, PllBiomeDefinition Source, DefinitionCatalog Catalog)
        {
            const string Action = "Apply Existing Environment";
            PllAtmosphereAuthoringCommand.RequireCurrentBiomeRoot(Root);
            var Target = Root.GetComponent<PllBiomeRoot>();
            if (!Target || !Target.Definition || !Source || !AssetDatabase.IsMainAsset(Source))
                throw new InvalidOperationException("Select a linked target biome and one persistent source biome definition.");
            PllSourceIdentityValidator.ResolveEditableOwner(
                Root.GetComponentInChildren<PllBiomePrefabAnchors>(true).FloorRoot, Action);
            if (Source == Target.Definition)
                throw new InvalidOperationException("Choose a different source biome definition.");
            Catalog ??= new PllSourceCatalogValidator().Validate(Source);
            var Fog = Source.RootPrefab.GetComponent<CustomSegmentRuntime>().FogOrigin.GetComponent<PllFogAuthoring>();
            var TargetFogOrigin = Root.GetComponent<CustomSegmentRuntime>().FogOrigin;
            var TargetFog = TargetFogOrigin ? TargetFogOrigin.GetComponent<PllFogAuthoring>() : null;
            if (!TargetFog)
                throw new InvalidOperationException("The target fog origin requires its existing fog authoring component.");
            var NestedFogRoot = PrefabUtility.GetNearestPrefabInstanceRoot(TargetFogOrigin.gameObject);
            if (TargetFogOrigin.gameObject.scene != Root.scene
                || !TransformOwnership.Contains(Root.transform, TargetFogOrigin.transform)
                || NestedFogRoot && NestedFogRoot != Root)
                throw new InvalidOperationException("The target fog origin must belong to the current biome, outside nested prefab instances.");
            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            try
            {
                Undo.RecordObject(Target.Definition, Action);
                using (var Serialized = new SerializedObject(Target.Definition))
                {
                    PllAuthoringCommands.SetReference(Serialized, "dayNightProfile", Source.DayNightProfile);
                    PllAuthoringCommands.Property(Serialized, "color").colorValue = Source.Color;
                    PllAuthoringCommands.SetReferences(Serialized, "ambienceSounds", Source.AmbienceSounds);
                    PllAuthoringCommands.SetReferences(Serialized, "effectSounds", Source.EffectSounds);
                    PllAuthoringCommands.SetReferences(Serialized, "enterSounds", Source.EnterSounds);
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                if (Catalog.Atmosphere)
                    ApplyRoot<PllAtmosphereAuthoring>(Root, Action, Serialized =>
                    {
                        PllAuthoringCommands.SetReference(Serialized, "SkyboxMaterialValue", Catalog.Atmosphere.SkyboxMaterial);
                        PllAuthoringCommands.SetReference(Serialized, "FogTextureValue", Catalog.Atmosphere.FogTexture);
                    });
                if (Catalog.NativeVisualResolvers.Length != 0)
                    ApplyRoot<PLLNativeVisualResolver>(Root, Action, Serialized =>
                    {
                        PllAuthoringCommands.Property(Serialized, "preset").intValue = (int)Catalog.NativeVisualResolvers[0].Preset;
                        PllAuthoringCommands.Property(Serialized, "SuppressNativeParticlesValue").boolValue = Catalog.NativeVisualResolvers[0].SuppressNativeParticles;
                        PllAuthoringCommands.Property(Serialized, "ParticleColorModeValue").intValue = (int)Catalog.NativeVisualResolvers[0].ParticleColorMode;
                        PllAuthoringCommands.Property(Serialized, "ParticleColorValue").colorValue = Catalog.NativeVisualResolvers[0].ParticleColor;
                        PllAuthoringCommands.Property(Serialized, "MaterialColorChannelValue").intValue = (int)Catalog.NativeVisualResolvers[0].MaterialColorChannel;
                        PllAuthoringCommands.Property(Serialized, "MaterialColorValue").colorValue = Catalog.NativeVisualResolvers[0].MaterialColor;
                    });
                Undo.RecordObject(TargetFog, Action);
                using (var Serialized = new SerializedObject(TargetFog))
                {
                    PllAuthoringCommands.Property(Serialized, "mode").intValue = (int)Fog.Mode;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(TargetFog);
                EditorUtility.SetDirty(Target.Definition);
                EditorSceneManager.MarkSceneDirty(Root.scene);
                Undo.CollapseUndoOperations(Group);
                Selection.activeObject = Target.Definition;
            }
            catch
            {
                Undo.RevertAllDownToGroup(Group);
                throw;
            }
        }

        static void ApplyRoot<T>(GameObject Root, string Action,
            Action<SerializedObject> Configure) where T : Component
        {
            var Target = Root.GetComponent<T>();
            if (Target) Undo.RecordObject(Target, Action);
            else Target = Undo.AddComponent<T>(Root);
            using (var Serialized = new SerializedObject(Target))
            {
                Configure(Serialized);
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(Target);
        }
    }
}
