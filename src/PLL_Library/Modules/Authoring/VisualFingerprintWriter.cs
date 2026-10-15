using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PLL.Library;

internal sealed partial class VisualFingerprintWriter
{
    const string ParticleProtocol = "PLL/ParticleVisualGraph/v3";
    static readonly Dictionary<Type, ValueMemberDescriptor> ValueMemberDescriptors = new();
    static readonly Dictionary<Type, FieldInfo[]> SerializedFieldDescriptors = new();
    readonly BinaryWriter Writer;
    readonly Transform GraphRoot;
    readonly Transform IdentityRoot;
    readonly Dictionary<int, int> ResourceOrdinals = new();

    VisualFingerprintWriter(BinaryWriter Writer, Transform GraphRoot, Transform IdentityRoot)
    {
        this.Writer = Writer;
        this.GraphRoot = GraphRoot;
        this.IdentityRoot = IdentityRoot;
    }

    internal static byte[] HashParticleGraph(
        Transform Root,
        Transform IdentityRoot,
        IReadOnlyList<ParticleSystem> Particles,
        IReadOnlyList<ParticleSystem> Roots) =>
        ManifestEncoding.Hash(Writer =>
        {
            Writer.Write(ParticleProtocol);
            var Value = new VisualFingerprintWriter(Writer, Root, IdentityRoot);
            Value.WriteHierarchy(Root);
            Writer.Write(Particles.Count);
            foreach (var Particle in Particles) Value.WriteHierarchyReference(Particle);
            Writer.Write(Roots.Count);
            foreach (var Particle in Roots) Value.WriteHierarchyReference(Particle);
        });

    internal static byte[] HashSerializedObject(UnityEngine.Object Value, string Domain)
    {
        if (!Value) throw new ArgumentNullException(nameof(Value));
        return ManifestEncoding.Hash(Writer =>
        {
            Writer.Write(Domain);
            var Fingerprint = new VisualFingerprintWriter(Writer, Value is Component Component ? Component.transform : null!, null!);
            Fingerprint.WriteResource(Value);
        });
    }

    internal static byte[] HashEntityAppearanceMesh(Mesh Mesh) =>
        ManifestEncoding.Hash(Writer =>
        {
            Writer.Write("PLL/EntityAppearanceMesh/v1");
            var Fingerprint = new VisualFingerprintWriter(Writer, null!, null!);
            Fingerprint.WriteMesh(Mesh);
            var Bindposes = Mesh.bindposes; Writer.Write(Bindposes.Length);
            foreach (var Bindpose in Bindposes)
                for (var i = 0; i < 16; i++) Fingerprint.WriteFloat(Bindpose[i]);
            var Counts = Mesh.GetBonesPerVertex(); Writer.Write(Counts.Length);
            foreach (var Count in Counts) Writer.Write(Count);
            var Weights = Mesh.GetAllBoneWeights(); Writer.Write(Weights.Length);
            foreach (var Weight in Weights)
            {
                Writer.Write(Weight.boneIndex); Fingerprint.WriteFloat(Weight.weight);
            }
        });

    internal static byte[] HashGeneratedPropMesh(Mesh Mesh, long ByteBudget, out long ByteCount)
    {
        ByteCount = 0;
        using var Sha = SHA256.Create();
        using (var Stream = new CryptoStream(System.IO.Stream.Null, Sha, CryptoStreamMode.Write))
        using (var Writer = new BinaryWriter(Stream, new UTF8Encoding(false, true)))
        {
            Writer.Write("PLL/HookedPropRawMesh/v1"); Writer.Write((int)Mesh.indexFormat);
            ByteCount = new VisualFingerprintWriter(Writer, null!, null!).WriteMesh(Mesh, ByteBudget);
        }
        return Sha.Hash!;
    }

