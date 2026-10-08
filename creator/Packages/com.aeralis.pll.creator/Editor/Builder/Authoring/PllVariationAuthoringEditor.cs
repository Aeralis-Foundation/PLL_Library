using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PLL.Creator.Editor
{
    [CustomEditor(typeof(PllVariationAuthoring))]
    internal sealed class PllVariationAuthoringEditor : UnityEditor.Editor
    {
        GameObject NewRoot;
        bool ShowIdentity;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("id"), new GUIContent("Group ID"));
            var Entries = serializedObject.FindProperty("entries");
            ShowIdentity = EditorGUILayout.Foldout(ShowIdentity, "Advanced Identity", true);
            for (var i = 0; i < Entries.arraySize; i++)
            {
                var Entry = Entries.GetArrayElementAtIndex(i);
                var Root = Entry.FindPropertyRelative("root");
                var Weight = Entry.FindPropertyRelative("weight");
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.PropertyField(Root, new GUIContent("Entry " + (i + 1)));
                Weight.floatValue = DrawWeight(Weight.floatValue);
                EditorGUILayout.LabelField(Weight.floatValue == 0f ? "Disabled" : "Relative selection weight");
                if (ShowIdentity)
                    EditorGUILayout.LabelField("Entry ID", Entry.FindPropertyRelative("EntryIdValue").stringValue);
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("Move Up")) Entries.MoveArrayElement(i, i - 1);
                using (new EditorGUI.DisabledScope(i + 1 == Entries.arraySize))
                    if (GUILayout.Button("Move Down")) Entries.MoveArrayElement(i, i + 1);
                if (GUILayout.Button("Duplicate"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Duplicate(i);
                    GUIUtility.ExitGUI();
                }
                using (new EditorGUI.DisabledScope(Entries.arraySize <= 2))
                    if (GUILayout.Button("Remove")) Entries.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            serializedObject.ApplyModifiedProperties();
            NewRoot = (GameObject)EditorGUILayout.ObjectField("New Entry Root", NewRoot, typeof(GameObject), true);
            using (new EditorGUI.DisabledScope(!NewRoot))
                if (GUILayout.Button("Add Entry")) Add();
            if (GUILayout.Button("Prepare Entry IDs")) PrepareIds();
            PllBiomeDefinitionEditor.DrawDiagnostics(PllAuthoringValidation.Validate((PllVariationAuthoring)target));
        }

        void Add()
        {
            var Variation = (PllVariationAuthoring)target;
            if (NewRoot.transform.parent != Variation.transform)
            {
                EditorUtility.DisplayDialog("PLL Creator", "The entry root must be a direct child of the Variation Group.", "Close");
                return;
            }
            foreach (var Group in Variation.GetComponents<PllVariationAuthoring>())
                foreach (var Entry in Group.Entries)
                    if (Entry != null && Entry.Root == NewRoot)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", "The entry root already belongs to a Variation Group.", "Close");
                        return;
                    }
            serializedObject.Update();
            var Entries = serializedObject.FindProperty("entries");
            var Index = Entries.arraySize++;
            SetEntry(Entries.GetArrayElementAtIndex(Index), NewRoot, 1f);
            serializedObject.ApplyModifiedProperties();
            NewRoot = null;
        }

        void Duplicate(int Index)
        {
            var Variation = (PllVariationAuthoring)target;
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (EditorApplication.isPlayingOrWillChangePlaymode || Stage == null
                || !TransformOwnership.Contains(Stage.prefabContentsRoot.transform, Variation.transform))
            {
                EditorUtility.DisplayDialog("PLL Creator", "Open the Variation Group in Prefab Mode before duplicating an entry.", "Close");
                return;
            }
            var Entry = Variation.Entries[Index];
            if (Entry == null || !Entry.Root || Entry.Root.transform.parent != Variation.transform)
            {
                EditorUtility.DisplayDialog("PLL Creator", "Assign a direct child entry root before duplicating it.", "Close");
                return;
            }
            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Duplicate Variation Entry");
            try
            {
                var Root = Instantiate(Entry.Root, Variation.transform);
                Root.name = Entry.Root.name + " Copy";
                Undo.RegisterCreatedObjectUndo(Root, "Duplicate Variation Entry");
                serializedObject.Update();
                var Entries = serializedObject.FindProperty("entries");
                Entries.InsertArrayElementAtIndex(Index + 1);
                SetEntry(Entries.GetArrayElementAtIndex(Index + 1), Root, Entry.Weight);
                serializedObject.ApplyModifiedProperties();
                Undo.CollapseUndoOperations(Group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(Group);
                throw;
            }
        }

        void PrepareIds()
        {
            serializedObject.Update();
            var Entries = serializedObject.FindProperty("entries");
            var Seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < Entries.arraySize; i++)
            {
                var Id = Entries.GetArrayElementAtIndex(i).FindPropertyRelative("EntryIdValue");
                if (!PllAuthoringValueContract.IsVariationEntryId(Id.stringValue) || !Seen.Add(Id.stringValue))
                {
                    Id.stringValue = Guid.NewGuid().ToString("N");
                    Seen.Add(Id.stringValue);
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        internal static void SetEntry(SerializedProperty Entry, GameObject Root, float Weight)
        {
            Entry.FindPropertyRelative("root").objectReferenceValue = Root;
            Entry.FindPropertyRelative("weight").floatValue = Weight;
            Entry.FindPropertyRelative("EntryIdValue").stringValue = Guid.NewGuid().ToString("N");
        }

        internal static float DrawWeight(float Weight)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Weight");
            EditorGUI.BeginChangeCheck();
            var Slider = GUILayout.HorizontalSlider(Weight, 0f, 100f);
            if (EditorGUI.EndChangeCheck())
                Weight = (float)(Math.Round(Slider * PllAuthoringValueContract.ManifestQuantum,
                    MidpointRounding.AwayFromZero) / PllAuthoringValueContract.ManifestQuantum);
            Weight = EditorGUILayout.FloatField(Weight, GUILayout.Width(70f));
            EditorGUILayout.EndHorizontal();
            return Weight;
        }
    }

    internal sealed class PllCreateVariationWindow : EditorWindow
    {
        GameObject Parent;
        string Id = "";
        readonly List<GameObject> Roots = new List<GameObject> { null, null };
        readonly List<float> Weights = new List<float> { 1f, 1f };

        [MenuItem("PLL/Variations/Create Variation Group", priority = 30)]
        static void Open() => GetWindow<PllCreateVariationWindow>(true, "Create Variation Group").Show();

        void OnGUI()
        {
            Parent = (GameObject)EditorGUILayout.ObjectField("Parent", Parent, typeof(GameObject), true);
            Id = EditorGUILayout.TextField("Group ID", Id);
            for (var i = 0; i < Roots.Count; i++)
            {
                Roots[i] = (GameObject)EditorGUILayout.ObjectField("Entry " + (i + 1), Roots[i], typeof(GameObject), true);
                Weights[i] = PllVariationAuthoringEditor.DrawWeight(Weights[i]);
            }
            if (GUILayout.Button("Add Entry")) { Roots.Add(null); Weights.Add(1f); }
            using (new EditorGUI.DisabledScope(Roots.Count <= 2))
                if (GUILayout.Button("Remove Last Entry")) { Roots.RemoveAt(Roots.Count - 1); Weights.RemoveAt(Weights.Count - 1); }
            if (GUILayout.Button("Create Variation Group")) Create();
        }

        void Create()
        {
            try
            {
                var Stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (EditorApplication.isPlayingOrWillChangePlaymode || !Parent || Stage == null
                    || !TransformOwnership.Contains(Stage.prefabContentsRoot.transform, Parent.transform))
                    throw new InvalidOperationException("Choose a parent in the current biome Prefab Mode stage.");
                var Runtimes = Stage.prefabContentsRoot.GetComponentsInChildren<CustomSegmentRuntime>(true);
                if (Runtimes.Length != 1 || Runtimes[0].gameObject != Stage.prefabContentsRoot
                    || Stage.prefabContentsRoot.GetComponentsInChildren<PllBiomePrefabAnchors>(true).Length != 1)
                    throw new InvalidOperationException("The Prefab Mode stage must contain one complete PLL biome root.");
                PllSourceIdentityValidator.RequireStableOwned(Stage.prefabContentsRoot.transform,
                    Parent.transform, "Variation Group parent");
                if (!PllAuthoringValueContract.IsLocalId(Id))
                    throw new InvalidOperationException("Group ID must be one valid local ID.");
                foreach (var Group in Stage.prefabContentsRoot.GetComponentsInChildren<PllVariationAuthoring>(true))
                    if (Group.Id == Id || TransformOwnership.Contains(Group.transform, Parent.transform))
                        throw new InvalidOperationException("The group ID already exists or the parent belongs to another Variation Group.");
                var Seen = new HashSet<GameObject>();
                var Positive = false;
                for (var i = 0; i < Roots.Count; i++)
                {
                    if (!Roots[i] || Roots[i].transform.parent != Parent.transform || !Seen.Add(Roots[i]))
                        throw new InvalidOperationException("Assign distinct ordered entry roots that are direct children of the parent.");
                    if (!PllAuthoringValueContract.IsVariationWeight(Weights[i]))
                        throw new InvalidOperationException("Every Weight must be exactly encodable and between 0 and 100.");
                    if (Roots[i].GetComponentsInChildren<PllVariationAuthoring>(true).Length != 0
                        || Roots[i].GetComponentsInChildren<VariantObject>(true).Length != 0
                        || Roots[i].GetComponentsInChildren<VariantObjectSelector>(true).Length != 0
                        || Roots[i].GetComponentsInChildren<VariationSwapper>(true).Length != 0)
                        throw new InvalidOperationException("An entry contains a competing Variation Group or native variation component.");
                    Positive |= Weights[i] > 0f;
                }
                if (!Positive) throw new InvalidOperationException("Set at least one variation weight above zero.");
                Publish();
                Close();
            }
            catch (InvalidOperationException Failure)
            {
                EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
            }
        }

        void Publish()
        {
            Undo.IncrementCurrentGroup();
            var Group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Variation Group");
            try
            {
                var Owner = new GameObject(Id);
                Undo.RegisterCreatedObjectUndo(Owner, "Create Variation Group");
                Undo.SetTransformParent(Owner.transform, Parent.transform, "Create Variation Group");
                Owner.transform.localPosition = Vector3.zero;
                Owner.transform.localRotation = Quaternion.identity;
                Owner.transform.localScale = Vector3.one;
                var Variation = Undo.AddComponent<PllVariationAuthoring>(Owner);
                var Serialized = new SerializedObject(Variation);
                Serialized.FindProperty("id").stringValue = Id;
                var Entries = Serialized.FindProperty("entries");
                Entries.arraySize = Roots.Count;
                for (var i = 0; i < Roots.Count; i++)
                {
                    Undo.SetTransformParent(Roots[i].transform, Owner.transform, "Create Variation Group");
                    PllVariationAuthoringEditor.SetEntry(Entries.GetArrayElementAtIndex(i), Roots[i], Weights[i]);
                }
                Serialized.ApplyModifiedProperties();
                Selection.activeObject = Variation;
                Undo.CollapseUndoOperations(Group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(Group);
                throw;
            }
        }
    }
}
