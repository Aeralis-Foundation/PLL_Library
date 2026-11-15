using System;
using PLL.Library;
using UnityEditor;
using UnityEngine;

namespace PLL.Creator.Editor
{
    internal sealed class PllCreateNativeActorZoneWindow : EditorWindow
    {
        enum NativeActor { Beetle, MushroomZombie, Condor, Tornado, Tumbleweed, VanillaActor, Eruption }

        internal static string PlacementHint(PllNativeActorProfile Profile) => Profile switch
        {
            PllNativeActorProfile.Scorpions => "Scorpions: rock faces and ledges up to 45\u00b0, aimed slightly downward into the wall; "
                + "spots buried inside other rock are skipped.",
            PllNativeActorProfile.Antlions => "Antlions: each pit checks about 34 m of ground all around itself (within 40\u00b0 of level, "
                + "10 m above to 15 m below the pit). Give it a wide, open sand flat; pits stay 25 m apart.",
            PllNativeActorProfile.Spiders => "Spiders: undersides of overhangs and ceilings (140-220\u00b0), with at least 20 m of clear "
                + "drop below, ground within 200 m, and rock 3-10 m above the hanging point. Point the zone's arrow up into the overhang. "
                + "Spiders stay 30 m apart.",
            PllNativeActorProfile.Beehives => "Beehives: walls, ledges and branch undersides, aimed 45\u00b0 downward; tilt toward the "
                + "surface's normal. PEAK keeps them on wood when surface tag rules are kept.",
            PllNativeActorProfile.JellyfishGround => "Jellyfish (ground): floors up to 40\u00b0, 5 m apart; stone only when surface "
                + "tag rules are kept.",
            PllNativeActorProfile.JellyfishWalls => "Jellyfish (walls): ledges and walls reached by 45\u00b0 rays, surfaces up to 40\u00b0, "
                + "5 m apart; stone only when surface tag rules are kept.",
            PllNativeActorProfile.VenusFlyTraps => "Venus fly traps: open ground, 10 m apart.",
            PllNativeActorProfile.GhostFires => "Ghost fires: level ground up to 30\u00b0 with a clear line above. PEAK keeps them 60 m "
                + "apart across the whole map, so set a minimum spacing to place more than one in a small zone.",
            PllNativeActorProfile.Cacti => "Cacti: open ground, 1.5 m apart.",
            PllNativeActorProfile.CactusBalls => "Cactus balls: open ground, 1.5 m apart.",
            PllNativeActorProfile.Dynamite => "Dynamite: rock faces and ledges up to 45\u00b0, aimed slightly downward; the stick appears "
                + "from the spawner when players are near.",
            PllNativeActorProfile.Mushrooms => "Shelf mushrooms: steep walls between 50\u00b0 and 130\u00b0, 25 m apart (a small zone "
                + "gets one unless the spacing is lowered).",
            PllNativeActorProfile.TickPlants => "Tick weeds: ground and ledges up to 40\u00b0, aimed 45\u00b0 downward; ticks jump on "
                + "players who brush through them.",
            PllNativeActorProfile.TickMonsteras => "Tick monsteras: ground and ledges up to 40\u00b0, aimed 45\u00b0 downward; ticks jump "
                + "on players who brush through them.",
            PllNativeActorProfile.HotSpring => "Hot spring: a capybara pool on a rock shelf that PEAK attaches to a cliff, 2 m out from "
                + "where a level ray meets the wall, with about 11 m of open air above the pool. Face the zone at a tall vertical wall; "
                + "one per zone looks like PEAK.",
            PllNativeActorProfile.Oasis => "Oasis: needs about 11 m of ground within 30\u00b0 of level all around it and open air above "
                + "the pool; oases stay 25 m apart.",
            PllNativeActorProfile.Frogs => "Lily-pad frogs: open ground or shallow water beds, 30 m apart.",
            PllNativeActorProfile.JungleVines => "Berry vines (jungle): hang from walls and ledge edges reached by 45\u00b0 rays; each "
                + "vine grows berries that players can pick.",
            PllNativeActorProfile.SwampVines => "Berry vines (swamp): hang from ledges and branch edges reached by straight-down rays; "
                + "each vine grows berries that players can pick.",
            PllNativeActorProfile.Cattails => "Cattails: shallow ground and water beds; each bunch can hold a glizzy item.",
            _ => "",
        };

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
            if (OwnedSource is not (PllBeetleZoneSource or PllMushroomZombieZoneSource or PllCondorZoneSource
                    or PllNativeActorZoneSource))
                throw new InvalidOperationException("Choose native Beetle, Mushroom Zombie, Condor or a vanilla actor profile.");
            OpenSource(OwnedSource);
        }

