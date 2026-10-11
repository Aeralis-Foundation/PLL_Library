using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace PLL.Library;

internal static class NativeContentTools
{
    internal const string ThumbnailAbsence = "Unsupported: the installed renderer features and render callbacks have no proven isolated thumbnail contract.";
    const string SupportedPeakSha256 = "B9A363213352EA58496F2F8598FA267C565BAF876F8C524EB56266764F8C199B";
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    static Work? Current;
    static string ToolDirectory => Path.Combine(Paths.PluginPath, NativeContentArtifact.DirectoryName);

    internal sealed class StartupTicket { }

    sealed class Claim : IDisposable
    {
        internal readonly NativeContentRequest Request;
        internal readonly string Directory;
        internal readonly string SettingsSha256;
        internal readonly int Ascent;
        internal readonly StartupTicket Ticket = new();
        readonly NativeContentRequestFile RequestFile;
        readonly List<string> StagedFiles = new();

        internal Claim(NativeContentRequest Request, NativeContentRequestFile RequestFile,
            string SettingsSha256, int Ascent)
        {
            this.Request = Request;
            this.RequestFile = RequestFile; this.SettingsSha256 = SettingsSha256;
            this.Ascent = Ascent; Directory = Path.Combine(ToolDirectory, Request.Id + ".pending");
        }

        internal string? ReadCancellationReason() => DateTime.UtcNow.Ticks >= Request.ExpiresUtcTicks
            ? "The native content request expired."
            : File.Exists(Path.Combine(ToolDirectory, Request.Id + ".cancel"))
                ? "The native content request was cancelled." : null;

        internal void ArchiveRequest()
        {
            var FilePath = Path.Combine(Directory, NativeContentArtifact.RequestFile);
            using var Archived = File.Open(FilePath,
                FileMode.CreateNew, FileAccess.Write, FileShare.None);
            StagedFiles.Add(FilePath);
            RequestFile.CopyTo(Archived); Archived.Flush(true);
        }

        internal void DiscardUnpublished()
        {
            foreach (var FilePath in StagedFiles) File.Delete(FilePath);
            StagedFiles.Clear();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, false);
        }

        internal void PublishJson(string FileName, object Value, int MaximumBytes)
        {
            var Json = NativeContentArtifact.Serialize(Value);
            if (StrictUtf8.GetByteCount(Json) > MaximumBytes)
                throw new InvalidOperationException("The native content artifact exceeds its metadata size limit.");
            PublishBytes(FileName, StrictUtf8.GetBytes(Json));
        }

        internal bool HasStagedFile(string FileName) => StagedFiles.Contains(Path.Combine(Directory, FileName));

        internal void PublishBytes(string FileName, ReadOnlySpan<byte> Bytes)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var FilePath = Path.Combine(Directory, FileName);
            using var Stream = File.Open(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            StagedFiles.Add(FilePath);
            Stream.Write(Bytes); Stream.Flush(true);
        }

