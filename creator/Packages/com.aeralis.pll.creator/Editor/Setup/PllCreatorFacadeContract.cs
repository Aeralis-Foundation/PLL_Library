using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using UnityEngine;
using ReflectionAssembly = System.Reflection.Assembly;

namespace PLL.Creator.Editor
{
    [Serializable]
    sealed class PllCreatorFacadeContract
    {
        internal const int SchemaVersion = 2;

        public int schemaVersion;
        public string sourceAssembly;
        public int serializedFieldCount;
        public PllCreatorContractType[] types;
        public PllCreatorContractMethod[] methods;
        public PllCreatorContractScript[] scripts;
        public PllCreatorContractLayer[] layers;
        public string[] dependencyRoots;
        internal string[] TemplateNames { get; private set; }

        internal static bool TryLoad(string PackageRoot, out PllCreatorFacadeContract Contract, out string Failure)
        {
            Contract = null;
            var ContractPath = PllCreatorEnvironment.PackageContractPath(PackageRoot);
            if (!File.Exists(ContractPath))
            {
                Failure = "The installed package has no PEAK facade contract.";
                return false;
            }
            try
            {
                Contract = JsonUtility.FromJson<PllCreatorFacadeContract>(File.ReadAllText(ContractPath));
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                              || Exception is ArgumentException || Exception is System.Security.SecurityException)
            {
                Failure = "The PEAK facade contract is invalid: " + Exception.Message;
                return false;
            }
            if (Contract == null || Contract.schemaVersion != SchemaVersion
                || !string.Equals(Contract.sourceAssembly, "Assembly-CSharp", StringComparison.Ordinal)
                || Contract.types == null || Contract.methods == null || Contract.scripts == null
                || Contract.layers == null || Contract.dependencyRoots == null
                || Contract.types.Any(Value => Value == null || string.IsNullOrEmpty(Value.name)
                    || string.IsNullOrEmpty(Value.metadataName) || Value.fields == null || Value.enumValues == null
                    || Value.enumValues.Any(Item => Item == null || string.IsNullOrEmpty(Item.name)
                        || string.IsNullOrEmpty(Item.value))
                    || Value.fields.Any(Field => Field == null || string.IsNullOrEmpty(Field.name)
                        || string.IsNullOrEmpty(Field.typeName) || Field.formerNames == null))
                || Contract.methods.Any(Value => Value == null || string.IsNullOrEmpty(Value.declaringType)
                    || string.IsNullOrEmpty(Value.name) || string.IsNullOrEmpty(Value.returnType)
                    || Value.parameterTypes == null || !ValidVisibility(Value.visibility))
                || Contract.scripts.Any(Value => Value == null || string.IsNullOrEmpty(Value.typeName)
                    || string.IsNullOrEmpty(Value.template))
                || Contract.layers.Any(Value => Value == null || string.IsNullOrEmpty(Value.name)
                    || Value.index < 0 || Value.index > 31)
                || Contract.dependencyRoots.Any(string.IsNullOrEmpty)
                || Contract.types.SelectMany(Value => Value.fields).Count(Value => Value.serialized)
                    != Contract.serializedFieldCount)
            {
                Contract = null;
                Failure = "The PEAK facade contract has an unsupported shape.";
                return false;
            }
            var LoadedContract = Contract;
            var Names = new HashSet<string>(StringComparer.Ordinal);
            if (LoadedContract.types.Any(Value => !Names.Add(Value.name))
                || LoadedContract.types.Select(Value => Value.metadataName).Distinct(StringComparer.Ordinal).Count()
                    != LoadedContract.types.Length
                || LoadedContract.scripts.Any(Value => !Names.Contains(Value.typeName))
                || LoadedContract.scripts.Select(Value => Value.typeName).Distinct(StringComparer.Ordinal).Count()
                    != LoadedContract.scripts.Length
                || LoadedContract.methods.Select(Value => Value.declaringType + "|" + Value.name + "|"
                    + Value.returnType + "|" + string.Join(",", Value.parameterTypes))
                    .Distinct(StringComparer.Ordinal).Count() != LoadedContract.methods.Length
                || LoadedContract.layers.Select(Value => Value.name).Distinct(StringComparer.Ordinal).Count()
                    != LoadedContract.layers.Length
                || LoadedContract.layers.Select(Value => Value.index).Distinct().Count() != LoadedContract.layers.Length
                || !LoadedContract.layers.Any(Value => string.Equals(Value.name, "Terrain", StringComparison.Ordinal)
                    && Value.index == 20)
                || !LoadedContract.layers.Any(Value => string.Equals(Value.name, "Map", StringComparison.Ordinal)
                    && Value.index == 21)
                || !LoadedContract.layers.Any(Value => string.Equals(Value.name, "InvisWall", StringComparison.Ordinal)
                    && Value.index == 22)
                || !new HashSet<string>(LoadedContract.dependencyRoots, StringComparer.Ordinal).SetEquals(new[]
                {
                    "PhotonUnityNetworking", "Sirenix.Serialization", "Zorro.Core.Runtime"
                }))
            {
                Contract = null;
                Failure = "The PEAK facade contract contains an invalid type catalog.";
                return false;
            }
            var TemplateNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Declaration in LoadedContract.types)
                TemplateNames.Add(TemplateName(Declaration.metadataName));
            if (LoadedContract.scripts.Any(Value =>
                    !string.Equals(Value.template,
                        TemplateName(LoadedContract.types.First(Type => string.Equals(Type.name, Value.typeName,
                            StringComparison.Ordinal)).metadataName), StringComparison.Ordinal)))
            {
                Contract = null;
                Failure = "The PEAK facade contract contains an invalid template catalog.";
                return false;
            }
            var TemplateRoot = PllCreatorEnvironment.PackageFacadeRootPath(PackageRoot);
            string[] ActualTemplates;
            try
            {
                TemplateRoot = Path.GetFullPath(TemplateRoot);
                ActualTemplates = Directory.Exists(TemplateRoot)
                    ? Directory.EnumerateFiles(TemplateRoot, "*.cs.template", SearchOption.AllDirectories)
                        .Select(File => string.Equals(Path.GetDirectoryName(File),
                                TemplateRoot, StringComparison.OrdinalIgnoreCase)
                            ? Path.GetFileName(File) : string.Empty).ToArray()
                    : new string[0];
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                                              || Exception is ArgumentException || Exception is System.Security.SecurityException)
            {
                Contract = null;
                Failure = "The installed package facade templates cannot be inspected: " + Exception.Message;
                return false;
            }
            if (ActualTemplates.Length != TemplateNames.Count
                || !TemplateNames.SetEquals(ActualTemplates))
            {
                Contract = null;
                Failure = "The installed package facade templates do not match the contract.";
                return false;
            }
            Contract.TemplateNames = TemplateNames.OrderBy(Value => Value, StringComparer.Ordinal).ToArray();
            Failure = string.Empty;
            return true;
        }

