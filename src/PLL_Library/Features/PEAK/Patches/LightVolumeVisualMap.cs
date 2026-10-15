using System;
using System.Collections.Generic;
using Peak;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Library;

internal sealed class LightVolumeVisualMap
{
    const long MaxReadableTextureBytes = 64L * 1024 * 1024;
    const long MaxPageAdvanceDecodedBytes = 512L * 1024;
    const int MaxDecodedColorArrayBytes = 64 * 1024;
    const int MaxDecodedPixelsPerArray = MaxDecodedColorArrayBytes / (sizeof(float) * 4);
    internal const long RgbaHalfBytesPerPixel = 8;
    const int MaxPageTilePixels = (int)(MaxPageAdvanceDecodedBytes / RgbaHalfBytesPerPixel);
    const string GridOffsetGlobal = "_GridOffset";
    const string GridResolutionGlobal = "_GridRes";
    const string LightMapGlobal = "_LightMap";
    const string RaySpacingGlobal = "_RaySpacing";
    const string CanonicalFallback = " The smaller light map remains active.";

    LightVolumeVisualMap(
        CompressableLightMap CompressedMap,
        uint3 Grid,
        Vector3 Offset,
        Vector3 Center,
        float Spacing,
        Texture3D Texture,
        bool Compressed,
        bool Compact)
    {
        this.CompressedMap = CompressedMap;
        this.Grid = Grid;
        this.Offset = Offset;
        this.Center = Center;
        this.Spacing = Spacing;
        this.Texture = Texture;
        this.Compressed = Compressed;
        this.Compact = Compact;
        var Bytes = checked(
            (long)Texture.width * Texture.height * Texture.depth * RgbaHalfBytesPerPixel);
        if (Texture.isReadable) Bytes = checked(Bytes * 2);
        if (Compressed)
        {
            foreach (var Slice in CompressedMap.rgbMapSlices)
                Bytes = checked(Bytes + OwnedTextureBytes(Slice));
            foreach (var Slice in CompressedMap.alphaChannelSlices)
                Bytes = checked(Bytes + OwnedTextureBytes(Slice));
        }
        OwnedBytes = Bytes;
    }

    readonly CompressableLightMap CompressedMap;
    uint3 Grid { get; }
    Vector3 Offset { get; }
    Vector3 Center { get; }
    float Spacing { get; }
    internal Texture3D Texture { get; }
    internal long OwnedBytes { get; }
    bool Compressed { get; }
    bool Compact { get; }

    internal sealed class Page
    {
        RenderTexture? Texture;

        internal Page(RenderTexture Texture, uint3 Grid, Vector3 Offset, float Spacing)
        {
            this.Texture = Texture;
            this.Grid = Grid;
            this.Offset = Offset;
            this.Spacing = Spacing;
        }

        internal uint3 Grid { get; }
        internal Vector3 Offset { get; }
        internal float Spacing { get; }
        internal long OwnedBytes => checked(
            (long)Grid.x * Grid.y * Grid.z * RgbaHalfBytesPerPixel);

        internal void Publish(Vector3 Translation)
        {
            var Value = Texture;
            if (!Value) throw new InvalidOperationException("The current light-volume page was released.");
            Shader.SetGlobalTexture(LightMapGlobal, Value);
            Shader.SetGlobalVector(GridResolutionGlobal, new Vector4(Grid.x, Grid.y, Grid.z));
            Shader.SetGlobalFloat(RaySpacingGlobal, Spacing);
            Shader.SetGlobalVector(GridOffsetGlobal, Offset + Translation);
        }

        internal void Release()
        {
            ReleasePageTexture(Texture);
            Texture = null;
        }
    }

    internal enum PageAdvance
    {
        Advanced,
        Complete,
        UploadRejected
    }

    internal sealed class PageBuild : IDisposable
    {
        readonly Texture2D[] RgbSlices;
        readonly Texture2D[] AlphaSlices;
        readonly int3 Start;
        readonly int3 Grid;
        readonly int2 TileGrid;
        readonly int SlicesPerAdvance;
        readonly Vector3 Offset;
        readonly float Spacing;
        RenderTexture? Texture;
        Texture2D? TileTexture;
        Page? Result;
        int SliceIndex;

        PageBuild(
            int3 Start,
            int3 Grid,
            Vector3 Offset,
            float Spacing,
            Texture2D[] RgbSlices,
            Texture2D[] AlphaSlices,
            RenderTexture Texture,
            Texture2D TileTexture,
            int2 TileGrid)
        {
            this.Start = Start;
            this.Grid = Grid;
            this.Offset = Offset;
            this.Spacing = Spacing;
            this.RgbSlices = RgbSlices;
            this.AlphaSlices = AlphaSlices;
            this.Texture = Texture;
            this.TileTexture = TileTexture;
            this.TileGrid = TileGrid;
            var SliceBytes = checked((long)Grid.x * Grid.y * RgbaHalfBytesPerPixel);
            SlicesPerAdvance = Math.Max(1, (int)(MaxPageAdvanceDecodedBytes / SliceBytes));
        }

