using System;
using System.Globalization;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using IEnumerator = System.Collections.IEnumerator;

namespace PLL.Library;

internal sealed class RunProtocolIdentity
{
    internal RunProtocolIdentity(
        string ProtocolRevision,
        string SegmentManifestRevision,
        string LayoutManifestRevision,
        string GenerationEpoch)
    {
        this.ProtocolRevision = ProtocolRevision;
        this.SegmentManifestRevision = SegmentManifestRevision;
        this.LayoutManifestRevision = LayoutManifestRevision;
        this.GenerationEpoch = GenerationEpoch;
    }

    internal string ProtocolRevision { get; }
    internal string SegmentManifestRevision { get; }
    internal string LayoutManifestRevision { get; }
    internal string GenerationEpoch { get; }
}

internal sealed class RunLayoutConfiguration
{
    internal RunLayoutConfiguration(
        int Seed,
        bool ReplaceGenerated,
        LayoutId SelectedLayoutId,
        bool IsLibraryBase,
        string Order,
        bool OrderEnabled,
        bool Generative,
        bool Infinite,
        string Repeatable,
        string Chances,
        int Scale,
        int BeginIndex,
        bool RemoveOld)
    {
        this.Seed = Seed;
        this.ReplaceGenerated = ReplaceGenerated;
        this.SelectedLayoutId = SelectedLayoutId;
        this.IsLibraryBase = IsLibraryBase;
        this.Order = Order;
        this.OrderEnabled = OrderEnabled;
        this.Generative = Generative;
        this.Infinite = Infinite;
        this.Repeatable = Repeatable;
        this.Chances = Chances;
        this.Scale = Scale;
        this.BeginIndex = BeginIndex;
        this.RemoveOld = RemoveOld;
    }

    internal int Seed { get; }
    internal bool ReplaceGenerated { get; }
    internal LayoutId SelectedLayoutId { get; }
    internal bool IsLibraryBase { get; }
    internal string Order { get; }
    internal bool OrderEnabled { get; }
    internal bool Generative { get; }
    internal bool Infinite { get; }
    internal string Repeatable { get; }
    internal string Chances { get; }
    internal int Scale { get; }
    internal int BeginIndex { get; }
    internal bool RemoveOld { get; }
}

internal sealed class RunTerminalConfiguration
{
    internal RunTerminalConfiguration(
        bool Winnable,
        bool PeakAtEnd,
        bool GemlessNadir,
        bool ValidNadir)
    {
        this.Winnable = Winnable;
        this.PeakAtEnd = PeakAtEnd;
        this.GemlessNadir = GemlessNadir;
        this.ValidNadir = ValidNadir;
    }

    internal bool Winnable { get; }
    internal bool PeakAtEnd { get; }
    internal bool GemlessNadir { get; }
    internal bool ValidNadir { get; }
}

internal sealed class RunEnvironmentConfiguration
{
    internal RunEnvironmentConfiguration(
        bool WaterFollowsBeach,
        bool VoidWaterFollowsNadir,
        bool RelocatePlane,
        bool RelocateStart,
        bool NoSkyProps,
        bool Fogless,
        bool NoRisingGhosts,
        bool NoSleepFog,
        bool NoWater,
        bool NoVoidWater,
        bool NoPlane,
        bool RemoveOldZones)
    {
        this.WaterFollowsBeach = WaterFollowsBeach;
        this.VoidWaterFollowsNadir = VoidWaterFollowsNadir;
        this.RelocatePlane = RelocatePlane;
        this.RelocateStart = RelocateStart;
        this.NoSkyProps = NoSkyProps;
        this.Fogless = Fogless;
        this.NoRisingGhosts = NoRisingGhosts;
        this.NoSleepFog = NoSleepFog;
        this.NoWater = NoWater;
        this.NoVoidWater = NoVoidWater;
        this.NoPlane = NoPlane;
        this.RemoveOldZones = RemoveOldZones;
    }

