using PLL.Library;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PLL.Creator.Editor
{
    [CustomEditor(typeof(PllEntityZoneAuthoring))]
    internal sealed class PllEntityZoneAuthoringEditor : PllActorZoneAuthoringEditor { }

    [CustomEditor(typeof(PllHazardZoneAuthoring))]
    internal sealed class PllHazardZoneAuthoringEditor : PllActorZoneAuthoringEditor { }

    internal abstract class PllActorZoneAuthoringEditor : PllAuthoringComponentEditor
    {
        readonly BoxBoundsHandle BoxHandle = new BoxBoundsHandle();
        readonly SphereBoundsHandle SphereHandle = new SphereBoundsHandle();

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.HelpBox("The volume limits initial placement, not movement. Actors keep PEAK's behavior and networking; hazards use native timing. Points and nest positions use zone-local coordinates.", MessageType.Info);
        }

        public override void OnSceneGUI()
        {
            if (targets.Length != 1) return;
            var Zone = (Component)target;
            var VolumeValue = Zone is PllEntityZoneAuthoring Entity ? Entity.Volume : ((PllHazardZoneAuthoring)Zone).Volume;
            if (PllAuthoringValueContract.ZoneVolumeFailure(VolumeValue) != null) return;
            serializedObject.UpdateIfRequiredOrScript();
            var Volume = serializedObject.FindProperty("VolumeValue");
            var Center = Volume.FindPropertyRelative("center");
            using (new Handles.DrawingScope(new Color(1f, 0.65f, 0.2f, 0.9f), Zone.transform.localToWorldMatrix))
            {
                EditorGUI.BeginChangeCheck();
                if (VolumeValue.Shape == PllZoneShape.Box)
                {
                    var Size = Volume.FindPropertyRelative("size");
                    BoxHandle.center = Center.vector3Value; BoxHandle.size = Size.vector3Value;
                    BoxHandle.DrawHandle();
                    if (!EditorGUI.EndChangeCheck()) return;
                    Center.vector3Value = BoxHandle.center; Size.vector3Value = BoxHandle.size;
                }
                else
                {
                    var Radius = Volume.FindPropertyRelative("radius");
                    SphereHandle.center = Center.vector3Value; SphereHandle.radius = Radius.floatValue;
                    SphereHandle.DrawHandle();
                    if (!EditorGUI.EndChangeCheck()) return;
                    Center.vector3Value = SphereHandle.center; Radius.floatValue = SphereHandle.radius;
                }
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