        internal static bool TryCreate(
            int3 Start,
            int3 Grid,
            Vector3 Offset,
            float Spacing,
            Texture2D[] RgbSlices,
            Texture2D[] AlphaSlices,
            out PageBuild? Result)
        {
            Result = null;
            RenderTexture? Texture = null;
            Texture2D? Tile = null;
            try
            {
                Texture = new RenderTexture(
                    Grid.x, Grid.y, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Texture.name = "LightVolumeOccurrencePage";
                Texture.dimension = TextureDimension.Tex3D;
                Texture.volumeDepth = Grid.z;
                Texture.useMipMap = false;
                Texture.autoGenerateMips = false;
                Texture.wrapMode = TextureWrapMode.Clamp;
                Texture.filterMode = FilterMode.Bilinear;
                if (!Texture.Create()) return false;
                var TileWidth = Math.Min(Grid.x, MaxPageTilePixels);
                var TileGrid = new int2(
                    TileWidth,
                    Math.Min(Grid.y, Math.Max(1, MaxPageTilePixels / TileWidth)));
                Tile = new Texture2D(
                    TileGrid.x, TileGrid.y,
                    TextureFormat.RGBAHalf, mipChain: false, linear: true, createUninitialized: true);
                Tile.name = "LightVolumeOccurrenceTile";
                Tile.wrapMode = TextureWrapMode.Clamp;
                Tile.filterMode = FilterMode.Bilinear;
                Result = new PageBuild(
                    Start, Grid, Offset, Spacing,
                    RgbSlices, AlphaSlices, Texture, Tile, TileGrid);
                Texture = null;
                Tile = null;
                return true;
            }
            catch (UnityException)
            {
                return false;
            }
            finally
            {
                List<Exception>? Failures = null;
                try { ReleasePageTexture(Texture); }
                catch (Exception Exception) { (Failures ??= new List<Exception>(2)).Add(Exception); }
                try { if (Tile) UnityEngine.Object.DestroyImmediate(Tile); }
                catch (Exception Exception) { (Failures ??= new List<Exception>(2)).Add(Exception); }
                CleanupOperations.Throw(Failures, "Light-volume page construction cleanup failed.");
            }
        }

        internal PageAdvance Advance()
        {
            if (Result is not null) return PageAdvance.Complete;
            var Value = Texture
                ?? throw new InvalidOperationException("The light-volume page build is not active.");
            if (SliceIndex < Grid.z)
            {
                var Tile = TileTexture
                    ?? throw new InvalidOperationException("The light-volume page tile was released.");
                var Pixels = Tile.GetPixelData<half4>(0);
                var SliceEnd = SliceIndex + Math.Min(Grid.z - SliceIndex, SlicesPerAdvance);
                try
                {
                    while (SliceIndex < SliceEnd)
                    {
                        for (var TileY = 0; TileY < Grid.y; TileY += TileGrid.y)
                        {
                            var Height = Math.Min(TileGrid.y, Grid.y - TileY);
                            for (var TileX = 0; TileX < Grid.x; TileX += TileGrid.x)
                            {
                                var Width = Math.Min(TileGrid.x, Grid.x - TileX);
                                WriteDecodedRegion(
                                    RgbSlices[Start.z + SliceIndex], AlphaSlices[Start.z + SliceIndex],
                                    new RectInt(Start.x + TileX, Start.y + TileY, Width, Height),
                                    Pixels, TileGrid.x);
                                Tile.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                                Graphics.CopyTexture(
                                    Tile, 0, 0, 0, 0, Width, Height,
                                    Value, SliceIndex, 0, TileX, TileY);
                            }
                        }
                        SliceIndex++;
                    }
                }
                catch (UnityException)
                {
                    return PageAdvance.UploadRejected;
                }
                if (SliceIndex < Grid.z) return PageAdvance.Advanced;
            }
            Result = new Page(
                Value, new uint3((uint)Grid.x, (uint)Grid.y, (uint)Grid.z), Offset, Spacing);
            Texture = null;
            return PageAdvance.Complete;
        }

        internal Page TakePage()
        {
            var Value = Result
                ?? throw new InvalidOperationException("The light-volume page build is incomplete.");
            Result = null;
            return Value;
        }

        public void Dispose()
        {
            var Tile = TileTexture;
            var PageTexture = Texture;
            var Completed = Result;
            TileTexture = null;
            Texture = null;
            Result = null;
            List<Exception>? Failures = null;
            try { if (Tile) UnityEngine.Object.DestroyImmediate(Tile); }
            catch (Exception Exception) { (Failures ??= new List<Exception>(3)).Add(Exception); }
            try { ReleasePageTexture(PageTexture); }
            catch (Exception Exception) { (Failures ??= new List<Exception>(3)).Add(Exception); }
            try { Completed?.Release(); }
            catch (Exception Exception) { (Failures ??= new List<Exception>(3)).Add(Exception); }
            CleanupOperations.Throw(Failures, "Light-volume page build cleanup failed.");
        }
    }

