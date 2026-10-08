using System;
using UnityEngine;

namespace PLL.Library;

internal abstract class NativeResolverState
{
    readonly GameObject Root;
    readonly EffectState? Presentation;
    bool Current;
    bool Attempted;
    bool PresentationActive;

    NativeResolverState(GameObject Root, EffectState? Presentation)
    {
        this.Root = Root ? Root : throw new InvalidOperationException("A native resolver runtime root was destroyed.");
        this.Presentation = Presentation;
    }

    internal static NativeResolverState CreateConfigured(ResolvedNativeResolver Resolved, EffectState[] Effects)
    {
        if (Resolved is null) throw new ArgumentNullException(nameof(Resolved));
        if (Effects is null) throw new ArgumentNullException(nameof(Effects));
        EffectState? Presentation = null;
        var PresentationIndex = Resolved.PresentationEffectIndex;
        if (PresentationIndex >= 0)
        {
            if (PresentationIndex >= Effects.Length)
                throw new InvalidOperationException("A native resolver presentation binding is outside the sealed effect catalog.");
            Presentation = Effects[PresentationIndex];
        }
        var Result = Resolved.CreateState(Presentation);
        Resolved.ConfigurePlaced();
        return Result;
    }

    internal static NativeResolverState CreateWater(GameObject Root, EffectState? Presentation)
    {
        if (Presentation is not null)
            throw new InvalidOperationException("A Water resolver cannot own a presentation effect.");
        return new WaterState(Root);
    }

    internal static NativeResolverState CreateWeather(
        GameObject Root,
        WindChillZone Zone,
        EffectState? Presentation) => new WeatherState(
            Root, Zone, Presentation
                ?? throw new InvalidOperationException("A weather resolver lost its presentation effect."));

    internal static NativeResolverState CreateSporeCloud(
        GameObject Root,
        WindAffectedStatusEmitter Emitter,
        EffectState? Presentation) => new SporeCloudState(
            Root, Emitter, Presentation
                ?? throw new InvalidOperationException("A SporeCloud resolver lost its presentation effect."));

    internal void SetCurrent(bool Value, float Now, double PresentationClock)
    {
        if (Current == Value) return;
        Current = Value;
        Attempted = false;
        if (!Value)
        {
            Deactivate(Now, PresentationClock);
            return;
        }
        ActivateNative(PresentationClock);
        Update(Now, PresentationClock);
    }

    internal bool Update(float Now, double PresentationClock)
    {
        if (!Current) return SetPresentation(false, Now, PresentationClock);
        if (!Attempted) ActivateNative(PresentationClock);
        return UpdateCurrent(Now, PresentationClock);
    }

    internal void Restore()
    {
        Current = false;
        Attempted = false;
        Deactivate(0f, 0d, true);
    }

    protected GameObject RuntimeRoot => Root;
    protected abstract void Activate(double PresentationClock);
    protected abstract bool UpdateCurrent(float Now, double PresentationClock);
    protected abstract void DeactivateNative(double PresentationClock);

    protected bool SetPresentation(bool Value, float Now, double PresentationClock)
    {
        if (PresentationActive == Value) return false;
        if (!Value)
        {
            Presentation?.Deactivate(PresentationClock);
            PresentationActive = false;
            return true;
        }
        try { Presentation?.Play(Now, PresentationClock); }
        catch (Exception ActivationFailure)
        {
            try { Presentation?.Stop(); }
            catch (Exception RollbackFailure)
            {
                throw new AggregateException(
                    "Native resolver presentation activation and rollback both failed.",
                    ActivationFailure,
                    RollbackFailure);
            }
            throw;
        }
        PresentationActive = true;
        return true;
    }

    void ActivateNative(double PresentationClock)
    {
        if (Attempted || !Root || !Root.activeInHierarchy) return;
        Attempted = true;
        Activate(PresentationClock);
    }

    void Deactivate(float Now, double PresentationClock, bool Terminal = false)
    {
        Exception? Failure = null;
        try
        {
            if (Terminal) { Presentation?.Stop(); PresentationActive = false; }
            else SetPresentation(false, Now, PresentationClock);
        }
        catch (Exception Exception) { Failure = Exception; }
        try { DeactivateNative(PresentationClock); }
        catch (Exception Exception)
        {
            Failure = Failure is null
                ? Exception
                : new AggregateException("Native resolver presentation and gameplay cleanup both failed.", Failure, Exception);
        }
        if (Failure is not null) ExceptionPropagation.Throw(Failure);
    }

    sealed class WaterState : NativeResolverState
    {
        internal WaterState(GameObject Root) : base(Root, null) { }
        protected override void Activate(double PresentationClock) { }
        protected override bool UpdateCurrent(float Now, double PresentationClock) => false;
        protected override void DeactivateNative(double PresentationClock) { }
    }

