using System;
using System.IO;

namespace PLL.Library;

internal static class ZoneVolumeContract
{
    internal static void Validate(PllZoneVolume Volume, BiomeId Biome, string Owner, bool Argument)
    {
        if (PllAuthoringValueContract.ZoneVolumeFailure(Volume) is not { } Failure) return;
        var Message = $"{Failure.Message} Biome '{Biome}', zone owner '{Owner}', shape {Volume.Shape}.";
        if (Argument) throw new ArgumentException(Message, "volume");
        throw new InvalidOperationException(Message);
    }

    internal static void WriteManifest(BinaryWriter Writer, PllZoneVolume Volume)
    {
        Writer.Write((byte)Volume.Shape);
        ManifestEncoding.WriteVector(Writer, Volume.Center);
        ManifestEncoding.WriteVector(Writer, Volume.Size);
        ManifestEncoding.WriteScalar(Writer, Volume.Radius);
    }
}