    internal bool WaterFollowsBeach { get; }
    internal bool VoidWaterFollowsNadir { get; }
    internal bool RelocatePlane { get; }
    internal bool RelocateStart { get; }
    internal bool NoSkyProps { get; }
    internal bool Fogless { get; }
    internal bool NoRisingGhosts { get; }
    internal bool NoSleepFog { get; }
    internal bool NoWater { get; }
    internal bool NoVoidWater { get; }
    internal bool NoPlane { get; }
    internal bool RemoveOldZones { get; }
}

internal sealed class ResolvedRunConfiguration
{
    internal ResolvedRunConfiguration(
        RunProtocolIdentity Protocol,
        RunLayoutConfiguration Layout,
        RunTerminalConfiguration Terminal,
        RunEnvironmentConfiguration Environment,
        bool LazyGeneration,
        BiomeLayoutConfigSnapshot Biomes)
    {
        this.Protocol = Protocol ?? throw new ArgumentNullException(nameof(Protocol));
        this.Layout = Layout ?? throw new ArgumentNullException(nameof(Layout));
        this.Terminal = Terminal ?? throw new ArgumentNullException(nameof(Terminal));
        this.Environment = Environment ?? throw new ArgumentNullException(nameof(Environment));
        this.LazyGeneration = LazyGeneration;
        this.Biomes = Biomes ?? throw new ArgumentNullException(nameof(Biomes));
    }

    internal RunProtocolIdentity Protocol { get; }
    internal RunLayoutConfiguration Layout { get; }
    internal RunTerminalConfiguration Terminal { get; }
    internal RunEnvironmentConfiguration Environment { get; }
    internal bool LazyGeneration { get; }
    internal BiomeLayoutConfigSnapshot Biomes { get; }
    internal MapLayoutMode Mode => MapLayoutModeResolver.Resolve(
        Layout.SelectedLayoutId, Layout.OrderEnabled, Layout.Generative, Layout.Infinite);
}

internal static class RunConfigurationCodec
{
    internal const string PropertyKey = PLLLibrary.PluginGuid + "/c";
    internal const string ProtocolRevision = "75";
    internal const int MaximumPayloadBytes = short.MaxValue;
    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static ResolvedRunConfiguration CaptureLocal(
        CapturedLayoutSelection? RestoredSelection = null,
        ResolvedRunConfiguration? StartupOverride = null)
    {
        if (StartupOverride is not null) return StartupOverride;
        var RestoredBiomes = RestoredSelection.HasValue ? Map.Ingress.LoadedQuicksaveLayout?.ParsedBiomes : null;
        var NativeVariations = FileManager.ReloadRunConfiguration(RestoredBiomes?.NativeVariations);
        var GenerationEpoch = Guid.NewGuid().ToString("N");
        var Selection = RestoredSelection ?? TerminalLayoutSelection.Capture(
            FileManager.LayoutOrder.Value, FileManager.DoOrder.Value);
        var Repeatable = FileManager.InfiniteMapRepeatable.Value;
        var Chances = FileManager.InfiniteMapChances.Value;
        LayoutChoiceEncoding.RequireValid(Selection.RawOrder, Repeatable, Chances);
        var Layout = new RunLayoutConfiguration(
            MapQuicksave.ResolveConfiguredSeed(FileManager.Seed.Value),
            FileManager.ReplaceGenerated.Value,
            Selection.Id,
            Selection.IsLibraryBase,
            Selection.RawOrder,
            Selection.DoOrder,
            FileManager.GenerativeMap.Value,
            FileManager.InfiniteMap.Value,
            Repeatable,
            Chances,
            FileManager.InfiniteMapScale.Value,
            FileManager.InfiniteMapBeginIndex.Value,
            FileManager.InfiniteMapRemoveOld.Value);
        if (Layout.Scale < 1 || Layout.Scale > byte.MaxValue)
            throw new InvalidOperationException("The local run settings are invalid.");
        return new ResolvedRunConfiguration(
            new RunProtocolIdentity(
                ProtocolRevision,
                SegmentRegistry.StartupManifestRevision,
                LayoutRegistry.ManifestRevision,
                GenerationEpoch),
            Layout,
            new RunTerminalConfiguration(
                FileManager.Winnable.Value,
                FileManager.PeakAtEnd.Value,
                FileManager.GemlessNadir.Value,
                FileManager.ValidNadir.Value),
            new RunEnvironmentConfiguration(
                FileManager.WaterFollowsBeach.Value,
                FileManager.VoidWaterFollowsNadir.Value,
                FileManager.RelocatePlane.Value,
                FileManager.RelocateStart.Value,
                FileManager.NoSkyProps.Value,
                FileManager.Fogless.Value,
                FileManager.NoRisingGhosts.Value,
                FileManager.NoSleepFog.Value,
                FileManager.NoWater.Value,
                FileManager.NoVoidWater.Value,
                FileManager.NoPlane.Value,
                FileManager.RemoveOld.Value),
            FileManager.LazyGeneration.Value,
            RestoredBiomes ?? CustomBiomeConfigRegistry.Capture(NativeVariations));
    }

