using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal sealed class GeneratedNetworkContract
{
    const int PreviewCharacterBudget = 1 << 16;
    const string HashDomain = "PLL/GeneratedNetworkContract/v1";
    const byte OccurrenceRecord = 1;
    const byte VariationRecord = 2;
    const byte FinalForceRecord = 3;
    const byte ViewRecord = 4;
    const byte RpcRecord = 5;
    const byte FakeItemRecord = 6;
    internal const byte NativeActorSourceRecord = 7;
    const byte NativeFlowRecord = 8;
    static readonly Dictionary<Type, RpcSignatureSet> RpcSignaturesByComponentType = new();
    static readonly object RpcSignatureCacheLock = new();

    GeneratedNetworkContract(byte[] Hash, int RecordCount, int TotalCharacters, string Preview)
    {
        this.Hash = Hash;
        this.RecordCount = RecordCount;
        this.TotalCharacters = TotalCharacters;
        this.Preview = Preview;
    }

    internal byte[] Hash { get; }
    internal int RecordCount { get; }
    internal int TotalCharacters { get; }
    internal string Preview { get; }

    internal static GeneratedNetworkContract Create(
        MapSlot[] Slots,
        int Index,
        bool IncludeMap,
        IReadOnlyList<(PhotonView View, Transform Root, string Path)> Views,
        IReadOnlyList<(FakeItem Item, string Path)> FakeItems,
        NativeSpawnAdmission NativeActors, Transform? NativeSourceRoot = null)
    {
        using var Writer = new ContractWriter();
        WriteSelections(Writer, Slots, Index, IncludeMap, NativeActors, NativeSourceRoot);
        foreach (var Entry in Views) WriteView(Writer, Entry.View, Entry.Root, Entry.Path);
        foreach (var Entry in FakeItems) WriteFakeItem(Writer, Entry.Item, Entry.Path);
        return Writer.Complete();
    }

    internal static GeneratedNetworkContract CreatePrefab(GameObject Root, IReadOnlyList<PhotonView> Views)
    {
        using var Writer = new ContractWriter();
        foreach (var View in Views)
            WriteView(Writer, View, Root.transform,
                DeterministicObjectIdentity.RelativeTypedComponentStructuralPath(View, Root.transform));
        return Writer.Complete();
    }

    static void WriteSelections(ContractWriter Writer, MapSlot[] Slots, int Index, bool IncludeMap,
        NativeSpawnAdmission NativeActors, Transform? NativeSourceRoot)
    {
        var First = IncludeMap ? 0 : Index;
        var Last = IncludeMap ? Slots.Length : Index + 1;
        for (var i = First; i < Last; i++)
        {
            var Occurrence = Slots[i].Occurrence;
            if (i > First && ReferenceEquals(Slots[i - 1].Occurrence, Occurrence)) continue;
            var OccurrenceSlots = Occurrence.Slots;
            Writer.StartRecord(OccurrenceRecord, "occurrence", OccurrenceSlots[0].Index.ToString(CultureInfo.InvariantCulture));
            Writer.Write(OccurrenceSlots[0].Index);
            Writer.Write(OccurrenceSlots[OccurrenceSlots.Count - 1].Index);
            Writer.Write(Occurrence.Variations.Count);
            NativeActors.WriteSourceNetworkContract(Writer, Occurrence, NativeSourceRoot);
            if (NativeSourceRoot is null && Occurrence.GeometryBudget.SourceCount != 0)
            {
                Writer.StartRecord(NativeFlowRecord, "native-flow", Occurrence.RequestIndex.ToString(CultureInfo.InvariantCulture));
                Writer.Write(Occurrence.GeometryBudget.FreezeRealizedOutput(Occurrence.Container));
            }
            for (var j = 0; j < Occurrence.Variations.Count; j++)
            {
                var Selection = Occurrence.Variations[j];
                Writer.StartRecord(VariationRecord, "variation", Selection.RootPath);
                Writer.Write(j);
                Writer.Write(Selection.Id);
                Writer.Write(Selection.RootPath);
            }
            for (var j = 0; j < OccurrenceSlots.Count; j++)
            {
                var Custom = OccurrenceSlots[j].CustomResolved;
                if (Custom is null) continue;
                Writer.StartRecord(FinalForceRecord, "final-force", OccurrenceSlots[j].Index.ToString(CultureInfo.InvariantCulture));
                Custom.FinalForce.WriteNetworkIdentity(Writer);
            }
        }
    }

    static void WriteView(
        ContractWriter Writer,
        PhotonView View,
        Transform Root,
        string Path)
    {
        if (!View || !Root)
            throw new InvalidOperationException("A synchronized PhotonView contract lost its batch root.");
        Writer.StartRecord(ViewRecord, "view", Path);
        Writer.Write(View.Group);
        Writer.Write(View.Prefix);
        Writer.Write((int)View.Synchronization);
        Writer.Write((int)View.OwnershipTransfer);
        Writer.Write((int)View.observableSearch);
        var Observed = View.ObservedComponents;
        Writer.Write(Observed is not null);
        if (Observed is not null)
        {
            Writer.Write(Observed.Count);
            for (var i = 0; i < Observed.Count; i++)
            {
                var Component = Observed[i];
                Writer.Write((bool)Component);
                if (!Component) continue;
                string ComponentPath;
                try
                {
                    ComponentPath = DeterministicObjectIdentity.RelativeTypedComponentStructuralPath(Component, Root);
                }
                catch (InvalidOperationException Exception)
                {
                    throw new InvalidOperationException(
                        $"PhotonView '{Path}' observed component {i} is outside its synchronized batch root.", Exception);
                }
                Writer.Write(ComponentPath);
                Writer.Write(DeterministicObjectIdentity.StableTypeName(Component.GetType()));
            }
        }
        var ItemOwner = FindItemOwner(View.transform);
        Writer.Write((bool)ItemOwner);
        if (ItemOwner) Writer.Write(ItemOwner.itemID);
        WriteRpcComponents(Writer, View, Path);
    }

    static void WriteRpcComponents(ContractWriter Writer, PhotonView View, string ViewPath)
    {
        var Components = View.GetComponents<MonoBehaviour>();
        for (var i = 0; i < Components.Length; i++)
        {
            var Component = Components[i];
            if (!Component) continue;
            var Signatures = GetRpcSignatures(Component.GetType());
            if (Signatures.Count == 0) continue;
            Writer.StartRecord(RpcRecord, "rpc", ViewPath);
            Writer.Write(Signatures.ComponentTypeName);
            Signatures.Write(Writer);
        }
    }

    static RpcSignatureSet GetRpcSignatures(Type Type)
    {
        lock (RpcSignatureCacheLock)
        {
            if (RpcSignaturesByComponentType.TryGetValue(Type, out var Existing)) return Existing;
            var Signatures = CreateRpcSignatures(
                Type, Type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            RpcSignaturesByComponentType.Add(Type, Signatures);
            return Signatures;
        }
    }

    static RpcSignatureSet CreateRpcSignatures(Type Type, MethodInfo[] Methods)
    {
        var Signatures = new List<RpcSignature>();
        foreach (var Method in Methods)
            if (Method.IsDefined(typeof(PunRPC), false)) Signatures.Add(new RpcSignature(Method));
        Signatures.Sort(RpcSignature.Compare);
        return new RpcSignatureSet(DeterministicObjectIdentity.StableTypeName(Type), Signatures.ToArray());
    }

    static Item? FindItemOwner(Transform Value)
    {
        for (var Current = Value; Current; Current = Current.parent)
            if (Current.TryGetComponent<Item>(out var Item)) return Item;
        return null;
    }

    static void WriteFakeItem(ContractWriter Writer, FakeItem Item, string Path)
    {
        if (!Item)
            throw new InvalidOperationException($"Synchronized FakeItem '{Path}' was destroyed before contract construction.");
        if (!Item.realItemPrefab)
            throw new InvalidOperationException($"Synchronized FakeItem '{Path}' has no real item prefab.");
        Writer.StartRecord(FakeItemRecord, "fake-item", Path);
        Writer.Write(Item.realItemPrefab.itemID);
    }

    sealed class RpcSignatureSet
    {
        readonly RpcSignature[] Signatures;

        internal RpcSignatureSet(string ComponentTypeName, RpcSignature[] Signatures)
        {
            this.ComponentTypeName = ComponentTypeName;
            this.Signatures = Signatures;
        }

        internal string ComponentTypeName { get; }
        internal int Count => Signatures.Length;

        internal void Write(ContractWriter Writer)
        {
            Writer.Write(Signatures.Length);
            foreach (var Signature in Signatures) Signature.Write(Writer);
        }
    }

    readonly struct RpcSignature
    {
        readonly string DeclaringTypeName;
        readonly string MethodName;
        readonly string ReturnTypeName;
        readonly int GenericArity;
        readonly string[] ParameterTypeNames;

        internal RpcSignature(MethodInfo Method)
        {
            DeclaringTypeName = DeterministicObjectIdentity.StableTypeName(Method.DeclaringType!);
            MethodName = Method.Name;
            ReturnTypeName = DeterministicObjectIdentity.StableTypeName(Method.ReturnType);
            GenericArity = Method.IsGenericMethodDefinition ? Method.GetGenericArguments().Length : 0;
            var Parameters = Method.GetParameters();
            ParameterTypeNames = new string[Parameters.Length];
            for (var i = 0; i < Parameters.Length; i++)
                ParameterTypeNames[i] = DeterministicObjectIdentity.StableTypeName(Parameters[i].ParameterType);
        }

        internal static int Compare(RpcSignature Left, RpcSignature Right)
        {
            var Result = StringComparer.Ordinal.Compare(Left.DeclaringTypeName, Right.DeclaringTypeName);
            if (Result != 0) return Result;
            Result = StringComparer.Ordinal.Compare(Left.MethodName, Right.MethodName);
            if (Result != 0) return Result;
            Result = StringComparer.Ordinal.Compare(Left.ReturnTypeName, Right.ReturnTypeName);
            if (Result != 0) return Result;
            Result = Left.GenericArity.CompareTo(Right.GenericArity);
            if (Result != 0) return Result;
            Result = Left.ParameterTypeNames.Length.CompareTo(Right.ParameterTypeNames.Length);
            if (Result != 0) return Result;
            for (var i = 0; i < Left.ParameterTypeNames.Length; i++)
            {
                Result = StringComparer.Ordinal.Compare(Left.ParameterTypeNames[i], Right.ParameterTypeNames[i]);
                if (Result != 0) return Result;
            }
            return 0;
        }

        internal void Write(ContractWriter Writer)
        {
            Writer.Write(DeclaringTypeName);
            Writer.Write(MethodName);
            Writer.Write(ReturnTypeName);
            Writer.Write(GenericArity);
            Writer.Write(ParameterTypeNames.Length);
            foreach (var ParameterTypeName in ParameterTypeNames) Writer.Write(ParameterTypeName);
        }
    }

    internal sealed class ContractWriter : IDisposable
    {
        readonly SHA256 HashAlgorithm = SHA256.Create();
        readonly CryptoStream HashStream;
        readonly BinaryWriter HashWriter;
        readonly StringBuilder Preview = new();
        int RecordCount;
        int TotalCharacters;
        bool Completed;

        internal ContractWriter()
        {
            HashStream = new CryptoStream(Stream.Null, HashAlgorithm, CryptoStreamMode.Write);
            HashWriter = new BinaryWriter(HashStream, Encoding.UTF8, true);
            HashWriter.Write(1);
            HashWriter.Write(HashDomain);
        }

        internal void StartRecord(byte Kind, string Name, string? Label = null)
        {
            if (Completed) throw new InvalidOperationException("A generated network contract is already complete.");
            HashWriter.Write(Kind);
            if (RecordCount != 0) AppendEvidence("|");
            RecordCount = checked(RecordCount + 1);
            AppendEvidence(Name);
            if (Label is null) return;
            AppendEvidence("@");
            AppendEvidence(Label.Length.ToString(CultureInfo.InvariantCulture));
            AppendEvidence(":");
            AppendEvidence(Label);
        }

        internal void Write(bool Value)
        {
            HashWriter.Write(Value);
            AppendField(Value ? "1" : "0");
        }

        internal void Write(byte Value)
        {
            HashWriter.Write(Value);
            AppendField(Value.ToString(CultureInfo.InvariantCulture));
        }

        internal void Write(ushort Value)
        {
            HashWriter.Write(Value);
            AppendField(Value.ToString(CultureInfo.InvariantCulture));
        }

        internal void Write(int Value)
        {
            HashWriter.Write(Value);
            AppendField(Value.ToString(CultureInfo.InvariantCulture));
        }

        internal void Write(string Value)
        {
            HashWriter.Write(Value);
            AppendField(Value);
        }

        internal void WritePose(Pose Value) => ManifestEncoding.WritePose(HashWriter, Value);

        internal GeneratedNetworkContract Complete()
        {
            if (Completed) throw new InvalidOperationException("A generated network contract completed twice.");
            HashWriter.Write(RecordCount);
            HashWriter.Flush();
            HashStream.FlushFinalBlock();
            Completed = true;
            var Hash = HashAlgorithm.Hash
                ?? throw new InvalidOperationException("A generated network contract produced no hash.");
            return new GeneratedNetworkContract((byte[])Hash.Clone(), RecordCount, TotalCharacters, Preview.ToString());
        }

        void AppendField(string Value)
        {
            AppendEvidence(";");
            AppendEvidence(Value.Length.ToString(CultureInfo.InvariantCulture));
            AppendEvidence(":");
            AppendEvidence(Value);
        }

        void AppendEvidence(string Value)
        {
            TotalCharacters = checked(TotalCharacters + Value.Length);
            var Available = PreviewCharacterBudget - Preview.Length;
            if (Available > 0) Preview.Append(Value, 0, Math.Min(Available, Value.Length));
        }

        public void Dispose()
        {
            HashWriter.Dispose();
            HashStream.Dispose();
            HashAlgorithm.Dispose();
        }
    }
}
