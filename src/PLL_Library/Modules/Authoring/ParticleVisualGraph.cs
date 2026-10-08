using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal sealed class ParticleVisualGraph
{
    readonly byte[] Fingerprint;
    readonly int ParticleCount;
    readonly int RootCount;
    readonly bool Staged;

    ParticleVisualGraph(byte[] Fingerprint, int ParticleCount, int RootCount, bool Staged)
    {
        this.Fingerprint = Fingerprint;
        this.ParticleCount = ParticleCount;
        this.RootCount = RootCount;
        this.Staged = Staged;
    }

    internal static ParticleVisualGraph Capture(
        GameObject Root,
        Transform IdentityRoot,
        PllCustomVisualAuthoring? Authoring,
        bool RequireStaged)
    {
        var Resolved = ParticleVisualGraphCapture.Capture(Root, IdentityRoot, Authoring, RequireStaged);
        return new ParticleVisualGraph(Resolved.Fingerprint, Resolved.AllParticles.Count, Resolved.RootParticles.Count, RequireStaged);
    }

    internal void Validate(
        GameObject Root,
        Transform IdentityRoot,
        PllCustomVisualAuthoring? Authoring,
        bool RequireStaged)
    {
        if (RequireStaged != Staged)
            throw new InvalidOperationException("A particle visual graph changed its staged-state contract.");
        var Current = ParticleVisualGraphCapture.Capture(Root, IdentityRoot, Authoring, RequireStaged);
        if (Current.AllParticles.Count != ParticleCount || Current.RootParticles.Count != RootCount
            || !DeterministicObjectIdentity.SameHash(Current.Fingerprint, Fingerprint))
            throw new InvalidOperationException("A particle visual graph changed after compatibility sealing.");
    }

    internal ResolvedParticleGraph Resolve(
        GameObject Root,
        Transform IdentityRoot,
        PllCustomVisualAuthoring? Authoring,
        bool RequireStaged)
    {
        var Current = ParticleVisualGraphCapture.Capture(Root, IdentityRoot, Authoring, RequireStaged);
        if (Current.AllParticles.Count != ParticleCount || Current.RootParticles.Count != RootCount
            || !DeterministicObjectIdentity.SameHash(Current.Fingerprint, Fingerprint))
            throw new InvalidOperationException("A cloned particle visual graph does not match its sealed source.");
        return Current;
    }

    internal void WriteManifest(BinaryWriter Writer)
    {
        Writer.Write(Staged);
        Writer.Write(ParticleCount);
        Writer.Write(RootCount);
        Writer.Write(Fingerprint.Length);
        Writer.Write(Fingerprint);
    }
}

internal sealed class ResolvedParticleGraph
{
    internal ResolvedParticleGraph(ParticleSystem[] AllParticles, ParticleSystem[] RootParticles, byte[] Fingerprint)
    {
        this.AllParticles = Array.AsReadOnly(AllParticles);
        this.RootParticles = Array.AsReadOnly(RootParticles);
        this.Fingerprint = Fingerprint;
    }

    internal IReadOnlyList<ParticleSystem> AllParticles { get; }
    internal IReadOnlyList<ParticleSystem> RootParticles { get; }
    internal byte[] Fingerprint { get; }
}

