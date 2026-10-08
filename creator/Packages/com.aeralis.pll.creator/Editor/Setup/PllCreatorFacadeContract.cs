using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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

        internal static string TemplateName(string metadataName)
        {
            var topLevel = metadataName.Split('/')[0];
            return topLevel.Substring(topLevel.LastIndexOf('.') + 1).Split('`')[0] + ".cs.template";
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
            var Loaded = new Dictionary<string, ReflectionAssembly>(StringComparer.OrdinalIgnoreCase);
            ReflectionAssembly Load(string AssemblyPath)
            {
                ReflectionAssembly Assembly;
                if (Loaded.TryGetValue(AssemblyPath, out Assembly)) return Assembly;
                Assembly = ReflectionAssembly.ReflectionOnlyLoad(File.ReadAllBytes(AssemblyPath));
                Loaded.Add(AssemblyPath, Assembly);
                return Assembly;
            }
            ResolveEventHandler Resolver = (Sender, Arguments) =>
            {
                string AssemblyPath;
                return Assemblies.TryGetValue(new AssemblyName(Arguments.Name).Name, out AssemblyPath)
                    ? Load(AssemblyPath) : null;
            };
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += Resolver;
            try
            {
                var Source = Load(SourcePath);
                if (!string.Equals(Source.GetName().Name, sourceAssembly, StringComparison.Ordinal))
                {
                    Failure = "PEAK's Assembly-CSharp.dll has assembly identity '" + Source.GetName().Name + "'.";
                    return false;
                }
                if (!TryValidateAssembly(Source, "PEAK", out _, out Failure)) return false;
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
                    foreach (var Reference in Load(AssemblyPath).GetReferencedAssemblies())
                        if (!PllCreatorProviderInventory.FrameworkAssembly(Reference.Name)
                            && !string.Equals(Reference.Name, sourceAssembly, StringComparison.Ordinal))
                            Queue.Enqueue(Reference.Name);
                }
                Dependencies = Result.OrderBy(Name => Name, StringComparer.Ordinal)
                    .Select(Name => Assemblies[Name]).ToArray();
                Failure = string.Empty;
                return true;
            }
            catch (Exception InspectionException) when (InspectionException is IOException
                || InspectionException is UnauthorizedAccessException || InspectionException is ArgumentException
                || InspectionException is BadImageFormatException || InspectionException is ReflectionTypeLoadException
                || InspectionException is TypeLoadException || InspectionException is CustomAttributeFormatException
                || InspectionException is System.Security.SecurityException)
            {
                Failure = "PEAK metadata cannot be inspected: " + InspectionException.Message;
                return false;
            }
            finally
            {
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= Resolver;
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
                Dictionary<string, Type> ActualTypes;
                if (!TryValidateAssembly(Source, "Generated", out ActualTypes, out Failure)) return false;
                Types = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Type>(ActualTypes);
                return true;
            }
            catch (Exception Exception) when (Exception is ArgumentException || Exception is FileNotFoundException
                                              || Exception is FileLoadException || Exception is TypeLoadException
                                              || Exception is ReflectionTypeLoadException
                                              || Exception is CustomAttributeFormatException)
            {
                Failure = "Generated facade metadata could not be inspected: " + Exception.Message;
                return false;
            }
        }

        bool TryValidateAssembly(
            ReflectionAssembly Source, string Owner, out Dictionary<string, Type> Types, out string Failure)
        {
            Types = null;
            var ActualTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var Declaration in types)
            {
                var TypeValue = Source.GetType(Declaration.metadataName.Replace('/', '+'), false);
                if (TypeValue == null)
                {
                    Failure = Owner + " type '" + Declaration.name + "' is unavailable.";
                    return false;
                }
                if (!ValidDeclaration(TypeValue, Declaration, Owner, out Failure)) return false;
                ActualTypes.Add(Declaration.name, TypeValue);
            }
            foreach (var Declaration in types)
                if (!ValidFields(ActualTypes[Declaration.name], Declaration, Owner, out Failure)) return false;
            foreach (var Method in methods)
                if (!ValidMethod(ActualTypes, Method, Owner, out Failure)) return false;
            foreach (var Script in scripts)
            {
                var TypeValue = ActualTypes[Script.typeName];
                var ExpectedRoot = string.Equals(Script.typeName, "DayNightProfile", StringComparison.Ordinal)
                    ? "UnityEngine.ScriptableObject" : "UnityEngine.MonoBehaviour";
                if (!DerivesFrom(TypeValue, ExpectedRoot) || TypeValue.IsAbstract)
                {
                    Failure = Owner + " type '" + Script.typeName + "' is not a concrete " + ExpectedRoot + ".";
                    return false;
                }
            }
            Types = ActualTypes;
            Failure = string.Empty;
            return true;
        }

        static bool TryIndexAssemblies(
            string managedPath, out Dictionary<string, string> assemblies, out string failure)
        {
            assemblies = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var path in Directory.GetFiles(managedPath, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    AssemblyName identity;
                    try
                    {
                        identity = AssemblyName.GetAssemblyName(path);
                    }
                    catch (BadImageFormatException)
                    {
                        continue;
                    }
                    if (assemblies.ContainsKey(identity.Name))
                    {
                        failure = "PEAK Managed contains duplicate assembly identity '" + identity.Name + "'.";
                        return false;
                    }
                    assemblies.Add(identity.Name, path);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                                              || exception is ArgumentException || exception is System.Security.SecurityException
                                              || exception is FileLoadException)
            {
                assemblies = null;
                failure = "PEAK managed metadata cannot be inspected: " + exception.Message;
                return false;
            }
            failure = string.Empty;
            return true;
        }

        static bool ValidDeclaration(
            Type type, PllCreatorContractType declaration, string owner, out string failure)
        {
            var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : "class";
            var baseType = type.BaseType == null ? string.Empty : ContractTypeName(type.BaseType);
            if (!string.Equals(kind, declaration.kind, StringComparison.Ordinal)
                || !string.Equals(baseType, declaration.baseType, StringComparison.Ordinal)
                || type.IsAbstract != declaration.isAbstract
                || !type.IsEnum && type.IsSerializable != declaration.isSerializable)
            {
                failure = owner + " type '" + declaration.name + "' does not match the required declaration.";
                return false;
            }
            if (!type.IsEnum)
            {
                failure = string.Empty;
                return true;
            }
            if (!string.Equals(ContractTypeName(Enum.GetUnderlyingType(type)), declaration.enumUnderlyingType,
                    StringComparison.Ordinal))
            {
                failure = owner + " enum '" + declaration.name + "' has an incompatible underlying type.";
                return false;
            }
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(value => value.IsLiteral).OrderBy(value => value.MetadataToken).ToArray();
            if (fields.Length != declaration.enumValues.Length)
            {
                failure = owner + " enum '" + declaration.name + "' has an incompatible value set.";
                return false;
            }
            for (var i = 0; i < fields.Length; i++)
            {
                var expected = declaration.enumValues[i];
                var actual = Convert.ToString(fields[i].GetRawConstantValue(), CultureInfo.InvariantCulture);
                if (!string.Equals(fields[i].Name, expected.name, StringComparison.Ordinal)
                    || !string.Equals(actual, expected.value, StringComparison.Ordinal))
                {
                    failure = owner + " enum '" + declaration.name + "' changed value '" + expected.name + "'.";
                    return false;
                }
            }
            failure = string.Empty;
            return true;
        }

        static bool ValidFields(Type type, PllCreatorContractType declaration, string owner, out string failure)
        {
            foreach (var field in declaration.fields)
            {
                var actual = type.GetField(field.name, BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (actual == null || !string.Equals(ContractTypeName(actual.FieldType), field.typeName,
                        StringComparison.Ordinal) || actual.IsPublic != field.isPublic
                    || HasAttribute(actual, "UnityEngine.SerializeReference") != field.serializeReference
                    || !FormerNames(actual).SequenceEqual(field.formerNames, StringComparer.Ordinal))
                {
                    failure = owner + " field '" + declaration.name + "." + field.name
                        + "' does not match the required contract.";
                    return false;
                }
            }
            if (!declaration.serializedSurface)
            {
                failure = string.Empty;
                return true;
            }
            var ExpectedSerialized = new HashSet<string>(declaration.fields.Where(Value => Value.serialized)
                .Select(SerializedFieldIdentity), StringComparer.Ordinal);
            var actualSerialized = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                bool serializeReference;
                if (IsSerializedField(field, out serializeReference)
                    && SupportedSerializedShape(field.FieldType, false, serializeReference))
                    actualSerialized.Add(field.Name + "|" + ContractTypeName(field.FieldType) + "|"
                        + (serializeReference ? "reference" : "value"));
            }
            if (!actualSerialized.SetEquals(ExpectedSerialized))
            {
                failure = owner + " type '" + declaration.name + "' changed its serialized field surface.";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        static string SerializedFieldIdentity(PllCreatorContractField field)
        {
            return field.name + "|" + field.typeName + "|" + (field.serializeReference ? "reference" : "value");
        }

        static bool IsSerializedField(FieldInfo field, out bool serializeReference)
        {
            serializeReference = HasAttribute(field, "UnityEngine.SerializeReference");
            return !field.IsStatic && !field.IsLiteral && !field.IsInitOnly
                && (field.Attributes & FieldAttributes.NotSerialized) == 0
                && (field.IsPublic || serializeReference || HasAttribute(field, "UnityEngine.SerializeField"));
        }

        static bool SupportedSerializedShape(Type type, bool container, bool serializeReference)
        {
            if (type.IsArray)
                return !container && type.GetArrayRank() == 1
                    && SupportedSerializedShape(type.GetElementType(), true, serializeReference);
            if (type.IsGenericType)
                return !container && string.Equals(type.GetGenericTypeDefinition().FullName,
                           "System.Collections.Generic.List`1", StringComparison.Ordinal)
                    && SupportedSerializedShape(type.GetGenericArguments()[0], true, serializeReference);
            if (type.IsEnum || type.IsPrimitive || string.Equals(type.FullName, "System.String", StringComparison.Ordinal)
                || IsUnityEngineType(type) || DerivesFrom(type, "UnityEngine.Object")) return true;
            if (serializeReference) return type.IsClass || type.IsInterface;
            return type.IsSerializable && !type.IsAbstract && !type.IsInterface;
        }

        static bool ValidMethod(
            IReadOnlyDictionary<string, Type> typesByName, PllCreatorContractMethod method,
            string owner, out string failure)
        {
            Type declaring;
            if (!typesByName.TryGetValue(method.declaringType, out declaring))
            {
                failure = "The facade contract method owner '" + method.declaringType + "' is invalid.";
                return false;
            }
            var candidates = string.Equals(method.name, ".ctor", StringComparison.Ordinal)
                ? declaring.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Cast<MethodBase>()
                : declaring.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(value => value.Name == method.name)
                    .Cast<MethodBase>();
            foreach (var candidate in candidates)
            {
                var parameters = candidate.GetParameters();
                if (parameters.Length != method.parameterTypes.Length) continue;
                var match = true;
                for (var i = 0; i < parameters.Length; i++)
                    if (!string.Equals(ContractTypeName(parameters[i].ParameterType), method.parameterTypes[i],
                            StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                if (!match) continue;
                var info = candidate as MethodInfo;
                if ((info == null && string.Equals(method.returnType, "System.Void", StringComparison.Ordinal)
                    || info != null && string.Equals(ContractTypeName(info.ReturnType), method.returnType,
                        StringComparison.Ordinal))
                    && string.Equals(MethodVisibility(candidate), method.visibility, StringComparison.Ordinal)
                    && candidate.IsStatic == method.isStatic && candidate.IsAbstract == method.isAbstract
                    && candidate.IsVirtual == method.isVirtual)
                {
                    failure = string.Empty;
                    return true;
                }
            }
            failure = owner + " member '" + method.declaringType + "." + method.name
                + "' does not match the required contract.";
            return false;
        }

        static bool ValidVisibility(string value)
        {
            return string.Equals(value, "public", StringComparison.Ordinal)
                || string.Equals(value, "family", StringComparison.Ordinal)
                || string.Equals(value, "familyOrAssembly", StringComparison.Ordinal)
                || string.Equals(value, "familyAndAssembly", StringComparison.Ordinal)
                || string.Equals(value, "assembly", StringComparison.Ordinal)
                || string.Equals(value, "private", StringComparison.Ordinal);
        }

        static string MethodVisibility(MethodBase method)
        {
            if (method.IsPublic) return "public";
            if (method.IsFamily) return "family";
            if (method.IsFamilyOrAssembly) return "familyOrAssembly";
            if (method.IsFamilyAndAssembly) return "familyAndAssembly";
            if (method.IsAssembly) return "assembly";
            return "private";
        }

        static IEnumerable<string> FormerNames(FieldInfo field)
        {
            return CustomAttributeData.GetCustomAttributes(field)
                .Where(value => string.Equals(value.Constructor.DeclaringType.FullName,
                    "UnityEngine.Serialization.FormerlySerializedAsAttribute", StringComparison.Ordinal))
                .Select(value => value.ConstructorArguments.Count == 1
                    ? value.ConstructorArguments[0].Value as string ?? string.Empty : string.Empty);
        }

        static bool HasAttribute(MemberInfo member, string fullName)
        {
            return CustomAttributeData.GetCustomAttributes(member).Any(value =>
                string.Equals(value.Constructor.DeclaringType.FullName, fullName, StringComparison.Ordinal));
        }

        static bool IsUnityEngineType(Type type)
        {
            var name = type.Assembly.GetName().Name;
            return string.Equals(name, "UnityEngine", StringComparison.Ordinal)
                || name.StartsWith("UnityEngine.", StringComparison.Ordinal);
        }

        static bool DerivesFrom(Type type, string fullName)
        {
            for (var current = type; current != null; current = current.BaseType)
                if (string.Equals(current.FullName, fullName, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static string ContractTypeName(Type type)
        {
            if (type.IsArray) return ContractTypeName(type.GetElementType()) + "[]";
            if (type.IsGenericParameter) return "!" + type.GenericParameterPosition.ToString(CultureInfo.InvariantCulture);
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                var name = (definition.FullName ?? definition.Name).Replace('+', '.');
                var tick = name.LastIndexOf('`');
                if (tick >= 0) name = name.Substring(0, tick);
                return name + "<" + string.Join(",", type.GetGenericArguments().Select(ContractTypeName)) + ">";
            }
            return (type.FullName ?? type.Name).Replace('+', '.');
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
