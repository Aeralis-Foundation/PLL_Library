using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Photon.Pun;
using PLL.Library;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Object = UnityEngine.Object;
using static PLL.Creator.Editor.PllBundleBuilder;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal sealed class PllBuildArtifacts
    {
        static readonly Lazy<byte[]> FogComposition = new Lazy<byte[]>(() =>
            ReadSdkShaderSource("PLL.Library.Fog.PllFogComposition.hlsl"));
        static readonly Lazy<byte[]> LayeredShader = new Lazy<byte[]>(() =>
            ReadSdkShaderSource("PLL.Library.Surface.PllLayeredSurface.shader"));
        static readonly Lazy<byte[]> LayeredInclude = new Lazy<byte[]>(() =>
            ReadSdkShaderSource("PLL.Library.Surface.PllLayeredSurface.hlsl"));

        static byte[] ReadSdkShaderSource(string Resource)
        {
            using (var Source = typeof(PllBiomeDefinition).Assembly.GetManifestResourceStream(Resource))
            {
                if (Source == null) throw new InvalidOperationException("The Creator SDK shader source '" + Resource + "' is missing.");
                using (var Content = new MemoryStream()) { Source.CopyTo(Content); return Content.ToArray(); }
            }
        }
        readonly PllCreatorInstallation Installation;

        internal PllBuildArtifacts(PllCreatorInstallation Installation)
        {
            this.Installation = Installation;
        }

        internal static PllFogShaderAdapter PrepareFogShaderAdapter(PllBiomeDefinition Definition, Shader OriginalShader)
        {
            if (!Definition || !EditorUtility.IsPersistent(Definition))
                throw new ArgumentException("Fog shader preparation requires an imported biome definition.", nameof(Definition));
            RequireFogShaderCompiler();
            var OriginalPath = ImportedShaderPath(OriginalShader, true);
            var OriginalFile = ShaderSourcePath(OriginalPath);
            var Original = File.ReadAllBytes(OriginalFile);
            if (Original.AsSpan().SequenceEqual(LayeredShader.Value))
            {
                var LayeredInput = LayeredFogShaderInput(OriginalFile, Original);
                AssetDatabase.ImportAsset(OriginalPath, ImportAssetOptions.ForceSynchronousImport);
                var Direct = PllFogShaderAdapter.FromPreparedLayeredContent(
                    OriginalShader, LayeredFogShaderReceipt(OriginalShader, LayeredInput));
                Definition.SetPreparedFogShaderAdapter(Direct);
                EditorUtility.SetDirty(Definition); AssetDatabase.SaveAssetIfDirty(Definition);
                return Direct;
            }
            if (!OriginalPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Wrapped fog shaders must be imported ShaderLab source assets in Assets.", nameof(OriginalShader));
            var Generated = CreateFogShaderSource(OriginalShader, Original, out var InputVersion);
            var ContentHash = Hash(Generated);
            var Identity = Hex(ContentHash);
            var Path = "Assets/PLLGenerated/Fog/" + Identity + ".shader";
            var Meta = PllCreatorFileSystem.StrictUtf8.GetBytes("fileFormatVersion: 2\nguid: " + Identity.Substring(0, 32)
                + "\nShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            WriteOwnedAsset(Path, Generated);
            if (!File.Exists(Path + ".meta")) WriteOwnedAsset(Path + ".meta", Meta);
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);
            var FogShader = AssetDatabase.LoadAssetAtPath<Shader>(Path);
            if (!FogShader || !FogShader.isSupported || ShaderUtil.ShaderHasError(FogShader))
                throw new InvalidOperationException("The fog shader derivative did not compile.");
            var Adapter = PllFogShaderAdapter.FromPreparedContent(
                OriginalShader, FogShader, FogShaderReceipt(OriginalShader, FogShader, InputVersion, ContentHash));
            Definition.SetPreparedFogShaderAdapter(Adapter);
            EditorUtility.SetDirty(Definition); AssetDatabase.SaveAssetIfDirty(Definition);
            return Adapter;

            void WriteOwnedAsset(string AssetPath, byte[] Content)
            {
                if (File.Exists(AssetPath))
                {
                    if (!File.ReadAllBytes(AssetPath).AsSpan().SequenceEqual(Content))
                        throw new InvalidOperationException("Generated fog shader asset '" + AssetPath + "' has changed.");
                    return;
                }
                using (var Output = new FileStream(AssetPath, FileMode.CreateNew, FileAccess.Write))
                    Output.Write(Content, 0, Content.Length);
            }
        }

        static void RequireFogShaderCompiler()
        {
            if (Application.unityVersion != "6000.3.15f1"
                || UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
                    "Packages/com.unity.render-pipelines.universal/package.json")?.version != "17.3.0"
                || SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
                throw new InvalidOperationException("Custom fog shader preparation requires Unity 6000.3.15f1, URP 17.3.0 and Direct3D11.");
        }

        static byte[] CreateFogShaderSource(Shader OriginalShader, byte[] Original, out byte[] InputVersion)
        {
            var Source = PllCreatorFileSystem.StrictUtf8.GetString(Original);
            if (!Regex.IsMatch(Source,
                    @"\AShader ""(?:[^""\\]|\\.)*""\s*\{\s*Properties\s*\{(?:[^{}""/]|""(?:[^""\\]|\\.)*"")*\}\s*"
                    + @"SubShader\s*\{\s*Tags\s*\{\s*""Queue""\s*=\s*""Transparent""\s*"
                    + @"""RenderPipeline""\s*=\s*""UniversalPipeline""\s*""RenderType""\s*=\s*""Transparent""\s*\}\s*"
                    + @"Pass\s*\{\s*Blend\s+SrcAlpha\s+OneMinusSrcAlpha\s+Cull\s+Off\s+ZWrite\s+Off\s+"
                    + @"HLSLPROGRAM[\s\S]*ENDHLSL\s*\}\s*\}\s*\}\s*\z", RegexOptions.CultureInvariant)
                || !Source.Contains("float4 PositionHCS : SV_POSITION;")
                || !Source.Contains("half4 Frag(Varyings Input) : SV_Target") || Source.Contains("PllFog"))
                throw new ArgumentException("The shader does not expose the supported vertex-color alpha fragment contract.", nameof(OriginalShader));
            using (var Content = new MemoryStream())
            {
                using (var Writer = new BinaryWriter(Content, PllCreatorFileSystem.StrictUtf8, true))
                {
                    Writer.Write("PLL/CustomFogSourceWrapper/v1"); Writer.Write(Application.unityVersion);
                    Writer.Write("URP17.3.0/StandaloneWindows64/Direct3D11/SM5");
                    Writer.Write(Original.Length); Writer.Write(Original);
                    Writer.Write(FogComposition.Value.Length); Writer.Write(FogComposition.Value);
                }
                InputVersion = Hash(Content.GetBuffer(), checked((int)Content.Length));
            }
            var EndName = Source.IndexOf('"', "Shader \"".Length);
            if (EndName < 0) throw new ArgumentException("The shader declaration has no closing quote.", nameof(OriginalShader));
            Source = "Shader \"PLL/Fog/Custom/" + Hex(InputVersion) + Source.Substring(EndName);
            Source = ReplaceUnique(Source, "#pragma target 2.0", "#pragma target 5.0\n            #pragma only_renderers d3d11");
            Source = ReplaceUnique(Source, "#pragma fragment Frag", "#pragma fragment PllFogFrag");
            Source = ReplaceUnique(Source, "            ENDHLSL", "            #define PLL_FOG_BLEND_MODE 0\n"
                + PllCreatorFileSystem.StrictUtf8.GetString(FogComposition.Value)
                + "\n            half4 PllFogFrag(Varyings Input) : SV_Target\n            {\n"
                + "                return PllFogApply(Input.PositionHCS, Frag(Input));\n            }\n            ENDHLSL");
            return PllCreatorFileSystem.StrictUtf8.GetBytes(Source);

            string ReplaceUnique(string Value, string Token, string Replacement)
            {
                var Index = Value.IndexOf(Token, StringComparison.Ordinal);
                if (Index < 0 || Value.IndexOf(Token, Index + Token.Length, StringComparison.Ordinal) >= 0)
                    throw new ArgumentException("The shader does not expose one supported fragment program.", nameof(OriginalShader));
                return Value.Substring(0, Index) + Replacement + Value.Substring(Index + Token.Length);
            }
        }

        static byte[] LayeredFogShaderInput(string Path, byte[] Source)
        {
            if (!Source.AsSpan().SequenceEqual(LayeredShader.Value)
                || !File.ReadAllBytes(System.IO.Path.GetDirectoryName(Path) + "/PllLayeredSurface.hlsl")
                    .AsSpan().SequenceEqual(LayeredInclude.Value)
                || !File.ReadAllBytes(System.IO.Path.GetDirectoryName(Path) + "/PllFogComposition.hlsl")
                    .AsSpan().SequenceEqual(FogComposition.Value))
                throw new InvalidOperationException("The layered shader source differs from the Creator SDK.");
            using (var Content = new MemoryStream())
            {
                using (var Writer = new BinaryWriter(Content, PllCreatorFileSystem.StrictUtf8, true))
                {
                    Writer.Write("PLL/LayeredFogSource/v1"); Writer.Write(Application.unityVersion);
                    Writer.Write("URP17.3.0/StandaloneWindows64/Direct3D11/SM5");
                    Writer.Write(Source.Length); Writer.Write(Source);
                    Writer.Write(LayeredInclude.Value.Length); Writer.Write(LayeredInclude.Value);
                    Writer.Write(FogComposition.Value.Length); Writer.Write(FogComposition.Value);
                }
                return Hash(Content.GetBuffer(), checked((int)Content.Length));
            }
        }

        static byte[] LayeredFogShaderReceipt(Shader Shader, byte[] InputVersion)
        {
            if (!Shader.isSupported || ShaderUtil.ShaderHasError(Shader))
                throw new InvalidOperationException("The layered fog shader did not compile.");
            return FogShaderReceipt(Shader, Shader, InputVersion, Hash(LayeredShader.Value), PllFogShaderAdapterKind.LayeredDirect);
        }

        static byte[] FogShaderReceipt(Shader OriginalShader, Shader FogShader, byte[] InputVersion,
            byte[] ContentHash, PllFogShaderAdapterKind Kind = PllFogShaderAdapterKind.Wrapped)
        {
            using (var Content = new MemoryStream())
            {
                using (var Writer = new BinaryWriter(Content, PllCreatorFileSystem.StrictUtf8, true))
                {
                    Writer.Write(Kind == PllFogShaderAdapterKind.Wrapped
                        ? "PLL/CustomFogPreparedContent/v1" : "PLL/LayeredFogPreparedContent/v1");
                    Writer.Write(InputVersion); Writer.Write(ContentHash);
                    WriteShader(OriginalShader); WriteShader(FogShader);

                    void WriteShader(Shader Value)
                    {
                        var Path = ImportedShaderPath(Value, Kind == PllFogShaderAdapterKind.LayeredDirect);
                        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(Value, out var Guid, out long LocalId))
                            throw new InvalidOperationException("A fog shader has no imported asset identity.");
                        if (Kind == PllFogShaderAdapterKind.Wrapped
                            && Value == FogShader && Guid != Hex(ContentHash).Substring(0, 32))
                            throw new InvalidOperationException("The fog shader asset identity does not match its content.");
                        Writer.Write(Guid); Writer.Write(LocalId);
                        Writer.Write(AssetDatabase.GetAssetDependencyHash(Path).ToString());
                    }
                }
                return Hash(Content.GetBuffer(), checked((int)Content.Length));
            }
        }

        static string ImportedShaderPath(Shader Value, bool AllowPackage = false)
        {
            var Path = AssetDatabase.GetAssetPath(Value);
            if (!Value || !EditorUtility.IsPersistent(Value)
                || !(Path.StartsWith("Assets/", StringComparison.Ordinal)
                    || AllowPackage && Path.StartsWith("Packages/", StringComparison.Ordinal))
                || !Path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Custom fog shaders must be imported ShaderLab source assets.");
            return Path;
        }

        static string ShaderSourcePath(string AssetPath)
        {
            if (!AssetPath.StartsWith("Packages/", StringComparison.Ordinal)) return AssetPath;
            var Package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(AssetPath);
            if (Package == null || !AssetPath.StartsWith(Package.assetPath + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("The shader package source is unavailable.");
            return System.IO.Path.Combine(Package.resolvedPath, AssetPath.Substring(Package.assetPath.Length + 1));
        }

        static byte[] Hash(byte[] Content, int? Length = null)
        {
            using (var Sha = SHA256.Create()) return Sha.ComputeHash(Content, 0, Length ?? Content.Length);
        }

        static string Hex(byte[] Content) => BitConverter.ToString(Content).Replace("-", "").ToLowerInvariant();

        static void ValidateFogShaderAdapters(PllBiomeDefinition Definition,
            Dictionary<Shader, (string Path, PllFogShaderAdapter Adapter)> LayeredAdapters)
        {
            if (Definition.FogShaderAdapters.Count == 0) return;
            RequireFogShaderCompiler();
            foreach (var Adapter in Definition.FogShaderAdapters)
            {
                if (Adapter == null) throw new InvalidOperationException("A prepared fog shader adapter is missing.");
                if (Adapter.Kind == PllFogShaderAdapterKind.LayeredDirect)
                {
                    if (!Adapter.OriginalShader || Adapter.FogShader != Adapter.OriginalShader)
                        throw new InvalidOperationException("The direct fog shader adapter has no matching shader.");
                    if (!LayeredAdapters.TryGetValue(Adapter.OriginalShader, out var Prepared))
                    {
                        var Path = ImportedShaderPath(Adapter.OriginalShader, true);
                        var SourcePath = ShaderSourcePath(Path);
                        var Receipt = LayeredFogShaderReceipt(Adapter.OriginalShader,
                            LayeredFogShaderInput(SourcePath, File.ReadAllBytes(SourcePath)));
                        if (!Adapter.PreparedContent.SequenceEqual(Receipt))
                            throw new InvalidOperationException("The fog shader preparation has changed. Prepare the shader again.");
                        Prepared = (Path, Adapter); LayeredAdapters.Add(Adapter.OriginalShader, Prepared);
                    }
                    else if (!Adapter.PreparedContent.SequenceEqual(Prepared.Adapter.PreparedContent))
                        throw new InvalidOperationException("The fog shader preparation has changed. Prepare the shader again.");
                    continue;
                }
                if (Adapter.Kind != PllFogShaderAdapterKind.Wrapped)
                    throw new InvalidOperationException("The fog shader adapter kind is unsupported.");
                var Generated = CreateFogShaderSource(Adapter.OriginalShader,
                    File.ReadAllBytes(ImportedShaderPath(Adapter.OriginalShader)), out var InputVersion);
                if (!File.ReadAllBytes(ImportedShaderPath(Adapter.FogShader)).AsSpan().SequenceEqual(Generated)
                    || !Adapter.PreparedContent.SequenceEqual(
                        FogShaderReceipt(Adapter.OriginalShader, Adapter.FogShader, InputVersion, Hash(Generated))))
                    throw new InvalidOperationException("The fog shader preparation has changed. Prepare the shader again.");
            }
        }

        static PllFogShaderAdapter[] PrepareBuildFogShaderAdapters(PllBiomeDefinition Source, DefinitionCatalog Catalog,
            Dictionary<Shader, (string Path, PllFogShaderAdapter Adapter)> LayeredAdapters)
        {
            var Adapters = new List<PllFogShaderAdapter>(Source.FogShaderAdapters);
            var Seen = new HashSet<Shader>(Adapters.Select(Value => Value.OriginalShader));
            var Added = new List<(string Path, PllFogShaderAdapter Adapter)>();
            foreach (var Surface in Catalog.SurfaceTextures) Add(Surface.Material, true);
            foreach (var Resolver in Catalog.NativeResolvers)
            {
                if (Resolver.FluidDefinition)
                {
                    Add(Resolver.FluidDefinition.ZoneMaterial); Add(Resolver.FluidDefinition.FlowMaterial);
                }
                Add(Resolver.SourceSettings.Material); Add(Resolver.GeneratedSurfaceMaterial);
                if (Resolver.SurfaceRenderer) Add(Resolver.SurfaceRenderer.sharedMaterial);
            }
            Added.Sort((Left, Right) => string.CompareOrdinal(Left.Path, Right.Path));
            foreach (var Prepared in Added) Adapters.Add(Prepared.Adapter);
            return Adapters.ToArray();

            void Add(Material Material, bool Required = false)
            {
                if (!Material) return;
                var Shader = Material.shader;
                if (!Seen.Add(Shader)) return;
                if (!LayeredAdapters.TryGetValue(Shader, out var Prepared))
                {
                    var Path = AssetDatabase.GetAssetPath(Shader);
                    if (!Required && (!Path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                            || !(Path.StartsWith("Assets/", StringComparison.Ordinal) || Path.StartsWith("Packages/", StringComparison.Ordinal)))) return;
                    Path = ImportedShaderPath(Shader, true);
                    var SourcePath = ShaderSourcePath(Path);
                    var Content = File.ReadAllBytes(SourcePath);
                    if (!Required && !Content.AsSpan().SequenceEqual(LayeredShader.Value)) return;
                    RequireFogShaderCompiler();
                    var InputVersion = LayeredFogShaderInput(SourcePath, Content);
                    AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);
                    Prepared = (Path, PllFogShaderAdapter.FromPreparedLayeredContent(Shader,
                        LayeredFogShaderReceipt(Shader, InputVersion)));
                    LayeredAdapters.Add(Shader, Prepared);
                }
                Added.Add(Prepared);
            }
        }

        static MountedToolInsertionRecipe[] CaptureMountedToolInsertions(DefinitionCatalog Catalog)
        {
            var Candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var RestPoint in Catalog.RestPoints)
            {
                if (RestPoint.Placement != PllRestPointPlacement.GeneratedTerrain) continue;
                var Node = OwnerNode(Catalog.Graph, RestPoint, "terrain mounted tool");
                if (Node.IsRoot || RestPoint.gameObject != Node.SourceRoot)
                    throw new InvalidOperationException("Terrain mounted tool must occupy an entire selected candidate root.");
                Candidates.Add(Node.SourcePath);
            }
            if (Candidates.Count == 0) return Array.Empty<MountedToolInsertionRecipe>();
            var Spawners = new List<(PrefabNode Node, PropSpawner Spawner)>();
            var Seen = new HashSet<PropSpawner>();
            var ImmediateChildren = new HashSet<PropSpawner>();
            foreach (var Node in Catalog.Graph.Nodes)
                foreach (var Edge in Node.Edges)
                {
                    if (Edge.Spawner && Catalog.Graph.ByPath[Edge.TargetPath].SourceRoot
                            .GetComponent<PllChildPropAuthoring>() is { Timing: PllChildPropTiming.Immediate })
                        ImmediateChildren.Add(Edge.Spawner);
                    if (!Candidates.Contains(Edge.TargetPath)) continue;
                    if (!Edge.Spawner || Edge.ChildProps || Edge.ActorZone || !Edge.Spawner.raycastPosition)
                        throw new InvalidOperationException("Terrain mounted tool needs a raycast PropSpawner for every selection.");
                    if (Seen.Add(Edge.Spawner)) Spawners.Add((Node, Edge.Spawner));
                }
            var Recipes = new MountedToolInsertionRecipe[Spawners.Count];
            for (var Index = 0; Index < Recipes.Length; Index++)
                Recipes[Index] = new MountedToolInsertionRecipe(Spawners[Index].Node, Spawners[Index].Spawner,
                    ImmediateChildren.Contains(Spawners[Index].Spawner));
            return Recipes;
        }

        internal StagedBuild StageDefinitions(
            ProjectStage Stage, IReadOnlyList<PllBiomeDefinition> SourceDefinitions,
            IReadOnlyList<DefinitionCatalog> Catalogs)
        {
            var Prepared = new StagedDefinition[SourceDefinitions.Count];
            var Definitions = new CompiledDefinition[SourceDefinitions.Count];
            var StagedPathSets = new IReadOnlyDictionary<string, string>[
                SourceDefinitions.Count];
            var StagedBySource = new Dictionary<int, StagedDefinition>(
                SourceDefinitions.Count);
            var Translations = new List<BuildTranslationData>();
            var FluidDefinitions = new Dictionary<PllFluidDefinition, PllFluidDefinition>();
            var LayeredAdapters = new Dictionary<Shader, (string Path, PllFogShaderAdapter Adapter)>();
            var PreparedFogAdapters = new PllFogShaderAdapter[SourceDefinitions.Count][];
            var MountedToolInsertions = new MountedToolInsertionRecipe[SourceDefinitions.Count][];
            var CurvatureBaker = new PllSurfaceCurvatureBaker(Stage.RootAssetPath);
            var PropCompiler = new PllPropInteractionCompiler(Stage.RootAssetPath);
            var MeshRecipes = new PreparedMeshRecipeCatalog(Catalogs);
            for (var Index = 0; Index < SourceDefinitions.Count; Index++)
            {
                var Source = SourceDefinitions[Index];
                MountedToolInsertions[Index] = CaptureMountedToolInsertions(Catalogs[Index]);
                ValidateFogShaderAdapters(Source, LayeredAdapters);
                PreparedFogAdapters[Index] = PrepareBuildFogShaderAdapters(Source, Catalogs[Index], LayeredAdapters);
                foreach (var Resolver in Catalogs[Index].NativeResolvers)
                {
                    var Fluid = Resolver.FluidDefinition;
                    if (!Fluid || FluidDefinitions.ContainsKey(Fluid)) continue;
                    var PreparedFluid = Object.Instantiate(Fluid);
                    try
                    {
                        PllBundleSerialization.SealFluidDefinition(PreparedFluid);
                        AssetDatabase.CreateAsset(PreparedFluid, Stage.RootAssetPath + "/fluid-"
                            + FluidDefinitions.Count.ToString("D4", CultureInfo.InvariantCulture) + ".asset");
                        FluidDefinitions.Add(Fluid, PreparedFluid);
                    }
                    finally
                    {
                        if (!EditorUtility.IsPersistent(PreparedFluid)) Object.DestroyImmediate(PreparedFluid);
                    }
                }
                var Prefix = Index.ToString("D4", CultureInfo.InvariantCulture);
                var DefinitionPath = Stage.RootAssetPath + "/" + Prefix
                    + "-definition.asset";
                var PrefabPath = Stage.RootAssetPath + "/" + Prefix
                    + "-root.prefab";
                var Clone = Object.Instantiate(Source);
                for (var AdapterIndex = Source.FogShaderAdapters.Count; AdapterIndex < PreparedFogAdapters[Index].Length; AdapterIndex++)
                    Clone.SetPreparedFogShaderAdapter(PreparedFogAdapters[Index][AdapterIndex]);
                AssetDatabase.CreateAsset(Clone, DefinitionPath);
                var StagedPaths = CopyPrefabGraph(
                    Stage, Prefix, PrefabPath, Catalogs[Index].Graph);
                var Definition = new StagedDefinition(
                    Clone, DefinitionPath, PrefabPath);
                Prepared[Index] = Definition;
                StagedPathSets[Index] = StagedPaths;
                StagedBySource.Add(Source.GetInstanceID(), Definition);
            }

            for (var Index = 0; Index < SourceDefinitions.Count; Index++)
            {
                var Source = SourceDefinitions[Index];
                var Definition = Prepared[Index];
                var Clone = Definition.Staged;
                var Prefix = Index.ToString("D4", CultureInfo.InvariantCulture);
                var StagedPaths = StagedPathSets[Index];
                var Translator = new PllStagedFeatureTranslator(
                    Stage, Prefix, Catalogs[Index], StagedPaths,
                    FluidDefinitions, CurvatureBaker, PropCompiler, MountedToolInsertions[Index]);
                foreach (var Node in Catalogs[Index].Graph.Nodes)
                {
                    var StagedPath = StagedPaths[Node.SourcePath];
                    var Contents = PrefabUtility.LoadPrefabContents(StagedPath);
                    try
                    {
                        Translator.Translate(Node, Contents);
                        if (Node.IsRoot)
                        {
                            foreach (var Link in Contents.GetComponentsInChildren<
                                         PllBiomeRoot>(true))
                            {
                                var SerializedLink = new SerializedObject(Link);
                                Property(SerializedLink, "definition")
                                    .objectReferenceValue = Clone;
                                SerializedLink.ApplyModifiedPropertiesWithoutUndo();
                            }
                            RewriteBiomeGroup(
                                Source.RootPrefab, Contents,
                                Catalogs[Index].Group, StagedBySource);
                        }
                        if (!PrefabUtility.SaveAsPrefabAsset(
                                Contents, StagedPath))
                            throw new InvalidOperationException(
                                "Unity could not save staged prefab '"
                                + StagedPath + "'.");
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(Contents);
                    }
                }

                var StagedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(
                    Definition.PrefabPath);
                if (!StagedRoot)
                    throw new InvalidOperationException(
                        "Unity could not reload staged root prefab '"
                        + Definition.PrefabPath + "'.");
                var Serialized = new SerializedObject(Clone);
                Property(Serialized, "rootPrefab").objectReferenceValue =
                    StagedRoot;
                Property(Serialized, "anchors").objectReferenceValue =
                    MapComponent(
                        Source.RootPrefab, StagedRoot, Source.Anchors,
                        "anchors");
                MapGraphComponentArray(
                    Serialized, "visuals", Catalogs[Index].Graph,
                    StagedPaths, Catalogs[Index].Visuals);
                MapGraphComponentArray(
                    Serialized, "effectDefinitions", Catalogs[Index].Graph,
                    StagedPaths, Catalogs[Index].Effects);
                MapGraphComponentArray(
                    Serialized, "luggageDefinitions", Catalogs[Index].Graph,
                    StagedPaths, Catalogs[Index].Luggage);
                MapComponentArray(
                    Serialized, "variationDefinitions", Source.RootPrefab,
                    StagedRoot, Catalogs[Index].Variations);
                MapGraphComponentArray(
                    Serialized, "soundEmitters", Catalogs[Index].Graph,
                    StagedPaths, Catalogs[Index].SoundEmitters);
                MapComponentArray(
                    Serialized, "stingerDefinitions", Source.RootPrefab,
                    StagedRoot, Catalogs[Index].Stingers);
                Serialized.ApplyModifiedPropertiesWithoutUndo();
                var GeneratedCatalog = Catalogs[Index].HookedGeneration.Seal(Clone, StagedPaths);
                Clone.SealBundleIdentity();
                EditorUtility.SetDirty(Clone);
                AssetDatabase.SaveAssetIfDirty(Clone);
                var SemanticIdentity = DefinitionSemanticIdentity.Capture(
                    Source, Catalogs[Index], StagedPaths, FluidDefinitions, MeshRecipes,
                    PreparedFogAdapters[Index], MountedToolInsertions[Index]);
                var Compiled = new CompiledDefinition(Definition, SemanticIdentity,
                    SemanticIdentity.CaptureGeneratedCatalog(
                        GeneratedCatalog, Catalogs[Index].Graph, StagedPaths));
                Compiled.Validate(Clone, "Staged biome definition");
                Definitions[Index] = Compiled;
                AddTranslations(
                    Translations, Stage, Source,
                    Definition.DefinitionPath, Catalogs[Index], StagedPaths,
                    StagedBySource, FluidDefinitions);
            }

            var StagedDefinitions = new PllBiomeDefinition[Definitions.Length];
            var StagedRoots = new List<GameObject>();
            for (var Index = 0; Index < Definitions.Length; Index++)
            {
                StagedDefinitions[Index] = Definitions[Index].Staged;
                foreach (var Node in Catalogs[Index].Graph.Nodes)
                {
                    var StagedPath = StagedPathSets[Index][Node.SourcePath];
                    var Root = AssetDatabase.LoadAssetAtPath<GameObject>(
                        StagedPath);
                    if (!Root)
                        throw new InvalidOperationException(
                            "Unity could not reload staged prefab '"
                            + StagedPath + "'.");
                    StagedRoots.Add(Root);
                }
            }
            PllSourceCatalogValidator.ValidateScope(
                StagedDefinitions, StagedRoots);
            return new StagedBuild(
                Definitions,
                Translations.OrderBy(
                        Value => Value.definition, StringComparer.Ordinal)
                    .ThenBy(
                        Value => Value.sourceAsset, StringComparer.Ordinal)
                    .ThenBy(
                        Value => Value.sourcePath, StringComparer.Ordinal)
                    .ThenBy(
                        Value => Value.sourceObject, StringComparer.Ordinal)
                    .ThenBy(
                        Value => Value.component, StringComparer.Ordinal)
                    .ThenBy(
                        Value => Value.field, StringComparer.Ordinal)
                    .ThenBy(
                        Value => Value.stagedAsset, StringComparer.Ordinal)
                    .ToArray());
        }

        static void RewriteBiomeGroup(
            GameObject SourceRoot, GameObject StagedRoot,
            PllBiomeGroupContract Group,
            IReadOnlyDictionary<int, StagedDefinition> StagedBySource)
        {
            if (Group == null) return;
            var StagedCarrier = MapComponent(
                SourceRoot, StagedRoot, Group.Carrier, "biomeGroup");
            var Serialized = new SerializedObject(StagedCarrier);
            var Prefixes = Property(Serialized, "PrefixesValue");
            Prefixes.arraySize = Group.Prefixes.Count;
            for (var Index = 0; Index < Group.Prefixes.Count; Index++)
                Prefixes.GetArrayElementAtIndex(Index).objectReferenceValue =
                    StagedBySource[Group.Prefixes[Index].GetInstanceID()]
                        .Staged;
            Serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddTranslations(
            ICollection<BuildTranslationData> Result, ProjectStage Stage,
            PllBiomeDefinition Definition, string DefinitionPath,
            DefinitionCatalog Catalog,
            IReadOnlyDictionary<string, string> StagedPaths,
            IReadOnlyDictionary<int, StagedDefinition> StagedBySource,
            IReadOnlyDictionary<PllFluidDefinition, PllFluidDefinition> FluidDefinitions)
        {
            var BiomeId = Definition.BiomeId.Value;
            var DefinitionAsset = AssetDatabase.GetAssetPath(Definition);
            Result.Add(new BuildTranslationData(BiomeId, DefinitionAsset, Definition.name,
                DefinitionAsset, typeof(PllBiomeDefinition).FullName, "definition",
                Stage.ReportAssetPath(DefinitionPath), "cloned PllBiomeDefinition"));
            foreach (var Node in Catalog.Graph.Nodes)
                Result.Add(new BuildTranslationData(BiomeId, Node.SourcePath, Node.SourceRoot.name,
                    Node.SourcePath + "#.", typeof(GameObject).FullName,
                    Node.IsRoot ? "rootPrefab" : "candidatePrefab",
                    Stage.ReportAssetPath(StagedPaths[Node.SourcePath]),
                    Node.IsRoot ? "cloned root prefab" : "cloned candidate prefab"));
            foreach (var Geometry in Catalog.CollisionGeometry)
                Result.Add(new BuildTranslationData(BiomeId,
                    Geometry.Node.SourcePath,
                    PllHierarchy.NamePath(Geometry.Node.SourceRoot.transform, Geometry.Target),
                    CanonicalComponentPath(Geometry.Node, Geometry.Target),
                    typeof(Transform).FullName, "collision",
                    Stage.ReportAssetPath(StagedPaths[Geometry.Node.SourcePath]),
                    Geometry.Outcome));
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths,
                Catalog.ClimbSurfaces, "climbSurfaces");
            if (Catalog.ClimbSurfaceCatalog.MaterialAuthoring)
            {
                var MaterialNode = OwnerNode(
                    Catalog.Graph, Catalog.ClimbSurfaceCatalog.MaterialAuthoring,
                    "climb surface material authoring");
                Result.Add(new BuildTranslationData(
                    BiomeId, MaterialNode.SourcePath,
                    PllHierarchy.NamePath(
                        MaterialNode.SourceRoot.transform,
                        Catalog.ClimbSurfaceCatalog.MaterialAuthoring.transform),
                    CanonicalComponentPath(
                        MaterialNode, Catalog.ClimbSurfaceCatalog.MaterialAuthoring),
                    typeof(PllClimbSurfaceMaterialAuthoring).FullName,
                    "climbSurfaceMaterial",
                    Stage.ReportAssetPath(StagedPaths[MaterialNode.SourcePath]),
                    TranslationOutcome(
                        Catalog.ClimbSurfaceCatalog.MaterialAuthoring)));
            }
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.Visuals, "visuals");
            AddCustomVisualTranslations(
                Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.CustomVisuals, "customVisuals");
            if (Catalog.Atmosphere)
            {
                var AtmosphereNode = OwnerNode(Catalog.Graph, Catalog.Atmosphere, "atmosphere");
                Result.Add(new BuildTranslationData(
                    BiomeId, AtmosphereNode.SourcePath,
                    PllHierarchy.NamePath(AtmosphereNode.SourceRoot.transform, Catalog.Atmosphere.transform),
                    CanonicalComponentPath(AtmosphereNode, Catalog.Atmosphere),
                    typeof(PllAtmosphereAuthoring).FullName, "atmosphere",
                    Stage.ReportAssetPath(StagedPaths[AtmosphereNode.SourcePath]),
                    TranslationOutcome(Catalog.Atmosphere)));
            }
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.Effects, "effects");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.EffectZones, "effectZones");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.FogZones, "fogZones");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.HookedGeneration.Sources, "hookedPropGeneration");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.EntityZones, "entityZones");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.HazardZones, "hazardZones");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.Hazards, "hazards");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.Entities, "entities");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.AmuletSpawners, "amuletSpawners");
            AddFeatureTranslations(
                Result, Stage, BiomeId, Catalog.Graph,
                StagedPaths, Catalog.Luggage, "luggage");
            AddLuggageTranslations(
                Result, Stage, BiomeId, Catalog.Graph,
                StagedPaths, Catalog.Luggage);
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.Campfires, "campfires");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.Statues, "statues");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.RestPoints, "restPoints");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.HangingRopes, "hangingRopes");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.SurfaceTextures, "surfaceTextures");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.BreakableProps, "breakableProps");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.SwingingProps, "swingingProps");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.Vines, "vines");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.Variations, "variations");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.SoundEmitters, "soundEmitters");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.Stingers, "stingerDefinitions");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.NativeResolvers, "nativeResolvers");
            foreach (var Resolver in Catalog.NativeResolvers)
            {
                if (!Resolver.FluidDefinition) continue;
                var Node = OwnerNode(Catalog.Graph, Resolver, "Fluid Definition");
                Result.Add(new BuildTranslationData(BiomeId, Node.SourcePath,
                    PllHierarchy.NamePath(Node.SourceRoot.transform, Resolver.transform),
                    CanonicalComponentPath(Node, Resolver), typeof(PLLNativeResolver).FullName,
                    "FluidDefinitionValue", Stage.ReportAssetPath(AssetDatabase.GetAssetPath(FluidDefinitions[Resolver.FluidDefinition])),
                    "cloned and sealed Fluid Definition; source asset unchanged"));
            }
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths,
                Catalog.NativeVisualResolvers, "nativeVisualResolvers");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.PropZones, "propZones");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.NoPropSpawn, "noPropSpawn");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.ChildProps, "childProps");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.ItemSpawners, "itemSpawners");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.NativeSpawners, "nativeSpawners");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.FinalGeneration, "finalGeneration");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.CriticalGeneration, "criticalGeneration");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths, Catalog.HookedProps, "hookedProps");
            AddFeatureTranslations(Result, Stage, BiomeId, Catalog.Graph, StagedPaths,
                DiscoverGraphComponents<PLLForceSpawner>(Catalog.Graph), "forceSpawners");
            if (Catalog.Group == null) return;
            var GroupNode = Catalog.Graph.ByPath[Catalog.Graph.RootPath];
            for (var Index = 0; Index < Catalog.Group.Prefixes.Count; Index++)
            {
                var PrefixDefinition = StagedBySource[
                    Catalog.Group.Prefixes[Index].GetInstanceID()];
                Result.Add(new BuildTranslationData(
                    BiomeId, GroupNode.SourcePath,
                    PllHierarchy.NamePath(
                        GroupNode.SourceRoot.transform,
                        Catalog.Group.Carrier.transform),
                    CanonicalComponentPath(
                        GroupNode, Catalog.Group.Carrier),
                    typeof(PllBiomeGroupAuthoring).FullName,
                    "PrefixesValue[" + Index.ToString(
                        "D4", CultureInfo.InvariantCulture) + "]",
                    Stage.ReportAssetPath(
                        StagedPaths[GroupNode.SourcePath]),
                    Stage.ReportAssetPath(
                        PrefixDefinition.DefinitionPath)));
            }
        }

        static void AddFeatureTranslations<T>(
            ICollection<BuildTranslationData> Result, ProjectStage Stage, string BiomeId, PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths, IReadOnlyList<T> Features, string Field) where T : Component
        {
            for (var i = 0; i < Features.Count; i++)
            {
                var Feature = Features[i];
                var Node = OwnerNode(Graph, Feature, Field + "[" + i + "]");
                Result.Add(new BuildTranslationData(BiomeId, Node.SourcePath,
                    PllHierarchy.NamePath(Node.SourceRoot.transform, Feature.transform), CanonicalComponentPath(Node, Feature),
                    Feature.GetType().FullName, Field + "[" + i.ToString(CultureInfo.InvariantCulture) + "]",
                    Stage.ReportAssetPath(StagedPaths[Node.SourcePath]), TranslationOutcome(Feature)));
            }
        }

        static void AddLuggageTranslations(
            ICollection<BuildTranslationData> Result,
            ProjectStage Stage, string BiomeId, PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            IReadOnlyList<PllLuggageAuthoring> Values)
        {
            for (var Index = 0; Index < Values.Count; Index++)
            {
                var Value = Values[Index];
                var Node = OwnerNode(
                    Graph, Value, "luggage[" + Index + "]");
                var SourceObject = PllHierarchy.NamePath(
                    Node.SourceRoot.transform, Value.transform);
                var SourcePath = CanonicalComponentPath(Node, Value);
                var StagedAsset = Stage.ReportAssetPath(
                    StagedPaths[Node.SourcePath]);
                var Prefix = "luggage["
                    + Index.ToString(CultureInfo.InvariantCulture) + "].";

                Add("ContentsModeValue", StagedAsset,
                    "retained logical contents mode; native and ItemDatabase resolution LIVE-UNVERIFIED");
                if (Value.NativeContentsId.Length != 0)
                    Add("NativeContentsIdValue", StagedAsset,
                        "retained native contents ID; native contents resolution LIVE-UNVERIFIED");
                if (Value.ReplacesNativeId.Length != 0)
                    Add("ReplacesNativeIdValue", StagedAsset,
                        "retained native replacement ID; native replacement resolution LIVE-UNVERIFIED");
                if (Value.AnimatorController)
                    Add("AnimatorControllerValue",
                        AssetDatabase.GetAssetPath(
                            Value.AnimatorController),
                        "retained authored AnimatorController");
                if (Value.ActivateOnOpen)
                    Add("ActivateOnOpenValue", StagedAsset,
                        "mapped ActivateOnOpen subtree");
                for (var GroupIndex = 0;
                    GroupIndex < Value.SpawnGroups.Count; GroupIndex++)
                {
                    var Group = Value.SpawnGroups[GroupIndex];
                    Add("SpawnGroupsValue["
                            + GroupIndex.ToString(
                                CultureInfo.InvariantCulture)
                            + "].WeightValue",
                        StagedAsset, "retained spawn-group weight");
                    for (var PointIndex = 0;
                        PointIndex < Group.Points.Count; PointIndex++)
                        Add("SpawnGroupsValue["
                                + GroupIndex.ToString(
                                    CultureInfo.InvariantCulture)
                                + "].PointsValue["
                                + PointIndex.ToString(
                                    CultureInfo.InvariantCulture) + "]",
                            StagedAsset,
                            "mapped spawn-point Transform");
                }
                for (var ItemIndex = 0;
                    ItemIndex < Value.CustomItems.Count; ItemIndex++)
                {
                    var Entry = Value.CustomItems[ItemIndex];
                    Add("CustomItemsValue["
                            + ItemIndex.ToString(
                                CultureInfo.InvariantCulture)
                            + "].ItemValue",
                        AssetDatabase.GetAssetPath(Entry.Item),
                        "retained item prefab; ItemDatabase identity LIVE-UNVERIFIED");
                    Add("CustomItemsValue["
                            + ItemIndex.ToString(
                                CultureInfo.InvariantCulture)
                            + "].WeightValue",
                        StagedAsset, "retained custom-item weight");
                }
                if (Value.ReplacesAuthored)
                {
                    var TargetNode = OwnerNode(
                        Graph, Value.ReplacesAuthored,
                        Prefix + "ReplacesAuthoredValue");
                    Add("ReplacesAuthoredValue", StagedAsset,
                        "mapped direct authored replacement "
                            + CanonicalComponentPath(
                                TargetNode,
                                Value.ReplacesAuthored));
                }

                void Add(
                    string Field, string TargetAsset,
                    string Outcome) => Result.Add(
                    new BuildTranslationData(
                        BiomeId, Node.SourcePath, SourceObject,
                        SourcePath,
                        typeof(PllLuggageAuthoring).FullName,
                        Prefix + Field, TargetAsset, Outcome));
            }
        }
        static void AddCustomVisualTranslations(
            ICollection<BuildTranslationData> Result, ProjectStage Stage, string BiomeId, PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            IReadOnlyList<CustomVisualParticleGraph> Features, string Field)
        {
            for (var i = 0; i < Features.Count; i++)
            {
                var Feature = Features[i].Authoring;
                var Node = OwnerNode(Graph, Feature, Field + "[" + i + "]");
                Result.Add(new BuildTranslationData(BiomeId, Node.SourcePath,
                    PllHierarchy.NamePath(Node.SourceRoot.transform, Feature.transform), CanonicalComponentPath(Node, Feature),
                    Feature.GetType().FullName, Field + "[" + i.ToString(CultureInfo.InvariantCulture) + "]",
                    Stage.ReportAssetPath(StagedPaths[Node.SourcePath]), TranslationOutcome(Feature)));
            }
        }

        static string CanonicalComponentPath(PrefabNode Node, Component Feature)
        {
            var Path = PllHierarchy.SiblingPath(Node.SourceRoot.transform, Feature.transform,
                "A staged component is not owned by its root prefab.");
            var Components = Feature.transform.GetComponents(Feature.GetType());
            var Component = Array.FindIndex(Components, Value => ReferenceEquals(Value, Feature));
            if (Component < 0) throw new InvalidOperationException("A staged feature is not owned by its source object.");
            var Transform = Path.Length == 0 ? "." : string.Join("/", Path.Select(Value =>
                Value.ToString(CultureInfo.InvariantCulture)));
            return Node.SourcePath + "#" + Transform + ":" + Component.ToString(CultureInfo.InvariantCulture);
        }

        static string TranslationOutcome(Component Feature)
        {
            if (Feature is PllClimbSurfaceAuthoring)
                return "retained inert PllClimbSurfaceAuthoring + neutral ClimbModifierSurface on the same Collider GameObject";
            if (Feature is PllClimbSurfaceMaterialAuthoring)
                return "retained inert material rules + neutral ClimbModifierSurface targets for exact shared PhysicsMaterial matches";
            if (Feature is PllCampfireAuthoring) return "Campfire + Photon.Pun.PhotonView";
            if (Feature is PllLuggageAuthoring) return "Luggage + Photon.Pun.PhotonView + Animator";
            if (Feature is PllStatueAuthoring) return "RespawnChest + Photon.Pun.PhotonView + Animator";
            if (Feature is PllRestPointAuthoring RestPoint)
                return RestPoint.Profile == PllRestPointProfile.Permanent
                    ? "permanent ClimbHandle + fixed PhotonView + admitted focused interaction bounds"
                    : "mounted-tool ClimbHandle + fixed PhotonView + disabled ShittyPiton; native recipe binding deferred";
            if (Feature is PllHangingRopeAuthoring) return "RopeAnchor + disabled callback source; native RopeDynamic creation after anchor-view admission";
            if (Feature is PllSurfaceTextureAuthoring Surface)
                return Surface.Target == PllSurfaceTarget.GeneratedFluidSurface
                    ? "Surface Textures profile retained for generated fluid surface; runtime geometry binding deferred"
                    : "Surface Textures renderer slots retained; automatic curvature meshes derived and sealed when required";
            if (Feature is PllBreakablePropAuthoring) return "Breakable Prop recipe retained; runtime authority and native binding deferred";
            if (Feature is PllSwingingPropAuthoring) return "Swinging Prop recipe retained; runtime motion and authority binding deferred";
            if (Feature is PllVineAuthoring Vine)
                return Vine.Appearance == PllVineAppearance.CustomSpan
                    ? "normalized custom span mesh sealed; native Vine / Hanging Bridge binding deferred"
                    : "native Vine / Hanging Bridge recipe retained; native endpoint and curve binding deferred";
            if (Feature is PllVariationAuthoring) return "VariantObject + VariantObjectSelector";
            if (Feature is PllEffectAuthoring)
                return "presentation inactive; particles playOnAwake=false and stopped/cleared";
            if (Feature is PllHookedPropGenerationAuthoring Generation)
                return "stable Hooked source;GenerateOnLoad=" + Generation.GenerateOnLoad
                    + ";EmitHook=" + Generation.EmitHook
                    + ";private staged grouper;canonical asset tables include an unselectable source-root row";
            if (Feature is PllEffectZoneAuthoring)
            {
                var Zone = (PllEffectZoneAuthoring)Feature;
                return "staged PllEffectZoneAuthoring;OccupancyHook="
                    + (Zone.OccupancyHook.Length == 0
                        ? "<disabled>" : Zone.OccupancyHook)
                    + ";EnvironmentFog=" + Zone.EnvironmentFog
                    + ";Looped=" + Zone.Looped + ";PropTriggers=" + Zone.PropTriggers.Count
                    + ";FogTriggers=" + Zone.FogTriggers.Count
                    + ";NativeSourceTriggers=" + Zone.NativeSourceTriggers.Count
                    + ";InteractionTriggers=" + Zone.InteractionTriggers.Count;
            }
            if (Feature is PllFogZoneAuthoring Fog)
                return "Fog Zone;StableId=" + Fog.StableId + ";automatic runtime presentation binding";
            if (Feature is PllChildPropAuthoring)
                return "PropSpawner + PllSocketTransformModifier per point; Late PropGrouper when required";
            if (Feature is PllPropZoneAuthoring) return "PllPropZoneConstraint on referenced PropSpawner components";
            if (Feature is PllEntityZoneAuthoring)
                return ((PllEntityZoneAuthoring)Feature).Source is PllPluginEntityZoneSource
                    ? "owned fixed plugin population; installed factory revision bound in PEAK; admission after network readiness; Continue regenerates entities"
                    : "owned output branch; native Entity Zone recipe/provider resolved in PEAK; network admission precedes publication";
            if (Feature is PllHazardZoneAuthoring)
                return "owned output branch; native Tornado/Tumbleweed points and timing retained; network admission precedes publication";
            if (Feature is PllHazardAuthoring Hazard)
                return Hazard.Behavior is PllContactInjuryBehavior
                    ? "fresh same-collider Injury CollisionModifier; actual placed instance gate before native contact; readiness-owned admission"
                    : "typed Hazard definition; private placed native/plugin graph; completed occurrence admission";
            if (Feature is PllEntityAuthoring)
                return "typed Entity definition; private placed native/plugin body; completed occurrence admission";
            if (Feature is PllAmuletSpawnerAuthoring)
                return "native Amulet Statues generator; native candidates and eligible support materials resolved in PEAK";
            if (Feature is PLLNoPropSpawn) return "PllNoPropSpawnConstraint on staged PropSpawner components";
            if (Feature is PllItemSpawnerAuthoring)
                return "Spawner + Peak.SpawnedItemTracker; live ItemDatabase resolution deferred";
            if (Feature is PllNativeSpawnerAuthoring Spawner)
                return Spawner.Capture().Kind + ";typed native spawn shape; weighted native selectors resolved in PEAK; no native prefab bundled";
            if (Feature is PLLFinalGeneration) return "staged marked root inactive";
            if (Feature is PLLCriticalGeneration Critical)
                return "active native Early prefix;Order=" + Critical.Order.ToString(CultureInfo.InvariantCulture)
                    + ";native deferred timing unchanged";
            if (Feature is PLLForceSpawner) return "staged PLLForceSpawner; live resolution deferred";
            if (Feature is PLLHookedProp) return "staged PLLHookedProp; live occurrence membership deferred";
            if (Feature is PllVisualAuthoring) return "staged PllBiomeDefinition.visuals reference";
            if (Feature is PllCustomVisualAuthoring)
            {
                var Visual = (PllCustomVisualAuthoring)Feature;
                var Offset = Visual.PositionOffset;
                return "Space=" + Visual.Space + "("
                    + ((byte)Visual.Space).ToString(CultureInfo.InvariantCulture) + ");PositionOffset=("
                    + InvariantFloat(Offset.x) + "," + InvariantFloat(Offset.y) + "," + InvariantFloat(Offset.z)
                    + ");RepositionDistance=" + InvariantFloat(Visual.RepositionDistance)
                    + ";root inactive;particles playOnAwake=false and stopped/cleared";
            }
            if (Feature is PllAtmosphereAuthoring)
            {
                var Atmosphere = (PllAtmosphereAuthoring)Feature;
                return "SkyboxMaterial=" + AssetLabel(Atmosphere.SkyboxMaterial)
                    + ";FogTexture=" + AssetLabel(Atmosphere.FogTexture)
                    + ";presentation dependency sealed; runtime assets unchanged";
            }
            if (Feature is PllSoundEmitterAuthoring) return "staged PllBiomeDefinition.soundEmitters reference";
            if (Feature is PllBiomeStingerAuthoring)
                return "staged PllBiomeDefinition.stingerDefinitions reference; AudioSource route cleared and stopped at sample zero";
            if (Feature is PLLNativeVisualResolver)
            {
                var VisualResolver = (PLLNativeVisualResolver)Feature;
                return "Preset=" + VisualResolver.Preset + "("
                    + ((byte)VisualResolver.Preset).ToString(CultureInfo.InvariantCulture)
                    + ");SuppressNativeParticles="
                    + (VisualResolver.SuppressNativeParticles ? "true" : "false")
                    + ";live native visual catalog binding deferred";
            }
            var Resolver = Feature as PLLNativeResolver;
            if (Resolver)
            {
                if (Resolver.Form == PllNativeResolverForm.Source)
                    return Resolver.Kind + "/" + Resolver.SourceProfile + ";runtime native recipe; authored inputs only";
                if (Resolver.Kind == PLLNativeResolverKind.Lava) return "Local Lava Zone; runtime native provider; placed bounds";
                if (Resolver.Kind == PLLNativeResolverKind.CustomFluid) return "Custom Fluid Zone; cloned Fluid Definition; runtime provider binding deferred";
                if (Resolver.Kind == PLLNativeResolverKind.Water)
                    return "WaterZone; generated water surface when absent";
                if (Resolver.Kind == PLLNativeResolverKind.SporeCloud)
                    return "WindAffectedStatusEmitter + DisableBasedOnRunSettings; live source fingerprint deferred";
                return "WindChillZone + Photon.Pun.PhotonView; live source fingerprint deferred";
            }
            throw new InvalidOperationException("A staged feature has no report translation outcome.");
        }

        static string InvariantFloat(float Value) => Value.ToString("R", CultureInfo.InvariantCulture);

        static Dictionary<string, string> CopyPrefabGraph(
            ProjectStage Stage, string Prefix, string RootPath, PrefabGraph Graph)
        {
            var Result = new Dictionary<string, string>(StringComparer.Ordinal);
            var CandidateIndex = 0;
            foreach (var Node in Graph.Nodes)
            {
                var Path = Node.IsRoot ? RootPath : Stage.RootAssetPath + "/" + Prefix + "-candidate-"
                    + CandidateIndex++.ToString("D4", CultureInfo.InvariantCulture) + ".prefab";
                if (!AssetDatabase.CopyAsset(Node.SourcePath, Path))
                    throw new InvalidOperationException("Unity could not stage prefab '" + Node.SourcePath + "'.");
                Result.Add(Node.SourcePath, Path);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return Result;
        }

        internal string CreateRegistry(ProjectStage Stage, IReadOnlyList<StagedDefinition> Definitions)
        {
            var Path = Stage.RootAssetPath + "/pll-biome-registry.asset";
            var Registry = ScriptableObject.CreateInstance<PllBiomeRegistry>();
            Registry.name = "PllBiomeRegistry";
            AssetDatabase.CreateAsset(Registry, Path);
            var Serialized = new SerializedObject(Registry);
            var Property = PllBundleBuilder.Property(Serialized, "definitions");
            Property.arraySize = Definitions.Count;
            for (var i = 0; i < Definitions.Count; i++)
                Property.GetArrayElementAtIndex(i).objectReferenceValue = Definitions[i].Staged;
            Serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(Registry);
            AssetDatabase.SaveAssetIfDirty(Registry);
            return Path;
        }

        internal AuditResult Audit(string[] AssetPaths)
        {
            var Dependencies = AssetDatabase.GetDependencies(AssetPaths, true)
                .Distinct(StringComparer.Ordinal).OrderBy(Value => Value, StringComparer.Ordinal).ToArray();
            var Providers = ManagedProviders(Installation);
            Func<string, bool> ContainsProvider = Providers.Contains;
            var PlayerAssemblies = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(Value => Value.name), StringComparer.Ordinal);
            var EditorOnly = new HashSet<string>(
                CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(Value => Value.name), StringComparer.Ordinal);
            EditorOnly.ExceptWith(PlayerAssemblies);
            var SerializedGraphs = new HashSet<Type>();
            foreach (var Path in Dependencies)
            {
                var Extension = System.IO.Path.GetExtension(Path);
                var ManagedSource = Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                    || Extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase);
                var SourceProvider = ManagedSource && Installation.IsSourceProviderAssetPath(Path);
                if (Path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0
                    && !Installation.IsProviderAssetPath(Path) && !SourceProvider)
                    throw new InvalidOperationException("Dependency '" + Path + "' is editor-only and cannot enter a .pll bundle.");
                if (ManagedSource && !SourceProvider)
                    throw new InvalidOperationException("Dependency '" + Path
                        + "' is managed source outside the verified runtime provider set.");
                if (Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                    && !Installation.IsProviderAssetPath(Path))
                    throw new InvalidOperationException("Dependency '" + Path
                        + "' is a managed DLL outside the PLL SDK and detected PEAK reference set.");
                foreach (var Asset in AssetDatabase.LoadAllAssetsAtPath(Path))
                {
                    var Root = Asset as GameObject;
                    if (Root) AuditHierarchy(Root, Path, ContainsProvider, EditorOnly, Installation, SerializedGraphs);
                    var Scriptable = Asset as ScriptableObject;
                    if (Scriptable) AuditScriptable(Scriptable, Path, ContainsProvider, EditorOnly, Installation, SerializedGraphs);
                }
            }
            return new AuditResult(Dependencies, Providers, EditorOnly, SerializedGraphs);
        }

        static void AuditHierarchy(
            GameObject Root, string AssetPath, Func<string, bool> ContainsProvider, HashSet<string> EditorOnly,
            PllCreatorInstallation Installation, HashSet<Type> SerializedGraphs)
        {
            foreach (var Transform in Root.GetComponentsInChildren<Transform>(true))
            {
                var GameObject = Transform.gameObject;
                var Missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(GameObject);
                if (Missing != 0)
                    throw new InvalidOperationException("Prefab dependency '" + AssetPath + "' object '"
                        + PllHierarchy.NamePath(Root.transform, Transform) + "' contains " + Missing + " missing script(s).");
                foreach (var Component in GameObject.GetComponents<Component>())
                {
                    if (!Component)
                        throw new InvalidOperationException("Prefab dependency '" + AssetPath + "' object '"
                            + PllHierarchy.NamePath(Root.transform, Transform) + "' contains a missing component.");
                    var Behaviour = Component as MonoBehaviour;
                    if (Behaviour) AuditManagedObject(Behaviour, MonoScript.FromMonoBehaviour(Behaviour), AssetPath,
                        PllHierarchy.NamePath(Root.transform, Transform), ContainsProvider, EditorOnly, Installation, SerializedGraphs);
                }
            }
        }

        static void AuditScriptable(
            ScriptableObject Value, string AssetPath, Func<string, bool> ContainsProvider, HashSet<string> EditorOnly,
            PllCreatorInstallation Installation, HashSet<Type> SerializedGraphs)
        {
            AuditManagedObject(Value, MonoScript.FromScriptableObject(Value), AssetPath, Value.name,
                ContainsProvider, EditorOnly, Installation, SerializedGraphs);
        }

        static void AuditManagedObject(
            Object Value, MonoScript Script, string AssetPath, string ObjectPath,
            Func<string, bool> ContainsProvider, HashSet<string> EditorOnly, PllCreatorInstallation Installation,
            HashSet<Type> SerializedGraphs)
        {
            var Type = Value.GetType();
            var AssemblyName = Type.Assembly.GetName().Name;
            if (string.Equals(AssemblyName, "PLL.Creator.Editor", StringComparison.Ordinal)
                || EditorOnly.Contains(AssemblyName) && !ContainsProvider(AssemblyName))
                throw new InvalidOperationException("Asset '" + AssetPath + "' object '" + ObjectPath
                    + "' uses editor-only component type '" + Type.FullName + "'.");
            if (string.Equals(AssemblyName, "UnityEngine", StringComparison.Ordinal)
                || !string.Equals(AssemblyName, "UnityEngine.UI", StringComparison.Ordinal)
                    && AssemblyName.StartsWith("UnityEngine.", StringComparison.Ordinal)) return;
            if (!ContainsProvider(AssemblyName))
                throw new InvalidOperationException("Asset '" + AssetPath + "' object '" + ObjectPath
                    + "' uses managed type '" + Type.FullName + "' from unavailable runtime assembly '"
                    + AssemblyName + "'. AssetBundles cannot supply that DLL.");
            if (!Script)
                throw new InvalidOperationException("Asset '" + AssetPath + "' object '" + ObjectPath
                    + "' has no resolvable MonoScript for '" + Type.FullName + "'.");
            var ScriptPath = AssetDatabase.GetAssetPath(Script);
            if (Script.GetClass() != Type)
                throw new InvalidOperationException("Asset '" + AssetPath + "' object '" + ObjectPath
                    + "' has a MonoScript that does not resolve to '" + Type.FullName + "'.");
            if (SerializedGraphs.Contains(Type)) return;
            if (!Installation.CheckSerializedTypeCompatibility(AssemblyName, ScriptPath, Type))
            {
                if (Installation.IsProviderAssetPath(ScriptPath))
                    throw new InvalidOperationException("Asset '" + AssetPath + "' object '" + ObjectPath
                        + "' has a serialized field graph that crosses an incompatible source provider.");
                throw new InvalidOperationException("Asset '" + AssetPath + "' object '" + ObjectPath
                    + "' uses managed script '" + ScriptPath + "' without a compatible serialized-type contract for '"
                    + Type.FullName + "'.");
            }
            SerializedGraphs.Add(Type);
        }

        internal InspectionResult InspectBundle(
            string Path, IReadOnlyList<CompiledDefinition> Expected, AuditResult Audit,
            IReadOnlyList<string> ExpectedAssetNames)
        {
            var Bundle = AssetBundle.LoadFromFile(Path);
            if (!Bundle) throw new InvalidOperationException("Unity could not reopen the built content.pll AssetBundle.");
            try
            {
                var AssetNames = Bundle.GetAllAssetNames().OrderBy(Value => Value, StringComparer.Ordinal).ToArray();
                var ExpectedNames = ExpectedAssetNames.Select(Value => Value.ToLowerInvariant())
                    .OrderBy(Value => Value, StringComparer.Ordinal).ToArray();
                if (!AssetNames.SequenceEqual(ExpectedNames, StringComparer.Ordinal))
                    throw new InvalidOperationException("Built bundle address catalog changed after reopen.");
                foreach (var Name in AssetNames)
                {
                    var Extension = System.IO.Path.GetExtension(Name);
                    if (Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                        || Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                        || Extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Built bundle contains managed code asset '" + Name + "'.");
                }
                var Definitions = Bundle.LoadAllAssets<PllBiomeDefinition>()
                    .OrderBy(Value => Value.BiomeId.Value, StringComparer.Ordinal).ToArray();
                if (Definitions.Length != Expected.Count)
                    throw new InvalidOperationException("Built bundle contains " + Definitions.Length
                        + " biome definitions; expected " + Expected.Count + ".");
                var ExpectedById = Expected.ToDictionary(Value => Value.SemanticIdentity.BiomeId, StringComparer.Ordinal);
                var ReopenedIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var Definition in Definitions)
                {
                    var BiomeId = Definition.BiomeId.Value;
                    CompiledDefinition Staged;
                    if (!ReopenedIds.Add(BiomeId) || !ExpectedById.TryGetValue(BiomeId, out Staged))
                        throw new InvalidOperationException("Built bundle contains duplicate or unexpected biome '"
                            + BiomeId + "'.");
                    Staged.Validate(Definition, "Built bundle biome definition", Bundle);
                }
                var Registries = Bundle.LoadAllAssets<PllBiomeRegistry>();
                if (Registries.Length != 1 || Registries[0].Definitions.Count != Expected.Count)
                    throw new InvalidOperationException("Built bundle must contain one complete PllBiomeRegistry.");
                Registries[0].Validate();
                var Roots = Bundle.LoadAllAssets<GameObject>();
                Func<string, bool> ContainsProvider = Audit.ContainsProvider;
                foreach (var Root in Roots)
                    AuditHierarchy(Root, "content.pll:" + Root.name, ContainsProvider, Audit.EditorOnly, Installation,
                        Audit.SerializedGraphs);
                PllSourceCatalogValidator.ValidateScope(Definitions, Roots);
                return new InspectionResult(AssetNames);
            }
            finally
            {
                Bundle.Unload(true);
            }
        }

        internal BuildReportData CreateReport(
            string OutputPath,
            StagedBuild Staged,
            AuditResult Audit,
            InspectionResult Inspection,
            string BuiltPath)
        {
            var PeakPath = Installation.GetAssemblyPath("Assembly-CSharp");
            var PeakIdentity = System.IO.File.Exists(PeakPath) ? AssemblyName.GetAssemblyName(PeakPath) : null;
            var PeakFile = System.IO.File.Exists(PeakPath) ? FileVersionInfo.GetVersionInfo(PeakPath) : null;
            var File = new FileInfo(BuiltPath);
            return new BuildReportData
            {
                output = OutputPath,
                target = BuildTarget.StandaloneWindows64.ToString(),
                unityVersion = Application.unityVersion,
                pllAssembly = typeof(PllBiomeDefinition).Assembly.GetName().FullName,
                peakAssembly = PeakIdentity != null ? PeakIdentity.FullName : "unavailable",
                peakFileVersion = PeakFile != null ? PeakFile.FileVersion : "unavailable",
                bundleBytes = File.Length,
                bundleSha256 = PllCreatorFileSystem.Sha256(BuiltPath),
                biomeIds = Staged.Definitions.Select(Value => Value.Staged.BiomeId.Value).ToArray(),
                biomeBehaviors = Staged.Definitions.Select(
                    Value => Value.Staged.Behavior.ToString()).ToArray(),
                assets = Inspection.AssetNames,
                dependencies = Audit.Dependencies,
                managedProviders = Audit.CopyOrderedProviders(),
                translations = Staged.Translations
            };
        }

        static HashSet<string> ManagedProviders(PllCreatorInstallation Installation)
        {
            return new HashSet<string>(Installation.ManagedAssemblyNames, StringComparer.Ordinal);
        }

        internal static T MapComponent<T>(GameObject SourceRoot, GameObject StagedRoot, T Source, string Field) where T : Component
        {
            if (!Source) throw new InvalidOperationException("Cannot stage missing component field '" + Field + "'.");
            var SourceTransform = Source.transform;
            var Path = PllHierarchy.SiblingPath(SourceRoot.transform, SourceTransform,
                "A staged component is not owned by its root prefab.");
            var StagedTransform = StagedRoot.transform;
            foreach (var Index in Path)
            {
                if (Index < 0 || Index >= StagedTransform.childCount)
                    throw new InvalidOperationException("Staged prefab changed component path for field '" + Field + "'.");
                StagedTransform = StagedTransform.GetChild(Index);
            }
            var SourceComponents = SourceTransform.GetComponents<T>();
            var ComponentIndex = Array.FindIndex(SourceComponents, Value => ReferenceEquals(Value, Source));
            var StagedComponents = StagedTransform.GetComponents<T>();
            if (ComponentIndex < 0 || ComponentIndex >= StagedComponents.Length)
                throw new InvalidOperationException("Staged prefab changed component order for field '" + Field + "'.");
            return StagedComponents[ComponentIndex];
        }

        internal static Transform MapTransform(GameObject SourceRoot, GameObject StagedRoot, Transform Source, string Field)
        {
            if (!Source) throw new InvalidOperationException("Cannot stage missing Transform field '" + Field + "'.");
            var Path = PllHierarchy.SiblingPath(SourceRoot.transform, Source,
                "A staged component is not owned by its root prefab.");
            var Staged = StagedRoot.transform;
            foreach (var Index in Path)
            {
                if (Index < 0 || Index >= Staged.childCount)
                    throw new InvalidOperationException("Staged prefab changed Transform path for field '" + Field + "'.");
                Staged = Staged.GetChild(Index);
            }
            return Staged;
        }

        internal static GameObject MapGameObject(GameObject SourceRoot, GameObject StagedRoot, GameObject Source, string Field)
        {
            if (!Source) throw new InvalidOperationException("Cannot stage missing GameObject field '" + Field + "'.");
            return MapTransform(SourceRoot, StagedRoot, Source.transform, Field).gameObject;
        }

        internal static void MapComponentArray<T>(
            SerializedObject Serialized, string Field, GameObject SourceRoot, GameObject StagedRoot,
            IReadOnlyList<T> Source) where T : Component
        {
            var Property = PllBundleBuilder.Property(Serialized, Field);
            Property.arraySize = Source.Count;
            for (var i = 0; i < Source.Count; i++)
                Property.GetArrayElementAtIndex(i).objectReferenceValue = MapComponent(
                    SourceRoot, StagedRoot, Source[i], Field + "[" + i + "]");
        }

        internal static void MapGraphComponentArray<T>(
            SerializedObject Serialized, string Field, PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths, IReadOnlyList<T> Source) where T : Component
        {
            var Property = PllBundleBuilder.Property(Serialized, Field);
            Property.arraySize = Source.Count;
            for (var i = 0; i < Source.Count; i++)
            {
                var Node = OwnerNode(Graph, Source[i], Field + "[" + i + "]");
                var StagedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(StagedPaths[Node.SourcePath]);
                if (!StagedRoot)
                    throw new InvalidOperationException("Cannot reload staged prefab for field '" + Field + "[" + i + "]'.");
                Property.GetArrayElementAtIndex(i).objectReferenceValue = MapComponent(
                    Node.SourceRoot, StagedRoot, Source[i], Field + "[" + i + "]");
            }
        }

    }

    sealed class StagedBuild
    {
        internal StagedBuild(CompiledDefinition[] Definitions, BuildTranslationData[] Translations)
        {
            this.Definitions = Definitions;
            this.Translations = Translations;
        }

        internal CompiledDefinition[] Definitions { get; }
        internal BuildTranslationData[] Translations { get; }
    }

    sealed class ProjectStage
    {
        const string FolderPrefix = "__pll_creator_staging_";
        const string MarkerName = "pll-staging-marker.txt";
        readonly string Token;

        ProjectStage(string RootAssetPath, string Token)
        {
            this.RootAssetPath = RootAssetPath;
            this.Token = Token;
        }

        internal string RootAssetPath { get; }

        internal static ProjectStage Create()
        {
            RemoveStale();
            var Token = System.Guid.NewGuid().ToString("N");
            var Folder = FolderPrefix + Token;
            var Guid = AssetDatabase.CreateFolder("Assets", Folder);
            if (string.IsNullOrEmpty(Guid)) throw new InvalidOperationException("Unity could not create builder staging.");
            var Stage = new ProjectStage("Assets/" + Folder, Token);
            try
            {
                File.WriteAllText(PllCreatorFileSystem.AssetPathToFullPath(
                    Stage.RootAssetPath + "/" + MarkerName), Token, PllCreatorFileSystem.StrictUtf8);
                AssetDatabase.ImportAsset(Stage.RootAssetPath + "/" + MarkerName, ImportAssetOptions.ForceSynchronousImport);
                return Stage;
            }
            catch (Exception CreationFailure)
            {
                try
                {
                    if (!AssetDatabase.DeleteAsset(Stage.RootAssetPath))
                        throw new InvalidOperationException("Unity could not clean failed project staging '"
                            + Stage.RootAssetPath + "'.");
                }
                catch (Exception CleanupFailure)
                {
                    throw new AggregateException("Project staging initialization and cleanup both failed.",
                        CreationFailure, CleanupFailure);
                }
                throw;
            }
        }

        internal string ReportAssetPath(string AssetPath)
        {
            var Prefix = RootAssetPath + "/";
            if (!AssetPath.StartsWith(Prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Staged asset is outside builder-owned project staging.");
            return "staging/" + AssetPath.Substring(Prefix.Length);
        }

        static void RemoveStale()
        {
            foreach (var Path in AssetDatabase.GetSubFolders("Assets").OrderBy(Value => Value, StringComparer.Ordinal))
            {
                var Folder = Path.Substring("Assets/".Length);
                if (!Folder.StartsWith(FolderPrefix, StringComparison.Ordinal)) continue;
                var Token = Folder.Substring(FolderPrefix.Length);
                Guid Parsed;
                if (!Guid.TryParseExact(Token, "N", out Parsed)
                    || !string.Equals(Parsed.ToString("N"), Token, StringComparison.Ordinal)) continue;
                var Marker = PllCreatorFileSystem.AssetPathToFullPath(Path + "/" + MarkerName);
                if (!File.Exists(Marker) || !string.Equals(File.ReadAllText(Marker), Token, StringComparison.Ordinal))
                    continue;
                if (!AssetDatabase.DeleteAsset(Path))
                    throw new InvalidOperationException("Unity could not clean stale builder-owned project staging '"
                        + Path + "'.");
            }
        }

        internal void Cleanup()
        {
            var Marker = PllCreatorFileSystem.AssetPathToFullPath(RootAssetPath + "/" + MarkerName);
            if (!File.Exists(Marker) || !string.Equals(File.ReadAllText(Marker), Token, StringComparison.Ordinal))
                throw new InvalidOperationException("Refused to clean project staging because its ownership marker changed.");
            if (!AssetDatabase.DeleteAsset(RootAssetPath))
                throw new InvalidOperationException("Unity could not clean builder-owned project staging '" + RootAssetPath + "'.");
        }
    }

    sealed class ExternalStage
    {
        const string MarkerName = ".pll-staging-marker";
        readonly string OutputDirectory;
        readonly string Token;

        ExternalStage(string RootPath, string OutputDirectory, string Token)
        {
            this.RootPath = RootPath;
            this.OutputDirectory = OutputDirectory;
            this.Token = Token;
        }

        internal string RootPath { get; }

        internal static ExternalStage Create(string OutputPath)
        {
            var Token = Guid.NewGuid().ToString("N");
            var OutputDirectory = Path.GetDirectoryName(OutputPath);
            var Root = Path.Combine(OutputDirectory, "pll-stage-" + Token);
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(Path.Combine(Root, MarkerName), Token, PllCreatorFileSystem.StrictUtf8);
                return new ExternalStage(Root, OutputDirectory, Token);
            }
            catch (Exception CreationFailure)
            {
                try
                {
                    if (Directory.Exists(Root)) Directory.Delete(Root, true);
                }
                catch (Exception CleanupFailure)
                {
                    throw new AggregateException("External staging initialization and cleanup both failed.",
                        CreationFailure, CleanupFailure);
                }
                throw;
            }
        }

        internal void Cleanup()
        {
            if (!Directory.Exists(RootPath)) return;
            var Marker = Path.Combine(RootPath, MarkerName);
            if (!PllCreatorFileSystem.Inside(RootPath, OutputDirectory) || !File.Exists(Marker)
                || !string.Equals(File.ReadAllText(Marker), Token, StringComparison.Ordinal))
                throw new InvalidOperationException("Refused to clean external staging because its ownership marker changed.");
            Directory.Delete(RootPath, true);
        }
    }

    sealed class DefinitionSemanticIdentity
    {
        readonly AssetIdentityCatalog AssetIdentities = new AssetIdentityCatalog();
        readonly string DisplayTitle;
        readonly string ContentRevision;
        readonly PllBiomeBehavior Behavior;
        readonly string[] FavoriteBiomes;
        readonly string[] OverwrittenBiomes;
        readonly string[] BeforeBiomes;
        readonly string[] AfterBiomes;
        readonly int SpawnChance;
        readonly PrefabBinding RootPrefab;
        readonly PrefabBinding Anchors;
        readonly AssetBinding DayNightProfile;
        readonly (AssetBinding Original, AssetBinding Fog, byte[] Content, PllFogShaderAdapterKind Kind)[] FogShaders;
        readonly PrefabBinding[] Visuals;
        readonly Color Color;
        readonly AssetBinding[] AmbienceSounds;
        readonly AssetBinding[] EffectSounds;
        readonly AssetBinding[] EnterSounds;
        readonly LuggageIdentity[] Luggage;
        readonly VariationIdentity[] Variations;
        readonly PrefabBinding[] Effects;
        readonly PrefabBinding[] SoundEmitters;
        readonly PrefabBinding[] Stingers;
        readonly BiomeGroupSemanticIdentity Group;
        readonly AtmosphereIdentity Atmosphere;
        readonly (bool NightCold, bool MesaHeatstroke) Modifiers;
        readonly ClimbSurfaceSemanticIdentity ClimbSurfaces;
        readonly CollisionGeometryIdentity[] CollisionGeometry;
        readonly EffectZoneIdentity[] EffectZones;
        readonly PrefabBinding[] SourceNodes;
        readonly NativeResolverIdentity[] NativeResolvers;
        readonly FluidDefinitionIdentity[] FluidDefinitions;
        readonly SurfaceTextureIdentity[] SurfaceTextures;
        readonly (PrefabBinding Binding, AssetBinding Preview, PllNativeMaterialSelection Selection)[] NativeMaterials;
        readonly NativeSpawnerIdentity[] NativeSpawners;
        readonly BreakablePropIdentity[] BreakableProps;
        readonly SwingingPropIdentity[] SwingingProps;
        readonly VineIdentity[] Vines;
        readonly MountedToolInsertionIdentity[] MountedToolInsertions;
        readonly FogZoneIdentity[] FogZones;
        readonly PresentationTransitionIdentity[] Transitions;
        readonly HookedGenerationIdentity[] HookedGeneration;
        readonly CriticalGenerationIdentity[] CriticalGeneration;
        readonly RestPointIdentity[] RestPoints;
        readonly HangingRopeIdentity[] HangingRopes;
        readonly ActorZoneSemanticIdentity[] EntityZones;
        readonly ActorZoneSemanticIdentity[] HazardZones;
        readonly ActorDefinitionIdentity[] Hazards;
        readonly ActorDefinitionIdentity[] Entities;

        DefinitionSemanticIdentity(
            PllBiomeDefinition Definition, DefinitionCatalog Catalog,
            IReadOnlyDictionary<string, string> StagedPaths,
            IReadOnlyDictionary<PllFluidDefinition, PllFluidDefinition> PreparedFluids,
            PreparedMeshRecipeCatalog MeshRecipes, IReadOnlyList<PllFogShaderAdapter> PreparedFogAdapters,
            IReadOnlyList<MountedToolInsertionRecipe> InsertionRecipes)
        {
            var Graph = Catalog.Graph;
            BiomeId = Definition.BiomeId.Value;
            DisplayTitle = Definition.DisplayTitle;
            ContentRevision = Definition.ContentRevision;
            Behavior = Definition.Behavior;
            FavoriteBiomes = BiomeIds(Definition.Favorites);
            OverwrittenBiomes = BiomeIds(Definition.Overwrites);
            BeforeBiomes = BiomeIds(Definition.Before);
            AfterBiomes = BiomeIds(Definition.After);
            SpawnChance = Definition.SpawnChance;
            RootPrefab = PrefabBinding.CaptureRoot(Graph, StagedPaths);
            Anchors = PrefabBinding.Capture(Graph, StagedPaths, Definition.Anchors, "anchors");
            DayNightProfile = AssetBinding.Capture(Definition.DayNightProfile, AssetIdentities, "dayNightProfile");
            FogShaders = new (AssetBinding, AssetBinding, byte[], PllFogShaderAdapterKind)[PreparedFogAdapters.Count];
            for (var Index = 0; Index < FogShaders.Length; Index++)
            {
                var Adapter = PreparedFogAdapters[Index];
                FogShaders[Index] = (
                    AssetBinding.Capture(Adapter.OriginalShader, AssetIdentities, "fog shader original"),
                    AssetBinding.Capture(Adapter.FogShader, AssetIdentities, "fog shader derivative"),
                    Adapter.PreparedContent.ToArray(), Adapter.Kind);
            }
            Visuals = PrefabBindings(Graph, StagedPaths, Catalog.Visuals, "visuals");
            Color = Definition.Color;
            AmbienceSounds = AssetBindings(Definition.AmbienceSounds, AssetIdentities, "ambienceSounds");
            EffectSounds = AssetBindings(Definition.EffectSounds, AssetIdentities, "effectSounds");
            EnterSounds = AssetBindings(Definition.EnterSounds, AssetIdentities, "enterSounds");
            Luggage = LuggageIdentities(
                Graph, StagedPaths, Catalog.Luggage, AssetIdentities);
            Variations = new VariationIdentity[Catalog.Variations.Length];
            for (var Index = 0; Index < Variations.Length; Index++)
                Variations[Index] = VariationIdentity.Capture(
                    Graph, StagedPaths, Catalog.Variations[Index],
                    "variationDefinitions[" + Index.ToString(CultureInfo.InvariantCulture) + "]");
            Effects = PrefabBindings(Graph, StagedPaths, Catalog.Effects, "effectDefinitions");
            Hazards = new ActorDefinitionIdentity[Catalog.Hazards.Length];
            for (var Index = 0; Index < Hazards.Length; Index++)
                Hazards[Index] = new ActorDefinitionIdentity(Graph, StagedPaths, Catalog.Hazards[Index], AssetIdentities);
            Entities = new ActorDefinitionIdentity[Catalog.Entities.Length];
            for (var Index = 0; Index < Entities.Length; Index++)
                Entities[Index] = new ActorDefinitionIdentity(Graph, StagedPaths, Catalog.Entities[Index], AssetIdentities);
            SoundEmitters = PrefabBindings(
                Graph, StagedPaths, Catalog.SoundEmitters, "soundEmitters");
            Stingers = PrefabBindings(
                Graph, StagedPaths, Catalog.Stingers, "stingerDefinitions");
            Group = BiomeGroupSemanticIdentity.Capture(
                Graph, StagedPaths, Catalog.Group);
            Atmosphere = AtmosphereIdentity.Capture(
                Definition.RootPrefab, Graph, StagedPaths, AssetIdentities);
            Modifiers = NativeModifiers.Capture(Definition.RootPrefab.transform);
            ClimbSurfaces = ClimbSurfaceSemanticIdentity.Capture(
                Graph, StagedPaths, Catalog.ClimbSurfaceCatalog,
                AssetIdentities);
            RestPoints = new RestPointIdentity[Catalog.RestPoints.Length];
            for (var Index = 0; Index < RestPoints.Length; Index++)
                RestPoints[Index] = new RestPointIdentity(Graph, StagedPaths, Catalog.RestPoints[Index]);
            HangingRopes = new HangingRopeIdentity[Catalog.HangingRopes.Length];
            for (var Index = 0; Index < HangingRopes.Length; Index++)
                HangingRopes[Index] = new HangingRopeIdentity(Graph, StagedPaths, Catalog.HangingRopes[Index], AssetIdentities);
            var CaptureResolver = new DefinitionBindingResolver(null);
            NativeMaterials = new (PrefabBinding, AssetBinding, PllNativeMaterialSelection)[Catalog.NativeMaterials.Length];
            NativeSpawners = new NativeSpawnerIdentity[Catalog.NativeSpawners.Length];
            for (var Index = 0; Index < NativeSpawners.Length; Index++)
                NativeSpawners[Index] = new NativeSpawnerIdentity(Graph, StagedPaths, Catalog.NativeSpawners[Index]);
            for (var Index = 0; Index < NativeMaterials.Length; Index++)
            {
                var Source = Catalog.NativeMaterials[Index]; var Selection = Source.Capture();
                NativeMaterials[Index] = (PrefabBinding.Capture(Graph, StagedPaths, Source, "native material"),
                    AssetBinding.Capture(Selection.Preview, AssetIdentities, "native material preview"), Selection);
            }
            SurfaceTextures = new SurfaceTextureIdentity[Catalog.SurfaceTextures.Length];
            for (var Index = 0; Index < SurfaceTextures.Length; Index++)
                SurfaceTextures[Index] = new SurfaceTextureIdentity(Graph, StagedPaths,
                    Catalog.SurfaceTextures[Index], AssetIdentities, CaptureResolver, MeshRecipes);
            BreakableProps = new BreakablePropIdentity[Catalog.BreakableProps.Length];
            for (var Index = 0; Index < BreakableProps.Length; Index++)
                BreakableProps[Index] = new BreakablePropIdentity(Graph, StagedPaths, Catalog.BreakableProps[Index], AssetIdentities);
            SwingingProps = new SwingingPropIdentity[Catalog.SwingingProps.Length];
            for (var Index = 0; Index < SwingingProps.Length; Index++)
                SwingingProps[Index] = new SwingingPropIdentity(Graph, StagedPaths, Catalog.SwingingProps[Index], AssetIdentities);
            Vines = new VineIdentity[Catalog.Vines.Length];
            for (var Index = 0; Index < Vines.Length; Index++)
                Vines[Index] = new VineIdentity(Graph, StagedPaths, Catalog.Vines[Index], AssetIdentities, CaptureResolver, MeshRecipes);
            MountedToolInsertions = new MountedToolInsertionIdentity[InsertionRecipes.Count];
            for (var Index = 0; Index < MountedToolInsertions.Length; Index++)
                MountedToolInsertions[Index] = new MountedToolInsertionIdentity(Graph, StagedPaths, InsertionRecipes[Index]);
            EntityZones = new ActorZoneSemanticIdentity[Catalog.EntityZones.Length];
            for (var Index = 0; Index < EntityZones.Length; Index++)
                EntityZones[Index] = new ActorZoneSemanticIdentity(Graph, StagedPaths,
                    Catalog.EntityZones[Index], AssetIdentities, "entityZones[" + Index + "]");
            HazardZones = new ActorZoneSemanticIdentity[Catalog.HazardZones.Length];
            for (var Index = 0; Index < HazardZones.Length; Index++)
                HazardZones[Index] = new ActorZoneSemanticIdentity(Graph, StagedPaths,
                    Catalog.HazardZones[Index], AssetIdentities, "hazardZones[" + Index + "]");
            CollisionGeometry = new CollisionGeometryIdentity[Catalog.CollisionGeometry.Length];
            for (var Index = 0; Index < CollisionGeometry.Length; Index++)
                CollisionGeometry[Index] = CollisionGeometryIdentity.Capture(
                    Graph, StagedPaths, Catalog.CollisionGeometry[Index], AssetIdentities, CaptureResolver, MeshRecipes);
            this.EffectZones = EffectZoneIdentities(
                Graph, StagedPaths, Catalog.EffectZones);
            SourceNodes = new PrefabBinding[Graph.Nodes.Length];
            for (var Index = 0; Index < SourceNodes.Length; Index++)
                SourceNodes[Index] = PrefabBinding.CaptureNode(Graph, StagedPaths, Graph.Nodes[Index]);
            NativeResolvers = new NativeResolverIdentity[Catalog.NativeResolvers.Length];
            var Fluids = new Dictionary<PllFluidDefinition, FluidDefinitionIdentity>();
            for (var Index = 0; Index < NativeResolvers.Length; Index++)
            {
                var Source = Catalog.NativeResolvers[Index];
                if (Source.FluidDefinition && !Fluids.ContainsKey(Source.FluidDefinition))
                    Fluids.Add(Source.FluidDefinition, new FluidDefinitionIdentity(
                        Graph, StagedPaths, Source, PreparedFluids[Source.FluidDefinition], AssetIdentities));
                NativeResolvers[Index] = new NativeResolverIdentity(Graph, StagedPaths,
                    Source, AssetIdentities, PreparedFluids);
            }
            FluidDefinitions = Fluids.Values.ToArray();
            FogZones = new FogZoneIdentity[Catalog.FogZones.Length];
            for (var Index = 0; Index < FogZones.Length; Index++)
                FogZones[Index] = new FogZoneIdentity(Graph, StagedPaths, Catalog.FogZones[Index]);
            Transitions = Catalog.Effects.Cast<Component>()
                .Concat(Catalog.EffectZones)
                .Concat(Catalog.Visuals).Concat(Catalog.SoundEmitters).Concat(Catalog.Stingers)
                .Concat(Catalog.CustomVisuals.Select(Value => Value.Authoring))
                .Concat(Catalog.NativeVisualResolvers).Select(Value =>
                    new PresentationTransitionIdentity(Graph, StagedPaths, Value)).ToArray();
            HookedGeneration = new HookedGenerationIdentity[Catalog.HookedGeneration.Sources.Length];
            for (var Index = 0; Index < HookedGeneration.Length; Index++)
                HookedGeneration[Index] = new HookedGenerationIdentity(Graph, StagedPaths, Catalog.HookedGeneration.Sources[Index]);
            CriticalGeneration = new CriticalGenerationIdentity[Catalog.CriticalGeneration.Length];
            for (var Index = 0; Index < CriticalGeneration.Length; Index++)
            {
                var Marker = Catalog.CriticalGeneration[Index];
                CriticalGeneration[Index] = new CriticalGenerationIdentity(
                    PrefabBinding.Capture(Graph, StagedPaths, Marker, "criticalGeneration[" + Index + "]"), Marker.Order);
            }
        }

        internal string BiomeId { get; }

        internal static DefinitionSemanticIdentity Capture(
            PllBiomeDefinition Definition, DefinitionCatalog Catalog,
            IReadOnlyDictionary<string, string> StagedPaths,
            IReadOnlyDictionary<PllFluidDefinition, PllFluidDefinition> PreparedFluids,
            PreparedMeshRecipeCatalog MeshRecipes, IReadOnlyList<PllFogShaderAdapter> PreparedFogAdapters,
            IReadOnlyList<MountedToolInsertionRecipe> InsertionRecipes) =>
            new DefinitionSemanticIdentity(
                Definition, Catalog, StagedPaths, PreparedFluids, MeshRecipes, PreparedFogAdapters, InsertionRecipes);

        internal GeneratedPropCatalogIdentity CaptureGeneratedCatalog(
            PllHookedPropGenerationCompiler.CompletedCatalog Catalog, PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths) =>
            new GeneratedPropCatalogIdentity(Catalog, AssetIdentities, Graph, StagedPaths);

        internal void Validate(
            PllBiomeDefinition Definition, string Context, AssetBundle Bundle,
            GeneratedPropCatalogIdentity GeneratedCatalog)
        {
            var Resolver = new DefinitionBindingResolver(Bundle);
            RequireString(Definition.BiomeId.Value, BiomeId, Context, "biomeId");
            RequireString(Definition.DisplayTitle, DisplayTitle, Context, "displayTitle");
            RequireString(Definition.ContentRevision, ContentRevision, Context, "contentRevision");
            if (Definition.Behavior != Behavior) throw Changed(Context, "behavior");
            RequireBiomeIds(Definition.Favorites, FavoriteBiomes, Context, "favorites");
            RequireBiomeIds(Definition.Overwrites, OverwrittenBiomes, Context, "overwrites");
            RequireBiomeIds(Definition.Before, BeforeBiomes, Context, "before");
            RequireBiomeIds(Definition.After, AfterBiomes, Context, "after");
            if (Definition.SpawnChance != SpawnChance) throw Changed(Context, "spawnChance");
            RootPrefab.Require(Definition.RootPrefab, Resolver, Context, "rootPrefab");
            Anchors.Require(Definition.Anchors, Resolver, Context, "anchors");
            DayNightProfile.Require(Definition.DayNightProfile, Resolver, Context, "dayNightProfile");
            var ActualFogShaders = Definition.CompiledFogShaderAdapters;
            if (ActualFogShaders.Count != FogShaders.Length) throw Changed(Context, "compiled fog shader adapters");
            for (var Index = 0; Index < FogShaders.Length; Index++)
            {
                var Adapter = ActualFogShaders[Index];
                if (Adapter == null || Adapter.Kind != FogShaders[Index].Kind
                    || !Adapter.PreparedContent.SequenceEqual(FogShaders[Index].Content))
                    throw Changed(Context, "compiled fog shader preparation");
                FogShaders[Index].Original.Require(Adapter.OriginalShader, Resolver, Context, "fog shader original");
                FogShaders[Index].Fog.Require(Adapter.FogShader, Resolver, Context, "fog shader derivative");
            }
            RequirePrefabBindings(Definition.Visuals, Visuals, Resolver, Context, "visuals");
            if (!Definition.Color.Equals(Color)) throw Changed(Context, "color");
            RequireAssetBindings(
                Definition.AmbienceSounds, AmbienceSounds, Resolver, Context, "ambienceSounds");
            RequireAssetBindings(
                Definition.EffectSounds, EffectSounds, Resolver, Context, "effectSounds");
            RequireAssetBindings(
                Definition.EnterSounds, EnterSounds, Resolver, Context, "enterSounds");
            RequireLuggage(Definition.LuggageDefinitions, Resolver, Context);
            if (Definition.VariationDefinitions.Count != Variations.Length)
                throw Changed(Context, "variationDefinitions");
            for (var Index = 0; Index < Variations.Length; Index++)
                Variations[Index].Require(Definition.VariationDefinitions[Index], Resolver, Context,
                    "variationDefinitions[" + Index.ToString(CultureInfo.InvariantCulture) + "]");
            RequirePrefabBindings(
                Definition.EffectDefinitions, Effects, Resolver, Context, "effectDefinitions");
            RequirePrefabBindings(
                Definition.SoundEmitters, SoundEmitters, Resolver, Context, "soundEmitters");
            RequirePrefabBindings(
                Definition.StingerDefinitions, Stingers, Resolver, Context, "stingerDefinitions");
            BiomeGroupSemanticIdentity.RequireOptional(
                Group, Definition.RootPrefab, Resolver, Context);
            AtmosphereIdentity.RequireOptional(
                Atmosphere, Definition.RootPrefab, Resolver, Context);
            if (NativeModifiers.Capture(Definition.RootPrefab.transform) != Modifiers)
                throw Changed(Context, "native modifiers");
            ClimbSurfaces.Require(Resolver, Context);
            foreach (var RestPoint in RestPoints) RestPoint.Require(Resolver, Context);
            foreach (var Rope in HangingRopes) Rope.Require(Resolver, Context);
            foreach (var Surface in SurfaceTextures) Surface.Require(Resolver, Context);
            foreach (var Material in NativeMaterials)
            {
                var Selection = Material.Binding.Resolve<PllNativeMaterialAuthoring>(Resolver, Context, "native material").Capture();
                if (!Selection.HasSameReferences(Material.Selection)) throw Changed(Context, "native material references");
                Material.Preview.Require(Selection.Preview, Resolver, Context, "native material preview");
            }
            foreach (var Spawner in NativeSpawners) Spawner.Require(Resolver, Context);
            foreach (var Breakable in BreakableProps) Breakable.Require(Resolver, Context);
            foreach (var Swinging in SwingingProps) Swinging.Require(Resolver, Context);
            foreach (var Vine in Vines) Vine.Require(Resolver, Context);
            foreach (var Insertion in MountedToolInsertions) Insertion.Require(Resolver, Context);
            foreach (var Hazard in Hazards) Hazard.Require(Resolver, Context);
            foreach (var Entity in Entities) Entity.Require(Resolver, Context);
            var ActualEntities = DiscoverComponents<PllEntityZoneAuthoring>(Definition.RootPrefab);
            var ActualHazards = DiscoverComponents<PllHazardZoneAuthoring>(Definition.RootPrefab);
            if (ActualEntities.Length != EntityZones.Length) throw Changed(Context, "entityZones");
            if (ActualHazards.Length != HazardZones.Length) throw Changed(Context, "hazardZones");
            for (var Index = 0; Index < EntityZones.Length; Index++)
                EntityZones[Index].Require(ActualEntities[Index], Definition.Anchors, Resolver, Context, "entityZones[" + Index + "]");
            for (var Index = 0; Index < HazardZones.Length; Index++)
                HazardZones[Index].Require(ActualHazards[Index], Definition.Anchors, Resolver, Context, "hazardZones[" + Index + "]");
            foreach (var Geometry in CollisionGeometry) Geometry.Require(Resolver, Context);
            for (var Index = 0; Index < EffectZones.Length; Index++)
                EffectZones[Index].Require(Resolver, Context,
                    "effectZones[" + Index.ToString(
                        CultureInfo.InvariantCulture) + "]");
            var FogCount = 0; var NativeCount = 0; var ZoneCount = 0;
            var HazardCount = 0; var EntityCount = 0;
            var SurfaceCount = 0; var BreakableCount = 0; var SwingingCount = 0; var VineCount = 0;
            var RestPointCount = 0; var HangingRopeCount = 0;
            var NativeMaterialCount = 0;
            var NativeSpawnerCount = 0;
            foreach (var Node in SourceNodes)
            {
                var Root = Node.Resolve<GameObject>(Resolver, Context, "source node");
                foreach (var Feature in Root.GetComponentsInChildren<MonoBehaviour>(true))
                    switch (Feature)
                    {
                        case PllFogZoneAuthoring _: FogCount++; break;
                        case PLLNativeResolver _: NativeCount++; break;
                        case PllEffectZoneAuthoring _: ZoneCount++; break;
                        case PllHazardAuthoring _: HazardCount++; break;
                        case PllEntityAuthoring _: EntityCount++; break;
                        case PllSurfaceTextureAuthoring _: SurfaceCount++; break;
                        case PllNativeMaterialAuthoring _: NativeMaterialCount++; break;
                        case PllNativeSpawnerAuthoring _: NativeSpawnerCount++; break;
                        case PllBreakablePropAuthoring _: BreakableCount++; break;
                        case PllSwingingPropAuthoring _: SwingingCount++; break;
                        case PllVineAuthoring _: VineCount++; break;
                        case PllRestPointAuthoring _: RestPointCount++; break;
                        case PllHangingRopeAuthoring _: HangingRopeCount++; break;
                    }
            }
            if (FogCount != FogZones.Length || NativeCount != NativeResolvers.Length || ZoneCount != EffectZones.Length)
                throw Changed(Context, "source zone and resolver inventory");
            if (HazardCount != Hazards.Length || EntityCount != Entities.Length)
                throw Changed(Context, "actor definition inventory");
            if (NativeMaterialCount != NativeMaterials.Length || SurfaceCount != SurfaceTextures.Length || BreakableCount != BreakableProps.Length
                || SwingingCount != SwingingProps.Length || VineCount != Vines.Length)
                throw Changed(Context, "surface and prop feature inventory");
            if (NativeSpawnerCount != NativeSpawners.Length) throw Changed(Context, "native spawner inventory");
            if (RestPointCount != RestPoints.Length || HangingRopeCount != HangingRopes.Length)
                throw Changed(Context, "climbing fixture inventory");
            foreach (var Fluid in FluidDefinitions) Fluid.Require(Resolver, Context);
            foreach (var Native in NativeResolvers) Native.Require(Resolver, Context);
            foreach (var Fog in FogZones) Fog.Require(Resolver, Context);
            foreach (var Transition in Transitions) Transition.Require(Resolver, Context);
            var ActualCritical = DiscoverComponents<PLLCriticalGeneration>(Definition.RootPrefab);
            var ActualHooked = DiscoverComponents<PllHookedPropGenerationAuthoring>(Definition.RootPrefab);
            if (ActualHooked.Length != HookedGeneration.Length) throw Changed(Context, "hookedPropGeneration");
            for (var Index = 0; Index < HookedGeneration.Length; Index++)
                HookedGeneration[Index].Require(ActualHooked[Index], Resolver, Context);
            if (ActualCritical.Length != CriticalGeneration.Length) throw Changed(Context, "criticalGeneration");
            for (var Index = 0; Index < CriticalGeneration.Length; Index++)
                CriticalGeneration[Index].Require(ActualCritical[Index], Resolver, Context, "criticalGeneration[" + Index + "]");
            GeneratedCatalog.Require(Definition, Resolver, Context);
        }

        void RequireLuggage(
            IReadOnlyList<PllLuggageAuthoring> Actual,
            DefinitionBindingResolver Resolver, string Context)
        {
            if (Actual.Count != Luggage.Length) throw Changed(Context, "luggageDefinitions");
            for (var Index = 0; Index < Luggage.Length; Index++)
                Luggage[Index].Require(
                    Actual[Index], Resolver, Context,
                    "luggageDefinitions[" + Index.ToString(CultureInfo.InvariantCulture) + "]");
        }

        internal static void RequireString(string Actual, string Expected, string Context, string Field)
        {
            if (!string.Equals(Actual, Expected, StringComparison.Ordinal)) throw Changed(Context, Field);
        }

        static void RequireBiomeIds(
            IReadOnlyList<BiomeId> Actual, string[] Expected, string Context, string Field)
        {
            if (Actual.Count != Expected.Length) throw DefinitionSemanticIdentity.Changed(Context, Field);
            for (var Index = 0; Index < Expected.Length; Index++)
                if (!string.Equals(Actual[Index].Value, Expected[Index], StringComparison.Ordinal))
                    throw DefinitionSemanticIdentity.Changed(Context, Field);
        }

        static void RequirePrefabBindings<T>(
            IReadOnlyList<T> Actual, PrefabBinding[] Expected,
            DefinitionBindingResolver Resolver, string Context, string Field) where T : Component
        {
            if (Actual.Count != Expected.Length) throw DefinitionSemanticIdentity.Changed(Context, Field);
            for (var Index = 0; Index < Expected.Length; Index++)
                Expected[Index].Require(
                    Actual[Index], Resolver, Context,
                    Field + "[" + Index.ToString(CultureInfo.InvariantCulture) + "]");
        }

        static void RequireAssetBindings<T>(
            IReadOnlyList<T> Actual, AssetBinding[] Expected,
            DefinitionBindingResolver Resolver, string Context, string Field) where T : Object
        {
            if (Actual.Count != Expected.Length) throw DefinitionSemanticIdentity.Changed(Context, Field);
            for (var Index = 0; Index < Expected.Length; Index++)
                Expected[Index].Require(
                    Actual[Index], Resolver, Context,
                    Field + "[" + Index.ToString(CultureInfo.InvariantCulture) + "]");
        }

        static string[] BiomeIds(IReadOnlyList<BiomeId> Values)
        {
            var Result = new string[Values.Count];
            for (var Index = 0; Index < Result.Length; Index++) Result[Index] = Values[Index].Value;
            return Result;
        }

        static PrefabBinding[] PrefabBindings<T>(
            PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            IReadOnlyList<T> Values, string Field) where T : Component
        {
            var Result = new PrefabBinding[Values.Count];
            for (var Index = 0; Index < Result.Length; Index++)
                Result[Index] = PrefabBinding.Capture(
                    Graph, StagedPaths, Values[Index],
                    Field + "[" + Index.ToString(CultureInfo.InvariantCulture) + "]");
            return Result;
        }

        static AssetBinding[] AssetBindings<T>(
            IReadOnlyList<T> Values, AssetIdentityCatalog Identities, string Field) where T : Object
        {
            var Result = new AssetBinding[Values.Count];
            for (var Index = 0; Index < Result.Length; Index++)
                Result[Index] = AssetBinding.Capture(
                    Values[Index], Identities,
                    Field + "[" + Index.ToString(CultureInfo.InvariantCulture) + "]");
            return Result;
        }

        static LuggageIdentity[] LuggageIdentities(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            IReadOnlyList<PllLuggageAuthoring> Values,
            AssetIdentityCatalog AssetIdentities)
        {
            var Result = new LuggageIdentity[Values.Count];
            for (var Index = 0; Index < Result.Length; Index++)
            {
                var Field = "luggageDefinitions["
                    + Index.ToString(CultureInfo.InvariantCulture) + "]";
                Result[Index] = LuggageIdentity.Capture(
                    Graph, StagedPaths, Values[Index],
                    AssetIdentities, Field);
            }
            return Result;
        }
        static EffectZoneIdentity[] EffectZoneIdentities(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            IReadOnlyList<PllEffectZoneAuthoring> Values)
        {
            var Result = new EffectZoneIdentity[Values.Count];
            for (var Index = 0; Index < Result.Length; Index++)
            {
                var Field = "effectZones["
                    + Index.ToString(CultureInfo.InvariantCulture) + "]";
                Result[Index] = new EffectZoneIdentity(
                    PrefabBinding.Capture(
                        Graph, StagedPaths, Values[Index], Field),
                    Values[Index].OccupancyHook, Values[Index].EnvironmentFog,
                    Values[Index].Looped, Graph, StagedPaths, Values[Index].PropTriggers,
                    Values[Index].FogTriggers, Values[Index].NativeSourceTriggers, Values[Index].ActorSourceTriggers,
                    Values[Index].InteractionTriggers);
            }
            return Result;
        }

        internal static InvalidOperationException Changed(string Context, string Field) =>
            new InvalidOperationException(Context + " '" + Field + "' changed.");
    }

    sealed class NativeSpawnerIdentity
    {
        readonly PrefabBinding Binding;
        readonly PllNativeSpawnerSelection Selection;
        readonly PrefabBinding[] Points;
        readonly PrefabBinding UpTowards;

        internal NativeSpawnerIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PllNativeSpawnerAuthoring Source)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "native spawner"); Selection = Source.Capture();
            Points = new PrefabBinding[Selection.Points.Count];
            for (var i = 0; i < Points.Length; i++) Points[i] = PrefabBinding.Capture(Graph, StagedPaths, Selection.Points[i], "food spawn point");
            UpTowards = PrefabBinding.CaptureOptional(Graph, StagedPaths, Selection.UpTowards, "food up target");
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Source = Binding.Resolve<PllNativeSpawnerAuthoring>(Resolver, Context, "native spawner");
            var Actual = Source.Capture();
            if (!Selection.HasSameSettings(Actual)) throw DefinitionSemanticIdentity.Changed(Context, "native spawner settings");
            for (var i = 0; i < Points.Length; i++) Points[i].Require(Actual.Points[i], Resolver, Context, "food spawn point");
            PrefabBinding.RequireOptional(UpTowards, Actual.UpTowards, Resolver, Context, "food up target");
            PllNativeSpawnerShape.Require(Source, Actual);
        }
    }

    sealed class CriticalGenerationIdentity
    {
        readonly PrefabBinding Binding;
        readonly int Order;

        internal CriticalGenerationIdentity(PrefabBinding Binding, int Order)
        {
            this.Binding = Binding; this.Order = Order;
        }

        internal void Require(
            PLLCriticalGeneration Actual, DefinitionBindingResolver Resolver, string Context, string Field)
        {
            Binding.Require(Actual, Resolver, Context, Field);
            if (!Actual.enabled || !Actual.gameObject.activeSelf || Actual.Order != Order)
                throw DefinitionSemanticIdentity.Changed(Context, Field + ".Order or activation");
        }
    }

    sealed class BiomeGroupSemanticIdentity
    {
        readonly PrefabBinding Carrier;
        readonly string[] PrefixIds;

        BiomeGroupSemanticIdentity(
            PrefabBinding Carrier, string[] PrefixIds)
        {
            this.Carrier = Carrier;
            this.PrefixIds = PrefixIds;
        }

        internal static BiomeGroupSemanticIdentity Capture(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            PllBiomeGroupContract Group)
        {
            if (Group == null) return null;
            var PrefixIds = new string[Group.Prefixes.Count];
            for (var Index = 0; Index < PrefixIds.Length; Index++)
                PrefixIds[Index] = Group.Prefixes[Index].BiomeId.Value;
            return new BiomeGroupSemanticIdentity(
                PrefabBinding.Capture(
                    Graph, StagedPaths, Group.Carrier, "biomeGroup"),
                PrefixIds);
        }

        internal static void RequireOptional(
            BiomeGroupSemanticIdentity Expected, GameObject Root,
            DefinitionBindingResolver Resolver, string Context)
        {
            var Carriers = DiscoverComponents<PllBiomeGroupAuthoring>(Root);
            if (Expected == null)
            {
                if (Carriers.Length != 0)
                    throw DefinitionSemanticIdentity.Changed(
                        Context, "biomeGroup");
                return;
            }
            if (Carriers.Length != 1)
                throw DefinitionSemanticIdentity.Changed(
                    Context, "biomeGroup");
            Expected.Carrier.Require(
                Carriers[0], Resolver, Context, "biomeGroup");
            var Prefixes = Carriers[0].Prefixes;
            if (Prefixes.Count != Expected.PrefixIds.Length)
                throw DefinitionSemanticIdentity.Changed(
                    Context, "biomeGroup.prefixes");
            for (var Index = 0; Index < Expected.PrefixIds.Length; Index++)
                if (!Prefixes[Index]
                    || !string.Equals(
                        Prefixes[Index].BiomeId.Value,
                        Expected.PrefixIds[Index],
                        StringComparison.Ordinal))
                    throw DefinitionSemanticIdentity.Changed(
                        Context, "biomeGroup.prefixes["
                        + Index.ToString(CultureInfo.InvariantCulture) + "]");
        }
    }
    sealed class ClimbSurfaceSemanticIdentity
    {
        readonly PrefabBinding[] GraphRoots;
        readonly ClimbSurfaceRuleIdentity[] ObjectRules;
        readonly ClimbSurfaceMaterialIdentity MaterialRules;
        readonly ClimbSurfaceTargetIdentity[] Targets;

        ClimbSurfaceSemanticIdentity(
            PrefabBinding[] GraphRoots,
            ClimbSurfaceRuleIdentity[] ObjectRules,
            ClimbSurfaceMaterialIdentity MaterialRules,
            ClimbSurfaceTargetIdentity[] Targets)
        {
            this.GraphRoots = GraphRoots;
            this.ObjectRules = ObjectRules;
            this.MaterialRules = MaterialRules;
            this.Targets = Targets;
        }

        internal static ClimbSurfaceSemanticIdentity Capture(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            ClimbSurfaceAuthoringCatalog Catalog,
            AssetIdentityCatalog AssetIdentities)
        {
            var GraphRoots = new PrefabBinding[Graph.Nodes.Length];
            for (var Index = 0; Index < GraphRoots.Length; Index++)
                GraphRoots[Index] = PrefabBinding.Capture(
                    Graph, StagedPaths,
                    Graph.Nodes[Index].SourceRoot.transform,
                    "climbSurfaces.graph["
                        + Index.ToString(CultureInfo.InvariantCulture)
                        + "]");
            var ObjectRules = new ClimbSurfaceRuleIdentity[
                Catalog.Surfaces.Length];
            for (var Index = 0; Index < ObjectRules.Length; Index++)
                ObjectRules[Index] = ClimbSurfaceRuleIdentity.Capture(
                    Graph, StagedPaths, Catalog.Surfaces[Index],
                    "climbSurfaces.objects["
                        + Index.ToString(CultureInfo.InvariantCulture)
                        + "]");
            return new ClimbSurfaceSemanticIdentity(
                GraphRoots, ObjectRules,
                ClimbSurfaceMaterialIdentity.CaptureOptional(
                    Graph, StagedPaths, Catalog.MaterialAuthoring,
                    AssetIdentities),
                CaptureTargets(
                    Graph, StagedPaths, Catalog,
                    AssetIdentities));
        }

        internal void Require(
            DefinitionBindingResolver Resolver,
            string Context)
        {
            var Roots = new GameObject[GraphRoots.Length];
            for (var Index = 0; Index < Roots.Length; Index++)
                Roots[Index] = GraphRoots[Index].Resolve<Transform>(
                    Resolver, Context,
                    "climbSurfaces.graph["
                        + Index.ToString(CultureInfo.InvariantCulture)
                        + "]").gameObject;

            var ExpectedObjects = new HashSet<
                PllClimbSurfaceAuthoring>();
            for (var Index = 0; Index < ObjectRules.Length; Index++)
                ExpectedObjects.Add(ObjectRules[Index].Require(
                    Resolver, Context,
                    "climbSurfaces.objects["
                        + Index.ToString(CultureInfo.InvariantCulture)
                        + "]"));
            RequireExactGraphComponents(
                Roots, ExpectedObjects, Context,
                "climbSurfaces.objects");

            var ExpectedMaterials = new HashSet<
                PllClimbSurfaceMaterialAuthoring>();
            if (MaterialRules != null)
                ExpectedMaterials.Add(MaterialRules.Require(
                    Resolver, Context,
                    "climbSurfaces.materials"));
            RequireExactGraphComponents(
                Roots, ExpectedMaterials, Context,
                "climbSurfaces.materials");

            var ExpectedTargets = new HashSet<
                ClimbModifierSurface>();
            for (var Index = 0; Index < Targets.Length; Index++)
                ExpectedTargets.Add(Targets[Index].Require(
                    Resolver, Context,
                    "climbSurfaces.targets["
                        + Index.ToString(CultureInfo.InvariantCulture)
                        + "]"));
            RequireExactGraphComponents(
                Roots, ExpectedTargets, Context,
                "climbSurfaces.targets");
        }

        static ClimbSurfaceTargetIdentity[] CaptureTargets(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            ClimbSurfaceAuthoringCatalog Catalog,
            AssetIdentityCatalog AssetIdentities)
        {
            var Pending = new HashSet<GameObject>();
            foreach (var Surface in Catalog.Surfaces)
                Pending.Add(Surface.gameObject);
            foreach (var Target in Catalog.MaterialTargets.Keys)
                Pending.Add(Target);
            var Result = new List<ClimbSurfaceTargetIdentity>(
                Pending.Count);
            foreach (var Node in Graph.Nodes)
                foreach (var Transform in
                    DiscoverComponents<Transform>(Node.SourceRoot))
                {
                    if (!Pending.Remove(Transform.gameObject))
                        continue;
                    Result.Add(ClimbSurfaceTargetIdentity.Capture(
                        Graph, StagedPaths, Transform,
                        AssetIdentities,
                        "climbSurfaces.targets["
                            + Result.Count.ToString(
                                CultureInfo.InvariantCulture)
                            + "]"));
                }
            if (Pending.Count != 0)
                throw new InvalidOperationException(
                    "Cannot capture climb target semantic identity.");
            return Result.ToArray();
        }

        static void RequireExactGraphComponents<T>(
            IReadOnlyList<GameObject> Roots,
            HashSet<T> Expected, string Context,
            string Field) where T : Component
        {
            var Actual = new HashSet<T>();
            foreach (var Root in Roots)
                foreach (var Value in
                    Root.GetComponentsInChildren<T>(true))
                    if (Value) Actual.Add(Value);
            if (Actual.Count != Expected.Count
                || !Actual.SetEquals(Expected))
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field);
        }
    }

    sealed class ClimbSurfaceRuleIdentity
    {
        readonly PrefabBinding Binding;
        readonly PllClimbSurfaceMode Mode;
        readonly string Hook;
        readonly float StaminaUsageMultiplier;
        readonly float SpeedMultiplier;

        ClimbSurfaceRuleIdentity(
            PrefabBinding Binding,
            PllClimbSurfaceMode Mode, string Hook,
            float StaminaUsageMultiplier,
            float SpeedMultiplier)
        {
            this.Binding = Binding;
            this.Mode = Mode;
            this.Hook = Hook;
            this.StaminaUsageMultiplier =
                StaminaUsageMultiplier;
            this.SpeedMultiplier = SpeedMultiplier;
        }

        internal static ClimbSurfaceRuleIdentity Capture(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            PllClimbSurfaceAuthoring Value, string Field) =>
            new ClimbSurfaceRuleIdentity(
                PrefabBinding.Capture(
                    Graph, StagedPaths, Value, Field),
                Value.Mode, Value.EmitHook,
                Value.StaminaUsageMultiplier,
                Value.SpeedMultiplier);

        internal PllClimbSurfaceAuthoring Require(
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            var Actual = Binding.Resolve<
                PllClimbSurfaceAuthoring>(
                    Resolver, Context, Field);
            if (!Actual.enabled
                || Actual.GetComponents<
                    PllClimbSurfaceAuthoring>().Length != 1
                || Actual.Mode != Mode
                || !string.Equals(
                    Actual.EmitHook, Hook,
                    StringComparison.Ordinal)
                || !Actual.StaminaUsageMultiplier.Equals(
                    StaminaUsageMultiplier)
                || !Actual.SpeedMultiplier.Equals(
                    SpeedMultiplier))
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field);
            return Actual;
        }
    }

    sealed class ClimbSurfaceMaterialIdentity
    {
        readonly PrefabBinding Binding;
        readonly ClimbSurfaceMaterialRuleIdentity[] Rules;

        ClimbSurfaceMaterialIdentity(
            PrefabBinding Binding,
            ClimbSurfaceMaterialRuleIdentity[] Rules)
        {
            this.Binding = Binding;
            this.Rules = Rules;
        }

        internal static ClimbSurfaceMaterialIdentity CaptureOptional(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            PllClimbSurfaceMaterialAuthoring Value,
            AssetIdentityCatalog AssetIdentities)
        {
            if (!Value) return null;
            var Rules = new ClimbSurfaceMaterialRuleIdentity[
                Value.Rules.Count];
            for (var Index = 0; Index < Rules.Length; Index++)
                Rules[Index] =
                    ClimbSurfaceMaterialRuleIdentity.Capture(
                        Value.Rules[Index], AssetIdentities,
                        "climbSurfaces.materials.rules["
                            + Index.ToString(
                                CultureInfo.InvariantCulture)
                            + "]");
            return new ClimbSurfaceMaterialIdentity(
                PrefabBinding.Capture(
                    Graph, StagedPaths, Value,
                    "climbSurfaces.materials"),
                Rules);
        }

        internal PllClimbSurfaceMaterialAuthoring Require(
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            var Actual = Binding.Resolve<
                PllClimbSurfaceMaterialAuthoring>(
                    Resolver, Context, Field);
            if (!Actual.enabled
                || Actual.GetComponents<
                    PllClimbSurfaceMaterialAuthoring>().Length != 1
                || Actual.Rules.Count != Rules.Length)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field);
            for (var Index = 0; Index < Rules.Length; Index++)
                Rules[Index].Require(
                    Actual.Rules[Index], Resolver, Context,
                    Field + ".rules["
                        + Index.ToString(CultureInfo.InvariantCulture)
                        + "]");
            return Actual;
        }
    }

    sealed class ClimbSurfaceMaterialRuleIdentity
    {
        readonly AssetBinding Material;
        readonly PhysicsMaterialSemanticIdentity MaterialValues;
        readonly PllClimbSurfaceMode Mode;
        readonly string Hook;
        readonly float StaminaUsageMultiplier;
        readonly float SpeedMultiplier;

        ClimbSurfaceMaterialRuleIdentity(
            AssetBinding Material,
            PhysicsMaterialSemanticIdentity MaterialValues,
            PllClimbSurfaceMode Mode, string Hook,
            float StaminaUsageMultiplier,
            float SpeedMultiplier)
        {
            this.Material = Material;
            this.MaterialValues = MaterialValues;
            this.Mode = Mode;
            this.Hook = Hook;
            this.StaminaUsageMultiplier =
                StaminaUsageMultiplier;
            this.SpeedMultiplier = SpeedMultiplier;
        }

        internal static ClimbSurfaceMaterialRuleIdentity Capture(
            PllClimbSurfaceMaterialRule Value,
            AssetIdentityCatalog AssetIdentities,
            string Field) =>
            new ClimbSurfaceMaterialRuleIdentity(
                AssetBinding.Capture(
                    Value.PhysicsMaterial, AssetIdentities,
                    Field + ".physicsMaterial"),
                PhysicsMaterialSemanticIdentity.Capture(
                    Value.PhysicsMaterial),
                Value.Mode, Value.EmitHook,
                Value.StaminaUsageMultiplier,
                Value.SpeedMultiplier);

        internal void Require(
            PllClimbSurfaceMaterialRule Actual,
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            if (Actual == null
                || Actual.Mode != Mode
                || !string.Equals(
                    Actual.EmitHook, Hook,
                    StringComparison.Ordinal)
                || !Actual.StaminaUsageMultiplier.Equals(
                    StaminaUsageMultiplier)
                || !Actual.SpeedMultiplier.Equals(
                    SpeedMultiplier))
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field);
            Material.Require(
                Actual.PhysicsMaterial, Resolver,
                Context, Field + ".physicsMaterial");
            MaterialValues.Require(
                Actual.PhysicsMaterial, Context,
                Field + ".physicsMaterial");
        }
    }

    readonly struct PhysicsMaterialSemanticIdentity
    {
        readonly string Name;
        readonly float DynamicFriction;
        readonly float StaticFriction;
        readonly float Bounciness;
        readonly PhysicsMaterialCombine FrictionCombine;
        readonly PhysicsMaterialCombine BounceCombine;

        PhysicsMaterialSemanticIdentity(
            string Name, float DynamicFriction,
            float StaticFriction, float Bounciness,
            PhysicsMaterialCombine FrictionCombine,
            PhysicsMaterialCombine BounceCombine)
        {
            this.Name = Name;
            this.DynamicFriction = DynamicFriction;
            this.StaticFriction = StaticFriction;
            this.Bounciness = Bounciness;
            this.FrictionCombine = FrictionCombine;
            this.BounceCombine = BounceCombine;
        }

        internal static PhysicsMaterialSemanticIdentity Capture(
            PhysicsMaterial Value) =>
            new PhysicsMaterialSemanticIdentity(
                Value.name ?? string.Empty,
                Value.dynamicFriction, Value.staticFriction,
                Value.bounciness, Value.frictionCombine,
                Value.bounceCombine);

        internal void Require(
            PhysicsMaterial Actual, string Context,
            string Field)
        {
            if (!Actual
                || !string.Equals(
                    Actual.name ?? string.Empty, Name,
                    StringComparison.Ordinal)
                || !Actual.dynamicFriction.Equals(
                    DynamicFriction)
                || !Actual.staticFriction.Equals(
                    StaticFriction)
                || !Actual.bounciness.Equals(Bounciness)
                || Actual.frictionCombine != FrictionCombine
                || Actual.bounceCombine != BounceCombine)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field);
        }
    }

    sealed class ClimbSurfaceTargetIdentity
    {
        readonly PrefabBinding Binding;
        readonly int Layer;
        readonly ClimbSurfaceColliderIdentity[] Colliders;

        ClimbSurfaceTargetIdentity(
            PrefabBinding Binding, int Layer,
            ClimbSurfaceColliderIdentity[] Colliders)
        {
            this.Binding = Binding;
            this.Layer = Layer;
            this.Colliders = Colliders;
        }

        internal static ClimbSurfaceTargetIdentity Capture(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            Transform Target,
            AssetIdentityCatalog AssetIdentities,
            string Field)
        {
            var Values = Target.GetComponents<Collider>()
                .Where(PllClimbSurfaceAuthoringValidator.Eligible).ToArray();
            if (Values.Length == 0)
                throw new InvalidOperationException(
                    "Cannot capture climb target without Collider coverage.");
            var Colliders = new ClimbSurfaceColliderIdentity[
                Values.Length];
            for (var Index = 0; Index < Colliders.Length; Index++)
                Colliders[Index] =
                    ClimbSurfaceColliderIdentity.Capture(
                        Graph, StagedPaths, Values[Index],
                        AssetIdentities,
                        Field + ".colliders["
                            + Index.ToString(
                                CultureInfo.InvariantCulture)
                            + "]");
            return new ClimbSurfaceTargetIdentity(
                PrefabBinding.Capture(
                    Graph, StagedPaths, Target, Field),
                Target.gameObject.layer, Colliders);
        }

        internal ClimbModifierSurface Require(
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            var Target = Binding.Resolve<Transform>(
                Resolver, Context, Field);
            if (Target.gameObject.layer != Layer)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".layer");
            var ActualColliders = Target.GetComponents<Collider>()
                .Where(PllClimbSurfaceAuthoringValidator.Eligible).ToArray();
            if (ActualColliders.Length != Colliders.Length)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".colliders");
            for (var Index = 0; Index < Colliders.Length; Index++)
                Colliders[Index].Require(
                    ActualColliders[Index], Resolver, Context,
                    Field + ".colliders["
                        + Index.ToString(CultureInfo.InvariantCulture)
                        + "]");
            var Surfaces = Target.GetComponents<
                ClimbModifierSurface>();
            if (Surfaces.Length != 1)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".nativeSurface");
            try
            {
                PllClimbSurfaceAuthoringValidator.ValidateNeutralNative(
                    Surfaces[0], Context + " '" + Field + "'");
            }
            catch (InvalidOperationException Failure)
            {
                throw new InvalidOperationException(
                    Context + " '" + Field
                        + ".nativeSurface' changed.", Failure);
            }
            return Surfaces[0];
        }

    }

    sealed class ClimbSurfaceColliderIdentity
    {
        readonly PrefabBinding Binding;
        readonly AssetBinding Material;

        ClimbSurfaceColliderIdentity(
            PrefabBinding Binding, AssetBinding Material)
        {
            this.Binding = Binding;
            this.Material = Material;
        }

        internal static ClimbSurfaceColliderIdentity Capture(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            Collider Value,
            AssetIdentityCatalog AssetIdentities,
            string Field) =>
            new ClimbSurfaceColliderIdentity(
                PrefabBinding.Capture(
                    Graph, StagedPaths, Value, Field),
                Value.sharedMaterial
                    ? AssetBinding.Capture(
                        Value.sharedMaterial, AssetIdentities,
                        Field + ".physicsMaterial")
                    : null);

        internal void Require(
            Collider Actual,
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            Binding.Require(Actual, Resolver, Context, Field);
            if (Material == null)
            {
                if (Actual.sharedMaterial)
                    throw DefinitionSemanticIdentity.Changed(
                        Context, Field + ".physicsMaterial");
                return;
            }
            Material.Require(
                Actual.sharedMaterial, Resolver,
                Context, Field + ".physicsMaterial");
        }
    }

    sealed class EffectZoneIdentity
    {
        readonly PrefabBinding Binding;
        readonly string OccupancyHook;
        readonly PllEffectZoneEnvironmentFog EnvironmentFog;
        readonly bool Looped;
        readonly (PllEffectZoneTransition Transition, PrefabBinding Target)[] Triggers;
        readonly (PllEffectZoneTransition Transition, PllFogZoneOperation Operation, PrefabBinding Target)[] FogTriggers;
        readonly (PllEffectZoneTransition Transition, PllNativeSourceOperation Operation, PrefabBinding Target)[] NativeSourceTriggers;
        readonly (PllEffectZoneTransition Transition, PllNativeSourceOperation Operation, PrefabBinding Target)[] ActorSourceTriggers;
        readonly (PllEffectZoneTransition Transition, PllPropOperation Operation, bool Enabled, PrefabBinding Target)[] InteractionTriggers;

        internal EffectZoneIdentity(
            PrefabBinding Binding, string OccupancyHook,
            PllEffectZoneEnvironmentFog EnvironmentFog, bool Looped,
            PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            IReadOnlyList<PllEffectZonePropTrigger> Triggers,
            IReadOnlyList<PllEffectZoneFogTrigger> FogTriggers,
            IReadOnlyList<PllEffectZoneNativeSourceTrigger> NativeSourceTriggers,
            IReadOnlyList<PllEffectZoneActorSourceTrigger> ActorSourceTriggers,
            IReadOnlyList<PllEffectZoneInteractionTrigger> InteractionTriggers)
        {
            this.Binding = Binding;
            this.OccupancyHook = OccupancyHook;
            this.EnvironmentFog = EnvironmentFog;
            this.Looped = Looped;
            this.Triggers = new (PllEffectZoneTransition, PrefabBinding)[Triggers.Count];
            for (var Index = 0; Index < Triggers.Count; Index++)
                this.Triggers[Index] = (Triggers[Index].Transition,
                    PrefabBinding.Capture(Graph, StagedPaths, Triggers[Index].Target, "effectZonePropTrigger"));
            this.FogTriggers = new (PllEffectZoneTransition, PllFogZoneOperation, PrefabBinding)[FogTriggers.Count];
            for (var Index = 0; Index < FogTriggers.Count; Index++)
                this.FogTriggers[Index] = (FogTriggers[Index].Transition, FogTriggers[Index].Operation,
                    PrefabBinding.Capture(Graph, StagedPaths, FogTriggers[Index].Target, "effectZoneFogTrigger"));
            this.NativeSourceTriggers = new (PllEffectZoneTransition, PllNativeSourceOperation, PrefabBinding)[NativeSourceTriggers.Count];
            for (var Index = 0; Index < NativeSourceTriggers.Count; Index++)
                this.NativeSourceTriggers[Index] = (NativeSourceTriggers[Index].Transition, NativeSourceTriggers[Index].Operation,
                    PrefabBinding.Capture(Graph, StagedPaths, NativeSourceTriggers[Index].Target, "effectZoneNativeSourceTrigger"));
            this.ActorSourceTriggers = new (PllEffectZoneTransition, PllNativeSourceOperation, PrefabBinding)[ActorSourceTriggers.Count];
            for (var Index = 0; Index < ActorSourceTriggers.Count; Index++)
                this.ActorSourceTriggers[Index] = (ActorSourceTriggers[Index].Transition, ActorSourceTriggers[Index].Operation,
                    PrefabBinding.Capture(Graph, StagedPaths, ActorSourceTriggers[Index].Target, "effectZoneActorSourceTrigger"));
            this.InteractionTriggers = new (PllEffectZoneTransition, PllPropOperation, bool, PrefabBinding)[InteractionTriggers.Count];
            for (var Index = 0; Index < InteractionTriggers.Count; Index++)
                this.InteractionTriggers[Index] = (InteractionTriggers[Index].Transition, InteractionTriggers[Index].Operation,
                    InteractionTriggers[Index].Enabled,
                    PrefabBinding.Capture(Graph, StagedPaths, InteractionTriggers[Index].Target, "effectZoneInteractionTrigger"));
        }

        internal void Require(
            DefinitionBindingResolver Resolver, string Context, string Field)
        {
            var Actual = Binding.Resolve<PllEffectZoneAuthoring>(Resolver, Context, Field);
            if (!string.Equals(
                    Actual.OccupancyHook, OccupancyHook,
                    StringComparison.Ordinal))
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".occupancyHook");
            if (Actual.EnvironmentFog != EnvironmentFog)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".environmentFog");
            if (Actual.Looped != Looped || Actual.PropTriggers.Count != Triggers.Length)
                throw DefinitionSemanticIdentity.Changed(Context, Field + ".propTriggers");
            for (var Index = 0; Index < Triggers.Length; Index++)
            {
                var Trigger = Actual.PropTriggers[Index];
                if (Trigger is null || Trigger.Transition != Triggers[Index].Transition)
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".propTriggers");
                Triggers[Index].Target.Require(Trigger.Target, Resolver, Context, Field + ".propTriggers.Target");
            }
            if (Actual.FogTriggers.Count != FogTriggers.Length)
                throw DefinitionSemanticIdentity.Changed(Context, Field + ".fogTriggers");
            for (var Index = 0; Index < FogTriggers.Length; Index++)
            {
                var Trigger = Actual.FogTriggers[Index];
                if (Trigger == null || Trigger.Transition != FogTriggers[Index].Transition
                    || Trigger.Operation != FogTriggers[Index].Operation)
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".fogTriggers");
                FogTriggers[Index].Target.Require(Trigger.Target, Resolver, Context, Field + ".fogTriggers.Target");
            }
            if (Actual.NativeSourceTriggers.Count != NativeSourceTriggers.Length)
                throw DefinitionSemanticIdentity.Changed(Context, Field + ".nativeSourceTriggers");
            for (var Index = 0; Index < NativeSourceTriggers.Length; Index++)
            {
                var Trigger = Actual.NativeSourceTriggers[Index];
                if (Trigger == null || Trigger.Transition != NativeSourceTriggers[Index].Transition
                    || Trigger.Operation != NativeSourceTriggers[Index].Operation)
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".nativeSourceTriggers");
                NativeSourceTriggers[Index].Target.Require(Trigger.Target, Resolver, Context, Field + ".nativeSourceTriggers.Target");
            }
            if (Actual.ActorSourceTriggers.Count != ActorSourceTriggers.Length)
                throw DefinitionSemanticIdentity.Changed(Context, Field + ".actorSourceTriggers");
            for (var Index = 0; Index < ActorSourceTriggers.Length; Index++)
            {
                var Trigger = Actual.ActorSourceTriggers[Index];
                if (Trigger is null || Trigger.Transition != ActorSourceTriggers[Index].Transition
                    || Trigger.Operation != ActorSourceTriggers[Index].Operation)
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".actorSourceTriggers");
                ActorSourceTriggers[Index].Target.Require(Trigger.Target, Resolver, Context, Field + ".actorSourceTriggers.Target");
            }
            if (Actual.InteractionTriggers.Count != InteractionTriggers.Length)
                throw DefinitionSemanticIdentity.Changed(Context, Field + ".interactionTriggers");
            for (var Index = 0; Index < InteractionTriggers.Length; Index++)
            {
                var Trigger = Actual.InteractionTriggers[Index];
                if (Trigger is null || Trigger.Transition != InteractionTriggers[Index].Transition
                    || Trigger.Operation != InteractionTriggers[Index].Operation || Trigger.Enabled != InteractionTriggers[Index].Enabled)
                    throw DefinitionSemanticIdentity.Changed(Context, Field + ".interactionTriggers");
                InteractionTriggers[Index].Target.Require(Trigger.Target, Resolver, Context, Field + ".interactionTriggers.Target");
            }
            Actual.ValidateBundleSerialization();
        }
    }

    sealed class MountedToolInsertionRecipe
    {
        internal readonly PrefabNode Node;
        internal readonly PropSpawner Source;
        internal readonly int Index;
        internal readonly (Type Type, bool Mute)[] Modifiers;

        internal MountedToolInsertionRecipe(PrefabNode Node, PropSpawner Source, bool ImmediateChildren)
        {
            this.Node = Node; this.Source = Source;
            var Expected = new List<(Type, bool)>();
            var Children = false; Index = Source.modifiers?.Count ?? 0;
            if (Source.modifiers != null)
                foreach (var Modifier in Source.modifiers)
                {
                    if (Modifier == null)
                        throw new InvalidOperationException("Terrain insertion spawner has a missing modifier.");
                    if (Modifier is PllMountedToolInsertionModifier)
                        throw new InvalidOperationException("Terrain insertion modifiers belong to Creator staging.");
                    if (Modifier is PSM_ChildSpawners)
                    {
                        if (!Children) Index = Expected.Count;
                        Children = true;
                    }
                    else if (Children)
                        throw new InvalidOperationException("Terrain insertion needs child modifiers after ordinary modifiers.");
                    Expected.Add((Modifier.GetType(), Modifier.mute));
                }
            if (ImmediateChildren && !Children) Expected.Add((typeof(PSM_ChildSpawners), false));
            Expected.Insert(Index, (typeof(PllMountedToolInsertionModifier), false));
            Modifiers = Expected.ToArray();
        }
    }

    sealed class MountedToolInsertionIdentity
    {
        readonly PrefabBinding Spawner;
        readonly (Type Type, bool Mute)[] Modifiers;

        internal MountedToolInsertionIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            MountedToolInsertionRecipe Recipe)
        {
            Spawner = PrefabBinding.Capture(Graph, StagedPaths, Recipe.Source, "mounted tool producer");
            Modifiers = Recipe.Modifiers;
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Actual = Spawner.Resolve<PropSpawner>(Resolver, Context, "mounted tool producer");
            if (!Actual.raycastPosition || Actual.modifiers == null || Actual.modifiers.Count != Modifiers.Length)
                throw DefinitionSemanticIdentity.Changed(Context, "mounted tool producer modifiers");
            for (var Index = 0; Index < Modifiers.Length; Index++)
                if (Actual.modifiers[Index] == null || Actual.modifiers[Index].GetType() != Modifiers[Index].Type
                    || Actual.modifiers[Index].mute != Modifiers[Index].Mute)
                    throw DefinitionSemanticIdentity.Changed(Context, "mounted tool producer modifier order and policy");
        }
    }

    sealed class FluidDefinitionIdentity
    {
        readonly PrefabBinding Resolver;
        readonly AssetBinding Definition;
        readonly string DisplayName;
        readonly PllFluidBehavior Behavior;
        readonly (float Shallow, float Deep, float AnimShallow, float AnimDeep, float Jumping, float Jump, bool Disable) Water;
        readonly (float Rate, float Heat, bool Kiln) Lava;
        readonly (PllFluidMaterialMode Zone, PllFluidMaterialMode Flow) MaterialModes;
        readonly (PllFlowOutput Landing, PllFlowOutput Splash, PllFlowOutput Samples, PllFlowOutput Details) Outputs;
        readonly AssetBinding ZoneMaterial, FlowMaterial, Landing, Splash, Samples;
        readonly AssetBinding[] Details;

        internal FluidDefinitionIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PLLNativeResolver SourceResolver, PllFluidDefinition Prepared, AssetIdentityCatalog Identities)
        {
            var Source = SourceResolver.FluidDefinition;
            if (!Prepared || ReferenceEquals(Source, Prepared))
                throw new InvalidOperationException("Fluid Definition staging requires a separate asset.");
            Resolver = PrefabBinding.Capture(Graph, StagedPaths, SourceResolver, "Fluid Definition resolver");
            Definition = AssetBinding.Capture(Prepared, Identities, "staged Fluid Definition");
            DisplayName = Source.DisplayName; Behavior = Source.Behavior;
            Water = WaterSettings(Source); Lava = (Source.HeatRate, Source.Heat, Source.IsKiln);
            MaterialModes = (Source.ZoneMaterialMode, Source.FlowMaterialMode);
            Outputs = (Source.Landing, Source.Splash, Source.Samples, Source.Details);
            ZoneMaterial = AssetBinding.CaptureOptional(Source.ZoneMaterial, Identities, "fluid zone material");
            FlowMaterial = AssetBinding.CaptureOptional(Source.FlowMaterial, Identities, "fluid flow material");
            Landing = AssetBinding.CaptureOptional(Source.LandingPrefab, Identities, "fluid landing prefab");
            Splash = AssetBinding.CaptureOptional(Source.SplashPrefab, Identities, "fluid splash prefab");
            Samples = AssetBinding.CaptureOptional(Source.SamplesPrefab, Identities, "fluid sample prefab");
            Details = new AssetBinding[Source.DetailsPrefabs.Length];
            for (var Index = 0; Index < Details.Length; Index++)
                Details[Index] = AssetBinding.Capture(Source.DetailsPrefabs[Index], Identities, "fluid detail prefab");
        }

        internal void Require(DefinitionBindingResolver Bindings, string Context)
        {
            var Actual = Resolver.Resolve<PLLNativeResolver>(Bindings, Context, "Fluid Definition resolver").FluidDefinition;
            Definition.Require(Actual, Bindings, Context, "staged Fluid Definition");
            if (Actual.DisplayName != DisplayName || Actual.Behavior != Behavior || WaterSettings(Actual) != Water
                || (Actual.HeatRate, Actual.Heat, Actual.IsKiln) != Lava
                || (Actual.ZoneMaterialMode, Actual.FlowMaterialMode) != MaterialModes
                || (Actual.Landing, Actual.Splash, Actual.Samples, Actual.Details) != Outputs
                || Actual.DetailsPrefabs.Length != Details.Length)
                throw DefinitionSemanticIdentity.Changed(Context, "Fluid Definition payload");
            AssetBinding.RequireOptional(ZoneMaterial, Actual.ZoneMaterial, Bindings, Context, "fluid zone material");
            AssetBinding.RequireOptional(FlowMaterial, Actual.FlowMaterial, Bindings, Context, "fluid flow material");
            AssetBinding.RequireOptional(Landing, Actual.LandingPrefab, Bindings, Context, "fluid landing prefab");
            AssetBinding.RequireOptional(Splash, Actual.SplashPrefab, Bindings, Context, "fluid splash prefab");
            AssetBinding.RequireOptional(Samples, Actual.SamplesPrefab, Bindings, Context, "fluid sample prefab");
            for (var Index = 0; Index < Details.Length; Index++)
                Details[Index].Require(Actual.DetailsPrefabs[Index], Bindings, Context, "fluid detail prefab");
            Actual.ValidateBundleSerialization();
        }

        static (float, float, float, float, float, float, bool) WaterSettings(PllFluidDefinition Source) =>
            (Source.MovementShallow, Source.MovementDeep, Source.AnimationShallow, Source.AnimationDeep,
                Source.MovementJumping, Source.JumpMultiplier, Source.DisableSprintJump);
    }

    sealed class PreparedMeshRecipeCatalog
    {
        readonly HashSet<Renderer> CurvatureTargets = new HashSet<Renderer>();
        readonly Dictionary<Mesh, string> Curvature = new Dictionary<Mesh, string>();
        readonly Dictionary<(Mesh Mesh, Vector3 Start, Vector3 End, Vector3 Up), string> Spans =
            new Dictionary<(Mesh, Vector3, Vector3, Vector3), string>();

        internal PreparedMeshRecipeCatalog(IReadOnlyList<DefinitionCatalog> Catalogs)
        {
            foreach (var Catalog in Catalogs)
                foreach (var Surface in Catalog.SurfaceTextures)
                    if (Surface.Target == PllSurfaceTarget.AuthoredRendererSlots
                        && PllSurfaceProfileContract.NeedsBake(Surface.Material))
                        foreach (var Renderer in Surface.Renderers) CurvatureTargets.Add(Renderer);
        }

        internal bool NeedsCurvature(Renderer Renderer) => CurvatureTargets.Contains(Renderer);

        internal string CurvatureFingerprint(Mesh Source)
        {
            if (Curvature.TryGetValue(Source, out var Existing)) return Existing;
            var Values = PllSurfaceCurvature.Bake(Source);
            var Prepared = Object.Instantiate(Source);
            try
            {
                Prepared.SetUVs(PllSurfaceProfileContract.CurvatureChannel, Values);
                var Fingerprint = PllSurfaceCurvatureBaker.FingerprintMesh(Prepared);
                Curvature.Add(Source, Fingerprint); return Fingerprint;
            }
            finally { Object.DestroyImmediate(Prepared); }
        }

        internal string SpanFingerprint(Mesh Source, Vector3 Start, Vector3 End, Vector3 Up)
        {
            var Recipe = (Source, Start, End, Up);
            if (Spans.TryGetValue(Recipe, out var Existing)) return Existing;
            var Prepared = PllPropInteractionCompiler.Normalize(Source, Start, End, Up);
            try
            {
                var Fingerprint = PllSurfaceCurvatureBaker.FingerprintMesh(Prepared);
                Spans.Add(Recipe, Fingerprint); return Fingerprint;
            }
            finally { Object.DestroyImmediate(Prepared); }
        }
    }

    sealed class MeshSemanticIdentity
    {
        readonly AssetBinding Binding;
        readonly string PreparedFingerprint;

        MeshSemanticIdentity(AssetBinding Binding, string PreparedFingerprint)
        {
            this.Binding = Binding; this.PreparedFingerprint = PreparedFingerprint;
        }

        internal static MeshSemanticIdentity CaptureOriginal(Mesh Source, AssetIdentityCatalog Identities, string Field) =>
            new MeshSemanticIdentity(AssetBinding.Capture(Source, Identities, Field), null);

        internal static MeshSemanticIdentity CapturePrepared(Mesh Source, Mesh Prepared, string Fingerprint,
            AssetIdentityCatalog Identities, DefinitionBindingResolver Resolver, string Field)
        {
            if (!Prepared || ReferenceEquals(Source, Prepared)
                || Resolver.FingerprintMesh(Prepared) != Fingerprint)
                throw DefinitionSemanticIdentity.Changed("Staged prefab", Field + " derived geometry");
            return new MeshSemanticIdentity(AssetBinding.Capture(Prepared, Identities, Field), Fingerprint);
        }

        internal void Require(Mesh Actual, DefinitionBindingResolver Resolver, string Context, string Field)
        {
            Binding.Require(Actual, Resolver, Context, Field);
            if (PreparedFingerprint != null && Resolver.FingerprintMesh(Actual) != PreparedFingerprint)
                throw DefinitionSemanticIdentity.Changed(Context, Field + " derived geometry");
        }
    }

    sealed class SurfaceTextureIdentity
    {
        readonly PrefabBinding Binding, FluidSource;
        readonly PllSurfaceTarget Target;
        readonly PllSurfaceCoordinates Coordinates;
        readonly AssetBinding Material;
        readonly PrefabBinding[] Renderers;
        readonly MeshSemanticIdentity[] Meshes;
        readonly int[] Slots;
        readonly int BakeVersion;
        readonly AssetBinding[] BakedMeshes;

        internal SurfaceTextureIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PllSurfaceTextureAuthoring Source, AssetIdentityCatalog Identities,
            DefinitionBindingResolver CaptureResolver, PreparedMeshRecipeCatalog MeshRecipes)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "Surface Textures");
            FluidSource = PrefabBinding.CaptureOptional(Graph, StagedPaths, Source.FluidSource, "Surface Textures fluid source");
            Target = Source.Target; Coordinates = Source.Coordinates;
            Material = AssetBinding.Capture(Source.Material, Identities, "Surface Textures material");
            Slots = Source.Slots.ToArray(); Renderers = new PrefabBinding[Source.Renderers.Count];
            Meshes = new MeshSemanticIdentity[Renderers.Length];
            var Bake = Target == PllSurfaceTarget.AuthoredRendererSlots && PllSurfaceProfileContract.NeedsBake(Source.Material);
            using var Serialized = new SerializedObject(Source);
            var SourceBaked = PllBundleBuilder.Property(Serialized, "BundleBakedMeshes");
            BakeVersion = Bake ? PllSurfaceProfileContract.BakeVersion
                : PllBundleBuilder.Property(Serialized, "BundleBakeVersion").intValue;
            BakedMeshes = new AssetBinding[Bake ? Renderers.Length : SourceBaked.arraySize];
            for (var Index = 0; Index < Renderers.Length; Index++)
            {
                Renderers[Index] = PrefabBinding.Capture(Graph, StagedPaths, Source.Renderers[Index], "Surface Textures renderer");
                var Original = RendererMesh(Source.Renderers[Index]);
                var Prepared = RendererMesh(Renderers[Index].Resolve<Renderer>(CaptureResolver, "Staged prefab", "Surface Textures renderer"));
                Meshes[Index] = MeshRecipes.NeedsCurvature(Source.Renderers[Index])
                    ? MeshSemanticIdentity.CapturePrepared(Original, Prepared, MeshRecipes.CurvatureFingerprint(Original), Identities, CaptureResolver, "surface mesh")
                    : MeshSemanticIdentity.CaptureOriginal(Original, Identities, "surface mesh");
                if (Bake) BakedMeshes[Index] = AssetBinding.Capture(Prepared, Identities, "prepared curvature mesh");
            }
            if (!Bake)
                for (var Index = 0; Index < BakedMeshes.Length; Index++)
                    BakedMeshes[Index] = AssetBinding.CaptureOptional(SourceBaked.GetArrayElementAtIndex(Index).objectReferenceValue,
                        Identities, "curvature mesh receipt");
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Actual = Binding.Resolve<PllSurfaceTextureAuthoring>(Resolver, Context, "Surface Textures");
            if (!Actual.enabled || Actual.Target != Target || Actual.Coordinates != Coordinates
                || Actual.Renderers.Count != Renderers.Length || !Actual.Slots.SequenceEqual(Slots))
                throw DefinitionSemanticIdentity.Changed(Context, "Surface Textures recipe");
            Material.Require(Actual.Material, Resolver, Context, "Surface Textures material");
            PrefabBinding.RequireOptional(FluidSource, Actual.FluidSource, Resolver, Context, "Surface Textures fluid source");
            for (var Index = 0; Index < Renderers.Length; Index++)
            {
                Renderers[Index].Require(Actual.Renderers[Index], Resolver, Context, "Surface Textures renderer");
                Meshes[Index].Require(RendererMesh(Actual.Renderers[Index]), Resolver, Context, "surface mesh");
            }
            using var Serialized = new SerializedObject(Actual);
            var Prepared = PllBundleBuilder.Property(Serialized, "BundleBakedMeshes");
            if (PllBundleBuilder.Property(Serialized, "BundleBakeVersion").intValue != BakeVersion
                || Prepared.arraySize != BakedMeshes.Length)
                throw DefinitionSemanticIdentity.Changed(Context, "curvature preparation receipt");
            for (var Index = 0; Index < BakedMeshes.Length; Index++)
                AssetBinding.RequireOptional(BakedMeshes[Index], Prepared.GetArrayElementAtIndex(Index).objectReferenceValue,
                    Resolver, Context, "curvature mesh receipt");
        }

        static Mesh RendererMesh(Renderer Renderer) => Renderer is SkinnedMeshRenderer Skinned ? Skinned.sharedMesh
            : Renderer is MeshRenderer && Renderer.TryGetComponent<MeshFilter>(out var Filter) ? Filter.sharedMesh
            : throw new InvalidOperationException("Surface Textures renderer has no supported mesh owner.");
    }

    sealed class BreakablePropIdentity
    {
        readonly PrefabBinding Binding, Intact, Broken, Particles;
        readonly PrefabBinding[] Supports;
        readonly AssetBinding Clip;
        readonly (string Id, bool Enabled, PllPropBreakTrigger Trigger, float Speed, int Capacity,
            float Overload, PllBridgeCapacity BridgeCapacity, int NativeMaximum, string Hook) Settings;

        internal BreakablePropIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PllBreakablePropAuthoring Source, AssetIdentityCatalog Identities)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "Breakable Prop");
            Settings = Values(Source);
            Intact = PrefabBinding.CaptureOptional(Graph, StagedPaths, Source.IntactModel ? Source.IntactModel.transform : null, "intact model");
            Broken = PrefabBinding.CaptureOptional(Graph, StagedPaths, Source.BrokenModel ? Source.BrokenModel.transform : null, "broken model");
            Particles = PrefabBinding.CaptureOptional(Graph, StagedPaths, Source.BreakParticles, "break particles");
            Clip = AssetBinding.CaptureOptional(Source.BreakClip, Identities, "break clip");
            Supports = new PrefabBinding[Source.SupportColliders?.Length ?? 0];
            for (var Index = 0; Index < Supports.Length; Index++)
                Supports[Index] = PrefabBinding.Capture(Graph, StagedPaths, Source.SupportColliders[Index], "prop support");
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Actual = Binding.Resolve<PllBreakablePropAuthoring>(Resolver, Context, "Breakable Prop");
            if (!Actual.enabled || Values(Actual) != Settings || (Actual.SupportColliders?.Length ?? 0) != Supports.Length)
                throw DefinitionSemanticIdentity.Changed(Context, "Breakable Prop recipe");
            PrefabBinding.RequireOptional(Intact, Actual.IntactModel ? Actual.IntactModel.transform : null, Resolver, Context, "intact model");
            PrefabBinding.RequireOptional(Broken, Actual.BrokenModel ? Actual.BrokenModel.transform : null, Resolver, Context, "broken model");
            PrefabBinding.RequireOptional(Particles, Actual.BreakParticles, Resolver, Context, "break particles");
            AssetBinding.RequireOptional(Clip, Actual.BreakClip, Resolver, Context, "break clip");
            for (var Index = 0; Index < Supports.Length; Index++)
                Supports[Index].Require(Actual.SupportColliders[Index], Resolver, Context, "prop support");
        }

        static (string, bool, PllPropBreakTrigger, float, int, float, PllBridgeCapacity, int, string)
            Values(PllBreakablePropAuthoring Source) => (Source.Id, Source.InitiallyEnabled, Source.Trigger,
                Source.ImpactSpeed, Source.Capacity, Source.OverloadSeconds, Source.BridgeCapacity, Source.NativeMaximumPeople, Source.EmitHook);
    }

    sealed class SwingingPropIdentity
    {
        readonly PrefabBinding Binding, MotionRoot;
        readonly AssetBinding WarningClip;
        readonly (string Id, PllPropMotion Profile, Vector3 Axis, float Center, float Amplitude,
            float Period, float Phase, bool Running, string Started, string Paused) Motion;
        readonly (PllBridgeResponseProfile Profile, float Warning, float Distance, float Amount, float Climbing, float Interval) Response;

        internal SwingingPropIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PllSwingingPropAuthoring Source, AssetIdentityCatalog Identities)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "Swinging Prop");
            MotionRoot = PrefabBinding.CaptureOptional(Graph, StagedPaths, Source.MotionRoot, "prop motion root");
            WarningClip = AssetBinding.CaptureOptional(Source.WarningClip, Identities, "bridge warning clip");
            Motion = MotionValues(Source); Response = ResponseValues(Source);
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Actual = Binding.Resolve<PllSwingingPropAuthoring>(Resolver, Context, "Swinging Prop");
            if (!Actual.enabled || !MotionValues(Actual).Equals(Motion) || !ResponseValues(Actual).Equals(Response))
                throw DefinitionSemanticIdentity.Changed(Context, "Swinging Prop recipe");
            PrefabBinding.RequireOptional(MotionRoot, Actual.MotionRoot, Resolver, Context, "prop motion root");
            AssetBinding.RequireOptional(WarningClip, Actual.WarningClip, Resolver, Context, "bridge warning clip");
        }

        static (string, PllPropMotion, Vector3, float, float, float, float, bool, string, string)
            MotionValues(PllSwingingPropAuthoring Source) => (Source.Id, Source.Profile, Source.Axis, Source.CenterDegrees,
                Source.AmplitudeDegrees, Source.PeriodSeconds, Source.InitialPhase, Source.InitiallyRunning, Source.StartedHook, Source.PausedHook);
        static (PllBridgeResponseProfile, float, float, float, float, float) ResponseValues(PllSwingingPropAuthoring Source) =>
            (Source.BridgeResponse, Source.WarningSeconds, Source.StartShakeDistance, Source.StartShakeAmount,
                Source.ClimbingShakeAmount, Source.ClimbingShakeInterval);
    }

    sealed class VineIdentity
    {
        readonly PrefabBinding Binding, Start, End;
        readonly (string Id, PllVineProfile Profile, PllVineEndpoints Endpoints, float Minimum, float Maximum,
            float Height, float Hang, float Offset, Vector2 Box, PllVineAppearance Appearance) Settings;
        readonly Vector3 ModelStart, ModelEnd, ModelUp;
        readonly int SpanVersion;
        readonly MeshSemanticIdentity Span;

        internal VineIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PllVineAuthoring Source, AssetIdentityCatalog Identities,
            DefinitionBindingResolver CaptureResolver, PreparedMeshRecipeCatalog MeshRecipes)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "Vine / Hanging Bridge");
            Start = PrefabBinding.CaptureOptional(Graph, StagedPaths, Source.StartAnchor, "vine start anchor");
            End = PrefabBinding.CaptureOptional(Graph, StagedPaths, Source.EndAnchor, "vine end anchor");
            Settings = Values(Source);
            if (Source.Appearance == PllVineAppearance.CustomSpan)
            {
                var Prepared = Binding.Resolve<PllVineAuthoring>(CaptureResolver, "Staged prefab", "Vine / Hanging Bridge");
                Span = MeshSemanticIdentity.CapturePrepared(Source.SpanMesh, Prepared.SpanMesh,
                    MeshRecipes.SpanFingerprint(Source.SpanMesh, Source.ModelStart, Source.ModelEnd, Source.ModelUp), Identities, CaptureResolver, "vine span mesh");
                ModelStart = Vector3.zero; ModelEnd = Vector3.forward * Vector3.Distance(Source.ModelStart, Source.ModelEnd);
                ModelUp = Vector3.up; SpanVersion = PllVineAuthoring.SpanNormalizationVersion;
            }
            else
            {
                if (Source.SpanMesh) Span = MeshSemanticIdentity.CaptureOriginal(Source.SpanMesh, Identities, "vine span mesh");
                ModelStart = Source.ModelStart; ModelEnd = Source.ModelEnd; ModelUp = Source.ModelUp; SpanVersion = Source.BundleSpanVersion;
            }
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Actual = Binding.Resolve<PllVineAuthoring>(Resolver, Context, "Vine / Hanging Bridge");
            if (!Actual.enabled || !Values(Actual).Equals(Settings) || !Actual.ModelStart.Equals(ModelStart)
                || !Actual.ModelEnd.Equals(ModelEnd) || !Actual.ModelUp.Equals(ModelUp) || Actual.BundleSpanVersion != SpanVersion)
                throw DefinitionSemanticIdentity.Changed(Context, "Vine / Hanging Bridge recipe");
            PrefabBinding.RequireOptional(Start, Actual.StartAnchor, Resolver, Context, "vine start anchor");
            PrefabBinding.RequireOptional(End, Actual.EndAnchor, Resolver, Context, "vine end anchor");
            if (Span != null) Span.Require(Actual.SpanMesh, Resolver, Context, "vine span mesh");
            else if (Actual.SpanMesh) throw DefinitionSemanticIdentity.Changed(Context, "vine span mesh");
        }

        static (string, PllVineProfile, PllVineEndpoints, float, float, float, float, float, Vector2, PllVineAppearance)
            Values(PllVineAuthoring Source) => (Source.Id, Source.Profile, Source.Endpoints, Source.MinimumDistance,
                Source.MaximumDistance, Source.MaximumHeightDifference, Source.HangAmount, Source.ColliderOffset, Source.BoxShape, Source.Appearance);
    }

    sealed class NativeResolverIdentity
    {
        readonly PrefabBinding Binding;
        readonly PrefabBinding Root, Emitter;
        readonly AssetBinding FluidDefinition;
        readonly bool InitiallyEnabled;
        readonly PrefabBinding[] SurfaceRenderers, ContactFrames, ContactProxies;
        readonly int[] SurfaceSlots;
        readonly PllFluidContactMode ContactMode;
        readonly PllZoneVolume[] ContactRegions;
        readonly PLLNativeResolverKind Kind;
        readonly PllNativeResolverForm Form;
        readonly PllNativeFlowProfile Profile;
        readonly PllNativeFlowSettings Scalars;
        readonly PllZoneVolume Volume;
        readonly float CoverDelaySeconds, HeatRate, Heat;
        readonly bool IsKiln;
        readonly PrefabBinding[] Anchors;
        readonly AssetBinding[] Outputs, Details;
        readonly PrefabBinding Surface, Presentation;
        readonly AssetBinding GeneratedSurface;
        readonly (float Shallow, float Deep, float AnimShallow, float AnimDeep) WaterModifiers;

        internal NativeResolverIdentity(PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths, PLLNativeResolver Source,
            AssetIdentityCatalog Identities,
            IReadOnlyDictionary<PllFluidDefinition, PllFluidDefinition> PreparedFluids)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "nativeResolver");
            Root = PrefabBinding.Capture(Graph, StagedPaths, Source.Root.transform, "native resolver root");
            Emitter = PrefabBinding.CaptureOptional(Graph, StagedPaths,
                Source.EmitterModel ? Source.EmitterModel.transform : null, "fluid emitter model");
            if (Source.FluidDefinition)
                FluidDefinition = AssetBinding.Capture(PreparedFluids[Source.FluidDefinition], Identities, "Fluid Definition");
            InitiallyEnabled = Source.InitiallyEnabled; ContactMode = Source.ContactMode;
            SurfaceSlots = Source.SurfaceSlots.ToArray();
            SurfaceRenderers = new PrefabBinding[Source.SurfaceRenderers.Count];
            for (var Index = 0; Index < SurfaceRenderers.Length; Index++)
                SurfaceRenderers[Index] = PrefabBinding.Capture(Graph, StagedPaths, Source.SurfaceRenderers[Index], "fluid surface renderer");
            ContactFrames = new PrefabBinding[Source.ContactRegionCount];
            ContactRegions = new PllZoneVolume[ContactFrames.Length];
            for (var Index = 0; Index < ContactFrames.Length; Index++)
            {
                ContactFrames[Index] = PrefabBinding.Capture(Graph, StagedPaths, Source.ContactFrames[Index], "fluid contact frame");
                ContactRegions[Index] = Source.ContactRegion(Index);
            }
            ContactProxies = new PrefabBinding[Source.ContactProxies.Count];
            for (var Index = 0; Index < ContactProxies.Length; Index++)
                ContactProxies[Index] = PrefabBinding.Capture(Graph, StagedPaths, Source.ContactProxies[Index], "fluid contact proxy");
            Kind = Source.Kind; Form = Source.Form; Profile = Source.SourceProfile; Volume = Source.Volume;
            CoverDelaySeconds = Source.Transition.CoverDelaySeconds;
            HeatRate = Source.LavaHeatRate; Heat = Source.LavaHeat; IsKiln = Source.LavaIsKiln;
            if (Source.SurfaceRenderer) Surface = PrefabBinding.Capture(Graph, StagedPaths, Source.SurfaceRenderer, "native surface");
            if (Source.Presentation) Presentation = PrefabBinding.Capture(Graph, StagedPaths, Source.Presentation, "native presentation");
            if (Source.GeneratedSurfaceMaterial) GeneratedSurface = AssetBinding.Capture(Source.GeneratedSurfaceMaterial, Identities, "native generated surface material");
            WaterModifiers = (Source.MovementModifierShallow, Source.MovementModifierDeep,
                Source.AnimModifierShallow, Source.AnimModifierDeep);
            var Settings = Source.SourceSettings;
            Scalars = ScalarSettings(Settings);
            Anchors = new PrefabBinding[3];
            var SourceAnchors = new[] { Settings.RayStart, Settings.RayEnd, Settings.Endpoint };
            for (var i = 0; i < Anchors.Length; i++)
                if (SourceAnchors[i]) Anchors[i] = PrefabBinding.Capture(Graph, StagedPaths, SourceAnchors[i], "flow anchor");
            Outputs = new AssetBinding[5];
            var Inputs = new Object[] { Settings.EndRock, Settings.Splash, Settings.SpawnAlongSpline, Settings.Material, Settings.PusherSfx };
            for (var i = 0; i < Outputs.Length; i++)
                if (Inputs[i]) Outputs[i] = AssetBinding.Capture(Inputs[i], Identities, "flow input");
            Details = new AssetBinding[Settings.DetailsAlongSpline?.Length ?? 0];
            for (var i = 0; i < Details.Length; i++)
                Details[i] = AssetBinding.Capture(Settings.DetailsAlongSpline[i], Identities, "flow detail input");
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Actual = Binding.Resolve<PLLNativeResolver>(Resolver, Context, "nativeResolver");
            Root.Require(Actual.Root ? Actual.Root.transform : null, Resolver, Context, "native resolver root");
            PrefabBinding.RequireOptional(Emitter, Actual.EmitterModel ? Actual.EmitterModel.transform : null,
                Resolver, Context, "fluid emitter model");
            AssetBinding.RequireOptional(FluidDefinition, Actual.FluidDefinition, Resolver, Context, "Fluid Definition");
            Actual.ValidateContactArrays();
            if (Actual.InitiallyEnabled != InitiallyEnabled || Actual.ContactMode != ContactMode
                || !Actual.SurfaceSlots.SequenceEqual(SurfaceSlots)
                || Actual.SurfaceRenderers.Count != SurfaceRenderers.Length
                || Actual.ContactRegionCount != ContactFrames.Length || Actual.ContactProxies.Count != ContactProxies.Length)
                throw DefinitionSemanticIdentity.Changed(Context, "fluid source settings and contacts");
            for (var Index = 0; Index < SurfaceRenderers.Length; Index++)
                SurfaceRenderers[Index].Require(Actual.SurfaceRenderers[Index], Resolver, Context, "fluid surface renderer");
            for (var Index = 0; Index < ContactFrames.Length; Index++)
            {
                ContactFrames[Index].Require(Actual.ContactFrames[Index], Resolver, Context, "fluid contact frame");
                if (!Actual.ContactRegion(Index).Equals(ContactRegions[Index]))
                    throw DefinitionSemanticIdentity.Changed(Context, "fluid contact region");
            }
            for (var Index = 0; Index < ContactProxies.Length; Index++)
                ContactProxies[Index].Require(Actual.ContactProxies[Index], Resolver, Context, "fluid contact proxy");
            var Settings = Actual.SourceSettings;
            if (Surface != null) Surface.Require(Actual.SurfaceRenderer, Resolver, Context, "native surface");
            else if (Actual.SurfaceRenderer) throw DefinitionSemanticIdentity.Changed(Context, "native surface");
            if (Presentation != null) Presentation.Require(Actual.Presentation, Resolver, Context, "native presentation");
            else if (Actual.Presentation) throw DefinitionSemanticIdentity.Changed(Context, "native presentation");
            if (GeneratedSurface != null) GeneratedSurface.Require(Actual.GeneratedSurfaceMaterial, Resolver, Context, "native generated surface material");
            else if (Actual.GeneratedSurfaceMaterial) throw DefinitionSemanticIdentity.Changed(Context, "native generated surface material");
            if ((Actual.MovementModifierShallow, Actual.MovementModifierDeep,
                    Actual.AnimModifierShallow, Actual.AnimModifierDeep) != WaterModifiers)
                throw DefinitionSemanticIdentity.Changed(Context, "native water modifiers");
            if (Actual.Kind != Kind || Actual.Form != Form || Actual.SourceProfile != Profile
                || !ScalarSettings(Settings).Equals(Scalars) || !Actual.Volume.Equals(Volume)
                || Actual.Transition.CoverDelaySeconds != CoverDelaySeconds
                || Actual.LavaHeatRate != HeatRate || Actual.LavaHeat != Heat || Actual.LavaIsKiln != IsKiln)
                throw DefinitionSemanticIdentity.Changed(Context, "nativeResolver settings");
            var ActualAnchors = new[] { Settings.RayStart, Settings.RayEnd, Settings.Endpoint };
            for (var i = 0; i < Anchors.Length; i++)
                if (Anchors[i] != null) Anchors[i].Require(ActualAnchors[i], Resolver, Context, "flow anchor");
                else if (ActualAnchors[i]) throw DefinitionSemanticIdentity.Changed(Context, "flow anchor");
            var Inputs = new Object[] { Settings.EndRock, Settings.Splash, Settings.SpawnAlongSpline, Settings.Material, Settings.PusherSfx };
            for (var i = 0; i < Outputs.Length; i++)
                if (Outputs[i] != null) Outputs[i].Require(Inputs[i], Resolver, Context, "flow input");
                else if (Inputs[i]) throw DefinitionSemanticIdentity.Changed(Context, "flow input");
            if ((Settings.DetailsAlongSpline?.Length ?? 0) != Details.Length)
                throw DefinitionSemanticIdentity.Changed(Context, "flow detail input count");
            for (var i = 0; i < Details.Length; i++)
                Details[i].Require(Settings.DetailsAlongSpline[i], Resolver, Context, "flow detail input");
            Actual.ValidateBundleSerialization();
        }

        static PllNativeFlowSettings ScalarSettings(PllNativeFlowSettings Settings)
        {
            Settings.RayStart = null; Settings.RayEnd = null; Settings.Endpoint = null;
            Settings.EndRock = null; Settings.Splash = null; Settings.SpawnAlongSpline = null;
            Settings.Material = null; Settings.PusherSfx = null; Settings.DetailsAlongSpline = null;
            return Settings;
        }
    }

    sealed class FogZoneIdentity
    {
        readonly PrefabBinding Binding;
        readonly string StableId;
        readonly PllZoneVolume Volume;
        readonly bool InitiallyEnabled;
        readonly float VisibilityDistance, BoundaryBlendDistance, CoverDelaySeconds;
        readonly Color Color;
        readonly int Priority;

        internal FogZoneIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PllFogZoneAuthoring Source)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "fogZone");
            StableId = Source.StableId; Volume = Source.Volume;
            InitiallyEnabled = Source.InitiallyEnabled; VisibilityDistance = Source.VisibilityDistance;
            Color = Source.Color; BoundaryBlendDistance = Source.BoundaryBlendDistance;
            Priority = Source.Priority; CoverDelaySeconds = Source.Transition.CoverDelaySeconds;
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Actual = Binding.Resolve<PllFogZoneAuthoring>(Resolver, Context, "fogZone");
            if (Actual.StableId != StableId || !Actual.Volume.Equals(Volume)
                || Actual.InitiallyEnabled != InitiallyEnabled || Actual.VisibilityDistance != VisibilityDistance
                || !Actual.Color.Equals(Color) || Actual.BoundaryBlendDistance != BoundaryBlendDistance
                || Actual.Priority != Priority || Actual.Transition.CoverDelaySeconds != CoverDelaySeconds)
                throw DefinitionSemanticIdentity.Changed(Context, "fogZone settings");
            Actual.ValidateSettings();
            Actual.ValidateBundleSerialization();
        }
    }

    sealed class PresentationTransitionIdentity
    {
        readonly PrefabBinding Binding;
        readonly float CoverDelaySeconds;

        internal PresentationTransitionIdentity(PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths, Component Source)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "presentation transition");
            CoverDelaySeconds = Duration(Source);
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Source = Binding.Resolve<Component>(Resolver, Context, "presentation transition");
            if (Duration(Source) != CoverDelaySeconds)
                throw DefinitionSemanticIdentity.Changed(Context, "presentation Cover Delay");
            switch (Source)
            {
                case PllEffectAuthoring Effect: Effect.ValidateBundleSerialization(); break;
                case PllEffectZoneAuthoring Zone: Zone.ValidateBundleSerialization(); break;
                case PllVisualAuthoring Visual: Visual.ValidateBundleSerialization(); break;
                case PllSoundEmitterAuthoring Emitter: Emitter.ValidateBundleSerialization(); break;
                case PllBiomeStingerAuthoring Stinger: Stinger.ValidateBundleSerialization(); break;
                case PllCustomVisualAuthoring Visual: Visual.ValidateBundleSerialization(); break;
                case PLLNativeVisualResolver Visual: Visual.ValidateBundleSerialization(); break;
            }
        }

        static float Duration(Component Source) => Source switch
        {
            PllEffectAuthoring Effect => Effect.Transition.CoverDelaySeconds,
            PllEffectZoneAuthoring Zone => Zone.Transition.CoverDelaySeconds,
            PllVisualAuthoring Visual => Visual.Transition.CoverDelaySeconds,
            PllSoundEmitterAuthoring Emitter => Emitter.Transition.CoverDelaySeconds,
            PllBiomeStingerAuthoring Stinger => Stinger.Transition.CoverDelaySeconds,
            PllCustomVisualAuthoring Visual => Visual.Transition.CoverDelaySeconds,
            PLLNativeVisualResolver Visual => Visual.Transition.CoverDelaySeconds,
            _ => throw new InvalidOperationException("Presentation transition source is unsupported.")
        };
    }

    sealed class HookedGenerationIdentity
    {
        readonly PrefabBinding Binding;
        readonly bool GenerateOnLoad;
        readonly string Hook;

        internal HookedGenerationIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PllHookedPropGenerationAuthoring Source)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "hookedPropGeneration");
            GenerateOnLoad = Source.GenerateOnLoad; Hook = Source.EmitHook;
        }
        internal void Require(PllHookedPropGenerationAuthoring Actual, DefinitionBindingResolver Resolver, string Context)
        {
            Binding.Require(Actual, Resolver, Context, "hookedPropGeneration");
            if (Actual.GenerateOnLoad != GenerateOnLoad || Actual.EmitHook != Hook)
                throw DefinitionSemanticIdentity.Changed(Context, "hookedPropGeneration");
        }
    }

    sealed class GeneratedPropCatalogIdentity
    {
        readonly ReadOnlyMemory<byte> Bytes;
        readonly PrefabBinding[] Prefabs;
        readonly AssetBinding[] Meshes;
        readonly AssetBinding[] Materials;
        readonly AssetBinding[] PhysicsMaterials;

        internal GeneratedPropCatalogIdentity(PllHookedPropGenerationCompiler.CompletedCatalog Catalog,
            AssetIdentityCatalog Identities, PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths)
        {
            Bytes = Catalog.Bytes;
            Prefabs = new PrefabBinding[Catalog.Prefabs.Count];
            var SourceByStagedPath = new Dictionary<string, PrefabNode>(StringComparer.Ordinal);
            if (Prefabs.Length != 0)
                foreach (var Node in Graph.Nodes)
                    SourceByStagedPath.Add(StagedPaths[Node.SourcePath], Node);
            for (var Index = 0; Index < Prefabs.Length; Index++)
                Prefabs[Index] = PrefabBinding.CaptureNode(Graph, StagedPaths,
                    SourceByStagedPath[AssetDatabase.GetAssetPath(Catalog.Prefabs[Index])]);
            Meshes = CaptureAssets(Catalog.Meshes, PllHookedPropGenerationCompiler.MeshesField);
            Materials = CaptureAssets(Catalog.Materials, PllHookedPropGenerationCompiler.MaterialsField);
            PhysicsMaterials = CaptureAssets(Catalog.PhysicsMaterials, PllHookedPropGenerationCompiler.PhysicsMaterialsField);

            AssetBinding[] CaptureAssets<T>(IReadOnlyList<T> Assets, string Field) where T : Object
            {
                var Bindings = new AssetBinding[Assets.Count];
                for (var Index = 0; Index < Bindings.Length; Index++)
                    Bindings[Index] = AssetBinding.Capture(Assets[Index], Identities, Field + "[" + Index + "]");
                return Bindings;
            }
        }

        internal void Require(PllBiomeDefinition Actual, DefinitionBindingResolver Resolver, string Context)
        {
            using (var Serialized = new SerializedObject(Actual))
            {
                var Payload = PllBundleBuilder.Property(Serialized, PllHookedPropGenerationCompiler.CatalogField);
                if (Payload.arraySize != Bytes.Length)
                    throw DefinitionSemanticIdentity.Changed(Context, PllHookedPropGenerationCompiler.CatalogField);
                var Expected = Bytes.Span;
                for (var Index = 0; Index < Expected.Length; Index++)
                    if (Payload.GetArrayElementAtIndex(Index).intValue != Expected[Index])
                        throw DefinitionSemanticIdentity.Changed(Context, PllHookedPropGenerationCompiler.CatalogField);
                var PrefabRows = PllBundleBuilder.Property(Serialized, PllHookedPropGenerationCompiler.PrefabsField);
                if (PrefabRows.arraySize != Prefabs.Length)
                    throw DefinitionSemanticIdentity.Changed(Context, PllHookedPropGenerationCompiler.PrefabsField);
                for (var Index = 0; Index < Prefabs.Length; Index++)
                    Prefabs[Index].Require(PrefabRows.GetArrayElementAtIndex(Index).objectReferenceValue,
                        Resolver, Context, PllHookedPropGenerationCompiler.PrefabsField + "[" + Index + "]");
                RequireAssets(Meshes, PllHookedPropGenerationCompiler.MeshesField);
                RequireAssets(Materials, PllHookedPropGenerationCompiler.MaterialsField);
                RequireAssets(PhysicsMaterials, PllHookedPropGenerationCompiler.PhysicsMaterialsField);

                void RequireAssets(AssetBinding[] Bindings, string Field)
                {
                    var Rows = PllBundleBuilder.Property(Serialized, Field);
                    if (Rows.arraySize != Bindings.Length)
                        throw DefinitionSemanticIdentity.Changed(Context, Field);
                    for (var Index = 0; Index < Bindings.Length; Index++)
                        Bindings[Index].Require(Rows.GetArrayElementAtIndex(Index).objectReferenceValue,
                            Resolver, Context, Field + "[" + Index + "]");
                }
            }
        }
    }

    sealed class DefinitionBindingResolver
    {
        readonly AssetBundle Bundle;
        readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        readonly Dictionary<Mesh, string> MeshFingerprints = new Dictionary<Mesh, string>();
        readonly List<(string Identity, GameObject Root)> ReopenedPrefabs =
            new List<(string Identity, GameObject Root)>();
        readonly List<(int Identity, Object Value)> ReopenedAssets =
            new List<(int Identity, Object Value)>();
        readonly List<(string Identity, RuntimeAnimatorController Value)>
            ReopenedControllers = new List<(
                string Identity, RuntimeAnimatorController Value)>();

        internal DefinitionBindingResolver(AssetBundle Bundle)
        {
            this.Bundle = Bundle;
        }

        internal bool Reopened => Bundle != null;

        internal string FingerprintMesh(Mesh Mesh)
        {
            if (MeshFingerprints.TryGetValue(Mesh, out var Fingerprint)) return Fingerprint;
            Fingerprint = PllSurfaceCurvatureBaker.FingerprintMesh(Mesh);
            MeshFingerprints.Add(Mesh, Fingerprint); return Fingerprint;
        }

        internal GameObject Prefab(string Path)
        {
            if (Prefabs.TryGetValue(Path, out var Result)) return Result;
            Result = Bundle
                ? Bundle.LoadAsset<GameObject>(Path.ToLowerInvariant())
                : AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (!Result) throw new InvalidOperationException("Semantic prefab binding '" + Path + "' is unavailable.");
            Prefabs.Add(Path, Result);
            return Result;
        }

        internal void RequirePrefab(
            string Identity, GameObject Root, string Context, string Field)
        {
            if (!Bundle) return;
            foreach (var Binding in ReopenedPrefabs)
            {
                if (string.Equals(Binding.Identity, Identity, StringComparison.Ordinal))
                {
                    if (!ReferenceEquals(Binding.Root, Root))
                        throw DefinitionSemanticIdentity.Changed(Context, Field);
                    return;
                }
                if (ReferenceEquals(Binding.Root, Root))
                    throw DefinitionSemanticIdentity.Changed(Context, Field);
            }
            ReopenedPrefabs.Add((Identity, Root));
        }

        internal void RequireGeneratedController(
            string Identity, RuntimeAnimatorController Actual,
            string Context, string Field)
        {
            if (!Bundle || !Actual
                || !string.Equals(
                    Actual.name, Identity, StringComparison.Ordinal))
                throw DefinitionSemanticIdentity.Changed(Context, Field);
            foreach (var Binding in ReopenedControllers)
            {
                if (string.Equals(
                        Binding.Identity, Identity,
                        StringComparison.Ordinal))
                {
                    if (!ReferenceEquals(Binding.Value, Actual))
                        throw DefinitionSemanticIdentity.Changed(
                            Context, Field);
                    return;
                }
                if (ReferenceEquals(Binding.Value, Actual))
                    throw DefinitionSemanticIdentity.Changed(
                        Context, Field);
            }
            ReopenedControllers.Add((Identity, Actual));
        }

        internal void RequireAsset(
            Object Actual, Object Source, Type Type, int Identity,
            string Context, string Field)
        {
            if (!Bundle)
            {
                if (!Actual || !ReferenceEquals(Actual, Source))
                    throw DefinitionSemanticIdentity.Changed(Context, Field);
                return;
            }
            if (!Actual || Actual.GetType() != Type)
                throw DefinitionSemanticIdentity.Changed(Context, Field);
            foreach (var Binding in ReopenedAssets)
            {
                if (Binding.Identity == Identity)
                {
                    if (!ReferenceEquals(Binding.Value, Actual))
                        throw DefinitionSemanticIdentity.Changed(Context, Field);
                    return;
                }
                if (ReferenceEquals(Binding.Value, Actual))
                    throw DefinitionSemanticIdentity.Changed(Context, Field);
            }
            ReopenedAssets.Add((Identity, Actual));
        }
    }

    sealed class PrefabBinding
    {
        readonly string Identity;
        readonly string Path;
        readonly string RootPath;
        readonly bool Addressable;
        readonly PrefabEdgeBinding[] NodePath;
        readonly int[] TransformPath;
        readonly Type ComponentType;
        readonly int ComponentIndex;

        PrefabBinding(
            string Identity, string Path, string RootPath,
            bool Addressable, PrefabEdgeBinding[] NodePath,
            int[] TransformPath, Type ComponentType,
            int ComponentIndex)
        {
            this.Identity = Identity;
            this.Path = Path;
            this.RootPath = RootPath;
            this.Addressable = Addressable;
            this.NodePath = NodePath;
            this.TransformPath = TransformPath;
            this.ComponentType = ComponentType;
            this.ComponentIndex = ComponentIndex;
        }

        internal static PrefabBinding CaptureRoot(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths) =>
            CaptureNode(Graph, StagedPaths, Graph.ByPath[Graph.RootPath]);

        internal static PrefabBinding CaptureNode(PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths, PrefabNode Node)
        {
            var RootPath = StagedPaths[Graph.RootPath];
            return new PrefabBinding(
                Node.SourcePath, StagedPaths[Node.SourcePath], RootPath, Node.IsRoot,
                PrefabEdgeBinding.CapturePath(Graph, Node.SourcePath),
                Array.Empty<int>(), null, -1);
        }

        internal static PrefabBinding Capture(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            Component Value, string Field)
        {
            var Node = OwnerNode(Graph, Value, Field);
            var Components = Value.transform.GetComponents(Value.GetType());
            var ComponentIndex = Array.FindIndex(
                Components, Candidate => ReferenceEquals(Candidate, Value));
            if (ComponentIndex < 0)
                throw new InvalidOperationException(
                    "Cannot capture semantic component binding '" + Field + "'.");
            return new PrefabBinding(
                Node.SourcePath, StagedPaths[Node.SourcePath],
                StagedPaths[Graph.RootPath], Node.IsRoot,
                PrefabEdgeBinding.CapturePath(Graph, Node.SourcePath),
                PllHierarchy.SiblingPath(
                    Node.SourceRoot.transform, Value.transform,
                    "Semantic component binding '" + Field
                        + "' is outside its prefab root."),
                Value.GetType(), ComponentIndex);
        }

        internal static PrefabBinding CaptureOptional(PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths, Component Value, string Field) =>
            Value ? Capture(Graph, StagedPaths, Value, Field) : null;

        internal static void RequireOptional(PrefabBinding Expected, Object Actual,
            DefinitionBindingResolver Resolver, string Context, string Field)
        {
            if (Expected != null) Expected.Require(Actual, Resolver, Context, Field);
            else if (Actual) throw DefinitionSemanticIdentity.Changed(Context, Field);
        }

        internal T Resolve<T>(
            DefinitionBindingResolver Resolver,
            string Context, string Field) where T : Object
        {
            var Expected = Resolve(Resolver, Context, Field);
            if (Expected is not T Result)
                throw DefinitionSemanticIdentity.Changed(Context, Field);
            return Result;
        }

        internal void Require(
            Object Actual, DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            if (!Actual || Actual != Resolve(Resolver, Context, Field))
                throw DefinitionSemanticIdentity.Changed(Context, Field);
        }

        Object Resolve(
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            var ExpectedRoot = !Resolver.Reopened || Addressable
                ? Resolver.Prefab(Path)
                : ResolveNode(Resolver, Context, Field);
            Resolver.RequirePrefab(
                Identity, ExpectedRoot, Context, Field);
            Object Expected = ExpectedRoot;
            if (ComponentType == null) return Expected;
            var Transform = ExpectedRoot.transform;
            foreach (var Index in TransformPath)
            {
                if (Index < 0 || Index >= Transform.childCount)
                    throw DefinitionSemanticIdentity.Changed(
                        Context, Field);
                Transform = Transform.GetChild(Index);
            }
            var Components = Transform.GetComponents(ComponentType);
            if (ComponentIndex < 0
                || ComponentIndex >= Components.Length)
                throw DefinitionSemanticIdentity.Changed(Context, Field);
            Expected = Components[ComponentIndex];
            if (!Expected)
                throw DefinitionSemanticIdentity.Changed(Context, Field);
            return Expected;
        }

        GameObject ResolveNode(
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            var Result = Resolver.Prefab(RootPath);
            foreach (var Edge in NodePath)
                Result = Edge.Resolve(Result, Context, Field);
            return Result;
        }
    }

    sealed class PrefabEdgeBinding
    {
        readonly int[] TransformPath;
        readonly Type ComponentType;
        readonly int ComponentIndex;
        readonly int CandidateIndex;

        PrefabEdgeBinding(
            int[] TransformPath, Type ComponentType,
            int ComponentIndex, int CandidateIndex)
        {
            this.TransformPath = TransformPath;
            this.ComponentType = ComponentType;
            this.ComponentIndex = ComponentIndex;
            this.CandidateIndex = CandidateIndex;
        }

        internal static PrefabEdgeBinding[] CapturePath(
            PrefabGraph Graph, string TargetPath)
        {
            if (string.Equals(
                    Graph.RootPath, TargetPath,
                    StringComparison.Ordinal))
                return Array.Empty<PrefabEdgeBinding>();
            var Pending = new Queue<(
                string Path, PrefabEdgeBinding[] Edges)>();
            var Seen = new HashSet<string>(StringComparer.Ordinal);
            Pending.Enqueue((
                Graph.RootPath, Array.Empty<PrefabEdgeBinding>()));
            while (Pending.Count != 0)
            {
                var Current = Pending.Dequeue();
                if (!Seen.Add(Current.Path)) continue;
                var Node = Graph.ByPath[Current.Path];
                foreach (var Edge in Node.Edges)
                {
                    var Edges = new PrefabEdgeBinding[
                        Current.Edges.Length + 1];
                    Array.Copy(
                        Current.Edges, Edges,
                        Current.Edges.Length);
                    Edges[Edges.Length - 1] = Capture(Node, Edge);
                    if (string.Equals(
                            Edge.TargetPath, TargetPath,
                            StringComparison.Ordinal)) return Edges;
                    Pending.Enqueue((Edge.TargetPath, Edges));
                }
            }
            throw new InvalidOperationException(
                "Cannot capture unreachable semantic prefab node '"
                    + TargetPath + "'.");
        }

        internal GameObject Resolve(
            GameObject Root, string Context, string Field)
        {
            var Transform = Root.transform;
            foreach (var Index in TransformPath)
            {
                if (Index < 0 || Index >= Transform.childCount)
                    throw DefinitionSemanticIdentity.Changed(
                        Context, Field);
                Transform = Transform.GetChild(Index);
            }
            var Components = Transform.GetComponents(ComponentType);
            if (ComponentIndex < 0
                || ComponentIndex >= Components.Length)
                throw DefinitionSemanticIdentity.Changed(Context, Field);
            GameObject Result;
            if (Components[ComponentIndex] is PropSpawner Spawner)
            {
                var Candidates = Spawner.props;
                if (Candidates == null
                    || CandidateIndex < 0
                    || CandidateIndex >= Candidates.Length)
                    throw DefinitionSemanticIdentity.Changed(
                        Context, Field);
                Result = Candidates[CandidateIndex];
            }
            else if (Components[ComponentIndex]
                is PllChildPropAuthoring ChildProps)
            {
                if (CandidateIndex < 0
                    || CandidateIndex >= ChildProps.Props.Count)
                    throw DefinitionSemanticIdentity.Changed(
                        Context, Field);
                Result = ChildProps.Props[CandidateIndex];
            }
            else if (Components[ComponentIndex] is PllEntityZoneAuthoring Entity
                && Entity.Source is PllAuthoredEntityZoneSource AuthoredEntity)
                Result = AuthoredEntity.Prefab;
            else if (Components[ComponentIndex] is PllHazardZoneAuthoring Hazard
                && Hazard.Source is PllAuthoredHazardZoneSource AuthoredHazard)
                Result = AuthoredHazard.Prefab;
            else
                throw DefinitionSemanticIdentity.Changed(Context, Field);
            if (!Result || Result.transform.parent)
                throw DefinitionSemanticIdentity.Changed(Context, Field);
            return Result;
        }

        static PrefabEdgeBinding Capture(
            PrefabNode Node, PrefabEdge Edge)
        {
            Component Source = Edge.Spawner
                ? (Component)Edge.Spawner : Edge.ChildProps ? (Component)Edge.ChildProps : Edge.ActorZone;
            var Components = Source.transform.GetComponents(
                Source.GetType());
            var ComponentIndex = Array.FindIndex(
                Components,
                Candidate => ReferenceEquals(Candidate, Source));
            if (ComponentIndex < 0)
                throw new InvalidOperationException(
                    "Cannot capture semantic prefab edge.");
            return new PrefabEdgeBinding(
                PllHierarchy.SiblingPath(
                    Node.SourceRoot.transform, Source.transform,
                    "Semantic prefab edge is outside its source root."),
                Source.GetType(), ComponentIndex, Edge.Index);
        }
    }

    sealed class AssetIdentityCatalog
    {
        readonly List<Object> Values = new List<Object>();

        internal int Add(Object Value, string Field)
        {
            if (!Value) throw new InvalidOperationException("Cannot capture missing semantic asset '" + Field + "'.");
            for (var Index = 0; Index < Values.Count; Index++)
                if (ReferenceEquals(Values[Index], Value)) return Index;
            Values.Add(Value);
            return Values.Count - 1;
        }
    }

    sealed class AssetBinding
    {
        readonly Object Source;
        readonly Type Type;
        readonly int Identity;

        AssetBinding(Object Source, Type Type, int Identity)
        {
            this.Source = Source;
            this.Type = Type;
            this.Identity = Identity;
        }

        internal static AssetBinding Capture(
            Object Value, AssetIdentityCatalog Identities, string Field)
        {
            var Identity = Identities.Add(Value, Field);
            return new AssetBinding(Value, Value.GetType(), Identity);
        }

        internal static AssetBinding CaptureOptional(Object Value, AssetIdentityCatalog Identities, string Field) =>
            Value ? Capture(Value, Identities, Field) : null;

        internal static void RequireOptional(AssetBinding Expected, Object Actual,
            DefinitionBindingResolver Resolver, string Context, string Field)
        {
            if (Expected != null) Expected.Require(Actual, Resolver, Context, Field);
            else if (Actual) throw DefinitionSemanticIdentity.Changed(Context, Field);
        }

        internal void Require(
            Object Actual, DefinitionBindingResolver Resolver, string Context, string Field) =>
            Resolver.RequireAsset(Actual, Source, Type, Identity, Context, Field);
    }

    sealed class ActorDefinitionIdentity
    {
        readonly PrefabBinding Binding;
        readonly PrefabBinding Model;
        readonly string Id;
        readonly bool InitiallyEnabled;
        readonly string Metadata;
        readonly ActorInputBinding[] Inputs;
        readonly bool Hazard;

        internal ActorDefinitionIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            Component Source, AssetIdentityCatalog Assets)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "actor definition");
            Hazard = Source is PllHazardAuthoring;
            var ModelRoot = Hazard ? ((PllHazardAuthoring)Source).ModelRoot : ((PllEntityAuthoring)Source).ModelRoot;
            Id = Hazard ? ((PllHazardAuthoring)Source).Id : ((PllEntityAuthoring)Source).Id;
            InitiallyEnabled = Hazard ? ((PllHazardAuthoring)Source).InitiallyEnabled : ((PllEntityAuthoring)Source).InitiallyEnabled;
            Model = PrefabBinding.Capture(Graph, StagedPaths, ModelRoot.transform, "actor model");
            object Behavior = Hazard ? ((PllHazardAuthoring)Source).Behavior : ((PllEntityAuthoring)Source).Behavior;
            Metadata = Behavior.GetType().FullName + ":" + ScalarJson(JsonUtility.ToJson(Behavior));
            var References = ReferencesFor(Behavior);
            Inputs = new ActorInputBinding[References.Count];
            for (var Index = 0; Index < Inputs.Length; Index++)
                Inputs[Index] = ActorInputBinding.Capture(Graph, StagedPaths, References[Index], Assets);
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Source = Binding.Resolve<Component>(Resolver, Context, "actor definition");
            object Behavior;
            if (Hazard && Source is PllHazardAuthoring HazardSource)
            {
                if (HazardSource.Id != Id || HazardSource.InitiallyEnabled != InitiallyEnabled)
                    throw DefinitionSemanticIdentity.Changed(Context, "Hazard definition");
                Model.Require(HazardSource.ModelRoot.transform, Resolver, Context, "Hazard model");
                Behavior = HazardSource.Behavior;
                if (PllHazardValueContract.Failure(HazardSource) is string Failure) throw new InvalidOperationException(Failure);
            }
            else if (!Hazard && Source is PllEntityAuthoring EntitySource)
            {
                if (EntitySource.Id != Id || EntitySource.InitiallyEnabled != InitiallyEnabled)
                    throw DefinitionSemanticIdentity.Changed(Context, "Entity definition");
                Model.Require(EntitySource.ModelRoot.transform, Resolver, Context, "Entity model");
                Behavior = EntitySource.Behavior;
                if (PllEntityValueContract.Failure(EntitySource) is string Failure) throw new InvalidOperationException(Failure);
            }
            else throw DefinitionSemanticIdentity.Changed(Context, "actor definition type");
            if (Metadata != Behavior.GetType().FullName + ":" + ScalarJson(JsonUtility.ToJson(Behavior)))
                throw DefinitionSemanticIdentity.Changed(Context, "actor behavior settings");
            var References = ReferencesFor(Behavior);
            if (References.Count != Inputs.Length) throw DefinitionSemanticIdentity.Changed(Context, "actor inputs");
            for (var Index = 0; Index < Inputs.Length; Index++)
                if (Inputs[Index] is { } Input) Input.Require(References[Index], Resolver, Context);
                else if (References[Index]) throw DefinitionSemanticIdentity.Changed(Context, "optional actor input");
        }

        internal static string ScalarJson(string Json) => Regex.Replace(Json, "\"instanceID\"\\s*:\\s*-?[0-9]+", "\"instanceID\":0", RegexOptions.CultureInvariant);

        static List<Object> ReferencesFor(object Behavior)
        {
            var Values = new List<Object>();
            PllNativeEntityAppearance Appearance = null;
            switch (Behavior)
            {
                case PllContactInjuryBehavior Contact:
                    Values.Add(Contact.Contact); Values.Add(Contact.Contact.sharedMaterial);
                    if (Contact.Contact is MeshCollider ContactMesh) Values.Add(ContactMesh.sharedMesh);
                    break;
                case PllArrowShooterBehavior Arrow:
                    Values.Add(Arrow.Muzzle); Values.Add(Arrow.ArrowPrefab); Values.Add(Arrow.TrailParticles);
                    Values.Add(Arrow.FiredParticles); Values.Add(Arrow.EmptyParticles);
                    Values.Add(Arrow.WarningLineMaterial); Values.Add(Arrow.ColorblindLineMaterial);
                    Values.AddRange(Arrow.ShotClips); break;
                case PllMovingSawBehavior Saw:
                    Values.Add(Saw.Rail); Values.Add(Saw.RailStart); Values.Add(Saw.RailEnd);
                    Values.Add(Saw.Blade); Values.Add(Saw.BladeVisual); Values.Add(Saw.Hitbox); Values.Add(Saw.HitClip); break;
                case PllSpikeTrapBehavior Spike:
                    Values.Add(Spike.Spikes); Values.Add(Spike.ActivePose); Values.Add(Spike.ReadyPose);
                    Values.Add(Spike.RestPose); Values.Add(Spike.SupportCollider); Values.Add(Spike.DetectionTrigger);
                    Values.AddRange(Spike.AttackClips); Values.AddRange(Spike.RestClips); break;
                case PllBeetleBehavior Beetle: Appearance = Beetle.Appearance; break;
                case PllCondorBehavior Condor: Appearance = Condor.Appearance; break;
                case PllMushroomZombieBehavior or PllPluginEntityBehavior or PllPluginHazardBehavior: break;
                default: throw new InvalidOperationException("Actor behavior identity is unsupported.");
            }
            if (Appearance is not null)
                foreach (var Renderer in Appearance.Renderers)
                {
                    Values.Add(Renderer.Mesh); Values.AddRange(Renderer.Materials);
                }
            return Values;
        }
    }

    abstract class ActorInputBinding
    {
        internal static ActorInputBinding Capture(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            Object Source, AssetIdentityCatalog Assets)
        {
            if (!Source) return null;
            if (Source is Component Component && Graph.ByPath.ContainsKey(AssetDatabase.GetAssetPath(Component)))
                return new OwnedActorInput(PrefabBinding.Capture(Graph, StagedPaths, Component, "actor input"),
                    Component is Transform or Collider ? ActorDefinitionIdentity.ScalarJson(EditorJsonUtility.ToJson(Component)) : null);
            else if (Source is GameObject Object && Graph.ByPath.ContainsKey(AssetDatabase.GetAssetPath(Object)))
                return new OwnedActorInput(PrefabBinding.Capture(Graph, StagedPaths, Object.transform, "actor input"), null);
            return new AssetActorInput(AssetBinding.Capture(Source, Assets, "actor input"));
        }
        internal abstract void Require(Object Actual, DefinitionBindingResolver Resolver, string Context);
    }

    sealed class OwnedActorInput : ActorInputBinding
    {
        readonly PrefabBinding Binding;
        readonly string Geometry;
        internal OwnedActorInput(PrefabBinding Binding, string Geometry) { this.Binding = Binding; this.Geometry = Geometry; }
        internal override void Require(Object Actual, DefinitionBindingResolver Resolver, string Context)
        {
            Binding.Require(Actual is GameObject Object ? Object.transform : Actual, Resolver, Context, "actor input");
            if (Geometry is not null && Geometry != ActorDefinitionIdentity.ScalarJson(EditorJsonUtility.ToJson(Actual)))
                throw DefinitionSemanticIdentity.Changed(Context, "actor input geometry or pose");
        }
    }

    sealed class AssetActorInput : ActorInputBinding
    {
        readonly AssetBinding Binding;
        internal AssetActorInput(AssetBinding Binding) => this.Binding = Binding;
        internal override void Require(Object Actual, DefinitionBindingResolver Resolver, string Context) =>
            Binding.Require(Actual, Resolver, Context, "actor input");
    }

    sealed class AtmosphereIdentity
    {
        readonly PrefabBinding Binding;
        readonly AssetBinding[] Dependencies;

        AtmosphereIdentity(PrefabBinding Binding, AssetBinding[] Dependencies)
        {
            this.Binding = Binding;
            this.Dependencies = Dependencies;
        }

        internal static AtmosphereIdentity Capture(
            GameObject Root, PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            AssetIdentityCatalog Identities)
        {
            var Values = Root.GetComponentsInChildren<PllAtmosphereAuthoring>(true);
            if (Values.Length == 0) return null;
            if (Values.Length != 1)
                throw new InvalidOperationException(
                    "Cannot capture ambiguous atmosphere semantic identity.");
            var Assets = new List<Object>();
            PllAtmosphereAuthoringValidator.VisitDependencies(Values[0], Assets.Add);
            var Dependencies = new AssetBinding[Assets.Count];
            for (var Index = 0; Index < Dependencies.Length; Index++)
                Dependencies[Index] = AssetBinding.Capture(
                    Assets[Index], Identities, "atmosphere.dependencies["
                        + Index.ToString(CultureInfo.InvariantCulture) + "]");
            return new AtmosphereIdentity(
                PrefabBinding.Capture(Graph, StagedPaths, Values[0], "atmosphere"),
                Dependencies);
        }

        internal static void RequireOptional(
            AtmosphereIdentity Expected, GameObject Root,
            DefinitionBindingResolver Resolver, string Context)
        {
            var Values = Root.GetComponentsInChildren<PllAtmosphereAuthoring>(true);
            if (Expected == null)
            {
                if (Values.Length != 0)
                    throw DefinitionSemanticIdentity.Changed(Context, "atmosphere");
                return;
            }
            if (Values.Length != 1)
                throw DefinitionSemanticIdentity.Changed(Context, "atmosphere");
            Expected.Binding.Require(Values[0], Resolver, Context, "atmosphere");
            var Assets = new List<Object>();
            PllAtmosphereAuthoringValidator.VisitDependencies(Values[0], Assets.Add);
            if (Assets.Count != Expected.Dependencies.Length)
                throw DefinitionSemanticIdentity.Changed(Context, "atmosphere.dependencies");
            for (var Index = 0; Index < Expected.Dependencies.Length; Index++)
                Expected.Dependencies[Index].Require(
                    Assets[Index], Resolver, Context, "atmosphere.dependencies["
                        + Index.ToString(CultureInfo.InvariantCulture) + "]");
        }
    }

    sealed class RestPointIdentity
    {
        readonly PrefabBinding Binding;
        readonly PrefabBinding Model;
        readonly PrefabBinding Hold;
        readonly string Id;
        readonly Vector3 Center;
        readonly Vector3 Size;
        readonly PllRestPointProfile Profile;
        readonly PllMountedToolModel MountedModel;
        readonly PllFixturePresentation CrackPresentation, BreakPresentation;
        readonly PrefabBinding Crack, BreakVfx;
        readonly (string Hang, string Warning, string Break) Hooks;
        readonly PllRestPointPlacement Placement;
        readonly float InsertionOffset;

        internal RestPointIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PllRestPointAuthoring Source)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "Rest Point");
            Model = PrefabBinding.Capture(Graph, StagedPaths, Source.ModelRoot.transform, "Rest Point model");
            Hold = PrefabBinding.Capture(Graph, StagedPaths, Source.HoldAnchor, "Rest Point hold");
            Id = Source.Id; Center = Source.InteractionCenter; Size = Source.InteractionSize;
            Profile = Source.Profile; MountedModel = Source.MountedModel;
            CrackPresentation = Source.CrackPresentation; BreakPresentation = Source.BreakPresentation;
            Hooks = (Source.HangHook, Source.WarningHook, Source.BreakHook);
            Placement = Source.Placement; InsertionOffset = Source.InsertionOffset;
            Crack = PrefabBinding.CaptureOptional(Graph, StagedPaths, Source.CrackRoot ? Source.CrackRoot.transform : null, "mounted tool crack");
            BreakVfx = PrefabBinding.CaptureOptional(Graph, StagedPaths, Source.BreakVfxRoot ? Source.BreakVfxRoot.transform : null, "mounted tool break effect");
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Source = Binding.Resolve<PllRestPointAuthoring>(Resolver, Context, "Rest Point");
            if (!Source.enabled || Source.Id != Id || Source.InteractionCenter != Center || Source.InteractionSize != Size
                || Source.Profile != Profile || Source.MountedModel != MountedModel
                || Source.CrackPresentation != CrackPresentation || Source.BreakPresentation != BreakPresentation
                || (Source.HangHook, Source.WarningHook, Source.BreakHook) != Hooks
                || Source.Placement != Placement || Source.InsertionOffset != InsertionOffset)
                throw DefinitionSemanticIdentity.Changed(Context, "Rest Point recipe");
            Model.Require(Source.ModelRoot ? Source.ModelRoot.transform : null, Resolver, Context, "Rest Point model");
            Hold.Require(Source.HoldAnchor, Resolver, Context, "Rest Point hold");
            PrefabBinding.RequireOptional(Crack, Source.CrackRoot ? Source.CrackRoot.transform : null, Resolver, Context, "mounted tool crack");
            PrefabBinding.RequireOptional(BreakVfx, Source.BreakVfxRoot ? Source.BreakVfxRoot.transform : null, Resolver, Context, "mounted tool break effect");
            var Handles = Source.HoldAnchor.GetComponents<ClimbHandle>();
            var Views = Source.HoldAnchor.GetComponents<PhotonView>();
            var Colliders = Source.HoldAnchor.GetComponents<Collider>();
            if (Handles.Length != 1 || !Handles[0].enabled
                || Handles[0].isPickaxe != (Profile == PllRestPointProfile.NativeFragileMountedTool)
                || Views.Length != 1 || !Views[0].enabled || Views[0].ViewID != 0
                || Views[0].OwnershipTransfer != OwnershipOption.Fixed || Views[0].ObservedComponents.Count != 0
                || Colliders.Length != 1 || Colliders[0] is not BoxCollider Interaction
                || Interaction.enabled || Interaction.isTrigger || Interaction.attachedRigidbody
                || Interaction.center != Center || Interaction.size != Size || Source.HoldAnchor.gameObject.layer != 0)
                throw DefinitionSemanticIdentity.Changed(Context, "Rest Point native graph");
            var Pitons = Source.HoldAnchor.GetComponents<ShittyPiton>();
            if (Profile == PllRestPointProfile.NativeFragileMountedTool
                    ? Pitons.Length != 1 || Pitons[0].enabled || Pitons[0].crack || Pitons[0].vfx
                        || Pitons[0].cracKSFX is { Length: > 0 } || Pitons[0].breakSFX is { Length: > 0 }
                    : Pitons.Length != 0)
                throw DefinitionSemanticIdentity.Changed(Context, "mounted tool staged callback");
        }
    }

    sealed class HangingRopeIdentity
    {
        readonly PrefabBinding Binding;
        readonly PrefabBinding Model;
        readonly PrefabBinding Ghost;
        readonly PrefabBinding Point;
        readonly string Id;
        readonly float Segments;
        readonly float SpoolOutSeconds;
        readonly PllRopeAppearance Appearance;
        readonly PllRopeAnchorBehavior AnchorBehavior;
        readonly AssetBinding RopeMesh, RestMesh, Material, GhostMaterial;
        readonly string RendererPath, NativeRigFingerprint;
        readonly string[] BonePaths;
        readonly Bounds Bounds;
        readonly Vector3 RestStart, RestEnd;
        readonly (float Minimum, float Maximum, float Animation, float Drop) BreakSettings;
        readonly (string Warning, string Break) Hooks;

        internal HangingRopeIdentity(PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PllHangingRopeAuthoring Source, AssetIdentityCatalog Identities)
        {
            Binding = PrefabBinding.Capture(Graph, StagedPaths, Source, "Hanging Rope");
            Model = PrefabBinding.Capture(Graph, StagedPaths, Source.ModelRoot.transform, "Hanging Rope model");
            Ghost = PrefabBinding.Capture(Graph, StagedPaths, Source.GhostRoot.transform, "Hanging Rope ghost");
            Point = PrefabBinding.Capture(Graph, StagedPaths, Source.AnchorPoint, "Hanging Rope point");
            Id = Source.Id; Segments = Source.Segments; SpoolOutSeconds = Source.SpoolOutSeconds;
            Appearance = Source.RopeAppearance; AnchorBehavior = Source.AnchorBehavior;
            RopeMesh = AssetBinding.CaptureOptional(Source.RopeMesh, Identities, "rope skin mesh");
            RestMesh = AssetBinding.CaptureOptional(Source.RopeRestMesh, Identities, "rope rest mesh");
            Material = AssetBinding.CaptureOptional(Source.RopeMaterial, Identities, "rope material");
            GhostMaterial = AssetBinding.CaptureOptional(Source.RopeGhostMaterial, Identities, "rope ghost material");
            RendererPath = Source.RopeRendererPath; BonePaths = (string[])Source.RopeBonePaths.Clone();
            NativeRigFingerprint = Source.RopeNativeRigFingerprint; Bounds = Source.RopeBounds;
            RestStart = Source.RopeRestStart; RestEnd = Source.RopeRestEnd;
            BreakSettings = (Source.BreakTimeMinimum, Source.BreakTimeMaximum, Source.BreakAnimationSeconds, Source.DropSegments);
            Hooks = (Source.WarningHook, Source.BreakHook);
        }

        internal void Require(DefinitionBindingResolver Resolver, string Context)
        {
            var Source = Binding.Resolve<PllHangingRopeAuthoring>(Resolver, Context, "Hanging Rope");
            if (!Source.enabled || Source.Id != Id || !Source.Segments.Equals(Segments)
                || !Source.SpoolOutSeconds.Equals(SpoolOutSeconds) || Source.RopeAppearance != Appearance
                || Source.AnchorBehavior != AnchorBehavior || Source.RopeRendererPath != RendererPath
                || !Source.RopeBonePaths.SequenceEqual(BonePaths) || Source.RopeNativeRigFingerprint != NativeRigFingerprint
                || !Source.RopeBounds.Equals(Bounds) || !Source.RopeRestStart.Equals(RestStart) || !Source.RopeRestEnd.Equals(RestEnd)
                || (Source.BreakTimeMinimum, Source.BreakTimeMaximum, Source.BreakAnimationSeconds, Source.DropSegments) != BreakSettings
                || (Source.WarningHook, Source.BreakHook) != Hooks)
                throw DefinitionSemanticIdentity.Changed(Context, "Hanging Rope recipe");
            Model.Require(Source.ModelRoot ? Source.ModelRoot.transform : null, Resolver, Context, "Hanging Rope model");
            Ghost.Require(Source.GhostRoot ? Source.GhostRoot.transform : null, Resolver, Context, "Hanging Rope ghost");
            Point.Require(Source.AnchorPoint, Resolver, Context, "Hanging Rope point");
            AssetBinding.RequireOptional(RopeMesh, Source.RopeMesh, Resolver, Context, "rope skin mesh");
            AssetBinding.RequireOptional(RestMesh, Source.RopeRestMesh, Resolver, Context, "rope rest mesh");
            AssetBinding.RequireOptional(Material, Source.RopeMaterial, Resolver, Context, "rope material");
            AssetBinding.RequireOptional(GhostMaterial, Source.RopeGhostMaterial, Resolver, Context, "rope ghost material");
            var Anchors = Source.GetComponents<RopeAnchor>();
            var Providers = Source.GetComponents<RopeAnchorWithRope>();
            var Views = Source.GetComponents<PhotonView>();
            if (Anchors.Length != 1 || !Anchors[0].enabled || Anchors[0].normalPart != Source.ModelRoot
                || Anchors[0].ghostPart != Source.GhostRoot || Anchors[0].anchorPoint != Source.AnchorPoint
                || Providers.Length != 1 || Providers[0].enabled || Providers[0].ropePrefab
                || !Providers[0].ropeSegmentLength.Equals(Segments) || !Providers[0].spoolOutTime.Equals(SpoolOutSeconds)
                || Views.Length != 1 || !Views[0].enabled || Views[0].ViewID != 0
                || Views[0].OwnershipTransfer != OwnershipOption.Fixed || Views[0].ObservedComponents.Count != 0)
                throw DefinitionSemanticIdentity.Changed(Context, "Hanging Rope native graph");
            var Breakers = Source.GetComponents<BreakableRopeAnchor>();
            if (AnchorBehavior == PllRopeAnchorBehavior.NativeBreakableAnchor
                    ? Breakers.Length != 1 || Breakers[0].enabled
                        || !Breakers[0].breakableTimeMinMax.Equals(new Vector2(BreakSettings.Minimum, BreakSettings.Maximum))
                        || Breakers[0].breakAnimTime != BreakSettings.Animation || Breakers[0].dropSegments != BreakSettings.Drop
                    : Breakers.Length != 0)
                throw DefinitionSemanticIdentity.Changed(Context, "breakable rope staged callback");
        }
    }

    sealed class VariationIdentity
    {
        readonly PrefabBinding Binding;
        readonly string Id;
        readonly PrefabBinding[] Roots;
        readonly string[] EntryIds;
        readonly float[] Weights;

        VariationIdentity(PrefabBinding Binding, string Id,
            PrefabBinding[] Roots, string[] EntryIds, float[] Weights)
        {
            this.Binding = Binding;
            this.Id = Id;
            this.Roots = Roots;
            this.EntryIds = EntryIds;
            this.Weights = Weights;
        }

        internal static VariationIdentity Capture(
            PrefabGraph Graph, IReadOnlyDictionary<string, string> StagedPaths,
            PllVariationAuthoring Value, string Field)
        {
            var Roots = new PrefabBinding[Value.Entries.Count];
            var EntryIds = new string[Roots.Length];
            var Weights = new float[Roots.Length];
            for (var Index = 0; Index < Roots.Length; Index++)
            {
                var Entry = Value.Entries[Index];
                Roots[Index] = PrefabBinding.Capture(Graph, StagedPaths, Entry.Root.transform,
                    Field + ".entries[" + Index.ToString(CultureInfo.InvariantCulture) + "].root");
                EntryIds[Index] = Entry.EntryId;
                Weights[Index] = Entry.Weight;
            }
            return new VariationIdentity(PrefabBinding.Capture(Graph, StagedPaths, Value, Field),
                Value.Id, Roots, EntryIds, Weights);
        }

        internal void Require(PllVariationAuthoring Actual,
            DefinitionBindingResolver Resolver, string Context, string Field)
        {
            Binding.Require(Actual, Resolver, Context, Field);
            DefinitionSemanticIdentity.RequireString(Actual.Id, Id, Context, Field + ".id");
            if (!Actual.enabled || Actual.Entries.Count != Roots.Length)
                throw DefinitionSemanticIdentity.Changed(Context, Field + ".entries");
            var Group = Array.IndexOf(Actual.GetComponents<PllVariationAuthoring>(), Actual);
            var Selectors = Actual.GetComponents<VariantObjectSelector>();
            if (Group is < 0 or > 3 || Selectors.Length != 1 || !Selectors[0].enabled)
                throw DefinitionSemanticIdentity.Changed(Context, Field + ".selector");
            for (var Index = 0; Index < Roots.Length; Index++)
            {
                var Entry = Actual.Entries[Index];
                var EntryField = Field + ".entries[" + Index.ToString(CultureInfo.InvariantCulture) + "]";
                if (Entry == null || !Entry.Root || Entry.Root.transform.parent != Actual.transform
                    || Entry.Root.activeSelf || !Entry.Weight.Equals(Weights[Index])
                    || Entry.EntryId != EntryIds[Index])
                    throw DefinitionSemanticIdentity.Changed(Context, EntryField);
                Roots[Index].Require(Entry.Root.transform, Resolver, Context, EntryField + ".root");
                var Native = Entry.Root.GetComponents<VariantObject>();
                if (Native.Length != 1 || !Native[0].enabled || (int)Native[0].group != Group
                    || !Native[0].spawnChance.Equals(Weights[Index]))
                    throw DefinitionSemanticIdentity.Changed(Context, EntryField + ".native");
            }
        }
    }

    sealed class LuggageIdentity
    {
        readonly PrefabBinding Binding;
        readonly string Id;
        readonly string DisplayName;
        readonly LuggageSpawnGroupIdentity[] SpawnGroups;
        readonly AssetBinding AnimatorController;
        readonly float OpenTime;
        readonly PrefabBinding ActivateOnOpen;
        readonly PllLuggageContentsMode ContentsMode;
        readonly LuggageItemIdentity[] CustomItems;
        readonly string NativeContentsId;
        readonly PllLuggageRepeatMode RepeatMode;
        readonly float SpawnChance;
        readonly PrefabBinding ReplacesAuthored;
        readonly string ReplacesNativeId;
        readonly bool RootActive;
        string GeneratedControllerIdentity;

        LuggageIdentity(
            PrefabBinding Binding, string Id, string DisplayName,
            LuggageSpawnGroupIdentity[] SpawnGroups,
            AssetBinding AnimatorController, float OpenTime,
            PrefabBinding ActivateOnOpen,
            PllLuggageContentsMode ContentsMode,
            LuggageItemIdentity[] CustomItems,
            string NativeContentsId,
            PllLuggageRepeatMode RepeatMode, float SpawnChance,
            PrefabBinding ReplacesAuthored,
            string ReplacesNativeId, bool RootActive)
        {
            this.Binding = Binding;
            this.Id = Id;
            this.DisplayName = DisplayName;
            this.SpawnGroups = SpawnGroups;
            this.AnimatorController = AnimatorController;
            this.OpenTime = OpenTime;
            this.ActivateOnOpen = ActivateOnOpen;
            this.ContentsMode = ContentsMode;
            this.CustomItems = CustomItems;
            this.NativeContentsId = NativeContentsId;
            this.RepeatMode = RepeatMode;
            this.SpawnChance = SpawnChance;
            this.ReplacesAuthored = ReplacesAuthored;
            this.ReplacesNativeId = ReplacesNativeId;
            this.RootActive = RootActive;
        }

        internal static LuggageIdentity Capture(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            PllLuggageAuthoring Value,
            AssetIdentityCatalog AssetIdentities,
            string Field)
        {
            var SpawnGroups = new LuggageSpawnGroupIdentity[
                Value.SpawnGroups.Count];
            for (var Index = 0; Index < SpawnGroups.Length; Index++)
                SpawnGroups[Index] = LuggageSpawnGroupIdentity.Capture(
                    Graph, StagedPaths, Value.SpawnGroups[Index],
                    Field + ".spawnGroups["
                        + Index.ToString(CultureInfo.InvariantCulture) + "]");
            var CustomItems = new LuggageItemIdentity[
                Value.CustomItems.Count];
            for (var Index = 0; Index < CustomItems.Length; Index++)
                CustomItems[Index] = LuggageItemIdentity.Capture(
                    Value.CustomItems[Index], AssetIdentities,
                    Field + ".customItems["
                        + Index.ToString(CultureInfo.InvariantCulture) + "]");
            return new LuggageIdentity(
                PrefabBinding.Capture(
                    Graph, StagedPaths, Value, Field),
                Value.Id, Value.DisplayName, SpawnGroups,
                Value.AnimatorController
                    ? AssetBinding.Capture(
                        Value.AnimatorController, AssetIdentities,
                        Field + ".animatorController")
                    : null,
                Value.OpenTime,
                Value.ActivateOnOpen
                    ? PrefabBinding.Capture(
                        Graph, StagedPaths,
                        Value.ActivateOnOpen.transform,
                        Field + ".activateOnOpen")
                    : null,
                Value.ContentsMode, CustomItems,
                Value.NativeContentsId, Value.RepeatMode,
                Value.SpawnChance,
                Value.ReplacesAuthored
                    ? PrefabBinding.Capture(
                        Graph, StagedPaths, Value.ReplacesAuthored,
                        Field + ".replacesAuthored")
                    : null,
                Value.ReplacesNativeId, Value.gameObject.activeSelf);
        }

        internal void Require(
            PllLuggageAuthoring Actual,
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            Binding.Require(Actual, Resolver, Context, Field);
            DefinitionSemanticIdentity.RequireString(Actual.Id, Id, Context, Field + ".id");
            DefinitionSemanticIdentity.RequireString(
                Actual.DisplayName, DisplayName,
                Context, Field + ".displayName");
            if (Actual.SpawnGroups.Count != SpawnGroups.Length)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".spawnGroups");
            for (var Index = 0; Index < SpawnGroups.Length; Index++)
                SpawnGroups[Index].Require(
                    Actual.SpawnGroups[Index], Resolver, Context,
                    Field + ".spawnGroups["
                        + Index.ToString(CultureInfo.InvariantCulture) + "]");
            if (AnimatorController == null)
            {
                if (Actual.AnimatorController)
                    throw DefinitionSemanticIdentity.Changed(
                        Context, Field + ".animatorController");
            }
            else AnimatorController.Require(
                Actual.AnimatorController, Resolver, Context,
                Field + ".animatorController");
            if (!Actual.OpenTime.Equals(OpenTime))
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".openTime");
            if (ActivateOnOpen == null)
            {
                if (Actual.ActivateOnOpen)
                    throw DefinitionSemanticIdentity.Changed(
                        Context, Field + ".activateOnOpen");
            }
            else if (!Actual.ActivateOnOpen)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".activateOnOpen");
            else ActivateOnOpen.Require(
                Actual.ActivateOnOpen.transform, Resolver,
                Context, Field + ".activateOnOpen");
            if (Actual.ContentsMode != ContentsMode)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".contentsMode");
            if (Actual.CustomItems.Count != CustomItems.Length)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".customItems");
            for (var Index = 0; Index < CustomItems.Length; Index++)
                CustomItems[Index].Require(
                    Actual.CustomItems[Index], Resolver, Context,
                    Field + ".customItems["
                        + Index.ToString(CultureInfo.InvariantCulture) + "]");
            DefinitionSemanticIdentity.RequireString(
                Actual.NativeContentsId, NativeContentsId,
                Context, Field + ".nativeContentsId");
            if (Actual.RepeatMode != RepeatMode)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".repeatMode");
            if (!Actual.SpawnChance.Equals(SpawnChance))
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".spawnChance");
            if (ReplacesAuthored == null)
            {
                if (Actual.ReplacesAuthored)
                    throw DefinitionSemanticIdentity.Changed(
                        Context, Field + ".replacesAuthored");
            }
            else ReplacesAuthored.Require(
                Actual.ReplacesAuthored, Resolver,
                Context, Field + ".replacesAuthored");
            DefinitionSemanticIdentity.RequireString(
                Actual.ReplacesNativeId, ReplacesNativeId,
                Context, Field + ".replacesNativeId");
            if (Actual.gameObject.activeSelf != RootActive)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".rootActive");
            RequireStagedShape(Actual, Resolver, Context, Field);
        }

        void RequireStagedShape(
            PllLuggageAuthoring Actual,
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            var Root = Actual.gameObject;
            var Native = Root.GetComponents<global::Luggage>();
            var Views = Root.GetComponents<Photon.Pun.PhotonView>();
            var Animators = Root.GetComponents<Animator>();
            if (!Actual.enabled
                || Root.GetComponents<PllLuggageAuthoring>().Length != 1
                || Root.GetComponentsInChildren<PllLuggageAuthoring>(true).Length != 1
                || Native.Length != 1 || !Native[0].enabled
                || Native[0].GetType() != typeof(global::Luggage)
                || Views.Length != 1 || Animators.Length != 1
                || !PllStagedFeatureTranslator.MatchesConfiguredAnimator(
                    Root, Animators[0])
                || Root.GetComponentsInChildren<global::Luggage>(true).Length != 1
                || Root.GetComponentsInChildren<Photon.Pun.PhotonView>(true).Length != 1
                || Root.GetComponentsInChildren<Item>(true).Length != 0
                || Root.GetComponentsInChildren<Campfire>(true).Length != 0
                || Root.GetComponentsInChildren<RespawnChest>(true).Length != 0
                || !PllStagedFeatureTranslator.MatchesLuggageShape(
                    Native[0], Actual))
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".stagedShape");
            PllStagedFeatureTranslator.ValidateConfiguredPhotonView(
                Root, Views[0], Context + " " + Field);
            var RuntimeController =
                Animators[0].runtimeAnimatorController;
            if (!Resolver.Reopened)
            {
                PllStagedFeatureTranslator.ValidateConfiguredAnimator(
                    Root, Animators[0], Actual.AnimatorController,
                    Context + " " + Field);
                if (AnimatorController == null)
                {
                    var Path = AssetDatabase.GetAssetPath(
                        RuntimeController);
                    if (string.IsNullOrEmpty(Path)
                        || !EditorUtility.IsPersistent(
                            RuntimeController))
                        throw DefinitionSemanticIdentity.Changed(
                            Context, Field + ".animatorController");
                    if (GeneratedControllerIdentity == null)
                        GeneratedControllerIdentity =
                            RuntimeController.name;
                    else if (!string.Equals(
                        GeneratedControllerIdentity,
                        RuntimeController.name,
                        StringComparison.Ordinal))
                        throw DefinitionSemanticIdentity.Changed(
                            Context, Field + ".animatorController");
                }
            }
            else if (AnimatorController == null)
            {
                if (string.IsNullOrEmpty(
                        GeneratedControllerIdentity))
                    throw DefinitionSemanticIdentity.Changed(
                        Context, Field + ".animatorController");
                Resolver.RequireGeneratedController(
                    GeneratedControllerIdentity, RuntimeController,
                    Context, Field + ".animatorController");
            }
            else if (RuntimeController != Actual.AnimatorController)
                throw DefinitionSemanticIdentity.Changed(
                    Context, Field + ".animatorController");
        }
    }

    sealed class LuggageSpawnGroupIdentity
    {
        readonly int Weight;
        readonly PrefabBinding[] Points;

        LuggageSpawnGroupIdentity(
            int Weight, PrefabBinding[] Points)
        {
            this.Weight = Weight;
            this.Points = Points;
        }

        internal static LuggageSpawnGroupIdentity Capture(
            PrefabGraph Graph,
            IReadOnlyDictionary<string, string> StagedPaths,
            PllLuggageSpawnGroup Value, string Field)
        {
            var Points = new PrefabBinding[Value.Points.Count];
            for (var Index = 0; Index < Points.Length; Index++)
                Points[Index] = PrefabBinding.Capture(
                    Graph, StagedPaths, Value.Points[Index],
                    Field + ".points["
                        + Index.ToString(CultureInfo.InvariantCulture) + "]");
            return new LuggageSpawnGroupIdentity(
                Value.Weight, Points);
        }

        internal void Require(
            PllLuggageSpawnGroup Actual,
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            if (Actual == null || Actual.Weight != Weight
                || Actual.Points.Count != Points.Length)
                throw DefinitionSemanticIdentity.Changed(Context, Field);
            for (var Index = 0; Index < Points.Length; Index++)
                Points[Index].Require(
                    Actual.Points[Index], Resolver, Context,
                    Field + ".points["
                        + Index.ToString(CultureInfo.InvariantCulture) + "]");
        }
    }

    sealed class LuggageItemIdentity
    {
        readonly AssetBinding Item;
        readonly int Weight;

        LuggageItemIdentity(AssetBinding Item, int Weight)
        {
            this.Item = Item;
            this.Weight = Weight;
        }

        internal static LuggageItemIdentity Capture(
            PllLuggageItemEntry Value,
            AssetIdentityCatalog AssetIdentities,
            string Field) => new LuggageItemIdentity(
                AssetBinding.Capture(
                    Value.Item, AssetIdentities, Field + ".item"),
                Value.Weight);

        internal void Require(
            PllLuggageItemEntry Actual,
            DefinitionBindingResolver Resolver,
            string Context, string Field)
        {
            if (Actual == null || Actual.Weight != Weight)
                throw DefinitionSemanticIdentity.Changed(Context, Field);
            Item.Require(
                Actual.Item, Resolver, Context, Field + ".item");
        }
    }
    class StagedDefinition
    {
        internal StagedDefinition(
            PllBiomeDefinition Staged, string DefinitionPath,
            string PrefabPath)
        {
            this.Staged = Staged;
            this.DefinitionPath = DefinitionPath;
            this.PrefabPath = PrefabPath;
        }

        internal PllBiomeDefinition Staged { get; }
        internal string DefinitionPath { get; }
        internal string PrefabPath { get; }
    }

    sealed class CompiledDefinition : StagedDefinition
    {
        readonly GeneratedPropCatalogIdentity GeneratedCatalog;

        internal CompiledDefinition(StagedDefinition Prepared, DefinitionSemanticIdentity SemanticIdentity,
            GeneratedPropCatalogIdentity GeneratedCatalog)
            : base(Prepared.Staged, Prepared.DefinitionPath, Prepared.PrefabPath)
        {
            this.SemanticIdentity = SemanticIdentity;
            this.GeneratedCatalog = GeneratedCatalog;
        }

        internal DefinitionSemanticIdentity SemanticIdentity { get; }

        internal void Validate(PllBiomeDefinition Actual, string Context, AssetBundle Bundle = null) =>
            SemanticIdentity.Validate(Actual, Context, Bundle, GeneratedCatalog);
    }

    sealed class AuditResult
    {
        readonly HashSet<string> Providers;

        internal AuditResult(
            string[] Dependencies, HashSet<string> Providers, HashSet<string> EditorOnly,
            HashSet<Type> SerializedGraphs)
        {
            this.Dependencies = Dependencies;
            this.Providers = Providers;
            this.EditorOnly = EditorOnly;
            this.SerializedGraphs = SerializedGraphs;
        }

        internal string[] Dependencies { get; }
        internal bool ContainsProvider(string Name) => Providers.Contains(Name);
        internal string[] CopyOrderedProviders() => Providers.OrderBy(Value => Value, StringComparer.Ordinal).ToArray();
        internal HashSet<string> EditorOnly { get; }
        internal HashSet<Type> SerializedGraphs { get; }
    }

    sealed class InspectionResult
    {
        internal InspectionResult(string[] AssetNames)
        {
            this.AssetNames = AssetNames;
        }

        internal string[] AssetNames { get; }
    }

    [Serializable]
    sealed class BuildTranslationData
    {
        internal BuildTranslationData(
            string Definition, string SourceAsset, string SourceObject, string SourcePath, string Component,
            string Field, string StagedAsset, string Outcome)
        {
            this.definition = Definition;
            this.sourceAsset = SourceAsset;
            this.sourceObject = SourceObject;
            this.sourcePath = SourcePath;
            this.component = Component;
            this.field = Field;
            this.stagedAsset = StagedAsset;
            this.outcome = Outcome;
        }

        public string definition;
        public string sourceAsset;
        public string sourceObject;
        public string sourcePath;
        public string component;
        public string field;
        public string stagedAsset;
        public string outcome;
    }

    [Serializable]
    sealed class BuildReportData
    {
        [NonSerialized] internal string PublishedReportSha256;
        public string output;
        public string target;
        public string unityVersion;
        public string pllAssembly;
        public string peakAssembly;
        public string peakFileVersion;
        public long bundleBytes;
        public string bundleSha256;
        public string[] biomeIds;
        public string[] biomeBehaviors;
        public string[] assets;
        public string[] dependencies;
        public string[] managedProviders;
        public BuildTranslationData[] translations;
    }
}
