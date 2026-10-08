using System;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllRouteToolsWindow : EditorWindow
    {
        GameObject Geometry;
        GameObject Parent;
        Mesh LedgeMesh;
        Material LedgeMaterial;
        Vector3 LedgePosition;
        Vector3 LedgeRotation;
        Vector3 LedgeScale = Vector3.one;
        SegmentPort Port;
        Transform Anchor;
        PllBiomeDefinition PreviousBiome;
        Vector2 Scroll;

        [MenuItem("PLL/Geometry/Edit Route and Rest Ledges", priority = 24)]
        static void Open()
        {
            var Window = GetWindow<PllRouteToolsWindow>("Route and Rest Ledges");
            Window.Geometry = Selection.activeGameObject;
            Window.Parent = Selection.activeGameObject;
            Window.Show();
        }

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            EditorGUILayout.LabelField("Existing Route Geometry", EditorStyles.boldLabel);
            Geometry = (GameObject)EditorGUILayout.ObjectField("Geometry", Geometry, typeof(GameObject), true);
            if (Geometry)
            {
                EditorGUI.BeginChangeCheck();
                var Position = EditorGUILayout.Vector3Field("Local Position", Geometry.transform.localPosition);
                var Rotation = EditorGUILayout.Vector3Field("Local Rotation", Geometry.transform.localEulerAngles);
                var Scale = EditorGUILayout.Vector3Field("Local Scale", Geometry.transform.localScale);
                if (EditorGUI.EndChangeCheck())
                    Run(() => SetGeometryPose(Geometry, Position, Quaternion.Euler(Rotation), Scale));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Move")) Run(() => SelectGeometryTool(Tool.Move));
                if (GUILayout.Button("Rotate")) Run(() => SelectGeometryTool(Tool.Rotate));
                if (GUILayout.Button("Scale")) Run(() => SelectGeometryTool(Tool.Scale));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rest Ledge from Configured Assets", EditorStyles.boldLabel);
            Parent = (GameObject)EditorGUILayout.ObjectField("Parent under Floor Root", Parent, typeof(GameObject), true);
            LedgeMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", LedgeMesh, typeof(Mesh), false);
            LedgeMaterial = (Material)EditorGUILayout.ObjectField("Material", LedgeMaterial, typeof(Material), false);
            LedgePosition = EditorGUILayout.Vector3Field("Local Position", LedgePosition);
            LedgeRotation = EditorGUILayout.Vector3Field("Local Rotation", LedgeRotation);
            LedgeScale = EditorGUILayout.Vector3Field("Local Scale", LedgeScale);
            using (new EditorGUI.DisabledScope(!Parent || !LedgeMesh || !LedgeMaterial))
                if (GUILayout.Button("Create Rest Ledge"))
                    Run(() => Geometry = CreateLedge(Parent, LedgeMesh, LedgeMaterial,
                        LedgePosition, Quaternion.Euler(LedgeRotation), LedgeScale));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Exact Connector Poses", EditorStyles.boldLabel);
            Port = (SegmentPort)EditorGUILayout.ObjectField("Entry or Exit Port", Port, typeof(SegmentPort), true);
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (Port && Stage != null
                && TransformOwnership.Contains(Stage.prefabContentsRoot.transform, Port.transform))
            {
                var Root = Stage.prefabContentsRoot.transform;
                EditorGUILayout.LabelField("Connector", Port.Connector);
                EditorGUI.BeginChangeCheck();
                var Position = EditorGUILayout.Vector3Field("Root-relative Position", Root.InverseTransformPoint(Port.transform.position));
                var Rotation = EditorGUILayout.Vector3Field("Root-relative Rotation",
                    (Quaternion.Inverse(Root.rotation) * Port.transform.rotation).eulerAngles);
                if (EditorGUI.EndChangeCheck())
                    Run(() => SetPortPose(Port, Position, Quaternion.Euler(Rotation)));
            }
            Anchor = (Transform)EditorGUILayout.ObjectField("Geometry Anchor", Anchor, typeof(Transform), true);
            using (new EditorGUI.DisabledScope(!Port || !Anchor))
                if (GUILayout.Button("Snap Port to Geometry Anchor")) Run(() => SnapPort(Port, Anchor));
            PreviousBiome = (PllBiomeDefinition)EditorGUILayout.ObjectField(
                "Previous Biome", PreviousBiome, typeof(PllBiomeDefinition), false);
            using (new EditorGUI.DisabledScope(!Port || !PreviousBiome))
                if (GUILayout.Button("Match Entry Rotation to Previous Exit"))
                    Run(() => MatchEntryRotation(Port, PreviousBiome));
            EditorGUILayout.HelpBox("Connector matching uses the exact authored poses and matching IDs, with the same forward orientation. Matching a previous Exit preserves this Entry's position. Geometry and collider editing do not prove reachability, resting support or character stamina.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        void SelectGeometryTool(Tool Tool)
        {
            RequireGeometry(Geometry, "Edit Route Geometry", false);
            Selection.activeGameObject = Geometry;
            Tools.current = Tool;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        internal static void SetGeometryPose(GameObject Geometry, Vector3 Position,
            Quaternion Rotation, Vector3 Scale)
        {
            const string Action = "Edit Route Geometry";
            RequirePose(Position, Rotation, Scale, Action);
            RequireGeometry(Geometry, Action, false);
            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            Undo.RecordObject(Geometry.transform, Action);
            Geometry.transform.SetLocalPositionAndRotation(Position, Rotation);
            Geometry.transform.localScale = Scale;
            EditorSceneManager.MarkSceneDirty(Geometry.scene);
            Undo.CollapseUndoOperations(Group);
        }

        internal static GameObject CreateLedge(GameObject Parent, Mesh Mesh, Material Material,
            Vector3 Position, Quaternion Rotation, Vector3 Scale)
        {
            const string Action = "Create Rest Ledge";
            RequirePose(Position, Rotation, Scale, Action);
            if (!Mesh || !Material || !EditorUtility.IsPersistent(Mesh) || !EditorUtility.IsPersistent(Material))
                throw new InvalidOperationException("A rest ledge requires persistent mesh and material assets.");
            RequireGeometry(Parent, Action, true);
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Target = new GameObject("Rest Ledge");
                Undo.RegisterCreatedObjectUndo(Target, Action);
                Target.transform.SetParent(Parent.transform, false);
                Target.transform.SetLocalPositionAndRotation(Position, Rotation);
                Target.transform.localScale = Scale;
                Target.layer = PllClimbSurfaceAuthoringValidator.TerrainLayer;
                var Filter = Undo.AddComponent<MeshFilter>(Target);
                Filter.sharedMesh = Mesh;
                var Renderer = Undo.AddComponent<MeshRenderer>(Target);
                Renderer.sharedMaterial = Material;
                Renderer.receiveGI = ReceiveGI.LightProbes;
                PllClimbSurfaceAuthoringValidator.ConfigureStaticMeshCollider(
                    Undo.AddComponent<MeshCollider>(Target), Mesh);
                return Filter;
            }).gameObject;
        }

        static void RequireGeometry(GameObject Target, string Action, bool Parent)
        {
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Target, Action);
            if (!Owner.IsBiomeRoot || !TransformOwnership.Contains(Owner.Floor, Target.transform)
                || !Parent && (Target.transform == Owner.Floor
                    || !Target.GetComponentInChildren<Collider>(true)
                        && !Target.GetComponentInChildren<MeshFilter>(true)))
                throw new InvalidOperationException("Route geometry must be editable geometry beneath the biome's Floor Root.");
            PllClimbSurfaceAuthoringValidator.RequireStaticTarget(Target.transform, Owner, Action);
        }

        static void RequirePose(Vector3 Position, Quaternion Rotation, Vector3 Scale, string Action)
        {
            PllSourceIdentityValidator.ValidateFinite(Position, Action + " position");
            PllSourceIdentityValidator.ValidateQuaternion(Rotation, Action + " rotation");
            PllSourceIdentityValidator.ValidateFinite(Scale, Action + " scale");
            if (Scale.x <= 0f || Scale.y <= 0f || Scale.z <= 0f)
                throw new InvalidOperationException("Geometry scale must be positive on every axis.");
        }

        internal static void SetPortPose(SegmentPort Port, Vector3 Position, Quaternion Rotation)
        {
            const string Action = "Edit Connector Pose";
            PllSourceIdentityValidator.ValidateFinite(Position, "Connector position");
            PllSourceIdentityValidator.ValidateQuaternion(Rotation, "Connector rotation");
            var Owner = RequirePort(Port, Action);
            WritePortPose(Port, Owner.StageRoot.transform.TransformPoint(Position),
                Owner.StageRoot.transform.rotation * Rotation, Action);
        }

        internal static void SnapPort(SegmentPort Port, Transform Anchor)
        {
            const string Action = "Snap Connector to Geometry Anchor";
            var Owner = RequirePort(Port, Action);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, Anchor, Action);
            PllSourceIdentityValidator.ValidateTransformPath(Owner.StageRoot.transform, Anchor, Action);
            WritePortPose(Port, Anchor.position, Anchor.rotation, Action);
        }

        internal static void MatchEntryRotation(SegmentPort Entry, PllBiomeDefinition Previous)
        {
            const string Action = "Match Entry Connector Rotation";
            var Owner = RequirePort(Entry, Action);
            if (Entry.Kind != SegmentPortKind.Entry || !Previous || !Previous.RootPrefab
                || !AssetDatabase.IsMainAsset(Previous) || !PrefabUtility.IsPartOfPrefabAsset(Previous.RootPrefab))
                throw new InvalidOperationException("Select an Entry port and a saved previous biome.");
            var Exit = BiomeConnectorPorts.Capture(Previous).Exit;
            PllSourceIdentityValidator.ValidateQuaternion(Exit.transform.rotation, "Previous Exit rotation");
            if (!PllConnectorContract.Compatible(Exit.Connector, Exit.transform.rotation,
                    Entry.Connector, Exit.transform.rotation))
                throw new InvalidOperationException("The previous Exit and current Entry have different connector IDs.");
            var Root = Owner.StageRoot.transform;
            WritePortPose(Entry, Entry.transform.position,
                Root.rotation * Quaternion.Inverse(Root.localRotation) * Exit.transform.rotation, Action);
        }

        static EditablePrefabOwner RequirePort(SegmentPort Port, string Action)
        {
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!Port || Stage == null || !Stage.prefabContentsRoot
                || Port.Kind is not (SegmentPortKind.Entry or SegmentPortKind.Exit)
                || !TransformOwnership.Contains(Stage.prefabContentsRoot.transform, Port.transform))
                throw new InvalidOperationException("Select an Entry or Exit port in the current biome's Prefab Mode hierarchy.");
            var Root = Stage.prefabContentsRoot;
            var Anchors = Root.GetComponent<PllBiomePrefabAnchors>();
            if (!Anchors || !Anchors.FloorRoot)
                throw new InvalidOperationException("The current prefab has no bound Floor Root.");
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Anchors.FloorRoot, Action);
            if (!Owner.IsBiomeRoot)
                throw new InvalidOperationException("Connector editing requires a linked biome root.");
            var Nested = PrefabUtility.GetNearestPrefabInstanceRoot(Port.gameObject);
            if (Nested && Nested != Root || TransformOwnership.HasConfigurationBranch(Root.transform, Port.transform)
                || TransformOwnership.HasPropSpawnerInPath(Root.transform, Port.transform)
                || TransformOwnership.Contains(Owner.Floor, Port.transform)
                || TransformOwnership.Contains(Owner.Campfire, Port.transform))
                throw new InvalidOperationException("Connector ports must remain permanent root-owned markers outside feature roots and nested prefabs.");
            PllSourceIdentityValidator.ValidateTransformPath(Root.transform, Port.transform, Action);
            PllSourceIdentityValidator.ValidateToken(Port.Connector, "Connector ID");
            BiomeConnectorPorts.Capture(Root.GetComponent<PllBiomeRoot>().Definition, Root);
            return Owner;
        }

        static void WritePortPose(SegmentPort Port, Vector3 Position, Quaternion Rotation, string Action)
        {
            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            Undo.RecordObject(Port.transform, Action);
            Port.transform.SetPositionAndRotation(Position, Rotation);
            EditorSceneManager.MarkSceneDirty(Port.gameObject.scene);
            Undo.CollapseUndoOperations(Group);
        }

        static void Run(Action Mutation)
        {
            try { Mutation(); }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }
    }
}
