using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Mono.Cecil;
using PLL.Library;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllProfilePublication
    {
        internal enum RuntimeResult { Unverified, Sealed, Rejected }

        internal sealed class Target
        {
            Target(string Root, string RuntimePath, string CorePath, Guid RuntimeMvid)
            {
                this.Root = Root;
                this.RuntimePath = RuntimePath;
                this.CorePath = CorePath;
                this.RuntimeMvid = RuntimeMvid;
                RuntimeSha256 = PllCreatorFileSystem.Sha256(RuntimePath);
                CoreSha256 = PllCreatorFileSystem.Sha256(CorePath);
            }

            internal string Root { get; }
            internal string RuntimePath { get; }
            internal string RuntimeSha256 { get; }
            internal Guid RuntimeMvid { get; }
            internal string CorePath { get; }
            internal string CoreSha256 { get; }
            internal string BundleDirectory => Path.Combine(Root, "BepInEx", "plugins", "PLL_Biomes");

            internal static Target Inspect(string Root, string RuntimePath, string PllAssembly)
            {
                Root = Path.GetFullPath(Root);
                RuntimePath = Path.GetFullPath(RuntimePath);
                if (PllCreatorFileSystem.Inside(Root, Directory.GetParent(Application.dataPath).FullName))
                    throw new InvalidOperationException("The game profile must be outside the Unity project.");
                var Plugins = Path.Combine(Root, "BepInEx", "plugins");
                if (!Directory.Exists(Plugins) || !PllCreatorFileSystem.Inside(RuntimePath, Plugins))
                    throw new InvalidOperationException("Select the profile's installed PLL runtime DLL inside BepInEx/plugins.");
                var CorePath = Path.Combine(Root, "BepInEx", "core", "BepInEx.dll");
                if (!File.Exists(CorePath))
                    throw new InvalidOperationException("The selected profile has no BepInEx core installation.");
                using (var RuntimeStream = File.OpenRead(RuntimePath))
                using (File.OpenRead(CorePath))
                {
                    if (AssemblyName.GetAssemblyName(CorePath).Name != "BepInEx")
                        throw new InvalidOperationException("The selected profile has no BepInEx core installation.");
                    using (var RuntimeModule = ModuleDefinition.ReadModule(RuntimeStream))
                    {
                        if (RuntimeModule.Assembly == null || !string.Equals(RuntimeModule.Assembly.Name.FullName,
                                PllAssembly, StringComparison.Ordinal)
                            || FileVersionInfo.GetVersionInfo(RuntimePath).FileDescription != MyPluginInfo.PLUGIN_NAME)
                            throw new InvalidOperationException("The selected runtime DLL does not match this build's PLL runtime version.");
                        return new Target(Root, RuntimePath, CorePath, RuntimeModule.Mvid);
                    }
                }
            }
        }

        internal PllProfilePublication(BuildReportData Report)
        {
            BundlePath = Report.output;
            ReportPath = Report.output + ".report.json";
            BundleSha256 = Report.bundleSha256;
            ReportSha256 = Report.PublishedReportSha256;
            PllAssembly = Report.pllAssembly;
        }

        internal string BundlePath { get; }
        internal string ReportPath { get; }
        internal string BundleSha256 { get; }
        internal string ReportSha256 { get; }
        internal string PllAssembly { get; }

        internal RuntimeResult ReadRuntimeResult(Target Profile)
        {
            var LogPath = Path.Combine(Profile.Root, "BepInEx", "LogOutput.log");
            if (!File.Exists(LogPath)) return RuntimeResult.Unverified;
            var Prefix = "PLL bundle-result/v1 bundleSha256=" + BundleSha256.ToLowerInvariant()
                + " runtimeMvid=" + Profile.RuntimeMvid.ToString("D") + " stage=";
            var Result = RuntimeResult.Unverified;
            using (File.OpenRead(Profile.RuntimePath))
            using (var Stream = File.OpenRead(LogPath))
            {
                if (Stream.Length > 16 * 1024 * 1024)
                    throw new InvalidOperationException("The profile log exceeds 16 MiB. Archive it before recording another run.");
                if (PllCreatorFileSystem.Sha256(Profile.RuntimePath) != Profile.RuntimeSha256)
                    throw new InvalidOperationException("The selected runtime changed. Inspect the profile again before reading its result.");
                using (var Reader = new StreamReader(Stream, PllCreatorFileSystem.StrictUtf8, true))
                {
                    string Line;
                    while ((Line = Reader.ReadLine()) != null)
                    {
                        var Index = Line.IndexOf(Prefix, StringComparison.Ordinal);
                        if (Index < 0 || Index != 0 && (Index < 2 || Line[Index - 2] != ']' || Line[Index - 1] != ' '))
                            continue;
                        var Record = Line.AsSpan(Index + Prefix.Length);
                        var Live = Record.StartsWith("live ".AsSpan(), StringComparison.Ordinal);
                        if (Live) Record = Record.Slice("live ".Length);
                        else if (Record.StartsWith("startup ".AsSpan(), StringComparison.Ordinal))
                            Record = Record.Slice("startup ".Length);
                        else continue;
                        if (Live && Record.SequenceEqual("outcome=accepted".AsSpan()))
                            Result = RuntimeResult.Sealed;
                        else if (Record.SequenceEqual("outcome=rejected".AsSpan())
                            || Record.StartsWith("outcome=rejected: ".AsSpan(), StringComparison.Ordinal))
                            Result = RuntimeResult.Rejected;
                    }
                }
            }
            return Result;
        }

        internal string Install(Target Profile)
        {
            var Destination = Path.Combine(Profile.BundleDirectory, Path.GetFileName(BundlePath));
            if (string.Equals(Path.GetFullPath(BundlePath), Destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The build output is already at the selected installation target.");
            var Processes = Process.GetProcessesByName("PEAK");
            foreach (var Process in Processes) Process.Dispose();
            if (Processes.Length != 0)
                throw new InvalidOperationException("PEAK is running. Close it before installing the bundle.");
            using (File.OpenRead(Profile.RuntimePath))
            using (File.OpenRead(Profile.CorePath))
            using (File.OpenRead(BundlePath))
            using (File.OpenRead(ReportPath))
            {
                if (PllCreatorFileSystem.Sha256(Profile.RuntimePath) != Profile.RuntimeSha256
                    || PllCreatorFileSystem.Sha256(Profile.CorePath) != Profile.CoreSha256)
                    throw new InvalidOperationException("The selected profile changed. Inspect it again before installing.");
                if (PllCreatorFileSystem.Sha256(BundlePath) != BundleSha256
                    || PllCreatorFileSystem.Sha256(ReportPath) != ReportSha256)
                    throw new InvalidOperationException("The published build changed. Build the bundle again before installing.");
                var Stage = ExternalStage.Create(Destination);
                Exception PrimaryFailure = null;
                try
                {
                    var BundleStage = Path.Combine(Stage.RootPath, "content.pll");
                    var ReportStage = Path.Combine(Stage.RootPath, "report.json");
                    File.Copy(BundlePath, BundleStage);
                    File.Copy(ReportPath, ReportStage);
                    PllBuildPublisher.Publish(BundleStage, Destination, ReportStage, Destination + ".report.json");
                }
                catch (Exception Failure)
                {
                    PrimaryFailure = Failure;
                }
                try
                {
                    Stage.Cleanup();
                }
                catch (Exception CleanupFailure)
                {
                    if (PrimaryFailure == null) throw;
                    throw new AggregateException("Profile installation and staging cleanup both failed.",
                        PrimaryFailure, CleanupFailure);
                }
                if (PrimaryFailure != null) ExceptionDispatchInfo.Capture(PrimaryFailure).Throw();
            }
            return Destination;
        }
    }
}
