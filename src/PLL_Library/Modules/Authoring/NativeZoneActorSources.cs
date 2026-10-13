using System;
using System.IO;
using Photon.Pun;
using UnityEngine;

namespace PLL.Library;

internal static class NativeNetworkPrefabGraph
{
    internal static void AdmitAliveCount(int Count)
    {
        if (Count >= PhotonNetwork.MAX_VIEW_IDS)
            throw new InvalidOperationException($"The hazard alive count {Count} exceeds the native PhotonView capacity {PhotonNetwork.MAX_VIEW_IDS - 1}.");
    }

    internal static GameObject Resource(string Address)
    {
        var Prefab = Resources.Load<GameObject>(Address);
        if (!Prefab) throw new InvalidOperationException($"PEAK's native actor resource '{Address}' is missing.");
        if (PhotonNetwork.PrefabPool is DefaultPool Pool
            && Pool.ResourceCache.TryGetValue(Address, out var Cached) && Cached != Prefab)
            throw new InvalidOperationException($"Photon resource '{Address}' no longer refers to PEAK's native actor.");
        return Prefab;
    }

    internal static byte[] Fingerprint(GameObject Prefab, string Domain, Action<BinaryWriter> WritePolicy)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        {
            Writer.Write(Domain);
            var Graph = DeterministicObjectIdentity.HashObjectHierarchy(Prefab.transform, Domain);
            Writer.Write(Graph.Length); Writer.Write(Graph);
            foreach (var View in Prefab.GetComponentsInChildren<PhotonView>(true))
            {
                Writer.Write((int)View.Synchronization); Writer.Write((int)View.OwnershipTransfer);
                Writer.Write(View.ObservedComponents.Count);
                foreach (var Component in View.ObservedComponents)
                    Writer.Write(DeterministicObjectIdentity.RelativeTypedComponentStructuralPath(Component, Prefab.transform));
            }
            foreach (var Renderer in Prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Writer.Write(Renderer.bones.Length);
                foreach (var Bone in Renderer.bones)
                    Writer.Write(DeterministicObjectIdentity.RelativeComponentStructuralPath(Bone, Prefab.transform));
            }
            WritePolicy(Writer);
        }
        return Stream.ToArray();
    }

    internal static void RequireSkin(GameObject Prefab, int BoneCount)
    {
        var Found = false;
        foreach (var Renderer in Prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!Renderer.sharedMesh || Renderer.bones.Length != Renderer.sharedMesh.bindposes.Length)
                throw new InvalidOperationException("PEAK's native actor has an incomplete skinned mesh.");
            foreach (var Bone in Renderer.bones)
                if (!Bone || !TransformOwnership.Contains(Prefab.transform, Bone))
                    throw new InvalidOperationException("PEAK's native actor skeleton leaves its owning graph.");
            Found |= Renderer.bones.Length == BoneCount;
        }
        if (!Found) throw new InvalidOperationException($"PEAK's native actor has no complete {BoneCount}-bone renderer.");
    }

    internal static void WriteCurve(BinaryWriter Writer, AnimationCurve Curve)
    {
        Writer.Write((int)Curve.preWrapMode); Writer.Write((int)Curve.postWrapMode);
        var Keys = Curve.keys; Writer.Write(Keys.Length);
        foreach (var Key in Keys)
        {
            ManifestEncoding.WriteScalar(Writer, Key.time); ManifestEncoding.WriteScalar(Writer, Key.value);
            ManifestEncoding.WriteScalar(Writer, Key.inTangent); ManifestEncoding.WriteScalar(Writer, Key.outTangent);
            ManifestEncoding.WriteScalar(Writer, Key.inWeight); ManifestEncoding.WriteScalar(Writer, Key.outWeight);
            Writer.Write((int)Key.weightedMode);
        }
    }
}

internal sealed class NativeTornadoSource
{
    readonly byte[] Fingerprint;
    readonly float FirstMinimum;
    readonly float FirstMaximum;
    readonly float RepeatMinimum;
    readonly float RepeatMaximum;