    void WriteHierarchy(Transform Value)
    {
        Writer.Write(Value.gameObject.activeSelf);
        Writer.Write(Value.gameObject.layer);
        WriteVector(Value.localPosition);
        WriteQuaternion(Value.localRotation);
        WriteVector(Value.localScale);
        var Components = Value.GetComponents<Component>();
        Writer.Write(Components.Length);
        foreach (var Component in Components)
        {
            if (!Component) throw new InvalidOperationException("A particle visual graph contains a missing component.");
            Writer.Write(DeterministicObjectIdentity.StableTypeName(Component.GetType()));
            switch (Component)
            {
                case ParticleSystem Particle:
                    WriteParticleSystem(Particle);
                    break;
                case ParticleSystemRenderer Renderer:
                    WriteRenderer(Renderer);
                    WriteParticleRenderer(Renderer);
                    break;
                case Renderer Renderer:
                    WriteRenderer(Renderer);
                    break;
                case MeshFilter Filter:
                    WriteUnityObject(Filter.sharedMesh);
                    break;
                case ParticleSystemForceField Force:
                    Writer.Write(Force.enabled);
                    WriteWritableProperties(Force);
                    break;
                case Light Light:
                    NativeLightSnapshot.Capture(Light).WriteManifest(Writer);
                    break;
            }
        }
        Writer.Write(Value.childCount);
        for (var i = 0; i < Value.childCount; i++) WriteHierarchy(Value.GetChild(i));
    }

    void WriteParticleSystem(ParticleSystem Particle)
    {
        Writer.Write(Particle.useAutoRandomSeed);
        if (!Particle.useAutoRandomSeed) Writer.Write(Particle.randomSeed);
        WriteModule(Particle.main);
        var Emission = Particle.emission;
        WriteModule(Emission);
        var Bursts = new ParticleSystem.Burst[Emission.burstCount];
        Writer.Write(Emission.GetBursts(Bursts));
        foreach (var Burst in Bursts) WriteValue(Burst);
        WriteModule(Particle.shape);
        WriteModule(Particle.velocityOverLifetime);
        WriteModule(Particle.limitVelocityOverLifetime);
        WriteModule(Particle.inheritVelocity);
        WriteModule(Particle.lifetimeByEmitterSpeed);
        WriteModule(Particle.forceOverLifetime);
        WriteModule(Particle.colorOverLifetime);
        WriteModule(Particle.colorBySpeed);
        WriteModule(Particle.sizeOverLifetime);
        WriteModule(Particle.sizeBySpeed);
        WriteModule(Particle.rotationOverLifetime);
        WriteModule(Particle.rotationBySpeed);
        var ExternalForces = Particle.externalForces;
        WriteModule(ExternalForces);
        Writer.Write(ExternalForces.influenceCount);
        for (var i = 0; i < ExternalForces.influenceCount; i++) WriteUnityObject(ExternalForces.GetInfluence(i));
        WriteModule(Particle.noise);
        var Collision = Particle.collision;
        WriteModule(Collision);
        Writer.Write(Collision.planeCount);
        for (var i = 0; i < Collision.planeCount; i++) WriteUnityObject(Collision.GetPlane(i));
        var Trigger = Particle.trigger;
        WriteModule(Trigger);
        Writer.Write(Trigger.colliderCount);
        for (var i = 0; i < Trigger.colliderCount; i++) WriteUnityObject(Trigger.GetCollider(i));
        var SubEmitters = Particle.subEmitters;
        WriteModule(SubEmitters);
        Writer.Write(SubEmitters.subEmittersCount);
        for (var i = 0; i < SubEmitters.subEmittersCount; i++)
        {
            WriteUnityObject(SubEmitters.GetSubEmitterSystem(i));
            Writer.Write((int)SubEmitters.GetSubEmitterType(i));
            Writer.Write((int)SubEmitters.GetSubEmitterProperties(i));
            Writer.Write(SubEmitters.GetSubEmitterEmitProbability(i));
        }
        var Texture = Particle.textureSheetAnimation;
        WriteModule(Texture);
        Writer.Write(Texture.spriteCount);
        for (var i = 0; i < Texture.spriteCount; i++) WriteUnityObject(Texture.GetSprite(i));
        WriteModule(Particle.lights);
        WriteModule(Particle.trails);
        var Custom = Particle.customData;
        WriteModule(Custom);
        for (var Stream = ParticleSystemCustomData.Custom1; Stream <= ParticleSystemCustomData.Custom2; Stream++)
        {
            Writer.Write((int)Stream);
            Writer.Write((int)Custom.GetMode(Stream));
            WriteValue(Custom.GetColor(Stream));
            var Count = Custom.GetVectorComponentCount(Stream);
            Writer.Write(Count);
            for (var i = 0; i < Count; i++) WriteValue(Custom.GetVector(Stream, i));
        }
    }

