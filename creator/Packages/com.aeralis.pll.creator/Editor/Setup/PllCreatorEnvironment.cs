using System;
using System.IO;
using System.Security;
using UnityEditor.PackageManager;

namespace PLL.Creator.Editor
{
    internal static class PllCreatorEnvironment
    {
        internal const string GeneratedAssetRoot = "Assets/PLL.Creator.Generated";
        const string PackageName = "com.aeralis.pll.creator";
        const string InvalidPeakRootFailure =
            "Select the PEAK directory containing PEAK_Data and UnityPlayer.dll.";

        internal static string PackageContractPath(string PackageRoot)
        {
            return Path.Combine(PackagePayloadRootPath(PackageRoot), "PllPeakFacade.contract.json");
        }

        internal static string PackageFacadeRootPath(string PackageRoot)
        {
            return Path.Combine(PackagePayloadRootPath(PackageRoot), "Facade");
        }

        internal static string PackagePluginMetadataPath(string PackageRoot)
        {
            return Path.Combine(PackagePayloadRootPath(PackageRoot), "PLL_Library.dll.meta.template");
        }

        internal static string PackageSdkPath(string PackageRoot)
        {
            return Path.Combine(PackagePayloadRootPath(PackageRoot), "PLL_Library.dll.bytes");
        }

        internal static string GeneratedRootPath()
        {
            return Path.Combine(PllCreatorFileSystem.ProjectRootPath(),
                GeneratedAssetRoot.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static string PendingPath()
        {
            return Path.Combine(PllCreatorFileSystem.ProjectRootPath(), "Library", "PLL.Creator", "pending.json");
        }

        internal static string TransactionRootPath()
        {
            return Path.Combine(PllCreatorFileSystem.ProjectRootPath(), "Library", "PLL.Creator", "Transactions");
        }

        static string PackagePayloadRootPath(string PackageRoot)
        {
            return Path.Combine(PackageRoot, "Editor", "Setup", "Payload");
        }

        internal static bool TryResolvePackage(out string Root, out string Version, out string Failure)
        {
            var Package = PackageInfo.FindForAssembly(typeof(PllCreatorEnvironment).Assembly);
            if (Package == null || !string.Equals(Package.name, PackageName, StringComparison.Ordinal))
            {
                Root = string.Empty;
                Version = string.Empty;
                Failure = "Unity could not resolve the installed " + PackageName + " package.";
                return false;
            }
            Root = Package.resolvedPath;
            Version = Package.version;
            Failure = string.Empty;
            return true;
        }

        internal static bool TryNormalizePeakRoot(string Selected, out string Root, out string Failure)
        {
            try
            {
                Root = Path.GetFullPath(Selected.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var SelectedName = Path.GetFileName(Root);
                var ManagedSelected = string.Equals(SelectedName, "Managed", StringComparison.OrdinalIgnoreCase);
                if (ManagedSelected || string.Equals(SelectedName, "PEAK_Data", StringComparison.OrdinalIgnoreCase))
                {
                    var ParentRoot = Directory.GetParent(Root);
                    var PeakRoot = ManagedSelected ? ParentRoot?.Parent : ParentRoot;
                    if (PeakRoot == null)
                    {
                        Root = string.Empty;
                        Failure = InvalidPeakRootFailure;
                        return false;
                    }
                    Root = PeakRoot.FullName;
                }
            }
            catch (Exception Exception) when (Exception is ArgumentException || Exception is NotSupportedException
                                               || Exception is SecurityException)
            {
                Root = string.Empty;
                Failure = "The selected PEAK path is invalid: " + Exception.Message;
                return false;
            }
            if (!File.Exists(Path.Combine(Root, "PEAK_Data", "Managed", "Assembly-CSharp.dll"))
                || !File.Exists(Path.Combine(Root, "UnityPlayer.dll")))
            {
                Root = string.Empty;
                Failure = InvalidPeakRootFailure;
                return false;
            }
            Failure = string.Empty;
            return true;
        }
    }
}