    NativeTornadoSource(TornadoSpawner Source, GameObject Prefab)
    {
        FirstMinimum = Source.minSpawnTimeFirst; FirstMaximum = Source.maxSpawnTimeFirst;
        RepeatMinimum = Source.minSpawnTime; RepeatMaximum = Source.maxSpawnTime;
        Fingerprint = NativeNetworkPrefabGraph.Fingerprint(Prefab, "PLL/NativeTornado/v1", Writer =>
        {
            ManifestEncoding.WriteScalar(Writer, FirstMinimum); ManifestEncoding.WriteScalar(Writer, FirstMaximum);
            ManifestEncoding.WriteScalar(Writer, RepeatMinimum); ManifestEncoding.WriteScalar(Writer, RepeatMaximum);
            var Actor = Prefab.GetComponent<global::Tornado>();
            ManifestEncoding.WriteScalar(Writer, Actor.force); ManifestEncoding.WriteScalar(Writer, Actor.range);
            ManifestEncoding.WriteScalar(Writer, Actor.captureDistance);
            ManifestEncoding.WriteScalar(Writer, Actor.tornadoLifetimeMin);
            ManifestEncoding.WriteScalar(Writer, Actor.tornadoLifetimeMax);
            NativeNetworkPrefabGraph.WriteCurve(Writer, Actor.inStrC); NativeNetworkPrefabGraph.WriteCurve(Writer, Actor.upStrC);
        });
    }

    internal static NativeTornadoSource Capture(NativeSegmentCatalog Catalog)
    {
        var Root = Catalog.RequireOrdinary("Mesa").SegmentParent.transform.Find("Misc/Tornados");
        var Source = Root ? Root.GetComponent<TornadoSpawner>() : null;
        var Points = Root ? Root.Find("TornadoPoints") : null;
        var Prefab = NativeNetworkPrefabGraph.Resource("Tornado");
        var Actor = Prefab.GetComponent<global::Tornado>();
        var View = Prefab.GetComponent<PhotonView>();
        if (!Source || !Points || Points.childCount == 0 || !Root!.GetComponent<PhotonView>()
            || !Actor || !Actor.tornadoSFX || !View || View.OwnershipTransfer != OwnershipOption.Fixed
            || Prefab.GetComponentsInChildren<PhotonView>(true).Length != 1
            || !Prefab.GetComponent<PhotonTransformView>()
            || !Prefab.GetComponent<Peak.PhotonCleanupHelper>()
            || Source.minSpawnTimeFirst <= 0f || Source.maxSpawnTimeFirst < Source.minSpawnTimeFirst
            || Source.minSpawnTime <= 0f || Source.maxSpawnTime < Source.minSpawnTime)
            throw new InvalidOperationException("PEAK's native Tornado source has an incomplete timer, point or actor graph.");
        return new NativeTornadoSource(Source, Prefab);
    }

    internal void Copy(TornadoSpawner Target)
    {
        Target.minSpawnTimeFirst = FirstMinimum; Target.maxSpawnTimeFirst = FirstMaximum;
        Target.minSpawnTime = RepeatMinimum; Target.maxSpawnTime = RepeatMaximum;
    }

    internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }
}

internal sealed class NativeTumbleweedSource
{
    readonly byte[] Fingerprint;
    readonly float Minimum;
    readonly float Maximum;
    readonly float Exponent;
    readonly RunSettings.SETTINGTYPE Setting;

