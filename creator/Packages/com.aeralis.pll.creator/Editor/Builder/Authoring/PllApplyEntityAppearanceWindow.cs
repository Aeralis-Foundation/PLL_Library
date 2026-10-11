using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllApplyEntityAppearanceWindow : EditorWindow
    {
        PllEntityZoneAuthoring Zone;
        NativeContentRig Rig;
        int SkinIndex;
        string[] SkinLabels = Array.Empty<string>();
        SkinnedMeshRenderer ArtistRenderer;
        int[] BoneSlots = Array.Empty<int>();
        Bounds LocalBounds;
        Vector2 Scroll;

        [MenuItem("PLL/Spawning/Apply Entity Appearance to Selected Zone", priority = 29)]
        static void Open()
        {
            var Window = GetWindow<PllApplyEntityAppearanceWindow>("Apply Entity Appearance");
            Window.Zone = Selection.activeGameObject
                ? Selection.activeGameObject.GetComponent<PllEntityZoneAuthoring>() : null;
            Window.Show();
        }

        internal static void Open(NativeContentRig Rig)
        {
            var Window = GetWindow<PllApplyEntityAppearanceWindow>("Apply Entity Appearance");
            Window.Zone = Selection.activeGameObject
                ? Selection.activeGameObject.GetComponent<PllEntityZoneAuthoring>() : null;
            Window.Rig = Rig; Window.SkinIndex = 0;
            Window.SkinLabels = Array.ConvertAll(Rig.Skins, Skin => Skin.RendererPath);
            Array.Fill(Window.BoneSlots, -1);
            Window.Show();
        }

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Zone = (PllEntityZoneAuthoring)EditorGUILayout.ObjectField("Entity Zone", Zone, typeof(PllEntityZoneAuthoring), true);
            if (GUILayout.Button("Import Captured Native Rig"))
            {
                var Path = EditorUtility.OpenFilePanel("Import PEAK native content catalog", "", "json");
                if (Path != "")
                {
                    try
                    {
                        var Catalog = PllNativeContentWindow.ReadCatalog(Path);
                        var Actor = Zone && Zone.Source is PllBeetleZoneSource ? "Beetle"
                            : Zone && Zone.Source is PllCondorZoneSource ? "Condor" : "";
                        var Imported = Array.Find(Catalog.Rigs, Value => Value.Actor == Actor);
                        if (Imported == null) throw new InvalidOperationException("The catalog has no captured rig for this zone's native actor.");
                        if (Imported.Skins.Length == 0) throw new InvalidOperationException("The captured rig has no renderer bindings.");
                        Rig = Imported;
                        SkinIndex = 0;
                        SkinLabels = Array.ConvertAll(Rig.Skins, Skin => Skin.RendererPath);
                        Array.Fill(BoneSlots, -1);
                    }
                    catch (Exception Failure) when (Failure is ArgumentException || Failure is InvalidOperationException
                        || Failure is System.IO.IOException || Failure is UnauthorizedAccessException)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            }
            if (Rig != null)
            {
                EditorGUILayout.LabelField("Native actor", Rig.Actor);
                EditorGUILayout.LabelField("Descriptor SHA-256", Rig.Fingerprint);
                EditorGUI.BeginChangeCheck();
                SkinIndex = EditorGUILayout.Popup("Native Renderer", SkinIndex, SkinLabels);
                if (EditorGUI.EndChangeCheck()) Array.Fill(BoneSlots, -1);
            }
            EditorGUI.BeginChangeCheck();
            ArtistRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Artist Renderer", ArtistRenderer, typeof(SkinnedMeshRenderer), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (ArtistRenderer)
                {
                    BoneSlots = new int[ArtistRenderer.sharedMesh ? ArtistRenderer.sharedMesh.bindposeCount : 0];
                    Array.Fill(BoneSlots, -1);
                    LocalBounds = ArtistRenderer.localBounds;
                }
                else BoneSlots = Array.Empty<int>();
            }
            LocalBounds.center = EditorGUILayout.Vector3Field("Local Bounds Center", LocalBounds.center);
            LocalBounds.extents = EditorGUILayout.Vector3Field("Local Bounds Extents", LocalBounds.extents);
            var Skin = Rig != null && Rig.Skins.Length != 0 ? Rig.Skins[SkinIndex] : null;
            using (new EditorGUI.DisabledScope(Skin == null || !ArtistRenderer
                || BoneSlots.Length != Skin.BonePaths.Length))
                if (GUILayout.Button("Use Reference Renderer Bone Order"))
                {
                    for (var i = 0; i < BoneSlots.Length; i++) BoneSlots[i] = i;
                }
            if (ArtistRenderer && Skin != null)
            {
                var Bones = ArtistRenderer.bones;
                for (var i = 0; i < BoneSlots.Length; i++)
                    BoneSlots[i] = EditorGUILayout.Popup(
                        "Bone " + i + (i < Bones.Length && Bones[i] ? " (" + Bones[i].name + ")" : ""),
                        BoneSlots[i], Skin.BonePaths);
            }
            EditorGUILayout.HelpBox("Import the rig captured from PEAK and map each mesh bone to a native bone path. "
                + "Enable Read/Write and use compatible bindposes. PEAK keeps its skeleton, animation, body and sockets.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Zone || Skin == null || !ArtistRenderer))
                if (GUILayout.Button("Apply Renderer Binding"))
                {
                    try { Apply(Zone, Rig, Skin, ArtistRenderer, BoneSlots, LocalBounds); }
                    catch (Exception Failure) when (Failure is ArgumentException or InvalidOperationException)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            using (new EditorGUI.DisabledScope(!Zone))
                if (GUILayout.Button("Use Native Appearance"))
                {
                    try { RequireEditableZone(Zone); SetAppearance(Zone, null); }
                    catch (InvalidOperationException Failure) { EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close"); }
                }
            EditorGUILayout.EndScrollView();
        }

        internal static void Apply(PllEntityZoneAuthoring Zone, NativeContentRig Rig,
            NativeContentSkin Skin, SkinnedMeshRenderer ArtistRenderer,
            IReadOnlyList<int> BoneSlots, Bounds LocalBounds)
        {
            RequireEditableZone(Zone);
            if (Rig.Actor != (Zone.Source is PllBeetleZoneSource ? "Beetle" : "Condor"))
                throw new InvalidOperationException("The captured rig belongs to a different native actor.");
            SetAppearance(Zone, BuildAppearance(Rig, Skin, ArtistRenderer, BoneSlots, LocalBounds,
                PllNativeEntityAppearanceValueContract.Appearance(Zone.Source)));
        }

        internal static PllNativeEntityAppearance BuildAppearance(NativeContentRig Rig,
            NativeContentSkin Skin, SkinnedMeshRenderer ArtistRenderer,
            IReadOnlyList<int> BoneSlots, Bounds LocalBounds, PllNativeEntityAppearance Previous)
        {
            if (Skin.BlendShapeCount != 0)
                throw new InvalidOperationException("This native renderer requires blend shapes and cannot be replaced.");
            if (Previous != null)
            {
                if (Previous.Renderers == null)
                    throw new InvalidOperationException("The existing appearance has no renderer bindings. Use Native Appearance before rebinding.");
                if (Previous.NativeDescriptorFingerprint != ""
                    && Previous.NativeDescriptorFingerprint != Rig.Fingerprint)
                    throw new InvalidOperationException("The existing appearance uses a different native rig. Use Native Appearance before rebinding.");
            }
            if (!ArtistRenderer || !EditorUtility.IsPersistent(ArtistRenderer))
                throw new InvalidOperationException("Choose an artist renderer from a saved prefab.");
            var Mesh = ArtistRenderer.sharedMesh;
            if (!Mesh || !EditorUtility.IsPersistent(Mesh) || !Mesh.isReadable)
                throw new InvalidOperationException("The artist renderer needs a saved mesh with Read/Write enabled.");
            var Bindposes = Mesh.bindposes;
            if (BoneSlots.Count != Bindposes.Length)
                throw new InvalidOperationException("Choose one native bone path for every artist bindpose.");
            var Paths = new string[BoneSlots.Count];
            for (var i = 0; i < Paths.Length; i++)
            {
                var Slot = BoneSlots[i];
                if (Slot < 0 || Slot >= Skin.BonePaths.Length)
                    throw new InvalidOperationException("Artist bone " + i + " has no selected native bone path.");
                var Expected = new Matrix4x4();
                for (var j = 0; j < 16; j++) Expected[j] = Skin.Bindposes[Slot * 16 + j];
                if (!PllNativeEntityAppearanceValueContract.CompatibleBindpose(Bindposes[i], Expected))
                    throw new InvalidOperationException("Artist bone " + i + " does not match the explicitly selected native bindpose.");
                Paths[i] = Skin.BonePaths[Slot];
            }
            var Materials = ArtistRenderer.sharedMaterials;
            foreach (var Material in Materials)
                if (!Material || !EditorUtility.IsPersistent(Material))
                    throw new InvalidOperationException("Save each artist renderer material as an asset.");
            var Replacement = new PllNativeEntityRenderer
            {
                RendererPath = Skin.RendererPath,
                Mesh = Mesh, Materials = Materials, BonePaths = Paths, LocalBounds = LocalBounds
            };
            var Entries = new List<PllNativeEntityRenderer>();
            if (Previous != null)
            {
                foreach (var Renderer in Previous.Renderers)
                {
                    if (Renderer == null)
                        throw new InvalidOperationException("The existing appearance has an empty renderer binding. Use Native Appearance before rebinding.");
                    if (Renderer.RendererPath != Replacement.RendererPath) Entries.Add(Renderer);
                }
            }
            Entries.Add(Replacement);
            return new PllNativeEntityAppearance
            {
                NativeDescriptorFingerprint = Rig.Fingerprint, Renderers = Entries.ToArray()
            };
        }

        static void RequireEditableZone(PllEntityZoneAuthoring Zone)
        {
            const string Action = "Apply Entity Appearance";
            if (!Zone || Zone.Source is not (PllBeetleZoneSource or PllCondorZoneSource))
                throw new InvalidOperationException("Entity appearance supports native Beetle and Condor zones only.");
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Zone.gameObject, Action);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, Zone.transform, Action);
        }

        static void SetAppearance(PllEntityZoneAuthoring Zone, PllNativeEntityAppearance Appearance)
        {
            const string Action = "Apply Entity Appearance";
            if (PllNativeEntityAppearanceValueContract.Failure(Appearance) is { } Failure)
                throw new InvalidOperationException(Failure);
            var Serialized = new SerializedObject(Zone);
            var Property = PllAuthoringCommands.Property(Serialized, "SourceValue").FindPropertyRelative("Appearance");
            if (Property == null || Property.propertyType != SerializedPropertyType.ManagedReference)
                throw new InvalidOperationException("Entity appearance requires the current PLL Creator SDK. Configure setup again.");
            Undo.RecordObject(Zone, Action);
            Property.managedReferenceValue = Appearance;
            Serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(Zone); EditorSceneManager.MarkSceneDirty(Zone.gameObject.scene);
            Selection.activeObject = Zone; EditorGUIUtility.PingObject(Zone);
        }
    }
}
