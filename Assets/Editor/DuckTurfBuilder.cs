using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Bakes BloomRush.unity: the territory arena, saved to disk.
    ///
    /// The generator writes a FILE, which is the load-bearing half of this and the project's
    /// established pattern rather than a new idea — DuckSceneBuilder, DuckArenaBuilder and
    /// DuckRallyBuilder all treat a scene as generated output that somebody then opens, drags and
    /// keeps. Geometry that exists only in play mode cannot be selected, cannot be nudged and cannot
    /// be judged without pressing play, which for an arena whose entire job is to be interesting to
    /// drive around is the wrong tool.
    ///
    /// Everything positional comes out of <see cref="TurfArena"/>, so the ground you can see, the
    /// hedges you crash into, the cells the score is counted from and the routes the opponents plan
    /// cannot disagree about where anything is. Change a number there, run this, look.
    ///
    /// It touches nothing outside its own scene. The mowing round, the goose rally and the menu are
    /// built by their own tools from their own numbers and are not read, written or re-registered
    /// here beyond adding one row to the build settings.
    /// </summary>
    public static class DuckTurfBuilder
    {
        public const string ScenePath = "Assets/Scenes/BloomRush.unity";
        const string MatDir = "Assets/Materials";

        /// <summary>
        /// The plaza radius the hub's furniture was modelled against.
        ///
        /// Not a layout number — <see cref="TurfArena.PlazaRadius"/> is the layout number, and it
        /// moved to 19 m. This is the size the fountain was drawn at, kept so the hub's props can
        /// be scaled by the ratio instead of by a figure somebody eyeballed. Re-export those props
        /// at the new size and this becomes 19 and every scale becomes 1.
        ///
        /// The CrownRing kerb was the other prop measured against this number, and it is gone — see
        /// <see cref="BuildPlazaEdgeLine"/> for what replaced it and why. Only the fountain reads
        /// this now.
        /// </summary>
        const float AuthoredPlazaRadius = 13f;

        /// <summary>
        /// How far the fountain reaches out from its own axis, at the size it was MODELLED.
        ///
        /// Read off <c>plaza_fountain()</c> in <c>Art/Blender/build_turf_props.py</c> rather than
        /// guessed: the lower basin is the widest thing in the piece at <c>rim_out_r = 2.45</c>, and
        /// its rings are drawn with <c>ring_z(..., e=2.2)</c>, a superellipse that bulges about 3%
        /// past a circle on the diagonals. 2.45 x 1.03 is 2.52, and 2.52 is the number a sight line
        /// has to clear rather than the one on the page.
        ///
        /// This is here because the fountain is not decoration to anything trying to see past it. It
        /// is the only obstacle inside the core, it stands on the world Y axis, and <see
        /// cref="FountainRadius"/> is the radius every shot aimed across the middle of this arena has
        /// to stay outside of. Re-model the prop and this moves; re-scale the plaza and
        /// <see cref="FountainRadius"/> moves on its own, which is the point of deriving it.
        /// </summary>
        const float AuthoredFountainRadius = 2.52f;

        /// <summary>
        /// The fountain's outer radius as it actually stands: 3.68 m, because BuildPlaza grows the
        /// prop by <c>PlazaRadius / AuthoredPlazaRadius</c> and nothing else in the core does.
        /// </summary>
        static float FountainRadius => AuthoredFountainRadius * (TurfArena.PlazaRadius / AuthoredPlazaRadius);

        /// <summary>
        /// Clear air between the fountain's rim and the judges' line, in metres.
        ///
        /// The bench's origin IS roughly the judges' line — <c>BuildJudgeBench</c> stands them at
        /// local z between -0.10 and +0.05 and puts the desk in FRONT of them — so this is the gap
        /// behind three animals' backs, not a gap to a piece of furniture. Just under a metre: enough
        /// that the basin is behind them rather than against them, small enough that the bench stays
        /// well inside the core wall and the lens stays as close to that wall as it can get.
        /// </summary>
        const float BenchFountainGap = 0.92f;

        static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        [MenuItem("Duck/4 · Build bloom rush scene", priority = 4)]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Make the materials before anything asks for them, because this builder cannot survive
            // their absence and does not say so when it happens.
            //
            // BuildMaterials is what creates M_WoodWarm, M_WoodDark and the rest, and every other
            // entry point calls it — Duck/2 is nothing else, Duck/3 opens with it, the menu builder
            // and the pipeline both do. This one did not, and simply assumed another menu item had
            // been run first. That assumption held right up until the day the folder was empty.
            //
            // What it cost: the bench and the scoreboard fetch their materials through
            // DuckMeshLibrary.Mat / DuckVenueBuilder.Mat, which are a bare LoadAssetAtPath and
            // return NULL SILENTLY. The crowd stands go through EnsureLit, which CREATES the asset
            // when it is missing. So a rebuild against an empty folder produced a scene in which the
            // stands were perfect and the judges' bench and the scoreboard were baked with
            // m_Materials: [{fileID: 0}] — magenta, with nothing in the console, because a null
            // material is not a missing shader and EnsureMaterial's error never got the chance to
            // fire. Sixteen renderers in BloomRush.unity, none in any other scene, all explained by
            // which of the two idioms fetched them.
            DuckSceneBuilder.BuildMaterials();

            DuckSceneBuilder.BuildLighting();
            DuckSceneBuilder.BuildEnvironmentLighting();
            DuckSceneBuilder.BuildPostProcessing();

            BuildNight();

            var root = new GameObject("~ Bloom Rush").transform;

            BuildGround(root);
            BuildBlades(root);
            BuildHedges(root);
            BuildPlaza(root);
            BuildGateways(root);
            BuildBarrier(root);
            BuildCrowd(root);
            BuildDressing(root);
            BuildNightLights(root);

            var competitors = new List<TurfCompetitor>(4);
            GameObject playerMower = null;

            for (int i = 0; i < TurfArena.Count; i++)
            {
                var slot = TurfArena.Get(i);
                var lane = new GameObject($"Gardener {i} — {slot.contestant}").transform;
                lane.SetParent(root, false);

                var mower = BuildMower(lane, slot);
                if (slot.isPlayer) playerMower = mower;

                var holder = new GameObject($"Competitor {i} — {slot.contestant}");
                holder.transform.SetParent(lane, false);
                holder.transform.position = slot.SpawnPosition;

                var comp = holder.AddComponent<TurfCompetitor>();
                comp.slot = i;
                comp.isPlayer = slot.isPlayer;
                comp.mower = mower != null ? mower.GetComponent<MowerController>() : null;

                // The collision relay goes on the MACHINE, because that is the only object Unity
                // will deliver a collision to — see TurfContact. Without it every shunt in the mode
                // is silently dropped, and the code that would have handled it looks correct.
                if (mower != null)
                {
                    var contact = mower.GetComponent<TurfContact>() ?? mower.AddComponent<TurfContact>();
                    contact.competitor = comp;
                }

                if (!slot.isPlayer)
                {
                    var brain = holder.AddComponent<TurfBrain>();
                    brain.competitor = comp;
                    brain.skill = slot.skill;
                    brain.plan = PlanFor(i);
                    comp.brain = brain;
                }

                competitors.Add(comp);
            }

            // ---- systems ----

            var systems = new GameObject("~ Systems").transform;

            // First, and it runs in edit mode. Without it the saved scene opens with every turf
            // constant at zero, the ground shader decides nothing in the world is playable, and the
            // whole arena renders as bare soil — a scene nobody can judge without pressing play.
            // See TurfPalette.
            systems.gameObject.AddComponent<TurfPaletteBinder>();

            var mask = systems.gameObject.AddComponent<TurfMask>();
            mask.stampShader = Shader.Find("Duck/TurfStamp");
            if (mask.stampShader == null)
                Debug.LogError("[Bloom] Duck/TurfStamp is missing. Nothing will be claimable.");

            var fx = systems.gameObject.AddComponent<TurfFX>();
            fx.glowShader = Shader.Find("Duck/TurfGlow");

            var director = systems.gameObject.AddComponent<TurfDirector>();
            director.competitors = competitors.ToArray();
            director.fx = fx;
            // The mowing round's own length, so a standalone review run is the same match the round
            // hands over. Entering from a round the bootstrap overwrites this with the live value —
            // this is the fallback for when there is no round to ask.
            director.matchSeconds = 75f;
            // The board readout, on while the mode is being tuned. Four seconds is slow enough to
            // read and fast enough to catch an opponent that has stopped taking ground.
            director.traceInterval = 4f;

            var camera = BuildCameraRig(playerMower);
            director.cameraDirector = camera;

            // ---- the bench ----
            //
            // The round's own three judges, in the arena, so the end of this stage is delivered by
            // the panel the player already knows rather than by a results screen. Every other
            // verdict in the game is three animals at a desk; a stage that ended on centred text
            // was speaking a language the rest of it does not.
            //
            // ON THE ISLAND, ON ITS +Z FACE — the NEAR side, the side everything that looks at this
            // bench already stands on.
            //
            // It stood nine metres beyond the barrier — outside the fence, behind the crowd, fifty
            // metres from anywhere the match was actually driven. That is a bench nobody sees during
            // play and which the ending had to fly sixty metres to reach and sixty metres back from,
            // and the two long blends were most of the reason the ending felt like three unrelated
            // screens. On the island it is inside the arena, visible over the core wall from every
            // lap, and the closing camera moves are short because the machines park in front of it.
            //
            // Then it stood on the island's FAR face, at (0, 0, -4.6), and the fountain ate the
            // ending. Three separate things aim themselves at this bench along world +Z and every
            // one of them was aiming through seven and a half metres of stone standing five high:
            //
            //   · the wide judges shot puts its lens at bench + (0.45, 1.74, +7.6), which from a
            //     bench at z = -4.6 is z = +3.0 — 3.03 m from the world Y axis, INSIDE a basin whose
            //     rim reaches 3.68. A screenshot from that exact mark is fountain and nothing else:
            //     column up the middle, basin across the bottom, upper bowl hanging into the top.
            //   · the per-judge push-in (judgeCloseDistance 3.3) landed at z = -1.3, further inside
            //     the same bowl.
            //   · TurfDirector.ParkForVerdict lines all four machines up on the core's +Z face
            //     facing -Z "at the desk" — with the whole fountain between them and it.
            //
            // Mirroring the bench to +Z answers all three at once, and it is the only move that
            // can. The proof is short. The shot is a 7.6 m segment from the bench to the lens; the
            // fountain blocks a disc of radius 3.68 about the origin; the bench has to stay on the
            // island. With both ends of that segment on the SAME side of the middle, the segment's
            // closest approach to the axis is the bench's own standoff — 4.60 m here, clear by
            // 0.92 — and with them on opposite sides the segment runs straight through the middle at
            // any bench position the core can hold. There is no z on this line that works from the
            // far face: to clear a 7.37 m obstacle the whole 7.6 m segment would have to sit beyond
            // z = +3.68 or beyond z = -11.28, and the second is nearly four metres outside the
            // island with the core wall across the sight line.
            //
            // MOVING THE FOUNTAIN was the other candidate and it is worse than merely ugly, it does
            // not fit: pushed far enough off the centre line to clear this shot (about 3.9 m of x)
            // its rim reaches 7.58 m, which is the inner face of its own core wall, and the 96 m
            // reveal then photographs a perfectly centred ring with the landmark shoved against one
            // side of it. SHRINKING it does not work either, and for a reason worth writing down:
            // the thing standing on the sight line at eye height is not the basin, it is the CENTRE
            // COLUMN, 0.44 m across and on the axis. Any fountain has one. The only fountain that
            // clears a 1.26-to-1.74 m sight line is one under 1.26 m tall, which is a fountain that
            // can no longer be seen over the hedge — the one job it was scaled up to do.
            //
            // The plaza stands this bench on a plinth and carrying that height across by hand is how
            // the rally ended up with three judges hovering a metre over the lawn — this arena is
            // flat, so they stand on it.
            //
            // THE PRICE, recorded rather than discovered later: with the bench on the near face the
            // lens sits at z = +12.2, which is 3.85 m outside the core wall, so the wall's 0.95 m top
            // edge crosses the judging shot about three quarters of the way down and the bottom
            // quarter of that frame is pale stone. Nothing that carries information is behind it —
            // the grazing line over the wall clears the bench at y = 0.42, under the desk top at
            // 0.83 and under every card that stands on it — and what it reads as is the island's own
            // parapet in the foreground of a shot taken from the plaza, which is exactly what it is.
            // If it ever reads as the camera being stuck behind a wall, the knob is BenchFountainGap:
            // smaller pulls the lens in toward the wall and shrinks the band.
            //
            // They watch the PLAYER's machine while the match runs — see BuildJudgeBench for why
            // that and not the leader.
            var bench = DuckSceneBuilder.BuildJudgeBench(
                playerMower != null ? playerMower.transform : null);
            if (bench != null)
            {
                // FACING +Z, and that is not an arbitrary choice.
                //
                // CameraDirector's judges shot places itself at judgesLookAt + (0.45, h, +7.6) in
                // WORLD space — it does not read the bench's rotation. In the venue that is fine
                // because the venue's bench faces +Z. Point this one along an arena bearing instead
                // and the camera lands behind three judges' heads, which is exactly what "the
                // judges are not visible" looked like. Facing the bench up +Z makes the round's own
                // shot correct here for free, rather than needing a fourth camera mode.
                //
                // The POSITION is measured off the fountain rather than off the wall, because the
                // fountain is what constrains it. Written as "wall minus 3.4" it was a number that
                // happened to be clear on the day somebody typed it and would silently stop being
                // clear the next time the hub is re-scaled — which is the failure this whole comment
                // is about, one sign earlier.
                float benchZ = FountainRadius + BenchFountainGap;
                bench.transform.SetPositionAndRotation(
                    new Vector3(0f, 0f, benchZ),
                    Quaternion.identity);
                bench.transform.SetParent(root, true);

                // Printed every build, because the two clearances this position is balanced between
                // are invisible in the Inspector and both of them are one constant away from being
                // violated by somebody who was changing something else. The bench's own back corners
                // are the far ones: it is 6 m wide, so at x = +-3.0 with the desk 0.83 m deep the
                // outermost point is sqrt(3^2 + (z + 0.83)^2) from the middle, and that has to stay
                // inside the core wall's inner face.
                float benchReach = Mathf.Sqrt(9f + (benchZ + 0.83f) * (benchZ + 0.83f));
                float wallInner = TurfArena.CoreRadius - 0.35f;
                Debug.Log($"[Bloom] bench at z {benchZ:0.00} — {BenchFountainGap:0.00} m clear of a " +
                          $"{FountainRadius:0.00} m fountain, corners at {benchReach:0.00} m inside a " +
                          $"{wallInner:0.00} m wall; judges lens lands at z {benchZ + 7.6f:0.00}.");
                if (benchReach > wallInner)
                    Debug.LogError($"[Bloom] the judges' bench is through the core wall by " +
                                   $"{benchReach - wallInner:0.00} m. Reduce BenchFountainGap or widen the core.");

                // The wire that was missing. Without it the shot aims at the venue's bench mark,
                // (0, 0, -39.5), which in this arena is a patch of turf outside the hedge.
                if (camera != null) camera.judgesLookAt = bench.transform;

                // Portraits, rendered from the real models at startup. The bench holds up the
                // WINNER'S FACE at the end of this stage rather than four percentages — see
                // TurfVerdict — so without these the beat is three judges holding up blanks.
                //
                // Built into this scene rather than borrowed from Main, and that is what makes the
                // arena openable on its own: in a championship Main is only asleep behind the
                // arena, so its portraits would in fact answer, but a BloomRush.unity opened
                // straight from the Project window has no Main behind it and would show nothing.
                // The rally solved this the same way in its own scene. The SUBJECT LIST is shared
                // with the rally rather than written again, so the goose on this bench is the same
                // goose on that one and on the venue's tour card.
                var portraits = systems.gameObject.AddComponent<ContestantPortraits>();
                portraits.subjects = DuckRallyBuilder.BuildPortraitSubjects();

                var v = systems.gameObject.AddComponent<TurfVerdict>();
                v.director = director;
                v.panel = bench;
                v.cameraDirector = camera;
                v.portraits = portraits;
                director.verdict = v;
            }
            else
            {
                Debug.LogWarning("[Bloom] no judges bench was built; the ending goes straight from " +
                                 "the overhead to the board.");
            }

            // The venue's own results board, at the far end of the arena.
            //
            // Built rather than invented, and it is the same object the championship posts to — so
            // the four rows the player reads at the end of this stage are laid out exactly like the
            // rows they read after the judges. A second board with its own layout would be a second
            // answer to the same question, and this stage is not entitled to one.
            //
            // Placed OPPOSITE the player's start, which is a staging decision rather than a tidy
            // one: the reveal lifts overhead, and the sweep down to the board is then ninety metres
            // of travel onto something the player has not been looking at. Stood behind them it
            // would already be on screen and the move would not be a move.
            var boardRoot = new GameObject("Scoreboard rig").transform;
            boardRoot.SetParent(root, false);
            var board = DuckVenueBuilder.BuildScoreboard(boardRoot);
            if (board != null)
            {
                Vector3 away = -TurfArena.Get(0).outward;
                board.SetPositionAndRotation(away * (TurfArena.BarrierRadius + 18f),
                                             Quaternion.LookRotation(-away, Vector3.up));
                director.scoreboard = board.GetComponent<Scoreboard>();
                camera.scoreboardAnchor = board;
            }
            else
            {
                Debug.LogWarning("[Bloom] no scoreboard was built; the reveal ends on the overhead.");
            }

            var hud = systems.gameObject.AddComponent<TurfHud>();
            hud.director = director;
            hud.view = camera != null ? camera.GetComponent<Camera>() : null;

            // The round's own card kit, handed over here because the sprites live under
            // Assets/Art/Textures/UI rather than in Resources, so a runtime-built HUD cannot reach
            // them on its own. Baking the references in is what makes stage three's result card
            // the same pieces of art as stage one's instead of a careful imitation of them.
            hud.cardPanel = DuckUIBuilder.Spr("panel_card_256");
            hud.cardPanelDark = DuckUIBuilder.Spr("panel_card_dark_256");
            hud.scorecard = DuckUIBuilder.Spr("scorecard_blank_256");
            hud.gaugeBg = DuckUIBuilder.Spr("progress_bar_bg_256");
            hud.gaugeFill = DuckUIBuilder.Spr("progress_bar_fill_256");

            var boot = systems.gameObject.AddComponent<TurfBootstrap>();
            boot.director = director;

            // Sound. Without this AudioDirector.Instance is null in a standalone run and every horn,
            // cheer and thud the match fires does nothing at all — silently.
            DuckSceneBuilder.BuildAudioDirector(
                playerMower != null ? playerMower.GetComponent<MowerController>() : null, null);

            EditorSceneManager.MarkSceneDirty(scene);
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            // The scene cannot be entered from a round unless it is in the build settings, and
            // leaving that to a separate menu item means this reports success while producing a
            // scene the game cannot load.
            EnsureInBuildSettings();

            Debug.Log($"[Bloom] built {ScenePath}: {TurfArena.GapCount} openings " +
                      $"({TurfArena.SpokeHalfWidth * 2f:0.0} m spokes, " +
                      $"{TurfArena.ChokeHalfWidth * 2f:0.0} m chokes" +
                      (TurfArena.RampHeight > 0.01f
                          ? $" climbing {TurfArena.RampHeight:0.00} m), "
                          : " on the flat), ") +
                      $"hub {TurfArena.PlazaRadius:0} m, arena {TurfArena.ArenaRadius:0} m. " +
                      "Open it and press play.");

            // The layout's own audit, printed every build. The point is the ring split: score is
            // counted per square metre with no weighting, so whichever ring has the most area is
            // where a rational player spends the match, whatever the design intends. The loop was
            // 70% of the board for a long time and no comment in either file said so.
            AuditRings();
        }

        /// <summary>
        /// Who plays how.
        ///
        /// Matched to the contestants the player already knows from the lawn rather than assigned
        /// round-robin. HORACE is a hare with the best pace and the worst precision in the field, so
        /// he raids; MARGOT has the most flair and the least accuracy, so she plants herself in the
        /// middle where everyone can see her; BRAMBLE is the slow, precise one, so he works the outer
        /// loop and quietly finishes what he starts. A player who has beaten these three on the
        /// lawn should recognise them here from how they drive.
        /// </summary>
        static TurfBrain.Plan PlanFor(int slot) => slot switch
        {
            1 => TurfBrain.Plan.Raider,     // HORACE
            2 => TurfBrain.Plan.Warlord,    // MARGOT
            _ => TurfBrain.Plan.Expander    // BRAMBLE
        };

        static void EnsureInBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in list)
                if (s.path == ScenePath) { s.enabled = true; EditorBuildSettings.scenes = list.ToArray(); return; }
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------ ground

        /// <summary>
        /// The floor: one radial mesh carrying the whole arena, including the four climbs.
        ///
        /// Radial rather than a grid because everything in this arena is at a bearing and a distance
        /// — the hedge ring, the openings, the loop and the hub are all defined in polar terms, and a
        /// square tessellation puts its edges at forty-five degrees to every feature on it, which
        /// shows as stair-stepping along the hedge line and as a visible diamond pattern in the
        /// claimed turf.
        ///
        /// One mesh, one material, one collider, one draw. The ownership is a channel inside this
        /// surface rather than a decal on top of it, which is the reason nothing in the mode can
        /// z-fight: there is no second surface to fight with.
        /// </summary>
        static void BuildGround(Transform root)
        {
            const int segments = 168;
            const float outer = TurfArena.BarrierRadius + 6f;
            const float ringStep = 0.72f;
            int rings = Mathf.CeilToInt(outer / ringStep);

            var verts = new List<Vector3>((rings + 1) * segments);
            var norms = new List<Vector3>((rings + 1) * segments);
            var tris = new List<int>(rings * segments * 6);

            for (int ring = 0; ring <= rings; ring++)
            {
                // The innermost ring is a pinprick rather than a point. Collapsing it to the origin
                // gives 168 zero-area triangles, and RecalculateNormals averages a zero-length
                // normal across them — which normalises to NaN and renders as a black hole in the
                // middle of the plaza. Five centimetres of radius costs nothing and is under the
                // fountain anyway.
                float r = Mathf.Max(ring * ringStep, 0.05f);
                for (int s = 0; s < segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
                    verts.Add(new Vector3(x, TurfArena.GroundHeight(x, z), z));
                    norms.Add(Vector3.up);
                }
            }

            for (int ring = 0; ring < rings; ring++)
            for (int s = 0; s < segments; s++)
            {
                int a = ring * segments + s;
                int b = ring * segments + (s + 1) % segments;
                int c = (ring + 1) * segments + s;
                int d = (ring + 1) * segments + (s + 1) % segments;
                tris.Add(a); tris.Add(c); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(b);
            }

            var mesh = new Mesh { name = "BloomGround", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            // Recalculated rather than left flat-up: the climbs are the only slopes in the arena and
            // a mower nosing over a crest lit as though the ground were level is the one moment the
            // ramps would stop reading as ramps.
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh = DuckMeshLibrary.Persist(mesh, "BloomGround");

            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = TurfMat();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            // The only collider the mower is allowed to find underfoot, and the surface its four
            // suspension raycasts land on going over a climb.
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
        }

        /// <summary>
        /// The planting: real blade geometry over the whole arena, coloured by who owns the ground.
        ///
        /// The mowing round's own <see cref="GrassField"/>, with its ground plane switched off. That
        /// reuse is the point. Ownership drawn only into the floor is a coloured texture however
        /// carefully it is shaded — it has no height, it does not move in the wind, and from the
        /// seat of a mower the mask's texel grid is visible as pixels. Ownership drawn as GRASS
        /// stands up, catches the light on one side, bends in the same gust as the lawn, and turns
        /// the boundary between two gardeners into a boundary between two kinds of growth.
        ///
        /// The chunk grid is square and the arena is round; blades that fall outside it, or under a
        /// hedge, are zeroed in the vertex shader by the same playability test the score uses. Which
        /// is cheaper than trying to bake a circular chunk grid and cannot disagree with the mask.
        /// </summary>
        static void BuildBlades(Transform root)
        {
            var shader = Shader.Find("Duck/TurfBlades");
            if (shader == null)
            {
                Debug.LogWarning("[Bloom] Duck/TurfBlades is missing; the arena has no planting, " +
                                 "only the painted floor.");
                return;
            }

            var mat = DuckSceneBuilder.EnsureMaterial("M_BloomBlades", "Duck/TurfBlades");
            if (mat != null)
            {
                mat.SetColor("_WildBase", DuckSceneBuilder.HexL("#274A1C"));
                mat.SetColor("_WildTip", DuckSceneBuilder.HexL("#4E8232"));
                mat.SetFloat("_GrowHeight", 1.75f);
                mat.SetFloat("_BloomTip", 0.62f);
                EditorUtility.SetDirty(mat);
            }

            var go = new GameObject("Planting");
            go.transform.SetParent(root, false);

            var field = go.AddComponent<GrassField>();
            field.buildGround = false;             // the arena owns its own floor, ramps and all
            field.bladeMaterial = mat;
            field.fieldSize = TurfMask.Size;       // exactly the mask, so no blade is unaddressable
            field.chunksPerSide = 12;
            // Thinner than the mowing lawn. This arena is ninety-six metres across against the
            // round's sixty-four, there are four machines on it instead of one, and the blades are
            // decoration on top of a floor that already carries the ownership — so the budget goes
            // to frame rate rather than to density nobody can see past the first ten metres.
            field.density0 = 62f;
            field.density1 = 18f;
            field.bladeHeight = 0.26f;
        }

        /// <summary>The material the whole arena floor wears. Its palette is the mode's palette.</summary>
        static Material TurfMat()
        {
            var m = DuckSceneBuilder.EnsureMaterial("M_BloomTurf", "Duck/TurfGround");
            if (m == null) return DuckSceneBuilder.EnsureLit("M_BloomTurfFallback", "#3F6B33");

            // Set every time rather than only on creation, so re-running the builder after a palette
            // change actually changes the palette instead of quietly keeping the first one.
            // Lifted well off the first pass. At #2E4A24 the unclaimed arena read as a dark green
            // hole that the first patch of livery appeared to be cut OUT of, rather than as a lawn
            // somebody was going to mow. Neutral ground has to look worth taking.
            m.SetColor("_NeutralBase", DuckSceneBuilder.HexL("#3B6130"));
            m.SetColor("_NeutralTip", DuckSceneBuilder.HexL("#64914A"));
            // Earth, not a hole. #2A2118 is within a few percent of black once the wrapped lighting
            // has been through it, and the gap mouths read as doorways into nothing.
            m.SetColor("_SoilColor", DuckSceneBuilder.HexL("#4A3A28"));
            m.SetFloat("_TurfDarken", 0.34f);
            m.SetFloat("_PatternDepth", 0.42f);
            // Fewer and smaller than the first pass. At 1.35 per metre and 26 cm across, the
            // flowers touched each other and a claimed patch read as pale gravel rather than as
            // planted turf with flowers in it.
            m.SetFloat("_BloomDensity", 0.85f);
            m.SetFloat("_BloomSize", 0.17f);
            m.SetFloat("_BorderEdge", 1.15f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ hedges

        /// <summary>
        /// The wall: one ring of clipped hedge, four metres thick, with eight openings cut in it.
        ///
        /// ONE row of authored sections and one line of colliders, and the simplicity is the point.
        /// The band used to be thirteen metres deep, which needed a generated mass for the body and
        /// two more rows of authored sections for its faces — three pieces of geometry stacked in
        /// the same place, each hiding the others, and a quarter of the arena given over to ground
        /// nobody could ever use. Thinning the wall to the depth of the authored arc deleted all of
        /// that: the prop IS the hedge now, at the size it was modelled at.
        ///
        /// The runs stop exactly where <see cref="TurfArena.GapOpening"/> says an opening starts, so
        /// the wall a mower hits and the ground the score refuses to count are the same shape to the
        /// centimetre. A hedge placed by eye against a separately authored validity test is how a
        /// territory mode ends up with a strip that looks paintable and is not.
        /// </summary>
        static void BuildHedges(Transform root)
        {
            var parent = new GameObject("Hedges").transform;
            parent.SetParent(root, false);

            var art = new GameObject("Planting").transform;
            art.SetParent(parent, false);

            var mesh = Prop("HedgeArc");
            var pillar = Prop("HedgePillar");
            var mat = PropMat();

            float inner = TurfArena.HedgeInner, outer = TurfArena.HedgeOuter;
            float mid = TurfArena.HedgeMid;
            float depth = outer - inner;
            const float sectionLength = 8f;          // the authored arc's own length
            int index = 0;

            // Each of the eight runs between two consecutive openings, laid out from the openings
            // themselves so a hedge end and a gap mouth are the same line by construction rather
            // than by two numbers somebody has to keep equal by hand.
            //
            // CONSECUTIVE means consecutive AROUND THE RING, which is not the same list as
            // consecutive by gap index — see TurfArena.GapInBearingOrder for what that cost.
            for (int b = 0; b < TurfArena.GapCount; b++)
            {
                var (from, to) = RunBetween(b, mid);
                float span = Mathf.DeltaAngle(from, to);
                if (span <= 0f) span += 360f;
                float arc = span * Mathf.Deg2Rad * mid;
                if (arc < 1.5f) continue;

                int count = Mathf.Max(1, Mathf.RoundToInt(arc / sectionLength));
                float step = span / count;

                for (int sIdx = 0; sIdx < count; sIdx++)
                {
                    float ang = from + step * (sIdx + 0.5f);
                    Vector3 dir = TurfArena.Bearing(ang);
                    Vector3 at = dir * mid;

                    var section = new GameObject($"Hedge {index++}").transform;
                    section.SetParent(art, false);
                    // LookRotation(dir) — local Z outward, which puts local X along the ring. That
                    // is the axis the arc is authored down, and the other way round is not a subtle
                    // error: the first build pointed local X radially and the ring came out as a
                    // pinwheel of blades fanning across the outer loop.
                    section.SetPositionAndRotation(at, Quaternion.LookRotation(dir, Vector3.up));

                    float chord = step * Mathf.Deg2Rad * mid;
                    if (mesh != null)
                    {
                        var go = Spawn(section, "Arc", mesh, mat, true);
                        // Lengthwise to close the run, and across to fill the wall's depth. The
                        // arc is authored 8 m long and 2.85 m deep, so both numbers are near one
                        // and nothing is visibly stretched.
                        go.transform.localScale = new Vector3(chord / sectionLength, 1f, depth / 2.85f);
                    }
                    else
                    {
                        Box(section, "Arc", HedgeMat(), new Vector3(chord * 1.02f, 2.4f, depth),
                            at + Vector3.up * 1.2f, section.rotation,
                            keepCollider: false, castShadows: true);
                    }
                    section.gameObject.isStatic = true;

                    // The collider is a box on the section, not the authored mesh: a glancing blow
                    // should slide along a hedge, and a concave mesh collider at arcade speed
                    // catches a wheel on every leaf the artist put in.
                    var col = section.gameObject.AddComponent<BoxCollider>();
                    col.size = new Vector3(chord * 1.06f, 2.6f, depth);
                    col.center = new Vector3(0f, 1.3f, 0f);
                }
            }

            // A pillar either side of each opening, so a gap reads as a gateway somebody built
            // rather than as a place the hedge happens to stop.
            var gates = new GameObject("Gate pillars").transform;
            gates.SetParent(parent, false);

            for (int g = 0; g < TurfArena.GapCount; g++)
            {
                float ang = TurfArena.GapAngle(g);
                float half = TurfArena.GapHalfWidth(g);

                foreach (float r in new[] { inner + 0.5f, outer - 0.5f })
                foreach (float side in new[] { -1f, 1f })
                {
                    // The half-angle is worked out at THIS radius, so the pillars sit on the mouth
                    // at both ends of a gap that is a constant width in metres rather than a
                    // constant angle.
                    float halfDeg = (half + 0.75f) / r * Mathf.Rad2Deg;
                    Vector3 dir = TurfArena.Bearing(ang + side * halfDeg);
                    Vector3 at = dir * r;
                    at.y = TurfArena.GroundHeight(at.x, at.z);

                    var rot = Quaternion.LookRotation(-dir, Vector3.up);
                    if (pillar != null)
                    {
                        var go = Spawn(gates, $"Pillar {g}", pillar, mat, true);
                        go.transform.SetPositionAndRotation(at, rot);
                        // Solid. These stand 0.75 m outside the opening's edge, so a collider on
                        // them only bites once the pillar is wider than 1.5 m — see the clearance
                        // check in AuditRings, which is there because "the gateposts are solid now"
                        // and "the gate is narrower than the number says" are the same change and
                        // only one of them is intended.
                        Solid(go, pillar);
                        go.isStatic = true;
                    }
                    else
                    {
                        Box(gates, $"Pillar {g}", PoleMat(), new Vector3(0.7f, 2.8f, 0.7f),
                            at + Vector3.up * 1.4f, rot,
                            keepCollider: true, castShadows: true).gameObject.isStatic = true;
                    }
                }
            }
        }

        /// <summary>
        /// The angular span of solid hedge between the <paramref name="b"/>th opening around the
        /// ring and the next one, at radius <paramref name="r"/>.
        ///
        /// Derived from the openings rather than authored separately, so a hedge end and a gap mouth
        /// are the same line by construction. Both halves are converted from metres to degrees AT
        /// THIS RADIUS: the gaps are a constant width in metres, so the angle they subtend is nearly
        /// twice as large at the inner face of the ring as at the outer one, and a run laid out from
        /// a single mid-radius angle overhangs one mouth and leaves a wedge at the other.
        ///
        /// The argument is a position around the ring, NOT a gap index, and that distinction is the
        /// only thing standing between this arena and the one that shipped with no doors in it. This
        /// used to take a gap index and step it by one, which steps ninety degrees and straight over
        /// the opening in between: every run was laid across a doorway, all eight doorways were
        /// filled with hedge and box colliders, and the eight runs between them covered 458 degrees
        /// of a 360 degree ring — which is what the doubled-up, scalloped, seamless wall in the
        /// survey overheads actually was. Nothing else in the mode disagreed loudly enough to
        /// notice, because everything else asks TurfArena where the openings are and TurfArena was
        /// right; only the geometry a mower can hit was wrong.
        /// </summary>
        static (float from, float to) RunBetween(int b, float r)
        {
            int g = TurfArena.GapInBearingOrder(b);
            int next = TurfArena.GapInBearingOrder(b + 1);
            float pad = 0.9f;
            float from = TurfArena.GapAngle(g)
                       + (TurfArena.GapHalfWidth(g) + pad) / r * Mathf.Rad2Deg;
            float to = TurfArena.GapAngle(next)
                     - (TurfArena.GapHalfWidth(next) + pad) / r * Mathf.Rad2Deg;
            return (from, to);
        }

        // ------------------------------------------------------------------ the hub

        /// <summary>
        /// The middle: an open plaza with a fountain in it and its edge painted on the floor.
        ///
        /// NOTHING HERE IS A KERB ANY MORE, and that is the change this method most needs explaining.
        /// The hub used to be ringed by the CrownRing prop at 19 m with 24 box colliders under it and
        /// eight approaches cut through them. All of it is gone, at the player's request, and the
        /// reasoning it carried is answered rather than deleted: see <see cref="BuildPlazaEdgeLine"/>,
        /// which keeps the line and drops the stone. The plaza edge is still drawn — it is just drawn
        /// as paint, so the boundary the crown is scored against is visible and is not something a
        /// machine can meet.
        ///
        /// The fountain is the arena's one landmark and it is doing real work rather than decorating.
        /// Everything else here is flat ground and hedge at chest height, so from a chase camera on
        /// the outer loop there is nothing to navigate by; the fountain is tall enough to be seen
        /// over the hedges from anywhere on the pitch, which is what lets a player who has been
        /// spun round re-orient without looking at the map. It has no collider — the hub is the most
        /// contested ground in the mode and a pillar in the middle of it to wedge on would be a
        /// punishment for going where the game is telling everyone to go.
        /// </summary>
        static void BuildPlaza(Transform root)
        {
            var parent = new GameObject("Plaza").transform;
            parent.SetParent(root, false);

            var mat = PropMat();

            // The edge, painted. Unconditionally — there is no prop to look for and therefore no
            // fallback branch, which is the point. The kerb had one: a ring of 44 low boxes built
            // whenever the FBX failed to load, so an arena the player had explicitly asked to have
            // no kerb in it would grow one back the first time somebody was mid-re-export. A
            // fallback that rebuilds the thing a decision removed is not a safety net, it is a
            // decision that reverts itself, and the honest shape for "the hub has no kerb" is a
            // builder with no code path that can produce one.
            BuildPlazaEdgeLine(parent);

            BuildCoreWall(parent);

            var fountain = Prop("PlazaFountain");
            if (fountain != null)
            {
                var go = Spawn(parent, "Fountain", fountain, mat, true);
                go.transform.position = Vector3.zero;
                // Grown with the hub it stands in. This is a navigation landmark before it is
                // decoration — it is the one thing tall enough to be seen over the hedge from
                // anywhere on the pitch, and it is what a driver who has been spun round re-orients
                // on. At its authored size in a 19 m plaza it was a white speck in a car park: too
                // small to steer by from the loop, and too small to make the largest, most valuable
                // open space on the map feel like it was about anything.
                float k = TurfArena.PlazaRadius / AuthoredPlazaRadius;
                go.transform.localScale = Vector3.one * k;
                go.isStatic = true;

                // Solid, at its full bounding radius rather than a tight fit. The fountain sits in
                // the middle of the most contested ground in the mode and is the one obstacle a
                // driver can be shoved into by somebody else, so it has to be a shape they can
                // predict from across the plaza: a fat round post, hit anywhere, always slides.
                // A tight collider on a flared stone basin is worse than a generous one, because
                // the half metre it saves is exactly the half metre a player thought they had.
                Solid(go, fountain);

                // No glowing water, and it was tried twice. A lit slab in the basin is the obvious
                // way to make the landmark read at night and it does not work here: the fountain is
                // one combined mesh, so the light has to be a separate box guessed into position
                // against a silhouette this builder cannot see, and both guesses put a plain blue
                // rectangle against the plinth that read as a placeholder somebody forgot to
                // delete. The fountain does not need it — it is pale stone standing five metres
                // above dark turf under a moon, which is already the brightest thing in the hub and
                // visible over the hedge from anywhere on the loop. If it ever does need lighting,
                // the honest fix is a separate emissive submesh on the prop, in Blender.
            }
            else
            {
                Box(parent, "Fountain", KerbMat(), new Vector3(3.2f, 0.6f, 3.2f),
                    Vector3.up * 0.3f, Quaternion.identity, keepCollider: false, castShadows: true);
                Box(parent, "Fountain column", KerbMat(), new Vector3(1.1f, 3.4f, 1.1f),
                    Vector3.up * 2.0f, Quaternion.identity, keepCollider: false, castShadows: true);
            }

            // Planters around the rim, off the racing line, so the plaza has furniture without
            // having obstacles.
            // Twelve rather than eight. Eight of these round an 11.7 m rim sat a chair's width
            // apart and read as a rhythm; round a 17.7 m one they are fourteen metres apart and read
            // as four things somebody forgot to finish placing.
            //
            // These now carry more weight than they were placed to carry. With the kerb gone they
            // are the only three-dimensional thing marking the hub's edge — the painted ring draws
            // it and these are what a driver sees standing at it from across the arena. That is an
            // argument for keeping twelve, not for making them bigger: they are still furniture
            // pushed to the rim, and the moment they become a boundary they become the kerb again.
            var planter = Prop("TrophyPlanter");
            const int planters = 12;
            for (int i = 0; i < planters; i++)
            {
                float a = 15f + i * (360f / planters);
                Vector3 at = TurfArena.Bearing(a) * (TurfArena.PlazaRadius - 1.3f);
                if (planter != null)
                {
                    var go = Spawn(parent, $"Planter {i}", planter, mat, true);
                    go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, a, 0f));
                    // Solid, and deliberately at the rim. These are the plaza's only furniture and
                    // they sit 1.3 m inside the painted edge, so the whole 17 m disc a fight for the
                    // crown actually happens on stays clear — they are something to be shoved into
                    // at the edge, not something to thread between in the middle. They are also the
                    // only colliders left anywhere near the plaza boundary now, which is the number
                    // this arena wanted: twelve posts you can see coming, rather than a ring.
                    Solid(go, planter);
                    go.isStatic = true;
                }
                else
                {
                    Box(parent, $"Planter {i}", KerbMat(), new Vector3(0.9f, 1.0f, 0.9f),
                        at + Vector3.up * 0.5f, Quaternion.Euler(0f, a, 0f),
                        keepCollider: true, castShadows: true).gameObject.isStatic = true;
                }
            }
        }

        /// <summary>
        /// The plaza edge, drawn as paint: a flat ring on the floor whose centreline is exactly
        /// <see cref="TurfArena.PlazaRadius"/>, with nothing on it a machine can meet.
        ///
        /// THIS IS WHAT IS LEFT OF THE KERB, and the reduction is the whole point. What used to be
        /// here was the CrownRing prop scaled onto the 19 m line with 24 box colliders under it and
        /// eight approaches cut through them, and it came out at the player's request. Two reasons
        /// it deserved to go, beyond being asked for. A 34 cm ring around the most contested ground
        /// in the mode is something four machines meet sideways at arcade speed, in the one place
        /// the game spends the entire match telling them to go — the hub was fenced against the
        /// behaviour it exists to reward. And the eight cut approaches made that worse rather than
        /// better: a boundary that stops you around 300 degrees of its circumference and waves you
        /// through the other 60 is a boundary nobody can learn, so it stopped reading as an edge and
        /// started reading as an intermittent bug.
        ///
        /// But the argument the kerb carried is TRUE, and it does not leave with the stone. The
        /// crown in this mode is won on PlazaShare — a rule scored against a circle at 19 m — and
        /// the old comment's complaint was exactly right: a rule with no place attached to it is a
        /// rule the player is asked to obey blind, and a boundary drawn at the wrong radius is worse
        /// still, because every player believes the drawing over the rule. So the LINE stays and
        /// only the OBSTACLE goes. Paint says the same thing to a driver's eye and nothing at all to
        /// their wheels, and it cannot be drawn in the wrong place here because its radius is read
        /// from the same constant the score is.
        ///
        /// FLAT, in the strongest available sense. This is a two-dimensional annulus: two rings of
        /// vertices and the triangles between them, no side walls, no collider, no thickness. There
        /// is no geometry above the floor for a wheel to climb, catch on, bounce off or wedge under,
        /// because there is no geometry above the floor at all beyond the 2 cm that keeps it clear of
        /// the ground plane. It is not a low kerb and it is not a shallow step; it is a decal made
        /// of triangles.
        ///
        /// Two centimetres rather than zero, and that number is doing a job. The arena floor is one
        /// mesh with the ownership carried as a channel INSIDE it, specifically so that the mode has
        /// no second surface to z-fight with (see <see cref="BuildGround"/>) — this ring is the one
        /// exception in the arena, so it is lifted clear rather than laid coplanar and made to argue
        /// with the depth buffer over 38 m. Two centimetres is well under the mower's ride height and
        /// an order of magnitude under the 26 cm planting, so it never reads as a plate on the lawn.
        ///
        /// UNLIT, which is the lesson the lanterns already paid for. This arena is at night under a
        /// 0.42 moon, and a lit chalk material here comes out a dark grey smear — a boundary marker
        /// that does not mark, which is the same failure as having no marker with extra draw calls.
        /// So it takes the unlit material <see cref="GlowMat"/> makes. But at roughly half the art
        /// bible's chalk value rather than at it: an unlit near-white hoop 38 m across would outshine
        /// every lantern in the arena and read as a HUD overlay rather than as a line painted on
        /// grass. That value is the knob on this method — too dark and the edge is undrawn again,
        /// too bright and the hub wears a halo. Change it, run the builder, look.
        ///
        /// CONTINUOUS, with no openings, and that is a deliberate difference from the kerb. The
        /// kerb's eight gaps existed only because the kerb was solid and the spokes had to arrive
        /// somewhere; paint has nothing to open. More importantly the circle PlazaShare is counted
        /// from has no gaps in it, so a marker with gaps would be drawing a rule the mode does not
        /// have.
        ///
        /// The band straddles the radius rather than sitting inside or outside it, so its centreline
        /// IS the scored line. And it is tessellated to 168 segments, which is the ground mesh's own
        /// segment count, so both of its circles land on angles the floor already has vertices at and
        /// the band cannot scallop against the surface underneath it.
        ///
        /// One honest caveat, recorded rather than hidden: the planting is 26 cm tall and grows over
        /// the whole playable arena, this ring included. Grass will break the line up from a low
        /// camera in a way a 34 cm kerb did not. The band is 60 cm wide partly for that — a wide
        /// pale band survives being interrupted where a thin one would dissolve — but whether it
        /// reads from the driver's seat is a screenshot question, not a code one.
        /// </summary>
        static void BuildPlazaEdgeLine(Transform parent)
        {
            // The ground's own tessellation. Sharing it is what keeps the two circles off each
            // other's facets.
            const int Segments = 168;
            const float HalfWidth = 0.30f;      // a 60 cm band, centred on the radius
            const float Lift = 0.02f;

            float rIn = TurfArena.PlazaRadius - HalfWidth;
            float rOut = TurfArena.PlazaRadius + HalfWidth;

            var verts = new Vector3[Segments * 2];
            var norms = new Vector3[Segments * 2];
            var tris = new int[Segments * 6];

            for (int s = 0; s < Segments; s++)
            {
                float a = s / (float)Segments * Mathf.PI * 2f;
                float sin = Mathf.Sin(a), cos = Mathf.Cos(a);
                verts[s * 2] = new Vector3(sin * rIn, Lift, cos * rIn);
                verts[s * 2 + 1] = new Vector3(sin * rOut, Lift, cos * rOut);
                norms[s * 2] = Vector3.up;
                norms[s * 2 + 1] = Vector3.up;
            }

            for (int s = 0; s < Segments; s++)
            {
                int next = (s + 1) % Segments;
                int a = s * 2, b = a + 1;           // inner, outer at this angle
                int c = next * 2, d = c + 1;        // inner, outer at the next one
                int t = s * 6;
                // The same winding BuildGround uses, taken from it rather than reasoned out again —
                // a ring wound the other way is invisible from above and perfectly fine from below,
                // which is the one viewpoint nobody will check.
                tris[t] = a; tris[t + 1] = b; tris[t + 2] = d;
                tris[t + 3] = a; tris[t + 4] = d; tris[t + 5] = c;
            }

            var mesh = new Mesh { name = "BloomPlazaEdge" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            // Persisted, like the floor. A mesh built in the builder and never written to disk
            // survives exactly until the scene is closed, and then the plaza edge is a MeshFilter
            // pointing at nothing in a scene whose whole premise is that it is saved output.
            mesh = DuckMeshLibrary.Persist(mesh, "BloomPlazaEdge");

            var go = new GameObject("Plaza edge line");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GlowMat("M_BloomPlazaEdge", "#8A887B");
            // No collider is added here and none should ever be. The entire reason this exists in
            // place of the kerb is that it is not something a mower can hit.
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.isStatic = true;

            Debug.Log($"[Bloom] plaza edge: painted ring at {TurfArena.PlazaRadius:0.#} m, " +
                      $"{HalfWidth * 2f:0.##} m wide, flat and uncollidable. No kerb.");
        }

        /// <summary>
        /// An arch over each climbing choke, and a marker post at each spoke mouth.
        ///
        /// The four narrow routes are the ground the mode most wants people to fight over, and they
        /// are also the hardest to see: a five-metre opening in a hedge at forty metres is a slightly
        /// darker patch. The arches give them a silhouette above the hedge line, so a player picking
        /// a route across the arena can see all four of them from the loop.
        /// </summary>
        static void BuildGateways(Transform root)
        {
            var parent = new GameObject("Gateways").transform;
            parent.SetParent(root, false);

            var arch = Prop("ChokeGate");
            var post = Prop("BloomPost");
            var mat = PropMat();

            for (int i = 0; i < 4; i++)
            {
                float ang = TurfArena.ChokeAngle(i);
                Vector3 dir = TurfArena.Bearing(ang);
                // At the crest of the climb, which is also the middle of the hedge line — so the
                // arch stands at the highest and least visible point of the route.
                Vector3 at = dir * TurfArena.HedgeMid;
                at.y = TurfArena.GroundHeight(at.x, at.z);
                // The arch spans its own local X, which has to lie across the route — so local Z
                // points along it, which is the direction a mower is travelling.
                var rot = Quaternion.LookRotation(dir, Vector3.up);

                if (arch != null)
                {
                    var go = Spawn(parent, $"Choke arch {i}", arch, mat, true);
                    go.transform.SetPositionAndRotation(at, rot);
                    // Stretched across the opening it is framing, measured off the mesh. The arch
                    // was authored against a 5.8 m choke and dropped in at scale 1; the chokes are
                    // 7.2 m now, so it stood a metre inside each hedge face with daylight either
                    // side of it and read as scenery near a gap rather than as the gate. Only the
                    // span is scaled — the height is the silhouette this prop exists for.
                    float span = arch.bounds.size.x;
                    if (span > 0.01f)
                    {
                        var s = go.transform.localScale;
                        s.x = TurfArena.ChokeHalfWidth * 2f / span;
                        go.transform.localScale = s;
                    }
                    go.isStatic = true;

                    // Two capsules on the LEGS, not one collider on the arch. The arch is a hoop:
                    // its bounding volume is mostly the empty air a mower is supposed to drive
                    // through, so anything fitted to the whole prop walls the choke off completely.
                    // The legs stand on the opening's edges, which is where the hedge already is,
                    // so this only ever catches a machine that was going to clip the hedge anyway —
                    // and the crossbar five metres up stays clear, because nothing in this arena
                    // leaves the ground.
                    Vector3 across = Vector3.Cross(Vector3.up, dir);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var leg = new GameObject($"Arch leg {i}");
                        leg.transform.SetParent(parent, false);
                        leg.transform.position = at + across * (side * TurfArena.ChokeHalfWidth);
                        var col = leg.AddComponent<CapsuleCollider>();
                        col.direction = 1;
                        col.radius = 0.24f;
                        col.height = 4.2f;
                        col.center = new Vector3(0f, 2.1f, 0f);
                        leg.isStatic = true;
                    }
                }
                else
                {
                    foreach (float side in new[] { -1f, 1f })
                        Box(parent, $"Arch leg {i}", PoleMat(), new Vector3(0.28f, 4f, 0.28f),
                            at + Vector3.Cross(Vector3.up, dir) * (side * TurfArena.ChokeHalfWidth)
                              + Vector3.up * 2f, rot, keepCollider: true, castShadows: true);
                    // The crossbar stays a ghost: it is four metres up, and a collider there is one
                    // nobody can ever touch and everybody's physics has to test against.
                    Box(parent, $"Arch top {i}", PoleMat(),
                        new Vector3(TurfArena.ChokeHalfWidth * 2.2f, 0.3f, 0.3f),
                        at + Vector3.up * 4f, rot, keepCollider: false, castShadows: true);
                }
            }

            // Lamp posts at the mouths of ALL EIGHT openings, on both sides and both faces of the
            // hedge, so every route in is framed and legible from the loop and from the hub alike.
            //
            // The spokes used to have these and the chokes did not, which had it exactly backwards:
            // the wide diagonal roads are the openings you can already see, and the narrow cardinal
            // ones — the shortest way to the hub, and the ground the mode most wants contested —
            // were marked only by an arch five metres up. At night that difference is the whole
            // difference between four ways in and eight.
            var lamps = new GameObject("Lamp posts").transform;
            lamps.SetParent(parent, false);

            for (int g = 0; g < TurfArena.GapCount; g++)
            {
                float ang = TurfArena.GapAngle(g);
                float half = TurfArena.GapHalfWidth(g);
                foreach (float r in new[] { TurfArena.HedgeInner - 1.8f, TurfArena.HedgeOuter + 1.8f })
                foreach (float side in new[] { -1f, 1f })
                {
                    // 1.1 m clear of the opening's edge, worked out at THIS radius. The posts are
                    // solid now, so that margin is load bearing rather than cosmetic: it is the
                    // difference between a gatepost you thread past and a gatepost in the doorway.
                    float halfDeg = (half + 1.1f) / r * Mathf.Rad2Deg;
                    Vector3 dir = TurfArena.Bearing(ang + side * halfDeg);
                    Vector3 at = dir * r;
                    at.y = TurfArena.GroundHeight(at.x, at.z);
                    LampPost(lamps, $"Lamp post {g}", post, mat, at, Quaternion.Euler(0f, ang, 0f));
                }
            }
        }

        /// <summary>
        /// One lamp post: the prop, a collider on it, and a lit lantern at the top.
        ///
        /// SOLID, which is a change and a deliberate one. Everything the builder places as art has
        /// its collider taken off — see <see cref="Box"/> — and these were no exception, so a mower
        /// drove through the gateposts as though they were painted on. That reads as the arena being
        /// unfinished, and it also throws away the only thing standing near a gap that could ever
        /// make threading one cost something. A capsule rather than a box so a glancing blow slides
        /// off and spins you rather than stopping you dead on a corner; the post is 1.1 m outside
        /// the opening, so hitting one is a line you got wrong, not a doorway that was too narrow.
        ///
        /// LIT, because Duck/Prop cannot do it. The lantern is part of a single combined mesh drawn
        /// with one material, so there is no way to make that one piece of it glow from the material
        /// side — and no emission property on the shader to do it with even if there were. The
        /// lantern's light is therefore a separate unlit box sitting in the lantern's place, sized
        /// and positioned off the prop's own bounds so it stays there if the post is re-modelled.
        /// </summary>
        static void LampPost(Transform parent, string name, Mesh post, Material mat,
                             Vector3 at, Quaternion rot)
        {
            float top = 3.2f;

            if (post != null)
            {
                var go = Spawn(parent, name, post, mat, true);
                go.transform.SetPositionAndRotation(at, rot);
                go.isStatic = true;
                top = post.bounds.max.y;

                var col = go.AddComponent<CapsuleCollider>();
                col.direction = 1;                       // Y, up the pole
                col.radius = 0.22f;
                col.height = top;
                col.center = new Vector3(0f, top * 0.5f, 0f);
            }
            else
            {
                var box = Box(parent, name, PoleMat(), new Vector3(0.18f, top, 0.18f),
                              at + Vector3.up * (top * 0.5f), rot,
                              keepCollider: true, castShadows: true);
                box.gameObject.isStatic = true;
            }

            // The glass. Just under the top of the post, which is where the cap is.
            Box(parent, name + " glow", GlowMat("M_BloomLantern", "#FFCE86"),
                new Vector3(0.30f, 0.34f, 0.30f), at + Vector3.up * (top - 0.42f), rot,
                keepCollider: false, castShadows: false).gameObject.isStatic = true;
        }

        // Each gardener used to start on an apron: a 7 x 3.4 m plate of their livery painted on the
        // loop, there to answer "which of the four am I" from a seat at ground level. It is gone at
        // the player's request, and the job it was doing has moved up onto the stand behind each
        // start — see BuildCrowd. A coloured rectangle lying on the floor of a mode whose entire
        // subject is coloured rectangles lying on the floor was always going to be read as claimed
        // ground, and the mask deliberately starts neutral. A band of colour at eye height over
        // your own crowd says the same thing and cannot be mistaken for territory.

        // ------------------------------------------------------------------ night

        /// <summary>
        /// Put the scene into night, and make it stick.
        ///
        /// The colours are <see cref="CeremonyNight"/>'s own, read off the component rather than
        /// retyped, because the championship ends by fading to night on those values and a night
        /// LEVEL lit from a second set somebody typed into a builder is how the last shot of the
        /// game ends up being a different night from the level before it.
        ///
        /// The rest of this is repair work, and it is needed because <c>ApplyNightNow</c> writes
        /// three settings that the scene it is being applied to has already made inert:
        ///
        ///   * it sets <c>RenderSettings.ambientLight</c>, and BuildEnvironmentLighting has set
        ///     ambientMode to Trilight — in Trilight the three band colours ARE the probe and
        ///     ambientLight is not read at all. So the arena kept a full daylight sky probe.
        ///   * it sets <c>fogDensity</c>, and the fog here is Linear, which uses start/end distance
        ///     and ignores density completely.
        ///   * it does not touch the skybox, and the skybox is a bright blue afternoon that goes on
        ///     lighting every reflective surface in the arena through the default reflection probe.
        ///
        /// The result was a scene that had genuinely had night applied to it and was still, visibly,
        /// the middle of the day — moon at 0.42 under a midday probe under a midday sky. Nothing
        /// logged, nothing failed. This is the sort of thing that gets called "the night setting
        /// doesn't work" and then gets fixed by inventing darker numbers, which fixes the symptom in
        /// this scene and desynchronises it from the ceremony forever.
        /// </summary>
        static void BuildNight()
        {
            // The same read ApplyNightNow() does internally, done here so the sky and the fog
            // distances can be derived from the identical colours the moon and the ambient get.
            var defaults = new GameObject("~ NightDefaults").AddComponent<CeremonyNight>();
            Color ambient = defaults.nightAmbient, fog = defaults.nightFog, moon = defaults.moonColour;
            float moonIntensity = defaults.moonIntensity;
            Vector3 moonAngles = defaults.moonAngles;
            Object.DestroyImmediate(defaults.gameObject);

            // AND HERE THE ARENA'S NIGHT PARTS COMPANY WITH THE CEREMONY'S, deliberately, against
            // the advice directly above.
            //
            // They were kept identical so the two could never drift, which is the right instinct and
            // the wrong answer, because they are not doing the same job. The ceremony is a held beat
            // where the player looks at a trophy under a moon and the darkness IS the point. This is
            // seventy-five seconds of driving in which they have to read a hedge line, a touchline,
            // a plaza edge and four liveries at speed — and at the ceremony's levels they could not.
            // The stage came back from a playtest as unreadable murk.
            //
            // The lift is mostly AMBIENT rather than key, and that distinction is the whole trick.
            // Reading was being lost in the shadowed ground, not on the lit faces, so ambient roughly
            // doubles and opens those up. The moon goes 0.42 -> 0.92 and stays blue: raising the key
            // to a daylight value instead was tried first and it did make the arena legible, by
            // taking the night away with it, which nobody asked for. Sky, fog and reflection are
            // untouched below, so the palette stays cool and the lanterns stay the warm things in
            // frame — it reads as a floodlit night match, which is what it always wanted to be.
            //
            // CeremonyNight's own defaults are NOT edited, so the championship's closing night is
            // exactly as it was. Checked on screen at all three settings: murk, daylight, then this.
            moonIntensity = 0.92f;
            moon = new Color(0.72f, 0.80f, 1.00f);
            ambient = new Color(0.27f, 0.32f, 0.45f);

            CeremonyNight.ApplyNightNow(ambient, fog, moon, moonIntensity, moonAngles);

            // Flat, so the colour ApplyNightNow just wrote is the one Unity actually uses.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;

            // Linear fog, sized to this arena rather than to the density ApplyNightNow set and this
            // fog mode ignores. Starting inside the arena and ending just past the stands is what
            // separates the near hedge from the far one — at night, with one flat green material
            // everywhere, depth has to come from somewhere.
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            // Ending well past the stands rather than just beyond them. The reveal is shot from 96 m
            // up, so the ground in the money shot is 100-115 m from the lens — with the fog ending
            // at 170 that put more than half a fog over the one frame the whole match is read from,
            // and four liveries seen through half a fog are four greys.
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 260f;

            // A night sky, built from the night colours rather than picked. Its own material, never
            // M_Sky — that one is the afternoon every other scene in the game is played under.
            var sky = DuckSceneBuilder.EnsureMaterial("M_BloomNightSky", "Duck/SkyGradient");
            if (sky != null)
            {
                sky.SetColor("_Zenith", fog * 0.55f);
                sky.SetColor("_Mid", fog);
                sky.SetColor("_Horizon", Color.Lerp(fog, ambient, 0.7f));
                sky.SetColor("_GroundCol", fog * 0.4f);
                sky.SetColor("_SunColor", moon);
                sky.SetColor("_CloudColor", Color.Lerp(fog, moon, 0.25f));
                // The moon is a disc, not a glare. The sun glow is what makes a daytime sky read as
                // hot, and at these colours it reads as an unexplained bright smear.
                sky.SetFloat("_SunSize", 0.02f);
                sky.SetFloat("_SunGlow", 30f);
                sky.SetFloat("_SunGlowStrength", 0.16f);
                sky.SetFloat("_CloudAmount", 0.22f);
                sky.SetFloat("_Exposure", 1f);
                EditorUtility.SetDirty(sky);
                RenderSettings.skybox = sky;
            }

            // The reflection probe is the skybox, so it is now the night one. Kept low: a mode whose
            // whole readout is four saturated colours painted on turf cannot afford a bright grey
            // wash sitting on top of them.
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.22f;
            DynamicGI.UpdateEnvironment();
        }

        // ------------------------------------------------------------------ the audit

        /// <summary>
        /// Two things this arena got wrong for a long time, checked on every build.
        ///
        /// FIRST, ARE THERE DOORS. <see cref="RunBetween"/> walks the openings and lays hedge in the
        /// spans between them; if it walks them in the wrong order it lays hedge ACROSS them, which
        /// is precisely what happened — all eight, with colliders, in a mode about choosing a route.
        /// Nothing caught it because nothing else in the mode believed it: TurfArena said the gaps
        /// were open, the shader drew them open, the score counted them as ground, and only the
        /// geometry a mower can actually hit disagreed. So the check is on the geometry: every
        /// opening's centre line must fall outside every hedge run, and a failure is an error naming
        /// the gap rather than a scene somebody has to drive around to distrust.
        ///
        /// SECOND, WHERE IS THE SCORE. <see cref="TurfMask"/> counts cells, not rings — every square
        /// metre is worth exactly one square metre wherever it is. So the ring holding the most AREA
        /// is where a player who wants to win should spend the match, and no amount of prose about
        /// the hub being the most valuable ground changes that. Printing the split every build is
        /// the only way a layout change gets to be checked against the thing it is really deciding.
        /// </summary>
        static void AuditRings()
        {
            // Doors. Measured at the hedge's inner face, where the runs are angularly widest and a
            // door is therefore at its narrowest — if a gap survives here it survives everywhere.
            float r = TurfArena.HedgeInner;
            for (int g = 0; g < TurfArena.GapCount; g++)
            {
                float at = TurfArena.GapAngle(g);
                for (int b = 0; b < TurfArena.GapCount; b++)
                {
                    var (from, to) = RunBetween(b, r);
                    float span = Mathf.DeltaAngle(from, to);
                    if (span <= 0f) span += 360f;
                    float into = Mathf.Repeat(at - from, 360f);
                    if (into < span)
                        Debug.LogError(
                            $"[Bloom] the opening at {at:0} deg is buried under hedge run {b} " +
                            $"({from:0.0} to {to:0.0} deg). The wall has fewer doors than the " +
                            "layout thinks, and mowers will drive into it. See RunBetween.");
                }
            }

            // Gate clearance. The gateposts are solid now, so the width of an opening is no longer
            // what TurfArena says it is — it is that, minus whatever the two pillars beside it stick
            // back into. Checked rather than assumed, because "the gates are solid" and "the gates
            // got narrower" are one change and only one of them was asked for.
            var pillarMesh = Prop("HedgePillar");
            float pillarRadius = pillarMesh != null
                ? Mathf.Max(pillarMesh.bounds.size.x, pillarMesh.bounds.size.z) * 0.5f : 0.35f;
            for (int g = 0; g < TurfArena.GapCount; g++)
            {
                // The pillars sit 0.75 m outside the mouth; the collider eats back in by its radius.
                float clear = TurfArena.GapHalfWidth(g) + 0.75f - pillarRadius;
                float lost = TurfArena.GapHalfWidth(g) - clear;
                if (lost > 0.01f)
                    Debug.LogWarning(
                        $"[Bloom] the opening at {TurfArena.GapAngle(g):0} deg is " +
                        $"{TurfArena.GapHalfWidth(g) * 2f:0.0} m of ground but only " +
                        $"{clear * 2f:0.0} m of drivable width — the gate pillars' colliders take " +
                        $"{lost * 2f:0.0} m of it. Move them out or slim them.");
            }

            // The edge of the world, which has to be continuous or a mower leaves the arena. The
            // barrier is 32 flat panels standing on a circle, and a flat panel spans a CHORD while
            // its width is worked out from an ARC — so the check is whether each panel is still
            // wide enough to meet its neighbours after the arena radius changed.
            const int walls = 32;
            float wallR = TurfArena.BarrierRadius + 0.4f;
            float panel = 2f * Mathf.PI * wallR / walls * 1.15f;
            float chord = 2f * wallR * Mathf.Sin(Mathf.PI / walls);
            if (panel < chord)
                Debug.LogError($"[Bloom] the perimeter wall has gaps in it: {walls} panels of " +
                               $"{panel:0.00} m around a circle that needs {chord:0.00} m each. " +
                               "Mowers will drive out of the arena through the seams.");

            // Area, by sampling the same validity test the score is counted through.
            const float step = 0.25f;
            float cell = step * step;
            float plaza = 0f, court = 0f, wall = 0f, loop = 0f;
            for (float x = -TurfArena.ArenaRadius; x < TurfArena.ArenaRadius; x += step)
            for (float z = -TurfArena.ArenaRadius; z < TurfArena.ArenaRadius; z += step)
            {
                if (!TurfArena.IsPlayable(x, z)) continue;
                float d = Mathf.Sqrt(x * x + z * z);
                if (d < TurfArena.PlazaRadius) plaza += cell;
                else if (d < TurfArena.HedgeInner) court += cell;
                else if (d <= TurfArena.HedgeOuter) wall += cell;   // the ground inside the openings
                else loop += cell;
            }

            float total = Mathf.Max(plaza + court + wall + loop, 1f);
            Debug.Log($"[Bloom] paintable ground: hub {plaza:0} m2 ({plaza / total:P0}), " +
                      $"court {court:0} m2 ({court / total:P0}), gates {wall:0} m2 ({wall / total:P0}), " +
                      $"loop {loop:0} m2 ({loop / total:P0}). " +
                      $"Inside the wall: {(plaza + court) / total:P0} of the board.");
        }

        // ------------------------------------------------------------------ night

        /// <summary>
        /// Lamps at the eight openings, and one over the fountain. The night level's legibility.
        ///
        /// Moonlight is 0.42 of a directional and the arena is a dark green wheel inside a dark
        /// green ring, so at night the wall stops being a wall with doors in it and becomes a
        /// silhouette. That is fatal here specifically: the reported failure of this mode is that
        /// nobody goes to the middle, the first requirement of going to the middle is seeing where
        /// the ways in ARE, and turning the lights off is the most direct possible way to undo that.
        ///
        /// So the night is used the other way round. Eight warm lamps in a dark ring do what no
        /// amount of daylight could — they make the openings the brightest thing on the map, so
        /// from anywhere on the loop the arena reads as a dark hedge with eight lit doorways in it,
        /// and the eye is pulled through them at the lit fountain behind. The layout stops needing
        /// to be learned and starts being visible.
        ///
        /// NO LIGHTS. This was nine point lights before it was measured, and nine point lights in
        /// this project do nothing whatsoever: Duck/TurfGround only ever calls GetMainLight, and
        /// Duck/Prop declares the _ADDITIONAL_LIGHTS keyword and then never calls GetAdditionalLight
        /// anywhere in its fragment stage. Every lamp in the arena was costing a shadow-map slot and
        /// a culling pass to illuminate nothing at all, in a mode that has to hold sixty frames in a
        /// browser. If additional-light support is ever added to those two shaders this is where the
        /// lamps go back; until then, placing them is a way of believing the arena is lit.
        ///
        /// So the light is GEOMETRY. Unlit material, full brightness, ignores the moon entirely —
        /// which is what a lamp looks like anyway — and being unlit it is exactly as bright from
        /// forty metres out on the ring as it is from underneath, cannot be culled by a per-object
        /// light budget, and costs one flat-shaded draw. Eight pairs of them mark the eight
        /// doorways and one marks the fountain, so the arena at night reads as a dark hedge with
        /// eight lit gates in it and a lit landmark behind them. The eye goes where the lights are,
        /// and the lights are on every route to the middle.
        /// </summary>
        static void BuildNightLights(Transform root)
        {
            var parent = new GameObject("Night lamps").transform;
            parent.SetParent(root, false);

            var lantern = GlowMat("M_BloomLantern", "#FFCE86");

            // A band of the same light down the inner face of each gate pillar. The lamp posts
            // themselves are lit where they stand — see LampPost — but a lantern is a dot, and
            // eight dots on a dark ring at forty metres are indistinguishable from the pennant
            // poles behind them. A vertical BAR is a doorframe, and two of them the right distance
            // apart is a doorway, which is the shape the player has to recognise from the loop.
            for (int g = 0; g < TurfArena.GapCount; g++)
            {
                float ang = TurfArena.GapAngle(g);
                float half = TurfArena.GapHalfWidth(g);

                // The same radius and half-angle the hedge builder stands its pillars at, so the
                // light is ON the doorframe rather than near it. Never in the doorway: nothing may
                // stand in a gap a mower takes at ten metres a second.
                foreach (float r in new[] { TurfArena.HedgeInner + 0.5f, TurfArena.HedgeOuter - 0.5f })
                foreach (float side in new[] { -1f, 1f })
                {
                    float halfDeg = (half + 0.75f) / r * Mathf.Rad2Deg;
                    Vector3 dir = TurfArena.Bearing(ang + side * halfDeg);
                    Vector3 at = dir * r;
                    float y = TurfArena.GroundHeight(at.x, at.z);

                    Box(parent, $"Gate jamb {g}", lantern, new Vector3(0.14f, 2.0f, 0.14f),
                        at + Vector3.up * (y + 1.35f) - dir * 0.40f,
                        Quaternion.LookRotation(-dir, Vector3.up),
                        keepCollider: false, castShadows: false).gameObject.isStatic = true;
                }
            }

            // The fountain's own light lives with the fountain, in BuildPlaza — it is placed off
            // that prop's bounds and would only drift from it if it were positioned from here.

            // No Unity lights here, and that is deliberate — see TurfCommon.TurfGateGlow. URP is
            // configured PerVertex with a per-object limit of two, and the arena floor is a single
            // mesh, so eight point lights would light either nothing or one eighth of what they
            // should. The gateway pools are computed in the turf shaders from the same TurfArena
            // geometry these lamps are placed from, which costs no lights, no variants and no
            // project-wide graphics setting shared with two finished stages.
        }

        /// <summary>
        /// A material that is simply bright, for the parts of the night that have to be SEEN rather
        /// than lit.
        ///
        /// Unlit, not emissive. Emission was the first attempt and it is a trap here: Duck/Prop has
        /// no emission property at all, so EnableKeyword("_EMISSION") and a colour in
        /// _EmissionColor both land on the material, both show in the Inspector, and both are
        /// discarded by a shader that never reads them. The lamps looked configured and rendered as
        /// dark grey boxes. An unlit shader cannot fail that way — there is nothing to opt into.
        /// </summary>
        static Material GlowMat(string name, string hex)
        {
            var m = DuckSceneBuilder.EnsureMaterial(name, "Universal Render Pipeline/Unlit")
                 ?? DuckSceneBuilder.EnsureMaterial(name, "Unlit/Color");
            if (m == null) return DuckSceneBuilder.EnsureLit(name, hex);
            // Both shaders are covered: URP/Unlit reads _BaseColor, the built-in one reads _Color.
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", DuckSceneBuilder.HexL(hex));
            if (m.HasProperty("_Color")) m.SetColor("_Color", DuckSceneBuilder.HexL(hex));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ surrounds

        /// <summary>
        /// A hoarding around the whole arena.
        ///
        /// It closes the composition and it is also the only thing stopping a mower leaving: unlike
        /// the rally, where each competitor is confined to their own strip, everyone here may go
        /// anywhere, so the edge of the world has to be a real wall rather than a shove.
        /// </summary>
        static void BuildBarrier(Transform root)
        {
            var parent = new GameObject("Barrier").transform;
            parent.SetParent(root, false);

            float r = TurfArena.BarrierRadius;
            var mesh = Prop("ArenaBarrier");
            const float panelWidth = 2.4f;
            int panels = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * r / panelWidth));

            for (int i = 0; i < panels; i++)
            {
                float a = i / (float)panels * 360f;
                Vector3 dir = TurfArena.Bearing(a);
                Vector3 p = dir * r;
                var rot = Quaternion.LookRotation(-dir, Vector3.up);
                float chord = 2f * Mathf.PI * r / panels;

                if (mesh != null)
                {
                    var go = Spawn(parent, "Panel", mesh, PropMat(), true);
                    go.transform.SetPositionAndRotation(p, rot);
                    go.transform.localScale = new Vector3(chord / panelWidth, 1f, 1f);
                    go.isStatic = true;
                }
                else
                {
                    Box(parent, "Panel", i % 2 == 0 ? BarrierMat() : BarrierAltMat(),
                        new Vector3(chord * 0.98f, 1.1f, 0.2f), p + Vector3.up * 0.55f,
                        Quaternion.LookRotation(dir, Vector3.up),
                        keepCollider: false, castShadows: true).gameObject.isStatic = true;
                }
            }

            // The wall itself: a ring of box colliders, invisible, standing behind the hoarding.
            // Separate from the panels so the art can be changed, restyled or replaced without
            // anybody accidentally opening a hole in the edge of the arena.
            var wall = new GameObject("Wall").transform;
            wall.SetParent(parent, false);
            const int walls = 32;
            for (int i = 0; i < walls; i++)
            {
                float a = i / (float)walls * 360f;
                Vector3 dir = TurfArena.Bearing(a);
                var go = new GameObject($"Wall {i}");
                go.transform.SetParent(wall, false);
                go.transform.SetPositionAndRotation(dir * (r + 0.4f),
                                                    Quaternion.LookRotation(dir, Vector3.up));
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(2f * Mathf.PI * (r + 0.4f) / walls * 1.15f, 3f, 0.6f);
                col.center = new Vector3(0f, 1.5f, 0f);
                go.isStatic = true;
            }
        }

        /// <summary>
        /// Spectators, as banked blocks rather than as characters. Four stands, one behind each
        /// gardener's start, so every competitor has a crowd of their own to be cheered by.
        /// </summary>
        static void BuildCrowd(Transform root)
        {
            var parent = new GameObject("Crowd").transform;
            parent.SetParent(root, false);

            var mesh = Prop("CrowdStand");
            const float standWidth = 3.0f;
            var rng = new System.Random(90210);
            var seats = new List<SpectatorCrowd.Seat>(320);

            for (int i = 0; i < TurfArena.Count; i++)
            {
                var slot = TurfArena.Get(i);
                Vector3 back = slot.outward * (TurfArena.BarrierRadius + 3.2f);
                var rot = Quaternion.LookRotation(slot.inward, Vector3.up);

                if (mesh != null)
                {
                    for (int s = -5; s < 5; s++)
                    {
                        var go = Spawn(parent, $"Stand {i}.{s}", mesh, PropMat(), true);
                        go.transform.SetPositionAndRotation(
                            back + slot.right * ((s + 0.5f) * standWidth), rot);
                        // A box rather than a capsule: a grandstand is a wall, and rounding it off
                        // would let a machine slide along the crowd. Solid mostly on principle —
                        // the perimeter wall stands three metres in front of these and nothing can
                        // reach them — but a stand that would be driven through if the barrier were
                        // ever opened is a stand that is not really there.
                        var col = go.AddComponent<BoxCollider>();
                        col.size = new Vector3(standWidth, 3.2f, 3.2f);
                        col.center = new Vector3(0f, 1.6f, 0f);
                        go.isStatic = true;
                    }
                }
                else
                {
                    for (int tier = 0; tier < 3; tier++)
                        Box(parent, $"Stand {i}.{tier}", tier == 1 ? StandAltMat() : StandMat(),
                            new Vector3(30f - tier * 2f, 0.9f, 2.4f),
                            back + slot.outward * (tier * 2.2f) + Vector3.up * (0.45f + tier * 0.85f),
                            rot, keepCollider: false, castShadows: true).gameObject.isStatic = true;
                }

                SeatCrowd(seats, slot, back, standWidth, rng);

                // A band of the contestant's colour above their own stand, so the map in the corner
                // and the world agree about who is where.
                //
                // Taller and dropped to 2.4 m now that it is the ONLY thing telling a driver which
                // of the four they are — the start aprons that used to do it were floor-coloured
                // rectangles in a mode about floor-coloured rectangles, and they are gone. At 3.6 m
                // and 0.7 m deep this band sat above the chase camera's frame at the countdown,
                // which is the one moment it has to be readable. At 2.4 m it is dead centre of the
                // shot over the mower's roof, and at 1.5 m deep it survives being forty metres away.
                Box(parent, $"Colours {i}", LiveryMat(slot), new Vector3(30f, 1.5f, 0.15f),
                    back + slot.outward * 5.4f + Vector3.up * 2.4f, rot,
                    keepCollider: false, castShadows: false).gameObject.isStatic = true;
            }

            // One crowd for all four banks, once every bank has been seated.
            InstallCrowd(parent, seats);
        }

        /// <summary>
        /// Fill one bank of stands, every spectator turned to face the pitch.
        ///
        /// The facing is the part that has to be right and the part that is easiest to get wrong.
        /// Each bank sits on its own bearing, so "toward the arena" is a different heading for every
        /// one of the four — a single shared yaw would leave three quarters of the crowd staring
        /// into the dark. It comes off the slot's own inward vector, the same number the stands
        /// themselves are rotated by, so a spectator can never disagree with the bench they are on.
        ///
        /// Everyone is ON a bench. The rows step back and up together because that is what a stand
        /// is — each row has to see over the one in front — and the run is clamped to the width of
        /// the stands actually built, so nobody is left sitting on air past the end of the bank.
        /// That clamp is the whole reason the stand width is passed in rather than assumed.
        ///
        /// Positions are jittered along the row and the row is never quite full: a completely full
        /// stand reads as wallpaper and a completely random one reads as noise, and about one seat
        /// in seven left empty reads as a crowd.
        /// </summary>
        static void SeatCrowd(List<SpectatorCrowd.Seat> seats, in TurfArena.Slot slot,
                              Vector3 back, float standWidth, System.Random rng)
        {
            const int rows = 3;
            const float rowStep = 1.05f;     // metres back per row
            const float rowRise = 0.62f;     // and up per row
            const float alongStep = 0.95f;   // spacing across the bank

            // Ten stand pieces are placed either side of centre, so the bench runs this far each
            // way. Half a piece is kept clear at each end so nobody perches on the last edge.
            const int standPieces = 10;
            float halfBank = standPieces * standWidth * 0.5f - standWidth * 0.5f;

            float yaw = Quaternion.LookRotation(slot.inward, Vector3.up).eulerAngles.y;

            for (int row = 0; row < rows; row++)
            {
                int across = Mathf.FloorToInt(halfBank * 2f / alongStep) - row * 2;
                for (int a = 0; a < across; a++)
                {
                    if (rng.NextDouble() < 0.14) continue;

                    float u = (a - (across - 1) * 0.5f) * alongStep
                            + (float)(rng.NextDouble() - 0.5) * 0.22f;
                    if (Mathf.Abs(u) > halfBank) continue;      // never past the end of the bench

                    Vector3 p = back
                              + slot.right * u
                              + slot.outward * (row * rowStep + 0.4f)
                              + Vector3.up * (0.95f + row * rowRise);

                    seats.Add(new SpectatorCrowd.Seat
                    {
                        position = p,
                        // A few degrees of turn each way, so the bank is not a firing squad — but
                        // every one of them is still looking at the pitch.
                        yaw = yaw + (float)(rng.NextDouble() - 0.5) * 18f,
                        scale = 0.88f + (float)rng.NextDouble() * 0.3f,
                        species = rng.Next(8),
                        phase = (float)rng.NextDouble() * 6.28f,
                    });
                }
            }
        }

        /// <summary>
        /// The instanced crowd itself, on the venue's own spectator meshes and material.
        ///
        /// One SpectatorCrowd for all four banks rather than one each: it draws by species with
        /// DrawMeshInstanced, so a single component batches three hundred spectators into a handful
        /// of calls, and four components would be four times the batches for the same people.
        ///
        /// The idle is pushed well past the venue's. On the lawn the crowd is scenery a long way
        /// off; here they ring an arena the player drives at, and a bank of three hundred figures
        /// standing perfectly still is the deadest thing on screen. Bobbing them out of phase —
        /// every seat carries its own — reads as a room full of people rather than as one animation
        /// played three hundred times.
        /// </summary>
        static void InstallCrowd(Transform parent, List<SpectatorCrowd.Seat> seats)
        {
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
                crowd.crowdMaterial = Mat("M_Spectators");
                for (int i = 0; i < seats.Count; i++)
                {
                    var seat = seats[i];
                    seat.species = seat.species % species.Count;
                    seats[i] = seat;
                }
            }
            else
            {
                // Generated blobs rather than empty benches. Still a crowd, and still reacts.
                crowd.crowdMaterial = Mat("M_Crowd");
            }

            crowd.seats = seats.ToArray();
            // Bigger and faster than the venue's 0.026 / 1.6. These are close enough to read as
            // individuals, and a bob you cannot see is a bob nobody wrote.
            crowd.idleBobAmplitude = 0.075f;
            crowd.idleBobSpeed = 2.3f;
            crowd.cheerBobAmplitude = 0.26f;
            crowd.cheerBobSpeed = 8.5f;
            crowd.EnsurePlaceholderMeshes();

            Debug.Log($"[Bloom] crowd: {seats.Count} spectators seated across " +
                      $"{TurfArena.Count} banks, all facing the arena.");
        }

        /// <summary>
        /// The last of the arena's life: pennants around the hoarding, in the four liveries.
        ///
        /// Ambience with a job. The arena is a wheel of green with a stone thing in the middle and
        /// nothing in it moves except four mowers; a ring of colour at the edge gives the wind
        /// somewhere to show and gives the overhead reveal a frame.
        /// </summary>
        static void BuildDressing(Transform root)
        {
            var parent = new GameObject("Dressing").transform;
            parent.SetParent(root, false);

            const int flags = 40;
            for (int i = 0; i < flags; i++)
            {
                float a = i / (float)flags * 360f;
                Vector3 dir = TurfArena.Bearing(a);
                Vector3 at = dir * (TurfArena.BarrierRadius - 0.6f);
                var rot = Quaternion.LookRotation(-dir, Vector3.up);

                // NO COLLIDER on the poles, and this is a considered exception rather than an
                // oversight. They stand at 44.9 m — outside the 42 m touchline, inside the 45.9 m
                // wall — which is the run-off, and the run-off is the strip where a machine that
                // has already been shoved off the pitch is being pushed back toward the middle by
                // TurfCompetitor.Recover. Forty nine-centimetre posts standing in a recovery lane
                // are forty things for a mower to catch on while it is being pushed sideways past
                // them, which turns a shove into a wedge. The wall three metres behind is what stops
                // anybody leaving; these are bunting.
                var pole = Box(parent, $"Pole {i}", PoleMat(), new Vector3(0.09f, 3.4f, 0.09f),
                               at + Vector3.up * 1.7f, rot, keepCollider: false, castShadows: false);
                pole.gameObject.isStatic = true;

                var pennant = Box(parent, $"Pennant {i}", LiveryMat(TurfArena.Get(i % 4)),
                                  new Vector3(0.05f, 0.55f, 0.9f), at + Vector3.up * 3.0f, rot,
                                  keepCollider: false, castShadows: false);
                // Left non-static: the ambience component in the scene animates these, and a static
                // batched renderer cannot be moved at all — which is how a "reactive" flag ends up
                // being a painted one.
                pennant.gameObject.isStatic = false;
            }

            if (parent.GetComponent<TurfAmbience>() == null)
                parent.gameObject.AddComponent<TurfAmbience>();
        }

        // ------------------------------------------------------------------ machines

        static GameObject BuildMower(Transform lane, in TurfArena.Slot slot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mower.prefab");
            if (prefab == null)
            {
                Debug.LogWarning("[Bloom] Mower.prefab is missing; the arena has no machines in it.");
                return null;
            }

            // The existing prefab, four times over. A second machine authored here would drift from
            // the one the player has spent the game driving, and the premise is that all four
            // gardeners are on the identical model with the identical physics.
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = $"Mower — {slot.contestant}";
            go.transform.SetParent(lane, false);
            go.transform.SetPositionAndRotation(slot.SpawnPosition, slot.SpawnRotation);

            var controller = go.GetComponent<MowerController>();

            // Mini-turbos, in this mode and no other.
            //
            // MowerController ships with driftBoost false, so the round and the rally handle exactly
            // as they did before drift charging existed. It is switched on HERE because this arena
            // is the one that needs it: the loop is a ring, every opening through the wall is
            // radial, and entering one is therefore a ninety degree turn out of a tangent. At full
            // throttle the mower has 88 deg/s of yaw, which is 6.5 m of turning circle — wider than
            // the chokes were — so the honest way in used to be to lift, turn, and set off again,
            // and lifting to go somewhere is how a route becomes a chore.
            //
            // A handbrake slide is 1.45x the yaw at a third of the grip: 4.5 m of circle, tight
            // enough to carry the full ninety without shedding the entry speed, and it pays out a
            // timed burst on release. So the manoeuvre that gets you off the safe ring is the same
            // manoeuvre that fires you across the court on the other side. The arena's one corner,
            // eight times over, is now the thing there is to be good at.
            if (controller != null) controller.driftBoost = true;

            if (controller != null && slot.isPlayer)
                // Dust, skids and spray on the player's machine only. Four sets of mower VFX in a
                // WebGL frame buys three of them being seen from forty metres away.
                DuckVFXBuilder.Build(go, controller);

            if (slot.isPlayer)
            {
                if (go.GetComponent<DuckRider>() == null) go.AddComponent<DuckRider>();
                return go;
            }

            Repaint(go, slot);
            // Through DuckModelIntegration, which is where the one copy of this now lives. The copy
            // that used to be in this file parented the driver to the mower ROOT and read the seat
            // from a constant, which is correct in the saved prefab and wrong the instant the scene
            // runs — MowerVisuals drops VisualPivot by its ground offset at Awake and takes the duck
            // with it, so a root-parented rival was left 0.44 m in the air on a 0.42 m seat. Every
            // gardener, every match. The rally builder had already found and fixed this; only its
            // copy got the fix.
            DuckModelIntegration.SeatRival(go.transform, slot.contestant);
            return go;
        }

        // Unseat() USED TO BE HERE, alongside this file's own SeatRival. Both are gone into
        // DuckModelIntegration.SeatRival, which does the two halves together — because they ARE one
        // operation, and splitting them is how this copy ended up able to hide the duck and then put
        // the rival somewhere else. The shared version also puts the duck back when a rival mesh is
        // missing, which the pair here could not: Unseat had already run.

        static void Repaint(GameObject mower, in TurfArena.Slot slot)
        {
            var spec = Venue.PlotOf(slot.contestant);
            var livery = DuckVenueBuilder.LiveryMower(spec);
            if (livery == null) return;
            foreach (var r in mower.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Only the body. Repainting everything takes the tyres and the duck with it.
                if (r.name.Contains("Wheel") || r.name.Contains("Duck")) continue;
                r.sharedMaterial = livery;
            }
        }


        /// <summary>
        /// The wall around the core: a low stone ring nobody drives into.
        ///
        /// Sixteen segments rather than a single mesh collider, because the shape a driver has to
        /// predict is a circle and sixteen chords of a circle is one. The wall is deliberately LOW —
        /// waist height on a mower — so it stops a machine without hiding the three judges standing
        /// behind it, which is the entire reason the core exists. A wall the bench cannot be seen
        /// over would have moved the panel into the middle of the arena and then hidden it there.
        ///
        /// Colliders on every segment, and the ring is closed. Half a metre of gap in a barrier at
        /// the centre of a territory arena is not a gap, it is a shortcut, and it would be found in
        /// the first thirty seconds by the bot whose entire plan is the middle.
        /// </summary>
        static void BuildCoreWall(Transform parent)
        {
            const int Segments = 16;
            const float Height = 0.95f;
            const float Thickness = 0.7f;

            var root = new GameObject("Core wall").transform;
            root.SetParent(parent, false);

            float r = TurfArena.CoreRadius;
            // Chord length, plus an overlap so the corners meet rather than leaving sixteen slots.
            float chord = 2f * r * Mathf.Sin(Mathf.PI / Segments) + 0.18f;

            for (int i = 0; i < Segments; i++)
            {
                float deg = i * 360f / Segments;
                Vector3 dir = TurfArena.Bearing(deg);
                Box(root, $"Core {i:00}", KerbMat(),
                    new Vector3(chord, Height, Thickness),
                    dir * r + Vector3.up * (Height * 0.5f),
                    Quaternion.LookRotation(dir, Vector3.up),
                    keepCollider: true, castShadows: true);
            }

            Debug.Log($"[Bloom] core wall: {Segments} segments sealing a {r:0.#} m island at centre.");
        }

        /// <summary>
        /// The camera: the project's chase rig, with a lens choice and nothing else.
        ///
        /// Deliberately almost identical to the rally's. Bloom Rush is very tempting to film from
        /// above — the mode is a map, and a map wants a map shot — and every metre of height taken
        /// here is a metre of the driving lost. The lift happens once, at the end, when the driving
        /// is over.
        /// </summary>
        static CameraDirector BuildCameraRig(GameObject playerMower)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";

            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.12f;
            cam.farClipPlane = 340f;
            cam.allowHDR = false;
            if (go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            var director = go.AddComponent<CameraDirector>();
            if (playerMower != null)
            {
                director.target = playerMower.transform;
                director.mower = playerMower.GetComponent<MowerController>();
            }
            director.chaseDistance = 6.0f;
            director.chaseDistanceAtSpeed = 7.3f;
            director.chaseHeight = 3.2f;
            director.chaseHeightAtSpeed = 3.9f;
            director.fovBase = 53f;
            director.fovAtSpeed = 61f;
            // High enough to hold an 84 m arena with its stands in frame, which is the shot the
            // percentages are read over. Scaled with the barrier ring rather than left at the 104 m
            // the old 92 m arena needed — a reveal that pulls further back than the thing it is
            // revealing turns the four colours it exists to compare into four smudges.
            director.revealHeight = 96f;
            director.revealFov = 44f;

            // The closing shot on the player's machine, opened up for THIS stage's grass.
            //
            // The round's own numbers frame a duck standing in a mown plot; here the same shot is
            // taken from inside knee-high planted blades on a machine that has stopped in whatever
            // it was growing, and at 4.3 m the mower came out cropped against the bottom edge with
            // grass across its wheels. Further back and higher clears the blades and puts the whole
            // machine in the right-hand half, which is the half this shot exists to keep empty.
            director.verdictDistance = 5.9f;
            director.verdictHeight = 1.85f;
            director.verdictFov = 36f;

            go.AddComponent<AudioListener>();
            return director;
        }

        // ------------------------------------------------------------------ materials, on disk

        /// <summary>
        /// The generated hedge body.
        ///
        /// Keyed to the AUTHORED sections planted on its faces rather than picked as a plausible
        /// hedge green on its own. Duck/Prop multiplies a white base by Blender's vertex paint, so
        /// the authored arcs land around #7CC46E; the mass was first set to #2C4A22, which is a
        /// perfectly reasonable dark hedge and roughly a quarter of that. Beside the sections it
        /// read as pure black, and the ring came out looking like a green wall with a hole milled
        /// through the middle of it. Two greens that are meant to be one hedge have to be the same
        /// green.
        /// </summary>
        static Material HedgeMat() => DuckSceneBuilder.EnsureLit("M_BloomHedge", "#6FAF62");
        static Material KerbMat() => DuckSceneBuilder.EnsureLit("M_BloomKerb", "#C9C0A8");
        static Material PoleMat() => DuckSceneBuilder.EnsureLit("M_BloomPole", "#C8C2B4");
        static Material BarrierMat() => DuckSceneBuilder.EnsureLit("M_BloomBarrier", "#D8D2C4");
        static Material BarrierAltMat() => DuckSceneBuilder.EnsureLit("M_BloomBarrierAlt", "#4C7C6A");
        static Material StandMat() => DuckSceneBuilder.EnsureLit("M_BloomStand", "#9A9284");
        static Material StandAltMat() => DuckSceneBuilder.EnsureLit("M_BloomStandAlt", "#857D70");

        static Material LiveryMat(in TurfArena.Slot slot)
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(slot.livery);
            return DuckSceneBuilder.EnsureLit($"M_BloomLivery_{slot.contestant}", hex, 0.18f);
        }

        /// <summary>
        /// One of the authored arena props, by name.
        ///
        /// Returns null when the FBX has not been built, and every caller has a box fallback for
        /// exactly that case. Not because primitives are acceptable — they are the thing the art
        /// pass exists to remove — but because a builder that throws while an artist is halfway
        /// through a re-export leaves nobody with a scene to look at.
        /// </summary>
        static Mesh Prop(string name)
        {
            var m = DuckAssetLibrary.GetCombined("TurfProps.fbx", name, "Turf_" + name);
            // The barrier and the stands are the rally's; the two arenas share a venue's worth of
            // furniture rather than each having its own set that has to be restyled twice.
            return m ?? DuckAssetLibrary.GetCombined("RallyProps.fbx", name, "Rally_" + name);
        }

        static Material PropMat()
            => Mat("M_TurfProps") ?? Mat("M_RallyProps") ?? Mat("M_PropsAuthored")
            ?? Mat("M_FoliageAuthored") ?? DuckSceneBuilder.EnsureLit("M_TurfProps", "#FFFFFF");

        /// <summary>
        /// Give a placed prop something a mower can hit, sized from the prop's own mesh.
        ///
        /// A CAPSULE, always, and never the authored mesh. Every prop in this arena is a concave
        /// low-poly model with leaves, mouldings and rims on it, and a concave mesh collider at
        /// arcade speed catches a wheel on every one of them — the machine stops dead on a detail
        /// nobody can see, which reads as the game freezing rather than as a collision. A capsule
        /// makes every one of these a rounded post: a square hit stops you, a glancing one slides
        /// off and spins you, which is the behaviour the hedges already have and the reason the
        /// arena feels consistent to drive around.
        ///
        /// Sized off the mesh rather than typed, so a prop that gets re-modelled or re-scaled keeps
        /// a collider that matches what is on screen. <paramref name="tighten"/> pulls the radius in
        /// where the bounding circle would be dishonestly fat — a lamp post's box includes its lamp.
        /// </summary>
        static void Solid(GameObject go, Mesh mesh, float tighten = 1f)
        {
            if (go == null || mesh == null) return;
            var b = mesh.bounds;

            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 1;                                    // Y, standing up
            col.radius = Mathf.Max(b.size.x, b.size.z) * 0.5f * tighten;
            col.height = b.size.y;
            col.center = new Vector3(0f, b.min.y + b.size.y * 0.5f, 0f);
        }

        static GameObject Spawn(Transform parent, string name, Mesh mesh, Material mat, bool castShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }

        static Transform Box(Transform parent, string name, Material mat, Vector3 scale,
                             Vector3 position, Quaternion rotation, bool keepCollider, bool castShadows)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            // Colliders come off everything the builder places as art. The hedges and the perimeter
            // wall add their own deliberately, so what a mower can hit is a short list somebody
            // chose rather than whatever happened to be a cube.
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());

            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;

            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true;
            }
            return go.transform;
        }
    }
}