    internal static LightVolumeVisualMap Capture(global::LightVolume Volume, bool RequireSampling)
    {
        var CompressedMap = Volume.lightMapData;
        var PublishedGrid = Volume.gridRes;
        if (!TryDescribe(
                ref CompressedMap, out var Grid, out var OutputGrid,
                out var Compressed, out var Compact)
            || PublishedGrid.x != (uint)Grid.x || PublishedGrid.y != (uint)Grid.y
            || PublishedGrid.z != (uint)Grid.z || CompressedMap.gridOffset != Volume.gridOffset
            || CompressedMap.gridCenter != Volume.gridCenter
            || Volume.raySpacing <= 0f || float.IsNaN(Volume.raySpacing)
            || float.IsInfinity(Volume.raySpacing))
            throw new InvalidOperationException("PEAK's published light volume is not coherent.");
        var Texture = CompressedMap.Uncompressed;
        if (!ValidTexture(Texture, OutputGrid, Compact))
            throw new InvalidOperationException("PEAK's published light volume has an invalid visual texture.");
        if (RequireSampling && Shader.GetGlobalTexture(LightMapGlobal) != Texture)
            throw new InvalidOperationException("PEAK's canonical light texture is not globally published.");
        return new LightVolumeVisualMap(
            CompressedMap, PublishedGrid, Volume.gridOffset, Volume.gridCenter,
            Volume.raySpacing, Texture, Compressed, Compact);
    }

    internal bool TryBeginPageBuild(
        Bounds SourceBounds,
        string OccurrenceId,
        long PageByteLimit,
        long AllocationByteLimit,
        out PageBuild? Result,
        out string? Rejection)
    {
        Result = null;
        Rejection = null;
        if (!Compressed)
            throw new InvalidOperationException(
                "Only the compressed canonical light volume can build projection pages.");
        var Minimum = SourceBounds.min;
        var Maximum = SourceBounds.max;
        if (!UnityValueContract.Finite(Minimum) || !UnityValueContract.Finite(Maximum)
            || Minimum.x > Maximum.x || Minimum.y > Maximum.y || Minimum.z > Maximum.z)
            throw new InvalidOperationException($"Native occurrence '{OccurrenceId}' has invalid light-volume bounds.");
        var MinimumCoordinates = (Minimum - Offset) / Spacing + 0.5f * Vector3.one;
        var MaximumCoordinates = (Maximum - Offset) / Spacing + 0.5f * Vector3.one;
        var Start = new int3(
            Math.Clamp((int)Math.Floor(MinimumCoordinates.x) - 1, 0, (int)Grid.x),
            Math.Clamp((int)Math.Floor(MinimumCoordinates.y) - 1, 0, (int)Grid.y),
            Math.Clamp((int)Math.Floor(MinimumCoordinates.z) - 1, 0, (int)Grid.z));
        var End = new int3(
            Math.Clamp((int)Math.Floor(MaximumCoordinates.x) + 2, 0, (int)Grid.x),
            Math.Clamp((int)Math.Floor(MaximumCoordinates.y) + 2, 0, (int)Grid.y),
            Math.Clamp((int)Math.Floor(MaximumCoordinates.z) + 2, 0, (int)Grid.z));
        var PageGrid = End - Start;
        if (PageGrid.x <= 0 || PageGrid.y <= 0 || PageGrid.z <= 0)
            throw new InvalidOperationException($"Native occurrence '{OccurrenceId}' is outside the canonical light volume.");
        var PixelCount = checked((long)PageGrid.x * PageGrid.y * PageGrid.z);
        var Bytes = checked(PixelCount * RgbaHalfBytesPerPixel);
        if (Bytes > PageByteLimit)
        {
            Rejection = $"Native occurrence '{OccurrenceId}' requires a {Bytes}-byte light-volume page; "
                + $"the limit is {PageByteLimit} bytes. Reduce this occurrence's active render bounds"
                + " to enable exact paging." + CanonicalFallback;
            return false;
        }
        var TileBytes = checked(
            Math.Min((long)PageGrid.x * PageGrid.y, MaxPageTilePixels)
            * RgbaHalfBytesPerPixel);
        var AllocationBytes = checked(Bytes + TileBytes * 2);
        if (AllocationBytes > AllocationByteLimit)
        {
            Rejection = $"Native occurrence '{OccurrenceId}' requires {AllocationBytes} bytes of free"
                + " light-volume paging memory." + CanonicalFallback;
            return false;
        }
        const CopyTextureSupport RequiredCopySupport = CopyTextureSupport.Basic
            | CopyTextureSupport.Copy3D | CopyTextureSupport.DifferentTypes
            | CopyTextureSupport.TextureToRT;
        if (!SystemInfo.supports3DRenderTextures
            || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf)
            || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            || (SystemInfo.copyTextureSupport & RequiredCopySupport) != RequiredCopySupport)
        {
            Rejection = $"Native occurrence '{OccurrenceId}' cannot use RGBAHalf 3D render-texture paging"
                + " on this graphics device." + CanonicalFallback;
            return false;
        }
        var MaxTextureSize = SystemInfo.maxTexture3DSize;
        if (PageGrid.x > MaxTextureSize || PageGrid.y > MaxTextureSize || PageGrid.z > MaxTextureSize)
        {
            Rejection = $"Native occurrence '{OccurrenceId}' requires a"
                + $" {PageGrid.x}x{PageGrid.y}x{PageGrid.z} light-volume page; this graphics device"
                + $" supports at most {MaxTextureSize} pixels per 3D axis. Reduce this occurrence's"
                + " active render bounds to enable exact paging." + CanonicalFallback;
            return false;
        }

