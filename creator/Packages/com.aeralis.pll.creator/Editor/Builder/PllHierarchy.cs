using System;
using System.Collections.Generic;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal static class PllHierarchy
    {
        internal static string NamePath(Transform root, Transform value)
        {
            var names = new List<string>();
            for (var current = value; current; current = current.parent)
            {
                names.Add(current.name);
                if (current == root) break;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        internal static int[] SiblingPath(Transform root, Transform value, string OwnershipFailure)
        {
            var indices = new List<int>();
            for (var current = value; current != root; current = current.parent)
            {
                if (!current || !current.parent) throw new InvalidOperationException(OwnershipFailure);
                indices.Add(current.GetSiblingIndex());
            }
            indices.Reverse();
            return indices.ToArray();
        }
    }
}
