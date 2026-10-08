#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Profile;
#endif

using System;
using System.IO;
using Sirenix.OdinInspector;
using System.Text.RegularExpressions;
using Peak.ProcGen;
using Photon.Pun;
using System.Collections.Generic;
using System.Linq;
using Peak;
using UnityEngine;
using UnityEngine.Serialization;
using Zorro.Core;
using Zorro.Core.Editor;
using Random = UnityEngine.Random;


[CreateAssetMenu(menuName = "Peak/MapBaker")]
public class MapBaker : SingletonAsset<MapBaker>
{
    public string[] ScenePaths;
    [System.Obsolete("Renamed to ScenePaths.")]
    public string[] AllLevels => ScenePaths;
    public List<string> BiomeIDs;
    public List<BiomeResult> selectedBiomes = new();
    static bool IsMidBuild =>
#if UNITY_EDITOR
        BuildPipeline.isBuildingPlayer;
#else
        false;
#endif
    
#if UNITY_EDITOR
    [System.Serializable]
    public class MapBakerSettings
    {
        [FormerlySerializedAs("DesiredAmountOfLevels")] [SerializeField]
        int numLevels;

        int _numLevelOverride = 0;
        
        public int DesiredAmountOfLevels
        {
            get
            {
                if (CLI.TryGetArg(CLI.Args.NumMaps, out int numMaps))
                {
                    return numMaps;
                }
                return _numLevelOverride > 0 
                    ? _numLevelOverride 
                    : numLevels;
            }
        }
        public int preventMoreThanXIdenticalBiomeSelectionsInARow = 0;
        public SceneAsset Island;
        [SerializeField]
        [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        BiomeSelectionSettings _biomeSettings;

        public List<BiomeSelection> biomeSelection => _biomeSettings.Settings;

        public void OverrideNumLevels(int overrideNumber)
            => _numLevelOverride = overrideNumber;
    }

    [Serializable]
    public struct MapBakeReport
    {
        public const string PrefsKey = "MAP_BAKE_REPORT";
        
        public List<string> BiomeIDs;
        public List<BiomeResult> BiomeResults;
    }

    [SerializeField]
    MapBakerSettings _editorSettings = new MapBakerSettings()
        { };

    MapBakerSettings _overrideSettings = null;
    [ShowInInspector, ReadOnly]
    static bool ShouldUseSettingsOverride => Instance?._overrideSettings?.Island != null;
    
    public int DesiredAmountOfLevelsAppended = 2;
    
    public List<SceneAsset> SceneAssets;
    
    string prevBiomesID = "ZZZZZZZ";
    public static MapBakerSettings CurrentSettings => 
        ShouldUseSettingsOverride ? Instance._overrideSettings : Instance._editorSettings;
    public static List<SceneAsset> Scenes => Instance.SceneAssets;

    public static int NumExistingLevels
    {
        get
        {
            Instance.RefreshSceneList();
            return Instance.SceneAssets.Count;
        }
    }

    const string GeneratedScenesDirectory = "8_SCENES/Generated";
    static string GeneratedLevelDirectory => $"Assets/{GeneratedScenesDirectory}";
    const string GeneratedLevelPrefix = "Level_";
    
    static string AssetPathForLevel(int i)
        => $"{GeneratedLevelDirectory}/{GeneratedLevelPrefix}{i}.unity";

    static string BakeReportPath => $"{Application.dataPath}/{GeneratedScenesDirectory}/bake_report.json";


    public static void GenerateLevels(int numLevels)
    {
        CurrentSettings.OverrideNumLevels(numLevels);
        Instance.GenerateMaps();
    }

    public override void OnLoaded()
    {
        TryLoadBakeReport();
    }

    public static void SaveBakeReport()
    {
        var report = new MapBakeReport()
        {
            BiomeIDs = Instance.BiomeIDs,
            BiomeResults = Instance.selectedBiomes
        };
        
        File.WriteAllText(BakeReportPath, JsonUtility.ToJson(report, true));
    }