        var PageOffset = Offset + Spacing * new Vector3(Start.x, Start.y, Start.z);
        if (PageBuild.TryCreate(
                Start, PageGrid, PageOffset, Spacing,
                CompressedMap.rgbMapSlices, CompressedMap.alphaChannelSlices, out Result))
            return true;
        Rejection = $"Native occurrence '{OccurrenceId}' could not create its RGBAHalf"
            + " light-volume paging resources on this graphics device." + CanonicalFallback;
        return false;
    }

    internal static string PageUploadRejection(string OccurrenceId) =>
        $"Native occurrence '{OccurrenceId}' could not upload its RGBAHalf light-volume page"
        + " on this graphics device." + CanonicalFallback;

    internal bool MatchesLive(global::LightVolume Volume)
    {
        ref var Live = ref Volume.lightMapData;
        return Live.Uncompressed == Texture && Volume.gridRes.Equals(Grid)
            && Volume.gridOffset == Offset && Volume.gridCenter == Center
            && Volume.raySpacing == Spacing;
    }

    internal Color Sample(Vector3 WorldPosition)
    {
        var Coordinates = (WorldPosition - Offset) / Spacing + 0.5f * Vector3.one;
        var SourceGrid = CompressedMap.gridRes;
        var X = Math.Clamp((int)Coordinates.x, 0, SourceGrid.x - 1);
        var Y = Math.Clamp((int)Coordinates.y, 0, SourceGrid.y - 1);
        var Z = Math.Clamp((int)Coordinates.z, 0, SourceGrid.z - 1);
        if (!Compressed) return Texture.GetPixel(X, Y, Z);
        return CompressedPixel(
            CompressedMap.rgbMapSlices[Z].GetPixel(X, Y),
            CompressedMap.alphaChannelSlices[Z].GetPixel(X, Y));
    }

    internal void RestoreLive(global::LightVolume Volume)
    {
        Volume.lightMapData = CompressedMap;
        Volume.gridRes = Grid;
        Volume.gridOffset = Offset;
        Volume.gridCenter = Center;
        Volume.raySpacing = Spacing;
        PublishMapGlobals();
    }

    internal void ApplyVisual(global::LightVolume Volume)
    {
        Volume.lightMapData = CompressedMap;
        Volume.gridRes = Grid;
        Volume.gridOffset = Offset;
        Volume.gridCenter = Center;
        Volume.raySpacing = Spacing;
        Shader.SetGlobalTexture(LightMapGlobal, Texture);
        Volume.SetShaderVars();
    }

    internal void PublishMapGlobals(Vector3 Translation = default)
    {
        Shader.SetGlobalTexture(LightMapGlobal, Texture);
        var CompressedGrid = CompressedMap.gridRes;
        if (!Compact)
        {
            Shader.SetGlobalVector(
                GridResolutionGlobal,
                new Vector4(CompressedGrid.x, CompressedGrid.y, CompressedGrid.z));
            Shader.SetGlobalFloat(RaySpacingGlobal, Spacing);
            Shader.SetGlobalVector(GridOffsetGlobal, Offset + Translation);
            return;
        }
        PublishCompactShaderGeometry(CompressedGrid, Spacing, Offset, Translation);
    }

