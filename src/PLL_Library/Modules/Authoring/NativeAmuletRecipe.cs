using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace PLL.Library;

internal sealed class NativeAmuletRecipe : IDisposable
{
    readonly NativeAmuletSourceContract Provider;
    readonly Transform Parent;
    readonly Dictionary<AmuletSpawnerContract, GameObject[]> Candidates = new();
    GameObject? PrivateRoot;

    internal NativeAmuletRecipe(NativeAmuletSourceContract Provider, Transform Parent)
    {
        this.Provider = Provider ?? throw new ArgumentNullException(nameof(Provider));
        this.Parent = Parent ? Parent : throw new ArgumentNullException(nameof(Parent));
        for (var Index = 0; Index < Provider.CandidateCount; Index++)
            ValidateCandidate(Provider.Candidate(Index));
    }

    internal void Configure(ResolvedAmuletSpawner Source, MapHandler Handler)
    {
        CopyPlacement(Provider.Source, Source.Generator);
        if (!Candidates.TryGetValue(Source.Contract, out var Values))
        {
            Values = new GameObject[Provider.CandidateCount];
            if (Source.Contract.Pedestal is PllNativeAmuletPedestal)
                for (var Index = 0; Index < Values.Length; Index++) Values[Index] = Provider.Candidate(Index);
            else
            {
                if (!PrivateRoot)
                {
                    PrivateRoot = new GameObject("PLL_AmuletPrototypes");
                    PrivateRoot.SetActive(false);
                    PrivateRoot.transform.SetParent(Parent, false);
                    PrivateRoot.AddComponent<NativeAmuletPrototypeRoot>();
                }
                var Custom = (PllCustomAmuletPedestal)Source.Contract.Pedestal;
                for (var Index = 0; Index < Values.Length; Index++)
                {
                    var Original = Provider.Candidate(Index);
                    var Prototype = UnityEngine.Object.Instantiate(Original, PrivateRoot.transform, false);
                    Values[Index] = Prototype;
                    Prototype.name = Original.name;
                    Prototype.transform.localPosition = Vector3.zero;
                    Prototype.transform.localRotation = Quaternion.identity;
                    var Fake = Prototype.GetComponentInChildren<FakeItem>(true);
                    foreach (var Renderer in Prototype.GetComponentsInChildren<MeshRenderer>(true))
                        if (!TransformOwnership.Contains(Fake.transform, Renderer.transform)) Renderer.enabled = false;
                    foreach (var Collider in Prototype.GetComponentsInChildren<Collider>(true))
                        if (!TransformOwnership.Contains(Fake.transform, Collider.transform)) Collider.enabled = false;
                    var Model = UnityEngine.Object.Instantiate(Custom.ModelPrefab, Prototype.transform, false);
                    Model.transform.localPosition = Vector3.zero;
                    Model.transform.localRotation = Quaternion.identity;
                    var Scale = Prototype.transform.localScale;
                    Model.transform.localScale = new Vector3(1f / Scale.x, 1f / Scale.y, 1f / Scale.z);
                    var Anchor = Source.Contract.ResolveAnchor(Model);
                    Fake.transform.SetPositionAndRotation(Anchor.position, Anchor.rotation);
                    Fake.pickedUp = false; Fake.index = -1;
                }
            }
            Candidates.Add(Source.Contract, Values);
        }
        Source.Contract.Configure(Source, Handler, Values, Provider.Source.replaceThis);
    }

    static void CopyPlacement(Peak.PropSpawner_AmuletStatues Source, Peak.PropSpawner_AmuletStatues Target)
    {
        var Rotation = (PSM_SetUpRotationToNormal)Source.modifiers[0];
        var TargetRotation = (PSM_SetUpRotationToNormal)Target.modifiers[0];
        TargetRotation.flipNormal = Rotation.flipNormal;
        TargetRotation.minEffect = Rotation.minEffect; TargetRotation.maxEffect = Rotation.maxEffect;
        TargetRotation.randomPow = Rotation.randomPow;
        var Offset = (PSM_NormalOffset)Source.modifiers[1];
        var TargetOffset = (PSM_NormalOffset)Target.modifiers[1];
        TargetOffset.minOffset = Offset.minOffset; TargetOffset.maxOffset = Offset.maxOffset;
        TargetOffset.randomPow = Offset.randomPow;
        TargetOffset.useAngleBasedAdditives = Offset.useAngleBasedAdditives;
        TargetOffset.angleBasedAdditiveUp = Offset.angleBasedAdditiveUp;
        TargetOffset.angleBasedAdditiveSide = Offset.angleBasedAdditiveSide;
        TargetOffset.angleBasedAdditiveDown = Offset.angleBasedAdditiveDown;
        var Normal = (PSC_Normal)Source.constraints[0];
        var TargetNormal = (PSC_Normal)Target.constraints[0];
        TargetNormal.minAngle = Normal.minAngle; TargetNormal.maxAngle = Normal.maxAngle;
        PSC_Embedded? Embedded = null;
        foreach (var Constraint in Source.postConstraints)
            if (Constraint is PSCP_LateConstraintCheck { Constraint: PSC_Embedded Value }) Embedded = Value;
        var TargetEmbedded = (PSC_Embedded)((PSCP_LateConstraintCheck)Target.postConstraints[1]).Constraint;
        TargetEmbedded.LayerType = Embedded!.LayerType;
        TargetEmbedded.Deadzone = Embedded.Deadzone;
        TargetEmbedded.RaycastDistance = Embedded.RaycastDistance;
        TargetEmbedded.DesiredResult = Embedded.DesiredResult;
        TargetEmbedded.PostSpawnTransformName = Embedded.PostSpawnTransformName;
        NativePrefabContracts.ValidationConstraints(Target) = new List<IValidationConstraint> { TargetEmbedded };
    }

