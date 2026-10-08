using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Library;

public static class PllSurfaceCurvature
{
    struct Edge
    {
        internal int Start, Face, OtherFace;
        internal Edge(int Start, int Face)
        {
            this.Start = Start; this.Face = Face; OtherFace = -1;
        }
    }

    public static List<Vector2> Bake(Mesh Mesh)
    {
        if (!Mesh || !Mesh.isReadable || Mesh.vertexCount < 3)
            throw new ArgumentException("Curvature bake needs a readable triangle mesh. Choose an imported mask.", nameof(Mesh));
        if (Mesh.HasVertexAttribute(VertexAttribute.TexCoord7))
            throw new ArgumentException("UV channel 7 is occupied. Choose an imported curvature mask.", nameof(Mesh));
        if (Mesh.blendShapeCount != 0 || Mesh.HasVertexAttribute(VertexAttribute.BlendWeight))
            throw new ArgumentException("Deforming meshes require imported curvature.", nameof(Mesh));
        var Vertices = Mesh.vertices;
        var ReferenceLength = Mathf.Max(Mesh.bounds.size.x, Mathf.Max(Mesh.bounds.size.y, Mesh.bounds.size.z));
        if (!(ReferenceLength > 0f) || float.IsInfinity(ReferenceLength))
            throw new ArgumentException("Curvature mesh bounds must have positive finite size.", nameof(Mesh));
        var Groups = new Dictionary<Vector3, int>();
        var Positions = new List<Vector3>();
        var VertexGroups = new int[Vertices.Length];
        for (var Index = 0; Index < Vertices.Length; Index++)
        {
            var Position = Vertices[Index];
            if (!Finite(Position.x) || !Finite(Position.y) || !Finite(Position.z))
                throw new ArgumentException("Curvature mesh positions must be finite.", nameof(Mesh));
            if (!Groups.TryGetValue(Position, out var Group))
            {
                Group = Positions.Count; Groups.Add(Position, Group); Positions.Add(Position);
            }
            VertexGroups[Index] = Group;
        }
        var Areas = new double[Positions.Count];
        var Normals = new Vector3[Positions.Count];
        var Laplace = new double[Positions.Count, 3];
        var Edges = new Dictionary<(int, int), Edge>();
        var Faces = new List<(int, int, int)>();
        var UniqueFaces = new HashSet<(int, int, int)>();
        var Incident = new List<int>[Positions.Count];
        for (var Index = 0; Index < Incident.Length; Index++) Incident[Index] = new List<int>();
        for (var Submesh = 0; Submesh < Mesh.subMeshCount; Submesh++)
        {
            if (Mesh.GetTopology(Submesh) != MeshTopology.Triangles)
                throw new ArgumentException("Curvature bake supports triangle topology only.", nameof(Mesh));
            var Indices = Mesh.GetTriangles(Submesh);
            for (var Index = 0; Index < Indices.Length; Index += 3)
            {
                var A = VertexGroups[Indices[Index]];
                var B = VertexGroups[Indices[Index + 1]];
                var C = VertexGroups[Indices[Index + 2]];
                var AB = Positions[B] - Positions[A];
                var AC = Positions[C] - Positions[A];
                var BC = Positions[C] - Positions[B];
                var Cross = Vector3.Cross(AB, AC);
                var DoubleArea = (double)Cross.magnitude;
                var DotA = (double)Vector3.Dot(AB, AC);
                var DotB = -(double)Vector3.Dot(AB, BC);
                var DotC = (double)Vector3.Dot(AC, BC);
                if (A == B || B == C || C == A || !(DoubleArea > 0d) || double.IsInfinity(DoubleArea))
                    throw new ArgumentException("Curvature mesh contains a degenerate triangle. Repair the mesh or choose an imported mask.", nameof(Mesh));
                var Low = Math.Min(A, Math.Min(B, C));
                var High = Math.Max(A, Math.Max(B, C));
                if (!UniqueFaces.Add((Low, A + B + C - Low - High, High)))
                    throw new ArgumentException("Coincident curvature faces are ambiguous. Repair the mesh or choose an imported mask.", nameof(Mesh));
                var Face = Faces.Count;
                Faces.Add((A, B, C));
                Incident[A].Add(Face); Incident[B].Add(Face); Incident[C].Add(Face);
                Normals[A] += Cross; Normals[B] += Cross; Normals[C] += Cross;
                AddEdge(A, B, DotC / DoubleArea, Face);
                AddEdge(B, C, DotA / DoubleArea, Face);
                AddEdge(C, A, DotB / DoubleArea, Face);
                if (DotA < 0d || DotB < 0d || DotC < 0d)
                {
                    Areas[A] += DoubleArea * (DotA < 0d ? 0.25d : 0.125d);
                    Areas[B] += DoubleArea * (DotB < 0d ? 0.25d : 0.125d);
                    Areas[C] += DoubleArea * (DotC < 0d ? 0.25d : 0.125d);
                }
                else
                {
                    Areas[A] += (AB.sqrMagnitude * DotC + AC.sqrMagnitude * DotB) / (8d * DoubleArea);
                    Areas[B] += (AB.sqrMagnitude * DotC + BC.sqrMagnitude * DotA) / (8d * DoubleArea);
                    Areas[C] += (AC.sqrMagnitude * DotB + BC.sqrMagnitude * DotA) / (8d * DoubleArea);
                }
            }
        }
        var Adjacency = new List<int>[Faces.Count];
        var BoundaryEdges = new int[Positions.Count];
        for (var Index = 0; Index < Adjacency.Length; Index++) Adjacency[Index] = new List<int>(3);
        foreach (var Pair in Edges)
        {
            var (A, B) = Pair.Key;
            var Edge = Pair.Value;
            if (Edge.OtherFace < 0) { BoundaryEdges[A]++; BoundaryEdges[B]++; }
            else
            {
                Adjacency[Edge.Face].Add(Edge.OtherFace);
                Adjacency[Edge.OtherFace].Add(Edge.Face);
            }
        }
        var Curvatures = new float[Positions.Count];
        var Reachable = new HashSet<int>();
        var Pending = new Queue<int>();
        for (var Index = 0; Index < Positions.Count; Index++)
        {
            if (Incident[Index].Count == 0 || BoundaryEdges[Index] is not (0 or 2)
                || !(Areas[Index] > 0d) || Normals[Index].sqrMagnitude == 0f)
                throw new ArgumentException("Curvature vertices have unsupported topology. Repair the mesh or choose an imported mask.", nameof(Mesh));
            Reachable.Clear(); Reachable.Add(Incident[Index][0]);
            Pending.Enqueue(Incident[Index][0]);
            while (Pending.Count != 0)
            {
                foreach (var Neighbor in Adjacency[Pending.Dequeue()])
                {
                    var Face = Faces[Neighbor];
                    if ((Face.Item1 == Index || Face.Item2 == Index || Face.Item3 == Index)
                        && Reachable.Add(Neighbor)) Pending.Enqueue(Neighbor);
                }
            }
            if (Reachable.Count != Incident[Index].Count)
                throw new ArgumentException("Coincident curvature vertex fans are disconnected. Repair the mesh or choose an imported mask.", nameof(Mesh));
            var Normal = Normals[Index].normalized;
            var SignedMean = -(Laplace[Index, 0] * Normal.x + Laplace[Index, 1] * Normal.y
                + Laplace[Index, 2] * Normal.z) / (4d * Areas[Index]);
            Curvatures[Index] = (float)Math.Max(-1d, Math.Min(1d, SignedMean * ReferenceLength));
        }
        var Result = new List<Vector2>(Vertices.Length);
        foreach (var Group in VertexGroups) Result.Add(new Vector2(Curvatures[Group], 0f));
        return Result;

        void AddEdge(int A, int B, double Cotangent, int Face)
        {
            var Key = A < B ? (A, B) : (B, A);
            if (!Edges.TryGetValue(Key, out var Edge)) Edges.Add(Key, new Edge(A, Face));
            else
            {
                if (Edge.OtherFace >= 0 || Edge.Start == A)
                    throw new ArgumentException("Curvature mesh has nonmanifold edges or inconsistent winding. Repair the mesh or choose an imported mask.", nameof(Mesh));
                Edge.OtherFace = Face; Edges[Key] = Edge;
            }
            var Difference = Positions[B] - Positions[A];
            for (var Axis = 0; Axis < 3; Axis++)
            {
                var Value = Cotangent * Difference[Axis];
                Laplace[A, Axis] += Value; Laplace[B, Axis] -= Value;
            }
        }
    }

    static bool Finite(float Value) => !float.IsNaN(Value) && !float.IsInfinity(Value);
}