        internal static void Open(PllHazardZoneSource OwnedSource)
        {
            if (OwnedSource is not (PllTornadoZoneSource or PllTumbleweedZoneSource or PllEruptionZoneSource))
                throw new InvalidOperationException("Choose native Tornado, Tumbleweed or Caldera Eruption.");
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
                PllNativeActorZoneSource => NativeActor.VanillaActor,
                PllEruptionZoneSource => NativeActor.Eruption,
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
                    NativeActor.VanillaActor => new PllNativeActorZoneSource(),
                    NativeActor.Eruption => new PllEruptionZoneSource(),
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
                case PllNativeActorZoneSource Actors:
                    Actors.Profile = (PllNativeActorProfile)EditorGUILayout.EnumPopup("Vanilla Actor", Actors.Profile);
                    Actors.MaximumInitialCount = EditorGUILayout.IntField("Maximum Initial Count", Actors.MaximumInitialCount);
                    Actors.SpawnChance = EditorGUILayout.Slider("Spawn Chance", Actors.SpawnChance, 0f, 1f);
                    Actors.MinimumSpacing = EditorGUILayout.FloatField("Minimum World Spacing (0 = PEAK's)", Actors.MinimumSpacing);
                    Actors.KeepSurfaceTagRules = EditorGUILayout.Toggle("Keep PEAK's Surface Tag Rules", Actors.KeepSurfaceTagRules);
                    Actors.KeepPatchyDistribution = EditorGUILayout.Toggle("Keep PEAK's Patchy Spread", Actors.KeepPatchyDistribution);
                    EditorGUILayout.HelpBox("Spawns exactly like PEAK does in its own biome: the same prefabs, aim, scale, rotation and "
                        + "spacing rules, and PEAK's own networking. Rays travel along the zone's rotation the way PEAK aims that "
                        + "spawner (straight down for ground actors, angled forward into walls for wall actors), so rotate the zone "
                        + "to face the walls you want. Rules tied to PEAK's own map (heights, terrain materials, light volumes) are "
                        + "left out; tag rules are optional.", MessageType.None);
                    EditorGUILayout.HelpBox(PlacementHint(Actors.Profile), MessageType.Info);
                    break;
                case PllEruptionZoneSource Eruption:
                    Eruption.Frequency = EditorGUILayout.Slider("Frequency (1 = PEAK's)", Eruption.Frequency, 0f, 4f);
                    Eruption.ActivationPadding = EditorGUILayout.FloatField("Activation Padding", Eruption.ActivationPadding);
                    EditorGUILayout.HelpBox("PEAK's caldera lava geysers. Use a box: eruptions rise out of its bottom face at random "
                        + "spots, so set the bottom at your lava surface. They run on PEAK's own timer (first after about 10 s, then "
                        + "every few seconds, sometimes in bursts) while any player stands over the box's footprint or within the "
                        + "padding around it. Each geyser heats players within about 10 m and fades after 35 s.", MessageType.Info);
                    break;
                case PllTumbleweedZoneSource Tumbleweed:
                    Tumbleweed.MaximumAlive = EditorGUILayout.IntField("Maximum Alive", Tumbleweed.MaximumAlive);
                    Tumbleweed.SpawnOffset = EditorGUILayout.Vector3Field("Zone-local Spawn Offset", Tumbleweed.SpawnOffset);
                    DrawPositions(ref Tumbleweed.Positions);
                    break;
            }
            EditorGUILayout.HelpBox("The volume limits where actors spawn, not where they move. Points, nests and offsets use zone-local coordinates. "
                + "Zero population or chance prevents spawning; the maximum population is a cap, not a guaranteed count. "
                + "Hazards keep PEAK's timing. Use Apply Entity Appearance for custom models.", MessageType.Info);
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
