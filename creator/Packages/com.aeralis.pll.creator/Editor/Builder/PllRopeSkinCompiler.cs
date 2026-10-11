using System;
using PLL.Library;
using UnityEngine;
using UnityEngine.Rendering;

namespace PLL.Creator.Editor
{
    internal static class PllRopeSkinCompiler
    {
        internal static Mesh Bake(Mesh Source, Vector3 RestStart, Vector3 RestEnd, NativeContentRig Rig)
        {
            if (Rig.Actor != "RopeDynamic" || Rig.Skins.Length != 1)
                throw new InvalidOperationException("Automatic Rope skinning requires one captured RopeDynamic skin.");
            var Skin = Rig.Skins[0];
            if (!Source || !Source.isReadable || Source.vertexCount == 0 || Source.subMeshCount != 1
                || Source.GetTopology(0) is not (MeshTopology.Triangles or MeshTopology.Quads)
                || Source.blendShapeCount != 0 || Source.bindposes.Length != 0
                || Source.GetAllBoneWeights().Length != 0
                || !PllAuthoringValueContract.CanEncode(RestStart) || !PllAuthoringValueContract.CanEncode(RestEnd)
                || (RestEnd - RestStart).sqrMagnitude == 0f)
                throw new InvalidOperationException("Automatic Rope skinning needs a readable unskinned rest mesh with distinct finite endpoints.");
            if (Skin.BonePaths.Length != PllHangingRopeAuthoring.MaximumSegments
                || Skin.Bindposes.Length != Skin.BonePaths.Length * 16 || Skin.BlendShapeCount != 0)
                throw new InvalidOperationException("The captured Rope skin does not match the supported native layout.");
            var Bindposes = new Matrix4x4[Skin.BonePaths.Length];
            var Positions = new Vector3[Bindposes.Length];
            for (var i = 0; i < Bindposes.Length; i++)
            {
                for (var j = 0; j < 16; j++) Bindposes[i][j] = Skin.Bindposes[i * 16 + j];
                Positions[i] = Bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero);
                if (!PllAuthoringValueContract.CanEncode(Positions[i])) throw new InvalidOperationException("The captured Rope bindposes have an invalid rest frame.");
            }
            var Span = Positions[Positions.Length - 1] - Positions[0];
            if (Span.sqrMagnitude == 0f)
                throw new InvalidOperationException("The captured Rope bones have no longitudinal span.");
            var Coordinates = new float[Positions.Length];
            for (var i = 0; i < Positions.Length; i++)
            {
                Coordinates[i] = Vector3.Dot(Positions[i] - Positions[0], Span) / Span.sqrMagnitude;
                if (i != 0 && Coordinates[i] <= Coordinates[i - 1]
                    || (Positions[i] - Positions[0] - Span * Coordinates[i]).sqrMagnitude > Span.sqrMagnitude * 0.00000001f)
                    throw new InvalidOperationException("The captured Rope bones are not a straight ordered rest layout. Use Compatible Skin.");
            }
            var ArtistSpan = RestEnd - RestStart;
            var Rotation = Quaternion.FromToRotation(ArtistSpan, Span);
            var Scale = Span.magnitude / ArtistSpan.magnitude;
            var Vertices = Source.vertices; var Weights = new BoneWeight[Vertices.Length];
            var Minimum = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var Maximum = -Minimum;
            for (var i = 0; i < Vertices.Length; i++)
            {
                var Coordinate = Vector3.Dot(Vertices[i] - RestStart, ArtistSpan) / ArtistSpan.sqrMagnitude;
                if (!PllAuthoringValueContract.CanEncode(Vertices[i]) || Coordinate < 0f || Coordinate > 1f)
                    throw new InvalidOperationException("The Rope mesh has a vertex outside its declared rest span. Adjust Rest Start and Rest End.");
                var Right = Array.BinarySearch(Coordinates, Coordinate);
                if (Right < 0) Right = ~Right;
                if (Right == 0) Weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                else
                {
                    var Left = Right - 1;
                    var Blend = (Coordinate - Coordinates[Left]) / (Coordinates[Right] - Coordinates[Left]);
                    Weights[i] = new BoneWeight { boneIndex0 = Left, boneIndex1 = Right, weight0 = 1f - Blend, weight1 = Blend };
                }
                Vertices[i] = Positions[0] + Rotation * (Vertices[i] - RestStart) * Scale;
                if (!PllAuthoringValueContract.CanEncode(Vertices[i]))
                    throw new InvalidOperationException("The Rope skin has a nonfinite transformed vertex.");
                Minimum = Vector3.Min(Minimum, Vertices[i]); Maximum = Vector3.Max(Maximum, Vertices[i]);
            }
            var Center = new Vector3((float)(((double)Minimum.x + Maximum.x) * 0.5),
                (float)(((double)Minimum.y + Maximum.y) * 0.5), (float)(((double)Minimum.z + Maximum.z) * 0.5));
            var Bounds = new Bounds
            {
                center = Center,
                extents = new Vector3(EnclosingExtent(Center.x, Minimum.x, Maximum.x),
                    EnclosingExtent(Center.y, Minimum.y, Maximum.y), EnclosingExtent(Center.z, Minimum.z, Maximum.z))
            };
            var Normals = Source.normals;
            for (var i = 0; i < Normals.Length; i++)
            {
                Normals[i] = Rotation * Normals[i];
                if (!PllAuthoringValueContract.CanEncode(Normals[i]))
                    throw new InvalidOperationException("The Rope mesh contains a nonfinite or out-of-range normal.");
            }
            var Tangents = Source.tangents;
            for (var i = 0; i < Tangents.Length; i++)
            {
                var Direction = Rotation * new Vector3(Tangents[i].x, Tangents[i].y, Tangents[i].z);
                if (!PllAuthoringValueContract.CanEncode(Direction) || !PllAuthoringValueContract.CanEncode(Tangents[i].w))
                    throw new InvalidOperationException("The Rope mesh contains a nonfinite or out-of-range tangent.");
                Tangents[i] = new Vector4(Direction.x, Direction.y, Direction.z, Tangents[i].w);
            }
            var Mesh = UnityEngine.Object.Instantiate(Source);
            Mesh.name = Source.name + "_PLL_RopeSkin";
            Mesh.vertices = Vertices; Mesh.bindposes = Bindposes; Mesh.boneWeights = Weights;
            Mesh.normals = Normals; Mesh.tangents = Tangents; Mesh.bounds = Bounds;
            return Mesh;
        }

        static float EnclosingExtent(float Center, float Minimum, float Maximum)
        {
            var Required = Math.Max((double)Center - Minimum, (double)Maximum - Center);
            var Extent = (float)Required;
            if (Extent < Required || Center - Extent > Minimum || Center + Extent < Maximum)
                Extent = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(Extent) + 1);
            return Extent;
        }
    }
}
