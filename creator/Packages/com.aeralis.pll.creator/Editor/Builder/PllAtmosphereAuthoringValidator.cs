using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using static PLL.Creator.Editor.PllSourceIdentityValidator;

namespace PLL.Creator.Editor
{
    internal static class PllAtmosphereAuthoringValidator
    {
        internal static PllAtmosphereAuthoring ValidateOptional(SourceIdentity Identity)
        {
            var Values = DiscoverGraphComponents<PllAtmosphereAuthoring>(Identity.Graph);
            if (Values.Length == 0) return null;
            if (Values.Length != 1)
                throw new InvalidOperationException("Biome definition '" + Identity.Label
                    + "' must place at most one PllAtmosphereAuthoring directly on its root.");
            var Atmosphere = Values[0];
            var Context = "Biome definition '" + Identity.Label + "' atmosphere";
            if (!Atmosphere || Atmosphere.gameObject != Identity.Root)
                throw new InvalidOperationException(Context
                    + " must be the one PllAtmosphereAuthoring directly on the biome root.");
            ValidateAssets(Atmosphere.SkyboxMaterial, Atmosphere.FogTexture, Context);
            return Atmosphere;
        }

        internal static void ValidateComponent(
            GameObject BiomeRoot,
            PllAtmosphereAuthoring Atmosphere,
            string Context)
        {
            if (!BiomeRoot || !Atmosphere || Atmosphere.gameObject != BiomeRoot
                || BiomeRoot.GetComponentsInChildren<PllAtmosphereAuthoring>(true).Length != 1
                || BiomeRoot.GetComponents<PllAtmosphereAuthoring>().Length != 1)
                throw new InvalidOperationException(Context
                    + " must be the one PllAtmosphereAuthoring directly on the biome root.");
            ValidateAssets(Atmosphere.SkyboxMaterial, Atmosphere.FogTexture, Context);
        }

        internal static void ValidateAssets(
            Material SkyboxMaterial,
            Texture2D FogTexture,
            string Context)
        {
            if (!ReferenceEquals(SkyboxMaterial, null) && !SkyboxMaterial
                || !ReferenceEquals(FogTexture, null) && !FogTexture)
                throw new InvalidOperationException(Context + " contains a destroyed asset reference.");
            if (!SkyboxMaterial && !FogTexture)
                throw new InvalidOperationException(Context
                    + " requires a skybox Material, a fog Texture2D, or both.");
            if (SkyboxMaterial)
            {
                if (!SkyboxMaterial.shader)
                    throw new InvalidOperationException(Context + " skybox Material has no live Shader.");
                RequirePersistentAsset(SkyboxMaterial, Context + " skybox Material");
                RequirePersistentAsset(SkyboxMaterial.shader, Context + " skybox Shader");
                VisitMaterialTextures(
                    SkyboxMaterial,
                    Texture => RequirePersistentAsset(Texture, Context + " skybox texture"));
            }
            if (FogTexture) RequirePersistentAsset(FogTexture, Context + " fog Texture2D");
        }

        internal static void VisitDependencies(
            PllAtmosphereAuthoring Atmosphere,
            Action<Object> Visit)
        {
            if (!Atmosphere) throw new ArgumentNullException(nameof(Atmosphere));
            if (Visit is null) throw new ArgumentNullException(nameof(Visit));
            var SkyboxMaterial = Atmosphere.SkyboxMaterial;
            if (SkyboxMaterial)
            {
                Visit(SkyboxMaterial);
                Visit(SkyboxMaterial.shader);
                VisitMaterialTextures(SkyboxMaterial, Visit);
            }
            if (Atmosphere.FogTexture) Visit(Atmosphere.FogTexture);
        }

        static void VisitMaterialTextures(Material Material, Action<Object> Visit)
        {
            var Shader = Material.shader;
            for (var Index = 0; Index < Shader.GetPropertyCount(); Index++)
            {
                if (Shader.GetPropertyType(Index) != ShaderPropertyType.Texture) continue;
                var Texture = Material.GetTexture(Shader.GetPropertyName(Index));
                if (Texture) Visit(Texture);
            }
        }

        static void RequirePersistentAsset(Object Value, string Context)
        {
            var Path = AssetDatabase.GetAssetPath(Value);
            if (!Value || !EditorUtility.IsPersistent(Value)
                || !Path.StartsWith("Assets/", StringComparison.Ordinal)
                    && !Path.StartsWith("Packages/", StringComparison.Ordinal)
                || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    Value,
                    out var Guid,
                    out long LocalId)
                || string.IsNullOrEmpty(Guid)
                || LocalId == 0)
                throw new InvalidOperationException(Context
                    + " must be a persistent project or package asset with stable identity.");
        }
    }
}