        internal static string TemplateName(string MetadataName)
        {
            var End = MetadataName.IndexOf('/');
            var TopLevel = End < 0 ? MetadataName : MetadataName.Substring(0, End);
            var Start = TopLevel.LastIndexOf('.') + 1;
            var Generic = TopLevel.IndexOf('`', Start);
            return TopLevel.Substring(Start, (Generic < 0 ? TopLevel.Length : Generic) - Start) + ".cs.template";
        }

        internal bool TryValidatePeak(
            string ManagedPath, out string SourcePath, out string[] Dependencies, out string Failure)
        {
            Dependencies = null;
            SourcePath = Path.Combine(ManagedPath, sourceAssembly + ".dll");
            if (!File.Exists(SourcePath))
            {
                Failure = "PEAK_Data\\Managed has no Assembly-CSharp.dll.";
                return false;
            }
            Dictionary<string, string> Assemblies;
            if (!TryIndexAssemblies(ManagedPath, out Assemblies, out Failure)) return false;
            try
            {
                using (var Metadata = new AssemblyMetadata(Reference =>
                    Assemblies.TryGetValue(Reference.Name, out var Path)
                        ? Path : throw new AssemblyResolutionException(Reference)))
                {
                    var Source = Metadata.Read(SourcePath, sourceAssembly);
                    if (!TryValidateAssembly(Source, "PEAK", out Failure)) return false;
                    var Result = new HashSet<string>(StringComparer.Ordinal);
                    var Queue = new Queue<string>(dependencyRoots);
                    while (Queue.Count != 0)
                    {
                        var Name = Queue.Dequeue();
                        if (PllCreatorProviderInventory.FrameworkAssembly(Name) || !Result.Add(Name)) continue;
                        string AssemblyPath;
                        if (!Assemblies.TryGetValue(Name, out AssemblyPath))
                        {
                            Failure = "PEAK managed dependency '" + Name + "' is unavailable.";
                            return false;
                        }
                        foreach (var Reference in Metadata.Read(AssemblyPath, Name).MainModule.AssemblyReferences)
                            if (!PllCreatorProviderInventory.FrameworkAssembly(Reference.Name)
                                && !string.Equals(Reference.Name, sourceAssembly, StringComparison.Ordinal))
                                Queue.Enqueue(Reference.Name);
                    }
                    Dependencies = Result.OrderBy(Name => Name, StringComparer.Ordinal)
                        .Select(Name => Assemblies[Name]).ToArray();
                    Failure = string.Empty;
                    return true;
                }
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                || Exception is ArgumentException || Exception is BadImageFormatException || Exception is TypeLoadException
                || Exception is AssemblyResolutionException || Exception is ResolutionException
                || Exception is System.Security.SecurityException)
            {
                Failure = "PEAK metadata cannot be inspected: " + Exception.Message;
                return false;
            }
        }

