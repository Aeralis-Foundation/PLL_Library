using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class TransformBinding
{
    readonly int[] Path;
    readonly Pose[] LocalPoses;
    readonly Vector3[] LocalScales;

    TransformBinding(Transform Source, int[] Path, Pose[] LocalPoses, Vector3[] LocalScales)
    {
        this.Source = Source;
        this.Path = Path;
        this.LocalPoses = LocalPoses;
        this.LocalScales = LocalScales;
    }

    internal Transform Source { get; }

    internal static TransformBinding Create(Transform Root, Transform Target)
    {
        if (!Root) throw new ArgumentException("The template transform was destroyed.", nameof(Root));
        if (!Target) throw new ArgumentException("The referenced transform was destroyed.", nameof(Target));
        var Nodes = new List<Transform>();
        for (var Current = Target; Current != Root; Current = Current.parent)
        {
            if (!Current || !Current.parent)
                throw new ArgumentException("The target is not owned by the template.", nameof(Target));
            Nodes.Add(Current);
        }
        var Indices = new int[Nodes.Count];
        var Poses = new Pose[Nodes.Count];
        var Scales = new Vector3[Nodes.Count];
        for (var i = 0; i < Nodes.Count; i++)
        {
            var Node = Nodes[Nodes.Count - 1 - i];
            var Pose = new Pose(Node.localPosition, Node.localRotation);
            var Scale = Node.localScale;
            if (!ManifestEncoding.CanEncode(Pose.position.x) || !ManifestEncoding.CanEncode(Pose.position.y) || !ManifestEncoding.CanEncode(Pose.position.z)
                || !ManifestEncoding.CanEncode(Pose.rotation.x) || !ManifestEncoding.CanEncode(Pose.rotation.y)
                || !ManifestEncoding.CanEncode(Pose.rotation.z) || !ManifestEncoding.CanEncode(Pose.rotation.w)
                || Pose.rotation.x == 0f && Pose.rotation.y == 0f
                && Pose.rotation.z == 0f && Pose.rotation.w == 0f
                || !ManifestEncoding.CanEncode(Scale.x) || !ManifestEncoding.CanEncode(Scale.y) || !ManifestEncoding.CanEncode(Scale.z))
                throw new ArgumentException("A referenced transform path must have finite local pose and scale values within the manifest range.", nameof(Target));
            Indices[i] = Node.GetSiblingIndex();
            Poses[i] = Pose;
            Scales[i] = Scale;
        }
        return new TransformBinding(Target, Indices, Poses, Scales);
    }

    internal Transform Resolve(Transform Root)
    {
        if (!Root) throw new InvalidOperationException("A registered segment clone root was destroyed.");
        var Current = Root;
        foreach (var Index in Path)
        {
            if (!Current || Index < 0 || Index >= Current.childCount)
                throw new InvalidOperationException("A registered segment clone does not match its template hierarchy.");
            Current = Current.GetChild(Index);
        }
        if (!Current) throw new InvalidOperationException("A registered segment clone contains a destroyed transform.");
        return Current;
    }

    internal void Validate(Transform Root, string Id, string Name)
    {
        if (!Root || !Source)
            throw new InvalidOperationException($"Registered segment '{Id}' contains a destroyed {Name} transform.");
        var Current = ResolveValidated(Root, Id, Name);
        if (Current != Source)
            throw new InvalidOperationException($"Registered segment '{Id}' replaced its {Name} transform before sealing.");
    }

    internal Transform ResolveValidated(Transform Root, string Id, string Name)
    {
        if (!Root)
            throw new InvalidOperationException($"Registered segment '{Id}' contains a destroyed {Name} root.");
        var Current = Root;
        for (var i = 0; i < Path.Length; i++)
        {
            if (!Current || Path[i] < 0 || Path[i] >= Current.childCount)
                throw new InvalidOperationException($"Registered segment '{Id}' changed its {Name} hierarchy.");
            Current = Current.GetChild(Path[i]);
            if (!Current || !Current.localPosition.Equals(LocalPoses[i].position)
                || !Current.localRotation.Equals(LocalPoses[i].rotation)
                || !Current.localScale.Equals(LocalScales[i]))
                throw new InvalidOperationException($"Registered segment '{Id}' changed its {Name} transform path.");
        }
        return Current;
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Path.Length);
        for (var i = 0; i < Path.Length; i++)
        {
            Writer.Write(Path[i]);
            ManifestEncoding.WritePose(Writer, LocalPoses[i]);
            ManifestEncoding.WriteVector(Writer, LocalScales[i]);
        }
    }

    internal void ProjectPoses(string NamedPath, Dictionary<string, NativeContentTransform> Transforms)
    {
        if (NamedPath == ".") return;
        var Names = NamedPath.Split('/');
        var CurrentPath = "";
        for (var i = 0; i < Names.Length; i++)
        {
            CurrentPath = CurrentPath.Length == 0 ? Names[i] : CurrentPath + "/" + Names[i];
            if (Transforms.ContainsKey(CurrentPath)) continue;
            var Position = LocalPoses[i].position; var Rotation = LocalPoses[i].rotation;
            var Scale = LocalScales[i];
            Transforms.Add(CurrentPath, new NativeContentTransform
            {
                Path = CurrentPath,
                Position = new[] { Position.x, Position.y, Position.z },
                Rotation = new[] { Rotation.x, Rotation.y, Rotation.z, Rotation.w },
                Scale = new[] { Scale.x, Scale.y, Scale.z }
            });
        }
    }

}
