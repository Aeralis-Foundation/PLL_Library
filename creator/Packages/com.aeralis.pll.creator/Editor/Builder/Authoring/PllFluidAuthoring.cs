#nullable enable

using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{

    internal static class PllFluidAuthoringValidator
    {
        internal static void Validate(PLLNativeResolver Resolver, string Context)
        {
            if (!Resolver.enabled || Resolver.Root != Resolver.gameObject
                || Resolver.GetComponents<PLLNativeResolver>().Length != 1 || Resolver.Presentation
                || Resolver.Form is not (PllNativeResolverForm.Source or PllNativeResolverForm.Zone))
                throw new InvalidOperationException(Context + " requires one enabled Fluid resolver on its owned root.");
            PllPresentationAuthoringValidator.ValidateTransition(Resolver.Transition);
            PllNativeFlowSettings.ValidateTransform(Resolver.transform);
            Resolver.ValidateContactArrays();
            var Definition = Resolver.FluidDefinition;
            if (Resolver.Kind == PLLNativeResolverKind.CustomFluid)
            {
                if (!Definition || !EditorUtility.IsPersistent(Definition))
                    throw new InvalidOperationException(Context + " needs a saved Fluid Definition asset.");
                Definition.Validate();
                foreach (var Material in new[] { Definition.ZoneMaterial, Definition.FlowMaterial })
                    if (Material && !EditorUtility.IsPersistent(Material))
                        throw new InvalidOperationException("Fluid materials must be saved assets.");
                foreach (var Prefab in new[] { Definition.LandingPrefab, Definition.SplashPrefab, Definition.SamplesPrefab })
                    if (Prefab) PllSourceIdentityValidator.RequirePrefabAsset(Prefab, "Fluid default output");
                foreach (var Prefab in Definition.DetailsPrefabs) PllSourceIdentityValidator.RequirePrefabAsset(Prefab, "Fluid default detail");
                if (Resolver.GeneratedSurfaceMaterial || Resolver.SourceSettings.Material)
                    throw new InvalidOperationException("Custom Fluid materials belong to its Fluid Definition.");
            }
            else if (Resolver.Kind is not (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava) || Definition)
                throw new InvalidOperationException(Context + " has a conflicting or unsupported Fluid selection.");
            var Behavior = Definition ? Definition.Behavior
                : Resolver.Kind == PLLNativeResolverKind.Water ? PllFluidBehavior.Water : PllFluidBehavior.Lava;
            Resolver.ValidateEmitterModel();
            if (Resolver.Form == PllNativeResolverForm.Zone)
            {
                if (Resolver.SourceProfile == PllNativeFlowProfile.AuthoredSurface)
                    throw new InvalidOperationException("Authored Surface is a Source profile, not a Zone.");
                if (Behavior != PllFluidBehavior.VisualOnly)
                {
                    PllNativeFlowSettings.ValidateZoneTransform(Resolver.transform);
                    if (Resolver.Volume.Shape != PllZoneShape.Box)
                        throw new InvalidOperationException("Fluid gameplay Zones require a Box volume.");
                    if (PllAuthoringValueContract.ZoneVolumeFailure(Resolver.Volume) is { } Failure)
                        throw new InvalidOperationException(Failure.Message);
                }
                RequireSurface(Resolver.SurfaceRenderer, Resolver.transform); return;
            }
            var Settings = Resolver.SourceSettings;
            Settings.ValidateOutputSelections(Definition);
            Settings.Validate(Behavior == PllFluidBehavior.Water ? PLLNativeResolverKind.Water : PLLNativeResolverKind.Lava,
                Resolver.SourceProfile);
            if (Resolver.Transition.CoverDelaySeconds > 0f && Settings.SpawnLights)
                throw new InvalidOperationException("Baked flow lighting cannot fade. Set Cover Delay to zero.");
            if (!Resolver.InitiallyEnabled && Settings.SpawnLights)
                throw new InvalidOperationException("Initially disabled fluid sources cannot use baked flow lights. Disable Spawn Lights.");
            if (Resolver.SourceProfile == PllNativeFlowProfile.AuthoredSurface)
            {
                Resolver.ValidateAuthoredSurface(Behavior);
                if (Settings.LandingModel != PllFlowOutput.None || Settings.SplashPresentation != PllFlowOutput.None
                    || Settings.SamplePresentation != PllFlowOutput.None || Settings.DetailPresentation != PllFlowOutput.None
                    || Settings.SpawnLights || Settings.EnablePusher)
                    throw new InvalidOperationException("Authored Surface does not generate landing models, spline decoration, lights or pushers.");
            }
            else if (Resolver.SourceProfile == PllNativeFlowProfile.RayWaterfall)
            {
                RequireSurface(Resolver.SurfaceRenderer, Resolver.transform);
                foreach (var Anchor in new[] { Settings.RayStart, Settings.RayEnd, Settings.Endpoint })
                    if (Anchor && (Anchor == Resolver.transform || !Anchor.IsChildOf(Resolver.transform)))
                        throw new InvalidOperationException("Flow anchors must be owned children.");
                var Material = Definition ? Definition.FlowMaterial : Settings.Material;
                if (Definition && Definition.FlowMaterialMode == PllFluidMaterialMode.Custom
                    && !Material!.HasProperty("_WorldPositionY"))
                    throw new InvalidOperationException("Ray waterfall material requires the native _WorldPositionY crop channel.");
            }
            else if (Resolver.SurfaceRenderer || Resolver.SurfaceRenderers.Count != 0)
                throw new InvalidOperationException("Simulated Spline generates its surface and cannot use an authored surface renderer.");
            foreach (var Prefab in new[] { Settings.EndRock, Settings.Splash, Settings.SpawnAlongSpline })
                if (Prefab) PllSourceIdentityValidator.RequirePrefabAsset(Prefab, "Flow output input");
            if (Settings.DetailsAlongSpline is { } Details)
                foreach (var Prefab in Details) PllSourceIdentityValidator.RequirePrefabAsset(Prefab, "Flow detail input");
        }

        static void RequireSurface(MeshRenderer? Renderer, Transform Root)
        {
            if (!Renderer || !Renderer.enabled || Renderer.forceRenderingOff || !Renderer.transform.IsChildOf(Root)
                || !Renderer.GetComponent<MeshFilter>() || !Renderer.GetComponent<MeshFilter>().sharedMesh
                || Renderer.sharedMaterials.Length != 1 || !Renderer.sharedMaterial)
                throw new InvalidOperationException("Fluid requires an owned static mesh renderer and one material.");
        }
    }

    [CustomEditor(typeof(PllFluidDefinition))]
    internal sealed class PllFluidDefinitionInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DisplayNameValue"), new GUIContent("Display Name"));
            var Interaction = serializedObject.FindProperty("InteractionValue");
            var Previous = Interaction.managedReferenceValue as PllFluidInteraction;
            var Behavior = Previous?.Behavior ?? PllFluidBehavior.Water;
            EditorGUI.BeginChangeCheck();
            Behavior = (PllFluidBehavior)EditorGUILayout.EnumPopup("Behavior", Behavior);
            if (EditorGUI.EndChangeCheck() || Previous is null)
            {
                Interaction.managedReferenceValue = Behavior switch
                {
                    PllFluidBehavior.Water => new PllWaterInteraction(),
                    PllFluidBehavior.Lava => new PllLavaInteraction(),
                    PllFluidBehavior.VisualOnly => new PllVisualFluidInteraction(),
                    _ => throw new InvalidOperationException("Fluid behavior is unsupported.")
                };
                if (Behavior == PllFluidBehavior.VisualOnly)
                {
                    foreach (var Role in new[] { "Zone", "Flow" })
                        serializedObject.FindProperty(Role + "MaterialModeValue").enumValueIndex = (int)PllFluidMaterialMode.Custom;
                    foreach (var Output in new[] { "Landing", "Splash", "Samples", "Details" })
                        serializedObject.FindProperty(Output + "Value").enumValueIndex = (int)PllFlowOutput.None;
                    foreach (var Output in new[] { "Landing", "Splash", "Samples" })
                        serializedObject.FindProperty(Output + "PrefabValue").objectReferenceValue = null;
                    serializedObject.FindProperty("DetailsPrefabsValue").arraySize = 0;
                }
            }
            if (Behavior != PllFluidBehavior.VisualOnly) EditorGUILayout.PropertyField(Interaction, new GUIContent("Interaction"), true);
            DrawMaterial("Zone"); DrawMaterial("Flow");
            DrawOutput("Landing"); DrawOutput("Splash"); DrawOutput("Samples"); DrawOutput("Details");
            serializedObject.ApplyModifiedProperties();
            try { ((PllFluidDefinition)target).Validate(); }
            catch (ArgumentException Failure) { EditorGUILayout.HelpBox(Failure.Message, MessageType.Error); }
        }

        void DrawMaterial(string Role)
        {
            var Mode = serializedObject.FindProperty(Role + "MaterialModeValue");
            EditorGUI.BeginChangeCheck(); EditorGUILayout.PropertyField(Mode, new GUIContent(Role + " Surface"));
            var Material = serializedObject.FindProperty(Role + "MaterialValue");
            if (EditorGUI.EndChangeCheck() && Mode.enumValueIndex == (int)PllFluidMaterialMode.NativeDefault)
                Material.objectReferenceValue = null;
            if (Mode.enumValueIndex != (int)PllFluidMaterialMode.NativeDefault)
                EditorGUILayout.PropertyField(Material, new GUIContent(Mode.enumValueIndex == (int)PllFluidMaterialMode.Custom ? "Custom Material" : "Native Material Overrides"));
        }

        void DrawOutput(string Name)
        {
            var Choice = serializedObject.FindProperty(Name + "Value");
            var Selected = (PllFlowOutput)Choice.enumValueIndex;
            var Index = Selected == PllFlowOutput.Native ? 0 : Selected == PllFlowOutput.Custom ? 1 : 2;
            EditorGUI.BeginChangeCheck();
            Index = EditorGUILayout.Popup(Name + " Default", Index, new[] { "Native", "Custom", "None" });
            var Payload = serializedObject.FindProperty(Name == "Details" ? "DetailsPrefabsValue" : Name + "PrefabValue");
            if (EditorGUI.EndChangeCheck())
            {
                Choice.enumValueIndex = (int)(Index == 0 ? PllFlowOutput.Native : Index == 1 ? PllFlowOutput.Custom : PllFlowOutput.None);
                if (Index != 1)
                {
                    if (Payload.isArray) Payload.arraySize = 0; else Payload.objectReferenceValue = null;
                }
            }
            if (Index == 1) EditorGUILayout.PropertyField(Payload, true);
        }
    }
}
