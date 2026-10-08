using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal abstract class NativeClimbingFixtureBinding
{
    protected NativeClimbingFixtureBinding(Component Authoring, PhotonView View)
    {
        this.Authoring = Authoring; this.View = View;
    }

    internal Component Authoring { get; }
    protected PhotonView View { get; }
    protected bool Admitted;
    internal bool IsAdmitted => Admitted;
    internal virtual bool HasWork => false;
    internal virtual NativeClimbingFixtureProgress? Initialize(int Seed, double Clock) => null;
    internal virtual NativeClimbingFixtureProgress? Capture(double Clock) => null;
    internal virtual (string Hang, string Warning, string Break) Apply(NativeClimbingFixtureProgress Progress, bool Replay) =>
        throw new InvalidOperationException("Permanent climbing fixtures do not have break progress.");
    internal virtual void Tick(double Clock, float DeltaSeconds, bool CanRequest, Action<NativeClimbingFixtureProgress> Request) { }
    internal abstract void Admit();
    internal abstract void Retire();

    internal sealed class RestPoint : NativeClimbingFixtureBinding
    {
        readonly ClimbHandle Handle;
        readonly BoxCollider Interaction;
        readonly ShittyPiton? Fragile;
        readonly GameObject? NativeModel;
        NativeClimbingFixtureProgress.MountedTool? Progress;
        bool PresentingBreak;

        internal RestPoint(PllRestPointAuthoring Authoring, ClimbHandle Handle,
            PhotonView View, BoxCollider Interaction, ShittyPiton? Fragile = null,
            GameObject? NativeModel = null) : base(Authoring, View)
        {
            this.Handle = Handle; this.Interaction = Interaction;
            this.Fragile = Fragile; this.NativeModel = NativeModel;
            Handle.view = View;
            if (Fragile)
            {
                Fragile!.handle = Handle; Fragile.view = View;
                Handle.onHangStart += Fragile.OnHang; Handle.onHangStop += Fragile.OnHangStop;
            }
        }

        internal override NativeClimbingFixtureProgress? Initialize(int Seed, double Clock)
        {
            if (!Fragile) return null;
            var Previous = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(Seed);
                Progress = new NativeClimbingFixtureProgress.MountedTool(NativeClimbingFixtureProgress.Phase.Intact,
                    UnityEngine.Random.Range(1f, 5f), 0f, 4, 10f, Clock);
            }
            finally { UnityEngine.Random.state = Previous; }
            return Progress;
        }

        internal override NativeClimbingFixtureProgress? Capture(double Clock)
        {
            if (Progress is not { } State) return null;
            var Age = State.Loaded ? (float)Math.Max(0d, Clock - State.Origin) : 0f;
            return new NativeClimbingFixtureProgress.MountedTool(State.State,
                Mathf.Max(0f, State.HangRemaining - Age), State.CrackScale, State.CracksRemaining,
                State.State == NativeClimbingFixtureProgress.Phase.Breaking ? State.SinceCrack + Age : State.SinceCrack,
                Clock, State.Loaded);
        }

        internal override (string Hang, string Warning, string Break) Apply(NativeClimbingFixtureProgress Progress, bool Replay)
        {
            if (!Fragile || Progress is not NativeClimbingFixtureProgress.MountedTool State
                || State.State < this.Progress!.State)
                throw new InvalidDataException("Mounted tool progress does not match its live recipe or terminal state.");
            var Previous = this.Progress!;
            var Marker = (PllRestPointAuthoring)Authoring;
            var Hooks = Replay || !Admitted ? (string.Empty, string.Empty, string.Empty)
                : (!Previous.Loaded && State.Loaded ? Marker.HangHook : string.Empty,
                    Previous.State == NativeClimbingFixtureProgress.Phase.Intact
                        && State.State == NativeClimbingFixtureProgress.Phase.Breaking ? Marker.WarningHook : string.Empty,
                    Previous.State != NativeClimbingFixtureProgress.Phase.Broken
                        && State.State == NativeClimbingFixtureProgress.Phase.Broken ? Marker.BreakHook : string.Empty);
            this.Progress = State;
            Fragile!.totalSecondsOfHang = State.HangRemaining; Fragile.sinceCrack = State.SinceCrack;
            Fragile.crackScale = State.CrackScale; Fragile.cracksToBreak = State.CracksRemaining;
            Fragile.isBreaking = State.State != NativeClimbingFixtureProgress.Phase.Intact;
            Fragile.disabled = State.State == NativeClimbingFixtureProgress.Phase.Broken;
            if (State.State == NativeClimbingFixtureProgress.Phase.Intact) return Hooks;
            if (State.State == NativeClimbingFixtureProgress.Phase.Breaking)
            {
                Fragile.crack.SetActive(true);
                if (Replay) Fragile.crack.transform.localScale = Vector3.one * State.CrackScale;
                else if (Previous.State == NativeClimbingFixtureProgress.Phase.Intact) Fragile.crack.transform.localScale = Vector3.zero;
                else if (State.CracksRemaining < Previous.CracksRemaining)
                    foreach (var Sound in Fragile.cracKSFX) Sound.Play(Handle.transform.position);
                return Hooks;
            }
            Interaction.enabled = false; Detach();
            if (Previous.State == NativeClimbingFixtureProgress.Phase.Broken) return Hooks;
            ((PllRestPointAuthoring)Authoring).ModelRoot.SetActive(false);
            if (!Replay)
            {
                foreach (var Sound in Fragile.cracKSFX) Sound.Play(Handle.transform.position);
                PresentingBreak = true;
                try { Fragile.RPCA_Break(); }
                finally { PresentingBreak = false; }
            }
            else
            {
                foreach (Transform Child in Handle.transform) Child.gameObject.SetActive(false);
            }
            return Hooks;
        }

        internal override void Tick(double Clock, float DeltaSeconds, bool CanRequest, Action<NativeClimbingFixtureProgress> Request)
        {
            if (!Fragile || !Admitted || Progress!.State == NativeClimbingFixtureProgress.Phase.Broken) return;
            if (Progress.State == NativeClimbingFixtureProgress.Phase.Breaking)
                Fragile!.crack.transform.localScale = Vector3.Lerp(Fragile.crack.transform.localScale,
                    Vector3.one * Progress.CrackScale, DeltaSeconds * 15f);
            if (!CanRequest || !View.IsMine) return;
            var State = Progress;
            var Age = State.Loaded ? (float)Math.Max(0d, Clock - State.Origin) : 0f;
            var RemainingHang = Mathf.Max(0f, State.HangRemaining - Age);
            var SinceCrack = State.SinceCrack + (State.State == NativeClimbingFixtureProgress.Phase.Breaking ? Age : 0f);
            var Loaded = Handle.hanger && Handle.hanger.data.currentClimbHandle == Handle;
            if (Loaded != State.Loaded)
            {
                Request(new NativeClimbingFixtureProgress.MountedTool(State.State, RemainingHang,
                    State.CrackScale, State.CracksRemaining, SinceCrack, Clock, Loaded));
                return;
            }
            if (State.State == NativeClimbingFixtureProgress.Phase.Intact)
            {
                if (Loaded && RemainingHang == 0f)
                    Request(new NativeClimbingFixtureProgress.MountedTool(NativeClimbingFixtureProgress.Phase.Breaking,
                        0f, 0f, 4, SinceCrack, Clock, true));
                return;
            }
            if (SinceCrack > 1.5f)
            {
                var Remaining = State.CracksRemaining - 1;
                Request(new NativeClimbingFixtureProgress.MountedTool(Remaining == 0
                    ? NativeClimbingFixtureProgress.Phase.Broken : NativeClimbingFixtureProgress.Phase.Breaking,
                    0f, State.CrackScale + 0.75f, Remaining, 0f, Clock, Remaining != 0 && Loaded));
            }
        }

        internal override void Admit()
        {
            if (View.ViewID == 0) throw new InvalidOperationException("Rest Point interaction needs its admitted PhotonView.");
            Admitted = true;
            Interaction.enabled = Progress?.State != NativeClimbingFixtureProgress.Phase.Broken;
        }

        internal override void Retire()
        {
            Admitted = false;
            if (Fragile is not null)
            {
                if (Handle) { Handle.onHangStart -= Fragile.OnHang; Handle.onHangStop -= Fragile.OnHangStop; }
                if (Fragile.crack) UnityEngine.Object.Destroy(Fragile.crack);
                if (Fragile.vfx) UnityEngine.Object.Destroy(Fragile.vfx);
            }
            if (NativeModel) UnityEngine.Object.Destroy(NativeModel);
            if (!Handle) return;
            if (Interaction) Interaction.enabled = false;
            Detach();
        }

        void Detach()
        {
            var Hanger = Handle.hanger;
            if (!Hanger) return;
            if (Hanger.data.currentClimbHandle != Handle)
            {
                Handle.hanger = null!; return;
            }
            if (Hanger.IsLocal) Hanger.refs.climbing.CancelHandle(false);
            if (Handle.hanger == Hanger) Handle.RPCA_UnHang(Hanger.photonView);
        }

        internal bool Owns(ShittyPiton Source) => Source == Fragile;
        internal override bool HasWork => Progress is not null && Progress.State != NativeClimbingFixtureProgress.Phase.Broken;
        internal bool Owns(ClimbHandle Source) => Source == Handle;
        internal bool AcceptsHang => Admitted && Progress!.State != NativeClimbingFixtureProgress.Phase.Broken;
        internal bool NativeBreakAllowed => PresentingBreak;
    }

    internal sealed class HangingRope : NativeClimbingFixtureBinding
    {
        readonly RopeAnchorWithRope Provider;
        readonly NativeSpawnAdmission.NativeFixtureSource Output;
        readonly PllHangingRopeAuthoring Marker;
        readonly BreakableRopeAnchor? Breakable;
        readonly NativeRopeAppearance? Appearance;
        NativeClimbingFixtureProgress.RopeAnchor? Progress;

        internal HangingRope(PllHangingRopeAuthoring Authoring, RopeAnchorWithRope Provider,
            PhotonView View, GameObject NativePrefab, NativeSpawnAdmission Admission,
            FeatureOccurrence Occurrence, BreakableRopeAnchor? Breakable = null,
            NativeRopeAppearance? Appearance = null) : base(Authoring, View)
        {
            this.Provider = Provider;
            Marker = Authoring; this.Breakable = Breakable; this.Appearance = Appearance;
            Output = Admission.EnrollFixture(Occurrence, Provider,
                Authoring.Id, NativePrefab, BindPeer, ReleaseLocal);
        }

        internal override NativeClimbingFixtureProgress? Initialize(int Seed, double Clock)
        {
            if (!Breakable) return null;
            var Previous = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(Seed);
                Progress = new NativeClimbingFixtureProgress.RopeAnchor(NativeClimbingFixtureProgress.Phase.Intact,
                    UnityEngine.Random.Range(Marker.BreakTimeMinimum, Marker.BreakTimeMaximum), 0f, 0f, Clock);
            }
            finally { UnityEngine.Random.state = Previous; }
            Breakable!.anchor = Provider; Breakable.photonView = View;
            return Progress;
        }

        internal override NativeClimbingFixtureProgress? Capture(double Clock)
        {
            if (Progress is not { } State) return null;
            var Age = (float)Math.Max(0d, Clock - State.Origin);
            return new NativeClimbingFixtureProgress.RopeAnchor(State.State,
                Mathf.Max(0f, State.BreakRemaining - (State.Loaded ? Age : 0f)),
                State.State == NativeClimbingFixtureProgress.Phase.Breaking
                    ? Mathf.Min(Marker.BreakAnimationSeconds, State.Elapsed + Age) : State.Elapsed,
                State.StartSegments, Clock, State.Loaded);
        }

        internal override (string Hang, string Warning, string Break) Apply(NativeClimbingFixtureProgress Progress, bool Replay)
        {
            if (!Breakable || Progress is not NativeClimbingFixtureProgress.RopeAnchor State
                || State.BreakRemaining > Marker.BreakTimeMaximum || State.Elapsed > Marker.BreakAnimationSeconds
                || State.State != NativeClimbingFixtureProgress.Phase.Intact
                    && State.StartSegments > Marker.Segments + Marker.DropSegments
                || State.State < this.Progress!.State)
                throw new InvalidDataException("Breakable Rope progress does not match its live timing, segment or terminal recipe.");
            var Previous = this.Progress!;
            if (this.Progress!.State == NativeClimbingFixtureProgress.Phase.Intact
                && State.State != NativeClimbingFixtureProgress.Phase.Intact) Provider.StopAllCoroutines();
            this.Progress = State;
            Breakable!.willBreakInTime = State.BreakRemaining;
            Breakable.isBreaking = State.State != NativeClimbingFixtureProgress.Phase.Intact;
            ApplyRopeProgress(State);
            return Replay || !Admitted ? (string.Empty, string.Empty, string.Empty)
                : (string.Empty,
                    Previous.State == NativeClimbingFixtureProgress.Phase.Intact
                        && State.State == NativeClimbingFixtureProgress.Phase.Breaking ? Marker.WarningHook : string.Empty,
                    Previous.State != NativeClimbingFixtureProgress.Phase.Broken
                        && State.State == NativeClimbingFixtureProgress.Phase.Broken ? Marker.BreakHook : string.Empty);
        }

        void ApplyRopeProgress(NativeClimbingFixtureProgress.RopeAnchor? State, float? Elapsed = null)
        {
            if (State is null || State.State == NativeClimbingFixtureProgress.Phase.Intact || !Provider.rope) return;
            var Rope = Provider.rope;
            Rope.Segments = Mathf.Lerp(State.StartSegments, State.StartSegments + 1f, (Elapsed ?? State.Elapsed) / 0.5f);
            if (State.State != NativeClimbingFixtureProgress.Phase.Broken) return;
            DetachClimbers(Rope);
            if (Rope.photonView.IsMine && Rope.GetRopeSegments().Count != 0) Rope.Detach_Rpc(Rope.Segments);
            else
            {
                Rope.attachmenState = global::Rope.ATTACHMENT.unattached;
                Rope.GetComponent<RopeBoneVisualizer>().StartTransform = null!;
            }
        }

        internal override void Tick(double Clock, float DeltaSeconds, bool CanRequest, Action<NativeClimbingFixtureProgress> Request)
        {
            if (!Breakable || !Admitted || !Provider.rope || Progress!.State == NativeClimbingFixtureProgress.Phase.Broken) return;
            var Rope = Provider.rope;
            var State = Progress;
            var Age = (float)Math.Max(0d, Clock - State.Origin);
            var Remaining = Mathf.Max(0f, State.BreakRemaining - (State.Loaded ? Age : 0f));
            if (State.State == NativeClimbingFixtureProgress.Phase.Breaking)
            {
                var Elapsed = Mathf.Min(Marker.BreakAnimationSeconds, State.Elapsed + Age);
                ApplyRopeProgress(State, Elapsed);
                if (CanRequest && Rope.photonView.IsMine && Elapsed == Marker.BreakAnimationSeconds)
                    Request(new NativeClimbingFixtureProgress.RopeAnchor(NativeClimbingFixtureProgress.Phase.Broken,
                        0f, Elapsed, State.StartSegments, Clock));
                return;
            }
            if (!CanRequest || !Rope.photonView.IsMine) return;
            var Loaded = false;
            foreach (var Character in Rope.charactersClimbing)
                if (Character && Character.data.isRopeClimbing && Character.data.heldRope == Rope) { Loaded = true; break; }
            if (Loaded != State.Loaded)
            {
                Request(new NativeClimbingFixtureProgress.RopeAnchor(State.State, Remaining,
                    State.Elapsed, State.StartSegments, Clock, Loaded)); return;
            }
            if (Remaining == 0f && Rope.Segments >= 1f)
                Request(new NativeClimbingFixtureProgress.RopeAnchor(NativeClimbingFixtureProgress.Phase.Breaking,
                    0f, 0f, Rope.Segments + Marker.DropSegments, Clock));
        }

        internal override void Admit()
        {
            if (View.ViewID == 0) throw new InvalidOperationException("Hanging Rope creation needs its admitted anchor PhotonView.");
            Admitted = true;
            if (View.IsMine) Output.Spawn();
        }

        void BindPeer(GameObject Root)
        {
            Provider.ropeInstance = Root; Provider.rope = Root.GetComponent<Rope>();
            Appearance?.Apply(Root); ApplyRopeProgress(Progress);
        }

        void ReleaseLocal(GameObject Root)
        {
            if (Provider) Provider.StopAllCoroutines();
            var Rope = Root ? Root.GetComponent<Rope>() : null;
            if (!Rope) return;
            DetachClimbers(Rope);
            Rope.Clear(true);
            if (!Provider || Provider.ropeInstance != Root) return;
            Provider.ropeInstance = null!; Provider.rope = null!;
        }

        static void DetachClimbers(Rope Rope)
        {
            for (var Index = Rope.charactersClimbing.Count - 1; Index >= 0; Index--)
            {
                var Character = Rope.charactersClimbing[Index];
                if (!Character || Character.data.heldRope != Rope) continue;
                if (Character.IsLocal) Character.refs.ropeHandling.Stop();
                else Character.refs.ropeHandling.StopRopeClimbingRpc(false);
            }
        }

        internal override void Retire()
        {
            if (Provider) Provider.StopAllCoroutines();
            Admitted = false;
            Output.Retire();
        }

        internal bool Owns(Rope Rope) => Provider && Provider.rope == Rope;
        internal override bool HasWork => Progress is not null && Progress.State != NativeClimbingFixtureProgress.Phase.Broken;
        internal bool Owns(RopeAnchorWithRope Source) => Source == Provider;
        internal bool AcceptsAttachment => Progress?.State != NativeClimbingFixtureProgress.Phase.Broken;
        internal void FinishNativeSpawn()
        {
            if (Progress is not { State: not NativeClimbingFixtureProgress.Phase.Intact }) return;
            Provider.StopAllCoroutines(); ApplyRopeProgress(Progress);
        }
    }
}