    NativeTumbleweedSource(NetworkedObjectSpawner Source, RunSettings.SETTINGTYPE Setting)
    {
        Prefab = Source.objToSpawn; Minimum = Source.minRate; Maximum = Source.maxRate;
        Exponent = Source.randomPow; this.Setting = Setting;
        Fingerprint = NativeNetworkPrefabGraph.Fingerprint(Prefab, "PLL/NativeTumbleweed/v1", Writer =>
        {
            ManifestEncoding.WriteScalar(Writer, Minimum); ManifestEncoding.WriteScalar(Writer, Maximum);
            ManifestEncoding.WriteScalar(Writer, Exponent); Writer.Write((int)Setting);
            var Actor = Prefab.GetComponent<TumbleWeed>();
            ManifestEncoding.WriteScalar(Writer, Actor.rollForce);
            ManifestEncoding.WriteScalar(Writer, Actor.collisionForce);
            ManifestEncoding.WriteScalar(Writer, Actor.powerMultiplier);
            Writer.Write(Actor.testFullPower);
            var Lifetime = Prefab.GetComponent<RemoveAfterSeconds>();
            ManifestEncoding.WriteScalar(Writer, Lifetime.seconds);
            Writer.Write(Lifetime.shrink); Writer.Write(Lifetime.photonRemove);
        });
    }

    internal GameObject Prefab { get; }

    internal static NativeTumbleweedSource Capture(NativeSegmentCatalog Catalog)
    {
        var Root = Catalog.RequireOrdinary("Mesa").SegmentParent.transform.Find("Platteau/Props/Tumblers");
        NetworkedObjectSpawner? Source = null;
        var Prefab = NativeNetworkPrefabGraph.Resource("tumbleweed");
        if (Root)
            foreach (var Candidate in Root.GetComponentsInChildren<NetworkedObjectSpawner>(true))
            {
                var Setting = Candidate.GetComponent<DisableBasedOnRunSettings>();
                if (!Setting || Setting.disableIfSettingDisabled != RunSettings.SETTINGTYPE.Hazard_Tumbleweeds
                    || Candidate.objToSpawn != Prefab || Candidate.minRate <= 0f
                    || Candidate.maxRate < Candidate.minRate || Candidate.randomPow <= 0f)
                    throw new InvalidOperationException("PEAK's native Tumbleweed source has an invalid resource or timer policy.");
                if (Source && (Source.minRate != Candidate.minRate || Source.maxRate != Candidate.maxRate
                    || Source.randomPow != Candidate.randomPow || Source.spawnOffset != Candidate.spawnOffset))
                    throw new InvalidOperationException("PEAK's native Tumbleweed sources have different timer or offset policies.");
                Source = Candidate;
            }
        var View = Prefab.GetComponent<PhotonView>();
        var Lifetime = Prefab.GetComponent<RemoveAfterSeconds>();
        var Body = Prefab.GetComponent<Rigidbody>();
        var Collision = Prefab.GetComponent<SphereCollider>();
        if (!Source || !View || View.OwnershipTransfer != OwnershipOption.Fixed
            || Prefab.GetComponentsInChildren<PhotonView>(true).Length != 1
            || !Prefab.GetComponent<TumbleWeed>() || !Prefab.GetComponent<PhotonRigidbodyView>()
            || !Prefab.GetComponent<Peak.PhotonCleanupHelper>() || !Lifetime
            || Lifetime.seconds <= 0f || !Lifetime.photonRemove || !Body || Body.isKinematic
            || !Collision || !Collision.enabled || Collision.isTrigger)
            throw new InvalidOperationException("PEAK's native Tumbleweed has an incomplete physics, network or lifetime graph.");
        return new NativeTumbleweedSource(Source, RunSettings.SETTINGTYPE.Hazard_Tumbleweeds);
    }

    internal void Copy(NetworkedObjectSpawner Target, DisableBasedOnRunSettings TargetSetting)
    {
        Target.objToSpawn = Prefab; Target.minRate = Minimum; Target.maxRate = Maximum;
        Target.randomPow = Exponent; TargetSetting.disableIfSettingDisabled = Setting;
    }

    internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }
}

internal sealed class NativeZombieSource
{
    readonly byte[] Fingerprint;

