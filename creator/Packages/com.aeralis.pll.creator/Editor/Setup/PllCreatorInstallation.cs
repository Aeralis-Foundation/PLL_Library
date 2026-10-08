using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;

namespace PLL.Creator.Editor
{
    public sealed class PllCreatorInstallation
    {
        readonly Dictionary<string, string> ManagedPaths;
        readonly HashSet<string> ProviderAssetPaths;
        readonly HashSet<string> SourceAssetPaths;
        readonly Dictionary<string, string> SourceTypePaths;

        PllCreatorInstallation(PllCreatorGeneratedManifest Manifest, PllCreatorFacadeContract Contract)
        {
            ManagedPaths = Manifest.managedAssemblies.ToDictionary(
                Value => Value.name, Value => Value.sourcePath, StringComparer.Ordinal);
            ProviderAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var Assembly in Manifest.managedAssemblies)
                if (!string.IsNullOrEmpty(Assembly.providerAssetPath))
                    ProviderAssetPaths.Add(Normalize(Assembly.providerAssetPath));
            SourceAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SourceTypePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var Declaration in Contract.types)
            {
                var Template = PllCreatorFacadeContract.TemplateName(Declaration.metadataName);
                var PathValue = Normalize(PllCreatorEnvironment.GeneratedAssetRoot + "/Facade/"
                    + Template.Substring(0, Template.Length - ".template".Length));
                SourceAssetPaths.Add(PathValue);
                ProviderAssetPaths.Add(PathValue);
                SourceTypePaths.Add(Declaration.name, PathValue);
            }
            ManagedAssemblyNames = Array.AsReadOnly(ManagedPaths.Keys.OrderBy(
                Value => Value, StringComparer.Ordinal).ToArray());
        }

        public IReadOnlyList<string> ManagedAssemblyNames { get; }

        sealed class InspectionFacts
        {
            internal string PackageVersion = string.Empty;
            internal string State = "Invalid";
            internal PllCreatorFacadeContract Contract;
            internal PllCreatorGeneratedManifest Manifest;
            internal readonly Dictionary<string, string> Identities = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        internal sealed class Status
        {
            internal string PackageVersion { get; }
            internal string State { get; }
            internal string Failure { get; }
            internal PllCreatorFacadeContract Contract { get; }
            internal PllCreatorGeneratedManifest Manifest { get; }
            internal IReadOnlyDictionary<string, string> InspectedIdentities { get; }

            internal Status()
            {
                var Facts = new InspectionFacts();
                bool Ready;
                string Cause;
                try
                {
                    Ready = TryInspectInstallation(true, true, Facts, out _, out _, out Cause);
                }
                catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                                   || Exception is SecurityException)
                {
                    Ready = false;
                    Cause = "The generated installation cannot be read: " + Exception.Message;
                }
                PackageVersion = Facts.PackageVersion;
                Contract = Facts.Contract;
                Manifest = Facts.Manifest;
                InspectedIdentities = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Facts.Identities);
                State = Ready ? "Ready" : Facts.State;
                Failure = Cause;
            }
        }

        public static bool TryValidateUnityVersion(out string Failure)
        {
            var Version = UnityEngine.Application.unityVersion;
            if (Version.StartsWith(
                    "6000.3.", StringComparison.Ordinal))
            {
                Failure = string.Empty;
                return true;
            }
            Failure = "PLL Creator requires Unity 6000.3.x; current Editor is '"
                + Version + "'.";
            return false;
        }

        public static PllCreatorInstallation RequireReady()
        {
            PllCreatorInstallation Installation;
            string Failure;
            if (TryInspectReadyInstallation(out Installation, out Failure)) return Installation;
            throw new InvalidOperationException("PLL Creator setup is incomplete: " + Failure + " Run PLL > Setup > Configure.");
        }

        public bool IsProviderAssetPath(string AssetPath)
        {
            return !string.IsNullOrEmpty(AssetPath) && ProviderAssetPaths.Contains(Normalize(AssetPath));
        }

        public bool IsSourceProviderAssetPath(string AssetPath)
        {
            return !string.IsNullOrEmpty(AssetPath) && SourceAssetPaths.Contains(Normalize(AssetPath));
        }

        public bool IsSourceProviderScript(string AssemblyName, string AssetPath)
        {
            return string.Equals(AssemblyName, "Assembly-CSharp", StringComparison.Ordinal)
                && IsSourceProviderAssetPath(AssetPath);
        }

        public bool CheckSerializedTypeCompatibility(string AssemblyName, string AssetPath, Type TypeValue)
        {
            if (string.IsNullOrEmpty(AssemblyName) || string.IsNullOrEmpty(AssetPath) || TypeValue == null
                || !string.Equals(TypeValue.Assembly.GetName().Name, AssemblyName, StringComparison.Ordinal))
                return false;
            if (!string.Equals(AssemblyName, "Assembly-CSharp", StringComparison.Ordinal))
                return ProviderAssetPaths.Contains(Normalize(AssetPath)) && ManagedPaths.ContainsKey(AssemblyName);
            string Expected;
            return SourceTypePaths.TryGetValue((TypeValue.FullName ?? TypeValue.Name).Replace('+', '.'), out Expected)
                && string.Equals(Expected, Normalize(AssetPath), StringComparison.OrdinalIgnoreCase);
        }

        public string GetAssemblyPath(string AssemblyName)
        {
            string PathValue;
            if (!ManagedPaths.TryGetValue(AssemblyName, out PathValue))
                throw new InvalidOperationException(
                    "PLL Creator setup has no provider for assembly '" + AssemblyName + "'.");
            return PathValue;
        }

        internal static bool TryInspectReadyInstallation(
            out PllCreatorInstallation Installation, out string Failure)
        {
            return TryInspectInstallation(true, true, null, out Installation, out _, out Failure);
        }

        internal static bool TryInspectGeneratedInstallation(
            bool RequireReady, out PllCreatorInstallation Installation, out string Failure)
        {
            return TryInspectGeneratedInstallation(RequireReady, out Installation, out _, out Failure);
        }

        internal static bool TryInspectGeneratedInstallation(
            bool RequireReady, out PllCreatorInstallation Installation,
            out PllCreatorFacadeContract Contract, out string Failure)
        {
            return TryInspectInstallation(RequireReady, false, null, out Installation, out Contract, out Failure);
        }

        static bool TryInspectInstallation(bool RequireReady, bool CheckPending, InspectionFacts Inspection,
            out PllCreatorInstallation Installation, out PllCreatorFacadeContract InspectedContract, out string Failure)
        {
            Installation = null;
            InspectedContract = null;
            string PackageRoot;
            string PackageVersion;
            if (!PllCreatorEnvironment.TryResolvePackage(out PackageRoot, out PackageVersion, out Failure)) return false;
            if (Inspection != null) Inspection.PackageVersion = PackageVersion;
            if (CheckPending && PllCreatorSetupStore.PendingExists())
            {
                if (Inspection != null) Inspection.State = "Pending setup";
                Failure = "A PLL Creator setup transaction is pending.";
                return false;
            }
            PllCreatorFacadeContract Contract;
            if (!PllCreatorFacadeContract.TryLoad(PackageRoot, out Contract, out Failure)) return false;
            if (Inspection != null) Inspection.Contract = Contract;
            var Root = PllCreatorEnvironment.GeneratedRootPath();
            string OwnerTransaction;
            if (!PllCreatorSetupStore.TryReadOwnershipTransaction(Root, out OwnerTransaction))
            {
                if (Directory.Exists(Root))
                    Failure = "The generated Assets directory has no valid ownership record.";
                else
                {
                    if (Inspection != null) Inspection.State = "Not configured";
                    Failure = "The generated Assets installation is absent.";
                }
                return false;
            }
            PllCreatorGeneratedManifest Manifest;
            if (!PllCreatorSetupStore.TryLoadGeneratedManifest(Root, out Manifest, out Failure)) return false;
            if (Inspection != null) Inspection.Manifest = Manifest;
            if (!string.Equals(Manifest.packageVersion, PackageVersion, StringComparison.Ordinal))
            {
                Failure = "The generated Assets package version is '" + Manifest.packageVersion
                    + "'; installed Creator package is '" + PackageVersion + "'. Reconfigure PLL Creator.";
                return false;
            }
            if (!PllCreatorProjectLayers.Installed(Contract.layers, out Failure)) return false;
            if (Manifest.schemaVersion != PllCreatorGeneratedManifest.SchemaVersion
                || Manifest.files == null || Manifest.managedAssemblies == null || Manifest.scripts == null
                || RequireReady && !Manifest.ready
                || !string.Equals(OwnerTransaction, Manifest.transactionId, StringComparison.Ordinal)
                || !PllCreatorSetupStore.TextMatches(
                    Root + ".meta", PllCreatorMetadata.Folder("PLL Creator generated Assets root")))
            {
                Failure = "The generated Assets manifest is incompatible.";
                return false;
            }
            var ContractPath = PllCreatorEnvironment.PackageContractPath(PackageRoot);
            if (!File.Exists(ContractPath) || !string.Equals(
                    PllCreatorFileSystem.Sha256(ContractPath), Manifest.contractSha256, StringComparison.Ordinal))
            {
                Failure = "The generated Assets contract no longer matches the installed package.";
                return false;
            }
            var FileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> ActualFiles;
            try
            {
                ActualFiles = new HashSet<string>(Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
                    .Select(Value => Value.Substring(Root.Length).TrimStart(Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar).Replace('\\', '/'))
                    .Where(Value => !string.Equals(
                        Value, PllCreatorSetupStore.SetupFileName, StringComparison.OrdinalIgnoreCase)),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                               || Exception is ArgumentException || Exception is SecurityException)
            {
                Failure = "The generated Assets inventory cannot be read: " + Exception.Message;
                return false;
            }
            foreach (var FileRecord in Manifest.files)
            {
                string FullPath;
                if (FileRecord == null || !TryResolveOwnedRelativePath(Root, FileRecord.relativePath, out FullPath)
                    || !FileNames.Add(FileRecord.relativePath) || !File.Exists(FullPath)
                    || !string.Equals(PllCreatorFileSystem.Sha256(FullPath), FileRecord.sha256,
                        StringComparison.Ordinal))
                {
                    Failure = "Generated file '" + (FileRecord?.relativePath ?? "<unknown>")
                        + "' is missing or changed.";
                    return false;
                }
            }
            if (!ActualFiles.SetEquals(FileNames))
            {
                Failure = "The generated Assets installation contains an unowned or unrecorded file.";
                return false;
            }
            var ScriptPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Script in Manifest.scripts)
                if (Script == null || !ScriptPaths.Add(Script.typeName + "|" + Script.assetPath))
                {
                    Failure = "The generated script manifest is invalid.";
                    return false;
                }
            var ExpectedScripts = new HashSet<string>(Contract.scripts.Select(Value => Value.typeName + "|"
                + PllCreatorEnvironment.GeneratedAssetRoot + "/Facade/" + Value.template.Substring(
                    0, Value.template.Length - ".template".Length)), StringComparer.Ordinal);
            if (!ScriptPaths.SetEquals(ExpectedScripts))
            {
                Failure = "The generated script manifest no longer matches the installed package.";
                return false;
            }
            var AssemblyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Assembly in Manifest.managedAssemblies)
            {
                string Identity;
                if (Assembly == null || string.IsNullOrEmpty(Assembly.name) || !AssemblyNames.Add(Assembly.name)
                    || !ValidGeneratedAssemblyRecord(Root, Assembly)
                    || !ValidAssembly(Assembly.sourcePath, Assembly.sourceSha256, Assembly.name, true, out Identity))
                {
                    Failure = "PEAK managed source '" + (Assembly?.name ?? "<unknown>") + "' changed.";
                    return false;
                }
                if (Inspection != null) Inspection.Identities.Add(Assembly.name, Identity);
                if (!string.IsNullOrEmpty(Assembly.providerFullPath)
                    && !(UnityPackageSourceRecord(Assembly)
                        ? ValidAssembly(Assembly.providerFullPath, string.Empty, Assembly.name, false, out _)
                        : ValidAssembly(Assembly.providerFullPath, Assembly.providerSha256, Assembly.name, true, out _)))
                {
                    Failure = "Managed provider '" + Assembly.name + "' changed.";
                    return false;
                }
            }
            if (!AssemblyNames.Contains(Contract.sourceAssembly) || !AssemblyNames.Contains("PLL_Library")
                || Contract.dependencyRoots.Any(Value => !AssemblyNames.Contains(Value)))
            {
                Failure = "The generated managed provider manifest is incomplete.";
                return false;
            }
            var DependencyRecords = Manifest.managedAssemblies.Where(Value =>
                    !string.Equals(Value.name, Contract.sourceAssembly, StringComparison.Ordinal)
                    && !string.Equals(Value.name, "PLL_Library", StringComparison.Ordinal))
                .ToDictionary(Value => Value.name, StringComparer.Ordinal);
            PllCreatorProviderInventory Providers;
            PllCreatorAssemblyPlan[] CurrentPlans;
            if (!PllCreatorProviderInventory.TryCreate(PllCreatorEnvironment.GeneratedAssetRoot,
                    PllCreatorEnvironment.GeneratedRootPath(), out Providers, out Failure)
                || !Providers.TryPlanDependencies(
                    DependencyRecords.Values.Select(Value => Value.sourcePath).ToArray(), out CurrentPlans, out Failure))
            {
                Failure = "Generated dependency providers no longer match this project. " + Failure;
                return false;
            }
            foreach (var Plan in CurrentPlans)
            {
                var Record = DependencyRecords[Plan.Source.Identity.Name];
                if (Plan.Candidate == null && Record.owned) continue;
                if (Plan.Candidate != null && !Record.owned
                    && string.Equals(Plan.Candidate.AssetPath, Record.providerAssetPath,
                        StringComparison.OrdinalIgnoreCase)
                    && PllCreatorFileSystem.SamePath(Plan.Candidate.FullPath, Record.providerFullPath)
                    && string.Equals(Plan.Candidate.Sha256, Record.providerSha256, StringComparison.Ordinal)) continue;
                Failure = "Managed provider '" + Record.name + "' is no longer the validated setup provider.";
                return false;
            }
            if (RequireReady && !PllCreatorCompilation.ValidMonoScripts(Contract, out Failure))
                return false;
            if (Inspection == null) Installation = new PllCreatorInstallation(Manifest, Contract);
            InspectedContract = Contract;
            Failure = string.Empty;
            return true;
        }

        internal static string InspectDirectorySha256(string Root)
        {
            var FullRoot = Path.GetFullPath(Root).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var IoRoot = PllCreatorFileSystem.ExtendedPath(FullRoot);
            if (!Directory.Exists(IoRoot)) throw new DirectoryNotFoundException(FullRoot);
            var PrefixLength = IoRoot.Length + 1;
            var Entries = Directory.GetFileSystemEntries(IoRoot, "*", SearchOption.AllDirectories)
                .Select(PathValue => new
                {
                    FullPath = PathValue,
                    RelativePath = PathValue.Substring(PrefixLength).Replace('\\', '/')
                }).OrderBy(Value => Value.RelativePath, StringComparer.Ordinal).ToArray();
            var Serialized = new StringBuilder(Entries.Length * 96);
            foreach (var Entry in Entries)
            {
                var DirectoryEntry = Directory.Exists(Entry.FullPath);
                Serialized.Append(DirectoryEntry ? 'D' : 'F').Append(':')
                    .Append(Entry.RelativePath.Length.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(Entry.RelativePath);
                if (!DirectoryEntry) Serialized.Append(':').Append(PllCreatorFileSystem.Sha256(Entry.FullPath));
                Serialized.Append('\n');
            }
            return PllCreatorFileSystem.Sha256Text(Serialized.ToString());
        }

        static string Normalize(string PathValue)
        {
            return PathValue.Replace('\\', '/');
        }

        static bool TryResolveOwnedRelativePath(string Root, string RelativePath, out string FullPath)
        {
            FullPath = string.Empty;
            if (string.IsNullOrEmpty(RelativePath) || Path.IsPathRooted(RelativePath)) return false;
            try
            {
                FullPath = Path.GetFullPath(Path.Combine(
                    Root, RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                return PllCreatorFileSystem.Inside(FullPath, Root)
                    && !PllCreatorFileSystem.SamePath(FullPath, Root);
            }
            catch (Exception Exception) when (Exception is ArgumentException || Exception is NotSupportedException
                                               || Exception is SecurityException)
            {
                return false;
            }
        }

        static bool ValidGeneratedAssemblyRecord(string Root, PllCreatorGeneratedAssembly Assembly)
        {
            try
            {
                if (!Path.IsPathRooted(Assembly.sourcePath)) return false;
                if (string.Equals(Assembly.name, "Assembly-CSharp", StringComparison.Ordinal))
                    return !Assembly.owned && string.IsNullOrEmpty(Assembly.providerAssetPath)
                        && string.IsNullOrEmpty(Assembly.providerFullPath)
                        && string.IsNullOrEmpty(Assembly.providerSha256);
                if (string.IsNullOrEmpty(Assembly.providerAssetPath)
                    || string.IsNullOrEmpty(Assembly.providerFullPath)
                    || !Path.IsPathRooted(Assembly.providerFullPath)) return false;
                var InsideGeneratedRoot = PllCreatorFileSystem.Inside(Assembly.providerFullPath, Root);
                if (!Assembly.owned) return !InsideGeneratedRoot;
                if (!InsideGeneratedRoot || !string.Equals(
                        Assembly.sourceSha256, Assembly.providerSha256, StringComparison.Ordinal)) return false;
                var FullRoot = Path.GetFullPath(Root).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var RelativePath = Path.GetFullPath(Assembly.providerFullPath).Substring(FullRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                return string.Equals(Assembly.providerAssetPath,
                    PllCreatorEnvironment.GeneratedAssetRoot + "/" + RelativePath,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception Exception) when (Exception is ArgumentException || Exception is NotSupportedException
                                               || Exception is SecurityException)
            {
                return false;
            }
        }

        static bool UnityPackageSourceRecord(PllCreatorGeneratedAssembly Assembly)
        {
            return !Assembly.owned && !string.IsNullOrEmpty(Assembly.providerAssetPath)
                && Assembly.providerAssetPath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase);
        }

        static bool ValidAssembly(string PathValue, string Sha256, string Name,
            bool ValidateContent, out string Identity)
        {
            Identity = string.Empty;
            try
            {
                if (string.IsNullOrEmpty(PathValue) || !File.Exists(PathValue)
                    || ValidateContent && !string.Equals(
                        PllCreatorFileSystem.Sha256(PathValue), Sha256, StringComparison.Ordinal)) return false;
                var AssemblyIdentity = AssemblyName.GetAssemblyName(PathValue);
                if (!string.Equals(AssemblyIdentity.Name, Name, StringComparison.Ordinal)) return false;
                Identity = AssemblyIdentity.FullName;
                return true;
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                               || Exception is SecurityException || Exception is ArgumentException
                                               || Exception is BadImageFormatException || Exception is FileLoadException)
            {
                return false;
            }
        }
    }
}
