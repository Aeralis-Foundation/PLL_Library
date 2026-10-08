using System;
using PLL.Library;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PLL.Creator.Editor
{
    [CustomEditor(typeof(PllPropZoneAuthoring))]
    internal sealed class PllPropZoneAuthoringEditor : PllAuthoringComponentEditor
    {
        readonly BoxBoundsHandle BoxHandle = new BoxBoundsHandle();
        readonly SphereBoundsHandle SphereHandle = new SphereBoundsHandle();
        SerializedProperty CenterProperty;
        SerializedProperty SizeProperty;
        SerializedProperty RadiusProperty;
        bool PaintRegion;
        bool ExplainZero;
        (SceneView View, int Control, Vector3 Start, PllZoneVolume Volume, Matrix4x4 Pose)? Stroke;

        void OnEnable()
        {
            var Volume = serializedObject.FindProperty("volume");
            CenterProperty = Volume.FindPropertyRelative("center");
            SizeProperty = Volume.FindPropertyRelative("size");
            RadiusProperty = Volume.FindPropertyRelative("radius");
        }

        void OnDisable() => ReleasePaintControl();

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            if (targets.Length != 1) return;
            var Zone = (PllPropZoneAuthoring)target;
            EditorGUI.BeginChangeCheck();
            PaintRegion = EditorGUILayout.Toggle("Paint Region in Scene", PaintRegion);
            if (EditorGUI.EndChangeCheck())
            {
                ReleasePaintControl();
                SceneView.RepaintAll();
            }
            if (PaintRegion)
                EditorGUILayout.HelpBox("Drag on the zone's local XZ plane. Boxes retain their height; spheres use the first point as their center. Release to commit, or press Escape to discard.", MessageType.Info);
            ExplainZero = EditorGUILayout.Foldout(ExplainZero, "Explain Zero (Static Checks)", true);
            if (!ExplainZero) return;
            foreach (var Spawner in Zone.Spawners)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Spawner", Spawner, typeof(PropSpawner), true);
                var Cause = FindStaticZeroCause(Spawner);
                EditorGUILayout.HelpBox(Cause ?? "No supported static zero cause found.",
                    Cause == null ? MessageType.None : MessageType.Warning);
            }
            EditorGUILayout.HelpBox("These checks read configured candidates and constraints. The separate sampling-bounds preview does not prove a zero result. Override candidates, physics hits, material/embedded/post constraints and native seeded generation require native testing.", MessageType.Info);
        }

        internal static string FindStaticZeroCause(PropSpawner Spawner)
        {
            if (!Spawner) return "The zone has a missing spawner reference.";
            if ((!Spawner.randomSpawns && Spawner.nrOfSpawns <= 0)
                || Spawner.randomSpawns && Math.Max(Spawner.minSpawnCount, Spawner.nrOfSpawns) <= 0)
                return "No positive spawn count is configured.";
            if (Spawner.GetType() == typeof(PropSpawner)
                && (!Spawner.overrideProps || !Spawner.overridePropsList)
                && Spawner.props != null && Spawner.props.Length == 0)
                return "Native spawning fails because the candidate array is empty.";
            if (Spawner.constraints != null)
                foreach (var Constraint in Spawner.constraints)
                    if (Constraint is PSC_Normal Normal && !Normal.mute
                        && Normal.GetType() == typeof(PSC_Normal)
                        && (Normal.maxAngle <= Normal.minAngle
                            || Normal.maxAngle <= 0f || Normal.minAngle >= 180f))
                        return "An unmuted normal constraint has no allowed slope angle.";
            return null;
        }

        public override void OnSceneGUI()
        {
            if (targets.Length != 1) return;
            var Zone = (PllPropZoneAuthoring)target;
            if (Stroke.HasValue && !Stroke.Value.View) ReleasePaintControl();
            if (Stroke.HasValue && Stroke.Value.View != SceneView.currentDrawingSceneView) return;
            serializedObject.UpdateIfRequiredOrScript();
            if (Stroke.HasValue && (!Zone.Volume.Equals(Stroke.Value.Volume)
                || !Zone.transform.localToWorldMatrix.Equals(Stroke.Value.Pose))) ReleasePaintControl();
            if (PllAuthoringValueContract.ZoneVolumeFailure(Zone.Volume) != null)
            {
                ReleasePaintControl();
                return;
            }
            if (PaintRegion)
            {
                DrawRegionPainting(Zone);
                return;
            }
            using (new Handles.DrawingScope(
                new Color(0.2f, 0.75f, 1f, 0.9f),
                Zone.transform.localToWorldMatrix))
            {
                if (Zone.Volume.Shape == PllZoneShape.Box)
                    DrawBox();
                else
                    DrawSphere();
            }
        }

        void DrawRegionPainting(PllPropZoneAuthoring Zone)
        {
            var Current = Event.current;
            var Control = GUIUtility.GetControlID(FocusType.Passive);
            if (Stroke.HasValue && GUIUtility.hotControl != Stroke.Value.Control)
                ReleasePaintControl();
            if (Current.type == EventType.Layout && !Current.alt)
                HandleUtility.AddDefaultControl(Control);
            if (Stroke.HasValue && (Current.type == EventType.Ignore
                || Current.type == EventType.KeyDown && Current.keyCode == KeyCode.Escape))
            {
                ReleasePaintControl();
                Current.Use();
                SceneView.RepaintAll();
                return;
            }
            var Ray = HandleUtility.GUIPointToWorldRay(Current.mousePosition);
            var LocalRay = new Ray(Zone.transform.InverseTransformPoint(Ray.origin),
                Zone.transform.InverseTransformVector(Ray.direction));
            var Plane = new Plane(Vector3.up, Zone.Volume.Center);
            if (!Plane.Raycast(LocalRay, out var Distance))
            {
                if (Stroke.HasValue && Current.type == EventType.MouseUp && Current.button == 0)
                {
                    ReleasePaintControl();
                    Current.Use();
                    SceneView.RepaintAll();
                }
                return;
            }
            var Point = LocalRay.GetPoint(Distance);
            if (Current.type == EventType.MouseDown && Current.button == 0 && !Current.alt
                && HandleUtility.nearestControl == Control)
            {
                try { PllSourceIdentityValidator.ResolveEditableOwner(Zone.gameObject, "Paint Prop Zone"); }
                catch (InvalidOperationException Failure)
                {
                    EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    Current.Use();
                    return;
                }
                GUIUtility.hotControl = Control;
                Stroke = (SceneView.currentDrawingSceneView, Control, Point,
                    Zone.Volume, Zone.transform.localToWorldMatrix);
                Current.Use();
            }
            if (!Stroke.HasValue) return;
            var Start = Stroke.Value.Start;
            var Center = Zone.Volume.Shape == PllZoneShape.Box ? (Start + Point) * 0.5f : Start;
            var Size = new Vector3(Mathf.Abs(Point.x - Start.x), Zone.Volume.Size.y,
                Mathf.Abs(Point.z - Start.z));
            var Radius = Vector3.Distance(Start, Point);
            using (new Handles.DrawingScope(new Color(0.3f, 1f, 0.6f, 0.9f), Zone.transform.localToWorldMatrix))
            {
                if (Zone.Volume.Shape == PllZoneShape.Box) Handles.DrawWireCube(Center, Size);
                else
                {
                    Handles.DrawWireDisc(Center, Vector3.up, Radius);
                    Handles.DrawWireDisc(Center, Vector3.right, Radius);
                    Handles.DrawWireDisc(Center, Vector3.forward, Radius);
                }
            }
            if (Current.type == EventType.MouseDrag)
            {
                Current.Use();
                SceneView.RepaintAll();
            }
            if (Current.type != EventType.MouseUp || Current.button != 0) return;
            ReleasePaintControl();
            Current.Use();
            if (PllAuthoringValueContract.ZoneVolumeFailure(Zone.Volume.Shape, Center, Size, Radius) != null)
                return;
            try
            {
                PllSourceIdentityValidator.ResolveEditableOwner(Zone.gameObject, "Paint Prop Zone");
                Undo.IncrementCurrentGroup();
                var Group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Paint Prop Zone");
                CenterProperty.vector3Value = Center;
                if (Zone.Volume.Shape == PllZoneShape.Box) SizeProperty.vector3Value = Size;
                else RadiusProperty.floatValue = Radius;
                serializedObject.ApplyModifiedProperties();
                Undo.CollapseUndoOperations(Group);
            }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
            SceneView.RepaintAll();
        }

        void ReleasePaintControl()
        {
            if (Stroke.HasValue && GUIUtility.hotControl == Stroke.Value.Control) GUIUtility.hotControl = 0;
            Stroke = null;
        }

        void DrawBox()
        {
            BoxHandle.center = CenterProperty.vector3Value;
            BoxHandle.size = SizeProperty.vector3Value;
            EditorGUI.BeginChangeCheck();
            BoxHandle.DrawHandle();
            if (!EditorGUI.EndChangeCheck()) return;
            CenterProperty.vector3Value = BoxHandle.center;
            SizeProperty.vector3Value = BoxHandle.size;
            serializedObject.ApplyModifiedProperties();
        }

        void DrawSphere()
        {
            SphereHandle.center = CenterProperty.vector3Value;
            SphereHandle.radius = RadiusProperty.floatValue;
            EditorGUI.BeginChangeCheck();
            SphereHandle.DrawHandle();
            if (!EditorGUI.EndChangeCheck()) return;
            CenterProperty.vector3Value = SphereHandle.center;
            RadiusProperty.floatValue = SphereHandle.radius;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
