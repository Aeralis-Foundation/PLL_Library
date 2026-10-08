using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateNativeActorZoneWindow : EditorWindow
    {
        enum NativeActor { Beetle, MushroomZombie, Condor, Tornado, Tumbleweed }

        GameObject Parent;
        string Id;
        Vector3 LocalPosition;
        readonly PllAuthoringVolume Volume = new PllAuthoringVolume();
        object OwnedSource = new PllBeetleZoneSource();
        Vector2 Scroll;

        [MenuItem("PLL/Spawning/Create Native Actor Zone", priority = 28)]
        static void Open() => Open(new PllBeetleZoneSource());

        internal static void Open(PllEntityZoneSource OwnedSource)
        {
            if (OwnedSource is not (PllBeetleZoneSource or PllMushroomZombieZoneSource or PllCondorZoneSource))
                throw new InvalidOperationException("Choose native Beetle, Mushroom Zombie or Condor.");
            OpenSource(OwnedSource);
        }

        internal static void Open(PllHazardZoneSource OwnedSource)
        {
            if (OwnedSource is not (PllTornadoZoneSource or PllTumbleweedZoneSource))
                throw new InvalidOperationException("Choose native Tornado or Tumbleweed.");
            OpenSource(OwnedSource);
        }

        static void OpenSource(object OwnedSource)
        {
            var Window = GetWindow<PllCreateNativeActorZoneWindow>("Create Native Actor Zone");
            var Selected = Selection.activeGameObject;
            var Anchors = Selected ? Selected.GetComponent<PllBiomePrefabAnchors>() : null;
            Window.Parent = Anchors ? Anchors.FloorRoot : Selected;
            Window.Id = "actor-" + Guid.NewGuid().ToString("N");
            Window.LocalPosition = Vector3.zero;
            Window.OwnedSource = OwnedSource;
            Window.Show();
        }

        void OnGUI()
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            Parent = (GameObject)EditorGUILayout.ObjectField("Parent", Parent, typeof(GameObject), true);
            Id = EditorGUILayout.TextField("Stable Zone ID", Id);
            LocalPosition = EditorGUILayout.Vector3Field("Parent-local Position", LocalPosition);
            var Kind = OwnedSource switch
            {
                PllBeetleZoneSource => NativeActor.Beetle,
                PllMushroomZombieZoneSource => NativeActor.MushroomZombie,
                PllCondorZoneSource => NativeActor.Condor,
                PllTornadoZoneSource => NativeActor.Tornado,
                PllTumbleweedZoneSource => NativeActor.Tumbleweed,
                _ => throw new InvalidOperationException("The native actor source is unsupported.")
            };
            var SelectedKind = (NativeActor)EditorGUILayout.EnumPopup("Native Actor", Kind);
            if (SelectedKind != Kind)
            {
                OwnedSource = SelectedKind switch
                {
                    NativeActor.Beetle => new PllBeetleZoneSource(),
                    NativeActor.MushroomZombie => new PllMushroomZombieZoneSource(),
                    NativeActor.Condor => new PllCondorZoneSource(),
                    NativeActor.Tornado => new PllTornadoZoneSource(),
                    NativeActor.Tumbleweed => new PllTumbleweedZoneSource(),
                    _ => throw new InvalidOperationException("The native actor selection is unsupported.")
                };
                GUIUtility.ExitGUI();
            }
            Volume.Draw();
            switch (OwnedSource)
            {
                case PllBeetleZoneSource Beetles:
                    DrawGround(ref Beetles.MaximumInitialCount, ref Beetles.SpawnChance,
                        ref Beetles.MaximumSlope, ref Beetles.MinimumSpacing);
                    break;
                case PllMushroomZombieZoneSource Zombies:
                    DrawGround(ref Zombies.MaximumInitialCount, ref Zombies.SpawnChance,
                        ref Zombies.MaximumSlope, ref Zombies.MinimumSpacing);
                    Zombies.CullRandomly = EditorGUILayout.Toggle("Cull Randomly", Zombies.CullRandomly);
                    break;
                case PllCondorZoneSource Condor:
                    Condor.SpawnChance = EditorGUILayout.Slider("Spawn Chance", Condor.SpawnChance, 0f, 1f);
                    Condor.NestPosition = EditorGUILayout.Vector3Field("Zone-local Nest Position", Condor.NestPosition);
                    break;
                case PllTornadoZoneSource Tornado:
                    Tornado.MaximumAlive = EditorGUILayout.IntField("Maximum Alive", Tornado.MaximumAlive);
                    DrawPositions(ref Tornado.Points);
                    break;
                case PllTumbleweedZoneSource Tumbleweed:
                    Tumbleweed.MaximumAlive = EditorGUILayout.IntField("Maximum Alive", Tumbleweed.MaximumAlive);
                    Tumbleweed.SpawnOffset = EditorGUILayout.Vector3Field("Zone-local Spawn Offset", Tumbleweed.SpawnOffset);
                    DrawPositions(ref Tumbleweed.Positions);
                    break;
            }
            EditorGUILayout.HelpBox("Creates an empty source child; PEAK owns native generation and separate output. Ground placement uses the volume; native movement is not confined. Points, nest and spawn offsets use zone-local coordinates. Zero population/chance produces no actors; maximum population is a limit, not a promised count. Hazards retain sealed native timing. Use Apply Entity Appearance for artist geometry.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Parent))
                if (GUILayout.Button("Create Native Actor Zone"))
                {
                    try
                    {
                        if (OwnedSource is PllEntityZoneSource Entity)
                            CreateEntity(Parent, Id, LocalPosition, Volume, Entity);
                        else CreateHazard(Parent, Id, LocalPosition, Volume, (PllHazardZoneSource)OwnedSource);
                        Close();
                    }
                    catch (InvalidOperationException Failure)
                    {
                        EditorUtility.DisplayDialog("PLL Creator", Failure.Message, "Close");
                    }
                }
            EditorGUILayout.EndScrollView();
        }

        static void DrawGround(ref int Count, ref float Chance, ref float Slope, ref float Spacing)
        {
            Count = EditorGUILayout.IntField("Maximum Initial Count", Count);
            Chance = EditorGUILayout.Slider("Spawn Chance", Chance, 0f, 1f);
            Slope = EditorGUILayout.Slider("Maximum Slope Degrees", Slope, 0f, 90f);
            Spacing = EditorGUILayout.FloatField("Minimum World Spacing", Spacing);
        }

        static void DrawPositions(ref Vector3[] Positions)
        {
            for (var Index = 0; Index < Positions.Length; Index++)
            {
                Positions[Index] = EditorGUILayout.Vector3Field("Zone-local Point " + (Index + 1), Positions[Index]);
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(Index == 0))
                    if (GUILayout.Button("Move Up"))
                    {
                        (Positions[Index - 1], Positions[Index]) = (Positions[Index], Positions[Index - 1]);
                        GUIUtility.ExitGUI();
                    }
                if (GUILayout.Button("Remove Point"))
                {
                    var Remaining = new Vector3[Positions.Length - 1];
                    Array.Copy(Positions, 0, Remaining, 0, Index);
                    Array.Copy(Positions, Index + 1, Remaining, Index, Remaining.Length - Index);
                    Positions = Remaining;
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add Point"))
            {
                Array.Resize(ref Positions, Positions.Length + 1);
                GUIUtility.ExitGUI();
            }
        }

        internal static PllEntityZoneAuthoring CreateEntity(GameObject Parent, string Id,
            Vector3 LocalPosition, PllAuthoringVolume Volume, PllEntityZoneSource OwnedSource)
        {
            const string Action = "Create Entity Zone";
            PllSourceIdentityValidator.ValidateFinite(LocalPosition, "Actor zone parent-local position");
            Volume.RequireValid();
            if (PllEntityZoneValueContract.SourceFailure(OwnedSource) is { } Failure)
                throw new InvalidOperationException(Failure);
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Parent, Action);
            if (!Owner.IsBiomeRoot)
                throw new InvalidOperationException("Native actor zones require a linked biome's editable FloorRoot or CampfireRoot.");
            PllActorZoneAuthoringValidator.ValidateNewSourceParent(
                Owner.StageRoot, Owner.Floor, Owner.Campfire, Parent.transform, Id);
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Target = CreateSourceObject(Parent, LocalPosition, "Entity Zone", Action);
                var Zone = Undo.AddComponent<PllEntityZoneAuthoring>(Target);
                Configure(Zone, Id, Volume, OwnedSource);
                return Zone;
            });
        }

        internal static PllHazardZoneAuthoring CreateHazard(GameObject Parent, string Id,
            Vector3 LocalPosition, PllAuthoringVolume Volume, PllHazardZoneSource OwnedSource)
        {
            const string Action = "Create Hazard Zone";
            PllSourceIdentityValidator.ValidateFinite(LocalPosition, "Actor zone parent-local position");
            if (PllHazardZoneValueContract.SourceFailure(OwnedSource, Volume.Shape,
                    Volume.Center, Volume.Size, Volume.Radius) is { } Failure)
                throw new InvalidOperationException(Failure);
            var Owner = PllSourceIdentityValidator.ResolveEditableOwner(Parent, Action);
            if (!Owner.IsBiomeRoot)
                throw new InvalidOperationException("Native actor zones require a linked biome's editable FloorRoot or CampfireRoot.");
            PllActorZoneAuthoringValidator.ValidateNewSourceParent(
                Owner.StageRoot, Owner.Floor, Owner.Campfire, Parent.transform, Id);
            return PllAuthoringCommands.Create(Action, () =>
            {
                var Target = CreateSourceObject(Parent, LocalPosition, "Hazard Zone", Action);
                var Zone = Undo.AddComponent<PllHazardZoneAuthoring>(Target);
                Configure(Zone, Id, Volume, OwnedSource);
                return Zone;
            });
        }

        static GameObject CreateSourceObject(GameObject Parent, Vector3 LocalPosition, string Name, string Action)
        {
            var Target = new GameObject(Name);
            Undo.RegisterCreatedObjectUndo(Target, Action);
            Target.transform.SetParent(Parent.transform, false);
            Target.transform.localPosition = LocalPosition;
            return Target;
        }

        static void Configure(Component Zone, string Id, PllAuthoringVolume Volume, object OwnedSource)
        {
            using (var Serialized = new SerializedObject(Zone))
            {
                PllAuthoringCommands.Property(Serialized, "IdValue").stringValue = Id;
                var Property = PllAuthoringCommands.Property(Serialized, "VolumeValue");
                Property.FindPropertyRelative("shape").intValue = (int)Volume.Shape;
                Property.FindPropertyRelative("center").vector3Value = Volume.Center;
                Property.FindPropertyRelative("size").vector3Value = Volume.Size;
                Property.FindPropertyRelative("radius").floatValue = Volume.Radius;
                PllAuthoringCommands.Property(Serialized, "SourceValue").managedReferenceValue = OwnedSource;
                Serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