    [Button]
    void ResaveBakeReport()
    {
        if (!File.Exists(BakeReportPath))
            return;
        var reportSerialized = File.ReadAllText(BakeReportPath);
            if (reportSerialized == String.Empty)
                return;
        var report = JsonUtility.FromJson<MapBakeReport>(reportSerialized);
        File.WriteAllText(BakeReportPath, JsonUtility.ToJson(report, true));
    }

    public static bool TryLoadBakeReport()
    {
        if (!File.Exists(BakeReportPath))
            return false;
        var reportSerialized = File.ReadAllText(BakeReportPath);
        if (reportSerialized == String.Empty)
            return false;
        var report = JsonUtility.FromJson<MapBakeReport>(reportSerialized);
        Debug.Log($"Last BiomeIDs loaded: {report.BiomeIDs}");
        Instance.BiomeIDs = report.BiomeIDs;
        Instance.selectedBiomes = report.BiomeResults;
        EditorUtility.SetDirty(Instance);
        AssetDatabase.SaveAssetIfDirty(Instance);
        return true;
    }

    public IEnumerable<EditorBuildSettingsScene> GeneratedLevelsForBuild
    {
        get
        {
            RefreshSceneList(CurrentSettings.DesiredAmountOfLevels);
            foreach (var sceneAsset in SceneAssets)
                yield return new EditorBuildSettingsScene(AssetDatabase.GetAssetOrScenePath(sceneAsset), true);
        }
    }

    public void AddMapsToBuildProfile()
    {
        var buildProfile = BuildProfile.GetActiveBuildProfile();
        if (buildProfile.overrideGlobalScenes)
        {
            Debug.Log($"Scene overrides will be overwritten by the global settings + generated scenes.");
            buildProfile.overrideGlobalScenes = false;
        }
        var baseScenes = buildProfile.GetScenesForBuild();
        buildProfile.overrideGlobalScenes = true;
        buildProfile.scenes = baseScenes.Concat(GeneratedLevelsForBuild).ToArray();
        EditorUtility.SetDirty(buildProfile);
        AssetDatabase.SaveAssetIfDirty(buildProfile);
    }

    [Button]
    public void RefreshSceneList(int numLevelsAllowed = -1)
    {
        var shouldRestrictList = numLevelsAllowed != -1;
        var sceneGuids = 
            AssetDatabase.FindAssets($"{GeneratedLevelPrefix}*", new [] {GeneratedLevelDirectory});
        ScenePaths = new string[shouldRestrictList ? numLevelsAllowed : sceneGuids.Length];
        SceneAssets.Clear();
        for (int i = 0; i < sceneGuids.Length; ++i)
        {
            if (shouldRestrictList && i >= numLevelsAllowed)
                break;
            var scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
            ScenePaths[i] = scenePath;
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            SceneAssets.Add(sceneAsset);
        }
        Debug.Log($"Found {ScenePaths.Length} valid Levels");
        ValidateSceneList();
    }

    void ValidateSceneList()
    {
        int numBadPaths = 0;
        for (int i = 0; i < ScenePaths.Length; i++)
        {
            var expectedPath = AssetPathForLevel(i);
            if (!ScenePaths.Contains(expectedPath))
            {
                Debug.LogWarning($"Expected to find a Scene at {expectedPath} but none is there!");
                numBadPaths++;
            }
            else if (SceneAssets[i] == null)
            {
                Debug.LogWarning($"Scene at path {ScenePaths[i]} doesn't exist!");
                numBadPaths++;
            }
        }

        
        if (numBadPaths > 0)
        {
            Debug.LogError($"We have {SceneAssets.Count} but the path names are wrong or assets are missing for " +
                           $"{numBadPaths} of the Scenes.");
        }
    }
    
