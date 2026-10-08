using System;
using System.Collections.Generic;
using System.Linq;
using Peak;
using Sirenix.OdinInspector;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

[ExecuteInEditMode]
public class LightVolume : MonoBehaviour
{
    public bool showVolumeGizmos = true;

    [FoldoutGroup("SETTINGS")]
    public float brightness = 1f;
    [FoldoutGroup("SETTINGS")]
    public float ambienceStrength = 1f;
    [FoldoutGroup("SETTINGS")]
    public float ambienceMin = .05f;

    [OnValueChanged(nameof(brightness))]
    [OnValueChanged(nameof(ambienceStrength))]
    [OnValueChanged(nameof(ambienceMin))]
    private void SetShaderVars()
    {
        Shader.SetGlobalFloat("_Brightness", brightness);
        Shader.SetGlobalFloat("_AmbienceStrength", ambienceStrength);
        Shader.SetGlobalFloat("_AmbienceMin", ambienceMin);
        Shader.SetGlobalVector("_GridRes", new Vector4(gridRes.x, gridRes.y, gridRes.z));
        Shader.SetGlobalFloat("_RaySpacing", raySpacing);
        Shader.SetGlobalVector("_GridOffset", gridOffset);
    }

    [FoldoutGroup("SETTINGS")]
    public Color skyColor = Color.white;
    [FoldoutGroup("SETTINGS")]
    public FilterMode filterModeRT = FilterMode.Bilinear;
    [FoldoutGroup("SETTINGS")]
    public TextureFormat textureFormat = TextureFormat.RGBAHalf;
    // [FoldoutGroup("SETTINGS")]
    // public bool forceBuildCompressionInEditor = false;
    // [FoldoutGroup("SETTINGS")]
    // public bool buildCompressionIsHighQuality = true;

    [FoldoutGroup("SETTINGS")]
    public int rayCount = 128;
    [FoldoutGroup("SETTINGS")]
    public float raySpacing = 1.5f;

    [FoldoutGroup("SETTINGS")]
    [Tooltip("Renderers matching this mask will be used for light tracing, colliders not matching will be ignored")]
    public LayerMask occluderMask = ~0;

    [FoldoutGroup("SETTINGS")][Tooltip("Radius (in texels) for how much to box blur the output texture")]
    public int blurRadius;
    [Sirenix.OdinInspector.ReadOnly] public uint3 gridRes;
    [Sirenix.OdinInspector.ReadOnly] public Vector3 gridOffset;
    [Sirenix.OdinInspector.ReadOnly] public Vector3 gridCenter;

    [FoldoutGroup("REFS")]public GameObject sceneParent;
    [FoldoutGroup("REFS")]public ComputeShader computeShader;
    [FoldoutGroup("REFS")]public RayTracingShader rayTracingShaderHardware;
    [FoldoutGroup("REFS")]public ComputeShader rayTracingShaderCompute;
    static IRayTracingShader rayTracingShader;

    //[FoldoutGroup("DEBUG"), NonSerialized, ShowInInspector] Texture3D lightMap;
    [FoldoutGroup("DEBUG")] public CompressableLightMap lightMapData;

    // public bool useHardcoded;
    // // VALUES SPECIFICALLY FOR RAY SPACING = 4 
    // [ShowInInspector]
    // static readonly uint3 HardcodedGridRes = new uint3(180, 360, 720);
    // [ShowInInspector]
    // static readonly Vector3 HardcodedGridOffset = new Vector3(-350f, -160f, -510f);
    // Color32[] _lightMapData;
    // [ShowInInspector]
    // Texture3D _decompressedLightMap;

    // [FoldoutGroup("DEBUG")]
    // public bool loadDirect = true;
    
    
    [Button, DisableIf("Running")]
    void LoadLightMapData()
    {
        if (lightMapData.HasCompressedData)
        {
            lightMapData.Decompress();
            gridRes = (uint3)lightMapData.gridRes;
            gridOffset = lightMapData.gridOffset;
            gridCenter = lightMapData.gridCenter;
        }
        else if (lightMapData.Uncompressed == null)
        {
            Debug.LogWarning("No lightmap data to load!");
            return;
        }

        //lightMap = lightMapData.Uncompressed;
        Shader.SetGlobalTexture("_LightMap", lightMapData.Uncompressed);
    }

    [Button, DisableIf("Running")]
    void DumpUncompressedData()
    {
        Shader.SetGlobalTexture("_LightMap", null);
        lightMapData.DumpUncompressedLightmap();
    }

