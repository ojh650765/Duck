using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Bakes GooseRally.unity: the four-way arena, saved to disk.
    ///
    /// The generator writes a FILE. That is the load-bearing half of this and it is the project's
    /// established pattern rather than a new idea — DuckSceneBuilder and DuckArenaBuilder both treat
    /// a scene as generated output that somebody then opens, drags and keeps. Geometry that exists
    /// only in play mode cannot be selected, cannot be nudged and cannot be judged without pressing
    /// play, which for an arena whose entire job is to feel good to drive around is the wrong tool.
    ///
    /// Everything positional comes out of <see cref="RallyArena"/>, so the ground and the code that
    /// plays on it cannot disagree about where a garden is. Change a number there, run this, look.
    /// </summary>
    public static class DuckRallyBuilder
    {
        public const string ScenePath = "Assets/Scenes/GooseRally.unity";
        const string MatDir = "Assets/Materials";

        static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        [MenuItem("Duck/3 · Build goose rally scene", priority = 3)]
        public static void Build()
        {
            // The flock's prefab is authored by its own tool and this scene is useless without it, so
            // build it here rather than leaving a menu item somebody has to know to run first.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DuckGooseRig.PrefabPath) == null)
                DuckGooseRig.Build();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            DuckSceneBuilder.BuildLighting();
            DuckSceneBuilder.BuildEnvironmentLighting();
            DuckSceneBuilder.BuildPostProcessing();

            var root = new GameObject("~ Rally").transform;

            BuildGround(root);
            BuildBarrier(root);
            BuildCrowd(root);
            ScatterProps(root, new System.Random(90210));
            PlantSurrounds(root, new System.Random(31337));

            var competitors = new List<RallyCompetitor>(4);
            GameObject playerMower = null;

            for (int i = 0; i < RallyArena.Count; i++)
            {
                var slot = RallyArena.Get(i);
                var quadrant = new GameObject($"Quadrant {i} — {slot.contestant}").transform;
                quadrant.SetParent(root, false);

                BuildBandEdging(quadrant, slot);
                // NO BARE EARTH ANYWHERE. The defending strips, their scuffed patches and the
                // gardens' soil slabs are all gone: the arena is one continuous lawn, and where a
                // competitor may drive is stated by the fence, the garden and the flag rather than
                // by a change of surface. Three passes were spent trying to make brown ground look
                // good next to the lawn — dark mud, pale sand, stony earth — and the honest answer
                // was that the lawn never needed interrupting.

                var beds = new List<Transform>(RallyArena.BedsPerGarden);
                PlantGarden(quadrant, slot, beds);

                var sections = new List<Transform>(RallyArena.FenceSections);
                BuildFence(quadrant, slot, sections);

                var flag = BuildFlag(quadrant, slot);
                var totem = BuildTotem(quadrant, slot, out Transform totemBar);

                var mower = BuildMower(quadrant, slot);
                if (slot.isPlayer) playerMower = mower;

                // The garden and the competitor are two components on one object per quadrant, which
                // keeps everything about one contestant selectable in a single click.
                var holder = new GameObject($"Competitor {i} — {slot.contestant}");
                holder.transform.SetParent(quadrant, false);
                holder.transform.position = slot.bandCentre;

                var garden = holder.AddComponent<RallyGarden>();
                garden.slot = i;
                garden.beds = beds.ToArray();
                garden.fenceSections = sections.ToArray();
                garden.flag = flag;
                garden.totem = totem;
                garden.totemBar = totemBar;
                garden.trampledMaterial = TrampledMat();

                var comp = holder.AddComponent<RallyCompetitor>();
                comp.slot = i;
                comp.isPlayer = slot.isPlayer;
                comp.garden = garden;
                comp.mower = mower != null ? mower.GetComponent<MowerController>() : null;

                if (!slot.isPlayer)
                {
                    var brain = holder.AddComponent<RallyBrain>();
                    brain.competitor = comp;
                    brain.skill = slot.skill;
                    comp.brain = brain;
                }

                competitors.Add(comp);
            }

            // ---- systems ----

            var systems = new GameObject("~ Systems").transform;

            systems.gameObject.AddComponent<RallyFX>();
            // The world-space punctuation: knockout stars, breach stars and the horn's arcs. Kept
            // apart from RallyFX because that one throws debris and this one speaks — see its note.
            systems.gameObject.AddComponent<RallyWorldFX>();
            // NO TYRE TRACKS. They were marks pressed into bare earth, and with the earth gone a
            // dark band laid on grass is paint rather than a print — which is exactly the cheapness
            // that got them called tacky in the first place. RallyTracks is still there and still
            // works the moment there is a surface that takes a print.

            // The lawn remembers. Round one's mask, unchanged and unresized: it covers a 64 m square
            // on the origin, and every square metre anybody can DRIVE on here is inside 25.3 m of
            // the origin, so the four boxes fall well within it without a single number moving.
            // The grass outside is beyond the mask and simply never cut, which is correct — nothing
            // out there is drivable.
            //
            // Wheel ruts come free with it: MowerController presses tracks through the same mask
            // that cuts, so the pressed-flat lines behind each machine are the real thing rather
            // than the dark ribbons that had to be thrown away.
            systems.gameObject.AddComponent<CutMask>();

            var flock = new GameObject("~ Flock").transform;
            flock.SetParent(systems, false);

            // ONE GOOSE, SAVED INTO THE SCENE, INACTIVE.
            //
            // The flock is pooled from a prefab at runtime, and in the editor that works perfectly.
            // In the WebGL build the match ran — gardens took damage, the score moved — and not one
            // goose was visible. A prefab that is only ever reached through a serialized component
            // reference is included as data, but nothing in any BUILD SCENE is drawing a skinned
            // mesh with its materials, so the renderer's variants and the mesh's skinning path have
            // no reason to survive the build's stripping.
            //
            // A scene-resident instance gives them one. It is switched off, costs nothing at runtime,
            // and has the side benefit that somebody opening the scene can finally SEE the bird the
            // whole mode is about instead of an empty transform called "~ Flock".
            var goosePrefabGO = AssetDatabase.LoadAssetAtPath<GameObject>(DuckGooseRig.PrefabPath);
            if (goosePrefabGO != null)
            {
                var warm = (GameObject)PrefabUtility.InstantiatePrefab(goosePrefabGO);
                warm.name = "Goose (kept for the build)";
                warm.transform.SetParent(flock, false);
                warm.transform.localPosition = Vector3.zero;
                warm.SetActive(false);
            }

            var director = systems.gameObject.AddComponent<RallyDirector>();
            director.competitors = competitors.ToArray();
            director.flockParent = flock;
            // The board readout, on while the mode is being tuned. Three seconds is slow enough to
            // read and fast enough to catch a defender standing still through a whole approach.
            director.traceInterval = 3f;
            director.goosePrefab = AssetDatabase
                .LoadAssetAtPath<GameObject>(DuckGooseRig.PrefabPath)?.GetComponent<RallyGoose>();
            if (director.goosePrefab == null)
                Debug.LogWarning("[Rally] the goose prefab has no RallyGoose component; " +
                                 "run 'Duck/Rig the goose' and rebuild.");

            var camera = BuildCameraRig(playerMower);
            director.cameraDirector = camera;

            var hud = systems.gameObject.AddComponent<RallyHud>();
            hud.director = director;
            hud.view = camera != null ? camera.GetComponent<Camera>() : null;
            BuildHud(hud, hud.view);

            // ---- the bench ----
            //
            // The round's own judges, in the arena, so the end of the rally is delivered by the
            // panel the player already knows rather than by a results screen. Stood at the far side
            // beyond the barrier, raised, facing in: from there the three of them are in shot for
            // the closing camera without ever being between a competitor and a goose.
            var bench = DuckSceneBuilder.BuildJudgeBench();
            JudgePanel panel = null;
            if (bench != null)
            {
                panel = bench;
                var t = bench.transform;
                t.SetPositionAndRotation(
                    // On the ground. The plaza stands this bench on a raised plinth and the 1.1 m
                    // was that plinth's height carried across by hand — here the arena is dead flat,
                    // so the same number left three judges hovering a metre over the lawn.
                    new Vector3(0f, 0f, -(RallyArena.ArenaRadius + 13f)),
                    Quaternion.LookRotation(Vector3.forward, Vector3.up));
                // The camera's judging shot frames off this. Without it the closing beat is pointed
                // at the venue's origin, which in this scene is the middle of an empty pitch.
                if (camera != null) camera.judgesLookAt = t;
            }

            // The opening shot's subject: the middle of the pitch, seen from above.
            //
            // Round one opens on the whole plot before it drops onto the machine, and stage two
            // has to open the same way or the player is put behind a mower in a place they have
            // never been shown. Reveal frames whatever this points at, so one empty transform at
            // the origin buys the entire establishing beat.
            var arenaCentre = new GameObject("Arena centre").transform;
            arenaCentre.SetParent(root, false);
            arenaCentre.position = Vector3.zero;
            if (camera != null)
            {
                camera.revealLookAt = arenaCentre;
                camera.revealHeight = 78f;
                camera.revealTilt = 16f;
                camera.revealFov = 46f;
            }

            // Portraits, rendered from the real models at startup. The cards carry a FACE at the end
            // of a rally rather than a mark — see RallyVerdict — so without these the beat is three
            // judges holding up blanks.
            var portraits = systems.gameObject.AddComponent<ContestantPortraits>();
            portraits.subjects = BuildPortraitSubjects();

            // The championship board, the same one the plaza uses, stood behind the bench.
            //
            // Built here rather than invented, because the numbers it prints are the tournament's
            // and a second board with its own layout would be a second answer to the same question.
            // Behind and above the judges, so the closing camera move is one continuous push past
            // the panel onto the table rather than a cut to somewhere else.
            var boardRoot = new GameObject("Scoreboard rig").transform;
            boardRoot.SetParent(root, false);
            var board = DuckVenueBuilder.BuildScoreboard(boardRoot);
            Scoreboard scoreboard = null;
            if (board != null)
            {
                // At the OPPOSITE end of the arena from the bench, and that is a staging decision
                // rather than a tidy one. Standing thirteen metres behind the judges it loomed over
                // them, blank, through the whole verdict — the biggest object in the frame doing
                // nothing — and the camera move that follows would have been a push of thirteen
                // metres onto something already on screen, which is not a move.
                //
                // From the north end the verdict shot has open sky behind the bench, and the beat
                // that follows sweeps the whole length of the pitch to a board the player has not
                // been looking at. Same two shots, a hundred and thirty metres of travel between
                // them instead of thirteen.
                board.SetPositionAndRotation(
                    new Vector3(0f, 0f, RallyArena.ArenaRadius + 26f),
                    Quaternion.LookRotation(Vector3.back, Vector3.up));
                scoreboard = board.GetComponent<Scoreboard>();
                if (camera != null) camera.scoreboardAnchor = board;
            }

            var verdict = systems.gameObject.AddComponent<RallyVerdict>();
            verdict.panel = panel;
            verdict.cameraDirector = camera;
            verdict.portraits = portraits;
            verdict.scoreboard = scoreboard;

            var boot = systems.gameObject.AddComponent<RallyBootstrap>();
            boot.director = director;
            boot.verdict = verdict;

            // Sound. Without this AudioDirector.Instance is null in a standalone run and every honk,
            // thud and crowd cue the match fires does nothing at all — silently.
            DuckSceneBuilder.BuildAudioDirector(
                playerMower != null ? playerMower.GetComponent<MowerController>() : null, null);

            EditorSceneManager.MarkSceneDirty(scene);
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            // The scene cannot be entered from a round unless it is in the build settings, and
            // leaving that to a separate menu item means this reports success while producing a scene
            // the game cannot load.
            DuckMenuBuilder.RegisterBuildScenes();
            EnsureInBuildSettings();

            Debug.Log($"[Rally] built {ScenePath}: 4 quadrants, " +
                      $"{RallyArena.BedsPerGarden} beds and {RallyArena.FenceSections} fence sections each, " +
                      $"gardens {RallyArena.Reach:0} m out. Open it and press play.");
        }

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
        /// The floor.
        ///
        /// One box rather than the venue's GrassField, because the arena is 92 m across and the
        /// field is 64 — running the lawn here would leave the outer quarter of every quadrant, which
        /// is exactly where the gardens are, standing over a hole. It carries the only collider in
        /// the scene the mower is allowed to find.
        /// </summary>
        static void BuildGround(Transform root)
        {
            // STAGE ONE'S GRASS, minus the mowing.
            //
            // This was a flat box with fourteen hundred grass GameObjects scattered over it, and the
            // scatter was the wrong tool twice over: a plant per object is a transform, a renderer
            // and a culling entry each, which is a lot to ask of a browser for something the player
            // reads as "turf" — and even at fourteen hundred it was still visibly SPARSE, because
            // real turf needs blades in the hundreds of thousands and objects can never get there.
            //
            // GrassField already solves it and has since the first round: chunked blade meshes baked
            // on a jittered grid, two levels of detail, distance culling, and wind in the shader
            // rather than on the CPU. It is the same lawn the player has been mowing all game, which
            // is also the right answer artistically.
            //
            // No CutMask, deliberately. Nothing in the arena is mowable, so the grass simply stands
            // uncut — that is the whole of "stage one's grass minus the mowing logic".
            const float size = 152f;
            var lawn = new GameObject("Lawn").transform;
            lawn.SetParent(root, false);
            var grass = lawn.gameObject.AddComponent<GrassField>();
            grass.fieldSize = size;
            // No mask. There is no bare earth left for blades to be kept off — see the quadrant
            // build. RallyBareGround and GrassField's exclusion path both still exist and still
            // work; nothing in this arena has anything for them to do.
            grass.groundMaterial = Mat("M_GrassGround");
            grass.bladeMaterial = Mat("M_GrassBlades");
            // Coarser chunks and a shorter draw distance than the lawn: the arena is three times the
            // area, and the player is looking across it rather than down at it, so blades past about
            // thirty metres are costing frames to draw something that reads as flat colour anyway.
            // STAGE ONE'S GRASS, UNCHANGED.
            //
            // Density, blade shape and LOD distances are left at their defaults — the same numbers
            // the mowing round's lawn uses — because "bring it as it is" is the whole instruction and
            // every previous attempt to be clever about the cost made it look worse. Two passes were
            // spent thinning it and both times the answer came back that the lawn was too sparse.
            //
            // The ONE thing scaled is the chunk COUNT, and that is what keeps it honest rather than
            // what changes it: Stage 1 divides a 64 m field into 8 chunks, so a chunk is 8 m. This
            // field is 152 m, so it needs nineteen of them to keep chunks the same size. Same blades
            // per chunk, same LOD behaviour, same cost per square metre — just more square metres.
            grass.chunksPerSide = Mathf.RoundToInt(size / 8f);

            BuildSurround(lawn);
        }

        /// <summary>
        /// The country the arena stands in: round one's own surround and round one's own hills.
        ///
        /// The first attempt at this was a single 900 m box under everything, and it did not work.
        /// A dead-flat plate has no horizon in it — it meets the sky in a hard straight line at a
        /// fixed distance, so the trees standing near that line read as cut-outs hanging over
        /// nothing however precisely they are grounded. What sells distance is not more ground, it
        /// is ground that ROLLS and something standing on it that is plainly further away than the
        /// trees.
        ///
        /// So: the same SquareRing the venue uses, out to 470 m — past the hills, no further than
        /// the haze can see — and the same staggered ring of thirteen low mounds. Low on purpose;
        /// tall ones at that distance read as domes rather than as landscape.
        ///
        /// The flat radius is the load-bearing number. Undulation begins at 155 m, and the furthest
        /// tree this builder plants is at 148 — so every tree stands on ground that is exactly y=0,
        /// and none of them can be left hanging over a dip or buried to the knee in a rise. That is
        /// the hardest fault of its kind to spot in a screenshot, because a floating tree at 180 m
        /// in haze just looks like a tree.
        /// </summary>
        static void BuildSurround(Transform parent)
        {
            const float lawnHalf = 74f;      // just inside the 152 m field, so there is no seam
            const float outer = 470f;
            const float flat = 155f;

            var ground = new GameObject("Surround").transform;
            ground.SetParent(parent, false);

            var ring = DuckMeshLibrary.Persist(
                DuckPrimitives.SquareRing(lawnHalf, outer, 44, 4.5f, 23, flat), "RallySurround");
            var surround = Spawn(ground, "Surround", ring, SurroundMat(), false);
            surround.transform.position = new Vector3(0f, -0.05f, 0f);
            surround.isStatic = true;

            var hills = new GameObject("Hills").transform;
            hills.SetParent(parent, false);
            var hillMat = Mat("M_Hills") ?? SurroundMat();
            var rng = new System.Random(6180);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            for (int i = 0; i < 13; i++)
            {
                float a = i / 13f * Mathf.PI * 2f + R(-0.18f, 0.18f);
                float dist = R(255f, 380f);
                float radius = R(70f, 150f);
                float height = R(9f, 21f);
                var mesh = DuckMeshLibrary.Persist(
                    DuckPrimitives.Hill(radius, height, 4, 20, 700 + i), $"RallyHill_{i}");
                var go = Spawn(hills, $"Hill_{i}", mesh, hillMat, false);
                go.transform.SetPositionAndRotation(
                    new Vector3(Mathf.Cos(a) * dist, R(-6f, -2f), Mathf.Sin(a) * dist),
                    Quaternion.Euler(0f, R(0f, 360f), 0f));
                go.isStatic = true;
            }
        }

        /// <summary>
        /// The skyline behind the stands, and nothing on the lawn.
        ///
        /// Bales, troughs, rocks and molehills are modelled and they are good, and they are all
        /// switched off here on purpose: the playing surface is a competition lawn and every extra
        /// species of object on it is one more thing competing with the geese for the player's
        /// attention. The floodlights stay because they are not ON the lawn — they stand behind the
        /// crowd, where the skyline was otherwise empty sky in every wide shot.
        /// </summary>
        static void ScatterProps(Transform parent, System.Random rng)
        {
            var light = Dressing("Floodlight");
            if (light == null) return;
            var mat = PropMat();
            for (int i = 0; i < RallyArena.Count; i++)
            {
                var s = RallyArena.Get(i);
                foreach (float side in new[] { -1f, 1f })
                {
                    var go = Spawn(parent, "Floodlight", light, mat, true);
                    go.transform.position = s.outward * (RallyArena.ArenaRadius + 9f)
                                          + s.right * (side * 15f);
                    go.transform.rotation = Quaternion.LookRotation(s.inward, Vector3.up);
                    go.isStatic = true;
                }
            }
        }

        static Mesh Dressing(string name)
            => DuckAssetLibrary.GetCombined("RallyDressing.fbx", name, "RallyDress_" + name);

        /// <summary>
        /// The parkland the arena stands in — round one's own trees, round one's own hedges.
        ///
        /// Not new assets and not a new look: Foliage.fbx is what the venue is planted with, so
        /// using anything else here would make stage two a different place rather than a different
        /// event at the same one. The arena was a disc of lawn on an empty plane, and an empty
        /// horizon is what makes a pitch read as a test level however well the pitch itself is
        /// built.
        ///
        /// Everything sits OUTSIDE the barrier. Not one tree is inside the playable radius: a
        /// canopy over a defending box would hide a goose, and a trunk near a garden would be an
        /// obstacle nobody asked for. The ring is scenery and is allowed to be nothing else.
        /// </summary>
        static void PlantSurrounds(Transform parent, System.Random rng)
        {
            var species = new[]
            {
                DuckAssetLibrary.GetCombined("Foliage.fbx", "Tree_Oak", "Tree_Oak"),
                DuckAssetLibrary.GetCombined("Foliage.fbx", "Tree_Poplar", "Tree_Poplar"),
                DuckAssetLibrary.GetCombined("Foliage.fbx", "Tree_Apple", "Tree_Apple"),
            };
            var bush = DuckAssetLibrary.GetCombined("Foliage.fbx", "Bush", "Bush");

            bool any = false;
            foreach (var m in species) if (m != null) any = true;
            if (!any)
            {
                Debug.LogWarning("[Rally] Foliage.fbx has no Tree_* meshes; the arena stays on a bare plane.");
                return;
            }

            var wood = new GameObject("Surrounds").transform;
            wood.SetParent(parent, false);
            var mat = Mat("M_FoliageAuthored") ?? PropMat();

            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // The stands and the judges' bench are due south, so the ring opens up there — a wall of
            // oaks behind the bench would put the closing shot in a hedge.
            void Ring(float radius, int count, float scaleLo, float scaleHi, float jitter)
            {
                for (int i = 0; i < count; i++)
                {
                    float a = (i / (float)count) * 360f + R(-9f, 9f);
                    float rad = a * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

                    // Two gaps, at the two ends the ceremony uses: south is the bench, the stands
                    // and the way in; north is the championship board and the camera's run up to it.
                    if (Mathf.Abs(Mathf.DeltaAngle(a, 180f)) < 34f) continue;
                    if (Mathf.Abs(Mathf.DeltaAngle(a, 0f)) < 22f) continue;

                    var mesh = species[rng.Next(species.Length)];
                    if (mesh == null) continue;

                    Vector3 p = dir * (radius + R(-jitter, jitter));
                    float s = R(scaleLo, scaleHi);
                    var go = Spawn(wood, "Tree", mesh, mat, true);
                    go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, R(0f, 360f), 0f));
                    go.transform.localScale = new Vector3(s * R(0.92f, 1.1f), s * R(0.9f, 1.2f), s * R(0.92f, 1.1f));
                    go.isStatic = true;

                    // A companion two thirds of the time, so the line reads as woodland rather than
                    // as fence posts with leaves on.
                    if (rng.NextDouble() < 0.66)
                    {
                        var m2 = species[rng.Next(species.Length)];
                        if (m2 == null) continue;
                        float s2 = s * R(0.55f, 0.85f);
                        var g2 = Spawn(wood, "Tree", m2, mat, true);
                        g2.transform.SetPositionAndRotation(
                            p + new Vector3(R(-7f, 7f), 0f, R(-7f, 7f)),
                            Quaternion.Euler(0f, R(0f, 360f), 0f));
                        g2.transform.localScale = new Vector3(s2, s2 * R(0.9f, 1.15f), s2);
                        g2.isStatic = true;
                    }
                }
            }

            Ring(RallyArena.ArenaRadius + 22f, 26, 1.5f, 2.4f, 5f);
            Ring(RallyArena.ArenaRadius + 48f, 30, 1.8f, 3.0f, 11f);
            Ring(RallyArena.ArenaRadius + 84f, 26, 2.2f, 3.6f, 18f);

            if (bush == null) return;
            for (int i = 0; i < 70; i++)
            {
                float a = R(0f, 360f) * Mathf.Deg2Rad;
                float rad = R(RallyArena.ArenaRadius + 12f, RallyArena.ArenaRadius + 100f);
                var p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * rad;
                float s = R(0.7f, 1.5f);
                var go = Spawn(wood, "Bush", bush, mat, false);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, R(0f, 360f), 0f));
                go.transform.localScale = new Vector3(s, s * R(0.7f, 1.2f), s);
                go.isStatic = true;
            }
        }

        /// <summary>
        /// The four contestants, set up to be photographed for their portraits.
        ///
        /// Assembled the same way DuckSceneBuilder assembles the venue's — same meshes, same
        /// materials, same framing — so the face on a judge's card at the end of a rally is the same
        /// face the venue tour puts on its contestant card. Two different portraits of the same
        /// animal would read as two different animals.
        ///
        /// PUBLIC because Bloom Rush's bench holds up faces too now, and DuckTurfBuilder wires its
        /// own ContestantPortraits from this list rather than assembling a third one. The sentence
        /// above is the whole reason: a second list here is how the same goose ends up with two
        /// faces in one evening. It lives in this file rather than being hoisted somewhere neutral
        /// because this is where it was written and moving it would be churn in three files to
        /// change nothing — the arenas are already close relatives.
        /// </summary>
        public static ContestantPortraits.Subject[] BuildPortraitSubjects()
        {
            var subjects = new List<ContestantPortraits.Subject>(4);

            // "Duck" is the FBX's own root: this model's parts sit at the top level of the file with
            // no Duck_Root node, so asking for one returns nothing.
            var duck = DuckAssetLibrary.GetCombined("Duck.fbx", "Duck", "PortraitDuck");
            if (duck != null)
                subjects.Add(new ContestantPortraits.Subject
                {
                    contestant = Venue.Player.contestant,
                    mesh = duck,
                    material = Mat("M_Duck"),
                    lookOffset = new Vector3(0f, 0.30f, 0f),
                    framing = 0.26f,
                    yaw = 24f
                });
            // "[Duck]" and not "[Rally]" on both of these: two arenas build their benches from this
            // list now, so a tag naming one of them would send whoever reads the warning to the
            // wrong scene.
            else Debug.LogWarning("[Duck] no duck mesh for the player's portrait.");

            foreach (var spec in Venue.Plots)
            {
                if (spec.isPlayer) continue;
                string blenderName = char.ToUpper(spec.contestant[0]) + spec.contestant.Substring(1).ToLower();
                var mesh = DuckAssetLibrary.GetCombined("Rivals.fbx", $"{blenderName}_Root",
                                                        $"Rival_{blenderName}");
                if (mesh == null)
                {
                    Debug.LogWarning($"[Duck] no portrait mesh for {spec.contestant}.");
                    continue;
                }
                subjects.Add(new ContestantPortraits.Subject
                {
                    contestant = spec.contestant,
                    mesh = mesh,
                    material = Mat("M_Rivals"),
                    lookOffset = new Vector3(0f, 0.34f, 0f),
                    framing = 0.30f,
                    yaw = 26f
                });
            }
            return subjects.ToArray();
        }

        /// <summary>
        /// A flat, irregular patch of soil: a wobbled ellipse fanned from its centre.
        ///
        /// Two harmonics rather than random noise per vertex. Random gives a jagged sawtooth edge
        /// that reads as a broken mesh; two slow sine terms at different frequencies give a border
        /// that bulges and pinches the way dug ground actually does, and the low frequency is what
        /// stops it looking like a cog.
        ///
        /// Seeded off the quadrant, so the four gardens are different patches rather than one shape
        /// rotated four times — which is the tell that gives procedural placement away.
        /// </summary>
        static Mesh SoilPatch(float halfWidth, float halfDepth, int seed)
        {
            string key = $"RallySoilPatch_{seed}_{Mathf.RoundToInt(halfWidth * 10f)}" +
                         $"_{Mathf.RoundToInt(halfDepth * 10f)}";
            var cached = AssetDatabase.LoadAssetAtPath<Mesh>($"Assets/Meshes/{key}.asset");
            if (cached != null) return cached;

            const int seg = 56;
            var verts = new Vector3[seg + 1];
            var norms = new Vector3[seg + 1];
            var uvs = new Vector2[seg + 1];
            var tris = new int[seg * 3];

            float phase = seed * 1.37f;
            verts[seg] = Vector3.zero;
            norms[seg] = Vector3.up;
            uvs[seg] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float wobble = 1f
                             + Mathf.Sin(a * 3f + phase) * 0.085f
                             + Mathf.Sin(a * 7f + phase * 2.1f) * 0.045f;
                verts[i] = new Vector3(Mathf.Sin(a) * halfWidth * wobble, 0f,
                                       Mathf.Cos(a) * halfDepth * wobble);
                norms[i] = Vector3.up;
                uvs[i] = new Vector2(0.5f + Mathf.Sin(a) * 0.5f, 0.5f + Mathf.Cos(a) * 0.5f);
            }
            for (int i = 0; i < seg; i++)
            {
                int t = i * 3;
                tris[t] = seg;
                tris[t + 1] = (i + 1) % seg;
                tris[t + 2] = i;
            }

            var mesh = new Mesh { name = key };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return DuckMeshLibrary.Persist(mesh, key);
        }

        /// <summary>The dirt a competitor defends from. Bare earth, so where you may drive is obvious.</summary>
        /// <summary>
        /// The movement area, marked with a line of stones set into the grass.
        ///
        /// Drawn on <see cref="RallyArena.DriveHalfWidth"/> — the line the confinement actually
        /// enforces, not the nominal box — so the marking and the rule are the same line.
        ///
        /// No colliders. The stones say where a MACHINE may go, and a machine is held there by
        /// RallyCompetitor.Confine; a goose is not, and has to be able to walk straight over them.
        /// </summary>
        static void BuildBandEdging(Transform quadrant, in RallyArena.Slot slot)
        {
            var parent = new GameObject("Edging").transform;
            parent.SetParent(quadrant, false);

            var mat = StoneMat();
            var rng = new System.Random(4400 + slot.index);
            float w = RallyArena.DriveHalfWidth;
            float d = RallyArena.DriveHalfDepth;

            void Run(Vector3 a, Vector3 b, Vector3 outAxis)
            {
                float len = Vector3.Distance(a, b);
                int n = Mathf.Max(2, Mathf.RoundToInt(len / 1.05f));
                for (int i = 0; i <= n; i++)
                {
                    // Gaps, so it reads as stones laid by hand and not as a fence of them.
                    if (i > 0 && i < n && rng.NextDouble() < 0.12) continue;

                    float t = i / (float)n;
                    float jitter = (float)(rng.NextDouble() - 0.5) * 0.34f;
                    Vector3 p = Vector3.Lerp(a, b, t) + outAxis * ((float)rng.NextDouble() * 0.22f);
                    p += (b - a).normalized * jitter;

                    float s = 0.42f + (float)rng.NextDouble() * 0.5f;
                    Stone(parent, mat, p, s, rng);

                    // Every so often a smaller one tucked beside it.
                    if (rng.NextDouble() < 0.3)
                        Stone(parent, mat,
                              p + outAxis * (0.3f + (float)rng.NextDouble() * 0.25f)
                                + (b - a).normalized * ((float)rng.NextDouble() - 0.5f) * 0.5f,
                              s * 0.55f, rng);
                }
            }

            Vector3 c = slot.bandCentre;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 mid = c + slot.outward * (side * d);
                Run(mid - slot.right * w, mid + slot.right * w, slot.outward * side);
                Vector3 end = c + slot.right * (side * w);
                Run(end - slot.outward * d, end + slot.outward * d, slot.right * side);
            }
        }

        /// <summary>One irregular boulder, part-buried, on its own random axis.</summary>
        static void Stone(Transform parent, Material mat, Vector3 pos, float size, System.Random rng)
        {
            var go = new GameObject("Stone");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(
                new Vector3(pos.x, size * 0.5f - 0.14f * size, pos.z),
                Quaternion.Euler((float)rng.NextDouble() * 22f - 11f,
                                 (float)rng.NextDouble() * 360f,
                                 (float)rng.NextDouble() * 22f - 11f));
            go.transform.localScale = new Vector3(size * (0.85f + (float)rng.NextDouble() * 0.5f),
                                                  size * (0.6f + (float)rng.NextDouble() * 0.35f),
                                                  size * (0.85f + (float)rng.NextDouble() * 0.5f));
            go.isStatic = true;

            // PAINT, NOT KERB — no collider, deliberately.
            //
            // These used to carry a SphereCollider, on the reasoning that a machine should be
            // stopped by something it can SEE rather than by an invisible rule. Sound instinct,
            // wrong remedy, and it produced the exact complaint it was meant to prevent: "there is
            // an invisible wall, but when you turn or hit it you stop immediately."
            //
            // The arithmetic is why. Confine holds the machine's HULL at DriveHalfWidth — the same
            // line these stones are laid on — but a stone's body straddles that line and reaches
            // some 0.55m inboard of it at the fattest. So the collider was met FIRST, half a
            // machine before the rule, and the two disagreed by that margin all the way along. Then
            // Run() skips 12% of the stones on purpose, so it is laid with 1.3m gaps and the mower
            // is 0.92m wide: drive at one stretch of edge and you are stopped hard by a boulder,
            // drive at the next and you slide through the gap and are caught by the rule instead.
            // Two boundaries, half a machine apart, alternating along a single edge. No amount of
            // tuning either one fixes a player being taught two different edges.
            //
            // So the rule is the only boundary now, and the stones do what a marked line does:
            // they say where it is. They keep their mesh, their shadow and their jitter — a
            // boundary the player cannot see is the fault this whole thread started from, and the
            // marking has to survive the collider going away.
            //
            // Precedent, and it is the same fix: commit 380bbb8 ("The plaza edge is paint now, not
            // a kerb to hit") replaced Bloom Rush's collidable plaza kerb with a flat uncollidable
            // painted ring. DuckTurfBuilder's Box(...) takes a keepCollider flag for this reason,
            // and so does the copy at the bottom of THIS file, whose comment already states the
            // house rule outright: colliders come off everything but the ground. These stones were
            // the one thing in the arena that ignored it — note that BuildBandEdging's own summary
            // has claimed "No colliders" the whole time. The doc was right; the code had drifted.

            go.AddComponent<MeshFilter>().sharedMesh = StoneMesh(rng.Next(0, StoneVariants));
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
        }

        const int StoneVariants = 5;
        static Mesh[] _stoneMeshes;

        static Mesh StoneMesh(int variant)
        {
            if (_stoneMeshes == null) _stoneMeshes = new Mesh[StoneVariants];
            variant = Mathf.Clamp(variant, 0, StoneVariants - 1);
            if (_stoneMeshes[variant] != null) return _stoneMeshes[variant];

            const int rings = 5, seg = 9;
            var rng = new System.Random(8100 + variant);
            var pts = new Vector3[(rings + 1) * (seg + 1)];

            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int s = 0; s <= seg; s++)
                {
                    float th = Mathf.PI * 2f * s / seg;
                    var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th),
                                        Mathf.Cos(phi),
                                        Mathf.Sin(phi) * Mathf.Sin(th));
                    float lump = 1f
                        + 0.22f * Mathf.Sin(th * 3f + variant) * Mathf.Sin(phi * 2f)
                        + 0.14f * Mathf.Cos(th * 5f - variant * 2f)
                        + (float)(rng.NextDouble() - 0.5) * 0.13f;
                    var p = n * (0.5f * lump);
                    if (p.y < -0.16f) p.y = -0.16f;      // flat-ish base, so it sits
                    pts[r * (seg + 1) + s] = p;
                }
            }

            var verts = new System.Collections.Generic.List<Vector3>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var cols = new System.Collections.Generic.List<Color>();
            var tris = new System.Collections.Generic.List<int>();

            void Face(Vector3 a, Vector3 b, Vector3 cc)
            {
                var nrm = Vector3.Cross(b - a, cc - a).normalized;
                float shade = 0.78f + Mathf.Abs(nrm.y) * 0.28f
                            + (float)(rng.NextDouble() - 0.5) * 0.09f;
                var col = new Color(shade, shade * 0.985f, shade * 0.95f, 1f);
                int b0 = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(cc);
                norms.Add(nrm); norms.Add(nrm); norms.Add(nrm);
                cols.Add(col); cols.Add(col); cols.Add(col);
                tris.Add(b0); tris.Add(b0 + 1); tris.Add(b0 + 2);
            }

            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    Vector3 p00 = pts[r * (seg + 1) + s], p01 = pts[r * (seg + 1) + s + 1];
                    Vector3 p10 = pts[(r + 1) * (seg + 1) + s], p11 = pts[(r + 1) * (seg + 1) + s + 1];
                    Face(p00, p10, p11);
                    Face(p00, p11, p01);
                }

            var mesh = new Mesh { name = $"Stone{variant}" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            _stoneMeshes[variant] = DuckMeshLibrary.Persist(mesh, $"RallyStone{variant}");
            return _stoneMeshes[variant];
        }

        static void BuildDirt(Transform quadrant, in RallyArena.Slot slot)
        {
            var t = Box(quadrant, "DefenceDirt", DirtMat(),
                        new Vector3(RallyArena.BandHalfWidth * 2f, 0.06f, RallyArena.BandHalfDepth * 2f),
                        slot.bandCentre + Vector3.up * 0.03f,
                        Quaternion.LookRotation(slot.outward, Vector3.up),
                        keepCollider: false, castShadows: false);
            t.gameObject.isStatic = true;

            // A scuffed lip along the front edge, so the strip has a boundary the eye can find at
            // speed rather than a colour change that vanishes under the mower's own dust.
            //
            // Its top face sits ABOVE the strip's, and that is the whole of the fix for the z-fighting
            // that made every one of these flicker. The strip's top was at y = 0.06 and the lip was
            // centred at 0.035 with a height of 0.05 — so its top was at 0.06 as well. Two opaque
            // faces at identical depth is the textbook case, and no amount of biasing or draw order
            // helps: they have to stop being coplanar. Three centimetres of clearance is invisible
            // from any camera the game uses and unambiguous to the depth buffer.
            Box(quadrant, "DirtLip", DirtDarkMat(),
                new Vector3(RallyArena.BandHalfWidth * 2f + 1.2f, 0.05f, 0.5f),
                slot.bandCentre - slot.outward * RallyArena.BandHalfDepth + Vector3.up * 0.065f,
                Quaternion.LookRotation(slot.outward, Vector3.up),
                keepCollider: false, castShadows: false).gameObject.isStatic = true;
        }

        /// <summary>
        /// Scuffed patches in the defending strip.
        ///
        /// The ruts that used to be built here are gone: wheel tracks are laid at runtime now, as
        /// the machines actually drive — see <see cref="RallyTracks"/>. Pre-placing them claimed a
        /// history before anybody had made one, and the ground is more interesting when it is a
        /// record than when it is a texture.
        ///
        /// What stays is the earth's own character: irregular darker patches where a strip gets
        /// churned. Those are true whoever is driving and whatever they do.
        /// </summary>
        static void BuildScuffs(Transform quadrant, in RallyArena.Slot slot)
        {
            var parent = new GameObject("Scuffs").transform;
            parent.SetParent(quadrant, false);
            var rng = new System.Random(4100 + slot.index);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var mat = RutMat();

            for (int i = 0; i < 5; i++)
            {
                var p = slot.bandCentre
                      + slot.right * R(-RallyArena.BandHalfWidth * 0.92f, RallyArena.BandHalfWidth * 0.92f)
                      + slot.outward * R(-RallyArena.BandHalfDepth * 0.7f, RallyArena.BandHalfDepth * 0.7f);
                var go = new GameObject("Scuff");
                go.transform.SetParent(parent, false);
                // Under the runtime tracks (0.068) and over the strip (0.060).
                go.transform.SetPositionAndRotation(p + Vector3.up * 0.064f,
                                                    Quaternion.Euler(0f, R(0f, 360f), 0f));
                go.AddComponent<MeshFilter>().sharedMesh = SoilPatch(R(0.9f, 2.1f), R(0.7f, 1.6f),
                                                                     900 + slot.index * 7 + i);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
                go.isStatic = true;
            }
        }

        static void BuildGardenFloor(Transform quadrant, in RallyArena.Slot slot)
        {
            // An irregular patch of turned earth, not a rectangle.
            //
            // It was a box, and a box is what a bed of soil is least like. Nothing in a garden has a
            // straight edge except the things somebody built — the fence, the bed frames — and a
            // hard-cornered brown rectangle under them read as a placeholder floor tile, which is
            // exactly what it was. The outline is now a wobbled ellipse: same footprint, same beds
            // standing on it, but the edge wanders the way a dug border does.
            //
            // Flat rather than a slab, too. The box's SIDES were the other half of the problem —
            // from a low camera they drew a dark step all the way round the garden and the flowers
            // looked like they were standing on a pallet.
            var go = new GameObject("GardenSoil");
            go.transform.SetParent(quadrant, false);
            go.transform.SetPositionAndRotation(slot.gardenCentre + Vector3.up * 0.03f,
                                                Quaternion.LookRotation(slot.outward, Vector3.up));
            go.AddComponent<MeshFilter>().sharedMesh =
                SoilPatch(RallyArena.GardenHalfWidth + 1.1f, RallyArena.GardenHalfDepth + 0.8f,
                          slot.index);
            var smr = go.AddComponent<MeshRenderer>();
            smr.sharedMaterial = SoilMat();
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = true;
            go.isStatic = true;
        }

        // ------------------------------------------------------------------ gardens

        /// <summary>
        /// Plant a garden as fifteen beds, each carrying BOTH states.
        ///
        /// Intact and Ruined are authored meshes sharing a footprint, edging and bloom positions, and
        /// both are built here with the wreck switched off — so destroying a bed is one SetActive
        /// pair rather than a scale and a material swap. That matters more than it sounds: the old
        /// version squashed the standing mesh to fourteen percent height and repainted it brown,
        /// which from the mower reads as a bed that has been scaled rather than one that has been
        /// destroyed. See RallyGarden.Flatten for the fallback when a bed has neither child.
        /// </summary>
        static void PlantGarden(Transform quadrant, in RallyArena.Slot slot, List<Transform> collect)
        {
            var parent = new GameObject("Garden").transform;
            parent.SetParent(quadrant, false);
            parent.position = slot.gardenCentre;

            var intactMesh = Prop("GardenBed") ?? BedMesh();
            var ruinedMesh = Prop("GardenBed_Trampled");
            var mat = PropMat();
            var rot = Quaternion.LookRotation(slot.inward, Vector3.up);

            for (int c = 0; c < RallyArena.BedColumns; c++)
            for (int r = 0; r < RallyArena.BedRows; r++)
            {
                Vector3 p = RallyArena.BedPosition(slot, c, r);
                // Sat on the soil slab's top face, not level with it — coplanar surfaces z-fight,
                // and a garden that flickers is worse than one on bare ground.
                p.y = 0.05f;

                var go = new GameObject($"Bed {c},{r}");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(p, rot);

                if (intactMesh != null)
                {
                    Spawn(go.transform, "Intact", intactMesh, mat, true);
                    if (ruinedMesh != null)
                        Spawn(go.transform, "Ruined", ruinedMesh, mat, true).SetActive(false);
                }
                else
                {
                    // No authored bed is a content fault, not a reason to ship a garden with nothing
                    // in it. A tinted block in the contestant's colour keeps the quadrant readable
                    // while somebody fixes the FBX.
                    Box(go.transform, "Proxy", LiveryMat(slot),
                        new Vector3(1.5f, 0.4f, 1.0f), p + Vector3.up * 0.2f, rot,
                        keepCollider: false, castShadows: true);
                }
                collect.Add(go.transform);
            }
        }

        /// <summary>
        /// The fence, as SECTIONS rather than as loose pickets.
        ///
        /// Each section is a parent holding two uprights and two rails, so a goose coming through
        /// takes out a whole panel and leaves a hole with an obvious width. Toppling individual
        /// pickets — which is what the one-on-one arena did — reads as the fence losing a tooth
        /// rather than as something having been smashed through it.
        /// </summary>
        static void BuildFence(Transform quadrant, in RallyArena.Slot slot, List<Transform> collect)
        {
            var parent = new GameObject("Fence").transform;
            parent.SetParent(quadrant, false);
            parent.position = slot.fenceCentre;

            var mat = PropMat();
            var fallbackMat = FenceMat();
            var post = Prop("FencePost");
            var picketMesh = Prop("FencePicket");
            var rail = Prop("FenceRail");
            var brokenMesh = Prop("FenceSection_Broken");
            var debris = new[] { Prop("FenceDebris_A"), Prop("FenceDebris_B"), Prop("FenceDebris_C") };

            // The authored rail is two metres long and the run is 18.8, so nine sections come out at
            // 2.09 — close enough that the rails butt with a 4 cm gap behind a post rather than
            // needing the mesh rescaled, which would visibly stretch its bevels.
            // Facing INWARD, so the section's local X runs along the frontage and its local Z is the
            // fence's thickness. That is not cosmetic: the authored rail is two metres long down its
            // own X and every post and picket below is offset along the same axis, so a rotation
            // that puts X across the run stacks the whole panel into the garden and leaves the
            // frontage in clumps with holes between them. (RallyArena.right is defined as
            // cross(up, inward), which is exactly what LookRotation(inward) gives as local X.)
            var rot = Quaternion.LookRotation(slot.inward, Vector3.up);
            float width = RallyArena.FenceHalfWidth * 2f / RallyArena.FenceSections;

            for (int i = 0; i < RallyArena.FenceSections; i++)
            {
                float u = -RallyArena.FenceHalfWidth + width * (i + 0.5f);
                Vector3 centre = slot.fenceCentre + slot.right * u;

                var section = new GameObject($"Section {i}").transform;
                section.SetParent(parent, false);
                section.SetPositionAndRotation(centre, rot);

                if (post != null && rail != null)
                {
                    var intact = new GameObject("Intact").transform;
                    intact.SetParent(section, false);

                    // Posts on the section boundaries, so neighbouring panels share an upright line
                    // and the run reads as one fence rather than as nine fences in a row.
                    foreach (float side in new[] { -1f, 1f })
                        Spawn(intact, "Post", post, mat, true).transform.localPosition =
                            new Vector3(side * width * 0.5f, 0f, 0f);

                    // Pickets between them. A fence is read from its verticals; five across two
                    // metres is dense enough to be a barrier rather than a set of standing stones.
                    if (picketMesh != null)
                        for (int p = 0; p < 5; p++)
                        {
                            float pu = (p / 4f - 0.5f) * (width * 0.72f);
                            Spawn(intact, "Picket", picketMesh, mat, true).transform.localPosition =
                                new Vector3(pu, 0f, 0f);
                        }

                    // Two rails do most of the work: a picket fence is read from its HORIZONTAL
                    // lines, and two long members turn the uprights into one continuous barrier.
                    foreach (float y in new[] { 0.26f, 0.58f })
                    {
                        var r = Spawn(intact, "Rail", rail, mat, true);
                        r.transform.localPosition = new Vector3(0f, y, 0f);
                        r.transform.localScale = new Vector3(width / 2f, 1f, 1f);
                    }

                    if (brokenMesh != null)
                    {
                        var wrecked = new GameObject("Ruined").transform;
                        wrecked.SetParent(section, false);
                        Spawn(wrecked, "Splinters", brokenMesh, mat, true);
                        // Loose chunks around it, so a break-in leaves wreckage on the ground rather
                        // than one tidy broken panel standing where a whole one used to be.
                        for (int d = 0; d < debris.Length; d++)
                        {
                            if (debris[d] == null) continue;
                            var chunk = Spawn(wrecked, $"Debris {d}", debris[d], mat, false);
                            chunk.transform.localPosition =
                                new Vector3((d - 1) * 0.55f, 0.02f, 0.35f + d * 0.22f);
                            chunk.transform.localRotation = Quaternion.Euler(0f, d * 63f, 0f);
                        }
                        wrecked.gameObject.SetActive(false);
                    }
                }
                else
                {
                    // No authored fence: three boxes and two rails, which is a barrier somebody can
                    // still read and smash while the FBX is being fixed.
                    for (int p = 0; p < 3; p++)
                        Box(section, "Picket", fallbackMat, new Vector3(0.10f, 0.70f, 0.09f),
                            centre + slot.right * ((p - 1) * width * 0.33f) + Vector3.up * 0.35f,
                            rot, keepCollider: false, castShadows: true);
                    foreach (float y in new[] { 0.24f, 0.54f })
                        Box(section, "Rail", fallbackMat, new Vector3(width * 0.98f, 0.08f, 0.05f),
                            centre + Vector3.up * y, Quaternion.LookRotation(slot.outward, Vector3.up),
                            keepCollider: false, castShadows: true);
                }

                collect.Add(section);
            }
        }

        // ------------------------------------------------------------------ quadrant dressing

        static Transform BuildFlag(Transform quadrant, in RallyArena.Slot slot)
        {
            // Behind the garden and off to one side, so it is over the quadrant it belongs to and
            // never between the player and anything they are aiming at.
            Vector3 basePos = slot.gardenCentre + slot.outward * 3.4f
                            + slot.right * (RallyArena.GardenHalfWidth + 1.6f);

            // The whole pole moves. RallyGarden flexes this transform a few degrees as the alarm
            // rises, which on a one-piece authored mast has to stay small — see its note.
            var pivot = new GameObject("Flag").transform;
            pivot.SetParent(quadrant, false);
            pivot.position = basePos;
            pivot.rotation = Quaternion.LookRotation(slot.inward, Vector3.up);

            var mesh = Prop("ArenaFlag");
            if (mesh != null)
            {
                var go = Spawn(pivot, "Pole", mesh, PropMat(), true);
                // Scaled up: the authored flag is 3.28 m, which is right beside a garden and lost
                // entirely from a mower forty metres away on the far diagonal. The quadrant flags are
                // one of only three things that mark a corner from across the arena.
                go.transform.localScale = Vector3.one * 1.55f;
                // A livery banner on the mast, since the prop's colour is baked vertex paint and
                // cannot be tinted per contestant without its own slot.
                Box(pivot, "Livery", LiveryMat(slot), new Vector3(0.09f, 1.05f, 1.5f),
                    basePos + Vector3.up * 3.5f + slot.inward * 0.5f, pivot.rotation,
                    keepCollider: false, castShadows: false);
                return pivot;
            }

            Box(pivot, "PoleProxy", PoleMat(), new Vector3(0.16f, 5.2f, 0.16f),
                basePos + Vector3.up * 2.6f, Quaternion.identity,
                keepCollider: false, castShadows: true);
            Box(pivot, "Cloth", LiveryMat(slot), new Vector3(0.06f, 0.9f, 1.9f),
                basePos + Vector3.up * 4.9f + slot.inward * 0.95f, pivot.rotation,
                keepCollider: false, castShadows: false);
            return pivot;
        }

        /// <summary>
        /// The quadrant's marker post, and the gauge on it.
        ///
        /// The bar is returned separately and scaled by <see cref="RallyGarden"/> as the garden is
        /// destroyed, so each competitor's score stands in the world rather than only in the corner
        /// of the player's screen. In a free-for-all that is not decoration: the decision the player
        /// makes forty times a match is which of three opponents to send the bird at, and answering
        /// it should be a matter of looking across the arena.
        /// </summary>
        static Transform BuildTotem(Transform quadrant, in RallyArena.Slot slot, out Transform bar)
        {
            // On the fence line at the edge of the frontage, where a defender driving their own strip
            // has it in peripheral vision the whole match.
            Vector3 p = slot.fenceCentre - slot.right * (RallyArena.FenceHalfWidth + 1.1f);

            var pivot = new GameObject("Totem").transform;
            pivot.SetParent(quadrant, false);
            pivot.position = p;
            pivot.rotation = Quaternion.LookRotation(slot.inward, Vector3.up);
            bar = null;

            var mesh = Prop("ScoreTotem");
            if (mesh != null)
            {
                Spawn(pivot, "Post", mesh, PropMat(), true).transform.localScale = Vector3.one * 1.6f;

                var barMesh = Prop("ScoreTotem_Bar");
                if (barMesh != null)
                {
                    var b = Spawn(pivot, "Bar", barMesh, LiveryMat(slot), false);
                    b.transform.localScale = Vector3.one * 1.6f;
                    bar = b.transform;
                }
                // The contestant's colour, since the prop's roundel is baked vertex paint.
                Box(pivot, "Livery", LiveryMat(slot), new Vector3(0.95f, 0.34f, 0.09f),
                    p + Vector3.up * 3.2f, pivot.rotation, keepCollider: false, castShadows: false);
                return pivot;
            }

            Box(pivot, "Post", PoleMat(), new Vector3(0.22f, 2.0f, 0.22f),
                p + Vector3.up * 1.0f, pivot.rotation, keepCollider: false, castShadows: true);
            var plate = Box(pivot, "Plate", LiveryMat(slot), new Vector3(1.15f, 0.75f, 0.1f),
                            p + Vector3.up * 2.15f, pivot.rotation,
                            keepCollider: false, castShadows: true);
            bar = plate;
            return pivot;
        }

        /// <summary>
        /// A low hoarding around the whole arena.
        ///
        /// It exists to close the composition, not to stop anything: the competitors are held on
        /// their own dirt by <see cref="RallyCompetitor"/> and the geese leave over the top of it.
        /// Without it the arena's edge is where the grass box happens to stop, which from a low chase
        /// camera reads as the level running out.
        /// </summary>
        static void BuildBarrier(Transform root)
        {
            var parent = new GameObject("Barrier").transform;
            parent.SetParent(root, false);

            float r = RallyArena.ArenaRadius + 4f;
            var mesh = Prop("ArenaBarrier");
            // Sized off the authored panel so the ring tiles end to end with no doubled geometry at
            // the seams — the prop is modelled to butt at x = ±1.20.
            const float panelWidth = 2.4f;
            int panels = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * r / panelWidth));

            for (int i = 0; i < panels; i++)
            {
                float a = i / (float)panels * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                // Faces the middle of the arena, which puts the panel's long axis along the ring.
                // Pointing it down the tangent instead lays every hoarding radially and the barrier
                // reads as a circle of easels rather than as a wall.
                var rot = Quaternion.LookRotation(-new Vector3(p.x, 0f, p.z).normalized, Vector3.up);

                if (mesh != null)
                {
                    var go = Spawn(parent, "Panel", mesh, PropMat(), true);
                    go.transform.SetPositionAndRotation(p, rot);
                    // Stretched a hair so the ring closes exactly rather than leaving one wedge gap.
                    float chord = 2f * Mathf.PI * r / panels;
                    go.transform.localScale = new Vector3(chord / panelWidth, 1f, 1f);
                    go.isStatic = true;
                    continue;
                }

                Box(parent, "Panel", i % 2 == 0 ? BarrierMat() : BarrierAltMat(),
                    new Vector3(2f * Mathf.PI * r / panels * 0.96f, 0.9f, 0.18f),
                    p + Vector3.up * 0.45f,
                    Quaternion.LookRotation(new Vector3(p.x, 0f, p.z).normalized, Vector3.up),
                    keepCollider: false, castShadows: true).gameObject.isStatic = true;
            }
        }

        /// <summary>
        /// Spectators, as banked blocks rather than as characters.
        ///
        /// Four stands, one behind each quadrant, so every competitor has a crowd of their own to be
        /// cheered and groaned at by — which is what makes the audio directional even though the mix
        /// is not. Blocks because forty rigged extras is a frame budget this mode does not have and a
        /// detail nobody looks at from inside a mower.
        /// </summary>
        static void BuildCrowd(Transform root)
        {
            var parent = new GameObject("Crowd").transform;
            parent.SetParent(root, false);

            var mesh = Prop("CrowdStand");
            const float standWidth = 3.0f;
            var rng = new System.Random(5150);
            var seats = new System.Collections.Generic.List<SpectatorCrowd.Seat>(280);

            for (int i = 0; i < RallyArena.Count; i++)
            {
                var slot = RallyArena.Get(i);
                Vector3 back = slot.outward * (RallyArena.ArenaRadius + 6.5f);
                var rot = Quaternion.LookRotation(slot.inward, Vector3.up);

                if (mesh != null)
                {
                    // Eight authored stands butted end to end — the prop tiles at x = ±1.50 with no
                    // side cheeks, so a run of them reads as one bank rather than as eight sheds.
                    for (int s = -4; s < 4; s++)
                    {
                        var go = Spawn(parent, $"Stand {i}.{s}", mesh, PropMat(), true);
                        go.transform.SetPositionAndRotation(
                            back + slot.right * ((s + 0.5f) * standWidth), rot);
                        go.isStatic = true;
                    }
                }
                else
                {
                    for (int tier = 0; tier < 3; tier++)
                        Box(parent, $"Stand {i}.{tier}", tier == 1 ? StandAltMat() : StandMat(),
                            new Vector3(24f - tier * 2f, 0.9f, 2.4f),
                            back + slot.outward * (tier * 2.2f) + Vector3.up * (0.45f + tier * 0.85f),
                            rot, keepCollider: false, castShadows: true).gameObject.isStatic = true;
                }

                // A band of the contestant's colour above their own stand, so the map in the corner
                // and the world agree about who is where.
                // Behind the top row and just above it, so it reads as a hoarding at the back of the
                // stand. Any higher and it floats in the sky with nothing under it.
                Box(parent, $"Banner {i}", LiveryMat(slot), new Vector3(12f, 1.0f, 0.14f),
                    back + slot.outward * 1.15f + Vector3.up * 2.25f, rot,
                    keepCollider: false, castShadows: false).gameObject.isStatic = true;

                SeatCrowd(seats, slot, back, rng);
            }

            InstallCrowd(parent, seats);
        }

        /// <summary>
        /// Fill one bank of stands, every spectator turned to face the pitch.
        ///
        /// The facing is the part that has to be right and the part that is easiest to get wrong.
        /// Each bank sits on its own corner, so "toward the arena" is a different heading for every
        /// one of the four — a single shared yaw would leave three quarters of the crowd staring
        /// into the trees. It comes off the slot's own inward vector, the same number the stands
        /// themselves are rotated by, so a spectator can never disagree with the bench they are on.
        ///
        /// Rows step back and up together because that is what a stand is: each row must see over
        /// the one in front. Positions are jittered along the row and the row is never quite full,
        /// so it reads as people who chose where to sit rather than as a grid.
        /// </summary>
        static void SeatCrowd(System.Collections.Generic.List<SpectatorCrowd.Seat> seats,
                              in RallyArena.Slot slot, Vector3 back, System.Random rng)
        {
            const int rows = 3;
            const float rowStep = 1.05f;     // metres back per row
            const float rowRise = 0.62f;     // and up per row
            const float alongStep = 0.95f;   // spacing across the bank

            float yaw = Quaternion.LookRotation(slot.inward, Vector3.up).eulerAngles.y;

            for (int row = 0; row < rows; row++)
            {
                int across = 22 - row * 2;
                for (int a = 0; a < across; a++)
                {
                    // Gaps. A completely full stand reads as wallpaper, and a completely random one
                    // reads as noise; leaving roughly one seat in seven empty reads as a crowd.
                    if (rng.NextDouble() < 0.14) continue;

                    float u = (a - (across - 1) * 0.5f) * alongStep
                            + (float)(rng.NextDouble() - 0.5) * 0.22f;

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

        /// <summary>The instanced crowd itself, on the venue's own spectator meshes and material.</summary>
        static void InstallCrowd(Transform parent,
                                 System.Collections.Generic.List<SpectatorCrowd.Seat> seats)
        {
            var crowd = parent.gameObject.AddComponent<SpectatorCrowd>();

            var species = new System.Collections.Generic.List<Mesh>();
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
                    var s = seats[i];
                    s.species = s.species % species.Count;
                    seats[i] = s;
                }
            }
            else
            {
                // Generated blobs rather than empty benches. Still a crowd, and still reacts.
                crowd.crowdMaterial = Mat("M_Crowd");
            }

            crowd.seats = seats.ToArray();
            crowd.EnsurePlaceholderMeshes();
            Debug.Log($"[Rally] crowd: {seats.Count} spectators across four banks, " +
                      $"{species.Count} species.");
        }

        // ------------------------------------------------------------------ machines

        static GameObject BuildMower(Transform quadrant, in RallyArena.Slot slot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mower.prefab");
            if (prefab == null)
            {
                Debug.LogWarning("[Rally] Mower.prefab is missing; the arena has no machines in it.");
                return null;
            }

            // The existing prefab, four times over. A second machine authored here would drift from
            // the one the player has spent the round driving, and the whole premise is that all four
            // competitors are on the identical model with the identical physics.
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = $"Mower — {slot.contestant}";
            go.transform.SetParent(quadrant, false);
            go.transform.SetPositionAndRotation(slot.SpawnPosition, slot.SpawnRotation);

            var controller = go.GetComponent<MowerController>();
            if (controller != null && slot.isPlayer)
                // Dust, skids and blade spray, on the player's machine only. Four sets of mower VFX
                // in a WebGL frame buys three of them being seen from forty metres away.
                DuckVFXBuilder.Build(go, controller);

            if (slot.isPlayer)
            {
                if (go.GetComponent<DuckRider>() == null) go.AddComponent<DuckRider>();
                return go;
            }

            // The rivals arrive in their own colours and with their own driver in the seat, so three
            // opponents are three CONTESTANTS rather than three copies of the player's machine.
            Repaint(go, slot);
            SeatRival(go.transform, slot);
            return go;
        }

        static void Repaint(GameObject mower, in RallyArena.Slot slot)
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
        /// Put the contestant in the seat — and take the duck out of it first.
        ///
        /// Mower.prefab ships WITH the player's duck already sitting on it, under
        /// VisualPivot/Duck. Adding a rival on top of that seated three opponents inside the duck
        /// and, because the rival was parented to the mower ROOT rather than to VisualPivot, it did
        /// not inherit the visual pivot's offset either — so the rivals sat above their own machines,
        /// hovering, with a duck's head coming out of them.
        ///
        /// The fix is both halves: hide the duck, and take the rival's local transform FROM the
        /// duck's rather than from a constant. The seat offset is a property of the model, and the
        /// model already states it.
        /// </summary>
        static void SeatRival(Transform mower, in RallyArena.Slot slot)
        {
            var pivot = mower.Find("VisualPivot");
            var duck = pivot != null ? pivot.Find("Duck") : null;

            // Off, not deleted. The prefab connection stays intact, and a scene somebody opens can
            // still be told what used to be there.
            if (duck != null) duck.gameObject.SetActive(false);

            string blenderName = char.ToUpper(slot.contestant[0]) + slot.contestant.Substring(1).ToLower();
            var mesh = DuckAssetLibrary.GetCombined("Rivals.fbx", $"{blenderName}_Root",
                                                    $"Rival_{blenderName}");
            if (mesh == null)
            {
                // No rival model is not a reason to ship an empty seat — put the duck back.
                if (duck != null) duck.gameObject.SetActive(true);
                Debug.LogWarning($"[Rally] no rival mesh for {slot.contestant}; leaving the duck in the seat.");
                return;
            }

            var go = new GameObject("Driver");
            go.transform.SetParent(pivot != null ? pivot : mower, false);

            // The duck's POSITION, but never its rotation or scale.
            //
            // GetCombined expresses each piece relative to the FBX root's parent, which keeps the
            // importer's axis conversion baked into the vertices — so a rival mesh is already the
            // right way up and wants an IDENTITY rotation. Copying the duck's local rotation on top
            // of that is a second conversion applied to an already-converted mesh, and it laid every
            // contestant on their back staring at the sky. DuckVenueBuilder seats the same meshes the
            // same way and carries the same warning; this is that warning being re-learned.
            //
            // The seat height still comes from the duck, because that is measured against this exact
            // model and is not worth re-deriving.
            go.transform.localPosition = duck != null ? duck.localPosition
                                                      : DuckModelIntegration.DuckSeatOffset;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat("M_Rivals") ?? Mat("M_PropsAuthored");
            mr.shadowCastingMode = ShadowCastingMode.On;
        }

        static CameraDirector BuildCameraRig(GameObject playerMower)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";

            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.12f;
            cam.farClipPlane = 320f;
            cam.allowHDR = false;
            if (go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            var director = go.AddComponent<CameraDirector>();
            if (playerMower != null)
            {
                director.target = playerMower.transform;
                director.mower = playerMower.GetComponent<MowerController>();
            }
            // The arena is wider than the lawn and the action happens further from the machine, so
            // the shot opens up. Still the same chase camera with the same language — a lens choice,
            // not a different rig.
            //
            // HIGHER rather than further back, and that is the whole correction. The first pass ran a
            // long low boom, and a long low boom on a nine-metre strip puts the camera behind the
            // player's own fence: a third of every frame was the duck's own flowerbeds, seen from
            // behind, while the match happened in the top half. Lifting the lens and letting it look
            // down puts the mower on the lower third and gives the arena — which is the only part of
            // the picture anything ever happens in — the rest of the screen.
            // Speed does NOT move the camera here, and every "AtSpeed" value below is deliberately
            // equal to its base. On the lawn a boom that lengthens with speed sells how fast the
            // mower is going. In the arena the mower spends the whole match accelerating across a
            // nine-metre strip and braking again, so the same rule turns into a lens that pumps in
            // and out several times a second and never settles. A defender judging where to be needs
            // a fixed frame more than they need a speed cue.
            director.chaseDistance = 5.6f;
            director.chaseDistanceAtSpeed = 5.6f;
            director.chaseHeight = 4.0f;
            director.chaseHeightAtSpeed = 4.0f;
            director.lookHeight = 1.0f;
            // The aim leads eight metres into the pitch. Height alone was not the answer — a high
            // camera aimed AT the mower just gets a steeper picture of the same dirt. See lookForward.
            director.lookForward = 8f;
            // One focal length. Same reason as the boom above — a lens that widens with speed is a
            // lens that is always moving on a pitch this size.
            director.fovBase = 58f;
            director.fovAtSpeed = 58f;
            director.fovBoostKick = 0f;
            // A longer boom at full rally energy than the lawn uses: three geese loose need the room,
            // and the arena has nothing for the camera to back into.
            director.defencePullBack = 3.4f;
            director.defenceLift = 1.6f;
            director.defenceFovWiden = 8f;

            go.AddComponent<AudioListener>();
            return director;
        }

        // ------------------------------------------------------------------ HUD

        static readonly Color Cream = new Color(1.00f, 0.97f, 0.88f);
        static readonly Color Gold = new Color(1.00f, 0.85f, 0.45f);
        static readonly Color Alarm = new Color(1.00f, 0.42f, 0.32f);

        /// <summary>
        /// Bake the rally's canvas out of the ROUND'S OWN UI KIT.
        ///
        /// This is the whole answer to "it looks nothing like stage one". The mowing HUD is not a
        /// set of colours that can be copied — it is a set of painted assets: a dark card with a
        /// worn edge, a ribbon, a timer ring, a segmented bar, five rosettes. A HUD assembled from
        /// untextured rectangles will read as a different game no matter how carefully its palette
        /// matches, because the palette was never what the player was recognising.
        ///
        /// So it is built here rather than at runtime, for the same reason the arena is: sprites live
        /// in the AssetDatabase, which only exists in the editor — and, more usefully, a baked canvas
        /// is one somebody can open, select and drag without pressing play.
        /// </summary>
        static void BuildHud(RallyHud hud, Camera view)
        {
            var canvasGO = new GameObject("~ Rally HUD",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();

            // SCREEN SPACE - CAMERA, not Overlay, and the reason is reviewability rather than looks.
            //
            // An Overlay canvas is composited after every camera has drawn, so it is invisible to
            // anything that renders a camera into a RenderTexture — which is how every capture tool
            // in this project works. The result was a full review loop reporting a game with no HUD
            // on it, and a round of debugging spent hunting a HUD that had never been missing.
            //
            // Rendered through the camera, the same contact sheets that judge the arena now judge the
            // HUD over it, which is the only way to find out whether it is legible against grass.
            canvas.renderMode = view != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = view;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 40;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Match HEIGHT, not the average. The arena is played on wide monitors and phones alike,
            // and matching on width shrinks everything on a tall screen until the cards clip off the
            // top — which is what "잘림" was. Height-matched, the layout keeps its vertical spacing
            // and simply gains or loses side margin.
            scaler.matchWidthOrHeight = 1f;
            var root = (RectTransform)canvasGO.transform;

            BuildClock(hud, root);
            BuildCards(hud, root);
            BuildTicker(hud, root);
            BuildResult(hud, root);
            BuildArrows(hud, root);
        }

        /// <summary>
        /// The round's timer. Not a version of it — THE one, copied out of BuildRoundHud.
        ///
        /// Same fractional slot, same ring sprite untinted, same fill mode and origin, same 54 pt
        /// bold cream text in the same sub-rect. It had been "adapted" — a dark card added behind it,
        /// the ring tinted gold, the type resized and unbolded — and every one of those was a change
        /// with no reason behind it. The clock is the element a player looks at most and recognises
        /// fastest; there is nothing about the rally that makes the round's clock wrong for it.
        /// </summary>
        static void BuildClock(RallyHud hud, RectTransform root)
        {
            var timer = Frac("Timer", root, 0.435f, 0.735f, 0.565f, 0.985f);

            var ring = Frac("Ring", timer, 0f, 0f, 1f, 1f);
            var ringImg = DuckUIBuilder.AddImage(ring, DuckUIBuilder.Spr("timer_ring_256"), Color.white);
            ringImg.type = Image.Type.Filled;
            ringImg.fillMethod = Image.FillMethod.Radial360;
            ringImg.fillOrigin = (int)Image.Origin360.Top;
            ringImg.fillClockwise = false;
            hud.timerRing = ringImg;

            var timerText = Frac("TimerText", timer, 0.12f, 0.3f, 0.88f, 0.72f);
            hud.timerText = DuckUIBuilder.AddText(timerText, "1:18", 54f, TextAlignmentOptions.Center,
                                                  Cream, 0.26f, false);
            hud.timerText.fontStyle = FontStyles.Bold;
        }

        /// <summary>Fractional rect, the same primitive DuckUIBuilder lays the round's HUD out with.</summary>
        static RectTransform Frac(string name, Transform parent, float xMin, float yMin,
                                  float xMax, float yMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// Four contestant cards down the left.
        ///
        /// In arena slot order rather than by score, so a card never jumps position under the
        /// player's eye mid-glance — the bar carries the ranking, and a row that moves as well is a
        /// row that has to be re-found every time it is read.
        /// </summary>
        static void BuildCards(RallyHud hud, RectTransform root)
        {
            const float w = 336f, h = 82f, gap = 10f;
            var list = new List<RallyHud.Card>(RallyArena.Count);

            for (int i = 0; i < RallyArena.Count; i++)
            {
                var slot = RallyArena.Get(i);
                var card = new RallyHud.Card();

                var rt = UINode($"Card {i} {slot.contestant}", root, new Vector2(0f, 1f),
                                new Vector2(28f + w * 0.5f, -(28f + h * 0.5f + i * (h + gap))),
                                new Vector2(w, h));
                card.root = rt;

                // The player's card is the LIGHT one. Four identical plates make the player hunt for
                // their own row every time they check the score. Both colourways are the same
                // painting and measure identically, so the field below is the same either way.
                string plateSprite = slot.isPlayer ? DuckUIBuilder.CardLight : DuckUIBuilder.CardDark;

                // The alarm wash sits over the plate and under everything else, driven to full only
                // while a goose is actually inbound at this quadrant.
                var glow = UINode("Alarm", rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
                card.alarmGlow = DuckUIBuilder.AddImage(glow, DuckUIBuilder.Spr("panel_card_256"),
                                                        new Color(Alarm.r, Alarm.g, Alarm.b, 0f),
                                                        Image.Type.Sliced);

                // Laid out in FRACTIONS of the card's WRITABLE FIELD, the way the round's HUD lays
                // out everything.
                //
                // The pixel-offset version put the percentage's centre 26 px in from the card's right
                // edge with a 104 px box around it, so half the box — and the "100" in it — hung
                // outside the plate. Fractions cannot make that mistake: 0.97 is inside the card by
                // definition, at any card size and any aspect ratio.
                //
                // Everything used to be held inside 0.06..0.93 across and 0.17..0.84 up, chosen by
                // somebody who had correctly diagnosed that panel_card_dark_256 is a PAINTED card
                // with a decorative rule inset from its edge — and it still did not clear it. A card
                // 336x82 turns 0.06 into 20 px against a 30 px rule and 0.17 into 14 px against 22.
                // That is the whole argument for CardArt: the diagnosis was right, the measurement
                // was a guess, and a fraction cannot hold a fixed number of pixels anyway.
                var field = DuckUIBuilder.Plate(rt, plateSprite, out var plateImg);
                card.plate = plateImg;

                var swatch = Frac("Livery", field, 0f, 0.06f, 0.115f, 0.94f);
                DuckUIBuilder.AddImage(swatch, DuckUIBuilder.Spr("progress_bar_fill_256"),
                                       slot.livery, Image.Type.Sliced);

                // BOLD, and given room. The round's HUD bolds every label that has to be read at a
                // glance, and AddText auto-sizes DOWN to fit its box — so a name in a tight rect was
                // being quietly shrunk to about half the size asked for, which is why these were
                // unreadable rather than merely small.
                var name = Frac($"Name {i}", field, 0.10f, 0.50f, 0.68f, 1f);
                card.name = DuckUIBuilder.AddText(name, slot.isPlayer ? "YOU" : slot.contestant, 30f,
                                                  TextAlignmentOptions.Left,
                                                  slot.isPlayer ? Gold : Cream, 0.30f, false);
                card.name.fontStyle = FontStyles.Bold;

                var barBg = Frac("BarBg", field, 0.10f, 0.06f, 0.68f, 0.44f);
                DuckUIBuilder.AddImage(barBg, DuckUIBuilder.Spr("progress_bar_bg_256"), Color.white,
                                       Image.Type.Sliced);
                var barFill = Frac("BarFill", barBg, 0f, 0f, 1f, 1f);
                var fill = DuckUIBuilder.AddImage(barFill, DuckUIBuilder.Spr("progress_bar_fill_256"),
                                                  slot.livery, Image.Type.Filled);
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = 0;
                fill.fillAmount = 1f;
                card.fill = fill;

                // The horn meter: a thin vertical bar down the left edge of the card, filling
                // upward as the horn recharges. On the LEFT because that is the edge the eye
                // already goes to for this card — the livery swatch is there — and a meter beside
                // an owner's colour needs no label to say whose it is.
                //
                // Deliberately still measured against the CARD and not its field, and this is the
                // one place in the sweep where that is the right answer: the meter is edge
                // furniture, drawn along the frame on purpose, not content that has to be read off
                // the writable part. CardArt is a rule about what may be printed on the card, not a
                // rule that everything must live inside it.
                var hornBg = Frac("HornBg", rt, 0.020f, 0.20f, 0.048f, 0.80f);
                DuckUIBuilder.AddImage(hornBg, DuckUIBuilder.Spr("progress_bar_bg_256"),
                                       new Color(1f, 1f, 1f, 0.5f), Image.Type.Sliced);
                var hornFill = Frac("HornFill", hornBg, 0f, 0f, 1f, 1f);
                var horn = DuckUIBuilder.AddImage(hornFill, DuckUIBuilder.Spr("progress_bar_fill_256"),
                                                  slot.livery, Image.Type.Filled);
                horn.fillMethod = Image.FillMethod.Vertical;
                horn.fillOrigin = 0;
                horn.fillAmount = 1f;
                card.hornMeter = horn;

                var pct = Frac($"Pct {i}", field, 0.68f, 0.02f, 1f, 0.98f);
                card.percent = DuckUIBuilder.AddText(pct, "100", 44f, TextAlignmentOptions.Right,
                                                     Cream, 0.30f, false);
                card.percent.fontStyle = FontStyles.Bold;

                list.Add(card);
            }
            hud.cards = list.ToArray();
        }

        static void BuildTicker(RallyHud hud, RectTransform root)
        {
            // Under the timer's slot, not overlapping it. The timer occupies y 0.735..0.985, so this
            // sits directly beneath and the two never fight for the same pixels at any aspect ratio.
            var rt = Frac("Ticker", root, 0.30f, 0.665f, 0.70f, 0.725f);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            hud.tickerGroup = group;
            var field = DuckUIBuilder.Plate(rt, DuckUIBuilder.CardDark);

            // Fills the field rather than sitting in a hand-sized 740x52 box in the middle of it.
            // That box was 14 px in from the sides of a 768 px plate whose rule is at 30, and 6 px
            // from the top and bottom of a 65 px one whose rule — shrunk with the slice at that
            // height — is at 14. Every edge of it was on the paint.
            var t = DuckUIBuilder.Frac("Text", field, 0f, 0f, 1f, 1f);
            hud.tickerText = DuckUIBuilder.AddText(t, "", 34f, TextAlignmentOptions.Center,
                                                   Cream, 0.26f, false);
        }

        /// <summary>The card at the horn. The mode previously ended on an ordinary gameplay frame.</summary>
        static void BuildResult(RallyHud hud, RectTransform root)
        {
            // Along the BOTTOM, not across the middle. In the middle it covered the bench entirely —
            // the beat is three judges raising the winner's face and the card announcing it was
            // parked directly in front of them. A results panel that hides the result is worse than
            // no panel at all.
            var rt = Frac("Result", root, 0.18f, 0.045f, 0.82f, 0.235f);
            hud.resultGroup = rt.gameObject.AddComponent<CanvasGroup>();
            var field = DuckUIBuilder.Plate(rt, DuckUIBuilder.CardDark);

            var ros = Frac("Rosette", field, 0f, 0f, 0.111f, 1f);
            hud.resultRosette = DuckUIBuilder.AddImage(ros, DuckUIBuilder.Spr("rosette_S_256"),
                                                       Color.white);
            hud.rosetteByPlace = new[]
            {
                DuckUIBuilder.Spr("rosette_S_256"), DuckUIBuilder.Spr("rosette_A_256"),
                DuckUIBuilder.Spr("rosette_B_256"), DuckUIBuilder.Spr("rosette_C_256"),
            };

            // Ends before the award begins, or a long placing line and a two-digit award overlap.
            var placing = Frac("Placing", field, 0.132f, 0.417f, 0.734f, 1f);
            hud.resultPlacing = DuckUIBuilder.AddText(placing, "", 46f, TextAlignmentOptions.Left,
                                                      Gold, 0.30f, false);

            // The points, on the right where the eye finishes reading. Big, because it is the line
            // the whole card exists to deliver.
            //
            // It ran to 0.985 of the card once, which put right-aligned text on the decorative rule
            // so the number read as though it were falling off the plate. That was corrected to
            // 0.94 by eye — and 0.94 of this card is 74 px in from the edge against a rule at 30, so
            // the guess overshot by more than it had undershot and left the number stranded in the
            // middle. It goes to the field's own edge now, which is the number the artwork actually
            // draws, at 38.
            var award = Frac("Award", field, 0.756f, 0.34f, 1f, 1f);
            hud.resultAward = DuckUIBuilder.AddText(award, "", 58f, TextAlignmentOptions.Right,
                                                    Gold, 0.32f, false);
            hud.resultAward.fontStyle = FontStyles.Bold;

            var detail = Frac("Detail", field, 0.132f, 0f, 0.809f, 0.387f);
            hud.resultDetail = DuckUIBuilder.AddText(detail, "", 24f, TextAlignmentOptions.Left,
                                                     Cream, 0.24f, false);
        }

        static void BuildArrows(RallyHud hud, RectTransform root)
        {
            var made = new List<Graphic>(3);
            for (int i = 0; i < 3; i++)
            {
                var rt = UINode($"Threat {i}", root, new Vector2(0.5f, 0.5f), Vector2.zero,
                                new Vector2(130f, 130f));
                var t = DuckUIBuilder.AddText(rt, "▲", 84f, TextAlignmentOptions.Center,
                                              Alarm, 0.34f, false);
                t.enabled = false;
                made.Add(t);
            }
            hud.arrows = made.ToArray();
        }

        static RectTransform UINode(string name, Transform parent, Vector2 anchor, Vector2 pos,
                                    Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        // ------------------------------------------------------------------ materials, on disk

        /// <summary>
        /// A ground surface on the project's own noise-blend shader rather than a flat colour.
        ///
        /// This is the answer to "flat single colours look like a prototype", and it is not a new
        /// idea — the venue hit exactly this and solved it: Duck/GrassPlain is a two-colour blend
        /// driven by noise at THREE scales (metre-scale grain, ten-metre mottle, hundred-metre
        /// patches), and the venue uses it for turf AND for bare earth because earth needs the same
        /// machinery. Flat lit colours were what made the meadow "read as untextured placeholder"
        /// next to it, and the rally's ground, dirt strips and soil were making exactly that mistake
        /// over the largest areas of every frame.
        ///
        /// Grain is the term that matters at rally range: the player drives past these surfaces at
        /// ten metres a second with the camera four metres up, so the metre-scale speckle is what
        /// stops the floor looking like paper.
        /// </summary>
        static Material Ground(string name, string baseHex, string tipHex, string darkHex, string dryHex,
                               float grain = 0.14f, float stripe = 0.05f)
        {
            var m = DuckSceneBuilder.EnsureMaterial(name, "Duck/GrassPlain");
            if (m == null) return DuckSceneBuilder.EnsureLit(name + "_flat", baseHex);

            m.SetColor("_UncutBase", DuckSceneBuilder.HexL(baseHex));
            m.SetColor("_UncutTip", DuckSceneBuilder.HexL(tipHex));
            m.SetColor("_PatchDark", DuckSceneBuilder.HexL(darkHex));
            m.SetColor("_DryTint", DuckSceneBuilder.HexL(dryHex));
            // Tighter and stronger than the venue's settings, and that is a scale decision rather
            // than a taste one. The venue is looked at from ninety metres up, so its noise is sized
            // in tens of metres; the rally is driven past at eye level, where a sixteen-metre mottle
            // cell covers the whole visible ground and reads as flat colour. Halving the cell size
            // and pushing the grain is what actually puts variation in front of the camera.
            m.SetFloat("_MottleScale", 0.14f);
            m.SetFloat("_MottleAmount", 0.85f);
            m.SetFloat("_PatchScale", 0.030f);
            m.SetFloat("_PatchAmount", 0.62f);
            m.SetFloat("_DryAmount", 0.45f);
            m.SetFloat("_GrainScale", 2.4f);
            m.SetFloat("_GrainAmount", grain);
            m.SetFloat("_Wrap", 0.36f);
            m.SetFloat("_OldStripe", stripe);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material GrassMat() => Mat("M_GrassGround")
            ?? Ground("M_RallyGrass", "#3E8A30", "#6DB945", "#36752F", "#9FB552");
        // Separated enough to be seen, close enough to still be one lawn. The first pass had four
        // percent between them and the mown pattern was simply not there on screen.
        // The two mown bands differ by a step of value, not a step of hue, and both carry the full
        // noise blend — so the alternation reads as a mower having been over the grass rather than as
        // two flat greens painted in rings.
        // The two mown-band materials that used to live here are GONE, not merely unused. They were
        // left behind when the ring pattern was removed and the arena went over to round one's own
        // lawn, and dead material factories are not free: a reviewer looking for why stage two's
        // grass reads differently from stage one's found these, matched their mottle against
        // M_GrassGround's, and reported a cause that no longer had anything to do with the screen.

        // EARTH WITH STONES IN IT — a farm track, not a beach.
        //
        // Two wrong answers got here. It started as dark chocolate mud, which was the heaviest mass
        // in every frame and made the arena look ploughed; the correction overshot to pale sand,
        // which reads as a bunker. What a country-show apron actually is, is warm brown earth packed
        // hard with grit showing through it.
        //
        // The stones come from the GRAIN term, pushed high. That is the metre-and-under noise
        // octave, so at the range a mower drives past it breaks into flecks rather than washing —
        // which is what embedded gravel looks like. The larger octaves stay moderate so the strip
        // still reads as one surface.
        // Wide value spread between base and tip. A five-percent gap is invisible under a bright sun
        // and is exactly what made these read as one flat colour; earth in daylight has trodden
        // hollows and dry raised crust, and those are far apart.
        static Material DirtMat()
            => Ground("M_RallyDirt", "#7E6242", "#B59873", "#63492E", "#CBB48C", 0.52f, 0.03f);
        static Material RutMat()
            => Ground("M_RallyRut", "#6B5133", "#997B55", "#523B23", "#AD9269", 0.50f, 0.03f);
        static Material DirtDarkMat()
            => Ground("M_RallyDirtEdge", "#6A5136", "#8F7350", "#4E381F", "#A08863", 0.54f, 0.02f);
        /// <summary>
        /// The ground beyond the lawn. A GROUND shader, and that is the whole of the fix.
        ///
        /// The first version of this plane fell through to EnsureLit, which hands back a Duck/Prop
        /// material — and Duck/Prop carries a rim term, `pow(1 - dot(N, V), _RimPower)`. On a prop
        /// that is a lit edge. On a four-hundred-metre plane whose normal is straight up, the view
        /// vector goes flat as it reaches the horizon, so dot(N, V) falls to nothing and the rim
        /// goes to FULL across the far half of the ground — and the band it makes slides about as
        /// soon as the camera tilts. That is why the meadow changed colour when you turned. A rim
        /// light is a property of an object with an edge; ground has no edge, it has a horizon.
        ///
        /// Duck/GrassPlain has no view-dependent term at all: albedo times light, and nothing else.
        /// It is also the meadow the venue itself wears, authored to abut a lawn on these exact
        /// ambient terms — any mismatch in the fill draws a visible rectangle around the pitch.
        /// </summary>
        static Material SurroundMat()
        {
            var m = Mat("M_Apron");
            if (m != null) return m;
            m = DuckSceneBuilder.EnsureMaterial("M_RallySurround", "Duck/GrassPlain");
            return m ?? DuckSceneBuilder.EnsureLit("M_RallySurroundFallback", "#4C7A32");
        }

        static Material StoneMat()
        {
            var m = DuckSceneBuilder.EnsureLit("M_RallyStone", "#8C8880", 0.14f);
            if (m != null && m.HasProperty("_VertexColorAmount")) m.SetFloat("_VertexColorAmount", 1f);
            if (m != null) m.enableInstancing = true;
            return m;
        }

        static Material SoilMat()
            => Ground("M_RallySoil", "#54402B", "#8E6F4C", "#3E2C1C", "#A98D62", 0.36f, 0.02f);
        static Material FenceMat() => DuckSceneBuilder.EnsureLit("M_RallyPicket", "#DCD8CE");
        static Material TrampledMat() => DuckSceneBuilder.EnsureLit("M_RallyTrampled", "#4C3B2E");
        static Material PoleMat() => DuckSceneBuilder.EnsureLit("M_RallyPole", "#C8C2B4");
        static Material BarrierMat() => DuckSceneBuilder.EnsureLit("M_RallyBarrier", "#D8D2C4");
        static Material BarrierAltMat() => DuckSceneBuilder.EnsureLit("M_RallyBarrierAlt", "#B24A3C");
        static Material StandMat() => DuckSceneBuilder.EnsureLit("M_RallyStand", "#9A9284");
        static Material StandAltMat() => DuckSceneBuilder.EnsureLit("M_RallyStandAlt", "#857D70");

        static Material LiveryMat(in RallyArena.Slot slot)
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(slot.livery);
            return DuckSceneBuilder.EnsureLit($"M_RallyLivery_{slot.contestant}", hex, 0.18f);
        }

        static Material BloomMat()
            => Mat("M_FoliageAuthored") ?? Mat("M_PropsAuthored")
            ?? DuckSceneBuilder.EnsureLit("M_RallyBloom", "#D15C70");

        static Mesh BedMesh() => DuckAssetLibrary.GetCombined("Foliage.fbx", "Flowerbed", "RallyFlowerbed");

        /// <summary>
        /// One of the authored rally props, by name.
        ///
        /// Returns null when the FBX has not been built, and every caller has a box fallback for
        /// exactly that case. Not because primitives are acceptable — they are the thing this whole
        /// pass exists to remove — but because a builder that throws when an artist is halfway
        /// through re-exporting leaves nobody with a scene to look at.
        /// </summary>
        static Mesh Prop(string name)
            => DuckAssetLibrary.GetCombined("RallyProps.fbx", name, "Rally_" + name);

        /// <summary>The material every authored prop wears: white base, vertex colours carrying the paint.</summary>
        static Material PropMat()
            => Mat("M_RallyProps") ?? Mat("M_PropsAuthored") ?? Mat("M_FoliageAuthored")
            ?? DuckSceneBuilder.EnsureLit("M_RallyProps", "#FFFFFF");

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

        // ------------------------------------------------------------------ helpers

        static Transform Box(Transform parent, string name, Material mat, Vector3 scale,
                             Vector3 position, Quaternion rotation, bool keepCollider, bool castShadows)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            // Colliders come off everything but the ground. Beds, pickets and barriers with colliders
            // would turn the arena into an obstacle course the mowers wedge in, and every competitor
            // has to be drivable at every moment.
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
