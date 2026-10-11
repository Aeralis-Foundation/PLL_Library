using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PLL.Library;

internal static class NativeFogRenderRuntime
{
    static readonly int ViewProperty = Shader.PropertyToID("PllFogView");
    static readonly int ContributionsProperty = Shader.PropertyToID("PllFogContributions");
    static readonly int DepthProperty = Shader.PropertyToID("PllFogDepth");
    static readonly int CoverageProperty = Shader.PropertyToID("_PllFlowCoverage");
    static Session? Owner;

    internal static void WriteRenderManifest(BinaryWriter Writer) =>
        Writer.Write((Owner ??= new Session()).RenderFingerprint);

    internal static void Prepare(Scene Scene, IEnumerable<PllFogShaderAdapter> ContentShaders)
    {
        if (Owner is { View: not null }) return;
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            throw new InvalidOperationException("Fog rendering requires Direct3D11.");
        if (!Scene.IsValid() || !Scene.isLoaded)
            throw new ArgumentException("Fog rendering requires a loaded scene.", nameof(Scene));
        var Current = Owner ?? new Session();
        Owner = null;
        Current.Prepare(Scene, ContentShaders);
        Owner = Current;
    }

    internal static void BindSources(Func<double?> ReadClock, FogZoneState[] Sources)
    {
        if (Owner is not { } Current) return;
        var Compositor = new FogZoneCompositor(ReadClock, Sources);
        Current.AllocateContributions(Sources.Length);
        Current.Compositor = Compositor;
    }

    internal static void ReplaceSources(FogZoneState[] Sources)
    {
        if (Owner is not { Compositor: { } Compositor } Current) return;
        Current.AllocateContributions(Sources.Length);
        Compositor.ReplaceSources(Sources);
    }

    internal static void ClearSources()
    {
        if (Owner is not { ZeroView: not null } Current) return;
        Current.Compositor = null;
        Shader.SetGlobalConstantBuffer(ViewProperty, Current.ZeroView, 0, 96);
    }

    internal static bool SupportsFlowMaterial(Material Material)
    {
        if (Owner is not { } Current) return false;
        var Shader = CanonicalShader(Material.shader);
        return Current.FlowShaders.TryGetValue(Shader, out var Adapted)
            && (Shader != Adapted || Material.GetFloat("_SurfaceMode") == (float)PllSurfaceRenderMode.FluidTransparent);
    }

    internal static bool SupportsMaterial(Shader Shader)
    {
        if (Owner is not { } Current) return false;
        if (Current.NativeShaders.TryGetValue(Shader, out var Native)) Shader = Native;
        return Current.FogShaders.ContainsKey(Shader);
    }

    internal static int AdaptFlowMaterial(Material Owned)
    {
        var Current = Owner ?? throw new InvalidOperationException("Fog rendering is not prepared.");
        if (!Current.FlowShaders.TryGetValue(Owned.shader, out var Adapted))
            throw new ArgumentException("Flow material has no supported opacity channel.", nameof(Owned));
        if (Adapted != Owned.shader)
        {
            Current.Leases.Add(Owned, new MaterialLease(Owned, Owned.shader, null, 1));
            AssignShader(Owned, Adapted);
        }
        Owned.SetFloat(CoverageProperty, 1f);
        return CoverageProperty;
    }

    internal static void RetireMaterial(Material Material)
    {
        if (Owner is not { } Current || !Current.Leases.TryGetValue(Material, out var Lease)) return;
        if (Lease.Pins == 0) throw new InvalidOperationException("Material lease has no retained owner.");
        Lease.Pins--;
        Current.Retire(Lease);
    }

    internal static Material CanonicalMaterial(Material Material) =>
        Owner is { } Current && Current.Leases.TryGetValue(Material, out var Lease)
            && Lease.Source ? Lease.Source! : Material;

    internal static Shader CanonicalShader(Shader Shader) =>
        Owner is { } Current && Current.NativeShaders.TryGetValue(Shader, out var Native) ? Native : Shader;