    void WriteModule<T>(T Module) where T : struct
    {
        Writer.Write(DeterministicObjectIdentity.StableTypeName(typeof(T)));
        WriteWritableProperties(Module);
    }

    void WriteRenderer(Renderer Renderer)
    {
        WriteWritableProperties(Renderer);
        var Materials = Renderer.sharedMaterials;
        Writer.Write(Materials.Length);
        foreach (var Material in Materials) WriteUnityObject(Material);
    }

    void WriteParticleRenderer(ParticleSystemRenderer Renderer)
    {
        WriteUnityObject(Renderer.trailMaterial);
        var Meshes = new Mesh[Renderer.meshCount];
        Writer.Write(Renderer.GetMeshes(Meshes));
        foreach (var Mesh in Meshes) WriteUnityObject(Mesh);
    }

    void WriteValue(object? Value)
    {
        if (Value is null)
        {
            Writer.Write((byte)0);
            return;
        }
        Writer.Write((byte)1);
        var Type = Value.GetType();
        Writer.Write(DeterministicObjectIdentity.StableTypeName(Type));
        if (Value is UnityEngine.Object UnityObject)
        {
            WriteUnityObject(UnityObject);
            return;
        }
        if (Type.IsEnum)
        {
            Writer.Write(Convert.ToInt64(Value));
            return;
        }
        switch (Value)
        {
            case bool Boolean: Writer.Write(Boolean); return;
            case byte Number: Writer.Write(Number); return;
            case sbyte Number: Writer.Write(Number); return;
            case short Number: Writer.Write(Number); return;
            case ushort Number: Writer.Write(Number); return;
            case int Number: Writer.Write(Number); return;
            case uint Number: Writer.Write(Number); return;
            case long Number: Writer.Write(Number); return;
            case ulong Number: Writer.Write(Number); return;
            case float Number: WriteFloat(Number); return;
            case double Number: WriteDouble(Number); return;
            case char Character: Writer.Write(Character); return;
            case string Text: Writer.Write(Text); return;
            case Vector2 Vector: WriteFloat(Vector.x); WriteFloat(Vector.y); return;
            case Vector3 Vector: WriteVector(Vector); return;
            case Vector4 Vector: WriteFloat(Vector.x); WriteFloat(Vector.y); WriteFloat(Vector.z); WriteFloat(Vector.w); return;
            case Vector2Int Vector: Writer.Write(Vector.x); Writer.Write(Vector.y); return;
            case Vector3Int Vector: Writer.Write(Vector.x); Writer.Write(Vector.y); Writer.Write(Vector.z); return;
            case Quaternion Rotation: WriteQuaternion(Rotation); return;
            case Color Color: WriteFloat(Color.r); WriteFloat(Color.g); WriteFloat(Color.b); WriteFloat(Color.a); return;
            case Color32 Color: Writer.Write(Color.r); Writer.Write(Color.g); Writer.Write(Color.b); Writer.Write(Color.a); return;
            case Bounds Bounds: WriteVector(Bounds.center); WriteVector(Bounds.extents); return;
            case Rect Rect: WriteFloat(Rect.x); WriteFloat(Rect.y); WriteFloat(Rect.width); WriteFloat(Rect.height); return;
            case AnimationCurve Curve: WriteCurve(Curve); return;
            case Gradient Gradient: WriteGradient(Gradient); return;
            case Array Array:
                Writer.Write(Array.Length);
                foreach (var Item in Array) WriteValue(Item);
                return;
            case IList List:
                Writer.Write(List.Count);
                foreach (var Item in List) WriteValue(Item);
                return;
        }
        WriteFields(Value, Type);
    }

