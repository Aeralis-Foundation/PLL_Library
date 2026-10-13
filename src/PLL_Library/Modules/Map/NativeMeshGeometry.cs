using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeMeshGeometry
{
    const int LeafSize = 4;
    const int MaximumProjectedPolygonVertices = 12;
    const float MaximumGroundAngle = 50f;

    readonly struct Point
    {
        internal Point(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal Point(Vector3 value) : this(value.x, value.y, value.z) { }

        internal double X { get; }
        internal double Y { get; }
        internal double Z { get; }

        public static Point operator +(Point left, Point right) =>
            new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        public static Point operator -(Point left, Point right) =>
            new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static Point operator *(Point value, double scale) =>
            new(value.X * scale, value.Y * scale, value.Z * scale);
    }

    readonly struct Triangle
    {
        internal Triangle(int order, int a, int b, int c, Vector3 center)
        {
            Order = order;
            A = a;
            B = b;
            C = c;
            Center = center;
        }

        internal int Order { get; }
        internal int A { get; }
        internal int B { get; }
        internal int C { get; }
        internal Vector3 Center { get; }
    }

    readonly struct Node
    {
        internal Node(Vector3 minimum, Vector3 maximum, int startOrRight, int count)
        {
            Minimum = minimum;
            Maximum = maximum;
            StartOrRight = startOrRight;
            Count = count;
        }

        internal Vector3 Minimum { get; }
        internal Vector3 Maximum { get; }
        internal int StartOrRight { get; }
        internal int Count { get; }
        internal bool IsLeaf => Count != 0;
    }

    sealed class TriangleComparer : IComparer<Triangle>
    {
        readonly int axis;

        internal TriangleComparer(int axis) => this.axis = axis;

        public int Compare(Triangle left, Triangle right)
        {
            var comparison = left.Center[axis].CompareTo(right.Center[axis]);
            if (comparison != 0) return comparison;
            comparison = left.A.CompareTo(right.A);
            if (comparison != 0) return comparison;
            comparison = left.B.CompareTo(right.B);
            if (comparison != 0) return comparison;
            comparison = left.C.CompareTo(right.C);
            return comparison != 0 ? comparison : left.Order.CompareTo(right.Order);
        }
    }

    struct SearchState
    {
        internal SearchState(Point query)
        {
            Query = query;
            Closest = default;
            SquaredDistance = double.PositiveInfinity;
        }

        internal Point Query { get; }
        internal Point Closest { get; private set; }
        internal double SquaredDistance { get; private set; }

        internal void Consider(Point point)
        {
            var offset = point - Query;
            var squaredDistance = Dot(offset, offset);
            if (squaredDistance >= SquaredDistance) return;
            Closest = point;
            SquaredDistance = squaredDistance;
        }
    }

    struct GroundBoundaryState
    {
        internal GroundBoundaryState(Point forward, Point up)
        {
            Forward = forward;
            Up = up;
            Found = false;
            Ambiguous = false;
            Selected = default;
            ForwardProjection = 0d;
            Height = 0d;
            TriangleOrder = int.MaxValue;
        }

        internal Point Forward { get; }
        internal Point Up { get; }
        internal bool Found { get; private set; }
        internal bool Ambiguous { get; private set; }
        internal Point Selected { get; private set; }
        internal double ForwardProjection { get; private set; }
        internal double Height { get; private set; }
        internal int TriangleOrder { get; private set; }

        internal void Consider(Point point, int triangleOrder)
        {
            var forwardProjection = Dot(point, Forward);
            var height = Dot(point, Up);
            if (!Found || forwardProjection > ForwardProjection
                || forwardProjection == ForwardProjection && height > Height)
            {
                Found = true;
                Ambiguous = false;
                Selected = point;
                ForwardProjection = forwardProjection;
                Height = height;
                TriangleOrder = triangleOrder;
                return;
            }
            if (forwardProjection < ForwardProjection
                || forwardProjection == ForwardProjection && height < Height) return;
            var comparison = ComparePoint(point, Selected);
            if (comparison != 0) Ambiguous = true;
            if (comparison < 0 || comparison == 0 && triangleOrder < TriangleOrder)
            {
                Selected = point;
                TriangleOrder = triangleOrder;
            }
        }
    }

    struct GroundLineState
    {
        internal GroundLineState(Point origin)
        {
            Origin = origin;
            Found = false;
            Ambiguous = false;
            Selected = default;
            SquaredDistance = double.PositiveInfinity;
            TriangleOrder = int.MaxValue;
        }

        internal Point Origin { get; }
        internal bool Found { get; private set; }
        internal bool Ambiguous { get; private set; }
        internal Point Selected { get; private set; }
        internal double SquaredDistance { get; private set; }
        internal int TriangleOrder { get; private set; }

        internal void Consider(Point point, int triangleOrder)
        {
            var offset = point - Origin;
            var squaredDistance = Dot(offset, offset);
            if (squaredDistance < SquaredDistance)
            {
                Found = true;
                Ambiguous = false;
                Selected = point;
                SquaredDistance = squaredDistance;
                TriangleOrder = triangleOrder;
                return;
            }
            if (squaredDistance > SquaredDistance) return;
            var comparison = ComparePoint(point, Selected);
            if (comparison != 0) Ambiguous = true;
            if (comparison < 0 || comparison == 0 && triangleOrder < TriangleOrder)
            {
                Selected = point;
                TriangleOrder = triangleOrder;
            }
        }
    }

    struct GroundSectionState
    {
        internal GroundSectionState(Point forward, Point up)
        {
            Forward = forward;
            Up = up;
            Found = false;
            Selected = default;
            ForwardProjection = 0d;
            Height = 0d;
            TriangleOrder = int.MaxValue;
        }

        internal Point Forward { get; }
        internal Point Up { get; }
        internal bool Found { get; private set; }
        internal Point Selected { get; private set; }
        internal double ForwardProjection { get; private set; }
        internal double Height { get; private set; }
        internal int TriangleOrder { get; private set; }

        internal void Consider(Point point, int triangleOrder)
        {
            var forwardProjection = Dot(point, Forward);
            var height = Dot(point, Up);
            var comparison = Found ? ComparePoint(point, Selected) : 0;
            if (Found && (forwardProjection > ForwardProjection
                || forwardProjection == ForwardProjection && height < Height
                || forwardProjection == ForwardProjection && height == Height && comparison > 0
                || forwardProjection == ForwardProjection && height == Height
                    && comparison == 0 && triangleOrder >= TriangleOrder)) return;
            Found = true;
            Selected = point;
            ForwardProjection = forwardProjection;
            Height = height;
            TriangleOrder = triangleOrder;
        }
    }

    readonly struct ProjectedPoint : IEquatable<ProjectedPoint>
    {
        internal ProjectedPoint(double x, double z)
        {
            X = x;
            Z = z;
        }

        internal double X { get; }
        internal double Z { get; }

        public bool Equals(ProjectedPoint other) => X.Equals(other.X) && Z.Equals(other.Z);
        public override bool Equals(object? value) => value is ProjectedPoint other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Z.GetHashCode());

        public static ProjectedPoint operator +(ProjectedPoint left, ProjectedPoint right) =>
            new(left.X + right.X, left.Z + right.Z);

        public static ProjectedPoint operator -(ProjectedPoint left, ProjectedPoint right) =>
            new(left.X - right.X, left.Z - right.Z);

        public static ProjectedPoint operator *(ProjectedPoint value, double scale) =>
            new(value.X * scale, value.Z * scale);
    }

    readonly struct ProjectedGroundTriangle
    {
        readonly double heightX;
        readonly double heightZ;
        readonly double heightConstant;
        readonly double aHeight;
        readonly double bHeight;
        readonly double cHeight;

        internal ProjectedGroundTriangle(int order, Point a, Point b, Point c)
        {
            Order = order;
            var normal = Cross(b - a, c - a);
            if (normal.Y == 0d)
                throw new InvalidOperationException("A groundable triangle has no projected area.");
            heightX = -normal.X / normal.Y;
            heightZ = -normal.Z / normal.Y;
            heightConstant = a.Y - heightX * a.X - heightZ * a.Z;
            A = new ProjectedPoint(a.X, a.Z);
            B = new ProjectedPoint(b.X, b.Z);
            C = new ProjectedPoint(c.X, c.Z);
            aHeight = a.Y;
            bHeight = b.Y;
            cHeight = c.Y;
            if (ProjectedSide(A, B, C) < 0d)
            {
                (B, C) = (C, B);
                (bHeight, cHeight) = (cHeight, bHeight);
            }
            if (ProjectedSide(A, B, C) == 0d)
                throw new InvalidOperationException("A groundable triangle has no projected area.");
            MinimumX = Math.Min(A.X, Math.Min(B.X, C.X));
            MaximumX = Math.Max(A.X, Math.Max(B.X, C.X));
            MinimumZ = Math.Min(A.Z, Math.Min(B.Z, C.Z));
            MaximumZ = Math.Max(A.Z, Math.Max(B.Z, C.Z));
        }

        internal int Order { get; }
        internal ProjectedPoint A { get; }
        internal ProjectedPoint B { get; }
        internal ProjectedPoint C { get; }
        internal double MinimumX { get; }
        internal double MaximumX { get; }
        internal double MinimumZ { get; }
        internal double MaximumZ { get; }

        internal bool Overlaps(ProjectedGroundTriangle other) =>
            MaximumX >= other.MinimumX && MinimumX <= other.MaximumX
            && MaximumZ >= other.MinimumZ && MinimumZ <= other.MaximumZ;

        internal bool Contains(ProjectedPoint point) =>
            point.X >= MinimumX && point.X <= MaximumX
            && point.Z >= MinimumZ && point.Z <= MaximumZ
            && ProjectedSide(A, B, point) >= 0d
            && ProjectedSide(B, C, point) >= 0d
            && ProjectedSide(C, A, point) >= 0d;

        internal double Height(ProjectedPoint point)
        {
            if (point.Equals(A)) return aHeight;
            if (point.Equals(B)) return bHeight;
            if (point.Equals(C)) return cHeight;
            return heightX * point.X + heightZ * point.Z + heightConstant;
        }
    }

    sealed class ProjectedPolygon
    {
        internal ProjectedPolygon(ProjectedPoint[] vertices, double signedArea)
        {
            Vertices = vertices;
            SignedArea = signedArea;
            MinimumX = double.PositiveInfinity;
            MaximumX = double.NegativeInfinity;
            MinimumZ = double.PositiveInfinity;
            MaximumZ = double.NegativeInfinity;
            for (var i = 0; i < vertices.Length; i++)
            {
                MinimumX = Math.Min(MinimumX, vertices[i].X);
                MaximumX = Math.Max(MaximumX, vertices[i].X);
                MinimumZ = Math.Min(MinimumZ, vertices[i].Z);
                MaximumZ = Math.Max(MaximumZ, vertices[i].Z);
            }
        }

        internal ProjectedPoint[] Vertices { get; }
        internal double SignedArea { get; }
        internal double MinimumX { get; }
        internal double MaximumX { get; }
        internal double MinimumZ { get; }
        internal double MaximumZ { get; }
        internal bool HasArea => Vertices.Length >= 3 && SignedArea > 0d;
        internal bool Overlaps(ProjectedGroundTriangle triangle) => Vertices.Length != 0
            && MaximumX >= triangle.MinimumX && MinimumX <= triangle.MaximumX
            && MaximumZ >= triangle.MinimumZ && MinimumZ <= triangle.MaximumZ;
    }

    readonly struct BridgeOverlap
    {
        internal BridgeOverlap(ProjectedPolygon polygon, int seedIndex)
        {
            Polygon = polygon;
            SeedIndex = seedIndex;
        }

        internal ProjectedPolygon Polygon { get; }
        internal int SeedIndex { get; }
    }
    struct PenetrationState
    {
        internal PenetrationState(double clearance)
        {
            Clearance = clearance;
            Found = false;
            Penetration = 0d;
            EntryOrder = int.MaxValue;
            BridgeOrder = int.MaxValue;
            Selected = default;
        }

        internal double Clearance { get; }
        internal bool Found { get; private set; }
        internal double Penetration { get; private set; }
        internal int EntryOrder { get; private set; }
        internal int BridgeOrder { get; private set; }
        internal Point Selected { get; private set; }

        internal void Consider(
            double entryHeight,
            double bridgeHeight,
            int entryOrder,
            int bridgeOrder,
            Point worldPoint)
        {
            var penetration = entryHeight - bridgeHeight + Clearance;
            if (!double.IsFinite(penetration))
                throw new InvalidOperationException("Groundable top-envelope penetration is non-finite.");
            if (Found && penetration < Penetration) return;
            if (Found && penetration == Penetration)
            {
                var comparison = ComparePoint(worldPoint, Selected);
                if (comparison > 0 || comparison == 0 && (entryOrder > EntryOrder
                    || entryOrder == EntryOrder && bridgeOrder >= BridgeOrder)) return;
            }
            Found = true;
            Penetration = penetration;
            EntryOrder = entryOrder;
            BridgeOrder = bridgeOrder;
            Selected = worldPoint;
        }
    }

    static readonly TriangleComparer XComparer = new(0);
    static readonly TriangleComparer YComparer = new(1);
    static readonly TriangleComparer ZComparer = new(2);

    readonly Vector3[] vertices;
    readonly Triangle[] triangles;
    readonly Node[] nodes;

    internal NativeMeshGeometry(Vector3[] localVertices, int[] triangleIndices, Matrix4x4 localToWorld)
    {
        if (localVertices is null) throw new ArgumentNullException(nameof(localVertices));
        if (triangleIndices is null) throw new ArgumentNullException(nameof(triangleIndices));
        if (!Finite(localToWorld))
            throw new ArgumentException("The local-to-world matrix contains a non-finite value.", nameof(localToWorld));
        if (triangleIndices.Length % 3 != 0)
            throw new ArgumentException("Triangle index count must be a multiple of three.", nameof(triangleIndices));
        for (var i = 0; i < triangleIndices.Length; i++)
            if ((uint)triangleIndices[i] >= (uint)localVertices.Length)
                throw new ArgumentOutOfRangeException(
                    nameof(triangleIndices), $"Triangle index at offset {i} is outside the vertex array.");

        vertices = new Vector3[localVertices.Length];
        for (var i = 0; i < localVertices.Length; i++)
        {
            if (!UnityValueContract.Finite(localVertices[i]))
                throw new ArgumentException($"Local vertex {i} contains a non-finite value.", nameof(localVertices));
            vertices[i] = localToWorld.MultiplyPoint3x4(localVertices[i]);
            if (!UnityValueContract.Finite(vertices[i]))
                throw new ArgumentException(
                    $"Local vertex {i} cannot be transformed to a finite authored-world position.", nameof(localToWorld));
        }

        triangles = new Triangle[triangleIndices.Length / 3];
        for (var i = 0; i < triangles.Length; i++)
        {
            var offset = i * 3;
            var a = triangleIndices[offset];
            var b = triangleIndices[offset + 1];
            var c = triangleIndices[offset + 2];
            var va = vertices[a];
            var vb = vertices[b];
            var vc = vertices[c];
            var center = new Vector3(
                (float)(((double)va.x + vb.x + vc.x) / 3d),
                (float)(((double)va.y + vb.y + vc.y) / 3d),
                (float)(((double)va.z + vb.z + vc.z) / 3d));
            triangles[i] = new Triangle(i, a, b, c, center);
        }

        if (triangles.Length == 0)
        {
            nodes = Array.Empty<Node>();
            return;
        }

        var buffer = new Node[triangles.Length * 2 - 1];
        var nodeCount = 0;
        Build(vertices, triangles, buffer, ref nodeCount, 0, triangles.Length);
        if (nodeCount != buffer.Length) Array.Resize(ref buffer, nodeCount);
        nodes = buffer;
    }

    internal NativeMeshGeometry Transform(Vector3 pivot, Vector3 offset, Quaternion rotation)
    {
        if (!UnityValueContract.Finite(pivot))
            throw new ArgumentException("The transform pivot contains a non-finite value.", nameof(pivot));
        if (!UnityValueContract.Finite(offset))
            throw new ArgumentException("The transform offset contains a non-finite value.", nameof(offset));
        if (!UnityValueContract.Finite(rotation)
            || rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f)
            throw new ArgumentException("The transform rotation is invalid.", nameof(rotation));
        rotation = rotation.normalized;
        var transformed = new Vector3[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
            transformed[i] = pivot + offset + rotation * (vertices[i] - pivot);
        var indices = new int[triangles.Length * 3];
        for (var i = 0; i < triangles.Length; i++)
        {
            var triangle = triangles[i];
            var index = triangle.Order * 3;
            indices[index] = triangle.A;
            indices[index + 1] = triangle.B;
            indices[index + 2] = triangle.C;
        }
        return new NativeMeshGeometry(transformed, indices, Matrix4x4.identity);
    }

    internal Vector3 ClosestPoint(Vector3 query, Vector3 translation)
    {
        if (triangles.Length == 0)
            throw new InvalidOperationException("An empty mesh geometry has no closest point.");
        if (!UnityValueContract.Finite(query))
            throw new ArgumentException("The query contains a non-finite value.", nameof(query));
        if (!UnityValueContract.Finite(translation))
            throw new ArgumentException("The translation contains a non-finite value.", nameof(translation));

        var state = new SearchState(new Point(query));
        Search(0, translation, ref state);
        var result = ToVector3(state.Closest);
        if (!UnityValueContract.Finite(result))
            throw new InvalidOperationException("The translated mesh geometry has no finite closest point.");
        return result;
    }

    internal bool TryVerticalSeparation(NativeMeshGeometry Ground, Vector3 Translation, out float Separation)
    {
        var Maximum = double.NegativeInfinity;
        if (nodes.Length != 0 && Ground.nodes.Length != 0) Compare(0, 0);
        Separation = (float)Maximum;
        if ((double)Separation < Maximum) Separation = NextRepresentableFloat(Separation);
        return !double.IsNegativeInfinity(Maximum);

        void Compare(int ModelIndex, int GroundIndex)
        {
            var ModelNode = nodes[ModelIndex];
            var GroundNode = Ground.nodes[GroundIndex];
            if (ModelNode.Maximum.x + Translation.x < GroundNode.Minimum.x
                || ModelNode.Minimum.x + Translation.x > GroundNode.Maximum.x
                || ModelNode.Maximum.z + Translation.z < GroundNode.Minimum.z
                || ModelNode.Minimum.z + Translation.z > GroundNode.Maximum.z
                || (double)GroundNode.Maximum.y - (ModelNode.Minimum.y + Translation.y) <= Maximum) return;
            if (!ModelNode.IsLeaf)
            {
                Compare(ModelIndex + 1, GroundIndex);
                Compare(ModelNode.StartOrRight, GroundIndex);
                return;
            }
            if (!GroundNode.IsLeaf)
            {
                Compare(ModelIndex, GroundIndex + 1);
                Compare(ModelIndex, GroundNode.StartOrRight);
                return;
            }
            Span<ProjectedPoint> Subject = stackalloc ProjectedPoint[3];
            Span<ProjectedPoint> Intersection = stackalloc ProjectedPoint[MaximumProjectedPolygonVertices];
            for (var i = 0; i < ModelNode.Count; i++)
            {
                var ModelTriangle = triangles[ModelNode.StartOrRight + i];
                var a = Translate(vertices[ModelTriangle.A], Translation);
                var b = Translate(vertices[ModelTriangle.B], Translation);
                var c = Translate(vertices[ModelTriangle.C], Translation);
                if (Cross(b - a, c - a).Y == 0d) continue;
                var ModelProjection = new ProjectedGroundTriangle(ModelTriangle.Order, a, b, c);
                Subject[0] = ModelProjection.A; Subject[1] = ModelProjection.B; Subject[2] = ModelProjection.C;
                for (var j = 0; j < GroundNode.Count; j++)
                {
                    var GroundTriangle = Ground.triangles[GroundNode.StartOrRight + j];
                    var d = new Point(Ground.vertices[GroundTriangle.A]);
                    var e = new Point(Ground.vertices[GroundTriangle.B]);
                    var f = new Point(Ground.vertices[GroundTriangle.C]);
                    if (Cross(e - d, f - d).Y <= 0d) continue;
                    var GroundProjection = new ProjectedGroundTriangle(GroundTriangle.Order, d, e, f);
                    if (!ModelProjection.Overlaps(GroundProjection)) continue;
                    var Count = ClipProjected(Subject, GroundProjection, Intersection);
                    for (var k = 0; k < Count; k++)
                    {
                        var Point = Intersection[k];
                        Maximum = Math.Max(Maximum, GroundProjection.Height(Point) - ModelProjection.Height(Point));
                    }
                }
            }
        }
    }

    internal float GroundableTopEnvelopePenetration(
        NativeMeshGeometry entry,
        Vector3 entryTranslation,
        Vector3 transverse,
        Vector3 forward,
        Vector3 up,
        float clearance,
        out double requiredPenetration)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        if (!UnityValueContract.Finite(entryTranslation))
            throw new ArgumentException("The entry translation contains a non-finite value.", nameof(entryTranslation));
        if (!UnityValueContract.Finite(transverse) || transverse.sqrMagnitude <= 0f)
            throw new ArgumentException("The overlap transverse axis is invalid.", nameof(transverse));
        if (!UnityValueContract.Finite(forward) || forward.sqrMagnitude <= 0f)
            throw new ArgumentException("The overlap forward axis is invalid.", nameof(forward));
        if (!UnityValueContract.Finite(up) || up.sqrMagnitude <= 0f)
            throw new ArgumentException("The overlap up axis is invalid.", nameof(up));
        if (!float.IsFinite(clearance) || clearance < 0f)
            throw new ArgumentOutOfRangeException(nameof(clearance), "The overlap clearance is invalid.");

        transverse.Normalize();
        forward.Normalize();
        up.Normalize();
        var handedness = Vector3.Dot(Vector3.Cross(forward, transverse), up);
        if (!float.IsFinite(handedness) || handedness <= 0f)
            throw new ArgumentException("The overlap basis is degenerate.", nameof(up));
        var projectedTransverse = Normalize(new Point(transverse));
        var projectedForward = Normalize(new Point(forward));
        var projectedUp = Normalize(new Point(up));
        var bridgeTriangles = ProjectGroundableTriangles(
            Vector3.zero, projectedTransverse, projectedForward, projectedUp, up);
        var entryTriangles = entry.ProjectGroundableTriangles(
            entryTranslation, projectedTransverse, projectedForward, projectedUp, up);
        if (bridgeTriangles.Length == 0)
            throw new InvalidOperationException("The Peak Bridge has no groundable projected triangles.");
        if (entryTriangles.Length == 0)
            throw new InvalidOperationException("The selected entry surface has no groundable projected triangles.");

        var bridgeOverlaps = BridgeOverlapPolygons(bridgeTriangles);
        var candidates = new Dictionary<ProjectedPoint, int>();
        var state = new PenetrationState(clearance);
        for (var entryIndex = 0; entryIndex < entryTriangles.Length; entryIndex++)
        {
            var entryTriangle = entryTriangles[entryIndex];
            candidates.Clear();
            for (var bridgeIndex = 0; bridgeIndex < bridgeTriangles.Length; bridgeIndex++)
            {
                var bridgeTriangle = bridgeTriangles[bridgeIndex];
                if (!entryTriangle.Overlaps(bridgeTriangle)) continue;
                AddVertices(
                    IntersectProjected(entryTriangle, bridgeTriangle), bridgeIndex, bridgeTriangles, candidates);
            }
            for (var overlapIndex = 0; overlapIndex < bridgeOverlaps.Count; overlapIndex++)
            {
                var overlap = bridgeOverlaps[overlapIndex];
                if (!overlap.Polygon.Overlaps(entryTriangle)) continue;
                AddVertices(
                    IntersectProjected(overlap.Polygon, entryTriangle),
                    overlap.SeedIndex,
                    bridgeTriangles,
                    candidates);
            }

            foreach (var candidate in candidates)
            {
                var point = candidate.Key;
                TopHeight(
                    bridgeTriangles, candidate.Value, point, out var bridgeHeight, out var bridgeOrder);
                var entryHeight = entryTriangle.Height(point);
                var worldPoint = projectedTransverse * point.X
                    + projectedUp * entryHeight
                    + projectedForward * point.Z;
                state.Consider(
                    entryHeight, bridgeHeight, entryTriangle.Order, bridgeOrder, worldPoint);
            }
        }

        requiredPenetration = state.Found ? Math.Max(0d, state.Penetration) : 0d;
        if (!double.IsFinite(requiredPenetration) || requiredPenetration > float.MaxValue)
            throw new InvalidOperationException("Groundable top-envelope penetration exceeds float range.");
        var result = (float)requiredPenetration;
        if ((double)result < requiredPenetration) result = NextRepresentableFloat(result);
        if (!float.IsFinite(result))
            throw new InvalidOperationException("Groundable top-envelope penetration is not a finite float.");
        return result;
    }

    internal float MinimumGroundableWorldY()
    {
        var Minimum = float.PositiveInfinity;
        foreach (var Triangle in triangles)
        {
            var a = vertices[Triangle.A];
            var b = vertices[Triangle.B];
            var c = vertices[Triangle.C];
            if (Groundable(a, b, c, Vector3.up))
                Minimum = Math.Min(Minimum, Math.Min(a.y, Math.Min(b.y, c.y)));
        }
        if (!float.IsFinite(Minimum))
            throw new InvalidOperationException("Native mesh has no groundable walking surface.");
        return Minimum;
    }

    internal bool TryUniqueGroundableBoundaryPoint(
        Vector3 axis,
        float coordinate,
        Vector3 forward,
        Vector3 up,
        out Vector3 result)
    {
        if (!UnityValueContract.Finite(axis) || axis.sqrMagnitude <= 0f)
            throw new ArgumentException("The boundary section axis is invalid.", nameof(axis));
        if (!float.IsFinite(coordinate))
            throw new ArgumentException("The boundary section coordinate is non-finite.", nameof(coordinate));
        if (!UnityValueContract.Finite(forward) || forward.sqrMagnitude <= 0f)
            throw new ArgumentException("The boundary forward axis is invalid.", nameof(forward));
        if (!UnityValueContract.Finite(up) || up.sqrMagnitude <= 0f)
            throw new ArgumentException("The boundary up axis is invalid.", nameof(up));

        var normal = new Point(axis);
        var state = new GroundBoundaryState(Normalize(new Point(forward)), Normalize(new Point(up)));
        foreach (var triangle in triangles)
        {
            var av = vertices[triangle.A];
            var bv = vertices[triangle.B];
            var cv = vertices[triangle.C];
            if (!Groundable(av, bv, cv, up)) continue;
            var a = new Point(av);
            var b = new Point(bv);
            var c = new Point(cv);
            var aDistance = Dot(a, normal) - coordinate;
            var bDistance = Dot(b, normal) - coordinate;
            var cDistance = Dot(c, normal) - coordinate;
            ConsiderGroundBoundaryEdge(a, aDistance, b, bDistance, triangle.Order, ref state);
            ConsiderGroundBoundaryEdge(b, bDistance, c, cDistance, triangle.Order, ref state);
            ConsiderGroundBoundaryEdge(c, cDistance, a, aDistance, triangle.Order, ref state);
        }
        result = state.Found && !state.Ambiguous ? ToVector3(state.Selected) : default;
        return state.Found && !state.Ambiguous;
    }

    internal bool TryUniqueNearestGroundableIntersection(
        Vector3 linePoint,
        Vector3 lineDirection,
        Vector3 up,
        out Vector3 result)
    {
        if (!UnityValueContract.Finite(linePoint))
            throw new ArgumentException("The line point contains a non-finite value.", nameof(linePoint));
        if (!UnityValueContract.Finite(lineDirection) || lineDirection.sqrMagnitude <= 0f)
            throw new ArgumentException("The line direction is invalid.", nameof(lineDirection));
        if (!UnityValueContract.Finite(up) || up.sqrMagnitude <= 0f)
            throw new ArgumentException("The ground up axis is invalid.", nameof(up));

        var origin = new Point(linePoint);
        var direction = Normalize(new Point(lineDirection));
        var state = new GroundLineState(origin);
        foreach (var triangle in triangles)
        {
            var a = vertices[triangle.A];
            var b = vertices[triangle.B];
            var c = vertices[triangle.C];
            if (!Groundable(a, b, c, up)
                || !TryLineTriangle(origin, direction, new Point(a), new Point(b), new Point(c), out var point)) continue;
            state.Consider(point, triangle.Order);
        }
        result = state.Found && !state.Ambiguous ? ToVector3(state.Selected) : default;
        return state.Found && !state.Ambiguous;
    }

    internal bool TryGroundableEntryPoint(
        Vector3 axis,
        float coordinate,
        Vector3 forward,
        Vector3 up,
        out Vector3 result)
    {
        if (!UnityValueContract.Finite(axis) || axis.sqrMagnitude <= 0f)
            throw new ArgumentException("The section axis is invalid.", nameof(axis));
        if (!float.IsFinite(coordinate)) throw new ArgumentException("The section coordinate is non-finite.", nameof(coordinate));
        if (!UnityValueContract.Finite(forward) || forward.sqrMagnitude <= 0f)
            throw new ArgumentException("The section forward axis is invalid.", nameof(forward));
        if (!UnityValueContract.Finite(up) || up.sqrMagnitude <= 0f)
            throw new ArgumentException("The section up axis is invalid.", nameof(up));

        var normal = new Point(axis);
        var state = new GroundSectionState(Normalize(new Point(forward)), Normalize(new Point(up)));
        foreach (var triangle in triangles)
        {
            var av = vertices[triangle.A];
            var bv = vertices[triangle.B];
            var cv = vertices[triangle.C];
            if (!Groundable(av, bv, cv, up)) continue;
            var a = new Point(av);
            var b = new Point(bv);
            var c = new Point(cv);
            var aDistance = Dot(a, normal) - coordinate;
            var bDistance = Dot(b, normal) - coordinate;
            var cDistance = Dot(c, normal) - coordinate;
            ConsiderGroundSectionEdge(a, aDistance, b, bDistance, triangle.Order, ref state);
            ConsiderGroundSectionEdge(b, bDistance, c, cDistance, triangle.Order, ref state);
            ConsiderGroundSectionEdge(c, cDistance, a, aDistance, triangle.Order, ref state);
        }
        result = state.Found ? ToVector3(state.Selected) : default;
        return state.Found;
    }

    ProjectedGroundTriangle[] ProjectGroundableTriangles(
        Vector3 translation,
        Point transverse,
        Point forward,
        Point up,
        Vector3 groundUp)
    {
        var count = 0;
        for (var i = 0; i < triangles.Length; i++)
        {
            var triangle = triangles[i];
            if (Groundable(
                vertices[triangle.A], vertices[triangle.B], vertices[triangle.C], groundUp)) count++;
        }

        var result = new ProjectedGroundTriangle[count];
        var index = 0;
        for (var i = 0; i < triangles.Length; i++)
        {
            var triangle = triangles[i];
            if (!Groundable(
                    vertices[triangle.A], vertices[triangle.B], vertices[triangle.C], groundUp)) continue;
            result[index++] = new ProjectedGroundTriangle(
                triangle.Order,
                Project(vertices[triangle.A], translation, transverse, forward, up),
                Project(vertices[triangle.B], translation, transverse, forward, up),
                Project(vertices[triangle.C], translation, transverse, forward, up));
        }
        Array.Sort(result, static (left, right) => left.Order.CompareTo(right.Order));
        return result;
    }

    static List<BridgeOverlap> BridgeOverlapPolygons(
        ProjectedGroundTriangle[] bridgeTriangles)
    {
        var result = new List<BridgeOverlap>();
        for (var leftIndex = 0; leftIndex < bridgeTriangles.Length; leftIndex++)
        {
            var left = bridgeTriangles[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < bridgeTriangles.Length; rightIndex++)
            {
                var right = bridgeTriangles[rightIndex];
                if (!left.Overlaps(right)) continue;
                var overlap = IntersectProjected(left, right);
                if (!overlap.HasArea) continue;
                var minimumDifference = double.PositiveInfinity;
                var maximumDifference = double.NegativeInfinity;
                for (var pointIndex = 0; pointIndex < overlap.Vertices.Length; pointIndex++)
                {
                    var point = overlap.Vertices[pointIndex];
                    var difference = left.Height(point) - right.Height(point);
                    if (!double.IsFinite(difference))
                        throw new InvalidOperationException("Peak Bridge overlap height is non-finite.");
                    minimumDifference = Math.Min(minimumDifference, difference);
                    maximumDifference = Math.Max(maximumDifference, difference);
                }
                if (minimumDifference < 0d && maximumDifference > 0d)
                    throw new InvalidOperationException(
                        "Peak Bridge groundable triangle height order crosses within projected overlap.");
                result.Add(new BridgeOverlap(overlap, leftIndex));
            }
        }
        return result;
    }

    static ProjectedPolygon IntersectProjected(
        ProjectedGroundTriangle left,
        ProjectedGroundTriangle right)
    {
        Span<ProjectedPoint> subject = stackalloc ProjectedPoint[3];
        subject[0] = left.A;
        subject[1] = left.B;
        subject[2] = left.C;
        return ClipProjected(subject, right);
    }

    static ProjectedPolygon IntersectProjected(
        ProjectedPolygon left,
        ProjectedGroundTriangle right) => ClipProjected(left.Vertices, right);

    static ProjectedPolygon ClipProjected(ReadOnlySpan<ProjectedPoint> Subject, ProjectedGroundTriangle Clip)
    {
        Span<ProjectedPoint> Vertices = stackalloc ProjectedPoint[MaximumProjectedPolygonVertices];
        return CanonicalProjectedPolygon(Vertices, ClipProjected(Subject, Clip, Vertices));
    }

    static int ClipProjected(ReadOnlySpan<ProjectedPoint> Subject, ProjectedGroundTriangle Clip, Span<ProjectedPoint> Result)
    {
        Span<ProjectedPoint> First = stackalloc ProjectedPoint[MaximumProjectedPolygonVertices];
        Span<ProjectedPoint> Second = stackalloc ProjectedPoint[MaximumProjectedPolygonVertices];
        Subject.CopyTo(First);
        var Source = First;
        var Destination = Second;
        var Count = Subject.Length;
        for (var Edge = 0; Edge < 3 && Count != 0; Edge++)
        {
            var Start = Edge == 0 ? Clip.A : Edge == 1 ? Clip.B : Clip.C;
            var End = Edge == 0 ? Clip.B : Edge == 1 ? Clip.C : Clip.A;
            Count = ClipProjectedEdge(Source[..Count], Start, End, Destination);
            var Swap = Source;
            Source = Destination;
            Destination = Swap;
        }
        Source[..Count].CopyTo(Result);
        return Count;
    }

    static int ClipProjectedEdge(
        ReadOnlySpan<ProjectedPoint> source,
        ProjectedPoint edgeStart,
        ProjectedPoint edgeEnd,
        Span<ProjectedPoint> result)
    {
        var count = 0;
        var previous = source[source.Length - 1];
        var previousDistance = ProjectedSide(edgeStart, edgeEnd, previous);
        var previousInside = previousDistance >= 0d;
        for (var i = 0; i < source.Length; i++)
        {
            var point = source[i];
            var distance = ProjectedSide(edgeStart, edgeEnd, point);
            var inside = distance >= 0d;
            if (inside)
            {
                if (!previousInside)
                    result[count++] = previous
                        + (point - previous) * (previousDistance / (previousDistance - distance));
                result[count++] = point;
            }
            else if (previousInside)
                result[count++] = previous
                    + (point - previous) * (previousDistance / (previousDistance - distance));
            previous = point;
            previousDistance = distance;
            previousInside = inside;
        }
        return count;
    }

    static ProjectedPolygon CanonicalProjectedPolygon(
        Span<ProjectedPoint> points,
        int count)
    {
        var write = 0;
        for (var i = 0; i < count; i++)
            if (write == 0 || !points[i].Equals(points[write - 1])) points[write++] = points[i];
        if (write > 1 && points[0].Equals(points[write - 1])) write--;

        var changed = true;
        while (changed && write >= 3)
        {
            changed = false;
            for (var i = 0; i < write; i++)
            {
                var previous = points[(i + write - 1) % write];
                var point = points[i];
                var next = points[(i + 1) % write];
                if (ProjectedSide(previous, point, next) != 0d
                    || ProjectedDot(point - previous, point - next) > 0d) continue;
                for (var move = i; move < write - 1; move++) points[move] = points[move + 1];
                write--;
                changed = true;
                break;
            }
        }

        for (var i = 0; i < write; i++)
            if (!double.IsFinite(points[i].X) || !double.IsFinite(points[i].Z))
                throw new InvalidOperationException("Projected mesh intersection is non-finite.");
        var signedArea = ProjectedSignedArea(points[..write]);
        if (!double.IsFinite(signedArea))
            throw new InvalidOperationException("Projected mesh intersection area is non-finite.");
        if (signedArea < 0d)
        {
            points[..write].Reverse();
            signedArea = -signedArea;
        }
        var vertices = new ProjectedPoint[write];
        points[..write].CopyTo(vertices);
        return new ProjectedPolygon(vertices, signedArea);
    }

    static double ProjectedSignedArea(ReadOnlySpan<ProjectedPoint> polygon)
    {
        if (polygon.Length < 3) return 0d;
        var area = 0d;
        var previous = polygon[polygon.Length - 1];
        for (var i = 0; i < polygon.Length; i++)
        {
            var point = polygon[i];
            area += previous.X * point.Z - point.X * previous.Z;
            previous = point;
        }
        return area * 0.5d;
    }

    static void AddVertices(
        ProjectedPolygon polygon,
        int seedIndex,
        ProjectedGroundTriangle[] bridgeTriangles,
        Dictionary<ProjectedPoint, int> result)
    {
        for (var i = 0; i < polygon.Vertices.Length; i++)
        {
            var point = polygon.Vertices[i];
            if (!result.TryGetValue(point, out var previous)
                || bridgeTriangles[seedIndex].Order < bridgeTriangles[previous].Order)
                result[point] = seedIndex;
        }
    }

    static void TopHeight(
        ProjectedGroundTriangle[] triangles,
        int seedIndex,
        ProjectedPoint point,
        out double height,
        out int order)
    {
        height = triangles[seedIndex].Height(point);
        order = triangles[seedIndex].Order;
        for (var i = 0; i < triangles.Length; i++)
        {
            if (i == seedIndex || !triangles[i].Contains(point)) continue;
            var candidate = triangles[i].Height(point);
            if (!double.IsFinite(candidate))
                throw new InvalidOperationException("Peak Bridge top-envelope height is non-finite.");
            if (candidate > height || candidate == height && triangles[i].Order < order)
            {
                height = candidate;
                order = triangles[i].Order;
            }
        }
    }

    static Point Project(
        Vector3 point,
        Vector3 translation,
        Point transverse,
        Point forward,
        Point up)
    {
        var translated = Translate(point, translation);
        return new Point(
            Dot(translated, transverse),
            Dot(translated, up),
            Dot(translated, forward));
    }

    static double ProjectedSide(
        ProjectedPoint start,
        ProjectedPoint end,
        ProjectedPoint point) =>
        (end.X - start.X) * (point.Z - start.Z)
        - (end.Z - start.Z) * (point.X - start.X);

    static double ProjectedDot(ProjectedPoint left, ProjectedPoint right) =>
        left.X * right.X + left.Z * right.Z;

    static float NextRepresentableFloat(float Value) => Value == 0f ? float.Epsilon
        : BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(Value) + (Value > 0f ? 1 : -1));

    static int Build(
        Vector3[] vertices,
        Triangle[] triangles,
        Node[] nodes,
        ref int nodeCount,
        int start,
        int count)
    {
        var nodeIndex = nodeCount++;
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var centerMinimum = minimum;
        var centerMaximum = maximum;
        for (var i = start; i < start + count; i++)
        {
            var triangle = triangles[i];
            var a = vertices[triangle.A];
            var b = vertices[triangle.B];
            var c = vertices[triangle.C];
            minimum = Vector3.Min(minimum, Vector3.Min(a, Vector3.Min(b, c)));
            maximum = Vector3.Max(maximum, Vector3.Max(a, Vector3.Max(b, c)));
            centerMinimum = Vector3.Min(centerMinimum, triangle.Center);
            centerMaximum = Vector3.Max(centerMaximum, triangle.Center);
        }

        if (count <= LeafSize)
        {
            nodes[nodeIndex] = new Node(minimum, maximum, start, count);
            return nodeIndex;
        }

        var x = (double)centerMaximum.x - centerMinimum.x;
        var y = (double)centerMaximum.y - centerMinimum.y;
        var z = (double)centerMaximum.z - centerMinimum.z;
        var axis = x >= y && x >= z ? 0 : y >= z ? 1 : 2;
        Array.Sort(triangles, start, count, axis == 0 ? XComparer : axis == 1 ? YComparer : ZComparer);
        var leftCount = count / 2;
        Build(vertices, triangles, nodes, ref nodeCount, start, leftCount);
        var right = Build(vertices, triangles, nodes, ref nodeCount, start + leftCount, count - leftCount);
        nodes[nodeIndex] = new Node(minimum, maximum, right, 0);
        return nodeIndex;
    }

    void Search(int nodeIndex, Vector3 translation, ref SearchState state)
    {
        var node = nodes[nodeIndex];
        if (node.IsLeaf)
        {
            for (var i = node.StartOrRight; i < node.StartOrRight + node.Count; i++)
            {
                var triangle = triangles[i];
                ConsiderTriangle(
                    state.Query,
                    Translate(vertices[triangle.A], translation),
                    Translate(vertices[triangle.B], translation),
                    Translate(vertices[triangle.C], translation),
                    ref state);
            }
            return;
        }

        var left = nodeIndex + 1;
        var right = node.StartOrRight;
        var leftDistance = BoundsSquaredDistance(nodes[left], translation, state.Query);
        var rightDistance = BoundsSquaredDistance(nodes[right], translation, state.Query);
        if (leftDistance <= rightDistance)
        {
            if (leftDistance <= state.SquaredDistance) Search(left, translation, ref state);
            if (rightDistance <= state.SquaredDistance) Search(right, translation, ref state);
        }
        else
        {
            if (rightDistance <= state.SquaredDistance) Search(right, translation, ref state);
            if (leftDistance <= state.SquaredDistance) Search(left, translation, ref state);
        }
    }

    static bool Groundable(Vector3 a, Vector3 b, Vector3 c, Vector3 up)
    {
        var normal = Vector3.Cross(b - a, c - a);
        return normal.sqrMagnitude > 0f && Vector3.Angle(normal, up) < MaximumGroundAngle;
    }

    static bool TryLineTriangle(
        Point origin,
        Point direction,
        Point a,
        Point b,
        Point c,
        out Point result)
    {
        var ab = b - a;
        var ac = c - a;
        var normal = Cross(ab, ac);
        var normalSquared = Dot(normal, normal);
        var denominator = Dot(direction, normal);
        if (normalSquared == 0d || denominator == 0d)
        {
            result = default;
            return false;
        }
        result = origin + direction * (Dot(a - origin, normal) / denominator);
        return InsideTriangle(result, a, ab, ac, normal, normalSquared);
    }

    static void ConsiderGroundBoundaryEdge(
        Point a,
        double aDistance,
        Point b,
        double bDistance,
        int triangleOrder,
        ref GroundBoundaryState state)
    {
        if (aDistance == 0d) state.Consider(a, triangleOrder);
        if (bDistance == 0d) state.Consider(b, triangleOrder);
        if (aDistance < 0d && bDistance > 0d || aDistance > 0d && bDistance < 0d)
            state.Consider(a + (b - a) * (aDistance / (aDistance - bDistance)), triangleOrder);
    }

    static void ConsiderGroundSectionEdge(
        Point a,
        double aDistance,
        Point b,
        double bDistance,
        int triangleOrder,
        ref GroundSectionState state)
    {
        if (aDistance == 0d) state.Consider(a, triangleOrder);
        if (bDistance == 0d) state.Consider(b, triangleOrder);
        if (aDistance < 0d && bDistance > 0d || aDistance > 0d && bDistance < 0d)
            state.Consider(a + (b - a) * (aDistance / (aDistance - bDistance)), triangleOrder);
    }

    static void ConsiderTriangle(Point query, Point a, Point b, Point c, ref SearchState state)
    {
        var ab = b - a;
        var ac = c - a;
        var normal = Cross(ab, ac);
        var normalSquared = Dot(normal, normal);
        if (normalSquared > 0d)
        {
            var projected = query - normal * (Dot(query - a, normal) / normalSquared);
            var offset = projected - a;
            var v = Dot(Cross(offset, ac), normal) / normalSquared;
            var w = Dot(Cross(ab, offset), normal) / normalSquared;
            if (v >= 0d && w >= 0d && v + w <= 1d)
            {
                state.Consider(projected);
                return;
            }
        }

        state.Consider(ClosestOnSegment(query, a, b));
        state.Consider(ClosestOnSegment(query, b, c));
        state.Consider(ClosestOnSegment(query, c, a));
    }

    static bool InsideTriangle(
        Point point,
        Point a,
        Point ab,
        Point ac,
        Point normal,
        double normalSquared)
    {
        var offset = point - a;
        var v = Dot(Cross(offset, ac), normal);
        var w = Dot(Cross(ab, offset), normal);
        return v >= 0d && w >= 0d && v + w <= normalSquared;
    }

    static Point ClosestOnSegment(Point query, Point start, Point end)
    {
        var direction = end - start;
        var squaredLength = Dot(direction, direction);
        if (squaredLength == 0d) return start;
        var amount = Dot(query - start, direction) / squaredLength;
        if (amount <= 0d) return start;
        if (amount >= 1d) return end;
        return start + direction * amount;
    }

    static double BoundsSquaredDistance(Node node, Vector3 translation, Point point)
    {
        var minimum = TranslateVector(node.Minimum, translation);
        var maximum = TranslateVector(node.Maximum, translation);
        var x = AxisDistance(point.X, minimum.x, maximum.x);
        var y = AxisDistance(point.Y, minimum.y, maximum.y);
        var z = AxisDistance(point.Z, minimum.z, maximum.z);
        return x * x + y * y + z * z;
    }

    static double AxisDistance(double value, float minimum, float maximum)
    {
        if (value < minimum) return minimum - value;
        return value > maximum ? value - maximum : 0d;
    }

    static int ComparePoint(Point left, Point right)
    {
        var comparison = left.X.CompareTo(right.X);
        if (comparison != 0) return comparison;
        comparison = left.Y.CompareTo(right.Y);
        return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
    }

    static Vector3 ToVector3(Point point) => new((float)point.X, (float)point.Y, (float)point.Z);

    static Point Translate(Vector3 point, Vector3 translation) => new(TranslateVector(point, translation));

    static Vector3 TranslateVector(Vector3 point, Vector3 translation) => new(
        point.x + translation.x,
        point.y + translation.y,
        point.z + translation.z);

    static Point Cross(Point left, Point right) => new(
        left.Y * right.Z - left.Z * right.Y,
        left.Z * right.X - left.X * right.Z,
        left.X * right.Y - left.Y * right.X);

    static Point Normalize(Point value)
    {
        var length = Math.Sqrt(Dot(value, value));
        if (length == 0d || double.IsNaN(length) || double.IsInfinity(length))
            throw new ArgumentException("The vector cannot be normalized.", nameof(value));
        return value * (1d / length);
    }

    static double Dot(Point left, Point right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    static bool Finite(Matrix4x4 value)
    {
        for (var i = 0; i < 16; i++)
            if (!float.IsFinite(value[i])) return false;
        return true;
    }
}
