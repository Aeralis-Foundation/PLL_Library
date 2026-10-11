using System;
using System.Collections.Generic;
using System.Linq;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PLL.Creator.Editor
{
    [CustomPropertyDrawer(typeof(PllEffectZoneInteractionTrigger), true)]
    internal sealed class PllInteractionTriggerDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty Property, GUIContent Label) =>
            EditorGUIUtility.singleLineHeight * (Property.managedReferenceValue is PllEffectZoneSetBreakableEnabled ? 4f : 3f)
            + EditorGUIUtility.standardVerticalSpacing * (Property.managedReferenceValue is PllEffectZoneSetBreakableEnabled ? 3f : 2f);

        public override void OnGUI(Rect Position, SerializedProperty Property, GUIContent Label)
        {
            var Operation = (Property.managedReferenceValue as PllEffectZoneInteractionTrigger)?.Operation ?? PllPropOperation.Break;
            var Row = new Rect(Position.x, Position.y, Position.width, EditorGUIUtility.singleLineHeight);
            var Selected = (PllPropOperation)EditorGUI.EnumPopup(Row, Label, Operation);
            if (Property.managedReferenceValue is null || Operation != Selected)
            {
                var Transition = Property.FindPropertyRelative("TransitionValue")?.intValue ?? 0;
                var Target = Property.FindPropertyRelative("TargetValue")?.objectReferenceValue;
                Property.managedReferenceValue = Selected switch
                {
                    PllPropOperation.Break => new PllEffectZoneBreakProp(),
                    PllPropOperation.SetBreakableEnabled => new PllEffectZoneSetBreakableEnabled(),
                    PllPropOperation.StartMotion => new PllEffectZoneStartMotion(),
                    PllPropOperation.PauseMotion => new PllEffectZonePauseMotion(),
                    PllPropOperation.ResumeMotion => new PllEffectZoneResumeMotion(),
                    _ => throw new InvalidOperationException("Prop interaction operation is unsupported.")
                };
                Property.FindPropertyRelative("TransitionValue").intValue = Transition;
                Property.FindPropertyRelative("TargetValue").objectReferenceValue = Selected is PllPropOperation.Break or PllPropOperation.SetBreakableEnabled
                    ? Target as PllBreakablePropAuthoring : Target as PllSwingingPropAuthoring;
            }
            Row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(Row, Property.FindPropertyRelative("TransitionValue"));
            Row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(Row, Property.FindPropertyRelative("TargetValue"));
            if (Selected != PllPropOperation.SetBreakableEnabled) return;
            Row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(Row, Property.FindPropertyRelative("EnabledValue"));
        }
    }

    [CustomPropertyDrawer(typeof(PllEffectZoneActorSourceTrigger), true)]
    internal sealed class PllActorSourceTriggerDrawer : PropertyDrawer
    {
        enum TargetKind { Hazard, Entity }
        public override float GetPropertyHeight(SerializedProperty Property, GUIContent Label) =>
            EditorGUIUtility.singleLineHeight * 4f + EditorGUIUtility.standardVerticalSpacing * 3f;

        public override void OnGUI(Rect Position, SerializedProperty Property, GUIContent Label)
        {
            var Kind = Property.managedReferenceValue is PllEffectZoneEntitySourceTrigger ? TargetKind.Entity : TargetKind.Hazard;
            var Row = new Rect(Position.x, Position.y, Position.width, EditorGUIUtility.singleLineHeight);
            var Selected = (TargetKind)EditorGUI.EnumPopup(Row, Label, Kind);
            if (Property.managedReferenceValue is null || Kind != Selected)
                Property.managedReferenceValue = Selected == TargetKind.Hazard
                    ? (PllEffectZoneActorSourceTrigger)new PllEffectZoneHazardSourceTrigger() : new PllEffectZoneEntitySourceTrigger();
            Row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(Row, Property.FindPropertyRelative("TransitionValue"));
            Row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(Row, Property.FindPropertyRelative("OperationValue"));
            Row.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.PropertyField(Row, Property.FindPropertyRelative("TargetValue"));
        }
    }

    internal enum PllCreatorDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    internal sealed class PllCreatorDiagnostic
    {
        internal PllCreatorDiagnostic(PllCreatorDiagnosticSeverity Severity, string Message, Object Context, string PropertyPath = "")
        {
            this.Severity = Severity;
            this.Message = Message;
            this.Context = Context;
            this.PropertyPath = PropertyPath;
        }

        internal PllCreatorDiagnosticSeverity Severity { get; }
        internal string Message { get; }
        internal Object Context { get; }
        internal string PropertyPath { get; }
    }

    internal static class PllAuthoringValidation
    {
        internal static PllBiomeBehavior? LinkedBehavior(Component Value)
        {
            var Link = Value.GetComponentInParent<PllBiomeRoot>(true);
            return Link && Link.Definition ? Link.Definition.Behavior : null;
        }

        internal static IReadOnlyList<PllCreatorDiagnostic> Validate(PllBiomeDefinition Definition)
        {
            var Result = new List<PllCreatorDiagnostic>();
            if (!Definition)
            {
                Error(Result, "The selected biome definition is missing.", Definition);
                return Result;
            }
            if (!Definition.BiomeId.IsValid)
                Error(Result, "Biome ID must use lowercase 'namespace/name'.", Definition, "biomeId");
            if (!PllAuthoringValueContract.IsDefined(Definition.Behavior))
                Error(Result, "Behavior must be Authored or one explicit native compatibility preset.", Definition, "behavior");
            if (string.IsNullOrEmpty(Definition.ContentRevision))
                Error(Result, "Content Revision is required and is never incremented automatically.", Definition, "contentRevision");
            if (!Definition.DayNightProfile)
                Error(Result, "Assign a project-owned DayNightProfile or create one through PLL > Biomes > Create Day Night Profile.", Definition, "dayNightProfile");
            var Root = Definition.RootPrefab;
            if (!Root)
            {
                Error(Result, "Root Prefab is required. Use PLL > Biomes > Create Biome to create the stable source topology.", Definition, "rootPrefab");
                return Result;
            }
            if (Root.activeSelf)
                Error(Result, "Root Prefab must be inactive so PLL can stage mechanics before activation.", Root);
            if (!PrefabUtility.IsPartOfPrefabAsset(Root) || Root.transform.parent)
                Error(Result, "Root Prefab must be a saved prefab root, not a child object.", Root);
            var Runtimes = Root.GetComponentsInChildren<CustomSegmentRuntime>(true);
            if (Runtimes.Length != 1 || Runtimes[0].gameObject != Root)
                Error(Result, "Root Prefab must directly own exactly one CustomSegmentRuntime.", Root);
            if (Root.GetComponentsInChildren<PllFogAuthoring>(true).Length != 1)
                Error(Result, "Root Prefab must contain exactly one PllFogAuthoring on its fog origin.", Root);
            var Anchors = Root.GetComponentsInChildren<PllBiomePrefabAnchors>(true);
            if (Anchors.Length != 1 || Anchors[0] != Definition.Anchors)
                Error(Result, "Root Prefab must own exactly the PllBiomePrefabAnchors assigned by the definition.", Root);
            if (Runtimes.Length == 1 && Anchors.Length == 1
                && (Runtimes[0].SegmentParent != Anchors[0].FloorRoot
                    || Runtimes[0].SegmentCampfire != Anchors[0].CampfireRoot))
                Error(Result, "Floor Root and Campfire Root must match both CustomSegmentRuntime and PllBiomePrefabAnchors.", Root);
            if (Anchors.Length == 1)
            {
                var Statues = Root.GetComponentsInChildren<PllStatueAuthoring>(true);
                if (Statues.Length > 1 || Statues.Length == 1 && Anchors[0].StatueRoot != Statues[0].gameObject
                    || Statues.Length == 0 && Anchors[0].StatueRoot)
                    Error(Result, "Statue Root must be empty or reference the biome's one PllStatueAuthoring wrapper.", Anchors[0]);
            }
            var Links = Root.GetComponentsInChildren<PllBiomeRoot>(true);
            if (Links.Length > 1 || Links.Length == 1
                && (Links[0].gameObject != Root || Links[0].Definition != Definition))
                Error(Result, "Optional PllBiomeRoot must be unique, direct, and linked to this definition.", Root);
            var NativeVisuals = Root.GetComponentsInChildren<PLLNativeVisualResolver>(true);
            if (NativeVisuals.Length > 1 || NativeVisuals.Any(Value => Value.gameObject != Root))
                Error(Result, "PLLNativeVisualResolver is optional, unique, and directly owned by the biome root.", Root);
            var Ports = Root.GetComponentsInChildren<SegmentPort>(true);
            foreach (var Kind in new[] { SegmentPortKind.Entry, SegmentPortKind.Exit, SegmentPortKind.Start })
                if (Ports.Count(Value => Value && Value.Kind == Kind) != 1)
                    Error(Result, "Root Prefab must contain exactly one " + Kind + " SegmentPort.", Root);
            if (Root.GetComponentsInChildren<SpawnPoint>(true).Length != 0)
                Error(Result, "Remove custom SpawnPoint components. PLL relocates PEAK's existing player formation.", Root);
            ValidatePrefabContent(Root, Result);
            return Result;
        }

        internal static IReadOnlyList<PllCreatorDiagnostic> ValidatePrefabContent(GameObject Root)
        {
            var Result = new List<PllCreatorDiagnostic>();
            if (Root) ValidatePrefabContent(Root, Result);
            return Result;
        }

        internal static IReadOnlyList<PllCreatorDiagnostic> Validate(Component Component)
        {
            var Result = new List<PllCreatorDiagnostic>();
            if (Component)
            {
                Validate(Component, Result);
                if (Component is PllEffectZoneAuthoring)
                    ValidateEffectZoneActions(Component.transform.root.gameObject, Result);
            }
            return Result;
        }

        internal static IReadOnlyList<PllCreatorDiagnostic> Validate(
            PllCustomVisualAuthoring Visual, out CustomVisualParticleGraph Graph)
        {
            var Result = new List<PllCreatorDiagnostic>();
            Graph = Visual ? ValidateCustomVisual(Visual, Result) : null;
            return Result;
        }

        static void Validate(Component Component, ICollection<PllCreatorDiagnostic> Result, HashSet<string> ActorIds = null)
        {
            var Campfire = Component as PllCampfireAuthoring;
            if (Campfire) ValidateCampfire(Campfire, Result);
            else if (Component is PllCollisionAuthoring Collision)
            {
                if (!Collision.enabled || Collision.Mode is not
                    (PllCollisionMode.Automatic or PllCollisionMode.Authored or PllCollisionMode.None))
                    Error(Result, "Collision requires an enabled component and a valid mode.", Collision);
            }
            else if (Component is PllClimbSurfaceAuthoring)
                ValidateClimbSurface((PllClimbSurfaceAuthoring)Component, Result);
            else if (Component is PllClimbSurfaceMaterialAuthoring)
                ValidateClimbSurfaceMaterials(
                    (PllClimbSurfaceMaterialAuthoring)Component, Result);
            else if (Component is PllBiomeGroupAuthoring)
                ValidateBiomeGroup((PllBiomeGroupAuthoring)Component, Result);
            else if (Component is ClimbModifierSurface)
                Error(Result, "Remove creator-authored ClimbModifierSurface. Use PllClimbSurfaceAuthoring.", Component);
            else if (Component is PllStatueAuthoring) ValidateStatue((PllStatueAuthoring)Component, Result);
            else if (Component is PllRestPointAuthoring or PllHangingRopeAuthoring)
            {
                try { PllClimbingFixtureCompiler.ValidateComponent(Component); }
                catch (InvalidOperationException Failure) { Error(Result, Failure.Message, Component); }
            }
            else if (Component is PllBreakablePropAuthoring or PllSwingingPropAuthoring or PllVineAuthoring)
            {
                if (PllPropInteractionValueContract.Failure(Component.transform,
                    Component.GetComponent<PllBreakablePropAuthoring>(), Component.GetComponent<PllSwingingPropAuthoring>(),
                    Component.GetComponent<PllVineAuthoring>()) is { } Failure) Error(Result, Failure, Component);
            }
            else if (Component is PllHazardAuthoring Hazard)
            {
                if (PllHazardValueContract.Failure(Hazard) is string Failure) Error(Result, Failure, Hazard);
            }
            else if (Component is PllEntityAuthoring Entity)
            {
                if (PllEntityValueContract.Failure(Entity) is string Failure) Error(Result, Failure, Entity);
            }
            else if (Component is PllLuggageAuthoring) ValidateLuggage((PllLuggageAuthoring)Component, Result);
            else if (Component is PllAmuletSpawnerAuthoring Amulet)
            {
                var Anchors = Amulet.transform.root.GetComponentInChildren<PllBiomePrefabAnchors>(true);
                if (!Anchors || !Anchors.FloorRoot)
                    Error(Result, "Amulet Spawner needs biome anchors with a Floor Root.", Amulet);
                else
                    try
                    {
                        PllGenerationAuthoringValidator.ValidateAmulet(Amulet, Anchors.FloorRoot.transform);
                        if (Amulet.Category == PllAmuletOpportunityCategory.InheritBiome)
                        {
                            if (LinkedBehavior(Amulet) is { } Behavior)
                                PllAmuletSpawnerAuthoring.ResolveCategory(Amulet.Category, Behavior);
                            else Info(Result, "Use Validate Selected Biome to check the inherited amulet category.", Amulet);
                        }
                    }
                    catch (Exception Failure) when (Failure is ArgumentException or InvalidOperationException)
                    { Error(Result, Failure.Message, Amulet); }
            }
            else if (Component is PllEffectAuthoring) ValidateEffect((PllEffectAuthoring)Component, Result);
            else if (Component is PllVisualAuthoring) ValidateVisual((PllVisualAuthoring)Component, Result);
            else if (Component is PllAtmosphereAuthoring)
                ValidateAtmosphere((PllAtmosphereAuthoring)Component, Result);
            else if (Component is NativeModifiers Modifiers)
            {
                try { NativeModifiers.Capture(Modifiers.transform.root); }
                catch (ArgumentException Failure) { Error(Result, Failure.Message, Modifiers); }
            }
            else if (Component is PllNativeMaterialAuthoring Material)
            {
                try { Material.Capture(); }
                catch (ArgumentException Failure) { Error(Result, Failure.Message, Material); }
            }
            else if (Component is PllNativeSpawnerAuthoring Spawner)
            {
                try { Spawner.Capture(); }
                catch (ArgumentException Failure) { Error(Result, Failure.Message, Spawner); }
            }
            else if (Component is PllCustomVisualAuthoring)
                ValidateCustomVisual((PllCustomVisualAuthoring)Component, Result);
            else if (Component is PllSoundEmitterAuthoring) ValidateEmitter((PllSoundEmitterAuthoring)Component, Result);
            else if (Component is PllBiomeStingerAuthoring)
                ValidateStinger((PllBiomeStingerAuthoring)Component, Result);
            else if (Component is PllFogAuthoring) ValidateFog((PllFogAuthoring)Component, Result);
            else if (Component is PllVariationAuthoring) ValidateVariation((PllVariationAuthoring)Component, Result);
            else if (Component is PllFogZoneAuthoring Fog)
            {
                try { Fog.ValidateSettings(); }
                catch (ArgumentException Failure) { Error(Result, Failure.Message, Fog); }
            }
            else if (Component is PllEffectZoneAuthoring) ValidateEffectZone((PllEffectZoneAuthoring)Component, Result);
            else if (Component is PllPropZoneAuthoring) ValidatePropZone((PllPropZoneAuthoring)Component, Result);
            else if (Component is PllEntityZoneAuthoring or PllHazardZoneAuthoring)
            {
                try { PllActorZoneAuthoringValidator.ValidateComponent(Component, ActorIds); }
                catch (InvalidOperationException Failure) { Error(Result, Failure.Message, Component); }
            }
            else if (Component is PLLNativeResolver) ValidateResolver((PLLNativeResolver)Component, Result);
            else if (Component is PLLNativeVisualResolver)
                ValidateNativeVisualResolver((PLLNativeVisualResolver)Component, Result);
            else if (Component is PLLNoPropSpawn NoProp && !NoProp.enabled)
                Error(Result, "No Prop Spawn must be enabled.", NoProp);
            else if (Component is PllChildPropAuthoring) ValidateChildProps((PllChildPropAuthoring)Component, Result);
            else if (Component is PllItemSpawnerAuthoring) ValidateItemSpawner((PllItemSpawnerAuthoring)Component, Result);
            else if (Component is PLLForceSpawner) ValidateForce((PLLForceSpawner)Component, Result);
            else if (Component is PLLHookedProp) ValidateHooked((PLLHookedProp)Component, Result);
            else if (Component is PllHookedPropGenerationAuthoring Generation)
            {
                if (!PllAuthoringValueContract.IsHook(Generation.EmitHook))
                    Error(Result, "Emit Hook must contain one valid hook.", Generation, "EmitHookValue");
                var Anchors = Generation.GetComponentInParent<PllBiomePrefabAnchors>(true);
                if (!Anchors || !Anchors.FloorRoot || !Anchors.CampfireRoot)
                    Error(Result, "Hooked Generation requires valid biome anchors.", Generation);
                else
                    try
                    {
                        PllHookedPropGenerationSourceContract.ValidateMarker(Anchors.transform,
                            Anchors.FloorRoot.transform, Anchors.CampfireRoot.transform, Generation, false,
                            PllHookedPropGenerationSourceContract.UnstableRoots(Anchors.transform));
                    }
                    catch (ArgumentException Failure) { Error(Result, Failure.Message, Generation); }
            }
            else if (Component is PLLCriticalGeneration Critical)
            {
                try
                {
                    var Anchors = Critical.GetComponentInParent<PllBiomePrefabAnchors>(true);
                    if (!Anchors || !Anchors.FloorRoot || !Anchors.CampfireRoot)
                        throw new InvalidOperationException("Critical Generation requires valid biome anchors.");
                    PllCriticalGenerationContract.ValidateMarker(Critical, "Critical Generation");
                    PllCriticalGenerationContract.ValidateScope(Anchors, Critical.transform, "Critical Generation", true);
                }
                catch (InvalidOperationException Failure) { Error(Result, Failure.Message, Critical); }
            }
            else if (Component is PLLFinalGeneration && !Component.gameObject.activeSelf)
                Error(Result, "Final-generation source targets stay active; only the staged copy becomes inactive.", Component);
        }

        static void ValidatePrefabContent(GameObject Root, ICollection<PllCreatorDiagnostic> Result)
        {
            ValidateLighting(Root, Result);
            var ActorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Component in Root.GetComponentsInChildren<MonoBehaviour>(true))
                Validate(Component, Result, ActorIds);
            ValidateEffectZoneActions(Root, Result);
        }

        static void ValidateAtmosphere(
            PllAtmosphereAuthoring Atmosphere,
            ICollection<PllCreatorDiagnostic> Result)
        {
            try
            {
                PllAtmosphereAuthoringValidator.ValidateComponent(
                    Atmosphere.transform.root.gameObject,
                    Atmosphere,
                    "Atmosphere authoring");
            }
            catch (InvalidOperationException Failure)
            {
                Error(Result, Failure.Message, Atmosphere);
            }
        }

        static void ValidateBiomeGroup(
            PllBiomeGroupAuthoring Value,
            ICollection<PllCreatorDiagnostic> Result)
        {
            var Link = Value.GetComponent<PllBiomeRoot>();
            if (!Link || !Link.Definition)
            {
                Error(Result,
                    "Biome groups require a direct PllBiomeRoot linked to their owner definition.",
                    Value);
                return;
            }
            try
            {
                var Group = PllBiomeGroupContract.Capture(Link.Definition);
                if (Group == null || !ReferenceEquals(Group.Carrier, Value))
                    Error(Result,
                        "Biome group carrier does not match its owner definition.",
                        Value);
            }
            catch (InvalidOperationException Failure)
            {
                Error(Result, Failure.Message, Value);
            }
        }

        static void ValidateClimbSurface(
            PllClimbSurfaceAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            if (!Value.enabled || Value.GetComponents<PllClimbSurfaceAuthoring>().Length != 1)
                Error(Result, "Climb surface requires exactly one enabled marker.", Value);
            if (!PllClimbSurfaceAuthoringValidator.ValidValues(
                    Value.Mode, Value.EmitHook,
                    Value.StaminaUsageMultiplier,
                    Value.SpeedMultiplier))
                Error(Result,
                    "Climb surfaces require a valid mode and hook pair plus nonnegative values exactly representable to four decimal places.",
                    Value);
            if (Value.gameObject.layer != PllClimbSurfaceAuthoringValidator.TerrainLayer
                && Value.gameObject.layer != PllClimbSurfaceAuthoringValidator.MapLayer)
                Error(Result, "Use Terrain layer 20 or Map layer 21.", Value);
            if (!Value.GetComponents<Collider>().Any(Collider => Collider && Collider.enabled && !Collider.isTrigger))
                Error(Result, "Add an enabled non-trigger Collider to this GameObject.", Value);
        }

        static void ValidateClimbSurfaceMaterials(
            PllClimbSurfaceMaterialAuthoring Value,
            ICollection<PllCreatorDiagnostic> Result)
        {
            var Root = Value.GetComponent<PllBiomeRoot>();
            if (!Value.enabled
                || Value.GetComponents<PllClimbSurfaceMaterialAuthoring>().Length != 1
                || !Root || Root.transform != Value.transform)
                Error(Result,
                    "Climb material rules require one enabled carrier directly on the biome root.",
                    Value);
            if (Value.Rules.Count == 0)
                Error(Result, "Add at least one climb material rule.", Value);
            var Materials = new HashSet<PhysicsMaterial>();
            for (var Index = 0; Index < Value.Rules.Count; Index++)
            {
                var Rule = Value.Rules[Index];
                if (Rule == null || !Rule.PhysicsMaterial
                    || !Materials.Add(Rule.PhysicsMaterial))
                {
                    Error(Result,
                        "Each climb material rule requires one unique PhysicsMaterial.",
                        Value);
                    continue;
                }
                if (!PllClimbSurfaceAuthoringValidator.ValidMaterial(
                        Rule.PhysicsMaterial))
                    Error(Result,
                        "Each climb material rule needs a saved PhysicsMaterial with finite friction and bounciness.",
                        Value);
                if (!PllClimbSurfaceAuthoringValidator.ValidValues(
                        Rule.Mode, Rule.EmitHook,
                        Rule.StaminaUsageMultiplier,
                        Rule.SpeedMultiplier))
                    Error(Result,
                        "Each climb material rule requires a valid mode and hook pair plus nonnegative values exactly representable to four decimal places.",
                        Value);
            }
        }
        static void ValidateCampfire(PllCampfireAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            RequireOwnRoot(Value, Value.Root, Result);
            RequireStrictChild(Value, Value.ModelRoot, "Model Root", Result);
            RequireOwned(Value, Value.FireParticles, "Fire Particles", Result);
            RequireOwned(Value, Value.SmokeParticlesOff, "Smoke Particles Off", Result);
            RequireOwned(Value, Value.SmokeParticlesLit, "Smoke Particles Lit", Result);
            RequireStrictChild(Value, Value.LogRoot, "Log Root", Result);
            if (!Value.FireParticles || !Value.SmokeParticlesOff || !Value.SmokeParticlesLit
                || Value.FireParticles == Value.SmokeParticlesOff
                || Value.FireParticles == Value.SmokeParticlesLit
                || Value.SmokeParticlesOff == Value.SmokeParticlesLit)
                Error(Result, "Fire, unlit smoke, and lit smoke must be three distinct owned ParticleSystems.", Value);
            else
            {
                if (!ExactCampfireParticleState(Value.FireParticles, false, true))
                    Error(Result, "Fire Particles must be inactive, stopped and cleared, with Play On Awake enabled.", Value);
                if (!ExactCampfireParticleState(Value.SmokeParticlesOff, true, true))
                    Error(Result, "Smoke Particles Off must be active, stopped and cleared, with Play On Awake enabled.", Value);
                if (!ExactCampfireParticleState(Value.SmokeParticlesLit, true, false))
                    Error(Result, "Smoke Particles Lit must be active, stopped and cleared, with Play On Awake disabled.", Value);
            }
            if (Value.LogRoot && Value.ModelRoot && (Value.ModelRoot.transform == Value.LogRoot
                    || !TransformOwnership.Contains(Value.LogRoot, Value.ModelRoot.transform)))
                Error(Result, "Model Root must belong to Log Root.", Value);
            if (Value.EnableWhenLit != (Value.FireParticles ? Value.FireParticles.gameObject : null)
                || Value.DisableWhenLit != (Value.SmokeParticlesOff ? Value.SmokeParticlesOff.gameObject : null))
                Error(Result, "Enable When Lit must be Fire Particles and Disable When Lit must be Smoke Particles Off.", Value);
            RequireRendererAndCollider(Value, Value.ModelRoot, Result);
            if (Value.ModelRoot && !Value.ModelRoot.GetComponentsInChildren<Collider>(true).Any(Collider =>
                    Collider && Collider.enabled && !Collider.isTrigger
                    && TransformOwnership.DiscoverableWhenActivated(Value.ModelRoot.transform, Collider.transform)))
                Error(Result, "Model Root must contain an active-relative enabled non-trigger Collider.", Value);
            var Serialized = new SerializedObject(Value);
            Serialized.Update();
            var LoopProperty = PllAuthoringCommands.Property(Serialized, "loop");
            var Loop = LoopProperty.objectReferenceValue as AudioSource;
            if (!Loop && LoopProperty.objectReferenceInstanceIDValue != 0)
                Error(Result, "Loop references a destroyed AudioSource.", Value);
            else if (Loop && (!TransformOwnership.Contains(Value.transform, Loop.transform)
                    || !TransformOwnership.DiscoverableWhenActivated(Value.transform, Loop.transform)
                    || !Loop.enabled || !Loop.clip || !Loop.loop || !Loop.playOnAwake
                    || !Loop.volume.Equals(0f) || Loop.isPlaying || Loop.timeSamples != 0))
                Error(Result, "Loop must be an owned active enabled AudioSource with a clip, looping Play On Awake, zero volume, and stopped sample-zero preview state.", Value);
            RejectRawMechanics(Value, Result, Component => Component is global::Campfire or Photon.Pun.PhotonView);
        }

        static bool ExactCampfireParticleState(ParticleSystem Value, bool Active, bool PlayOnAwake) =>
            Value && Value.gameObject.activeSelf == Active && Value.main.playOnAwake == PlayOnAwake
            && Value.isStopped && Value.particleCount == 0;

        static void ValidateStatue(PllStatueAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            RequireStrictChild(Value, Value.ModelRoot, "Model Root", Result);
            RequireStrictChild(Value, Value.SpawnPoint, "Spawn Point", Result);
            if (Value.SkeletonRoot) RequireStrictChild(Value, Value.SkeletonRoot, "Skeleton Root", Result);
            if (string.IsNullOrEmpty(Value.DisplayName)) Error(Result, "Display Name is required.", Value);
            if (!FinitePositive(Value.OpenTime)) Error(Result, "Open Time must be finite and greater than zero.", Value);
            RequireRendererAndCollider(Value, Value.ModelRoot, Result);
            RejectRawMechanics(Value, Result, Component => Component is Luggage or Photon.Pun.PhotonView);
        }

        static void ValidateLuggage(PllLuggageAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            try
            {
                PllFixtureAuthoringValidator.ValidateLuggageComponent(
                    Value, LinkedBehavior(Value), "Luggage authoring");
            }
            catch (InvalidOperationException Failure)
            {
                Error(Result, Failure.Message, Value);
            }
        }
        static void ValidateTransition(Component Value, PllPresentationTransition Transition,
            ICollection<PllCreatorDiagnostic> Result)
        {
            if (PllAuthoringValueContract.TransitionFailure(Transition.CoverDelaySeconds) is { } Failure)
                Error(Result, Failure, Value, "TransitionValue.SecondsValue");
        }

        static void ValidateEffect(PllEffectAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            RequireOwnRoot(Value, Value.Root, Result);
            ValidateTransition(Value, Value.Transition, Result);
            if (Value.Transition.CoverDelaySeconds > 0f && Value.Root)
            {
                foreach (var Particle in Value.Root.GetComponentsInChildren<ParticleSystem>(true))
                    if (PllAuthoringValueContract.ParticleTransitionFailure(Particle) is { } Failure)
                        Error(Result, Failure, Particle);
            }
            try { PllEffectAppearanceValueContract.Validate(Value, Value.transform.root); }
            catch (InvalidOperationException Failure) { Error(Result, Failure.Message, Value); }
            if (Value.Mode != PllEffectMode.Timed && Value.Mode != PllEffectMode.EmitHook
                && Value.Mode != PllEffectMode.Zone)
                Error(Result, "Mode must be Timed, Emit Hook, or Zone.", Value);
            if (Value.Mode == PllEffectMode.EmitHook && !PllAuthoringValueContract.IsHook(Value.EmitHook))
                Error(Result, "Hook must contain 1 to 128 UTF-16 code units and no whitespace, control character, '\\', '>', or malformed surrogate pair.", Value);
            else if (Value.Mode == PllEffectMode.EmitHook && !LooksNamespaced(Value.EmitHook))
                Warning(Result, "Prefix the hook with the author and biome, for example 'aurora/crystal-caves/breakable-vine'.", Value);
            if (Value.Mode != PllEffectMode.EmitHook && !string.IsNullOrEmpty(Value.EmitHook))
                Error(Result, "Emit Hook is used only by EmitHook effects.", Value);
            if (!float.IsFinite(Value.Duration) || Value.Duration < 0f)
                Error(Result, "Duration must be finite and nonnegative.", Value);
            else if (Value.Mode == PllEffectMode.Timed && Value.Duration <= 0f)
                Error(Result, "Timed effects require a positive Duration.", Value);
            var Particles = new HashSet<ParticleSystem>();
            foreach (var Particle in Value.Particles)
                if (!Particle || !Particles.Add(Particle)
                    || !TransformOwnership.Contains(Value.transform, Particle.transform))
                {
                    Error(Result, "Particles must be distinct systems under the effect Root.", Value);
                    break;
                }
            if (Value.PresentationRoot && (Value.PresentationRoot.transform == Value.transform || !TransformOwnership.Contains(Value.transform, Value.PresentationRoot.transform)))
                Error(Result, "Presentation Root must be an owned strict descendant.", Value);
            if (Value.PresentationRoot)
                foreach (var Component in Value.PresentationRoot.GetComponentsInChildren<Component>(true))
                {
                    if (!Component)
                    {
                        Error(Result, "Presentation Root contains a missing component.", Value.PresentationRoot);
                        continue;
                    }
                    if (Value.Transition.CoverDelaySeconds > 0f)
                    {
                        if (Component is Animator || Component is Renderer and not ParticleSystemRenderer)
                            Error(Result, "Cover Delay requires particle-only effect presentation without Animator control.", Component);
                        if (Component is Light Light && (!PllAuthoringValueContract.CanEncode(Light.intensity) || Light.intensity < 0f))
                            Error(Result, "Cover Delay light intensity must be finite and nonnegative.", Light);
                    }
                    if (Component is Transform || Component is ParticleSystem || Component is Renderer
                        || Component is MeshFilter || Component is Light || Component is Animator
                        || Component is ParticleSystemForceField || Component is PllVisualAuthoring) continue;
                    Error(Result, "Presentation Root contains unsupported component '"
                        + Component.GetType().FullName + "'.", Component);
                }
            Info(Result, "The build stops and clears copied particles and disables Play On Awake. Source previews stay unchanged.", Value);
        }

        static void ValidateVisual(PllVisualAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            ValidateTransition(Value, Value.Transition, Result);
            if (Value.Transition.CoverDelaySeconds > 0f && string.IsNullOrEmpty(Value.PropertyName))
                Error(Result, "Cover Delay requires a resolved Float or Range material property.", Value);
            RequireOwnRoot(Value, Value.Root, Result);
            var Renderers = Value.Root ? Value.Root.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            if (Renderers.Length == 0) Error(Result, "Visual Root must contain at least one Renderer.", Value);
            if ((Value.Shader || Value.Material) && string.IsNullOrEmpty(Value.PropertyName))
                Error(Result, "A Shader or Material override requires one exact float/range Property Name.", Value);
            if (!Value.Shader && !Value.Material && !string.IsNullOrEmpty(Value.PropertyName))
                Error(Result, "Property Name requires a Shader or Material target.", Value);
            if (Value.Shader && Value.Material && Value.Material.shader != Value.Shader)
                Error(Result, "The assigned Material does not use the assigned Shader.", Value);
            foreach (var Renderer in Renderers)
                if (Renderer.sharedMaterials.Any(Material => !Material))
                    Error(Result, "Renderer '" + PllHierarchy.NamePath(Value.transform, Renderer.transform)
                        + "' contains a missing material dependency.", Renderer);
            var TargetShader = Value.Shader ? Value.Shader : Value.Material ? Value.Material.shader : null;
            if (TargetShader && !TargetShader.isSupported)
                Warning(Result, "Shader '" + TargetShader.name
                    + "' is unsupported by the current Windows editor graphics device or render path. Test the built .pll on the supported PEAK target.", TargetShader);
            if (!string.IsNullOrEmpty(Value.PropertyName))
            {
                var Matches = Renderers.SelectMany(Renderer => Renderer.sharedMaterials
                        .Where(Material => Material && (!Value.Material || Material == Value.Material)
                            && (!Value.Shader || Material.shader == Value.Shader)))
                    .Count();
                if (Matches != 1)
                    Error(Result, "Shader-property targeting must resolve exactly one Renderer material slot; current matches: "
                        + Matches + ".", Value);
                if (TargetShader)
                {
                    var PropertyIndex = TargetShader.FindPropertyIndex(Value.PropertyName);
                    if (PropertyIndex < 0)
                        Error(Result, "Shader '" + TargetShader.name + "' has no property named '"
                            + Value.PropertyName + "'.", Value);
                    else
                    {
                        var PropertyType = TargetShader.GetPropertyType(PropertyIndex);
                        if (PropertyType != ShaderPropertyType.Float && PropertyType != ShaderPropertyType.Range)
                            Error(Result, "Shader property '" + Value.PropertyName + "' is " + PropertyType
                                + "; PLL supports only Float or Range overrides.", Value);
                    }
                }
            }
        }

        static CustomVisualParticleGraph ValidateCustomVisual(
            PllCustomVisualAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            CustomVisualParticleGraph Graph = null;
            ValidateTransition(Value, Value.Transition, Result);
            if (!Enum.IsDefined(typeof(PllCustomVisualSpace), Value.Space))
                Error(Result, "Space must be Segment or Observed Player.", Value);
            if (!float.IsFinite(Value.PositionOffset.x) || !float.IsFinite(Value.PositionOffset.y)
                || !float.IsFinite(Value.PositionOffset.z))
                Error(Result, "Position Offset must be finite.", Value);
            if (!float.IsFinite(Value.RepositionDistance) || Value.RepositionDistance < 0f)
                Error(Result, "Reposition Distance must be finite and nonnegative.", Value);
            try
            {
                Graph = PllPresentationAuthoringValidator.ValidateCustomVisualParticleGraph(
                    Value.gameObject, Value, "Custom visual '" + Value.name + "'");
                if (Value.Transition.CoverDelaySeconds > 0f)
                {
                    foreach (var Renderer in Graph.Renderers)
                        if (Renderer is not ParticleSystemRenderer)
                            Error(Result, "Cover Delay requires particle-only custom visual presentation.", Renderer);
                    foreach (var Particle in Graph.Particles)
                        if (PllAuthoringValueContract.ParticleTransitionFailure(Particle) is { } Failure)
                            Error(Result, Failure, Particle);
                }
                Info(Result, "The build stops and clears copied particles, disables Play On Awake and starts the copied root inactive. Source previews stay unchanged.", Value);
            }
            catch (InvalidOperationException Exception)
            {
                Error(Result, Exception.Message, Value);
            }
            return Graph;
        }

        static void ValidateEmitter(PllSoundEmitterAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            ValidateTransition(Value, Value.Transition, Result);
            if (Value.Transition.CoverDelaySeconds > 0f && Value.AudioSource && !Value.AudioSource.loop)
                Error(Result, "Cover Delay requires looping audio.", Value.AudioSource);
            RequireOwnRoot(Value, Value.Root, Result);
            if (!Value.AudioSource || Value.AudioSource.gameObject != Value.gameObject)
                Error(Result, "Audio Source must be the one directly owned by this emitter Root.", Value);
        }

        static void ValidateFog(PllFogAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            if (!Value.enabled) Error(Result, "Fog authoring must be enabled.", Value);
            var Defined = Enum.IsDefined(typeof(PllFogMode), Value.Mode);
            if (!Defined) Error(Result, "Mode must be Normal, Rising Fogless, or Fogless.", Value);
            var Origins = Value.GetComponents<FogSphereOrigin>();
            if (Origins.Length != 1)
                Error(Result, "PllFogAuthoring must share its GameObject with exactly one FogSphereOrigin.", Value);
            var Runtime = Value.GetComponentInParent<CustomSegmentRuntime>(true);
            if (!Runtime || !Runtime.FogOrigin || Runtime.FogOrigin.gameObject != Value.gameObject)
                Error(Result, "PllFogAuthoring must be on the exact Fog Origin GameObject referenced by CustomSegmentRuntime.", Value);
            else
            {
                var Authoring = Runtime.gameObject.GetComponentsInChildren<PllFogAuthoring>(true);
                if (Authoring.Length != 1 || Authoring[0] != Value)
                    Error(Result, "The custom biome root must contain exactly one PllFogAuthoring.", Value);
            }
            if (Defined)
                Info(Result, "Build staging derives FogSphereOrigin.disableFog="
                    + (Value.Mode == PllFogMode.Normal ? "false" : "true")
                    + " from Mode and leaves the source flag unchanged.", Value);
        }

        static void ValidateStinger(PllBiomeStingerAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            var Runtime = Value.GetComponentInParent<CustomSegmentRuntime>(true);
            var PlacementError = PllBiomeStingerValidation.PlacementError(
                Runtime ? Runtime.gameObject : null, Value.gameObject);
            PllBiomeStingerValidation.Validate(Value, PlacementError,
                (Message, Context) => Error(Result, Message, Context));
            if ((Value.Mode == PllBiomeStingerMode.EmitHook
                    || Value.Mode == PllBiomeStingerMode.OnEnterAndEmitHook)
                && PllAuthoringValueContract.IsHook(Value.EmitHook) && !LooksNamespaced(Value.EmitHook))
                Warning(Result, "Prefix the hook with the author and biome, for example 'aurora/crystal-caves/climb-stinger'.", Value);
            Info(Result, "The build clears the copied mixer route, disables Play On Awake and resets playback to sample zero. Source previews stay unchanged.", Value);
        }

        static void ValidateVariation(PllVariationAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            if (!Value.enabled) Error(Result, "Variation authoring must be enabled.", Value);
            if (!PllAuthoringValueContract.IsLocalId(Value.Id)) Error(Result, "Variation ID must contain 1 to 64 non-whitespace characters and cannot contain '/', '\\', or '>'.", Value);
            if (Value.Entries.Count < 2) Error(Result, "A variation group needs at least two explicit branch entries.", Value);
            var Roots = new HashSet<GameObject>();
            var Ids = new HashSet<string>(StringComparer.Ordinal);
            var Positive = false;
            foreach (var Entry in Value.Entries)
            {
                if (Entry == null || !Entry.Root || Entry.Root.transform.parent != Value.transform || !Roots.Add(Entry.Root))
                    Error(Result, "Variation entries must reference distinct direct child roots.", Value);
                if (Entry == null) continue;
                if (!PllAuthoringValueContract.IsVariationEntryId(Entry.EntryId) || !Ids.Add(Entry.EntryId))
                    Error(Result, "An entry identity is missing, invalid, or duplicated. Use Prepare Entry IDs.", Value);
                if (!PllAuthoringValueContract.IsVariationWeight(Entry.Weight))
                    Error(Result, "Every Weight must be exactly encodable and between 0 and 100.", Value);
                Positive |= Entry.Weight > 0f;
            }
            if (!Positive) Error(Result, "Set at least one variation weight above zero.", Value);
        }

        static void ValidateEffectZone(PllEffectZoneAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            RequireOwnRoot(Value, Value.Root, Result);
            ValidateZone(Value, Value.Volume, Result);
            if (PllAuthoringValueContract.TransitionFailure(Value.Transition.CoverDelaySeconds) is { } Failure)
                Error(Result, Failure, Value);
            if (!PllAuthoringValueContract.IsDefined(Value.EnvironmentFog))
                Error(Result, "Environment Fog must be Inherit or Environmental Fogless.", Value);
            if (Value.OccupancyHook.Length != 0
                && !PllAuthoringValueContract.IsHook(Value.OccupancyHook))
                Error(Result,
                    "Occupancy Hook must contain 1 to 128 UTF-16 code units and no whitespace, control character, '\\', '>', or malformed surrogate pair.",
                    Value);
            else if (Value.OccupancyHook.Length != 0
                && !LooksNamespaced(Value.OccupancyHook))
                Warning(Result,
                    "Prefix the occupancy hook with the author and biome, for example 'aurora/crystal-caves/mist-zone'.",
                    Value);
            if (Value.Effects.Count == 0 && Value.ActionCount == 0
                && Value.OccupancyHook.Length == 0 && Value.EnvironmentFog != PllEffectZoneEnvironmentFog.EnvironmentalFogless)
                Error(Result, "Add Zone effects, generation or source triggers, an Occupancy Hook, or Environmental Fogless.", Value);
            if (Value.Effects.Any(Effect => !Effect || Effect.Mode != PllEffectMode.Zone
                    || Effect.transform == Value.transform
                    || !TransformOwnership.Contains(Value.transform, Effect.transform)))
                Error(Result, "Zone effects must be owned strict descendants.", Value);
        }

        static void ValidateEffectZoneActions(GameObject Root, ICollection<PllCreatorDiagnostic> Result)
        {
            var Zones = Root.GetComponentsInChildren<PllEffectZoneAuthoring>(true);
            if (Zones.Length == 0) return;
            var Targets = Root.GetComponentsInChildren<PllHookedPropGenerationAuthoring>(true);
            var Owners = new Dictionary<PllHookedPropGenerationAuthoring, PllEffectZoneAuthoring>();
            var Generated = !Root.GetComponent<PllBiomePrefabAnchors>();
            var FogOwners = new Dictionary<PllFogZoneAuthoring, PllEffectZoneAuthoring>();
            var NativeSourceOwners = new Dictionary<PLLNativeResolver, PllEffectZoneAuthoring>();
            foreach (var Zone in Zones)
            {
                try { PllHookedPropGenerationSourceContract.ValidateActions(Zone, Generated, Targets, Owners); }
                catch (ArgumentException Failure) { Error(Result, Failure.Message, Zone, "PropTriggersValue"); }
                var Pairs = new HashSet<(PllEffectZoneTransition, PllFogZoneAuthoring)>();
                foreach (var Trigger in Zone.FogTriggers)
                {
                    if (Trigger == null || !Trigger.Target
                        || Trigger.Transition is not (PllEffectZoneTransition.Enter or PllEffectZoneTransition.Exit)
                        || Trigger.Operation is not (PllFogZoneOperation.Activate or PllFogZoneOperation.Deactivate)
                        || !Pairs.Add((Trigger.Transition, Trigger.Target)))
                    {
                        Error(Result, "Fog action has a missing, duplicate, conflicting, or unsupported target.", Zone, "FogTriggersValue");
                        continue;
                    }
                    if (FogOwners.TryGetValue(Trigger.Target, out var Previous) && Previous != Zone)
                        Warning(Result, "This Fog Zone is controlled by multiple EffectZones; the last committed action wins.", Zone, "FogTriggersValue");
                    FogOwners[Trigger.Target] = Zone;
                }
                var SourcePairs = new HashSet<(PllEffectZoneTransition, PLLNativeResolver)>();
                foreach (var Trigger in Zone.NativeSourceTriggers)
                {
                    if (Trigger == null || !Trigger.Target
                        || Trigger.Transition is not (PllEffectZoneTransition.Enter or PllEffectZoneTransition.Exit)
                        || Trigger.Operation is not (PllNativeSourceOperation.Activate or PllNativeSourceOperation.Deactivate)
                        || !(Trigger.Target.Form == PllNativeResolverForm.Source
                                && Trigger.Target.Kind is (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid)
                            || Trigger.Target.Form == PllNativeResolverForm.Zone && Trigger.Target.Kind is (PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid)
                                && Trigger.Target.transform.root.GetComponent<PllBiomePrefabAnchors>())
                        || !SourcePairs.Add((Trigger.Transition, Trigger.Target)))
                    {
                        Error(Result, "Native source action has a missing, duplicate, conflicting, or unsupported target, transition, or operation.", Zone, "NativeSourceTriggersValue");
                        continue;
                    }
                    if (NativeSourceOwners.TryGetValue(Trigger.Target, out var Previous) && Previous != Zone)
                        Warning(Result, "This native source is controlled by multiple EffectZones; the last committed action wins.", Zone, "NativeSourceTriggersValue");
                    NativeSourceOwners[Trigger.Target] = Zone;
                }
            }
        }

        static void ValidatePropZone(PllPropZoneAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            RequireOwnRoot(Value, Value.Root, Result);
            ValidateZone(Value, Value.Volume, Result);
            if (Value.Spawners.Count == 0 || Value.Spawners.Any(Spawner => !Spawner || Value.transform.IsChildOf(Spawner.transform)))
                Error(Result, "Spawners must contain one or more non-null components that do not own this zone.", Value);
        }

        static void ValidateResolver(PLLNativeResolver Value, ICollection<PllCreatorDiagnostic> Result)
        {
            if (Value.Kind == PLLNativeResolverKind.CustomFluid || Value.SourceProfile == PllNativeFlowProfile.AuthoredSurface)
            {
                try { PllFluidAuthoringValidator.Validate(Value, "Fluid"); }
                catch (ArgumentException Failure) { Error(Result, Failure.Message, Value); }
                catch (InvalidOperationException Failure) { Error(Result, Failure.Message, Value); }
                return;
            }
            RequireOwnRoot(Value, Value.Root, Result);
            ValidateTransition(Value, Value.Transition, Result);
            if (Value.Form == PllNativeResolverForm.Zone && Value.Transition.CoverDelaySeconds > 0f)
                Error(Result, "Native Zone Cover Delay has no supported presentation channel.", Value, "TransitionValue.SecondsValue");
            if (Value.Form == PllNativeResolverForm.Zone) ValidateZone(Value, Value.Volume, Result);
            if (Value.Kind == PLLNativeResolverKind.None || !Enum.IsDefined(typeof(PLLNativeResolverKind), Value.Kind))
                Error(Result, "Choose one supported native resolver Kind. None cannot be built.", Value);
            if (Value.Form == PllNativeResolverForm.Source)
            {
                try { PllPresentationAuthoringValidator.ValidateFlowSource(Value, "Native Resolver"); }
                catch (ArgumentException Failure) { Error(Result, Failure.Message, Value, "SourceSettingsValue"); }
                catch (InvalidOperationException Failure) { Error(Result, Failure.Message, Value, "SourceSettingsValue"); }
                return;
            }
            if (Value.Form != PllNativeResolverForm.Zone)
                Error(Result, "Native Resolver Form is unsupported.", Value, "FormValue");
            if (Value.Kind == PLLNativeResolverKind.SporeCloud && Value.Volume.Shape != PllZoneShape.Sphere)
                Error(Result, "SporeCloud requires one Sphere Volume.", Value);
            else if (Value.Kind == PLLNativeResolverKind.SporeCloud && FinitePositive(Value.Volume.Radius))
                Info(Result, "The final world radius must match PEAK's Roots SporeCloud radius within 0.01. "
                    + "Test the built biome in PEAK.", Value);
        }

        static void ValidateNativeVisualResolver(
            PLLNativeVisualResolver Value, ICollection<PllCreatorDiagnostic> Result)
        {
            ValidateTransition(Value, Value.Transition, Result);
            try { PllEffectAppearanceValueContract.ValidateNativeVisual(Value); }
            catch (InvalidOperationException Failure) { Error(Result, Failure.Message, Value); }
            if (Value.Preset == PllNativeVisualPreset.None
                || !Enum.IsDefined(typeof(PllNativeVisualPreset), Value.Preset))
                Error(Result, "Choose one supported native visual Preset. None cannot be built.", Value);
            else
                Info(Result, "PEAK loads and checks this preset when loading the biome.", Value);
        }

        static void ValidateChildProps(PllChildPropAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            RequireOwnRoot(Value, Value.Root, Result);
            if (Value.Points.Count == 0 || Value.Points.Any(Point => !Point || Point == Value.transform || !TransformOwnership.Contains(Value.transform, Point)
                || Point.childCount != 0 || Point.GetComponents<Component>().Length != 1))
                Error(Result, "Points must contain one or more owned empty strict descendant Transforms.", Value);
            if (Value.Props.Count == 0 || Value.Props.Any(Prop => !Prop))
                Error(Result, "Assign at least one bundle-owned prefab candidate in Props.", Value);
        }

        static void ValidateItemSpawner(PllItemSpawnerAuthoring Value, ICollection<PllCreatorDiagnostic> Result)
        {
            RequireOwnRoot(Value, Value.Root, Result);
            if (!Value.Item) Error(Result, "Assign one identification-only existing PEAK item prefab.", Value);
            if (Value.SpawnPoints.Count == 0 || Value.SpawnPoints.Any(Point => !Point || Point == Value.transform || !TransformOwnership.Contains(Value.transform, Point)))
                Error(Result, "Spawn Points must contain one or more owned strict descendant Transforms.", Value);
        }

        static void ValidateForce(PLLForceSpawner Value, ICollection<PllCreatorDiagnostic> Result)
        {
            if (!ForceObjectNameContract.IsValid(Value.ObjectName))
                Error(Result, "Object Name " + ForceObjectNameContract.Requirement + ".", Value);
            else Info(Result, "Live resolution is deferred; PLL never serializes or copies a PEAK prefab into the source asset.", Value);
        }

        static void ValidateHooked(PLLHookedProp Value, ICollection<PllCreatorDiagnostic> Result)
        {
            if (!PllAuthoringValueContract.IsHook(Value.Hook))
                Error(Result, "Hook must contain 1 to 128 UTF-16 code units and no whitespace, control character, '\\', '>', or malformed surrogate pair.", Value);
            else if (!LooksNamespaced(Value.Hook))
                Warning(Result, "Prefix the hook with the author and biome, for example 'aurora/crystal-caves/breakable-vine'.", Value);
        }

        static void ValidateZone(Component Owner, PllZoneVolume Volume, ICollection<PllCreatorDiagnostic> Result)
        {
            if (PllAuthoringValueContract.ZoneVolumeFailure(Volume) is { } Failure)
                Error(Result, Failure.Message, Owner, Failure.PropertyPath);
        }
        static void ValidateLighting(GameObject Root, ICollection<PllCreatorDiagnostic> Result)
        {
            foreach (var Light in Root.GetComponentsInChildren<Light>(true))
                if (Light && Light.lightmapBakeType != LightmapBakeType.Realtime)
                    Warning(Result, "Baked or Mixed Light '" + PllHierarchy.NamePath(Root.transform, Light.transform)
                        + "' is unsupported because PLL never rebakes occurrences. Change it to Realtime or use an unlit material.", Light);
            foreach (var Renderer in Root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!Renderer) continue;
                var Flags = GameObjectUtility.GetStaticEditorFlags(Renderer.gameObject);
                var SerializedLightmap = Renderer.lightmapIndex >= 0 && Renderer.lightmapIndex < 0xFFFE;
                if (SerializedLightmap || (Flags & StaticEditorFlags.ContributeGI) != 0 && Renderer.receiveGI == ReceiveGI.Lightmaps)
                    Warning(Result, "Baked-only Renderer '" + PllHierarchy.NamePath(Root.transform, Renderer.transform)
                        + "' cannot rely on source lightmaps in live PEAK. Clear Contribute GI/lightmap data and use dynamic light probes or an unlit shader.", Renderer);
            }
        }

        static void RequireOwnRoot(Component Owner, GameObject Root, ICollection<PllCreatorDiagnostic> Result)
        {
            if (!Root || Root != Owner.gameObject) Error(Result, "Root is editor-maintained and must equal this component's GameObject.", Owner);
        }

        static void RequireStrictChild(Component Owner, GameObject Value, string Field,
            ICollection<PllCreatorDiagnostic> Result)
        {
            if (!Value || Value.transform == Owner.transform || !TransformOwnership.Contains(Owner.transform, Value.transform))
                Error(Result, Field + " must be an owned strict descendant.", Owner);
        }

        static void RequireStrictChild(Component Owner, Transform Value, string Field,
            ICollection<PllCreatorDiagnostic> Result)
        {
            if (!Value || Value == Owner.transform || !TransformOwnership.Contains(Owner.transform, Value))
                Error(Result, Field + " must be an owned strict descendant.", Owner);
        }

        static void RequireOwned(Component Owner, Component Value, string Field,
            ICollection<PllCreatorDiagnostic> Result)
        {
            if (!Value || !TransformOwnership.Contains(Owner.transform, Value.transform)) Error(Result, Field + " must be owned by this authoring root.", Owner);
        }

        static void RequireOwned(Component Owner, Transform Value, string Field,
            ICollection<PllCreatorDiagnostic> Result)
        {
            if (!Value || !TransformOwnership.Contains(Owner.transform, Value)) Error(Result, Field + " must be owned by this authoring root.", Owner);
        }

        static void RequireRendererAndCollider(Component Owner, GameObject Root,
            ICollection<PllCreatorDiagnostic> Result)
        {
            if (!Root) return;
            if (!Root.GetComponentsInChildren<MeshRenderer>(true).Any(Renderer => Renderer && Renderer.enabled))
                Error(Result, "Model Root must contain at least one enabled MeshRenderer.", Owner);
            if (!Root.GetComponentsInChildren<Collider>(true).Any(Collider => Collider && Collider.enabled))
                Error(Result, "Model Root must contain at least one enabled Collider.", Owner);
        }

        static void RejectRawMechanics(Component Owner, ICollection<PllCreatorDiagnostic> Result, Func<Component, bool> IsNativeMechanic)
        {
            foreach (var Component in Owner.GetComponentsInChildren<Component>(true))
            {
                if (!Component || !IsNativeMechanic(Component)) continue;
                Error(Result, "Remove creator-added " + Component.GetType().Name
                    + ". PLL stages the required PEAK mechanics and networking only on the build copy.", Component);
            }
        }

        static bool LooksNamespaced(string Value)
        {
            var Slash = Value.IndexOf('/');
            return Slash > 0 && Slash < Value.Length - 1;
        }
        static bool FinitePositive(float Value) => float.IsFinite(Value) && Value > 0f;

        static void Info(ICollection<PllCreatorDiagnostic> Result, string Message, Object Context) =>
            Result.Add(new PllCreatorDiagnostic(PllCreatorDiagnosticSeverity.Info, Message, Context));
        static void Warning(ICollection<PllCreatorDiagnostic> Result, string Message, Object Context, string PropertyPath = "") =>
            Result.Add(new PllCreatorDiagnostic(PllCreatorDiagnosticSeverity.Warning, Message, Context, PropertyPath));
        static void Error(ICollection<PllCreatorDiagnostic> Result, string Message, Object Context, string PropertyPath = "") =>
            Result.Add(new PllCreatorDiagnostic(PllCreatorDiagnosticSeverity.Error, Message, Context, PropertyPath));
    }

    [CustomEditor(typeof(PllBiomeDefinition))]
    internal sealed class PllBiomeDefinitionEditor : UnityEditor.Editor
    {
        bool CatalogExpanded = true;
        bool CatalogLoaded;
        Hash128 CatalogHash;
        DefinitionCatalog Catalog;
        PLLForceSpawner[] ForceSpawners;
        string CatalogError;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "visuals", "luggageDefinitions",
                "variationDefinitions", "effectDefinitions", "soundEmitters", "stingerDefinitions", "dayNightProfile", "color");
            DrawVisualEnvironment((PllBiomeDefinition)target);
            if (serializedObject.ApplyModifiedProperties()) CatalogLoaded = false;
            var Definition = (PllBiomeDefinition)target;
            EditorGUILayout.Space();
            CatalogExpanded = EditorGUILayout.Foldout(CatalogExpanded, "Automatic Component Catalogs", true);
            if (CatalogExpanded) DrawCatalog(Definition);
            DrawDiagnostics(PllAuthoringValidation.Validate(Definition));
        }

        void DrawVisualEnvironment(PllBiomeDefinition Definition)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Biome Visual Environment", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(PllAuthoringCommands.Property(serializedObject, "dayNightProfile"));
            EditorGUILayout.PropertyField(PllAuthoringCommands.Property(serializedObject, "color"),
                new GUIContent("Final Ambient Tint"));
            var Root = Definition.RootPrefab;
            if (!Root)
            {
                EditorGUILayout.LabelField("Native Visual Preset", "Root Prefab unavailable");
                EditorGUILayout.LabelField("Constant Particles", "Root Prefab unavailable");
                EditorGUILayout.LabelField("Renderer Gates", "Root Prefab unavailable");
                return;
            }
            var Native = Root.GetComponents<PLLNativeVisualResolver>();
            EditorGUILayout.LabelField("Native Visual Preset", Native.Length == 0 ? "None authored"
                : Native.Length == 1 ? Native[0].Preset + " (live presentation deferred)"
                : "Invalid: " + Native.Length + " root resolvers");
            var Custom = PllSourceIdentityValidator.DiscoverComponents<PllCustomVisualAuthoring>(Root);
            var Gates = PllSourceIdentityValidator.DiscoverComponents<PllVisualAuthoring>(Root);
            using (new EditorGUI.DisabledScope(true))
            {
                DrawVisualSummary("Constant Particles", Custom);
                DrawVisualSummary("Renderer Gates", Gates);
            }
        }

        static void DrawVisualSummary<T>(string Label, IReadOnlyList<T> Values) where T : Component
        {
            EditorGUILayout.LabelField(Label, Values.Count.ToString());
            foreach (var Value in Values) EditorGUILayout.ObjectField(Value, typeof(T), false);
        }

        void DrawCatalog(PllBiomeDefinition Definition)
        {
            var Path = AssetDatabase.GetAssetPath(Definition);
            var Hash = string.IsNullOrEmpty(Path) ? default : AssetDatabase.GetAssetDependencyHash(Path);
            if (!CatalogLoaded || CatalogHash != Hash)
            {
                CatalogLoaded = true;
                CatalogHash = Hash;
                Catalog = null;
                CatalogError = null;
                try
                {
                    Catalog = new PllSourceCatalogValidator().Validate(Definition);
                    ForceSpawners = PllSourceIdentityValidator.DiscoverGraphComponents<PLLForceSpawner>(Catalog.Graph);
                }
                catch (InvalidOperationException Exception)
                {
                    CatalogError = Exception.Message;
                }
            }
            if (Catalog == null)
            {
                EditorGUILayout.HelpBox("The source catalog is unavailable: " + CatalogError, MessageType.Error);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                DrawVisualSummary("Campfires", Catalog.Campfires);
                DrawVisualSummary("Climb Surfaces", Catalog.ClimbSurfaces);
                EditorGUILayout.ObjectField("Climb Material Rules", Catalog.ClimbSurfaceCatalog.MaterialAuthoring,
                    typeof(PllClimbSurfaceMaterialAuthoring), false);
                DrawVisualSummary("Statues", Catalog.Statues);
                DrawVisualSummary("Luggage", Catalog.Luggage);
                DrawVisualSummary("Rest Points", Catalog.RestPoints);
                DrawVisualSummary("Hanging Ropes", Catalog.HangingRopes);
                DrawVisualSummary("Variations", Catalog.Variations);
                DrawVisualSummary("Effects", Catalog.Effects);
                DrawVisualSummary("Effect Zones", Catalog.EffectZones);
                DrawVisualSummary("Fog Zones", Catalog.FogZones);
                DrawVisualSummary("Visuals", Catalog.Visuals);
                EditorGUILayout.LabelField("Custom Visuals", Catalog.CustomVisuals.Length.ToString());
                foreach (var Visual in Catalog.CustomVisuals)
                    EditorGUILayout.ObjectField(Visual.Authoring, typeof(PllCustomVisualAuthoring), false);
                EditorGUILayout.ObjectField("Atmosphere", Catalog.Atmosphere, typeof(PllAtmosphereAuthoring), false);
                DrawVisualSummary("Sound Emitters", Catalog.SoundEmitters);
                DrawVisualSummary("Stinger Definitions", Catalog.Stingers);
                DrawVisualSummary("Native Resolvers", Catalog.NativeResolvers);
                DrawVisualSummary("Native Visual Resolvers", Catalog.NativeVisualResolvers);
                DrawVisualSummary("Prop Zones", Catalog.PropZones);
                DrawVisualSummary("No Prop Spawn", Catalog.NoPropSpawn);
                DrawVisualSummary("Child Props", Catalog.ChildProps);
                DrawVisualSummary("Item Spawners", Catalog.ItemSpawners);
                DrawVisualSummary("Native Spawners", Catalog.NativeSpawners);
                DrawVisualSummary("Force Spawners", ForceSpawners);
                DrawVisualSummary("Final Generation", Catalog.FinalGeneration);
                DrawVisualSummary("Critical Generation", Catalog.CriticalGeneration);
                DrawVisualSummary("Hooked Props", Catalog.HookedProps);
                DrawVisualSummary("Hooked Generation", Catalog.HookedGeneration.Sources);
                DrawVisualSummary("Entity Zones", Catalog.EntityZones);
                DrawVisualSummary("Hazard Zones", Catalog.HazardZones);
                DrawVisualSummary("Entities", Catalog.Entities);
                DrawVisualSummary("Hazards", Catalog.Hazards);
                DrawVisualSummary("Amulet Spawners", Catalog.AmuletSpawners);
                DrawVisualSummary("Surface Textures", Catalog.SurfaceTextures);
                DrawVisualSummary("Native Materials", Catalog.NativeMaterials);
                DrawVisualSummary("Breakable Props", Catalog.BreakableProps);
                DrawVisualSummary("Swinging Props", Catalog.SwingingProps);
                DrawVisualSummary("Vines", Catalog.Vines);
            }
            EditorGUILayout.HelpBox("Shows components in this biome and its referenced source prefabs. The list is read-only.", MessageType.None);
        }

        internal static void DrawDiagnostics(IReadOnlyList<PllCreatorDiagnostic> Diagnostics)
        {
            if (Diagnostics.Count == 0)
            {
                EditorGUILayout.HelpBox("No local issues found. Use Validate Selected Biome to check the biome and its referenced prefabs.", MessageType.Info);
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Authoring Validation", EditorStyles.boldLabel);
            foreach (var Diagnostic in Diagnostics)
            {
                if (!string.IsNullOrEmpty(Diagnostic.PropertyPath))
                    EditorGUILayout.LabelField("Field", Diagnostic.PropertyPath);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox(Diagnostic.Message, MessageTypeFor(Diagnostic.Severity));
                if (Diagnostic.Context && GUILayout.Button("Select", GUILayout.Width(52f), GUILayout.Height(38f)))
                {
                    Selection.activeObject = Diagnostic.Context;
                    EditorGUIUtility.PingObject(Diagnostic.Context);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        static MessageType MessageTypeFor(PllCreatorDiagnosticSeverity Severity)
        {
            return Severity == PllCreatorDiagnosticSeverity.Error ? MessageType.Error
                : Severity == PllCreatorDiagnosticSeverity.Warning ? MessageType.Warning : MessageType.Info;
        }
    }

    internal abstract class PllAuthoringComponentEditor : UnityEditor.Editor
    {
        static readonly PllEffectMode[] EffectModes =
            { PllEffectMode.Timed, PllEffectMode.EmitHook, PllEffectMode.Zone };
        static readonly string[] EffectModeLabels = { "Timed", "Emit Hook", "Zone" };
        static readonly PllFogMode[] FogModes =
            { PllFogMode.Normal, PllFogMode.RisingFogless, PllFogMode.Fogless };
        static readonly string[] FogModeLabels = { "Normal", "Rising Fogless", "Fogless" };
        static readonly PllBiomeStingerMode[] StingerModes =
            { PllBiomeStingerMode.OnEnter, PllBiomeStingerMode.EmitHook,
                PllBiomeStingerMode.OnEnterAndEmitHook };
        static readonly string[] StingerModeLabels = { "On Enter", "Emit Hook", "On Enter and Emit Hook" };
        static readonly PLLNativeResolverKind[] ResolverKinds =
            { PLLNativeResolverKind.None, PLLNativeResolverKind.Water, PLLNativeResolverKind.Rain,
                PLLNativeResolverKind.Wind, PLLNativeResolverKind.Snow, PLLNativeResolverKind.SporeCloud, PLLNativeResolverKind.Lava,
                PLLNativeResolverKind.CustomFluid };
        static readonly string[] ResolverKindLabels =
            { "Choose Kind", "Water", "Rain", "Wind", "Snow", "Spore Cloud (Roots)", "Lava", "Custom Fluid" };
        static readonly GUIContent HookLabel = new GUIContent("Hook",
            "Use 1-128 characters without spaces. Include the author and biome, for example aurora/crystal-caves/breakable-vine.");
        static readonly GUIContent OccupancyHookLabel = new GUIContent(
            "Occupancy Hook",
            "Optional local hook for players entering or leaving. Leave empty to disable it. Include the author and biome, for example aurora/crystal-caves/mist-zone.");
        static readonly PllEffectZoneEnvironmentFog[] EnvironmentFogModes =
            { PllEffectZoneEnvironmentFog.Inherit, PllEffectZoneEnvironmentFog.EnvironmentalFogless };
        static readonly string[] EnvironmentFogModeLabels =
            { "Inherit", "Environmental Fogless" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            AutoFillUnambiguousReferences();
            var Component = (Component)target;
            var Property = serializedObject.GetIterator();
            var EnterChildren = true;
            while (Property.NextVisible(EnterChildren))
            {
                EnterChildren = false;
                if (Property.propertyPath == "m_Script") continue;
                DrawCreatorProperty(Component, Property);
            }
            serializedObject.ApplyModifiedProperties();
            var CustomVisual = Component as PllCustomVisualAuthoring;
            CustomVisualParticleGraph CustomVisualGraph = null;
            var Diagnostics = CustomVisual
                ? PllAuthoringValidation.Validate(CustomVisual, out CustomVisualGraph)
                : PllAuthoringValidation.Validate(Component);
            DrawGuidance(Component, CustomVisualGraph);
            PllBiomeDefinitionEditor.DrawDiagnostics(Diagnostics);
        }

        public virtual void OnSceneGUI()
        {
            var Resolver = target as PLLNativeResolver;
            if (Resolver)
            {
                if (Resolver.Form == PllNativeResolverForm.Zone) DrawZone(Resolver.transform, Resolver.Volume);
                else if (Resolver.SourceProfile == PllNativeFlowProfile.RayWaterfall)
                {
                    var Settings = Resolver.SourceSettings;
                    if (Settings.RayStart && Settings.RayEnd) Handles.DrawLine(Settings.RayStart.position, Settings.RayEnd.position);
                }
                else if (Resolver.SourceProfile == PllNativeFlowProfile.AuthoredSurface
                    && (Resolver.Kind == PLLNativeResolverKind.Water || Resolver.FluidDefinition && Resolver.FluidDefinition.Behavior == PllFluidBehavior.Water))
                {
                    try { Resolver.ValidateContactArrays(); }
                    catch (ArgumentException) { return; }
                    if (Resolver.ContactMode == PllFluidContactMode.Automatic)
                    {
                        if (Resolver.SurfaceRenderers.Count == 0 || Resolver.SurfaceRenderers.Any(Renderer => !Renderer)) return;
                        var Bounds = Resolver.AuthoredSurfaceBounds();
                        Handles.DrawWireCube(Bounds.center, Bounds.size);
                        Handles.ArrowHandleCap(0, Resolver.transform.position, Resolver.transform.rotation,
                            HandleUtility.GetHandleSize(Resolver.transform.position), EventType.Repaint);
                    }
                    else for (var Index = 0; Index < Resolver.ContactRegionCount; Index++)
                    {
                        var Frame = Resolver.ContactFrames[Index];
                        if (!Frame) continue;
                        DrawZone(Frame, Resolver.ContactRegion(Index));
                        Handles.ArrowHandleCap(0, Frame.position, Frame.rotation, HandleUtility.GetHandleSize(Frame.position), EventType.Repaint);
                    }
                }
            }
            else if (target is PllCustomVisualAuthoring)
                DrawCustomVisualGizmo((PllCustomVisualAuthoring)target);
            else if (target is PllEffectZoneAuthoring)
            {
                var Value = (PllEffectZoneAuthoring)target;
                DrawZone(Value.transform, Value.Volume);
            }
        }

        void AutoFillUnambiguousReferences()
        {
            var Root = serializedObject.FindProperty("root");
            if (Root != null && Root.objectReferenceValue == null)
                Root.objectReferenceValue = ((Component)target).gameObject;
            var Model = serializedObject.FindProperty("modelRoot");
            if (Model == null || Model.objectReferenceValue != null) return;
            var Owner = ((Component)target).transform;
            Transform Candidate = null;
            for (var i = 0; i < Owner.childCount; i++)
            {
                var Child = Owner.GetChild(i);
                if (!Child.GetComponentInChildren<Renderer>(true)) continue;
                if (Candidate) return;
                Candidate = Child;
            }
            if (Candidate) Model.objectReferenceValue = Candidate.gameObject;
        }

        void DrawCreatorProperty(Component Component, SerializedProperty Property)
        {
            if (Property.name == "TransitionValue")
            {
                EditorGUILayout.PropertyField(Property.FindPropertyRelative("SecondsValue"),
                    new GUIContent("Cover Delay (seconds)", "Symmetric fill/clear duration. Zero applies immediately."));
                return;
            }
            if (Component is PllFogAuthoring && Property.name == "mode")
            {
                DrawEnum(Property, "Mode", FogModes, FogModeLabels);
                return;
            }
            if (Component is PllBiomeStingerAuthoring)
            {
                if (Property.name == "mode")
                {
                    DrawEnum(Property, "Mode", StingerModes, StingerModeLabels);
                    return;
                }
                if (Property.name == "emitHook")
                {
                    var Mode = serializedObject.FindProperty("mode");
                    if (Mode != null && Mode.intValue == (int)PllBiomeStingerMode.OnEnter) return;
                    EditorGUILayout.PropertyField(Property, HookLabel);
                    return;
                }
            }
            var Effect = Component as PllEffectAuthoring;
            if (Effect && Property.name == "mode")
            {
                DrawEnum(Property, "Mode", EffectModes, EffectModeLabels);
                return;
            }
            if (Effect && Property.name == "emitHook")
            {
                var Mode = serializedObject.FindProperty("mode");
                if (Mode != null && Mode.intValue != (int)PllEffectMode.EmitHook && string.IsNullOrEmpty(Property.stringValue))
                    return;
                EditorGUILayout.PropertyField(Property, HookLabel);
                return;
            }
            if (Component is PLLHookedProp && Property.name == "hook")
            {
                EditorGUILayout.PropertyField(Property, HookLabel);
                return;
            }
            if (Component is PllEffectZoneAuthoring)
            {
                if (Property.name == "OccupancyHookValue")
                {
                    EditorGUILayout.PropertyField(Property, OccupancyHookLabel);
                    return;
                }
                if (Property.name == "EnvironmentFogValue")
                {
                    DrawEnum(Property, "Environment Fog", EnvironmentFogModes, EnvironmentFogModeLabels);
                    return;
                }
            }
            if (Component is PLLNativeResolver)
            {
                if (Property.name == "kind")
                {
                    var PreviousKind = Property.intValue;
                    DrawEnum(Property, "Kind", ResolverKinds, ResolverKindLabels);
                    if (Property.intValue != PreviousKind)
                    {
                        if ((PLLNativeResolverKind)Property.intValue is not (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid))
                            serializedObject.FindProperty("FormValue").intValue = (int)PllNativeResolverForm.Zone;
                        if ((PLLNativeResolverKind)Property.intValue == PLLNativeResolverKind.Lava)
                            serializedObject.FindProperty("SourceProfileValue").intValue = (int)PllNativeFlowProfile.SimulatedSpline;
                    }
                    return;
                }
                var Kind = serializedObject.FindProperty("kind");
                if (Property.name == "SourceProfileValue")
                {
                    if (serializedObject.FindProperty("FormValue").intValue != (int)PllNativeResolverForm.Source) return;
                    var Previous = Property.intValue;
                    if (Kind.intValue == (int)PLLNativeResolverKind.Lava)
                        DrawEnum(Property, "Source Profile", new[] { PllNativeFlowProfile.SimulatedSpline, PllNativeFlowProfile.AuthoredSurface },
                            new[] { "Simulated Spline", "Authored Surface" });
                    else EditorGUILayout.PropertyField(Property);
                    if (Previous != Property.intValue && Property.intValue == (int)PllNativeFlowProfile.AuthoredSurface)
                    {
                        var Settings = serializedObject.FindProperty("SourceSettingsValue");
                        foreach (var Name in new[] { "LandingModel", "SplashPresentation", "SamplePresentation", "DetailPresentation" })
                            Settings.FindPropertyRelative(Name).intValue = (int)PllFlowOutput.None;
                        foreach (var Name in new[] { "GenerateSplash", "GenerateAlongSpline", "GenerateDetails", "SpawnLights", "EnablePusher" })
                            Settings.FindPropertyRelative(Name).boolValue = false;
                        foreach (var Name in new[] { "EndRock", "Splash", "SpawnAlongSpline", "RayStart", "RayEnd", "Endpoint", "PusherSfx" })
                            Settings.FindPropertyRelative(Name).objectReferenceValue = null;
                        Settings.FindPropertyRelative("DetailsAlongSpline").arraySize = 0;
                        var Surface = serializedObject.FindProperty("surfaceRenderer");
                        if (Surface.objectReferenceValue)
                        {
                            var Renderers = serializedObject.FindProperty("SurfaceRenderersValue");
                            var Slots = serializedObject.FindProperty("SurfaceSlotsValue");
                            Renderers.arraySize = Slots.arraySize = 1;
                            Renderers.GetArrayElementAtIndex(0).objectReferenceValue = Surface.objectReferenceValue;
                            Slots.GetArrayElementAtIndex(0).intValue = 0;
                            Surface.objectReferenceValue = null;
                        }
                    }
                    return;
                }
                var Form = serializedObject.FindProperty("FormValue");
                var Profile = serializedObject.FindProperty("SourceProfileValue");
                if (Property.name == "generatedSurfaceMaterial" && Kind.intValue == (int)PLLNativeResolverKind.Lava
                    && Form.intValue == (int)PllNativeResolverForm.Zone)
                {
                    EditorGUILayout.PropertyField(Property, new GUIContent("Material Overrides"), true);
                    return;
                }
                if (Property.name == "SourceSettingsValue")
                {
                    if (Form.intValue != (int)PllNativeResolverForm.Source) return;
                    var Definition = serializedObject.FindProperty("FluidDefinitionValue").objectReferenceValue as PllFluidDefinition;
                    DrawFlowSettings(Property, (PLLNativeResolverKind)Kind.intValue, (PllNativeFlowProfile)Profile.intValue, Definition);
                    return;
                }
                if (!(Property.propertyType == SerializedPropertyType.ObjectReference && Property.objectReferenceValue
                        || Property.isArray && Property.arraySize != 0)
                    && Kind != null && !ResolverPropertyVisible(Property.name, (PLLNativeResolverKind)Kind.intValue,
                        (PllNativeResolverForm)Form.intValue, (PllNativeFlowProfile)Profile.intValue)) return;
            }
            using (new EditorGUI.DisabledScope(Property.name == "root"))
                EditorGUILayout.PropertyField(Property, true);
        }

        static void DrawEnum<T>(SerializedProperty Property, string Label, T[] Values, string[] Labels) where T : Enum
        {
            var Current = Array.FindIndex(Values, Value => Convert.ToInt32(Value) == Property.intValue);
            var PreviousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = Property.hasMultipleDifferentValues || Current < 0;
            EditorGUI.BeginChangeCheck();
            var Selected = EditorGUILayout.Popup(Label, Math.Max(Current, 0), Labels);
            if (EditorGUI.EndChangeCheck()) Property.intValue = Convert.ToInt32(Values[Selected]);
            EditorGUI.showMixedValue = PreviousMixed;
        }

        static void DrawFlowSettings(SerializedProperty Settings, PLLNativeResolverKind Kind, PllNativeFlowProfile Profile, PllFluidDefinition Definition)
        {
            EditorGUILayout.LabelField("Source Settings", EditorStyles.boldLabel);
            if (Profile != PllNativeFlowProfile.AuthoredSurface && GUILayout.Button("Use Source Only"))
            {
                foreach (var Name in new[] { "LandingModel", "SplashPresentation", "SamplePresentation", "DetailPresentation" })
                    Settings.FindPropertyRelative(Name).intValue = (int)PllFlowOutput.None;
                foreach (var Name in new[] { "EndRock", "Splash", "SpawnAlongSpline" })
                    Settings.FindPropertyRelative(Name).objectReferenceValue = null;
                Settings.FindPropertyRelative("DetailsAlongSpline").arraySize = 0;
                foreach (var Name in new[] { "GenerateSplash", "GenerateAlongSpline", "GenerateDetails" })
                    Settings.FindPropertyRelative(Name).boolValue = false;
                Settings.FindPropertyRelative("SpawnLights").boolValue = false;
                Settings.serializedObject.FindProperty("EmitterModelValue").objectReferenceValue = null;
            }
            var Field = Settings.Copy(); var End = Settings.GetEndProperty();
            if (!Field.NextVisible(true)) return;
            do
            {
                if (SerializedProperty.EqualContents(Field, End)) break;
                if (Field.depth != Settings.depth + 1) continue;
                var Name = Field.name;
                if (Name is "LandingModel" or "SplashPresentation" or "SamplePresentation" or "DetailPresentation")
                {
                    if (Profile == PllNativeFlowProfile.AuthoredSurface) continue;
                    var Choices = Definition
                        ? new[] { PllFlowOutput.Legacy, PllFlowOutput.UseFluidDefault, PllFlowOutput.Native, PllFlowOutput.Custom, PllFlowOutput.None }
                        : new[] { PllFlowOutput.Legacy, PllFlowOutput.Native, PllFlowOutput.Custom, PllFlowOutput.None };
                    var Labels = Definition ? new[] { "Use Fluid Default (Legacy)", "Use Fluid Default", "Native", "Custom", "None" }
                        : new[] { "Legacy", "Native", "Custom", "None" };
                    var Previous = Field.intValue;
                    DrawEnum(Field, Field.displayName, Choices, Labels);
                    if (Previous != Field.intValue && Field.intValue is not ((int)PllFlowOutput.Legacy) and not ((int)PllFlowOutput.Custom))
                    {
                        var Payload = Settings.FindPropertyRelative(Name == "LandingModel" ? "EndRock"
                            : Name == "SplashPresentation" ? "Splash" : Name == "SamplePresentation" ? "SpawnAlongSpline" : "DetailsAlongSpline");
                        if (Payload.isArray) Payload.arraySize = 0; else Payload.objectReferenceValue = null;
                    }
                    continue;
                }
                if (Profile == PllNativeFlowProfile.AuthoredSurface
                    && Name is not ("WaterForce" or "WaterDrag" or "WaterRagdollLength" or "WaterRagdoll")) continue;
                if (Kind == PLLNativeResolverKind.CustomFluid && Name == "Material") continue;
                var Selection = Name is "GenerateSplash" or "Splash" ? "SplashPresentation"
                    : Name is "GenerateAlongSpline" or "SpawnAlongSpline" ? "SamplePresentation"
                    : Name is "GenerateDetails" or "DetailsAlongSpline" or "StepsAlongSpline" ? "DetailPresentation"
                    : Name == "EndRock" ? "LandingModel" : null;
                if (Selection != null && Settings.FindPropertyRelative(Selection).intValue != (int)PllFlowOutput.Legacy)
                {
                    var Choice = (PllFlowOutput)Settings.FindPropertyRelative(Selection).intValue;
                    if (Name.StartsWith("Generate", StringComparison.Ordinal) || Choice != PllFlowOutput.Custom && Name != "StepsAlongSpline") continue;
                    if (Name == "StepsAlongSpline" && Choice == PllFlowOutput.None) continue;
                }
                var Ray = Name is "RayStart" or "RayEnd" or "Endpoint" or "LandingPolicy";
                var Contact = Name.StartsWith("Water", StringComparison.Ordinal)
                    || Name.StartsWith("Pusher", StringComparison.Ordinal) || Name == "EnablePusher";
                var SharedOutput = Name is "GenerateSplash" or "Splash" or "GenerateAlongSpline"
                    or "SpawnAlongSpline" or "GenerateDetails" or "DetailsAlongSpline";
                if (Contact && Kind != PLLNativeResolverKind.Water && (!Definition || Definition.Behavior != PllFluidBehavior.Water)) continue;
                if (Ray != (Profile == PllNativeFlowProfile.RayWaterfall) && !Contact && !SharedOutput
                    && Name != "Material" && Name != "EndRock") continue;
                if (Name == "Endpoint" && Settings.FindPropertyRelative("LandingPolicy").intValue != (int)PllNativeFlowLandingPolicy.AuthoredEndpoint) continue;
                if (Name == "Splash" && !Settings.FindPropertyRelative("GenerateSplash").boolValue
                    || Name == "SpawnAlongSpline" && !Settings.FindPropertyRelative("GenerateAlongSpline").boolValue
                    || Name is "DetailsAlongSpline" or "StepsAlongSpline" && !Settings.FindPropertyRelative("GenerateDetails").boolValue) continue;
                EditorGUILayout.PropertyField(Field, new GUIContent(Name == "Material" ? "Material Overrides"
                    : Name == "DetailsAlongSpline" ? "Detail Overrides (Empty Uses Native)" : Field.displayName), true);
            } while (Field.NextVisible(false));
        }

        static bool ResolverPropertyVisible(string Name, PLLNativeResolverKind Kind,
            PllNativeResolverForm Form, PllNativeFlowProfile Profile)
        {
            var Flow = Kind is PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava or PLLNativeResolverKind.CustomFluid;
            if (Name == "FluidDefinitionValue") return Kind == PLLNativeResolverKind.CustomFluid;
            if (Name is "EmitterModelValue" or "InitiallyDisabledValue") return Flow;
            if (Name is "SurfaceRenderersValue" or "SurfaceSlotsValue" or "ContactModeValue"
                or "ContactFramesValue" or "ContactShapesValue" or "ContactCentersValue" or "ContactSizesValue" or "ContactRadiiValue" or "ContactProxiesValue")
                return Flow && Form == PllNativeResolverForm.Source && Profile == PllNativeFlowProfile.AuthoredSurface;
            if (Name == "FormValue") return Flow || Form != PllNativeResolverForm.Zone;
            if (Name == "SourceProfileValue") return Flow && Form == PllNativeResolverForm.Source;
            if (Name is "LavaHeatRateValue" or "LavaHeatValue" or "LavaIsKilnValue")
                return Kind == PLLNativeResolverKind.Lava && Form == PllNativeResolverForm.Zone;
            if (Name == "volume") return Form == PllNativeResolverForm.Zone;
            if (Name == "surfaceRenderer") return Flow && (Form == PllNativeResolverForm.Zone || Profile == PllNativeFlowProfile.RayWaterfall);
            if (Name == "generatedSurfaceMaterial"
                || Name == "movementModifierShallow" || Name == "movementModifierDeep"
                || Name == "animModifierShallow" || Name == "animModifierDeep")
                return Kind == PLLNativeResolverKind.Water && Form == PllNativeResolverForm.Zone;
            if (Name == "presentation")
                return Kind == PLLNativeResolverKind.Rain || Kind == PLLNativeResolverKind.Wind
                    || Kind == PLLNativeResolverKind.Snow || Kind == PLLNativeResolverKind.SporeCloud;
            return true;
        }

        static void DrawGuidance(Component Component, CustomVisualParticleGraph CustomVisualGraph)
        {
            string Message = null;
            if (Component is PllCampfireAuthoring)
                Message = "Keep only the custom model and PLL references here. PLL adds the campfire and networking components during the build.";
            else if (Component is PllClimbSurfaceAuthoring)
                Message = "PLL adds a ClimbModifierSurface to this collider during the build and applies the configured rule in PEAK.";
            else if (Component is PllClimbSurfaceMaterialAuthoring)
                Message = "PLL applies these rules to colliders using the exact assigned PhysicsMaterial assets.";
            else if (Component is PllStatueAuthoring)
                Message = "Source contains no RespawnChest or PhotonView. Empty Items creates a revive-only statue.";
            else if (Component is PllLuggageAuthoring)
                Message = PllAuthoringValidation.LinkedBehavior(Component)
                    == PllBiomeBehavior.Authored
                    ? "Authored biomes cannot use Native Progression. Fixed luggage stays inactive under CampfireRoot; generated luggage is one active PropSpawner candidate root. Build staging supplies PEAK mechanics."
                    : "Choose Native Progression, one native luggage contents ID, or weighted custom items. Fixed luggage stays inactive under CampfireRoot; generated luggage is one active PropSpawner candidate root.";
            else if (Component is PllEffectAuthoring)
                Message = "The build stops and clears copied particles, disables Play On Awake and starts Presentation inactive. Source previews stay unchanged.";
            else if (Component is PllEffectZoneAuthoring)
                Message = "Native Source Triggers turn Water or Lava Sources and biome-root Lava Zones on or off. Environmental Fogless applies Caldera fog while the observed character is inside. Occupancy hooks are local and are not saved.";
            else if (Component is PLLForceSpawner)
                Message = "Enter the exact native GameObject name. PEAK finds it when loading the biome.";
            else if (Component is PLLFinalGeneration)
                Message = "Keep this source active. PLL starts its built copy inactive and generates it during PEAK's final generation phase.";
            else if (Component is PLLCriticalGeneration)
                Message = "Runs before ordinary Early generation, sorted by Order and then source key. Deferred child and post-spawn work keeps its normal timing.";
            else if (Component is PLLNoPropSpawn)
                Message = "Prevents prop placement when a raycast hits a collider beneath this marker. Materials, tags, layers and colliders stay unchanged.";
            else if (Component is PllVisualAuthoring)
                Message = "Leave Shader, Material and Property Name empty to toggle renderers only. Custom materials are included in the bundle.";
            else if (Component is PllCustomVisualAuthoring)
                Message = "These particles run while this biome is current. The build stops and clears the copied particles; source previews stay unchanged.";
            else if (Component is PllFogAuthoring)
                Message = "Mode controls the built copy's FogSphereOrigin.disableFog setting. The source prefab stays unchanged.";
            else if (Component is PllBiomeStingerAuthoring)
                Message = "Each component is a separate track. Multiple automatic stingers play together, not as random choices or a playlist.";
            else if (Component is PLLNativeVisualResolver)
                Message = "Selects native visuals from PEAK. Suppress Native Particles disables that preset's ambient particles in this biome.";
            else if (Component is PllPropZoneAuthoring)
                Message = "The volume applies to each assigned spawner. The preview warns when its sampling bounds do not overlap the zone.";
            else if (Component is PLLHookedProp)
                Message = "Hook identifies spawned instances for an external plugin. It does not run a callback or add networking.";
            if (Message != null) EditorGUILayout.HelpBox(Message, MessageType.Info);

            var Resolver = Component as PLLNativeResolver;
            if (Resolver) DrawResolverPreview(Resolver);
            var NativeVisual = Component as PLLNativeVisualResolver;
            if (NativeVisual) DrawNativeVisualPreview(NativeVisual);
            var Fog = Component as PllFogAuthoring;
            if (Fog) DrawFogPreview(Fog);
            var Stinger = Component as PllBiomeStingerAuthoring;
            if (Stinger) DrawStingerPreview(Stinger);
            var CustomVisual = Component as PllCustomVisualAuthoring;
            if (CustomVisual) DrawCustomVisualPreview(CustomVisual, CustomVisualGraph);
            var EffectZone = Component as PllEffectZoneAuthoring;
            if (EffectZone) DrawEffectZonePreview(EffectZone);
            var PropZone = Component as PllPropZoneAuthoring;
            if (PropZone) DrawPropZonePreview(PropZone);
            var Child = Component as PllChildPropAuthoring;
            if (Child) DrawChildPreview(Child);
            var Item = Component as PllItemSpawnerAuthoring;
            if (Item) DrawItemPreview(Item);
            var Effect = Component as PllEffectAuthoring;
            if (Effect) DrawEffectPreview(Effect);
            var Variation = Component as PllVariationAuthoring;
            if (Variation) DrawVariationPreview(Variation);
            var Final = Component as PLLFinalGeneration;
            if (Final) DrawFinalPreview(Final);
            if (Component is PLLCriticalGeneration Critical)
            {
                EditorGUILayout.LabelField("Primary Generation Sources", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(true))
                    foreach (var Source in Critical.GetComponentsInChildren<Component>(true))
                        if (Source is PropSpawner || Source is PllChildPropAuthoring ImmediateChild
                            && ImmediateChild.Timing == PllChildPropTiming.Immediate)
                            EditorGUILayout.ObjectField(Source, Source.GetType(), true);
            }
            var NoProp = Component as PLLNoPropSpawn;
            if (NoProp) DrawNoPropPreview(NoProp);
        }

        static void DrawResolverPreview(PLLNativeResolver Resolver)
        {
            EditorGUILayout.LabelField("Resolver Preparation", EditorStyles.boldLabel);
            if (Resolver.Form == PllNativeResolverForm.Source)
            {
                EditorGUILayout.LabelField("Native Profile", Resolver.Kind + " / " + Resolver.SourceProfile);
                EditorGUILayout.LabelField("Geometry", "Generated from the final placed pose in PEAK");
                EditorGUILayout.LabelField("Inputs", "Owned anchors and asset bindings; runtime outputs are not authored");
                EditorGUILayout.LabelField("Live Binding", "Loaded and checked in PEAK");
                return;
            }
            switch (Resolver.Kind)
            {
                case PLLNativeResolverKind.Water:
                    EditorGUILayout.LabelField("Native Shape", "__PLL_WaterZone [WaterZone]");
                    EditorGUILayout.LabelField("Surface", Resolver.SurfaceRenderer
                        ? "Retain authored MeshRenderer" : Resolver.GeneratedSurfaceMaterial
                            ? "Generate __PLL_WaterSurface [MeshFilter, MeshRenderer]" : "Assign exactly one Water surface mode");
                    EditorGUILayout.LabelField("Live Binding", "No PEAK source lookup");
                    break;
                case PLLNativeResolverKind.Lava:
                    EditorGUILayout.LabelField("Native Shape", "Owned local Lava Zone from placed surface bounds");
                    EditorGUILayout.LabelField("Live Binding", "Caldera local Lava provider; no global rising-lava controller");
                    break;
                case PLLNativeResolverKind.Rain:
                case PLLNativeResolverKind.Wind:
                case PLLNativeResolverKind.Snow:
                    EditorGUILayout.LabelField("Native Shape", "__PLL_NativeWeather [PhotonView, WindChillZone], staged inactive");
                    DrawResolverPresentation(Resolver);
                    EditorGUILayout.LabelField("Live Binding", Resolver.Kind + " fingerprint deferred until compatibility sealing");
                    break;
                case PLLNativeResolverKind.SporeCloud:
                    EditorGUILayout.LabelField("Native Shape", "__PLL_NativeSporeCloud [WindAffectedStatusEmitter, DisableBasedOnRunSettings], staged inactive");
                    DrawResolverPresentation(Resolver);
                    EditorGUILayout.LabelField("Live Binding", "Roots SporeCloud fingerprint deferred until compatibility sealing");
                    break;
                default:
                    EditorGUILayout.LabelField("Native Shape", "Choose one supported Kind");
                    break;
            }
        }

        static void DrawResolverPresentation(PLLNativeResolver Resolver)
        {
            var Presentation = Resolver.Presentation;
            EditorGUILayout.LabelField("Zone Presentation", Presentation && Presentation.Root
                ? PllHierarchy.NamePath(Resolver.transform, Presentation.Root.transform) : "Assign one owned Zone effect");
        }

        static void DrawNativeVisualPreview(PLLNativeVisualResolver Resolver)
        {
            EditorGUILayout.LabelField("Deferred Native Presentation", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Preset", Resolver.Preset.ToString());
            EditorGUILayout.LabelField("Day / Night", "One live native source profile");
            EditorGUILayout.LabelField("Ambient Particles", Resolver.SuppressNativeParticles
                ? "Suppressed for this authored biome"
                : "Enabled for this preset");
            EditorGUILayout.LabelField("Special Day", "Optional primary non-global visual values");
            EditorGUILayout.LabelField("Excluded", "Audio, hazards, lava, gameplay, networking, camera, and cutscenes");
            EditorGUILayout.LabelField("Live Binding", "Loaded and checked in PEAK");
        }

        static void DrawFogPreview(PllFogAuthoring Fog)
        {
            EditorGUILayout.LabelField("Fog Mode Preview", EditorStyles.boldLabel);
            switch (Fog.Mode)
            {
                case PllFogMode.Normal:
                    EditorGUILayout.LabelField("Rising Fog", "Ordinary PEAK behavior");
                    EditorGUILayout.LabelField("Environment", "Authored fog preserved");
                    break;
                case PllFogMode.RisingFogless:
                    EditorGUILayout.LabelField("Rising Fog", "Suppressed for this occurrence");
                    EditorGUILayout.LabelField("Environment", "Authored fog preserved");
                    break;
                case PllFogMode.Fogless:
                    EditorGUILayout.LabelField("Rising Fog", "Suppressed for this occurrence");
                    EditorGUILayout.LabelField("Environment", "Uses Caldera's environmental fog");
                    break;
                default:
                    EditorGUILayout.LabelField("Rising Fog", "Invalid Mode");
                    EditorGUILayout.LabelField("Environment", "Invalid Mode");
                    break;
            }
            EditorGUILayout.LabelField("Staged FogSphereOrigin.disableFog",
                Fog.Mode == PllFogMode.Normal ? "false (derived)"
                    : Fog.Mode == PllFogMode.RisingFogless || Fog.Mode == PllFogMode.Fogless
                        ? "true (derived)" : "Invalid Mode");
        }

        static void DrawStingerPreview(PllBiomeStingerAuthoring Stinger)
        {
            EditorGUILayout.LabelField("Biome Stinger Preview", EditorStyles.boldLabel);
            var Sources = Stinger.GetComponents<AudioSource>();
            var Source = Sources.Length == 1 ? Sources[0] : null;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Audio Source", Source, typeof(AudioSource), true);
                EditorGUILayout.ObjectField("Direct Clip", Source ? Source.clip : null, typeof(AudioClip), false);
            }
            EditorGUILayout.LabelField("Automatic Start", Stinger.Mode == PllBiomeStingerMode.OnEnter
                || Stinger.Mode == PllBiomeStingerMode.OnEnterAndEmitHook ? "On occurrence entry" : "None");
            EditorGUILayout.LabelField("Staged State",
                "Validated 2D and non-looping; playOnAwake=false; stopped at sample zero");
            EditorGUILayout.LabelField("Staged Route",
                "Cleared, then bound to the current local PEAK music output before playback");
        }

        static void DrawCustomVisualPreview(
            PllCustomVisualAuthoring Visual, CustomVisualParticleGraph Graph)
        {
            EditorGUILayout.LabelField("Constant Particle Ownership", EditorStyles.boldLabel);
            if (Graph == null)
            {
                EditorGUILayout.LabelField("Particle Graph", "Invalid; see Authoring Validation");
                return;
            }
            EditorGUILayout.LabelField("Space", Visual.Space.ToString());
            EditorGUILayout.LabelField("Root State", Visual.gameObject.activeSelf
                ? "Source active -> staged inactive" : "Source inactive -> staged inactive");
            foreach (var Particle in Graph.Particles)
            {
                var Role = Array.IndexOf(Graph.RootParticles, Particle) >= 0 ? "Root" : "Sub-emitter";
                EditorGUILayout.LabelField(PllHierarchy.NamePath(Visual.transform, Particle.transform), Role
                    + "; Loop " + Particle.main.loop + "; playOnAwake "
                    + Particle.main.playOnAwake + " -> false; staged stopped/cleared");
                var SubEmitters = Particle.subEmitters;
                for (var i = 0; i < SubEmitters.subEmittersCount; i++)
                {
                    var Child = SubEmitters.GetSubEmitterSystem(i);
                    EditorGUILayout.LabelField("  sub-emitter[" + i + "]", Child
                        ? PllHierarchy.NamePath(Visual.transform, Child.transform) : "Missing");
                }
            }
            DrawCustomVisualDependencies(Visual, Graph);
        }

        static void DrawCustomVisualDependencies(
            PllCustomVisualAuthoring Visual, CustomVisualParticleGraph Graph)
        {
            EditorGUILayout.LabelField("Presentation Dependencies", EditorStyles.miniBoldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                foreach (var Renderer in Graph.Renderers)
                {
                    var Path = PllHierarchy.NamePath(Visual.transform, Renderer.transform);
                    EditorGUILayout.ObjectField(Path, Renderer, Renderer.GetType(), true);
                    var Materials = Renderer.sharedMaterials;
                    for (var i = 0; i < Materials.Length; i++)
                    {
                        var Material = Materials[i];
                        EditorGUILayout.ObjectField("  Material[" + i + "]", Material, typeof(Material), false);
                        if (Material)
                            EditorGUILayout.ObjectField("  Shader[" + i + "]", Material.shader, typeof(Shader), false);
                    }
                    var ParticleRenderer = Renderer as ParticleSystemRenderer;
                    if (!ParticleRenderer) continue;
                    if (ParticleRenderer.renderMode == ParticleSystemRenderMode.Mesh)
                    {
                        var Meshes = new Mesh[ParticleRenderer.meshCount];
                        ParticleRenderer.GetMeshes(Meshes);
                        for (var i = 0; i < Meshes.Length; i++)
                            EditorGUILayout.ObjectField("  Mesh[" + i + "]", Meshes[i], typeof(Mesh), false);
                    }
                    var Particle = ParticleRenderer.GetComponent<ParticleSystem>();
                    if (Particle && Particle.trails.enabled)
                        EditorGUILayout.ObjectField(
                            "  Trail Material", ParticleRenderer.trailMaterial, typeof(Material), false);
                }
                foreach (var Filter in Graph.MeshFilters)
                    EditorGUILayout.ObjectField(PllHierarchy.NamePath(Visual.transform, Filter.transform) + " Mesh",
                        Filter.sharedMesh, typeof(Mesh), false);
                foreach (var Particle in Graph.Particles)
                {
                    var Lights = Particle.lights;
                    if (!Lights.light) continue;
                    EditorGUILayout.ObjectField(
                        PllHierarchy.NamePath(Visual.transform, Particle.transform) + " Light",
                        Lights.light, typeof(Light), true);
                }
            }
        }

        static void DrawCustomVisualGizmo(PllCustomVisualAuthoring Visual)
        {
            var OldColor = Handles.color;
            try
            {
                Handles.color = new Color(0.65f, 0.35f, 1f, 0.9f);
                if (Visual.Space == PllCustomVisualSpace.Segment)
                {
                    Handles.DrawWireDisc(Visual.transform.position, Visual.transform.up,
                        HandleUtility.GetHandleSize(Visual.transform.position) * 0.25f);
                    return;
                }
                var Offset = Visual.PositionOffset;
                var Distance = Visual.RepositionDistance;
                if (!float.IsFinite(Offset.x) || !float.IsFinite(Offset.y) || !float.IsFinite(Offset.z)
                    || !float.IsFinite(Distance) || Distance < 0f) return;
                var Center = Visual.transform.position + Offset;
                Handles.DrawDottedLine(Visual.transform.position, Center, 4f);
                Handles.DrawWireDisc(Center, Vector3.right, Distance);
                Handles.DrawWireDisc(Center, Vector3.up, Distance);
                Handles.DrawWireDisc(Center, Vector3.forward, Distance);
            }
            finally
            {
                Handles.color = OldColor;
            }
        }

        static void DrawEffectZonePreview(PllEffectZoneAuthoring Zone)
        {
            EditorGUILayout.LabelField("Zone Effect Ownership", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Occupancy Publication",
                Zone.OccupancyHook.Length == 0
                    ? "Disabled" : Zone.OccupancyHook);
            EditorGUILayout.LabelField("Environment Fog",
                Zone.EnvironmentFog == PllEffectZoneEnvironmentFog.EnvironmentalFogless
                    ? "Environmental Fogless" : "Inherit");
            if (Zone.Effects.Count == 0)
            {
                EditorGUILayout.LabelField(
                    Zone.EnvironmentFog == PllEffectZoneEnvironmentFog.EnvironmentalFogless
                        ? "Environmental fog only; no effects assigned."
                        : "No effects assigned.");
                return;
            }
            foreach (var Effect in Zone.Effects)
            {
                if (!Effect)
                {
                    EditorGUILayout.LabelField("Missing effect", "Invalid");
                    continue;
                }
                var Owned = Effect.Mode == PllEffectMode.Zone && Effect.Root
                    && Effect.Root.transform != Zone.transform && Effect.Root.transform.IsChildOf(Zone.transform);
                EditorGUILayout.LabelField(PllHierarchy.NamePath(Zone.transform, Effect.transform),
                    Owned ? "Owned Zone effect" : "Requires one strict-descendant Zone effect");
            }
        }

        static void DrawPropZonePreview(PllPropZoneAuthoring Zone)
        {
            EditorGUILayout.LabelField("Spawner Sampling Preview", EditorStyles.boldLabel);
            Bounds VolumeBounds;
            var VolumeValid = TryVolumeBounds(Zone.transform, Zone.Volume, out VolumeBounds);
            foreach (var Spawner in Zone.Spawners)
            {
                if (!Spawner)
                {
                    EditorGUILayout.LabelField("Missing spawner", "Invalid");
                    continue;
                }
                Bounds SamplingBounds;
                var Status = !VolumeValid || !TrySamplingBounds(Spawner, out SamplingBounds)
                    ? "Overlap unavailable" : VolumeBounds.Intersects(SamplingBounds)
                        ? Zone.Inverted ? "Detectable overlap; exclusion applies" : "Detectable overlap"
                        : "No detectable overlap";
                EditorGUILayout.LabelField(PllHierarchy.NamePath(Spawner.transform.root, Spawner.transform), Status);
            }
        }

        static bool TryVolumeBounds(Transform Root, PllZoneVolume Volume, out Bounds Bounds)
        {
            Bounds = default;
            if (!float.IsFinite(Volume.Center.x) || !float.IsFinite(Volume.Center.y) || !float.IsFinite(Volume.Center.z)) return false;
            if (Volume.Shape == PllZoneShape.Box)
            {
                if (!FinitePositive(Volume.Size.x) || !FinitePositive(Volume.Size.y) || !FinitePositive(Volume.Size.z)) return false;
            }
            else if (Volume.Shape != PllZoneShape.Sphere || !FinitePositive(Volume.Radius)) return false;
            Bounds = PllGenerationAuthoringValidator.TransformedBounds(Root, Volume);
            return true;
        }

        static bool TrySamplingBounds(PropSpawner Spawner, out Bounds Bounds)
        {
            Bounds = new Bounds(Spawner.transform.position, Vector3.zero);
            var Sphere = Spawner as PropSpawner_Sphere;
            if (Sphere)
            {
                if (!float.IsFinite(Spawner.rayLength)) return false;
                if (Sphere.rayCastSpawn) Bounds.Expand(Vector3.one * (Mathf.Abs(Spawner.rayLength) * 2f));
                return true;
            }
            var Line = Spawner as PropSpawner_Line;
            if (Line)
            {
                if (!float.IsFinite(Line.height) || !float.IsFinite(Line.coreWidth) || !float.IsFinite(Line.RayWiggle) || !float.IsFinite(Spawner.rayLength)) return false;
                Bounds.Encapsulate(Spawner.transform.position + Spawner.transform.up * (Line.height * 0.5f));
                Bounds.Encapsulate(Spawner.transform.position - Spawner.transform.up * (Line.height * 0.5f));
                if (Line.rayCastSpawn)
                {
                    var Reach = Mathf.Max(Mathf.Abs(Line.coreWidth), Mathf.Abs(Spawner.rayLength))
                        * (1f + Mathf.Sqrt(3f) * Mathf.Abs(Line.RayWiggle));
                    Bounds.Expand(Vector3.one * (Reach * 2f));
                }
                return true;
            }
            if (Spawner.GetType() != typeof(PropSpawner) || !float.IsFinite(Spawner.area.x) || !float.IsFinite(Spawner.area.y)) return false;
            if (Spawner.raycastPosition && (!float.IsFinite(Spawner.rayNearCutoff) || !float.IsFinite(Spawner.rayLength)
                || !float.IsFinite(Spawner.rayDirectionOffset.x) || !float.IsFinite(Spawner.rayDirectionOffset.y)
                || !float.IsFinite(Spawner.rayDirectionOffset.z))) return false;
            var Right = Spawner.transform.right * (Spawner.area.x * 0.5f);
            var Up = Spawner.transform.up * (Spawner.area.y * 0.5f);
            var Position = Spawner.transform.position;
            Bounds.Encapsulate(Position + Right + Up);
            Bounds.Encapsulate(Position + Right - Up);
            Bounds.Encapsulate(Position - Right + Up);
            Bounds.Encapsulate(Position - Right - Up);
            if (Spawner.raycastPosition)
            {
                var Direction = (Spawner.transform.forward * (1f + Spawner.rayNearCutoff)
                    + Spawner.rayDirectionOffset).normalized * (Spawner.rayLength - Spawner.rayNearCutoff);
                Bounds.Encapsulate(Position + Right + Up + Direction);
                Bounds.Encapsulate(Position + Right - Up + Direction);
                Bounds.Encapsulate(Position - Right + Up + Direction);
                Bounds.Encapsulate(Position - Right - Up + Direction);
            }
            return true;
        }

        static void DrawChildPreview(PllChildPropAuthoring Child)
        {
            EditorGUILayout.LabelField("Fixed-Point Timing Preview", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Timing", Child.Timing.ToString());
            var Permanent = Child.transform.root.GetComponent<CustomSegmentRuntime>();
            EditorGUILayout.LabelField("Staged Route", Permanent
                ? Child.Timing == PllChildPropTiming.Late
                    ? "One fixed spawner per Point beneath a Late PropGrouper"
                    : "One fixed spawner per Point in the owning feature phase"
                : Child.Timing == PllChildPropTiming.Late
                    ? "Parent candidate edge uses PSB_ChildSpawners"
                    : "Parent candidate edge uses PSM_ChildSpawners");
            EditorGUILayout.LabelField("Points / Weighted Candidates", Child.Points.Count + " / " + Child.Props.Count);
        }

        static void DrawItemPreview(PllItemSpawnerAuthoring Authoring)
        {
            EditorGUILayout.LabelField("Native Item Mapping", EditorStyles.boldLabel);
            var Items = Authoring.Item ? Authoring.Item.GetComponentsInChildren<Item>(true) : Array.Empty<Item>();
            EditorGUILayout.LabelField("Identity", Items.Length == 1 && Items[0]
                ? Items[0].itemID + " | " + Items[0].gameObject.name + " | "
                    + (Items[0].GetType().FullName ?? Items[0].GetType().Name)
                : "Assign a prefab containing exactly one PEAK Item");
            EditorGUILayout.LabelField("Staged Shape", "SingleItem Spawner + SpawnedItemTracker; "
                + Authoring.SpawnPoints.Count + " ordered point(s)");
            EditorGUILayout.LabelField("Live Binding", "Exact ItemDatabase prefab deferred until compatibility sealing");
        }

        static void DrawEffectPreview(PllEffectAuthoring Effect)
        {
            EditorGUILayout.LabelField("Staged Presentation Preview", EditorStyles.boldLabel);
            if (Effect.PresentationRoot)
                EditorGUILayout.LabelField("Presentation", Effect.PresentationRoot.activeSelf
                    ? "Source active -> staged inactive" : "Source inactive -> staged inactive");
            foreach (var Particle in Effect.Particles.Where(Value => Value))
                EditorGUILayout.LabelField(PllHierarchy.NamePath(Effect.transform, Particle.transform),
                    "playOnAwake " + Particle.main.playOnAwake + " -> false; staged stopped/cleared");
        }

        static void DrawVariationPreview(PllVariationAuthoring Variation)
        {
            EditorGUILayout.LabelField("Spawner Ownership Preview", EditorStyles.boldLabel);
            var Spawners = Variation.GetComponentsInChildren<PropSpawner>(true);
            if (Spawners.Length == 0)
            {
                EditorGUILayout.LabelField("No descendant PropSpawner components.");
                return;
            }
            foreach (var Spawner in Spawners)
            {
                GameObject Owner = null;
                var Overlapping = false;
                foreach (var Entry in Variation.Entries)
                {
                    if (Entry == null || !Entry.Root || !Spawner.transform.IsChildOf(Entry.Root.transform)) continue;
                    if (Owner) { Overlapping = true; break; }
                    Owner = Entry.Root;
                }
                var Ownership = Overlapping ? "Invalid overlapping entries" : Owner ? "Entry: " + Owner.name : "Shared";
                EditorGUILayout.LabelField(PllHierarchy.NamePath(Variation.transform, Spawner.transform), Ownership);
            }
        }

        static void DrawFinalPreview(PLLFinalGeneration Final)
        {
            EditorGUILayout.LabelField("Exact Final Target", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(Final.gameObject, typeof(GameObject), true);
            var Steps = Final.GetComponentsInChildren<LevelGenStep>(true);
            EditorGUILayout.LabelField("Owned Generator Steps", Steps.Length.ToString());
            using (new EditorGUI.DisabledScope(true))
                foreach (var Step in Steps) EditorGUILayout.ObjectField(Step, Step.GetType(), true);
            EditorGUILayout.LabelField("State", Final.gameObject.activeSelf
                ? "Source active -> staged inactive" : "Invalid source inactive");
        }

        static void DrawNoPropPreview(PLLNoPropSpawn NoProp)
        {
            var Colliders = NoProp.GetComponentsInChildren<Collider>(true).Where(Value => Value && Value.enabled).ToArray();
            EditorGUILayout.LabelField("Enabled Collider Coverage", Colliders.Length.ToString());
            using (new EditorGUI.DisabledScope(true))
                foreach (var Collider in Colliders) EditorGUILayout.ObjectField(Collider, Collider.GetType(), true);
        }
        static bool FinitePositive(float Value) => float.IsFinite(Value) && Value > 0f;

        static void DrawZone(Transform Root, PllZoneVolume Volume)
        {
            var OldMatrix = Handles.matrix;
            var OldColor = Handles.color;
            try
            {
                Handles.matrix = Root.localToWorldMatrix;
                Handles.color = new Color(0.2f, 0.75f, 1f, 0.9f);
                if (Volume.Shape == PllZoneShape.Box) Handles.DrawWireCube(Volume.Center, Volume.Size);
                else
                {
                    Handles.DrawWireDisc(Volume.Center, Vector3.right, Volume.Radius);
                    Handles.DrawWireDisc(Volume.Center, Vector3.up, Volume.Radius);
                    Handles.DrawWireDisc(Volume.Center, Vector3.forward, Volume.Radius);
                }
            }
            finally
            {
                Handles.matrix = OldMatrix;
                Handles.color = OldColor;
            }
        }
    }

    [CustomEditor(typeof(PllClimbSurfaceAuthoring))]
    internal sealed class PllClimbSurfaceAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ModeValue"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("EmitHookValue"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(
                "StaminaUsageMultiplierValue"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(
                "SpeedMultiplierValue"));
            serializedObject.ApplyModifiedProperties();

            var Marker = (PllClimbSurfaceAuthoring)target;
            var Layer = Marker.gameObject.layer;
            var LayerName = LayerMask.LayerToName(Layer);
            EditorGUILayout.LabelField("Current Layer", LayerName.Length == 0
                ? Layer.ToString() : Layer + " (" + LayerName + ")");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Terrain Layer (20)"))
                SetLayer(Marker, PllClimbSurfaceAuthoringValidator.TerrainLayer, "Use Terrain Layer");
            if (GUILayout.Button("Use Map Layer (21)"))
                SetLayer(Marker, PllClimbSurfaceAuthoringValidator.MapLayer, "Use Map Layer");
            EditorGUILayout.EndHorizontal();
            PllBiomeDefinitionEditor.DrawDiagnostics(PllAuthoringValidation.Validate(Marker));
        }

        static void SetLayer(PllClimbSurfaceAuthoring Marker, int Layer, string UndoName)
        {
            if (Marker.gameObject.layer == Layer) return;
            Undo.RecordObject(Marker.gameObject, UndoName);
            Marker.gameObject.layer = Layer;
            EditorUtility.SetDirty(Marker.gameObject);
        }
    }

    [CustomEditor(typeof(PllCollisionAuthoring))]
    internal sealed class PllCollisionAuthoringEditor : PllAuthoringComponentEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var Collision = (PllCollisionAuthoring)target;
            EditorGUILayout.HelpBox(Collision.Mode switch
            {
                PllCollisionMode.Automatic => "Automatic collision preserves authored shapes and stages missing static MeshColliders.",
                PllCollisionMode.Authored => "Authored collision requires an enabled nontrigger shape on this geometry or candidate owner.",
                PllCollisionMode.None => "Decorative geometry receives no generated collision; existing shapes remain unchanged.",
                _ => "Select a valid Collision mode."
            }, MessageType.Info);
        }
    }

    [CustomEditor(typeof(PllClimbSurfaceMaterialAuthoring))] internal sealed class PllClimbSurfaceMaterialAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllBiomeGroupAuthoring))] internal sealed class PllBiomeGroupAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllCampfireAuthoring))] internal sealed class PllCampfireAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllStatueAuthoring))] internal sealed class PllStatueAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllLuggageAuthoring))] internal sealed class PllLuggageAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllEffectAuthoring))] internal sealed class PllEffectAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllVisualAuthoring))] internal sealed class PllVisualAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllCustomVisualAuthoring))] internal sealed class PllCustomVisualAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllSoundEmitterAuthoring))] internal sealed class PllSoundEmitterAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllBreakablePropAuthoring))] internal sealed class PllBreakablePropAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllSwingingPropAuthoring))] internal sealed class PllSwingingPropAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllVineAuthoring))] internal sealed class PllVineAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllRestPointAuthoring))] internal sealed class PllRestPointAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllHangingRopeAuthoring))] internal sealed class PllHangingRopeAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllBiomeStingerAuthoring))] internal sealed class PllBiomeStingerAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllFogAuthoring))] internal sealed class PllFogAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllEffectZoneAuthoring))] internal sealed class PllEffectZoneAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PLLNativeResolver))] internal sealed class PllNativeResolverEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PLLNativeVisualResolver))] internal sealed class PllNativeVisualResolverEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PLLNoPropSpawn))] internal sealed class PllNoPropSpawnEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllChildPropAuthoring))] internal sealed class PllChildPropAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllItemSpawnerAuthoring))] internal sealed class PllItemSpawnerAuthoringEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PLLForceSpawner))] internal sealed class PllForceSpawnerEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PLLFinalGeneration))] internal sealed class PllFinalGenerationEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PLLCriticalGeneration))] internal sealed class PllCriticalGenerationEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PLLHookedProp))] internal sealed class PllHookedPropEditor : PllAuthoringComponentEditor { }
    [CustomEditor(typeof(PllHookedPropGenerationAuthoring))] internal sealed class PllHookedPropGenerationEditor : PllAuthoringComponentEditor { }

    internal abstract class PllReadOnlyBindingEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
            EditorGUILayout.HelpBox("Use PLL > Biomes > Create Biome to create these bindings. Edit the referenced source objects, not runtime IDs or generated components.", MessageType.Info);
        }
    }

    [CustomEditor(typeof(NativeModifiers))]
    internal sealed class PllNativeModifiersEditor : PllAuthoringComponentEditor { }

    [CustomEditor(typeof(PllNativeMaterialAuthoring))]
    internal sealed class PllNativeMaterialEditor : PllAuthoringComponentEditor { }

    [CustomEditor(typeof(PllFoodSpawnerAuthoring))]
    internal sealed class PllFoodSpawnerEditor : PllAuthoringComponentEditor { }

    [CustomEditor(typeof(PllContentSpawnerAuthoring))]
    internal sealed class PllContentSpawnerEditor : PllAuthoringComponentEditor { }

    [CustomEditor(typeof(CustomSegmentRuntime))]
    internal sealed class PllCustomSegmentRuntimeEditor : PllReadOnlyBindingEditor { }
    [CustomEditor(typeof(PllBiomePrefabAnchors))]
    internal sealed class PllBiomePrefabAnchorsEditor : PllReadOnlyBindingEditor { }
    [CustomEditor(typeof(PllBiomeRoot))]
    internal sealed class PllBiomeRootEditor : PllReadOnlyBindingEditor { }

    [CustomEditor(typeof(SegmentPort))]
    internal sealed class PllSegmentPortEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(PllAuthoringCommands.Property(serializedObject, "kind"));
            EditorGUILayout.PropertyField(PllAuthoringCommands.Property(serializedObject, "connector"));
            serializedObject.ApplyModifiedProperties();
            var Port = (SegmentPort)target;
            if (string.IsNullOrEmpty(Port.Connector) || Port.Connector.Any(Character => char.IsWhiteSpace(Character) || char.IsControl(Character)))
                EditorGUILayout.HelpBox("Connector must contain 1 to 128 characters without whitespace or control characters.", MessageType.Error);
        }
    }

    internal static class PllValidateBiomeMenu
    {
        [MenuItem("PLL/Biomes/Validate Selected Biome", priority = 4)]
        static void ValidateSelected()
        {
            PllBiomeDefinition Definition = null;
            try
            {
                Definition = PllBundleBuilder.SelectedDefinition(Selection.activeObject);
                PllBundleBuilder.RequireSavedSources(new[] { Definition });
                var Catalog = new PllSourceCatalogValidator().ValidateSelected(Definition);
                var Automatic = 0;
                var Authored = 0;
                var Decorative = 0;
                foreach (var Geometry in Catalog.CollisionGeometry)
                    if (Geometry.Mode == PllCollisionMode.None) Decorative++;
                    else if (Geometry.GeneratedMesh) Automatic++;
                    else Authored++;
                PllCreatorResultWindow.ShowValidation(
                    "Source validation passed for '" + Definition.BiomeId + "'. Automatic collision: " + Automatic
                    + "; Authored collision: " + Authored + "; Decorative: " + Decorative
                    + ". Build validates the staged copy and bundle dependencies.");
            }
            catch (InvalidOperationException Failure)
            {
                PllCreatorResultWindow.ShowFailure(Failure, Definition ? (Object)Definition : Selection.activeObject);
            }
        }
    }
}