    void WriteFields(object Value, Type Type)
    {
        if (TryWriteValueMembers(Value)) return;
        switch (Value)
        {
            case ShaderParameters Parameters when Type == typeof(ShaderParameters):
                Writer.Write(2);
                Writer.Write(nameof(Parameters.paramValue)); WriteValue(Parameters.paramValue);
                Writer.Write(nameof(Parameters.parameter)); WriteValue(Parameters.parameter);
                Writer.Write(0);
                return;
            case ShaderParametersColors Parameters when Type == typeof(ShaderParametersColors):
                Writer.Write(2);
                Writer.Write(nameof(Parameters.paramValue)); WriteValue(Parameters.paramValue);
                Writer.Write(nameof(Parameters.parameter)); WriteValue(Parameters.parameter);
                Writer.Write(0);
                return;
            case AnimatedShaderParameters Parameters when Type == typeof(AnimatedShaderParameters):
                Writer.Write(2);
                Writer.Write(nameof(Parameters.paramValue)); WriteValue(Parameters.paramValue);
                Writer.Write(nameof(Parameters.parameter)); WriteValue(Parameters.parameter);
                Writer.Write(0);
                return;
        }
        var Members = ValueMembers(Type);
        if (Members.Fields.Length == 0 && Members.Properties.Length == 0)
            throw new InvalidOperationException($"Particle visual value type '{Type.FullName}' has no supported canonical fields.");
        Writer.Write(Members.Fields.Length);
        foreach (var Field in Members.Fields)
        {
            Writer.Write(Field.Name);
            WriteValue(Field.GetValue(Value));
        }
        Writer.Write(Members.Properties.Length);
        foreach (var Property in Members.Properties)
        {
            Writer.Write(Property.Name);
            WriteValue(Property.GetValue(Value));
        }
    }

    void WriteMembers(ParticleSystem.MinMaxCurve Value)
    {
        var Mode = Value.mode;
        Writer.Write((int)Mode);
        switch (Mode)
        {
            case ParticleSystemCurveMode.Constant: WriteFloat(Value.constant); break;
            case ParticleSystemCurveMode.Curve:
                WriteValue(Value.curve); WriteFloat(Value.curveMultiplier); break;
            case ParticleSystemCurveMode.TwoConstants:
                WriteFloat(Value.constantMin); WriteFloat(Value.constantMax); break;
            case ParticleSystemCurveMode.TwoCurves:
                WriteValue(Value.curveMin); WriteValue(Value.curveMax);
                WriteFloat(Value.curveMultiplier); break;
            default: throw new InvalidOperationException($"Particle curve mode '{Mode}' is invalid.");
        }
    }

    void WriteMembers(ParticleSystem.MinMaxGradient Value)
    {
        var Mode = Value.mode;
        Writer.Write((int)Mode);
        switch (Mode)
        {
            case ParticleSystemGradientMode.Color: WriteValue(Value.color); break;
            case ParticleSystemGradientMode.Gradient:
            case ParticleSystemGradientMode.RandomColor: WriteValue(Value.gradient); break;
            case ParticleSystemGradientMode.TwoColors:
                WriteValue(Value.colorMin); WriteValue(Value.colorMax); break;
            case ParticleSystemGradientMode.TwoGradients:
                WriteValue(Value.gradientMin); WriteValue(Value.gradientMax); break;
            default: throw new InvalidOperationException($"Particle gradient mode '{Mode}' is invalid.");
        }
    }

    void WriteUnityObject(UnityEngine.Object? Value)
    {
        Writer.Write((bool)Value);
        if (!Value) return;
        if (TryWriteHierarchyReference(Value)) return;
        Writer.Write(false);
        WriteResource(Value);
    }

    bool TryWriteHierarchyReference(UnityEngine.Object Value)
    {
        var Transform = Value switch
        {
            GameObject GameObject => GameObject.transform,
            Component Component => Component.transform,
            _ => null
        };
        if (!Transform || !GraphRoot || !TransformOwnership.Contains(GraphRoot, Transform)) return false;
        Writer.Write(true);
        WriteRelativePath(GraphRoot, Transform);
        if (Value is GameObject)
        {
            Writer.Write(-1);
            return true;
        }
        var ComponentValue = (Component)Value;
        var Components = ComponentValue.GetComponents<Component>();
        var Ordinal = Array.IndexOf(Components, ComponentValue);
        if (Ordinal < 0) throw new InvalidOperationException("A particle dependency component lost its owner.");
        Writer.Write(Ordinal);
        return true;
    }

    void WriteHierarchyReference(Component Value)
    {
        if (!TryWriteHierarchyReference(Value))
            throw new InvalidOperationException("A particle graph system is outside its visual root.");
    }

