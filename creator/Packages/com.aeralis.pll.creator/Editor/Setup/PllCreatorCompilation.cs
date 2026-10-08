using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Object = UnityEngine.Object;
using ReflectionAssembly = System.Reflection.Assembly;

namespace PLL.Creator.Editor
{
    internal enum PllCreatorCompilationStatus
    {
        Waiting,
        Ready,
        Failed
    }

    [InitializeOnLoad]
    internal static class PllCreatorCompilation
    {
        const string RequestedSessionPrefix = "PLL.Creator.CompilationRequested.";
        static bool PollScheduled;
        static double PollTime;

        static PllCreatorCompilation()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        internal static void Begin(PllCreatorPendingSetup Pending)
        {
            Pending.state = PllCreatorSetupTransaction.AwaitingCompilation;
            Pending.compilationFinished = false;
            Pending.compilerErrorCount = 0;
            Pending.firstCompilerError = string.Empty;
            PllCreatorSetupStore.SavePending(Pending);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            RequestCompilation(Pending);
        }

        internal static PllCreatorCompilationStatus Continue(
            PllCreatorPendingSetup Pending, out string Failure)
        {
            if (Pending.compilerErrorCount != 0)
            {
                Failure = "Unity script compilation failed: " + Pending.firstCompilerError;
                return PllCreatorCompilationStatus.Failed;
            }
            if (Pending.compilationFinished)
            {
                SessionState.EraseBool(RequestedSessionKey(Pending));
                return TryVerifyReadiness(out Failure)
                    ? PllCreatorCompilationStatus.Ready : PllCreatorCompilationStatus.Failed;
            }
            if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
                RequestCompilation(Pending);
            Failure = string.Empty;
            return PllCreatorCompilationStatus.Waiting;
        }

        internal static bool AwaitingCompletion()
        {
            PllCreatorPendingSetup Pending;
            return PllCreatorSetupStore.TryLoadPending(out Pending, out _)
                && string.Equals(Pending.state,
                    PllCreatorSetupTransaction.AwaitingCompilation, StringComparison.Ordinal)
                && !Pending.compilationFinished;
        }

        internal static void ScheduleResume()
        {
            if (PollScheduled) return;
            PollScheduled = true;
            PollTime = EditorApplication.timeSinceStartup + 0.25;
            EditorApplication.update += Poll;
        }

        internal static bool ValidMonoScripts(PllCreatorFacadeContract Contract, out string Failure)
        {
            var SourceAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(Value =>
                string.Equals(Value.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal));
            return ValidMonoScripts(Contract, SourceAssembly, out _, out Failure);
        }

        static bool ValidMonoScripts(
            PllCreatorFacadeContract Contract, ReflectionAssembly SourceAssembly,
            out IReadOnlyDictionary<string, Type> Types, out string Failure)
        {
            if (!Contract.TryValidateFacade(SourceAssembly, out Types, out Failure)) return false;
            foreach (var Declaration in Contract.types.Where(Value => Value.metadataName.IndexOf('/') < 0))
            {
                var TypeValue = Types[Declaration.name];
                if (!typeof(MonoBehaviour).IsAssignableFrom(TypeValue)
                    && !typeof(ScriptableObject).IsAssignableFrom(TypeValue)) continue;
                var Template = PllCreatorFacadeContract.TemplateName(Declaration.metadataName);
                var AssetPath = PllCreatorEnvironment.GeneratedAssetRoot + "/Facade/"
                    + Template.Substring(0, Template.Length - ".template".Length);
                var Script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetPath);
                if (Script && Script.GetClass() == TypeValue) continue;
                Failure = "Generated script '" + AssetPath + "' does not resolve to '" + Declaration.name + "'.";
                return false;
            }
            Failure = string.Empty;
            return true;
        }

        static void RequestCompilation(PllCreatorPendingSetup Pending)
        {
            var SessionKey = RequestedSessionKey(Pending);
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            CompilationPipeline.RequestScriptCompilation();
        }

        static string RequestedSessionKey(PllCreatorPendingSetup Pending)
        {
            return RequestedSessionPrefix + Pending.transactionId;
        }

        static void OnCompilationStarted(object Context)
        {
            PllCreatorPendingSetup Pending;
            if (!PllCreatorSetupTransaction.TryReadPending(out Pending, out _)
                || !string.Equals(Pending.state,
                    PllCreatorSetupTransaction.AwaitingCompilation, StringComparison.Ordinal)) return;
            Pending.compilationFinished = false;
            Pending.compilerErrorCount = 0;
            Pending.firstCompilerError = string.Empty;
            SavePendingFromCallback(Pending);
        }

        static void OnAssemblyCompilationFinished(string AssemblyPath, CompilerMessage[] Messages)
        {
            var ErrorCount = 0;
            string FirstError = null;
            foreach (var Message in Messages)
            {
                if (Message.type != CompilerMessageType.Error) continue;
                ErrorCount++;
                if (FirstError == null) FirstError = Message.message;
            }
            if (ErrorCount == 0) return;
            PllCreatorPendingSetup Pending;
            if (!PllCreatorSetupTransaction.TryReadPending(out Pending, out _)
                || !string.Equals(Pending.state,
                    PllCreatorSetupTransaction.AwaitingCompilation, StringComparison.Ordinal)) return;
            Pending.compilerErrorCount += ErrorCount;
            if (string.IsNullOrEmpty(Pending.firstCompilerError))
                Pending.firstCompilerError = "'" + Path.GetFileNameWithoutExtension(AssemblyPath)
                    + "': " + FirstError;
            SavePendingFromCallback(Pending);
        }