    internal static string Serialize(ResolvedRunConfiguration Configuration)
    {
        if (Configuration is null) throw new ArgumentNullException(nameof(Configuration));
        var Result = new StringBuilder();
        LengthPrefixedText.Append(Result, Configuration.Protocol.ProtocolRevision);
        LengthPrefixedText.Append(Result, Configuration.Protocol.SegmentManifestRevision);
        LengthPrefixedText.Append(Result, Configuration.Protocol.LayoutManifestRevision);
        LengthPrefixedText.Append(Result, Configuration.Protocol.GenerationEpoch);
        LengthPrefixedText.Append(Result, Configuration.Layout.Seed.ToString(CultureInfo.InvariantCulture));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Layout.ReplaceGenerated));
        LengthPrefixedText.Append(Result, Configuration.Layout.SelectedLayoutId.Value);
        LengthPrefixedText.Append(Result, Boolean(Configuration.Layout.IsLibraryBase));
        LengthPrefixedText.Append(Result, Configuration.Layout.Order);
        LengthPrefixedText.Append(Result, Boolean(Configuration.Layout.OrderEnabled));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Layout.Generative));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Layout.Infinite));
        LengthPrefixedText.Append(Result, Configuration.Layout.Repeatable);
        LengthPrefixedText.Append(Result, Configuration.Layout.Chances);
        LengthPrefixedText.Append(Result, Configuration.Layout.Scale.ToString(CultureInfo.InvariantCulture));
        LengthPrefixedText.Append(Result, Configuration.Layout.BeginIndex.ToString(CultureInfo.InvariantCulture));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Layout.RemoveOld));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Terminal.Winnable));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Terminal.PeakAtEnd));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Terminal.GemlessNadir));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Terminal.ValidNadir));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.WaterFollowsBeach));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.VoidWaterFollowsNadir));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.RelocatePlane));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.RelocateStart));
        LengthPrefixedText.Append(Result, Boolean(Configuration.LazyGeneration));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.NoSkyProps));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.Fogless));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.NoRisingGhosts));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.NoSleepFog));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.NoWater));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.NoVoidWater));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.NoPlane));
        LengthPrefixedText.Append(Result, Boolean(Configuration.Environment.RemoveOldZones));
        LengthPrefixedText.Append(Result, Configuration.Biomes.Serialized);
        var Payload = Result.ToString();
        if (!ValidPayload(Payload))
            throw new InvalidOperationException(
                $"PLL Library's serialized room configuration is not valid UTF-8 text within {MaximumPayloadBytes} bytes.");
        return Payload;
    }

    internal static bool TryParse(
        string Data,
        out ResolvedRunConfiguration Configuration,
        out string Failure)
    {
        Configuration = null!;
        Failure = "The host sent invalid PLL room settings.";
        var Offset = 0;
        if (!ValidPayload(Data)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var ProtocolRevisionValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var SegmentManifestRevision)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var LayoutManifestRevision)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var GenerationEpoch)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var SeedValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var ReplaceGeneratedValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var SelectedLayoutValue, LayoutChoiceEncoding.MaximumFieldLength)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var IsLibraryBaseValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var Order, LayoutChoiceEncoding.MaximumFieldLength)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var OrderEnabledValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var GenerativeValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var InfiniteValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var Repeatable, LayoutChoiceEncoding.MaximumFieldLength)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var Chances, LayoutChoiceEncoding.MaximumFieldLength)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var ScaleValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var BeginIndexValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var RemoveOldValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var WinnableValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var PeakAtEndValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var GemlessNadirValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var ValidNadirValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var WaterFollowsBeachValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var VoidWaterFollowsNadirValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var RelocatePlaneValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var RelocateStartValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var LazyGenerationValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var NoSkyPropsValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var FoglessValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var NoRisingGhostsValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var NoSleepFogValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var NoWaterValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var NoVoidWaterValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var NoPlaneValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var RemoveOldZonesValue)
            || !LengthPrefixedText.TryRead(Data, ref Offset, out var BiomeConfigurationValue, MaximumPayloadBytes)
            || Offset != Data.Length
            || !TryBoolean(ReplaceGeneratedValue, out var ReplaceGenerated)
            || !TryBoolean(IsLibraryBaseValue, out var IsLibraryBase)
            || !TryBoolean(OrderEnabledValue, out var OrderEnabled)
            || !TryBoolean(GenerativeValue, out var Generative)
            || !TryBoolean(InfiniteValue, out var Infinite)
            || !TryBoolean(RemoveOldValue, out var RemoveOld)
            || !TryBoolean(WinnableValue, out var Winnable)
            || !TryBoolean(PeakAtEndValue, out var PeakAtEnd)
            || !TryBoolean(GemlessNadirValue, out var GemlessNadir)
            || !TryBoolean(ValidNadirValue, out var ValidNadir)
            || !TryBoolean(WaterFollowsBeachValue, out var WaterFollowsBeach)
            || !TryBoolean(VoidWaterFollowsNadirValue, out var VoidWaterFollowsNadir)
            || !TryBoolean(RelocatePlaneValue, out var RelocatePlane)
            || !TryBoolean(RelocateStartValue, out var RelocateStart)
            || !TryBoolean(LazyGenerationValue, out var LazyGeneration)
            || !TryBoolean(NoSkyPropsValue, out var NoSkyProps)
            || !TryBoolean(FoglessValue, out var Fogless)
            || !TryBoolean(NoRisingGhostsValue, out var NoRisingGhosts)
            || !TryBoolean(NoSleepFogValue, out var NoSleepFog)
            || !TryBoolean(NoWaterValue, out var NoWater)
            || !TryBoolean(NoVoidWaterValue, out var NoVoidWater)
            || !TryBoolean(NoPlaneValue, out var NoPlane)
            || !TryBoolean(RemoveOldZonesValue, out var RemoveOldZones)
            || !LayoutChoiceEncoding.IsValid(Order) || !LayoutChoiceEncoding.IsValid(Repeatable)
                || !LayoutChoiceEncoding.IsValid(Chances)
            || !Guid.TryParseExact(GenerationEpoch, "N", out _)
            || !int.TryParse(SeedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var Seed)
            || !int.TryParse(ScaleValue, NumberStyles.None, CultureInfo.InvariantCulture, out var Scale)
            || !int.TryParse(
                BeginIndexValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var BeginIndex)
            || !LayoutId.TryParse(SelectedLayoutValue, out var SelectedLayoutId)
            || Scale < 1 || Scale > byte.MaxValue) return false;

        if (!string.Equals(ProtocolRevisionValue, ProtocolRevision, StringComparison.Ordinal))
        {
            Failure = $"PLL Library protocol mismatch: host {ProtocolRevisionValue}, client {ProtocolRevision}.";
            return false;
        }
        if (!string.Equals(
            SegmentManifestRevision, SegmentRegistry.StartupManifestRevision, StringComparison.Ordinal))
        {
            Failure = "The players have different custom biomes.";
            return false;
        }
        if (!string.Equals(LayoutManifestRevision, LayoutRegistry.ManifestRevision, StringComparison.Ordinal))
        {
            Failure = "The players have different registered layouts.";
            return false;
        }
        if (!CustomBiomeConfigRegistry.TryParseSnapshot(
            BiomeConfigurationValue, out var BiomeConfigurations))
        {
            Failure = "The host sent invalid custom biome settings.";
            return false;
        }
        if (!ValidSelection(SelectedLayoutId, Order, OrderEnabled, IsLibraryBase))
        {
            Failure = "The host sent inconsistent layout settings.";
            return false;
        }

        Configuration = new ResolvedRunConfiguration(
            new RunProtocolIdentity(
                ProtocolRevisionValue,
                SegmentManifestRevision,
                LayoutManifestRevision,
                GenerationEpoch),
            new RunLayoutConfiguration(
                Seed,
                ReplaceGenerated,
                SelectedLayoutId,
                IsLibraryBase,
                Order,
                OrderEnabled,
                Generative,
                Infinite,
                Repeatable,
                Chances,
                Scale,
                BeginIndex,
                RemoveOld),
            new RunTerminalConfiguration(
                Winnable, PeakAtEnd, GemlessNadir, ValidNadir),
            new RunEnvironmentConfiguration(
                WaterFollowsBeach,
                VoidWaterFollowsNadir,
                RelocatePlane,
                RelocateStart,
                NoSkyProps,
                Fogless,
                NoRisingGhosts,
                NoSleepFog,
                NoWater,
                NoVoidWater,
                NoPlane,
                RemoveOldZones),
            LazyGeneration,
            BiomeConfigurations);
        Failure = string.Empty;
        return true;
    }

    static string Boolean(bool Value) => Value ? "1" : "0";

    static bool TryBoolean(string Value, out bool Result)
    {
        Result = string.Equals(Value, "1", StringComparison.Ordinal);
        return Result || string.Equals(Value, "0", StringComparison.Ordinal);
    }

    static bool ValidPayload(string Value)
    {
        try
        {
            return StrictUtf8.GetByteCount(Value) <= MaximumPayloadBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    static bool ValidSelection(
        LayoutId Id,
        string Order,
        bool OrderEnabled,
        bool IsLibraryBase)
    {
        if (IsLibraryBase)
            return Id.IsDefault && OrderEnabled
                && string.Equals(Order, TerminalLayoutSelection.LibraryBaseOrder, StringComparison.Ordinal);
        if (Id.IsDefault) return true;
        if (!OrderEnabled || !LayoutRegistry.TryGet(Id, out var Definition)) return false;
        return string.Equals(Order, TerminalLayoutSelection.RawOrder(Definition), StringComparison.Ordinal);
    }
}

internal static class RunConfigurationResolution
{
    static ResolvedRunConfiguration? CurrentConfiguration;
    internal static BiomeLayoutConfigSnapshot? CurrentBiomes => CurrentConfiguration?.Biomes;

    internal static ResolvedRunConfiguration RequireCurrent() => CurrentConfiguration
        ?? throw new InvalidOperationException("The run settings are unavailable.");

    internal static void AdoptForPublication(ResolvedRunConfiguration Configuration)
    {
        CurrentConfiguration = Configuration ?? throw new ArgumentNullException(nameof(Configuration));
        FinalManifestConsensus.Reset();
    }

    internal static IEnumerator WaitForStartupAuthority(bool OnlineIntent)
    {
        CurrentConfiguration = null;
        if (!OnlineIntent && PhotonNetwork.OfflineMode) yield break;

        var EnteredGameServer = false;
        var EnteredPhotonConnection = false;
        while (true)
        {
            var State = PhotonNetwork.NetworkClientState;
            var Room = PhotonNetwork.CurrentRoom;
            if (OnlineIntent)
            {
                if (!IslandLoadPublication.IsOnlineStartupState(NetworkConnector.CurrentConnectionState))
                    throw PhotonRoomRejection.Disconnect(
                        "PEAK stopped connecting to the room. Run startup stopped.");
                if (State is not ClientState.Disconnected and not ClientState.PeerCreated)
                    EnteredPhotonConnection = true;
                if (State == ClientState.Joined
                    && (PhotonNetwork.OfflineMode || Room is { IsOffline: true }))
                    throw PhotonRoomRejection.Disconnect(
                        "An online run cannot use an offline room.");
                if (State == ClientState.Disconnecting
                    || State == ClientState.Disconnected && EnteredPhotonConnection)
                    throw PhotonRoomRejection.Disconnect(
                        "Photon disconnected during run startup.");
                if (State is ClientState.Disconnected or ClientState.PeerCreated)
                {
                    yield return null;
                    continue;
                }
            }
            else if (PhotonNetwork.OfflineMode) yield break;

            if (State is ClientState.ConnectingToGameServer
                or ClientState.ConnectedToGameServer or ClientState.Joining or ClientState.Joined)
                EnteredGameServer = true;
            if (State == ClientState.Joined && Room is not null
                && PhotonNetwork.IsMessageQueueRunning
                && Room.CustomProperties.ContainsKey(RunConfigurationCodec.PropertyKey)) yield break;
            if (State is ClientState.Disconnecting or ClientState.Disconnected)
                throw new InvalidOperationException(
                    "Photon disconnected before run startup finished.");
            if (State == ClientState.Joined && Room is null)
                throw PhotonRoomRejection.Disconnect(
                    "Photon reported a joined connection without a room.");
            if (EnteredGameServer && State is (ClientState.DisconnectingFromGameServer
                or ClientState.ConnectingToMasterServer or ClientState.ConnectedToMasterServer))
                throw PhotonRoomRejection.Disconnect(
                    "Photon stopped joining the room. Run startup stopped.");
            yield return null;
        }
    }

    internal static ResolvedRunConfiguration ResolveStartup()
    {
        CurrentConfiguration = null;
        FinalManifestConsensus.Reset();
        var Room = PhotonNetwork.CurrentRoom;
        if (PhotonNetwork.OfflineMode)
        {
            CapturedLayoutSelection? RestoredSelection = null;
            if (Peak.Quicksave.ShouldUseSaveData)
            {
                if (!MapQuicksave.TryCaptureQuicksaveLayoutSelection(out var Selection, out var SelectionFailure))
                    throw MapQuicksave.RejectQuicksave(SelectionFailure);
                RestoredSelection = Selection;
            }
            var Configuration = RunConfigurationCodec.CaptureLocal(RestoredSelection);
            CurrentConfiguration = Configuration;
            Complete(Configuration);
            return Configuration;
        }
        if (Room is null || PhotonNetwork.NetworkClientState != ClientState.Joined)
            throw PhotonRoomRejection.Disconnect(
                "The online run has no joined Photon room.");
        if (!Room.CustomProperties.TryGetValue(RunConfigurationCodec.PropertyKey, out var Raw)
            || Raw is not string Data)
            throw PhotonRoomRejection.Disconnect(
                "The host did not send PLL room settings.");
        if (!RunConfigurationCodec.TryParse(Data, out var Resolved, out var Failure))
            throw PhotonRoomRejection.Disconnect(Failure);
        CurrentConfiguration = Resolved;
        Complete(Resolved);
        return Resolved;
    }

    static void Complete(ResolvedRunConfiguration Configuration)
    {
        TerminalLayoutSelection.CompleteFlightCapture();
        AscentUIStartPatch.RefreshResolved(Configuration);
    }
}
