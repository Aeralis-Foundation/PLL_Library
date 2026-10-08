#nullable enable
using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PLL.Creator.Editor
{
    internal sealed class PllPropInteractionCompiler
    {
        readonly string Directory;
        readonly Dictionary<(Mesh Source, Vector3 Start, Vector3 End, Vector3 Up), Mesh> Spans = new();
        internal PllPropInteractionCompiler(string Directory) => this.Directory = Directory;

        internal void Prepare(GameObject StagedRoot)
        {
            var Roots = new HashSet<Transform>();
            foreach (var Component in StagedRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (Component is not PllBreakablePropAuthoring and not PllSwingingPropAuthoring and not PllVineAuthoring
                    || !Roots.Add(Component.transform)) continue;
                var Root = Component.transform;
                var Break = Root.GetComponent<PllBreakablePropAuthoring>();
                var Motion = Root.GetComponent<PllSwingingPropAuthoring>();
                var Vine = Root.GetComponent<PllVineAuthoring>();
                if (PllPropInteractionValueContract.Failure(Root, Break, Motion, Vine) is { } Failure)
                    throw new InvalidOperationException(Failure);
                if (!Vine || Vine.Appearance != PllVineAppearance.CustomSpan) continue;
                if (Vine.BundleSpanVersion == PllVineAuthoring.SpanNormalizationVersion
                    && Spans.ContainsValue(Vine.SpanMesh!)) continue;
                var Key = (Vine.SpanMesh!, Vine.ModelStart, Vine.ModelEnd, Vine.ModelUp);
                if (!Spans.TryGetValue(Key, out var Span))
                {
                    Span = Normalize(Vine.SpanMesh!, Vine.ModelStart, Vine.ModelEnd, Vine.ModelUp);
                    try
                    {
                        AssetDatabase.CreateAsset(Span, Directory + "/Span_" + Spans.Count + ".asset");
                        Spans.Add(Key, Span);
                    }
                    catch
                    {
                        if (!EditorUtility.IsPersistent(Span)) Object.DestroyImmediate(Span);
                        throw;
                    }
                }
                Vine.SealSpanMesh(Span, Vector3.Distance(Vine.ModelStart, Vine.ModelEnd));
                EditorUtility.SetDirty(Vine);
            }
        }

        internal static Mesh Normalize(Mesh Source, Vector3 Start, Vector3 End, Vector3 Up)
        {
            var Frame = Quaternion.Inverse(Quaternion.LookRotation(End - Start, Up));
            var Vertices = Source.vertices;
            if (Vertices.Length == 0) throw new InvalidOperationException("Custom span mesh has no vertices.");
            for (var i = 0; i < Vertices.Length; i++)
            {
                if (!Finite(Vertices[i])) throw new InvalidOperationException("Custom span mesh contains a nonfinite vertex.");
                Vertices[i] = Frame * (Vertices[i] - Start);
                if (!Finite(Vertices[i])) throw new InvalidOperationException("Normalized span exceeds the supported coordinate range.");
            }
            var Normals = Source.normals;
            for (var i = 0; i < Normals.Length; i++)
            {
                if (!Finite(Normals[i])) throw new InvalidOperationException("Custom span mesh contains a nonfinite normal.");
                Normals[i] = Frame * Normals[i];
            }
            var Tangents = Source.tangents;
            for (var i = 0; i < Tangents.Length; i++)
            {
                var Direction = new Vector3(Tangents[i].x, Tangents[i].y, Tangents[i].z);
                if (!Finite(Direction) || !PllAuthoringValueContract.CanEncode(Tangents[i].w))
                    throw new InvalidOperationException("Custom span mesh contains a nonfinite tangent.");
                Direction = Frame * Direction;
                Tangents[i] = new Vector4(Direction.x, Direction.y, Direction.z, Tangents[i].w);
            }
            var Mesh = Object.Instantiate(Source);
            Mesh.name = "PLL Span v" + PllVineAuthoring.SpanNormalizationVersion;
            Mesh.vertices = Vertices; Mesh.normals = Normals; Mesh.tangents = Tangents;
            Mesh.RecalculateBounds(); return Mesh;
        }
        static bool Finite(Vector3 Value) => PllAuthoringValueContract.CanEncode(Value.x)
            && PllAuthoringValueContract.CanEncode(Value.y) && PllAuthoringValueContract.CanEncode(Value.z);
    }
}
