using System;
using System.Collections.Generic;
using System.Linq;
using PLL.Library;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using static PLL.Creator.Editor.PllBundleBuilder;

namespace PLL.Creator.Editor
{
    internal sealed class PllSourceIdentityValidator
    {
        internal SourceIdentity Validate(PllBiomeDefinition Definition)
        {
            var Label = AssetLabel(Definition);
            ValidateCreatorDiagnostics(PllAuthoringValidation.Validate(Definition), Definition);
            var DefinitionSerialized = new SerializedObject(Definition);
            DefinitionSerialized.Update();
            var BiomeIdProperty = Property(DefinitionSerialized, "biomeId").FindPropertyRelative("value");
            BiomeId SourceId;
            if (BiomeIdProperty == null || !BiomeId.TryParse(BiomeIdProperty.stringValue, out SourceId))
                throw new InvalidOperationException("Biome definition '" + Label + "' field 'biomeId' is not a canonical lowercase qualified ID.");
            ValidateToken(Definition.ContentRevision, "Biome definition '" + Label + "' field 'contentRevision'");

            ValidateBiomeIds(Definition.Favorites, SourceId, Label, "favorites");
            ValidateBiomeIds(Definition.Overwrites, SourceId, Label, "overwrites", RejectReservedStructural: true);
            ValidateBiomeIds(Definition.Before, SourceId, Label, "before");
            ValidateBiomeIds(Definition.After, SourceId, Label, "after");
            if (Definition.SpawnChance < 0 || Definition.SpawnChance > 100)
                throw new InvalidOperationException("Biome definition '" + Label + "' field 'spawnChance' must be between 0 and 100.");
            ValidateColor(Definition.Color, "Biome definition '" + Label + "' field 'color'");
            if (!Definition.RootPrefab)
                throw new InvalidOperationException("Biome definition '" + Label + "' field 'rootPrefab' is missing.");
            if (!Definition.Anchors)
                throw new InvalidOperationException("Biome definition '" + Label + "' field 'anchors' is missing.");
            if (!Definition.DayNightProfile)
                throw new InvalidOperationException("Biome definition '" + Label + "' field 'dayNightProfile' is missing.");
            var Root = Definition.RootPrefab;
            var PrefabPath = AssetDatabase.GetAssetPath(Root);
            if (string.IsNullOrEmpty(PrefabPath) || !PrefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                || !PrefabUtility.IsPartOfPrefabAsset(Root))
                throw new InvalidOperationException("Biome definition '" + Label + "' field 'rootPrefab' must reference a prefab asset.");
            if (Root.scene.IsValid() || Root.transform.parent || !EditorUtility.IsPersistent(Root))
                throw new InvalidOperationException("Biome definition '" + Label + "' root prefab must be a parentless persistent asset root.");
            if (Root.activeSelf)
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' root prefab must be inactive so runtime clones can be prepared before Awake.");
            if (Root.GetComponentsInChildren<SpawnPoint>(true).Length != 0)
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' root prefab must not contain SpawnPoint components; PLL preserves PEAK's native player formation.");
            ValidateRootTransform(Root.transform, "Biome definition '" + Label + "' root prefab");

            var Anchors = Definition.Anchors;
            var AnchorComponents = Root.GetComponentsInChildren<PllBiomePrefabAnchors>(true);
            if (AnchorComponents.Length != 1 || AnchorComponents[0] != Anchors)
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' root prefab must own exactly the supplied PllBiomePrefabAnchors.");
            RequireOwned(Root, Anchors, Label, "anchors");
            RequireOwned(Root, Anchors.FloorRoot, Label, "anchors.floorRoot");
            RequireOwned(Root, Anchors.CampfireRoot, Label, "anchors.campfireRoot");
            RequireOwned(Root, Anchors.PlayerFormationRoot, Label, "anchors.playerFormationRoot");
            RequireOwned(Root, Anchors.PlaneAnchor, Label, "anchors.planeAnchor");
            RequireOwned(Root, Anchors.FogBeginAnchor, Label, "anchors.fogBeginAnchor");
            RequireOwned(Root, Anchors.FogCutoffAnchor, Label, "anchors.fogCutoffAnchor");
            var AnchorsSerialized = new SerializedObject(Anchors);
            AnchorsSerialized.Update();
            var StatueProperty = Property(AnchorsSerialized, "statueRoot");
            var StatueRoot = StatueProperty.objectReferenceValue as GameObject;
            if (!StatueRoot && StatueProperty.objectReferenceInstanceIDValue != 0)
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' field 'anchors.statueRoot' references a destroyed GameObject.");
            if (StatueRoot) RequireOwned(Root, StatueRoot, Label, "anchors.statueRoot");

            var Runtimes = Root.GetComponentsInChildren<CustomSegmentRuntime>(true);
            if (Runtimes.Length != 1 || Runtimes[0].gameObject != Root)
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' field 'rootPrefab' must directly own exactly one CustomSegmentRuntime.");
            var Segment = Runtimes[0];
            RequireOwned(Root, Segment.SegmentParent, Label, "rootPrefab.CustomSegmentRuntime.segmentParent");
            RequireOwned(Root, Segment.SegmentCampfire, Label, "rootPrefab.CustomSegmentRuntime.segmentCampfire");
            RequireOwned(Root, Segment.FogOrigin, Label, "rootPrefab.CustomSegmentRuntime.fogOrigin");
            RequireOwned(Root, Segment.FogCutout, Label, "rootPrefab.CustomSegmentRuntime.fogCutout");
            if (Segment.ProgressPoint == null || !Segment.ProgressPoint.transform)
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' field 'rootPrefab.CustomSegmentRuntime.progressPoint' is missing.");
            RequireOwned(Root, Segment.ProgressPoint.transform, Label, "rootPrefab.CustomSegmentRuntime.progressPoint");
            if (Segment.SegmentParent != Anchors.FloorRoot || Segment.SegmentCampfire != Anchors.CampfireRoot)
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' anchor floor/campfire roots must match CustomSegmentRuntime.");
            var Floor = Segment.SegmentParent.transform;
            var Campfire = Segment.SegmentCampfire.transform;
            if (TransformOwnership.Contains(Floor, Campfire) || TransformOwnership.Contains(Campfire, Floor))
                throw new InvalidOperationException("Biome definition '" + Label + "' floor and campfire roots must be disjoint.");

            ValidateFogAndProgress(Root, Floor, Campfire, Segment, Label);

            var Ports = Root.GetComponentsInChildren<SegmentPort>(true);
            if (Ports.Length != 3)
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' root prefab must contain exactly one Entry, Exit, and Start SegmentPort.");
            SegmentPort Entry = null, Exit = null, Start = null;
            foreach (var Port in Ports)
            {
                RequireOutsideFeatures(Root, Port.transform, Floor, Campfire,
                    "Biome definition '" + Label + "' " + Port.Kind + " SegmentPort");
                ValidateToken(Port.Connector, "Biome definition '" + Label + "' " + Port.Kind + " SegmentPort connector");
                ValidatePortPose(Root.transform, Port.transform,
                    "Biome definition '" + Label + "' " + Port.Kind + " SegmentPort");
                switch (Port.Kind)
                {
                    case SegmentPortKind.Entry:
                        if (Entry) throw DuplicatePort(Label, Port.Kind);
                        Entry = Port;
                        break;
                    case SegmentPortKind.Exit:
                        if (Exit) throw DuplicatePort(Label, Port.Kind);
                        Exit = Port;
                        break;
                    case SegmentPortKind.Start:
                        if (Start) throw DuplicatePort(Label, Port.Kind);
                        Start = Port;
                        break;
                    default:
                        throw new InvalidOperationException("Biome definition '" + Label
                            + "' contains unknown SegmentPort kind '" + Port.Kind + "'.");
                }
            }
            if (!Entry || !Exit || !Start)
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' root prefab must contain exactly one Entry, Exit, and Start SegmentPort.");
            if (!string.Equals(Start.Connector, PLLLibrary.StartConnector, StringComparison.Ordinal))
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' Start SegmentPort connector must be exactly '" + PLLLibrary.StartConnector + "'.");
            if (Start.transform != Anchors.StartAnchor)
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' field 'anchors.startAnchor' must be the Start SegmentPort transform.");
            if (!TransformOwnership.SamePose(Anchors.PlayerFormationRoot.transform, Start.transform))
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' player formation root must match the Start anchor position and rotation.");

            var Links = Root.GetComponentsInChildren<PllBiomeRoot>(true);
            if (Links.Length > 1 || Links.Length == 1 && (Links[0].gameObject != Root || Links[0].Definition != Definition))
                throw new InvalidOperationException("Biome definition '" + Label
                    + "' root prefab may contain only one direct PllBiomeRoot linked to that definition.");
            var Group = PllBiomeGroupContract.Capture(Definition);
            var Graph = DiscoverPrefabGraph(Root);
            foreach (var Node in Graph.Nodes)
            {
                if (Node.IsRoot) continue;
                var Context = "Biome definition '" + Label + "' candidate '" + Node.SourcePath + "'";
                PllBiomeGroupContract.RequireNoCarrier(Node.SourceRoot, Context);
                if (!Node.SourceRoot.activeSelf)
                    throw new InvalidOperationException(Context + " root must be active.");
                ValidateRootTransform(Node.SourceRoot.transform, Context + " root");
            }
            foreach (var Node in Graph.Nodes)
                if (!Node.IsRoot)
                    ValidateCreatorDiagnostics(
                        PllAuthoringValidation.ValidatePrefabContent(Node.SourceRoot), Node.SourceRoot);
            return new SourceIdentity(
                Definition, Label, Root, Anchors, Segment, Floor, Campfire,
                StatueRoot, Entry, Exit, Group, Graph);
        }