        static void OnCompilationFinished(object Context)
        {
            PllCreatorPendingSetup Pending;
            if (!PllCreatorSetupTransaction.TryReadPending(out Pending, out _)
                || !string.Equals(Pending.state,
                    PllCreatorSetupTransaction.AwaitingCompilation, StringComparison.Ordinal)) return;
            Pending.compilationFinished = true;
            SavePendingFromCallback(Pending);
            EditorApplication.delayCall += PllCreatorSetup.ResumeAfterCompilation;
        }

        static void SavePendingFromCallback(PllCreatorPendingSetup Pending)
        {
            try
            {
                PllCreatorSetupStore.SavePending(Pending);
            }
            catch (Exception PersistenceFailure)
            {
                Pending.state = PllCreatorSetupTransaction.RollingBack;
                Pending.failure = "PLL Creator could not record compilation state: " + PersistenceFailure.Message;
                Pending.deadlineUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
                try
                {
                    PllCreatorSetupStore.SavePending(Pending);
                }
                catch (Exception FailureStatePersistenceFailure)
                {
                    throw new AggregateException(
                        "PLL Creator compilation-state persistence and failure-state persistence both failed.",
                        PersistenceFailure, FailureStatePersistenceFailure);
                }
                EditorApplication.delayCall += PllCreatorSetup.ResumeAfterCompilation;
                throw;
            }
        }

        static bool TryVerifyReadiness(out string Failure)
        {
            PllCreatorFacadeContract Contract;
            if (!PllCreatorInstallation.TryInspectGeneratedInstallation(false, out _, out Contract, out Failure))
                return false;
            ReflectionAssembly SourceAssembly = null, BuilderAssembly = null;
            foreach (var Assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var Name = Assembly.GetName().Name;
                if (SourceAssembly == null && string.Equals(Name, "Assembly-CSharp", StringComparison.Ordinal))
                    SourceAssembly = Assembly;
                else if (BuilderAssembly == null && string.Equals(Name, "Assembly-CSharp-Editor", StringComparison.Ordinal))
                    BuilderAssembly = Assembly;
                if (SourceAssembly != null && BuilderAssembly != null) break;
            }
            IReadOnlyDictionary<string, Type> Types;
            if (!ValidMonoScripts(Contract, SourceAssembly, out Types, out Failure)) return false;
            if (BuilderAssembly == null
                || BuilderAssembly.GetType("PLL.Creator.Editor.PllBundleBuilder", false) == null)
            {
                Failure = "Unity did not compile the copied PLL builder into Assembly-CSharp-Editor.";
                return false;
            }
            return ProbeSaveReload(Contract, Types, out Failure);
        }

        static bool ProbeSaveReload(
            PllCreatorFacadeContract Contract, IReadOnlyDictionary<string, Type> Types, out string Failure)
        {
            var AssetPath = PllCreatorEnvironment.GeneratedAssetRoot + "/Editor/PllCreatorReadiness.asset";
            var PrefabPath = PllCreatorEnvironment.GeneratedAssetRoot + "/Editor/PllCreatorReadiness.prefab";
            Object Transient = null;
            GameObject Root = null;
            try
            {
                AssetDatabase.DeleteAsset(AssetPath);
                AssetDatabase.DeleteAsset(PrefabPath);
                var ProfileType = Types["DayNightProfile"];
                Transient = ScriptableObject.CreateInstance(ProfileType);
                AssetDatabase.CreateAsset(Transient, AssetPath);
                Transient = null;
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
                var Reloaded = AssetDatabase.LoadAssetAtPath(AssetPath, ProfileType);
                if (!Reloaded || Reloaded.GetType() != ProfileType)
                {
                    Failure = "Unity could not save and reload a DayNightProfile facade asset.";
                    return false;
                }
                Root = new GameObject("PLL Creator readiness");
                Root.SetActive(false);
                var ComponentTypes = Contract.scripts.Where(Value =>
                        !string.Equals(Value.typeName, "DayNightProfile", StringComparison.Ordinal))
                    .Select(Value => Types[Value.typeName]).ToArray();
                foreach (var TypeValue in ComponentTypes)
                    if (!Root.AddComponent(TypeValue))
                    {
                        Failure = "Unity could not add generated component '" + TypeValue.FullName + "'.";
                        return false;
                    }
                if (!PrefabUtility.SaveAsPrefabAsset(Root, PrefabPath))
                {
                    Failure = "Unity could not save the generated component readiness prefab.";
                    return false;
                }
                Object.DestroyImmediate(Root);
                Root = null;
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
                var Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (!Prefab || GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(Prefab) != 0)
                {
                    Failure = "Unity could not reload the generated component readiness prefab.";
                    return false;
                }
                var Components = Prefab.GetComponents<Component>();
                foreach (var TypeValue in ComponentTypes)
                {
                    if (Components.Count(Component => Component && Component.GetType() == TypeValue) == 1) continue;
                    Failure = "Unity could not reload generated component '" + TypeValue.FullName + "'.";
                    return false;
                }
                Failure = string.Empty;
                return true;
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                               || Exception is SecurityException || Exception is ArgumentException
                                               || Exception is TypeLoadException || Exception is MissingMethodException
                                               || Exception is TargetInvocationException || Exception is UnityException)
            {
                Failure = "Generated source save/reload verification failed: " + Exception.Message;
                return false;
            }
            finally
            {
                if (Root) Object.DestroyImmediate(Root);
                if (Transient) Object.DestroyImmediate(Transient);
                AssetDatabase.DeleteAsset(AssetPath);
                AssetDatabase.DeleteAsset(PrefabPath);
                AssetDatabase.SaveAssets();
            }
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < PollTime) return;
            EditorApplication.update -= Poll;
            PollScheduled = false;
            PllCreatorSetup.ResumeAfterCompilation();
        }
    }
}