    internal static void ValidateNewMap(global::LightVolume Volume)
    {
        ref var StagedMap = ref Volume.lightMapData;
        if (!TryDescribe(
                ref StagedMap, out var Grid, out var OutputGrid,
                out var Compressed, out var Compact)
            || Compressed || Compact
            || Volume.gridRes.x != (uint)Grid.x || Volume.gridRes.y != (uint)Grid.y
            || Volume.gridRes.z != (uint)Grid.z || Volume.gridOffset != StagedMap.gridOffset
            || Volume.gridCenter != StagedMap.gridCenter)
            throw new InvalidOperationException("PEAK produced an incoherent staged light-volume map.");
        if (!ValidTexture(StagedMap.Uncompressed, OutputGrid, Compact))
            throw new InvalidOperationException("PEAK produced an invalid staged light-volume texture.");
    }


    internal static bool TrySampleCompressed(
        global::LightVolume Volume,
        Vector3 WorldPosition,
        out Color Sample)
    {
        if (!IsCompact(Volume, out var Grid))
        {
            Sample = default;
            return false;
        }
        var Coordinates = Volume.WorldPosToLightmapCoords(WorldPosition);
        var X = Math.Clamp((int)Coordinates.x, 0, Grid.x - 1);
        var Y = Math.Clamp((int)Coordinates.y, 0, Grid.y - 1);
        var Z = Math.Clamp((int)Coordinates.z, 0, Grid.z - 1);
        ref var CompressedMap = ref Volume.lightMapData;
        Sample = CompressedPixel(
            CompressedMap.rgbMapSlices[Z].GetPixel(X, Y),
            CompressedMap.alphaChannelSlices[Z].GetPixel(X, Y));
        return true;
    }

    internal static void RepublishCompactGlobals(global::LightVolume Volume)
    {
        if (!IsCompact(Volume, out var Grid)) return;
        PublishCompactShaderGeometry(Grid, Volume.raySpacing, Volume.gridOffset, Vector3.zero);
    }

    internal static bool TryDecompress(ref CompressableLightMap CompressedMap)
    {
        if (!TryGetSupported(
                ref CompressedMap, out var Grid, out var RgbSlices,
                out var AlphaSlices, out var PixelCount)) return false;
        if (!TryGetOutputGrid(Grid, PixelCount, out var OutputGrid, out var Compact))
            throw new InvalidOperationException(
                $"PEAK's {Grid.x}x{Grid.y}x{Grid.z} light-volume dimensions cannot produce an"
                + $" RGBAHalf visual texture within {MaxReadableTextureBytes} bytes using the supported"
                + " exact full or 2x compact representation.");

        var Texture = new Texture3D(
            OutputGrid.x, OutputGrid.y, OutputGrid.z,
            TextureFormat.RGBAHalf, mipChain: false, createUninitialized: true);
        try
        {
            Texture.name = "LightVolumeBakeTexture";
            Texture.wrapMode = TextureWrapMode.Clamp;
            Texture.filterMode = FilterMode.Bilinear;
            if (Compact) WriteCompact(Texture, Grid, RgbSlices, AlphaSlices);
            else WriteFull(Texture, Grid, RgbSlices, AlphaSlices);
            Texture.Apply(updateMipmaps: false, makeNoLongerReadable: Compact);
            CompressedMap.DestroyLastLightMap();
            CompressedMap.Uncompressed = Texture;
            Texture = null!;
        }
        finally
        {
            if (Texture) UnityEngine.Object.DestroyImmediate(Texture);
        }
        return true;
    }

    internal static void Destroy(
        LightVolumeVisualMap Disposed,
        params LightVolumeVisualMap[] Preserved) =>
        DestroyResources(Disposed.CompressedMap, Array.Empty<UnityEngine.Object>(), Preserved);

    internal static void DestroyResources(
        CompressableLightMap Disposed,
        IReadOnlyList<UnityEngine.Object> Additional,
        params LightVolumeVisualMap[] Preserved)
    {
        var Keep = new HashSet<int>();
        var Identified = new HashSet<int>();
        var Owned = new List<UnityEngine.Object>();
        foreach (var PreservedMap in Preserved)
        {
            Preserve(PreservedMap.Texture);
            PreserveAll(PreservedMap.CompressedMap.rgbMapSlices);
            PreserveAll(PreservedMap.CompressedMap.alphaChannelSlices);
        }
        Identify(Disposed.Uncompressed);
        IdentifyAll(Disposed.rgbMapSlices);
        IdentifyAll(Disposed.alphaChannelSlices);
        foreach (var Resource in Additional) Identify(Resource);

        List<Exception>? Failures = null;
        foreach (var Resource in Owned)
            try { UnityEngine.Object.DestroyImmediate(Resource); }
            catch (Exception Exception) { (Failures ??= new List<Exception>()).Add(Exception); }
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Light-volume resource cleanup failed.", Failures);

        void Preserve(UnityEngine.Object? Resource)
        {
            if (Resource) Keep.Add(Resource.GetInstanceID());
        }

        void PreserveAll(Texture2D[]? Resources)
        {
            if (Resources is null) return;
            foreach (var Resource in Resources) Preserve(Resource);
        }

        void Identify(UnityEngine.Object? Resource)
        {
            if (!Resource) return;
            var Identity = Resource.GetInstanceID();
            if (!Keep.Contains(Identity) && Identified.Add(Identity)) Owned.Add(Resource);
        }

        void IdentifyAll(Texture2D[]? Resources)
        {
            if (Resources is null) return;
            foreach (var Resource in Resources) Identify(Resource);
        }
    }