    internal static Material TranslateMaterial(Material Material) =>
        Owner is { } Current && Current.Aliases.TryGetValue(Material, out var Alias) ? Alias.Material : Material;

    internal static Material RetainMaterialAlias(Material Material) =>
        Owner is { } Current ? Current.RetainMaterialAlias(Material) : Material;

    internal static void BindRenderer(Renderer Renderer) => Owner?.BindRenderer(Renderer);

    internal static void RetireRoot(GameObject Root)
    {
        if (Owner is null) return;
        foreach (var Component in Root.GetComponentsInChildren<Component>(true))
            if (Component is ShaderEffect Effect) RetireEffect(Effect);
            else if (Component is Renderer Renderer) RetireRenderer(Renderer);
    }

    internal static void RetireEffect(ShaderEffect Effect)
    {
        if (Owner is not { } Current || !Current.Effects.TryGetValue(Effect, out var Materials)) return;
        Current.Effects.Remove(Effect);
        foreach (var Material in Materials) RetireMaterial(Material);
    }

    internal static void RetireRenderer(Renderer Renderer)
    {
        if (Owner is not { } Current || !Current.Renderers.TryGetValue(Renderer, out var Leases)) return;
        Current.Renderers.Remove(Renderer);
        foreach (var Lease in Leases)
        {
            Lease.Renderers.Remove(Renderer);
            Current.Retire(Lease);
        }
    }

    internal static void Release()
    {
        var Released = Owner;
        Owner = null;
        Released?.Dispose();
    }

    static void AssignShader(Material Material, Shader Shader)
    {
        var Queue = Material.renderQueue;
        Material.shader = Shader;
        Material.renderQueue = Queue;
    }

    sealed class MaterialLease
    {
        internal readonly Material Material;
        internal readonly Shader Native;
        internal readonly Material? Source;
        internal readonly HashSet<Renderer> Renderers = new();
        internal int Pins;

        internal MaterialLease(Material Material, Shader Native, Material? Source, int Pins)
        {
            this.Material = Material; this.Native = Native; this.Source = Source;
            this.Pins = Pins;
        }

        internal void Release()
        {
            if (!Material) return;
            if (Source is not null) UnityEngine.Object.Destroy(Material);
            else AssignShader(Material, Native);
        }
    }

    sealed class Session : IDisposable
    {
        internal readonly Dictionary<Shader, Shader> FogShaders = new(), FlowShaders = new(), NativeShaders = new();
        internal readonly Dictionary<Material, MaterialLease> Leases = new(), Aliases = new();
        internal readonly Dictionary<Renderer, HashSet<MaterialLease>> Renderers = new();
        internal readonly Dictionary<ShaderEffect, List<Material>> Effects = new();
        internal FogZoneCompositor? Compositor;
        internal GraphicsBuffer View = null!, ZeroView = null!;
        internal Material Opaque = null!;
        internal FogPass Pass = null!;
        internal ResetPass Reset = null!, Neutral = null!;
        internal readonly Vector4[] ViewRows = new Vector4[6];
        internal Texture2D? Contributions;
        internal RTHandle? ContributionHandle;
        internal Vector4[] Pixels = Array.Empty<Vector4>();
        internal readonly byte[] RenderFingerprint;
        AssetBundle Bundle = null!;
        readonly Stream BundleStream;
        Shader? StockParticleFog;

