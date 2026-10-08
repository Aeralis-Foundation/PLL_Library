using System;
using System.Collections;
using System.Collections.Generic;
using Peak;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace PLL.Library;

internal sealed class LightVolumeRebake
{
    readonly LightVolumeRun Owner;
    readonly MapRun Run;
    readonly MapSlot TargetSlot;
    readonly bool LightingGeometryDirty;
    readonly MapRun.FeatureWriter Writer;

    internal LightVolumeRebake(
        LightVolumeRun Owner,
        MapRun Run,
        MapSlot TargetSlot,
        bool LightingGeometryDirty,
        MapRun.FeatureWriter Writer)
    {
        this.Owner = Owner;
        this.Run = Run;
        this.TargetSlot = TargetSlot;
        this.LightingGeometryDirty = LightingGeometryDirty;
        this.Writer = Writer;
    }

    internal IEnumerator Execute()
    {
        if (!LightingGeometryDirty) yield break;
        Writer.RequireCurrent();
        Run.RequirePublished();
        var RetainedOperation = Writer.RetainOperation();
        LightVolumeBakeOperation? Bake = null;
        var AwaitPending = false;
        var Disposed = false;
        var Cleaned = false;

        void Cleanup()
        {
            if (Cleaned || AwaitPending) return;
            Cleaned = true;
            Exception? Failure = null;
            try { Bake?.Rollback(); }
            catch (Exception Exception) { Failure = Exception; }
            try { RetainedOperation.Dispose(); }
            catch (Exception Exception)
            {
                Failure = Failure is null
                    ? Exception
                    : new AggregateException(
                        "Light-volume cleanup and writer release both failed.", Failure, Exception);
            }
            if (Failure is not null) ExceptionPropagation.Throw(Failure);
        }

        try
        {
            Bake = Owner.BeginBake(Run, TargetSlot, Writer.RequireCurrent);
            var Unsupported = Bake.UnsupportedDeviceOrPipelineReason();
            if (Unsupported is null)
            {
                Bake.Begin();
                Writer.RequireCurrent();
                Unsupported = Bake.UnsupportedGridReason();
            }
            if (Unsupported is { } Reason)
            {
                var Page = Owner.RequestFallbackPage(Run, TargetSlot, Writer.RequireCurrent);
                try
                {
                    while (Page.MoveNext()) yield return Page.Current;
                }
                finally
                {
                    (Page as IDisposable)?.Dispose();
                }
                if (Owner.RecordUnsupportedReason(Reason))
                    Plugin.PLLLog.LogWarning(
                        $"Light-volume rebake was unavailable because {Reason}. "
                        + "The canonical projection cannot represent generated shelter geometry.");
                Bake.Rollback();
                Owner.SettleFallbackProjection(TargetSlot);
                Owner.PublishCompletedCurrent(Run, TargetSlot);
                yield break;
            }
            Writer.RequireCurrent();
            var Awaiter = Bake.StartBake().GetAwaiter();
            Exception? AwaitFailure = null;
            AwaitPending = !Awaiter.IsCompleted;

            void CompleteAwait()
            {
                try { Awaiter.GetResult(); }
                catch (Exception Exception) { AwaitFailure = Exception; }
                AwaitPending = false;
                if (!Disposed) return;

                var Failures = new List<Exception>(2);
                if (AwaitFailure is not null) Failures.Add(AwaitFailure);
                try { Cleanup(); }
                catch (Exception Exception) { Failures.Add(Exception); }
                if (Failures.Count == 0) return;
                var DetachedFailure = Failures.Count == 1
                    ? Failures[0]
                    : new AggregateException(
                        "Light-volume bake completion and detached cleanup failed.", Failures);
                Plugin.PLLLog.LogError(
                    $"Light-volume baking failed after its map iterator stopped. {DetachedFailure}");
            }

            if (AwaitPending)
            {
                try { Awaiter.OnCompleted(CompleteAwait); }
                catch (Exception)
                {
                    AwaitPending = false;
                    throw;
                }
            }
            else CompleteAwait();

            var CancellationObserved = false;
            while (AwaitPending)
            {
                yield return null;
                CancellationObserved |= Writer.CancellationRequested;
            }
            if (AwaitFailure is not null) ExceptionPropagation.Throw(AwaitFailure);
            if (CancellationObserved) throw MapRun.FeatureWriter.Cancelled();
            Bake.Stage();
        }
        finally
        {
            Disposed = true;
            Cleanup();
        }
    }
}

