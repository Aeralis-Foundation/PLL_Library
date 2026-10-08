using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;
using static PLL.Creator.Editor.PllCreatorFileSystem;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreatorProviderInventory
    {
        internal const string ImportedPluginProvider = "importedPlugin";
        internal const string UnityPackagePluginProvider = "unityPackagePlugin";
        internal const string UnityPackageSourceProvider = "unityPackageSource";
        static readonly Func<PackageManagerInfo, bool> PackageHasFullTrust = BindPackageTrust();
        readonly PllCreatorProviderConflict[] Conflicts;
        readonly Dictionary<string, PllCreatorAssemblyCandidate[]> CandidatesByName;

        PllCreatorProviderInventory(
            PllCreatorProviderConflict[] Conflicts,
            Dictionary<string, PllCreatorAssemblyCandidate[]> CandidatesByName)
        {
            this.Conflicts = Conflicts;
            this.CandidatesByName = CandidatesByName;
        }

        internal static bool TryCreate(
            string GeneratedAssetRoot, string GeneratedRootPath,
            out PllCreatorProviderInventory Inventory, out string Failure)
        {
            try
            {
                Inventory = Create(GeneratedAssetRoot, GeneratedRootPath);
                Failure = string.Empty;
                return true;
            }
            catch (Exception InspectionException) when (InspectionException is IOException || InspectionException is UnauthorizedAccessException
                                              || InspectionException is SecurityException || InspectionException is ArgumentException
                                              || InspectionException is BadImageFormatException || InspectionException is FileLoadException
                                              || InspectionException is UnityException)
            {
                Inventory = null;
                Failure = "Unity dependency providers could not be inspected: " + InspectionException.Message;
                return false;
            }
        }

        internal bool TryPlanDependencies(
            IReadOnlyList<string> Paths, out PllCreatorAssemblyPlan[] Plans, out string Failure)
        {
            try
            {
                var Result = new List<PllCreatorAssemblyPlan>(Paths.Count);
                var Conflicts = new List<string>();
                foreach (var Path in Paths)
                {
                    var IoPath = ExtendedPath(Path);
                    var Source = new PllCreatorManagedAssembly(
                        AssemblyName.GetAssemblyName(IoPath), Path, Sha256(IoPath));
                    PllCreatorAssemblyCandidate[] Found;
                    IReadOnlyList<PllCreatorAssemblyCandidate> Candidates = CandidatesByName.TryGetValue(
                        Source.Identity.Name, out Found)
                        ? Found : Array.Empty<PllCreatorAssemblyCandidate>();
                    PllCreatorAssemblyCandidate Active;
                    if (!TrySelectDependencyProvider(Candidates, out Active)
                        || Active != null && !CanReuseDependency(Source, Active))
                    {
                        Conflicts.Add(DependencyConflict(Source, Candidates));
                        continue;
                    }
                    var RequiredPackage = string.Equals(
                            Source.Identity.Name, "Unity.InputSystem", StringComparison.Ordinal)
                        ? "com.unity.inputsystem 1.19.0"
                        : string.Equals(Source.Identity.Name, "UnityEngine.UI", StringComparison.Ordinal)
                            ? "com.unity.ugui 2.0.0" : null;
                    if (Active == null && RequiredPackage != null)
                    {
                        Conflicts.Add("Assembly '" + Source.Identity.Name
                            + "' requires the official Unity package provider " + RequiredPackage + ".");
                        continue;
                    }
                    Result.Add(Active == null
                        ? PllCreatorAssemblyPlan.Copy(Source) : PllCreatorAssemblyPlan.Reuse(Source, Active));
                }
                if (Conflicts.Count != 0)
                {
                    Plans = null;
                    Failure = "Managed provider conflicts prevent setup:\n- "
                        + string.Join("\n- ", Conflicts);
                    return false;
                }
                Plans = Result.ToArray();
                Failure = string.Empty;
                return true;
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                              || Exception is SecurityException || Exception is ArgumentException
                                              || Exception is BadImageFormatException || Exception is FileLoadException)
            {
                Plans = null;
                Failure = "Unity dependency providers could not be inspected: " + Exception.Message;
                return false;
            }
        }

        internal bool NoConflicts(
            IReadOnlyCollection<string> RequiredNames, string SourceAssembly, out string Failure)
        {
            var Required = new HashSet<string>(RequiredNames, StringComparer.Ordinal);
            foreach (var Conflict in Conflicts)
            {
                if (!Required.Contains(Conflict.AssemblyName)) continue;
                if (Conflict.Kind == PllCreatorProviderConflictKind.Plugin)
                    Failure = "Managed provider '" + Conflict.AssetPath
                        + "' conflicts with the generated '" + Conflict.AssemblyName + "' provider.";
                else if (!string.Equals(Conflict.AssemblyName, SourceAssembly, StringComparison.Ordinal))
                    Failure = "Compiled assembly '" + Conflict.AssemblyName
                        + "' conflicts with the generated provider.";
                else
                    continue;
                return false;
            }
            Failure = string.Empty;
            return true;
        }

        static PllCreatorProviderInventory Create(string GeneratedAssetRoot, string GeneratedRootPath)
        {
            var ProviderConflicts = new List<PllCreatorProviderConflict>();
            var CandidatesByName = new Dictionary<string, List<PllCreatorAssemblyCandidate>>(StringComparer.OrdinalIgnoreCase);
            var Physical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var PlayerAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var EditorAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            var OfficialReferences = AutoReferencedOfficialPluginPaths(PlayerAssemblies);
            foreach (var AssetPath in AssetDatabase.GetAllAssetPaths())
            {
                if (!AssetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || AssetPath.StartsWith(GeneratedAssetRoot + "/", StringComparison.OrdinalIgnoreCase)) continue;
                var Importer = AssetImporter.GetAtPath(AssetPath) as PluginImporter;
                if (Importer == null || Importer.isNativePlugin) continue;
                string FullPath;
                if (!TryAssetPathToFullPath(AssetPath, out FullPath)) continue;
                var IoPath = ExtendedPath(FullPath);
                if (!File.Exists(IoPath) || !Physical.Add(FullPath)) continue;
                var Identity = AssemblyName.GetAssemblyName(IoPath);
                var Sha256 = PllCreatorFileSystem.Sha256(IoPath);
                var TargetCompatible = Importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64);
                var EditorCompatible = Importer.GetCompatibleWithEditor();
                var UnityProvider = Identity.Name.StartsWith("Unity.", StringComparison.Ordinal);
                var Automatic = TargetCompatible && EditorCompatible && PluginAutoReferenced(FullPath);
                var Reusable = Automatic || TargetCompatible && EditorCompatible && UnityProvider
                    && OfficialReferences.Contains(FullPath);
                PllCreatorAssemblyCandidate Candidate = null;
                if (!Reusable || !TryUnityPackagePluginProvider(
                        AssetPath, FullPath, Identity, Sha256, out Candidate))
                    Candidate = PllCreatorAssemblyCandidate.ImportedPlugin(
                        Identity, FullPath, AssetPath, Sha256, Automatic,
                        TargetCompatible || EditorCompatible);
                Add(Candidate);
                if (TargetCompatible || EditorCompatible)
                    ProviderConflicts.Add(PllCreatorProviderConflict.Plugin(Identity.Name, AssetPath));
            }
            foreach (var Assembly in PlayerAssemblies) AddCompilation(Assembly, true);
            foreach (var Assembly in EditorAssemblies) AddCompilation(Assembly, false);
            foreach (var Path in CompilationPipeline.GetPrecompiledAssemblyPaths(
                         CompilationPipeline.PrecompiledAssemblySources.UserAssembly))
            {
                string FullPath;
                if (string.IsNullOrEmpty(Path) || !TryAssetPathToFullPath(Path, out FullPath)
                    || Ignored(FullPath)) continue;
                var IoPath = ExtendedPath(FullPath);
                if (!File.Exists(IoPath) || !Physical.Add(FullPath)) continue;
                Add(PllCreatorAssemblyCandidate.Untrusted(
                    AssemblyName.GetAssemblyName(IoPath), FullPath, Sha256(IoPath)));
            }
            return new PllCreatorProviderInventory(ProviderConflicts.ToArray(), CandidatesByName.ToDictionary(
                Entry => Entry.Key, Entry => Entry.Value.ToArray(), StringComparer.OrdinalIgnoreCase));

            void AddCompilation(UnityEditor.Compilation.Assembly Assembly, bool Player)
            {
                ProviderConflicts.Add(PllCreatorProviderConflict.Compiled(Assembly.name));
                string FullPath;
                if (string.IsNullOrEmpty(Assembly.outputPath)
                    || !TryAssetPathToFullPath(Assembly.outputPath, out FullPath))
                {
                    Add(PllCreatorAssemblyCandidate.Untrusted(
                        new AssemblyName(Assembly.name), "<missing Unity output>", string.Empty));
                    return;
                }
                if (Ignored(FullPath) || !Physical.Add(FullPath)) return;
                var IoPath = ExtendedPath(FullPath);
                if (!File.Exists(IoPath))
                {
                    Add(PllCreatorAssemblyCandidate.Untrusted(
                        new AssemblyName(Assembly.name), FullPath, string.Empty));
                    return;
                }
                PllCreatorAssemblyCandidate Candidate;
                if (!Player || !TryUnityPackageSourceProvider(Assembly, FullPath, out Candidate))
                    Candidate = PllCreatorAssemblyCandidate.Untrusted(
                        AssemblyName.GetAssemblyName(IoPath), FullPath, Sha256(IoPath));
                Add(Candidate);
            }

            bool Ignored(string FullPath)
            {
                return Inside(FullPath, GeneratedRootPath);
            }

            void Add(PllCreatorAssemblyCandidate Candidate)
            {
                if (FrameworkAssembly(Candidate.Identity.Name)) return;
                List<PllCreatorAssemblyCandidate> Candidates;
                if (!CandidatesByName.TryGetValue(Candidate.Identity.Name, out Candidates))
                {
                    Candidates = new List<PllCreatorAssemblyCandidate>();
                    CandidatesByName.Add(Candidate.Identity.Name, Candidates);
                }
                Candidates.Add(Candidate);
            }
        }

        static HashSet<string> AutoReferencedOfficialPluginPaths(
            IEnumerable<UnityEditor.Compilation.Assembly> Assemblies)
        {
            var Result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var Assembly in Assemblies)
            {
                PackageManagerInfo Package;
                string DefinitionPath;
                var Definition = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(Assembly.name);
                if (!TryOfficialPackageAsset(Definition, null, out Package, out DefinitionPath)
                    || !DefinitionPath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
                    || !AssemblyDefinitionAutoReferenced(DefinitionPath)) continue;
                foreach (var Reference in Assembly.allReferences ?? Array.Empty<string>())
                {
                    string FullPath;
                    if (TryAssetPathToFullPath(Reference, out FullPath) && File.Exists(ExtendedPath(FullPath)))
                        Result.Add(FullPath);
                }
            }
            return Result;
        }

        static bool TrySelectDependencyProvider(
            IReadOnlyList<PllCreatorAssemblyCandidate> Candidates, out PllCreatorAssemblyCandidate Active)
        {
            Active = null;
            foreach (var Candidate in Candidates)
            {
                if (!Candidate.Reusable
                    && string.Equals(Candidate.ProviderKind, ImportedPluginProvider, StringComparison.Ordinal))
                {
                    if (Candidate.IncludedForCreatorPlatforms) return false;
                    continue;
                }
                if (Active != null) return false;
                Active = Candidate;
            }
            return true;
        }

        static bool CanReuseDependency(PllCreatorManagedAssembly Source, PllCreatorAssemblyCandidate Candidate)
        {
            if (!Candidate.Reusable || !string.Equals(
                    Source.Identity.Name, Candidate.Identity.Name, StringComparison.Ordinal)) return false;
            if (string.Equals(Candidate.ProviderKind, ImportedPluginProvider, StringComparison.Ordinal))
                return SameDependencyBinary(Source, Candidate);
            if (string.Equals(Candidate.ProviderKind, UnityPackagePluginProvider, StringComparison.Ordinal))
                return SameDependencyBinary(Source, Candidate)
                    || Source.Identity.Name.StartsWith("Unity.", StringComparison.Ordinal)
                    && string.Equals(Source.Identity.FullName, Candidate.Identity.FullName, StringComparison.Ordinal);
            return string.Equals(Candidate.ProviderKind, UnityPackageSourceProvider, StringComparison.Ordinal)
                && (Source.Identity.Name.StartsWith("Unity.", StringComparison.Ordinal)
                    || string.Equals(Source.Identity.Name, "UnityEngine.UI", StringComparison.Ordinal))
                && string.Equals(Source.Identity.FullName, Candidate.Identity.FullName, StringComparison.Ordinal);
        }

        static bool SameDependencyBinary(
            PllCreatorManagedAssembly Source, PllCreatorAssemblyCandidate Candidate)
        {
            return string.Equals(Source.Identity.FullName, Candidate.Identity.FullName, StringComparison.Ordinal)
                && string.Equals(Source.Sha256, Candidate.Sha256, StringComparison.Ordinal);
        }

        static string DependencyConflict(
            PllCreatorManagedAssembly Source, IEnumerable<PllCreatorAssemblyCandidate> Candidates)
        {
            var Actual = string.Join(", ", Candidates.OrderBy(Value => Value.FullPath,
                    StringComparer.OrdinalIgnoreCase).Select(Value => "'" + Value.FullPath + "' ("
                    + Value.Identity.FullName + ", "
                    + (string.IsNullOrEmpty(Value.ProviderKind) ? "untrusted" : Value.ProviderKind) + ")"));
            var ExactImported = Candidates.Any(Value => Value.IncludedForCreatorPlatforms
                && string.Equals(Value.ProviderKind, ImportedPluginProvider, StringComparison.Ordinal)
                && SameDependencyBinary(Source, Value));
            var Action = ExactImported
                ? "Enable Auto Reference and Editor + StandaloneWindows64 compatibility on the exact provider, "
                    + "or remove it"
                : "Remove or isolate the existing provider(s)";
            return "Assembly '" + Source.Identity.Name + "' conflicts with PEAK source '" + Source.Path
                + "'. " + Action + ": " + Actual + ".";
        }

        static bool TryUnityPackageSourceProvider(
            UnityEditor.Compilation.Assembly Assembly, string FullPath, out PllCreatorAssemblyCandidate Candidate)
        {
            Candidate = null;
            var Definition = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(Assembly.name);
            PackageManagerInfo Package;
            string DefinitionPath;
            if (!TryOfficialPackageAsset(Definition, null, out Package, out DefinitionPath)
                || !DefinitionPath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
                || !AssemblyDefinitionAutoReferenced(DefinitionPath)
                || Assembly.sourceFiles == null || Assembly.sourceFiles.Length == 0) return false;
            var SourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var Source in Assembly.sourceFiles)
            {
                PackageManagerInfo SourcePackage;
                string SourcePath;
                if (!TryOfficialPackageAsset(Source, Package, out SourcePackage, out SourcePath)
                    || !SourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || !SourcePaths.Add(SourcePath)) return false;
            }
            var Identity = AssemblyName.GetAssemblyName(ExtendedPath(FullPath));
            if (!string.Equals(Identity.Name, Assembly.name, StringComparison.Ordinal)) return false;
            Candidate = PllCreatorAssemblyCandidate.UnityPackageSource(
                Identity, FullPath, DefinitionPath,
                UnityPackageSourceSeal(Package, Identity, DefinitionPath, SourcePaths));
            return true;
        }

        static string UnityPackageSourceSeal(
            PackageManagerInfo Package, AssemblyName Identity, string DefinitionPath, IEnumerable<string> SourcePaths)
        {
            var Seal = new StringBuilder();
            Seal.Append(Package.name).Append('\0').Append(Package.version).Append('\0')
                .Append(Identity.FullName).Append('\0');
            foreach (var AssetPath in SourcePaths.Concat(new[] { DefinitionPath })
                         .OrderBy(Value => Value, StringComparer.OrdinalIgnoreCase))
                Seal.Append(AssetPath.ToUpperInvariant()).Append('\0')
                    .Append(Sha256(ExtendedPath(AssetPathToFullPath(AssetPath)))).Append('\0');
            return Sha256Text(Seal.ToString());
        }

        static bool TryUnityPackagePluginProvider(
            string AssetPath, string FullPath, AssemblyName Identity, string Sha256,
            out PllCreatorAssemblyCandidate Candidate)
        {
            Candidate = null;
            PackageManagerInfo Package;
            string Normalized;
            if (!TryOfficialPackageAsset(AssetPath, null, out Package, out Normalized)
                || !SamePath(FullPath, AssetPathToFullPath(Normalized))) return false;
            Candidate = PllCreatorAssemblyCandidate.UnityPackagePlugin(Identity, FullPath, Normalized, Sha256);
            return true;
        }

        static bool AssemblyDefinitionAutoReferenced(string AssetPath)
        {
            var Text = File.ReadAllText(ExtendedPath(AssetPathToFullPath(AssetPath)));
            return !Regex.IsMatch(Text, "\\\"autoReferenced\\\"\\s*:\\s*false",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        static bool PluginAutoReferenced(string FullPath)
        {
            var MetaPath = ExtendedPath(FullPath + ".meta");
            return File.Exists(MetaPath) && Regex.IsMatch(File.ReadAllText(MetaPath),
                "(?m)^  isExplicitlyReferenced: 0\\s*$", RegexOptions.CultureInvariant);
        }

        static bool TryOfficialPackageAsset(
            string Path, PackageManagerInfo Expected, out PackageManagerInfo Package, out string AssetPath)
        {
            Package = null;
            AssetPath = string.Empty;
            if (string.IsNullOrEmpty(Path)) return false;
            var Normalized = Path.Replace('\\', '/');
            if (Normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                AssetPath = Normalized;
                Package = PackageManagerInfo.FindForAssetPath(AssetPath);
            }
            else if (System.IO.Path.IsPathRooted(Path))
            {
                var FullPath = System.IO.Path.GetFullPath(Path);
                Package = Expected != null
                    ? Inside(FullPath, Expected.resolvedPath) ? Expected : null
                    : PackageManagerInfo.GetAllRegisteredPackages().FirstOrDefault(Value =>
                        !string.IsNullOrEmpty(Value.resolvedPath)
                        && Inside(FullPath, Value.resolvedPath));
                if (Package != null)
                {
                    var Root = System.IO.Path.GetFullPath(Package.resolvedPath).TrimEnd(
                        System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                    var Relative = FullPath.Substring(Root.Length).TrimStart(
                        System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    AssetPath = "Packages/" + Package.name + "/" + Relative;
                }
            }
            if (Package == null || !TrustedOfficialPackage(Package)
                || string.IsNullOrEmpty(Package.name) || !Package.name.StartsWith("com.unity.", StringComparison.Ordinal)
                || string.IsNullOrEmpty(Package.version) || string.IsNullOrEmpty(Package.resolvedPath)
                || !AssetPath.StartsWith("Packages/" + Package.name + "/", StringComparison.OrdinalIgnoreCase)
                || Expected != null && (!string.Equals(Package.name, Expected.name, StringComparison.Ordinal)
                    || !string.Equals(Package.version, Expected.version, StringComparison.Ordinal)
                    || !SamePath(Package.resolvedPath, Expected.resolvedPath))) return false;
            var Registered = PackageManagerInfo.FindForAssetPath(AssetPath);
            var FullAssetPath = AssetPathToFullPath(AssetPath);
            if (Registered == null || !TrustedOfficialPackage(Registered)
                || !string.Equals(Registered.name, Package.name, StringComparison.Ordinal)
                || !string.Equals(Registered.version, Package.version, StringComparison.Ordinal)
                || !SamePath(Registered.resolvedPath, Package.resolvedPath)
                || !File.Exists(ExtendedPath(FullAssetPath)) || !Inside(FullAssetPath, Package.resolvedPath)) return false;
            AssetPath = AssetPath.Replace('\\', '/');
            return true;
        }

        static Func<PackageManagerInfo, bool> BindPackageTrust()
        {
            var Property = typeof(PackageManagerInfo).GetProperty(
                "trustLevel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var Getter = Property?.GetGetMethod(true);
            if (Getter == null || !Property.PropertyType.IsEnum) return Package => false;
            var FullTrust = Property.PropertyType.GetField("FullTrust", BindingFlags.Public | BindingFlags.Static);
            if (FullTrust == null) return Package => false;
            var TrustedValue = FullTrust.GetValue(null);
            return Package => TrustedValue.Equals(Getter.Invoke(Package, null));
        }

        static bool TrustedOfficialPackage(PackageManagerInfo Package)
        {
            if (Package.source == UnityEditor.PackageManager.PackageSource.Registry)
            {
                var Registry = Package.registry;
                if (Registry == null || !Registry.isDefault || !OfficialRegistryUrl(Registry.url)) return false;
            }
            else if (Package.source != UnityEditor.PackageManager.PackageSource.BuiltIn) return false;
            try
            {
                return PackageHasFullTrust(Package);
            }
            catch (Exception Exception) when (Exception is TargetException || Exception is TargetInvocationException
                                              || Exception is MethodAccessException)
            {
                return false;
            }
        }

        static bool OfficialRegistryUrl(string Value)
        {
            Uri Registry;
            return Uri.TryCreate(Value, UriKind.Absolute, out Registry)
                && string.Equals(Registry.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Registry.IdnHost, "packages.unity.com", StringComparison.OrdinalIgnoreCase)
                && Registry.IsDefaultPort && string.IsNullOrEmpty(Registry.UserInfo)
                && (string.IsNullOrEmpty(Registry.AbsolutePath) || Registry.AbsolutePath == "/")
                && string.IsNullOrEmpty(Registry.Query) && string.IsNullOrEmpty(Registry.Fragment);
        }

        enum PllCreatorProviderConflictKind
        {
            Plugin,
            Compiled
        }

        sealed class PllCreatorProviderConflict
        {
            PllCreatorProviderConflict(
                PllCreatorProviderConflictKind Kind, string AssemblyName, string AssetPath)
            {
                this.Kind = Kind;
                this.AssemblyName = AssemblyName;
                this.AssetPath = AssetPath;
            }

            internal PllCreatorProviderConflictKind Kind { get; }
            internal string AssemblyName { get; }
            internal string AssetPath { get; }

            internal static PllCreatorProviderConflict Plugin(string AssemblyName, string AssetPath)
            {
                return new PllCreatorProviderConflict(PllCreatorProviderConflictKind.Plugin, AssemblyName, AssetPath);
            }

            internal static PllCreatorProviderConflict Compiled(string AssemblyName)
            {
                return new PllCreatorProviderConflict(
                    PllCreatorProviderConflictKind.Compiled, AssemblyName, string.Empty);
            }
        }

        internal static bool FrameworkAssembly(string Name)
        {
            return string.Equals(Name, "mscorlib", StringComparison.Ordinal)
                || string.Equals(Name, "netstandard", StringComparison.Ordinal)
                || string.Equals(Name, "Microsoft.CSharp", StringComparison.Ordinal)
                || string.Equals(Name, "UnityEngine", StringComparison.Ordinal)
                || Name.StartsWith("UnityEngine.", StringComparison.Ordinal)
                && !string.Equals(Name, "UnityEngine.UI", StringComparison.Ordinal)
                || string.Equals(Name, "System", StringComparison.Ordinal)
                || Name.StartsWith("System.", StringComparison.Ordinal);
        }
    }

    internal sealed class PllCreatorManagedAssembly
    {
        internal PllCreatorManagedAssembly(AssemblyName Identity, string Path, string Sha256)
        {
            this.Identity = Identity;
            this.Path = Path;
            this.Sha256 = Sha256;
        }

        internal AssemblyName Identity { get; }
        internal string Path { get; }
        internal string Sha256 { get; }
    }

    internal sealed class PllCreatorAssemblyCandidate
    {
        PllCreatorAssemblyCandidate(
            AssemblyName Identity, string FullPath, string AssetPath, string Sha256,
            bool Reusable, string ProviderKind, bool IncludedForCreatorPlatforms)
        {
            this.Identity = Identity;
            this.FullPath = FullPath;
            this.AssetPath = AssetPath;
            this.Sha256 = Sha256;
            this.Reusable = Reusable;
            this.ProviderKind = ProviderKind;
            this.IncludedForCreatorPlatforms = IncludedForCreatorPlatforms;
        }

        internal AssemblyName Identity { get; }
        internal string FullPath { get; }
        internal string AssetPath { get; }
        internal string Sha256 { get; }
        internal bool Reusable { get; }
        internal string ProviderKind { get; }
        internal bool IncludedForCreatorPlatforms { get; }

        internal static PllCreatorAssemblyCandidate ImportedPlugin(
            AssemblyName Identity, string FullPath, string AssetPath, string Sha256, bool Reusable,
            bool IncludedForCreatorPlatforms)
        {
            return new PllCreatorAssemblyCandidate(Identity, FullPath, AssetPath, Sha256, Reusable,
                PllCreatorProviderInventory.ImportedPluginProvider, IncludedForCreatorPlatforms);
        }

        internal static PllCreatorAssemblyCandidate UnityPackagePlugin(
            AssemblyName Identity, string FullPath, string AssetPath, string Sha256)
        {
            return new PllCreatorAssemblyCandidate(Identity, FullPath, AssetPath, Sha256, true,
                PllCreatorProviderInventory.UnityPackagePluginProvider, true);
        }

        internal static PllCreatorAssemblyCandidate UnityPackageSource(
            AssemblyName Identity, string FullPath, string AssetPath, string Sha256)
        {
            return new PllCreatorAssemblyCandidate(Identity, FullPath, AssetPath, Sha256, true,
                PllCreatorProviderInventory.UnityPackageSourceProvider, true);
        }

        internal static PllCreatorAssemblyCandidate Untrusted(
            AssemblyName Identity, string FullPath, string Sha256)
        {
            return new PllCreatorAssemblyCandidate(
                Identity, FullPath, string.Empty, Sha256, false, string.Empty, true);
        }
    }

    internal sealed class PllCreatorAssemblyPlan
    {
        PllCreatorAssemblyPlan(PllCreatorManagedAssembly Source, PllCreatorAssemblyCandidate Candidate)
        {
            this.Source = Source;
            this.Candidate = Candidate;
        }

        internal PllCreatorManagedAssembly Source { get; }
        internal PllCreatorAssemblyCandidate Candidate { get; }

        internal static PllCreatorAssemblyPlan Copy(PllCreatorManagedAssembly Source)
        {
            return new PllCreatorAssemblyPlan(Source, null);
        }

        internal static PllCreatorAssemblyPlan Reuse(
            PllCreatorManagedAssembly Source, PllCreatorAssemblyCandidate Candidate)
        {
            return new PllCreatorAssemblyPlan(Source, Candidate);
        }
    }
}
