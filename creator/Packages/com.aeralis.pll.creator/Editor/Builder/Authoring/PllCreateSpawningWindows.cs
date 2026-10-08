using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreatePropSpawnerWindow : EditorWindow
    {
        GameObject Parent;
        readonly List<GameObject> Candidates = new List<GameObject>();
        Vector2 Area = new Vector2(20f, 20f);
        Vector3 Position = new Vector3(0f, 10f, 0f);
        int Count = 10;
        int Minimum;
        float RayLength = 100f;

        [MenuItem("PLL/Spawning/Create Prop Spawner under Selected Source", priority = 20)]
        static void Open()
        {
            var Window = GetWindow<PllCreatePropSpawnerWindow>("Create Prop Spawner");
            Window.Parent = Selection.activeGameObject;
            Window.Candidates.Clear();
            Window.Show();
        }

        Vector2 Scroll;

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Parent = (GameObject)EditorGUILayout.ObjectField("Parent", Parent, typeof(GameObject), true);
            Area = EditorGUILayout.Vector2Field("Area", Area);
            Position = EditorGUILayout.Vector3Field("Local Ray Origin", Position);
            Minimum = EditorGUILayout.IntField("Minimum Spawns", Minimum);
            Count = EditorGUILayout.IntField("Maximum Spawns", Count);
            RayLength = EditorGUILayout.FloatField("Ray Length", RayLength);
            PllAuthoringCommands.DrawReferences("Candidate", Candidates, false);
            EditorGUILayout.HelpBox("Candidates remain persistent prefab assets. The new source owns only runtime-generated children and casts down onto Terrain and Map collision.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Parent || Candidates.Count == 0))
                if (GUILayout.Button("Create Prop Spawner"))
                {
                    try { Create(Parent, Candidates, Position, Area, Minimum, Count, RayLength); Close(); }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            EditorGUILayout.EndScrollView();
        }

        internal static PropSpawner Create(GameObject Parent, IReadOnlyList<GameObject> Candidates,
            Vector3 Position, Vector2 Area, int Minimum, int Count, float RayLength)
        {
            const string Action = "Create Prop Spawner";
            PllSourceIdentityValidator.ResolveEditableOwner(Parent, Action);
            PllSourceIdentityValidator.ValidateFinite(Position, "Prop Spawner local ray origin");
            if (Candidates.Count == 0 || Minimum < 0 || Count < 1 || Minimum > Count
                || !PllAuthoringValueContract.CanEncode(Area.x) || Area.x <= 0f
                || !PllAuthoringValueContract.CanEncode(Area.y) || Area.y <= 0f
                || !PllAuthoringValueContract.CanEncode(RayLength) || RayLength <= 0f)
                throw new InvalidOperationException("Provide prefab candidates, positive area/ray length and a valid spawn-count range.");
            foreach (var Candidate in Candidates)
            {
                PllSourceIdentityValidator.CandidatePath(Candidate, "Prop Spawner candidate");
                if (!Candidate.activeSelf)
                    throw new InvalidOperationException("Prop Spawner candidates must be active prefab roots.");
            }
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Target = new GameObject("Prop Spawner");
                Undo.RegisterCreatedObjectUndo(Target, Action);
                Target.transform.SetParent(Parent.transform, false);
                Target.transform.localPosition = Position;
                Target.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                var Source = Undo.AddComponent<PropSpawner>(Target);
                Source.props = new GameObject[Candidates.Count];
                for (var Index = 0; Index < Candidates.Count; Index++) Source.props[Index] = Candidates[Index];
                Source.area = Area; Source.nrOfSpawns = Count; Source.minSpawnCount = Minimum;
                Source.randomSpawns = Minimum != Count; Source.rayLength = RayLength;
                return Source;
            });
        }
    }

    internal sealed class PllCreateChildPropsWindow : EditorWindow
    {
        GameObject Target;
        readonly List<Transform> Points = new List<Transform>();
        readonly List<GameObject> Candidates = new List<GameObject>();
        PllChildPropTiming Timing;
        float Chance = 1f;

        [MenuItem("PLL/Spawning/Add Child Props to Selected Source", priority = 21)]
        static void Open()
        {
            var Window = GetWindow<PllCreateChildPropsWindow>("Add Child Props");
            Window.Target = Selection.activeGameObject;
            Window.Points.Clear(); Window.Candidates.Clear();
            Window.Show();
        }

        Vector2 Scroll;

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Target = (GameObject)EditorGUILayout.ObjectField("Source", Target, typeof(GameObject), true);
            Timing = (PllChildPropTiming)EditorGUILayout.EnumPopup("Timing", Timing);
            Chance = EditorGUILayout.Slider("Chance per Point", Chance, 0f, 1f);
            PllAuthoringCommands.DrawReferences("Point", Points, true);
            PllAuthoringCommands.DrawReferences("Candidate", Candidates, false);
            EditorGUILayout.HelpBox("Points are distinct empty child objects of the source. Candidates are active persistent prefab assets; no candidate is stored beneath a PropSpawner.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Target || Points.Count == 0 || Candidates.Count == 0))
                if (GUILayout.Button("Add Child Props"))
                {
                    try { Create(Target, Points, Candidates, Timing, Chance); Close(); }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            EditorGUILayout.EndScrollView();
        }

        internal static PllChildPropAuthoring Create(GameObject Target, IReadOnlyList<Transform> Points,
            IReadOnlyList<GameObject> Candidates, PllChildPropTiming Timing, float Chance)
        {
            const string Action = "Add Child Props";
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PllChildPropAuthoring>(Target, Action);
            if (!Owner.IsBiomeRoot && Target != Owner.StageRoot)
                throw new InvalidOperationException("Candidate Child Props must occupy the candidate prefab root.");
            if (!Enum.IsDefined(typeof(PllChildPropTiming), Timing)
                || !PllAuthoringValueContract.CanEncode(Chance) || Chance < 0f || Chance > 1f
                || Points.Count == 0 || Candidates.Count == 0)
                throw new InvalidOperationException("Child Props requires points, candidates, supported timing and a probability from zero to one.");
            var Seen = new HashSet<Transform>();
            foreach (var Point in Points)
            {
                if (!Point || Point == Target.transform || !Point.IsChildOf(Target.transform)
                    || !Seen.Add(Point) || Point.childCount != 0 || Point.GetComponents<Component>().Length != 1)
                    throw new InvalidOperationException("Child Prop points must be distinct empty strict descendants.");
                PllSourceIdentityValidator.RequireEditableTarget(Owner, Point, Action);
                if (!TransformOwnership.DiscoverableWhenActivated(Target.transform, Point))
                    throw new InvalidOperationException("Child Prop points must have active ancestors.");
            }
            foreach (var Candidate in Candidates)
            {
                PllSourceIdentityValidator.CandidatePath(Candidate, "Child Prop candidate");
                if (!Candidate.activeSelf)
                    throw new InvalidOperationException("Child Prop candidates must be active prefab roots.");
            }
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Source = Undo.AddComponent<PllChildPropAuthoring>(Target);
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.SetReference(Serialized, "root", Target);
                    PllAuthoringCommands.SetReferences(Serialized, "points", Points);
                    PllAuthoringCommands.SetReferences(Serialized, "props", Candidates);
                    PllAuthoringCommands.Property(Serialized, "timing").intValue = (int)Timing;
                    PllAuthoringCommands.Property(Serialized, "chancePerPoint").floatValue = Chance;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Source;
            });
        }
    }

    internal sealed class PllCreateForceSpawnerWindow : EditorWindow
    {
        GameObject Target;
        string ObjectName = "";

        [MenuItem("PLL/Spawning/Add Force Spawner to Selected Source", priority = 27)]
        static void Open()
        {
            var Window = GetWindow<PllCreateForceSpawnerWindow>("Add Force Spawner");
            Window.Target = Selection.activeGameObject;
            Window.Show();
        }

        Vector2 Scroll;

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Target = (GameObject)EditorGUILayout.ObjectField("Source", Target, typeof(GameObject), true);
            ObjectName = EditorGUILayout.TextField("Native Object Name", ObjectName);
            EditorGUILayout.HelpBox("Use an exact native object name. PEAK resolves the source during live compatibility sealing; the Creator does not copy game assets.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Target || !ForceObjectNameContract.IsValid(ObjectName)))
                if (GUILayout.Button("Add Force Spawner"))
                {
                    try { Create(Target, ObjectName); Close(); }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            EditorGUILayout.EndScrollView();
        }

        internal static PLLForceSpawner Create(GameObject Target, string ObjectName)
        {
            const string Action = "Add Force Spawner";
            PllAuthoringCommands.RequireFeatureTarget<PLLForceSpawner>(Target, Action);
            if (!ForceObjectNameContract.IsValid(ObjectName))
                throw new InvalidOperationException("Native Object Name " + ForceObjectNameContract.Requirement + ".");
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Source = Undo.AddComponent<PLLForceSpawner>(Target);
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.Property(Serialized, "objectName").stringValue = ObjectName;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Source;
            });
        }
    }
}
