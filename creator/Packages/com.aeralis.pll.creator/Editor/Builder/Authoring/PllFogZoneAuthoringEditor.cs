using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PLL.Creator.Editor
{
    [CustomEditor(typeof(PllFogZoneAuthoring))]
    internal sealed class PllFogZoneAuthoringEditor : UnityEditor.Editor
    {
        readonly BoxBoundsHandle Box = new BoxBoundsHandle();
        readonly SphereBoundsHandle Sphere = new SphereBoundsHandle();

        void OnEnable()
        {
            if (serializedObject.FindProperty("StableIdValue").stringValue.Length != 0) return;
            Undo.RecordObject(target, "Create Fog Zone Identity");
            serializedObject.FindProperty("StableIdValue").stringValue = Guid.NewGuid().ToString("N");
            serializedObject.ApplyModifiedProperties();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("volume"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("InitiallyEnabledValue"), new GUIContent("Initially Enabled"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("VisibilityDistanceValue"), new GUIContent("Visibility Distance (m)"));
            var Tint = serializedObject.FindProperty("ColorValue");
            Tint.colorValue = EditorGUILayout.ColorField(new GUIContent("Color"), Tint.colorValue, true, false, true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("BoundaryBlendDistanceValue"), new GUIContent("Boundary Blend Distance (m)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PriorityValue"), new GUIContent("Priority"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TransitionValue").FindPropertyRelative("SecondsValue"), new GUIContent("Cover Delay (s)"));
            serializedObject.ApplyModifiedProperties();
            PllBiomeDefinitionEditor.DrawDiagnostics(Validate((PllFogZoneAuthoring)target));
            EditorGUILayout.HelpBox("Scene handles edit the maximum volume. Camera fog renders in PEAK and never adds cold or damage.", MessageType.Info);
        }

        internal static IReadOnlyList<PllCreatorDiagnostic> Validate(PllFogZoneAuthoring Source)
        {
            var Diagnostics = new List<PllCreatorDiagnostic>();
            try
            {
                Source.ValidateSettings();
                var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Source.gameObject, "Edit Fog Zone");
                foreach (var Other in Owner.StageRoot.GetComponentsInChildren<PllFogZoneAuthoring>(true))
                    if (Other != Source && Other.StableId == Source.StableId)
                        throw new InvalidOperationException("Fog Zone identity is duplicated in this source.");
            }
            catch (Exception Failure) when (Failure is ArgumentException || Failure is InvalidOperationException)
            {
                Diagnostics.Add(new PllCreatorDiagnostic(PllCreatorDiagnosticSeverity.Error, Failure.Message, Source));
            }
            return Diagnostics;
        }

        void OnSceneGUI()
        {
            var Source = (PllFogZoneAuthoring)target;
            var Volume = Source.Volume;
            using (new Handles.DrawingScope(new Color(0.3f, 0.75f, 1f, 0.9f), Source.transform.localToWorldMatrix))
            {
                EditorGUI.BeginChangeCheck();
                if (Volume.Shape == PllZoneShape.Box)
                {
                    Box.center = Volume.Center; Box.size = Volume.Size; Box.DrawHandle();
                }
                else
                {
                    Sphere.center = Volume.Center; Sphere.radius = Volume.Radius; Sphere.DrawHandle();
                }
                if (!EditorGUI.EndChangeCheck()) return;
                Undo.RecordObject(Source, "Edit Fog Zone Volume");
                serializedObject.Update();
                var Property = serializedObject.FindProperty("volume");
                Property.FindPropertyRelative("center").vector3Value = Volume.Shape == PllZoneShape.Box ? Box.center : Sphere.center;
                if (Volume.Shape == PllZoneShape.Box) Property.FindPropertyRelative("size").vector3Value = Box.size;
                else Property.FindPropertyRelative("radius").floatValue = Sphere.radius;
                serializedObject.ApplyModifiedProperties();
            }
        }
    }

    [CustomPropertyDrawer(typeof(PllEffectZoneFogTrigger))]
    internal sealed class PllEffectZoneFogTriggerDrawer : PropertyDrawer
    {
        static readonly string[] OperationLabels = { "Activate Fog Zone", "Deactivate Fog Zone" };
        public override float GetPropertyHeight(SerializedProperty Property, GUIContent Label) =>
            3f * EditorGUIUtility.singleLineHeight + 2f * EditorGUIUtility.standardVerticalSpacing;

        public override void OnGUI(Rect Position, SerializedProperty Property, GUIContent Label)
        {
            EditorGUI.BeginProperty(Position, Label, Property);
            Position.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(Position, Property.FindPropertyRelative("TransitionValue"), new GUIContent("Transition"));
            Position.y += Position.height + EditorGUIUtility.standardVerticalSpacing;
            var Operation = Property.FindPropertyRelative("OperationValue");
            Operation.intValue = EditorGUI.Popup(Position, "Operation", Operation.intValue, OperationLabels);
            Position.y += Position.height + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(Position, Property.FindPropertyRelative("TargetValue"), new GUIContent("Fog Zone"));
            EditorGUI.EndProperty();
        }
    }

    internal sealed class PllCreateFogZoneWindow : EditorWindow
    {
        GameObject Parent;
        string Name = "Fog Zone";
        readonly PllAuthoringVolume Volume = new PllAuthoringVolume();
        bool InitiallyEnabled = true;
        float VisibilityDistance = 10f;
        Color Tint = Color.white;
        float BoundaryBlendDistance;
        int Priority;
        float CoverDelay;

        [MenuItem("PLL/Effects/Create Fog Zone in Selected Biome", priority = 24)]
        static void Open()
        {
            var Window = GetWindow<PllCreateFogZoneWindow>("Create Fog Zone");
            Window.Parent = Selection.activeGameObject; Window.Show();
        }

        void OnGUI()
        {
            Parent = (GameObject)EditorGUILayout.ObjectField("Parent", Parent, typeof(GameObject), true);
            Name = EditorGUILayout.TextField("Name", Name);
            Volume.Draw();
            InitiallyEnabled = EditorGUILayout.Toggle("Initially Enabled", InitiallyEnabled);
            VisibilityDistance = EditorGUILayout.FloatField("Visibility Distance (m)", VisibilityDistance);
            Tint = EditorGUILayout.ColorField(new GUIContent("Color"), Tint, true, false, true);
            BoundaryBlendDistance = EditorGUILayout.FloatField("Boundary Blend Distance (m)", BoundaryBlendDistance);
            Priority = EditorGUILayout.IntField("Priority", Priority);
            CoverDelay = EditorGUILayout.FloatField("Cover Delay (s)", CoverDelay);
            using (new EditorGUI.DisabledScope(!Parent))
                if (GUILayout.Button("Create Fog Zone"))
                {
                    try
                    {
                        Create(Parent, Name, Volume, InitiallyEnabled, VisibilityDistance,
                            Tint, BoundaryBlendDistance, Priority, CoverDelay);
                        Close();
                    }
                    catch (Exception Failure) when (Failure is ArgumentException || Failure is InvalidOperationException)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        internal static PllFogZoneAuthoring Create(GameObject Parent, string Name,
            PllAuthoringVolume Volume, bool InitiallyEnabled, float VisibilityDistance,
            Color Tint, float BoundaryBlendDistance, int Priority, float CoverDelay)
        {
            const string Action = "Create Fog Zone";
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Parent, Action);
            if (!Owner.IsBiomeRoot || !TransformOwnership.Contains(Owner.Floor, Parent.transform)
                && !TransformOwnership.Contains(Owner.Campfire, Parent.transform))
                throw new InvalidOperationException("Fog Zone parent must be under Floor Root or Campfire Root in the selected biome.");
            Volume.RequireValid();
            PllFogZoneAuthoring.ValidateTransform(Parent.transform, Volume.Shape);
            if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("Fog Zone name is required.");
            if (!PllAuthoringValueContract.CanEncode(VisibilityDistance) || VisibilityDistance <= 0f
                || !PllAuthoringValueContract.CanEncode(BoundaryBlendDistance) || BoundaryBlendDistance < 0f
                || !PllAuthoringValueContract.CanEncode(CoverDelay) || CoverDelay < 0f)
                throw new InvalidOperationException("Visibility must be positive; boundary blend and Cover Delay must be nonnegative finite values.");
            PllSourceIdentityValidator.ValidateColor(new Color(Tint.r, Tint.g, Tint.b, 1f), "Fog Zone color");
            if (!PllSourceIdentityValidator.DiscoverableWhenConfigured(Owner.StageRoot.transform, Parent.transform,
                    PllSourceIdentityValidator.VariationEntryRoots(Owner.StageRoot.GetComponentsInChildren<PllVariationAuthoring>(true)))
                || TransformOwnership.HasPropSpawnerInPath(Owner.StageRoot.transform, Parent.transform))
                throw new InvalidOperationException("Fog Zone parent must have active or configured variation ancestors outside generated-child storage.");
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Root = new GameObject(Name); Undo.RegisterCreatedObjectUndo(Root, Action);
                Root.transform.SetParent(Parent.transform, false);
                var Source = Undo.AddComponent<PllFogZoneAuthoring>(Root);
                using (var Serialized = new SerializedObject(Source))
                {
                    Serialized.FindProperty("StableIdValue").stringValue = Guid.NewGuid().ToString("N");
                    Volume.Apply(Serialized);
                    Serialized.FindProperty("InitiallyEnabledValue").boolValue = InitiallyEnabled;
                    Serialized.FindProperty("VisibilityDistanceValue").floatValue = VisibilityDistance;
                    Serialized.FindProperty("ColorValue").colorValue = Tint;
                    Serialized.FindProperty("BoundaryBlendDistanceValue").floatValue = BoundaryBlendDistance;
                    Serialized.FindProperty("PriorityValue").intValue = Priority;
                    Serialized.FindProperty("TransitionValue").FindPropertyRelative("SecondsValue").floatValue = CoverDelay;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Source;
            });
        }
    }
}