    NativeZombieSource(GameObject Prefab)
    {
        this.Prefab = Prefab;
        var Zombie = Prefab.GetComponent<MushroomZombieSpawner>().mushroomZombiePrefab;
        Fingerprint = NativeNetworkPrefabGraph.Fingerprint(Zombie.gameObject, "PLL/NativeZombie/v1", Writer =>
        {
            Writer.Write(Zombie.isNPCZombie);
            ManifestEncoding.WriteScalar(Writer, MushroomZombieSpawner.SPAWN_CHANCE);
            ManifestEncoding.WriteScalar(Writer, Zombie.distanceToEnable);
            ManifestEncoding.WriteScalar(Writer, Zombie.reachForce);
            ManifestEncoding.WriteScalar(Writer, Zombie.zombieSprintDistance);
            ManifestEncoding.WriteScalar(Writer, Zombie.lookAngleBeforeWakeup);
            ManifestEncoding.WriteScalar(Writer, Zombie.distanceBeforeWakeup);
            ManifestEncoding.WriteScalar(Writer, Zombie.initialWakeUpTime);
            ManifestEncoding.WriteScalar(Writer, Zombie.distanceBeforeChase);
            ManifestEncoding.WriteScalar(Writer, Zombie.chaseTimeBeforeSprint);
            ManifestEncoding.WriteScalar(Writer, Zombie.zombieLungeDistance);
            ManifestEncoding.WriteScalar(Writer, Zombie.lungeTime);
            ManifestEncoding.WriteScalar(Writer, Zombie.lungeRecoveryTime);
            ManifestEncoding.WriteScalar(Writer, Zombie.biteStunTime);
            ManifestEncoding.WriteScalar(Writer, Zombie.biteInitialInjury);
            ManifestEncoding.WriteScalar(Writer, Zombie.biteInitialSpores);
            ManifestEncoding.WriteScalar(Writer, Zombie.biteDelayBeforeSpores);
            ManifestEncoding.WriteScalar(Writer, Zombie.biteSporesPerSecond);
            ManifestEncoding.WriteScalar(Writer, Zombie.totalBiteSporesTime);
            ManifestEncoding.WriteScalar(Writer, Zombie.lifetime);
            ManifestEncoding.WriteScalar(Writer, Zombie.minMaxMushroomGrowTime.x);
            ManifestEncoding.WriteScalar(Writer, Zombie.minMaxMushroomGrowTime.y);
        });
    }

    internal GameObject Prefab { get; }

    internal static NativeZombieSource Capture(NativeSegmentCatalog Catalog)
    {
        var Root = Catalog.RequireOrdinary("Roots").SegmentParent.transform.Find("PlateauProps/Zombie Spawners");
        var Spawner = Root ? Root.GetComponent<PropSpawner>() : null;
        var Candidates = Spawner ? NativePrefabContracts.EffectiveProps(Spawner) : null;
        GameObject? Prefab = null;
        if (Candidates is not null)
            foreach (var Candidate in Candidates)
            {
                if (!Candidate) throw new InvalidOperationException("PEAK's native zombie source has a missing candidate.");
                if (!Candidate.GetComponent<MushroomZombieSpawner>()) continue;
                if (Prefab && Prefab != Candidate)
                    throw new InvalidOperationException("PEAK's native candidate graph has distinct NPC zombie markers.");
                Prefab = Candidate;
            }
        var SourceMarker = Prefab ? Prefab.GetComponent<MushroomZombieSpawner>() : null;
        var Zombie = SourceMarker ? SourceMarker.mushroomZombiePrefab : null;
        if (!SourceMarker || !Zombie || !Zombie.isNPCZombie || Zombie.gameObject.name != "MushroomZombie"
            || Zombie.gameObject != NativeNetworkPrefabGraph.Resource("MushroomZombie")
            || Prefab!.GetComponentsInChildren<PhotonView>(true).Length != 0
            || !Zombie.GetComponent<Character>() || !Zombie.GetComponent<CharacterSyncer>()
            || Zombie.GetComponentsInChildren<PhotonView>(true).Length != 2)
            throw new InvalidOperationException("PEAK's native NPC zombie marker has an incomplete Character or Photon graph.");
        NativeNetworkPrefabGraph.RequireSkin(Zombie.gameObject, 120);
        return new NativeZombieSource(Prefab!);
    }

    internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }
}

