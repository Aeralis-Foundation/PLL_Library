using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateClimbingFixtureWindow : EditorWindow
    {
        enum Recipe { RestPoint, HangingRope }
        Recipe Kind;
        GameObject Target;
        string Id = "rest";
        GameObject Model;
        GameObject Ghost;
        Transform Point;
        Vector3 InteractionCenter;
        Vector3 InteractionSize = new Vector3(0.35f, 0.35f, 0.35f);
        float Segments = 10f;
        float SpoolOutSeconds = 5f;
        PllRestPointProfile Profile;
        PllMountedToolModel MountedModel;
        PllRestPointPlacement Placement;
        float InsertionOffset;
        PllRopeAnchorBehavior AnchorBehavior;

        [MenuItem("PLL/Climbing/Add Rest Point or Hanging Rope to Selected Source", priority = 23)]
        static void Open()
        {
            var Window = GetWindow<PllCreateClimbingFixtureWindow>("Climbing Fixture");
            Window.Target = Selection.activeGameObject; Window.Show();
        }

        void OnGUI()
        {
            Kind = (Recipe)EditorGUILayout.EnumPopup("Native Recipe", Kind);
            Target = (GameObject)EditorGUILayout.ObjectField("Fixture Wrapper", Target, typeof(GameObject), true);
            Id = EditorGUILayout.TextField("Stable ID", Id);
            Model = (GameObject)EditorGUILayout.ObjectField("Active Model Child", Model, typeof(GameObject), true);
            Point = (Transform)EditorGUILayout.ObjectField(
                Kind == Recipe.RestPoint ? "Hold Anchor" : "Rope Anchor Point", Point, typeof(Transform), true);
            if (Kind == Recipe.RestPoint)
            {
                Profile = (PllRestPointProfile)EditorGUILayout.EnumPopup("Rest Profile", Profile);
                if (Profile == PllRestPointProfile.NativeFragileMountedTool)
                {
                    MountedModel = (PllMountedToolModel)EditorGUILayout.EnumPopup("Tool Model", MountedModel);
                    Placement = (PllRestPointPlacement)EditorGUILayout.EnumPopup("Placement", Placement);
                    if (Placement == PllRestPointPlacement.GeneratedTerrain)
                        InsertionOffset = EditorGUILayout.FloatField("Surface Normal Offset", InsertionOffset);
                }
                InteractionCenter = EditorGUILayout.Vector3Field("Interaction Center", InteractionCenter);
                InteractionSize = EditorGUILayout.Vector3Field("Interaction Size", InteractionSize);
            }
            else
            {
                Ghost = (GameObject)EditorGUILayout.ObjectField("Inactive Ghost Child", Ghost, typeof(GameObject), true);
                AnchorBehavior = (PllRopeAnchorBehavior)EditorGUILayout.EnumPopup("Anchor Behavior", AnchorBehavior);
                Segments = EditorGUILayout.Slider("Native Segments", Segments, 1f,
                    PllHangingRopeAuthoring.MaximumSegments - (AnchorBehavior == PllRopeAnchorBehavior.NativeBreakableAnchor ? 2f : 0f));
                SpoolOutSeconds = EditorGUILayout.FloatField("Spool Seconds", SpoolOutSeconds);
            }
            EditorGUILayout.HelpBox("The builder creates native mechanics after final placement. Interaction waits for occurrence view admission. Custom Rope Appearance uses the separate captured-rig command; it never replaces native rope simulation.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Target || !Model || !Point || Kind == Recipe.HangingRope && !Ghost))
                if (GUILayout.Button("Add Climbing Fixture"))
                {
                    try
                    {
                        if (Kind == Recipe.RestPoint) CreateRestPoint(Target, Id, Model, Point, InteractionCenter, InteractionSize,
                            Profile, Profile == PllRestPointProfile.Permanent ? PllMountedToolModel.Custom : MountedModel,
                            Profile == PllRestPointProfile.Permanent ? PllRestPointPlacement.FixedPose : Placement,
                            Profile == PllRestPointProfile.Permanent ? 0f : InsertionOffset);
                        else CreateHangingRope(Target, Id, Model, Ghost, Point, Segments, SpoolOutSeconds, AnchorBehavior);
                        Close();
                    }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        internal static PllRestPointAuthoring CreateRestPoint(GameObject Target, string Id,
            GameObject Model, Transform HoldAnchor, Vector3 InteractionCenter, Vector3 InteractionSize,
            PllRestPointProfile Profile = PllRestPointProfile.Permanent, PllMountedToolModel MountedModel = PllMountedToolModel.Custom,
            PllRestPointPlacement Placement = PllRestPointPlacement.FixedPose, float InsertionOffset = 0f)
        {
            const string Action = "Add Rest Point";
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PllRestPointAuthoring>(Target, Action);
            if (Profile is not (PllRestPointProfile.Permanent or PllRestPointProfile.NativeFragileMountedTool)
                || MountedModel is not (PllMountedToolModel.Custom or PllMountedToolModel.Native)
                || Placement is not (PllRestPointPlacement.FixedPose or PllRestPointPlacement.GeneratedTerrain)
                || Placement == PllRestPointPlacement.GeneratedTerrain && Profile != PllRestPointProfile.NativeFragileMountedTool
                || !PllAuthoringValueContract.CanEncode(InsertionOffset))
                throw new InvalidOperationException("Mounted tool profile, placement or surface offset is invalid.");
            if (PllClimbingFixtureShape.Failure(Target.transform, Id, Model, HoldAnchor,
                    InteractionCenter, InteractionSize, Profile == PllRestPointProfile.NativeFragileMountedTool && MountedModel == PllMountedToolModel.Native) is string Failure)
                throw new InvalidOperationException(Failure);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, Model.transform, Action);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, HoldAnchor, Action);
            if (HoldAnchor.GetComponents<Collider>().Length != 0)
                throw new InvalidOperationException("Rest Point hold anchor must not already own collision.");
            PllClimbingFixtureCompiler.ValidateNewSource(Owner.StageRoot, Target, Id);
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Source = Undo.AddComponent<PllRestPointAuthoring>(Target);
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.Property(Serialized, "IdValue").stringValue = Id;
                    PllAuthoringCommands.Property(Serialized, "ProfileValue").intValue = (int)Profile;
                    PllAuthoringCommands.Property(Serialized, "MountedModelValue").intValue = (int)MountedModel;
                    PllAuthoringCommands.Property(Serialized, "PlacementValue").intValue = (int)Placement;
                    PllAuthoringCommands.Property(Serialized, "InsertionOffsetValue").floatValue = InsertionOffset;
                    PllAuthoringCommands.SetReference(Serialized, "ModelRootValue", Model);
                    PllAuthoringCommands.SetReference(Serialized, "HoldAnchorValue", HoldAnchor);
                    PllAuthoringCommands.Property(Serialized, "InteractionCenterValue").vector3Value = InteractionCenter;
                    PllAuthoringCommands.Property(Serialized, "InteractionSizeValue").vector3Value = InteractionSize;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Source;
            });
        }

        internal static PllHangingRopeAuthoring CreateHangingRope(GameObject Target, string Id,
            GameObject Model, GameObject Ghost, Transform AnchorPoint, float Segments, float SpoolOutSeconds,
            PllRopeAnchorBehavior AnchorBehavior = PllRopeAnchorBehavior.Permanent)
        {
            const string Action = "Add Hanging Rope";
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PllHangingRopeAuthoring>(Target, Action);
            if (AnchorBehavior is not (PllRopeAnchorBehavior.Permanent or PllRopeAnchorBehavior.NativeBreakableAnchor)
                || AnchorBehavior == PllRopeAnchorBehavior.NativeBreakableAnchor && Segments + 2f > PllHangingRopeAuthoring.MaximumSegments)
                throw new InvalidOperationException("Breakable Rope must leave two native segments for its default drop.");
            if (PllClimbingFixtureShape.Failure(Target.transform, Id, Model, Ghost,
                    AnchorPoint, Segments, SpoolOutSeconds) is string Failure)
                throw new InvalidOperationException(Failure);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, Model.transform, Action);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, Ghost.transform, Action);
            PllSourceIdentityValidator.RequireEditableTarget(Owner, AnchorPoint, Action);
            PllClimbingFixtureCompiler.ValidateNewSource(Owner.StageRoot, Target, Id);
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Source = Undo.AddComponent<PllHangingRopeAuthoring>(Target);
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.Property(Serialized, "IdValue").stringValue = Id;
                    PllAuthoringCommands.Property(Serialized, "AnchorBehaviorValue").intValue = (int)AnchorBehavior;
                    PllAuthoringCommands.SetReference(Serialized, "ModelRootValue", Model);
                    PllAuthoringCommands.SetReference(Serialized, "GhostRootValue", Ghost);
                    PllAuthoringCommands.SetReference(Serialized, "AnchorPointValue", AnchorPoint);
                    PllAuthoringCommands.Property(Serialized, "SegmentsValue").floatValue = Segments;
                    PllAuthoringCommands.Property(Serialized, "SpoolOutSecondsValue").floatValue = SpoolOutSeconds;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Source;
            });
        }
    }
}
