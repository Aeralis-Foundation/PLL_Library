using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateHazardWindow : EditorWindow
    {
        internal enum Profile { ContactInjury, ArrowShooter, MovingSaw, SpikeTrap, InstalledPlugin }
        Profile Kind;
        GameObject Target;
        GameObject Model;
        bool InitiallyEnabled = true;
        [SerializeReference] PllHazardBehavior Behavior = new PllContactInjuryBehavior();

        [MenuItem("PLL/Spawning/Create Hazard", priority = 24)]
        static void Open()
        {
            var Window = GetWindow<PllCreateHazardWindow>("Create Hazard");
            Window.Target = Selection.activeGameObject;
            Window.Show();
        }

        internal static PllHazardBehavior CreateBehavior(Profile Kind) => Kind switch
        {
            Profile.ContactInjury => new PllContactInjuryBehavior(),
            Profile.ArrowShooter => new PllArrowShooterBehavior(),
            Profile.MovingSaw => new PllMovingSawBehavior(),
            Profile.SpikeTrap => new PllSpikeTrapBehavior(),
            Profile.InstalledPlugin => new PllPluginHazardBehavior(),
            _ => throw new InvalidOperationException("Hazard profile is unsupported.")
        };

        void OnGUI()
        {
            Target = (GameObject)EditorGUILayout.ObjectField("Definition Root", Target, typeof(GameObject), true);
            Model = (GameObject)EditorGUILayout.ObjectField("Owned Model", Model, typeof(GameObject), true);
            var Selected = (Profile)EditorGUILayout.EnumPopup("Behavior", Kind);
            if (Selected != Kind) { Kind = Selected; Behavior = CreateBehavior(Kind); }
            InitiallyEnabled = EditorGUILayout.Toggle("Initially Enabled", InitiallyEnabled);
            using (var Serialized = new SerializedObject(this))
            {
                Serialized.Update();
                EditorGUILayout.PropertyField(Serialized.FindProperty("Behavior"), true);
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorGUILayout.HelpBox("The definition owns behavior, not population. Contact Injury uses an owned solid collider and native per-victim cooldown. Moving and networked profiles require their declared bindings. Installed plugin instances regenerate on Continue and do not support reversible source actions.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Target || !Model))
                if (GUILayout.Button("Create Hazard"))
                    try { Create(Target, Model, InitiallyEnabled, Behavior); Close(); }
                    catch (InvalidOperationException Failure) { EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close"); }
        }

        internal static PllHazardAuthoring Create(GameObject Target, GameObject Model,
            bool InitiallyEnabled, PllHazardBehavior Behavior)
        {
            const string Action = "Create Hazard";
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PllHazardAuthoring>(Target, Action);
            var Id = "hazard-" + Guid.NewGuid().ToString("N");
            if (PllHazardValueContract.Failure(Target.transform, Id, Model, InitiallyEnabled, Behavior) is string Failure)
                throw new InvalidOperationException(Failure);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, Model.transform, Action);
            if (Target.GetComponentInChildren<PllHazardAuthoring>(true)
                || Target.GetComponentInChildren<PllEntityAuthoring>(true)
                || Target.GetComponentInChildren<CollisionModifier>(true))
                throw new InvalidOperationException("Hazard root already owns actor definitions or native contact mechanics.");
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Source = Undo.AddComponent<PllHazardAuthoring>(Target);
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.Property(Serialized, "IdValue").stringValue = Id;
                    PllAuthoringCommands.SetReference(Serialized, "ModelRootValue", Model);
                    PllAuthoringCommands.Property(Serialized, "InitiallyEnabledValue").boolValue = InitiallyEnabled;
                    PllAuthoringCommands.Property(Serialized, "BehaviorValue").managedReferenceValue = Behavior;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Source;
            });
        }
    }

    [CustomEditor(typeof(PllHazardAuthoring))]
    internal sealed class PllHazardAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var Id = serializedObject.FindProperty("IdValue");
            if (Id.stringValue.Length == 0) Id.stringValue = "hazard-" + Guid.NewGuid().ToString("N");
            EditorGUILayout.LabelField("Source ID", Id.stringValue);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ModelRootValue"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("InitiallyEnabledValue"));
            var Behavior = serializedObject.FindProperty("BehaviorValue");
            var Kind = Behavior.managedReferenceValue switch
            {
                PllArrowShooterBehavior => PllCreateHazardWindow.Profile.ArrowShooter,
                PllMovingSawBehavior => PllCreateHazardWindow.Profile.MovingSaw,
                PllSpikeTrapBehavior => PllCreateHazardWindow.Profile.SpikeTrap,
                PllPluginHazardBehavior => PllCreateHazardWindow.Profile.InstalledPlugin,
                _ => PllCreateHazardWindow.Profile.ContactInjury
            };
            var Selected = (PllCreateHazardWindow.Profile)EditorGUILayout.EnumPopup("Behavior", Kind);
            if (Behavior.managedReferenceValue is null || Selected != Kind)
                Behavior.managedReferenceValue = PllCreateHazardWindow.CreateBehavior(Selected);
            EditorGUILayout.PropertyField(Behavior, true);
            serializedObject.ApplyModifiedProperties();
            if (PllHazardValueContract.Failure((PllHazardAuthoring)target) is string Failure)
                EditorGUILayout.HelpBox(Failure, MessageType.Error);
            EditorGUILayout.HelpBox("Injury Amount is applied per native contact, not per second. Cooldown uses native scaled time. Contact support collision remains intact when behavior is disabled. Continue recreates native contact cooldown.", MessageType.Info);
        }
    }
}