        internal Session()
        {
            var BundlePath = Path.Combine(Paths.PluginPath,
                NativeFogResourceContract.DirectoryName, NativeFogResourceContract.FileName);
            try { BundleStream = new FileStream(BundlePath, FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (IOException Failure) when (Failure is FileNotFoundException or DirectoryNotFoundException)
            {
                throw new FileNotFoundException(
                    $"Fog resources are missing. Install {NativeFogResourceContract.PackageId}.", BundlePath, Failure);
            }
            try
            {
                if (BundleStream.Length != NativeFogResourceContract.BundleBytes)
                    throw new InvalidDataException(
                        $"Fog resources have the wrong size. Reinstall {NativeFogResourceContract.PackageId}.");
                using var Hash = SHA256.Create();
                RenderFingerprint = Hash.ComputeHash(BundleStream);
                if (BitConverter.ToString(RenderFingerprint).Replace("-", string.Empty)
                    != NativeFogResourceContract.BundleSha256)
                    throw new InvalidDataException(
                        $"Fog resources have different content. Reinstall {NativeFogResourceContract.PackageId}.");
                BundleStream.Position = 0;
            }
            catch
            {
                BundleStream.Dispose();
                throw;
            }
        }

        internal void Prepare(Scene Scene, IEnumerable<PllFogShaderAdapter> ContentShaders)
        {
            try
            {
                Bundle = AssetBundle.LoadFromStream(BundleStream);
                if (!Bundle) throw new InvalidOperationException("Fog shader bundle failed to load.");
                var FogByName = new Dictionary<string, Shader>(StringComparer.Ordinal);
                var FlowByName = new Dictionary<string, Shader>(StringComparer.Ordinal);
                const string NativePrefix = "PLL/Fog/Native/", FlowPrefix = "PLL/Fog/Flow/";
                foreach (var Shader in Bundle.LoadAllAssets<Shader>())
                {
                    if (Shader.name.StartsWith(NativePrefix, StringComparison.Ordinal)) FogByName.Add(Shader.name.Substring(NativePrefix.Length), Shader);
                    else if (Shader.name.StartsWith(FlowPrefix, StringComparison.Ordinal)) FlowByName.Add(Shader.name.Substring(FlowPrefix.Length), Shader);
                }
                FogByName.TryGetValue("Particles/Standard Unlit", out StockParticleFog);
                var FogRenderers = new List<Renderer>();
                var FogMaterialWriters = new List<(Renderer Renderer, Material Material)>();
                var FogShaderEffects = new List<ShaderEffect>();
                var Captured = new Dictionary<Shader, string>();
                var Visited = new HashSet<GameObject>();
                var Pending = new Queue<GameObject>(Scene.GetRootGameObjects());
                while (Pending.Count != 0)
                {
                    var Root = Pending.Dequeue();
                    if (!Visited.Add(Root)) continue;
                    var InScene = Root.scene == Scene;
                    foreach (var Component in Root.GetComponentsInChildren<Component>(true))
                        switch (Component)
                        {
                            case Renderer Renderer:
                                foreach (var Material in Renderer.sharedMaterials)
                                {
                                    if (!Material) continue;
                                    var Shader = Material.shader;
                                    if (Shader && FogByName.ContainsKey(Shader.name)) Captured[Shader] = Shader.name;
                                }
                                if (InScene) FogRenderers.Add(Renderer);
                                break;
                            case StormVisual Storm when InScene && Storm.quadRend && Storm.quadMat:
                                FogMaterialWriters.Add((Storm.quadRend, Storm.quadMat)); break;
                            case Transition_Shader Transition when InScene && Transition.rend && Transition.mat:
                                FogMaterialWriters.Add((Transition.rend, Transition.mat)); break;
                            case ShaderEffect Effect when InScene: FogShaderEffects.Add(Effect); break;
                            case PropSpawner Spawner:
                                if (NativePrefabContracts.EffectiveProps(Spawner) is { } Candidates)
                                    foreach (var Candidate in Candidates) if (Candidate) Pending.Enqueue(Candidate);
                                break;
                            case LavaRiver River:
                                if (River.splash) Pending.Enqueue(River.splash);
                                if (River.spawnAlongSpline) Pending.Enqueue(River.spawnAlongSpline);
                                if (River.detailsAlongSpline is { } Details)
                                    foreach (var Detail in Details) if (Detail) Pending.Enqueue(Detail);
                                break;
                        }
                }
                foreach (var Pair in Captured)
                {
                    var Fog = FogByName[Pair.Value];
                    if (!Fog || !Fog.isSupported) throw new InvalidOperationException($"Fog shader '{Pair.Value}' is unsupported.");
                    FogShaders.Add(Pair.Key, Fog); NativeShaders.Add(Fog, Pair.Key);
                    if (FlowByName.TryGetValue(Pair.Value, out var Flow))
                    {
                        if (!Flow || !Flow.isSupported) throw new InvalidOperationException($"Flow shader '{Pair.Value}' is unsupported.");
                        FlowShaders.Add(Pair.Key, Flow); NativeShaders.Add(Flow, Pair.Key);
                    }
                }
                foreach (var Adapter in ContentShaders)
                {
                    var Original = Adapter.OriginalShader;
                    var Fog = Adapter.FogShader;
                    if (FogShaders.TryGetValue(Original, out var Existing))
                    {
                        if (Existing != Fog) throw new InvalidOperationException("Content fog shaders conflict for the same original shader.");
                        continue;
                    }
                    if (NativeShaders.ContainsKey(Original) || FogShaders.ContainsKey(Fog)
                        || NativeShaders.ContainsKey(Fog))
                        throw new InvalidOperationException("Content fog shaders contain a conflicting adapter identity.");
                    if (!Fog.isSupported) throw new InvalidOperationException("A content fog shader is unsupported.");
                    FogShaders.Add(Original, Fog);
                    if (Adapter.Kind == PllFogShaderAdapterKind.LayeredDirect) FlowShaders.Add(Original, Fog);
                    else NativeShaders.Add(Fog, Original);
                }
                var OpaqueShader = Bundle.LoadAsset<Shader>("assets/fog/pllfogopaque.shader");
                if (!OpaqueShader || !OpaqueShader.isSupported) throw new InvalidOperationException("Opaque fog shader is unsupported.");
                Opaque = new Material(OpaqueShader);
                View = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 6, 16);
                ZeroView = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 6, 16);
                ZeroView.SetData(ViewRows);
                Pass = new FogPass(this);
                Reset = new ResetPass(RenderPassEvent.AfterRenderingTransparents);
                Neutral = new ResetPass(RenderPassEvent.BeforeRendering);
                Reset.Buffer = Neutral.Buffer = ZeroView;
                foreach (var Value in FogMaterialWriters) Borrow(Value.Renderer, Value.Material);
                foreach (var Effect in FogShaderEffects)
                    for (var i = 0; i < Effect.currentEffects.Count; i++)
                        Effect.currentEffects[i] = RetainEffect(Effect, Effect.currentEffects[i]);
                foreach (var Renderer in FogRenderers) BindRenderer(Renderer);
                Shader.SetGlobalConstantBuffer(ViewProperty, ZeroView, 0, 96);
            }
            catch (Exception Failure)
            {
                try { Dispose(); }
                catch (Exception CleanupFailure) { throw new AggregateException("Fog rendering preparation and cleanup failed.", Failure, CleanupFailure); }
                throw;
            }
        }