        internal void DeleteQueuedInput() => RequestFile.DeleteAfterPublication();
        public void Dispose() => RequestFile.Dispose();
    }

    abstract class Work
    {
        protected Work(Claim Claim) => this.Claim = Claim;
        internal Claim Claim { get; }
    }

    sealed class Startup : Work
    {
        internal Startup(Claim Claim) : base(Claim) { }
    }

    sealed class Export : Work
    {
        internal Export(Claim Claim, ResolvedRunConfiguration Configuration) : base(Claim)
        {
            this.Configuration = Configuration;
        }
        internal readonly ResolvedRunConfiguration Configuration;
        internal readonly List<NativeContentEntry> Entries = new();
        internal readonly List<NativeContentRig> Rigs = new();
        internal readonly Queue<IconCapture> Icons = new();
        internal readonly Dictionary<Texture2D, IconCapture> IconSources = new();
    }

    sealed class IconCapture
    {
        internal IconCapture(NativeContentEntry Entry, Texture2D Texture)
        {
            Entries.Add(Entry); this.Texture = Texture;
        }
        internal readonly List<NativeContentEntry> Entries = new();
        internal readonly Texture2D Texture;

        internal void SetImageAbsence(string Reason)
        {
            foreach (var Entry in Entries) Entry.ImageAbsence = Reason;
        }
    }

    sealed class IconPending : Work
    {
        internal IconPending(Export Export, MapRun Run) : base(Export.Claim)
        {
            this.Export = Export; this.Run = Run;
        }
        internal readonly Export Export;
        internal readonly MapRun Run;
    }

    sealed class IconReadback : Work
    {
        internal IconReadback(IconPending Batch, IconCapture Icon) : base(Batch.Claim)
        {
            this.Batch = Batch; this.Icon = Icon;
        }
        internal readonly IconPending Batch;
        internal readonly IconCapture Icon;
        internal RenderTexture? Target;
        internal string? CancellationReason;

        internal void ReleaseTarget()
        {
            var Texture = Target; Target = null;
            if (!Texture) return;
            Texture.Release(); UnityEngine.Object.Destroy(Texture);
        }
    }

    sealed class Preview : Work
    {
        internal Preview(Claim Claim, ResolvedRunConfiguration Configuration) : base(Claim)
        {
            this.Configuration = Configuration;
        }
        internal readonly ResolvedRunConfiguration Configuration;
    }

    sealed class AwaitingRender : Work
    {
        internal AwaitingRender(Preview Preview, MapRun Run, MapSlot Slot, Camera Camera) : base(Preview.Claim)
        {
            this.Preview = Preview; this.Run = Run; this.Slot = Slot; this.Camera = Camera;
        }
        internal readonly Preview Preview;
        internal readonly MapRun Run;
        internal readonly MapSlot Slot;
        internal readonly Camera Camera;
    }

    sealed class Rendering : Work
    {
        internal Rendering(AwaitingRender Source) : base(Source.Claim)
        {
            this.Source = Source; Frame = Time.frameCount;
            CameraWorldPose = Matrix(Source.Camera.transform.localToWorldMatrix);
            CameraProjection = Matrix(Source.Camera.projectionMatrix);
        }
        internal readonly AwaitingRender Source;
        internal readonly int Frame;
        internal readonly float[] CameraWorldPose;
        internal readonly float[] CameraProjection;
        internal string? CancellationReason;
    }

    internal static bool ExportRequested => Current is Export;

    internal static StartupTicket? CaptureStartupTicket() => Current?.Claim.Ticket;
    internal static bool Owns(StartupTicket Ticket) => ReferenceEquals(Ticket, Current?.Claim.Ticket);

    internal static ResolvedRunConfiguration? BeginStartup(
        int Ascent, byte[] SerializedRunSettings, out StartupTicket? Ticket)
    {
        Ticket = null;
        var RequestPath = Path.Combine(ToolDirectory, NativeContentArtifact.RequestFile);
        if (!File.Exists(RequestPath)) return null;
        if (Current is not null)
            throw new InvalidOperationException("A native content request already owns this startup.");
        NativeContentRequestFile Input;
        try { Input = NativeContentRequestFile.Open(RequestPath, false); }
        catch (IOException Failure) when ((Failure.HResult & 0xffff) is 2 or 3) { return null; }
        Claim? Claimed = null;
        try
        {
            var Request = NativeContentArtifact.Parse<NativeContentRequest>(Input.ReadText());
            NativeContentArtifact.RequireRequest(Request);
            if (System.IO.Directory.Exists(Path.Combine(ToolDirectory, Request.Id)))
                throw new InvalidOperationException("The native content request already completed. Clear its queue entry.");
            if (System.IO.Directory.Exists(Path.Combine(ToolDirectory, Request.Id + ".pending")))
                throw new InvalidOperationException("The native content publication was interrupted. Cancel its queue entry.");
            RequireInstalledIdentity(Request.Identity);
            if (Peak.Quicksave.ShouldUseSaveData)
                throw new InvalidOperationException("Native content requests require a fresh normal run, not Continue.");
            var SettingsSha256 = Hash(SerializedRunSettings);
            Claimed = new Claim(Request, Input, SettingsSha256, Ascent);
            Current = new Startup(Claimed);
            Ticket = Claimed.Ticket;
            if (Claimed.ReadCancellationReason() is { } CancellationReason)
                throw new OperationCanceledException(CancellationReason);
            if (Request.Kind == NativeContentRequestKind.Export) return null;
            if (Request.Ascent != Ascent || Request.NativeRunSettingsSha256 != SettingsSha256)
                throw new InvalidOperationException("The native run settings or ascent differ from the requested preview.");
            if (!RunConfigurationCodec.TryParse(Request.Configuration, out var Configuration, out var Failure))
                throw new InvalidOperationException(Failure);
            var Layout = Configuration.Layout;
            return new ResolvedRunConfiguration(
                new RunProtocolIdentity(Configuration.Protocol.ProtocolRevision,
                    Configuration.Protocol.SegmentManifestRevision,
                    Configuration.Protocol.LayoutManifestRevision, Guid.NewGuid().ToString("N")),
                new RunLayoutConfiguration(MapQuicksave.SeedFromText(Request.SeedText),
                    Layout.ReplaceGenerated, Layout.SelectedLayoutId, Layout.IsLibraryBase,
                    Layout.Order, Layout.OrderEnabled, Layout.Generative, Layout.Infinite,
                    Layout.Repeatable, Layout.Chances, Layout.Scale, Layout.BeginIndex, Layout.RemoveOld),
                Configuration.Terminal, Configuration.Environment,
                Configuration.LazyGeneration, Configuration.Biomes);
        }
        catch (Exception Failure)
        {
            if (Claimed is null) Input.Dispose();
            else Finish(Claimed, new NativeContentResult
            {
                Schema = NativeContentArtifact.ResultSchema,
                RequestId = Claimed.Request.Id, Identity = Claimed.Request.Identity,
                Outcome = Failure is OperationCanceledException ? "Cancelled" : "Rejected",
                Failure = Failure.Message
            }, Failure);
            throw;
        }
    }

    internal static void ConfigurationCaptured(StartupTicket? Ticket, ResolvedRunConfiguration Configuration)
    {
        if (Current is not Startup Startup || !ReferenceEquals(Ticket, Startup.Claim.Ticket)) return;
        if (!MapQuicksave.RuntimeMapProcessingRequired(Configuration))
        {
            var Failure = new InvalidOperationException("Native content export and preview require a PLL-processed layout. Select one before starting the run.");
            FailTool(Failure); throw Failure;
        }
        Current = Startup.Claim.Request.Kind == NativeContentRequestKind.Export
            ? new Export(Startup.Claim, Configuration) : new Preview(Startup.Claim, Configuration);
    }

    internal static void CaptureForceContent()
    {
        if (Current is Export Export) NativeSpawnCatalog.ProjectContent(Export.Entries);
    }

    internal static void CaptureItemIcon(NativeContentEntry Entry, Texture2D? Icon)
    {
        var Export = (Export)Current!;
        if (Export.Claim.ReadCancellationReason() is { } CancellationReason)
            throw new OperationCanceledException(CancellationReason);
        if (!Icon) { Entry.ImageAbsence = "PEAK provides no primary UI icon for this item."; return; }
        if (Export.IconSources.TryGetValue(Icon, out var Existing)) Existing.Entries.Add(Entry);
        else
        {
            var Capture = new IconCapture(Entry, Icon);
            Export.IconSources.Add(Icon, Capture); Export.Icons.Enqueue(Capture);
        }
    }

    static void SubmitIcon(IconPending Batch)
    {
        if (Batch.Export.Icons.Count == 0) { CompleteExport(Batch.Export, Batch.Run); return; }
        var Icon = Batch.Export.Icons.Dequeue(); var Texture = Icon.Texture;
        if (!Texture) throw new InvalidOperationException("The borrowed native item UI texture was destroyed before export.");
        if (Texture.width > 512 || Texture.height > 512)
        {
            Icon.SetImageAbsence("The native UI icon exceeds the supported 512 by 512 pixel image bound.");
            return;
        }
        var SourceFormat = Texture.graphicsFormat;
        var Format = GraphicsFormatUtility.IsSRGBFormat(SourceFormat)
            ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
        if (!SystemInfo.supportsAsyncGPUReadback
            || !SystemInfo.IsFormatSupported(Format, GraphicsFormatUsage.ReadPixels))
        {
            Icon.SetImageAbsence("This graphics device cannot read the native UI icon texture.");
            return;
        }
        var RequiresConversion = !SystemInfo.IsFormatSupported(SourceFormat, GraphicsFormatUsage.ReadPixels);
        if (RequiresConversion && !SystemInfo.IsFormatSupported(Format, GraphicsFormatUsage.Render))
        {
            Icon.SetImageAbsence("This graphics device cannot convert the native UI icon texture.");
            return;
        }
        var Copy = new IconReadback(Batch, Icon);
        Current = Copy;
        try
        {
            if (RequiresConversion)
            {
                Copy.Target = new RenderTexture(Texture.width, Texture.height, 0, Format);
                if (!Copy.Target.Create())
                    throw new InvalidOperationException("Native UI icon render target allocation failed.");
                using var Commands = new CommandBuffer();
                Commands.Blit(Texture, Copy.Target);
                var PreviousSrgbWrite = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = Copy.Target.sRGB;
                    Graphics.ExecuteCommandBuffer(Commands);
                }
                finally { GL.sRGBWrite = PreviousSrgbWrite; }
            }
            if (Copy.CancellationReason is { } Reason) { Current = Batch; Cancel(Reason); return; }
            AsyncGPUReadback.Request(Copy.Target ? Copy.Target : Texture, 0, Format,
                Result => IconCompleted(Copy, Result, SourceFormat, Format));
        }
        catch
        {
            if (ReferenceEquals(Current, Copy)) Current = Batch;
            throw;
        }
        finally
        {
            if (!ReferenceEquals(Current, Copy)) Copy.ReleaseTarget();
        }
    }

    static void IconCompleted(IconReadback Copy, AsyncGPUReadbackRequest Readback,
        GraphicsFormat SourceFormat, GraphicsFormat Format)
    {
        if (!ReferenceEquals(Current, Copy))
            throw new InvalidOperationException("A native icon readback lost its owned completion callback.");
        try
        {
            var CancellationReason = Copy.CancellationReason ?? Copy.Claim.ReadCancellationReason();
            if (CancellationReason is not null)
            {
                Current = Copy.Batch; Cancel(CancellationReason); return;
            }
            if (Readback.hasError) { Copy.Icon.SetImageAbsence("Native UI icon GPU readback failed."); return; }
            using var Png = ImageConversion.EncodeNativeArrayToPNG(Readback.GetData<byte>(), Format,
                (uint)Readback.width, (uint)Readback.height);
            if (Png.Length == 0) throw new InvalidOperationException("The native icon encoder returned an empty PNG.");
            var ImageSha256 = Hash(Png.AsReadOnlySpan()); var ImageFile = ImageSha256 + ".png";
            if (!Copy.Claim.HasStagedFile(ImageFile)) Copy.Claim.PublishBytes(ImageFile, Png.AsReadOnlySpan());
            foreach (var Entry in Copy.Icon.Entries)
            {
                Entry.ImageSha256 = ImageSha256; Entry.ImageFile = ImageFile;
                Entry.PoseProvenance = "Native primary Item.UIData.icon; authored UI pixels, not a live mesh or rig pose.";
                Entry.LightingProvenance = $"Authored UI icon colors; source {SourceFormat}, readback {Format}, not scene lighting.";
                Entry.Fingerprint = Fingerprint(Writer =>
                {
                    Writer.Write("PLL/NativeItemIcon/v1"); Writer.Write(Entry.ItemId); Writer.Write(Entry.Name);
                    Writer.Write(Entry.ComponentType); Writer.Write(Entry.ComponentAssembly); Writer.Write(ImageSha256);
                });
                Entry.ImageAbsence = "";
            }
        }
        catch (Exception Failure)
        {
            Current = Copy.Batch; FailTool(Failure);
        }
        finally
        {
            Copy.ReleaseTarget();
            if (ReferenceEquals(Current, Copy))
            {
                Current = Copy.Batch;
                if (Copy.CancellationReason is { } Reason) Cancel(Reason);
            }
        }
    }

    internal static void AcceptLiveCatalog()
    {
        if (Current is null) return;
        try
        {
            BiomeRegistration.RequireToolBundle(Current.Claim.Request.Identity.BundleFile,
                Current.Claim.Request.Identity.BundleSha256);
        }
        catch (Exception Failure) { FailTool(Failure); throw; }
    }

    internal static void CaptureRig(string Actor, NativeEntitySkinDescriptor Descriptor)
    {
        if (Current is Export Export) Export.Rigs.Add(Descriptor.Project(Actor));
    }

    internal static void CaptureRecipe(NativeContentPickKind Kind, string Id, string Name, string Fingerprint = "")
    {
        if (Current is not Export Export) return;
        Export.Entries.Add(new NativeContentEntry
        {
            Kind = Kind, Id = Id, Name = Name, Fingerprint = Fingerprint,
            ImageAbsence = Kind == NativeContentPickKind.Visual
                ? "Unsupported: atmosphere requires a rendered generated occurrence, not an asset portrait."
                : ThumbnailAbsence
        });
    }

    static void CompleteExport(Export Export, MapRun Run)
    {
        if (Export.Claim.ReadCancellationReason() is { } CancellationReason) { Cancel(CancellationReason); return; }
        var Entries = Export.Entries.ToArray(); var Rigs = Export.Rigs.ToArray();
        Array.Sort(Entries, (Left, Right) =>
        {
            var Kind = Left.Kind.CompareTo(Right.Kind);
            return Kind != 0 ? Kind : StringComparer.Ordinal.Compare(Left.Id, Right.Id);
        });
        Array.Sort(Rigs, (Left, Right) => StringComparer.Ordinal.Compare(Left.Actor, Right.Actor));
        var Catalog = new NativeContentCatalog
        {
            Schema = NativeContentArtifact.CatalogSchema,
            RequestId = Export.Claim.Request.Id, Identity = Export.Claim.Request.Identity,
            Configuration = RunConfigurationCodec.Serialize(Export.Configuration),
            ResolvedSeed = Export.Configuration.Layout.Seed,
            NativeRunSettingsSha256 = Export.Claim.SettingsSha256, Ascent = Export.Claim.Ascent,
            Entries = Entries, Rigs = Rigs,
            Occurrences = Array.ConvertAll(Run.Slots, Slot => new NativeContentOccurrence
            {
                SlotIndex = Slot.Index, OccurrenceRequestIndex = Slot.Occurrence.RequestIndex,
                SourceId = Slot.Source.Id
            })
        };
        Export.Claim.PublishJson(NativeContentArtifact.CatalogFile, Catalog, NativeContentArtifact.MaximumCatalogBytes);
        Finish(Export.Claim, new NativeContentResult
        {
            Schema = NativeContentArtifact.ResultSchema,
            RequestId = Catalog.RequestId, Identity = Catalog.Identity, Outcome = "Exported",
            Configuration = Catalog.Configuration, ResolvedSeed = Catalog.ResolvedSeed,
            NativeRunSettingsSha256 = Catalog.NativeRunSettingsSha256, Ascent = Catalog.Ascent
        });
    }

    internal static void ObserveReadyCurrent(MapRun Run)
    {
        if (!ReferenceEquals(Map.CurrentRun, Run)) return;
        try
        {
            if (Current is Export Export)
            {
                if (Run.Configuration.Protocol.GenerationEpoch == Export.Configuration.Protocol.GenerationEpoch
                    && Run.ReadyCurrentSlot is not null)
                {
                    Export.IconSources.Clear(); Current = new IconPending(Export, Run);
                }
                return;
            }
            if (Current is not Preview Preview || Run.Configuration.Protocol.GenerationEpoch
                    != Preview.Configuration.Protocol.GenerationEpoch) return;
            if (Preview.Claim.ReadCancellationReason() is { } CancellationReason) { Cancel(CancellationReason); return; }
            var Request = Preview.Claim.Request;
            if (Run.ReadyCurrentSlot is not { } Slot || Slot.Index != Request.SlotIndex
                || Slot.Occurrence.RequestIndex != Request.OccurrenceRequestIndex
                || Slot.Source.Id != Request.SourceId) return;
            var Camera = MainCamera.instance ? MainCamera.instance.cam : null;
            if (!Camera)
            {
                Cancel("The completed occurrence has no native game camera.");
                return;
            }
            Current = new AwaitingRender(Preview, Run, Slot, Camera);
            RenderPipelineManager.endCameraRendering += CameraRendered;
        }
        catch (Exception Failure) { FailTool(Failure); }
    }

    static void CameraRendered(ScriptableRenderContext Context, Camera Camera)
    {
        if (Current is not AwaitingRender Awaiting || Camera != Awaiting.Camera
            || LoadingScreenHandler.loading) return;
        RenderPipelineManager.endCameraRendering -= CameraRendered;
        var Render = new Rendering(Awaiting);
        Current = Render;
        try { Plugin.StartRoutine(CaptureFrame(Render)); }
        catch (Exception Failure)
        {
            FailTool(Failure);
        }
    }

    static IEnumerator CaptureFrame(Rendering Render)
    {
        Texture2D? Pixels = null;
        try
        {
            yield return new WaitForEndOfFrame();
            var Source = Render.Source; var Run = Source.Run;
            var CancellationReason = Render.CancellationReason ?? Render.Claim.ReadCancellationReason();
            if (CancellationReason is not null
                || !ReferenceEquals(Map.CurrentRun, Run)
                || !ReferenceEquals(Run.ReadyCurrentSlot, Source.Slot)
                || !Source.Camera || !MainCamera.instance || MainCamera.instance.cam != Source.Camera
                || LoadingScreenHandler.loading || Time.frameCount != Render.Frame)
            {
                Finish(Render.Claim, CreateResult(Render, "Cancelled",
                    CancellationReason ?? "The rendered occurrence changed before capture."));
                yield break;
            }
            NativeContentResult Result;
            try
            {
                Pixels = ScreenCapture.CaptureScreenshotAsTexture();
                if (!Pixels) throw new InvalidOperationException("PEAK did not produce a rendered display capture.");
                var Png = Pixels.EncodeToPNG();
                Result = CreateResult(Render, "Rendered", "");
                Result.ImageFile = NativeContentArtifact.PreviewImageFile; Result.ImageSha256 = Hash(Png);
                Result.ImageWidth = Pixels.width; Result.ImageHeight = Pixels.height;
                Render.Claim.PublishBytes(Result.ImageFile, Png);
            }
            catch (Exception Failure)
            {
                Render.Claim.DiscardUnpublished();
                Result = CreateResult(Render, "Rejected", Failure.Message);
            }
            Finish(Render.Claim, Result);
        }
        finally
        {
            if (Pixels) UnityEngine.Object.Destroy(Pixels);
            if (ReferenceEquals(Current, Render))
            {
                try
                {
                    Render.Claim.DiscardUnpublished();
                    Finish(Render.Claim, CreateResult(Render, "Cancelled",
                        Render.CancellationReason ?? "The owned preview capture was stopped."));
                }
                catch (Exception Failure) { Plugin.PLLLog.LogError(Failure); }
                finally
                {
                    Render.Claim.Dispose();
                    if (ReferenceEquals(Current, Render)) Current = null;
                }
            }
        }
    }

    static NativeContentResult CreateResult(Rendering Render, string Outcome, string Failure)
    {
        var Source = Render.Source; var Claim = Render.Claim;
        return new NativeContentResult
        {
            Schema = NativeContentArtifact.ResultSchema,
            RequestId = Claim.Request.Id, Identity = Claim.Request.Identity,
            Outcome = Outcome, Failure = Failure, SeedText = Claim.Request.SeedText,
            ResolvedSeed = Source.Run.Configuration.Layout.Seed,
            Configuration = RunConfigurationCodec.Serialize(Source.Run.Configuration),
            SlotIndex = Source.Slot.Index, SourceId = Source.Slot.Source.Id,
            OccurrenceRequestIndex = Source.Slot.Occurrence.RequestIndex,
            RenderedFrame = Render.Frame, CameraWorldPose = Render.CameraWorldPose,
            CameraProjection = Render.CameraProjection,
            NativeRunSettingsSha256 = Claim.SettingsSha256, Ascent = Claim.Ascent
        };
    }

    internal static void RunReleased(MapRun Run)
    {
        var Configuration = Current switch
        {
            Export Export => Export.Configuration,
            IconPending Batch => Batch.Export.Configuration,
            IconReadback Copy => Copy.Batch.Export.Configuration,
            Preview Preview => Preview.Configuration,
            AwaitingRender Awaiting => Awaiting.Preview.Configuration,
            Rendering Render => Render.Source.Preview.Configuration,
            _ => null
        };
        if (Configuration?.Protocol.GenerationEpoch == Run.Configuration.Protocol.GenerationEpoch)
            Cancel("The preview run was released before capture completed.");
    }

    internal static void BindRun(MapRun Run)
    {
        if (Current is not Preview Preview || Run.Configuration.Protocol.GenerationEpoch
            != Preview.Configuration.Protocol.GenerationEpoch) return;
        var Request = Preview.Claim.Request;
        if (Request.SlotIndex >= Run.Slots.Length
            || Run.Slots[Request.SlotIndex].Source.Id != Request.SourceId
            || Run.Slots[Request.SlotIndex].Occurrence.RequestIndex != Request.OccurrenceRequestIndex)
        {
            var Failure = new InvalidOperationException("The prepared layout differs from the requested preview occurrence.");
            FailTool(Failure); throw Failure;
        }
    }

    internal static void Cancel(string Failure)
    {
        if (Current is null) return;
        if (Current is IconReadback Copy)
        {
            Copy.CancellationReason ??= Failure; Copy.Batch.Export.Icons.Clear();
            return;
        }
        if (Current is Rendering Render) { Render.CancellationReason ??= Failure; return; }
        RenderPipelineManager.endCameraRendering -= CameraRendered;
        var Claim = Current.Claim;
        try
        {
            Claim.DiscardUnpublished();
            Finish(Claim, new NativeContentResult
            {
                Schema = NativeContentArtifact.ResultSchema,
                RequestId = Claim.Request.Id, Identity = Claim.Request.Identity,
                Outcome = "Cancelled", Failure = Failure
            });
        }
        catch (Exception PublicationFailure) { Plugin.PLLLog.LogError(PublicationFailure); }
        finally
        {
            Claim.Dispose();
            if (ReferenceEquals(Current?.Claim, Claim)) Current = null;
        }
    }

    internal static void StartupFailed(StartupTicket? Ticket, Exception Failure)
    {
        if (Current is not { } Work || !ReferenceEquals(Ticket, Work.Claim.Ticket)) return;
        if (Work is IconReadback Copy)
        {
            Copy.CancellationReason ??= Failure.Message; Copy.Batch.Export.Icons.Clear();
            return;
        }
        if (Work is Rendering Render) { Render.CancellationReason ??= Failure.Message; return; }
        FailTool(Failure);
    }

    internal static void Tick()
    {
        if (Current is Rendering { CancellationReason: not null }) return;
        if (Current is IconReadback { CancellationReason: not null }) return;
        if (Current is { } Work && Work.Claim.ReadCancellationReason() is { } CancellationReason)
        {
            Cancel(CancellationReason); return;
        }
        if (Current is IconPending Batch)
            try { SubmitIcon(Batch); }
            catch (Exception Failure) { FailTool(Failure); }
    }

    static void FailTool(Exception Failure)
    {
        RenderPipelineManager.endCameraRendering -= CameraRendered;
        if (Current is not { } Work) { Plugin.PLLLog.LogError(Failure); return; }
        try
        {
            Work.Claim.DiscardUnpublished();
            Finish(Work.Claim, new NativeContentResult
            {
                Schema = NativeContentArtifact.ResultSchema,
                RequestId = Work.Claim.Request.Id, Identity = Work.Claim.Request.Identity,
                Outcome = Failure is OperationCanceledException ? "Cancelled" : "Rejected", Failure = Failure.Message
            });
        }
        catch (Exception PublicationFailure)
        {
            Plugin.PLLLog.LogError(new AggregateException(
                "Native content publication or queue cleanup failed.",
                Failure, PublicationFailure));
        }
        finally
        {
            Work.Claim.Dispose();
            if (ReferenceEquals(Current?.Claim, Work.Claim)) Current = null;
        }
    }

    static void Finish(Claim Claim, NativeContentResult Result, Exception? Cause = null)
    {
        var Published = false;
        try
        {
            Claim.PublishJson(NativeContentArtifact.ResultFile, Result, NativeContentArtifact.MaximumRequestBytes);
            Claim.ArchiveRequest();
            System.IO.Directory.Move(Claim.Directory, Path.Combine(ToolDirectory, Claim.Request.Id));
            Published = true;
            Claim.DeleteQueuedInput(); Claim.Dispose();
            var CancelPath = Path.Combine(ToolDirectory, Claim.Request.Id + ".cancel");
            if (File.Exists(CancelPath)) File.Delete(CancelPath);
        }
        catch (Exception PublicationFailure)
        {
            Exception? CleanupFailure = null;
            if (!Published)
                try { Claim.DiscardUnpublished(); }
                catch (Exception Failure) { CleanupFailure = Failure; }
            if (Cause is not null || CleanupFailure is not null)
            {
                var Failures = new List<Exception>();
                if (Cause is not null) Failures.Add(Cause);
                Failures.Add(PublicationFailure);
                if (CleanupFailure is not null) Failures.Add(CleanupFailure);
                throw new AggregateException("Native content publication or cleanup failed.", Failures);
            }
            throw;
        }
        finally
        {
            Claim.Dispose();
            if (ReferenceEquals(Current?.Claim, Claim)) Current = null;
        }
    }

    static void RequireInstalledIdentity(NativeContentIdentity Identity)
    {
        var Runtime = typeof(Plugin).Assembly;
        if (Runtime.ManifestModule.ModuleVersionId.ToString("D") != Identity.RuntimeMvid
            || Application.unityVersion != Identity.UnityVersion
            || Identity.PeakSha256 != SupportedPeakSha256
            || HashFile(Runtime.Location) != Identity.RuntimeSha256
            || HashFile(typeof(Item).Assembly.Location) != Identity.PeakSha256)
            throw new InvalidOperationException("The installed game, runtime or bundle differs from the native content request.");
        BiomeRegistration.RequireToolBundle(Identity.BundleFile, Identity.BundleSha256);
    }

    internal static string HashFile(string Path)
    {
        using var Stream = File.OpenRead(Path);
        using var Sha = SHA256.Create();
        return NativeContentArtifact.Hex(Sha.ComputeHash(Stream));
    }

    internal static string Hash(ReadOnlySpan<byte> Bytes)
    {
        using var Sha = SHA256.Create();
        Span<byte> Digest = stackalloc byte[32];
        if (!Sha.TryComputeHash(Bytes, Digest, out var Written) || Written != Digest.Length)
            throw new InvalidOperationException("SHA-256 did not produce its required 32-byte digest.");
        return NativeContentArtifact.Hex(Digest);
    }

    internal static string Fingerprint(Action<BinaryWriter> Write)
    {
        using var Sha = SHA256.Create();
        using (var Stream = new CryptoStream(System.IO.Stream.Null, Sha, CryptoStreamMode.Write))
        using (var Writer = new BinaryWriter(Stream, StrictUtf8)) Write(Writer);
        return NativeContentArtifact.Hex(Sha.Hash!);
    }

    static float[] Matrix(Matrix4x4 Value)
    {
        var Result = new float[16];
        for (var i = 0; i < 16; i++) Result[i] = Value[i];
        return Result;
    }

}