    public static async Awaitable<bool> TryGenerateWithSettings(MapBakerSettings settings, bool canKeepExisting)
    {
        Instance.RefreshSceneList();
        Debug.Log("Starting map gen with overridden settings");
        Instance._overrideSettings = settings;
        var numScenesNeeded = settings.DesiredAmountOfLevels;
        bool succeeded;
        if (canKeepExisting)
        {
            var numScenesToAdd = numScenesNeeded - MapBaker.NumExistingLevels;
            Debug.Log($"Build settings require another {numScenesToAdd} levels to be generated.");
            succeeded = await Instance.AppendLevels(settings.Island,
                numScenesNeeded - NumExistingLevels);
        }
        else
        {
            Debug.Log($"Need to generate {numScenesNeeded} fresh levels for this build profile.");
            succeeded = await Instance.ClearAllAndGenerate(settings.Island, numScenesNeeded);
        }

        Instance._overrideSettings = null;
        return succeeded;
    }

    async Awaitable<bool> ClearAllAndGenerate(SceneAsset island, int numLevels)
    {
        bool generationSuccessful = true;

        try
        {
            Debug.Log($"Generating {numLevels} fresh levels using {island.name}");
            RefreshSceneList();
            foreach (var level in SceneAssets)
            {
                var p = AssetDatabase.GetAssetPath(level);
                AssetDatabase.DeleteAsset(p);
                Debug.Log($"Clearing level: {p}");
            }

            SceneAssets.Clear();
            BiomeIDs.Clear();
            prevBiomesID = "ZZZZZZZ";
            
            GenerateBiomeSelection(numLevels);
            for (int i = 0; i < numLevels; i++)
            {
                if (!await GenerateMap(island, i))
                {
                    generationSuccessful = false;
                }
                prevBiomesID = BiomeIDs.Last();
            }

            ScenePaths = SceneAssets.Select(AssetDatabase.GetAssetPath).ToArray();
            EditorUtility.ClearProgressBar();
        }
        catch (Exception e)
        {
            Debug.LogError($"Map Generation FAILED: {e}");
            return false;
        }

        SaveBakeReport();
        return generationSuccessful;
    }
    
