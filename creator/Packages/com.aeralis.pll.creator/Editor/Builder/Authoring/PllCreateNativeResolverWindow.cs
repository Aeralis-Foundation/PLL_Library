using System;
using System.Collections.Generic;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateNativeResolverWindow : EditorWindow
    {
        static readonly PLLNativeResolverKind[] Kinds =
        {
            PLLNativeResolverKind.Water, PLLNativeResolverKind.Lava,
            PLLNativeResolverKind.Rain, PLLNativeResolverKind.Wind,
            PLLNativeResolverKind.Snow, PLLNativeResolverKind.SporeCloud
        };
        static readonly string[] KindLabels = Array.ConvertAll(Kinds, Value => ObjectNames.NicifyVariableName(Value.ToString()));
        static readonly PllNativeFlowProfile[] Profiles = { PllNativeFlowProfile.SimulatedSpline, PllNativeFlowProfile.RayWaterfall };
        static readonly string[] ProfileLabels = Array.ConvertAll(Profiles, Value => ObjectNames.NicifyVariableName(Value.ToString()));
        GameObject Target;
        PLLNativeResolverKind Kind = PLLNativeResolverKind.Water;
        readonly PllAuthoringVolume Volume = new PllAuthoringVolume();
        MeshRenderer Surface;
        Material SurfaceMaterial;
        PllEffectAuthoring Presentation;
        PllNativeResolverForm Form;
        PllNativeFlowProfile Profile = PllNativeFlowProfile.SimulatedSpline;
        Vector3 RayStartPosition, RayEndPosition = Vector3.down * 10f;

        [MenuItem("PLL/Effects/Add Native Resolver to Selected Source", priority = 22)]
        static void Open() => Open(PLLNativeResolverKind.Water);

        internal static void Open(PLLNativeResolverKind Kind)
        {
            if (Array.IndexOf(Kinds, Kind) < 0)
                throw new InvalidOperationException("Choose Water, Lava, Rain, Wind, Snow or SporeCloud.");
            var Window = GetWindow<PllCreateNativeResolverWindow>("Add Native Resolver");
            Window.Target = Selection.activeGameObject;
            Window.Kind = Kind; Window.Form = PllNativeResolverForm.Zone;
            Window.Surface = null; Window.SurfaceMaterial = null; Window.Presentation = null;
            Window.Show();
        }

        void OnGUI()
        {
            Target = (GameObject)EditorGUILayout.ObjectField("Source", Target, typeof(GameObject), true);
            Kind = Kinds[EditorGUILayout.Popup("Native Kind", Array.IndexOf(Kinds, Kind), KindLabels)];
            if (Kind is PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava)
                Form = (PllNativeResolverForm)EditorGUILayout.EnumPopup("Form", Form);
            else Form = PllNativeResolverForm.Zone;
            if (Form == PllNativeResolverForm.Source)
            {
                if (Kind == PLLNativeResolverKind.Water)
                    Profile = Profiles[EditorGUILayout.Popup("Profile", Array.IndexOf(Profiles, Profile), ProfileLabels)];
                else Profile = PllNativeFlowProfile.SimulatedSpline;
                if (Profile == PllNativeFlowProfile.RayWaterfall)
                {
                    RayStartPosition = EditorGUILayout.Vector3Field("Ray Start (local meters)", RayStartPosition);
                    RayEndPosition = EditorGUILayout.Vector3Field("Ray End (local meters)", RayEndPosition);
                }
            }
            Volume.Shape = Kind == PLLNativeResolverKind.SporeCloud ? PllZoneShape.Sphere : PllZoneShape.Box;
            Volume.Draw();
            if (Form == PllNativeResolverForm.Source && Profile == PllNativeFlowProfile.SimulatedSpline)
                EditorGUILayout.HelpBox("PEAK supplies the mesh, material and collision setup. Edit the source settings in the Inspector.", MessageType.Info);
            else if (Kind is PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava)
            {
                Surface = (MeshRenderer)EditorGUILayout.ObjectField("Surface Renderer Child", Surface, typeof(MeshRenderer), true);
                if (Kind == PLLNativeResolverKind.Water && Form == PllNativeResolverForm.Zone)
                    SurfaceMaterial = (Material)EditorGUILayout.ObjectField("Generated Surface Material", SurfaceMaterial, typeof(Material), false);
                else SurfaceMaterial = null;
                EditorGUILayout.HelpBox(Form == PllNativeResolverForm.Source
                    ? "Choose a surface renderer inside this source. PLL creates its ray anchors and builds the native flow in PEAK."
                    : Kind == PLLNativeResolverKind.Water
                        ? "Choose one surface renderer or material. Water Zone requires a world-upright box."
                        : "Choose a lava surface renderer inside this source. PLL uses its placed geometry to set the heat bounds.", MessageType.Info);
            }
            else
            {
                Presentation = (PllEffectAuthoring)EditorGUILayout.ObjectField("Zone Presentation Child", Presentation, typeof(PllEffectAuthoring), true);
                EditorGUILayout.HelpBox("Choose a child effect in Zone mode. SporeCloud needs duration zero and a radius matching the native source in PEAK.", MessageType.Info);
            }
            using (new EditorGUI.DisabledScope(!Target))
                if (GUILayout.Button("Add Native Resolver"))
                {
                    try
                    {
                        Create(Target, Kind, Volume,
                            Kind is PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava
                                && !(Form == PllNativeResolverForm.Source && Profile == PllNativeFlowProfile.SimulatedSpline) ? Surface : null,
                            Kind == PLLNativeResolverKind.Water && Form == PllNativeResolverForm.Zone ? SurfaceMaterial : null,
                            Kind is PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava ? null : Presentation,
                            Form, Profile, RayStartPosition, RayEndPosition);
                        Close();
                    }
                    catch (Exception Failure) when (Failure is InvalidOperationException or ArgumentException)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
        }

        internal static PLLNativeResolver Create(GameObject Target, PLLNativeResolverKind Kind,
            PllAuthoringVolume Volume, MeshRenderer Surface, Material SurfaceMaterial,
            PllEffectAuthoring Presentation, PllNativeResolverForm Form = PllNativeResolverForm.Zone,
            PllNativeFlowProfile Profile = PllNativeFlowProfile.SimulatedSpline,
            Vector3 RayStartPosition = default, Vector3 RayEndPosition = default)
        {
            const string Action = "Add Native Resolver";
            var Owner = PllAuthoringCommands.RequireFeatureTarget<PLLNativeResolver>(Target, Action);
            if ((!Owner.IsBiomeRoot && Form != PllNativeResolverForm.Source)
                || Target.transform == Owner.Floor || Target.transform == Owner.Campfire)
                throw new InvalidOperationException("Native Resolver requires its own child root in the biome.");
            if (Array.IndexOf(Kinds, Kind) < 0)
                throw new InvalidOperationException("Choose Water, Lava, Rain, Wind, Snow or SporeCloud.");
            Volume.RequireValid();
            if (Volume.Shape != (Kind == PLLNativeResolverKind.SporeCloud ? PllZoneShape.Sphere : PllZoneShape.Box))
                throw new InvalidOperationException("SporeCloud requires a sphere; water and weather require a box.");
            if (Form is not (PllNativeResolverForm.Zone or PllNativeResolverForm.Source)
                || Form == PllNativeResolverForm.Source && Kind is not (PLLNativeResolverKind.Water or PLLNativeResolverKind.Lava))
                throw new InvalidOperationException("Source Form requires Water or Lava.");
            if (Form == PllNativeResolverForm.Source)
            {
                PllNativeFlowSettings.ValidateTransform(Target.transform);
                if (Presentation || SurfaceMaterial || Kind == PLLNativeResolverKind.Lava && Profile != PllNativeFlowProfile.SimulatedSpline)
                    throw new InvalidOperationException("Choose one supported flow profile with no Zone presentation or generated surface.");
                if (Profile == PllNativeFlowProfile.RayWaterfall)
                {
                    if (!Surface || Surface.transform == Target.transform || !Surface.transform.IsChildOf(Target.transform)
                        || !Surface.TryGetComponent<MeshFilter>(out var Filter) || !Filter.sharedMesh
                        || Surface.sharedMaterials.Length != 1 || !Surface.sharedMaterial)
                        throw new InvalidOperationException("Ray waterfall requires one owned surface mesh and material.");
                    if (!PllSourceIdentityValidator.ManifestNumber(RayStartPosition.x)
                        || !PllSourceIdentityValidator.ManifestNumber(RayStartPosition.y)
                        || !PllSourceIdentityValidator.ManifestNumber(RayStartPosition.z)
                        || !PllSourceIdentityValidator.ManifestNumber(RayEndPosition.x)
                        || !PllSourceIdentityValidator.ManifestNumber(RayEndPosition.y)
                        || !PllSourceIdentityValidator.ManifestNumber(RayEndPosition.z)
                        || RayStartPosition == RayEndPosition)
                        throw new InvalidOperationException("Ray anchor positions must be finite and distinct.");
                }
                else if (Profile != PllNativeFlowProfile.SimulatedSpline || Surface)
                    throw new InvalidOperationException("Simulated flow uses the surface supplied by PEAK.");
            }
            else if (Kind == PLLNativeResolverKind.Lava)
            {
                if (!Surface || Surface.transform == Target.transform || !Surface.transform.IsChildOf(Target.transform)
                    || SurfaceMaterial || Presentation)
                    throw new InvalidOperationException("Lava Zone requires an owned surface renderer and no water material or Zone effect.");
                PllSourceIdentityValidator.RequireEditableTarget(Owner, Surface.transform, Action);
            }
            else if (Kind == PLLNativeResolverKind.Water)
            {
                if ((bool)Surface == (bool)SurfaceMaterial || Presentation)
                    throw new InvalidOperationException("Water requires exactly one surface renderer or material and no Zone presentation.");
                if (SurfaceMaterial && !EditorUtility.IsPersistent(SurfaceMaterial))
                    throw new InvalidOperationException("Save the water surface material as an asset.");
                if (Surface)
                {
                    if (Surface.transform == Target.transform || !Surface.transform.IsChildOf(Target.transform))
                        throw new InvalidOperationException("Water surface renderer must be a strict child of the source.");
                    PllSourceIdentityValidator.RequireEditableTarget(Owner, Surface.transform, Action);
                    var Filter = Surface.GetComponent<MeshFilter>();
                    if (!Filter || !Filter.sharedMesh || Surface.sharedMaterials.Length == 0)
                        throw new InvalidOperationException("Water surface renderer requires a mesh and material slots.");
                    foreach (var Material in Surface.sharedMaterials)
                        if (!Material || !EditorUtility.IsPersistent(Material))
                            throw new InvalidOperationException("Every water surface material slot needs a saved material asset.");
                }
            }
            else
            {
                if (Surface || SurfaceMaterial || !Presentation
                    || Presentation.Mode != PllEffectMode.Zone
                    || Presentation.transform == Target.transform || !Presentation.transform.IsChildOf(Target.transform))
                    throw new InvalidOperationException("Weather and SporeCloud require one Zone-mode presentation child and no water surface.");
                PllSourceIdentityValidator.RequireEditableTarget(Owner, Presentation.transform, Action);
                PllSourceIdentityValidator.ValidateCreatorComponent(Presentation, Action);
            }
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Settings = Form == PllNativeResolverForm.Source ? PllNativeFlowSettings.CreateDefault(Kind) : default;
                if (Form == PllNativeResolverForm.Source && Profile == PllNativeFlowProfile.RayWaterfall)
                {
                    var Start = new GameObject("RayStart"); var End = new GameObject("RayEnd");
                    Undo.RegisterCreatedObjectUndo(Start, Action); Undo.RegisterCreatedObjectUndo(End, Action);
                    Start.transform.SetParent(Target.transform, false); End.transform.SetParent(Target.transform, false);
                    Start.transform.localPosition = RayStartPosition; End.transform.localPosition = RayEndPosition;
                    Settings.RayStart = Start.transform; Settings.RayEnd = End.transform;
                }
                var Source = Undo.AddComponent<PLLNativeResolver>(Target);
                using (var Serialized = new SerializedObject(Source))
                {
                    PllAuthoringCommands.SetReference(Serialized, "root", Target);
                    PllAuthoringCommands.Property(Serialized, "kind").intValue = (int)Kind;
                    Volume.Apply(Serialized);
                    PllAuthoringCommands.Property(Serialized, "FormValue").intValue = (int)Form;
                    PllAuthoringCommands.Property(Serialized, "SourceProfileValue").intValue = (int)Profile;
                    PllAuthoringCommands.Property(Serialized, "SourceSettingsValue").boxedValue = Settings;
                    PllAuthoringCommands.SetReference(Serialized, "surfaceRenderer", Surface);
                    PllAuthoringCommands.SetReference(Serialized, "generatedSurfaceMaterial", SurfaceMaterial);
                    PllAuthoringCommands.SetReference(Serialized, "presentation", Presentation);
                    Serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return Source;
            }, () =>
            {
                var Presentations = new HashSet<PllEffectAuthoring>();
                foreach (var Zone in Owner.StageRoot.GetComponentsInChildren<PllEffectZoneAuthoring>(true))
                    foreach (var Effect in Zone.Effects) Presentations.Add(Effect);
                if (!Owner.IsBiomeRoot)
                {
                    foreach (var Resolver in Owner.StageRoot.GetComponentsInChildren<PLLNativeResolver>(true))
                        PllPresentationAuthoringValidator.ValidateFlowSource(Resolver, Owner.Label);
                    return;
                }
                PllPresentationAuthoringValidator.ValidateNativeResolvers(Owner.StageRoot,
                    Owner.Floor, Owner.Campfire,
                    PllSourceIdentityValidator.DiscoverComponents<PLLNativeResolver>(Owner.StageRoot),
                    Owner.Label, Presentations);
            });
        }
    }
}