internal sealed class NativeClimbingFixtureRuntime
{
    readonly MapRun Run;
    readonly Dictionary<Component, (FeatureOccurrence Occurrence, NativeClimbingFixtureBinding Binding,
        WorldInteractionLedger.Registration? Registration, Action<NativeClimbingFixtureProgress>? Request)> Fixtures = new();
    readonly HashSet<FeatureOccurrence> StableOccurrences = new();

    internal NativeClimbingFixtureRuntime(MapRun Run) => this.Run = Run;

    internal bool HasWork
    {
        get
        {
            foreach (var Fixture in Fixtures.Values) if (Fixture.Binding.HasWork) return true;
            return false;
        }
    }

    internal void Enroll(FeatureOccurrence Occurrence)
    {
        foreach (var Slot in Occurrence.Slots)
        {
            if (Slot.CustomResolved is not { } Resolved) continue;
            if (!BiomeAuthoringCatalog.TryGet(Slot.Source.Id, out var Contract))
                throw new InvalidOperationException($"Slot {Slot.Index} lost its climbing fixture contract.");
            if (Contract.ClimbingFixtures.Count == 0) return;
            var BindStable = StableOccurrences.Add(Occurrence);
            var Root = Resolved.Root.Value;
            if (BindStable)
                foreach (var Definition in Contract.ClimbingFixtures)
                    if (!Definition.Generated) Add(Definition.Bind(Root, Contract.Id, Run.NativeSpawns, Occurrence));
            foreach (var Source in Root.GetComponentsInChildren<PllRestPointAuthoring>(true)) BindGenerated(Source, Contract);
            foreach (var Source in Root.GetComponentsInChildren<PllHangingRopeAuthoring>(true)) BindGenerated(Source, Contract);
            break;
        }

        void BindGenerated(Component Source, BiomeAuthoringContract Contract)
        {
            if (Fixtures.ContainsKey(Source)) return;
            foreach (var Definition in Contract.ClimbingFixtures)
                if (Definition.Generated && Definition.TryBindGenerated(Source, Contract.Id,
                        Run.NativeSpawns, Occurrence, out var Binding))
                {
                    Add(Binding); return;
                }
            throw new InvalidOperationException("A generated climbing fixture has no sealed candidate binding.");
        }

        void Add(NativeClimbingFixtureBinding Binding)
        {
            var Key = Run.NativeSpawns.InteractionSourceKey(Binding.Authoring, Occurrence);
            var Initial = Binding.Initialize(NativeSpawnAdmission.InteractionSourceSeed(Occurrence, Key), Run.ReadPresentationClock() ?? 0d);
            var Registration = Initial is null ? null : Run.WorldInteractions.Register(Key,
                WorldInteractionLedger.FixtureKind, Encode(Initial),
                (Bytes, Replay) =>
                {
                    var Hooks = Binding.Apply(Decode(Bytes), Replay);
                    if (!ReferenceEquals(AuthoringRuntime.PresentationOccurrence?.Slot.Occurrence, Occurrence)) return;
                    Observe(Hooks.Hang); Observe(Hooks.Warning); Observe(Hooks.Break);
                },
                Clock => EncodeCheckpoint(Binding.Capture(Clock)!, Clock),
                (Bytes, Clock) => RestoreCheckpoint(Bytes, Clock));
            Action<NativeClimbingFixtureProgress>? Request = Registration is null ? null
                : Progress => Registration.Request(Encode(Progress));
            Fixtures.Add(Binding.Authoring, (Occurrence, Binding, Registration, Request));
        }

        static void Observe(string Hook)
        {
            if (Hook.Length == 0) return;
            AuthoringRuntime.Emit(Hook); AuthoringRuntime.EmitStinger(Hook);
        }
    }

