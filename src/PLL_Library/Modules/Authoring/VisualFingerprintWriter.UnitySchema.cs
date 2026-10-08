using System;
using UnityEngine;

namespace PLL.Library;

[System.CodeDom.Compiler.GeneratedCode("PLL.UnityVisualSchema", "6000.3.15f1/v2")]
internal sealed partial class VisualFingerprintWriter
{
    void WriteWritableProperties(object Value)
    {
        switch (Value)
        {
            case UnityEngine.BillboardRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.LineRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.MeshRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystemForceField Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystemRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.SkinnedMeshRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.SpriteMask Typed: WriteProperties(Typed); return;
            case UnityEngine.SpriteRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.Tilemaps.TilemapRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.TrailRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.U2D.SpriteShapeRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.UIElements.UIRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.VFX.VFXRenderer Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.CollisionModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.ColorBySpeedModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.ColorOverLifetimeModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.CustomDataModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.EmissionModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.ExternalForcesModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.ForceOverLifetimeModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.InheritVelocityModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.LifetimeByEmitterSpeedModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.LightsModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.LimitVelocityOverLifetimeModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.MainModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.NoiseModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.RotationBySpeedModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.RotationOverLifetimeModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.ShapeModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.SizeBySpeedModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.SizeOverLifetimeModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.SubEmittersModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.TextureSheetAnimationModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.TrailModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.TriggerModule Typed: WriteProperties(Typed); return;
            case UnityEngine.ParticleSystem.VelocityOverLifetimeModule Typed: WriteProperties(Typed); return;
            default: throw new InvalidOperationException($"Visual property owner '{Value.GetType().FullName}' is not supported.");
        }
    }

