using System;
using System.Collections;
using System.Globalization;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal abstract class HazardZoneBinding
{
    readonly Transform Output;
    protected HazardZoneBinding(Transform Output, int MaximumAlive)
    {
        this.Output = Output; this.MaximumAlive = MaximumAlive;
    }
    internal int MaximumAlive { get; }
    internal virtual void AttachOccurrence(FeatureOccurrence Occurrence) { }
    internal virtual IEnumerator Generate() { GenerateImmediately(); yield break; }
    internal abstract void GenerateImmediately();
    protected Transform OutputRoot => Output;
    internal virtual void Clear()
    {
        Map.CurrentRun?.NativeSpawns.RetireOutputs(Output.gameObject);
        for (var Index = Output.childCount - 1; Index >= 0; Index--)
            UnityEngine.Object.DestroyImmediate(Output.GetChild(Index).gameObject);
    }

    internal sealed class Authored : HazardZoneBinding
    {
        readonly AuthoredPopulation Population;
        internal Authored(PllHazardZoneAuthoring Zone, Transform Output, PllZoneVolume Volume,
            GameObject Prefab, int Count, float Chance, float Slope, float Spacing) : base(Output, Count) =>
            Population = new(Zone, Output, Volume, Prefab, Count, Chance, Slope, Spacing);
        internal override void AttachOccurrence(FeatureOccurrence Occurrence) => Population.AttachOccurrence(Occurrence);
        internal override IEnumerator Generate() => Population.Generate();
        internal override void GenerateImmediately() => Population.GenerateImmediately();
        internal override void Clear() => Population.Clear();
    }

    sealed class AuthoredPopulation : EntityZoneBinding.GroundPopulation
    {
        readonly GameObject Prefab;
        readonly string ZoneId;
        int GenerationSeed;
        internal AuthoredPopulation(PllHazardZoneAuthoring Zone, Transform Output, PllZoneVolume Volume,
            GameObject Prefab, int Count, float Chance, float Slope, float Spacing)
            : base(Zone.transform, Output, Volume, Prefab, Count, Chance, Slope, Spacing)
        { this.Prefab = Prefab; ZoneId = Zone.Id; }
        internal void AttachOccurrence(FeatureOccurrence Occurrence) =>
            GenerationSeed = FeatureGeneration.FeatureSeed(Occurrence.GenerationSeed, 0, ZoneId);
        internal override IEnumerator Generate()
        {
            var Routine = base.Generate();
            try { while (Routine.MoveNext()) yield return Routine.Current; }
            finally { (Routine as IDisposable)?.Dispose(); }
            BindIdentity();
        }
        internal override void GenerateImmediately() { base.GenerateImmediately(); BindIdentity(); }
        void BindIdentity()
        {
            if (Output.childCount == 0) return;
            var Root = Output.GetChild(0);
            for (var Ordinal = 0; Ordinal < Root.childCount; Ordinal++)
            {
                var Instance = Root.GetChild(Ordinal);
                var Identity = Instance.GetComponent<PllGeneratedPrefabIdentity>();
                if (!Identity) { Identity = Instance.gameObject.AddComponent<PllGeneratedPrefabIdentity>(); Identity.Bind(Prefab); }
                Identity.PublishActorIdentity(new ActorPlacementIdentity(ZoneId, 0, GenerationSeed, Ordinal));
            }
        }
    }

    internal sealed class Tornado : HazardZoneBinding
    {
        readonly NativeTornadoSource? Source;
        readonly Vector3[] Points;
        internal Tornado(Transform Output, NativeTornadoSource? Source, int MaximumAlive, Vector3[] Points)
            : base(Output, MaximumAlive) { this.Source = Source; this.Points = Points; }
        internal override void GenerateImmediately()
        {
            Clear();
            if (MaximumAlive == 0) return;
            var Root = new GameObject("__PLL_TornadoSource");
            Root.SetActive(false); Root.transform.SetParent(OutputRoot, false);
            Root.AddComponent<PhotonView>();
            var Spawner = Root.AddComponent<TornadoSpawner>(); Source!.Copy(Spawner);
            var PointRoot = new GameObject("TornadoPoints"); PointRoot.transform.SetParent(Root.transform, false);
            for (var Index = 0; Index < Points.Length; Index++)
            {
                var Point = new GameObject(Index.ToString("D3", CultureInfo.InvariantCulture));
                Point.transform.SetParent(PointRoot.transform, false); Point.transform.localPosition = Points[Index];
            }
            Root.SetActive(true);
        }
    }

    internal sealed class Tumbleweed : HazardZoneBinding
    {
        readonly NativeTumbleweedSource? Source;
        readonly Vector3[] Positions;
        readonly Vector3 Offset;
        internal Tumbleweed(Transform Output, NativeTumbleweedSource? Source,
            int MaximumAlive, Vector3[] Positions, Vector3 Offset) : base(Output, MaximumAlive)
        {
            this.Source = Source; this.Positions = Positions; this.Offset = Offset;
        }
        internal override void GenerateImmediately()
        {
            Clear();
            if (MaximumAlive == 0) return;
            for (var Index = 0; Index < Positions.Length; Index++)
            {
                var Root = new GameObject("__PLL_TumbleweedSource_" + Index.ToString("D3", CultureInfo.InvariantCulture));
                Root.SetActive(false); Root.transform.SetParent(OutputRoot, false); Root.transform.localPosition = Positions[Index];
                var Spawner = Root.AddComponent<NetworkedObjectSpawner>();
                Source!.Copy(Spawner, Root.AddComponent<DisableBasedOnRunSettings>());
                Spawner.spawnOffset = OutputRoot.TransformVector(Offset); Root.SetActive(true);
            }
        }
    }
}
