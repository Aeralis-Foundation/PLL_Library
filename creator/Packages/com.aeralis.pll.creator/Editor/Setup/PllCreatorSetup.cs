using System;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PLL.Creator.Editor
{
    [InitializeOnLoad]
    public static class PllCreatorSetup
    {
        const string SessionFailureKey = "PLL.Creator.SetupFailure";
        const string SessionResumePeakRootKey = "PLL.Creator.ResumePeakRoot";

        static PllCreatorSetup()
        {
            EditorApplication.delayCall += ResumeAfterCompilation;
        }

        [MenuItem("PLL/Setup/Configure", priority = 0)]
        internal static void ConfigureFromMenu()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("PLL Creator",
                    "Wait for Unity to finish compiling and importing, then configure again.", "Close");
                return;
            }
            var Initial = PllCreatorSetupStore.InspectExistingPeakRoot();
            var Selected = EditorUtility.OpenFolderPanel(
                "Select the PEAK installation", Initial, string.Empty);
            if (string.IsNullOrEmpty(Selected)) return;
            string Failure;
            if (!TryConfigure(Selected, out Failure))
            {
                PllCreatorStatusWindow.RefreshOpenWindows();
                EditorUtility.DisplayDialog("PLL Creator", Failure, "Close");
                return;
            }
            SessionState.EraseString(SessionFailureKey);
            PllCreatorStatusWindow.RefreshOpenWindows();
        }

        internal static bool TryConfigure(string PeakRoot, out string Failure)
        {
            if (!PllCreatorInstallation.TryValidateUnityVersion(
                    out Failure)) return false;
            string NormalizedRoot;
            if (!PllCreatorEnvironment.TryNormalizePeakRoot(
                    PeakRoot, out NormalizedRoot, out Failure)) return false;
            if (!BuildPipeline.IsBuildTargetSupported(
                    BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Failure = "Install Unity's Windows Build Support module before configuring PLL Creator.";
                return false;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                SessionState.SetString(SessionResumePeakRootKey, NormalizedRoot);
                if (EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                {
                    Failure = string.Empty;
                    return true;
                }
                SessionState.EraseString(SessionResumePeakRootKey);
                Failure = "Unity could not switch the Creator project to Standalone Windows 64.";
                return false;
            }
            return PllCreatorSetupTransaction.TryStart(NormalizedRoot, out Failure);
        }

        internal static void ResumeAfterCompilation()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (!PllCreatorCompilation.AwaitingCompletion())
                    PllCreatorCompilation.ScheduleResume();
                return;
            }
            string Failure;
            bool SetupCompleted;
            if (PllCreatorSetupTransaction.Continue(out Failure, out SetupCompleted))
            {
                if (SetupCompleted)
                {
                    SessionState.EraseString(SessionFailureKey);
                    PllCreatorStatusWindow.RefreshOpenWindows();
                }
                else if (!string.IsNullOrEmpty(Failure))
                {
                    PllCreatorStatusWindow.RefreshOpenWindows();
                    ReportFailure(Failure);
                }
                return;
            }
            var ResumedRoot = SessionState.GetString(SessionResumePeakRootKey, string.Empty);
            if (!string.IsNullOrEmpty(ResumedRoot))
            {
                SessionState.EraseString(SessionResumePeakRootKey);
                if (TryConfigure(ResumedRoot, out Failure)) return;
                ReportFailure(Failure);
                return;
            }
            if (PllCreatorInstallation.TryInspectReadyInstallation(out _, out Failure))
            {
                SessionState.EraseString(SessionFailureKey);
                return;
            }
            if (!string.IsNullOrEmpty(SessionState.GetString(SessionFailureKey, string.Empty))) return;
            ReportFailure(Failure + " Run PLL > Setup > Configure.");
        }

        static void ReportFailure(string Failure)
        {
            if (string.IsNullOrWhiteSpace(Failure)) Failure = "PLL Creator setup failed.";
            if (string.Equals(SessionState.GetString(SessionFailureKey, string.Empty),
                    Failure, StringComparison.Ordinal)) return;
            SessionState.SetString(SessionFailureKey, Failure);
            Debug.LogError(Failure);
        }
    }
}