        internal Material RetainMaterialAlias(Material Material)
        {
            if (Leases.TryGetValue(Material, out var Existing))
            {
                Existing.Pins++;
                return Material;
            }
            if (!FogShaders.TryGetValue(Material.shader, out var Adapted) || Adapted == Material.shader) return Material;
            var Lease = Copy(Material);
            Lease.Pins++;
            return Lease.Material;
        }

        internal void BindRenderer(Renderer Renderer)
        {
            var Materials = Renderer.sharedMaterials;
            var Changed = false;
            for (var i = 0; i < Materials.Length; i++)
            {
                var Material = Materials[i];
                if (!Material) continue;
                if (Leases.TryGetValue(Material, out var Existing)) Attach(Renderer, Existing);
                else if (FogShaders.TryGetValue(Material.shader, out var Adapted) && Adapted != Material.shader)
                {
                    var Lease = Copy(Material);
                    Attach(Renderer, Lease);
                    Materials[i] = Lease.Material;
                    Changed = true;
                }
            }
            if (Changed) Renderer.sharedMaterials = Materials;
        }

        internal MaterialLease Copy(Material Source)
        {
            if (Aliases.TryGetValue(Source, out var Existing)) return Existing;
            var Adapted = FogShaders[Source.shader];
            AdmitStockParticles(Source, Adapted);
            var Material = new Material(Source);
            AssignShader(Material, Adapted);
            var Lease = new MaterialLease(Material, Source.shader, Source, 0);
            Aliases.Add(Source, Lease); Leases.Add(Material, Lease);
            return Lease;
        }

