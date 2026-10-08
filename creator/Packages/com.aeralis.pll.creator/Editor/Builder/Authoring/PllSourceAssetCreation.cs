using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PLL.Creator.Editor
{
    internal static class PllSourceAssetCreation
    {
        internal static Mesh CreatePrimitiveMesh(PrimitiveType Type, string Path)
        {
            var Primitive = GameObject.CreatePrimitive(Type);
            try
            {
                var Mesh = Object.Instantiate(Primitive.GetComponent<MeshFilter>().sharedMesh);
                Mesh.name = System.IO.Path.GetFileNameWithoutExtension(Path);
                return SaveAsset(Mesh, Path);
            }
            finally { Object.DestroyImmediate(Primitive); }
        }

        internal static T SaveAsset<T>(T Asset, string Path) where T : Object
        {
            try
            {
                AssetDatabase.CreateAsset(Asset, Path);
                if (!EditorUtility.IsPersistent(Asset))
                    throw new InvalidOperationException("Unity could not save project asset '" + Path + "'.");
                return Asset;
            }
            catch
            {
                if (!EditorUtility.IsPersistent(Asset)) Object.DestroyImmediate(Asset);
                throw;
            }
        }
    }
}