    static bool TryDescribe(
        ref CompressableLightMap Map,
        out int3 Grid,
        out int3 OutputGrid,
        out bool Compressed,
        out bool Compact)
    {
        if (TryGetSupported(ref Map, out Grid, out _, out _, out var PixelCount))
        {
            Compressed = true;
            return TryGetOutputGrid(Grid, PixelCount, out OutputGrid, out Compact);
        }
        Grid = Map.gridRes;
        OutputGrid = Grid;
        Compressed = false;
        Compact = false;
        return Grid.x > 0 && Grid.y > 0 && Grid.z > 0 && !Map.HasCompressedData
            && Map.rgbMapSlices is { Length: 0 }
            && Map.alphaChannelSlices is { Length: 0 };
    }

    static bool ValidTexture(Texture3D Texture, int3 Grid, bool Compact) =>
        Texture && Texture.format == TextureFormat.RGBAHalf
        && Texture.isReadable != Compact
        && Texture.width == Grid.x && Texture.height == Grid.y && Texture.depth == Grid.z;

    static long OwnedTextureBytes(Texture2D Texture)
    {
        var Bytes = TextureStorageBytes(Texture);
        return Texture.isReadable ? checked(Bytes * 2) : Bytes;
    }

    static long TextureStorageBytes(Texture2D Texture) => Texture.format switch
    {
        TextureFormat.BC6H => checked(
            (long)((Texture.width + 3) / 4) * ((Texture.height + 3) / 4) * 16),
        TextureFormat.BC4 => checked(
            (long)((Texture.width + 3) / 4) * ((Texture.height + 3) / 4) * 8),
        TextureFormat.RGBAHalf => checked(
            (long)Texture.width * Texture.height * RgbaHalfBytesPerPixel),
        TextureFormat.R8 => checked((long)Texture.width * Texture.height),
        _ => throw new InvalidOperationException(
            $"Light-volume texture '{Texture.name}' has unsupported format {Texture.format}.")
    };
    static bool TryGetSupported(
        ref CompressableLightMap CompressedMap,
        out int3 Grid,
        out Texture2D[] RgbSlices,
        out Texture2D[] AlphaSlices,
        out long PixelCount)
    {
        Grid = CompressedMap.gridRes;
        RgbSlices = CompressedMap.rgbMapSlices;
        AlphaSlices = CompressedMap.alphaChannelSlices;
        PixelCount = 0;
        if (Grid.x <= 0 || Grid.y <= 0 || Grid.z <= 0
            || RgbSlices is null || AlphaSlices is null
            || RgbSlices.Length != Grid.z || AlphaSlices.Length != Grid.z) return false;
        var SliceLength = (long)Grid.x * Grid.y;
        if (SliceLength > int.MaxValue) return false;
        PixelCount = SliceLength * Grid.z;
        var FirstRgb = RgbSlices[0];
        var FirstAlpha = AlphaSlices[0];
        if (!FirstRgb || !FirstAlpha) return false;
        var RgbFormat = FirstRgb.format;
        var AlphaFormat = FirstAlpha.format;
        if ((RgbFormat != TextureFormat.BC6H
                && (RgbFormat != TextureFormat.RGBAHalf || CompressedMap.hdrDecompressScalar != 1f))
            || AlphaFormat != TextureFormat.BC4 && AlphaFormat != TextureFormat.R8) return false;
        for (var SliceIndex = 0; SliceIndex < Grid.z; SliceIndex++)
        {
            var Rgb = RgbSlices[SliceIndex];
            var Alpha = AlphaSlices[SliceIndex];
            if (!Rgb || !Alpha || !Rgb.isReadable || !Alpha.isReadable
                || Rgb.width != Grid.x || Rgb.height != Grid.y
                || Alpha.width != Grid.x || Alpha.height != Grid.y
                || Rgb.format != RgbFormat || Alpha.format != AlphaFormat) return false;
        }
        return true;
    }