        internal bool TryValidateFacade(
            ReflectionAssembly Source, out IReadOnlyDictionary<string, Type> Types, out string Failure)
        {
            Types = null;
            if (Source == null || !string.Equals(Source.GetName().Name, sourceAssembly, StringComparison.Ordinal))
            {
                Failure = "Unity did not load the generated Assembly-CSharp facade.";
                return false;
            }
            try
            {
                var Assemblies = AppDomain.CurrentDomain.GetAssemblies();
                using (var Metadata = new AssemblyMetadata(Reference =>
                {
                    var Name = Reference.FullName;
                    string Path = null;
                    foreach (var Assembly in Assemblies)
                    {
                        if (Assembly.IsDynamic || Assembly.ReflectionOnly || Assembly.FullName != Name) continue;
                        var Location = Assembly.Location;
                        if (string.IsNullOrEmpty(Location)) continue;
                        if (Path != null && !string.Equals(Path, Location, StringComparison.Ordinal))
                            throw new AmbiguousMatchException("Loaded assembly '" + Name + "' has multiple file locations.");
                        Path = Location;
                    }
                    return Path ?? throw new AssemblyResolutionException(Reference);
                }))
                {
                    var Compiled = Metadata.Read(Source.Location, sourceAssembly);
                    if (Compiled.MainModule.Mvid != Source.ManifestModule.ModuleVersionId)
                    {
                        Failure = "Unity's loaded facade differs from its compiled file.";
                        return false;
                    }
                    if (!TryValidateAssembly(Compiled, "Generated", out Failure)) return false;
                    var ActualTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
                    foreach (var Declaration in types)
                    {
                        var Type = Source.GetType(Declaration.metadataName.Replace('/', '+'), true);
                        ActualTypes.Add(Declaration.name, Type);
                    }
                    Types = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Type>(ActualTypes);
                    return true;
                }
            }
            catch (Exception Exception) when (Exception is ArgumentException || Exception is IOException
                || Exception is UnauthorizedAccessException || Exception is TypeLoadException
                || Exception is ReflectionTypeLoadException || Exception is BadImageFormatException || Exception is AmbiguousMatchException
                || Exception is AssemblyResolutionException || Exception is ResolutionException
                || Exception is System.Security.SecurityException)
            {
                Failure = "Generated facade metadata cannot be inspected: " + Exception.Message;
                return false;
            }
        }

