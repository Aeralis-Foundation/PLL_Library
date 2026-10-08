using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PLL.Library;

public static class PllDependencySeal
{
    const string AddressPrefix = "pll-presentation-dependency-seal-v2:";
    const string Protocol = "PLL/PresentationDependencySeal/v2";
    const int HashLength = 32;
    const int MaximumEncodedBiomeIdLength = 768;
    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public readonly struct Entry
    {
        public Entry(string AssetGuid, long LocalId, string Type, string DependencyHash)
        {
            this.AssetGuid = AssetGuid ?? throw new ArgumentNullException(nameof(AssetGuid));
            this.LocalId = LocalId;
            this.Type = Type ?? throw new ArgumentNullException(nameof(Type));
            this.DependencyHash = DependencyHash ?? throw new ArgumentNullException(nameof(DependencyHash));
        }

        public string AssetGuid { get; }
        public long LocalId { get; }
        public string Type { get; }
        public string DependencyHash { get; }
    }

    public static byte[] CreateHash(IReadOnlyList<Entry> Entries)
    {
        if (Entries is null) throw new ArgumentNullException(nameof(Entries));
        var Ordered = new Entry[Entries.Count];
        for (var i = 0; i < Ordered.Length; i++) Ordered[i] = Entries[i];
        Array.Sort(Ordered, Compare);
        for (var i = 1; i < Ordered.Length; i++)
            if (Compare(Ordered[i - 1], Ordered[i]) == 0)
                throw new ArgumentException("A dependency seal contains a duplicate asset identity.", nameof(Entries));

        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, StrictUtf8, true))
        {
            Writer.Write(Protocol);
            Writer.Write(Ordered.Length);
            foreach (var Value in Ordered)
            {
                Writer.Write(Value.AssetGuid);
                Writer.Write(Value.LocalId);
                Writer.Write(Value.Type);
                Writer.Write(Value.DependencyHash);
            }
        }
        using var Sha = SHA256.Create();
        return Sha.ComputeHash(Stream.GetBuffer(), 0, checked((int)Stream.Length));
    }

    public static string CreateAddress(BiomeId Id, ReadOnlySpan<byte> Hash)
    {
        if (!Id.IsValid) throw new ArgumentException("A dependency seal requires a valid biome ID.", nameof(Id));
        if (Hash.Length != HashLength)
            throw new ArgumentException($"A dependency seal hash must contain {HashLength} bytes.", nameof(Hash));
        var EncodedId = StrictUtf8.GetBytes(Id.Value);
        if (EncodedId.Length * 2 > MaximumEncodedBiomeIdLength)
            throw new ArgumentException("The dependency seal biome ID is too long when UTF-8 encoded.", nameof(Id));
        return AddressPrefix + Hex(EncodedId) + ":" + Hex(Hash);
    }

    public static bool HasAddressPrefix(string? Address) =>
        Address?.StartsWith(AddressPrefix, StringComparison.Ordinal) == true;

    public static bool TryParseAddress(string? Address, out BiomeId Id, out byte[] Hash)
    {
        Id = default;
        Hash = Array.Empty<byte>();
        if (!HasAddressPrefix(Address)) return false;
        var Separator = Address!.IndexOf(':', AddressPrefix.Length);
        if (Separator <= AddressPrefix.Length || Address.IndexOf(':', Separator + 1) >= 0) return false;
        var EncodedIdLength = Separator - AddressPrefix.Length;
        if (EncodedIdLength > MaximumEncodedBiomeIdLength
            || Address.Length - Separator - 1 != HashLength * 2
            || !TryDecodeHex(Address.AsSpan(AddressPrefix.Length, EncodedIdLength), out var IdBytes)
            || !TryDecodeHex(Address.AsSpan(Separator + 1), out Hash)) return false;
        string Value;
        try { Value = StrictUtf8.GetString(IdBytes); }
        catch (DecoderFallbackException) { return false; }
        return BiomeId.TryParse(Value, out Id);
    }

    static int Compare(Entry Left, Entry Right)
    {
        var Result = StringComparer.Ordinal.Compare(Left.AssetGuid, Right.AssetGuid);
        if (Result != 0) return Result;
        Result = Left.LocalId.CompareTo(Right.LocalId);
        return Result != 0 ? Result : StringComparer.Ordinal.Compare(Left.Type, Right.Type);
    }

    static string Hex(ReadOnlySpan<byte> Value)
    {
        const string Digits = "0123456789abcdef";
        var Result = new char[Value.Length * 2];
        for (var i = 0; i < Value.Length; i++)
        {
            Result[i * 2] = Digits[Value[i] >> 4];
            Result[i * 2 + 1] = Digits[Value[i] & 15];
        }
        return new string(Result);
    }

    static bool TryDecodeHex(ReadOnlySpan<char> Value, out byte[] Bytes)
    {
        if (Value.Length == 0 || (Value.Length & 1) != 0)
        {
            Bytes = Array.Empty<byte>();
            return false;
        }
        Bytes = new byte[Value.Length / 2];
        for (var i = 0; i < Bytes.Length; i++)
        {
            var High = HexDigit(Value[i * 2]);
            var Low = HexDigit(Value[i * 2 + 1]);
            if (High < 0 || Low < 0)
            {
                Bytes = Array.Empty<byte>();
                return false;
            }
            Bytes[i] = (byte)(High << 4 | Low);
        }
        return true;
    }

    static int HexDigit(char Value) => Value is >= '0' and <= '9' ? Value - '0'
        : Value is >= 'a' and <= 'f' ? Value - 'a' + 10 : -1;
}