    public void Dispose()
    {
        Candidates.Clear();
        var Owned = PrivateRoot;
        PrivateRoot = null;
        if (Owned) UnityEngine.Object.Destroy(Owned);
    }

    internal static bool IsPrototype(FakeItem Item) =>
        Item.GetComponentInParent<NativeAmuletPrototypeRoot>(true);

    internal static FakeItem[] FindRuntimeFakeItems(FindObjectsInactive Inactive, FindObjectsSortMode Sort)
    {
        var Values = UnityEngine.Object.FindObjectsByType<FakeItem>(Inactive, Sort);
        var Count = 0;
        foreach (var Value in Values)
            if (!IsPrototype(Value)) Values[Count++] = Value;
        if (Count != Values.Length) Array.Resize(ref Values, Count);
        return Values;
    }

    static void ValidateCandidate(GameObject Candidate)
    {
        var Fakes = Candidate.GetComponentsInChildren<FakeItem>(true);
        var Spines = Candidate.GetComponents<SpineCheck>();
        var Triggers = Candidate.GetComponentsInChildren<Peak.TriggerOnFakeItemPickedUp>(true);
        var Scale = Candidate.transform.localScale;
        if (!Candidate.activeSelf || Fakes.Length != 1 || Fakes[0].GetType() != typeof(FakeItem)
            || Spines.Length != 1 || !Spines[0].spine || Spines[0].spine.childCount != 2
            || Triggers.Length != 1 || Triggers[0].fakeItem != Fakes[0]
            || !TransformOwnership.Contains(Candidate.transform, Spines[0].spine)
            || Scale.x <= 0f || !float.IsFinite(Scale.x) || Mathf.Abs(Scale.y - Scale.x) > 0.0001f
            || Mathf.Abs(Scale.z - Scale.x) > 0.0001f
            || !Fakes[0].GetComponentInChildren<MeshRenderer>(true)
            || Candidate.GetComponentInChildren<Photon.Pun.PhotonView>(true))
            throw new InvalidOperationException("The native amulet candidate has an unsupported interaction or placement graph.");
        foreach (var Particle in Triggers[0].disableParticles)
            if (!Particle || !TransformOwnership.Contains(Candidate.transform, Particle.transform))
                throw new InvalidOperationException("The native amulet pickup trigger has a foreign particle dependency.");
        foreach (var Component in Candidate.GetComponentsInChildren<MonoBehaviour>(true))
            if (Component is not FakeItem and not SpineCheck and not Peak.TriggerOnFakeItemPickedUp
                && Component.GetType() != typeof(LazyGizmo))
                throw new InvalidOperationException("The native amulet candidate contains an unsupported lifecycle component.");
    }

    [HarmonyPatch(typeof(FakeItemManager), nameof(FakeItemManager.RefreshList))]
    static class FakeInventoryPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            var Query = ((Func<FindObjectsInactive, FindObjectsSortMode, FakeItem[]>)
                UnityEngine.Object.FindObjectsByType<FakeItem>).Method;
            var Replacement = ((Func<FindObjectsInactive, FindObjectsSortMode, FakeItem[]>)FindRuntimeFakeItems).Method;
            var Replaced = false;
            foreach (var Instruction in Instructions)
            {
                if (Instruction.Calls(Query))
                {
                    if (Replaced) throw new InvalidOperationException("Native FakeItem discovery is ambiguous.");
                    Instruction.opcode = OpCodes.Call; Instruction.operand = Replacement;
                    Replaced = true;
                }
                yield return Instruction;
            }
            if (!Replaced) throw new MissingMethodException("Native FakeItem discovery was not found.");
        }
    }

    [HarmonyPatch(typeof(HelperFunctions), nameof(HelperFunctions.SpawnPrefab),
        typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Transform))]
    static class ComposedCandidatePosePatch
    {
        static bool Prefix(GameObject gameObject, Vector3 position, Quaternion rotation,
            Transform transform, ref GameObject __result)
        {
            if (!gameObject.transform.parent
                || !gameObject.transform.parent.GetComponent<NativeAmuletPrototypeRoot>()) return true;
            __result = UnityEngine.Object.Instantiate(gameObject, position, rotation);
            __result.transform.SetParent(transform, true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Peak.PropSpawner_AmuletStatues), nameof(Peak.PropSpawner_AmuletStatues.OnLevelGenFinished))]
    static class EmptyOwnedOpportunityMaterialPatch
    {
        static bool Prefix(Peak.PropSpawner_AmuletStatues __instance) =>
            !__instance.GetComponentInParent<PllAmuletSpawnerAuthoring>(true)
            || __instance.transform.childCount != 0;
    }
}

internal sealed class NativeAmuletPrototypeRoot : MonoBehaviour { }
