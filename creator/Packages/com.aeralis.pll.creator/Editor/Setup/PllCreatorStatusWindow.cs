using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreatorStatusWindow : EditorWindow
    {
        PllCreatorInstallation.Status Inspection;
        string UnityVersion, BuildTarget, EnvironmentFailure;
        bool WindowsBuildSupport;
        Vector2 Scroll;

        [MenuItem("PLL/Setup/Status", priority = 1)]
        static void Open()
        {
            GetWindow<PllCreatorStatusWindow>("PLL Setup").minSize = new Vector2(640f, 420f);
        }

        void OnEnable()
        {
            Refresh();
        }

        internal static void RefreshOpenWindows()
        {
            foreach (var Window in Resources.FindObjectsOfTypeAll<PllCreatorStatusWindow>())
                Window.Refresh();
        }

        void Refresh()
        {
            Inspection = new PllCreatorInstallation.Status();
            UnityVersion = Application.unityVersion;
            BuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
            WindowsBuildSupport = BuildPipeline.IsBuildTargetSupported(
                BuildTargetGroup.Standalone, UnityEditor.BuildTarget.StandaloneWindows64);
            if (PllCreatorInstallation.TryValidateUnityVersion(out EnvironmentFailure))
            {
                if (!WindowsBuildSupport)
                    EnvironmentFailure = "Windows Build Support is missing. Install it through Unity Hub.";
                else if (EditorUserBuildSettings.activeBuildTarget != UnityEditor.BuildTarget.StandaloneWindows64)
                    EnvironmentFailure = "The build target is not Standalone Windows 64. Configure PLL Creator.";
            }
            Repaint();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh")) Refresh();
                using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isUpdating))
                    if (GUILayout.Button(Inspection.Manifest == null ? "Configure" : "Reconfigure"))
                        PllCreatorSetup.ConfigureFromMenu();
            }
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            EditorGUILayout.LabelField("Generated installation", Inspection.State, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(Inspection.Failure))
                EditorGUILayout.HelpBox(Inspection.Failure, MessageType.Warning);
            if (!string.IsNullOrEmpty(EnvironmentFailure))
                EditorGUILayout.HelpBox(EnvironmentFailure, MessageType.Warning);
            EditorGUILayout.LabelField("Creator package (declared)", Inspection.PackageVersion);
            EditorGUILayout.LabelField("Unity Editor", UnityVersion);
            EditorGUILayout.LabelField("Active build target", BuildTarget);
            EditorGUILayout.LabelField("Windows Build Support", WindowsBuildSupport ? "Installed" : "Missing");
            if (Inspection.Contract != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Required layers", EditorStyles.boldLabel);
                foreach (var Layer in Inspection.Contract.layers)
                    EditorGUILayout.LabelField(Layer.index.ToString(), Layer.name);
                EditorGUILayout.LabelField("Required dependency roots", EditorStyles.boldLabel);
                foreach (var Dependency in Inspection.Contract.dependencyRoots)
                    EditorGUILayout.SelectableLabel(Dependency, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
            if (Inspection.Manifest != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Generated package (recorded)", Inspection.Manifest.packageVersion);
                EditorGUILayout.LabelField("PEAK installation (recorded)");
                EditorGUILayout.SelectableLabel(Inspection.Manifest.peakRoot, EditorStyles.wordWrappedLabel,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
                EditorGUILayout.LabelField("Managed providers", EditorStyles.boldLabel);
                if (Inspection.Manifest.managedAssemblies != null)
                    foreach (var Assembly in Inspection.Manifest.managedAssemblies)
                    {
                        if (Assembly == null) continue;
                        EditorGUILayout.LabelField(Assembly.name, EditorStyles.boldLabel);
                        string Identity;
                        if (Inspection.InspectedIdentities.TryGetValue(Assembly.name ?? string.Empty, out Identity))
                            EditorGUILayout.SelectableLabel("Inspected source: " + Identity, EditorStyles.wordWrappedLabel,
                                GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
                        EditorGUILayout.SelectableLabel("Recorded source: " + Assembly.sourcePath,
                            EditorStyles.wordWrappedLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
                        EditorGUILayout.SelectableLabel("Recorded SHA-256: " + Assembly.sourceSha256,
                            EditorStyles.wordWrappedLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
                        if (!string.IsNullOrEmpty(Assembly.providerAssetPath))
                            EditorGUILayout.SelectableLabel("Project provider: " + Assembly.providerAssetPath,
                                EditorStyles.wordWrappedLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));
                    }
            }
            EditorGUILayout.HelpBox("Ready means the generated installation passed setup and compilation checks. "
                + "This view does not inspect a game profile or establish that content was tested in PEAK.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
    }
}