internal static class ParticleVisualGraphCapture
{
    internal static ResolvedParticleGraph Capture(
        GameObject Root,
        Transform IdentityRoot,
        PllCustomVisualAuthoring? Authoring,
        bool RequireStaged)
    {
        if (!Root || !IdentityRoot || !TransformOwnership.Contains(IdentityRoot, Root.transform))
            throw new InvalidOperationException("A particle visual graph has invalid structural ownership.");
        var Authored = Root.GetComponentsInChildren<PllCustomVisualAuthoring>(true);
        if (Authoring
            ? Authoring.gameObject != Root || Authored.Length != 1 || Authored[0] != Authoring
            : Authored.Length != 0)
            throw new InvalidOperationException("A particle visual graph has invalid custom-authoring ownership.");
        foreach (var Component in Root.GetComponentsInChildren<Component>(true))
        {
            if (!Component) throw new InvalidOperationException("A particle visual graph contains a missing component.");
            if (Component is Transform || Component is ParticleSystem || Component is Renderer
                || Component is MeshFilter || Component is ParticleSystemForceField || Component is Light
                || Authoring && Component == Authoring) continue;
            throw new InvalidOperationException(
                $"A particle visual graph contains unsupported component '{Component.GetType().FullName}'.");
        }

        var Particles = Root.GetComponentsInChildren<ParticleSystem>(true);
        if (Particles.Length == 0) throw new InvalidOperationException("A particle visual graph contains no ParticleSystem.");
        if (Particles.Length > 1)
        {
            var Keys = new string[Particles.Length];
            for (var i = 0; i < Particles.Length; i++)
                Keys[i] = DeterministicObjectIdentity.RelativeComponentPath(Particles[i], Root.transform, 0);
            Array.Sort(Keys, Particles, StringComparer.Ordinal);
        }
        var Owned = new HashSet<ParticleSystem>(Particles);
        var Incoming = new HashSet<ParticleSystem>();
        var Edges = new Dictionary<ParticleSystem, ParticleSystem[]>();
        var ReferencedLights = new HashSet<Light>();
        foreach (var Particle in Particles)
        {
            ValidateCallbacks(Particle);
            if (RequireStaged && (Particle.main.playOnAwake || !Particle.isStopped || Particle.particleCount != 0))
                throw new InvalidOperationException("A staged particle visual graph must be stopped, cleared, and playOnAwake=false.");
            var SubEmitters = Particle.subEmitters;
            var Children = new ParticleSystem[SubEmitters.subEmittersCount];
            for (var i = 0; i < Children.Length; i++)
            {
                var Child = SubEmitters.GetSubEmitterSystem(i);
                if (!Child || !Owned.Contains(Child))
                    throw new InvalidOperationException("A particle visual graph has a missing or externally owned sub-emitter.");
                Children[i] = Child;
                Incoming.Add(Child);
            }
            Edges.Add(Particle, Children);
            var Lights = Particle.lights;
            if (Lights.enabled && !Lights.light)
                throw new InvalidOperationException("A particle visual graph has an enabled Lights module without a Light prefab.");
            if (Lights.light) ReferencedLights.Add(Lights.light);
        }
        foreach (var Light in Root.GetComponentsInChildren<Light>(true))
            if (!ReferencedLights.Contains(Light))
                throw new InvalidOperationException("A particle visual graph owns a Light not referenced by its Lights modules.");
        ValidateRenderers(Root);

        var State = new Dictionary<ParticleSystem, byte>();
        foreach (var Particle in Particles) Visit(Particle);
        var Roots = new List<ParticleSystem>();
        foreach (var Particle in Particles)
            if (!Incoming.Contains(Particle))
            {
                if (RequireStaged && !Particle.main.loop)
                    throw new InvalidOperationException("A custom visual root ParticleSystem must enable Loop.");
                Roots.Add(Particle);
            }
        if (RequireStaged && Root.activeSelf)
            throw new InvalidOperationException("A staged custom visual root must be inactive.");

        var Fingerprint = VisualFingerprintWriter.HashParticleGraph(Root.transform, IdentityRoot, Particles, Roots);
        return new ResolvedParticleGraph(Particles, Roots.ToArray(), Fingerprint);

        void Visit(ParticleSystem Particle)
        {
            if (State.TryGetValue(Particle, out var Current))
            {
                if (Current == 1) throw new InvalidOperationException("A particle visual graph contains a sub-emitter cycle.");
                return;
            }
            State.Add(Particle, 1);
            foreach (var Child in Edges[Particle]) Visit(Child);
            State[Particle] = 2;
        }
    }

    internal static void ValidateCallbacks(ParticleSystem Particle)
    {
        if (PllAuthoringValueContract.ParticleCallbackFailure(Particle) is { } Failure)
            throw new InvalidOperationException(Failure);
    }

    static void ValidateRenderers(GameObject Root)
    {
        foreach (var Renderer in Root.GetComponentsInChildren<Renderer>(true))
        {
            var Materials = Renderer.sharedMaterials;
            if (Materials.Length == 0 || Array.Exists(Materials, Material => !Material || !Material.shader))
                throw new InvalidOperationException("A particle visual Renderer has a missing material or shader.");
            if (Renderer is not ParticleSystemRenderer ParticleRenderer) continue;
            if (ParticleRenderer.renderMode == ParticleSystemRenderMode.Mesh)
            {
                var Meshes = new Mesh[ParticleRenderer.meshCount];
                if (Meshes.Length == 0 || ParticleRenderer.GetMeshes(Meshes) != Meshes.Length
                    || Array.Exists(Meshes, Mesh => !Mesh))
                    throw new InvalidOperationException("A particle visual mesh Renderer has a missing mesh.");
            }
            var Particle = ParticleRenderer.GetComponent<ParticleSystem>();
            if (Particle && Particle.trails.enabled
                && (!ParticleRenderer.trailMaterial || !ParticleRenderer.trailMaterial.shader))
                throw new InvalidOperationException("A particle visual trail Renderer has a missing material or shader.");
        }
        foreach (var Filter in Root.GetComponentsInChildren<MeshFilter>(true))
            if (!Filter.sharedMesh || !Filter.GetComponent<Renderer>())
                throw new InvalidOperationException("A particle visual MeshFilter has no mesh Renderer dependency.");
    }
}