    static bool isAutomatedBake = false;

    private void OnDrawGizmosSelected()
    {
        if (!showVolumeGizmos)
            return;

        Vector3 size = (float3)gridRes * raySpacing;

        Gizmos.color = Color.black;
        Gizmos.DrawWireCube(gridOffset + size * 0.5f - Vector3.one * 0.25f, size);
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(gridOffset + size * 0.5f + Vector3.one * 0.25f, size);
    }

    void Awake()
    {
#if UNITY_EDITOR
        isAutomatedBake = UnityEditorInternal.InternalEditorUtility.inBatchMode;
#endif
    }

    void OnEnable()
    {
        instance = this;
        // Ensure lightmap gets loaded in editor
        if (!Application.isPlaying && lightMapData.HasCompressedData)
            LoadLightMapData();
    }

    void OnDisable()
    {
        Dispose();
    }

    private void Start()
    {
        if (!lightMapData.HasCompressedData)
            return;

        LoadLightMapData();
        SetShaderVars();

        // Debug.Log($"Lightmap {lightMap} applied with a size of " +
        //           $"({lightMap?.width}, {lightMap?.height}, {lightMap?.depth})");
    }

#if UNITY_EDITOR
    void Reset()
    {
        LoadShaders();
    }
#endif

    public static async void BakeAll()
    {
        if (TryGetInstance(out LightVolume lightVolume))
        {
            lightVolume.SetSize();
            await lightVolume.Bake();
        }
    }

    Awaitable _runner;
    bool Running
    {
        get
        {
            if (_runner == null)
                return false;
            if (!_runner.GetAwaiter().IsCompleted)
                return true;
            // On the frame that we see IsCompleted, we need to kill the reference
            // to the Awaitable because the pointer will get freed
            _runner = null;
            return false;
        }
    }

