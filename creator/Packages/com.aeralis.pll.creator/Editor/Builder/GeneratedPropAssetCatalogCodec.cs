using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PLL.Library
{
    internal static class GeneratedPropAssetCatalogCodec
    {
        const string Protocol = "PLL/GeneratedPropAssetCatalog/v1";
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal readonly struct Entry
        {
            internal Entry(string AssetGuid, long LocalId, string DependencyHash)
            {
                RequireHex(AssetGuid, "asset GUID");
                RequireHex(DependencyHash, "dependency hash");
                this.AssetGuid = AssetGuid;
                this.LocalId = LocalId;
                this.DependencyHash = DependencyHash;
            }

            internal string AssetGuid { get; }
            internal long LocalId { get; }
            internal string DependencyHash { get; }
        }

        internal sealed class Tables
        {
            internal Tables(Entry[] Prefabs, Entry[] Meshes, Entry[] Materials, Entry[] PhysicsMaterials)
            {
                this.Prefabs = Prefabs;
                this.Meshes = Meshes;
                this.Materials = Materials;
                this.PhysicsMaterials = PhysicsMaterials;
            }

            internal Entry[] Prefabs { get; }
            internal Entry[] Meshes { get; }
            internal Entry[] Materials { get; }
            internal Entry[] PhysicsMaterials { get; }
        }

        internal static byte[] Encode(Tables Catalog)
        {
            using var Stream = new MemoryStream();
            using (var Writer = new BinaryWriter(Stream, StrictUtf8, true))
            {
                Writer.Write(Protocol);
                WriteTable(Writer, Catalog.Prefabs, typeof(GameObject));
                WriteTable(Writer, Catalog.Meshes, typeof(Mesh));
                WriteTable(Writer, Catalog.Materials, typeof(Material));
                WriteTable(Writer, Catalog.PhysicsMaterials, typeof(PhysicsMaterial));
            }
            return Stream.ToArray();
        }

        internal static Tables Decode(byte[] Bytes)
        {
            using var Stream = new MemoryStream(Bytes, false);
            using var Reader = new BinaryReader(Stream, StrictUtf8, true);
            ReadExpected(Reader, Protocol);
            var Catalog = new Tables(
                ReadTable(Reader, typeof(GameObject)), ReadTable(Reader, typeof(Mesh)),
                ReadTable(Reader, typeof(Material)), ReadTable(Reader, typeof(PhysicsMaterial)));
            if (Stream.Position != Stream.Length)
                throw new InvalidDataException("Generated prop asset catalog has trailing bytes.");
            return Catalog;
        }

        internal static int Compare(Entry Left, Entry Right)
        {
            var Order = StringComparer.Ordinal.Compare(Left.AssetGuid, Right.AssetGuid);
            return Order != 0 ? Order : Left.LocalId.CompareTo(Right.LocalId);
        }

        static string TypeName(Type Type) => Type.Assembly.GetName().Name + ":" + Type.FullName;

        static void WriteTable(BinaryWriter Writer, Entry[] Entries, Type Type)
        {
            Writer.Write(Entries.Length);
            var Name = TypeName(Type);
            for (var i = 0; i < Entries.Length; i++)
            {
                var Entry = Entries[i];
                if (i != 0 && Compare(Entries[i - 1], Entry) >= 0)
                    throw new InvalidDataException("Generated prop assets must be unique and ordered by GUID and local ID.");
                Writer.Write(Entry.AssetGuid); Writer.Write(Entry.LocalId);
                Writer.Write(Name); Writer.Write(Entry.DependencyHash);
            }
        }

        static Entry[] ReadTable(BinaryReader Reader, Type Type)
        {
            var Count = Reader.ReadInt32();
            var Name = TypeName(Type);
            if (Count < 0 || Count > (Reader.BaseStream.Length - Reader.BaseStream.Position) / (75 + Name.Length))
                throw new InvalidDataException("Generated prop asset count exceeds its encoded table.");
            var Entries = new Entry[Count];
            for (var i = 0; i < Entries.Length; i++)
            {
                var Guid = ReadHex(Reader);
                var LocalId = Reader.ReadInt64();
                ReadExpected(Reader, Name);
                Entries[i] = new Entry(Guid, LocalId, ReadHex(Reader));
                if (i != 0 && Compare(Entries[i - 1], Entries[i]) >= 0)
                    throw new InvalidDataException("Generated prop assets must be unique and ordered by GUID and local ID.");
            }
            return Entries;
        }

        static string ReadHex(BinaryReader Reader)
        {
            if (Reader.ReadByte() != 32)
                throw new InvalidDataException("Generated prop asset identity must contain 32 lowercase hexadecimal digits.");
            var Bytes = Reader.ReadBytes(32);
            if (Bytes.Length != 32) throw new EndOfStreamException("Generated prop asset identity is incomplete.");
            return StrictUtf8.GetString(Bytes);
        }

        static void ReadExpected(BinaryReader Reader, string Expected)
        {
            if (Reader.ReadByte() != Expected.Length)
                throw new InvalidDataException("Generated prop asset catalog has an unsupported protocol or asset type.");
            for (var i = 0; i < Expected.Length; i++)
                if (Reader.ReadByte() != Expected[i])
                    throw new InvalidDataException("Generated prop asset catalog has an unsupported protocol or asset type.");
        }

        static void RequireHex(string Value, string Name)
        {
            if (Value == null || Value.Length != 32)
                throw new ArgumentException("Generated prop " + Name + " must contain 32 lowercase hexadecimal digits.");
            for (var i = 0; i < Value.Length; i++)
                if (Value[i] < '0' || Value[i] > '9' && Value[i] < 'a' || Value[i] > 'f')
                    throw new ArgumentException("Generated prop " + Name + " must contain 32 lowercase hexadecimal digits.");
        }
    }
}