        static void ValidateCreatorDiagnostics(
            IReadOnlyList<PllCreatorDiagnostic> Diagnostics, Object Context)
        {
            foreach (var Diagnostic in Diagnostics)
            {
                if (Diagnostic.Severity == PllCreatorDiagnosticSeverity.Error)
                    throw new PllAuthoringFailure(Diagnostic.Message,
                        Diagnostic.Context ? Diagnostic.Context : Context, Diagnostic.PropertyPath,
                        Diagnostics: Diagnostics);
                if (Diagnostic.Severity == PllCreatorDiagnosticSeverity.Warning)
                    UnityEngine.Debug.LogWarning(Diagnostic.Message,
                        Diagnostic.Context ? Diagnostic.Context : Context);
            }
        }

        internal static T[] DiscoverGraphComponents<T>(PrefabGraph Graph) where T : Component =>
            Graph.Nodes.SelectMany(Node => Node.Components).OfType<T>().ToArray();

        internal static T[] DiscoverComponents<T>(PrefabNode Node) where T : Component =>
            Node.Components.OfType<T>().ToArray();

        internal static T[] ComponentsInNode<T>(PrefabNode Node, IReadOnlyList<T> Values) where T : Component =>
            Values.Where(Value => BelongsToNode(Node, Value)).ToArray();

