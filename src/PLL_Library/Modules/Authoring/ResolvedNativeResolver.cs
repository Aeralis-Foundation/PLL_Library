using System;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class ResolvedNativeResolver
{
    readonly ResolverAlternative Variant;

    ResolvedNativeResolver(GameObject Root, ResolverAlternative Variant)
    {
        this.Root = Root;
        this.Variant = Variant;
    }

    GameObject Root { get; }
    internal int PresentationEffectIndex => Variant.PresentationEffectIndex;

    internal static ResolvedNativeResolver CreateWater(
        GameObject Root,
        PllZoneVolume Volume,
        WaterZone Zone,
        MeshRenderer Plane,
        float MovementShallow,
        float MovementDeep,
        float AnimationShallow,
        float AnimationDeep)
    {
        if (!Root || !Zone || !Plane)
            throw new InvalidOperationException("A resolved Water alternative is incomplete.");
        return new ResolvedNativeResolver(Root, new WaterAlternative(
            Volume, Zone, Plane, MovementShallow, MovementDeep, AnimationShallow, AnimationDeep));
    }

    internal static ResolvedNativeResolver CreateWeather(
        GameObject Root,
        PLLNativeResolverKind Kind,
        PllZoneVolume Volume,
        int PresentationEffectIndex,
        PhotonView View,
        WindChillZone Zone)
    {
        if (!Root || !View || !Zone || PresentationEffectIndex < 0)
            throw new InvalidOperationException("A resolved weather alternative is incomplete.");
        return new ResolvedNativeResolver(
            Root, new WeatherAlternative(Kind, Volume, PresentationEffectIndex, View, Zone));
    }

    internal static ResolvedNativeResolver CreateSporeCloud(
        GameObject Root,
        PllZoneVolume Volume,
        int PresentationEffectIndex,
        WindAffectedStatusEmitter Emitter,
        DisableBasedOnRunSettings Setting)
    {
        if (!Root || !Emitter || !Setting || PresentationEffectIndex < 0)
            throw new InvalidOperationException("A resolved SporeCloud alternative is incomplete.");
        return new ResolvedNativeResolver(
            Root, new SporeCloudAlternative(Volume, PresentationEffectIndex, Emitter, Setting));
    }

    internal void ConfigurePlaced() => Variant.ConfigurePlaced(Root);
    internal NativeResolverState CreateState(EffectState? Presentation) => Variant.CreateState(Root, Presentation);

    abstract class ResolverAlternative
    {
        protected ResolverAlternative(int PresentationEffectIndex)
        {
            this.PresentationEffectIndex = PresentationEffectIndex;
        }

        internal int PresentationEffectIndex { get; }
        internal abstract void ConfigurePlaced(GameObject Root);
        internal abstract NativeResolverState CreateState(GameObject Root, EffectState? Presentation);
    }

    sealed class WaterAlternative : ResolverAlternative
    {
        readonly PllZoneVolume Volume;
        readonly WaterZone Zone;
        readonly MeshRenderer Plane;
        readonly float MovementShallow;
        readonly float MovementDeep;
        readonly float AnimationShallow;
        readonly float AnimationDeep;

        internal WaterAlternative(
            PllZoneVolume Volume,
            WaterZone Zone,
            MeshRenderer Plane,
            float MovementShallow,
            float MovementDeep,
            float AnimationShallow,
            float AnimationDeep)
            : base(-1)
        {
            this.Volume = Volume;
            this.Zone = Zone;
            this.Plane = Plane;
            this.MovementShallow = MovementShallow;
            this.MovementDeep = MovementDeep;
            this.AnimationShallow = AnimationShallow;
            this.AnimationDeep = AnimationDeep;
        }

        internal override void ConfigurePlaced(GameObject Root) => NativeResolverRuntime.ConfigureWater(
            Root, Volume, Zone, Plane, MovementShallow, MovementDeep, AnimationShallow, AnimationDeep);

        internal override NativeResolverState CreateState(GameObject Root, EffectState? Presentation) =>
            NativeResolverState.CreateWater(Root, Presentation);
    }

    sealed class WeatherAlternative : ResolverAlternative
    {
        readonly PLLNativeResolverKind Kind;
        readonly PllZoneVolume Volume;
        readonly PhotonView View;
        readonly WindChillZone Zone;

        internal WeatherAlternative(
            PLLNativeResolverKind Kind,
            PllZoneVolume Volume,
            int PresentationEffectIndex,
            PhotonView View,
            WindChillZone Zone)
            : base(PresentationEffectIndex)
        {
            if (Kind is not (PLLNativeResolverKind.Rain or PLLNativeResolverKind.Wind or PLLNativeResolverKind.Snow))
                throw new InvalidOperationException($"Native resolver kind '{Kind}' is not weather.");
            this.Kind = Kind;
            this.Volume = Volume;
            this.View = View;
            this.Zone = Zone;
        }

        internal override void ConfigurePlaced(GameObject Root) =>
            NativeResolverRuntime.ConfigureWeather(Kind, Root, Volume, View, Zone);

        internal override NativeResolverState CreateState(GameObject Root, EffectState? Presentation) =>
            NativeResolverState.CreateWeather(Root, Zone, Presentation);
    }

    sealed class SporeCloudAlternative : ResolverAlternative
    {
        readonly PllZoneVolume Volume;
        readonly WindAffectedStatusEmitter Emitter;
        readonly DisableBasedOnRunSettings Setting;

        internal SporeCloudAlternative(
            PllZoneVolume Volume,
            int PresentationEffectIndex,
            WindAffectedStatusEmitter Emitter,
            DisableBasedOnRunSettings Setting)
            : base(PresentationEffectIndex)
        {
            this.Volume = Volume;
            this.Emitter = Emitter;
            this.Setting = Setting;
        }

        internal override void ConfigurePlaced(GameObject Root) =>
            NativeResolverRuntime.ConfigureSporeCloud(Root, Volume, Emitter, Setting);

        internal override NativeResolverState CreateState(GameObject Root, EffectState? Presentation) =>
            NativeResolverState.CreateSporeCloud(Root, Emitter, Presentation);
    }
}