        internal void Attach(Renderer Renderer, MaterialLease Lease)
        {
            if (!Renderers.TryGetValue(Renderer, out var Materials)) Renderers.Add(Renderer, Materials = new());
            Materials.Add(Lease); Lease.Renderers.Add(Renderer);
        }

        internal Material RetainEffect(ShaderEffect Effect, Material Material)
        {
            var Alias = RetainMaterialAlias(Material);
            if (!Leases.ContainsKey(Alias)) return Material;
            if (!Effects.TryGetValue(Effect, out var Materials)) Effects.Add(Effect, Materials = new());
            Materials.Add(Alias);
            return Alias;
        }

        internal void RemoveEffect(ShaderEffect Effect, Material Material)
        {
            if (!Effects.TryGetValue(Effect, out var Materials) || !Materials.Remove(Material)) return;
            if (Materials.Count == 0) Effects.Remove(Effect);
            foreach (var Renderer in Effect.renderers) Reconcile(Renderer);
            RetireMaterial(Material);
        }

        internal void Reconcile(Renderer Renderer)
        {
            if (!Renderers.TryGetValue(Renderer, out var Existing)) { BindRenderer(Renderer); return; }
            var Current = Renderer.sharedMaterials;
            var Retiring = new List<MaterialLease>();
            foreach (var Lease in Existing)
                if (Array.IndexOf(Current, Lease.Material) < 0) Retiring.Add(Lease);
            foreach (var Lease in Retiring)
            {
                Existing.Remove(Lease); Lease.Renderers.Remove(Renderer); Retire(Lease);
            }
            BindRenderer(Renderer);
        }

        internal void Borrow(Renderer Renderer, Material Material)
        {
            if (!Material) return;
            if (Leases.TryGetValue(Material, out var Existing)) { Attach(Renderer, Existing); return; }
            var Native = NativeShaders.TryGetValue(Material.shader, out var Original) ? Original : Material.shader;
            if (!FogShaders.TryGetValue(Native, out var Adapted) || Adapted == Native) return;
            AdmitStockParticles(Material, Adapted);
            var Lease = new MaterialLease(Material, Native, null, 0);
            Leases.Add(Material, Lease); Attach(Renderer, Lease);
            if (!NativeShaders.ContainsKey(Material.shader)) AssignShader(Material, Adapted);
        }

        internal void AdmitStockParticles(Material Material, Shader Adapted)
        {
            if (Adapted != StockParticleFog) return;
            var Source = Material.GetFloat("_SrcBlend");
            var Destination = Material.GetFloat("_DstBlend");
            var Operation = Material.GetFloat("_BlendOp");
            if (Source != 5f || Destination != 10f || Operation != 0f)
                throw new ArgumentException($"Particle material '{Material.name}' needs normal alpha blending for fog; received {Source}/{Destination}/{Operation}.");
        }

        internal void Retire(MaterialLease Lease)
        {
            if (Lease.Pins != 0 || Lease.Renderers.Count != 0) return;
            Leases.Remove(Lease.Material);
            if (Lease.Source is not null) Aliases.Remove(Lease.Source);
            Lease.Release();
        }

        internal void AllocateContributions(int Count)
        {
            var Texels = Math.Max(2, checked(Count * 2));
            var Width = Math.Min(Texels, SystemInfo.maxTextureSize);
            var Height = (Texels + Width - 1) / Width;
            if (Height > SystemInfo.maxTextureSize)
                throw new InvalidOperationException("Fog contributions exceed the texture capacity.");
            if (!Contributions || Contributions.width * Contributions.height < Texels)
            {
                ContributionHandle?.Release();
                if (Contributions) UnityEngine.Object.Destroy(Contributions);
                Contributions = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true)
                { name = "PLL Fog Contributions", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                Pixels = new Vector4[Width * Height];
                ContributionHandle = RTHandles.Alloc(Contributions);
            }
        }

