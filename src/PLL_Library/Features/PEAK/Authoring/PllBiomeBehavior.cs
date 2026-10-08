namespace PLL.Library;

public enum PllBiomeBehavior : byte
{
    Undefined = 0,
    Authored = 1,
    Beach = 2,
    Tropics = 3,
    Roots = 4,
    Alpine = 5,
    Mesa = 6,
    Caldera = 7,
    Gloom = 8,
    Volcano = 9,
    Citadel = 10
}

internal readonly struct NativeCompatibility
{
    internal NativeCompatibility(Segment Role, Biome.BiomeType Biome)
    {
        this.Role = Role;
        this.Biome = Biome;
    }

    internal Segment Role { get; }
    internal Biome.BiomeType Biome { get; }
}

internal static class PllBiomeBehaviorCompatibility
{
    internal static void RequireDefined(PllBiomeBehavior Behavior, string Context)
    {
        if (!PllAuthoringValueContract.IsDefined(Behavior))
            throw new System.ArgumentException(
                $"{Context} has undefined biome behavior '{Behavior}'.", "definition");
    }

    internal static NativeCompatibility? Resolve(PllBiomeBehavior Behavior) => Behavior switch
    {
        PllBiomeBehavior.Authored => null,
        PllBiomeBehavior.Beach => new(Segment.Beach, global::Biome.BiomeType.Shore),
        PllBiomeBehavior.Tropics => new(Segment.Tropics, global::Biome.BiomeType.Tropics),
        PllBiomeBehavior.Roots => new(Segment.Tropics, global::Biome.BiomeType.Roots),
        PllBiomeBehavior.Alpine => new(Segment.Alpine, global::Biome.BiomeType.Alpine),
        PllBiomeBehavior.Mesa => new(Segment.Alpine, global::Biome.BiomeType.Mesa),
        PllBiomeBehavior.Caldera => new(Segment.Caldera, global::Biome.BiomeType.Volcano),
        PllBiomeBehavior.Gloom => new(Segment.Caldera, global::Biome.BiomeType.Swamp),
        PllBiomeBehavior.Volcano => new(Segment.TheKiln, global::Biome.BiomeType.Volcano),
        PllBiomeBehavior.Citadel => new(Segment.TheKiln, global::Biome.BiomeType.Temple),
        _ => throw new System.InvalidOperationException(
            $"Biome behavior '{Behavior}' is outside the sealed behavior model.")
    };
}
