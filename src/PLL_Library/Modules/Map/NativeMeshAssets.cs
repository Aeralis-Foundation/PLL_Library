using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Library;

internal sealed class NativeMeshAssets
{
    readonly Dictionary<Mesh, (Vector3[] Vertices, int[] Triangles)> Cache = new();

    internal static GraphicsBuffer? OpenIndexBuffer(Mesh Mesh) =>
        Mesh.GetNativeIndexBufferPtr() == IntPtr.Zero ? null : Mesh.GetIndexBuffer();

    internal NativeMeshGeometry Resolve(MeshCollider Collider) =>
        Resolve(Collider.sharedMesh, Collider.transform.localToWorldMatrix);

    internal NativeMeshGeometry Resolve(MeshFilter Filter) =>
        Resolve(Filter.sharedMesh, Filter.transform.localToWorldMatrix);

    NativeMeshGeometry Resolve(Mesh Mesh, Matrix4x4 LocalToWorld)
    {
        if (!Cache.TryGetValue(Mesh, out var Data))
        {
            Data = Read(Mesh);
            Cache.Add(Mesh, Data);
        }
        return new NativeMeshGeometry(Data.Vertices, Data.Triangles, LocalToWorld);
    }

    static (Vector3[] Vertices, int[] Triangles) Read(Mesh Mesh)
    {
        var SubMeshes = new SubMeshDescriptor[Mesh.subMeshCount];
        var IndexCount = 0;
        for (var i = 0; i < SubMeshes.Length; i++)
        {
            var SubMesh = SubMeshes[i] = Mesh.GetSubMesh(i);
            if (SubMesh.topology != MeshTopology.Triangles)
                throw new NotSupportedException(
                    $"Native mesh '{Mesh.name}' submesh {i} uses '{SubMesh.topology}', not triangles.");
            IndexCount = checked(IndexCount + SubMesh.indexCount);
        }
        if (Mesh.isReadable) return (Mesh.vertices, Mesh.triangles);

        var Format = Mesh.GetVertexAttributeFormat(VertexAttribute.Position);
        var ComponentBytes = Format switch
        {
            VertexAttributeFormat.Float32 => sizeof(float),
            VertexAttributeFormat.Float16 => sizeof(ushort),
            _ => throw new NotSupportedException(
                $"Native mesh '{Mesh.name}' uses unsupported position format '{Format}'.")
        };
        if (Mesh.GetVertexAttributeDimension(VertexAttribute.Position) < 3)
            throw new NotSupportedException($"Native mesh '{Mesh.name}' has fewer than three position components.");
        var Stream = Mesh.GetVertexAttributeStream(VertexAttribute.Position);
        var Offset = Mesh.GetVertexAttributeOffset(VertexAttribute.Position);
        var Stride = Mesh.GetVertexBufferStride(Stream);
        var Vertices = new Vector3[Mesh.vertexCount];
        using (var Buffer = Mesh.GetVertexBuffer(Stream))
        {
            if (Format == VertexAttributeFormat.Float32 && Offset == 0 && Stride == 3 * sizeof(float))
                Buffer.GetData(Vertices);
            else
            {
                var Bytes = new byte[checked(Vertices.Length * Stride)];
                Buffer.GetData(Bytes);
                for (var i = 0; i < Vertices.Length; i++)
                {
                    var Position = i * Stride + Offset;
                    Vertices[i] = Format == VertexAttributeFormat.Float32
                        ? new Vector3(BitConverter.ToSingle(Bytes, Position),
                            BitConverter.ToSingle(Bytes, Position + ComponentBytes),
                            BitConverter.ToSingle(Bytes, Position + 2 * ComponentBytes))
                        : new Vector3(Mathf.HalfToFloat(BitConverter.ToUInt16(Bytes, Position)),
                            Mathf.HalfToFloat(BitConverter.ToUInt16(Bytes, Position + ComponentBytes)),
                            Mathf.HalfToFloat(BitConverter.ToUInt16(Bytes, Position + 2 * ComponentBytes)));
                }
            }
        }
        var Triangles = new int[IndexCount];
        using (var Buffer = Mesh.GetIndexBuffer())
        {
            var Written = 0;
            if (Mesh.indexFormat == IndexFormat.UInt16)
            {
                var Indices = new ushort[Buffer.count];
                Buffer.GetData(Indices);
                foreach (var SubMesh in SubMeshes)
                    for (var i = 0; i < SubMesh.indexCount; i++)
                        Triangles[Written++] = checked(Indices[SubMesh.indexStart + i] + SubMesh.baseVertex);
            }
            else
                foreach (var SubMesh in SubMeshes)
                {
                    Buffer.GetData(Triangles, Written, SubMesh.indexStart, SubMesh.indexCount);
                    if (SubMesh.baseVertex != 0)
                        for (var i = Written; i < Written + SubMesh.indexCount; i++)
                            Triangles[i] = checked(Triangles[i] + SubMesh.baseVertex);
                    Written += SubMesh.indexCount;
                }
        }
        return (Vertices, Triangles);
    }
}