internal sealed class LightVolumeBakePlan
{
    internal LightVolumeBakePlan(
        bool Compute,
        int BlasInitialBytes,
        long MapBytes,
        long PeakAdditionalBytes,
        long AccelerationPeakBytes,
        long BuildScratchBytes,
        long TraceScratchBytes,
        long TopLevelBytes)
    {
        this.Compute = Compute;
        this.BlasInitialBytes = BlasInitialBytes;
        this.MapBytes = MapBytes;
        this.PeakAdditionalBytes = PeakAdditionalBytes;
        this.AccelerationPeakBytes = AccelerationPeakBytes;
        this.BuildScratchBytes = BuildScratchBytes;
        this.TraceScratchBytes = TraceScratchBytes;
        this.TopLevelBytes = TopLevelBytes;
    }

    internal bool Compute { get; }
    internal int BlasInitialBytes { get; }
    internal long MapBytes { get; }
    internal long PeakAdditionalBytes { get; }
    internal long AccelerationPeakBytes { get; }
    internal long BuildScratchBytes { get; }
    internal long TraceScratchBytes { get; }
    internal long TopLevelBytes { get; }
}
internal sealed class LightVolumeBakeOperation
{
    readonly LightVolumeRun Owner;
    readonly MapRun Run;
    readonly global::LightVolume Volume;
    readonly MapSlot TargetSlot;
    readonly Action RequireCurrent;
    readonly List<UnityEngine.Object> CreatedResources = new(2);
    RayTracingContext? Context;
    ComputeRayTracingAccelStruct? Acceleration;
    CommandBuffer? PendingCommandBuffer;
    GraphicsFence? SubmittedFence;
    LightVolumeBakeInventory? Inventory;
    LightVolumeBakePlan? Plan;
    LightVolumeVisualMap? StagedMap;
    MapLoadKeepAliveLease? KeepAlive;
    bool ComputeBackend;
    bool BakeAuthorized;
    bool ContextExpected;
    bool AccelerationExpected;
    bool BuildScratchExpected;
    bool TraceScratchExpected;
    bool TopLevelExpected;
    bool ReadableMapExpected;
    bool PendingCommandBufferRequiresFence;
    bool BakeStarted;
    bool BakeResourcesDisposed;
    bool Settled;
    int VolumeTextureCount;

    internal LightVolumeBakeOperation(
        LightVolumeRun Owner,
        MapRun Run,
        global::LightVolume Volume,
        LightVolumeVisualMap Previous,
        MapSlot TargetSlot,
        Action RequireCurrent)
    {
        this.Owner = Owner;
        this.Run = Run;
        this.Volume = Volume;
        this.Previous = Previous;
        this.TargetSlot = TargetSlot;
        this.RequireCurrent = RequireCurrent
            ?? throw new ArgumentNullException(nameof(RequireCurrent));
    }

    internal LightVolumeVisualMap Previous { get; }
    internal bool NeedsComputeResources => ComputeBackend;

    internal void Begin()
    {
        Volume.lightMapData = default;
        PublishPreviousGlobals();
    }