    void WriteResource(UnityEngine.Object Value)
    {
        if (Value is Shader ShaderValue) Value = NativeFogRenderRuntime.CanonicalShader(ShaderValue);
        var ResourceId = Value.GetInstanceID();
        if (ResourceOrdinals.TryGetValue(ResourceId, out var Existing))
        {
            Writer.Write(Existing);
            Writer.Write(false);
            return;
        }
        var Ordinal = ResourceOrdinals.Count;
        ResourceOrdinals.Add(ResourceId, Ordinal);
        Writer.Write(Ordinal);
        Writer.Write(true);
        Writer.Write(DeterministicObjectIdentity.StableTypeName(Value.GetType()));
        switch (Value)
        {
            case Mesh Mesh: WriteMesh(Mesh); return;
            case Material Material: WriteMaterial(Material); return;
            case Shader Shader: WriteShader(Shader); return;
            case Texture Texture: WriteTexture(Texture); return;
            case Sprite Sprite: WriteSprite(Sprite); return;
            case UnityEngine.Flare Flare:
                WriteValue(Flare.texture);
                WriteValue(Flare.textureLayout);
                WriteValue(Flare.useFog);
                WriteValue(Flare.elements);
                return;
            case Light Light when !Light.gameObject.scene.IsValid():
                NativeLightSnapshot.Capture(Light).WriteManifest(Writer);
                return;
            case ScriptableObject Scriptable:
                WriteSerializedFields(Scriptable);
                return;
            case Component Component when IdentityRoot && TransformOwnership.Contains(IdentityRoot, Component.transform):
                WriteRelativePath(IdentityRoot, Component.transform);
                Writer.Write(Array.IndexOf(Component.GetComponents<Component>(), Component));
                return;
            default:
                throw new InvalidOperationException(
                    $"Particle visual dependency '{Value.GetType().FullName}' has no stable supported fingerprint.");
        }
    }

    long WriteMesh(Mesh Mesh, long ByteBudget = long.MaxValue)
    {
        Writer.Write(Mesh.vertexCount);
        Writer.Write(Mesh.subMeshCount);
        WriteValue(Mesh.bounds);
        var Attributes = Mesh.GetVertexAttributes();
        Writer.Write(Attributes.Length);
        foreach (var Attribute in Attributes) WriteValue(Attribute);
        long ByteCount;
        if (Mesh.isReadable)
        {
            using var Data = Mesh.AcquireReadOnlyMeshData(Mesh);
            var Snapshot = Data[0];
            var Indices = Snapshot.GetIndexData<byte>();
            ByteCount = Indices.Length;
            for (var Stream = 0; Stream < Snapshot.vertexBufferCount; Stream++)
                ByteCount = checked(ByteCount + Snapshot.GetVertexData<byte>(Stream).Length);
            if (ByteCount > ByteBudget)
                throw new InvalidOperationException("Generated mesh bytes exceed the remaining output budget.");
            Writer.Write(Snapshot.vertexBufferCount);
            for (var Stream = 0; Stream < Snapshot.vertexBufferCount; Stream++)
            {
                var Bytes = Snapshot.GetVertexData<byte>(Stream);
                Writer.Write(Bytes.Length);
                Writer.Write(Bytes.AsReadOnlySpan());
            }
            Writer.Write(Indices.Length);
            Writer.Write(Indices.AsReadOnlySpan());
            for (var i = 0; i < Snapshot.subMeshCount; i++) WriteValue(Snapshot.GetSubMesh(i));
        }
        else
        {
            using var IndexBuffer = NativeMeshAssets.OpenIndexBuffer(Mesh);
            var IndexBytes = IndexBuffer is null ? 0 : checked(IndexBuffer.count * IndexBuffer.stride);
            ByteCount = IndexBytes;
            for (var Stream = 0; Stream < Mesh.vertexBufferCount; Stream++)
                ByteCount = checked(ByteCount + (long)Mesh.vertexCount * Mesh.GetVertexBufferStride(Stream));
            if (ByteCount > ByteBudget)
                throw new InvalidOperationException("Generated mesh bytes exceed the remaining output budget.");
            Writer.Write(Mesh.vertexBufferCount);
            for (var Stream = 0; Stream < Mesh.vertexBufferCount; Stream++)
            {
                var Bytes = new byte[checked(Mesh.vertexCount * Mesh.GetVertexBufferStride(Stream))];
                using var Buffer = Mesh.GetVertexBuffer(Stream);
                Buffer.GetData(Bytes);
                Writer.Write(Bytes.Length);
                Writer.Write(Bytes);
            }
            Writer.Write(IndexBytes);
            if (IndexBuffer is not null)
            {
                var Indices = new byte[IndexBytes];
                IndexBuffer.GetData(Indices);
                Writer.Write(Indices);
            }
            for (var i = 0; i < Mesh.subMeshCount; i++) WriteValue(Mesh.GetSubMesh(i));
        }
        var BlendShapes = Mesh.blendShapeCount;
        Writer.Write(BlendShapes);
        for (var i = 0; i < BlendShapes; i++)
        {
            Writer.Write(Mesh.GetBlendShapeName(i) ?? string.Empty);
            var Frames = Mesh.GetBlendShapeFrameCount(i);
            Writer.Write(Frames);
            for (var Frame = 0; Frame < Frames; Frame++)
                WriteFloat(Mesh.GetBlendShapeFrameWeight(i, Frame));
        }
        return ByteCount;
    }

