using System;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllApplyRopeAppearanceWindow : EditorWindow
    {
        PllHangingRopeAuthoring Source;
        NativeContentRig Rig;
        SkinnedMeshRenderer ArtistSkin;
        int[] BoneSlots = Array.Empty<int>();

        [MenuItem("PLL/Climbing/Apply Custom Rope Appearance", priority = 28)]
        static void Open()
        {
            var Window = GetWindow<PllApplyRopeAppearanceWindow>("Custom Rope Appearance");
            Window.Source = Selection.activeGameObject
                ? Selection.activeGameObject.GetComponent<PllHangingRopeAuthoring>() : null;
            Window.Show();
        }

        void OnGUI()
        {
            Source = (PllHangingRopeAuthoring)EditorGUILayout.ObjectField("Hanging Rope", Source,
                typeof(PllHangingRopeAuthoring), true);
            if (GUILayout.Button("Import Captured Rope Rig"))
            {
                var Path = EditorUtility.OpenFilePanel("Import PEAK native content catalog", "", "json");
                if (Path != "")
                {
                    try
                    {
                        Rig = Array.Find(PllNativeContentWindow.ReadCatalog(Path).Rigs, Entry => Entry.Actor == "RopeDynamic")
                            ?? throw new InvalidOperationException("The catalog has no captured RopeDynamic rig. Export native content from PEAK.");
                        Array.Fill(BoneSlots, -1);
                    }
                    catch (Exception Failure) when (Failure is ArgumentException || Failure is InvalidOperationException
                        || Failure is System.IO.IOException || Failure is UnauthorizedAccessException)
                    { EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close"); }
                }
            }
            if (GUILayout.Button("Import Offline Rope Rig Asset"))
            {
                var Path = EditorUtility.OpenFilePanel("Import offline RopeDynamic rig asset", "", "json");
                if (Path != "")
                {
                    try
                    {
                        Rig = NativeContentArtifact.ReadRigAsset(Path).Rig;
                        Array.Fill(BoneSlots, -1);
                    }
                    catch (Exception Failure) when (Failure is ArgumentException || Failure is InvalidOperationException
                        || Failure is System.IO.IOException || Failure is UnauthorizedAccessException)
                    { EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close"); }
                }
            }
            if (Rig != null) EditorGUILayout.LabelField("Native rig", Rig.Fingerprint);
            if (Source && Source.RopeAppearance == PllRopeAppearance.CompatibleSkin)
            {
                EditorGUI.BeginChangeCheck();
                ArtistSkin = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Artist Skin", ArtistSkin,
                    typeof(SkinnedMeshRenderer), false);
                if (EditorGUI.EndChangeCheck())
                {
                    BoneSlots = new int[ArtistSkin && ArtistSkin.sharedMesh ? ArtistSkin.sharedMesh.bindposes.Length : 0];
                    Array.Fill(BoneSlots, -1);
                }
                if (Rig != null && Rig.Skins.Length == 1)
                    for (var i = 0; i < BoneSlots.Length; i++)
                        BoneSlots[i] = EditorGUILayout.Popup("Artist Bone " + i, BoneSlots[i], Rig.Skins[0].BonePaths);
                using (new EditorGUI.DisabledScope(Rig == null || !ArtistSkin))
                    if (GUILayout.Button("Bind Compatible Skin"))
                    {
                        try { ApplyCompatible(Source, Rig, ArtistSkin, BoneSlots); }
                        catch (Exception Failure) when (Failure is ArgumentException || Failure is InvalidOperationException
                            || Failure is System.IO.IOException || Failure is UnauthorizedAccessException)
                        { EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close"); }
                    }
                return;
            }
            if (Source && Source.RopeAppearance == PllRopeAppearance.Native)
                EditorGUILayout.HelpBox("Select Compatible Skin or Automatic Skin on the Hanging Rope.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Source || Rig == null))
                if (GUILayout.Button("Bake Rest Mesh"))
                {
                    var Path = EditorUtility.SaveFilePanelInProject("Save derived Rope skin", "RopeSkin", "asset", "Choose an asset path.");
                    if (Path == "") return;
                    try { Apply(Source, Rig, Path); }
                    catch (Exception Failure) when (Failure is ArgumentException || Failure is InvalidOperationException
                        || Failure is System.IO.IOException || Failure is UnauthorizedAccessException)
                    { EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close"); }
                }
        }

        internal static void ApplyCompatible(PllHangingRopeAuthoring Source, NativeContentRig Rig,
            SkinnedMeshRenderer ArtistSkin, int[] BoneSlots)
        {
            const string Action = "Bind Compatible Rope Skin";
            if (!Source || Source.RopeAppearance != PllRopeAppearance.CompatibleSkin
                || Rig.Actor != "RopeDynamic" || Rig.Skins.Length != 1)
                throw new InvalidOperationException("Compatible Rope appearance requires one captured RopeDynamic skin.");
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Source.gameObject, Action);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, Source.transform, Action);
            var Appearance = PllApplyEntityAppearanceWindow.BuildAppearance(Rig, Rig.Skins[0], ArtistSkin,
                BoneSlots, ArtistSkin.localBounds, null);
            if (PllNativeEntityAppearanceValueContract.Failure(Appearance) is { } Failure)
                throw new InvalidOperationException(Failure);
            if (Appearance.Renderers[0].Materials.Length != 1)
                throw new InvalidOperationException("Compatible Rope skin requires one material and one submesh.");
            SetAppearance(Source, Rig.Fingerprint, Appearance.Renderers[0], Action);
        }

        internal static void Apply(PllHangingRopeAuthoring Source, NativeContentRig Rig, string Path)
        {
            const string Action = "Bake Automatic Rope Skin";
            if (!Source || Source.RopeAppearance != PllRopeAppearance.AutomaticSkin)
                throw new InvalidOperationException("Select Automatic Skin on the Hanging Rope.");
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Source.gameObject, Action);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, Source.transform, Action);
            if (AssetDatabase.LoadMainAssetAtPath(Path))
                throw new InvalidOperationException("The derived skin path already contains an asset. Choose a new path.");
            var Mesh = PllSourceAssetCreation.SaveAsset(
                PllRopeSkinCompiler.Bake(Source.RopeRestMesh, Source.RopeRestStart, Source.RopeRestEnd, Rig), Path);
            SetAppearance(Source, Rig.Fingerprint, new PllNativeEntityRenderer
            {
                Mesh = Mesh, Materials = new[] { Source.RopeMaterial }, LocalBounds = Mesh.bounds,
                RendererPath = Rig.Skins[0].RendererPath, BonePaths = Rig.Skins[0].BonePaths
            }, Action);
        }

        static void SetAppearance(PllHangingRopeAuthoring Source, string Fingerprint,
            PllNativeEntityRenderer Renderer, string Action)
        {
            var Serialized = new SerializedObject(Source);
            Undo.RecordObject(Source, Action);
            PllAuthoringCommands.Property(Serialized, "RopeMeshValue").objectReferenceValue = Renderer.Mesh;
            PllAuthoringCommands.Property(Serialized, "RopeMaterialValue").objectReferenceValue = Renderer.Materials[0];
            PllAuthoringCommands.Property(Serialized, "RopeBoundsValue").boundsValue = Renderer.LocalBounds;
            PllAuthoringCommands.Property(Serialized, "RopeRendererPathValue").stringValue = Renderer.RendererPath;
            PllAuthoringCommands.Property(Serialized, "RopeNativeRigFingerprintValue").stringValue = Fingerprint;
            var Bones = PllAuthoringCommands.Property(Serialized, "RopeBonePathsValue"); Bones.arraySize = Renderer.BonePaths.Length;
            for (var i = 0; i < Bones.arraySize; i++) Bones.GetArrayElementAtIndex(i).stringValue = Renderer.BonePaths[i];
            Serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(Source);
            if (Source.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(Source.gameObject.scene);
            Selection.activeObject = Source; EditorGUIUtility.PingObject(Source);
        }
    }
}
