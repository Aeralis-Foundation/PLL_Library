using System;
using System.IO;
using System.Security;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal static class PllCreatorSetupStore
    {
        internal const string OwnerFileName = "owner.txt";
        internal const string OwnerMarker = "PLL Creator generated Assets v1";
        internal const string SetupFileName = "setup.json";
        const int MaximumAtomicWriteAttempts = 5;
        const int AtomicWriteRetryDelayMilliseconds = 50;

        internal static bool PendingExists()
        {
            return File.Exists(PllCreatorEnvironment.PendingPath());
        }

        internal static bool TryLoadPending(out PllCreatorPendingSetup Pending, out string Failure)
        {
            Pending = null;
            var PathValue = PllCreatorEnvironment.PendingPath();
            if (!File.Exists(PathValue))
            {
                Failure = string.Empty;
                return false;
            }
            try
            {
                Pending = JsonUtility.FromJson<PllCreatorPendingSetup>(File.ReadAllText(PathValue, PllCreatorFileSystem.StrictUtf8));
                if (Pending == null) throw new ArgumentException("The pending record is empty.");
                Failure = string.Empty;
                return true;
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                               || Exception is ArgumentException || Exception is SecurityException)
            {
                Pending = null;
                Failure = "The pending PLL Creator setup record is invalid: " + Exception.Message;
                return false;
            }
        }

        internal static void SavePending(PllCreatorPendingSetup Pending)
        {
            var PathValue = PllCreatorEnvironment.PendingPath();
            Directory.CreateDirectory(Path.GetDirectoryName(PathValue));
            WriteJsonAtomically(PathValue, Pending);
        }

        internal static void DeletePending()
        {
            var PathValue = PllCreatorEnvironment.PendingPath();
            if (File.Exists(PathValue)) File.Delete(PathValue);
            if (File.Exists(PathValue + ".tmp")) File.Delete(PathValue + ".tmp");
        }

        internal static bool TryLoadGeneratedManifest(
            string Root, out PllCreatorGeneratedManifest Manifest, out string Failure)
        {
            Manifest = null;
            try
            {
                Manifest = JsonUtility.FromJson<PllCreatorGeneratedManifest>(
                    File.ReadAllText(Path.Combine(Root, SetupFileName), PllCreatorFileSystem.StrictUtf8));
                if (Manifest == null) throw new ArgumentException("The generated Assets manifest is empty.");
                Failure = string.Empty;
                return true;
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                               || Exception is ArgumentException || Exception is SecurityException)
            {
                Failure = "The generated Assets manifest is unreadable: " + Exception.Message;
                return false;
            }
        }

        internal static string InspectExistingPeakRoot()
        {
            PllCreatorGeneratedManifest Manifest;
            return TryLoadGeneratedManifest(PllCreatorEnvironment.GeneratedRootPath(), out Manifest, out _)
                && Directory.Exists(Manifest.peakRoot) ? Manifest.peakRoot : string.Empty;
        }

        internal static void SaveGeneratedManifest(string Root, PllCreatorGeneratedManifest Manifest)
        {
            WriteJsonAtomically(Path.Combine(Root, SetupFileName), Manifest);
        }

        internal static bool HasOwnership(string Root, string TransactionId)
        {
            string Actual;
            return TryReadOwnershipTransaction(Root, out Actual) && (string.IsNullOrEmpty(TransactionId)
                || string.Equals(Actual, TransactionId, StringComparison.Ordinal));
        }

        internal static bool TryReadOwnershipTransaction(string Root, out string TransactionId)
        {
            TransactionId = string.Empty;
            if (string.IsNullOrEmpty(Root) || !Directory.Exists(Root)) return false;
            var OwnerPath = Path.Combine(Root, OwnerFileName);
            if (!File.Exists(OwnerPath)) return false;
            try
            {
                var Lines = File.ReadAllLines(OwnerPath, PllCreatorFileSystem.StrictUtf8);
                if (Lines.Length != 2 || !string.Equals(Lines[0], OwnerMarker, StringComparison.Ordinal)
                    || !ValidTransactionId(Lines[1])) return false;
                TransactionId = Lines[1];
                return true;
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                               || Exception is SecurityException)
            {
                return false;
            }
        }

        internal static bool ValidTransactionId(string Value)
        {
            if (Value == null || Value.Length != 32) return false;
            foreach (var Character in Value)
                if ((Character < '0' || Character > '9') && (Character < 'a' || Character > 'f')) return false;
            return true;
        }

        internal static bool ValidSha256(string Value)
        {
            if (Value == null || Value.Length != 64) return false;
            foreach (var Character in Value)
                if ((Character < '0' || Character > '9') && (Character < 'A' || Character > 'F')) return false;
            return true;
        }

        internal static bool FileSha256Matches(string PathValue, string Expected)
        {
            try
            {
                return File.Exists(PathValue) && string.Equals(
                    PllCreatorFileSystem.Sha256(PathValue), Expected, StringComparison.Ordinal);
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                               || Exception is SecurityException || Exception is ArgumentException)
            {
                return false;
            }
        }

        internal static bool TextMatches(string PathValue, string Expected)
        {
            try
            {
                return File.Exists(PathValue) && string.Equals(
                    File.ReadAllText(PathValue, PllCreatorFileSystem.StrictUtf8), Expected, StringComparison.Ordinal);
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                               || Exception is ArgumentException || Exception is SecurityException)
            {
                return false;
            }
        }

        internal static void WritePrivateText(string PathValue, string Text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathValue));
            File.WriteAllText(PathValue, Text, PllCreatorFileSystem.StrictUtf8);
        }

        internal static void WriteTextAtomically(string PathValue, string Text)
        {
            var TemporaryPath = PathValue + ".tmp";
            File.WriteAllText(TemporaryPath, Text, PllCreatorFileSystem.StrictUtf8);
            ReplaceTemporary(PathValue, TemporaryPath);
        }

        static void WriteJsonAtomically<T>(string PathValue, T Value)
        {
            WriteTextAtomically(PathValue, JsonUtility.ToJson(Value, true) + "\n");
        }

        static void ReplaceTemporary(string DestinationPath, string TemporaryPath)
        {
            for (var Attempt = 1; ; Attempt++)
            {
                try
                {
                    if (File.Exists(DestinationPath)) File.Replace(TemporaryPath, DestinationPath, null);
                    else File.Move(TemporaryPath, DestinationPath);
                    return;
                }
                catch (IOException) when (Attempt < MaximumAtomicWriteAttempts)
                {
                    System.Threading.Thread.Sleep(AtomicWriteRetryDelayMilliseconds);
                }
            }
        }
    }

    [Serializable]
    internal sealed class PllCreatorGeneratedManifest
    {
        internal const int SchemaVersion = 5;
        public int schemaVersion;
        public string transactionId;
        public string packageVersion;
        public string peakRoot;
        public string peakManagedPath;
        public string contractSha256;
        public bool ready;
        public PllCreatorGeneratedFile[] files;
        public PllCreatorGeneratedAssembly[] managedAssemblies;
        public PllCreatorGeneratedScript[] scripts;
    }

    [Serializable]
    internal sealed class PllCreatorGeneratedFile
    {
        public string relativePath;
        public string sha256;
    }

    [Serializable]
    internal sealed class PllCreatorGeneratedAssembly
    {
        public string name;
        public string sourcePath;
        public string sourceSha256;
        public string providerAssetPath;
        public string providerFullPath;
        public string providerSha256;
        public bool owned;
    }

    [Serializable]
    internal sealed class PllCreatorGeneratedScript
    {
        public string typeName;
        public string assetPath;
    }

    [Serializable]
    internal sealed class PllCreatorPendingLayer
    {
        public int Index;
        public string Name;
        public bool PreviouslyPresent;
    }

    [Serializable]
    internal sealed class PllCreatorPendingSetup
    {
        public int schemaVersion;
        public string state;
        public string transactionId;
        public string transactionRoot;
        public string stagedRoot;
        public string previousAssetsRoot;
        public string previousAssetsMeta;
        public bool hadPreviousAssets;
        public string previousAssetsTransactionId;
        public string previousAssetsTreeSha256;
        public string previousAssetsMetaSha256;
        public PllCreatorPendingLayer[] ProjectLayers;
        public bool generatedMetaPrepared;
        public string stagedAssetsTreeSha256;
        public bool compilationFinished;
        public int compilerErrorCount;
        public string firstCompilerError;
        public long deadlineUtcTicks;
        public string failure;
    }
}
