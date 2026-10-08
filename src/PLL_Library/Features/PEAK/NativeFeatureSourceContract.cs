using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal static class NativeFeatureSourceContract
{
    internal sealed class SupplementalRootContract
    {
        const int FingerprintVersion = 3;

        readonly bool[] ActiveSelfPath;
        readonly string PathValue;
        readonly PropGrouper.PropGrouperTiming Timing;

        SupplementalRootContract(
            Transform Source, Transform Container, PropGrouper Grouper, bool UsesParentCollisionDomain)
        {
            this.Source = Source;
            PathValue = DeterministicObjectIdentity.RelativeComponentPath(Grouper, Container, 0);
            ActiveSelfPath = CaptureActiveSelfPath(Source, Container);
            Timing = Grouper.timing;
            this.UsesParentCollisionDomain = UsesParentCollisionDomain;
        }

        internal Transform Source { get; }
        internal bool UsesParentCollisionDomain { get; }

        internal static SupplementalRootContract[] Create(
            Transform Container, IReadOnlyList<LayoutSegmentSource> Sources)
        {
            if (!Container) throw new ArgumentNullException(nameof(Container));
            if (Sources is null) throw new ArgumentNullException(nameof(Sources));
            if (Sources.Count == 0) return Array.Empty<SupplementalRootContract>();

            var Declared = new List<Transform>(Sources.Count * 2);
            var DeclaredIds = new HashSet<int>();
            var NativeCitadel = false;
            foreach (var Source in Sources)
            {
                if (Source is null) throw new ArgumentException("A placement source is null.", nameof(Sources));
                if (Source is CustomLayoutSegmentSource) return Array.Empty<SupplementalRootContract>();
                if (Source is PeakAttachmentSource) continue;
                NativeCitadel |= Source is NativeOrdinarySegmentSource
                    { Id: "Citadel", NativeRole: Segment.TheKiln };
                AddDeclared(Source.SegmentParent.transform);
                if (Source.SegmentCampfire) AddDeclared(Source.SegmentCampfire.transform);
            }

            var Candidates = new List<SupplementalRootContract>();
            var Seen = new HashSet<int>();
            foreach (var Grouper in Container.GetComponentsInChildren<PropGrouper>(true))
            {
                if (!Grouper) throw new InvalidOperationException("A supplemental PropGrouper component is missing.");
                var Root = Grouper.transform;
                if (Root == Container || !Seen.Add(Root.GetInstanceID())) continue;
                var Overlaps = false;
                foreach (var DeclaredRoot in Declared)
                    if (Root == DeclaredRoot || Root.IsChildOf(DeclaredRoot) || DeclaredRoot.IsChildOf(Root))
                    {
                        Overlaps = true;
                        break;
                    }
                if (Overlaps) continue;
                var Groupers = Root.GetComponents<PropGrouper>();
                if (Groupers.Length != 1 || Groupers[0] != Grouper)
                    throw new InvalidOperationException(
                        "A supplemental native feature root must own exactly one direct PropGrouper.");
                var UsesParentCollisionDomain = NativeCitadel && Root.name == "Treasure Room Spawners"
                    && Root.parent != Container && Root.parent.name == "Gloom Temple";
                Candidates.Add(new SupplementalRootContract(Root, Container, Grouper, UsesParentCollisionDomain));
            }

            Candidates.Sort((Left, Right) =>
            {
                var Depth = Left.ActiveSelfPath.Length.CompareTo(Right.ActiveSelfPath.Length);
                return Depth != 0 ? Depth : string.CompareOrdinal(Left.PathValue, Right.PathValue);
            });
            var Result = new List<SupplementalRootContract>(Candidates.Count);
            foreach (var Candidate in Candidates)
            {
                var Nested = false;
                foreach (var Accepted in Result)
                    if (Candidate.Source.IsChildOf(Accepted.Source))
                    {
                        Nested = true;
                        break;
                    }
                if (!Nested) Result.Add(Candidate);
            }
            Result.Sort((Left, Right) => string.CompareOrdinal(Left.PathValue, Right.PathValue));
            return Result.ToArray();

            void AddDeclared(Transform Root)
            {
                if (Root != Container && !Root.IsChildOf(Container))
                    throw new InvalidOperationException("A declared feature root left its placement container.");
                if (DeclaredIds.Add(Root.GetInstanceID())) Declared.Add(Root);
            }
        }

        internal void ValidateBinding(Transform Root, Transform Container, bool ValidateActivation = true)
        {
            if (!Root || !Container || Root == Container || !Root.IsChildOf(Container))
                throw new InvalidOperationException(
                    "A supplemental native feature root left its occurrence container.");
            var Groupers = Root.GetComponents<PropGrouper>();
            if (Groupers.Length != 1)
                throw new InvalidOperationException(
                    "A supplemental native feature root must own exactly one direct PropGrouper.");
            var ActualPath = DeterministicObjectIdentity.RelativeComponentPath(Groupers[0], Container, 0);
            var ActualActiveSelfPath = CaptureActiveSelfPath(Root, Container);
            if (!string.Equals(PathValue, ActualPath, StringComparison.Ordinal)
                || ActualActiveSelfPath.Length != ActiveSelfPath.Length || Groupers[0].timing != Timing)
                throw new InvalidOperationException("A supplemental native feature-root binding changed.");
            if (ValidateActivation)
                for (var i = 0; i < ActiveSelfPath.Length; i++)
                    if (ActiveSelfPath[i] != ActualActiveSelfPath[i])
                        throw new InvalidOperationException(
                            "A supplemental native feature-root activation path changed.");
        }

        internal void WriteFingerprint(BinaryWriter Writer)
        {
            if (Writer is null) throw new ArgumentNullException(nameof(Writer));
            Writer.Write(FingerprintVersion);
            Writer.Write(PathValue);
            Writer.Write(ActiveSelfPath.Length);
            foreach (var Active in ActiveSelfPath) Writer.Write(Active);
            Writer.Write((int)Timing);
            Writer.Write(UsesParentCollisionDomain);
        }

        static bool[] CaptureActiveSelfPath(Transform Root, Transform Container)
        {
            var Result = new List<bool>();
            for (var Current = Root; Current != Container; Current = Current.parent)
            {
                if (!Current.parent)
                    throw new InvalidOperationException(
                        "A supplemental native feature root is outside its occurrence container.");
                Result.Add(Current.gameObject.activeSelf);
            }
            Result.Reverse();
            return Result.ToArray();
        }
    }
}
