using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal abstract class EntityZoneBinding
{
    static readonly ConditionalWeakTable<Mob, MobAdmission> NativeMobs = new();
    static readonly ConditionalWeakTable<MushroomZombieSpawner, EntityBinding> AuthoredZombies = new();

    internal abstract IEnumerator Generate();
    internal abstract void GenerateImmediately();
    internal abstract void Clear();
    internal virtual void ReleaseAppearance() { }
    internal virtual void Publish() { }
    internal static EntityZoneBinding Empty { get; } = new EmptyPopulation();

    sealed class EmptyPopulation : EntityZoneBinding
    {
        internal override IEnumerator Generate() { yield break; }
        internal override void GenerateImmediately() { }
        internal override void Clear() { }
    }

    internal void DeclareZombieSources(FeatureOccurrence Occurrence, ISet<string> Keys)
    {
        if (this is ZombieMarkers Markers) Markers.DeclareSources(Occurrence, Keys);
    }

    internal static EntityZoneBinding Beetles(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume,
        GameObject? Prefab, NativeEntityAppearanceBinding? Appearance,
        int Count, float Chance, float Slope, float Spacing) =>
        new BeetlePopulation(Zone, Output, Volume, Prefab, Appearance, Count, Chance, Slope, Spacing);
    internal static EntityZoneBinding Zombies(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume,
        GameObject? Prefab, int Count, float Chance, float Slope, float Spacing, bool CullRandomly) =>
        new ZombieMarkers(Zone, Output, Volume, Prefab, Count, Chance, Slope, Spacing, CullRandomly);
    internal static EntityZoneBinding Condor(Transform Output, PllZoneVolume Volume,
        GameObject? Prefab, NativeEntityAppearanceBinding? Appearance, float Chance, Vector3 Nest) =>
        new CondorPopulation(Output, Volume, Prefab, Appearance, Chance, Nest);

    internal static EntityBinding BindAuthored(PllEntityAuthoring Source, FeatureOccurrence Occurrence,
        GameObject Prefab, NativeEntityAppearanceBinding? Appearance, Vector3 NestOffset) =>
        new NativeInstance(Source, Occurrence, Prefab, Appearance, NestOffset);

    readonly struct DormantBody
    {
        readonly Rigidbody Body;
        readonly RigidbodyConstraints Constraints;
        readonly bool Kinematic;

        internal DormantBody(Rigidbody Body)
        {
            this.Body = Body; Constraints = Body.constraints; Kinematic = Body.isKinematic;
            Body.isKinematic = true; Body.constraints = RigidbodyConstraints.FreezeAll;
        }

        internal void Restore() { Body.constraints = Constraints; Body.isKinematic = Kinematic; }
    }

    sealed class MobAdmission
    {
        internal readonly List<Mob> PendingStarts = new();
        internal readonly Dictionary<Mob, DormantBody> DormantBodies = new();
        internal bool Published;
        internal EntityBinding? Definition;
        internal bool Ready => Published && (Definition is null || Definition.IsReady);

        internal void Publish()
        {
            if (Published) return;
            Published = true;
            foreach (var Body in DormantBodies.Values) Body.Restore();
            DormantBodies.Clear();
            foreach (var Mob in PendingStarts) Mob.Start();
            PendingStarts.Clear();
        }

        internal void Clear() { Published = false; PendingStarts.Clear(); DormantBodies.Clear(); }
    }

    sealed class NativeInstance : EntityBinding
    {
        readonly GameObject Actor;
        readonly MobAdmission? Admission;
        readonly GameObject? Destination;
        DormantBody? CondorBody;
        bool Published;

        internal NativeInstance(PllEntityAuthoring Source, FeatureOccurrence Occurrence,
            GameObject Prefab, NativeEntityAppearanceBinding? Appearance, Vector3 NestOffset)
            : base(Source, Occurrence)
        {
            var Holder = new GameObject("__PLL_EntityPreparation"); Holder.SetActive(false);
            try
            {
                Actor = UnityEngine.Object.Instantiate(Prefab, Holder.transform, false);
                Actor.SetActive(false); Appearance?.Apply(Actor.transform); PhotonCloneIdentity.Reset(Actor);
                Actor.AddComponent<NativeBodyLifetime>().Destroyed = BodyDestroyed;
                foreach (var View in Actor.GetComponentsInChildren<PhotonView>(true)) View.FindObservables(false);
                if (Actor.TryGetComponent<Beetle>(out var Beetle))
                {
                    Admission = new() { Definition = this }; NativeMobs.Add(Beetle, Admission);
                    Admission.DormantBodies.Add(Beetle, new DormantBody(Beetle.GetComponent<Rigidbody>()));
                }
                else if (Actor.TryGetComponent<Condor>(out var Bird))
                {
                    Destination = new GameObject("NestDestination"); Destination.transform.SetParent(Holder.transform, false);
                    Destination.transform.localPosition = NestOffset; Bird.nest = Destination;
                    CondorBody = new DormantBody(Bird.rb);
                }
                else
                {
                    var Marker = Actor.GetComponent<MushroomZombieSpawner>(); Marker.cullRandomly = false;
                    AuthoredZombies.Add(Marker, this);
                }
                foreach (var Renderer in Source.ModelRoot.GetComponentsInChildren<Renderer>(true)) Renderer.enabled = false;
                foreach (var Collider in Source.ModelRoot.GetComponentsInChildren<Collider>(true)) Collider.enabled = false;
                Actor.transform.SetParent(Source.transform, false);
                Actor.transform.localPosition = Vector3.zero; Actor.transform.localRotation = Quaternion.identity;
                if (Destination) Destination.transform.SetParent(Source.transform, false);
            }
            finally { UnityEngine.Object.DestroyImmediate(Holder); }
        }

        internal override void Publish()
        {
            if (Retired || Published) return;
            Published = true; Admission?.Publish();
            if (CondorBody is { } Body) { CondorBody = null; Body.Restore(); }
            Resume();
        }
        internal override void SetEnabled(bool Value)
        {
            if (Retired) return;
            Enabled = Value;
            if (Value) Resume(); else Suspend();
        }
        internal override void Suspend()
        {
            if (Retired) return;
            Actor.SetActive(false); Occurrence.Run.NativeSpawns.RetireOutputs(Actor);
        }
        internal override void Resume() { if (!Retired) Actor.SetActive(Published && IsReady); }
        internal override void Retire()
        {
            if (Retired) return;
            try { Release(); }
            finally
            {
                if (Actor) { Actor.SetActive(false); UnityEngine.Object.DestroyImmediate(Actor); }
            }
        }
        void BodyDestroyed() => Release();
        void Release()
        {
            if (Retired) return;
            Retired = true; Admission?.Clear(); CondorBody = null;
            try { if (Actor) Occurrence.Run.NativeSpawns.RetireOutputs(Actor); }
            finally { if (Destination) UnityEngine.Object.DestroyImmediate(Destination); }
        }
    }

    sealed class NativeBodyLifetime : MonoBehaviour
    {
        internal Action? Destroyed;
        void OnDestroy() => Destroyed?.Invoke();
    }

    internal abstract class GroundPopulation : EntityZoneBinding
    {
        protected readonly Transform PlacementRoot;
        protected readonly Transform Output;
        readonly PllZoneVolume Volume;
        readonly GameObject? Prefab;
        protected readonly int Count;
        protected readonly float Chance;
        readonly float Slope;
        readonly float Spacing;

        protected GroundPopulation(Transform PlacementRoot, Transform Output, PllZoneVolume Volume,
            GameObject? Prefab, int Count, float Chance, float Slope, float Spacing)
        {
            this.PlacementRoot = PlacementRoot; this.Output = Output; this.Volume = Volume; this.Prefab = Prefab;
            this.Count = Count; this.Chance = Chance; this.Slope = Slope; this.Spacing = Spacing;
        }

        internal override IEnumerator Generate()
        {
            var Spawner = CreateSpawner();
            if (!Spawner) yield break;
            var Routine = Spawner.Execute();
            try { while (Routine.MoveNext()) yield return null; }
            finally { (Routine as IDisposable)?.Dispose(); }
        }

        internal override void GenerateImmediately()
        {
            var Spawner = CreateSpawner();
            if (Spawner) Spawner.ExecuteImmediately();
        }

        protected virtual void AddConstraints(List<PropSpawnerConstraint> Constraints) { }
        protected virtual GameObject PreparePrefab() => Prefab!;

        PropSpawner? CreateSpawner()
        {
            Clear();
            if (Count == 0 || Chance == 0f) return null;
            var SpawnPrefab = PreparePrefab();
            var Root = new GameObject(this is ZombieMarkers ? "__PLL_ZombieMarkers"
                : this is PluginEntityPopulation ? "__PLL_PluginEntities" : "__PLL_Beetles");
            Root.transform.SetParent(Output, false);
            var Bounds = Volume.Shape == PllZoneShape.Box ? Volume.Size : Vector3.one * (Volume.Radius * 2f);
            Root.transform.localPosition = Volume.Center + Vector3.up * (Bounds.y * 0.5f);
            Root.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var Spawner = Root.AddComponent<PropSpawner>();
            Spawner.area = new Vector2(
                PlacementRoot.TransformVector(Vector3.right * Bounds.x).magnitude,
                PlacementRoot.TransformVector(Vector3.forward * Bounds.z).magnitude);
            Spawner.raycastPosition = true;
            Spawner.rayLength = PlacementRoot.TransformVector(Vector3.up * Bounds.y).magnitude;
            Spawner.nrOfSpawns = Count; Spawner.randomSpawns = false;
            Spawner.chanceToUseSpawner = Chance; Spawner.syncTransforms = true;
            Spawner.layerType = HelperFunctions.LayerType.TerrainMap;
            Spawner.props = new[] { SpawnPrefab };
            Spawner.modifiers = new List<PropSpawnerMod> { new PSM_SetUpRotationToNormal() };
            Spawner.constraints = new List<PropSpawnerConstraint>
            {
                new PllNoPropSpawnConstraint(), new PllPropZoneConstraint(PlacementRoot, Volume, false),
                new MaximumSlopeConstraint(Slope),
                new PSC_SameTypeDistance { minDistance = Spacing, findAllSpawners = false, axisMultipliers = Vector3.one }
            };
            AddConstraints(Spawner.constraints);
            Spawner.postConstraints = new List<PropSpawnerConstraintPost>();
            Spawner.postSpawnBehaviors = new List<PostSpawnBehavior>();
            NativePrefabContracts.ValidationConstraints(Spawner) = new List<IValidationConstraint>();
            return Spawner;
        }

        internal override void Clear()
        {
            Map.CurrentRun?.NativeSpawns.RetireOutputs(Output.gameObject);
            for (var Index = Output.childCount - 1; Index >= 0; Index--)
                UnityEngine.Object.DestroyImmediate(Output.GetChild(Index).gameObject);
        }

        sealed class MaximumSlopeConstraint : PropSpawnerConstraint
        {
            readonly float MaximumSlope;

            internal MaximumSlopeConstraint(float MaximumSlope) => this.MaximumSlope = MaximumSlope;

            public override bool CheckConstraint(PropSpawner.SpawnData Data) =>
                Vector3.Angle(Vector3.up, Data.normal) <= MaximumSlope;
        }
    }

    sealed class BeetlePopulation : GroundPopulation
    {
        readonly NativeEntityAppearanceBinding? Appearance;
        NativeEntityAppearanceBinding.Prototype? Prototype;
        internal readonly MobAdmission Admission = new();

        internal BeetlePopulation(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume,
            GameObject? Prefab, NativeEntityAppearanceBinding? Appearance,
            int Count, float Chance, float Slope, float Spacing)
            : base(Zone.transform, Output, Volume, Prefab, Count, Chance, Slope, Spacing) => this.Appearance = Appearance;

        protected override GameObject PreparePrefab() => Appearance is null ? base.PreparePrefab()
            : (Prototype = NativeEntityAppearanceBinding.Prototype.Create(Appearance)).Prefab;

        internal override void ReleaseAppearance() { Prototype?.Dispose(); Prototype = null; }

        internal override void Clear()
        {
            Admission.Clear(); base.Clear(); ReleaseAppearance();
        }

        internal override void Publish()
        {
            Admission.Publish();
        }
    }

    sealed class ZombieMarkers : GroundPopulation
    {
        readonly bool CullRandomly;
        readonly string ZoneId;

        internal ZombieMarkers(PllEntityZoneAuthoring Zone, Transform Output, PllZoneVolume Volume,
            GameObject? Prefab, int Count, float Chance, float Slope, float Spacing, bool CullRandomly)
            : base(Zone.transform, Output, Volume, Prefab, Count, Chance, Slope, Spacing)
        { this.CullRandomly = CullRandomly; ZoneId = Zone.Id; }

        protected override void AddConstraints(List<PropSpawnerConstraint> Constraints)
        {
            if (CullRandomly) Constraints.Add(new ZombieCullConstraint());
        }

        internal void DeclareSources(FeatureOccurrence Occurrence, ISet<string> Keys)
        {
            if (Chance == 0f) return;
            if (Count > GeneratedRpcDeferral.MaximumPropertyBytes - Keys.Count)
                throw new InvalidOperationException("The declared zombie markers exceed the room property capacity.");
            var Prefix = NativeSpawnAdmission.ZombieZonePrefix(Occurrence, ZoneId);
            for (var Index = 0; Index < Count; Index++)
                Keys.Add(Prefix + Index.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    sealed class ZombieCullConstraint : PropSpawnerConstraint
    {
        public override bool CheckConstraint(PropSpawner.SpawnData Data) =>
            UnityEngine.Random.Range(0f, 1f) <= MushroomZombieSpawner.SPAWN_CHANCE;
    }

    sealed class CondorPopulation : EntityZoneBinding
    {
        readonly NativeEntityAppearanceBinding? Appearance;
        readonly Transform Output;
        readonly PllZoneVolume Volume;
        readonly GameObject? Prefab;
        readonly float Chance;
        readonly Vector3 Nest;
        DormantBody? Body;

        internal CondorPopulation(Transform Output, PllZoneVolume Volume,
            GameObject? Prefab, NativeEntityAppearanceBinding? Appearance, float Chance, Vector3 Nest)
        {
            this.Output = Output; this.Volume = Volume;
            this.Appearance = Appearance;
            this.Prefab = Prefab; this.Chance = Chance; this.Nest = Nest;
        }

        internal override IEnumerator Generate() { GenerateImmediately(); yield break; }

        internal override void GenerateImmediately()
        {
            Clear();
            if (Chance == 0f || Chance < 1f && UnityEngine.Random.value > Chance) return;
            var Root = new GameObject("__PLL_CondorPopulation");
            Root.SetActive(false); Root.transform.SetParent(Output, false);
            var Actor = UnityEngine.Object.Instantiate(Prefab!, Root.transform, false);
            Appearance?.Apply(Actor.transform);
            PhotonCloneIdentity.Reset(Actor);
            Actor.transform.localPosition = Volume.Center; Actor.transform.localRotation = Quaternion.identity;
            var Bird = Actor.GetComponent<global::Condor>();
            var Destination = new GameObject("NestDestination");
            Destination.transform.SetParent(Root.transform, false); Destination.transform.localPosition = Nest;
            Bird.nest = Destination; Body = new DormantBody(Bird.rb);
            Root.SetActive(true);
        }

        internal override void Publish()
        {
            if (Body is not { } Dormant) return;
            Body = null; Dormant.Restore();
        }

        internal override void Clear()
        {
            Body = null;
            Map.CurrentRun?.NativeSpawns.RetireOutputs(Output.gameObject);
            for (var Index = Output.childCount - 1; Index >= 0; Index--)
                UnityEngine.Object.DestroyImmediate(Output.GetChild(Index).gameObject);
        }
    }

    [HarmonyPatch(typeof(HelperFunctions), nameof(HelperFunctions.SpawnPrefab),
        new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Transform) })]
    static class EntityBirthPatch
    {
        [HarmonyPostfix]
        static void Postfix(Transform __3, GameObject __result)
        {
            if (!__3 || !__result) return;
            var Zone = __3.GetComponentInParent<PllEntityZoneAuthoring>();
            if (!Zone) return;
            if (Zone.Binding is BeetlePopulation Binding)
            {
                var Mob = __result.GetComponent<Beetle>();
                NativeMobs.Add(Mob, Binding.Admission);
                if (!Binding.Admission.Published) Binding.Admission.DormantBodies.Add(Mob, new DormantBody(Mob.rig));
            }
            else if (Zone.Binding is ZombieMarkers)
                __result.GetComponent<MushroomZombieSpawner>().cullRandomly = false;
        }
    }

    [HarmonyPatch(typeof(Mob), nameof(Mob.Start))]
    static class MobStartPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Mob __instance)
        {
            if (!NativeMobs.TryGetValue(__instance, out var Binding) || Binding.Ready) return true;
            Binding.PendingStarts.Add(__instance); return false;
        }
    }

    [HarmonyPatch(typeof(Mob), nameof(Mob.Update))]
    static class MobUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Mob __instance) =>
            (!NativeMobs.TryGetValue(__instance, out var Binding) || Binding.Ready)
            && ViewBatchSynchronization.GeneratedBatchOriginReady(__instance.photonView, Simulating: true);
    }

    [HarmonyPatch(typeof(Mob), nameof(Mob.FixedUpdate))]
    static class MobFixedUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Mob __instance) =>
            (!NativeMobs.TryGetValue(__instance, out var Binding) || Binding.Ready)
            && ViewBatchSynchronization.GeneratedBatchOriginReady(__instance.photonView, Simulating: true);
    }

    [HarmonyPatch(typeof(Mob), nameof(Mob.LateUpdate))]
    static class MobLateUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Mob __instance) =>
            (!NativeMobs.TryGetValue(__instance, out var Binding) || Binding.Ready)
            && ViewBatchSynchronization.GeneratedBatchOriginReady(__instance.photonView, Simulating: true);
    }

    [HarmonyPatch(typeof(Mob), nameof(Mob.OnDestroy))]
    static class MobDestroyPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Mob __instance)
        {
            if (!NativeMobs.TryGetValue(__instance, out var Binding)) return true;
            NativeMobs.Remove(__instance); Binding.PendingStarts.Remove(__instance);
            Binding.DormantBodies.Remove(__instance);
            return MobManager.instance && MobManager.instance.mobs.Contains(__instance);
        }
    }

    [HarmonyPatch(typeof(MushroomZombieSpawner), nameof(MushroomZombieSpawner.ReadyToSpawn))]
    static class AuthoredZombieReadyPatch
    {
        [HarmonyPrefix]
        static bool Prefix(MushroomZombieSpawner __instance, ref bool __result)
        {
            if (!AuthoredZombies.TryGetValue(__instance, out var Binding) || Binding.IsReady) return true;
            __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(MushroomZombieSpawner), nameof(MushroomZombieSpawner.Spawn))]
    static class AuthoredZombieSpawnPatch
    {
        [HarmonyPrefix]
        static bool Prefix(MushroomZombieSpawner __instance) =>
            !AuthoredZombies.TryGetValue(__instance, out var Binding) || Binding.IsReady;
    }
}
