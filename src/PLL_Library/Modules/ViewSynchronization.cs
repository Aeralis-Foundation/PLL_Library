using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

namespace PLL.Library;

internal sealed class MapNetworkSession
{
    readonly MapRun Run;

    internal MapNetworkSession(MapRun Run)
    {
        this.Run = Run ?? throw new ArgumentNullException(nameof(Run));
        RunProperties = new RunPropertyPublication(Run);
        GenerationBarrier = new InitialGenerationBarrier(Run);
        ViewBatches = new ViewBatchSynchronization(Run);
        DeferredOperations = new DeferredMapNetworkOperations(Run);
    }

    internal RunPropertyPublication RunProperties { get; }
    internal InitialGenerationBarrier GenerationBarrier { get; }
    internal ViewBatchSynchronization ViewBatches { get; }
    internal DeferredMapNetworkOperations DeferredOperations { get; }
    internal MapNetworkBridge? Bridge { get; private set; }
    internal bool OwnsCurrentRun => ReferenceEquals(Map.CurrentRun, Run) && Run.HasPublishedLayout;

    internal bool OwnsCallback(long Identity, string GenerationEpoch, int LayoutRevision) =>
        OwnsCurrentRun && Run.Matches(Identity, GenerationEpoch, LayoutRevision);

    internal void BindBridge(MapNetworkBridge Bridge)
    {
        if (!OwnsCurrentRun)
            throw new InvalidOperationException("A network bridge requires the current published map run.");
        if (this.Bridge)
            throw new InvalidOperationException("A map run already owns a network bridge.");
        Bridge.Bind(this);
        this.Bridge = Bridge;
    }

    internal void ReleaseBridge()
    {
        var ReleasedBridge = Bridge;
        Bridge = null;
        if (ReleasedBridge) UnityEngine.Object.DestroyImmediate(ReleasedBridge);
    }

    internal void DetachBridge(MapNetworkBridge Bridge)
    {
        if (ReferenceEquals(this.Bridge, Bridge)) this.Bridge = null;
    }

    internal void ReleaseState()
    {
        List<Exception>? Failures = null;
        CleanupOperations.Capture(ref Failures, ReleaseBridge);
        CleanupOperations.Capture(ref Failures, ViewBatches.ReleaseState);
        CleanupOperations.Capture(ref Failures, RunProperties.CancelAll);
        CleanupOperations.Capture(ref Failures, GenerationBarrier.ReleaseState);
        CleanupOperations.Capture(ref Failures, DeferredOperations.Clear);
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null)
            throw new AggregateException("Map network state release failed.", Failures);
    }
}

internal sealed class MapNetworkBridge : MonoBehaviourPunCallbacks, IOnEventCallback
{
    MapNetworkSession? Session;

    internal void Bind(MapNetworkSession Owner)
    {
        if (Session is not null)
            throw new InvalidOperationException("A network bridge cannot bind more than once.");
        Session = Owner ?? throw new ArgumentNullException(nameof(Owner));
    }

    public override void OnRoomPropertiesUpdate(PhotonHashtable PropertiesThatChanged) =>
        Session?.DeferredOperations.RoomPropertiesUpdated(PropertiesThatChanged);

    public void OnEvent(EventData PhotonEvent) => Session?.DeferredOperations.NetworkEvent(PhotonEvent);

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player NewPlayer) =>
        Session?.DeferredOperations.PlayerEnteredRoom();

    public override void OnMasterClientSwitched(Photon.Realtime.Player NewMasterClient) =>
        Session?.DeferredOperations.MasterClientSwitched();

    void OnDestroy()
    {
        var Owner = Session;
        Session = null;
        Owner?.DetachBridge(this);
    }
}
