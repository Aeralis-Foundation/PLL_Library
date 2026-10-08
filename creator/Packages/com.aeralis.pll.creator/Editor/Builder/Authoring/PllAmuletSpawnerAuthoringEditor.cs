using PLL.Library;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PLL.Creator.Editor
{
    [CustomEditor(typeof(PllAmuletSpawnerAuthoring))]
    internal sealed class PllAmuletSpawnerAuthoringEditor : PllAuthoringComponentEditor
    {
        readonly BoxBoundsHandle Box = new BoxBoundsHandle();
        readonly SphereBoundsHandle Sphere = new SphereBoundsHandle();

        public override void OnSceneGUI()
        {
            if (targets.Length != 1) return;
            var Source = (PllAmuletSpawnerAuthoring)target;
            serializedObject.UpdateIfRequiredOrScript();
            var Volume = serializedObject.FindProperty("VolumeValue");
            using (new Handles.DrawingScope(new Color(0.3f, 0.8f, 1f, 0.9f), Source.transform.localToWorldMatrix))
            {
                EditorGUI.BeginChangeCheck();
                if (Source.Volume.Shape == PllZoneShape.Box)
                {
                    Box.center = Source.Volume.Center; Box.size = Source.Volume.Size;
                    Box.DrawHandle();
                    if (!EditorGUI.EndChangeCheck()) return;
                    Volume.FindPropertyRelative("center").vector3Value = Box.center;
                    Volume.FindPropertyRelative("size").vector3Value = Box.size;
                }
                else
                {
                    Sphere.center = Source.Volume.Center; Sphere.radius = Source.Volume.Radius;
                    Sphere.DrawHandle();
                    if (!EditorGUI.EndChangeCheck()) return;
                    Volume.FindPropertyRelative("center").vector3Value = Sphere.center;
                    Volume.FindPropertyRelative("radius").floatValue = Sphere.radius;
                }
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
