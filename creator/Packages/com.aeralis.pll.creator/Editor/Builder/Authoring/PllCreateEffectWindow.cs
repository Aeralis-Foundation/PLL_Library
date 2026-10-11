using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateEffectWindow : EditorWindow
    {
        GameObject Target;
        PllEffectMode Mode = PllEffectMode.Timed;
        float Duration = 1f;
        string Hook = "";
        AudioClip Ambience;
        AudioClip Sound;
        Color EffectColor = Color.white;
        GameObject Presentation;
        PllNativeEffectParticles NativeParticles = PllNativeEffectParticles.None;

        [MenuItem("PLL/Effects/Add Effect to Selected Presentation", priority = 20)]
        static void Open()
        {
            var Window = GetWindow<PllCreateEffectWindow>("Add Effect");
            Window.Target = Selection.activeGameObject;
            Window.Show();
        }

        Vector2 Scroll;

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Target = (GameObject)EditorGUILayout.ObjectField("Source", Target, typeof(GameObject), true);
            Mode = (PllEffectMode)EditorGUILayout.EnumPopup("Mode", Mode);
            Duration = EditorGUILayout.FloatField("Duration Seconds", Duration);
            if (Mode == PllEffectMode.EmitHook) Hook = EditorGUILayout.TextField("Emit Hook", Hook);
            Ambience = (AudioClip)EditorGUILayout.ObjectField("Ambience", Ambience, typeof(AudioClip), false);
            Sound = (AudioClip)EditorGUILayout.ObjectField("Sound", Sound, typeof(AudioClip), false);
            EffectColor = EditorGUILayout.ColorField("Biome Ambient Color", EffectColor);
            Presentation = (GameObject)EditorGUILayout.ObjectField("Presentation Child", Presentation, typeof(GameObject), true);
            NativeParticles = (PllNativeEffectParticles)EditorGUILayout.EnumPopup("Native Particles", NativeParticles);
            EditorGUILayout.HelpBox("Use existing particles or native fire or smoke, not both. Native particles require no custom particles or presentation child. Bind Zone effects to an Effect Zone or Native Resolver.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Target))
                if (GUILayout.Button("Add Effect"))
                {
                    try { Create(Target, Mode, Duration, Mode == PllEffectMode.EmitHook ? Hook : "", Ambience, Sound, EffectColor, Presentation, NativeParticles); Close(); }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            EditorGUILayout.EndScrollView();
        }

        internal static PllEffectAuthoring Create(GameObject Target, PllEffectMode Mode,
            float Duration, string Hook, AudioClip Ambience, AudioClip Sound,
            Color Color, GameObject Presentation)
            => Create(Target, Mode, Duration, Hook, Ambience, Sound, Color, Presentation, PllNativeEffectParticles.None);

        internal static PllEffectAuthoring Create(GameObject Target, PllEffectMode Mode,
            float Duration, string Hook, AudioClip Ambience, AudioClip Sound,
            Color Color, GameObject Presentation, PllNativeEffectParticles NativeParticles)
        {
            const string Action = "Add Effect";
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PllEffectAuthoring>(Target, Action);
            if (Target == Owner.StageRoot)
                throw new InvalidOperationException("An effect requires its own child root.");
            if (!Enum.IsDefined(typeof(PllEffectMode), Mode)
                || !PllAuthoringValueContract.CanEncode(Duration) || Duration < 0f
                || Mode == PllEffectMode.Timed && Duration <= 0f
                || Mode == PllEffectMode.EmitHook && !PllAuthoringValueContract.IsHook(Hook)
                || Mode != PllEffectMode.EmitHook && Hook.Length != 0)
                throw new InvalidOperationException("Choose a supported effect mode, valid duration and a hook only for Emit Hook.");
            if (Ambience && !EditorUtility.IsPersistent(Ambience)
                || Sound && !EditorUtility.IsPersistent(Sound))
                throw new InvalidOperationException("Effect audio must use saved assets.");
            PllSourceIdentityValidator.ValidateColor(Color, "Effect color");
            if (Presentation)
            {
                if (Presentation == Target || !Presentation.transform.IsChildOf(Target.transform))
                    throw new InvalidOperationException("Presentation must be a strict child of the effect source.");
                PllSourceIdentityValidator.RequireEditableTarget(Owner, Presentation.transform, Action);
            }
            foreach (var Effect in Owner.StageRoot.GetComponentsInChildren<PllEffectAuthoring>(true))
                if (TransformOwnership.Overlaps(Target.transform, Effect.transform))
                    throw new InvalidOperationException("Effect roots cannot overlap.");
            var Particles = PllPresentationAuthoringValidator.ValidateEffectParticleGraph(Target, Action);
            PllEffectAppearanceValueContract.ValidateNativeParticles(NativeParticles, Particles.Length != 0, Presentation);
            foreach (var Particle in Target.GetComponentsInChildren<ParticleSystem>(true))
            {
                PllSourceIdentityValidator.RequireEditableTarget(Owner, Particle.transform, Action);
                if (!TransformOwnership.DiscoverableWhenActivated(Target.transform, Particle.transform))
                    throw new InvalidOperationException("Effect particles must have active ancestors.");
            }
            if (Particles.Length == 0 && !Ambience && !Sound && !Presentation
                && NativeParticles == PllNativeEffectParticles.None)
                throw new InvalidOperationException("Supply existing particles, audio or a presentation child before adding an effect.");
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Source = Undo.AddComponent<PllEffectAuthoring>(Target);
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.SetReference(Serialized, "root", Target);
                    PllAuthoringCommands.SetReferences(Serialized, "particles", Particles);
                    PllAuthoringCommands.SetReference(Serialized, "ambience", Ambience);
                    PllAuthoringCommands.SetReference(Serialized, "sound", Sound);
                    PllAuthoringCommands.SetReference(Serialized, "presentationRoot", Presentation);
                    PllAuthoringCommands.Property(Serialized, "mode").intValue = (int)Mode;
                    PllAuthoringCommands.Property(Serialized, "duration").floatValue = Duration;
                    PllAuthoringCommands.Property(Serialized, "emitHook").stringValue = Hook;
                    PllAuthoringCommands.Property(Serialized, "color").colorValue = Color;
                    PllAuthoringCommands.Property(Serialized, "NativeParticlesValue").intValue = (int)NativeParticles;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Source;
            });
        }
    }
}
