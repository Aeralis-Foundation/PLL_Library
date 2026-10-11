using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal static class PllCreatorSetupTransaction
    {
        internal const string AwaitingCompilation = "awaitingCompilation";
        internal const string RollingBack = "rollingBack";
        const int PendingSchema = 7;
        const string CleaningAbortedSetup = "cleaningAbortedSetup";
        const string CleaningCompletedRollback = "cleaningCompletedRollback";
        const string CleaningCompletedSetup = "cleaningCompletedSetup";
        const string InstallingAssets = "installingAssets";
        const string PublishingStagedAssets = "publishingStagedAssets";

        internal static bool TryStart(string PeakRoot, out string Failure)
        {
            if (PllCreatorSetupStore.PendingExists())
            {
                Failure = "PLL Creator setup is already pending. "
                    + "If Unity cannot resume it, use the Creator version that started it.";
                return false;
            }
            PllCreatorSetupPlan Plan;
            if (!TryPrepare(PeakRoot, out Plan, out Failure)) return false;
            var TransactionId = Guid.NewGuid().ToString("N");
            var TransactionRoot = Path.Combine(PllCreatorEnvironment.TransactionRootPath(), TransactionId);
            var Pending = new PllCreatorPendingSetup
            {
                schemaVersion = PendingSchema,
                state = InstallingAssets,
                transactionId = TransactionId,
                transactionRoot = TransactionRoot,
                stagedRoot = Path.Combine(TransactionRoot, "new-assets"),
                previousAssetsRoot = Path.Combine(TransactionRoot, "previous-assets"),
                previousAssetsMeta = Path.Combine(TransactionRoot, "previous-assets.meta"),
                ProjectLayers = Plan.ProjectLayers,
                deadlineUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks,
                failure = string.Empty
            };
            var TransactionPublished = false;
            var MutationStarted = false;
            try
            {
                Directory.CreateDirectory(TransactionRoot);
                BuildStage(Pending, Plan);
                CapturePreviousState(Pending);
                PllCreatorSetupStore.SavePending(Pending);
                TransactionPublished = true;
                BackupPreviousState(Pending, ref MutationStarted);
                PllCreatorProjectLayers.Install(Pending.ProjectLayers, ref MutationStarted);
                BeginStagedAssetPublication(Pending, ref MutationStarted);
                Failure = string.Empty;
                return true;
            }
            catch (Exception SetupFailure) when (ExpectedSetupFailure(SetupFailure))
            {
                Failure = CollectStartFailure(Pending, TransactionPublished, MutationStarted, SetupFailure);
                return false;
            }
        }

        internal static bool Continue(out string Failure, out bool SetupCompleted)
        {
            SetupCompleted = false;
            PllCreatorPendingSetup Pending;
            if (!TryReadPending(out Pending, out Failure)) return PllCreatorSetupStore.PendingExists();
            if (TerminalCleanupState(Pending.state))
            {
                ContinueTerminalCleanup(Pending, out Failure, out SetupCompleted);
                return true;
            }
            if (string.Equals(Pending.state, PublishingStagedAssets, StringComparison.Ordinal))
            {
                try
                {
                    var MutationStarted = false;
                    InstallStagedAssets(Pending, ref MutationStarted);
                }
                catch (Exception PublicationFailure) when (ExpectedSetupFailure(PublicationFailure))
                {
                    BeginRollback(Pending, "The generated Assets provider could not be published: "
                        + PublicationFailure.Message);
                }
                Failure = string.Empty;
                return true;
            }
            if (string.Equals(Pending.state, RollingBack, StringComparison.Ordinal))
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    PllCreatorCompilation.ScheduleResume();
                    Failure = string.Empty;
                    return true;
                }
                try
                {
                    RestorePreviousState(Pending);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    string RestoreFailure;
                    if (!PreviousStateRestored(Pending, out RestoreFailure))
                    {
                        Failure = Pending.failure + " Rollback is incomplete: " + RestoreFailure;
                        return true;
                    }
                    Pending.state = CleaningCompletedRollback;
                    Pending.deadlineUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
                    PllCreatorSetupStore.SavePending(Pending);
                    ContinueTerminalCleanup(Pending, out Failure, out SetupCompleted);
                    return true;
                }
                catch (Exception RollbackFailure) when (ExpectedSetupFailure(RollbackFailure))
                {
                    Failure = Pending.failure + " Rollback is incomplete: " + RollbackFailure.Message;
                    if (DateTime.UtcNow.Ticks <= Pending.deadlineUtcTicks)
                        PllCreatorCompilation.ScheduleResume();
                    return true;
                }
            }
            if (string.Equals(Pending.state, InstallingAssets, StringComparison.Ordinal))
            {
                BeginRollback(Pending,
                    "PLL Creator setup was interrupted before generated Assets publication.");
                Failure = string.Empty;
                return true;
            }
            if (!string.Equals(Pending.state, AwaitingCompilation, StringComparison.Ordinal))
            {
                BeginRollback(Pending, "The pending PLL Creator setup state is invalid.");
                Failure = string.Empty;
                return true;
            }
            var CompilationStatus = PllCreatorCompilation.Continue(Pending, out Failure);
            if (CompilationStatus == PllCreatorCompilationStatus.Ready)
            {
                CompleteSetup(Pending, out Failure, out SetupCompleted);
                return true;
            }
            if (CompilationStatus == PllCreatorCompilationStatus.Failed)
            {
                BeginRollback(Pending, Failure);
                Failure = string.Empty;
            }
            return true;
        }

        internal static bool TryReadPending(out PllCreatorPendingSetup Pending, out string Failure)
        {
            if (!PllCreatorSetupStore.TryLoadPending(out Pending, out Failure)) return false;
            try
            {
                if (Pending.schemaVersion != PendingSchema)
                    throw new ArgumentException("The pending record schema is incompatible.");
                var ValidTransaction = PllCreatorSetupStore.ValidTransactionId(Pending.transactionId);
                var ExpectedRoot = ValidTransaction
                    ? Path.Combine(PllCreatorEnvironment.TransactionRootPath(), Pending.transactionId) : string.Empty;
                var Terminal = TerminalCleanupState(Pending.state);
                var KnownState = Terminal
                    || string.Equals(Pending.state, InstallingAssets, StringComparison.Ordinal)
                    || string.Equals(Pending.state, PublishingStagedAssets, StringComparison.Ordinal)
                    || string.Equals(Pending.state, AwaitingCompilation, StringComparison.Ordinal)
                    || string.Equals(Pending.state, RollingBack, StringComparison.Ordinal);
                var FailedState = string.Equals(Pending.state, RollingBack, StringComparison.Ordinal)
                    || string.Equals(Pending.state, CleaningCompletedRollback, StringComparison.Ordinal)
                    || string.Equals(Pending.state, CleaningAbortedSetup, StringComparison.Ordinal);
                if (!ValidTransaction || !KnownState
                    || !PllCreatorFileSystem.SamePath(Pending.transactionRoot, ExpectedRoot)
                    || !PllCreatorFileSystem.SamePath(Pending.stagedRoot, Path.Combine(ExpectedRoot, "new-assets"))
                    || !PllCreatorFileSystem.SamePath(
                        Pending.previousAssetsRoot, Path.Combine(ExpectedRoot, "previous-assets"))
                    || !PllCreatorFileSystem.SamePath(
                        Pending.previousAssetsMeta, Path.Combine(ExpectedRoot, "previous-assets.meta"))
                    || Pending.hadPreviousAssets
                    && (!PllCreatorSetupStore.ValidTransactionId(Pending.previousAssetsTransactionId)
                        || !PllCreatorSetupStore.ValidSha256(Pending.previousAssetsTreeSha256)
                        || !PllCreatorSetupStore.ValidSha256(Pending.previousAssetsMetaSha256))
                    || !Pending.hadPreviousAssets && (!string.IsNullOrEmpty(Pending.previousAssetsTransactionId)
                        || !string.IsNullOrEmpty(Pending.previousAssetsTreeSha256)
                        || !string.IsNullOrEmpty(Pending.previousAssetsMetaSha256))
                    || FailedState && string.IsNullOrWhiteSpace(Pending.failure)
                    || !FailedState && !string.IsNullOrEmpty(Pending.failure)
                    || !ValidGeneratedPublication(Pending))
                    throw new ArgumentException("The pending record has an unsupported shape.");
                string PackageRoot;
                PllCreatorFacadeContract Contract;
                if (!PllCreatorEnvironment.TryResolvePackage(out PackageRoot, out _, out Failure)
                    || !PllCreatorFacadeContract.TryLoad(PackageRoot, out Contract, out Failure))
                {
                    Pending = null;
                    return false;
                }
                if (!PllCreatorProjectLayers.MatchesContract(Pending.ProjectLayers, Contract.layers))
                    throw new ArgumentException("The pending project layers do not match the active facade contract.");
                Failure = string.Empty;
                return true;
            }
            catch (Exception ValidationFailure) when (ValidationFailure is IOException
                                                       || ValidationFailure is UnauthorizedAccessException
                                                       || ValidationFailure is ArgumentException
                                                       || ValidationFailure is SecurityException)
            {
                Pending = null;
                Failure = "The pending PLL Creator setup record is invalid: " + ValidationFailure.Message;
                return false;
            }
        }

        static bool TryPrepare(string PeakRoot, out PllCreatorSetupPlan Plan, out string Failure)
        {
            Plan = null;
            string PackageRoot;
            string PackageVersion;
            if (!PllCreatorEnvironment.TryResolvePackage(out PackageRoot, out PackageVersion, out Failure)) return false;
            PllCreatorFacadeContract Contract;
            if (!PllCreatorFacadeContract.TryLoad(PackageRoot, out Contract, out Failure)) return false;
            PllCreatorPendingLayer[] ProjectLayers;
            if (!PllCreatorProjectLayers.TryInspect(Contract.layers, out ProjectLayers, out Failure)) return false;
            var ManagedPath = Path.Combine(PeakRoot, "PEAK_Data", "Managed");
            string SourcePath;
            string[] Dependencies;
            if (!Contract.TryValidatePeak(ManagedPath, out SourcePath, out Dependencies, out Failure)) return false;
            var SdkPath = PllCreatorEnvironment.PackageSdkPath(PackageRoot);
            AssemblyName SdkIdentity;
            try
            {
                if (!File.Exists(SdkPath))
                {
                    Failure = "The installed package has no valid creator SDK payload.";
                    return false;
                }
                SdkIdentity = AssemblyName.GetAssemblyName(SdkPath);
                if (!string.Equals(SdkIdentity.Name, "PLL_Library", StringComparison.Ordinal))
                {
                    Failure = "The installed package has no valid creator SDK payload.";
                    return false;
                }
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                               || Exception is SecurityException || Exception is ArgumentException
                                               || Exception is BadImageFormatException || Exception is FileLoadException)
            {
                Failure = "The installed creator SDK payload is invalid: " + Exception.Message;
                return false;
            }
            PllCreatorProviderInventory Inventory;
            PllCreatorAssemblyPlan[] DependencyPlans;
            if (!PllCreatorProviderInventory.TryCreate(PllCreatorEnvironment.GeneratedAssetRoot,
                    PllCreatorEnvironment.GeneratedRootPath(), out Inventory, out Failure)
                || !Inventory.TryPlanDependencies(Dependencies, out DependencyPlans, out Failure)
                || !Inventory.NoConflicts(new[] { Contract.sourceAssembly, SdkIdentity.Name },
                    Contract.sourceAssembly, out Failure)) return false;
            Plan = new PllCreatorSetupPlan(PackageRoot, PackageVersion, PeakRoot, ManagedPath,
                SourcePath, SdkPath, DependencyPlans, Contract, ProjectLayers);
            Failure = string.Empty;
            return true;
        }

        static string CollectStartFailure(
            PllCreatorPendingSetup Pending, bool TransactionPublished,
            bool MutationStarted, Exception SetupFailure)
        {
            Pending.failure = SetupFailure.Message;
            try
            {
                if (TransactionPublished && MutationStarted) BeginRollback(Pending, Pending.failure);
                else if (TransactionPublished) CompleteAbortedSetup(Pending);
                else
                {
                    PllCreatorSetupStore.DeletePending();
                    DeleteTransaction(Pending);
                }
                return Pending.failure;
            }
            catch (Exception RollbackFailure) when (ExpectedSetupFailure(RollbackFailure))
            {
                return Pending.failure + " Rollback failed: " + RollbackFailure.Message;
            }
        }

        static void CapturePreviousState(PllCreatorPendingSetup Pending)
        {
            var Target = PllCreatorEnvironment.GeneratedRootPath();
            var TargetMeta = Target + ".meta";
            Pending.hadPreviousAssets = Directory.Exists(Target);
            if (Pending.hadPreviousAssets != File.Exists(TargetMeta))
                throw new PllCreatorSetupException(
                    "Assets/PLL.Creator.Generated and its metadata must either both exist or both be absent.");
            if (!Pending.hadPreviousAssets) return;
            if (!PllCreatorSetupStore.TryReadOwnershipTransaction(
                    Target, out Pending.previousAssetsTransactionId))
                throw new PllCreatorSetupException("The previous generated Assets owner marker is invalid.");
            Pending.previousAssetsTreeSha256 = PllCreatorInstallation.InspectDirectorySha256(Target);
            Pending.previousAssetsMetaSha256 = PllCreatorFileSystem.Sha256(TargetMeta);
        }

        static void BackupPreviousState(PllCreatorPendingSetup Pending, ref bool MutationStarted)
        {
            if (!Pending.hadPreviousAssets) return;
            var Target = PllCreatorEnvironment.GeneratedRootPath();
            Directory.Move(Target, Pending.previousAssetsRoot);
            MutationStarted = true;
            if (!PreviousAssetsMatch(Pending.previousAssetsRoot, Pending))
                throw new PllCreatorSetupException(
                    "The previous generated Assets backup changed during capture.");
            File.Move(Target + ".meta", Pending.previousAssetsMeta);
            if (!PllCreatorSetupStore.FileSha256Matches(
                    Pending.previousAssetsMeta, Pending.previousAssetsMetaSha256))
                throw new PllCreatorSetupException(
                    "The previous generated Assets metadata backup changed during capture.");
        }

        static void BeginStagedAssetPublication(
            PllCreatorPendingSetup Pending, ref bool MutationStarted)
        {
            if (!PllCreatorSetupStore.HasOwnership(Pending.stagedRoot, Pending.transactionId))
                throw new PllCreatorSetupException(
                    "The staged generated Assets owner changed before publication.");
            Pending.state = PublishingStagedAssets;
            Pending.generatedMetaPrepared = false;
            Pending.stagedAssetsTreeSha256 = PllCreatorInstallation.InspectDirectorySha256(Pending.stagedRoot);
            Pending.deadlineUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
            PllCreatorSetupStore.SavePending(Pending);
            InstallStagedAssets(Pending, ref MutationStarted);
        }

        static void InstallStagedAssets(PllCreatorPendingSetup Pending, ref bool MutationStarted)
        {
            var Target = PllCreatorEnvironment.GeneratedRootPath();
            var TargetMeta = Target + ".meta";
            var StagedMeta = Pending.stagedRoot + ".meta";
            var ExpectedMeta = PllCreatorMetadata.Folder("PLL Creator generated Assets root");
            if (!Pending.generatedMetaPrepared)
            {
                if (!PllCreatorSetupStore.ValidSha256(Pending.stagedAssetsTreeSha256)
                    || !PllCreatorSetupStore.HasOwnership(Pending.stagedRoot, Pending.transactionId)
                    || !string.Equals(PllCreatorInstallation.InspectDirectorySha256(Pending.stagedRoot),
                        Pending.stagedAssetsTreeSha256, StringComparison.Ordinal))
                    throw new PllCreatorSetupException("The staged generated Assets tree changed.");
                if (Directory.Exists(Target) || File.Exists(TargetMeta))
                    throw new PllCreatorSetupException(
                        "Generated Assets or its metadata appeared before staged publication.");
                if (File.Exists(StagedMeta))
                {
                    if (!PllCreatorSetupStore.TextMatches(StagedMeta, ExpectedMeta))
                        throw new PllCreatorSetupException(
                            "The staged generated Assets metadata changed.");
                }
                else
                    PllCreatorSetupStore.WriteTextAtomically(StagedMeta, ExpectedMeta);
                Pending.generatedMetaPrepared = true;
                PllCreatorSetupStore.SavePending(Pending);
            }
            var StagedMetaExists = File.Exists(StagedMeta);
            var TargetMetaExists = File.Exists(TargetMeta);
            if (StagedMetaExists == TargetMetaExists
                || StagedMetaExists && !PllCreatorSetupStore.TextMatches(StagedMeta, ExpectedMeta)
                || TargetMetaExists && !PllCreatorSetupStore.TextMatches(TargetMeta, ExpectedMeta))
                throw new PllCreatorSetupException("Generated Assets metadata publication is inconsistent.");
            var StagedExists = Directory.Exists(Pending.stagedRoot);
            var TargetExists = Directory.Exists(Target);
            var StagedExact = StagedExists
                && PllCreatorSetupStore.HasOwnership(Pending.stagedRoot, Pending.transactionId)
                && string.Equals(PllCreatorInstallation.InspectDirectorySha256(Pending.stagedRoot),
                    Pending.stagedAssetsTreeSha256, StringComparison.Ordinal);
            var TargetExact = TargetExists && PllCreatorSetupStore.HasOwnership(Target, Pending.transactionId)
                && string.Equals(PllCreatorInstallation.InspectDirectorySha256(Target),
                    Pending.stagedAssetsTreeSha256, StringComparison.Ordinal);
            if (StagedExists && TargetExists)
            {
                if (StagedMetaExists || !TargetMetaExists || !StagedExact
                    || Directory.GetFileSystemEntries(Target).Length != 0)
                    throw new PllCreatorSetupException(
                        "Generated Assets directory publication is inconsistent.");
                Directory.Delete(Target, false);
                MutationStarted = true;
                TargetExists = false;
            }
            if (StagedExists == TargetExists || StagedExists && !StagedExact || TargetExists && !TargetExact)
                throw new PllCreatorSetupException("Generated Assets directory publication is inconsistent.");
            if (StagedMetaExists)
            {
                if (TargetExists)
                    throw new PllCreatorSetupException(
                        "Generated Assets became visible before its metadata.");
                File.Move(StagedMeta, TargetMeta);
                MutationStarted = true;
            }
            if (StagedExists)
            {
                if (Directory.Exists(Target))
                    throw new PllCreatorSetupException(
                        "Generated Assets appeared during staged publication.");
                Directory.Move(Pending.stagedRoot, Target);
                MutationStarted = true;
            }
            if (!PllCreatorSetupStore.HasOwnership(Target, Pending.transactionId)
                || !string.Equals(PllCreatorInstallation.InspectDirectorySha256(Target),
                    Pending.stagedAssetsTreeSha256, StringComparison.Ordinal)
                || !PllCreatorSetupStore.TextMatches(TargetMeta, ExpectedMeta))
                throw new PllCreatorSetupException("Generated Assets publication did not complete exactly.");
            PllCreatorCompilation.Begin(Pending);
        }

        static void CompleteAbortedSetup(PllCreatorPendingSetup Pending)
        {
            string Failure;
            if (!PreviousStateRestored(Pending, out Failure))
                throw new PllCreatorSetupException(
                    "The unchanged project state could not be sealed: " + Failure);
            Pending.state = CleaningAbortedSetup;
            Pending.deadlineUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
            PllCreatorSetupStore.SavePending(Pending);
            ContinueTerminalCleanup(Pending, out Failure, out _);
            if (!string.IsNullOrEmpty(Failure)) throw new PllCreatorSetupException(Failure);
        }

        static void CompleteSetup(
            PllCreatorPendingSetup Pending, out string Failure, out bool SetupCompleted)
        {
            SetupCompleted = false;
            if (!PllCreatorProjectLayers.Installed(Pending.ProjectLayers, out Failure))
            {
                BeginRollback(Pending, Failure);
                Failure = string.Empty;
                return;
            }
            PllCreatorGeneratedManifest Manifest;
            if (!PllCreatorSetupStore.TryLoadGeneratedManifest(
                    PllCreatorEnvironment.GeneratedRootPath(), out Manifest, out Failure))
            {
                BeginRollback(Pending, Failure);
                Failure = string.Empty;
                return;
            }
            Manifest.ready = true;
            PllCreatorSetupStore.SaveGeneratedManifest(PllCreatorEnvironment.GeneratedRootPath(), Manifest);
            if (!PllCreatorInstallation.TryInspectGeneratedInstallation(true, out _, out Failure))
            {
                BeginRollback(Pending, Failure);
                Failure = string.Empty;
                return;
            }
            Pending.state = CleaningCompletedSetup;
            Pending.deadlineUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
            PllCreatorSetupStore.SavePending(Pending);
            ContinueTerminalCleanup(Pending, out Failure, out SetupCompleted);
        }

        static void ContinueTerminalCleanup(
            PllCreatorPendingSetup Pending, out string Failure, out bool SetupCompleted)
        {
            SetupCompleted = false;
            try
            {
                FinishTerminalCleanup(Pending, out Failure, out SetupCompleted);
            }
            catch (Exception CleanupFailure) when (ExpectedSetupFailure(CleanupFailure))
            {
                Failure = "PLL Creator terminal cleanup is incomplete: " + CleanupFailure.Message;
                if (DateTime.UtcNow.Ticks <= Pending.deadlineUtcTicks)
                    PllCreatorCompilation.ScheduleResume();
            }
        }

        static void FinishTerminalCleanup(
            PllCreatorPendingSetup Pending, out string Failure, out bool SetupCompleted)
        {
            if (!TerminalStateMatches(Pending, out Failure)) throw new PllCreatorSetupException(Failure);
            DeleteTransaction(Pending);
            SetupCompleted = string.Equals(Pending.state, CleaningCompletedSetup, StringComparison.Ordinal);
            Failure = SetupCompleted ? string.Empty : Pending.failure;
            PllCreatorSetupStore.DeletePending();
        }

        static bool TerminalStateMatches(PllCreatorPendingSetup Pending, out string Failure)
        {
            if (string.Equals(Pending.state, CleaningCompletedSetup, StringComparison.Ordinal))
            {
                if (!PllCreatorProjectLayers.Installed(Pending.ProjectLayers, out Failure)) return false;
                if (!PllCreatorInstallation.TryInspectGeneratedInstallation(true, out _, out Failure))
                {
                    Failure = "The completed generated installation changed before cleanup: " + Failure;
                    return false;
                }
                Failure = string.Empty;
                return true;
            }
            if (!PreviousStateRestored(Pending, out Failure))
            {
                Failure = "The restored project state changed before cleanup: " + Failure;
                return false;
            }
            Failure = string.Empty;
            return true;
        }

        static void BeginRollback(PllCreatorPendingSetup Pending, string Failure)
        {
            Pending.state = RollingBack;
            Pending.failure = string.IsNullOrWhiteSpace(Failure) ? "PLL Creator setup failed." : Failure;
            Pending.deadlineUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
            PllCreatorSetupStore.SavePending(Pending);
            RestorePreviousState(Pending);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PllCreatorCompilation.ScheduleResume();
        }

        static void RestorePreviousState(PllCreatorPendingSetup Pending)
        {
            Exception LayerFailure = null;
            try
            {
                PllCreatorProjectLayers.Restore(Pending.ProjectLayers);
            }
            catch (Exception Failure)
            {
                LayerFailure = Failure;
            }
            Exception AssetsFailure = null;
            try
            {
                RestoreGeneratedAssets(Pending);
            }
            catch (Exception Failure)
            {
                AssetsFailure = Failure;
            }
            if (LayerFailure != null && AssetsFailure != null)
                throw new AggregateException(
                    "Project layers and generated Assets both failed to restore.", LayerFailure, AssetsFailure);
            var RestoreFailure = LayerFailure ?? AssetsFailure;
            if (RestoreFailure != null) ExceptionDispatchInfo.Capture(RestoreFailure).Throw();
        }

        static void RestoreGeneratedAssets(PllCreatorPendingSetup Pending)
        {
            var Target = PllCreatorEnvironment.GeneratedRootPath();
            var PreviousAssetsAvailable = Directory.Exists(Pending.previousAssetsRoot);
            if (PreviousAssetsAvailable && !PreviousAssetsMatch(Pending.previousAssetsRoot, Pending))
                throw new PllCreatorSetupException(
                    "Rollback stopped because the previous generated Assets backup changed.");
            if (Directory.Exists(Target))
            {
                if (Pending.hadPreviousAssets && PreviousAssetsMatch(Target, Pending))
                {
                    if (PreviousAssetsAvailable)
                        throw new PllCreatorSetupException(
                            "Rollback stopped because the previous generated Assets exists in two locations.");
                }
                else if (PllCreatorSetupStore.HasOwnership(Target, Pending.transactionId))
                    Directory.Delete(Target, true);
                else
                    throw new PllCreatorSetupException(
                        "Rollback stopped because generated Assets ownership changed.");
            }
            if (!Directory.Exists(Target) && Pending.hadPreviousAssets)
            {
                if (!PreviousAssetsAvailable)
                    throw new PllCreatorSetupException(
                        "Rollback stopped because the previous generated Assets backup is missing.");
                Directory.Move(Pending.previousAssetsRoot, Target);
            }
            else if (!Pending.hadPreviousAssets && PreviousAssetsAvailable)
                throw new PllCreatorSetupException(
                    "Rollback found an unexpected generated Assets backup.");
            var PreviousMetaAvailable = File.Exists(Pending.previousAssetsMeta);
            if (PreviousMetaAvailable)
            {
                if (!PllCreatorSetupStore.FileSha256Matches(
                        Pending.previousAssetsMeta, Pending.previousAssetsMetaSha256))
                    throw new PllCreatorSetupException(
                        "Rollback stopped because the previous generated Assets metadata backup changed.");
                if (File.Exists(Target + ".meta"))
                {
                    if (!PllCreatorSetupStore.TextMatches(
                            Target + ".meta", PllCreatorMetadata.Folder("PLL Creator generated Assets root")))
                        throw new PllCreatorSetupException(
                            "Rollback stopped because generated Assets metadata ownership changed.");
                    File.Delete(Target + ".meta");
                }
                File.Move(Pending.previousAssetsMeta, Target + ".meta");
            }
            else if (Pending.hadPreviousAssets)
            {
                if (!PllCreatorSetupStore.FileSha256Matches(
                        Target + ".meta", Pending.previousAssetsMetaSha256))
                    throw new PllCreatorSetupException(
                        "Rollback stopped because the previous generated Assets metadata is missing or changed.");
            }
            else if (File.Exists(Target + ".meta"))
            {
                if (!PllCreatorSetupStore.TextMatches(
                        Target + ".meta", PllCreatorMetadata.Folder("PLL Creator generated Assets root")))
                    throw new PllCreatorSetupException(
                        "Rollback stopped because generated Assets metadata ownership changed.");
                File.Delete(Target + ".meta");
            }
            string Failure;
            if (!GeneratedAssetsRestored(Pending, out Failure)) throw new PllCreatorSetupException(Failure);
        }

        static bool PreviousStateRestored(PllCreatorPendingSetup Pending, out string Failure)
        {
            return PllCreatorProjectLayers.Restored(Pending.ProjectLayers, out Failure)
                && GeneratedAssetsRestored(Pending, out Failure);
        }

        static bool GeneratedAssetsRestored(PllCreatorPendingSetup Pending, out string Failure)
        {
            var Target = PllCreatorEnvironment.GeneratedRootPath();
            if (Pending.hadPreviousAssets != Directory.Exists(Target)
                || Pending.hadPreviousAssets && !PreviousAssetsMatch(Target, Pending)
                || Directory.Exists(Pending.previousAssetsRoot))
            {
                Failure = "The previous generated Assets directory is not restored exactly.";
                return false;
            }
            if (Pending.hadPreviousAssets != File.Exists(Target + ".meta")
                || Pending.hadPreviousAssets && !PllCreatorSetupStore.FileSha256Matches(
                    Target + ".meta", Pending.previousAssetsMetaSha256)
                || File.Exists(Pending.previousAssetsMeta))
            {
                Failure = "The previous generated Assets metadata is not restored exactly.";
                return false;
            }
            Failure = string.Empty;
            return true;
        }

        static bool PreviousAssetsMatch(string PathValue, PllCreatorPendingSetup Pending)
        {
            try
            {
                return PllCreatorSetupStore.HasOwnership(PathValue, Pending.previousAssetsTransactionId)
                    && string.Equals(PllCreatorInstallation.InspectDirectorySha256(PathValue),
                        Pending.previousAssetsTreeSha256, StringComparison.Ordinal);
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                               || Exception is SecurityException || Exception is ArgumentException)
            {
                return false;
            }
        }

        static void BuildStage(PllCreatorPendingSetup Pending, PllCreatorSetupPlan Plan)
        {
            var StagedRoot = PllCreatorFileSystem.ExtendedPath(Pending.stagedRoot);
            Directory.CreateDirectory(StagedRoot);
            PllCreatorSetupStore.WritePrivateText(Path.Combine(StagedRoot, PllCreatorSetupStore.OwnerFileName),
                PllCreatorSetupStore.OwnerMarker + "\n" + Pending.transactionId + "\n");
            var Files = new List<PllCreatorGeneratedFile>();
            var MetadataByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.Combine(StagedRoot, PllCreatorSetupStore.OwnerFileName + ".meta")] =
                    PllCreatorMetadata.Text("owner marker"),
                [Path.Combine(StagedRoot, PllCreatorSetupStore.SetupFileName + ".meta")] =
                    PllCreatorMetadata.Text("setup manifest")
            };
            var FacadeRoot = Path.Combine(StagedRoot, "Facade");
            Directory.CreateDirectory(FacadeRoot);
            var TemplateRoot = PllCreatorEnvironment.PackageFacadeRootPath(Plan.PackageRoot);
            foreach (var TemplateName in Plan.Contract.TemplateNames)
            {
                var TemplatePath = Path.Combine(TemplateRoot, TemplateName);
                var Name = Path.GetFileNameWithoutExtension(TemplatePath);
                var Destination = Path.Combine(FacadeRoot, Name);
                File.Copy(TemplatePath, Destination);
                MetadataByPath.Add(Destination + ".meta", PllCreatorMetadata.Script("facade/" + Name));
            }
            var PluginRoot = Path.Combine(StagedRoot, "Plugins");
            Directory.CreateDirectory(PluginRoot);
            var SdkDestination = Path.Combine(PluginRoot, "PLL_Library.dll");
            File.Copy(Plan.SdkPath, SdkDestination);
            MetadataByPath.Add(SdkDestination + ".meta", File.ReadAllText(
                PllCreatorEnvironment.PackagePluginMetadataPath(Plan.PackageRoot),
                PllCreatorFileSystem.StrictUtf8));
            var Assemblies = new List<PllCreatorGeneratedAssembly>
            {
                ManagedAssembly("Assembly-CSharp", Plan.SourcePath, string.Empty, string.Empty, string.Empty, false),
                ManagedAssembly("PLL_Library", Plan.SdkPath, "Plugins/PLL_Library.dll",
                    Path.Combine(PllCreatorEnvironment.GeneratedRootPath(), "Plugins", "PLL_Library.dll"),
                    PllCreatorFileSystem.Sha256(Plan.SdkPath), true)
            };
            var DependencyRoot = Path.Combine(PluginRoot, "PEAKReferences");
            Directory.CreateDirectory(DependencyRoot);
            foreach (var DependencyPlan in Plan.Dependencies)
            {
                var Source = DependencyPlan.Source;
                if (DependencyPlan.Candidate != null)
                {
                    Assemblies.Add(ManagedAssembly(Source.Identity.Name, Source.Path,
                        DependencyPlan.Candidate.AssetPath, DependencyPlan.Candidate.FullPath,
                        DependencyPlan.Candidate.Sha256, false));
                    continue;
                }
                var RelativePath = "Plugins/PEAKReferences/" + Path.GetFileName(Source.Path);
                var Destination = Path.Combine(
                    StagedRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
                File.Copy(Source.Path, Destination);
                MetadataByPath.Add(Destination + ".meta",
                    PllCreatorMetadata.Plugin("dependency/" + Source.Identity.Name));
                Assemblies.Add(ManagedAssembly(Source.Identity.Name, Source.Path, RelativePath,
                    Path.Combine(PllCreatorEnvironment.GeneratedRootPath(),
                        RelativePath.Replace('/', Path.DirectorySeparatorChar)), Source.Sha256, true));
            }
            var BuilderSource = Path.Combine(Plan.PackageRoot, "Editor", "Builder");
            var BuilderRoot = Path.Combine(StagedRoot, "Editor", "Builder");
            foreach (var SourcePath in Directory.GetFiles(BuilderSource, "*.cs", SearchOption.AllDirectories))
            {
                var RelativePath = SourcePath.Substring(BuilderSource.Length).TrimStart(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var Destination = Path.Combine(BuilderRoot, RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(Destination));
                File.Copy(SourcePath, Destination);
                MetadataByPath.Add(Destination + ".meta",
                    PllCreatorMetadata.Script("builder/" + RelativePath.Replace('\\', '/')));
            }
            if (!File.Exists(Path.Combine(BuilderRoot, "PllBundleBuilder.cs")))
                throw new PllCreatorSetupException(
                    "The installed package has no builder source payload.");
            foreach (var DirectoryPath in Directory.GetDirectories(StagedRoot, "*", SearchOption.AllDirectories))
            {
                var RelativePath = DirectoryPath.Substring(StagedRoot.Length).TrimStart(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                MetadataByPath.Add(DirectoryPath + ".meta",
                    PllCreatorMetadata.Folder("folder/" + RelativePath));
            }
            WriteStagedMetadata(StagedRoot, MetadataByPath);
            foreach (var FilePath in Directory.GetFiles(StagedRoot, "*", SearchOption.AllDirectories))
            {
                var RelativePath = FilePath.Substring(StagedRoot.Length).TrimStart(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                if (string.Equals(RelativePath,
                        PllCreatorSetupStore.SetupFileName, StringComparison.OrdinalIgnoreCase)) continue;
                Files.Add(new PllCreatorGeneratedFile
                {
                    relativePath = RelativePath,
                    sha256 = PllCreatorFileSystem.Sha256(FilePath)
                });
            }
            var Manifest = new PllCreatorGeneratedManifest
            {
                schemaVersion = PllCreatorGeneratedManifest.SchemaVersion,
                transactionId = Pending.transactionId,
                packageVersion = Plan.PackageVersion,
                peakRoot = Plan.PeakRoot,
                peakManagedPath = Plan.ManagedPath,
                contractSha256 = PllCreatorFileSystem.Sha256(
                    PllCreatorEnvironment.PackageContractPath(Plan.PackageRoot)),
                ready = false,
                files = Files.OrderBy(Value => Value.relativePath, StringComparer.Ordinal).ToArray(),
                managedAssemblies = Assemblies.OrderBy(Value => Value.name, StringComparer.Ordinal).ToArray(),
                scripts = Plan.Contract.scripts.Select(Value => new PllCreatorGeneratedScript
                {
                    typeName = Value.typeName,
                    assetPath = PllCreatorEnvironment.GeneratedAssetRoot + "/Facade/"
                        + Value.template.Substring(0, Value.template.Length - ".template".Length)
                }).ToArray()
            };
            PllCreatorSetupStore.SaveGeneratedManifest(StagedRoot, Manifest);
        }

        static void WriteStagedMetadata(
            string StagedRoot, IReadOnlyDictionary<string, string> MetadataByPath)
        {
            var SetupPath = Path.Combine(StagedRoot, PllCreatorSetupStore.SetupFileName);
            foreach (var RequiredFile in Directory.EnumerateFiles(StagedRoot, "*", SearchOption.AllDirectories))
                if (!RequiredFile.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    && !MetadataByPath.ContainsKey(RequiredFile + ".meta"))
                    throw new PllCreatorSetupException(
                        "Generated asset '" + RequiredFile + "' has no owned metadata plan.");
            foreach (var Entry in MetadataByPath)
            {
                if (!Entry.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    throw new PllCreatorSetupException(
                        "A generated metadata path has an invalid extension.");
                var OwnedPath = Entry.Key.Substring(0, Entry.Key.Length - ".meta".Length);
                if (!File.Exists(OwnedPath) && !Directory.Exists(OwnedPath)
                    && !PllCreatorFileSystem.SamePath(OwnedPath, SetupPath))
                    throw new PllCreatorSetupException(
                        "Generated metadata has no owned asset: " + Entry.Key);
                if (File.Exists(Entry.Key))
                    throw new PllCreatorSetupException(
                        "Generated metadata already exists: " + Entry.Key);
                PllCreatorSetupStore.WritePrivateText(Entry.Key, Entry.Value);
                if (!PllCreatorSetupStore.TextMatches(Entry.Key, Entry.Value))
                    throw new PllCreatorSetupException(
                        "Generated metadata was not written exactly: " + Entry.Key);
            }
        }

        static PllCreatorGeneratedAssembly ManagedAssembly(
            string Name, string SourcePath, string ProviderPath, string ProviderFullPath,
            string ProviderSha256, bool Owned)
        {
            return new PllCreatorGeneratedAssembly
            {
                name = Name,
                sourcePath = SourcePath,
                sourceSha256 = PllCreatorFileSystem.Sha256(SourcePath),
                providerAssetPath = string.IsNullOrEmpty(ProviderPath) || !Owned
                    ? ProviderPath : PllCreatorEnvironment.GeneratedAssetRoot + "/" + ProviderPath,
                providerFullPath = ProviderFullPath,
                providerSha256 = ProviderSha256,
                owned = Owned
            };
        }

        static bool ValidGeneratedPublication(PllCreatorPendingSetup Pending)
        {
            var Publishing = string.Equals(Pending.state, PublishingStagedAssets, StringComparison.Ordinal);
            var Awaiting = string.Equals(Pending.state, AwaitingCompilation, StringComparison.Ordinal);
            if (!Publishing && !Awaiting)
                return TerminalCleanupState(Pending.state)
                        || string.Equals(Pending.state, RollingBack, StringComparison.Ordinal)
                    ? string.IsNullOrEmpty(Pending.stagedAssetsTreeSha256)
                        || PllCreatorSetupStore.ValidSha256(Pending.stagedAssetsTreeSha256)
                    : !Pending.generatedMetaPrepared && string.IsNullOrEmpty(Pending.stagedAssetsTreeSha256);
            if (!PllCreatorSetupStore.ValidSha256(Pending.stagedAssetsTreeSha256)) return false;
            var Target = PllCreatorEnvironment.GeneratedRootPath();
            var ExpectedMeta = PllCreatorMetadata.Folder("PLL Creator generated Assets root");
            var StagedMeta = File.Exists(Pending.stagedRoot + ".meta");
            var TargetMeta = File.Exists(Target + ".meta");
            var Staged = Directory.Exists(Pending.stagedRoot);
            var Installed = Directory.Exists(Target);
            var StagedExact = Staged
                && PllCreatorSetupStore.HasOwnership(Pending.stagedRoot, Pending.transactionId)
                && string.Equals(PllCreatorInstallation.InspectDirectorySha256(Pending.stagedRoot),
                    Pending.stagedAssetsTreeSha256, StringComparison.Ordinal);
            var InstalledExact = Installed && PllCreatorSetupStore.HasOwnership(Target, Pending.transactionId)
                && string.Equals(PllCreatorInstallation.InspectDirectorySha256(Target),
                    Pending.stagedAssetsTreeSha256, StringComparison.Ordinal);
            if (!Pending.generatedMetaPrepared)
                return Publishing && StagedExact && !Installed && !TargetMeta
                    && (!StagedMeta || PllCreatorSetupStore.TextMatches(Pending.stagedRoot + ".meta", ExpectedMeta));
            if (StagedMeta == TargetMeta
                || StagedMeta && !PllCreatorSetupStore.TextMatches(Pending.stagedRoot + ".meta", ExpectedMeta)
                || TargetMeta && !PllCreatorSetupStore.TextMatches(Target + ".meta", ExpectedMeta)) return false;
            if (Awaiting) return !Staged && InstalledExact && TargetMeta;
            if (Staged && Installed)
                return !StagedMeta && TargetMeta && StagedExact
                    && Directory.GetFileSystemEntries(Target).Length == 0;
            if (Staged == Installed) return false;
            return Staged ? StagedExact : InstalledExact;
        }

        static bool TerminalCleanupState(string State)
        {
            return string.Equals(State, CleaningAbortedSetup, StringComparison.Ordinal)
                || string.Equals(State, CleaningCompletedSetup, StringComparison.Ordinal)
                || string.Equals(State, CleaningCompletedRollback, StringComparison.Ordinal);
        }

        static void DeleteTransaction(PllCreatorPendingSetup Pending)
        {
            if (!Directory.Exists(Pending.transactionRoot)) return;
            if (!PllCreatorFileSystem.Inside(
                    Pending.transactionRoot, PllCreatorEnvironment.TransactionRootPath()))
                throw new PllCreatorSetupException(
                    "The transaction path is outside the PLL Creator transaction root.");
            Directory.Delete(Pending.transactionRoot, true);
        }

        static bool ExpectedSetupFailure(Exception Exception)
        {
            if (Exception is AggregateException Aggregate)
                return Aggregate.InnerExceptions.All(ExpectedSetupFailure);
            return Exception is IOException || Exception is UnauthorizedAccessException
                || Exception is SecurityException || Exception is ArgumentException
                || Exception is UnityException || Exception is PllCreatorSetupException;
        }

        sealed class PllCreatorSetupPlan
        {
            internal PllCreatorSetupPlan(
                string PackageRoot, string PackageVersion, string PeakRoot, string ManagedPath,
                string SourcePath, string SdkPath, PllCreatorAssemblyPlan[] Dependencies,
                PllCreatorFacadeContract Contract, PllCreatorPendingLayer[] ProjectLayers)
            {
                this.PackageRoot = PackageRoot;
                this.PackageVersion = PackageVersion;
                this.PeakRoot = PeakRoot;
                this.ManagedPath = ManagedPath;
                this.SourcePath = SourcePath;
                this.SdkPath = SdkPath;
                this.Dependencies = Dependencies;
                this.Contract = Contract;
                this.ProjectLayers = ProjectLayers;
            }

            internal string PackageRoot { get; }
            internal string PackageVersion { get; }
            internal string PeakRoot { get; }
            internal string ManagedPath { get; }
            internal string SourcePath { get; }
            internal string SdkPath { get; }
            internal PllCreatorAssemblyPlan[] Dependencies { get; }
            internal PllCreatorFacadeContract Contract { get; }
            internal PllCreatorPendingLayer[] ProjectLayers { get; }
        }
    }

    internal sealed class PllCreatorSetupException : Exception
    {
        internal PllCreatorSetupException(string Message) : base(Message)
        {
        }
    }
}
