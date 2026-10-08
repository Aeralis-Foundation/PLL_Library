using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    [CustomEditor(typeof(PllSurfaceTextureAuthoring))]
    internal sealed class PllSurfaceTextureInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var Target = serializedObject.FindProperty("TargetValue");
            var PreviousTarget = Target.enumValueIndex;
            EditorGUILayout.PropertyField(Target);
            if (Target.enumValueIndex != PreviousTarget)
            {
                if (Target.enumValueIndex == 0) serializedObject.FindProperty("FluidSourceValue").objectReferenceValue = null;
                else
                {
                    serializedObject.FindProperty("RenderersValue").arraySize = 0;
                    serializedObject.FindProperty("SlotsValue").arraySize = 0;
                }
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CoordinatesValue"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MaterialValue"));
            if (Target.enumValueIndex == 0)
            {
                var Renderers = serializedObject.FindProperty("RenderersValue");
                var Slots = serializedObject.FindProperty("SlotsValue");
                Slots.arraySize = Renderers.arraySize;
                for (var Index = 0; Index < Renderers.arraySize; Index++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(Renderers.GetArrayElementAtIndex(Index), GUIContent.none);
                    EditorGUILayout.PropertyField(Slots.GetArrayElementAtIndex(Index), new GUIContent("Slot"));
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        Renderers.DeleteArrayElementAtIndex(Index);
                        if (Renderers.arraySize > Slots.arraySize - 1) Renderers.DeleteArrayElementAtIndex(Index);
                        Slots.DeleteArrayElementAtIndex(Index);
                        EditorGUILayout.EndHorizontal(); break;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Add Renderer Slot"))
                {
                    var Index = Renderers.arraySize++;
                    Slots.arraySize = Renderers.arraySize;
                    Renderers.GetArrayElementAtIndex(Index).objectReferenceValue = null;
                    Slots.GetArrayElementAtIndex(Index).intValue = 0;
                }
            }
            else EditorGUILayout.PropertyField(serializedObject.FindProperty("FluidSourceValue"));
            serializedObject.ApplyModifiedProperties();
        }

        [MenuItem("PLL/Visuals/Add Surface Textures to Selected Object")]
        static void AddSelected()
        {
            var Object = Selection.activeGameObject;
            if (!Object) throw new InvalidOperationException("Select an object with a Mesh Renderer or Skinned Mesh Renderer.");
            var Renderer = Object.GetComponent<Renderer>();
            if (Renderer is not (MeshRenderer or SkinnedMeshRenderer))
                throw new InvalidOperationException("The selected object has no supported surface renderer.");
            var Component = Object.GetComponent<PllSurfaceTextureAuthoring>();
            if (Component) { Selection.activeObject = Component; return; }
            Component = Undo.AddComponent<PllSurfaceTextureAuthoring>(Object);
            var Serialized = new SerializedObject(Component);
            var Renderers = Serialized.FindProperty("RenderersValue");
            var Slots = Serialized.FindProperty("SlotsValue");
            var Materials = Renderer.sharedMaterials;
            Renderers.arraySize = Slots.arraySize = Materials.Length;
            for (var Index = 0; Index < Materials.Length; Index++)
            {
                Renderers.GetArrayElementAtIndex(Index).objectReferenceValue = Renderer;
                Slots.GetArrayElementAtIndex(Index).intValue = Index;
                if (Materials[Index] && Materials[Index].shader.name == PllSurfaceProfileContract.ShaderName)
                    Serialized.FindProperty("MaterialValue").objectReferenceValue = Materials[Index];
            }
            Serialized.ApplyModifiedProperties(); Selection.activeObject = Component;
        }
    }

    public sealed class PllSurfaceMaterialInspector : ShaderGUI
    {
        public override void OnGUI(MaterialEditor Editor, MaterialProperty[] Properties)
        {
            var Material = (Material)Editor.target;
            EditorGUI.BeginChangeCheck();
            var Mode = FindProperty("_SurfaceMode", Properties);
            Mode.floatValue = (float)(PllSurfaceRenderMode)EditorGUILayout.EnumPopup("Render Mode", (PllSurfaceRenderMode)(int)Mode.floatValue);
            if (Mode.floatValue == 1f) Draw("_Cutoff");
            if (Mode.floatValue == 2f) Draw("_Opacity");
            for (var Layer = 0; Layer < PllSurfaceProfileContract.LayerCount; Layer++)
            {
                EditorGUILayout.Space(); EditorGUILayout.LabelField(Layer == 0 ? "Base" : "Overlay " + Layer, EditorStyles.boldLabel);
                var Prefix = "_Layer" + Layer;
                if (Layer != 0)
                {
                    Draw(Prefix + "Enabled");
                    if (FindProperty(Prefix + "Enabled", Properties).floatValue == 0f) continue;
                }
                Editor.TexturePropertySingleLine(new GUIContent("Albedo / Tint"), FindProperty(Prefix + "Albedo", Properties), FindProperty(Prefix + "Tint", Properties));
                Editor.TexturePropertySingleLine(new GUIContent("Normal"), FindProperty(Prefix + "Normal", Properties));
                Draw(Prefix + "NormalStrength"); Draw(Prefix + "Smoothness"); Draw(Prefix + "Projection");
                if (FindProperty(Prefix + "Projection", Properties).floatValue == 0f)
                {
                    Draw(Prefix + "UV"); Draw(Prefix + "Transform");
                }
                else { Draw(Prefix + "TileSize"); Draw(Prefix + "Blend"); }
                if (Layer == 0) continue;
                Draw(Prefix + "Strength");
                var Mask = FindProperty(Prefix + "Mask", Properties);
                Mask.floatValue = (float)(PllSurfaceMask)EditorGUILayout.EnumFlagsField("Filters", (PllSurfaceMask)(int)Mask.floatValue);
                var Filters = (PllSurfaceMask)(int)Mask.floatValue;
                if ((Filters & PllSurfaceMask.Slope) != 0) Draw(Prefix + "Slope");
                if ((Filters & PllSurfaceMask.Height) != 0) Draw(Prefix + "Height");
                if ((Filters & PllSurfaceMask.Curvature) != 0) Draw(Prefix + "Curvature");
                if ((Filters & PllSurfaceMask.Vertex) != 0) Draw(Prefix + "VertexChannel");
                if ((Filters & PllSurfaceMask.Texture) != 0)
                {
                    Draw(Prefix + "MaskTexture"); Draw(Prefix + "MaskChannel"); Draw(Prefix + "MaskUV"); Draw(Prefix + "MaskTransform");
                }
            }
            if (PllSurfaceProfileContract.UsesCurvature(Material))
            {
                EditorGUILayout.Space(); EditorGUILayout.LabelField("Curvature: negative concave, positive convex", EditorStyles.boldLabel);
                var Input = FindProperty("_CurvatureInput", Properties);
                Input.floatValue = (float)(PllSurfaceCurvatureInput)EditorGUILayout.EnumPopup("Input", (PllSurfaceCurvatureInput)(int)Input.floatValue);
                Draw("_CurvatureContrast");
                if (Input.floatValue == 1f)
                {
                    Draw("_CurvatureTexture"); Draw("_CurvatureUV"); Draw("_CurvatureTransform");
                }
                EditorGUILayout.HelpBox("Automatic baking preserves mesh attributes and uses UV7.x. Imported curvature requires a linear mask; red 0.5 is flat.", MessageType.Info);
            }
            if (EditorGUI.EndChangeCheck())
                foreach (var Target in Editor.targets) ValidateMaterial((Material)Target);

            void Draw(string Name)
            {
                var Property = FindProperty(Name, Properties);
                Editor.ShaderProperty(Property, Property.displayName);
            }
        }

        public override void ValidateMaterial(Material Material)
        {
            PllSurfaceProfileContract.ConfigureMode(Material, (PllSurfaceRenderMode)(int)Material.GetFloat("_SurfaceMode"));
            PllSurfaceCurvatureBaker.ValidateNormalTextures(Material);
        }

        [MenuItem("PLL/Visuals/Create Layered Surface Material")]
        static void CreateMaterial()
        {
            var Shader = UnityEngine.Shader.Find(PllSurfaceProfileContract.ShaderName);
            if (!Shader) throw new InvalidOperationException("The PLL layered surface shader is not imported.");
            var Material = new Material(Shader);
            PllSurfaceProfileContract.ConfigureMode(Material, PllSurfaceRenderMode.Opaque);
            ProjectWindowUtil.CreateAsset(Material, "Layered Surface.mat");
        }
    }
}
