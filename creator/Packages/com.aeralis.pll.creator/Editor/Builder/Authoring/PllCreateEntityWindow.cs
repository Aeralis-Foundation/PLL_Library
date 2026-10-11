using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateEntityWindow : EditorWindow
    {
        internal enum Profile { Beetle, MushroomZombie, Condor, InstalledPlugin }
        Profile Kind;
        GameObject Target;
        GameObject Model;
        bool InitiallyEnabled = true;
        [SerializeReference] PllEntityBehavior Behavior = new PllBeetleBehavior();
        NativeContentRig Rig;
        string[] SkinLabels = Array.Empty<string>();
        int SkinIndex;
        SkinnedMeshRenderer ArtistRenderer;
        int[] BoneSlots = Array.Empty<int>();
        Bounds LocalBounds;
        Vector2 Scroll;

        [MenuItem("PLL/Spawning/Create Entity", priority = 25)]
        static void Open()
        {
            var Window = GetWindow<PllCreateEntityWindow>("Create Entity");
            Window.Target = Selection.activeGameObject; Window.Model = Window.Target;
            Window.Show();
        }

        internal static PllEntityBehavior CreateBehavior(Profile Kind) => Kind switch
        {
            Profile.Beetle => new PllBeetleBehavior(),
            Profile.MushroomZombie => new PllMushroomZombieBehavior(),
            Profile.Condor => new PllCondorBehavior(),
            Profile.InstalledPlugin => new PllPluginEntityBehavior(),
            _ => throw new InvalidOperationException("Entity profile is unsupported.")
        };

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Target = (GameObject)EditorGUILayout.ObjectField("Definition Root", Target, typeof(GameObject), true);
            Model = (GameObject)EditorGUILayout.ObjectField("Owned Model", Model, typeof(GameObject), true);
            var Selected = (Profile)EditorGUILayout.EnumPopup("Behavior", Kind);
            if (Selected != Kind) { Kind = Selected; Behavior = CreateBehavior(Kind); Rig = null; }
            InitiallyEnabled = EditorGUILayout.Toggle("Initially Enabled", InitiallyEnabled);
            if (Behavior is PllPluginEntityBehavior Plugin)
            {
                Plugin.PluginGuid = EditorGUILayout.TextField("Installed Plugin", Plugin.PluginGuid);
                Plugin.FactoryId = EditorGUILayout.TextField("Factory", Plugin.FactoryId);
                Plugin.Revision = EditorGUILayout.TextField("Revision", Plugin.Revision);
                EditorGUILayout.HelpBox("Install the matching plugin factory. Keep only Entity on the definition root and put the model in a separate child without scripts. Continue recreates instances. Disabling the source removes them permanently.", MessageType.Info);
            }
            else if (Behavior is PllMushroomZombieBehavior)
                EditorGUILayout.HelpBox("Zombie uses native appearance and spawning rules. Choose an empty model root; custom Zombie rigs are not supported.", MessageType.Info);
            else
            {
                if (Behavior is PllCondorBehavior Condor)
                    Condor.NestOffset = EditorGUILayout.Vector3Field("Definition-local Nest Offset", Condor.NestOffset);
                var Appearance = Behavior is PllBeetleBehavior Beetle ? Beetle.Appearance : ((PllCondorBehavior)Behavior).Appearance;
                EditorGUILayout.LabelField("Appearance", Appearance == null ? "Native" : "Mapped artist renderers");
                if (GUILayout.Button("Use Native Appearance")) SetAppearance(null);
                if (GUILayout.Button("Import Captured Native Rig"))
                {
                    var Path = EditorUtility.OpenFilePanel("Import PEAK native content catalog", "", "json");
                    if (Path != "")
                        try
                        {
                            var Catalog = PllNativeContentWindow.ReadCatalog(Path);
                            var Actor = Kind == Profile.Beetle ? "Beetle" : "Condor";
                            var Imported = Array.Find(Catalog.Rigs, Value => Value.Actor == Actor)
                                ?? throw new InvalidOperationException("The catalog has no captured rig for this Entity.");
                            if (Imported.Skins.Length == 0)
                                throw new InvalidOperationException("The captured rig has no renderer bindings.");
                            Rig = Imported;
                            SkinIndex = 0; SkinLabels = Array.ConvertAll(Rig.Skins, Skin => Skin.RendererPath);
                            Array.Fill(BoneSlots, -1);
                        }
                        catch (Exception Failure) when (Failure is ArgumentException or InvalidOperationException
                            or System.IO.IOException or UnauthorizedAccessException)
                        { EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close"); }
                }
                if (Rig != null)
                {
                    EditorGUILayout.LabelField("Native Descriptor", Rig.Fingerprint);
                    EditorGUI.BeginChangeCheck();
                    SkinIndex = EditorGUILayout.Popup("Native Renderer", SkinIndex, SkinLabels);
                    if (EditorGUI.EndChangeCheck()) Array.Fill(BoneSlots, -1);
                }
                EditorGUI.BeginChangeCheck();
                ArtistRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                    "Persistent Artist Renderer", ArtistRenderer, typeof(SkinnedMeshRenderer), false);
                if (EditorGUI.EndChangeCheck())
                {
                    BoneSlots = new int[ArtistRenderer && ArtistRenderer.sharedMesh ? ArtistRenderer.sharedMesh.bindposeCount : 0];
                    Array.Fill(BoneSlots, -1);
                    if (ArtistRenderer) LocalBounds = ArtistRenderer.localBounds;
                }
                var Skin = Rig == null ? null : Rig.Skins[SkinIndex];
                LocalBounds.center = EditorGUILayout.Vector3Field("Local Bounds Center", LocalBounds.center);
                LocalBounds.extents = EditorGUILayout.Vector3Field("Local Bounds Extents", LocalBounds.extents);
                if (ArtistRenderer && Skin != null)
                {
                    var Bones = ArtistRenderer.bones;
                    for (var Index = 0; Index < BoneSlots.Length; Index++)
                        BoneSlots[Index] = EditorGUILayout.Popup("Bone " + Index
                            + (Index < Bones.Length && Bones[Index] ? " (" + Bones[Index].name + ")" : ""),
                            BoneSlots[Index], Skin.BonePaths);
                    if (GUILayout.Button("Add Renderer Binding"))
                        try { SetAppearance(PllApplyEntityAppearanceWindow.BuildAppearance(
                            Rig, Skin, ArtistRenderer, BoneSlots, LocalBounds, Appearance)); }
                        catch (Exception Failure) when (Failure is ArgumentException or InvalidOperationException)
                        { EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close"); }
                }
                EditorGUILayout.HelpBox("Use an empty model root for native appearance. For custom Beetle or Condor visuals, map a mesh with compatible bindposes to the native bones. PEAK keeps its body, skeleton and animation; do not add another Rigidbody or Animator.", MessageType.Info);
            }
            using (new EditorGUI.DisabledScope(!Target || !Model))
                if (GUILayout.Button("Create Entity"))
                    try { Create(Target, Model, InitiallyEnabled, Behavior); Close(); }
                    catch (InvalidOperationException Failure) { EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close"); }
            EditorGUILayout.EndScrollView();
        }

        void SetAppearance(PllNativeEntityAppearance Appearance)
        {
            if (Behavior is PllBeetleBehavior Beetle) Beetle.Appearance = Appearance;
            else ((PllCondorBehavior)Behavior).Appearance = Appearance;
        }

        internal static PllEntityAuthoring Create(GameObject Target, GameObject Model,
            bool InitiallyEnabled, PllEntityBehavior Behavior)
        {
            const string Action = "Create Entity";
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PllEntityAuthoring>(Target, Action);
            var Id = "entity-" + Guid.NewGuid().ToString("N");
            if (PllEntityValueContract.Failure(Target.transform, Id, Model, InitiallyEnabled, Behavior) is string Failure)
                throw new InvalidOperationException(Failure);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, Model.transform, Action);
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Source = Undo.AddComponent<PllEntityAuthoring>(Target);
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

    [CustomEditor(typeof(PllEntityAuthoring))]
    internal sealed class PllEntityAuthoringEditor : UnityEditor.Editor
    {
        static readonly string[] Labels = { "Select Behavior", "Beetle", "Mushroom Zombie", "Condor", "Installed Plugin" };
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var Id = serializedObject.FindProperty("IdValue");
            if (Id.stringValue.Length == 0) Id.stringValue = "entity-" + Guid.NewGuid().ToString("N");
            EditorGUILayout.LabelField("Source ID", Id.stringValue);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ModelRootValue"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("InitiallyEnabledValue"));
            var Behavior = serializedObject.FindProperty("BehaviorValue");
            var Kind = Behavior.managedReferenceValue switch
            {
                PllBeetleBehavior => 1,
                PllMushroomZombieBehavior => 2,
                PllCondorBehavior => 3,
                PllPluginEntityBehavior => 4,
                _ => 0
            };
            var Selected = EditorGUILayout.Popup("Behavior", Kind, Labels);
            if (Selected != Kind)
                Behavior.managedReferenceValue = Selected == 0 ? null
                    : PllCreateEntityWindow.CreateBehavior((PllCreateEntityWindow.Profile)(Selected - 1));
            if (Behavior.managedReferenceValue != null) EditorGUILayout.PropertyField(Behavior, true);
            serializedObject.ApplyModifiedProperties();
            if (PllEntityValueContract.Failure((PllEntityAuthoring)target) is string Failure)
                EditorGUILayout.HelpBox(Failure, MessageType.Error);
            EditorGUILayout.HelpBox("Use Create Entity to map custom Beetle or Condor visuals. Native entities keep the game's body and behavior. Continue recreates plugin instances; disabling them is permanent for the current run.", MessageType.Info);
        }
    }
}