        internal static bool BelongsToNode(PrefabNode Node, Component Value) =>
            Value && string.Equals(AssetDatabase.GetAssetPath(Value), Node.SourcePath, StringComparison.Ordinal);

        internal static EditablePrefabOwner ResolveEditableOwner(
            GameObject Selected, string Context)
        {
            if (!Selected || EditorUtility.IsPersistent(Selected))
                throw new InvalidOperationException(
                    Context + " requires one nonpersistent GameObject in Prefab Mode.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    Context + " is unavailable while entering or running Play Mode.");
            var Stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (Stage == null || !Stage.prefabContentsRoot)
                throw new InvalidOperationException(
                    Context + " requires the selected prefab to be open in Prefab Mode.");
            var StageRoot = Stage.prefabContentsRoot;
            if (Selected.scene != StageRoot.scene
                || !TransformOwnership.Contains(
                    StageRoot.transform, Selected.transform))
                throw new InvalidOperationException(
                    Context + " requires an object in the current Prefab Mode hierarchy.");
            var StagePath = Stage.assetPath;
            var SourceRoot = string.IsNullOrEmpty(StagePath)
                ? null : AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (!SourceRoot || !PrefabUtility.IsPartOfPrefabAsset(SourceRoot)
                || SourceRoot != AssetDatabase.LoadMainAssetAtPath(StagePath))
                throw new InvalidOperationException(
                    Context + " requires a saved prefab asset root in Prefab Mode.");

            var Links = StageRoot.GetComponentsInChildren<PllBiomeRoot>(true);
            EditablePrefabOwner Owner;
            if (Links.Length != 0)
            {
                if (Links.Length != 1 || Links[0].gameObject != StageRoot
                    || !Links[0].Definition)
                    throw new InvalidOperationException(
                        Context + " found an invalid or nested PllBiomeRoot binding.");
                var Definition = Links[0].Definition;
                if (!Definition.RootPrefab
                    || Definition.RootPrefab != SourceRoot)
                    throw new InvalidOperationException(
                        Context + " found a PllBiomeRoot linked to a different root prefab.");
                var Graph = DiscoverPrefabGraph(Definition.RootPrefab);
                PrefabNode Node;
                if (!Graph.ByPath.TryGetValue(StagePath, out Node)
                    || !Node.IsRoot)
                    throw new InvalidOperationException(
                        Context + " could not resolve the linked biome root in its prefab graph.");
                var Anchors = StageRoot.GetComponentsInChildren<
                    PllBiomePrefabAnchors>(true);
                if (Anchors.Length != 1
                    || Anchors[0].gameObject != StageRoot
                    || !Anchors[0].FloorRoot
                    || !Anchors[0].CampfireRoot
                    || !TransformOwnership.Contains(
                        StageRoot.transform,
                        Anchors[0].FloorRoot.transform)
                    || !TransformOwnership.Contains(
                        StageRoot.transform,
                        Anchors[0].CampfireRoot.transform)
                    || TransformOwnership.Overlaps(
                        Anchors[0].FloorRoot.transform,
                        Anchors[0].CampfireRoot.transform))
                    throw new InvalidOperationException(
                        Context + " requires one valid direct PllBiomePrefabAnchors binding.");
                Owner = new EditablePrefabOwner(
                    StageRoot, StageRoot, AssetLabel(Definition),
                    true, Anchors[0].FloorRoot.transform,
                    Anchors[0].CampfireRoot.transform, Graph);
            }
            else
            {
                Owner = ResolveCandidateOwner(
                    StageRoot, SourceRoot, StagePath, Context);
            }
            RequireEditableRestrictions(
                Owner, Selected.transform, Context);
            return Owner;
        }

        static EditablePrefabOwner ResolveCandidateOwner(
            GameObject StageRoot, GameObject SourceRoot,
            string StagePath, string Context)
        {
            EditablePrefabOwner Result = null;
            var DefinitionPaths = AssetDatabase.FindAssets(
                    "t:PllBiomeDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(Value => Value, StringComparer.Ordinal);
            foreach (var DefinitionPath in DefinitionPaths)
            {
                var Definition = AssetDatabase.LoadAssetAtPath<
                    PllBiomeDefinition>(DefinitionPath);
                if (!Definition || !AssetDatabase.IsMainAsset(Definition)
                    || !Definition.RootPrefab) continue;
                var RootPath = AssetDatabase.GetAssetPath(
                    Definition.RootPrefab);
                if (string.IsNullOrEmpty(RootPath)
                    || !AssetDatabase.GetDependencies(RootPath, true)
                        .Contains(StagePath, StringComparer.Ordinal)) continue;
                var Graph = DiscoverPrefabGraph(Definition.RootPrefab);
                PrefabNode Node;
                if (!Graph.ByPath.TryGetValue(StagePath, out Node)
                    || Node.IsRoot
                    || Node.SourceRoot != SourceRoot) continue;
                if (Result != null)
                    throw new InvalidOperationException(
                        Context + " found more than one biome graph owning candidate prefab '"
                            + StagePath + "'.");
                Result = new EditablePrefabOwner(
                    StageRoot, Definition.RootPrefab,
                    AssetLabel(Definition), false, null, null, Graph);
            }
            if (Result == null)
                throw new InvalidOperationException(
                    Context + " requires a linked biome root or one uniquely reachable standalone candidate prefab.");
            return Result;
        }

        internal static void RequireEditableTarget(
            EditablePrefabOwner Owner, Transform Value,
            string Context)
        {
            if (!Value || EditorUtility.IsPersistent(Value)
                || Value.gameObject.scene != Owner.StageRoot.scene
                || !TransformOwnership.Contains(
                    Owner.StageRoot.transform, Value))
                throw new InvalidOperationException(
                    Context + " is outside the current editable Prefab Mode root.");
            RequireEditableRestrictions(Owner, Value, Context);
        }

        static void RequireEditableRestrictions(
            EditablePrefabOwner Owner, Transform Value,
            string Context)
        {
            var NestedRoot = PrefabUtility.GetNearestPrefabInstanceRoot(
                Value.gameObject);
            if (NestedRoot && NestedRoot != Owner.StageRoot)
                throw new InvalidOperationException(
                    Context + " belongs to a nested prefab instance. Open that prefab directly before editing it.");
            if (TransformOwnership.HasPropSpawnerInPath(
                    Owner.StageRoot.transform, Value))
                throw new InvalidOperationException(
                    Context + " cannot belong to a PropSpawner hierarchy.");
            if (ReservedSubtree(Owner.StageRoot.transform, Value))
                throw new InvalidOperationException(
                    Context + " cannot belong to RuntimeMarkers or Presentation.");
            if (Owner.IsBiomeRoot)
                RequireFeatureOwned(
                    Value, Owner.Floor, Owner.Campfire, Context);
        }

        static PrefabGraph DiscoverPrefabGraph(GameObject Root)
        {
            var RootPath = AssetDatabase.GetAssetPath(Root);
            var Pending = new SortedSet<string>(StringComparer.Ordinal) { RootPath };
            var Nodes = new Dictionary<string, PrefabNode>(StringComparer.Ordinal);
            while (Pending.Count != 0)
            {
                var Path = Pending.Min;
                Pending.Remove(Path);
                if (Nodes.ContainsKey(Path)) continue;
                var Prefab = string.Equals(Path, RootPath, StringComparison.Ordinal)
                    ? Root : AssetDatabase.LoadAssetAtPath<GameObject>(Path);
                if (!Prefab || !PrefabUtility.IsPartOfPrefabAsset(Prefab))
                    throw new InvalidOperationException("Reachable candidate '" + Path + "' is not a prefab asset.");
                var Node = new PrefabNode(Prefab, Path, string.Equals(Path, RootPath, StringComparison.Ordinal));
                Nodes.Add(Path, Node);
                foreach (var Spawner in Node.Components.OfType<PropSpawner>())
                {
                    var Candidates = NativePrefabContracts.EffectiveProps(Spawner);
                    if (Candidates == null) continue;
                    for (var i = 0; i < Candidates.Length; i++)
                    {
                        var CandidatePath = PllSourceIdentityValidator.CandidatePath(Candidates[i], "PropSpawner candidates[" + i + "]");
                        Node.Edges.Add(new PrefabEdge(Spawner, null, i, CandidatePath));
                        if (!Nodes.ContainsKey(CandidatePath)) Pending.Add(CandidatePath);
                    }
                }
                foreach (var Child in Node.Components.OfType<PllChildPropAuthoring>())
                    for (var i = 0; i < Child.Props.Count; i++)
                    {
                        var CandidatePath = PllSourceIdentityValidator.CandidatePath(Child.Props[i], "PllChildPropAuthoring.Props[" + i + "]");
                        Node.Edges.Add(new PrefabEdge(null, Child, i, CandidatePath));
                        if (!Nodes.ContainsKey(CandidatePath)) Pending.Add(CandidatePath);
                    }
                foreach (var Zone in Node.Components.OfType<PllEntityZoneAuthoring>())
                    if (Zone.Source is PllAuthoredEntityZoneSource Authored)
                    {
                        var CandidatePath = PllSourceIdentityValidator.CandidatePath(Authored.Prefab, "Authored Entity Zone prefab");
                        Node.Edges.Add(new PrefabEdge(null, null, 0, CandidatePath, Zone));
                        if (!Nodes.ContainsKey(CandidatePath)) Pending.Add(CandidatePath);
                    }
                foreach (var Zone in Node.Components.OfType<PllHazardZoneAuthoring>())
                    if (Zone.Source is PllAuthoredHazardZoneSource Authored)
                    {
                        var CandidatePath = PllSourceIdentityValidator.CandidatePath(Authored.Prefab, "Authored Hazard Zone prefab");
                        Node.Edges.Add(new PrefabEdge(null, null, 0, CandidatePath, Zone));
                        if (!Nodes.ContainsKey(CandidatePath)) Pending.Add(CandidatePath);
                    }
            }
            var Ordered = Nodes.Values.OrderBy(Value => Value.IsRoot ? "" : Value.SourcePath,
                StringComparer.Ordinal).ToArray();
            ValidateAcyclicPrefabGraph(Ordered, Nodes);
            return new PrefabGraph(RootPath, Ordered, Nodes);
        }

        internal static string CandidatePath(GameObject Candidate, string Field)
        {
            if (!Candidate) throw new InvalidOperationException(Field + " contains a missing candidate.");
            var Path = AssetDatabase.GetAssetPath(Candidate);
            var Root = string.IsNullOrEmpty(Path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (!Root || Root != Candidate || !PrefabUtility.IsPartOfPrefabAsset(Candidate))
                throw new InvalidOperationException(Field + " must reference a prefab asset root.");
            return Path;
        }

        static void ValidateAcyclicPrefabGraph(
            IReadOnlyList<PrefabNode> Ordered, IReadOnlyDictionary<string, PrefabNode> Nodes)
        {
            var States = new Dictionary<string, byte>(StringComparer.Ordinal);
            foreach (var Node in Ordered) Visit(Node);

            void Visit(PrefabNode Node)
            {
                byte State;
                if (States.TryGetValue(Node.SourcePath, out State))
                {
                    if (State == 1)
                        throw new InvalidOperationException("Reachable candidate graph contains a cycle at '"
                            + Node.SourcePath + "'.");
                    return;
                }
                States[Node.SourcePath] = 1;
                foreach (var Path in Node.Edges.Select(Value => Value.TargetPath).Distinct(StringComparer.Ordinal))
                    Visit(Nodes[Path]);
                States[Node.SourcePath] = 2;
            }
        }

        internal static T[] DiscoverComponents<T>(GameObject Root) where T : Component
        {
            var Values = new List<T>();
            var Pending = new Stack<Transform>();
            Pending.Push(Root.transform);
            while (Pending.Count != 0)
            {
                var Current = Pending.Pop();
                foreach (var Component in Current.GetComponents<T>())
                {
                    if (!Component)
                        throw new InvalidOperationException("Prefab '" + Root.name + "' object '"
                            + PllHierarchy.NamePath(Root.transform, Current) + "' contains a missing component.");
                    Values.Add(Component);
                }
                for (var Index = Current.childCount - 1; Index >= 0; Index--)
                    Pending.Push(Current.GetChild(Index));
            }
            return Values.ToArray();
        }

        internal static void ValidateCreatorComponent(Component Component, string Context)
        {
            var Diagnostics = PllAuthoringValidation.Validate(Component);
            var Error = Diagnostics.FirstOrDefault(Value => Value.Severity == PllCreatorDiagnosticSeverity.Error);
            if (Error != null) throw new InvalidOperationException(Context + ": " + Error.Message);
        }

        internal static void RequirePrefabAsset(GameObject Value, string Context, bool RequireActive = true)
        {
            if (!Value || RequireActive && !Value.activeSelf || !EditorUtility.IsPersistent(Value)
                || !PrefabUtility.IsPartOfPrefabAsset(Value))
                throw new InvalidOperationException(Context + " must reference an active prefab asset.");
        }

        internal static void RequireFeatureOwned(Transform Value, Transform Floor, Transform Campfire, string Context)
        {
            if (!TransformOwnership.Contains(Floor, Value) && !TransformOwnership.Contains(Campfire, Value))
                throw new InvalidOperationException(Context + " must belong to FloorRoot or CampfireRoot.");
        }

        internal static bool ReservedSubtree(
            Transform Root, Transform Value)
        {
            for (var Current = Value;
                Current && Current != Root; Current = Current.parent)
                if (string.Equals(
                        Current.name, "RuntimeMarkers",
                        StringComparison.Ordinal)
                    || string.Equals(
                        Current.name, "Presentation",
                        StringComparison.Ordinal)) return true;
            return false;
        }

        static void ValidateBiomeIds(
            IReadOnlyList<BiomeId> Values, BiomeId Owner, string Definition, string Field,
            bool RejectReservedStructural = false)
        {
            var Seen = new HashSet<BiomeId>();
            foreach (var Value in Values)
            {
                if (!Value.IsValid || Value == Owner || !Seen.Add(Value))
                    throw new InvalidOperationException("Biome definition '" + Definition + "' field '" + Field
                        + "' contains an invalid, self-referential, or duplicate biome ID '" + Value + "'.");
                if (RejectReservedStructural && IsReservedStructuralBiome(Value))
                    throw new InvalidOperationException("Biome definition '" + Definition + "' field '" + Field
                        + "' contains reserved structural biome ID '" + Value + "'.");
            }
        }

        static bool IsReservedStructuralBiome(BiomeId Value) =>
            Value.Value == "peak/peak" || Value.Value == "peak/caldera" || Value.Value == "peak/gloom";

        static void ValidateFogAndProgress(
            GameObject Root, Transform Floor, Transform Campfire, CustomSegmentRuntime Segment, string Definition)
        {
            var Origin = Segment.FogOrigin;
            var Cutout = Segment.FogCutout;
            var Progress = Segment.ProgressPoint;
            RequireOutsideFeatures(Root, Origin.transform, Floor, Campfire,
                "Biome definition '" + Definition + "' fog origin");
            RequireOutsideFeatures(Root, Cutout.transform, Floor, Campfire,
                "Biome definition '" + Definition + "' fog cutout");
            RequireOutsideFeatures(Root, Progress.transform, Floor, Campfire,
                "Biome definition '" + Definition + "' progress point");
            var Origins = Origin.GetComponents<FogSphereOrigin>();
            if (Origins.Length != 1 || Origins[0] != Origin)
                throw new InvalidOperationException("Biome definition '" + Definition
                    + "' fog origin GameObject must own exactly the supplied FogSphereOrigin.");
            var FogAuthoring = Root.GetComponentsInChildren<PllFogAuthoring>(true);
            if (FogAuthoring.Length != 1 || !FogAuthoring[0] || !FogAuthoring[0].enabled
                || FogAuthoring[0].gameObject != Origin.gameObject
                || !Enum.IsDefined(typeof(PllFogMode), FogAuthoring[0].Mode))
                throw new InvalidOperationException("Biome definition '" + Definition
                    + "' must contain exactly one enabled PllFogAuthoring on its exact fog origin GameObject with a defined mode.");
            var Cutouts = Cutout.GetComponents<FogCutoutZone>();
            if (Cutouts.Length != 1 || Cutouts[0] != Cutout)
                throw new InvalidOperationException("Biome definition '" + Definition
                    + "' fog cutout GameObject must own exactly the supplied FogCutoutZone.");
            ValidateFinite(Origin.size, "Biome definition '" + Definition + "' fog origin size");
            ValidateFinite(Cutout.min, "Biome definition '" + Definition + "' fog cutout minimum");
            ValidateFinite(Cutout.max, "Biome definition '" + Definition + "' fog cutout maximum");
            ValidateFinite(Cutout.amount, "Biome definition '" + Definition + "' fog cutout amount");
            var SegmentSerialized = new SerializedObject(Segment);
            SegmentSerialized.Update();
            var ProgressProperty = Property(SegmentSerialized, "progressPoint");
            var ClipProperty = ProgressProperty.FindPropertyRelative("clip");
            if (ClipProperty == null)
                throw new InvalidOperationException("Biome definition '" + Definition
                    + "' progress point clip field is unavailable.");
            var Clip = ClipProperty.objectReferenceValue as AudioClip;
            if (!Clip && ClipProperty.objectReferenceInstanceIDValue != 0)
                throw new InvalidOperationException("Biome definition '" + Definition
                    + "' progress point references a destroyed audio clip.");
            var WallNext = UniqueTransform(Origin.transform, "Fogwall_Next", Definition);
            var WallPrevious = UniqueTransform(Origin.transform, "Fogwall_Prev", Definition);
            ValidateTransformPath(Root.transform, WallNext,
                "Biome definition '" + Definition + "' Fogwall_Next");
            ValidateTransformPath(Root.transform, WallPrevious,
                "Biome definition '" + Definition + "' Fogwall_Prev");
        }

        static Transform UniqueTransform(Transform Root, string Name, string Definition)
        {
            Transform Result = null;
            foreach (var Candidate in Root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(Candidate.name, Name, StringComparison.Ordinal)) continue;
                if (Result)
                    throw new InvalidOperationException("Biome definition '" + Definition
                        + "' fog origin contains more than one '" + Name + "' transform.");
                Result = Candidate;
            }
            if (!Result)
                throw new InvalidOperationException("Biome definition '" + Definition
                    + "' fog origin contains no '" + Name + "' transform.");
            return Result;
        }

        static void RequireOutsideFeatures(
            GameObject Root, Transform Value, Transform Floor, Transform Campfire, string Context)
        {
            RequireStableOwned(Root.transform, Value, Context);
            if (TransformOwnership.Contains(Floor, Value) || TransformOwnership.Contains(Campfire, Value))
                throw new InvalidOperationException(Context + " must remain outside the floor and campfire roots.");
        }

        static InvalidOperationException DuplicatePort(string Definition, SegmentPortKind Kind) =>
            new InvalidOperationException("Biome definition '" + Definition
                + "' root prefab contains more than one " + Kind + " SegmentPort.");

        static void ValidatePortPose(Transform Root, Transform Port, string Context)
        {
            var Position = Root.InverseTransformPoint(Port.position);
            var Rotation = Quaternion.Inverse(Root.rotation) * Port.rotation;
            ValidateFinite(Position, Context + " position");
            ValidateQuaternion(Rotation, Context + " rotation");
        }

        internal static void ValidateRootTransform(Transform Root, string Context)
        {
            ValidateFinite(Root.localPosition, Context + " position");
            ValidateQuaternion(Root.localRotation, Context + " rotation");
            ValidateFinite(Root.localScale, Context + " scale");
            if (Root.localScale.x <= 0f || Root.localScale.y <= 0f || Root.localScale.z <= 0f)
                throw new InvalidOperationException(Context + " scale must be positive.");
        }

        internal static void ValidateTransformPath(Transform Root, Transform Value, string Context)
        {
            for (var Current = Value; Current != Root; Current = Current.parent)
            {
                if (!Current || !Current.parent)
                    throw new InvalidOperationException(Context + " is not owned by the root prefab.");
                ValidateFinite(Current.localPosition, Context + " local position");
                ValidateQuaternion(Current.localRotation, Context + " local rotation");
                ValidateFinite(Current.localScale, Context + " local scale");
            }
        }

        internal static void RequireStableOwned(Transform Root, Transform Value, string Context)
        {
            if (!Value || !TransformOwnership.Contains(Root, Value))
                throw new InvalidOperationException(Context + " must be owned by the root prefab.");
            if (TransformOwnership.HasPropSpawnerInPath(Root, Value))
                throw new InvalidOperationException(Context + " cannot belong to a PropSpawner hierarchy.");
            ValidateTransformPath(Root, Value, Context);
        }

        internal static HashSet<Transform> VariationEntryRoots(IReadOnlyList<PllVariationAuthoring> Variations)
        {
            return new HashSet<Transform>(Variations.SelectMany(Variation =>
                Variation.Entries.Select(Entry => Entry == null || !Entry.Root ? null : Entry.Root.transform))
                .Where(Entry => Entry));
        }

        internal static bool DiscoverableWhenConfigured(
            Transform Root, Transform Value, HashSet<Transform> EntryRoots)
        {
            if (!TransformOwnership.Contains(Root, Value)) return false;
            for (var Current = Value; Current != Root; Current = Current.parent)
                if (!Current || !Current.gameObject.activeSelf && !EntryRoots.Contains(Current)) return false;
            return true;
        }

        internal static bool ManifestNumber(float Value) =>
            PllAuthoringValueContract.CanEncode(Value);

        internal static bool ExactManifestNumber(float Value) =>
            PllAuthoringValueContract.CanEncodeExactly(Value);

        internal static void ValidateFinite(float Value, string Context)
        {
            if (!ManifestNumber(Value)) throw new InvalidOperationException(Context + " must be finite and within the synchronized range.");
        }

        internal static void ValidateFinite(Vector3 Value, string Context)
        {
            if (!ManifestNumber(Value.x) || !ManifestNumber(Value.y) || !ManifestNumber(Value.z))
                throw new InvalidOperationException(Context + " must be finite and within the synchronized range.");
        }

        internal static void ValidateQuaternion(Quaternion Value, string Context)
        {
            if (!ManifestNumber(Value.x) || !ManifestNumber(Value.y) || !ManifestNumber(Value.z)
                || !ManifestNumber(Value.w) || Value.x == 0f && Value.y == 0f && Value.z == 0f && Value.w == 0f)
                throw new InvalidOperationException(Context
                    + " must be a finite nonzero quaternion within the synchronized range.");
        }

        internal static void ValidateColor(Color Value, string Context)
        {
            if (!ManifestNumber(Value.r) || !ManifestNumber(Value.g)
                || !ManifestNumber(Value.b) || !ManifestNumber(Value.a))
                throw new InvalidOperationException(Context + " must be finite and within the synchronized range.");
        }

        internal static PrefabNode OwnerNode(PrefabGraph Graph, Component Value, string Context)
        {
            if (!Value) throw new InvalidOperationException(Context + " is missing.");
            var Path = AssetDatabase.GetAssetPath(Value);
            PrefabNode Node;
            if (string.IsNullOrEmpty(Path) || !Graph.ByPath.TryGetValue(Path, out Node))
                throw new InvalidOperationException(Context + " is outside the reachable prefab graph.");
            return Node;
        }

        internal static void RequireDisjointRoots(IReadOnlyList<Transform> Roots, string Message)
        {
            for (var i = 0; i < Roots.Count; i++)
                for (var j = i + 1; j < Roots.Count; j++)
                    if (TransformOwnership.Overlaps(Roots[i], Roots[j])) throw new InvalidOperationException(Message);
        }

        internal static void RequireComponents<T>(
            GameObject Root, IReadOnlyList<T> Values, string Definition, string Field) where T : Component
        {
            var Seen = new HashSet<T>();
            for (var i = 0; i < Values.Count; i++)
            {
                var Value = Values[i];
                if (!Value)
                    throw new InvalidOperationException("Biome definition '" + Definition + "' field '"
                        + Field + "[" + i + "]' is missing.");
                if (!Seen.Add(Value))
                    throw new InvalidOperationException("Biome definition '" + Definition + "' field '"
                        + Field + "[" + i + "]' duplicates a component.");
                RequireOwned(Root, Value, Definition, Field + "[" + i + "]");
            }
        }

        internal static void RequireGraphComponents<T>(
            PrefabGraph Graph, IReadOnlyList<T> Values, string Definition, string Field) where T : Component
        {
            var Seen = new HashSet<T>();
            for (var i = 0; i < Values.Count; i++)
            {
                var Value = Values[i];
                var Context = "Biome definition '" + Definition + "' field '" + Field + "[" + i + "]'";
                if (!Value || !Seen.Add(Value))
                    throw new InvalidOperationException(Context + " is missing or duplicates a component.");
                var Node = OwnerNode(Graph, Value, Context);
                if (!TransformOwnership.Contains(Node.SourceRoot.transform, Value.transform))
                    throw new InvalidOperationException(Context + " is outside its source prefab root.");
                ValidateTransformPath(Node.SourceRoot.transform, Value.transform, Context);
            }
        }

        internal static void RequireObjects<T>(IReadOnlyList<T> Values, string Definition, string Field) where T : Object
        {
            for (var i = 0; i < Values.Count; i++)
                if (!Values[i]) throw new InvalidOperationException("Biome definition '" + Definition + "' field '"
                    + Field + "[" + i + "]' is missing.");
        }

        static void RequireOwned(GameObject Root, Object Value, string Definition, string Field)
        {
            if (!Value)
                throw new PllAuthoringFailure("Biome definition '" + Definition + "' field '" + Field + "' is missing.", Root, Field);
            var Component = Value as Component;
            var GameObject = Value as GameObject;
            var Transform = Component ? Component.transform : GameObject ? GameObject.transform : null;
            if (!Transform || !TransformOwnership.Contains(Root.transform, Transform))
                throw new PllAuthoringFailure("Biome definition '" + Definition + "' field '" + Field
                    + "' must be owned by root prefab '" + Root.name + "'.", Value, Field);
            RequireStableOwned(Root.transform, Transform,
                "Biome definition '" + Definition + "' field '" + Field + "'");
        }

        internal static void ValidateToken(string Value, string Context)
        {
            if (string.IsNullOrEmpty(Value) || Value.Length > 128)
                throw new InvalidOperationException(Context + " must contain 1 to 128 characters.");
            for (var i = 0; i < Value.Length; i++)
            {
                if (char.IsControl(Value[i]) || char.IsWhiteSpace(Value[i]))
                    throw new InvalidOperationException(Context + " cannot contain whitespace or control characters.");
                if (char.IsHighSurrogate(Value[i]))
                {
                    if (++i >= Value.Length || !char.IsLowSurrogate(Value[i]))
                        throw new InvalidOperationException(Context + " contains malformed UTF-16.");
                }
                else if (char.IsLowSurrogate(Value[i]))
                    throw new InvalidOperationException(Context + " contains malformed UTF-16.");
            }
        }
    }

    internal sealed class EditablePrefabOwner
    {
        internal EditablePrefabOwner(
            GameObject StageRoot, GameObject MaterialRoot,
            string Label, bool IsBiomeRoot,
            Transform Floor, Transform Campfire, PrefabGraph Graph)
        {
            this.StageRoot = StageRoot;
            this.MaterialRoot = MaterialRoot;
            this.Label = Label;
            this.IsBiomeRoot = IsBiomeRoot;
            this.Floor = Floor;
            this.Campfire = Campfire;
            this.Graph = Graph;
        }

        internal GameObject StageRoot { get; }
        internal GameObject MaterialRoot { get; }
        internal string Label { get; }
        internal bool IsBiomeRoot { get; }
        internal Transform Floor { get; }
        internal Transform Campfire { get; }
        internal PrefabGraph Graph { get; }
    }

    internal sealed class SourceIdentity
    {
        internal SourceIdentity(
            PllBiomeDefinition Definition, string Label, GameObject Root,
            PllBiomePrefabAnchors Anchors, CustomSegmentRuntime Segment,
            Transform Floor, Transform Campfire, GameObject StatueRoot,
            SegmentPort Entry, SegmentPort Exit,
            PllBiomeGroupContract Group, PrefabGraph Graph)
        {
            this.Definition = Definition;
            this.Label = Label;
            this.Root = Root;
            this.Anchors = Anchors;
            this.Segment = Segment;
            this.Floor = Floor;
            this.Campfire = Campfire;
            this.StatueRoot = StatueRoot;
            this.Entry = Entry;
            this.Exit = Exit;
            this.Group = Group;
            this.Graph = Graph;
        }

        internal PllBiomeDefinition Definition { get; }
        internal string Label { get; }
        internal GameObject Root { get; }
        internal PllBiomePrefabAnchors Anchors { get; }
        internal CustomSegmentRuntime Segment { get; }
        internal Transform Floor { get; }
        internal Transform Campfire { get; }
        internal GameObject StatueRoot { get; }
        internal SegmentPort Entry { get; }
        internal SegmentPort Exit { get; }
        internal PllBiomeGroupContract Group { get; }
        internal PrefabGraph Graph { get; }
    }
}