internal sealed class NativeCondorSource
{
    readonly GameObject Holder;
    readonly byte[] Fingerprint;

    NativeCondorSource(GameObject Source)
    {
        Holder = new GameObject("__PLL_CondorTemplate");
        Holder.SetActive(false);
        try
        {
            Prefab = UnityEngine.Object.Instantiate(Source, Holder.transform, false);
            Prefab.name = Source.name;
            SkinDescriptor = NativeEntitySkinDescriptor.Capture(Prefab);
            var Bird = Source.GetComponent<Condor>();
            Fingerprint = NativeNetworkPrefabGraph.Fingerprint(Source, "PLL/NativeCondor/v1", Writer =>
            {
                ManifestEncoding.WriteScalar(Writer, Bird.radius); ManifestEncoding.WriteScalar(Writer, Bird.loopDuration);
                ManifestEncoding.WriteScalar(Writer, Bird.noiseStrength); Writer.Write(Bird.seed);
                ManifestEncoding.WriteScalar(Writer, Bird.extraDrag); ManifestEncoding.WriteScalar(Writer, Bird.grabForce);
                ManifestEncoding.WriteScalar(Writer, Bird.swoopTime);
                ManifestEncoding.WriteScalar(Writer, Bird.scanCharacterSuccessDistance);
                ManifestEncoding.WriteScalar(Writer, Bird.headGrabMult); ManifestEncoding.WriteScalar(Writer, Bird.bodyGrabMult);
                ManifestEncoding.WriteScalar(Writer, Bird.carryTime); ManifestEncoding.WriteScalar(Writer, Bird.moveToNestSpeed);
                ManifestEncoding.WriteScalar(Writer, Bird.nestDropHeight); ManifestEncoding.WriteScalar(Writer, Bird.nestDropDistance);
                ManifestEncoding.WriteScalar(Writer, Bird.returnToLoopDistance);
                ManifestEncoding.WriteScalar(Writer, Bird.letGoForceFalloffTime);
                NativeNetworkPrefabGraph.WriteCurve(Writer, Bird.swoopCurve);
                Writer.Write(DeterministicObjectIdentity.RelativeComponentStructuralPath(Bird.dragToPoint, Source.transform));
            });
            PhotonCloneIdentity.Reset(Prefab);
            Prefab.GetComponent<Condor>().nest = null;
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(Holder);
            throw;
        }
    }

    internal GameObject Prefab { get; }

    internal NativeEntitySkinDescriptor SkinDescriptor { get; }

    internal void Release() => UnityEngine.Object.DestroyImmediate(Holder);

    internal static NativeCondorSource Capture(NativeSegmentCatalog Catalog)
    {
        var Root = Catalog.RequireOrdinary("Caldera").SegmentParent.transform.Find(
            "Rocks/UltraTall/LavaPillar_ExtraTall/Condor");
        var Bird = Root ? Root.GetComponent<Condor>() : null;
        var Setting = Root ? Root.GetComponent<DisableBasedOnRunSettings>() : null;
        if (!Bird || !Bird.view || !Bird.rb || !Bird.anim || !Bird.anim.runtimeAnimatorController
            || !Bird.nest || Bird.nest.transform.parent != Root!.parent || !Bird.dragToPoint
            || !TransformOwnership.Contains(Root, Bird.dragToPoint) || !Bird.carryTrigger
            || !TransformOwnership.Contains(Root, Bird.carryTrigger.transform)
            || !Bird.carryTrigger.GetComponent<CapsuleCollider>() || !Setting
            || Setting.disableIfSettingDisabled != RunSettings.SETTINGTYPE.Hazard_Condors)
            throw new InvalidOperationException("PEAK's native Condor profile has an incomplete body, socket, nest or setting graph.");
        NativeNetworkPrefabGraph.RequireSkin(Root.gameObject, 54);
        return new NativeCondorSource(Root.gameObject);
    }

    internal void WriteManifest(BinaryWriter Writer) { Writer.Write(Fingerprint.Length); Writer.Write(Fingerprint); }
}