        sealed class AssemblyMetadata : IAssemblyResolver
        {
            readonly Func<AssemblyNameReference, string> ResolvePath;
            readonly Dictionary<string, AssemblyDefinition> Loaded =
                new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);
            readonly Dictionary<string, AssemblyDefinition> Resolved =
                new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);

            internal AssemblyMetadata(Func<AssemblyNameReference, string> ResolvePath) => this.ResolvePath = ResolvePath;

            internal AssemblyDefinition Read(string Path, string ExpectedName)
            {
                AssemblyDefinition Assembly;
                if (Loaded.TryGetValue(Path, out Assembly)) return Assembly;
                Assembly = AssemblyDefinition.ReadAssembly(Path,
                    new ReaderParameters { AssemblyResolver = this, InMemory = true });
                if (!string.Equals(Assembly.Name.Name, ExpectedName, StringComparison.Ordinal))
                {
                    var ActualName = Assembly.Name.Name;
                    Assembly.Dispose();
                    throw new BadImageFormatException("Managed assembly '" + ActualName
                        + "' does not match the required name '" + ExpectedName + "'.");
                }
                Loaded.Add(Path, Assembly);
                return Assembly;
            }

            public AssemblyDefinition Resolve(AssemblyNameReference Reference)
            {
                var Name = Reference.FullName;
                AssemblyDefinition Assembly;
                if (Resolved.TryGetValue(Name, out Assembly)) return Assembly;
                Assembly = Read(ResolvePath(Reference), Reference.Name);
                Resolved.Add(Name, Assembly);
                return Assembly;
            }

            public AssemblyDefinition Resolve(AssemblyNameReference Reference, ReaderParameters Parameters) => Resolve(Reference);

            public void Dispose()
            {
                foreach (var Assembly in Loaded.Values) Assembly.Dispose();
            }
        }

        bool TryValidateAssembly(
            AssemblyDefinition Source, string Owner, out string Failure)
        {
            var ActualTypes = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
            foreach (var Declaration in types)
            {
                var Type = Source.MainModule.GetType(Declaration.metadataName);
                if (Type == null)
                {
                    Failure = Owner + " type '" + Declaration.name + "' is unavailable.";
                    return false;
                }
                if (!ValidDeclaration(Type, Declaration, Owner, out Failure)) return false;
                ActualTypes.Add(Declaration.name, Type);
            }
            foreach (var Declaration in types)
                if (!ValidFields(ActualTypes[Declaration.name], Declaration, Owner, out Failure)) return false;
            foreach (var Method in methods)
                if (!ValidMethod(ActualTypes, Method, Owner, out Failure)) return false;
            foreach (var Script in scripts)
            {
                var Type = ActualTypes[Script.typeName];
                var ExpectedRoot = string.Equals(Script.typeName, "DayNightProfile", StringComparison.Ordinal)
                    ? "UnityEngine.ScriptableObject" : "UnityEngine.MonoBehaviour";
                if (!DerivesFrom(Type, ExpectedRoot) || Type.IsAbstract)
                {
                    Failure = Owner + " type '" + Script.typeName + "' is not a concrete " + ExpectedRoot + ".";
                    return false;
                }
            }
            Failure = string.Empty;
            return true;
        }

        static bool TryIndexAssemblies(
            string ManagedPath, out Dictionary<string, string> Assemblies, out string Failure)
        {
            Assemblies = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var Path in Directory.EnumerateFiles(ManagedPath, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    AssemblyName Identity;
                    try { Identity = AssemblyName.GetAssemblyName(Path); }
                    catch (BadImageFormatException) { continue; }
                    if (!Assemblies.TryAdd(Identity.Name, Path))
                    {
                        Failure = "PEAK Managed contains duplicate assembly identity '" + Identity.Name + "'.";
                        return false;
                    }
                }
            }
            catch (Exception Exception) when (Exception is IOException || Exception is UnauthorizedAccessException
                || Exception is ArgumentException || Exception is System.Security.SecurityException || Exception is FileLoadException)
            {
                Assemblies = null;
                Failure = "PEAK managed metadata cannot be inspected: " + Exception.Message;
                return false;
            }
            Failure = string.Empty;
            return true;
        }

        static bool ValidDeclaration(
            TypeDefinition Type, PllCreatorContractType Declaration, string Owner, out string Failure)
        {
            var Kind = Type.IsEnum ? "enum" : Type.IsInterface ? "interface" : "class";
            var BaseType = Type.BaseType == null ? string.Empty : ContractTypeName(Type.BaseType);
            if (!string.Equals(Kind, Declaration.kind, StringComparison.Ordinal)
                || !string.Equals(BaseType, Declaration.baseType, StringComparison.Ordinal)
                || Type.IsAbstract != Declaration.isAbstract || !Type.IsEnum && Type.IsSerializable != Declaration.isSerializable)
            {
                Failure = Owner + " type '" + Declaration.name + "' does not match the required declaration.";
                return false;
            }
            if (!Type.IsEnum) { Failure = string.Empty; return true; }
            var Underlying = Type.Fields.FirstOrDefault(Field => Field.Name == "value__");
            if (Underlying == null || !string.Equals(ContractTypeName(Underlying.FieldType),
                    Declaration.enumUnderlyingType, StringComparison.Ordinal))
            {
                Failure = Owner + " enum '" + Declaration.name + "' has an incompatible underlying type.";
                return false;
            }
            var Index = 0;
            foreach (var Field in Type.Fields)
            {
                if (!Field.IsPublic || !Field.IsStatic || !Field.IsLiteral) continue;
                if (Index >= Declaration.enumValues.Length)
                {
                    Failure = Owner + " enum '" + Declaration.name + "' has an incompatible value set.";
                    return false;
                }
                var Expected = Declaration.enumValues[Index++];
                if (!string.Equals(Field.Name, Expected.name, StringComparison.Ordinal)
                    || !string.Equals(Convert.ToString(Field.Constant, CultureInfo.InvariantCulture),
                        Expected.value, StringComparison.Ordinal))
                {
                    Failure = Owner + " enum '" + Declaration.name + "' changed value '" + Expected.name + "'.";
                    return false;
                }
            }
            if (Index != Declaration.enumValues.Length)
            {
                Failure = Owner + " enum '" + Declaration.name + "' has an incompatible value set.";
                return false;
            }
            Failure = string.Empty;
            return true;
        }

        static bool ValidFields(TypeDefinition Type, PllCreatorContractType Declaration, string Owner, out string Failure)
        {
            var Fields = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal);
            foreach (var Field in Type.Fields)
            {
                if (!Fields.TryAdd(Field.Name, Field))
                {
                    Failure = Owner + " type '" + Declaration.name + "' has duplicate field names.";
                    return false;
                }
            }
            foreach (var Field in Declaration.fields)
            {
                FieldDefinition Actual;
                if (!Fields.TryGetValue(Field.name, out Actual)
                    || !string.Equals(ContractTypeName(Actual.FieldType), Field.typeName, StringComparison.Ordinal)
                    || Actual.IsPublic != Field.isPublic
                    || HasAttribute(Actual, "UnityEngine.SerializeReference") != Field.serializeReference
                    || !FormerNames(Actual).SequenceEqual(Field.formerNames, StringComparer.Ordinal))
                {
                    Failure = Owner + " field '" + Declaration.name + "." + Field.name + "' does not match the required contract.";
                    return false;
                }
            }
            if (!Declaration.serializedSurface) { Failure = string.Empty; return true; }
            var ExpectedSerialized = new HashSet<string>(Declaration.fields.Where(Field => Field.serialized)
                .Select(Field => Field.name + "|" + Field.typeName + "|"
                    + (Field.serializeReference ? "reference" : "value")), StringComparer.Ordinal);
            var ActualSerialized = new HashSet<string>(StringComparer.Ordinal);
            foreach (var Field in Type.Fields)
            {
                var SerializeReference = HasAttribute(Field, "UnityEngine.SerializeReference");
                if (!Field.IsStatic && !Field.IsLiteral && !Field.IsInitOnly && !Field.IsNotSerialized
                    && (Field.IsPublic || SerializeReference || HasAttribute(Field, "UnityEngine.SerializeField"))
                    && SupportedSerializedShape(Field.FieldType, false, SerializeReference))
                    ActualSerialized.Add(Field.Name + "|" + ContractTypeName(Field.FieldType) + "|"
                        + (SerializeReference ? "reference" : "value"));
            }
            if (!ActualSerialized.SetEquals(ExpectedSerialized))
            {
                Failure = Owner + " type '" + Declaration.name + "' changed its serialized field surface.";
                return false;
            }
            Failure = string.Empty;
            return true;
        }

        static bool SupportedSerializedShape(TypeReference Type, bool Container, bool SerializeReference)
        {
            if (Type is ArrayType Array)
                return !Container && Array.Rank == 1 && SupportedSerializedShape(Array.ElementType, true, SerializeReference);
            if (Type is GenericInstanceType Generic)
                return !Container && string.Equals(Generic.ElementType.FullName,
                    "System.Collections.Generic.List`1", StringComparison.Ordinal)
                    && SupportedSerializedShape(Generic.GenericArguments[0], true, SerializeReference);
            if (Type.IsPrimitive || Type.FullName == "System.String") return true;
            if (Type is GenericParameter) return false;
            var Definition = Type.Resolve();
            if (Definition == null) throw new TypeLoadException("Type '" + Type.FullName + "' is unavailable.");
            var Assembly = Definition.Module.Assembly.Name.Name;
            if (Definition.IsEnum || Assembly == "UnityEngine" || Assembly.StartsWith("UnityEngine.", StringComparison.Ordinal)
                || DerivesFrom(Definition, "UnityEngine.Object")) return true;
            if (SerializeReference) return Definition.IsInterface || !Definition.IsValueType && Definition.IsClass;
            return Definition.IsSerializable && !Definition.IsAbstract && !Definition.IsInterface;
        }

        static bool ValidMethod(
            IReadOnlyDictionary<string, TypeDefinition> Types, PllCreatorContractMethod Method, string Owner, out string Failure)
        {
            TypeDefinition Declaring;
            if (!Types.TryGetValue(Method.declaringType, out Declaring))
            {
                Failure = "The facade contract method owner '" + Method.declaringType + "' is invalid.";
                return false;
            }
            foreach (var Candidate in Declaring.Methods)
            {
                if (Candidate.Name != Method.name || Candidate.Parameters.Count != Method.parameterTypes.Length) continue;
                var Match = true;
                for (var i = 0; i < Candidate.Parameters.Count; i++)
                    if (!string.Equals(ContractTypeName(Candidate.Parameters[i].ParameterType),
                            Method.parameterTypes[i], StringComparison.Ordinal))
                    {
                        Match = false;
                        break;
                    }
                if (Match && string.Equals(ContractTypeName(Candidate.ReturnType), Method.returnType, StringComparison.Ordinal)
                    && string.Equals(MethodVisibility(Candidate), Method.visibility, StringComparison.Ordinal)
                    && Candidate.IsStatic == Method.isStatic && Candidate.IsAbstract == Method.isAbstract
                    && Candidate.IsVirtual == Method.isVirtual)
                {
                    Failure = string.Empty;
                    return true;
                }
            }
            Failure = Owner + " member '" + Method.declaringType + "." + Method.name + "' does not match the required contract.";
            return false;
        }

        static bool ValidVisibility(string Value) => Value == "public" || Value == "family"
            || Value == "familyOrAssembly" || Value == "familyAndAssembly" || Value == "assembly" || Value == "private";

        static string MethodVisibility(MethodDefinition Method)
        {
            if (Method.IsPublic) return "public";
            if (Method.IsFamily) return "family";
            if (Method.IsFamilyOrAssembly) return "familyOrAssembly";
            if (Method.IsFamilyAndAssembly) return "familyAndAssembly";
            return Method.IsAssembly ? "assembly" : "private";
        }

        static IEnumerable<string> FormerNames(FieldDefinition Field) => Field.CustomAttributes
            .Where(Attribute => Attribute.AttributeType.FullName == "UnityEngine.Serialization.FormerlySerializedAsAttribute")
            .Select(Attribute => Attribute.ConstructorArguments.Count == 1
                ? Attribute.ConstructorArguments[0].Value as string ?? string.Empty : string.Empty);

        static bool HasAttribute(Mono.Cecil.ICustomAttributeProvider Member, string Name) =>
            Member.CustomAttributes.Any(Attribute => Attribute.AttributeType.FullName == Name);

        static bool DerivesFrom(TypeReference Type, string Name)
        {
            var Visited = new HashSet<TypeDefinition>();
            for (var Current = Type; Current != null;)
            {
                if (Current.FullName == Name) return true;
                var Definition = Current.Resolve();
                if (Definition == null) throw new TypeLoadException("Type '" + Current.FullName + "' is unavailable.");
                if (!Visited.Add(Definition))
                    throw new BadImageFormatException("Type '" + Type.FullName + "' has a cyclic base-type hierarchy.");
                Current = Definition.BaseType;
            }
            return false;
        }

        internal static string ContractTypeName(TypeReference Type)
        {
            if (Type is ArrayType Array) return ContractTypeName(Array.ElementType) + "[]";
            if (Type is GenericParameter Parameter) return "!" + Parameter.Position.ToString(CultureInfo.InvariantCulture);
            if (Type is GenericInstanceType Generic)
            {
                var Name = Generic.ElementType.FullName.Replace('/', '.');
                var Tick = Name.LastIndexOf('`');
                if (Tick >= 0) Name = Name.Substring(0, Tick);
                return Name + "<" + string.Join(",", Generic.GenericArguments.Select(ContractTypeName)) + ">";
            }
            if (Type.HasGenericParameters)
            {
                var Name = Type.FullName.Replace('/', '.');
                var Tick = Name.LastIndexOf('`');
                if (Tick >= 0) Name = Name.Substring(0, Tick);
                return Name + "<" + string.Join(",", Type.GenericParameters.Select(Parameter => ContractTypeName(Parameter))) + ">";
            }
            return Type.FullName.Replace('/', '.');
        }
    }

    [Serializable]
    sealed class PllCreatorContractType
    {
        public string name;
        public string metadataName;
        public string kind;
        public string baseType;
        public bool isAbstract;
        public bool isSerializable;
        public bool serializedSurface;
        public string enumUnderlyingType;
        public PllCreatorContractEnumValue[] enumValues;
        public PllCreatorContractField[] fields;
    }

    [Serializable]
    sealed class PllCreatorContractField
    {
        public string name;
        public string typeName;
        public bool serialized;
        public bool serializeReference;
        public bool isPublic;
        public string[] formerNames;
    }

    [Serializable]
    sealed class PllCreatorContractEnumValue
    {
        public string name;
        public string value;
    }

    [Serializable]
    sealed class PllCreatorContractMethod
    {
        public string declaringType;
        public string name;
        public string returnType;
        public string[] parameterTypes;
        public string visibility;
        public bool isStatic;
        public bool isAbstract;
        public bool isVirtual;
    }

    [Serializable]
    sealed class PllCreatorContractLayer
    {
        public string name;
        public int index;
    }

    [Serializable]
    sealed class PllCreatorContractScript
    {
        public string typeName;
        public string template;
    }
}