    [Button]
    async void GenerateMaps()
    {
        try
        {
            await ClearAllAndGenerate(CurrentSettings.Island, CurrentSettings.DesiredAmountOfLevels);

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
    
    async Awaitable<bool> AppendLevels(SceneAsset island, int numLevels)
    {
        bool succeeded = true;
        try
        {
            TryLoadBakeReport();
            Debug.Log($"Appending {numLevels} to the Scene list");
            prevBiomesID = BiomeIDs.Last();
            RefreshSceneList();
            int startingLevels = ScenePaths.Length;
            for (int i = 0; i < numLevels; i++)
            {
                if (!await GenerateMap(island, startingLevels + i))
                {
                    succeeded = false;
                }
                prevBiomesID = BiomeIDs.Last();
            }

            ScenePaths = SceneAssets.Select(AssetDatabase.GetAssetPath).ToArray();
            EditorUtility.ClearProgressBar();
        }
        catch (Exception e)
        {
            if (!IsMidBuild)
                throw;
            
            Console.WriteLine($"Map Generation FAILED: {e}");
            return false;
        }

        SaveBakeReport();
        return succeeded;
    }
    
    [Button]
    async void AppendMaps()
    {
        try
        {
            await AppendLevels(CurrentSettings.Island, DesiredAmountOfLevelsAppended);
            AssetDatabase.SaveAssetIfDirty(this);
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // Bitshifts a value by an amount of places with the original bits wrapping back around
    int CircleShift(int originalValue, int amountToShift)
    {
        var intSize = sizeof(int);
        var clampedShift = amountToShift % intSize;
        return (originalValue >> clampedShift) | (originalValue << intSize);
    }
    
    async Awaitable<bool> GenerateMap(SceneAsset island, int i)
    {
        string islandAssetPath = AssetDatabase.GetAssetPath(island);
        var newIslandName = $"{GeneratedLevelPrefix}{i}";
        string newAssetPath = $"{GeneratedLevelDirectory}/{newIslandName}.unity";
        Debug.Log($"Copying {islandAssetPath} to {newAssetPath}");
        bool success = AssetDatabase.CopyAsset(islandAssetPath, newAssetPath);
        if (!success)
        {
            Debug.LogError($"Failed to copy {islandAssetPath}");
            return false;
        }
        
        Debug.Log($"Preparing {newIslandName} for generation.");
        AssetDatabase.ImportAsset(newAssetPath);
        var newIslandAsset = (SceneAsset)AssetDatabase.LoadAssetAtPath(newAssetPath, typeof(SceneAsset));
        SceneAssets.Add(newIslandAsset);
        var newIslandScene = EditorSceneManager.OpenScene(newAssetPath);

        Debug.Log($"Beginning level generation on {newIslandName}");
        var levelGeneration = FindFirstObjectByType<LevelGeneration>();
        var veryRandomSeed = CircleShift(
            Random.Range(int.MinValue, int.MaxValue), 
            Environment.TickCount);
        levelGeneration.seed = veryRandomSeed;
        var biomeSelectors = levelGeneration.GetComponentsInChildren<BiomeSelector>();
        for (int x = 0; x < biomeSelectors.Length; x++)
        {
            if (x < selectedBiomes[i].biomeTypes.Count)
            {
                Debug.Log($"Trying to select biome {selectedBiomes[i].biomeTypes[x]} for index {x}");
                biomeSelectors[x].Select(selectedBiomes[i].biomeTypes[x]);
            }
            else
            {
                biomeSelectors[x].Select();
            }
        }

        var genSteps = levelGeneration.GetComponentsInChildren<IGenConfigStep>();
        foreach (var selector in genSteps)
        {
            selector.RunStep();
        }

        await levelGeneration.Generate();
        LightVolume.Instance().lightMapData.DumpUncompressedLightmap();
        PruneUnusedBiomesAndRecordVariant(levelGeneration, i);
        PhotonViewHandler.OnHierarchyChanged();
        MapConvertionTools.ReplaceSpawnedItems();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log($"Saved after generation {newAssetPath}");
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssetIfDirty(this);
        AssetDatabase.SaveAssets();
        return true;
    }


    [Button]
    public void GenerateBiomeSelection()
    {
        GenerateBiomeSelection(CurrentSettings.DesiredAmountOfLevels);
    }
    
    void PruneUnusedBiomesAndRecordVariant(LevelGeneration root, int levelIdx)
    {
        const string NoVariant = "None";
        var currentBiomeResult = selectedBiomes[levelIdx];
        var biomes = root.GetComponentsInChildren<Biome>(includeInactive:true);
        Debug.Log($"Going through {biomes.Length} biomes to find the active ones");
        var prunedBiomes = new List<string>();
        var prunedVariants = new List<string>();
        foreach (var biome in biomes)
        {
            if (!biome.isActiveAndEnabled)
            {
                prunedBiomes.Add(biome.name);
                DestroyImmediate(biome.gameObject);
                continue;
            }
            
            string variantName = "Default";
            var oldVariantSelector = biome.GetComponentInChildren<VariationSwapper>();
            var newVariantSelector = biome.GetComponentInParent<VariantObjectSelector>();
            if (oldVariantSelector != null)
            {
                variantName = FindVariantAndPruneSwapper(oldVariantSelector);
            }
            else if (newVariantSelector != null)
            {
                variantName = FindVariantAndPruneVariantObjects(biome);
            }
            else
            {
                Debug.Log($"Failed to find the variant selector for {biome.name}!", biome);
            }
            
            Debug.Log($"Variant detected as: {variantName}");
            currentBiomeResult.variantNames.Add(variantName);
        }
        
        Debug.Log($"Pruned {prunedBiomes.Count} un-used biomes: {prunedBiomes}" +
            $"\nPruned {prunedVariants.Count} un-used variants: {prunedVariants}");

        string FindVariantAndPruneVariantObjects(Biome variantsParent)
        {
            var rootsNaming = new Regex("(?<= ).*?(?= Variant)");
            string variantName = NoVariant;
            foreach (var variantObject in variantsParent.GetComponentsInChildren<VariantObject>(includeInactive:true))
            {
                if (!variantObject.isActiveAndEnabled)
                {
                    prunedVariants.Add(variantObject.gameObject.name);
                    DestroyImmediate(variantObject.gameObject);
                    continue;
                }

                if (variantName != NoVariant)
                {
                    Debug.LogError($"{variantName} was identified as active variant but {variantObject.name} is also active!", variantObject);
                    continue;
                }
                
                variantName = rootsNaming.IsMatch(variantObject.name)
                    ? rootsNaming.Match(variantObject.name).Value
                    : variantObject.name;
            }

            return variantName;
        }

        string FindVariantAndPruneSwapper(VariationSwapper oldSelector)
        {
            string variantName = NoVariant;
            Debug.Log("Old variant selector detected. Going through transform children...");
            var numSelectorChildren = oldSelector.transform.childCount;
            var variantParents = oldSelector.Variations.Select(v => v.parent).ToList();
            for (int i = numSelectorChildren - 1; i >= 0; --i)
            {
                var child = oldSelector.transform.GetChild(i);
                if (!child.gameObject.activeInHierarchy)
                {
                    prunedVariants.Add(child.gameObject.name);
                    DestroyImmediate(child.gameObject);
                    continue;
                }

                if (variantParents.Contains(child.gameObject))
                {
                    if (variantName != NoVariant)
                    {
                        Debug.LogError($"Uh oh! {child.gameObject.name} is active in the hierarchy but we " +
                                       $"already recorded {variantName} as active variant...", child.gameObject);
                    }
                    else
                    {
                        variantName = child.gameObject.name;
                    }
                }
            }

            return variantName;
        }
    }

    private int MAX_REPETITION_TRIES = 50;
    public void GenerateBiomeSelection(int count)
    {
        selectedBiomes.Clear();
        BiomeIDs.Clear();
        
        for (int i = 0; i < count; i++)
        {
            List<Biome.BiomeType> selectedBiomeTypes = new();

            BiomeResult biomeResult = null;

            int tries = 0;

            bool success = false;

            while (!success)
            {
                selectedBiomeTypes.Clear();
                // This foreach loop is cycling through all Biome selections for a SINGLE MAP
                foreach (BiomeSelection currentBiomeSelection in CurrentSettings.biomeSelection)
                {
                    int intResult = currentBiomeSelection.GetSelectionResult(out BiomeSelectionOption result);
                    
                    selectedBiomeTypes.Add(result.biome);
                }

                biomeResult = new BiomeResult(selectedBiomeTypes);

                if (tries >= MAX_REPETITION_TRIES)
                {
                    Debug.Log($"Timed out attempts. Adding a map that may have repetitions: {biomeResult}");
                    success = true;
                }
                
                // Validate - see if biomes repeated in an invalid way
                else success = Validate(biomeResult, i);

                if (success)
                {
                    Debug.Log($"Found valid biome result: {biomeResult}");
                }
                else
                {
                    Debug.Log($"Biome result has repetitions. Trying again: {biomeResult}");
                }
                tries++;
            }
            
            
            selectedBiomes.Add(biomeResult);
            BiomeIDs.Add(biomeResult.ToString());
        }
    }

    public bool Validate(BiomeResult result, int currentIndex)
    {
        if (result == null) return false;

        int recentIdentical = CountRecentIdenticalSelections(result, currentIndex - 1, 
            CurrentSettings.preventMoreThanXIdenticalBiomeSelectionsInARow + 1);
        if (recentIdentical >= CurrentSettings.preventMoreThanXIdenticalBiomeSelectionsInARow)
        {
            return false;
        }
        
        for (int i = 0; i < result.biomeTypes.Count; i++)
        {
            if (ShouldPreventBiomeRepetition(i))
            { 
                int preventMoreThanThis = GetBiomeRepetitionPreventionCount(result.biomeTypes[i]);
                int recentIdenticalBiomes =
                    CountRecentIdenticalBiomes(result, i, currentIndex - 1, preventMoreThanThis + 1);
                Debug.Log($"{result.biomeTypes[i]} repeated {recentIdenticalBiomes} times.");
                if (recentIdenticalBiomes >= preventMoreThanThis)
                {
                    Debug.Log($"Invalidated {result} due to {result.biomeTypes[i]} appearing too often.");
                    return false;
                }
            }
        }

        return true;
    }

    public int CountRecentIdenticalSelections(BiomeResult currentToCompare, int startingIndex, int breakAfter = -1)
    {
        int result = 0;
        
        for (int i = startingIndex; i >= 0; i--)
        {
            if (currentToCompare.IsIdenticalTo(selectedBiomes[i]))
            {
                result++;
                if (breakAfter != -1 && result >= breakAfter) return result;
            }
            else break;
        }

        return result;
    }

    public int CountRecentIdenticalBiomes(BiomeResult currentToCompare, int biomeIndex, int startingIndex, int breakAfter = -1)
    {
        int result = 0;
        
        for (int i = startingIndex; i >= 0; i--)
        {
            if (currentToCompare.IsIdenticalTo(selectedBiomes[i], biomeIndex))
            {
                result++;
                if (breakAfter != -1 && result >= breakAfter) return result;
            }
            else break;
        }

        return result;
    }

    BiomeSelectionOption GetBiomeSelectionOption(Biome.BiomeType type)
    {
        foreach (BiomeSelection selection in CurrentSettings.biomeSelection)
        {
            foreach (BiomeSelectionOption option in selection.biomeOptions)
            {
                if (option.biome == type) return option;
            }
        }
        return null;
    }

    bool ShouldPreventBiomeRepetition(int biomeIndex)
    {
        if (biomeIndex >= CurrentSettings.biomeSelection.Count) return false;
        return CurrentSettings.biomeSelection[biomeIndex].biomeOptions.Count > 1;
    }

    int GetBiomeRepetitionPreventionCount(Biome.BiomeType type)
    {
        var option = GetBiomeSelectionOption(type);
        if (option == null) return 0;
        return option.preventMoreThanXInARow;
    }
    
    
#endif // UNITY_EDITOR
    
    [System.Serializable]
    public class BiomeResult
    {
        public List<Biome.BiomeType> biomeTypes;
        public List<string> variantNames;
        
        public BiomeResult(List<Biome.BiomeType> biomeTypes)
        {
            this.biomeTypes = new List<Biome.BiomeType>(biomeTypes);
            variantNames = new List<string>();
        }
        
        public bool IsIdenticalTo(BiomeResult other)
        {
            for (int i = 0; i < biomeTypes.Count && i < other.biomeTypes.Count; i++)
            {
                if (other.biomeTypes[i] != biomeTypes[i])
                {
                    return false;
                }
            }

            return true;
        }
        
        public bool IsIdenticalTo(BiomeResult other, int biomeIndex)
        {
            if (biomeIndex >= other.biomeTypes.Count || biomeIndex >= biomeTypes.Count) return false;
            
            if (other.biomeTypes[biomeIndex] != biomeTypes[biomeIndex])
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            string s = "";
            foreach(Biome.BiomeType biome in biomeTypes)
            {
                s += biome.ToString().ToUpper()[0];
            }

            return s;
        }
    }
    
    public string GetBiomeID(int levelIndex)
    {
        if (BiomeIDs.Count == 0)
        {
            return "";
        }

        levelIndex %= BiomeIDs.Count;

        return BiomeIDs[levelIndex];
    }
    
    public string GetLevel(int levelIndex)
    {
        if (ScenePaths.Length == 0)
        {
            Debug.LogError("No levels found, using WilIsland...");
            return "";
        }
        
        levelIndex %= ScenePaths.Length;

        var fileName = 
            PathUtil.WithoutExtensions(PathUtil.GetFileName(ScenePaths[levelIndex]));

        if (Application.isEditor)
        {
            #if UNITY_EDITOR
            foreach(EditorBuildSettingsScene editorScene in EditorBuildSettings.scenes)
            {
                if (editorScene.enabled && editorScene.path.Contains(fileName))
                {
                    return fileName;
                }
            }
            
            Debug.LogError($"could not get level {fileName}, using WilIsland...");
            return "";
            
            #endif
        }

        return fileName;
    }
}
