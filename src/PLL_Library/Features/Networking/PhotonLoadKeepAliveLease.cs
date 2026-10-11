using System;
using Photon.Pun;

namespace PLL.Library;

internal sealed class PhotonLoadKeepAliveLease : IDisposable
{
    static Retention? Active;
    Retention? Owner;

    PhotonLoadKeepAliveLease(Retention Owner)
    {
        this.Owner = Owner;
        Owner.Leases++;
    }

    internal static PhotonLoadKeepAliveLease? Acquire()
    {
        if (PhotonNetwork.OfflineMode) return null;
        var Handler = PhotonHandler.Instance;
        if (Active is null || Active.Handler != Handler) Active = new Retention(Handler);
        return new PhotonLoadKeepAliveLease(Active);
    }

    public void Dispose()
    {
        var Retained = Owner;
        Owner = null;
        if (Retained is null || --Retained.Leases != 0) return;
        Retained.Handler.KeepAliveInBackground = Retained.PreviousKeepAliveInBackground;
        if (ReferenceEquals(Active, Retained)) Active = null;
    }

    sealed class Retention
    {
        internal readonly PhotonHandler Handler;
        internal readonly int PreviousKeepAliveInBackground;
        internal int Leases;

        internal Retention(PhotonHandler Handler)
        {
            this.Handler = Handler;
            PreviousKeepAliveInBackground = Handler.KeepAliveInBackground;
            Handler.KeepAliveInBackground = int.MaxValue;
        }
    }
}