    [Button("Bake"), DisableIf("Running")]
    public void BakeButton(bool skipCompression = true)
    {
        SetSize();
        _runner = Bake(skipCompression);
        _runner.GetAwaiter().OnCompleted(() =>
        {
            try
            {
                var awaiter = _runner.GetAwaiter();
                _runner = null;
                awaiter.GetResult();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        });
    }

    public async Awaitable Bake(bool skipCompression = false)
    {
        Dispose();

        //await WaitForFence();

        RayTracingResources rtResources = new();
        rtResources.LoadFromRenderPipelineResources();

        RayTracingBackend backend = RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware) 
            ? RayTracingBackend.Hardware 
            : RayTracingBackend.Compute;
        Debug.Log($"Baking light volume with backend {backend} for {SystemInfo.graphicsDeviceType}", this);
        RayTracingContext context = new RayTracingContext(backend, rtResources);
        rayTracingShader = context.CreateRayTracingShader(backend == RayTracingBackend.Hardware ? rayTracingShaderHardware : rayTracingShaderCompute);
        RenderTexture rawRT = Create3DTexture(FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, (int3)gridRes);

        await RunBake(context, rawRT);

        var lightMapRT = RunBlur(rawRT, out var toDestroy);
        // if (SystemInfo.supportsAsyncCompute)
        // {
        //     bakeFence = Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation,
        //         SynchronisationStageFlags.ComputeProcessing);
        //     await WaitForFence();
        // }
        // else
        // {
        //     Debug.Log("Skipping fence because we assume this is happening synchronously?");
        // }
        
        lightMapRT.name = "LightVolumeRenderTexture";

        SetShaderVars();
        //Shader.SetGlobalTexture("_LightMap", lightMapRT);

        await SaveTex(lightMapRT, skipCompression);
        DestroyImmediate(lightMapRT);
        if (blurRadius > 0)
            DestroyImmediate(toDestroy);
        LoadLightMapData();
#if UNITY_EDITOR
        if (!skipCompression) 
            UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    #region Set Size
    [Button, DisableIf("Running")]
    public void SetSize()
    {
        lightMapData.Clear();

        //if (useHardcoded)
        //{
        //    gridOffset = HardcodedGridOffset;
        //    gridRes = HardcodedGridRes;
        //    gridCenter = gridOffset + raySpacing / 2 * new Vector3(gridRes.x, gridRes.y, gridRes.z);
        //}
        //else
        //{
            var total = GetTotalBounds();
            gridOffset = total.min;
            gridCenter = total.center;
            gridRes = new uint3(
                CeilToMultiple(total.size.x / raySpacing, 4),
                CeilToMultiple(total.size.y / raySpacing, 4),
                CeilToMultiple(total.size.z / raySpacing, 4));
        //}
        // gridRes = new uint3(
        //     (uint)Mathf.CeilToInt((total.size.x + 3) / raySpacing),
        //     (uint)Mathf.CeilToInt((total.size.y + 3) / raySpacing),
        //     (uint)Mathf.CeilToInt((total.size.z + 3) / raySpacing)); 
    }

    void EncapsulateAndCheck(ref Bounds bounds, Renderer newRenderer)
    {
        var parentMeshName = newRenderer.transform.parent != null
            ? newRenderer.transform.parent.name
            : "";
        // This is sneaking into our lightmap bake and making everything stupid
        if (parentMeshName == "WaterMesh" || parentMeshName == "Water")
            return;
        var testBounds = bounds;
        testBounds.Encapsulate(newRenderer.bounds);
        var maxSize = 2048 * raySpacing;
        if (testBounds.size.x > maxSize || testBounds.size.y > maxSize || testBounds.size.z > maxSize)
        {
            Debug.LogWarning($"Cannot include {newRenderer.name} in bounds because it would go over max size", newRenderer);
        }
        else
        {
            bounds = testBounds;
        }
    }

    private Bounds GetTotalBounds()
    {
        var bounds = new Bounds();
        bounds.SetMinMax(Vector3.positiveInfinity, Vector3.negativeInfinity);

        IterateRenderers(meshRenderer => EncapsulateAndCheck(ref bounds, meshRenderer));

        return bounds;
    }
    #endregion Set Size

#region Bake
    static GraphicsFence? bakeFence;
    static Action disposeAction;

#if !UNITY_EDITOR
    static AssetBundle assetBundle;
#endif

    private async Awaitable RunBake(RayTracingContext context, RenderTexture target)
    {
        int lightCount = BuildLights(out ComputeBuffer lightBuffer);
        IRayTracingAccelStruct meshes = BuildMeshes(context, lightCount, out GraphicsBuffer buildScratchBuffer);

        uint sliceSizeVoxel = gridRes.x * gridRes.y;
        ulong sliceSizeBytes = rayTracingShader.GetTraceScratchBufferRequiredSizeInBytes(gridRes.x, gridRes.y, 1);

        uint sliceInterval;

        const uint maxVoxels = 3000000;

        if (sliceSizeBytes == 0u)
            sliceInterval = gridRes.z;
        else
            sliceInterval = math.min(maxVoxels / sliceSizeVoxel, (uint)(int.MaxValue / sliceSizeBytes));

        GraphicsBuffer traceScratchBuffer = RayTracingHelper.CreateScratchBufferForTrace(rayTracingShader, gridRes.x, gridRes.y, sliceInterval);

        if (traceScratchBuffer != null)
            disposeAction += traceScratchBuffer.Dispose;

        if (sliceInterval == 0)
            throw new Exception("Light volume slice size is greater than 2147483648");

        uint remainingSliceCount = gridRes.z;

    iterateSlice:
        CommandBuffer cmd = new();
        rayTracingShader.SetIntParam(cmd, Shader.PropertyToID("subdivisionOffset"), (int)(gridRes.z - remainingSliceCount));

        rayTracingShader.SetTextureParam(cmd, Shader.PropertyToID("lightMap"), target);
        rayTracingShader.SetFloatParam(cmd, Shader.PropertyToID("texelSize"), raySpacing);
        rayTracingShader.SetVectorParam(cmd, Shader.PropertyToID("originOffset"), gridOffset);
        rayTracingShader.SetVectorParam(cmd, Shader.PropertyToID("skyColor"), skyColor);
        rayTracingShader.SetIntParam(cmd, Shader.PropertyToID("rayCount"), rayCount);

        meshes.Build(cmd, buildScratchBuffer);
        rayTracingShader.SetAccelerationStructure(cmd, "_AccelStruct", meshes);

        rayTracingShader.SetBufferParam(cmd, Shader.PropertyToID("lightBuffer"), lightBuffer);
        rayTracingShader.SetIntParam(cmd, Shader.PropertyToID("lightBufferLength"), lightCount);


        for (int lightIndex = 0; lightIndex < lightCount + 1; lightIndex++)
        {
            rayTracingShader.SetIntParam(cmd, Shader.PropertyToID("doLightIndex"), lightIndex);
            rayTracingShader.Dispatch(cmd, traceScratchBuffer, gridRes.x, gridRes.y, math.min(remainingSliceCount, sliceInterval));
        }

        // if (SystemInfo.supportsAsyncCompute)
        // {
        //     bakeFence = cmd.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, 
        //         SynchronisationStageFlags.AllGPUOperations);
        // }
        // else
        // {
        //     Debug.Log("Skipping fence because we assume this is happening synchronously?");
        //     bakeFence = null;
        // }
        Debug.Log($"Dispatching {math.min(remainingSliceCount, sliceInterval)} slices for {lightCount} lights at offset {(int)(gridRes.z - remainingSliceCount)}");
        Graphics.ExecuteCommandBuffer(cmd);
        //await WaitForFence();

        if (remainingSliceCount > sliceInterval)
        {
            remainingSliceCount -= sliceInterval;

            if (Application.isPlaying)
            {
                await Awaitable.EndOfFrameAsync();
            }
            else
            {
                await Awaitable.WaitForSecondsAsync(1f/30f);
            }
            Debug.Log($"Adding {remainingSliceCount} more slices");
            goto iterateSlice;
        };
        
        //Debug.Log("Waiting for GPU Fence");

        // Async commented out because it isn't supported
        //Graphics.ExecuteCommandBufferAsync(cmd, ComputeQueueType.Urgent);

        //if (SystemInfo.supportsAsyncCompute)
        //    bakeFence = cmd.CreateAsyncGraphicsFence();

        //await WaitForFence();
        Debug.Log("Bake has been dispatched!");
    }

    private static RenderTexture Create3DTexture(FilterMode filterMode, RenderTextureFormat format, int3 resolution)
    {
        var tex = new RenderTexture(resolution.x, resolution.y, 0)
        {
            enableRandomWrite = true,
            format = format,
            dimension = TextureDimension.Tex3D,
            volumeDepth = resolution.z,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = filterMode,
            hideFlags = HideFlags.DontSave
            //HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor,
        };

        if (!tex.Create())
            throw new Exception("Failed to create texture");

        return tex;
    }

    private int BuildLights(out ComputeBuffer lightBuffer)
    {
        var lights = new List<GpuLight>();

        IterateComponents<BakedVolumeLight>(light => light.enabled, light =>
        {
            var color = new Vector3(light.color.r, light.color.g, light.color.b);
            var coneSize = light.mode switch
            {
                BakedVolumeLight.LightModes.Spot => light.coneSize * Mathf.Deg2Rad,
                BakedVolumeLight.LightModes.Point => 0,
                _ => throw new Exception(),
            };
            lights.Add(new GpuLight
            {
                Position = light.transform.position,
                ConeSize = coneSize,
                Direction = light.transform.forward,
                Radius = light.radius,
                Color = color * light.intensity,
                Falloff = light.falloff,
                ConeFalloff = light.coneFalloff,
            });
        });

        int count = lights.Count;
        // zero-length buffers aren't supported (sigh), so just do a stupid hack
        if (count == 0)
            lights.Add(default);
        lightBuffer = new ComputeBuffer(lights.Count, sizeof(float) * 13);
        lightBuffer.SetData(lights);

        disposeAction += lightBuffer.Dispose;

        return count;
    }

    #if UNITY_EDITOR
    [FoldoutGroup("DEBUG"), ShowInInspector]
    bool MeshBaked 
    {
        get
        {
            var selection = UnityEditor.Selection.activeGameObject;
            if (!selection || !selection.TryGetComponent<MeshFilter>(out var meshFilter))
                return false;
            return !UnreadableMeshes.Contains(meshFilter) && !NoVertexAttributeMeshes.Contains(meshFilter);
        }
    }
    #endif
    [FoldoutGroup("DEBUG"), ShowInInspector]
    List<MeshFilter> UnreadableMeshes = new List<MeshFilter>();
    [FoldoutGroup("DEBUG"), ShowInInspector]
    List<MeshFilter> NoVertexAttributeMeshes = new List<MeshFilter>();
    private IRayTracingAccelStruct BuildMeshes(RayTracingContext context, int lightCountForDebug, out GraphicsBuffer buildScratchBuffer)
    {
        UnreadableMeshes.Clear();
        NoVertexAttributeMeshes.Clear();
        //var parent = sceneParent == null ? gameObject : sceneParent;

        var options = new AccelerationStructureOptions()
        {
            buildFlags = BuildFlags.MinimizeMemory
        };
        IRayTracingAccelStruct meshes = context.CreateAccelerationStructure(options);

        int numMesh = 0;
        uint numIndicies = 0u;
        int numVerticies = 0;
        List<MeshInstanceDesc> allInstanceDescriptions = new List<MeshInstanceDesc>();
        var numUnreadableMeshes = 0;
        var numFailedArgCheck = 0;

        IterateRenderers(renderer =>
        {
            var meshFilter = renderer.GetComponent<MeshFilter>();
            var mesh = meshFilter.sharedMesh;
            if (mesh == null)
                return;

            if (!mesh.isReadable)
            {
                if (!isAutomatedBake)
                    UnreadableMeshes.Add(meshFilter);
                numUnreadableMeshes++;
                return;
            }

            numMesh++;
            numVerticies += mesh.vertexCount;

            int subMeshCount = mesh.subMeshCount;
            allInstanceDescriptions.Clear();

            for (int i = 0; i < subMeshCount; i++)
            {
                numIndicies += mesh.GetIndexCount(i);

                var instanceDesc = new MeshInstanceDesc(mesh, i)
                {
                    localToWorldMatrix = renderer.transform.localToWorldMatrix
                };
                if (!instanceDesc.mesh.HasVertexAttribute(VertexAttribute.Position))
                {
                    // Light baker will barf if we pass it this mesh
                    numFailedArgCheck++;
                    if (!isAutomatedBake)
                        NoVertexAttributeMeshes.Add(meshFilter);
                    break;
                }
                allInstanceDescriptions.Add(instanceDesc);
            }

            if (allInstanceDescriptions.Count != subMeshCount)
            {
                return;
            }

            foreach (var desc in allInstanceDescriptions)
            {
                meshes.AddInstance(desc);
            }
            // Need to find where flags go
            //var flags = new RayTracingSubMeshFlags[subMeshCount];
            //for (var i = 0; i < flags.Length; i++)
            //    flags[i] = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
        });

        buildScratchBuffer = RayTracingHelper.CreateScratchBufferForBuild(meshes);

        if (buildScratchBuffer != null)
            disposeAction += buildScratchBuffer.Dispose;

        Debug.Log(
            $"Light Volume Baker found: {lightCountForDebug} lights, {numMesh} meshes, {numIndicies} indices, {numVerticies} vertices. \n" +
            $"We skipped {numUnreadableMeshes} unreadable meshes and {numFailedArgCheck} for not having a vertex attribute");

        disposeAction += meshes.Dispose;

        return meshes;
    }

    private RenderTexture RunBlur(RenderTexture inputTex, out RenderTexture toDestroy)
    {
        toDestroy = null;
        if (blurRadius <= 0)
            return inputTex;

        computeShader.SetInt("blurRadius", blurRadius);

        var res = new int3(inputTex.width, inputTex.height, inputTex.volumeDepth);
        var outputTex = Create3DTexture(inputTex.filterMode, inputTex.format, res);

        // Both box and gaussian blurs are "seperable", which means you can do each axis one after the other and the
        // result is the same as running a full multidimentional blur all at once. Doing each axis one at a time is
        // *significantly* faster (`O(3n)` instead of `O(n^3)`), so do that. (Using box blur at the moment)
        for (var blurAxis = 0; blurAxis < 3; blurAxis++)
        {
            computeShader.SetTexture(1, "blurInputLightMap", inputTex);
            computeShader.SetTexture(1, "lightMap", outputTex);
            computeShader.SetInt("blurAxis", blurAxis);

            // GetKernelThreadGroupSizes is not supported on mac. Hardcode it.
            uint groupX = 4, groupY = 4, groupZ = 4;
            // computeShader.GetKernelThreadGroupSizes(1, out var groupX, out var groupY, out var groupZ);
            var launchX = (res.x + groupX - 1) / groupX;
            var launchY = (res.y + groupY - 1) / groupY;
            var launchZ = (res.z + groupZ - 1) / groupZ;
            computeShader.Dispatch(1, (int)launchX, (int)launchY, (int)launchZ);

            // Swap input and output to alternate back and forth
            (outputTex, inputTex) = (inputTex, outputTex);
        }
        
        toDestroy = outputTex;
        Debug.Log("Blur Pass Complete!");
        return inputTex;
    }

    async Awaitable GetPixelsAsync(RenderTexture renderTexture, NativeArray<byte> pixels)
    {
        // Must download data and SetPixelData instead of Graphics.CopyTexture because Graphics.CopyTexture does not set
        // the CPU side of the data, and the CPU side of the data must be set for serialization to work.
        var downloadedData = AsyncGPUReadback.Request(renderTexture);
        downloadedData.forcePlayerLoopUpdate = true;
        Debug.Log(("Downloading Light Volume Data from GPU asynchronously " +
            "(wave the mouse around if you're in Editor or this will never finish)"));
        
        if (!Application.isPlaying)
            downloadedData.WaitForCompletion();
        while (!downloadedData.done)
        {
            if (Application.isPlaying)
            {
                await Awaitable.EndOfFrameAsync();
            }
            // (this probably isn't needed since I call WaitForCompletion but I haven't tested getting rid of it)
            else
            {
                await Awaitable.WaitForSecondsAsync(1/30f);
            }
        }

        if (downloadedData.hasError)
        {
            Debug.LogError("Downloading data had an error");
            return;
        }
        else
            Debug.Log("GPU LightVolume downloaded. Copying read back data into pixel array!");

        for (var i = 0; i < downloadedData.layerCount; i++)
        {
            NativeArray<byte>.Copy(downloadedData.GetData<byte>(i), 0, pixels,
                i * downloadedData.layerDataSize, downloadedData.layerDataSize);
        }
    }

    private async Awaitable SaveTex(RenderTexture renderTexture, bool skipCompression)
    {
        var formatSizeUncompressed = CompressableLightMap.UncompressedBytesPerPixel;
        var numPixels = renderTexture.width * renderTexture.height * renderTexture.volumeDepth;
        var numBytesRaw = numPixels * formatSizeUncompressed;
        var rawData = new NativeArray<byte>(numBytesRaw, Allocator.Persistent); //[numBytesRaw];
        if (SystemInfo.supportsAsyncGPUReadback)
            await GetPixelsAsync(renderTexture, rawData);
        else
        {
            var request = AsyncGPUReadback.RequestIntoNativeArray(ref rawData, renderTexture);
            request.WaitForCompletion();
        }
        
        lightMapData = new CompressableLightMap(rawData, renderTexture.format, (int3)gridRes, gridOffset, gridCenter);
        rawData.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        if (skipCompression)
        {
            Debug.Log($"Memory footprint of lightmap is " +
                      $"{UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(lightMapData.Uncompressed) / 1000000}MB");
            return;
        }
        
        Debug.Log("Compressing lightmap. This could take a while...");
        // Give editor window time to catch up before we start compression
        if (!isAutomatedBake)
            await Awaitable.WaitForSecondsAsync(0.1f);
        lightMapData.CompressAndSerialize();
    }

    void Dispose()
    {
        disposeAction?.Invoke();
        disposeAction = null;
    }

    private async Awaitable WaitForFence()
    {
        if (bakeFence == null)
        {
            Debug.Log("No fence to wait for. Returning");
            return;
        }

        Debug.Log("Starting fence wait.");
        while (bakeFence is { passed: false })
        {
            if (Application.isPlaying)
            {
                await Awaitable.EndOfFrameAsync();
            }
            else
            {
                await Awaitable.WaitForSecondsAsync(0.1f);
            }
        }

        Debug.Log("Finished waiting for fence. Returning.");
        bakeFence = null;
    }
#endregion Bake

#if UNITY_EDITOR
    [Button("Load Shaders")]
    public void LoadShaders()
    {
        if (computeShader == null)
        {
            var assets = UnityEditor.AssetDatabase.FindAssets("t:computeshader LightVolumeRayTrace");
            computeShader =
                UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(assets[0]));

            UnityEditor.EditorUtility.SetDirty(this);
        }

        if (rayTracingShader == null)
        {
            var assets = UnityEditor.AssetDatabase.FindAssets("t:computeshader LightVolumeUnifiedRTShader");
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(assets[0]);

            rayTracingShaderHardware = UnityEditor.AssetDatabase.LoadAssetAtPath<RayTracingShader>(path);
            rayTracingShaderCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);

            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
#endif

    static uint CeilToMultiple(float value, int roundTo)
    {
        var ceilValue = math.ceil(value);
        var remainder = ceilValue % roundTo;

        return (uint)(ceilValue + roundTo - remainder);
    }

    bool CheckRendererValid(MeshRenderer meshRenderer)
    {
        if (!meshRenderer.enabled 
            || ((1 << meshRenderer.gameObject.layer) & occluderMask.value) == 0)
            return false;
        if (!meshRenderer.transform.parent ||
            !meshRenderer.transform.parent.TryGetComponent(out LODGroup lodGroup))
            return true;
        var lods = lodGroup.GetLODs();
        // LOD_1 is guaranteed to have a read/write mesh, making it a better renderer than the original
        var bestRenderer = lods.Length > 1
            ? lods[1].renderers?.FirstOrDefault()
            : lods.FirstOrDefault().renderers?.FirstOrDefault();
        return bestRenderer == meshRenderer;
    }

    void IterateComponents<T>(Func<T, bool> isValid, Action<T> action) where T : Component
    {
        if (sceneParent != null)
            IterateChildren(sceneParent);
        else
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                IterateChildren(root);

        void IterateChildren(GameObject parent)
        {
            foreach (var childComponent in parent.GetComponentsInChildren<T>())
                if (isValid?.Invoke(childComponent) != false)
                    action(childComponent);
        }
    }

    void IterateRenderers(Action<MeshRenderer> action)
    {
        IterateComponents(CheckRendererValid, action);
    }

    internal static LightVolume instance;
    public static LightVolume Instance()
    {
        if (instance == null)
            instance = FindAnyObjectByType<LightVolume>();

        return instance;
    }

    public static bool TryGetInstance(out LightVolume lightVolume)
    {
        if (instance == null)
            instance = FindAnyObjectByType<LightVolume>();

        lightVolume = instance;

        return lightVolume != null;
    }

    public Vector3 WorldPosToLightmapCoords(Vector3 worldPos)
    {
        return (worldPos - gridOffset) / raySpacing + 0.5f * Vector3.one;
    }

    public int LightMapCoordsToDataIndex(Vector3 lightMapCoords)
    {
        return (int)lightMapCoords.x + (int)gridRes.x * ((int)lightMapCoords.y + (int)gridRes.y * (int)lightMapCoords.z);
    }
    
    public Color SamplePosition(Vector3 worldPos, bool printDebug = false)
    {
        var uv = WorldPosToLightmapCoords(worldPos);
        // var oldPos = OldPositionToMap(worldPos);
        // var oldPixel = lightMap.GetPixel(
        //     (int)oldPos.x, (int)oldPos.y, (int)oldPos.z);
        //uv.Scale(new Vector3(1f/gridRes.x, 1f/gridRes.y, 1f/gridRes.z));
        var newPixel =
            //(Color)_lightMapData[Math.Clamp(LightMapCoordsToDataIndex(uv), 0, _lightMapData.Length - 1)];
            lightMapData.Uncompressed.GetPixel(
            (int)uv.x, (int)uv.y, (int)uv.z);
        // if (printDebug)
        //     Debug.Log($"Old sample would be at {oldPos} with alpha {oldPixel.a} while new pixel is at " +
        //           $"{uv} with alpha {newPixel.a}. These pixels are {Vector3.Distance(oldPos, uv)} units apart");
        
        return newPixel;
    }

    internal Vector3 OldPositionToMap(Vector3 worldPos)
    {
        worldPos -= gridCenter;
        worldPos += (Vector3)(raySpacing * (float3)gridRes * 0.5f);
        worldPos /= raySpacing;
        
        return worldPos;
    }

    public float SamplePositionAlpha(Vector3 worldPos, bool printDebug = false)
    {
        return SamplePosition(worldPos, printDebug).a;
    }

    struct GpuLight
    {
        public Vector3 Position;
        public float ConeSize;
        public Vector3 Direction;
        public float Radius;
        public Vector3 Color;
        public float Falloff;
        public float ConeFalloff;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Assets/Build AssetBundles To Landfall Lighting")]
    static void BuildAllAssetBundles()
    {
        var assets = UnityEditor.AssetDatabase.FindAssets("t:script LightVolume");
        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(assets[0]);
        path = path.Substring(0, path.LastIndexOf('/'));

        if (!System.IO.Directory.Exists(path))
            System.IO.Directory.CreateDirectory(path);

        UnityEditor.BuildPipeline.BuildAssetBundles(path,
                                        UnityEditor.BuildAssetBundleOptions.None,
                                        UnityEditor.BuildTarget.StandaloneWindows);
    }
#endif
}