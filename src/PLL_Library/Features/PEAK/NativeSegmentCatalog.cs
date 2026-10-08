using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeSegmentCatalog
{
    static readonly string[] BaseNames = { "Beach", "Tropics", "Alpine", "Caldera", "Volcano" };
    static readonly string?[] VariantNames = { null, "Roots", "Mesa", "Gloom", "Citadel" };
    internal static readonly IReadOnlyList<string> ConfigBiomeNames = CreateConfigBiomeNames();

    static string[] CreateConfigBiomeNames()
    {
        var Names = new List<string>(BaseNames);
        foreach (var Name in VariantNames) if (Name is not null) Names.Add(Name);
        Names.Add("Nadir"); Names.Add("Peak");
        Names.Sort(StringComparer.Ordinal);
        return Names.ToArray();
    }

    readonly IReadOnlyDictionary<string, LayoutSegmentSource> SourcesByName;

    NativeSegmentCatalog(
        Dictionary<string, LayoutSegmentSource> SourcesByName,
        NativeCampfireCheckpoint CampfireCheckpoint)
    {
        this.SourcesByName = new ReadOnlyDictionary<string, LayoutSegmentSource>(SourcesByName);
        this.CampfireCheckpoint = CampfireCheckpoint;
    }

    internal IReadOnlyDictionary<string, LayoutSegmentSource> Sources => SourcesByName;
    internal NativeCampfireCheckpoint CampfireCheckpoint { get; }
    internal NativePeakCampfireSource CapturePeakCampfireSource() =>
        NativePeakCampfireContract.Capture(RequireOrdinary("Gloom"), RequireOrdinary("Beach"));
    internal PeakAttachmentSource PeakPrototype => RequireOrdinary("Volcano").ContextualPeak
        ?? throw new InvalidOperationException("PEAK's native Peak attachment prototype is unavailable.");

    internal LayoutSegmentSource Require(string Name) => SourcesByName.TryGetValue(Name, out var Source)
        ? Source
        : throw new InvalidOperationException($"PEAK's native {Name} source is unavailable.");

    internal NativeOrdinarySegmentSource RequireOrdinary(string Name) => Require(Name) switch
    {
        NativeOrdinarySegmentSource Source => Source,
        _ => throw new InvalidOperationException($"PEAK's native {Name} ordinary source is unavailable.")
    };

    internal NadirLayoutSegmentSource Nadir => Require("Nadir") switch
    {
        NadirLayoutSegmentSource Source => Source,
        _ => throw new InvalidOperationException("PEAK's native Nadir source is unavailable.")
    };


    internal static bool IsCompositePair(LayoutSegmentSource Prefix, LayoutSegmentSource Owner) =>
        Prefix is NativeOrdinarySegmentSource PrefixSource
        && Owner is NativeOrdinarySegmentSource OwnerSource
        && PrefixSource.NativeRole == Segment.Caldera
        && OwnerSource.NativeRole == Segment.TheKiln
        && PrefixSource.IsNativeVariant == OwnerSource.IsNativeVariant
        && PrefixSource.Container == OwnerSource.Container
        && (PrefixSource.Id == "Caldera" && OwnerSource.Id == "Volcano"
            || PrefixSource.Id == "Gloom" && OwnerSource.Id == "Citadel");

    internal NativeOrdinarySegmentSource CompositePrefix(LayoutSegmentSource Owner)
    {
        if (Owner is not NativeOrdinarySegmentSource OwnerSource
            || OwnerSource.NativeRole != Segment.TheKiln)
            throw new ArgumentException("A native composite owner is required.", nameof(Owner));
        var Name = OwnerSource.Id switch
        {
            "Volcano" => "Caldera",
            "Citadel" => "Gloom",
            _ => throw new InvalidOperationException(
                $"Native composite owner '{OwnerSource.Id}' has no public layout identity.")
        };
        var Prefix = RequireOrdinary(Name);
        if (!IsCompositePair(Prefix, OwnerSource))
            throw new InvalidOperationException(
                $"PEAK's native {Name}/{OwnerSource.Id} composite is incomplete.");
        return Prefix;
    }

    internal LayoutSegmentCatalog IncludeRegisteredSegments() => LayoutSegmentCatalog.Create(this);

    internal static NativeSegmentCatalog Create(MapHandler Handler)
    {
        if (!Handler) throw new ArgumentNullException(nameof(Handler));
        if (Handler.segments is null || Handler.segments.Length < BaseNames.Length)
            throw new InvalidOperationException("PEAK's five ordinary native segment roles are unavailable.");

        var Descriptors = new List<Descriptor>(BaseNames.Length + VariantNames.Length - 1);
        for (var i = 0; i < BaseNames.Length; i++)
            AddDescriptor(Descriptors, BaseNames[i], (Segment)i, Handler.segments[i], false);

        var Variants = Handler.variantSegments
            ?? throw new InvalidOperationException("PEAK's native variant segment pool is unavailable.");
        var ClaimedVariants = new bool[Variants.Length];
        for (var i = 1; i < BaseNames.Length; i++)
        {
            var SegmentValue = Handler.segments[i]
                ?? throw new InvalidOperationException($"PEAK's native segment role {i} is unavailable.");
            if (!SegmentValue.hasVariant) continue;
            var VariantIndexValue = VariantIndex(SegmentValue, Variants);
            if (ClaimedVariants[VariantIndexValue])
                throw new InvalidOperationException(
                    $"PEAK's native variant index {VariantIndexValue} is assigned to multiple segment roles.");
            ClaimedVariants[VariantIndexValue] = true;
            var Name = VariantNames[i]
                ?? throw new InvalidOperationException($"PEAK's native variant role {i} has no catalog identity.");
            AddDescriptor(Descriptors, Name, (Segment)i, Variants[VariantIndexValue], true);
        }

        RequireDescriptors(Descriptors);
        var ProgressHandler = FindUniqueInScene<MountainProgressHandler>(Handler.gameObject.scene);
        var ProgressPoints = ProgressProjection.NativeProgressCatalog(ProgressHandler);
        var OrdinaryProgress = BindProgressPoints(Descriptors, ProgressPoints);
        var Summit = RequireSummit(ProgressPoints);
        var ByName = new Dictionary<string, LayoutSegmentSource>(StringComparer.Ordinal);
        for (var i = 0; i < Descriptors.Count; i++)
        {
            var Descriptor = Descriptors[i];
            var Source = new NativeOrdinarySegmentSource(
                Handler, Descriptor.Name, Descriptor.StableId, Descriptor.Role,
                Descriptor.Segment, OrdinaryProgress[i]);
            ValidateSource(Handler, Source);
            ByName.Add(Source.Id, Source);
        }

        ValidateContainers(ByName);
        ValidateAmuletCoverage(ByName);
        AttachPeak(Handler, RequireOrdinary(ByName, "Citadel"), Summit);
        AttachPeak(Handler, RequireOrdinary(ByName, "Volcano"), Summit);
        var Nadir = new NadirLayoutSegmentSource(
            Handler, FindUniqueInScene<Peak.VoidBiome>(Handler.gameObject.scene));
        NativeSegmentPorts.ValidateSource(
            Handler, Nadir.Id, Nadir.Container, Nadir.SegmentParent.transform, null);
        ByName.Add(Nadir.Id, Nadir);
        FileManager.BindNativeVariationChoices(CaptureVariationChoices(ByName));
        return new NativeSegmentCatalog(ByName, NativeCampfireCheckpoint.Capture(Handler));
    }

    static Dictionary<string, string[]> CaptureVariationChoices(IReadOnlyDictionary<string, LayoutSegmentSource> Sources)
    {
        var ChoicesByBiome = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var Biome in ConfigBiomeNames)
        {
            var Root = Biome == "Peak"
                ? ((NativeOrdinarySegmentSource)Sources["Volcano"]).ContextualPeak!.SegmentParent.transform
                : Sources[Biome].SegmentParent.transform;
            var Swappers = Root.GetComponentsInChildren<VariationSwapper>(true);
            var Variants = Root.GetComponentsInChildren<VariantObject>(true);
            var Count = Variants.Length + 1;
            foreach (var Swapper in Swappers) Count += Swapper.Variations.Length;
            var Choices = new string[Count];
            Choices[0] = "Random";
            var Index = 1;
            foreach (var Swapper in Swappers)
                foreach (var Variation in Swapper.Variations)
                    Choices[Index++] = VariationToken(Variation.parent.transform);
            foreach (var Variant in Variants) Choices[Index++] = VariationToken(Variant);
            Array.Sort(Choices, 1, Choices.Length - 1, StringComparer.Ordinal);
            for (var i = 2; i < Choices.Length; i++)
                if (Choices[i] == Choices[i - 1])
                    throw new InvalidOperationException($"Native biome '{Biome}' has duplicate variation '{Choices[i]}'.");
            ChoicesByBiome.Add(Biome, Choices);
        }
        return ChoicesByBiome;
    }

    internal static string VariationToken(VariantObject Variant) =>
        ((int)Variant.group).ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + Variant.name;
    internal static string VariationToken(Transform Parent) => "Terrain: " + Parent.name;

    static void AddDescriptor(
        ICollection<Descriptor> Descriptors,
        string Name,
        Segment Role,
        MapHandler.MapSegment? SegmentValue,
        bool Variant)
    {
        if (SegmentValue is null || !SegmentValue._segmentParent) return;
        if (SegmentValue.isVariant != Variant)
            throw new InvalidOperationException(
                $"PEAK's native {Name} segment has an invalid base/variant marker.");
        Descriptors.Add(new Descriptor(Name, StableId(Name), Role, SegmentValue));
    }

    static int VariantIndex(MapHandler.MapSegment SegmentValue, MapHandler.MapSegment[] Variants)
    {
        var Index = SegmentValue.variantBiomeIndex;
        if (Index < 0 || Index >= Variants.Length || Variants[Index] is null)
            throw new InvalidOperationException(
                $"PEAK's native segment variant index {Index} is outside its authored variant pool.");
        return Index;
    }

    static void RequireDescriptors(IReadOnlyList<Descriptor> Descriptors)
    {
        foreach (var Name in BaseNames)
            if (!Contains(Descriptors, Name))
                throw new InvalidOperationException(
                    $"PEAK's required native {Name} segment source was not found.");
        for (var i = 1; i < VariantNames.Length; i++)
        {
            var Name = VariantNames[i]!;
            if (!Contains(Descriptors, Name))
                throw new InvalidOperationException(
                    $"PEAK's required native {Name} variant did not map to its authored segment role.");
        }

        static bool Contains(IReadOnlyList<Descriptor> Values, string Name)
        {
            foreach (var Value in Values)
                if (string.Equals(Value.Name, Name, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    static MountainProgressHandler.ProgressPoint[] BindProgressPoints(
        IReadOnlyList<Descriptor> Descriptors,
        IReadOnlyList<MountainProgressHandler.ProgressPoint> Points)
    {
        var Result = new MountainProgressHandler.ProgressPoint[Descriptors.Count];
        if (Points.Count == BaseNames.Length + 1)
        {
            for (var i = 0; i < Descriptors.Count; i++)
            {
                var Index = (int)Descriptors[i].Role;
                if (Index < 0 || Index >= BaseNames.Length || !Points[Index].transform
                    || Points[Index].biome == Biome.BiomeType.Peak)
                    throw new InvalidOperationException(
                        $"PEAK's native progress binding for ordinary role {Descriptors[i].Role} is unavailable.");
                Result[i] = Points[Index];
            }
            return Result;
        }
        if (Points.Count != Descriptors.Count + 1)
            throw new InvalidOperationException(
                "PEAK's native progress catalog does not contain one point per native source and its summit.");

        var RoleAnchors = new Transform[BaseNames.Length];
        var RoleAnchorCount = 0;
        foreach (var Candidate in Points)
        {
            if (Candidate is null || Candidate.biome == Biome.BiomeType.Peak || !Candidate.transform) continue;
            var Known = false;
            for (var i = 0; i < RoleAnchorCount; i++)
                if (Candidate.transform == RoleAnchors[i])
                {
                    Known = true;
                    break;
                }
            if (Known) continue;
            if (RoleAnchorCount == RoleAnchors.Length)
                throw new InvalidOperationException(
                    "PEAK's native progress catalog contains too many ordinary role anchors.");
            RoleAnchors[RoleAnchorCount++] = Candidate.transform;
        }
        if (RoleAnchorCount != RoleAnchors.Length)
            throw new InvalidOperationException(
                "PEAK's native progress catalog does not contain five ordinary role anchors.");

        for (var i = 0; i < Descriptors.Count; i++)
        {
            var Descriptor = Descriptors[i];
            var RoleIndex = (int)Descriptor.Role;
            MountainProgressHandler.ProgressPoint? Point = null;
            foreach (var Candidate in Points)
            {
                if (Candidate is null || Candidate.transform != RoleAnchors[RoleIndex]
                    || Candidate.biome != Descriptor.Segment._biome) continue;
                if (Point is not null)
                    throw new InvalidOperationException(
                        $"PEAK's native progress binding for ordinary role {Descriptor.Role} and biome {Descriptor.Segment._biome} is ambiguous.");
                Point = Candidate;
            }
            Result[i] = Point is not null && Point.transform == RoleAnchors[RoleIndex]
                ? Point
                : throw new InvalidOperationException(
                    $"PEAK's native progress binding for ordinary role {Descriptor.Role} is unavailable.");
        }
        return Result;
    }

    static MountainProgressHandler.ProgressPoint RequireSummit(
        IReadOnlyList<MountainProgressHandler.ProgressPoint> Points)
    {
        if (Points.Count == 0)
            throw new InvalidOperationException("PEAK's native summit progress anchor is unavailable.");
        var Summit = Points[Points.Count - 1];
        if (Summit.biome != Biome.BiomeType.Peak || !Summit.transform)
            throw new InvalidOperationException(
                "PEAK's terminal progress point is not its native summit anchor.");
        return Summit;
    }

    static void AttachPeak(
        MapHandler Handler,
        NativeOrdinarySegmentSource Terminal,
        MountainProgressHandler.ProgressPoint Summit)
    {
        if (!Handler.respawnThePeak)
            throw new InvalidOperationException("PEAK's native Peak respawn anchor is unavailable.");
        var Peaks = Terminal.SegmentParent.GetComponentsInChildren<PeakHandler>(true);
        if (Peaks.Length != 1)
            throw new InvalidOperationException(
                $"PEAK's native {Terminal.Id} segment must own exactly one PeakHandler subtree.");
        Terminal.AttachPeak(new PeakAttachmentSource(
            Terminal, Peaks[0].transform, Handler.respawnThePeak, Summit));
    }

    static void ValidateSource(MapHandler Handler, NativeOrdinarySegmentSource Source) =>
        NativeSegmentPorts.ValidateSource(
            Handler,
            Source.Id,
            Source.Container,
            Source.SegmentParent.transform,
            Source.SegmentCampfire ? Source.SegmentCampfire.transform : null);

    static void ValidateContainers(IReadOnlyDictionary<string, LayoutSegmentSource> Sources)
    {
        var BaseContainer = Sources["Caldera"].Container;
        if (BaseContainer != Sources["Volcano"].Container)
            throw new InvalidOperationException(
                "PEAK's Caldera/Volcano container ownership is ambiguous.");
        var VariantContainer = Sources["Gloom"].Container;
        if (VariantContainer != Sources["Citadel"].Container || BaseContainer == VariantContainer)
            throw new InvalidOperationException(
                "PEAK's Gloom/Citadel container ownership is ambiguous.");
    }

    static void ValidateAmuletCoverage(IReadOnlyDictionary<string, LayoutSegmentSource> Sources)
    {
        NativeAmuletSourceContract? Common = null;
        var Categories = 0;
        foreach (var Value in Sources.Values)
        {
            var Source = Value as NativeOrdinarySegmentSource
                ?? throw new InvalidOperationException(
                    $"PEAK's native {Value.Id} source has no amulet-statue opportunity contract.");
            var Contract = Source.NadirAmulets;
            if (Common is not null && !Common.HasSameSemanticBijection(Contract))
                throw new InvalidOperationException(
                    "PEAK's native amulet-statue spawners disagree on their four-prop semantic bijection.");
            Common ??= Contract;
            Categories |= Contract.CoverageBit;
        }
        if (Categories != 15)
            throw new InvalidOperationException(
                "PEAK's native segment catalog does not cover all four amulet-statue categories.");
    }

    static NativeOrdinarySegmentSource RequireOrdinary(
        IReadOnlyDictionary<string, LayoutSegmentSource> Sources, string Name) => Sources[Name] switch
    {
        NativeOrdinarySegmentSource Source => Source,
        _ => throw new InvalidOperationException($"PEAK's native {Name} ordinary source is unavailable.")
    };

    static T FindUniqueInScene<T>(UnityEngine.SceneManagement.Scene Scene) where T : Component
    {
        T? Result = null;
        foreach (var Root in Scene.GetRootGameObjects())
            foreach (var Component in Root.GetComponentsInChildren<T>(true))
            {
                if (Result)
                    throw new InvalidOperationException(
                        $"PEAK's scene contains multiple {typeof(T).Name} components.");
                Result = Component;
            }
        return Result
            ? Result
            : throw new InvalidOperationException(
                $"PEAK's scene contains no {typeof(T).Name} component.");
    }

    static string StableId(string Name) => Name switch
    {
        "Beach" => SegmentId.Beach.Value,
        "Tropics" => SegmentId.Tropics.Value,
        "Roots" => SegmentId.Roots.Value,
        "Alpine" => SegmentId.Alpine.Value,
        "Mesa" => SegmentId.Mesa.Value,
        "Caldera" => SegmentId.Caldera.Value,
        "Gloom" => SegmentId.Gloom.Value,
        "Volcano" => SegmentId.Volcano.Value,
        "Citadel" => SegmentId.Citadel.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(Name))
    };

    sealed class Descriptor
    {
        internal Descriptor(
            string Name, string StableId, Segment Role, MapHandler.MapSegment Segment)
        {
            this.Name = Name;
            this.StableId = StableId;
            this.Role = Role;
            this.Segment = Segment;
        }

        internal string Name { get; }
        internal string StableId { get; }
        internal Segment Role { get; }
        internal MapHandler.MapSegment Segment { get; }
    }
}