        internal void Upload(IReadOnlyList<FogZoneContribution> Sample)
        {
            for (var i = 0; i < Sample.Count; i++)
            {
                var Contribution = Sample[i];
                Pixels[i * 2] = new Vector4(Contribution.Color.r, Contribution.Color.g, Contribution.Color.b, Contribution.Influence);
                Pixels[i * 2 + 1] = new Vector4(Contribution.VisibilityDistance, 0f, 0f, 0f);
            }
            Contributions!.SetPixelData(Pixels, 0); Contributions.Apply(false, false);
        }

        public void Dispose()
        {
            List<Exception>? Failures = null;
            if (View is not null)
                CleanupOperations.Capture(ref Failures, () => Shader.SetGlobalConstantBuffer(ViewProperty, (GraphicsBuffer)null!, 0, 0));
            foreach (var Effect in Effects.Keys)
            {
                if (!Effect) continue;
                CleanupOperations.Capture(ref Failures, () =>
                {
                    for (var i = 0; i < Effect.currentEffects.Count; i++)
                        if (Leases.TryGetValue(Effect.currentEffects[i], out var Lease) && Lease.Source)
                            Effect.currentEffects[i] = Lease.Source;
                });
            }
            foreach (var Renderer in Renderers.Keys)
            {
                if (!Renderer) continue;
                CleanupOperations.Capture(ref Failures, () =>
                {
                    var Materials = Renderer.sharedMaterials;
                    var Changed = false;
                    for (var i = 0; i < Materials.Length; i++)
                        if (Materials[i] && Leases.TryGetValue(Materials[i], out var Lease) && Lease.Source)
                        { Materials[i] = Lease.Source; Changed = true; }
                    if (Changed) Renderer.sharedMaterials = Materials;
                });
            }
            foreach (var Lease in Leases.Values) CleanupOperations.Capture(ref Failures, Lease.Release);
            Leases.Clear(); Aliases.Clear(); Renderers.Clear(); Effects.Clear();
            if (ContributionHandle is { } Handle) CleanupOperations.Capture(ref Failures, Handle.Release);
            if (Contributions) CleanupOperations.Capture(ref Failures, () => UnityEngine.Object.Destroy(Contributions));
            if (View is { } AllocatedView) CleanupOperations.Capture(ref Failures, AllocatedView.Dispose);
            if (ZeroView is { } AllocatedZero) CleanupOperations.Capture(ref Failures, AllocatedZero.Dispose);
            if (Opaque) CleanupOperations.Capture(ref Failures, () => UnityEngine.Object.Destroy(Opaque));
            if (Bundle) CleanupOperations.Capture(ref Failures, () => Bundle.Unload(true));
            CleanupOperations.Capture(ref Failures, BundleStream.Dispose);
            if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
            if (Failures is { Count: > 1 }) throw new AggregateException("Fog rendering cleanup failed.", Failures);
        }
    }

