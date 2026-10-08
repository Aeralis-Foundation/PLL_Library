using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllAuthoringVolume
    {
        internal PllZoneShape Shape;
        internal Vector3 Center;
        internal Vector3 Size = new Vector3(20f, 10f, 20f);
        internal float Radius = 10f;

        internal void Draw()
        {
            Shape = (PllZoneShape)EditorGUILayout.EnumPopup("Shape", Shape);
            Center = EditorGUILayout.Vector3Field("Local Center", Center);
            if (Shape == PllZoneShape.Box) Size = EditorGUILayout.Vector3Field("Size", Size);
            else Radius = EditorGUILayout.FloatField("Radius", Radius);
        }

        internal void RequireValid()
        {
            if (PllAuthoringValueContract.ZoneVolumeFailure(Shape, Center, Size, Radius) is { } Failure)
                throw new InvalidOperationException(Failure.Message);
        }

        internal void Apply(SerializedObject Serialized)
        {
            var Property = PllAuthoringCommands.Property(Serialized, "volume");
            Property.FindPropertyRelative("shape").intValue = (int)Shape;
            Property.FindPropertyRelative("center").vector3Value = Center;
            Property.FindPropertyRelative("size").vector3Value = Size;
            Property.FindPropertyRelative("radius").floatValue = Radius;
        }
    }

    internal sealed class PllCreatePropZoneWindow : EditorWindow
    {
        GameObject Target;
        readonly PllAuthoringVolume Volume = new PllAuthoringVolume();
        readonly List<PropSpawner> Spawners = new List<PropSpawner>();
        bool Inverted;

        [MenuItem("PLL/Spawning/Add Prop Zone to Selected Source", priority = 22)]
        static void Open()
        {
            var Window = GetWindow<PllCreatePropZoneWindow>("Add Prop Zone");
            Window.Target = Selection.activeGameObject;
            Window.Spawners.Clear();
            Window.Show();
        }

        Vector2 Scroll;

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Target = (GameObject)EditorGUILayout.ObjectField("Source", Target, typeof(GameObject), true);
            Volume.Draw();
            Inverted = EditorGUILayout.Toggle("Exclude Inside", Inverted);
            PllAuthoringCommands.DrawReferences("Spawner", Spawners, true);
            using (new EditorGUI.DisabledScope(!Target || Spawners.Count == 0))
                if (GUILayout.Button("Add Prop Zone"))
                {
                    try { Create(Target, Volume, Spawners, Inverted); Close(); }
                    catch (Exception Failure) when (Failure is InvalidOperationException or ArgumentException)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            EditorGUILayout.EndScrollView();
        }

        internal static PllPropZoneAuthoring Create(GameObject Target, PllAuthoringVolume Volume,
            IReadOnlyList<PropSpawner> Spawners, bool Inverted)
        {
            const string Action = "Add Prop Zone";
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PllPropZoneAuthoring>(Target, Action);
            Volume.RequireValid();
            if (Spawners.Count == 0)
                throw new InvalidOperationException("Prop Zone requires at least one spawner.");
            var Scope = !Owner.IsBiomeRoot ? Owner.StageRoot.transform
                : TransformOwnership.Contains(Owner.Floor, Target.transform) ? Owner.Floor : Owner.Campfire;
            var Bound = new HashSet<PropSpawner>();
            foreach (var Zone in Owner.StageRoot.GetComponentsInChildren<PllPropZoneAuthoring>(true))
                foreach (var Spawner in Zone.Spawners) Bound.Add(Spawner);
            foreach (var Spawner in Spawners)
            {
                if (!Spawner || !Bound.Add(Spawner)
                    || !TransformOwnership.Contains(Scope, Spawner.transform)
                    || TransformOwnership.Contains(Spawner.transform, Target.transform))
                    throw new InvalidOperationException("Prop Zone spawners must be distinct, unbound and in the same feature root outside the zone's ancestry.");
                var Nested = PrefabUtility.GetNearestPrefabInstanceRoot(Spawner.gameObject);
                if (Nested && Nested != Owner.StageRoot)
                    throw new InvalidOperationException("Open the spawner's nested prefab source before binding its Prop Zone.");
                if (Spawner.constraints != null)
                    foreach (var Constraint in Spawner.constraints)
                        if (Constraint is PllPropZoneConstraint || Constraint is PllNoPropSpawnConstraint)
                            throw new InvalidOperationException("The spawner already contains a generated PLL constraint.");
            }
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Source = Undo.AddComponent<PllPropZoneAuthoring>(Target);
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.SetReference(Serialized, "root", Target);
                    Volume.Apply(Serialized);
                    PllAuthoringCommands.SetReferences(Serialized, "spawners", Spawners);
                    PllAuthoringCommands.Property(Serialized, "inverted").boolValue = Inverted;
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Source;
            });
        }
    }

    internal sealed class PllCreateEffectZoneWindow : EditorWindow
    {
        GameObject Target;
        readonly PllAuthoringVolume Volume = new PllAuthoringVolume();
        readonly List<PllEffectAuthoring> Effects = new List<PllEffectAuthoring>();
        PllEffectZoneEnvironmentFog EnvironmentFog;
        string OccupancyHook = "";
        bool Looped;
        readonly List<(PllEffectZoneTransition Transition, PllFogZoneOperation Operation,
            PllFogZoneAuthoring Target)> FogTriggers = new List<(PllEffectZoneTransition, PllFogZoneOperation, PllFogZoneAuthoring)>();
        readonly List<(PllEffectZoneTransition Transition, PllNativeSourceOperation Operation,
            PLLNativeResolver Target)> NativeSourceTriggers = new List<(PllEffectZoneTransition, PllNativeSourceOperation, PLLNativeResolver)>();

        [MenuItem("PLL/Effects/Add Effect Zone to Selected Source", priority = 21)]
        static void Open()
        {
            var Window = GetWindow<PllCreateEffectZoneWindow>("Add Effect Zone");
            Window.Target = Selection.activeGameObject;
            Window.Effects.Clear(); Window.FogTriggers.Clear(); Window.NativeSourceTriggers.Clear(); Window.Looped = false;
            Window.Show();
        }

        Vector2 Scroll;

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Target = (GameObject)EditorGUILayout.ObjectField("Source", Target, typeof(GameObject), true);
            Volume.Draw();
            EnvironmentFog = (PllEffectZoneEnvironmentFog)EditorGUILayout.EnumPopup("Environment Fog", EnvironmentFog);
            OccupancyHook = EditorGUILayout.TextField("Occupancy Hook", OccupancyHook);
            PllAuthoringCommands.DrawReferences("Zone Effect", Effects, true);
            Looped = EditorGUILayout.Toggle("Looped", Looped);
            EditorGUILayout.LabelField("Fog Triggers", EditorStyles.boldLabel);
            for (var i = 0; i < FogTriggers.Count; i++)
            {
                var Trigger = FogTriggers[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    Trigger.Transition = (PllEffectZoneTransition)EditorGUILayout.EnumPopup(Trigger.Transition);
                    Trigger.Operation = (PllFogZoneOperation)EditorGUILayout.EnumPopup(Trigger.Operation);
                    Trigger.Target = (PllFogZoneAuthoring)EditorGUILayout.ObjectField(Trigger.Target, typeof(PllFogZoneAuthoring), true);
                    if (GUILayout.Button("Remove")) { FogTriggers.RemoveAt(i--); continue; }
                }
                FogTriggers[i] = Trigger;
            }
            using (new EditorGUI.DisabledScope(FogTriggers.Count + NativeSourceTriggers.Count >= PllBundleSerialization.MaximumHookedActions))
                if (GUILayout.Button("Add Fog Trigger"))
                    FogTriggers.Add((PllEffectZoneTransition.Enter, PllFogZoneOperation.Activate, null));
            EditorGUILayout.LabelField("Native Source Triggers", EditorStyles.boldLabel);
            for (var i = 0; i < NativeSourceTriggers.Count; i++)
            {
                var Trigger = NativeSourceTriggers[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    Trigger.Transition = (PllEffectZoneTransition)EditorGUILayout.EnumPopup(Trigger.Transition);
                    Trigger.Operation = (PllNativeSourceOperation)EditorGUILayout.EnumPopup(Trigger.Operation);
                    Trigger.Target = (PLLNativeResolver)EditorGUILayout.ObjectField(Trigger.Target, typeof(PLLNativeResolver), true);
                    if (GUILayout.Button("Remove")) { NativeSourceTriggers.RemoveAt(i--); continue; }
                }
                NativeSourceTriggers[i] = Trigger;
            }
            using (new EditorGUI.DisabledScope(FogTriggers.Count + NativeSourceTriggers.Count >= PllBundleSerialization.MaximumHookedActions))
                if (GUILayout.Button("Add Native Source Trigger"))
                    NativeSourceTriggers.Add((PllEffectZoneTransition.Enter, PllNativeSourceOperation.Activate, null));
            using (new EditorGUI.DisabledScope(!Target
                || Effects.Count == 0 && FogTriggers.Count == 0 && NativeSourceTriggers.Count == 0 && OccupancyHook.Length == 0
                    && EnvironmentFog != PllEffectZoneEnvironmentFog.EnvironmentalFogless))
                if (GUILayout.Button("Add Effect Zone"))
                {
                    try { Create(Target, Volume, Effects, EnvironmentFog, OccupancyHook, FogTriggers, Looped, NativeSourceTriggers); Close(); }
                    catch (Exception Failure) when (Failure is InvalidOperationException or ArgumentException)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            EditorGUILayout.EndScrollView();
        }

        internal static PllEffectZoneAuthoring Create(GameObject Target, PllAuthoringVolume Volume,
            IReadOnlyList<PllEffectAuthoring> Effects, PllEffectZoneEnvironmentFog EnvironmentFog,
            string OccupancyHook,
            IReadOnlyList<(PllEffectZoneTransition Transition, PllFogZoneOperation Operation,
                PllFogZoneAuthoring Target)> FogTriggers = null, bool Looped = false,
            IReadOnlyList<(PllEffectZoneTransition Transition, PllNativeSourceOperation Operation,
                PLLNativeResolver Target)> NativeSourceTriggers = null)
        {
            const string Action = "Add Effect Zone";
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PllEffectZoneAuthoring>(Target, Action);
            if (Target.transform == Owner.Floor || Target.transform == Owner.Campfire)
                throw new InvalidOperationException("Effect Zone requires its own child root in the biome.");
            var FogCount = FogTriggers?.Count ?? 0;
            var SourceCount = NativeSourceTriggers?.Count ?? 0;
            if ((long)FogCount + SourceCount > PllBundleSerialization.MaximumHookedActions
                || (Looped || !Owner.IsBiomeRoot) && FogCount == 0 && SourceCount == 0)
                throw new InvalidOperationException("Looped and generated EffectZones require typed fog or native source actions within the action capacity.");
            var Pairs = new HashSet<(PllEffectZoneTransition, PllFogZoneAuthoring)>();
            for (var i = 0; i < FogCount; i++)
            {
                var Trigger = FogTriggers[i];
                if (!Trigger.Target || Trigger.Transition is not (PllEffectZoneTransition.Enter or PllEffectZoneTransition.Exit)
                    || Trigger.Operation is not (PllFogZoneOperation.Activate or PllFogZoneOperation.Deactivate)
                    || !Pairs.Add((Trigger.Transition, Trigger.Target)))
                    throw new InvalidOperationException("Fog trigger has a missing, duplicate, conflicting, or unsupported target.");
                Trigger.Target.ValidateSettings();
            }
            if (SourceCount != 0)
            {
                var SourcePairs = new HashSet<(PllEffectZoneTransition, PLLNativeResolver)>();
                var BoundSources = new HashSet<PLLNativeResolver>();
                for (var i = 0; i < SourceCount; i++)
                {
                    var Trigger = NativeSourceTriggers[i];
                    if (!Trigger.Target || Trigger.Transition is not (PllEffectZoneTransition.Enter or PllEffectZoneTransition.Exit)
                        || Trigger.Operation is not (PllNativeSourceOperation.Activate or PllNativeSourceOperation.Deactivate)
                        || !SourcePairs.Add((Trigger.Transition, Trigger.Target)))
                        throw new InvalidOperationException("Native source trigger has a missing, duplicate, conflicting, or unsupported target.");
                    if (!BoundSources.Add(Trigger.Target)) continue;
                    if (TransformOwnership.Overlaps(Target.transform, Trigger.Target.transform))
                        throw new InvalidOperationException("Effect Zone cannot overlap its native source target.");
                    var IsBiomeRoot = Owner.IsBiomeRoot;
                    if (EditorUtility.IsPersistent(Trigger.Target))
                        IsBiomeRoot = PllSourceIdentityValidator.OwnerNode(Owner.Graph, Trigger.Target, "Native source trigger target").IsRoot;
                    else PllSourceIdentityValidator.RequireEditableTarget(Owner, Trigger.Target.transform, Action);
                    if (Trigger.Target.Form == PllNativeResolverForm.Source)
                        PllPresentationAuthoringValidator.ValidateFlowSource(Trigger.Target, Action);
                    else if (Trigger.Target.Form == PllNativeResolverForm.Zone && Trigger.Target.Kind == PLLNativeResolverKind.Lava
                        && IsBiomeRoot)
                        PllSourceIdentityValidator.ValidateCreatorComponent(Trigger.Target, Action);
                    else throw new InvalidOperationException("Native source trigger requires a Water or Lava Source or biome-root Lava Zone.");
                }
            }
            Volume.RequireValid();
            if (!PllAuthoringValueContract.IsDefined(EnvironmentFog)
                || OccupancyHook.Length != 0 && !PllAuthoringValueContract.IsHook(OccupancyHook)
                || Effects.Count == 0 && FogCount == 0 && SourceCount == 0 && OccupancyHook.Length == 0
                    && EnvironmentFog != PllEffectZoneEnvironmentFog.EnvironmentalFogless)
                throw new InvalidOperationException("Effect Zone requires one capability and a valid optional occupancy hook.");
            var Bound = new HashSet<PllEffectAuthoring>();
            foreach (var Zone in Owner.StageRoot.GetComponentsInChildren<PllEffectZoneAuthoring>(true))
            {
                if (TransformOwnership.Overlaps(Target.transform, Zone.transform))
                    throw new InvalidOperationException("Effect Zone roots cannot overlap.");
                foreach (var Effect in Zone.Effects) Bound.Add(Effect);
            }
            foreach (var Resolver in Owner.StageRoot.GetComponentsInChildren<PLLNativeResolver>(true))
                if (Resolver.Presentation) Bound.Add(Resolver.Presentation);
            foreach (var Effect in Effects)
            {
                if (!Effect || !Bound.Add(Effect) || Effect.Mode != PllEffectMode.Zone
                    || Effect.transform == Target.transform || !Effect.transform.IsChildOf(Target.transform))
                    throw new InvalidOperationException("Zone effects must be distinct unbound Zone-mode strict descendants.");
                PllSourceIdentityValidator.RequireEditableTarget(Owner, Effect.transform, Action);
                PllSourceIdentityValidator.ValidateCreatorComponent(Effect, Action);
            }
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Source = Undo.AddComponent<PllEffectZoneAuthoring>(Target);
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.SetReference(Serialized, "root", Target);
                    Volume.Apply(Serialized);
                    PllAuthoringCommands.SetReferences(Serialized, "effects", Effects);
                    PllAuthoringCommands.Property(Serialized, "EnvironmentFogValue").intValue = (int)EnvironmentFog;
                    PllAuthoringCommands.Property(Serialized, "OccupancyHookValue").stringValue = OccupancyHook;
                    PllAuthoringCommands.Property(Serialized, "LoopedValue").boolValue = Looped;
                    var FogActions = PllAuthoringCommands.Property(Serialized, "FogTriggersValue");
                    FogActions.arraySize = FogCount;
                    for (var i = 0; i < FogCount; i++)
                    {
                        var Row = FogActions.GetArrayElementAtIndex(i);
                        Row.FindPropertyRelative("TransitionValue").intValue = (int)FogTriggers[i].Transition;
                        Row.FindPropertyRelative("OperationValue").intValue = (int)FogTriggers[i].Operation;
                        Row.FindPropertyRelative("TargetValue").objectReferenceValue = FogTriggers[i].Target;
                    }
                    var SourceActions = PllAuthoringCommands.Property(Serialized, "NativeSourceTriggersValue");
                    SourceActions.arraySize = SourceCount;
                    for (var i = 0; i < SourceCount; i++)
                    {
                        var Row = SourceActions.GetArrayElementAtIndex(i);
                        Row.FindPropertyRelative("TransitionValue").intValue = (int)NativeSourceTriggers[i].Transition;
                        Row.FindPropertyRelative("OperationValue").intValue = (int)NativeSourceTriggers[i].Operation;
                        Row.FindPropertyRelative("TargetValue").objectReferenceValue = NativeSourceTriggers[i].Target;
                    }
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Source;
            });
        }
    }
}
