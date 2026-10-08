using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace PLL.Library
{
    internal enum NativeContentRequestKind { Export, Preview }
    internal enum NativeContentPickKind { ForceName, Item, Resolver, Visual, Actor }

    [DataContract]
    internal sealed class NativeContentIdentity
    {
        [DataMember(Order = 0, IsRequired = true)] public string RuntimeSha256 = "";
        [DataMember(Order = 1, IsRequired = true)] public string RuntimeMvid = "";
        [DataMember(Order = 2, IsRequired = true)] public string PeakSha256 = "";
        [DataMember(Order = 3, IsRequired = true)] public string UnityVersion = "";
        [DataMember(Order = 4, IsRequired = true)] public string BundleFile = "";
        [DataMember(Order = 5, IsRequired = true)] public string BundleSha256 = "";
    }

    [DataContract]
    internal sealed class NativeContentRequest
    {
        [DataMember(Order = 0, IsRequired = true)] public string Schema = "";
        [DataMember(Order = 1, IsRequired = true)] public string Id = "";
        [DataMember(Order = 2, IsRequired = true)] public NativeContentRequestKind Kind = (NativeContentRequestKind)(-1);
        [DataMember(Order = 3, IsRequired = true)] public NativeContentIdentity Identity = new NativeContentIdentity();
        [DataMember(Order = 4, IsRequired = true)] public long ExpiresUtcTicks = 0;
        [DataMember(Order = 5, IsRequired = true)] public string Configuration = "";
        [DataMember(Order = 6, IsRequired = true)] public string SeedText = "";
        [DataMember(Order = 7, IsRequired = true)] public int SlotIndex = -1;
        [DataMember(Order = 8, IsRequired = true)] public int OccurrenceRequestIndex = -1;
        [DataMember(Order = 9, IsRequired = true)] public string SourceId = "";
        [DataMember(Order = 10, IsRequired = true)] public string NativeRunSettingsSha256 = "";
        [DataMember(Order = 11, IsRequired = true)] public int Ascent = -1;
    }

    [DataContract]
    internal sealed class NativeContentCatalog
    {
        [DataMember(Order = 0, IsRequired = true)] public string Schema = "";
        [DataMember(Order = 1, IsRequired = true)] public string RequestId = "";
        [DataMember(Order = 2, IsRequired = true)] public NativeContentIdentity Identity = new NativeContentIdentity();
        [DataMember(Order = 3, IsRequired = true)] public string Configuration = "";
        [DataMember(Order = 4, IsRequired = true)] public int ResolvedSeed;
        [DataMember(Order = 5, IsRequired = true)] public string NativeRunSettingsSha256 = "";
        [DataMember(Order = 6, IsRequired = true)] public int Ascent;
        [DataMember(Order = 7, IsRequired = true)] public NativeContentEntry[] Entries = Array.Empty<NativeContentEntry>();
        [DataMember(Order = 8, IsRequired = true)] public NativeContentRig[] Rigs = Array.Empty<NativeContentRig>();
        [DataMember(Order = 9, IsRequired = true)] public NativeContentOccurrence[] Occurrences = Array.Empty<NativeContentOccurrence>();
    }

    [DataContract]
    internal sealed class NativeContentOccurrence
    {
        [DataMember(Order = 0, IsRequired = true)] public int SlotIndex = -1;
        [DataMember(Order = 1, IsRequired = true)] public int OccurrenceRequestIndex = -1;
        [DataMember(Order = 2, IsRequired = true)] public string SourceId = "";
    }

    [DataContract]
    internal sealed class NativeContentEntry
    {
        [DataMember(Order = 0, IsRequired = true)] public NativeContentPickKind Kind = (NativeContentPickKind)(-1);
        [DataMember(Order = 1, IsRequired = true)] public string Id = "";
        [DataMember(Order = 2, IsRequired = true)] public string Name = "";
        [DataMember(Order = 3, IsRequired = true)] public string ComponentType = "";
        [DataMember(Order = 4, IsRequired = true)] public string ComponentAssembly = "";
        [DataMember(Order = 5, IsRequired = true)] public int ItemId = -1;
        [DataMember(Order = 6, IsRequired = true)] public int CandidateCount;
        [DataMember(Order = 7, IsRequired = true)] public string SelectionFailure = "";
        [DataMember(Order = 8, IsRequired = true)] public string Fingerprint = "";
        [DataMember(Order = 9, IsRequired = true)] public string ImageFile = "";
        [DataMember(Order = 10, IsRequired = true)] public string ImageSha256 = "";
        [DataMember(Order = 11, IsRequired = true)] public string ImageAbsence = "No isolated render has completed.";
        [DataMember(Order = 12, IsRequired = true)] public string PoseProvenance = "";
        [DataMember(Order = 13, IsRequired = true)] public string LightingProvenance = "";
    }

    [DataContract]
    internal sealed class NativeContentRig
    {
        [DataMember(Order = 0, IsRequired = true)] public string Actor = "";
        [DataMember(Order = 1, IsRequired = true)] public string Fingerprint = "";
        [DataMember(Order = 2, IsRequired = true)] public NativeContentSkin[] Skins = Array.Empty<NativeContentSkin>();
        [DataMember(Order = 3, IsRequired = true)] public NativeContentTransform[] Transforms = Array.Empty<NativeContentTransform>();
    }

    [DataContract]
    internal sealed class NativeContentRigAsset
    {
        [DataMember(Order = 0, IsRequired = true)] public string Schema = "";
        [DataMember(Order = 1, IsRequired = true)] public string PeakSha256 = "";
        [DataMember(Order = 2, IsRequired = true)] public string SourceFile = "";
        [DataMember(Order = 3, IsRequired = true)] public string SourceSha256 = "";
        [DataMember(Order = 4, IsRequired = true)] public string SourceAsset = "";
        [DataMember(Order = 5, IsRequired = true)] public long SourcePathId = 0;
        [DataMember(Order = 6, IsRequired = true)] public string UnityVersion = "";
        [DataMember(Order = 7, IsRequired = true)] public NativeContentRig Rig = new NativeContentRig();
    }

    [DataContract]
    internal sealed class NativeContentSkin
    {
        [DataMember(Order = 0, IsRequired = true)] public string RendererPath = "";
        [DataMember(Order = 1, IsRequired = true)] public string[] BonePaths = Array.Empty<string>();
        [DataMember(Order = 2, IsRequired = true)] public float[] Bindposes = Array.Empty<float>();
        [DataMember(Order = 3, IsRequired = true)] public string RootBonePath = "";
        [DataMember(Order = 4, IsRequired = true)] public int BlendShapeCount;
    }

    [DataContract]
    internal sealed class NativeContentTransform
    {
        [DataMember(Order = 0, IsRequired = true)] public string Path = "";
        [DataMember(Order = 1, IsRequired = true)] public float[] Position = Array.Empty<float>();
        [DataMember(Order = 2, IsRequired = true)] public float[] Rotation = Array.Empty<float>();
        [DataMember(Order = 3, IsRequired = true)] public float[] Scale = Array.Empty<float>();
    }

    [DataContract]
    internal sealed class NativeContentResult
    {
        [DataMember(Order = 0, IsRequired = true)] public string Schema = "";
        [DataMember(Order = 1, IsRequired = true)] public string RequestId = "";
        [DataMember(Order = 2, IsRequired = true)] public NativeContentIdentity Identity = new NativeContentIdentity();
        [DataMember(Order = 3, IsRequired = true)] public string Outcome = "";
        [DataMember(Order = 4, IsRequired = true)] public string Failure = "";
        [DataMember(Order = 5, IsRequired = true)] public string Configuration = "";
        [DataMember(Order = 6, IsRequired = true)] public string SeedText = "";
        [DataMember(Order = 7, IsRequired = true)] public int ResolvedSeed;
        [DataMember(Order = 8, IsRequired = true)] public int SlotIndex;
        [DataMember(Order = 9, IsRequired = true)] public int OccurrenceRequestIndex;
        [DataMember(Order = 10, IsRequired = true)] public string SourceId = "";
        [DataMember(Order = 11, IsRequired = true)] public string NativeRunSettingsSha256 = "";
        [DataMember(Order = 12, IsRequired = true)] public int Ascent;
        [DataMember(Order = 13, IsRequired = true)] public string ImageFile = "";
        [DataMember(Order = 14, IsRequired = true)] public string ImageSha256 = "";
        [DataMember(Order = 15, IsRequired = true)] public int RenderedFrame;
        [DataMember(Order = 16, IsRequired = true)] public float[] CameraWorldPose = Array.Empty<float>();
        [DataMember(Order = 17, IsRequired = true)] public float[] CameraProjection = Array.Empty<float>();
        [DataMember(Order = 18, IsRequired = true)] public int ImageWidth;
        [DataMember(Order = 19, IsRequired = true)] public int ImageHeight;
    }

    internal static class NativeContentArtifact
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        internal const string RequestSchema = "PLL/native-content-request/v1";
        internal const string CatalogSchema = "PLL/native-content-catalog/v1";
        internal const string ResultSchema = "PLL/native-content-result/v1";
        internal const string RigAssetSchema = "PLL/NativeRigAsset/v1";
        internal const string DirectoryName = "PLL_ContentTools";
        internal const string RequestFile = "request.json";
        internal const string CatalogFile = "catalog.json";
        internal const string ResultFile = "result.json";
        internal const string PreviewImageFile = "preview.png";
        internal const int MaximumRequestBytes = 128 * 1024;
        internal const int MaximumCatalogBytes = 16 * 1024 * 1024;
        internal const int MaximumRigAssetBytes = 1024 * 1024;

        internal static NativeContentRigAsset ReadRigAsset(string Path)
        {
            using (var Stream = File.OpenRead(Path))
            {
                if (Stream.Length > MaximumRigAssetBytes)
                    throw new InvalidOperationException("The offline rig asset exceeds 1 MiB.");
                using (var Reader = new StreamReader(Stream, StrictUtf8, false))
                {
                    var Asset = Parse<NativeContentRigAsset>(Reader.ReadToEnd());
                    if (Asset == null || Asset.Schema != RigAssetSchema
                        || !Hash(Asset.PeakSha256) || !Hash(Asset.SourceSha256)
                        || Asset.SourceFile != "data.unity3d" || Asset.SourceAsset != "resources.assets"
                        || Asset.SourcePathId <= 0 || string.IsNullOrEmpty(Asset.UnityVersion))
                        throw new InvalidOperationException("The offline rig asset has incomplete PEAK or source provenance.");
                    RequireRig(Asset.Rig);
                    if (Asset.Rig.Actor != "RopeDynamic")
                        throw new InvalidOperationException("The offline rig asset does not describe RopeDynamic.");
                    return Asset;
                }
            }
        }

        internal static string Serialize(object Value)
        {
            using (var Stream = new MemoryStream())
            {
                new DataContractJsonSerializer(Value.GetType()).WriteObject(Stream, Value);
                return StrictUtf8.GetString(Stream.GetBuffer(), 0, checked((int)Stream.Length));
            }
        }

        internal static T Parse<T>(string Json)
        {
            using (var Stream = new MemoryStream(StrictUtf8.GetBytes(Json), false))
                try
                {
                    return (T)(new DataContractJsonSerializer(typeof(T)).ReadObject(Stream)
                        ?? throw new InvalidOperationException("Native content JSON must contain an object."));
                }
                catch (Exception Failure) when (Failure is SerializationException || Failure is XmlException)
                {
                    throw new InvalidOperationException("Native content JSON is malformed or missing required fields.", Failure);
                }
        }

        internal static void RequireRequest(NativeContentRequest Request)
        {
            if (Request == null || Request.Schema != RequestSchema
                || !Guid.TryParseExact(Request.Id, "N", out _)
                || !Enum.IsDefined(typeof(NativeContentRequestKind), Request.Kind)
                || Request.ExpiresUtcTicks <= DateTime.UtcNow.Ticks)
                throw new InvalidOperationException("The native content request is malformed or expired.");
            RequireIdentity(Request.Identity);
            if (Request.Kind == NativeContentRequestKind.Preview
                && (string.IsNullOrEmpty(Request.Configuration)
                    || string.IsNullOrEmpty(Request.SeedText) || Request.SlotIndex < 0
                    || Request.OccurrenceRequestIndex < 0
                    || string.IsNullOrEmpty(Request.SourceId)
                    || !Hash(Request.NativeRunSettingsSha256)))
                throw new InvalidOperationException("A preview requires a run snapshot, seed text, occurrence and native settings identity.");
        }

        internal static void RequireCatalog(NativeContentCatalog Catalog)
        {
            if (Catalog == null || Catalog.Schema != CatalogSchema
                || !Guid.TryParseExact(Catalog.RequestId, "N", out _)
                || Catalog.Entries == null || Catalog.Rigs == null || Catalog.Occurrences == null
                || string.IsNullOrEmpty(Catalog.Configuration)
                || !Hash(Catalog.NativeRunSettingsSha256))
                throw new InvalidOperationException("The native content catalog is malformed.");
            RequireIdentity(Catalog.Identity);
            var Slots = new HashSet<int>();
            foreach (var Occurrence in Catalog.Occurrences)
                if (Occurrence == null || Occurrence.SlotIndex < 0 || Occurrence.OccurrenceRequestIndex < 0
                    || !Slots.Add(Occurrence.SlotIndex) || string.IsNullOrEmpty(Occurrence.SourceId))
                    throw new InvalidOperationException("The native catalog has an invalid occurrence identity.");
            var Ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Entry in Catalog.Entries)
            {
                if (Entry == null || !Enum.IsDefined(typeof(NativeContentPickKind), Entry.Kind)
                    || string.IsNullOrEmpty(Entry.Id) || !Ids.Add(Entry.Kind + ":" + Entry.Id)
                    || string.IsNullOrEmpty(Entry.Name) || Entry.CandidateCount < 0
                    || Entry.Kind == NativeContentPickKind.Item
                        && (Entry.ItemId < 0 || Entry.ItemId > ushort.MaxValue
                            || string.IsNullOrEmpty(Entry.ComponentType)
                            || string.IsNullOrEmpty(Entry.ComponentAssembly))
                    || Entry.ImageAbsence == null || Entry.SelectionFailure == null)
                    throw new InvalidOperationException("The native content catalog has an invalid selector.");
                RequireImage(Entry.ImageFile, Entry.ImageSha256, Entry.ImageAbsence);
                if (Entry.ImageFile != "" && (!Hash(Entry.Fingerprint)
                    || string.IsNullOrEmpty(Entry.PoseProvenance) || string.IsNullOrEmpty(Entry.LightingProvenance)))
                    throw new InvalidOperationException("A native thumbnail requires captured source, pose and lighting provenance.");
            }
            var Actors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Rig in Catalog.Rigs)
            {
                if (Rig == null || !Actors.Add(Rig.Actor))
                    throw new InvalidOperationException("Native rig actors must be distinct.");
                RequireRig(Rig);
            }
        }

        internal static void RequireRig(NativeContentRig Rig)
        {
            if (Rig == null || string.IsNullOrEmpty(Rig.Actor)
                || !Hash(Rig.Fingerprint) || Rig.Skins == null || Rig.Transforms == null)
                throw new InvalidOperationException("The native rig descriptor is malformed.");
            var Transforms = new HashSet<string>(StringComparer.Ordinal) { "." };
            foreach (var Transform in Rig.Transforms)
            {
                if (Transform == null || !Path(Transform.Path) || !Transforms.Add(Transform.Path))
                    throw new InvalidOperationException("Native rig transform paths must be distinct.");
                RequireFinite(Transform.Position, 3); RequireFinite(Transform.Rotation, 4);
                RequireFinite(Transform.Scale, 3);
                if (Transform.Rotation[0] == 0 && Transform.Rotation[1] == 0
                    && Transform.Rotation[2] == 0 && Transform.Rotation[3] == 0)
                    throw new InvalidOperationException("A native rig rotation cannot be zero.");
            }
            foreach (var Transform in Rig.Transforms)
            {
                var Separator = Transform.Path.LastIndexOf('/');
                if (Separator >= 0 && !Transforms.Contains(Transform.Path.Substring(0, Separator)))
                    throw new InvalidOperationException("A native rig transform is missing its parent pose.");
            }
            var Renderers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Skin in Rig.Skins)
            {
                if (Skin == null || !Path(Skin.RendererPath) || !Renderers.Add(Skin.RendererPath)
                    || Skin.BonePaths == null || Skin.BonePaths.Length == 0
                    || Skin.Bindposes == null || Skin.Bindposes.Length != Skin.BonePaths.Length * 16
                    || Skin.BlendShapeCount < 0 || !Transforms.Contains(Skin.RendererPath)
                    || Skin.RootBonePath != "" && !Transforms.Contains(Skin.RootBonePath))
                    throw new InvalidOperationException("The native rig has an invalid skin binding.");
                var Bones = new HashSet<string>(StringComparer.Ordinal);
                foreach (var Bone in Skin.BonePaths)
                    if (!Path(Bone) || !Bones.Add(Bone) || !Transforms.Contains(Bone))
                        throw new InvalidOperationException("Native rig bone paths must be distinct.");
                RequireFinite(Skin.Bindposes, Skin.Bindposes.Length);
            }
        }

        internal static void RequireIdentity(NativeContentIdentity Identity)
        {
            if (Identity == null || !Hash(Identity.RuntimeSha256) || !Hash(Identity.PeakSha256)
                || !Hash(Identity.BundleSha256) || !Guid.TryParseExact(Identity.RuntimeMvid, "D", out _)
                || string.IsNullOrEmpty(Identity.UnityVersion) || !Leaf(Identity.BundleFile)
                || !Identity.BundleFile.EndsWith(".pll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The native content artifact has incomplete runtime or bundle identities.");
        }

        internal static void RequireResult(NativeContentResult Result)
        {
            if (Result == null || Result.Schema != ResultSchema
                || !Guid.TryParseExact(Result.RequestId, "N", out _)
                || Result.Outcome != "Exported" && Result.Outcome != "Rendered"
                    && Result.Outcome != "Cancelled" && Result.Outcome != "Rejected"
                || Result.Failure == null)
                throw new InvalidOperationException("The native content result is malformed.");
            RequireIdentity(Result.Identity);
            if (Result.Outcome == "Rendered")
            {
                RequireImage(Result.ImageFile, Result.ImageSha256, "");
                if (Result.SlotIndex < 0 || Result.OccurrenceRequestIndex < 0
                    || string.IsNullOrEmpty(Result.SourceId) || string.IsNullOrEmpty(Result.SeedText)
                    || string.IsNullOrEmpty(Result.Configuration) || !Hash(Result.NativeRunSettingsSha256)
                    || Result.ImageWidth < 1 || Result.ImageHeight < 1)
                    throw new InvalidOperationException("A rendered preview requires its actual run, occurrence and pixel identities.");
                RequireFinite(Result.CameraWorldPose, 16); RequireFinite(Result.CameraProjection, 16);
            }
        }

        internal static void RequireImage(string File, string Sha256, string Absence)
        {
            if (File == "" ? Sha256 != "" || string.IsNullOrEmpty(Absence)
                : !Leaf(File) || !File.EndsWith(".png", StringComparison.Ordinal)
                    || !Hash(Sha256) || Absence != "")
                throw new InvalidOperationException("A native image requires completed pixels or an explicit absence.");
        }

        internal static bool Hash(string Value)
        {
            if (Value == null || Value.Length != 64) return false;
            foreach (var Character in Value)
                if (!(Character >= '0' && Character <= '9'
                    || Character >= 'A' && Character <= 'F')) return false;
            return true;
        }

        internal static string Hex(ReadOnlySpan<byte> Bytes)
        {
            const string Digits = "0123456789ABCDEF";
            var Characters = new char[Bytes.Length * 2];
            for (var i = 0; i < Bytes.Length; i++)
            {
                Characters[i * 2] = Digits[Bytes[i] >> 4];
                Characters[i * 2 + 1] = Digits[Bytes[i] & 15];
            }
            return new string(Characters);
        }

        internal static bool Leaf(string Value) => !string.IsNullOrEmpty(Value)
            && Value != "." && Value != ".." && Value.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;

        internal static bool Path(string Value)
        {
            if (Value == ".") return true;
            if (string.IsNullOrEmpty(Value)) return false;
            foreach (var Part in Value.Split('/'))
                if (Part.Length == 0 || Part == "." || Part == "..") return false;
            return true;
        }

        static void RequireFinite(float[] Values, int Count)
        {
            if (Values == null || Values.Length != Count)
                throw new InvalidOperationException("Native rig poses and bindposes have an invalid size.");
            foreach (var Value in Values)
                if (float.IsNaN(Value) || float.IsInfinity(Value))
                    throw new InvalidOperationException("Native rig poses and bindposes must be finite.");
        }
    }
}