    internal void Tick(float DeltaSeconds)
    {
        if (!Run.PresentationClockReady) return;
        var Clock = Run.PresentationClock;
        foreach (var Fixture in Fixtures.Values)
            if (Run.IsOccurrenceReady(Fixture.Occurrence) && Fixture.Registration is { Ready: true } Registration)
            {
                if (!Fixture.Binding.IsAdmitted) Fixture.Binding.Admit();
                Fixture.Binding.Tick(Clock, DeltaSeconds, Registration.CanRequest, Fixture.Request!);
            }
    }

    static byte[] Encode(NativeClimbingFixtureProgress Progress)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true)) Progress.Write(Writer);
        return Stream.ToArray();
    }

    static byte[] EncodeCheckpoint(NativeClimbingFixtureProgress Progress, double Clock)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        { Writer.Write(Clock); Progress.Write(Writer); }
        return Stream.ToArray();
    }

    internal static byte[] RebaseCheckpoint(byte[] Bytes, double Origin, double Clock) =>
        EncodeCheckpoint(Decode(RestoreCheckpoint(Bytes, Origin)), Clock);

    static byte[] RestoreCheckpoint(byte[] Bytes, double Clock)
    {
        using var Stream = new MemoryStream(Bytes, false);
        using var Reader = new BinaryReader(Stream);
        var SavedClock = Reader.ReadDouble();
        if (double.IsNaN(SavedClock) || double.IsInfinity(SavedClock) || SavedClock < 0d)
            throw new InvalidDataException("Climbing fixture checkpoint clock is invalid.");
        var Progress = NativeClimbingFixtureProgress.Read(Reader);
        if (Stream.Position != Stream.Length) throw new InvalidDataException("Climbing fixture checkpoint has trailing bytes.");
        return Encode(Progress.ShiftOrigin(Clock - SavedClock));
    }

    static NativeClimbingFixtureProgress Decode(byte[] Bytes)
    {
        using var Stream = new MemoryStream(Bytes, false);
        using var Reader = new BinaryReader(Stream);
        var Result = NativeClimbingFixtureProgress.Read(Reader);
        if (Stream.Position != Stream.Length) throw new InvalidDataException("Climbing fixture progress has trailing bytes.");
        return Result;
    }

    internal void Admit(FeatureOccurrence Occurrence, Transform? Root = null)
    {
        foreach (var Fixture in Fixtures.Values)
            if (ReferenceEquals(Fixture.Occurrence, Occurrence)
                && (Fixture.Registration is null || Fixture.Registration.Ready)
                && (Root ? TransformOwnership.Contains(Root!, Fixture.Binding.Authoring.transform)
                    : Run.HookedGeneration.OwnedByOuter(Occurrence.Container, Fixture.Binding.Authoring.transform))) Fixture.Binding.Admit();
    }

    internal void Retire(GameObject Root)
    {
        var Removed = new List<Component>();
        List<Exception>? Failures = null;
        foreach (var Fixture in Fixtures)
        {
            if (Fixture.Key && !TransformOwnership.Contains(Root.transform, Fixture.Key.transform)) continue;
            if (Fixture.Value.Registration is { } Registration) AuthoringCleanup.Capture(ref Failures, Registration.Dispose);
            AuthoringCleanup.Capture(ref Failures, Fixture.Value.Binding.Retire); Removed.Add(Fixture.Key!);
        }
        foreach (var Source in Removed) Fixtures.Remove(Source);
        StableOccurrences.RemoveWhere(Occurrence => !Occurrence.Container
            || TransformOwnership.Contains(Root.transform, Occurrence.Container));
        GeneratedGeometryBudget.ThrowFailures(Failures);
    }

    internal void Clear()
    {
        List<Exception>? Failures = null;
        foreach (var Fixture in Fixtures.Values)
        {
            if (Fixture.Registration is { } Registration) AuthoringCleanup.Capture(ref Failures, Registration.Dispose);
            AuthoringCleanup.Capture(ref Failures, Fixture.Binding.Retire);
        }
        Fixtures.Clear(); StableOccurrences.Clear();
        GeneratedGeometryBudget.ThrowFailures(Failures);
    }

    [HarmonyPatch(typeof(Rope), nameof(Rope.OnMasterClientSwitched))]
    static class FixedRopeMasterChange
    {
        [HarmonyPrefix]
        static bool Prefix(Rope __instance) => !NativeSpawnAdmission.OwnsFixtureOutput(__instance.photonView);
    }

    [HarmonyPatch(typeof(Rope), nameof(Rope.OnOwnershipTransfered))]
    static class FixedRopeOwnershipChange
    {
        [HarmonyPrefix]
        static bool Prefix(Rope __instance, PhotonView targetView) => targetView != __instance.photonView
            || !NativeSpawnAdmission.OwnsFixtureOutput(targetView);
    }

    [HarmonyPatch(typeof(Rope), nameof(Rope.Update))]
    static class PendingRopeSimulation
    {
        [HarmonyPrefix]
        static bool Prefix(Rope __instance) => NativeSpawnAdmission.FixtureOutputReady(__instance.photonView);
    }

    [HarmonyPatch(typeof(Rope), nameof(Rope.FixedUpdate))]
    static class PendingRopePhysics
    {
        [HarmonyPrefix]
        static bool Prefix(Rope __instance) => NativeSpawnAdmission.FixtureOutputReady(__instance.photonView);
    }

    [HarmonyPatch(typeof(ShittyPiton), nameof(ShittyPiton.Start))]
    static class OwnedMountedStart
    {
        [HarmonyPrefix]
        static bool Prefix(ShittyPiton __instance) => !OwnedMountedTool(__instance);
    }

    [HarmonyPatch(typeof(ShittyPiton), nameof(ShittyPiton.Update))]
    static class OwnedMountedUpdate
    {
        [HarmonyPrefix]
        static bool Prefix(ShittyPiton __instance) => !OwnedMountedTool(__instance);
    }

    [HarmonyPatch(typeof(ShittyPiton), nameof(ShittyPiton.RPCA_StartBreaking))]
    static class OwnedMountedCrack
    {
        [HarmonyPrefix]
        static bool Prefix(ShittyPiton __instance) => !OwnedMountedTool(__instance);
    }

    [HarmonyPatch(typeof(ShittyPiton), nameof(ShittyPiton.RPCA_Break))]
    static class OwnedMountedBreak
    {
        [HarmonyPrefix]
        static bool Prefix(ShittyPiton __instance)
        {
            if (!OwnedMountedTool(__instance)) return true;
            if (Map.CurrentRun is not { } Run) return false;
            foreach (var Fixture in Run.ClimbingFixtures.Fixtures.Values)
                if (Fixture.Binding is NativeClimbingFixtureBinding.RestPoint Rest && Rest.Owns(__instance))
                    return Rest.NativeBreakAllowed;
            return false;
        }
    }

    static bool OwnedMountedTool(ShittyPiton Source) => Source.GetComponentInParent<PllRestPointAuthoring>() is { } Marker
        && Marker.Profile == PllRestPointProfile.NativeFragileMountedTool;

    [HarmonyPatch(typeof(ClimbHandle), nameof(ClimbHandle.RPCA_Hang))]
    static class TerminalMountedHang
    {
        [HarmonyPrefix]
        static bool Prefix(ClimbHandle __instance)
        {
            var Marker = __instance.GetComponentInParent<PllRestPointAuthoring>();
            if (!Marker || Marker.Profile != PllRestPointProfile.NativeFragileMountedTool) return true;
            if (Map.CurrentRun is not { } Run) return false;
            foreach (var Fixture in Run.ClimbingFixtures.Fixtures.Values)
                if (Fixture.Binding is NativeClimbingFixtureBinding.RestPoint Rest && Rest.Owns(__instance))
                    return Rest.AcceptsHang;
            return false;
        }
    }

    [HarmonyPatch(typeof(BreakableRopeAnchor), nameof(BreakableRopeAnchor.Start))]
    static class OwnedAnchorStart
    {
        [HarmonyPrefix]
        static bool Prefix(BreakableRopeAnchor __instance) => !OwnedBreakableAnchor(__instance);
    }

    [HarmonyPatch(typeof(BreakableRopeAnchor), nameof(BreakableRopeAnchor.Update))]
    static class OwnedAnchorUpdate
    {
        [HarmonyPrefix]
        static bool Prefix(BreakableRopeAnchor __instance) => !OwnedBreakableAnchor(__instance);
    }

    static bool OwnedBreakableAnchor(BreakableRopeAnchor Source) => Source.TryGetComponent<PllHangingRopeAuthoring>(out var Marker)
        && Marker.AnchorBehavior == PllRopeAnchorBehavior.NativeBreakableAnchor;

    [HarmonyPatch(typeof(Rope), nameof(Rope.AttachToAnchor_Rpc))]
    static class TerminalRopeAttachment
    {
        [HarmonyPrefix]
        static bool Prefix(Rope __instance)
        {
            if (!NativeSpawnAdmission.OwnsFixtureOutput(__instance.photonView) || Map.CurrentRun is not { } Run) return true;
            foreach (var Fixture in Run.ClimbingFixtures.Fixtures.Values)
                if (Fixture.Binding is NativeClimbingFixtureBinding.HangingRope Rope && Rope.Owns(__instance))
                    return Rope.AcceptsAttachment;
            return true;
        }
    }

    [HarmonyPatch(typeof(RopeAnchorWithRope), nameof(RopeAnchorWithRope.SpawnRope))]
    static class RestoredRopeSpooling
    {
        [HarmonyPostfix]
        static void Postfix(RopeAnchorWithRope __instance)
        {
            if (Map.CurrentRun is not { } Run) return;
            foreach (var Fixture in Run.ClimbingFixtures.Fixtures.Values)
                if (Fixture.Binding is NativeClimbingFixtureBinding.HangingRope Rope && Rope.Owns(__instance))
                { Rope.FinishNativeSpawn(); return; }
        }
    }

    [HarmonyPatch(typeof(RopeBoneVisualizer), nameof(RopeBoneVisualizer.LateUpdate))]
    static class PendingRopeAppearance
    {
        [HarmonyPrefix]
        static bool Prefix(RopeBoneVisualizer __instance) => NativeSpawnAdmission.FixtureOutputReady(__instance.view);
    }
}
