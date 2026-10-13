using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Creator.Editor
{
    internal sealed class PllNativeMaterialsWindow : EditorWindow
    {
        [SerializeField] PllNativeMaterialAuthoring Target;
        NativeContentCatalog Catalog;
        string CatalogPath = "", Search = "", ShaderFilter = "";
        NativeContentMaterial Selected;
        Vector2 Scroll;
        readonly Dictionary<string, Texture2D> Thumbnails = new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        void OnDisable() => ReleaseThumbnails();

        void ReleaseThumbnails()
        {
            foreach (var Image in Thumbnails.Values) if (Image) DestroyImmediate(Image);
            Thumbnails.Clear();
        }

        [MenuItem("PLL/Visuals/Browse PEAK Materials and Shaders", priority = 25)]
        static void Open() => Open(null);

        internal static void Open(PllNativeMaterialAuthoring Target)
        {
            var Window = GetWindow<PllNativeMaterialsWindow>("PEAK Materials");
            Window.Target = Target; Window.Show();
        }

        void OnGUI()
        {
            Target = (PllNativeMaterialAuthoring)EditorGUILayout.ObjectField("Renderer Slot", Target, typeof(PllNativeMaterialAuthoring), true);
            if (GUILayout.Button("Open PEAK Native Content Catalog"))
            {
                var Path = EditorUtility.OpenFilePanel("PEAK Native Content Catalog", "", "json");
                if (Path.Length != 0)
                    try { LoadCatalog(Path); }
                    catch (Exception Failure) when (Failure is IOException || Failure is UnauthorizedAccessException
                        || Failure is ArgumentException || Failure is InvalidOperationException || Failure is System.Runtime.Serialization.SerializationException)
                    { EditorUtility.DisplayDialog("Catalog could not open", Failure.Message, "Close"); }
            }
            if (Catalog == null || Catalog.Materials.Length == 0)
            {
                EditorGUILayout.HelpBox("Export native content from PEAK through PLL > Native Content > Browser and PEAK Preview, then open its catalog here.", MessageType.Info);
                return;
            }
            var NextSearch = EditorGUILayout.TextField("Find Material", Search);
            if (NextSearch != Search) { Search = NextSearch; Selected = null; ReleaseThumbnails(); }
            ShaderFilter = EditorGUILayout.TextField("Shader Filter", ShaderFilter);
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            foreach (var Material in Catalog.Materials)
            {
                if (Search.Length != 0 && Material.Name.IndexOf(Search, StringComparison.OrdinalIgnoreCase) < 0
                    || ShaderFilter.Length != 0 && Material.Shader.IndexOf(ShaderFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button(Material.Name + "  |  " + Material.Shader))
                {
                    ReleaseThumbnails(); Selected = null;
                    try
                    {
                        foreach (var Property in Material.Properties)
                            if (Property.Image != null && Property.Image.ImageFile.Length != 0)
                                Thumbnails.Add(Property.Name, PllNativeContentWindow.ReadThumbnail(CatalogPath, Property.Image));
                        Selected = Material;
                    }
                    catch (Exception Failure) when (Failure is IOException || Failure is UnauthorizedAccessException || Failure is ArgumentException)
                    {
                        ReleaseThumbnails();
                        EditorUtility.DisplayDialog("Texture preview could not open", Failure.Message, "Close");
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            if (Selected == null) return;
            EditorGUILayout.LabelField("Material", Selected.Name);
            EditorGUILayout.LabelField("Native Shader", Selected.Shader);
            EditorGUILayout.LabelField("Editable Shader Properties", Selected.Properties.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var Property in Selected.Properties)
            {
                EditorGUILayout.LabelField(Property.Label.Length == 0 ? Property.Name : Property.Label,
                    Property.Type == ShaderPropertyType.Texture ? Property.Texture : Property.Type.ToString());
                if (Thumbnails.TryGetValue(Property.Name, out var Image))
                    GUILayout.Label(Image, GUILayout.Width(96), GUILayout.Height(96));
            }
            EditorGUILayout.HelpBox("The Editor preview shows material properties, not PEAK's full shader. Native textures resolve in PEAK and are not copied into the biome bundle.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Target))
                if (GUILayout.Button("Create Editable Preview and Apply to Renderer"))
                {
                    var Path = EditorUtility.SaveFilePanelInProject("Save Native Material Preview", Selected.Name + " Preview", "mat", "Choose a material asset path.");
                    if (Path.Length == 0) return;
                    try { Apply(Target, Selected, Path); }
                    catch (Exception Failure) when (Failure is IOException || Failure is UnauthorizedAccessException
                        || Failure is ArgumentException || Failure is InvalidOperationException)
                    { EditorUtility.DisplayDialog("Material could not apply", Failure.Message, "Close"); }
                }
        }

        void LoadCatalog(string Path)
        {
            var Loaded = PllNativeContentWindow.ReadCatalog(Path);
            ReleaseThumbnails(); Catalog = Loaded; CatalogPath = Path; Selected = null;
        }

        internal static void Apply(PllNativeMaterialAuthoring Target, NativeContentMaterial Source, string MaterialPath)
        {
            PllSourceIdentityValidator.ResolveEditableOwner(Target.gameObject, "Apply Native Material");
            var Renderer = Target.GetComponent<Renderer>();
            if (!Renderer) throw new InvalidOperationException("Select a renderer with a Native Material component.");
            var Materials = Renderer.sharedMaterials;
            if (Target.MaterialSlot < 0 || Target.MaterialSlot >= Materials.Length)
                throw new InvalidOperationException("Select an existing renderer material slot.");
            if (AssetDatabase.LoadMainAssetAtPath(MaterialPath))
                throw new InvalidOperationException("The preview material asset already exists. Choose a new asset path.");
            var Preview = CreatePreview(Source, MaterialPath);
            var References = Source.Properties.Where(Property => Property.Type == ShaderPropertyType.Texture && Property.Texture.Length != 0)
                .Select(Property => new PllNativeTextureReference { Property = Property.Name, SourceMaterial = Source.Name, SourceProperty = Property.Name }).ToArray();
            Undo.RecordObjects(new UnityEngine.Object[] { Target, Renderer }, "Apply Native Material");
            Materials[Target.MaterialSlot] = Preview; Renderer.sharedMaterials = Materials;
            Target.NativeMaterial = Source.Name; Target.UsePreviewProperties = true; Target.Textures = References;
            EditorUtility.SetDirty(Target); EditorUtility.SetDirty(Renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(Target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(Renderer);
            Selection.activeObject = Target;
        }

        internal static Material CreatePreview(NativeContentMaterial Source, string MaterialPath)
        {
            var Code = PreviewShader(Source);
            string Hash;
            using (var Algorithm = SHA256.Create())
                Hash = BitConverter.ToString(Algorithm.ComputeHash(Encoding.UTF8.GetBytes(Code))).Replace("-", "").ToLowerInvariant();
            const string Folder = "Assets/PLLGenerated/NativeMaterialPreviews";
            var ShaderPath = Folder + "/" + Hash + ".shader";
            var FilePath = Path.Combine(Application.dataPath, ShaderPath.Substring("Assets/".Length));
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, Code.Replace("PLL_NATIVE_PREVIEW", "PLL/Native Preview/" + Hash), new UTF8Encoding(false));
                AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);
            }
            var Shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (!Shader || ShaderUtil.ShaderHasError(Shader)) throw new InvalidOperationException("The native preview shader could not compile.");
            var Preview = new Material(Shader) { name = Source.Name + " Preview", renderQueue = Source.RenderQueue };
            try
            {
                foreach (var Property in Source.Properties)
                {
                    var Values = Property.Values;
                    switch (Property.Type)
                    {
                        case ShaderPropertyType.Color: Preview.SetColor(Property.Name, new Color(Values[0], Values[1], Values[2], Values[3])); break;
                        case ShaderPropertyType.Vector: Preview.SetVector(Property.Name, new Vector4(Values[0], Values[1], Values[2], Values[3])); break;
                        case ShaderPropertyType.Int: Preview.SetInteger(Property.Name, Property.Integer); break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range: Preview.SetFloat(Property.Name, Values[0]); break;
                        case ShaderPropertyType.Texture:
                            Preview.SetTexture(Property.Name, null);
                            Preview.SetTextureScale(Property.Name, new Vector2(Values[0], Values[1]));
                            Preview.SetTextureOffset(Property.Name, new Vector2(Values[2], Values[3]));
                            break;
                    }
                }
                AssetDatabase.CreateAsset(Preview, MaterialPath); AssetDatabase.SaveAssets();
                return Preview;
            }
            catch { DestroyImmediate(Preview); throw; }
        }

        static string PreviewShader(NativeContentMaterial Source)
        {
            var Properties = new StringBuilder();
            string Texture = null, Color = null;
            foreach (var Property in Source.Properties)
            {
                var Kind = Property.Type switch
                {
                    ShaderPropertyType.Color => "Color", ShaderPropertyType.Vector => "Vector",
                    ShaderPropertyType.Float => "Float", ShaderPropertyType.Int => "Integer",
                    ShaderPropertyType.Range => "Range(" + Property.Minimum.ToString("R", CultureInfo.InvariantCulture) + "," + Property.Maximum.ToString("R", CultureInfo.InvariantCulture) + ")",
                    ShaderPropertyType.Texture => Property.Dimension switch
                    {
                        TextureDimension.Tex2D => "2D", TextureDimension.Cube => "Cube", TextureDimension.Tex3D => "3D",
                        TextureDimension.Tex2DArray => "2DArray", TextureDimension.CubeArray => "CubeArray",
                        _ => throw new InvalidOperationException("This native texture dimension has no Editor preview.")
                    },
                    _ => throw new InvalidOperationException("This native shader property has no Editor preview.")
                };
                var Value = Property.Type == ShaderPropertyType.Texture ? "\"white\" {}"
                    : Property.Type is ShaderPropertyType.Color or ShaderPropertyType.Vector ? "(1,1,1,1)"
                    : Property.Type == ShaderPropertyType.Range ? Property.Minimum.ToString("R", CultureInfo.InvariantCulture) : "0";
                Properties.Append(Property.Name).Append(" (\"").Append(Property.Label.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " "))
                    .Append("\", ").Append(Kind).Append(") = ").Append(Value).Append('\n');
                if (Texture == null && Property.Type == ShaderPropertyType.Texture && Property.Dimension == TextureDimension.Tex2D) Texture = Property.Name;
                if (Color == null && Property.Type == ShaderPropertyType.Color) Color = Property.Name;
            }
            return "Shader \"PLL_NATIVE_PREVIEW\" { Properties {\n" + Properties + "}\n"
                + "SubShader { Tags { \"RenderPipeline\"=\"UniversalPipeline\" } Pass {\nHLSLPROGRAM\n"
                + "#pragma vertex Vert\n#pragma fragment Frag\n#include \"UnityCG.cginc\"\n"
                + "struct Attributes { float4 Vertex : POSITION; float2 UV : TEXCOORD0; };\n"
                + "struct Varyings { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };\n"
                + (Texture == null ? "" : "sampler2D " + Texture + "; float4 " + Texture + "_ST;\n")
                + (Color == null ? "" : "float4 " + Color + ";\n")
                + "Varyings Vert(Attributes Input) { Varyings Output; Output.Position=UnityObjectToClipPos(Input.Vertex); Output.UV=Input.UV; return Output; }\n"
                + "float4 Frag(Varyings Input) : SV_Target { return " + (Color ?? "float4(1,1,1,1)")
                + (Texture == null ? "" : " * tex2D(" + Texture + ", Input.UV*" + Texture + "_ST.xy+" + Texture + "_ST.zw)")
                + "; }\nENDHLSL\n} } }";
        }
    }
}