    sealed class FogPass : ScriptableRenderPass
    {
        readonly Session Owner;
        internal FogPass(Session Owner)
        {
            this.Owner = Owner; renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            requiresIntermediateTexture = true; ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        sealed class PassInputs
        {
            internal TextureHandle Color, Depth, Contributions;
            internal Material Material = null!;
            internal GraphicsBuffer View = null!;
        }

        public override void RecordRenderGraph(RenderGraph Graph, ContextContainer Frame)
        {
            var Camera = Frame.Get<UniversalCameraData>();
            var Sample = Owner.Compositor?.Sample(Camera.camera.transform.position)
                ?? Array.Empty<FogZoneContribution>();
            if (Sample.Count == 0) { Owner.Neutral.RecordRenderGraph(Graph, Frame); return; }
            var Resources = Frame.Get<UniversalResourceData>();
            if (Resources.isActiveTargetBackBuffer || !Resources.cameraDepthTexture.IsValid())
                throw new InvalidOperationException("Fog camera has no intermediate color or copied opaque depth.");
            Owner.Upload(Sample);
            var Source = Resources.activeColorTexture;
            var Description = Graph.GetTextureDesc(Source);
            Description.name = "PLL Fog Camera Color"; Description.clearBuffer = false;
            var Inverse = (GL.GetGPUProjectionMatrix(Camera.GetProjectionMatrix(), true) * Camera.GetViewMatrix()).inverse;
            var Position = Camera.camera.transform.position;
            Owner.ViewRows[0] = new Vector4(1f / Camera.cameraTargetDescriptor.width,
                1f / Camera.cameraTargetDescriptor.height, Owner.Contributions!.width, SystemInfo.usesReversedZBuffer ? 0f : 1f);
            for (var i = 0; i < 4; i++) Owner.ViewRows[i + 1] = Inverse.GetRow(i);
            Owner.ViewRows[5] = new Vector4(Position.x, Position.y, Position.z, Sample.Count);
            Owner.View.SetData(Owner.ViewRows);
            var Destination = Graph.CreateTexture(Description);
            using (var Builder = Graph.AddRasterRenderPass<PassInputs>("PLL Fog Camera", out var Inputs))
            {
                Inputs.Color = Source; Inputs.Depth = Resources.cameraDepthTexture;
                Inputs.Material = Owner.Opaque; Inputs.View = Owner.View;
                Inputs.Contributions = Graph.ImportTexture(Owner.ContributionHandle!);
                Builder.UseTexture(Inputs.Color, AccessFlags.Read); Builder.UseTexture(Inputs.Depth, AccessFlags.Read);
                Builder.UseTexture(Inputs.Contributions, AccessFlags.Read);
                Builder.UseBuffer(Graph.ImportBuffer(Owner.View), AccessFlags.Read);
                Builder.SetRenderAttachment(Destination, 0, AccessFlags.WriteAll);
                Builder.AllowGlobalStateModification(true);
                Builder.SetRenderFunc((PassInputs Data, RasterGraphContext Context) =>
                {
                    Context.cmd.SetGlobalConstantBuffer(Data.View, ViewProperty, 0, 96);
                    Context.cmd.SetGlobalTexture(ContributionsProperty, Data.Contributions);
                    Context.cmd.SetGlobalTexture(DepthProperty, Data.Depth);
                    Blitter.BlitTexture(Context.cmd, Data.Color, new Vector4(1f, 1f, 0f, 0f), Data.Material, 0);
                });
            }
            Resources.cameraColor = Destination;
        }
    }

    sealed class ResetPass : ScriptableRenderPass
    {
        internal GraphicsBuffer Buffer = null!;
        internal ResetPass(RenderPassEvent Event) => renderPassEvent = Event;
        sealed class PassInputs { internal GraphicsBuffer Buffer = null!; }
        public override void RecordRenderGraph(RenderGraph Graph, ContextContainer Frame)
        {
            using var Builder = Graph.AddRasterRenderPass<PassInputs>("PLL Fog Camera Release", out var Inputs);
            Inputs.Buffer = Buffer;
            Builder.UseBuffer(Graph.ImportBuffer(Buffer), AccessFlags.Read);
            Builder.AllowGlobalStateModification(true); Builder.AllowPassCulling(false);
            Builder.SetRenderFunc((PassInputs Data, RasterGraphContext Context) =>
                Context.cmd.SetGlobalConstantBuffer(Data.Buffer, ViewProperty, 0, 96));
        }
    }

    [HarmonyPatch(typeof(ScriptableRenderer), "AddRenderPasses")]
    static class RenderPassPatch
    {
        [HarmonyPostfix]
        static void Postfix(ScriptableRenderer __instance, [HarmonyArgument(0)] ref RenderingData Data)
        {
            if (Owner is not { View: not null } Current) return;
            if (MainCamera.instance && Data.cameraData.camera == MainCamera.instance.cam)
            {
                __instance.EnqueuePass(Current.Pass); __instance.EnqueuePass(Current.Reset);
            }
            else __instance.EnqueuePass(Current.Neutral);
        }
    }