    internal string? UnsupportedDeviceOrPipelineReason()
    {
        if (!SystemInfo.supports3DTextures)
            return "the graphics device does not support 3D textures";
        if (!SystemInfo.supportsGraphicsFence)
            return "the graphics device does not support graphics fences";
        if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf)
            || !SystemInfo.SupportsTextureFormat(TextureFormat.R8))
            return "the graphics device lacks a required light-volume texture format";
        var Hardware = RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware);
        var Compute = RayTracingContext.IsBackendSupported(RayTracingBackend.Compute);
        if (!Hardware && !Compute) return "the graphics device has no supported ray-tracing backend";
        ComputeBackend = !Hardware;
        if (ComputeBackend
            && !GraphicsSettings.TryGetRenderPipelineSettings<RayTracingRenderPipelineResources>(out _)
            && !UnifiedRayTracingResources.TryPrepare(out var ResourceRejection))
            return ResourceRejection;
        if (Hardware && !Volume.rayTracingShaderHardware || ComputeBackend && !Volume.rayTracingShaderCompute)
            throw new InvalidOperationException("PEAK's selected light-volume ray-tracing shader is unavailable.");
        return null;
    }

    internal string? UnsupportedGridReason()
    {
        if (!LightVolumeBakeInventory.TryCapture(
                Volume, TargetSlot, ComputeBackend,
                out var CapturedInventory, out var InventoryRejection))
            return InventoryRejection;
        Inventory = CapturedInventory;
        if (!Owner.TryPrepareBake(
                Volume, ComputeBackend, CapturedInventory!,
                out var PreparedPlan, out var BudgetRejection)) return BudgetRejection;
        Plan = PreparedPlan;
        return null;
    }

    internal void PublishPreviousGlobals() => Owner.PublishVisualGlobals(Previous, TargetSlot);

    internal Awaitable StartBake()
    {
        if (BakeStarted) throw new InvalidOperationException("A light-volume operation started its bake twice.");
        var Value = PlanValue;
        Owner.RequireAdditionalAllocation(
            Value.PeakAdditionalBytes,
            Value.PeakAdditionalBytes,
            "bake working set");
        BakeStarted = true;
        KeepAlive = MapLoadKeepAliveLease.Acquire();
        ReadableMapExpected = true;
        BakeAuthorized = true;
        try { return Volume.Bake(skipCompression: true); }
        finally { BakeAuthorized = false; }
    }

    internal bool ConsumeBakeAuthorization(global::LightVolume Candidate)
    {
        if (Candidate != Volume || !BakeAuthorized) return false;
        BakeAuthorized = false;
        ContextExpected = true;
        return true;
    }

    internal bool TryTrackContext(RayTracingContext Candidate)
    {
        if (!ContextExpected) return false;
        if (Context is not null)
            throw new InvalidOperationException("The light-volume bake constructed two ray-tracing contexts.");
        ContextExpected = false;
        if ((Candidate.BackendType == RayTracingBackend.Compute) != PlanValue.Compute)
            throw new InvalidOperationException("PEAK selected a ray-tracing backend outside its bake plan.");
        Context = Candidate;
        AccelerationExpected = PlanValue.Compute;
        return true;
    }

    internal bool TryCreateComputeAcceleration(
        ComputeRayTracingBackend Backend,
        AccelerationStructureOptions Options,
        ReferenceCounter Counter,
        out IRayTracingAccelStruct? Result)
    {
        Result = null;
        var Value = PlanValue;
        if (!Value.Compute || !AccelerationExpected) return false;
        Owner.RequireAdditionalAllocation(
            Value.AccelerationPeakBytes,
            Value.PeakAdditionalBytes,
            "compute acceleration structure");
        AccelerationExpected = false;
        BuildScratchExpected = true;
        var Created = new ComputeRayTracingAccelStruct(
            Options, Backend.m_Resources, Counter, Value.BlasInitialBytes);
        Acceleration = Created;
        TopLevelExpected = true;
        Result = Created;
        return true;
    }

    internal bool TryRequireTopLevelAllocation(ComputeRayTracingAccelStruct Candidate)
    {
        if (!TopLevelExpected || !ReferenceEquals(Acceleration, Candidate)) return false;
        TopLevelExpected = false;
        var Value = PlanValue;
        Owner.RequireAdditionalAllocation(
            Value.TopLevelBytes,
            Value.PeakAdditionalBytes,
            "top-level acceleration structure");
        return true;
    }

    internal bool OwnsAcceleration(ComputeRayTracingAccelStruct Candidate) => ReferenceEquals(Acceleration, Candidate);
    internal bool OwnsContext(RayTracingContext Candidate) => Context is not null && ReferenceEquals(Context, Candidate);
    internal void RequireVolumeTextureAllocation()
    {
        var Value = PlanValue;
        Owner.RequireAdditionalAllocation(
            Value.MapBytes,
            Value.PeakAdditionalBytes,
            "3D render texture");
    }

    internal void TrackVolumeTexture(RenderTexture Resource)
    {
        if (!Resource) return;
        var ExpectedCount = Volume.blurRadius > 0 ? 2 : 1;
        if (VolumeTextureCount >= ExpectedCount)
            throw new InvalidOperationException("PEAK created an unexpected light-volume render texture.");
        VolumeTextureCount++;
        CreatedResources.Add(Resource);
    }

    internal bool TryRequireBuildScratchAllocation()
    {
        if (!BuildScratchExpected) return false;
        BuildScratchExpected = false;
        TraceScratchExpected = true;
        var Value = PlanValue;
        Owner.RequireAdditionalAllocation(
            Value.BuildScratchBytes,
            Value.PeakAdditionalBytes,
            "acceleration-structure scratch buffer");
        return true;
    }

    internal bool TryRequireTraceScratchAllocation(uint Width, uint Height, uint Depth)
    {
        if (!TraceScratchExpected) return false;
        TraceScratchExpected = false;
        var Value = PlanValue;
        var Bytes = Value.Compute
            ? checked((long)Width * Height * Depth * 256)
            : 0;
        if (Bytes > Value.TraceScratchBytes)
            throw new InvalidOperationException("PEAK exceeded the planned trace-scratch slice limit.");
        Owner.RequireAdditionalAllocation(
            Bytes,
            Value.PeakAdditionalBytes,
            "trace scratch buffer");
        return true;
    }

    internal void IterateRenderers(
        global::LightVolume Candidate,
        Action<MeshRenderer> Action)
    {
        if (Candidate != Volume)
            throw new InvalidOperationException("PEAK requested a foreign light-volume renderer inventory.");
        InventoryValue.IterateRenderers(Action);
    }

    internal void IterateLights(
        global::LightVolume Candidate,
        Func<BakedVolumeLight, bool>? IsValid,
        Action<BakedVolumeLight> Action)
    {
        if (Candidate != Volume)
            throw new InvalidOperationException("PEAK requested a foreign light-volume light inventory.");
        InventoryValue.IterateLights(IsValid, Action);
    }

    internal CommandBuffer TrackCommandBuffer(
        CommandBuffer Buffer,
        bool RequiresFence)
    {
        if (PendingCommandBuffer is not null)
            throw new InvalidOperationException("PEAK constructed overlapping light-volume command buffers.");
        PendingCommandBuffer = Buffer;
        PendingCommandBufferRequiresFence = RequiresFence;
        return Buffer;
    }

    internal void ExecuteCommandBuffer(
        CommandBuffer Buffer,
        bool RequiresFence)
    {
        if (!ReferenceEquals(PendingCommandBuffer, Buffer)
            || PendingCommandBufferRequiresFence != RequiresFence)
            throw new InvalidOperationException("PEAK submitted an unowned light-volume command buffer.");
        if (RequiresFence && SubmittedFence.HasValue)
            throw new InvalidOperationException("PEAK submitted another light-volume slice before its fence passed.");
        GraphicsFence? Fence = null;
        try
        {
            if (RequiresFence)
                Fence = Buffer.CreateGraphicsFence(
                    GraphicsFenceType.CPUSynchronisation,
                    SynchronisationStageFlags.ComputeProcessing);
            Graphics.ExecuteCommandBuffer(Buffer);
            SubmittedFence = Fence;
        }
        finally
        {
            Buffer.Dispose();
            PendingCommandBuffer = null;
            PendingCommandBufferRequiresFence = false;
        }
    }

    internal async Awaitable RunBake(RayTracingContext Context, RenderTexture Target)
    {
        Exception? Failure = null;
        try
        {
            var LightCount = Volume.BuildLights(out var LightBuffer);
            var Meshes = Volume.BuildMeshes(Context, LightCount, out var BuildScratch);
            var Shader = global::LightVolume.rayTracingShader;
            var Grid = Volume.gridRes;
            var TraceScratch = RayTracingHelper.CreateScratchBufferForTrace(Shader, Grid.x, Grid.y, 1);
            if (TraceScratch is not null) global::LightVolume.disposeAction += TraceScratch.Dispose;
            var SubdivisionOffset = UnityEngine.Shader.PropertyToID("subdivisionOffset");
            var LightMap = UnityEngine.Shader.PropertyToID("lightMap");
            var TexelSize = UnityEngine.Shader.PropertyToID("texelSize");
            var OriginOffset = UnityEngine.Shader.PropertyToID("originOffset");
            var SkyColor = UnityEngine.Shader.PropertyToID("skyColor");
            var RayCount = UnityEngine.Shader.PropertyToID("rayCount");
            var LightBufferProperty = UnityEngine.Shader.PropertyToID("lightBuffer");
            var LightBufferLength = UnityEngine.Shader.PropertyToID("lightBufferLength");
            var DoLightIndex = UnityEngine.Shader.PropertyToID("doLightIndex");
            for (uint Slice = 0; Slice < Grid.z; Slice++)
            {
                RequireCurrent();
                var Buffer = TrackCommandBuffer(new CommandBuffer(), RequiresFence: true);
                Shader.SetIntParam(Buffer, SubdivisionOffset, (int)Slice);
                Shader.SetTextureParam(Buffer, LightMap, Target);
                Shader.SetFloatParam(Buffer, TexelSize, Volume.raySpacing);
                Shader.SetVectorParam(Buffer, OriginOffset, Volume.gridOffset);
                Shader.SetVectorParam(Buffer, SkyColor, Volume.skyColor);
                Shader.SetIntParam(Buffer, RayCount, Volume.rayCount);
                if (Slice == 0) Meshes.Build(Buffer, BuildScratch);
                Shader.SetAccelerationStructure(Buffer, "_AccelStruct", Meshes);
                Shader.SetBufferParam(Buffer, LightBufferProperty, LightBuffer);
                Shader.SetIntParam(Buffer, LightBufferLength, LightCount);
                for (var LightIndex = 0; LightIndex <= LightCount; LightIndex++)
                {
                    Shader.SetIntParam(Buffer, DoLightIndex, LightIndex);
                    Shader.Dispatch(Buffer, TraceScratch, Grid.x, Grid.y, 1);
                }
                ExecuteCommandBuffer(Buffer, RequiresFence: true);
                await SettleSubmittedFence();
            }
        }
        catch (Exception Exception) { Failure = Exception; }
        try { await SettleSubmittedFence(); }
        catch (Exception Exception)
        {
            Failure = Failure is null
                ? Exception
                : new AggregateException(
                    "PEAK's light-volume bake and its final GPU fence both failed.", Failure, Exception);
        }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
        RequireCurrent();
    }

    internal async Awaitable SaveTex(RenderTexture Texture)
    {
        var Pixels = new NativeArray<byte>(
            checked(Texture.width * Texture.height * Texture.volumeDepth
                * CompressableLightMap.UncompressedBytesPerPixel), Allocator.Persistent);
        try
        {
            var Request = AsyncGPUReadback.RequestIntoNativeArray(ref Pixels, Texture, 0, null);
            Request.forcePlayerLoopUpdate = true;
            await AwaitReadback(Request);
            Volume.lightMapData = new CompressableLightMap(
                Pixels, Texture.format, (Unity.Mathematics.int3)Volume.gridRes,
                Volume.gridOffset, Volume.gridCenter);
        }
        finally { Pixels.Dispose(); }
    }

    internal async Awaitable AwaitReadback(AsyncGPUReadbackRequest Request)
    {
        Exception? AuthorityFailure = null;
        if (!SystemInfo.supportsAsyncGPUReadback || !Application.isPlaying)
            Request.WaitForCompletion();
        while (!Request.done)
        {
            if (Application.isPlaying) await Awaitable.NextFrameAsync();
            else await Awaitable.WaitForSecondsAsync(1f / 30f);
            if (AuthorityFailure is not null) continue;
            try { RequireCurrent(); }
            catch (Exception Exception) { AuthorityFailure = Exception; }
        }
        var ReadbackFailure = Request.hasError
            ? new InvalidOperationException("The asynchronous light-volume GPU readback failed.")
            : null;
        if (AuthorityFailure is not null && ReadbackFailure is not null)
            throw new AggregateException(
                "Light-volume ownership and GPU readback both failed.",
                AuthorityFailure,
                ReadbackFailure);
        if (AuthorityFailure is not null) ExceptionPropagation.Throw(AuthorityFailure);
        if (ReadbackFailure is not null) throw ReadbackFailure;
        RequireCurrent();
    }

    async Awaitable SettleSubmittedFence()
    {
        if (SubmittedFence is not { } Fence) return;
        while (!Fence.passed)
            if (Application.isPlaying) await Awaitable.NextFrameAsync();
            else await Awaitable.WaitForSecondsAsync(1f / 30f);
        SubmittedFence = null;
    }

    internal bool TryRequireReadableMapAllocation(Unity.Mathematics.int3 Grid)
    {
        if (!ReadableMapExpected || !Grid.Equals((Unity.Mathematics.int3)Volume.gridRes)) return false;
        ReadableMapExpected = false;
        var Value = PlanValue;
        Owner.RequireAdditionalAllocation(
            checked(Value.MapBytes * 2),
            Value.PeakAdditionalBytes,
            "readable staged texture");
        return true;
    }

    internal void Stage()
    {
        RequireCurrent();
        if (Settled) throw new InvalidOperationException("The light-volume operation was already settled.");
        LightVolumeVisualMap.ValidateNewMap(Volume);
        var Staged = LightVolumeVisualMap.Capture(Volume, RequireSampling: false);
        StagedMap = Staged;
        Previous.RestoreLive(Volume);
        PublishPreviousGlobals();
        DisposeBakeResources();
        Owner.PrepareVisual(Run, Previous, Staged, TargetSlot);
        StagedMap = null;
        Settled = true;
        Finish();
        try
        {
            LightVolumeVisualMap.DestroyResources(
                default, CreatedResources, Owner.CanonicalMap, Owner.CurrentMap, Staged);
        }
        finally { CreatedResources.Clear(); }
        Owner.PublishCompletedCurrent(Run, TargetSlot);
    }

    internal void Rollback()
    {
        if (Settled) return;
        var Partial = Volume ? Volume.lightMapData : default;
        List<Exception>? Failures = null;
        void Capture(Action Action)
        {
            try { Action(); }
            catch (Exception Exception) { (Failures ??= new List<Exception>(3)).Add(Exception); }
        }

        if (Volume) Capture(() =>
        {
            Previous.RestoreLive(Volume);
            PublishPreviousGlobals();
        });
        Capture(DisposeBakeResources);
        var Staged = StagedMap;
        StagedMap = null;
        if (Staged is not null && !ReferenceEquals(Staged, Owner.CurrentMap))
            Capture(() => LightVolumeVisualMap.Destroy(
                Staged, Owner.CanonicalMap, Owner.CurrentMap, Previous));
        Capture(() => LightVolumeVisualMap.DestroyResources(
            Partial, CreatedResources, Owner.CanonicalMap, Owner.CurrentMap, Previous));
        CreatedResources.Clear();
        Settled = true;
        Finish();
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Light-volume rollback failed.", Failures);
    }

    void DisposeBakeResources()
    {
        if (!BakeStarted || BakeResourcesDisposed) return;
        var CommandBuffer = PendingCommandBuffer;
        var OwnedAcceleration = Acceleration;
        var RayTracingContext = Context;
        PendingCommandBuffer = null;
        PendingCommandBufferRequiresFence = false;
        SubmittedFence = null;
        Acceleration = null;
        TopLevelExpected = false;
        Context = null;
        BakeResourcesDisposed = true;
        List<Exception>? Failures = null;
        void Release(Action Action)
        {
            try { Action(); }
            catch (Exception Exception) { (Failures ??= new List<Exception>(3)).Add(Exception); }
        }

        if (CommandBuffer is not null) Release(CommandBuffer.Dispose);
        Release(Volume.Dispose);
        if (OwnedAcceleration is not null) Release(OwnedAcceleration.Dispose);
        if (RayTracingContext is not null) Release(RayTracingContext.Dispose);
        if (KeepAlive is not null) Release(KeepAlive.Dispose);
        KeepAlive = null;
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Light-volume bake resource cleanup failed.", Failures);
    }

    LightVolumeBakeInventory InventoryValue => Inventory
        ?? throw new InvalidOperationException("The light-volume bake has no occurrence inventory.");

    LightVolumeBakePlan PlanValue => Plan
        ?? throw new InvalidOperationException("The light-volume bake has no allocation plan.");

    void Finish()
    {
        Owner.CompleteBake(TargetSlot.Occurrence);
        LightVolumeHarmonyAdapter.UnbindBake(this);
    }
}