    static bool TryGetOutputGrid(
        int3 Grid,
        long PixelCount,
        out int3 OutputGrid,
        out bool Compact)
    {
        Compact = PixelCount > MaxReadableTextureBytes / RgbaHalfBytesPerPixel;
        if (!Compact)
        {
            OutputGrid = Grid;
            return true;
        }
        if ((Grid.x & 1) != 0 || (Grid.y & 1) != 0 || (Grid.z & 1) != 0)
        {
            OutputGrid = default;
            return false;
        }
        OutputGrid = new int3(Grid.x / 2, Grid.y / 2, Grid.z / 2);
        return (long)OutputGrid.x * OutputGrid.y * OutputGrid.z
            <= MaxReadableTextureBytes / RgbaHalfBytesPerPixel;
    }

    static bool IsCompact(
        ref CompressableLightMap CompressedMap,
        uint3 PublishedGrid,
        Vector3 PublishedOffset,
        out int3 Grid)
    {
        Grid = CompressedMap.gridRes;
        var SliceLength = (long)Grid.x * Grid.y;
        if (Grid.x <= 0 || Grid.y <= 0 || Grid.z <= 0 || SliceLength > int.MaxValue
            || !TryGetOutputGrid(Grid, SliceLength * Grid.z, out var OutputGrid, out var Compact)
            || !Compact || PublishedGrid.x != (uint)Grid.x || PublishedGrid.y != (uint)Grid.y
            || PublishedGrid.z != (uint)Grid.z || PublishedOffset != CompressedMap.gridOffset) return false;
        var Texture = CompressedMap.Uncompressed;
        return Texture && !Texture.isReadable && Texture.format == TextureFormat.RGBAHalf
            && Texture.width == OutputGrid.x && Texture.height == OutputGrid.y
            && Texture.depth == OutputGrid.z;
    }

    static bool IsCompact(global::LightVolume Volume, out int3 Grid)
    {
        ref var CompressedMap = ref Volume.lightMapData;
        return IsCompact(ref CompressedMap, Volume.gridRes, Volume.gridOffset, out Grid);
    }

    static void PublishCompactShaderGeometry(
        int3 Grid,
        float Spacing,
        Vector3 Offset,
        Vector3 Translation)
    {
        Shader.SetGlobalVector(
            GridResolutionGlobal,
            new Vector4(Grid.x / 2f, Grid.y / 2f, Grid.z / 2f));
        Shader.SetGlobalFloat(RaySpacingGlobal, Spacing * 2f);
        Shader.SetGlobalVector(
            GridOffsetGlobal,
            Offset + Spacing * Vector3.one + Translation);
    }

    static void ReleasePageTexture(RenderTexture? Texture)
    {
        if (!Texture) return;
        List<Exception>? Failures = null;
        try { if (Texture.IsCreated()) Texture.Release(); }
        catch (Exception Exception) { (Failures ??= new List<Exception>(2)).Add(Exception); }
        try { UnityEngine.Object.DestroyImmediate(Texture); }
        catch (Exception Exception) { (Failures ??= new List<Exception>(2)).Add(Exception); }
        CleanupOperations.Throw(Failures, "Light-volume page texture cleanup failed.");
    }

    static half4 Pixel(Color Rgb, Color Alpha) => new(
        (half)Rgb.r, (half)Rgb.g, (half)Rgb.b, (half)Alpha.r);

    static Color CompressedPixel(Color Rgb, Color Alpha) => new(
        (float)(half)Rgb.r, (float)(half)Rgb.g,
        (float)(half)Rgb.b, (float)(half)Alpha.r);

    static void AddHalfPixel(ref float4 Sum, Color Rgb, Color Alpha)
    {
        Sum.x += (float)(half)Rgb.r;
        Sum.y += (float)(half)Rgb.g;
        Sum.z += (float)(half)Rgb.b;
        Sum.w += (float)(half)Alpha.r;
    }

    static void WriteDecodedRegion(
        Texture2D RgbTexture,
        Texture2D AlphaTexture,
        RectInt Source,
        Unity.Collections.NativeArray<half4> Output,
        int OutputRowLength)
    {
        var TileWidth = Math.Min(Source.width, MaxDecodedPixelsPerArray);
        var TileHeight = Math.Max(1, MaxDecodedPixelsPerArray / TileWidth);
        for (var TileY = 0; TileY < Source.height; TileY += TileHeight)
        {
            var Height = Math.Min(TileHeight, Source.height - TileY);
            for (var TileX = 0; TileX < Source.width; TileX += TileWidth)
            {
                var Width = Math.Min(TileWidth, Source.width - TileX);
                var Rgb = RgbTexture.GetPixels(Source.x + TileX, Source.y + TileY, Width, Height);
                var Alpha = AlphaTexture.GetPixels(Source.x + TileX, Source.y + TileY, Width, Height);
                for (var Row = 0; Row < Height; Row++)
                {
                    var SourceRow = Row * Width;
                    var OutputRow = (TileY + Row) * OutputRowLength + TileX;
                    for (var X = 0; X < Width; X++)
                        Output[OutputRow + X] = Pixel(Rgb[SourceRow + X], Alpha[SourceRow + X]);
                }
            }
        }
    }

