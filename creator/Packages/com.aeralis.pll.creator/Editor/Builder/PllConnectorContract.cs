using System;
using UnityEngine;

namespace PLL.Library
{
    internal static class PllConnectorContract
    {
        internal static bool Compatible(
            string ExitConnector, Quaternion ExitRotation,
            string EntryConnector, Quaternion EntryRotation) =>
            string.Equals(
                ExitConnector, EntryConnector,
                StringComparison.Ordinal)
            && Math.Abs(Quaternion.Dot(
                ExitRotation.normalized,
                EntryRotation.normalized)) >= 0.999999f;

        internal static void Require(
            string GroupId, string PreviousId,
            string ExitConnector, Quaternion ExitRotation,
            string NextId, string EntryConnector,
            Quaternion EntryRotation)
        {
            if (Compatible(
                    ExitConnector, ExitRotation,
                    EntryConnector, EntryRotation)) return;
            throw new InvalidOperationException(
                "Biome group '" + GroupId
                + "' has no compatible internal connector from '"
                + PreviousId + "' to '" + NextId + "'.");
        }
    }
}
