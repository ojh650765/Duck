using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Writes <c>Assets/Scenes/Arena.unity</c>: the defence pitch, both flower gardens, the fence, the
    /// opponent's board, the judges' bench, lighting, post processing, the camera rig and the mower —
    /// all as REAL GAMEOBJECTS SAVED IN THE SCENE FILE.
    ///
    /// The saving is the whole point, and it is worth stating why because a runtime version passed every
    /// test except the one that mattered. Geometry generated in Awake exists only in play mode: nobody
    /// can select it, nobody can drag it, nobody can even look at it without pressing play. The only
    /// adjustment available was to edit a constant, rebuild, play, capture, look at a PNG — a
    /// minutes-long loop for "move that bed two metres", on an arena whose entire job is to feel good to
    /// drive around. Baking is not a finishing step that freezes decisions; it is the tool that makes
    /// every remaining decision cheap.
    ///
    /// This follows DuckSceneBuilder's own pattern rather than inventing one. That builder treats the
    /// scene as generated output — "edit a number, rebuild, look" — and deterministic generation and a
    /// hand-inspectable result are only in tension if the generator forgets to write a file. Re-running
    /// this overwrites the arena, so hand tweaks are lost on a rebuild in exactly the way they are for
    /// the main scene; that is the established bargain here, not an oversight.
    ///
    /// Reuses <see cref="DuckSceneBuilder.BuildLighting"/>, <c>BuildEnvironmentLighting</c>,
    /// <c>BuildPostProcessing</c> and <c>BuildJudgeBench</c> so the arena cannot drift from the venue's
    /// look, and instantiates the existing <c>Mower.prefab</c> rather than duplicating a machine.
    ///
    /// Greybox: the beds are coloured boxes. The authored `Flowerbed` in Foliage.fbx — 1.6 x 0.84 m with
    /// sixteen vertex-coloured flowers, currently unused anywhere in the project — is the drop-in for the
    /// art pass, and swapping it in is one GetCombined call in <see cref="BedMesh"/>.
    /// </summary>
    public static class DuckArenaBuilder
    {
        public const string ScenePath = "Assets/Scenes/Arena.unity";

        // ---- dimensions. Every one of these is reasoned about in DefenceArena's own comments; the
        // ---- numbers live here because this is what writes them into the world.

        /// <summary>
        /// Half a garden's frontage. Widened from 5 m to 8 m.
        ///
        /// Five metres made the goal narrower than the machine's own turning circle, so defending it was
        /// standing still rather than covering ground — and it made the garden read as a flowerbox at the
        /// end of a large field instead of as a garden worth a championship. Sixteen metres of frontage is
        /// still comfortably a GOAL against a 30 m pitch, but it is now wide enough that where you choose
        /// to sit inside it is a real decision and a goose can beat you by going around you.
        /// </summary>
        const float GardenHalf = 8f;
        /// <summary>
        /// Centre to centre between the goals. The number the whole feel rests on.
        ///
        /// 50 m leaves 40 m of clear pitch. A mower tops out at 10 m/s and turns at 165 deg/s slow but
        /// only 88 fast, so a committed turn needs real ground: 40 m is four to five seconds end to end,
        /// which is enough to read where the goose will be, commit to an approach, OVERSHOOT, and
        /// recover. Being unable to overshoot is what made an 18 m box feel like rails even though
        /// nothing was holding the player — there was no room to be wrong in.
        /// </summary>
        const float GoalGap = 50f;
        const float PitchHalfWidth = 15f;

        [MenuItem("Duck/2 · Build defence arena scene", priority = 2)]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            DuckSceneBuilder.BuildLighting();
            DuckSceneBuilder.BuildEnvironmentLighting();
            DuckSceneBuilder.BuildPostProcessing();

            var root = new GameObject("~ Arena").transform;

            // Centred on the venue's field and split along its long axis, so both gardens sit on mown
            // ground and the judges' bench keeps the position the venue already gives it.
            Vector3 toward = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toward).normalized;
            Vector3 playerCentre = -toward * (GoalGap * 0.5f);
            Vector3 opponentCentre = playerCentre + toward * GoalGap;

            // THE VENUE'S OWN LEVEL DESIGN, not a pitch built here.
            //
            // The player's verdict on the hand-built version was "레벨 디자인이 쥬시하지 않음", followed
            // by "레벨을 이전 아레나 레벨 디자인 가져오고 정원만 재배치해" — and that is the right call
            // rather than a shortcut. This project has spent weeks art-directing the venue: ground with
            // its own detail, a hedge ring, the authored stands, the awning, tents, the pond, foliage,
            // landmarks, the cardinal ring, the judges' backdrop, field props and a seated crowd. The
            // arena reproduced none of it and could not, because every one of those passes is tuned
            // against the venue's own coordinates.
            //
            // So the arena IS the venue now, with two gardens placed in it. Everything I built by hand —
            // a flat pitch, painted lane stripes, a three-tier stand, a hedge run, a treeline, five tents
            // and a hill ridge — is gone, and it should be: it was a worse copy of what already existed
            // twenty metres away in the same project.
            //
            // The gardens straddle the field's long axis. Field.Half is 32 m, so a 50 m goal separation
            // fits inside the mown area with room at both ends for the mower to overshoot and recover.
            DuckEnvironmentBuilder.Build();

            // GRASS, AND THE GROUND ITSELF.
            //
            // Removing my hand-built pitch took the arena's floor with it: the venue's ground pass only
            // colliders the outer apron ring, and the mown centre is built by the lawn instead — so the
            // middle of the field was a hole and the mower fell through it forever. "계속 떨어짐" was
            // exactly that, and it was my doing.
            //
            // GrassField is the right fix rather than a bare plane, because it supplies both halves of
            // the problem at once: the blade layer that stops the ground reading as a flat matte sheet
            // ("잔디 깎기는 안되는걸로 다 매트해서 이상해서"), and its own BoxCollider across the whole
            // field. No CutMask here on purpose — nothing is mowable in the arena, so the grass simply
            // stands uncut, which is what a finished garden's lawn should look like anyway.
            var lawn = new GameObject("Lawn").transform;
            var grass = lawn.gameObject.AddComponent<GrassField>();
            grass.groundMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_GrassGround.mat");
            grass.bladeMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_GrassBlades.mat");

            var playerBeds = new List<Transform>(32);
            var opponentBeds = new List<Transform>(32);
            PlantGarden(root, "PlayerGarden", playerCentre, toward, side, playerBeds, PlayerBedMat());
            PlantGarden(root, "OpponentGarden", opponentCentre, -toward, side, opponentBeds, OpponentBedMat());

            // Only the PLAYER's fence is collected. The opponent's is 50 m away and never looked at
            // closely, so tracking it would be work nobody can see.
            var playerFence = new List<Transform>(80);
            Fence(root, playerCentre, toward, side, playerFence);
            Fence(root, opponentCentre, toward, side, null);

            var board = OpponentBoard(root, opponentCentre, toward, side);
            var npc = Opponent(root, opponentCentre, toward);

            // Anchors, as real empties so every one of them can be dragged and kept.
            var playerAnchor = Anchor(root, "Anchor_PlayerGarden", playerCentre, toward);
            var oppAnchor = Anchor(root, "Anchor_OpponentGarden", opponentCentre, -toward);
            var spawn = Anchor(root, "Anchor_Spawn",
                               playerCentre - toward * (GardenHalf * 0.8f) + Vector3.up * 0.4f, toward);
            // Portrait: in front of the garden it defended, turned three-quarters so the duck and the
            // beds are both in the closing shot.
            var portrait = Anchor(root, "Anchor_Portrait",
                                  playerCentre + toward * (GardenHalf + 3.2f) + side * 1.6f,
                                  Quaternion.LookRotation(
                                      (playerCentre - (playerCentre + toward * (GardenHalf + 3.2f))).normalized
                                      + side * 0.4f, Vector3.up));
            // The bench, placed to be SEEN rather than to be out of the way.
            //
            // It sat at PitchHalfWidth + 3.5 m — fifteen metres off the touchline plus three — which is
            // tidy and put it outside the chase camera's frustum for the entire phase. The judges were in
            // the scene and never once on screen, which for a beat whose whole job is "the bench marks
            // your defence here, with the flattened beds still in shot" is the same as their not being
            // there.
            //
            // Now just beyond the player's own garden and one third of the way up the touchline: the
            // chase camera looks from behind the mower down the pitch, so this sits inside that cone
            // without ever being between the player and the goose.
            Vector3 benchPos = playerCentre + side * (GardenHalf + 4.5f) + toward * (GoalGap * 0.3f);
            var benchAnchor = Anchor(root, "Anchor_Bench", benchPos,
                                     Quaternion.LookRotation(
                                         ((playerCentre + toward * (GoalGap * 0.18f)) - benchPos).normalized,
                                         Vector3.up));

            // The bench itself, authored here rather than borrowed from the venue at runtime. Reusing the
            // venue's builder means the same bench, the same three rigged judges and the same animators,
            // so the arena cannot drift from the panel the player already knows.
            var judges = DuckSceneBuilder.BuildJudgeBench();
            if (judges != null)
                judges.transform.SetPositionAndRotation(benchAnchor.position, benchAnchor.rotation);

            var mower = InstantiateMower(spawn);

            var systems = new GameObject("~ Systems").transform;
            systems.gameObject.AddComponent<InputReader>();

            var arena = systems.gameObject.AddComponent<DefenceArena>();
            arena.playerBeds = playerBeds.ToArray();
            arena.opponentBeds = opponentBeds.ToArray();
            arena.playerGarden = playerAnchor;
            arena.opponentGarden = oppAnchor;
            arena.spawnAnchor = spawn;
            arena.portraitAnchor = portrait;
            arena.benchAnchor = benchAnchor;
            arena.opponentMarker = board;
            arena.opponentNpc = npc;
            arena.flattenedMaterial = FlattenedMat();
            arena.gardenHalfWidth = GardenHalf;
            arena.fencePieces = playerFence.ToArray();

            var phase = systems.gameObject.AddComponent<GooseDefence>();

            // What makes this a level rather than a set. Without a driver in the scene the arena could
            // only ever be entered by GameDirector dragging the player into it mid-round from Main —
            // which is precisely what read as an abduction — and it could not be opened and played on
            // its own, so nothing about how it feels could be checked without running a whole round.
            var boot = systems.gameObject.AddComponent<ArenaBootstrap>();
            boot.defence = phase;

            // Tier and rally readouts. The component builds its own canvas at runtime — see its note on
            // why screen-space UI is the one thing here that does not need baking.
            var hud = systems.gameObject.AddComponent<DefenceHud>();
            hud.defence = phase;

            // Sound. Without this the arena had no AudioDirector, so AudioDirector.Instance was null and
            // every impact layer, honk, thud and crowd cue the phase fires did nothing at all — silently.
            DuckSceneBuilder.BuildAudioDirector(mower.GetComponent<MowerController>(), judges);

            // The duck flinching when the machine is hit. On the instance rather than the shared prefab,
            // because the prefab is authored elsewhere and this is the scene that needed it first — the
            // main venue's duck still sits through a gnome, which is worth fixing at the prefab.
            if (mower.GetComponent<DuckRider>() == null) mower.gameObject.AddComponent<DuckRider>();

            var camera = BuildCameraRig(mower);

            EditorSceneManager.MarkSceneDirty(scene);
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            // Registered here as well as in the project-settings pass. The scene cannot be entered from
            // a round unless it is in the build settings, and leaving that to a separate menu item meant
            // the builder reported success while producing a scene the game could not actually load.
            DuckMenuBuilder.RegisterBuildScenes();

            Debug.Log($"[Duck] built {ScenePath}: {playerBeds.Count} player beds, " +
                      $"{opponentBeds.Count} opponent beds, goals {GoalGap} m apart, " +
                      $"pitch {PitchHalfWidth * 2f} m wide. Open it and drag anything.");
        }

        // ------------------------------------------------------------------ pitch

        static void BuildPitch(Transform root, Vector3 centre, Vector3 toward)
        {
            // Wider and longer than the play area on purpose. The seam where this plane ends used to be
            // visible past the fence as a hard edge against the venue's darker grass 270 m away, which
            // read as an unfinished join; running it well past the goals puts that edge out of frame from
            // a low chase camera, and it also means a mower that overshoots its own goal is still on
            // solid ground rather than falling through and being flung back to the playfield by
            // MowerController's fall-reset.
            var g = Box(root, "Pitch", GroundMat(),
                        new Vector3(PitchHalfWidth * 2f + 34f, 1f, GoalGap + GardenHalf * 2f + 46f),
                        centre + toward * (GoalGap * 0.5f) + Vector3.down * 0.5f,
                        Quaternion.identity, keepCollider: true, castShadows: false);
            g.gameObject.isStatic = true;
        }

        /// <summary>
        /// Plant one garden's beds.
        ///
        /// A SHALLOW ARC across the frontage, not a straight line and not a cluster, and every part of
        /// that is a gameplay choice rather than a look:
        ///
        ///   * spread across the frontage, so which part of the goal you stand in front of is a real
        ///     positional decision. Clustered beds make one spot correct and the rest of the pitch
        ///     decorative.
        ///   * even spacing at about two dozen beds, so the 18% damage cap lands at four or five and one
        ///     flattened bed is a legible fraction. Uneven density would make identical play cost
        ///     different amounts depending only on where the goose happened to aim.
        ///   * an arc rather than a line, so the beds nearest the pitch are the exposed ones and the goal
        ///     has a front and a back for the crash radius to bite differently at.
        ///   * two rows, inset from the fence, so no bed is ever ambiguously behind a picket from a low
        ///     chase camera — which is the sort of thing that is invisible until you look at a frame.
        /// </summary>
        static void PlantGarden(Transform root, string name, Vector3 centre, Vector3 facing,
                                Vector3 side, List<Transform> into, Material mat)
        {
            var parent = new GameObject(name).transform;
            parent.SetParent(root, false);
            parent.position = centre;

            // Six, not twelve, and the authored bed's own width is what decides it.
            //
            // Twelve across a 8.6 m span is 0.78 m apart, and the authored flowerbed is 1.6 m wide — so
            // every bed overlapped its neighbours by half and the garden came out as one continuous strip
            // of flowers. That is not a cosmetic complaint: the phase is scored in BEDS LOST, and a
            // player who cannot see where one bed ends and the next begins cannot read "four gone" off
            // the ground. Six leaves 1.7 m centres, which clears the mesh with a gap to spare.
            const int perRow = 9;

            // A gravel apron under the whole planting, and edging around it.
            //
            // "각 정원 블록이 연결될만한 느낌을 주는 그런거 없냐" — and the answer a real garden gives is
            // that beds are never loose objects on a lawn: they sit in a bed of gravel or bark, inside a
            // kerb, with paths between them. Without that the eye reads N separate trays; with it, it
            // reads ONE garden containing N plantings, which is also what makes losing four of them feel
            // like damage to a thing rather than the removal of four things.
            //
            // Laid before the beds so the beds sit on top of it, and inset from the fence so the kerb is
            // visibly inside the enclosure rather than doubling it.
            GardenFloor(parent, centre, facing, side);
            const float bow = 1.9f;          // how far the arc's middle bulges toward the pitch
            float[] rowDepth = { 1.5f, 3.4f };

            for (int row = 0; row < rowDepth.Length; row++)
            {
                // The back row is offset half a step, so the two rows interlock rather than forming a
                // grid — a grid reads as a car park from above and leaves clean lanes through the goal.
                float stagger = row == 0 ? 0f : 0.5f;

                for (int i = 0; i < perRow; i++)
                {
                    float t = (i + stagger) / (perRow - 1f);
                    if (t > 1f) continue;

                    float across = Mathf.Lerp(-GardenHalf * 0.86f, GardenHalf * 0.86f, t);
                    // Arc: deepest at the edges, closest to the pitch in the middle.
                    float curve = (1f - Mathf.Abs(t * 2f - 1f)) * bow;
                    Vector3 p = centre + side * across + facing * (rowDepth[row] - curve);

                    // Turned and jittered per bed. The authored flowerbed has a rectangular tray, and
                    // laid on a strict grid at a shared angle forty of them read as "네모 블록" — a car
                    // park of boxes rather than a garden. Rotation is what breaks that: a bed at nine
                    // degrees off its neighbour stops the eye finding the rows, and it costs nothing
                    // because the bed root carries no non-uniform scale for a rotation to shear.
                    var jitterRng = new System.Random((i + row * 31) * 7717 + 5);
                    float J(float a, float b) => a + (float)jitterRng.NextDouble() * (b - a);
                    Vector3 jittered = p + side * J(-0.35f, 0.35f) + facing * J(-0.5f, 0.5f);
                    Quaternion turned = Quaternion.LookRotation(facing, Vector3.up)
                                      * Quaternion.Euler(0f, J(-26f, 26f), 0f);
                    into.Add(PlantBed(parent, jittered, turned * Vector3.forward, mat, i + row * perRow));
                }
            }
        }

        /// <summary>
        /// The ground the planting sits in: a gravel apron, a kerb around it, and paths between the rows.
        ///
        /// This is what turns a scatter of trays into a garden. The apron gives every bed a common
        /// surface to belong to, the kerb draws a boundary the eye can close, and the two cross paths
        /// break the apron into quarters so it reads as laid out rather than as a slab.
        /// </summary>
        static void GardenFloor(Transform parent, Vector3 centre, Vector3 facing, Vector3 side)
        {
            var gravel = DuckSceneBuilder.EnsureLit("M_ArenaGravel", "#9C9385");
            var kerb = DuckSceneBuilder.EnsureLit("M_ArenaKerb", "#B8AE9C");
            var path = DuckSceneBuilder.EnsureLit("M_ArenaPath", "#8A8073");

            float halfW = GardenHalf * 0.94f;
            float halfD = 2.9f;
            Vector3 mid = centre + facing * 2.45f;
            var rot = Quaternion.LookRotation(facing, Vector3.up);

            // The apron. Barely above the grass so blades still break its edge.
            Box(parent, "GardenFloor", gravel, new Vector3(halfW * 2f, 0.04f, halfD * 2f),
                mid + Vector3.up * 0.02f, rot, keepCollider: false, castShadows: false);

            // Kerb: four low rails framing it. Two long, two short, so the corners meet.
            foreach (int s in new[] { -1, 1 })
            {
                Box(parent, "GardenKerb", kerb, new Vector3(halfW * 2f + 0.3f, 0.13f, 0.15f),
                    mid + facing * (halfD * s) + Vector3.up * 0.065f, rot,
                    keepCollider: false, castShadows: true);
                Box(parent, "GardenKerb", kerb, new Vector3(0.15f, 0.13f, halfD * 2f),
                    mid + side * (halfW * s) + Vector3.up * 0.065f, rot,
                    keepCollider: false, castShadows: true);
            }

            // Two paths across it, so the apron is quartered rather than blank.
            Box(parent, "GardenPath", path, new Vector3(halfW * 2f, 0.05f, 0.55f),
                mid + Vector3.up * 0.03f, rot, keepCollider: false, castShadows: false);
            Box(parent, "GardenPath", path, new Vector3(0.55f, 0.05f, halfD * 2f),
                mid + Vector3.up * 0.03f, rot, keepCollider: false, castShadows: false);
        }

        /// <summary>
        /// One flowerbed: a soil trough with blooms standing in it.
        ///
        /// It was a single coloured box, and forty-six of them read as one red mass rather than as a
        /// garden — which mattered more than any effect layered on top, because the whole phase is about
        /// defending *this*, and a block is not something a player minds losing.
        ///
        /// The SOIL is the bed root, and that is load-bearing rather than tidy. DefenceArena flattens a
        /// bed by squashing `body.localScale` and swapping the renderer on that same transform, so
        /// putting the soil at the root gets both for free: the parent squash crushes the blooms along
        /// with the trough, and the material swap turns the trough to bare earth without touching them.
        /// Had the root been an empty holder, the swap would have found no renderer and a destroyed bed
        /// would have stayed in full colour.
        ///
        /// Blooms are boxes, not spheres. At the size these are actually seen at — a low camera thirty
        /// metres out — a sphere costs eight times the triangles to render the same four pixels, and
        /// there are 184 of them.
        /// </summary>
        static Transform PlantBed(Transform parent, Vector3 p, Vector3 facing, Material sideMat, int index)
        {
            var rot = Quaternion.LookRotation(facing, Vector3.up);

            // The authored flowerbed IS the bed, when it is on disk.
            //
            // Foliage.fbx has carried a `Flowerbed` — 1.6 x 0.84 m, sixteen vertex-coloured blooms and
            // its own soil trough — for this whole project and nothing had ever placed it.
            //
            // Making it the ROOT rather than a child of a box trough fixes three things at once that the
            // child version got wrong: the authored mesh brings its own soil, so a box underneath it only
            // ever z-fought and cast a black band; a child inherits the trough's non-uniform scale and
            // has to undo it, which is fragile arithmetic for no gain; and DefenceArena flattens a bed by
            // squashing this transform and swapping ITS renderer, so as the root the whole planting
            // crushes and turns to bare earth in one move. One renderer per bed, and the good-looking
            // option is also the cheap one.
            var authored = BedMesh();
            if (authored != null)
            {
                var go = new GameObject("Bed");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(p, rot);
                go.AddComponent<MeshFilter>().sharedMesh = authored;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = BloomVertexMat();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;
                return go.transform;
            }

            // Soil is SOIL, not the garden's colour. Passing the old bed material through here was the
            // whole original problem wearing a new shape: forty-six pink troughs read as one pink mass
            // exactly as forty-six pink blocks did. Whose garden it is comes from the blooms.
            var bed = Box(parent, "Bed", SoilMat(),
                          new Vector3(0.98f, 0.22f, 0.66f), p + Vector3.up * 0.11f, rot,
                          keepCollider: false, castShadows: true);

            // Deterministic per bed rather than random, so a rebuild does not reshuffle the garden the
            // level was dressed against.
            var rng = new System.Random(index * 7919 + 13);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            var palette = BloomMats(sideMat != null && sideMat.name.Contains("Rival"));

            for (int f = 0; f < 4; f++)
            {
                var mat = palette[(index + f) % palette.Length];
                float h = R(0.20f, 0.30f);

                // Local space: the bed is already placed and turned, so the blooms only need to be
                // spread across its own footprint. Deriving them from world coordinates is exactly the
                // mistake that put two rival gnomes on the player's lawn.
                var stem = Box(bed, "Stem", StemMat(),
                               new Vector3(0.035f, h, 0.035f),
                               Vector3.zero, Quaternion.identity,
                               keepCollider: false, castShadows: false);
                stem.localPosition = new Vector3(R(-0.34f, 0.34f), 0.5f + h * 0.5f / 0.22f, R(-0.2f, 0.2f));
                stem.localScale = new Vector3(0.035f / 0.98f, h / 0.22f, 0.035f / 0.66f);

                var head = Box(bed, "Bloom", mat,
                               new Vector3(0.15f, 0.12f, 0.15f),
                               Vector3.zero, Quaternion.identity,
                               keepCollider: false, castShadows: false);
                head.localPosition = new Vector3(stem.localPosition.x, 0.5f + (h + 0.06f) / 0.22f,
                                                 stem.localPosition.z);
                head.localScale = new Vector3(0.15f / 0.98f, 0.12f / 0.22f, 0.15f / 0.66f);
                // Deliberately NOT turned. The soil trough is scaled non-uniformly (0.98 / 0.22 / 0.66),
                // and a rotation inside a non-uniform parent shears its child — the blooms would come out
                // as leaning rhomboids. Variety comes from position and colour instead, which cost
                // nothing and cannot skew.
                head.localRotation = Quaternion.identity;
            }

            return bed;
        }

        /// <summary>
        /// Warm blooms for the player, cool for the rival. Hue is what tells the two gardens apart at a
        /// glance from a chase camera — the soil is identical, because soil is.
        /// </summary>
        static Material[] BloomMats(bool rival) => rival
            ? new[]
            {
                DuckSceneBuilder.EnsureLit("M_ArenaBloomRivalA", "#C0509E"),
                DuckSceneBuilder.EnsureLit("M_ArenaBloomRivalB", "#8E6BC8"),
                DuckSceneBuilder.EnsureLit("M_ArenaBloomRivalC", "#EFE4F2")
            }
            : new[]
            {
                DuckSceneBuilder.EnsureLit("M_ArenaBloomA", "#E4574C"),
                DuckSceneBuilder.EnsureLit("M_ArenaBloomB", "#F2B237"),
                DuckSceneBuilder.EnsureLit("M_ArenaBloomC", "#FBF3E2")
            };

        static Material StemMat() => DuckSceneBuilder.EnsureLit("M_ArenaStem", "#3F7A32");

        /// <summary>Turned earth. Identical on both sides, because soil is.</summary>
        static Material SoilMat() => DuckSceneBuilder.EnsureLit("M_ArenaSoil", "#4A3527");

        /// <summary>
        /// The authored flowerbed from Foliage.fbx, baked once and shared by every bed. Null when the
        /// export is missing, which is the signal to fall back to boxes rather than an error — the arena
        /// has to build on a fresh clone that has not run Blender yet.
        /// </summary>
        static Mesh BedMesh() =>
            DuckAssetLibrary.GetCombined("Foliage.fbx", "Flowerbed", "ArenaFlowerbed");

        /// <summary>
        /// White base with vertex colour fully on, because the sixteen blooms' colours live in the MESH
        /// rather than in a material. Tinting the base here would multiply every authored bloom by that
        /// tint and collapse the variety the asset exists to provide.
        /// </summary>
        static Material BloomVertexMat()
        {
            var m = DuckSceneBuilder.EnsureLit("M_ArenaBloomAuthored", "#FFFFFF");
            if (m != null && m.HasProperty("_VertexColorAmount")) m.SetFloat("_VertexColorAmount", 1f);
            return m;
        }

        /// <summary>A low picket ring. Also what a missed goose comes down through.</summary>
        static void Fence(Transform root, Vector3 centre, Vector3 toward, Vector3 side,
                          List<Transform> collect)
        {
            var parent = new GameObject("Fence").transform;
            parent.SetParent(root, false);
            parent.position = centre;

            var mat = FenceMat();

            // Fifteen pickets and TWO RAILS, where it was eleven pickets and nothing.
            //
            // Eleven 9 cm pickets across ten metres is ninety-one percent air, and the result read as a
            // ring of standing stones rather than as a fence — which matters beyond looks, because a
            // missed goose is supposed to SMASH THROUGH this and there was visibly nothing to smash.
            //
            // The rails do most of the work for almost nothing. A picket fence is read from its
            // horizontal lines, not from the count of its uprights: two long thin boxes per side turn
            // twenty-two disconnected posts into one continuous barrier for eight extra renderers.
            const int perSide = 15;
            const float railY0 = 0.20f, railY1 = 0.48f;

            for (int s = 0; s < 4; s++)
            {
                Vector3 along = (s < 2) ? side : toward;
                Vector3 outward = (s < 2) ? toward : side;
                float sign = (s % 2 == 0) ? 1f : -1f;
                Vector3 lineCentre = centre + outward * (GardenHalf * sign);

                for (int i = 0; i < perSide; i++)
                {
                    float t = Mathf.Lerp(-GardenHalf, GardenHalf, i / (float)(perSide - 1));
                    var picket = Box(parent, "Picket", mat, new Vector3(0.09f, 0.62f, 0.09f),
                        centre + along * t + outward * (GardenHalf * sign) + Vector3.up * 0.31f,
                        Quaternion.identity, keepCollider: false, castShadows: true);
                    collect?.Add(picket);
                }

                // Turned to lie along the run. Uniform in the two axes being rotated about, so no shear.
                var rot = Quaternion.LookRotation(along, Vector3.up);
                foreach (float y in new[] { railY0, railY1 })
                    collect?.Add(Box(parent, "Rail", mat, new Vector3(0.045f, 0.075f, GardenHalf * 2f),
                        lineCentre + Vector3.up * y, rot, keepCollider: false, castShadows: true));
            }
        }

        /// <summary>
        /// Mown stripes down the pitch.
        ///
        /// The pitch was one flat sheet of colour, and on a flat sheet a mower moving at ten metres a
        /// second has nothing to move RELATIVE TO — speed stopped reading, and so did which way the
        /// player was pointing. Stripes are the cheapest fix that is also the right one for this game:
        /// a mown lawn genuinely has them, they run naturally toward the goal the player is defending
        /// or attacking, and eight long quads cost eight renderers.
        ///
        /// Laid just above the pitch rather than blended into it, because the pitch is one mesh and this
        /// has to be adjustable by dragging.
        /// </summary>
        static void Lanes(Transform root, Vector3 centre, Vector3 toward, Vector3 side)
        {
            var parent = new GameObject("Lanes").transform;
            parent.SetParent(root, false);
            parent.position = centre;

            var mat = LaneMat();
            const int stripes = 8;
            float width = PitchHalfWidth * 2f / stripes;
            var rot = Quaternion.LookRotation(toward, Vector3.up);

            for (int i = 0; i < stripes; i += 2)
            {
                float across = Mathf.Lerp(-PitchHalfWidth, PitchHalfWidth, (i + 0.5f) / stripes);
                Box(parent, "Lane", mat,
                    new Vector3(width, 0.02f, GoalGap + GardenHalf * 4f),
                    centre + side * across + toward * (GoalGap * 0.5f) + Vector3.up * 0.012f,
                    rot, keepCollider: false, castShadows: false);
            }
        }

        /// <summary>A shade off the pitch, not a stripe of paint. Mown grass, not a road marking.</summary>
        static Material LaneMat() => DuckSceneBuilder.EnsureLit("M_ArenaLane", "#5E8F3E");

        /// <summary>
        /// The set the match is played in: a treeline, a hedge run, tents and a ridge of hills.
        ///
        /// The player's verdict on the arena was "스테이지 촌스러움" — the stage looks cheap — and they
        /// were right for a reason the frames show plainly. The pitch was a flat plane with painted
        /// stripes meeting the sky in a hard line: no horizon, no enclosure, no depth layers, nothing at
        /// any distance between the fence and the skybox. The main venue has marquees, hedges, a treeline
        /// and a ridge; the arena had NONE of it, so the same game looked like a demo scene next door to
        /// itself.
        ///
        /// Four layers, nearest to furthest, because that is what reads as depth rather than as more
        /// objects: a hedge run just outside the touchline, a scattered treeline behind it, tents beyond
        /// the crowd, and a low wide ridge on the skyline. Distances are widely separated on purpose —
        /// props at similar depths read as one cluttered band.
        ///
        /// Authored meshes where they exist, and Foliage.fbx has carried Tree_Oak, Tree_Poplar and
        /// Hedge_Straight all along. Falls back silently when an export is missing, so the arena still
        /// builds on a clone that has not run Blender.
        /// </summary>
        static void Dress(Transform root, Vector3 centre, Vector3 toward, Vector3 side)
        {
            var parent = new GameObject("Dressing").transform;
            parent.SetParent(root, false);

            var propMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_PropsAuthored.mat");
            var rng = new System.Random(90210);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            Vector3 mid = centre + toward * (GoalGap * 0.5f);

            // ---- 1. hedge run, just outside both touchlines ----
            var hedge = DuckAssetLibrary.GetCombined("Foliage.fbx", "Hedge_Straight", "Hedge_Straight");
            if (hedge != null && propMat != null)
            {
                for (int lane = -1; lane <= 1; lane += 2)
                {
                    // Skipped where the crowd stand and the judges' bench stand, so the dressing never
                    // grows through the things the frame is actually about.
                    for (float t = -0.5f; t <= 0.52f; t += 0.075f)
                    {
                        Vector3 p = mid + toward * (t * (GoalGap + GardenHalf * 3f))
                                        + side * (lane * (PitchHalfWidth + 6.5f));
                        if (lane < 0 && t > -0.34f && t < 0.34f) continue;   // crowd stand side
                        if (lane > 0 && t > -0.16f && t < 0.16f) continue;   // judges' bench side
                        Place(parent, "Hedge", hedge, propMat, p,
                              Quaternion.LookRotation(toward, Vector3.up), R(0.95f, 1.15f));
                    }
                }
            }

            // ---- 2. treeline, scattered behind the hedges ----
            var oak = DuckAssetLibrary.GetCombined("Foliage.fbx", "Tree_Oak", "Tree_Oak");
            var poplar = DuckAssetLibrary.GetCombined("Foliage.fbx", "Tree_Poplar", "Tree_Poplar");
            if (propMat != null && (oak != null || poplar != null))
            {
                for (int i = 0; i < 34; i++)
                {
                    float along = R(-0.75f, 0.75f) * (GoalGap + GardenHalf * 4f);
                    float across = (rng.Next(2) == 0 ? -1f : 1f) * R(PitchHalfWidth + 11f, PitchHalfWidth + 34f);
                    var mesh = (poplar != null && rng.Next(3) == 0) ? poplar : (oak ?? poplar);
                    Place(parent, "Tree", mesh, propMat, mid + toward * along + side * across,
                          Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.85f, 1.35f));
                }
            }

            // ---- 3. tents, beyond the crowd ----
            var tentA = DuckAssetLibrary.GetCombined("Landmarks.fbx", "Tent_A", "Tent_A");
            var tentB = DuckAssetLibrary.GetCombined("Landmarks.fbx", "Tent_B", "Tent_B");
            if (propMat != null && (tentA != null || tentB != null))
            {
                for (int i = 0; i < 5; i++)
                {
                    var mesh = (i % 2 == 0 ? tentA : tentB) ?? tentA ?? tentB;
                    Vector3 p = mid - side * R(PitchHalfWidth + 9f, PitchHalfWidth + 15f)
                                    + toward * R(-24f, 24f);
                    Place(parent, "Tent", mesh, propMat, p,
                          Quaternion.Euler(0f, R(-25f, 25f), 0f), R(0.95f, 1.2f));
                }
            }

            // ---- 4. the ridge, on the skyline ----
            //
            // Low and wide and overlapping. Tall mounds at this distance read as domes rather than as
            // landscape — the venue's own hills comment records the same finding.
            var hillMat = DuckSceneBuilder.EnsureLit("M_ArenaHills", "#6E8A5C");
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f + R(-0.16f, 0.16f);
                float dist = R(240f, 360f);
                var mesh = DuckPrimitives.Hill(R(70f, 150f), R(9f, 20f), 4, 20, 300 + i);
                Vector3 p = mid + new Vector3(Mathf.Cos(a) * dist, R(-6f, -2f), Mathf.Sin(a) * dist);
                Place(parent, $"Hill_{i}", mesh, hillMat, p,
                      Quaternion.Euler(0f, R(0f, 360f), 0f), 1f);
            }
        }

        /// <summary>One dressing prop. Shadow-casting but never collidable — the mower stays drivable.</summary>
        static void Place(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos,
                          Quaternion rot, float scale)
        {
            if (mesh == null || mat == null) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.receiveShadows = true;
        }

        /// <summary>
        /// A stand down the far touchline, with spectators on it.
        ///
        /// The arena had no crowd at all, and that blocked more than dressing: the phase already calls
        /// SpectatorCrowd.Excite on every parry and every crash, so "immediate crowd response" and
        /// "escalate through crowd excitement" were both wired to an object that did not exist. The code
        /// was doing nothing and silently succeeding.
        ///
        /// Seats are TYPED here, unlike the venue's, and that is not the mistake the venue's comment warns
        /// about. That warning is about a grid authored against one model and used with another; here the
        /// same function builds the benches and places the sitters, so there is one source of truth for
        /// where a bench is. Ray-sweeping geometry this simple would be ceremony.
        ///
        /// Opposite the judges deliberately: the bench is on the near-right, so putting the crowd on the
        /// far-left fills the other side of the frame instead of stacking both on one edge.
        /// </summary>
        static void Crowd(Transform root, Vector3 centre, Vector3 toward, Vector3 side)
        {
            var parent = new GameObject("Crowd").transform;
            parent.SetParent(root, false);

            var deck = DuckSceneBuilder.EnsureLit("M_ArenaStandDeck", "#C4B393");
            var skirt = DuckSceneBuilder.EnsureLit("M_ArenaStandSkirt", "#7B5F3E");

            const int tiers = 3;
            const float run = 34f;            // along the touchline
            const float tierDepth = 1.15f;
            const float tierRise = 0.62f;
            float outward = PitchHalfWidth + 2.2f;

            var seats = new List<SpectatorCrowd.Seat>(96);
            var rng = new System.Random(4211);

            for (int t = 0; t < tiers; t++)
            {
                float y = 0.31f + t * tierRise;
                float across = outward + t * tierDepth;
                Vector3 mid = centre - side * across + toward * (GoalGap * 0.5f);

                // The bench itself, and a skirt under it so the tier is not a slab floating on air.
                Box(parent, "StandDeck", deck, new Vector3(tierDepth, 0.16f, run),
                    mid + Vector3.up * (y + tierRise * 0.5f),
                    Quaternion.LookRotation(toward, Vector3.up), keepCollider: false, castShadows: true);
                Box(parent, "StandSkirt", skirt, new Vector3(tierDepth * 0.92f, y + tierRise * 0.5f, run),
                    mid + Vector3.up * ((y + tierRise * 0.5f) * 0.5f),
                    Quaternion.LookRotation(toward, Vector3.up), keepCollider: false, castShadows: false);

                int perTier = 14;
                for (int i = 0; i < perTier; i++)
                {
                    float alongT = Mathf.Lerp(-run * 0.46f, run * 0.46f, i / (float)(perTier - 1));
                    // Skip a few at random so the rows are not a picket line of animals.
                    if (rng.NextDouble() < 0.18) continue;

                    Vector3 p = mid + toward * alongT + Vector3.up * (y + tierRise * 0.5f + 0.08f);
                    seats.Add(new SpectatorCrowd.Seat
                    {
                        position = p,
                        // Facing the pitch, with a little scatter so the rows are not machined.
                        yaw = Quaternion.LookRotation(side, Vector3.up).eulerAngles.y
                              + (float)(rng.NextDouble() * 16.0 - 8.0),
                        scale = 0.9f + (float)rng.NextDouble() * 0.25f,
                        species = i + t,
                        phase = (float)rng.NextDouble() * 6.28f
                    });
                }
            }

            var crowd = parent.gameObject.AddComponent<SpectatorCrowd>();

            var species = new List<Mesh>();
            foreach (var n in new[] { "Rabbit_Root", "Sheep_Root", "Pig_Root", "Fox_Root", "Tortoise_Root" })
            {
                var m = DuckAssetLibrary.GetCombined("Spectators.fbx", n, n.Replace("_Root", ""));
                if (m != null) species.Add(m);
            }
            foreach (var n in new[] { "Goose_Root", "Hedgehog_Root", "Squirrel_Root" })
            {
                var m = DuckAssetLibrary.GetCombined("CrowdExtra.fbx", n, n.Replace("_Root", ""));
                if (m != null) species.Add(m);
            }

            if (species.Count > 0)
            {
                crowd.speciesMeshes = species.ToArray();
                crowd.crowdMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Spectators.mat");
                for (int i = 0; i < seats.Count; i++)
                {
                    var s = seats[i];
                    s.species = s.species % species.Count;
                    seats[i] = s;
                }
            }
            else
            {
                // No authored spectators on disk: the component falls back to generated blobs, which is
                // still a crowd that reacts. Better than an empty stand.
                crowd.crowdMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Crowd.mat");
            }

            crowd.seats = seats.ToArray();
        }

        /// <summary>
        /// The tall board behind the opponent's goal, in their livery.
        ///
        /// It exists because a garden is FLAT. The far goal is 50 m up the pitch and its ground sits a
        /// few degrees below the horizon from a chase camera, so a 10 m square of it spans about a degree
        /// and a half — thirty pixels of a nine-hundred-pixel frame, hard against the top edge. It was
        /// technically in shot and might as well not have been, which is how "send it back over there"
        /// ended up with no visible target twice running. Three and a half metres of vertical board
        /// subtends nearly six degrees at that range: give the far end HEIGHT and the frame can find it.
        /// </summary>
        static Transform OpponentBoard(Transform root, Vector3 centre, Vector3 toward, Vector3 side)
        {
            var parent = new GameObject("OpponentBoard").transform;
            parent.SetParent(root, false);

            var mat = LiveryMat();
            Vector3 at = centre + toward * (GardenHalf + 2.4f);

            var board = Box(parent, "Board", mat, new Vector3(6.5f, 3.4f, 0.25f),
                            at + Vector3.up * 3.1f, Quaternion.LookRotation(-toward, Vector3.up),
                            keepCollider: false, castShadows: true);

            Box(parent, "Post", mat, new Vector3(0.3f, 1.5f, 0.3f),
                at + side * 2.6f + Vector3.up * 0.75f, Quaternion.identity, false, true);
            Box(parent, "Post", mat, new Vector3(0.3f, 1.5f, 0.3f),
                at - side * 2.6f + Vector3.up * 0.75f, Quaternion.identity, false, true);

            return board;
        }

        static Transform Opponent(Transform root, Vector3 centre, Vector3 toward)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Opponent";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.localScale = new Vector3(0.8f, 0.6f, 0.8f);
            go.transform.SetPositionAndRotation(
                centre + toward * (GardenHalf * 0.55f) + Vector3.up * 1.2f,
                Quaternion.LookRotation(-toward, Vector3.up));
            go.GetComponent<MeshRenderer>().sharedMaterial = NpcMat();
            return go.transform;
        }

        // ------------------------------------------------------------------ systems

        static GameObject InstantiateMower(Transform spawn)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mower.prefab");
            if (prefab == null)
            {
                Debug.LogWarning("[Duck] Mower.prefab is missing; the arena has no machine in it.");
                return null;
            }

            // The existing prefab rather than a second machine built here. A duplicate would drift from
            // the one the player has spent the round driving, and the whole premise of this phase is that
            // it is the same mower on the same controls.
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = "Mower";
            go.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

            var controller = go.GetComponent<MowerController>();
            if (controller != null) DuckVFXBuilder.Build(go, controller);
            return go;
        }

        static CameraDirector BuildCameraRig(GameObject mower)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";

            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.12f;
            cam.farClipPlane = 420f;
            cam.allowHDR = false;
            if (go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            var director = go.AddComponent<CameraDirector>();
            if (mower != null)
            {
                director.target = mower.transform;
                director.mower = mower.GetComponent<MowerController>();
            }
            go.AddComponent<AudioListener>();
            return director;
        }

        // ------------------------------------------------------------------ materials, on disk

        // Every one of these is an ASSET, not a runtime material. A scene full of
        // HideFlags.DontSave materials looks wrong the moment somebody opens it without pressing play,
        // which would defeat the entire purpose of baking the arena in the first place.

        static Material GroundMat() => DuckSceneBuilder.EnsureLit("M_ArenaPitch", "#57703F");
        static Material PlayerBedMat() => DuckSceneBuilder.EnsureLit("M_ArenaBed", "#D15C70");
        static Material OpponentBedMat() => DuckSceneBuilder.EnsureLit("M_ArenaBedRival", "#C86A9E");
        static Material FlattenedMat() => DuckSceneBuilder.EnsureLit("M_ArenaBedFlat", "#4C3B2E");
        static Material FenceMat() => DuckSceneBuilder.EnsureLit("M_ArenaPicket", "#DCD8CE");
        static Material NpcMat() => DuckSceneBuilder.EnsureLit("M_ArenaOpponent", "#5C85C7");
        static Material LiveryMat() => DuckSceneBuilder.EnsureLit("M_ArenaBoard", "#F0B83D");

        // ------------------------------------------------------------------ helpers

        static Transform Box(Transform parent, string name, Material mat, Vector3 scale,
                             Vector3 position, Quaternion rotation, bool keepCollider, bool castShadows)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            // Colliders come off everything but the pitch. Beds and pickets with colliders would turn the
            // arena into an obstacle course the mower wedges in, and the phase needs the machine drivable
            // at every moment.
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());

            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;

            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = castShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
            }
            return go.transform;
        }

        static Transform Anchor(Transform parent, string name, Vector3 position, Vector3 forward)
            => Anchor(parent, name, position, Quaternion.LookRotation(forward, Vector3.up));

        static Transform Anchor(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            return go.transform;
        }
    }
}
