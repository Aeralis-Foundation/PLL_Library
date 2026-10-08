using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Creator.Editor
{
    internal sealed class PllSurfaceCurvatureBaker
    {
        readonly string StageDirectory;
        readonly Dictionary<string, Mesh> Derived = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        readonly HashSet<Mesh> PreparedMeshes = new HashSet<Mesh>();

        internal PllSurfaceCurvatureBaker(string StageDirectory) => this.StageDirectory = StageDirectory;

        internal void Prepare(PllSurfaceTextureAuthoring Source)
        {
            PllSurfaceProfileContract.Validate(Source.Material);
            ValidateNormalTextures(Source.Material);
            if (Source.Target != PllSurfaceTarget.AuthoredRendererSlots
                || !PllSurfaceProfileContract.NeedsBake(Source.Material)) return;
            var Prepared = new List<Mesh>();
            for (var Index = 0; Index < Source.Renderers.Count; Index++)
            {
                var Renderer = Source.Renderers[Index];
                if (Renderer is not MeshRenderer || !Renderer.TryGetComponent<MeshFilter>(out var Filter) || !Filter.sharedMesh)
                    throw new ArgumentException("Automatic curvature needs a static Mesh Renderer. Choose an imported mask.", nameof(Source));
                var Original = Filter.sharedMesh;
                if (Source.HasPreparedCurvature(Original) || PreparedMeshes.Contains(Original))
                {
                    Prepared.Add(Original); continue;
                }
                var Fingerprint = FingerprintMesh(Original);
                if (!Derived.TryGetValue(Fingerprint, out var Mesh))
                {
                    var Values = PllSurfaceCurvature.Bake(Original);
                    Mesh = UnityEngine.Object.Instantiate(Original);
                    Mesh.name = Original.name + " PLL Curvature";
                    Mesh.SetUVs(PllSurfaceProfileContract.CurvatureChannel, Values);
                    AssetDatabase.CreateAsset(Mesh, StageDirectory + "/surface-" + Fingerprint + ".asset");
                    Derived.Add(Fingerprint, Mesh);
                    PreparedMeshes.Add(Mesh);
                }
                Filter.sharedMesh = Mesh;
                Prepared.Add(Mesh);
            }
            Source.SetPreparedCurvature(Prepared.ToArray());
        }

        internal static void ValidateNormalTextures(Material Material)
        {
            for (var Layer = 0; Layer < PllSurfaceProfileContract.LayerCount; Layer++)
            {
                if (Layer != 0 && Material.GetFloat(PllSurfaceProfileContract.Property(Layer, "Enabled")) == 0f) continue;
                var Texture = Material.GetTexture(PllSurfaceProfileContract.Property(Layer, "Normal"));
                if (Texture && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(Texture)) is TextureImporter Importer
                    && Importer.textureType != TextureImporterType.NormalMap)
                    throw new ArgumentException("Surface normal textures must use the Normal Map import type.", nameof(Material));
            }
        }

        internal static string FingerprintMesh(Mesh Mesh)
        {
            using (var Hash = SHA256.Create())
            using (var Stream = new CryptoStream(System.IO.Stream.Null, Hash, CryptoStreamMode.Write))
            {
                using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
                using (var Meshes = Mesh.AcquireReadOnlyMeshData(Mesh))
                {
                    var Data = Meshes[0];
                    var Buffer = new byte[4096];
                    Writer.Write(PllSurfaceProfileContract.BakeDomain);
                    Writer.Write(Mesh.vertexCount); Writer.Write(Mesh.vertexBufferCount);
                    foreach (var Attribute in Mesh.GetVertexAttributes())
                    {
                        Writer.Write((int)Attribute.attribute); Writer.Write((int)Attribute.format);
                        Writer.Write(Attribute.dimension); Writer.Write(Attribute.stream);
                    }
                    for (var Index = 0; Index < Mesh.vertexBufferCount; Index++)
                    {
                        var Bytes = Data.GetVertexData<byte>(Index);
                        Writer.Write(Bytes.Length);
                        for (var Offset = 0; Offset < Bytes.Length; Offset += Buffer.Length)
                        {
                            var Count = Math.Min(Buffer.Length, Bytes.Length - Offset);
                            Unity.Collections.NativeArray<byte>.Copy(Bytes, Offset, Buffer, 0, Count);
                            Writer.Write(Buffer, 0, Count);
                        }
                    }
                    var Indices = Data.GetIndexData<byte>();
                    Writer.Write((int)Mesh.indexFormat); Writer.Write(Indices.Length);
                    for (var Offset = 0; Offset < Indices.Length; Offset += Buffer.Length)
                    {
                        var Count = Math.Min(Buffer.Length, Indices.Length - Offset);
                        Unity.Collections.NativeArray<byte>.Copy(Indices, Offset, Buffer, 0, Count);
                        Writer.Write(Buffer, 0, Count);
                    }
                    Writer.Write(Mesh.subMeshCount);
                    for (var Index = 0; Index < Mesh.subMeshCount; Index++)
                    {
                        var Submesh = Mesh.GetSubMesh(Index);
                        Writer.Write((int)Submesh.topology); Writer.Write(Submesh.indexStart);
                        Writer.Write(Submesh.indexCount); Writer.Write(Submesh.baseVertex);
                    }
                    Writer.Write(Mesh.bounds.center.x); Writer.Write(Mesh.bounds.center.y); Writer.Write(Mesh.bounds.center.z);
                    Writer.Write(Mesh.bounds.size.x); Writer.Write(Mesh.bounds.size.y); Writer.Write(Mesh.bounds.size.z);
                }
                Stream.FlushFinalBlock();
                return BitConverter.ToString(Hash.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
