using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateBiomeWindow : EditorWindow
    {
        internal enum StarterCourse { Flat, Climbing }

        DefaultAsset OutputFolder;
        string BiomeId = "creator/biome";
        string DisplayTitle = "Custom Biome";
        string EntryConnector = PLLLibrary.LayoutConnector;
        string ExitConnector = PLLLibrary.LayoutConnector;
        DayNightProfile SourceProfile;
        StarterCourse Course;

        [MenuItem("PLL/Biomes/Create Biome", priority = 2)]
        static void Open()
        {
            var Window = GetWindow<PllCreateBiomeWindow>(true, "Create PLL Biome");
            Window.minSize = new Vector2(460f, 350f);
            Window.Show();
        }

        [MenuItem("PLL/Biomes/Create Day Night Profile", priority = 3)]
        static void CreateProfileFromMenu()
        {
            RunUiAction(() =>
            {
                PllCreatorInstallation.RequireReady();
                var Source = Selection.activeObject as DayNightProfile;
                var Path = EditorUtility.SaveFilePanelInProject("Create Day Night Profile", "DayNightProfile", "asset",
                    Source ? "Create a project-owned duplicate of the selected profile." : "Create a dynamic-lighting profile.");
                if (string.IsNullOrEmpty(Path)) return;
                var Profile = CreateProfile(Path, Source);
                Selection.activeObject = Profile;
                EditorGUIUtility.PingObject(Profile);
            });
        }

        void OnEnable()
        {
            if (!OutputFolder) OutputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Source Assets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates a course prefab and its biome, lighting, mesh and material assets in one folder. "
                + "The prefab starts inactive and uses dynamic lighting with an unlit material.",
                MessageType.Info);
            OutputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", OutputFolder, typeof(DefaultAsset), false);
            Course = (StarterCourse)EditorGUILayout.EnumPopup("Starter Course", Course);
            BiomeId = EditorGUILayout.TextField("Biome ID", BiomeId);
            DisplayTitle = EditorGUILayout.TextField("Display Title", DisplayTitle);
            EntryConnector = EditorGUILayout.TextField("Entry Connector", EntryConnector);
            ExitConnector = EditorGUILayout.TextField("Exit Connector", ExitConnector);
            SourceProfile = (DayNightProfile)EditorGUILayout.ObjectField(
                new GUIContent("Profile To Duplicate", "Optional. The duplicate becomes a project-owned asset."),
                SourceProfile, typeof(DayNightProfile), false);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "PLL does not rebake lighting. Baked and Mixed lights, saved lightmaps and Contribute GI with lightmap reception are not supported. "
                + "Use Realtime lights, light probes or unlit shaders. Effect PresentationRoot lights must use Realtime.", MessageType.Warning);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Create Biome", GUILayout.Height(30f))) CreateFromUi();
        }

        void CreateFromUi()
        {
            RunUiAction(() =>
            {
                var Definition = CreateBiome(OutputPath(), BiomeId, DisplayTitle, EntryConnector, ExitConnector, SourceProfile, Course);
                Selection.activeObject = Definition;
                EditorGUIUtility.PingObject(Definition);
                Close();
            });
        }

        string OutputPath()
        {
            if (!OutputFolder) throw new InvalidOperationException("Choose an existing project folder.");
            var Path = AssetDatabase.GetAssetPath(OutputFolder);
            if (string.IsNullOrEmpty(Path) || !AssetDatabase.IsValidFolder(Path)
                || !Path.Equals("Assets", StringComparison.Ordinal)
                && !Path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("The output folder must be an existing folder under Assets.");
            return Path;
        }

        internal static PllBiomeDefinition CreateBiome(
            string OutputPath,
            string IdText,
            string Title,
            string Entry,
            string Exit,
            DayNightProfile ProfileSource, StarterCourse Course = StarterCourse.Flat)
        {
            PllCreatorInstallation.RequireReady();
            if (Course is not (StarterCourse.Flat or StarterCourse.Climbing))
                throw new ArgumentOutOfRangeException(nameof(Course));
            PLL.Library.BiomeId Id;
            if (!PLL.Library.BiomeId.TryParse(IdText, out Id))
                throw new InvalidOperationException("Biome ID must use lowercase 'namespace/name'.");
            PllSourceIdentityValidator.ValidateToken(Entry, "Entry connector");
            PllSourceIdentityValidator.ValidateToken(Exit, "Exit connector");
            if (!AssetDatabase.IsValidFolder(OutputPath)
                || !OutputPath.Equals("Assets", StringComparison.Ordinal)
                && !OutputPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("The output folder must be an existing folder under Assets.");

            var AssetName = PllCreateBiomeWindow.AssetName(Id.Value.Substring(Id.Value.IndexOf('/') + 1));
            var Folder = UniqueFolder(OutputPath, AssetName);
            var FolderGuid = AssetDatabase.CreateFolder(OutputPath, Path.GetFileName(Folder));
            if (string.IsNullOrEmpty(FolderGuid))
                throw new InvalidOperationException("Unity could not create biome folder '" + Folder + "'.");
            var DefinitionPath = Folder + "/" + AssetName + ".asset";
            var ProfilePath = Folder + "/" + AssetName + "DayNightProfile.asset";
            var PrefabPath = Folder + "/" + AssetName + ".prefab";
            GameObject Root = null;
            PllBiomeDefinition Result = null;
            Exception PrimaryFailure = null;
            try
            {
                var Profile = CreateProfile(ProfilePath, ProfileSource);
                var Definition = CreateInstance<PllBiomeDefinition>();
                Definition.name = AssetName;
                PllSourceAssetCreation.SaveAsset(Definition, DefinitionPath);
                var Shader = UnityEngine.Shader.Find("Unlit/Color");
                if (!Shader) throw new InvalidOperationException("Unity's Unlit/Color shader is unavailable.");
                var Material = PllSourceAssetCreation.SaveAsset(new Material(Shader)
                {
                    name = AssetName + "Course", color = new Color(0.35f, 0.55f, 0.65f)
                }, Folder + "/" + AssetName + "Course.mat");
                Root = new GameObject(AssetName);
                Root.SetActive(false);
                CreateRoot(Root, Definition, Title, Entry, Exit, Course,
                    PllSourceAssetCreation.CreatePrimitiveMesh(PrimitiveType.Cube, Folder + "/CourseCube.asset"),
                    PllSourceAssetCreation.CreatePrimitiveMesh(PrimitiveType.Cylinder, Folder + "/CampfireCylinder.asset"), Material);
                var Prefab = PrefabUtility.SaveAsPrefabAsset(Root, PrefabPath);
                if (!Prefab) throw new InvalidOperationException("Unity could not save root prefab '" + PrefabPath + "'.");
                var Anchors = Prefab.GetComponent<PllBiomePrefabAnchors>();
                var Serialized = new SerializedObject(Definition);
                var BiomeIdProperty = PllAuthoringCommands.Property(Serialized, "biomeId").FindPropertyRelative("value");
                if (BiomeIdProperty == null)
                    throw new InvalidOperationException("PllBiomeDefinition.biomeId is incompatible with this creator package.");
                BiomeIdProperty.stringValue = Id.Value;
                PllAuthoringCommands.Property(Serialized, "behavior").intValue =
                    (int)PllBiomeBehavior.Authored;
                PllAuthoringCommands.Property(Serialized, "displayTitle").stringValue = Title ?? string.Empty;
                PllAuthoringCommands.SetReference(Serialized, "rootPrefab", Prefab);
                PllAuthoringCommands.SetReference(Serialized, "anchors", Anchors);
                PllAuthoringCommands.SetReference(Serialized, "dayNightProfile", Profile);
                Serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(Definition);
                AssetDatabase.SaveAssets();
                new PllSourceCatalogValidator().Validate(Definition);
                Result = Definition;
            }
            catch (Exception Failure)
            {
                PrimaryFailure = Failure;
            }

            var CleanupFailures = new List<Exception>(2);
            try
            {
                if (Root) DestroyImmediate(Root);
            }
            catch (Exception Failure)
            {
                CleanupFailures.Add(Failure);
            }
            if (PrimaryFailure != null)
                try
                {
                    if (AssetDatabase.IsValidFolder(Folder) && !AssetDatabase.DeleteAsset(Folder))
                        throw new InvalidOperationException("Unity could not clean failed biome folder '" + Folder + "'.");
                }
                catch (Exception Failure)
                {
                    CleanupFailures.Add(Failure);
                }

            if (PrimaryFailure != null)
            {
                if (CleanupFailures.Count != 0)
                {
                    CleanupFailures.Insert(0, PrimaryFailure);
                    throw new AggregateException("Biome creation and cleanup failed.", CleanupFailures);
                }
                ExceptionDispatchInfo.Capture(PrimaryFailure).Throw();
                return null;
            }
            if (CleanupFailures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(CleanupFailures[0]).Throw();
                return null;
            }
            if (CleanupFailures.Count > 1)
                throw new AggregateException("Biome cleanup failed.", CleanupFailures);
            return Result;
        }

        static void CreateRoot(
            GameObject Root,
            PllBiomeDefinition Definition,
            string Title,
            string EntryConnector,
            string ExitConnector, StarterCourse Course,
            Mesh CourseMesh, Mesh CampfireMesh, Material Material)
        {
            var Runtime = Root.AddComponent<CustomSegmentRuntime>();
            var Anchors = Root.AddComponent<PllBiomePrefabAnchors>();
            var Link = Root.AddComponent<PllBiomeRoot>();
            var Floor = Child(Root.transform, "FloorRoot");
            var Campfire = Child(Root.transform, "CampfireRoot");
            var EndHeight = Course == StarterCourse.Climbing ? 9f : 0f;
            if (Course == StarterCourse.Flat)
                Surface(Floor.transform, "TestGround", CourseMesh, Material,
                    new Vector3(0f, -1f, 50f), new Vector3(64f, 2f, 100f));
            else
            {
                Surface(Floor.transform, "FormationGround", CourseMesh, Material,
                    new Vector3(0f, -1f, 17.5f), new Vector3(64f, 2f, 35f));
                Surface(Floor.transform, "FirstRestingLedge", CourseMesh, Material,
                    new Vector3(0f, 0.5f, 42.5f), new Vector3(64f, 5f, 15f));
                Surface(Floor.transform, "SecondRestingLedge", CourseMesh, Material,
                    new Vector3(0f, 2f, 57.5f), new Vector3(64f, 8f, 15f));
                Surface(Floor.transform, "EndpointPlateau", CourseMesh, Material,
                    new Vector3(0f, 3.5f, 82.5f), new Vector3(64f, 11f, 35f));
            }
            Campfire.transform.localPosition = new Vector3(0f, EndHeight, 85f);
            var Markers = Child(Root.transform, "RuntimeMarkers");
            Child(Root.transform, "Presentation");

            Port(Markers.transform, "Entry", SegmentPortKind.Entry, EntryConnector, Vector3.zero);
            Port(Markers.transform, "Exit", SegmentPortKind.Exit, ExitConnector, new Vector3(0f, EndHeight, 100f));
            var Start = Port(Markers.transform, "Start", SegmentPortKind.Start, PLLLibrary.StartConnector, new Vector3(-26f, 2f, 10f));
            var Formation = Child(Markers.transform, "PlayerFormationRoot");
            Formation.transform.SetLocalPositionAndRotation(Start.transform.localPosition, Start.transform.localRotation);
            var Plane = Pose(Markers.transform, "PlaneAnchor", new Vector3(18f, 3.5f, 20f));
            var FogBegin = Pose(Markers.transform, "FogBeginAnchor", new Vector3(0f, 0f, 10f));
            var FogCutoff = Pose(Markers.transform, "FogCutoffAnchor", new Vector3(0f, EndHeight, 90f));
            var FogOriginObject = Child(Markers.transform, "FogOrigin");
            var FogOrigin = FogOriginObject.AddComponent<FogSphereOrigin>();
            FogOriginObject.AddComponent<PllFogAuthoring>();
            FogOrigin.size = 120f;
            Pose(FogOriginObject.transform, "Fogwall_Next", new Vector3(0f, EndHeight, 100f));
            Pose(FogOriginObject.transform, "Fogwall_Prev", Vector3.zero);
            var FogCutoutObject = Child(Markers.transform, "FogCutout");
            var FogCutout = FogCutoutObject.AddComponent<FogCutoutZone>();
            FogCutout.min = 0f;
            FogCutout.max = 1f;
            FogCutout.amount = 1f;
            var ProgressObject = Child(Markers.transform, "ProgressPoint");
            ProgressObject.transform.localPosition = new Vector3(0f, EndHeight, 75f);

            var Model = Surface(Campfire.transform, "CampfireModel", CampfireMesh, Material,
                Vector3.zero, Vector3.one);
            PllAuthoringCommands.CreateCampfireForNewBiome(Model, Campfire);
            Model.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            Model.transform.localScale = new Vector3(1.4f, 0.35f, 1.4f);

            var RuntimeSerialized = new SerializedObject(Runtime);
            PllAuthoringCommands.SetReference(RuntimeSerialized, "segmentParent", Floor);
            PllAuthoringCommands.SetReference(RuntimeSerialized, "segmentCampfire", Campfire);
            PllAuthoringCommands.SetReference(RuntimeSerialized, "fogOrigin", FogOrigin);
            PllAuthoringCommands.SetReference(RuntimeSerialized, "fogCutout", FogCutout);
            PllAuthoringCommands.Property(RuntimeSerialized, "progressPoint").boxedValue =
                new MountainProgressHandler.ProgressPoint { transform = ProgressObject.transform, title = Title ?? string.Empty };
            RuntimeSerialized.ApplyModifiedPropertiesWithoutUndo();

            var AnchorsSerialized = new SerializedObject(Anchors);
            PllAuthoringCommands.SetReference(AnchorsSerialized, "floorRoot", Floor);
            PllAuthoringCommands.SetReference(AnchorsSerialized, "campfireRoot", Campfire);
            PllAuthoringCommands.SetReference(AnchorsSerialized, "startAnchor", Start.transform);
            PllAuthoringCommands.SetReference(AnchorsSerialized, "playerFormationRoot", Formation);
            PllAuthoringCommands.SetReference(AnchorsSerialized, "planeAnchor", Plane);
            PllAuthoringCommands.SetReference(AnchorsSerialized, "fogBeginAnchor", FogBegin);
            PllAuthoringCommands.SetReference(AnchorsSerialized, "fogCutoffAnchor", FogCutoff);
            AnchorsSerialized.ApplyModifiedPropertiesWithoutUndo();

            var LinkSerialized = new SerializedObject(Link);
            PllAuthoringCommands.SetReference(LinkSerialized, "definition", Definition);
            LinkSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject Surface(Transform Parent, string Name,
            Mesh Mesh, Material Material, Vector3 Position, Vector3 Scale)
        {
            var Value = Child(Parent, Name);
            Value.transform.localPosition = Position;
            Value.transform.localScale = Scale;
            Value.layer = PllClimbSurfaceAuthoringValidator.TerrainLayer;
            Value.AddComponent<MeshFilter>().sharedMesh = Mesh;
            var Renderer = Value.AddComponent<MeshRenderer>();
            Renderer.sharedMaterial = Material;
            Renderer.receiveGI = ReceiveGI.LightProbes;
            GameObjectUtility.SetStaticEditorFlags(Value, (StaticEditorFlags)0);
            PllClimbSurfaceAuthoringValidator.ConfigureStaticMeshCollider(
                Value.AddComponent<MeshCollider>(), Mesh);
            return Value;
        }

        static SegmentPort Port(Transform Parent, string Name, SegmentPortKind Kind, string Connector, Vector3 Position)
        {
            var Value = Child(Parent, Name);
            Value.transform.localPosition = Position;
            var Port = Value.AddComponent<SegmentPort>();
            var Serialized = new SerializedObject(Port);
            PllAuthoringCommands.Property(Serialized, "kind").enumValueIndex = (int)Kind;
            PllAuthoringCommands.Property(Serialized, "connector").stringValue = Connector;
            Serialized.ApplyModifiedPropertiesWithoutUndo();
            return Port;
        }

        static Transform Pose(Transform Parent, string Name, Vector3 Position)
        {
            var Value = Child(Parent, Name).transform;
            Value.localPosition = Position;
            return Value;
        }

        static GameObject Child(Transform Parent, string Name)
        {
            var Value = new GameObject(Name);
            Value.transform.SetParent(Parent, false);
            return Value;
        }

        static DayNightProfile CreateProfile(string Path, DayNightProfile Source)
        {
            if (!Path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DayNightProfile output must be a project .asset path.");
            if (AssetDatabase.LoadAssetAtPath<Object>(Path))
                throw new InvalidOperationException("Asset '" + Path + "' already exists; PLL did not overwrite it.");
            DayNightProfile Profile;
            if (Source)
            {
                if (!EditorUtility.IsPersistent(Source))
                    throw new InvalidOperationException("Profile To Duplicate must be a saved project or package asset.");
                Profile = Instantiate(Source);
            }
            else
            {
                Profile = CreateInstance<DayNightProfile>();
                Profile.sunIntensity = 1f;
                Profile.moonIntensity = 0.25f;
                Profile.sunGradient = FlatGradient(Color.white);
                Profile.skyTopGradient = FlatGradient(new Color(0.25f, 0.45f, 0.8f));
                Profile.skyMidGradient = FlatGradient(new Color(0.55f, 0.7f, 0.9f));
                Profile.skyBottomGradient = FlatGradient(new Color(0.75f, 0.8f, 0.85f));
                Profile.fogGradient = FlatGradient(new Color(0.65f, 0.7f, 0.75f));
                var Serialized = new SerializedObject(Profile);
                PllAuthoringCommands.Property(Serialized, "globalShaderFloats").arraySize = 0;
                PllAuthoringCommands.Property(Serialized, "globalStaticShaderFloats").arraySize = 0;
                PllAuthoringCommands.Property(Serialized, "globalStaticShaderColors").arraySize = 0;
                PllAuthoringCommands.Property(Serialized, "animatedGlobalShaderFloats").arraySize = 0;
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Profile.name = System.IO.Path.GetFileNameWithoutExtension(Path);
            Profile.hideFlags = HideFlags.None;
            return PllSourceAssetCreation.SaveAsset(Profile, Path);
        }

        static Gradient FlatGradient(Color Color)
        {
            var Gradient = new Gradient();
            Gradient.SetKeys(
                new[] { new GradientColorKey(Color, 0f), new GradientColorKey(Color, 1f) },
                new[] { new GradientAlphaKey(Color.a, 0f), new GradientAlphaKey(Color.a, 1f) });
            return Gradient;
        }

        static string UniqueFolder(string Parent, string Requested)
        {
            var Candidate = Parent + "/" + Requested;
            if (!AssetDatabase.IsValidFolder(Candidate)) return Candidate;
            for (var Suffix = 2; ; Suffix++)
            {
                Candidate = Parent + "/" + Requested + Suffix;
                if (!AssetDatabase.IsValidFolder(Candidate)) return Candidate;
            }
        }

        static string AssetName(string Value)
        {
            var Characters = Value.ToCharArray();
            for (var i = 0; i < Characters.Length; i++)
                if (!char.IsLetterOrDigit(Characters[i]) && Characters[i] != '-' && Characters[i] != '_') Characters[i] = '-';
            var Result = new string(Characters).Trim('-');
            return string.IsNullOrEmpty(Result) ? "CustomBiome" : Result;
        }

        static void RunUiAction(Action Action)
        {
            try
            {
                Action();
            }
            catch (Exception Failure) when (Failure is InvalidOperationException or ArgumentException or UnityException)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }
    }
}
