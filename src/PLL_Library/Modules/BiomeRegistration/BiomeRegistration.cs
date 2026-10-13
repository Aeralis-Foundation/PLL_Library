using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;

namespace PLL.Library;

internal static class BiomeRegistration
{
    enum RegistrationPhase
    {
        StartupOpen,
        StartupSealed,
        FinalSealed
    }

    enum RollbackScope
    {
        Registration,
        InitialSeal,
        FinalSeal
    }

    const string BundleDirectoryName = "PLL_Biomes";
    const string ExternalFingerprintProtocol = PLLLibrary.PluginGuid + "/content-file-external/v15";
    static readonly List<ContentFile> RetainedFiles = new();
    static readonly Guid RuntimeMvid = typeof(Plugin).Assembly.ManifestModule.ModuleVersionId;
    static RegistrationPhase Phase;
    static NativeVisualSeal? NativeVisualOwner;
    static NativeCondorSource? NativeCondorOwner;

    internal static void RequireToolBundle(string FileName, string Sha256)
    {
        foreach (var File in RetainedFiles)
        {
            if (!string.Equals(System.IO.Path.GetFileName(File.Path),
                    FileName, StringComparison.Ordinal)) continue;
            if (!string.Equals(File.BundleSha256, Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The registered bundle differs from the native content request.");
            return;
        }
        throw new InvalidOperationException("The native content request bundle is not registered.");
    }

    sealed class ContentFile
    {
        BiomeAuthoringContract[]? RegisteredAuthoring;
        CustomBiomeConfigBatch? ConfigurationBatch;
        SegmentRegistrationBatch? SegmentBatch;

        ContentFile(string Path, AssetBundle Bundle, FileStream BundleStream, string BundleSha256)
        {
            this.Path = Path;
            this.Bundle = Bundle;
            this.BundleStream = BundleStream;
            this.BundleSha256 = BundleSha256;
        }

        internal string Path { get; }
        internal string BundleSha256 { get; }
        AssetBundle Bundle { get; }
        FileStream BundleStream { get; }
        internal BiomeAuthoringContract[] Authoring => RegisteredAuthoring
            ?? throw new InvalidOperationException($"PLL content bundle '{Path}' has no authoring registration batch.");
        internal SegmentRegistrationBatch Registration => SegmentBatch
            ?? throw new InvalidOperationException($"PLL content bundle '{Path}' has no segment registration batch.");
        internal FinalForceAuthoringContract[] FinalForces { get; private set; } = Array.Empty<FinalForceAuthoringContract>();
        internal int RequiredResolverMask { get; private set; }
        internal int RequiredMechanicalHazardMask { get; private set; }
        internal int RequiredVisualMask { get; private set; }
        internal int RequiredParticleMask { get; private set; }
        internal int RequiredEffectParticleMask { get; private set; }
        internal bool NeedsNativeCatalog { get; private set; }
        internal bool RequiresNativeFog { get; private set; }
        internal bool RequiresNativeMaterials { get; private set; }
        internal bool RequiresFogRendering { get; private set; }
        internal bool RequiresNativeBeetles { get; private set; }
        internal bool RequiresNativeZombies { get; private set; }
        internal bool RequiresNativeCondors { get; private set; }
        internal bool RequiresNativeTornados { get; private set; }
        internal bool RequiresNativeTumbleweeds { get; private set; }
        internal bool RequiresNativeRopes { get; private set; }
        internal bool RequiresNativeMountedTools { get; private set; }
        internal int RequiredNativeVineMask { get; private set; }
        internal byte[]? ExternalFingerprint { get; set; }

        internal static ContentFile Register(string Path, out string? BundleSha256)
        {
            BundleSha256 = null;
            FileStream? Stream = new(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using (var Sha = SHA256.Create())
                    BundleSha256 = BitConverter.ToString(Sha.ComputeHash(Stream))
                        .Replace("-", string.Empty).ToLowerInvariant();
                Stream.Position = 0;
                var Bundle = AssetBundle.LoadFromStream(Stream);
                if (!Bundle) throw new IOException($"Unity could not load the AssetBundle '{Path}'.");
                var File = new ContentFile(Path, Bundle, Stream, BundleSha256);
                Stream = null;
                try
                {
                    var Content = BiomeContentIngress.Prepare(Bundle);
                    var Authoring = BiomeAuthoringCatalog.Prepare(Content.Definitions, Content.DependencySeals);
                    File.ConfigurationBatch = CustomBiomeConfigRegistry.RegisterDefaults(Content.ConfigurationDefaults);
                    BiomeAuthoringCatalog.Register(Authoring);
                    File.RegisteredAuthoring = Authoring;
                    File.SegmentBatch = SegmentRegistry.RegisterBatch(Content.Biomes, Content.Segments, Content.Groups);
                    File.CompleteFacts();
                    CustomBiomeConfigRegistry.Materialize(File.ConfigurationBatch);
                    CustomBiomeConfigRegistry.Commit(File.ConfigurationBatch);
                    return File;
                }
                catch (Exception Failure)
                {
                    var Cleanup = File.CollectRollbackFailures(RollbackScope.Registration);
                    if (Cleanup is null) throw;
                    Cleanup.Insert(0, Failure);
                    throw new AggregateException(
                        $"PLL content bundle '{Path}' registration and rollback both failed.", Cleanup);
                }
            }
            finally { Stream?.Dispose(); }
        }

        internal List<Exception>? CollectRollbackFailures(RollbackScope Scope)
        {
            List<Exception>? Failures = null;
            if (Scope != RollbackScope.Registration) Collect(() => NativeSpawnCatalog.UnsealBatch(FinalForces));
            if (Scope == RollbackScope.FinalSeal) return Failures;
            var Registration = SegmentBatch;
            if (Registration is not null) Collect(() => SegmentRegistry.UnregisterBatch(Registration));
            var Authoring = RegisteredAuthoring;
            if (Authoring is not null) Collect(() => BiomeAuthoringCatalog.Unregister(Authoring));
            var Configurations = ConfigurationBatch;
            if (Configurations is not null)
                Collect(() => CustomBiomeConfigRegistry.Unregister(Configurations));
            Collect(() => { Bundle.Unload(true); BundleStream.Dispose(); });
            return Failures;

            void Collect(Action Cleanup)
            {
                try { Cleanup(); }
                catch (Exception Failure) { (Failures ??= new List<Exception>()).Add(Failure); }
            }
        }

        void CompleteFacts()
        {
            var Segments = Registration.Segments;
            FinalForces = new FinalForceAuthoringContract[Segments.Length];
            for (var i = 0; i < Segments.Length; i++)
            {
                FinalForces[i] = Segments[i].FinalForce;
                RequiresNativeFog |= Segments[i].FogMode == PllFogMode.Fogless;
            }
            foreach (var Contract in Authoring)
            {
                RequiredResolverMask |= Contract.RequiredNativeResolverMask;
                RequiredMechanicalHazardMask |= Contract.RequiredMechanicalHazardMask;
                RequiredVisualMask |= Contract.RequiredNativeVisualMask;
                RequiredParticleMask |= Contract.RequiredNativeParticleMask;
                RequiredEffectParticleMask |= Contract.RequiredNativeEffectParticleMask;
                NeedsNativeCatalog |= Contract.NeedsNativeCatalog;
                RequiresNativeFog |= Contract.RequiresNativeFog;
                RequiresNativeMaterials |= Contract.NativeMaterialContracts.Count != 0;
                RequiresFogRendering |= Contract.FogZoneContracts.Count != 0
                    || Contract.HasFixedFlows || Contract.HasGeneratedFlows;
                RequiresNativeBeetles |= Contract.RequiresNativeBeetles;
                RequiresNativeZombies |= Contract.RequiresNativeZombies;
                RequiresNativeCondors |= Contract.RequiresNativeCondors;
                RequiresNativeTornados |= Contract.RequiresNativeTornados;
                RequiresNativeTumbleweeds |= Contract.RequiresNativeTumbleweeds;
                RequiresNativeRopes |= Contract.RequiresNativeRopes;
                RequiresNativeMountedTools |= Contract.RequiresNativeMountedTools;
                RequiredNativeVineMask |= Contract.RequiredNativeVineMask;
            }
        }
    }

    internal static void DiscoverStartupBundles()
    {
        if (Phase != RegistrationPhase.StartupOpen) return;
        var DirectoryPath = System.IO.Path.Combine(Paths.PluginPath, BundleDirectoryName);
        string[] BundlePaths;
        try { BundlePaths = Directory.GetFiles(DirectoryPath, "*.pll", SearchOption.TopDirectoryOnly); }
        catch (DirectoryNotFoundException) { BundlePaths = Array.Empty<string>(); }
        BundlePaths = Array.FindAll(
            BundlePaths, Path => string.Equals(System.IO.Path.GetExtension(Path), ".pll", StringComparison.Ordinal));
        for (var i = 0; i < BundlePaths.Length; i++) BundlePaths[i] = System.IO.Path.GetFullPath(BundlePaths[i]);
        Array.Sort(BundlePaths, StringComparer.Ordinal);

        try
        {
            foreach (var Path in BundlePaths)
            {
                var AlreadyRetained = false;
                foreach (var File in RetainedFiles)
                    if (string.Equals(File.Path, Path, StringComparison.Ordinal))
                    {
                        AlreadyRetained = true;
                        break;
                    }
                if (AlreadyRetained) continue;
                string? BundleSha256 = null;
                try
                {
                    var Registered = ContentFile.Register(Path, out BundleSha256);
                    RetainedFiles.Add(Registered);
                }
                catch (Exception Failure) when (
                    Failure is IOException or UnauthorizedAccessException
                        or ArgumentException or NotSupportedException
                    || Failure.GetType() == typeof(InvalidOperationException))
                {
                    RejectStartupBundle(Path, BundleSha256, Failure);
                }
                catch
                {
                    if (BundleSha256 is not null)
                        Plugin.PLLLog.LogError(BundleResult(BundleSha256, "startup", "rejected"));
                    throw;
                }
            }
            CustomBiomeConfigRegistry.SealStartup();
            SegmentRegistry.SealStartup();
            Phase = RegistrationPhase.StartupSealed;
        }
        catch
        {
            foreach (var File in RetainedFiles)
                Plugin.PLLLog.LogError(BundleResult(File.BundleSha256, "startup", "rejected"));
            throw;
        }
    }

    static void RejectStartupBundle(string Path, string? BundleSha256, Exception Failure)
    {
        var Rejection = $"Rejected PLL content bundle '{Path}': {Failure}";
        Plugin.PLLLog.LogError(BundleSha256 is null ? Rejection
            : $"{BundleResult(BundleSha256, "startup", "rejected")}: {Rejection}");
    }

    static string BundleResult(string BundleSha256, string Stage, string Outcome) =>
        $"PLL bundle-result/v1 bundleSha256={BundleSha256} runtimeMvid={RuntimeMvid:D} stage={Stage} outcome={Outcome}";

    internal static void SealLive(
        MapHandler Handler,
        NativeSegmentCatalog NativeCatalog)
    {
        if (!Handler) throw new ArgumentNullException(nameof(Handler));
        if (NativeCatalog is null) throw new ArgumentNullException(nameof(NativeCatalog));
        var NativeSources = NativeCatalog.Sources;
        if (Phase == RegistrationPhase.StartupOpen)
            throw new InvalidOperationException("PLL content startup registration is not sealed.");
        PluginEntityFactoryRegistry.Seal();
        NativeLuggageCatalog? Native = null;
        NativeVisualSeal? PendingVisuals = null;
        NativeCondorSource? PendingCondors = null;
        var StartupTicket = NativeContentTools.CaptureStartupTicket();
        try
        {
            NativeLuggageCatalog.Clear();
            BiomeAuthoringCatalog.ClearExternal();
            ReleaseNativeCondors();
            var PreviousVisualOwner = NativeVisualOwner;
            NativeVisualOwner = null;
            PreviousVisualOwner?.Release();
            var RequiresNativeCatalog = false;
            var ExportContent = NativeContentTools.ExportRequested;
            var ScanNativeProps = ExportContent;
            foreach (var File in RetainedFiles)
            {
                RequiresNativeCatalog |= File.NeedsNativeCatalog;
                foreach (var Contract in File.Authoring) ScanNativeProps |= Contract.RequiresNativeContentProps;
                foreach (var Force in File.FinalForces)
                    if (Force.ForceNames.Count != 0)
                    {
                        ScanNativeProps = true;
                        break;
                    }
            }
            ItemDatabaseCatalog? Items = null;
            if (RetainedFiles.Count == 0 && !ExportContent) NativeSpawnCatalog.BeginEmptySeal();
            else Items = NativeSpawnCatalog.BeginContentSeal(NativeSources, ScanNativeProps);
            var Resolvers = NativeResolverSourceCatalog.BeginSeal(NativeCatalog);
            var MechanicalSources = NativeMechanicalHazards.BeginSeal(NativeCatalog);
            var VineSources = NativeVineRecipes.BeginSeal(NativeCatalog);
            var Visuals = new NativeVisualSeal(Handler, NativeSources);
            PendingVisuals = Visuals;
            Lazy<NativeBeetleSource>? Beetles = null;
            Lazy<NativeZombieSource>? Zombies = null;
            Lazy<NativeCondorSource>? Condors = null;
            Lazy<NativeTornadoSource>? Tornados = null;
            Lazy<NativeTumbleweedSource>? Tumbleweeds = null;
            Lazy<NativeHangingRopeSource>? Ropes = null;
            Lazy<NativeMountedToolSource>? MountedTools = null;
            Exception? NativeFailure = null;
            if (RequiresNativeCatalog)
                try { Native = NativeLuggageCatalog.CreateCanonicalCatalog(Handler); }
                catch (Exception Failure) { NativeFailure = Failure; }
            var RequiredResolverMask = 0;
            var RequiredVisualMask = 0;
            var RequiredParticleMask = 0;
            var RequiredEffectParticleMask = 0;
            for (var index = 0; index < RetainedFiles.Count;)
            {
                var File = RetainedFiles[index];
                try
                {
                    SegmentRegistry.ValidateBatch(File.Registration);
                    var ResolverBatch = Resolvers.Prepare(File.RequiredResolverMask);
                    var MechanicalBatch = File.RequiredMechanicalHazardMask == 0
                        ? null : MechanicalSources.Prepare(File.RequiredMechanicalHazardMask);
                    var VineBatch = File.RequiredNativeVineMask == 0 ? null : VineSources.Prepare(File.RequiredNativeVineMask);
                    var VisualBatch = Visuals.Prepare(
                        File.RequiredVisualMask, File.RequiredParticleMask, File.RequiresNativeFog,
                        File.RequiredEffectParticleMask);
                    var BeetleProvider = File.RequiresNativeBeetles
                        ? (Beetles ??= new Lazy<NativeBeetleSource>(
                            () => NativeBeetleSource.Capture(NativeCatalog), System.Threading.LazyThreadSafetyMode.None)).Value
                        : null;
                    var ZombieProvider = File.RequiresNativeZombies
                        ? (Zombies ??= new Lazy<NativeZombieSource>(() => NativeZombieSource.Capture(NativeCatalog), System.Threading.LazyThreadSafetyMode.None)).Value : null;
                    var CondorProvider = File.RequiresNativeCondors
                        ? (Condors ??= new Lazy<NativeCondorSource>(() => PendingCondors = NativeCondorSource.Capture(NativeCatalog), System.Threading.LazyThreadSafetyMode.None)).Value : null;
                    var TornadoProvider = File.RequiresNativeTornados
                        ? (Tornados ??= new Lazy<NativeTornadoSource>(() => NativeTornadoSource.Capture(NativeCatalog), System.Threading.LazyThreadSafetyMode.None)).Value : null;
                    var TumbleweedProvider = File.RequiresNativeTumbleweeds
                        ? (Tumbleweeds ??= new Lazy<NativeTumbleweedSource>(() => NativeTumbleweedSource.Capture(NativeCatalog), System.Threading.LazyThreadSafetyMode.None)).Value : null;
                    var RopeProvider = File.RequiresNativeRopes
                        ? (Ropes ??= new Lazy<NativeHangingRopeSource>(NativeHangingRopeSource.Capture,
                            System.Threading.LazyThreadSafetyMode.None)).Value : null;
                    var ToolProvider = File.RequiresNativeMountedTools
                        ? (MountedTools ??= new Lazy<NativeMountedToolSource>(() => NativeMountedToolSource.Capture(NativeCatalog),
                            System.Threading.LazyThreadSafetyMode.None)).Value : null;
                    if (NativeFailure is not null && File.NeedsNativeCatalog)
                        throw new InvalidOperationException(
                            "PEAK's canonical native luggage catalog is unavailable.", NativeFailure);
                    foreach (var Contract in File.Authoring)
                    {
                        Contract.ValidateSource();
                        Contract.ResolveExternal(Items!, Native, VisualBatch,
                            File.RequiresNativeMaterials ? Visuals.Materials : null);
                        Contract.ResolveEntitySources(BeetleProvider, ZombieProvider, CondorProvider);
                        Contract.ResolveHazardSources(TornadoProvider, TumbleweedProvider);
                        Contract.ResolveMechanicalHazards(MechanicalBatch);
                        Contract.ResolveVineSources(VineBatch);
                        Contract.ResolveRopeSource(RopeProvider, ToolProvider);
                        Contract.ResolveAmuletSource(NativeCatalog.RequireOrdinary("Beach").NadirAmulets);
                    }
                    NativeSpawnCatalog.SealBatch(File.FinalForces);
                    var Fingerprint = CreateExternalFingerprint(File, ResolverBatch, VisualBatch,
                        BeetleProvider, ZombieProvider, CondorProvider, TornadoProvider, TumbleweedProvider,
                        RopeProvider, MechanicalBatch, ToolProvider, VineBatch);
                    if (File.ExternalFingerprint is { } Expected
                        && !DeterministicObjectIdentity.SameHash(Expected, Fingerprint))
                        throw new InvalidOperationException(
                            $"PLL content bundle '{File.Path}' changed its live external compatibility fingerprint.");
                    Resolvers.Commit(ResolverBatch);
                    Visuals.Commit(VisualBatch);
                    File.ExternalFingerprint ??= Fingerprint;
                    RequiredResolverMask |= ResolverBatch.RequiredMask;
                    RequiredVisualMask |= VisualBatch.RequiredMask;
                    RequiredParticleMask |= VisualBatch.RequiredParticleMask;
                    RequiredEffectParticleMask |= VisualBatch.RequiredEffectParticleMask;
                    index++;
                }
                catch (Exception Failure)
                {
                    var Rollback = Phase == RegistrationPhase.FinalSealed
                        ? RollbackScope.FinalSeal
                        : RollbackScope.InitialSeal;
                    var Cleanup = File.CollectRollbackFailures(Rollback);
                    if (Cleanup is not null)
                    {
                        Cleanup.Insert(0, Failure);
                        throw new AggregateException(
                            $"PLL content bundle '{File.Path}' failed live compatibility sealing.", Cleanup);
                    }
                    if (Rollback == RollbackScope.FinalSeal) throw;
                    RetainedFiles.RemoveAt(index);
                    Plugin.PLLLog.LogError(
                        $"{BundleResult(File.BundleSha256, "live", "rejected")}: Rejected PLL content bundle '{File.Path}' during live compatibility sealing: {Failure}");
                }
            }

            if (ExportContent)
            {
                var BeetleSource = (Beetles ??= new Lazy<NativeBeetleSource>(
                    () => NativeBeetleSource.Capture(NativeCatalog), System.Threading.LazyThreadSafetyMode.None)).Value;
                var ZombieSource = (Zombies ??= new Lazy<NativeZombieSource>(
                    () => NativeZombieSource.Capture(NativeCatalog), System.Threading.LazyThreadSafetyMode.None)).Value;
                var CondorSource = (Condors ??= new Lazy<NativeCondorSource>(
                    () => PendingCondors = NativeCondorSource.Capture(NativeCatalog), System.Threading.LazyThreadSafetyMode.None)).Value;
                var TornadoSource = (Tornados ??= new Lazy<NativeTornadoSource>(
                    () => NativeTornadoSource.Capture(NativeCatalog), System.Threading.LazyThreadSafetyMode.None)).Value;
                var TumbleweedSource = (Tumbleweeds ??= new Lazy<NativeTumbleweedSource>(
                    () => NativeTumbleweedSource.Capture(NativeCatalog), System.Threading.LazyThreadSafetyMode.None)).Value;
                NativeContentTools.CaptureRig("Beetle", BeetleSource.SkinDescriptor);
                NativeContentTools.CaptureRig("Condor", CondorSource.SkinDescriptor);
                var RopeSource = (Ropes ??= new Lazy<NativeHangingRopeSource>(NativeHangingRopeSource.Capture,
                    System.Threading.LazyThreadSafetyMode.None)).Value;
                NativeContentTools.CaptureRig("RopeDynamic", RopeSource.SkinDescriptor);
                NativeContentTools.CaptureRecipe(NativeContentPickKind.Actor, typeof(PllBeetleZoneSource).FullName!,
                    "Beetle", NativeContentTools.Fingerprint(BeetleSource.WriteManifest));
                NativeContentTools.CaptureRecipe(NativeContentPickKind.Actor, typeof(PllMushroomZombieZoneSource).FullName!,
                    "Mushroom Zombie", NativeContentTools.Fingerprint(ZombieSource.WriteManifest));
                NativeContentTools.CaptureRecipe(NativeContentPickKind.Actor, typeof(PllCondorZoneSource).FullName!,
                    "Condor", NativeContentTools.Fingerprint(CondorSource.WriteManifest));
                NativeContentTools.CaptureRecipe(NativeContentPickKind.Actor, typeof(PllTornadoZoneSource).FullName!,
                    "Tornado", NativeContentTools.Fingerprint(TornadoSource.WriteManifest));
                NativeContentTools.CaptureRecipe(NativeContentPickKind.Actor, typeof(PllTumbleweedZoneSource).FullName!,
                    "Tumbleweed", NativeContentTools.Fingerprint(TumbleweedSource.WriteManifest));
                var ResolverSources = Resolvers.Prepare(NativeResolverSourceCatalog.Seal.SupportedMask);
                for (var Value = (int)PLLNativeResolverKind.Rain; Value <= (int)PLLNativeResolverKind.SporeCloud; Value++)
                {
                    var Kind = (PLLNativeResolverKind)Value;
                    var Name = Kind.ToString();
                    NativeContentTools.CaptureRecipe(NativeContentPickKind.Resolver, Name, Name,
                        NativeContentTools.Fingerprint(Kind == PLLNativeResolverKind.SporeCloud
                            ? ResolverSources.SporeCloud().WriteManifest : ResolverSources.Weather(Kind).WriteManifest));
                }
                var VisualSources = Visuals.Prepare(NativeVisualCatalog.SupportedMask, 0, false);
                for (var Value = (int)PllNativeVisualPreset.Beach; Value <= (int)PllNativeVisualPreset.Nadir; Value++)
                {
                    var Preset = (PllNativeVisualPreset)Value;
                    var Name = Preset.ToString();
                    NativeContentTools.CaptureRecipe(NativeContentPickKind.Visual, Name, Name,
                        NativeContentTools.Fingerprint(Writer => VisualSources.Require(Preset).WriteManifest(Writer, false)));
                }
                NativeContentTools.CaptureForceContent();
            }
            NativeSpawnCatalog.CompleteSeal();
            NativeResolverSourceCatalog.CompleteSeal(Resolvers, RequiredResolverMask);
            NativeFogRuntime.Publish(
                Visuals.Complete(RequiredVisualMask, RequiredParticleMask, RequiredEffectParticleMask).Fog);
            foreach (var File in RetainedFiles)
                if (File.RequiresFogRendering)
                {
                    Visuals.PrepareFogRendering(RetainedFiles.SelectMany(File => File.Authoring)
                        .SelectMany(Contract => Contract.FogShaderAdapters));
                    break;
                }
            NativeVisualOwner = Visuals;
            PendingVisuals = null;
            NativeCondorOwner = PendingCondors;
            PendingCondors = null;
            NativeLuggageCatalog.Publish(Native);
            if (Phase == RegistrationPhase.FinalSealed)
            {
                NativeContentTools.AcceptLiveCatalog();
                return;
            }
            var Fingerprints = new byte[RetainedFiles.Count][];
            for (var i = 0; i < Fingerprints.Length; i++)
                Fingerprints[i] = RetainedFiles[i].ExternalFingerprint
                    ?? throw new InvalidOperationException(
                        $"PLL content bundle '{RetainedFiles[i].Path}' has no external compatibility fingerprint.");
            Array.Sort(Fingerprints, static (Left, Right) => Left.AsSpan().SequenceCompareTo(Right));
            SegmentRegistry.PublishFinal(Fingerprints);
            BiomeAuthoringCatalog.SealRegistry();
            CustomBiomeConfigRegistry.SealRegistry();
            Phase = RegistrationPhase.FinalSealed;
            NativeContentTools.AcceptLiveCatalog();
            foreach (var File in RetainedFiles)
                Plugin.PLLLog.LogInfo(BundleResult(File.BundleSha256, "live", "accepted"));
        }
        catch (Exception Failure)
        {
            foreach (var File in RetainedFiles)
                Plugin.PLLLog.LogError(BundleResult(File.BundleSha256, "live", "rejected"));
            NativeContentTools.StartupFailed(StartupTicket, Failure);
            ClearLiveCatalogs();
            throw;
        }
        finally
        {
            try { PendingVisuals?.Release(); }
            finally { PendingCondors?.Release(); }
        }
    }

    internal static void ClearLiveCatalogs()
    {
        NativeLuggageCatalog.Clear();
        BiomeAuthoringCatalog.ClearExternal();
        ReleaseNativeCondors();
        NativeSpawnCatalog.Clear();
        NativeResolverSourceCatalog.Clear();
        NativeFogRuntime.Clear();
        var VisualOwner = NativeVisualOwner;
        NativeVisualOwner = null;
        VisualOwner?.Release();
    }

    static void ReleaseNativeCondors()
    {
        var Owner = NativeCondorOwner;
        NativeCondorOwner = null;
        Owner?.Release();
    }

    static byte[] CreateExternalFingerprint(
        ContentFile File,
        NativeResolverSourceCatalog.Batch Resolvers,
        NativeVisualSources Visuals, NativeBeetleSource? Beetles, NativeZombieSource? Zombies,
        NativeCondorSource? Condors, NativeTornadoSource? Tornados, NativeTumbleweedSource? Tumbleweeds,
        NativeHangingRopeSource? Ropes, NativeMechanicalHazards? Mechanical, NativeMountedToolSource? MountedTool,
        NativeVineRecipes? Vines)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, new UTF8Encoding(false, true), true))
        {
            Writer.Write(ExternalFingerprintProtocol);
            File.Registration.WriteGroupManifest(Writer);
            Writer.Write(File.Authoring.Length);
            foreach (var Contract in File.Authoring) Contract.WriteManifest(Writer);
            Writer.Write(File.FinalForces.Length);
            foreach (var Contract in File.FinalForces) Contract.WriteExternalFingerprint(Writer);
            Writer.Write(Resolvers.RequiredMask);
            if (Resolvers.RequiredMask != 0) Resolvers.WriteManifest(Writer);
            Visuals.WriteManifest(Writer);
            var Fog = Visuals.Fog;
            if (File.RequiresNativeFog != (Fog is not null))
                throw new InvalidOperationException(
                    "A content file's native fog source does not match its sealed requirements.");
            Writer.Write(File.RequiresNativeFog);
            Fog?.WriteManifest(Writer);
            if (File.RequiresFogRendering) NativeFogRenderRuntime.WriteRenderManifest(Writer);
            if (Beetles is not null)
            {
                Writer.Write("PLL/NativeBeetle/v1"); Beetles.WriteManifest(Writer);
            }
            Zombies?.WriteManifest(Writer); Condors?.WriteManifest(Writer);
            Tornados?.WriteManifest(Writer); Tumbleweeds?.WriteManifest(Writer);
            Ropes?.WriteManifest(Writer);
            Mechanical?.WriteManifest(Writer);
            MountedTool?.WriteManifest(Writer);
            Vines?.WriteManifest(Writer);
        }
        using var Sha = SHA256.Create();
        return Sha.ComputeHash(Stream.GetBuffer(), 0, checked((int)Stream.Length));
    }

}