    void WriteMaterial(Material Material)
    {
        var Shader = NativeFogRenderRuntime.CanonicalShader(Material.shader);
        WriteUnityObject(Shader);
        Writer.Write(Material.renderQueue);
        Writer.Write(Material.enableInstancing);
        Writer.Write(Material.doubleSidedGI);
        Writer.Write((int)Material.globalIlluminationFlags);
        var Keywords = Material.shaderKeywords;
        Array.Sort(Keywords, StringComparer.Ordinal);
        Writer.Write(Keywords.Length);
        foreach (var Keyword in Keywords) Writer.Write(Keyword ?? string.Empty);
        var Count = Shader.GetPropertyCount();
        Writer.Write(Count);
        for (var i = 0; i < Count; i++)
        {
            var Name = Shader.GetPropertyName(i);
            var Type = Shader.GetPropertyType(i);
            Writer.Write(Name);
            Writer.Write((int)Type);
            switch (Type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color: WriteValue(Material.GetColor(Name)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector: WriteValue(Material.GetVector(Name)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range: WriteFloat(Material.GetFloat(Name)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Int: Writer.Write(Material.GetInteger(Name)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    WriteUnityObject(Material.GetTexture(Name));
                    WriteValue(Material.GetTextureOffset(Name));
                    WriteValue(Material.GetTextureScale(Name));
                    break;
                default: throw new InvalidOperationException($"Shader property type '{Type}' is not supported.");
            }
        }
    }

    void WriteShader(Shader Shader)
    {
        Writer.Write(Shader.name ?? string.Empty);
        var Count = Shader.GetPropertyCount();
        Writer.Write(Count);
        for (var i = 0; i < Count; i++)
        {
            var Type = Shader.GetPropertyType(i);
            Writer.Write(Shader.GetPropertyName(i));
            Writer.Write((int)Type);
            Writer.Write((int)Shader.GetPropertyFlags(i));
            Writer.Write(Shader.GetPropertyDescription(i) ?? string.Empty);
            switch (Type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    WriteValue(Shader.GetPropertyDefaultVectorValue(i));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                    WriteFloat(Shader.GetPropertyDefaultFloatValue(i));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    WriteFloat(Shader.GetPropertyDefaultFloatValue(i));
                    WriteValue(Shader.GetPropertyRangeLimits(i));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Int:
                    Writer.Write(Shader.GetPropertyDefaultIntValue(i));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    Writer.Write((int)Shader.GetPropertyTextureDimension(i));
                    Writer.Write(Shader.GetPropertyTextureDefaultName(i) ?? string.Empty);
                    break;
                default: throw new InvalidOperationException($"Shader property type '{Type}' is not supported.");
            }
        }
    }

    void WriteTexture(Texture Texture)
    {
        Writer.Write(Texture.width);
        Writer.Write(Texture.height);
        Writer.Write(Texture.mipmapCount);
        Writer.Write((int)Texture.dimension);
        Writer.Write((int)Texture.wrapModeU);
        Writer.Write((int)Texture.wrapModeV);
        Writer.Write((int)Texture.wrapModeW);
        Writer.Write((int)Texture.filterMode);
        Writer.Write(Texture.anisoLevel);
        WriteFloat(Texture.mipMapBias);
        Writer.Write(Texture.name ?? string.Empty);
        switch (Texture)
        {
            case Texture2D _: Writer.Write((byte)1); break;
            case Texture3D _: Writer.Write((byte)2); break;
            case Cubemap _: Writer.Write((byte)3); break;
            case Texture2DArray _: Writer.Write((byte)4); break;
            case CubemapArray _: Writer.Write((byte)5); break;
            case RenderTexture _: Writer.Write((byte)6); break;
            default: Writer.Write((byte)0); break;
        }
    }

    void WriteSprite(Sprite Sprite)
    {
        WriteUnityObject(Sprite.texture);
        WriteValue(Sprite.rect);
        WriteValue(Sprite.textureRect);
        WriteValue(Sprite.pivot);
        WriteValue(Sprite.border);
        WriteFloat(Sprite.pixelsPerUnit);
        var Vertices = Sprite.vertices;
        Writer.Write(Vertices.Length);
        foreach (var Vertex in Vertices) WriteValue(Vertex);
        var Triangles = Sprite.triangles;
        Writer.Write(Triangles.Length);
        foreach (var Triangle in Triangles) Writer.Write(Triangle);
    }

    void WriteSerializedFields(object Value)
    {
        if (Value.GetType() == typeof(DayNightProfile))
        {
            var Profile = (DayNightProfile)Value;
            var Owner = DeterministicObjectIdentity.StableTypeName(typeof(DayNightProfile));
            Writer.Write(11);
            Writer.Write(Owner); Writer.Write(nameof(Profile.animatedGlobalShaderFloats)); WriteValue(Profile.animatedGlobalShaderFloats);
            Writer.Write(Owner); Writer.Write(nameof(Profile.fogGradient)); WriteValue(Profile.fogGradient);
            Writer.Write(Owner); Writer.Write(nameof(Profile.globalShaderFloats)); WriteValue(Profile.globalShaderFloats);
            Writer.Write(Owner); Writer.Write(nameof(Profile.globalStaticShaderColors)); WriteValue(Profile.globalStaticShaderColors);
            Writer.Write(Owner); Writer.Write(nameof(Profile.globalStaticShaderFloats)); WriteValue(Profile.globalStaticShaderFloats);
            Writer.Write(Owner); Writer.Write(nameof(Profile.moonIntensity)); WriteValue(Profile.moonIntensity);
            Writer.Write(Owner); Writer.Write(nameof(Profile.skyBottomGradient)); WriteValue(Profile.skyBottomGradient);
            Writer.Write(Owner); Writer.Write(nameof(Profile.skyMidGradient)); WriteValue(Profile.skyMidGradient);
            Writer.Write(Owner); Writer.Write(nameof(Profile.skyTopGradient)); WriteValue(Profile.skyTopGradient);
            Writer.Write(Owner); Writer.Write(nameof(Profile.sunGradient)); WriteValue(Profile.sunGradient);
            Writer.Write(Owner); Writer.Write(nameof(Profile.sunIntensity)); WriteValue(Profile.sunIntensity);
            return;
        }
        var Fields = SerializedFields(Value.GetType());
        Writer.Write(Fields.Length);
        foreach (var Field in Fields)
        {
            Writer.Write(DeterministicObjectIdentity.StableTypeName(Field.DeclaringType!));
            Writer.Write(Field.Name);
            WriteValue(Field.GetValue(Value));
        }
    }

    void WriteCurve(AnimationCurve Curve)
    {
        Writer.Write((int)Curve.preWrapMode);
        Writer.Write((int)Curve.postWrapMode);
        var Keys = Curve.keys;
        Writer.Write(Keys.Length);
        foreach (var Key in Keys)
        {
            WriteFloat(Key.time); WriteFloat(Key.value); WriteFloat(Key.inTangent); WriteFloat(Key.outTangent);
            WriteFloat(Key.inWeight); WriteFloat(Key.outWeight); Writer.Write((int)Key.weightedMode);
        }
    }

    void WriteGradient(Gradient Gradient)
    {
        Writer.Write((int)Gradient.mode);
        Writer.Write((int)Gradient.colorSpace);
        var Colors = Gradient.colorKeys;
        Writer.Write(Colors.Length);
        foreach (var Key in Colors) { WriteValue(Key.color); WriteFloat(Key.time); }
        var Alphas = Gradient.alphaKeys;
        Writer.Write(Alphas.Length);
        foreach (var Key in Alphas) { WriteFloat(Key.alpha); WriteFloat(Key.time); }
    }

    void WriteRelativePath(Transform Root, Transform Value)
    {
        var Path = new List<int>();
        for (var Current = Value; Current != Root; Current = Current.parent)
        {
            if (!Current || !Current.parent) throw new InvalidOperationException("A visual dependency left its structural owner.");
            Path.Add(Current.GetSiblingIndex());
        }
        Writer.Write(Path.Count);
        for (var i = Path.Count - 1; i >= 0; i--) Writer.Write(Path[i]);
    }

    void WriteVector(Vector3 Value) { WriteFloat(Value.x); WriteFloat(Value.y); WriteFloat(Value.z); }
    void WriteQuaternion(Quaternion Value)
    {
        if (Value.w < 0f || Value.w == 0f && (Value.z < 0f
            || Value.z == 0f && (Value.y < 0f || Value.y == 0f && Value.x < 0f)))
            Value = new Quaternion(-Value.x, -Value.y, -Value.z, -Value.w);
        WriteFloat(Value.x); WriteFloat(Value.y); WriteFloat(Value.z); WriteFloat(Value.w);
    }
    void WriteFloat(float Value)
    {
        if (Value == 0f) Value = 0f;
        Writer.Write(float.IsNaN(Value) ? int.MaxValue : BitConverter.SingleToInt32Bits(Value));
    }
    void WriteDouble(double Value)
    {
        if (Value == 0d) Value = 0d;
        Writer.Write(double.IsNaN(Value) ? long.MaxValue : BitConverter.DoubleToInt64Bits(Value));
    }

    static ValueMemberDescriptor ValueMembers(Type RuntimeType)
    {
        if (ValueMemberDescriptors.TryGetValue(RuntimeType, out var Existing)) return Existing;
        var Fields = RuntimeType.GetFields(BindingFlags.Instance | BindingFlags.Public);
        Array.Sort(Fields, (Left, Right) => StringComparer.Ordinal.Compare(Left.Name, Right.Name));
        var Properties = Array.FindAll(RuntimeType.GetProperties(BindingFlags.Instance | BindingFlags.Public),
            Property => Property.CanRead && Property.CanWrite && Property.GetIndexParameters().Length == 0);
        Array.Sort(Properties, (Left, Right) => StringComparer.Ordinal.Compare(Left.Name, Right.Name));
        var Descriptor = new ValueMemberDescriptor(Fields, Properties);
        ValueMemberDescriptors.Add(RuntimeType, Descriptor);
        return Descriptor;
    }

    static FieldInfo[] SerializedFields(Type RuntimeType)
    {
        if (SerializedFieldDescriptors.TryGetValue(RuntimeType, out var Existing)) return Existing;
        var Fields = new List<FieldInfo>();
        for (var Type = RuntimeType; Type is not null && Type != typeof(UnityEngine.Object); Type = Type.BaseType)
            foreach (var Field in Type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (!Field.IsStatic && !Field.IsNotSerialized
                    && (Field.IsPublic || Field.GetCustomAttribute<SerializeField>() is not null)) Fields.Add(Field);
        Fields.Sort((Left, Right) =>
        {
            var Owner = StringComparer.Ordinal.Compare(Left.DeclaringType?.FullName, Right.DeclaringType?.FullName);
            return Owner != 0 ? Owner : StringComparer.Ordinal.Compare(Left.Name, Right.Name);
        });
        var Descriptor = Fields.ToArray();
        SerializedFieldDescriptors.Add(RuntimeType, Descriptor);
        return Descriptor;
    }

    sealed class ValueMemberDescriptor
    {
        internal readonly FieldInfo[] Fields;
        internal readonly PropertyInfo[] Properties;

        internal ValueMemberDescriptor(FieldInfo[] Fields, PropertyInfo[] Properties)
        {
            this.Fields = Fields;
            this.Properties = Properties;
        }
    }
}
