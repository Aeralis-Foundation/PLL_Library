using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Text;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PLL.Creator.Editor
{
    public static class PllBundleBuilder
    {
        const string BundleName = "content.pll";
        const string MenuPath = "PLL/Build/Build .pll";

        public static PllFogShaderAdapter PrepareFogShaderAdapter(PllBiomeDefinition Definition, Shader OriginalShader) =>
            PllBuildArtifacts.PrepareFogShaderAdapter(Definition, OriginalShader);

        [MenuItem(MenuPath, priority = 1)]
        static void BuildSelected()
        {
            try
            {
                var Definitions = CollectSelection();
                var Suggested = Definitions.Count == 1
                    ? FileName(Definitions[0].BiomeId.Value) : "pll-biomes";
                var Selected = EditorUtility.SaveFilePanel(
                    "Build PLL bundle", "", Suggested, "pll");
                if (string.IsNullOrEmpty(Selected)) return;
                PllCreatorResultWindow.ShowBuilt(
                    Build(Definitions, NormalizeExternalOutput(Selected)));
            }
            catch (InvalidOperationException Failure)
            {
                PllCreatorResultWindow.ShowFailure(Failure, Selection.activeObject);
            }
        }

        internal static BuildReportData Build(IReadOnlyList<PllBiomeDefinition> Definitions, string OutputPath)
        {
            RequireSavedSources(Definitions);
            if (!PllCreatorInstallation.TryValidateUnityVersion(
                    out var UnityFailure))
                throw new InvalidOperationException(UnityFailure);
            var Installation = PllCreatorInstallation.RequireReady();
            var Validator = new PllSourceCatalogValidator();
            var Artifacts = new PllBuildArtifacts(Installation);
            var Catalogs = new DefinitionCatalog[Definitions.Count];
            for (var i = 0; i < Definitions.Count; i++) Catalogs[i] = Validator.Validate(Definitions[i]);
            PllSourceCatalogValidator.ValidateScope(Definitions, Catalogs);

            ProjectStage ProjectStage = null;
            ExternalStage ExternalStage = null;
            Exception PrimaryFailure = null;
            BuildReportData Report = null;
            try
            {
                ProjectStage = ProjectStage.Create();
                ExternalStage = ExternalStage.Create(OutputPath);
                var Staged = Artifacts.StageDefinitions(ProjectStage, Definitions, Catalogs);
                var RegistryPath = Artifacts.CreateRegistry(ProjectStage, Staged.Definitions);
                var AssetPaths = Staged.Definitions.SelectMany(Value => new[] { Value.DefinitionPath, Value.PrefabPath })
                    .Append(RegistryPath).OrderBy(Value => Value, StringComparer.Ordinal).ToArray();

                var Audit = Artifacts.Audit(AssetPaths);
                var Build = new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = AssetPaths,
                    addressableNames = CreateAddressableNames(AssetPaths, Staged.Definitions, Catalogs)
                };
                var Options = BuildAssetBundleOptions.ChunkBasedCompression
                    | BuildAssetBundleOptions.ForceRebuildAssetBundle
                    | BuildAssetBundleOptions.StrictMode;
                var Manifest = BuildPipeline.BuildAssetBundles(
                    ExternalStage.RootPath, new[] { Build }, Options, BuildTarget.StandaloneWindows64);
                if (Manifest == null || !Manifest.GetAllAssetBundles().Contains(BundleName, StringComparer.Ordinal))
                    throw new InvalidOperationException("Unity did not produce the requested content.pll AssetBundle.");

                var BuiltPath = Path.Combine(ExternalStage.RootPath, BundleName);
                var Inspection = Artifacts.InspectBundle(
                    BuiltPath, Staged.Definitions, Audit, Build.addressableNames);
                Report = Artifacts.CreateReport(OutputPath, Staged, Audit, Inspection, BuiltPath);
                var ReportPath = Path.Combine(ExternalStage.RootPath, "build-report.json");
                File.WriteAllText(ReportPath, JsonUtility.ToJson(Report, true), PllCreatorFileSystem.StrictUtf8);
                Report.PublishedReportSha256 = PllCreatorFileSystem.Sha256(ReportPath);
                PllBuildPublisher.Publish(BuiltPath, OutputPath, ReportPath, OutputPath + ".report.json");
            }
            catch (Exception Failure)
            {
                PrimaryFailure = Failure;
            }

            var CleanupFailures = new List<Exception>(2);
            try
            {
                if (ExternalStage != null) ExternalStage.Cleanup();
            }
            catch (Exception Failure)
            {
                CleanupFailures.Add(Failure);
            }
            try
            {
                if (ProjectStage != null) ProjectStage.Cleanup();
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
                    throw new AggregateException("PLL bundle build and staging cleanup failed.", CleanupFailures);
                }
                ExceptionDispatchInfo.Capture(PrimaryFailure).Throw();
            }
            if (CleanupFailures.Count == 1)
                ExceptionDispatchInfo.Capture(CleanupFailures[0]).Throw();
            if (CleanupFailures.Count > 1)
                throw new AggregateException("PLL bundle staging cleanup failed.", CleanupFailures);
            return Report;
        }

        static string[] CreateAddressableNames(
            IReadOnlyList<string> AssetPaths,
            IReadOnlyList<StagedDefinition> Staged,
            IReadOnlyList<DefinitionCatalog> Catalogs)
        {
            var Definitions = new Dictionary<string, string>(Staged.Count, StringComparer.Ordinal);
            for (var i = 0; i < Staged.Count; i++)
            {
                if (Catalogs[i].CustomVisuals.Length == 0 && !Catalogs[i].Atmosphere
                    && Staged[i].Staged.CompiledFogShaderAdapters.Count == 0
                    && Catalogs[i].SurfaceTextures.Length == 0 && Catalogs[i].BreakableProps.Length == 0
                    && Catalogs[i].SwingingProps.Length == 0 && Catalogs[i].Vines.Length == 0
                    && Catalogs[i].RestPoints.Length == 0 && Catalogs[i].HangingRopes.Length == 0
                    && !Array.Exists(Catalogs[i].NativeResolvers,
                        Resolver => Resolver.Form == PllNativeResolverForm.Source || Resolver.Kind is PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid)
                    && !Array.Exists(Catalogs[i].EntityZones,
                        Zone => Zone.Source is PllPluginEntityZoneSource
                            || PllNativeEntityAppearanceValueContract.Appearance(Zone.Source) is not null)) continue;
                var IdentityRoot = Catalogs[i].Graph.ByPath[Catalogs[i].Graph.RootPath].SourceRoot.transform;
                var Seal = PllPresentationDependencySealBuilder.Create(
                    Catalogs[i], Staged[i].Staged.CompiledFogShaderAdapters, IdentityRoot);
                Definitions.Add(Staged[i].DefinitionPath, PllDependencySeal.CreateAddress(Staged[i].Staged.BiomeId, Seal));
            }
            var Result = new string[AssetPaths.Count];
            for (var i = 0; i < Result.Length; i++)
                Result[i] = Definitions.TryGetValue(AssetPaths[i], out var Address) ? Address : AssetPaths[i];
            return Result;
        }

        internal static List<PllBiomeDefinition> CollectSelection()
        {
            var ByObject = new HashSet<PllBiomeDefinition>();
            var Result = new List<PllBiomeDefinition>();
            var Selected = Selection.objects;
            if (Selected.Length == 0)
                Add(SelectedDefinition(null));
            else foreach (var Value in Selected)
            {
                if (Value is PllBiomeRegistry Registry)
                {
                    Registry.Validate();
                    foreach (var Candidate in Registry.Definitions) Add(Candidate);
                }
                else Add(SelectedDefinition(Value));
            }
            if (Result.Count == 0)
                throw new PllAuthoringFailure(
                    "The selected registries contain no biome definitions.", Selection.activeObject);
            for (var Index = 0; Index < Result.Count; Index++)
            {
                var Group = PllBiomeGroupContract.Capture(Result[Index]);
                if (Group == null) continue;
                foreach (var Prefix in Group.Prefixes) Add(Prefix);
            }
            Result.Sort(CompareDefinitions);
            for (var i = 1; i < Result.Count; i++)
                if (Result[i - 1].BiomeId == Result[i].BiomeId)
                    throw new PllAuthoringFailure("Biome ID '" + Result[i].BiomeId
                        + "' is owned by both '" + AssetLabel(Result[i - 1])
                        + "' and '" + AssetLabel(Result[i]) + "'.", Result[i], "biomeId");
            return Result;

            void Add(PllBiomeDefinition Candidate)
            {
                if (!Candidate)
                    throw new PllAuthoringFailure(
                        "A selected registry contains a missing definition.", Selection.activeObject, "definitions");
                if (!AssetDatabase.IsMainAsset(Candidate))
                    throw new PllAuthoringFailure(
                        "Biome definition must be a main project asset.", Candidate);
                if (ByObject.Add(Candidate)) Result.Add(Candidate);
            }
        }

        internal static PllBiomeDefinition SelectedDefinition(Object Value)
        {
            if (Value is PllBiomeDefinition Definition) return Definition;
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            var Selected = Value is Component Component ? Component.gameObject
                : Value as GameObject;
            if (!Value && Stage != null) Selected = Stage.prefabContentsRoot;
            if (!Selected)
                throw new PllAuthoringFailure(
                    "Select a biome definition, registry, or an object in a biome prefab.", Value);
            if (!EditorUtility.IsPersistent(Selected)
                && (Stage == null || Selected.scene != Stage.scene))
                throw new PllAuthoringFailure(
                    "Open the selected biome prefab in Prefab Mode.", Selected);
            var Link = Selected.GetComponentInParent<PllBiomeRoot>(true);
            if (!Link || !Link.Definition)
                throw new PllAuthoringFailure(
                    "The selected prefab has no biome definition binding.", Link ? (Object)Link : Selected, "definition");
            var Source = EditorUtility.IsPersistent(Selected)
                ? Link.gameObject : AssetDatabase.LoadAssetAtPath<GameObject>(Stage.assetPath);
            if (Link.transform.parent || !Source || Link.Definition.RootPrefab != Source)
                throw new PllAuthoringFailure(
                    "The biome root binding points to a different prefab.", Link, "definition");
            return Link.Definition;
        }

        internal static void RequireSavedSources(IReadOnlyList<PllBiomeDefinition> Definitions)
        {
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (Stage == null || !Stage.scene.isDirty) return;
            foreach (var Definition in Definitions)
                if (AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(Definition), true)
                    .Contains(Stage.assetPath, StringComparer.Ordinal))
                    throw new PllAuthoringFailure(
                        "The open source prefab has unsaved changes. Save it before building or validating.",
                        Stage.prefabContentsRoot, UnsavedPrefabPath: Stage.assetPath);
        }

        static string NormalizeExternalOutput(string Selected)
        {
            if (!string.Equals(Path.GetExtension(Selected), ".pll", StringComparison.Ordinal))
                throw new InvalidOperationException("Output filename must end with exact lowercase '.pll'.");
            var Output = Path.GetFullPath(Selected);
            var Project = Path.GetFullPath(System.IO.Directory.GetParent(Application.dataPath).FullName);
            if (PllCreatorFileSystem.Inside(Output, Project))
                throw new InvalidOperationException("Choose an output path outside the Unity creator project.");
            var Directory = Path.GetDirectoryName(Output);
            if (string.IsNullOrEmpty(Directory) || !System.IO.Directory.Exists(Directory))
                throw new DirectoryNotFoundException("Output directory '" + Directory + "' does not exist.");
            return Output;
        }

        internal static string AssetLabel(Object Value)
        {
            if (!Value) return "<missing>";
            var Path = AssetDatabase.GetAssetPath(Value);
            return string.IsNullOrEmpty(Path) ? Value.name : Path;
        }

        internal static SerializedProperty Property(SerializedObject Value, string Name)
        {
            var Property = Value.FindProperty(Name);
            if (Property == null) throw new InvalidOperationException("Serialized field '" + Name + "' is unavailable on '"
                + Value.targetObject.GetType().FullName + "'. Rebuild the creator SDK from the selected runtime snapshot.");
            return Property;
        }

        static int CompareDefinitions(PllBiomeDefinition Left, PllBiomeDefinition Right)
        {
            var Result = StringComparer.Ordinal.Compare(Left.BiomeId.Value, Right.BiomeId.Value);
            return Result != 0 ? Result : StringComparer.Ordinal.Compare(AssetLabel(Left), AssetLabel(Right));
        }

        static string FileName(string Value)
        {
            var Result = new StringBuilder(Value.Length);
            foreach (var Character in Value.ToLowerInvariant())
                Result.Append(char.IsLetterOrDigit(Character) || Character == '-' || Character == '_' ? Character : '-');
            return Result.Length == 0 ? "pll-biome" : Result.ToString();
        }

    }

    internal sealed class PllAuthoringFailure : InvalidOperationException
    {
        internal PllAuthoringFailure(
            string Message, Object Context, string PropertyPath = "",
            string UnsavedPrefabPath = "", Exception InnerFailure = null,
            IReadOnlyList<PllCreatorDiagnostic> Diagnostics = null) : base(Message, InnerFailure)
        {
            this.Context = Context;
            this.PropertyPath = PropertyPath;
            this.UnsavedPrefabPath = UnsavedPrefabPath;
            this.Diagnostics = Diagnostics == null ? Array.Empty<PllCreatorDiagnostic>()
                : Array.AsReadOnly(Diagnostics.ToArray());
        }

        internal Object Context { get; }
        internal string PropertyPath { get; }
        internal string UnsavedPrefabPath { get; }
        internal IReadOnlyList<PllCreatorDiagnostic> Diagnostics { get; }
    }

    internal sealed class PllCreatorResultWindow : EditorWindow
    {
        abstract class Result { }
        sealed class AuthoringFailure : Result
        {
            internal AuthoringFailure(PllAuthoringFailure Failure)
            {
                this.Failure = Failure;
                if (Failure.Diagnostics.Count == 0)
                {
                    Summary = Failure.Message;
                    return;
                }
                var Errors = 0;
                var Warnings = 0;
                foreach (var Diagnostic in Failure.Diagnostics)
                {
                    if (Diagnostic.Severity == PllCreatorDiagnosticSeverity.Error) Errors++;
                    else if (Diagnostic.Severity == PllCreatorDiagnosticSeverity.Warning) Warnings++;
                }
                Summary = "Source validation is blocked. Errors: " + Errors + "; warnings: " + Warnings + ".";
            }
            internal PllAuthoringFailure Failure { get; }
            internal string Summary { get; }
        }
        sealed class PublishedBuild : Result
        {
            internal PublishedBuild(BuildReportData Report)
            {
                Publication = new PllProfilePublication(Report);
                Biomes = string.Join(", ", Report.biomeIds);
                Bytes = Report.bundleBytes;
                UnityVersion = Report.unityVersion;
                PeakVersion = Report.peakFileVersion;
            }

            internal PllProfilePublication Publication { get; }
            internal string Biomes { get; }
            internal long Bytes { get; }
            internal string UnityVersion { get; }
            internal string PeakVersion { get; }
            internal PllProfilePublication.Target Profile;
            internal string InstalledBundlePath;
            internal PllProfilePublication.RuntimeResult RecordedResult;
        }
        sealed class Notice : Result
        {
            internal Notice(string Message) => this.Message = Message;
            internal string Message { get; }
        }

        Result CurrentResult = new Notice("Run Build .pll or Validate Selected Biome.");
        Vector2 Scroll;

        internal static void ShowFailure(InvalidOperationException Failure, Object Context) =>
            ShowResult(new AuthoringFailure(Failure as PllAuthoringFailure
                ?? new PllAuthoringFailure(Failure.Message, Context, InnerFailure: Failure)));

        internal static void ShowValidation(string Summary) => ShowResult(new Notice(Summary));
        internal static void ShowBuilt(BuildReportData Report) => ShowResult(new PublishedBuild(Report));

        static void ShowResult(Result Value)
        {
            var Window = GetWindow<PllCreatorResultWindow>("PLL Result");
            Window.minSize = new Vector2(560f, 300f);
            Window.CurrentResult = Value;
            Window.Show();
        }

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            switch (CurrentResult)
            {
                case AuthoringFailure Failed:
                    var Failure = Failed.Failure;
                    if (Failure.Diagnostics.Count != 0)
                    {
                        EditorGUILayout.HelpBox(Failed.Summary, MessageType.Error);
                        PllBiomeDefinitionEditor.DrawDiagnostics(Failure.Diagnostics);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(Failure.Message, MessageType.Error);
                        if (!string.IsNullOrEmpty(Failure.PropertyPath))
                            EditorGUILayout.LabelField("Field", Failure.PropertyPath);
                        if (Failure.Context && GUILayout.Button("Select Object"))
                        {
                            Selection.activeObject = Failure.Context;
                            EditorGUIUtility.PingObject(Failure.Context);
                        }
                    }
                    if (GUILayout.Button("Open Setup Status"))
                        EditorApplication.ExecuteMenuItem("PLL/Setup/Status");
                    if (!string.IsNullOrEmpty(Failure.UnsavedPrefabPath)
                        && GUILayout.Button("Save Prefab"))
                    {
                        try
                        {
                            SaveOpenPrefab(Failure);
                            CurrentResult = new Notice("Prefab saved. Run Build .pll or Validate Selected Biome again.");
                        }
                        catch (PllAuthoringFailure SaveFailure)
                        {
                            CurrentResult = new AuthoringFailure(SaveFailure);
                        }
                    }
                    break;
                case PublishedBuild Published:
                    EditorGUILayout.HelpBox("Bundle and report published. Gameplay is not tested.", MessageType.Info);
                    EditorGUILayout.LabelField("Biomes", Published.Biomes);
                    EditorGUILayout.LabelField("Bundle", Published.Publication.BundlePath);
                    EditorGUILayout.LabelField("Report", Published.Publication.ReportPath);
                    EditorGUILayout.LabelField("SHA-256", Published.Publication.BundleSha256);
                    EditorGUILayout.LabelField("Bytes", Published.Bytes.ToString());
                    EditorGUILayout.LabelField("Unity", Published.UnityVersion);
                    EditorGUILayout.LabelField("PEAK", Published.PeakVersion);
                    EditorGUILayout.LabelField("PLL", Published.Publication.PllAssembly);
                    if (GUILayout.Button("Show Bundle")) EditorUtility.RevealInFinder(Published.Publication.BundlePath);
                    if (GUILayout.Button("Show Report")) EditorUtility.RevealInFinder(Published.Publication.ReportPath);
                    DrawProfile(Published);
                    break;
                case Notice Message:
                    EditorGUILayout.HelpBox(Message.Message, MessageType.Info);
                    break;
                default:
                    throw new InvalidOperationException("PLL Creator result type is unsupported.");
            }
            EditorGUILayout.EndScrollView();
        }

        static void DrawProfile(PublishedBuild Published)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected game profile", EditorStyles.boldLabel);
            try
            {
                if (GUILayout.Button("Select and Inspect Game Profile"))
                {
                    var Root = EditorUtility.OpenFolderPanel("Select the game profile containing BepInEx",
                        Published.Profile == null ? string.Empty : Published.Profile.Root, string.Empty);
                    if (!string.IsNullOrEmpty(Root))
                    {
                        var RuntimePath = EditorUtility.OpenFilePanel("Select the installed PLL runtime DLL",
                            Path.Combine(Root, "BepInEx", "plugins"), "dll");
                        if (!string.IsNullOrEmpty(RuntimePath))
                        {
                            Published.Profile = PllProfilePublication.Target.Inspect(
                                Root, RuntimePath, Published.Publication.PllAssembly);
                            Published.RecordedResult = PllProfilePublication.RuntimeResult.Unverified;
                        }
                    }
                }
                if (Published.Profile != null)
                {
                    EditorGUILayout.LabelField("Inspected profile", Published.Profile.Root);
                    EditorGUILayout.LabelField("Inspected runtime DLL", Published.Profile.RuntimePath);
                    EditorGUILayout.LabelField("Runtime SHA-256", Published.Profile.RuntimeSha256);
                    EditorGUILayout.LabelField("Runtime module MVID", Published.Profile.RuntimeMvid.ToString("D"));
                    EditorGUILayout.LabelField("Installation directory", Published.Profile.BundleDirectory);
                    if (GUILayout.Button("Install Current Bundle and Report")
                        && EditorUtility.DisplayDialog("Install PLL bundle",
                            "Close PEAK before installing. Replace only this bundle and report at:\n"
                            + Path.Combine(Published.Profile.BundleDirectory,
                                Path.GetFileName(Published.Publication.BundlePath)), "Install", "Cancel"))
                    {
                        Published.InstalledBundlePath = Published.Publication.Install(Published.Profile);
                        Published.RecordedResult = PllProfilePublication.RuntimeResult.Unverified;
                    }
                    if (GUILayout.Button("Read Recorded Runtime Result"))
                        Published.RecordedResult = Published.Publication.ReadRuntimeResult(Published.Profile);
                    EditorGUILayout.LabelField("Last recorded result for this bundle and module",
                        Published.RecordedResult.ToString());
                }
            }
            catch (Exception Failure) when (Failure is IOException || Failure is UnauthorizedAccessException
                || Failure is SecurityException || Failure is ArgumentException || Failure is BadImageFormatException
                || Failure is InvalidOperationException
                || Failure is AggregateException)
            {
                EditorUtility.DisplayDialog("PLL game profile", Failure.Message, "Close");
            }
            if (!string.IsNullOrEmpty(Published.InstalledBundlePath))
                EditorGUILayout.LabelField("Installed bundle (recorded)", Published.InstalledBundlePath);
            EditorGUILayout.HelpBox("Checks the selected runtime DLL and BepInEx version, not game compatibility, dependencies or other mods. "
                + "Installation keeps the build report unchanged. Recorded results identify the bundle hash and runtime module ID, not the full DLL hash. "
                + "Sealed records a completed compatibility check; it does not confirm gameplay. Close PEAK before reading its log.", MessageType.Info);
        }

        internal static void SaveOpenPrefab(PllAuthoringFailure Failure)
        {
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (Stage == null || Stage.assetPath != Failure.UnsavedPrefabPath
                || Stage.prefabContentsRoot != Failure.Context)
                throw new PllAuthoringFailure(
                    "The source prefab is no longer open. Open it before saving.", Failure.Context);
            if (!EditorApplication.ExecuteMenuItem("File/Save") || Stage.scene.isDirty)
                throw new PllAuthoringFailure(
                    "The source prefab remains unsaved. Use File > Save in Prefab Mode.", Stage.prefabContentsRoot,
                    UnsavedPrefabPath: Stage.assetPath);
        }
    }

}
