using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateLuggageWindow : EditorWindow
    {
        GameObject Root;
        DefaultAsset AssetFolder;
        string DisplayName = "Luggage";
        float OpenTime = 0.5f;
        bool NativeProgression;
        enum Placement { Fixed, Generated }
        Placement Timing;
        PropSpawner Spawner;
        readonly List<GameObject> Items = new List<GameObject>();
        Vector2 Scroll;

        [MenuItem("PLL/Props/Create Luggage in Selected Biome", priority = 24)]
        static void Open()
        {
            try
            {
                var Selected = Selection.activeGameObject;
                PllAtmosphereAuthoringCommand.RequireCurrentBiomeRoot(Selected);
                var Window = GetWindow<PllCreateLuggageWindow>("Create Luggage");
                Window.Root = Selected;
                Window.NativeProgression = false;
                Window.Timing = Placement.Fixed;
                Window.Spawner = null;
                Window.Items.Clear();
                Window.AssetFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
                Window.Show();
            }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }

        void OnGUI()
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Biome", Root, typeof(GameObject), true);
            EditorGUILayout.HelpBox(
                "Creates a body, hinged lid, animation, collision and loot anchors. Fixed luggage stays inactive in CampfireRoot. Generated luggage becomes an active prefab asset referenced by the selected spawner; Undo removes its binding, not its assets.",
                MessageType.Info);
            DisplayName = EditorGUILayout.TextField("Display Name", DisplayName);
            Timing = (Placement)EditorGUILayout.EnumPopup("Placement", Timing);
            if (Timing == Placement.Generated)
                Spawner = (PropSpawner)EditorGUILayout.ObjectField("Prop Spawner", Spawner, typeof(PropSpawner), true);
            OpenTime = EditorGUILayout.FloatField("Opening Seconds", OpenTime);
            AssetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Asset Folder", AssetFolder, typeof(DefaultAsset), false);
            var Link = Root ? Root.GetComponent<PllBiomeRoot>() : null;
            var SupportsProgression = Link && Link.Definition
                && PllAuthoringValueContract.IsDefined(Link.Definition.Behavior)
                && Link.Definition.SupportsNativeProgressionContents;
            using (new EditorGUI.DisabledScope(!SupportsProgression))
                NativeProgression = EditorGUILayout.Toggle("Native Progression Contents", NativeProgression && SupportsProgression);
            if (!SupportsProgression)
                EditorGUILayout.HelpBox(
                    "This biome has no native loot mapping. Supply custom item prefabs without changing its biome behavior.", MessageType.Info);
            if (!NativeProgression)
            {
                Scroll = EditorGUILayout.BeginScrollView(Scroll);
                for (var Index = 0; Index < Items.Count; Index++)
                {
                    EditorGUILayout.BeginHorizontal();
                    Items[Index] = (GameObject)EditorGUILayout.ObjectField(
                        "Item " + (Index + 1), Items[Index], typeof(GameObject), false);
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        Items.RemoveAt(Index);
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("Add Item")) Items.Add(null);
            }
            using (new EditorGUI.DisabledScope(!Root || !AssetFolder
                || Timing == Placement.Generated && !Spawner
                || !NativeProgression && Items.Count == 0))
                if (GUILayout.Button(Timing == Placement.Fixed ? "Create Fixed Luggage" : "Create Generated Luggage"))
                {
                    try
                    {
                        if (Timing == Placement.Fixed)
                            Create(Root, AssetDatabase.GetAssetPath(AssetFolder),
                                DisplayName, OpenTime, NativeProgression, Items);
                        else CreateGenerated(Root, Spawner, AssetDatabase.GetAssetPath(AssetFolder),
                            DisplayName, OpenTime, NativeProgression, Items);
                        Close();
                    }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        internal static PllLuggageAuthoring Create(
            GameObject Root, string AssetFolder, string DisplayName,
            float OpenTime, bool NativeProgression, IReadOnlyList<GameObject> Items) =>
            CreateSource(Root, AssetFolder, DisplayName, OpenTime, NativeProgression, Items, null);

        internal static PllLuggageAuthoring CreateGenerated(GameObject Root, PropSpawner Spawner,
            string AssetFolder, string DisplayName, float OpenTime,
            bool NativeProgression, IReadOnlyList<GameObject> Items)
        {
            const string Action = "Create Generated Luggage";
            if (!Spawner || !Spawner.transform.parent)
                throw new InvalidOperationException("Select a configured PropSpawner in the biome's editable hierarchy.");
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Spawner.transform.parent.gameObject, Action);
            if (!Owner.IsBiomeRoot || Owner.StageRoot != Root
                || !TransformOwnership.DiscoverableWhenActivated(Root.transform, Spawner.transform)
                || Spawner.overrideProps)
                throw new InvalidOperationException("The PropSpawner must be active in the selected biome without a candidate override.");
            var Nested = PrefabUtility.GetNearestPrefabInstanceRoot(Spawner.gameObject);
            if (Nested && Nested != Root)
                throw new InvalidOperationException("Open the spawner's source prefab before adding generated luggage.");
            if (Spawner.props != null)
                foreach (var Candidate in Spawner.props)
                {
                    PllSourceIdentityValidator.CandidatePath(Candidate, "Generated luggage spawner candidate");
                    if (Candidate.GetComponentsInChildren<PllCampfireAuthoring>(true).Length != 0)
                        throw new InvalidOperationException("Generated luggage cannot share a campfire-factory spawner.");
                }
            return CreateSource(Root, AssetFolder, DisplayName, OpenTime, NativeProgression, Items, Spawner);
        }

        static PllLuggageAuthoring CreateSource(GameObject Root, string AssetFolder,
            string DisplayName, float OpenTime, bool NativeProgression,
            IReadOnlyList<GameObject> Items, PropSpawner Spawner)
        {
            var Action = Spawner ? "Create Generated Luggage" : "Create Fixed Luggage";
            PllAtmosphereAuthoringCommand.RequireCurrentBiomeRoot(Root);
            var Link = Root.GetComponent<PllBiomeRoot>();
            if (!Link || !Link.Definition)
                throw new InvalidOperationException("The biome root has no linked definition.");
            var Anchors = Root.GetComponentInChildren<PllBiomePrefabAnchors>(true);
            var Owner = Spawner ? null : PllSourceIdentityValidator.ResolveEditableOwner(Anchors.CampfireRoot, Action);
            if (!Spawner && (!TransformOwnership.DiscoverableWhenActivated(Root.transform, Owner.Campfire)
                || TransformOwnership.HasConfigurationBranch(Root.transform, Owner.Campfire)))
                throw new InvalidOperationException("CampfireRoot must have active ancestors outside configuration branches.");
            if (!PllAuthoringValueContract.IsDisplayText(DisplayName)
                || !PllAuthoringValueContract.CanEncodeExactly(OpenTime) || OpenTime <= 0f)
                throw new InvalidOperationException("Luggage requires a display name and a positive opening duration with at most four decimal places.");
            if (AssetFolder != "Assets" && !AssetFolder.StartsWith("Assets/", StringComparison.Ordinal)
                || !AssetDatabase.IsValidFolder(AssetFolder))
                throw new InvalidOperationException("Select an asset folder inside Assets.");
            if (!PllAuthoringValueContract.IsDefined(Link.Definition.Behavior))
                throw new InvalidOperationException("The biome behavior is undefined.");
            if (NativeProgression)
            {
                if (!Link.Definition.SupportsNativeProgressionContents)
                    throw new InvalidOperationException("This biome has no native loot mapping. Supply custom item prefabs.");
            }
            else
            {
                if (Items.Count == 0)
                    throw new InvalidOperationException("Add at least one custom item prefab.");
                PllFixtureAuthoringValidator.ValidateItemCatalog(Items, Action);
            }
            var Shader = UnityEngine.Shader.Find("Unlit/Color");
            if (!Shader)
                throw new InvalidOperationException("Unity's Unlit/Color shader is unavailable.");

            var Identity = Guid.NewGuid().ToString("N");
            var AssetPath = AssetFolder + "/Luggage-" + Identity;
            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            GameObject Case = null;
            var Registered = false;
            var FolderCreated = false;
            try
            {
                var FolderGuid = AssetDatabase.CreateFolder(AssetFolder, "Luggage-" + Identity);
                if (string.IsNullOrEmpty(FolderGuid))
                    throw new InvalidOperationException("Unity could not create the luggage asset folder.");
                AssetPath = AssetDatabase.GUIDToAssetPath(FolderGuid);
                FolderCreated = true;
                var Mesh = PllSourceAssetCreation.CreatePrimitiveMesh(PrimitiveType.Cube, AssetPath + "/CaseCube.asset");
                var Material = PllSourceAssetCreation.SaveAsset(
                    new Material(Shader) { name = "Luggage", color = new Color(0.2f, 0.45f, 0.55f) }, AssetPath + "/Case.mat");
                var Closed = PllSourceAssetCreation.SaveAsset(
                    new AnimationClip { name = "Luggage_Closed" }, AssetPath + "/Closed.anim");
                var Opened = PllSourceAssetCreation.SaveAsset(
                    new AnimationClip { name = "Luggage_Open" }, AssetPath + "/Open.anim");
                var Rotation = Quaternion.Euler(105f, 0f, 0f);
                var Components = new[] { "x", "y", "z", "w" };
                for (var Index = 0; Index < Components.Length; Index++)
                {
                    var Start = Quaternion.identity[Index];
                    Closed.SetCurve("Hinge", typeof(Transform), "m_LocalRotation." + Components[Index], AnimationCurve.Constant(0f, OpenTime, Start));
                    Opened.SetCurve("Hinge", typeof(Transform), "m_LocalRotation." + Components[Index], AnimationCurve.EaseInOut(0f, Start, OpenTime, Rotation[Index]));
                }
                Opened.EnsureQuaternionContinuity();
                EditorUtility.SetDirty(Closed);
                EditorUtility.SetDirty(Opened);
                AssetDatabase.SaveAssetIfDirty(Closed);
                AssetDatabase.SaveAssetIfDirty(Opened);
                var Controller = AnimatorController.CreateAnimatorControllerAtPath(AssetPath + "/Luggage.controller");
                if (!Controller)
                    throw new InvalidOperationException("Unity could not create the luggage animation controller.");
                var Machine = Controller.layers[0].stateMachine;
                var Idle = Machine.AddState("Idle");
                Idle.motion = Closed;
                var Unclasp = Machine.AddState("Luggage_Unclasp");
                Unclasp.motion = Closed;
                Machine.AddState("Luggage_Open").motion = Opened;
                Machine.defaultState = Idle;
                Controller.AddParameter("Reclasp", AnimatorControllerParameterType.Trigger);
                var Reclasp = Unclasp.AddTransition(Idle);
                Reclasp.hasExitTime = false;
                Reclasp.duration = 0f;
                Reclasp.AddCondition(AnimatorConditionMode.If, 0f, "Reclasp");
                EditorUtility.SetDirty(Controller);
                AssetDatabase.SaveAssetIfDirty(Controller);

                Case = new GameObject("Luggage-" + Identity);
                Case.SetActive(false);
                if (!Spawner) Case.transform.SetParent(Owner.Campfire, false);
                CreatePart(Case.transform, "Body", new Vector3(0f, 0.25f, 0f),
                    new Vector3(1f, 0.5f, 0.7f), Mesh, Material);
                var Hinge = new GameObject("Hinge");
                Hinge.transform.SetParent(Case.transform, false);
                Hinge.transform.localPosition = new Vector3(0f, 0.5f, 0.35f);
                CreatePart(Hinge.transform, "Lid", new Vector3(0f, 0.05f, -0.35f),
                    new Vector3(1f, 0.1f, 0.7f), Mesh, Material);
                var Points = new Transform[2];
                for (var Index = 0; Index < Points.Length; Index++)
                {
                    var Point = new GameObject("Contents" + (Index + 1));
                    Point.transform.SetParent(Case.transform, false);
                    Point.transform.localPosition = new Vector3(Index == 0 ? -0.2f : 0.2f, 0.6f, 0f);
                    Points[Index] = Point.transform;
                }
                var Source = Case.AddComponent<PllLuggageAuthoring>();
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.Property(Serialized, "IdValue").stringValue = Identity;
                    PllAuthoringCommands.Property(Serialized, "DisplayNameValue").stringValue = DisplayName;
                    PllAuthoringCommands.Property(Serialized, "OpenTimeValue").floatValue = OpenTime;
                    PllAuthoringCommands.SetReference(Serialized, "AnimatorControllerValue", Controller);
                    PllAuthoringCommands.Property(Serialized, "ContentsModeValue").intValue =
                        (int)(NativeProgression ? PllLuggageContentsMode.NativeProgression : PllLuggageContentsMode.CustomItems);
                    var SpawnGroups = PllAuthoringCommands.Property(Serialized, "SpawnGroupsValue");
                    SpawnGroups.arraySize = 1;
                    var SpawnGroup = SpawnGroups.GetArrayElementAtIndex(0);
                    SpawnGroup.FindPropertyRelative("WeightValue").intValue = 1;
                    var SpawnPoints = SpawnGroup.FindPropertyRelative("PointsValue");
                    SpawnPoints.arraySize = Points.Length;
                    for (var Index = 0; Index < Points.Length; Index++)
                        SpawnPoints.GetArrayElementAtIndex(Index).objectReferenceValue = Points[Index];
                    var CustomItems = PllAuthoringCommands.Property(Serialized, "CustomItemsValue");
                    CustomItems.arraySize = NativeProgression ? 0 : Items.Count;
                    for (var Index = 0; Index < CustomItems.arraySize; Index++)
                    {
                        var Entry = CustomItems.GetArrayElementAtIndex(Index);
                        Entry.FindPropertyRelative("ItemValue").objectReferenceValue = Items[Index];
                        Entry.FindPropertyRelative("WeightValue").intValue = 1;
                    }
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                PllFixtureAuthoringValidator.ValidateLuggageComponent(Source, Link.Definition.Behavior, Action);
                if (Spawner)
                {
                    Case.SetActive(true);
                    var Candidate = PrefabUtility.SaveAsPrefabAsset(Case, AssetPath + "/Luggage.prefab");
                    if (!Candidate)
                        throw new InvalidOperationException("Unity could not save the generated luggage prefab.");
                    DestroyImmediate(Case);
                    Case = null;
                    Undo.RecordObject(Spawner, Action);
                    var Candidates = new GameObject[(Spawner.props?.Length ?? 0) + 1];
                    if (Spawner.props != null) Array.Copy(Spawner.props, Candidates, Spawner.props.Length);
                    Candidates[Candidates.Length - 1] = Candidate;
                    Spawner.props = Candidates;
                    EditorUtility.SetDirty(Spawner);
                    Source = Candidate.GetComponent<PllLuggageAuthoring>();
                }
                else Undo.RegisterCreatedObjectUndo(Case, Action);
                Registered = true;
                EditorSceneManager.MarkSceneDirty(Root.scene);
                Undo.CollapseUndoOperations(Group);
                Selection.activeObject = Source;
                return Source;
            }
            catch
            {
                Undo.RevertAllDownToGroup(Group);
                if (!Registered && Case) DestroyImmediate(Case);
                if (FolderCreated) AssetDatabase.DeleteAsset(AssetPath);
                throw;
            }
        }

        static void CreatePart(Transform Parent, string Name, Vector3 Position,
            Vector3 Scale, Mesh Mesh, Material Material)
        {
            var Part = new GameObject(Name);
            Part.transform.SetParent(Parent, false);
            Part.transform.localPosition = Position;
            Part.transform.localScale = Scale;
            Part.AddComponent<MeshFilter>().sharedMesh = Mesh;
            Part.AddComponent<MeshRenderer>().sharedMaterial = Material;
            Part.AddComponent<BoxCollider>();
        }
    }
}
