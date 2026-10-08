using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal static class PhotonCloneIdentity
{
    internal static bool IsBatchOrigin(PhotonView View) =>
        View.sceneViewId == 0 && View.InstantiationId == 0;

    internal static void Reset(GameObject Root)
    {
        foreach (var View in Root.GetComponentsInChildren<PhotonView>(true))
        {
            View.sceneViewId = 0;
            View.InstantiationId = 0;
            View.viewIdField = 0;
            View.removedFromLocalViewList = false;
        }
    }

    internal static void DetachLocalView(PhotonView View)
    {
        View.viewIdField = 0;
        View.removedFromLocalViewList = true;
    }
}