    void WriteProperties(UnityEngine.BillboardRenderer Value)
    {
        Writer.Write(27);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.billboard)); WriteValue(Value.billboard);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
    }

    void WriteProperties(UnityEngine.LineRenderer Value)
    {
        Writer.Write(45);
        Writer.Write(nameof(Value.alignment)); WriteValue(Value.alignment);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.applyActiveColorSpace)); WriteValue(Value.applyActiveColorSpace);
        Writer.Write(nameof(Value.colorGradient)); WriteValue(Value.colorGradient);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.endColor)); WriteValue(Value.endColor);
        Writer.Write(nameof(Value.endWidth)); WriteValue(Value.endWidth);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.generateLightingData)); WriteValue(Value.generateLightingData);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.loop)); WriteValue(Value.loop);
        Writer.Write(nameof(Value.maskInteraction)); WriteValue(Value.maskInteraction);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.numCapVertices)); WriteValue(Value.numCapVertices);
        Writer.Write(nameof(Value.numCornerVertices)); WriteValue(Value.numCornerVertices);
        Writer.Write(nameof(Value.positionCount)); WriteValue(Value.positionCount);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.shadowBias)); WriteValue(Value.shadowBias);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.startColor)); WriteValue(Value.startColor);
        Writer.Write(nameof(Value.startWidth)); WriteValue(Value.startWidth);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
        Writer.Write(nameof(Value.textureMode)); WriteValue(Value.textureMode);
        Writer.Write(nameof(Value.textureScale)); WriteValue(Value.textureScale);
        Writer.Write(nameof(Value.useWorldSpace)); WriteValue(Value.useWorldSpace);
        Writer.Write(nameof(Value.widthCurve)); WriteValue(Value.widthCurve);
        Writer.Write(nameof(Value.widthMultiplier)); WriteValue(Value.widthMultiplier);
    }

    void WriteProperties(UnityEngine.MeshRenderer Value)
    {
        Writer.Write(28);
        Writer.Write(nameof(Value.additionalVertexStreams)); WriteValue(Value.additionalVertexStreams);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.enlightenVertexStream)); WriteValue(Value.enlightenVertexStream);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
    }

    void WriteProperties(UnityEngine.ParticleSystem.CollisionModule Value)
    {
        Writer.Write(22);
        Writer.Write(nameof(Value.bounce)); WriteValue(Value.bounce);
        Writer.Write(nameof(Value.bounceMultiplier)); WriteValue(Value.bounceMultiplier);
        Writer.Write(nameof(Value.colliderForce)); WriteValue(Value.colliderForce);
        Writer.Write(nameof(Value.collidesWith)); WriteValue(Value.collidesWith);
        Writer.Write(nameof(Value.dampen)); WriteValue(Value.dampen);
        Writer.Write(nameof(Value.dampenMultiplier)); WriteValue(Value.dampenMultiplier);
        Writer.Write(nameof(Value.enableDynamicColliders)); WriteValue(Value.enableDynamicColliders);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.lifetimeLoss)); WriteValue(Value.lifetimeLoss);
        Writer.Write(nameof(Value.lifetimeLossMultiplier)); WriteValue(Value.lifetimeLossMultiplier);
        Writer.Write(nameof(Value.maxCollisionShapes)); WriteValue(Value.maxCollisionShapes);
        Writer.Write(nameof(Value.maxKillSpeed)); WriteValue(Value.maxKillSpeed);
        Writer.Write(nameof(Value.minKillSpeed)); WriteValue(Value.minKillSpeed);
        Writer.Write(nameof(Value.mode)); WriteValue(Value.mode);
        Writer.Write(nameof(Value.multiplyColliderForceByCollisionAngle)); WriteValue(Value.multiplyColliderForceByCollisionAngle);
        Writer.Write(nameof(Value.multiplyColliderForceByParticleSize)); WriteValue(Value.multiplyColliderForceByParticleSize);
        Writer.Write(nameof(Value.multiplyColliderForceByParticleSpeed)); WriteValue(Value.multiplyColliderForceByParticleSpeed);
        Writer.Write(nameof(Value.quality)); WriteValue(Value.quality);
        Writer.Write(nameof(Value.radiusScale)); WriteValue(Value.radiusScale);
        Writer.Write(nameof(Value.sendCollisionMessages)); WriteValue(Value.sendCollisionMessages);
        Writer.Write(nameof(Value.type)); WriteValue(Value.type);
        Writer.Write(nameof(Value.voxelSize)); WriteValue(Value.voxelSize);
    }

    void WriteProperties(UnityEngine.ParticleSystem.ColorBySpeedModule Value)
    {
        Writer.Write(3);
        Writer.Write(nameof(Value.color)); WriteValue(Value.color);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.range)); WriteValue(Value.range);
    }

    void WriteProperties(UnityEngine.ParticleSystem.ColorOverLifetimeModule Value)
    {
        Writer.Write(2);
        Writer.Write(nameof(Value.color)); WriteValue(Value.color);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
    }

    void WriteProperties(UnityEngine.ParticleSystem.CustomDataModule Value)
    {
        Writer.Write(1);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
    }

    void WriteProperties(UnityEngine.ParticleSystem.EmissionModule Value)
    {
        Writer.Write(6);
        Writer.Write(nameof(Value.burstCount)); WriteValue(Value.burstCount);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.rateOverDistance)); WriteValue(Value.rateOverDistance);
        Writer.Write(nameof(Value.rateOverDistanceMultiplier)); WriteValue(Value.rateOverDistanceMultiplier);
        Writer.Write(nameof(Value.rateOverTime)); WriteValue(Value.rateOverTime);
        Writer.Write(nameof(Value.rateOverTimeMultiplier)); WriteValue(Value.rateOverTimeMultiplier);
    }

    void WriteProperties(UnityEngine.ParticleSystem.ExternalForcesModule Value)
    {
        Writer.Write(5);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.influenceFilter)); WriteValue(Value.influenceFilter);
        Writer.Write(nameof(Value.influenceMask)); WriteValue(Value.influenceMask);
        Writer.Write(nameof(Value.multiplier)); WriteValue(Value.multiplier);
        Writer.Write(nameof(Value.multiplierCurve)); WriteValue(Value.multiplierCurve);
    }

    void WriteProperties(UnityEngine.ParticleSystem.ForceOverLifetimeModule Value)
    {
        Writer.Write(9);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.randomized)); WriteValue(Value.randomized);
        Writer.Write(nameof(Value.space)); WriteValue(Value.space);
        Writer.Write(nameof(Value.x)); WriteValue(Value.x);
        Writer.Write(nameof(Value.xMultiplier)); WriteValue(Value.xMultiplier);
        Writer.Write(nameof(Value.y)); WriteValue(Value.y);
        Writer.Write(nameof(Value.yMultiplier)); WriteValue(Value.yMultiplier);
        Writer.Write(nameof(Value.z)); WriteValue(Value.z);
        Writer.Write(nameof(Value.zMultiplier)); WriteValue(Value.zMultiplier);
    }

    void WriteProperties(UnityEngine.ParticleSystem.InheritVelocityModule Value)
    {
        Writer.Write(4);
        Writer.Write(nameof(Value.curve)); WriteValue(Value.curve);
        Writer.Write(nameof(Value.curveMultiplier)); WriteValue(Value.curveMultiplier);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.mode)); WriteValue(Value.mode);
    }

    void WriteProperties(UnityEngine.ParticleSystem.LifetimeByEmitterSpeedModule Value)
    {
        Writer.Write(4);
        Writer.Write(nameof(Value.curve)); WriteValue(Value.curve);
        Writer.Write(nameof(Value.curveMultiplier)); WriteValue(Value.curveMultiplier);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.range)); WriteValue(Value.range);
    }

    void WriteProperties(UnityEngine.ParticleSystem.LightsModule Value)
    {
        Writer.Write(12);
        Writer.Write(nameof(Value.alphaAffectsIntensity)); WriteValue(Value.alphaAffectsIntensity);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.intensity)); WriteValue(Value.intensity);
        Writer.Write(nameof(Value.intensityMultiplier)); WriteValue(Value.intensityMultiplier);
        Writer.Write(nameof(Value.light)); WriteValue(Value.light);
        Writer.Write(nameof(Value.maxLights)); WriteValue(Value.maxLights);
        Writer.Write(nameof(Value.range)); WriteValue(Value.range);
        Writer.Write(nameof(Value.rangeMultiplier)); WriteValue(Value.rangeMultiplier);
        Writer.Write(nameof(Value.ratio)); WriteValue(Value.ratio);
        Writer.Write(nameof(Value.sizeAffectsRange)); WriteValue(Value.sizeAffectsRange);
        Writer.Write(nameof(Value.useParticleColor)); WriteValue(Value.useParticleColor);
        Writer.Write(nameof(Value.useRandomDistribution)); WriteValue(Value.useRandomDistribution);
    }

    void WriteProperties(UnityEngine.ParticleSystem.LimitVelocityOverLifetimeModule Value)
    {
        Writer.Write(16);
        Writer.Write(nameof(Value.dampen)); WriteValue(Value.dampen);
        Writer.Write(nameof(Value.drag)); WriteValue(Value.drag);
        Writer.Write(nameof(Value.dragMultiplier)); WriteValue(Value.dragMultiplier);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.limit)); WriteValue(Value.limit);
        Writer.Write(nameof(Value.limitMultiplier)); WriteValue(Value.limitMultiplier);
        Writer.Write(nameof(Value.limitX)); WriteValue(Value.limitX);
        Writer.Write(nameof(Value.limitXMultiplier)); WriteValue(Value.limitXMultiplier);
        Writer.Write(nameof(Value.limitY)); WriteValue(Value.limitY);
        Writer.Write(nameof(Value.limitYMultiplier)); WriteValue(Value.limitYMultiplier);
        Writer.Write(nameof(Value.limitZ)); WriteValue(Value.limitZ);
        Writer.Write(nameof(Value.limitZMultiplier)); WriteValue(Value.limitZMultiplier);
        Writer.Write(nameof(Value.multiplyDragByParticleSize)); WriteValue(Value.multiplyDragByParticleSize);
        Writer.Write(nameof(Value.multiplyDragByParticleVelocity)); WriteValue(Value.multiplyDragByParticleVelocity);
        Writer.Write(nameof(Value.separateAxes)); WriteValue(Value.separateAxes);
        Writer.Write(nameof(Value.space)); WriteValue(Value.space);
    }

    void WriteProperties(UnityEngine.ParticleSystem.MainModule Value)
    {
        Writer.Write(45);
        Writer.Write(nameof(Value.cullingMode)); WriteValue(Value.cullingMode);
        Writer.Write(nameof(Value.customSimulationSpace)); WriteValue(Value.customSimulationSpace);
        Writer.Write(nameof(Value.duration)); WriteValue(Value.duration);
        Writer.Write(nameof(Value.emitterVelocity)); WriteValue(Value.emitterVelocity);
        Writer.Write(nameof(Value.emitterVelocityMode)); WriteValue(Value.emitterVelocityMode);
        Writer.Write(nameof(Value.flipRotation)); WriteValue(Value.flipRotation);
        Writer.Write(nameof(Value.gravityModifier)); WriteValue(Value.gravityModifier);
        Writer.Write(nameof(Value.gravityModifierMultiplier)); WriteValue(Value.gravityModifierMultiplier);
        Writer.Write(nameof(Value.gravitySource)); WriteValue(Value.gravitySource);
        Writer.Write(nameof(Value.loop)); WriteValue(Value.loop);
        Writer.Write(nameof(Value.maxParticles)); WriteValue(Value.maxParticles);
        Writer.Write(nameof(Value.playOnAwake)); WriteValue(Value.playOnAwake);
        Writer.Write(nameof(Value.prewarm)); WriteValue(Value.prewarm);
        Writer.Write(nameof(Value.ringBufferLoopRange)); WriteValue(Value.ringBufferLoopRange);
        Writer.Write(nameof(Value.ringBufferMode)); WriteValue(Value.ringBufferMode);
        Writer.Write(nameof(Value.scalingMode)); WriteValue(Value.scalingMode);
        Writer.Write(nameof(Value.simulationSpace)); WriteValue(Value.simulationSpace);
        Writer.Write(nameof(Value.simulationSpeed)); WriteValue(Value.simulationSpeed);
        Writer.Write(nameof(Value.startColor)); WriteValue(Value.startColor);
        Writer.Write(nameof(Value.startDelay)); WriteValue(Value.startDelay);
        Writer.Write(nameof(Value.startDelayMultiplier)); WriteValue(Value.startDelayMultiplier);
        Writer.Write(nameof(Value.startLifetime)); WriteValue(Value.startLifetime);
        Writer.Write(nameof(Value.startLifetimeMultiplier)); WriteValue(Value.startLifetimeMultiplier);
        Writer.Write(nameof(Value.startRotation)); WriteValue(Value.startRotation);
        Writer.Write(nameof(Value.startRotation3D)); WriteValue(Value.startRotation3D);
        Writer.Write(nameof(Value.startRotationMultiplier)); WriteValue(Value.startRotationMultiplier);
        Writer.Write(nameof(Value.startRotationX)); WriteValue(Value.startRotationX);
        Writer.Write(nameof(Value.startRotationXMultiplier)); WriteValue(Value.startRotationXMultiplier);
        Writer.Write(nameof(Value.startRotationY)); WriteValue(Value.startRotationY);
        Writer.Write(nameof(Value.startRotationYMultiplier)); WriteValue(Value.startRotationYMultiplier);
        Writer.Write(nameof(Value.startRotationZ)); WriteValue(Value.startRotationZ);
        Writer.Write(nameof(Value.startRotationZMultiplier)); WriteValue(Value.startRotationZMultiplier);
        Writer.Write(nameof(Value.startSize)); WriteValue(Value.startSize);
        Writer.Write(nameof(Value.startSize3D)); WriteValue(Value.startSize3D);
        Writer.Write(nameof(Value.startSizeMultiplier)); WriteValue(Value.startSizeMultiplier);
        Writer.Write(nameof(Value.startSizeX)); WriteValue(Value.startSizeX);
        Writer.Write(nameof(Value.startSizeXMultiplier)); WriteValue(Value.startSizeXMultiplier);
        Writer.Write(nameof(Value.startSizeY)); WriteValue(Value.startSizeY);
        Writer.Write(nameof(Value.startSizeYMultiplier)); WriteValue(Value.startSizeYMultiplier);
        Writer.Write(nameof(Value.startSizeZ)); WriteValue(Value.startSizeZ);
        Writer.Write(nameof(Value.startSizeZMultiplier)); WriteValue(Value.startSizeZMultiplier);
        Writer.Write(nameof(Value.startSpeed)); WriteValue(Value.startSpeed);
        Writer.Write(nameof(Value.startSpeedMultiplier)); WriteValue(Value.startSpeedMultiplier);
        Writer.Write(nameof(Value.stopAction)); WriteValue(Value.stopAction);
        Writer.Write(nameof(Value.useUnscaledTime)); WriteValue(Value.useUnscaledTime);
    }

    void WriteProperties(UnityEngine.ParticleSystem.NoiseModule Value)
    {
        Writer.Write(30);
        Writer.Write(nameof(Value.damping)); WriteValue(Value.damping);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.frequency)); WriteValue(Value.frequency);
        Writer.Write(nameof(Value.octaveCount)); WriteValue(Value.octaveCount);
        Writer.Write(nameof(Value.octaveMultiplier)); WriteValue(Value.octaveMultiplier);
        Writer.Write(nameof(Value.octaveScale)); WriteValue(Value.octaveScale);
        Writer.Write(nameof(Value.positionAmount)); WriteValue(Value.positionAmount);
        Writer.Write(nameof(Value.quality)); WriteValue(Value.quality);
        Writer.Write(nameof(Value.remap)); WriteValue(Value.remap);
        Writer.Write(nameof(Value.remapEnabled)); WriteValue(Value.remapEnabled);
        Writer.Write(nameof(Value.remapMultiplier)); WriteValue(Value.remapMultiplier);
        Writer.Write(nameof(Value.remapX)); WriteValue(Value.remapX);
        Writer.Write(nameof(Value.remapXMultiplier)); WriteValue(Value.remapXMultiplier);
        Writer.Write(nameof(Value.remapY)); WriteValue(Value.remapY);
        Writer.Write(nameof(Value.remapYMultiplier)); WriteValue(Value.remapYMultiplier);
        Writer.Write(nameof(Value.remapZ)); WriteValue(Value.remapZ);
        Writer.Write(nameof(Value.remapZMultiplier)); WriteValue(Value.remapZMultiplier);
        Writer.Write(nameof(Value.rotationAmount)); WriteValue(Value.rotationAmount);
        Writer.Write(nameof(Value.scrollSpeed)); WriteValue(Value.scrollSpeed);
        Writer.Write(nameof(Value.scrollSpeedMultiplier)); WriteValue(Value.scrollSpeedMultiplier);
        Writer.Write(nameof(Value.separateAxes)); WriteValue(Value.separateAxes);
        Writer.Write(nameof(Value.sizeAmount)); WriteValue(Value.sizeAmount);
        Writer.Write(nameof(Value.strength)); WriteValue(Value.strength);
        Writer.Write(nameof(Value.strengthMultiplier)); WriteValue(Value.strengthMultiplier);
        Writer.Write(nameof(Value.strengthX)); WriteValue(Value.strengthX);
        Writer.Write(nameof(Value.strengthXMultiplier)); WriteValue(Value.strengthXMultiplier);
        Writer.Write(nameof(Value.strengthY)); WriteValue(Value.strengthY);
        Writer.Write(nameof(Value.strengthYMultiplier)); WriteValue(Value.strengthYMultiplier);
        Writer.Write(nameof(Value.strengthZ)); WriteValue(Value.strengthZ);
        Writer.Write(nameof(Value.strengthZMultiplier)); WriteValue(Value.strengthZMultiplier);
    }

    void WriteProperties(UnityEngine.ParticleSystem.RotationBySpeedModule Value)
    {
        Writer.Write(9);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.range)); WriteValue(Value.range);
        Writer.Write(nameof(Value.separateAxes)); WriteValue(Value.separateAxes);
        Writer.Write(nameof(Value.x)); WriteValue(Value.x);
        Writer.Write(nameof(Value.xMultiplier)); WriteValue(Value.xMultiplier);
        Writer.Write(nameof(Value.y)); WriteValue(Value.y);
        Writer.Write(nameof(Value.yMultiplier)); WriteValue(Value.yMultiplier);
        Writer.Write(nameof(Value.z)); WriteValue(Value.z);
        Writer.Write(nameof(Value.zMultiplier)); WriteValue(Value.zMultiplier);
    }

    void WriteProperties(UnityEngine.ParticleSystem.RotationOverLifetimeModule Value)
    {
        Writer.Write(8);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.separateAxes)); WriteValue(Value.separateAxes);
        Writer.Write(nameof(Value.x)); WriteValue(Value.x);
        Writer.Write(nameof(Value.xMultiplier)); WriteValue(Value.xMultiplier);
        Writer.Write(nameof(Value.y)); WriteValue(Value.y);
        Writer.Write(nameof(Value.yMultiplier)); WriteValue(Value.yMultiplier);
        Writer.Write(nameof(Value.z)); WriteValue(Value.z);
        Writer.Write(nameof(Value.zMultiplier)); WriteValue(Value.zMultiplier);
    }

    void WriteProperties(UnityEngine.ParticleSystem.ShapeModule Value)
    {
        Writer.Write(45);
        Writer.Write(nameof(Value.alignToDirection)); WriteValue(Value.alignToDirection);
        Writer.Write(nameof(Value.angle)); WriteValue(Value.angle);
        Writer.Write(nameof(Value.arc)); WriteValue(Value.arc);
        Writer.Write(nameof(Value.arcMode)); WriteValue(Value.arcMode);
        Writer.Write(nameof(Value.arcSpeed)); WriteValue(Value.arcSpeed);
        Writer.Write(nameof(Value.arcSpeedMultiplier)); WriteValue(Value.arcSpeedMultiplier);
        Writer.Write(nameof(Value.arcSpread)); WriteValue(Value.arcSpread);
        Writer.Write(nameof(Value.boxThickness)); WriteValue(Value.boxThickness);
        Writer.Write(nameof(Value.donutRadius)); WriteValue(Value.donutRadius);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.length)); WriteValue(Value.length);
        Writer.Write(nameof(Value.mesh)); WriteValue(Value.mesh);
        Writer.Write(nameof(Value.meshMaterialIndex)); WriteValue(Value.meshMaterialIndex);
        Writer.Write(nameof(Value.meshRenderer)); WriteValue(Value.meshRenderer);
        Writer.Write(nameof(Value.meshShapeType)); WriteValue(Value.meshShapeType);
        Writer.Write(nameof(Value.meshSpawnMode)); WriteValue(Value.meshSpawnMode);
        Writer.Write(nameof(Value.meshSpawnSpeed)); WriteValue(Value.meshSpawnSpeed);
        Writer.Write(nameof(Value.meshSpawnSpeedMultiplier)); WriteValue(Value.meshSpawnSpeedMultiplier);
        Writer.Write(nameof(Value.meshSpawnSpread)); WriteValue(Value.meshSpawnSpread);
        Writer.Write(nameof(Value.normalOffset)); WriteValue(Value.normalOffset);
        Writer.Write(nameof(Value.position)); WriteValue(Value.position);
        Writer.Write(nameof(Value.radius)); WriteValue(Value.radius);
        Writer.Write(nameof(Value.radiusMode)); WriteValue(Value.radiusMode);
        Writer.Write(nameof(Value.radiusSpeed)); WriteValue(Value.radiusSpeed);
        Writer.Write(nameof(Value.radiusSpeedMultiplier)); WriteValue(Value.radiusSpeedMultiplier);
        Writer.Write(nameof(Value.radiusSpread)); WriteValue(Value.radiusSpread);
        Writer.Write(nameof(Value.radiusThickness)); WriteValue(Value.radiusThickness);
        Writer.Write(nameof(Value.randomDirectionAmount)); WriteValue(Value.randomDirectionAmount);
        Writer.Write(nameof(Value.randomPositionAmount)); WriteValue(Value.randomPositionAmount);
        Writer.Write(nameof(Value.rotation)); WriteValue(Value.rotation);
        Writer.Write(nameof(Value.scale)); WriteValue(Value.scale);
        Writer.Write(nameof(Value.shapeType)); WriteValue(Value.shapeType);
        Writer.Write(nameof(Value.skinnedMeshRenderer)); WriteValue(Value.skinnedMeshRenderer);
        Writer.Write(nameof(Value.sphericalDirectionAmount)); WriteValue(Value.sphericalDirectionAmount);
        Writer.Write(nameof(Value.sprite)); WriteValue(Value.sprite);
        Writer.Write(nameof(Value.spriteRenderer)); WriteValue(Value.spriteRenderer);
        Writer.Write(nameof(Value.texture)); WriteValue(Value.texture);
        Writer.Write(nameof(Value.textureAlphaAffectsParticles)); WriteValue(Value.textureAlphaAffectsParticles);
        Writer.Write(nameof(Value.textureBilinearFiltering)); WriteValue(Value.textureBilinearFiltering);
        Writer.Write(nameof(Value.textureClipChannel)); WriteValue(Value.textureClipChannel);
        Writer.Write(nameof(Value.textureClipThreshold)); WriteValue(Value.textureClipThreshold);
        Writer.Write(nameof(Value.textureColorAffectsParticles)); WriteValue(Value.textureColorAffectsParticles);
        Writer.Write(nameof(Value.textureUVChannel)); WriteValue(Value.textureUVChannel);
        Writer.Write(nameof(Value.useMeshColors)); WriteValue(Value.useMeshColors);
        Writer.Write(nameof(Value.useMeshMaterialIndex)); WriteValue(Value.useMeshMaterialIndex);
    }

    void WriteProperties(UnityEngine.ParticleSystem.SizeBySpeedModule Value)
    {
        Writer.Write(11);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.range)); WriteValue(Value.range);
        Writer.Write(nameof(Value.separateAxes)); WriteValue(Value.separateAxes);
        Writer.Write(nameof(Value.size)); WriteValue(Value.size);
        Writer.Write(nameof(Value.sizeMultiplier)); WriteValue(Value.sizeMultiplier);
        Writer.Write(nameof(Value.x)); WriteValue(Value.x);
        Writer.Write(nameof(Value.xMultiplier)); WriteValue(Value.xMultiplier);
        Writer.Write(nameof(Value.y)); WriteValue(Value.y);
        Writer.Write(nameof(Value.yMultiplier)); WriteValue(Value.yMultiplier);
        Writer.Write(nameof(Value.z)); WriteValue(Value.z);
        Writer.Write(nameof(Value.zMultiplier)); WriteValue(Value.zMultiplier);
    }

    void WriteProperties(UnityEngine.ParticleSystem.SizeOverLifetimeModule Value)
    {
        Writer.Write(10);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.separateAxes)); WriteValue(Value.separateAxes);
        Writer.Write(nameof(Value.size)); WriteValue(Value.size);
        Writer.Write(nameof(Value.sizeMultiplier)); WriteValue(Value.sizeMultiplier);
        Writer.Write(nameof(Value.x)); WriteValue(Value.x);
        Writer.Write(nameof(Value.xMultiplier)); WriteValue(Value.xMultiplier);
        Writer.Write(nameof(Value.y)); WriteValue(Value.y);
        Writer.Write(nameof(Value.yMultiplier)); WriteValue(Value.yMultiplier);
        Writer.Write(nameof(Value.z)); WriteValue(Value.z);
        Writer.Write(nameof(Value.zMultiplier)); WriteValue(Value.zMultiplier);
    }

    void WriteProperties(UnityEngine.ParticleSystem.SubEmittersModule Value)
    {
        Writer.Write(1);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
    }

    void WriteProperties(UnityEngine.ParticleSystem.TextureSheetAnimationModule Value)
    {
        Writer.Write(16);
        Writer.Write(nameof(Value.animation)); WriteValue(Value.animation);
        Writer.Write(nameof(Value.cycleCount)); WriteValue(Value.cycleCount);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.fps)); WriteValue(Value.fps);
        Writer.Write(nameof(Value.frameOverTime)); WriteValue(Value.frameOverTime);
        Writer.Write(nameof(Value.frameOverTimeMultiplier)); WriteValue(Value.frameOverTimeMultiplier);
        Writer.Write(nameof(Value.mode)); WriteValue(Value.mode);
        Writer.Write(nameof(Value.numTilesX)); WriteValue(Value.numTilesX);
        Writer.Write(nameof(Value.numTilesY)); WriteValue(Value.numTilesY);
        Writer.Write(nameof(Value.rowIndex)); WriteValue(Value.rowIndex);
        Writer.Write(nameof(Value.rowMode)); WriteValue(Value.rowMode);
        Writer.Write(nameof(Value.speedRange)); WriteValue(Value.speedRange);
        Writer.Write(nameof(Value.startFrame)); WriteValue(Value.startFrame);
        Writer.Write(nameof(Value.startFrameMultiplier)); WriteValue(Value.startFrameMultiplier);
        Writer.Write(nameof(Value.timeMode)); WriteValue(Value.timeMode);
        Writer.Write(nameof(Value.uvChannelMask)); WriteValue(Value.uvChannelMask);
    }

    void WriteProperties(UnityEngine.ParticleSystem.TrailModule Value)
    {
        Writer.Write(22);
        Writer.Write(nameof(Value.attachRibbonsToTransform)); WriteValue(Value.attachRibbonsToTransform);
        Writer.Write(nameof(Value.colorOverLifetime)); WriteValue(Value.colorOverLifetime);
        Writer.Write(nameof(Value.colorOverTrail)); WriteValue(Value.colorOverTrail);
        Writer.Write(nameof(Value.dieWithParticles)); WriteValue(Value.dieWithParticles);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.generateLightingData)); WriteValue(Value.generateLightingData);
        Writer.Write(nameof(Value.inheritParticleColor)); WriteValue(Value.inheritParticleColor);
        Writer.Write(nameof(Value.lifetime)); WriteValue(Value.lifetime);
        Writer.Write(nameof(Value.lifetimeMultiplier)); WriteValue(Value.lifetimeMultiplier);
        Writer.Write(nameof(Value.minVertexDistance)); WriteValue(Value.minVertexDistance);
        Writer.Write(nameof(Value.mode)); WriteValue(Value.mode);
        Writer.Write(nameof(Value.ratio)); WriteValue(Value.ratio);
        Writer.Write(nameof(Value.ribbonCount)); WriteValue(Value.ribbonCount);
        Writer.Write(nameof(Value.shadowBias)); WriteValue(Value.shadowBias);
        Writer.Write(nameof(Value.sizeAffectsLifetime)); WriteValue(Value.sizeAffectsLifetime);
        Writer.Write(nameof(Value.sizeAffectsWidth)); WriteValue(Value.sizeAffectsWidth);
        Writer.Write(nameof(Value.splitSubEmitterRibbons)); WriteValue(Value.splitSubEmitterRibbons);
        Writer.Write(nameof(Value.textureMode)); WriteValue(Value.textureMode);
        Writer.Write(nameof(Value.textureScale)); WriteValue(Value.textureScale);
        Writer.Write(nameof(Value.widthOverTrail)); WriteValue(Value.widthOverTrail);
        Writer.Write(nameof(Value.widthOverTrailMultiplier)); WriteValue(Value.widthOverTrailMultiplier);
        Writer.Write(nameof(Value.worldSpace)); WriteValue(Value.worldSpace);
    }

    void WriteProperties(UnityEngine.ParticleSystem.TriggerModule Value)
    {
        Writer.Write(7);
        Writer.Write(nameof(Value.colliderQueryMode)); WriteValue(Value.colliderQueryMode);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.enter)); WriteValue(Value.enter);
        Writer.Write(nameof(Value.exit)); WriteValue(Value.exit);
        Writer.Write(nameof(Value.inside)); WriteValue(Value.inside);
        Writer.Write(nameof(Value.outside)); WriteValue(Value.outside);
        Writer.Write(nameof(Value.radiusScale)); WriteValue(Value.radiusScale);
    }

    void WriteProperties(UnityEngine.ParticleSystem.VelocityOverLifetimeModule Value)
    {
        Writer.Write(24);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.orbitalOffsetX)); WriteValue(Value.orbitalOffsetX);
        Writer.Write(nameof(Value.orbitalOffsetXMultiplier)); WriteValue(Value.orbitalOffsetXMultiplier);
        Writer.Write(nameof(Value.orbitalOffsetY)); WriteValue(Value.orbitalOffsetY);
        Writer.Write(nameof(Value.orbitalOffsetYMultiplier)); WriteValue(Value.orbitalOffsetYMultiplier);
        Writer.Write(nameof(Value.orbitalOffsetZ)); WriteValue(Value.orbitalOffsetZ);
        Writer.Write(nameof(Value.orbitalOffsetZMultiplier)); WriteValue(Value.orbitalOffsetZMultiplier);
        Writer.Write(nameof(Value.orbitalX)); WriteValue(Value.orbitalX);
        Writer.Write(nameof(Value.orbitalXMultiplier)); WriteValue(Value.orbitalXMultiplier);
        Writer.Write(nameof(Value.orbitalY)); WriteValue(Value.orbitalY);
        Writer.Write(nameof(Value.orbitalYMultiplier)); WriteValue(Value.orbitalYMultiplier);
        Writer.Write(nameof(Value.orbitalZ)); WriteValue(Value.orbitalZ);
        Writer.Write(nameof(Value.orbitalZMultiplier)); WriteValue(Value.orbitalZMultiplier);
        Writer.Write(nameof(Value.radial)); WriteValue(Value.radial);
        Writer.Write(nameof(Value.radialMultiplier)); WriteValue(Value.radialMultiplier);
        Writer.Write(nameof(Value.space)); WriteValue(Value.space);
        Writer.Write(nameof(Value.speedModifier)); WriteValue(Value.speedModifier);
        Writer.Write(nameof(Value.speedModifierMultiplier)); WriteValue(Value.speedModifierMultiplier);
        Writer.Write(nameof(Value.x)); WriteValue(Value.x);
        Writer.Write(nameof(Value.xMultiplier)); WriteValue(Value.xMultiplier);
        Writer.Write(nameof(Value.y)); WriteValue(Value.y);
        Writer.Write(nameof(Value.yMultiplier)); WriteValue(Value.yMultiplier);
        Writer.Write(nameof(Value.z)); WriteValue(Value.z);
        Writer.Write(nameof(Value.zMultiplier)); WriteValue(Value.zMultiplier);
    }

    void WriteProperties(UnityEngine.ParticleSystemForceField Value)
    {
        Writer.Write(18);
        Writer.Write(nameof(Value.directionX)); WriteValue(Value.directionX);
        Writer.Write(nameof(Value.directionY)); WriteValue(Value.directionY);
        Writer.Write(nameof(Value.directionZ)); WriteValue(Value.directionZ);
        Writer.Write(nameof(Value.drag)); WriteValue(Value.drag);
        Writer.Write(nameof(Value.endRange)); WriteValue(Value.endRange);
        Writer.Write(nameof(Value.gravity)); WriteValue(Value.gravity);
        Writer.Write(nameof(Value.gravityFocus)); WriteValue(Value.gravityFocus);
        Writer.Write(nameof(Value.length)); WriteValue(Value.length);
        Writer.Write(nameof(Value.multiplyDragByParticleSize)); WriteValue(Value.multiplyDragByParticleSize);
        Writer.Write(nameof(Value.multiplyDragByParticleVelocity)); WriteValue(Value.multiplyDragByParticleVelocity);
        Writer.Write(nameof(Value.rotationAttraction)); WriteValue(Value.rotationAttraction);
        Writer.Write(nameof(Value.rotationRandomness)); WriteValue(Value.rotationRandomness);
        Writer.Write(nameof(Value.rotationSpeed)); WriteValue(Value.rotationSpeed);
        Writer.Write(nameof(Value.shape)); WriteValue(Value.shape);
        Writer.Write(nameof(Value.startRange)); WriteValue(Value.startRange);
        Writer.Write(nameof(Value.vectorField)); WriteValue(Value.vectorField);
        Writer.Write(nameof(Value.vectorFieldAttraction)); WriteValue(Value.vectorFieldAttraction);
        Writer.Write(nameof(Value.vectorFieldSpeed)); WriteValue(Value.vectorFieldSpeed);
    }

    void WriteProperties(UnityEngine.ParticleSystemRenderer Value)
    {
        Writer.Write(46);
        Writer.Write(nameof(Value.alignment)); WriteValue(Value.alignment);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.allowRoll)); WriteValue(Value.allowRoll);
        Writer.Write(nameof(Value.applyActiveColorSpace)); WriteValue(Value.applyActiveColorSpace);
        Writer.Write(nameof(Value.cameraVelocityScale)); WriteValue(Value.cameraVelocityScale);
        Writer.Write(nameof(Value.enableGPUInstancing)); WriteValue(Value.enableGPUInstancing);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.flip)); WriteValue(Value.flip);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.freeformStretching)); WriteValue(Value.freeformStretching);
        Writer.Write(nameof(Value.lengthScale)); WriteValue(Value.lengthScale);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.maskInteraction)); WriteValue(Value.maskInteraction);
        Writer.Write(nameof(Value.maxParticleSize)); WriteValue(Value.maxParticleSize);
        Writer.Write(nameof(Value.meshDistribution)); WriteValue(Value.meshDistribution);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.minParticleSize)); WriteValue(Value.minParticleSize);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.normalDirection)); WriteValue(Value.normalDirection);
        Writer.Write(nameof(Value.pivot)); WriteValue(Value.pivot);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.renderMode)); WriteValue(Value.renderMode);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.rotateWithStretchDirection)); WriteValue(Value.rotateWithStretchDirection);
        Writer.Write(nameof(Value.shadowBias)); WriteValue(Value.shadowBias);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sortMode)); WriteValue(Value.sortMode);
        Writer.Write(nameof(Value.sortingFudge)); WriteValue(Value.sortingFudge);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
        Writer.Write(nameof(Value.velocityScale)); WriteValue(Value.velocityScale);
    }

    void WriteProperties(UnityEngine.SkinnedMeshRenderer Value)
    {
        Writer.Write(34);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.bones)); WriteValue(Value.bones);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.forceMatrixRecalculationPerRender)); WriteValue(Value.forceMatrixRecalculationPerRender);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.quality)); WriteValue(Value.quality);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.rootBone)); WriteValue(Value.rootBone);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sharedMesh)); WriteValue(Value.sharedMesh);
        Writer.Write(nameof(Value.skinnedMotionVectors)); WriteValue(Value.skinnedMotionVectors);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
        Writer.Write(nameof(Value.updateWhenOffscreen)); WriteValue(Value.updateWhenOffscreen);
        Writer.Write(nameof(Value.vertexBufferTarget)); WriteValue(Value.vertexBufferTarget);
    }

    void WriteProperties(UnityEngine.SpriteMask Value)
    {
        Writer.Write(35);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.alphaCutoff)); WriteValue(Value.alphaCutoff);
        Writer.Write(nameof(Value.backSortingLayerID)); WriteValue(Value.backSortingLayerID);
        Writer.Write(nameof(Value.backSortingOrder)); WriteValue(Value.backSortingOrder);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.frontSortingLayerID)); WriteValue(Value.frontSortingLayerID);
        Writer.Write(nameof(Value.frontSortingOrder)); WriteValue(Value.frontSortingOrder);
        Writer.Write(nameof(Value.isCustomRangeActive)); WriteValue(Value.isCustomRangeActive);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.maskSource)); WriteValue(Value.maskSource);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.sprite)); WriteValue(Value.sprite);
        Writer.Write(nameof(Value.spriteSortPoint)); WriteValue(Value.spriteSortPoint);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
    }

    void WriteProperties(UnityEngine.SpriteRenderer Value)
    {
        Writer.Write(36);
        Writer.Write(nameof(Value.adaptiveModeThreshold)); WriteValue(Value.adaptiveModeThreshold);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.color)); WriteValue(Value.color);
        Writer.Write(nameof(Value.drawMode)); WriteValue(Value.drawMode);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.flipX)); WriteValue(Value.flipX);
        Writer.Write(nameof(Value.flipY)); WriteValue(Value.flipY);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.maskInteraction)); WriteValue(Value.maskInteraction);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.size)); WriteValue(Value.size);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.sprite)); WriteValue(Value.sprite);
        Writer.Write(nameof(Value.spriteSortPoint)); WriteValue(Value.spriteSortPoint);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
        Writer.Write(nameof(Value.tileMode)); WriteValue(Value.tileMode);
    }

    void WriteProperties(UnityEngine.Tilemaps.TilemapRenderer Value)
    {
        Writer.Write(34);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.chunkCullingBounds)); WriteValue(Value.chunkCullingBounds);
        Writer.Write(nameof(Value.chunkSize)); WriteValue(Value.chunkSize);
        Writer.Write(nameof(Value.detectChunkCullingBounds)); WriteValue(Value.detectChunkCullingBounds);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.maskInteraction)); WriteValue(Value.maskInteraction);
        Writer.Write(nameof(Value.maxChunkCount)); WriteValue(Value.maxChunkCount);
        Writer.Write(nameof(Value.maxFrameAge)); WriteValue(Value.maxFrameAge);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.mode)); WriteValue(Value.mode);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sortOrder)); WriteValue(Value.sortOrder);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
    }

    void WriteProperties(UnityEngine.TrailRenderer Value)
    {
        Writer.Write(46);
        Writer.Write(nameof(Value.alignment)); WriteValue(Value.alignment);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.applyActiveColorSpace)); WriteValue(Value.applyActiveColorSpace);
        Writer.Write(nameof(Value.autodestruct)); WriteValue(Value.autodestruct);
        Writer.Write(nameof(Value.colorGradient)); WriteValue(Value.colorGradient);
        Writer.Write(nameof(Value.emitting)); WriteValue(Value.emitting);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.endColor)); WriteValue(Value.endColor);
        Writer.Write(nameof(Value.endWidth)); WriteValue(Value.endWidth);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.generateLightingData)); WriteValue(Value.generateLightingData);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.maskInteraction)); WriteValue(Value.maskInteraction);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.minVertexDistance)); WriteValue(Value.minVertexDistance);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.numCapVertices)); WriteValue(Value.numCapVertices);
        Writer.Write(nameof(Value.numCornerVertices)); WriteValue(Value.numCornerVertices);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.shadowBias)); WriteValue(Value.shadowBias);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.startColor)); WriteValue(Value.startColor);
        Writer.Write(nameof(Value.startWidth)); WriteValue(Value.startWidth);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
        Writer.Write(nameof(Value.textureMode)); WriteValue(Value.textureMode);
        Writer.Write(nameof(Value.textureScale)); WriteValue(Value.textureScale);
        Writer.Write(nameof(Value.time)); WriteValue(Value.time);
        Writer.Write(nameof(Value.widthCurve)); WriteValue(Value.widthCurve);
        Writer.Write(nameof(Value.widthMultiplier)); WriteValue(Value.widthMultiplier);
    }

    void WriteProperties(UnityEngine.U2D.SpriteShapeRenderer Value)
    {
        Writer.Write(28);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.color)); WriteValue(Value.color);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.maskInteraction)); WriteValue(Value.maskInteraction);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
    }

    void WriteProperties(UnityEngine.UIElements.UIRenderer Value)
    {
        Writer.Write(26);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
    }

    void WriteProperties(UnityEngine.VFX.VFXRenderer Value)
    {
        Writer.Write(26);
        Writer.Write(nameof(Value.allowOcclusionWhenDynamic)); WriteValue(Value.allowOcclusionWhenDynamic);
        Writer.Write(nameof(Value.enabled)); WriteValue(Value.enabled);
        Writer.Write(nameof(Value.forceMeshLod)); WriteValue(Value.forceMeshLod);
        Writer.Write(nameof(Value.forceRenderingOff)); WriteValue(Value.forceRenderingOff);
        Writer.Write(nameof(Value.lightProbeProxyVolumeOverride)); WriteValue(Value.lightProbeProxyVolumeOverride);
        Writer.Write(nameof(Value.lightProbeUsage)); WriteValue(Value.lightProbeUsage);
        Writer.Write(nameof(Value.lightmapIndex)); WriteValue(Value.lightmapIndex);
        Writer.Write(nameof(Value.lightmapScaleOffset)); WriteValue(Value.lightmapScaleOffset);
        Writer.Write(nameof(Value.localBounds)); WriteValue(Value.localBounds);
        Writer.Write(nameof(Value.meshLodSelectionBias)); WriteValue(Value.meshLodSelectionBias);
        Writer.Write(nameof(Value.motionVectorGenerationMode)); WriteValue(Value.motionVectorGenerationMode);
        Writer.Write(nameof(Value.probeAnchor)); WriteValue(Value.probeAnchor);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlags)); WriteValue(Value.rayTracingAccelerationStructureBuildFlags);
        Writer.Write(nameof(Value.rayTracingAccelerationStructureBuildFlagsOverride)); WriteValue(Value.rayTracingAccelerationStructureBuildFlagsOverride);
        Writer.Write(nameof(Value.rayTracingMode)); WriteValue(Value.rayTracingMode);
        Writer.Write(nameof(Value.realtimeLightmapIndex)); WriteValue(Value.realtimeLightmapIndex);
        Writer.Write(nameof(Value.realtimeLightmapScaleOffset)); WriteValue(Value.realtimeLightmapScaleOffset);
        Writer.Write(nameof(Value.receiveShadows)); WriteValue(Value.receiveShadows);
        Writer.Write(nameof(Value.reflectionProbeUsage)); WriteValue(Value.reflectionProbeUsage);
        Writer.Write(nameof(Value.rendererPriority)); WriteValue(Value.rendererPriority);
        Writer.Write(nameof(Value.renderingLayerMask)); WriteValue(Value.renderingLayerMask);
        Writer.Write(nameof(Value.shadowCastingMode)); WriteValue(Value.shadowCastingMode);
        Writer.Write(nameof(Value.sortingLayerID)); WriteValue(Value.sortingLayerID);
        Writer.Write(nameof(Value.sortingLayerName)); WriteValue(Value.sortingLayerName);
        Writer.Write(nameof(Value.sortingOrder)); WriteValue(Value.sortingOrder);
        Writer.Write(nameof(Value.staticShadowCaster)); WriteValue(Value.staticShadowCaster);
    }

    bool TryWriteValueMembers(object Value)
    {
        switch (Value)
        {
            case UnityEngine.Flare.FlareElement Typed: WriteMembers(Typed); return true;
            case UnityEngine.LayerMask Typed: WriteMembers(Typed); return true;
            case UnityEngine.ParticleSystem.Burst Typed: WriteMembers(Typed); return true;
            case UnityEngine.ParticleSystem.MinMaxCurve Typed: WriteMembers(Typed); return true;
            case UnityEngine.ParticleSystem.MinMaxGradient Typed: WriteMembers(Typed); return true;
            case UnityEngine.Rendering.SubMeshDescriptor Typed: WriteMembers(Typed); return true;
            case UnityEngine.Rendering.VertexAttributeDescriptor Typed: WriteMembers(Typed); return true;
            default: return false;
        }
    }

    void WriteMembers(UnityEngine.Flare.FlareElement Value)
    {
        Writer.Write(8);
        Writer.Write(nameof(Value.color)); WriteValue(Value.color);
        Writer.Write(nameof(Value.fade)); WriteValue(Value.fade);
        Writer.Write(nameof(Value.imageIndex)); WriteValue(Value.imageIndex);
        Writer.Write(nameof(Value.position)); WriteValue(Value.position);
        Writer.Write(nameof(Value.rotate)); WriteValue(Value.rotate);
        Writer.Write(nameof(Value.size)); WriteValue(Value.size);
        Writer.Write(nameof(Value.useLightColor)); WriteValue(Value.useLightColor);
        Writer.Write(nameof(Value.zoom)); WriteValue(Value.zoom);
        Writer.Write(0);
    }

    void WriteMembers(UnityEngine.LayerMask Value)
    {
        Writer.Write(0);
        Writer.Write(1);
        Writer.Write(nameof(Value.value)); WriteValue(Value.value);
    }

    void WriteMembers(UnityEngine.ParticleSystem.Burst Value)
    {
        Writer.Write(0);
        Writer.Write(7);
        Writer.Write(nameof(Value.count)); WriteValue(Value.count);
        Writer.Write(nameof(Value.cycleCount)); WriteValue(Value.cycleCount);
        Writer.Write(nameof(Value.maxCount)); WriteValue(Value.maxCount);
        Writer.Write(nameof(Value.minCount)); WriteValue(Value.minCount);
        Writer.Write(nameof(Value.probability)); WriteValue(Value.probability);
        Writer.Write(nameof(Value.repeatInterval)); WriteValue(Value.repeatInterval);
        Writer.Write(nameof(Value.time)); WriteValue(Value.time);
    }

    void WriteMembers(UnityEngine.Rendering.SubMeshDescriptor Value)
    {
        Writer.Write(0);
        Writer.Write(7);
        Writer.Write(nameof(Value.baseVertex)); WriteValue(Value.baseVertex);
        Writer.Write(nameof(Value.bounds)); WriteValue(Value.bounds);
        Writer.Write(nameof(Value.firstVertex)); WriteValue(Value.firstVertex);
        Writer.Write(nameof(Value.indexCount)); WriteValue(Value.indexCount);
        Writer.Write(nameof(Value.indexStart)); WriteValue(Value.indexStart);
        Writer.Write(nameof(Value.topology)); WriteValue(Value.topology);
        Writer.Write(nameof(Value.vertexCount)); WriteValue(Value.vertexCount);
    }

    void WriteMembers(UnityEngine.Rendering.VertexAttributeDescriptor Value)
    {
        Writer.Write(0);
        Writer.Write(4);
        Writer.Write(nameof(Value.attribute)); WriteValue(Value.attribute);
        Writer.Write(nameof(Value.dimension)); WriteValue(Value.dimension);
        Writer.Write(nameof(Value.format)); WriteValue(Value.format);
        Writer.Write(nameof(Value.stream)); WriteValue(Value.stream);
    }

}