    sealed class WeatherState : NativeResolverState
    {
        readonly WindChillZone Zone;
        WindChillZoneLease? Lease;

        internal WeatherState(GameObject Root, WindChillZone Zone, EffectState Presentation)
            : base(Root, Presentation)
        {
            this.Zone = Zone ? Zone : throw new InvalidOperationException("A weather resolver lost its translated WindChillZone.");
        }

        protected override void Activate(double PresentationClock)
        {
            NativeResolverRuntime.ResetWeather(Zone);
            Lease = new WindChillZoneLease(Zone, WindChillZone.instance);
            WindChillZone.instance = Zone;
            try { Zone.gameObject.SetActive(true); }
            catch
            {
                RestoreWeatherOwner();
                throw;
            }
        }

        protected override bool UpdateCurrent(float Now, double PresentationClock)
        {
            var Active = Zone && Zone.gameObject.activeInHierarchy;
            if (!Active) RestoreWeatherOwner();
            return SetPresentation(Active && Zone!.windActive && Zone.observedCharacterInsideBounds, Now, PresentationClock);
        }

        protected override void DeactivateNative(double PresentationClock)
        {
            try
            {
                if (Zone && Zone.gameObject.activeSelf) Zone.gameObject.SetActive(false);
            }
            finally
            {
                RestoreWeatherOwner();
            }
        }

        void RestoreWeatherOwner()
        {
            if (Lease is { } CurrentLease) CurrentLease.Restore();
            Lease = null;
        }

        static WindChillZone? CurrentNativeWeather()
        {
            var Index = Map.Index;
            var Slots = Map.Slots;
            if (!Map.Active || Index < 0 || Index >= Slots.Length) return null;
            var Slot = Slots[Index];
            if (Slot.Released || Slot.Source is CustomLayoutSegmentSource) return null;
            var SegmentParent = Slot.Segment.segmentParent;
            if (!SegmentParent) return null;
            WindChillZone? Result = null;
            foreach (var Candidate in SegmentParent.GetComponentsInChildren<WindChillZone>(false))
            {
                if (!Candidate || !Candidate.gameObject.activeInHierarchy) continue;
                if (Result) return null;
                Result = Candidate;
            }
            return Result;
        }

        readonly struct WindChillZoneLease
        {
            readonly WindChillZone Zone;
            readonly WindChillZone? Previous;

            internal WindChillZoneLease(WindChillZone Zone, WindChillZone? Previous)
            {
                this.Zone = Zone;
                this.Previous = Previous;
            }

            internal void Restore()
            {
                if (!ReferenceEquals(WindChillZone.instance, Zone)) return;
                WindChillZone.instance = Previous && !ReferenceEquals(Previous, Zone) && Previous.gameObject.activeInHierarchy
                    ? Previous
                    : CurrentNativeWeather();
            }
        }
    }

    sealed class SporeCloudState : NativeResolverState
    {
        readonly WindAffectedStatusEmitter Emitter;

        internal SporeCloudState(GameObject Root, WindAffectedStatusEmitter Emitter, EffectState Presentation)
            : base(Root, Presentation)
        {
            this.Emitter = Emitter
                ? Emitter
                : throw new InvalidOperationException("A SporeCloud resolver lost its translated emitter.");
        }

        protected override void Activate(double PresentationClock)
        {
            NativeResolverRuntime.CleanupSporeCloud(Emitter);
            Emitter.gameObject.SetActive(true);
        }

        protected override bool UpdateCurrent(float Now, double PresentationClock)
        {
            var Active = RuntimeRoot && RuntimeRoot.activeInHierarchy
                && Emitter && Emitter.gameObject.activeInHierarchy;
            if (Active) return SetPresentation(true, Now, PresentationClock);
            Exception? Failure = null;
            var Changed = false;
            try { NativeResolverRuntime.CleanupSporeCloud(Emitter!); }
            catch (Exception Exception) { Failure = Exception; }
            try { Changed = SetPresentation(false, Now, PresentationClock); }
            catch (Exception Exception)
            {
                Failure = Failure is null
                    ? Exception
                    : new AggregateException("SporeCloud gameplay and presentation cleanup both failed.", Failure, Exception);
            }
            if (Failure is not null) ExceptionPropagation.Throw(Failure);
            return Changed;
        }

        protected override void DeactivateNative(double PresentationClock)
        {
            Exception? Failure = null;
            try { NativeResolverRuntime.CleanupSporeCloud(Emitter); }
            catch (Exception Exception) { Failure = Exception; }
            try
            {
                if (Emitter && Emitter.gameObject.activeSelf) Emitter.gameObject.SetActive(false);
            }
            catch (Exception Exception)
            {
                Failure = Failure is null
                    ? Exception
                    : new AggregateException("SporeCloud cleanup and deactivation both failed.", Failure, Exception);
            }
            if (Failure is not null) ExceptionPropagation.Throw(Failure);
        }
    }
}
