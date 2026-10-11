using System;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllApplyEffectAppearanceWindow : EditorWindow
    {
        PllEffectAuthoring Effect;
        PllEffectParticleColorMode ParticleMode = PllEffectParticleColorMode.Original;
        Color ParticleColor = Color.white;
        PllEffectMaterialColorChannel Channel = PllEffectMaterialColorChannel.None;
        Renderer Renderer;
        int MaterialSlot;
        Color MaterialColor = Color.white;

        [MenuItem("PLL/Effects/Apply Effect Appearance", priority = 21)]
        static void Open()
        {
            var Window = GetWindow<PllApplyEffectAppearanceWindow>("Effect Appearance");
            Window.Load(Selection.activeGameObject ? Selection.activeGameObject.GetComponent<PllEffectAuthoring>() : null);
            Window.Show();
        }

        void Load(PllEffectAuthoring Source)
        {
            Effect = Source;
            if (!Effect) return;
            ParticleMode = Effect.ParticleColorMode;
            ParticleColor = Effect.ParticleColor;
            Channel = Effect.MaterialColorChannel;
            Renderer = Effect.ColorRenderer;
            MaterialSlot = Effect.ColorMaterialSlot;
            MaterialColor = Effect.MaterialColor;
        }

        void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            var Source = (PllEffectAuthoring)EditorGUILayout.ObjectField("Effect", Effect, typeof(PllEffectAuthoring), true);
            if (EditorGUI.EndChangeCheck()) Load(Source);
            ParticleMode = (PllEffectParticleColorMode)EditorGUILayout.EnumPopup("Particle Start Color", ParticleMode);
            if (ParticleMode == PllEffectParticleColorMode.Override)
                ParticleColor = EditorGUILayout.ColorField("Particle Color and Alpha", ParticleColor);
            Channel = (PllEffectMaterialColorChannel)EditorGUILayout.EnumPopup("Material Color Channel", Channel);
            if (Channel != PllEffectMaterialColorChannel.None)
            {
                if (Effect && Effect.NativeParticles == PllNativeEffectParticles.None)
                {
                    Renderer = (Renderer)EditorGUILayout.ObjectField("Owned Renderer", Renderer, typeof(Renderer), true);
                    MaterialSlot = EditorGUILayout.IntField("Material Slot", MaterialSlot);
                }
                MaterialColor = EditorGUILayout.ColorField("Material Color and Alpha", MaterialColor);
            }
            EditorGUILayout.HelpBox("Particle Color changes the selected systems' start colors while the effect plays. "
                + "Material Color changes one color property in the selected renderer slot. "
                + "Native fire and smoke use Color in slot 0. Original colors and property blocks are restored afterward. "
                + "Shared materials and the effect's biome Color stay unchanged.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Effect))
                if (GUILayout.Button("Apply Effect Appearance"))
                {
                    var AuthoredRenderer = Effect.NativeParticles == PllNativeEffectParticles.None
                        && Channel != PllEffectMaterialColorChannel.None;
                    try
                    {
                        Apply(Effect, ParticleMode, ParticleColor, Channel,
                            AuthoredRenderer ? Renderer : null, AuthoredRenderer ? MaterialSlot : 0, MaterialColor);
                        Close();
                    }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        internal static void Apply(PllEffectAuthoring Effect, PllEffectParticleColorMode ParticleMode,
            Color ParticleColor, PllEffectMaterialColorChannel Channel, Renderer Renderer,
            int MaterialSlot, Color MaterialColor)
        {
            const string Action = "Apply Effect Appearance";
            if (!Effect || !Effect.Root) throw new InvalidOperationException("Choose an effect with its owned root.");
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Effect.gameObject, Action);
            PllEffectAppearanceValueContract.ValidateParticleColor(ParticleMode, ParticleColor,
                Effect.Particles.Count != 0 || Effect.NativeParticles != PllNativeEffectParticles.None);
            PllEffectAppearanceValueContract.ValidateMaterialColor(Effect.Root, Owner.StageRoot.transform,
                Effect.NativeParticles, Channel, Renderer, MaterialSlot, MaterialColor);
            if (Renderer) PllSourceIdentityValidator.RequireEditableTarget(Owner, Renderer.transform, Action);
            Undo.RecordObject(Effect, Action);
            using (var Serialized = new SerializedObject(Effect))
            {
                PllAuthoringCommands.Property(Serialized, "ParticleColorModeValue").intValue = (int)ParticleMode;
                PllAuthoringCommands.Property(Serialized, "ParticleColorValue").colorValue = ParticleColor;
                PllAuthoringCommands.Property(Serialized, "MaterialColorChannelValue").intValue = (int)Channel;
                PllAuthoringCommands.SetReference(Serialized, "ColorRendererValue", Renderer);
                PllAuthoringCommands.Property(Serialized, "ColorMaterialSlotValue").intValue = MaterialSlot;
                PllAuthoringCommands.Property(Serialized, "MaterialColorValue").colorValue = MaterialColor;
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(Effect);
            EditorSceneManager.MarkSceneDirty(Effect.gameObject.scene);
        }
    }
}