    static void WriteFull(
        Texture3D Texture,
        int3 Grid,
        Texture2D[] RgbSlices,
        Texture2D[] AlphaSlices)
    {
        var SliceLength = Grid.x * Grid.y;
        var Source = new RectInt(0, 0, Grid.x, Grid.y);
        var Pixels = Texture.GetPixelData<half4>(0);
        for (var SliceIndex = 0; SliceIndex < Grid.z; SliceIndex++)
        {
            var Output = Pixels.GetSubArray(SliceIndex * SliceLength, SliceLength);
            WriteDecodedRegion(
                RgbSlices[SliceIndex], AlphaSlices[SliceIndex], Source, Output, Source.width);
        }
    }

    static void WriteCompact(
        Texture3D Texture,
        int3 Grid,
        Texture2D[] RgbSlices,
        Texture2D[] AlphaSlices)
    {
        var OutputWidth = Grid.x / 2;
        var OutputHeight = Grid.y / 2;
        var OutputSliceLength = OutputWidth * OutputHeight;
        var MaxOutputTilePixels = MaxDecodedPixelsPerArray / 4;
        var TileWidth = Math.Min(OutputWidth, MaxOutputTilePixels);
        var TileHeight = Math.Max(1, MaxOutputTilePixels / TileWidth);
        var Pixels = Texture.GetPixelData<half4>(0);
        for (var OutputZ = 0; OutputZ < Grid.z / 2; OutputZ++)
        {
            var SourceZ = OutputZ * 2;
            var Output = Pixels.GetSubArray(OutputZ * OutputSliceLength, OutputSliceLength);
            for (var TileY = 0; TileY < OutputHeight; TileY += TileHeight)
            {
                var Height = Math.Min(TileHeight, OutputHeight - TileY);
                for (var TileX = 0; TileX < OutputWidth; TileX += TileWidth)
                {
                    var Width = Math.Min(TileWidth, OutputWidth - TileX);
                    var SourceWidth = Width * 2;
                    var SourceHeight = Height * 2;
                    var SourceX = TileX * 2;
                    var SourceY = TileY * 2;
                    var Rgb0 = RgbSlices[SourceZ].GetPixels(
                        SourceX, SourceY, SourceWidth, SourceHeight);
                    var Alpha0 = AlphaSlices[SourceZ].GetPixels(
                        SourceX, SourceY, SourceWidth, SourceHeight);
                    var Rgb1 = RgbSlices[SourceZ + 1].GetPixels(
                        SourceX, SourceY, SourceWidth, SourceHeight);
                    var Alpha1 = AlphaSlices[SourceZ + 1].GetPixels(
                        SourceX, SourceY, SourceWidth, SourceHeight);
                    for (var OutputY = 0; OutputY < Height; OutputY++)
                    {
                        var SourceRow0 = OutputY * 2 * SourceWidth;
                        var SourceRow1 = SourceRow0 + SourceWidth;
                        var OutputRow = (TileY + OutputY) * OutputWidth + TileX;
                        for (var OutputX = 0; OutputX < Width; OutputX++)
                        {
                            var X = OutputX * 2;
                            var I00 = SourceRow0 + X;
                            var I01 = I00 + 1;
                            var I10 = SourceRow1 + X;
                            var I11 = I10 + 1;
                            var Sum = default(float4);
                            AddHalfPixel(ref Sum, Rgb0[I00], Alpha0[I00]);
                            AddHalfPixel(ref Sum, Rgb0[I01], Alpha0[I01]);
                            AddHalfPixel(ref Sum, Rgb0[I10], Alpha0[I10]);
                            AddHalfPixel(ref Sum, Rgb0[I11], Alpha0[I11]);
                            AddHalfPixel(ref Sum, Rgb1[I00], Alpha1[I00]);
                            AddHalfPixel(ref Sum, Rgb1[I01], Alpha1[I01]);
                            AddHalfPixel(ref Sum, Rgb1[I10], Alpha1[I10]);
                            AddHalfPixel(ref Sum, Rgb1[I11], Alpha1[I11]);
                            Output[OutputRow + OutputX] = new half4(
                                (half)(Sum.x * 0.125f), (half)(Sum.y * 0.125f),
                                (half)(Sum.z * 0.125f), (half)(Sum.w * 0.125f));
                        }
                    }
                }
            }
        }
    }
}