    [HarmonyPatch(typeof(Renderer), nameof(Renderer.material), MethodType.Getter)]
    static class MaterialPatch
    {
        [HarmonyPostfix]
        static void Postfix(Renderer __instance, Material __result) => Owner?.Borrow(__instance, __result);
    }

    [HarmonyPatch(typeof(Renderer), nameof(Renderer.materials), MethodType.Getter)]
    static class MaterialsPatch
    {
        [HarmonyPostfix]
        static void Postfix(Renderer __instance, Material[] __result)
        {
            if (Owner is not { } Current) return;
            foreach (var Material in __result) Current.Borrow(__instance, Material);
        }
    }

    [HarmonyPatch(typeof(Renderer), nameof(Renderer.GetMaterials), new[] { typeof(List<Material>) })]
    static class MaterialListPatch
    {
        [HarmonyPostfix]
        static void Postfix(Renderer __instance, [HarmonyArgument(0)] List<Material> Materials)
        {
            if (Owner is not { } Current) return;
            foreach (var Material in Materials) Current.Borrow(__instance, Material);
        }
    }

    [HarmonyPatch(typeof(Transition_Shader), "Awake")]
    static class TransitionMaterialPatch
    {
        [HarmonyPostfix]
        static void Postfix(Transition_Shader __instance) => Owner?.Borrow(__instance.rend, __instance.mat);
    }

    [HarmonyPatch(typeof(ShaderEffect), "AddEffect")]
    static class AddEffectPatch
    {
        [HarmonyPrefix]
        static void Prefix(ShaderEffect __instance, [HarmonyArgument(0)] ref Material Material)
        {
            if (Owner is { } Current) Material = Current.RetainEffect(__instance, Material);
        }
        [HarmonyPostfix]
        static void Postfix(ShaderEffect __instance)
        {
            foreach (var Renderer in __instance.renderers) BindRenderer(Renderer);
        }
    }

    [HarmonyPatch(typeof(ShaderEffect), "RemoveEffect")]
    static class RemoveEffectPatch
    {
        [HarmonyPrefix]
        static void Prefix([HarmonyArgument(0)] ref Material Material) => Material = TranslateMaterial(Material);
        [HarmonyPostfix]
        static void Postfix(ShaderEffect __instance, [HarmonyArgument(0)] Material Material) => Owner?.RemoveEffect(__instance, Material);
    }

    static bool CanonicalEquality(UnityEngine.Object Left, UnityEngine.Object Right) =>
        (Left is Material LeftMaterial ? CanonicalMaterial(LeftMaterial) : Left)
        == (Right is Material RightMaterial ? CanonicalMaterial(RightMaterial) : Right);

    [HarmonyPatch]
    static class MaterialIdentityPatch
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(PSM_ReplaceMaterial), nameof(PSM_ReplaceMaterial.ModifyObject));
            yield return AccessTools.DeclaredMethod(typeof(PSM_ReplaceMaterialWithRayTargetMaterial),
                nameof(PSM_ReplaceMaterialWithRayTargetMaterial.ModifyObject));
            yield return AccessTools.DeclaredMethod(typeof(PSM_MatchMaterial), nameof(PSM_MatchMaterial.ModifyObject));
            yield return AccessTools.DeclaredMethod(typeof(PSC_RequiredMaterial), nameof(PSC_RequiredMaterial.CheckConstraint));
            yield return AccessTools.DeclaredMethod(typeof(PSC_BannedMaterial), nameof(PSC_BannedMaterial.CheckConstraint));
            yield return AccessTools.DeclaredMethod(typeof(Peak.PropSpawner_AmuletStatues),
                nameof(Peak.PropSpawner_AmuletStatues.OnLevelGenFinished));
        }
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Canonical = ((Func<UnityEngine.Object, UnityEngine.Object, bool>)CanonicalEquality).Method;
            foreach (var Instruction in Instructions)
            {
                if (Instruction.operand is MethodInfo Method && Method.DeclaringType == typeof(UnityEngine.Object) && Method.Name == "op_Equality")
                {
                    Instruction.opcode = OpCodes.Call; Instruction.operand = Canonical;
                }
                yield return Instruction;
            }
        }
    }
}
