using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Builds the championship ground around the player's lawn: the rival plots, their judging
    /// stations and spectator banks, the paths that join them and the scoreboard on the shared
    /// corner.
    ///
    /// Everything is placed from <see cref="Venue"/>, so the layout exists in exactly one place
    /// and the runtime and the geometry can never drift apart. Each rival plot is built the same
    /// way the player's is — lawn, apron, hedge, station south, stand west — because a competition
    /// ground is standardised; the interest comes from what the contestants do on it.
    /// </summary>
    public static class DuckVenueBuilder
    {
        const string MatDir = "Assets/Materials";
        const string MeshDir = "Assets/Meshes/Generated";

        static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        static System.Random _rng;
        static float Rand => (float)_rng.NextDouble();
        static float Range(float a, float b) => a + (b - a) * Rand;

        /// <summary>Builds every plot except the player's, plus the plaza. Returns the rival components.</summary>
        public static RivalContestant[] Build(Transform worldRoot)
        {
            _rng = new System.Random(90210);

            var venue = new GameObject("~ Venue").transform;
            venue.SetParent(worldRoot, false);

            var rivals = new System.Collections.Generic.List<RivalContestant>();

            for (int i = 0; i < Venue.Plots.Length; i++)
            {
                var spec = Venue.Plots[i];
                var plot = new GameObject($"Plot_{spec.contestant}").transform;
                plot.SetParent(venue, false);

                // The player's plot already has its lawn, fence, bench and stands from the original
                // arena build. It still gets the shared furniture — path spur and marker board —
                // so it reads as one plot among four rather than as the level with extras bolted on.
                if (!spec.isPlayer)
                {
                    BuildLawn(plot, spec);
                    BuildApronAndHedge(plot, spec);
                    rivals.Add(BuildContestant(plot, spec));
                }

                BuildStation(plot, spec);
                BuildStand(plot, spec);
                BuildMarkerBoard(plot, spec);
                BuildPathSpur(plot, spec);
            }

            BuildPlaza(venue);

            return rivals.ToArray();
        }

        // ------------------------------------------------------------------ lawn

        static void BuildLawn(Transform plot, PlotSpec spec)
        {
            // Subdivided rather than a single quad so vertex fog and the wrap term interpolate
            // across it instead of over four corners.
            var mesh = DuckMeshLibrary.Quad(spec.size, spec.size, 24);
            var go = Spawn(plot, "Lawn", mesh, Mat("M_GrassGround"),
                           new Vector3(spec.centre.x, 0.005f, spec.centre.y), Quaternion.identity, false);
            go.layer = LayerMask.NameToLayer("Ground");

            // No collider and no blade layer. Nothing ever drives on a rival's lawn, and the blade
            // geometry costs more than everything else in the venue put together — at ninety-six
            // metres the ground shader alone is indistinguishable.
            var lawn = go.AddComponent<RivalLawn>();
            lawn.plotCentre = spec.centre;
            lawn.plotSize = spec.size;
        }

        static void BuildApronAndHedge(Transform plot, PlotSpec spec)
        {
            float half = spec.Half;
            var origin = new Vector3(spec.centre.x, 0f, spec.centre.y);

            var apron = DuckMeshLibrary.Persist(
                DuckPrimitives.SquareRing(half, half + 7f, 16, 1.2f, 7 + (int)spec.centre.x),
                $"Apron_{spec.contestant}");
            Spawn(plot, "Apron", apron, Mat("M_Apron"), origin, Quaternion.identity, false);

            // The same white post-and-rail fence the player's plot uses. A hedge was the wrong
            // call: it reads as landscaping rather than as a marked-out competition plot, and four
            // hedged squares looked like allotments instead of a championship ground.
            var authoredPost = DuckAssetLibrary.GetCombined("Props.fbx", "FencePost", "FencePost");
            var authoredRail = DuckAssetLibrary.GetCombined("Props.fbx", "FenceRail", "FenceRail");
            var white = authoredPost != null ? Mat("M_PropsAuthored") : Mat("M_FenceWhite");

            var post = authoredPost ?? DuckMeshLibrary.Persist(
                DuckPrimitives.ChamferBox(new Vector3(0.055f, 0.525f, 0.045f), 0.018f), "PlotFencePost");
            var rail = authoredRail ?? DuckMeshLibrary.Persist(
                DuckPrimitives.ChamferBox(new Vector3(1.1f, 0.05f, 0.03f), 0.014f), "PlotFenceRail");
            float postLift = authoredPost != null ? 0f : 0.525f;

            float ring = half + 3.2f;
            const float spacing = 2.2f;
            int perSide = Mathf.RoundToInt(ring * 2f / spacing);

            for (int side = 0; side < 4; side++)
            {
                for (int i = 0; i <= perSide; i++)
                {
                    float t = -ring + i * spacing;
                    if (t > ring) break;
                    // A gate on the south side, facing this plot's own judging station.
                    if (side == 1 && Mathf.Abs(t) < 2.6f) continue;

                    Vector3 p = side switch
                    {
                        0 => new Vector3(t, 0f, ring),
                        1 => new Vector3(t, 0f, -ring),
                        2 => new Vector3(ring, 0f, t),
                        _ => new Vector3(-ring, 0f, t),
                    };
                    float yaw = side < 2 ? 0f : 90f;
                    // Every post leans a little; a perfectly straight fence reads as CAD.
                    var rot = Quaternion.Euler(Range(-2.5f, 2.5f), yaw + Range(-3f, 3f), Range(-2.5f, 2.5f));

                    Spawn(plot, $"Post_{side}_{i}", post, white,
                          origin + p + Vector3.up * postLift, rot, false);

                    if (i < perSide && !(side == 1 && Mathf.Abs(t + spacing * 0.5f) < 2.6f))
                    {
                        Vector3 mid = p + (side < 2 ? new Vector3(spacing * 0.5f, 0f, 0f)
                                                    : new Vector3(0f, 0f, spacing * 0.5f));
                        foreach (float railY in new[] { 0.34f, 0.72f })
                            Spawn(plot, $"Rail_{side}_{i}_{railY:0.00}", rail, white,
                                  origin + mid + Vector3.up * railY,
                                  Quaternion.Euler(0f, yaw + Range(-2f, 2f), Range(-1.5f, 1.5f)), false);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ contestant

        static RivalContestant BuildContestant(Transform plot, PlotSpec spec)
        {
            var go = new GameObject($"Rival_{spec.contestant}");
            go.transform.SetParent(plot, false);

            var rc = go.AddComponent<RivalContestant>();
            rc.displayName = spec.contestant;
            rc.species = spec.species;
            rc.liveryColour = spec.livery;
            rc.plotCentre = spec.centre;
            rc.plotSize = spec.size;
            rc.shapeRadius = spec.Half * 0.80f;
            rc.skill = spec.skill;
            rc.flair = spec.flair;
            rc.stampShader = Shader.Find("Duck/CutStamp");

            // The real mower, in this contestant's colours.
            //
            // It was a box proxy on the grounds that a rival is only ever seen from a distance, but
            // that was the wrong trade: the tour camera does come down to sixty metres, and a
            // championship where three of the four machines are cardboard boxes undoes the point of
            // having modelled the thing. It is one combined mesh and one draw call, so the honest
            // version costs no more than the fake one did.
            var visual = new GameObject("Mower").transform;
            visual.SetParent(go.transform, false);
            visual.localPosition = new Vector3(spec.centre.x, 0f, spec.centre.y);

            var mowerMesh = DuckAssetLibrary.GetCombined("Mower.fbx", "Mower", "RivalMower")
                         ?? DuckAssetLibrary.GetCombined("Mower.fbx", "Mower_Root", "RivalMower");

            if (mowerMesh != null)
            {
                Spawn(visual, "Body", mowerMesh, LiveryMower(spec), Vector3.zero, Quaternion.identity, true);
            }
            else
            {
                Debug.LogWarning("[Duck] Mower.fbx root not found for rival mowers; using a proxy.");
                var body = DuckMeshLibrary.Persist(
                    DuckPrimitives.ChamferBox(new Vector3(0.42f, 0.20f, 0.66f), 0.10f), "RivalMowerBody");
                Spawn(visual, "Body", body, LiveryMower(spec), new Vector3(0f, 0.34f, 0f), Quaternion.identity, true);
            }

            // The contestant, in the seat. Same offset the player's duck uses, because it is the
            // same mower — that number was measured against this model and is not worth re-deriving.
            string blenderName = char.ToUpper(spec.contestant[0]) + spec.contestant.Substring(1).ToLower();
            var driverMesh = DuckAssetLibrary.GetCombined("Rivals.fbx", $"{blenderName}_Root",
                                                          $"Rival_{blenderName}");
            if (driverMesh != null)
            {
                // No conversion rotation: GetCombined expresses each piece relative to the FBX
                // root's PARENT, which keeps the importer's axis conversion baked in. Adding the
                // usual -90 about X on top laid every contestant flat on their back.
                Spawn(visual, "Driver", driverMesh, Mat("M_Rivals") ?? Mat("M_PropsAuthored"),
                      DuckModelIntegration.DuckSeatOffset, Quaternion.identity, true);
            }

            rc.mowerVisual = visual;
            // No separate blade spinner: the authored mower is one combined mesh, and a spinning
            // deck is not readable at any distance a rival is ever seen from.
            rc.bladeSpinner = null;
            return rc;
        }

        /// <summary>
        /// The player's mower material in a contestant's colours.
        ///
        /// The model carries its paint in vertex colours, so the livery goes through
        /// <c>_InstanceColor</c>, which multiplies over the top: the machine keeps every panel,
        /// shadow and highlight the modeller authored and simply arrives in blue, amber or green.
        /// Overwriting _BaseColor and switching the vertex colours off instead would have thrown
        /// away the model and left a flat silhouette.
        /// </summary>
        static Material LiveryMower(PlotSpec spec)
        {
            string path = $"{MatDir}/M_Livery_{spec.contestant}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var src = Mat("M_Mower") ?? Mat("M_PropsAuthored");
            if (m == null)
            {
                m = src != null ? new Material(src) : new Material(Shader.Find("Duck/Prop"));
                AssetDatabase.CreateAsset(m, path);
            }
            else if (src != null)
            {
                m.shader = src.shader;
                m.CopyPropertiesFromMaterial(src);
            }

            // The livery has to REPLACE the paint, not multiply into it. _InstanceColor multiplies,
            // and multiplying a red machine by green gives dark brown — the first attempt turned
            // three contestants into three mud-coloured mowers. So the hue comes from _BaseColor
            // and the vertex colours are dialled back to about a third, where they still carry the
            // model's light and shade — cream panels bright, engine block dark — over the top of
            // whichever colour this contestant races in.
            m.SetColor("_BaseColor", spec.livery);
            m.SetColor("_InstanceColor", Color.white);
            // Enough vertex colour left in to keep the panels, seat and tyres reading as separate
            // parts. At a third the machine was one flat slab of livery.
            m.SetFloat("_VertexColorAmount", 0.55f);
            m.SetFloat("_Wrap", 0.62f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ station & stand

        static void BuildStation(Transform plot, PlotSpec spec)
        {
            // The player's bench is built by the arena pass with the authored judges on it; the
            // rivals get the same furniture without characters, so the venue reads as consistent
            // from the air without paying for nine more animated animals.
            if (spec.isPlayer) return;

            var s = new GameObject("Station").transform;
            s.SetParent(plot, false);
            s.position = spec.StationPosition;

            var wood = Mat("M_WoodWarm");
            var woodDark = Mat("M_WoodDark");
            var top = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(2.4f, 0.06f, 0.62f), 0.05f), "StationTop");
            var front = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(2.4f, 0.36f, 0.06f), 0.04f), "StationFront");
            var leg = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(0.09f, 0.42f, 0.09f), 0.03f), "StationLeg");

            Spawn(s, "Top", top, wood, new Vector3(0f, 0.80f, 0.30f), Quaternion.identity, false);
            Spawn(s, "Front", front, wood, new Vector3(0f, 0.42f, 0.86f), Quaternion.identity, false);
            Spawn(s, "LegL", leg, woodDark, new Vector3(-2.1f, 0.42f, 0.1f), Quaternion.identity, false);
            Spawn(s, "LegR", leg, woodDark, new Vector3(2.1f, 0.42f, 0.1f), Quaternion.identity, false);

            // Three seated judges, authored. Different species from the player's panel so the
            // venue does not look like the same three animals cloned across four stations.
            string[] judgeNames = { "Cornelius", "Vesper", "Ottilie" };
            var judgeMat = Mat("M_StationJudges") ?? Mat("M_PropsAuthored");
            for (int i = 0; i < 3; i++)
            {
                var mesh = DuckAssetLibrary.GetCombined("StationJudges.fbx", $"{judgeNames[i]}_Root",
                                                        $"StationJudge_{judgeNames[i]}");
                if (mesh != null)
                {
                    // Already upright out of GetCombined; only the splay is ours, so the three are
                    // not a row of identical postures.
                    Spawn(s, $"Judge_{judgeNames[i]}", mesh, judgeMat,
                          new Vector3(-1.5f + i * 1.5f, 0.86f, -0.20f),
                          Quaternion.Euler(0f, Range(-11f, 11f), 0f), true);
                }
                else
                {
                    var sitter = DuckMeshLibrary.Persist(
                        DuckPrimitives.ChamferBox(new Vector3(0.30f, 0.42f, 0.26f), 0.12f), "StationSitter");
                    Spawn(s, $"Judge_{i}", sitter, Mat("M_TentCream"),
                          new Vector3(-1.5f + i * 1.5f, 1.14f, -0.18f),
                          Quaternion.Euler(0f, Range(-9f, 9f), 0f), true);
                }
            }

            // The awning that makes a station a station from a distance.
            // Width and depth swapped for the 90-degree turn, same as the main awning: the ridge
            // has to run along the bench.
            var canopy = DuckMeshLibrary.Persist(DuckPrimitives.Prism(2.4f, 0.7f, 5.6f), "StationCanopy");
            Spawn(s, "Canopy", canopy, Mat("M_TentRed"), new Vector3(0f, 2.3f, 0.3f),
                  Quaternion.Euler(0f, 90f, 0f), false);
            var post = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(0.07f, 1.15f, 0.07f), 0.02f), "StationPost");
            for (int i = -1; i <= 1; i += 2)
                for (int k = -1; k <= 1; k += 2)
                    Spawn(s, $"Post_{i}_{k}", post, Mat("M_WoodDark"),
                          new Vector3(i * 2.4f, 1.15f, 0.3f + k * 1.0f), Quaternion.identity, false);
        }

        static void BuildStand(Transform plot, PlotSpec spec)
        {
            if (spec.isPlayer) return;   // the player's stands come from the arena pass

            var s = new GameObject("Stand").transform;
            s.SetParent(plot, false);
            s.position = spec.StandPosition;
            s.rotation = Quaternion.Euler(0f, 90f, 0f);

            var wood = Mat("M_WoodWarm");
            var tier = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(4.2f, 0.10f, 0.55f), 0.06f), "StandTier");

            // Spectators go into the instanced crowd rather than being spawned as boxes here. The
            // crowd component already draws the authored species by the hundred for one draw call
            // each, and grey blocks on a rival stand next to real animals on the player's stand
            // read as unfinished work.
            var seats = new System.Collections.Generic.List<SpectatorCrowd.Seat>();

            for (int t = 0; t < 3; t++)
            {
                float y = 0.42f + t * 0.46f;
                float z = -t * 0.62f;
                Spawn(s, $"Tier_{t}", tier, wood, new Vector3(0f, y, z), Quaternion.identity, false);

                int n = 7 - t;
                for (int i = 0; i < n; i++)
                {
                    float x = (i + 0.5f) / n * 8.0f - 4.0f;
                    // Seats are world space and the stand is rotated, so convert through it.
                    // The tier slab is built from half-extents, so its top surface is y + 0.10.
                    Vector3 local = new Vector3(x + Range(-0.18f, 0.18f), y + 0.10f, z - 0.05f);
                    seats.Add(new SpectatorCrowd.Seat
                    {
                        position = s.TransformPoint(local),
                        yaw = s.eulerAngles.y + 180f + Range(-16f, 16f),
                        scale = Range(0.85f, 1.15f),
                        species = _rng.Next(0, 8),
                        phase = Rand
                    });
                }
            }

            AddSeats(seats);
        }

        /// <summary>Append seats to the one instanced crowd in the scene.</summary>
        static void AddSeats(System.Collections.Generic.List<SpectatorCrowd.Seat> extra)
        {
            if (extra.Count == 0) return;
            var crowd = Object.FindFirstObjectByType<SpectatorCrowd>();
            if (crowd == null) return;

            var all = new System.Collections.Generic.List<SpectatorCrowd.Seat>(
                crowd.seats ?? new SpectatorCrowd.Seat[0]);
            all.AddRange(extra);
            crowd.seats = all.ToArray();
        }

        /// <summary>The contestant's name board at the head of their plot. Reads from the air.</summary>
        static void BuildMarkerBoard(Transform plot, PlotSpec spec)
        {
            var b = new GameObject("MarkerBoard").transform;
            b.SetParent(plot, false);
            b.position = new Vector3(spec.centre.x + spec.Half + 4.5f, 0f, spec.centre.y - spec.Half - 4.5f);
            b.rotation = Quaternion.Euler(0f, -135f, 0f);

            var postMesh = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(0.09f, 1.1f, 0.09f), 0.03f), "BoardPost");
            var panelMesh = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(1.25f, 0.62f, 0.07f), 0.05f), "BoardPanel");
            Spawn(b, "Post", postMesh, Mat("M_WoodDark"), new Vector3(0f, 1.1f, 0f), Quaternion.identity, false);
            Spawn(b, "Panel", panelMesh, Mat("M_TentCream"), new Vector3(0f, 2.3f, 0f), Quaternion.identity, false);

            var textGO = new GameObject("Name");
            textGO.transform.SetParent(b, false);
            textGO.transform.localPosition = new Vector3(0f, 2.3f, -0.09f);
            textGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = textGO.AddComponent<TMPro.TextMeshPro>();
            tm.text = spec.contestant;
            tm.fontSize = 5.2f;
            tm.alignment = TMPro.TextAlignmentOptions.Center;
            tm.color = new Color(0.36f, 0.17f, 0.12f);
            tm.fontStyle = TMPro.FontStyles.Bold;
            tm.rectTransform.sizeDelta = new Vector2(2.4f, 1.1f);
        }

        /// <summary>A mown path from the plot's corner toward the plaza, so the venue joins up.</summary>
        static void BuildPathSpur(Transform plot, PlotSpec spec)
        {
            var from = new Vector3(spec.centre.x, 0.012f, spec.centre.y);
            Vector3 to = Venue.PlazaCentre;
            Vector3 dir = to - from; dir.y = 0f;
            float len = dir.magnitude;
            if (len < 1f) return;

            float start = spec.Half + 8f;
            float end = Venue.PlazaRadius;
            float span = len - start - end;
            if (span <= 2f) return;

            var mesh = DuckMeshLibrary.Persist(
                DuckPrimitives.ChamferBox(new Vector3(2.1f, 0.02f, span * 0.5f), 0.3f),
                $"Path_{spec.contestant}");
            Vector3 mid = from + dir.normalized * (start + span * 0.5f);
            Spawn(plot, "Path", mesh, Mat("M_Dirt"), new Vector3(mid.x, 0.014f, mid.z),
                  Quaternion.LookRotation(dir.normalized, Vector3.up), false);
        }

        // ------------------------------------------------------------------ plaza

        static void BuildPlaza(Transform venue)
        {
            var p = new GameObject("Plaza").transform;
            p.SetParent(venue, false);
            p.position = Venue.PlazaCentre;

            var disc = DuckMeshLibrary.Persist(
                DuckPrimitives.Cylinder(Venue.PlazaRadius, Venue.PlazaRadius, 0.06f, 40), "PlazaDisc");
            Spawn(p, "Ground", disc, Mat("M_Dirt"), new Vector3(0f, 0.02f, 0f), Quaternion.identity, false);

            // A ring of bunting posts, so the plaza reads as the centre of the venue from the air.
            var post = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(0.10f, 2.0f, 0.10f), 0.03f), "PlazaPost");
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                Spawn(p, $"Post_{i}", post, Mat("M_WoodDark"),
                      new Vector3(Mathf.Cos(a) * (Venue.PlazaRadius - 1.2f), 2.0f, Mathf.Sin(a) * (Venue.PlazaRadius - 1.2f)),
                      Quaternion.identity, false);
            }

            BuildScoreboard(p);
        }

        /// <summary>
        /// The championship board. Four rows that the endgame animates into rank order, facing
        /// back down the venue so the tour camera arrives at it head on.
        /// </summary>
        /// <summary>
        /// The championship board.
        ///
        /// Depth on this thing is laid out explicitly rather than by eye, because every layer is a
        /// flat slab parallel to every other and a centimetre of overlap is a z-fight across the
        /// whole face. Front surfaces, in order away from the plywood:
        ///
        ///     face   -0.22 .. +0.22
        ///     inlay  +0.20 .. +0.32
        ///     plates +0.34 .. +0.44
        ///     text            +0.50   (row text, clear of its own plate)
        ///     title/winner    +0.38   (no plate behind them, only the inlay)
        ///
        /// Vertically everything lives inside the inlay, which is inside the face — the winner line
        /// used to hang off the bottom edge into thin air.
        /// </summary>
        static void BuildScoreboard(Transform plaza)
        {
            var b = new GameObject("Scoreboard").transform;
            b.SetParent(plaza, false);
            b.localPosition = new Vector3(0f, 0f, Venue.PlazaRadius - 3.5f);
            // Faces back toward the quad's centre, which is where every tour leg approaches from.
            b.localRotation = Quaternion.Euler(0f, 180f + 45f, 0f);

            const float faceY = 6.4f, faceH = 3.6f;      // face spans 2.8 .. 10.0
            const float inlayH = 3.25f;                  // inlay spans 3.15 .. 9.65

            var legMesh = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(0.22f, 2.4f, 0.22f), 0.06f), "BoardLeg");
            Spawn(b, "LegL", legMesh, Mat("M_WoodDark"), new Vector3(-4.4f, 2.4f, 0f), Quaternion.identity, true);
            Spawn(b, "LegR", legMesh, Mat("M_WoodDark"), new Vector3(4.4f, 2.4f, 0f), Quaternion.identity, true);

            var faceMesh = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(5.2f, faceH, 0.22f), 0.14f), "BoardFace");
            Spawn(b, "Face", faceMesh, Mat("M_WoodDark"), new Vector3(0f, faceY, 0f), Quaternion.identity, true);

            var inlay = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(4.85f, inlayH, 0.06f), 0.08f), "BoardInlay");
            Spawn(b, "Inlay", inlay, Mat("M_WoodWarm"), new Vector3(0f, faceY, 0.26f), Quaternion.identity, false);

            var crest = DuckMeshLibrary.Persist(DuckPrimitives.Prism(5.4f, 1.1f, 0.4f), "BoardCrest");
            Spawn(b, "Crest", crest, Mat("M_TentRed"), new Vector3(0f, 10.2f, 0f), Quaternion.Euler(0f, 90f, 0f), true);

            var board = b.gameObject.AddComponent<Scoreboard>();

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(b, false);
            titleGO.transform.localPosition = new Vector3(0f, 9.15f, 0.38f);
            titleGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            board.title = MakeText(titleGO, "LAWN ART CHAMPIONSHIP", 3.6f, new Color(1f, 0.94f, 0.78f), 9.4f);

            board.rows = new ScoreboardRow[4];
            const float rowTop = 8.25f, rowStep = 1.28f;
            for (int i = 0; i < 4; i++)
            {
                // The row itself is NOT rotated. Turning the row round mirrored the x offsets of
                // everything on it, and it put the row's own plate in front of its text — the board
                // rendered as four blank slabs. Each text turns to face the camera individually,
                // exactly as the judges' desk cards do, and the plate sits behind them.
                var row = new GameObject($"Row_{i}").transform;
                row.SetParent(b, false);
                row.localPosition = new Vector3(0f, rowTop - i * rowStep, 0.50f);
                row.localRotation = Quaternion.identity;

                var plate = DuckMeshLibrary.Persist(DuckPrimitives.ChamferBox(new Vector3(4.55f, 0.55f, 0.05f), 0.07f), "BoardRow");
                var plateGO = Spawn(row, "Plate", plate, Mat("M_TentCream"), Vector3.zero, Quaternion.identity, false);
                plateGO.transform.localPosition = new Vector3(0f, 0f, -0.11f);

                // Sizes are world units, not points: a TextMeshPro in 3D sizes at roughly eleven
                // times the cap height in metres, so a 1.1 m tall plate wants a font size near five,
                // not near one. Reading these as points is what made the first two passes of this
                // board unreadable at any distance.
                var ink = new Color(0.14f, 0.10f, 0.08f);
                var r = new ScoreboardRow
                {
                    root = row,
                    plate = plateGO.GetComponent<MeshRenderer>(),
                    // Every column pulled inside the plate. The place label was hard against the
                    // left edge and the grade letter was hanging off the right one.
                    place = MakeText(Child(row, "Place", new Vector3(-3.45f, 0f, 0f)), "", 4.0f, new Color(0.70f, 0.38f, 0.10f), 1.6f),
                    name = MakeText(Child(row, "Name", new Vector3(-1.05f, 0f, 0f)), "", 4.4f, ink, 4.0f),
                    total = MakeText(Child(row, "Total", new Vector3(2.15f, 0f, 0f)), "", 4.4f, ink, 2.4f),
                    grade = MakeText(Child(row, "Grade", new Vector3(3.80f, 0f, 0f)), "", 5.4f, new Color(0.72f, 0.20f, 0.16f), 1.2f),
                };
                r.name.alignment = TMPro.TextAlignmentOptions.Left;
                r.place.alignment = TMPro.TextAlignmentOptions.Left;
                r.total.alignment = TMPro.TextAlignmentOptions.Right;
                board.rows[i] = r;
            }

            var winnerGO = new GameObject("Winner");
            winnerGO.transform.SetParent(b, false);
            winnerGO.transform.localPosition = new Vector3(0f, 3.62f, 0.38f);
            winnerGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            board.winnerLine = MakeText(winnerGO, "", 4.0f, new Color(1f, 0.92f, 0.72f), 9.2f);
            board.winnerLine.alpha = 0f;
        }

        /// <summary>
        /// A text slot on the board. Turned 180 degrees about Y so it reads from the side the board
        /// faces — the same convention the judges' desk cards use and which is verified on screen.
        /// </summary>
        static Transform Child(Transform parent, string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            // The x offset is negated with the turn. Read from the front, the board's local +x is
            // screen-left, so a column laid out left-to-right in local space came out backwards:
            // place on the right, grade on the left.
            t.localPosition = new Vector3(-local.x, local.y, local.z);
            t.localRotation = Quaternion.Euler(0f, 180f, 0f);
            return t;
        }

        static TMPro.TextMeshPro MakeText(Transform t, string text, float size, Color colour, float width)
        {
            var tm = t.gameObject.AddComponent<TMPro.TextMeshPro>();
            tm.text = text;
            tm.fontSize = size;
            tm.color = colour;
            tm.alignment = TMPro.TextAlignmentOptions.Center;
            tm.fontStyle = TMPro.FontStyles.Bold;
            tm.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tm.rectTransform.sizeDelta = new Vector2(width, size * 1.4f);
            return tm;
        }

        static TMPro.TextMeshPro MakeText(GameObject go, string text, float size, Color colour, float width)
            => MakeText(go.transform, text, size, colour, width);

        // ------------------------------------------------------------------ helpers

        static GameObject Spawn(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos,
                                Quaternion localRot, bool castShadow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }
    }
}
