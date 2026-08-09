using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using DuckMow;
using DuckMow.Flow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Builds the front page from scratch: Menu.unity, and the build-settings order that makes a
    /// player open on it.
    ///
    /// The menu is a camera in the real set. It calls the same lighting, post profile, judges' bench
    /// and environment builders the game scene does, drops in the authored mower with the duck on it,
    /// and mows a picture into the lawn so the premise is on screen before a word of type is read.
    /// The alternative — a colour, a logo and three buttons — would have been a tenth of this file
    /// and would have been the only screen in the game that did not look like the game.
    ///
    /// Everything about the shot lives in the vista table below, in metres and degrees, so the
    /// framing can be changed by editing a number and rebuilding rather than by nudging a camera by
    /// hand and hoping the scene gets saved. In play mode F1 and F2 walk through the table, which is
    /// how any of these numbers get judged — the first set was derived entirely from the lens and
    /// every one of them was wrong.
    /// </summary>
    public static class DuckMenuBuilder
    {
        public const string ScenePath = "Assets/Scenes/Menu.unity";
        const string MatDir = "Assets/Materials";
        const string TitleDir = "Assets/Art/Textures/Title";

        // ------------------------------------------------------------------ the shot
        //
        // ---- the problem this table exists to solve ----
        //
        // One frame has to carry four things: the duck on the mower, the picture mown into the lawn,
        // the judges' stand behind it, and enough sky to be a summer afternoon. Two of those four
        // fight each other on one axis, and the fight is not a smooth trade.
        //
        // A shape mown into grass opens out as the camera rises: its height on screen goes with the
        // sine of the pitch, so at 11 degrees the heart came out NINE PERCENT of the frame tall
        // against twenty-five percent wide, which reads as a band of pale grass. But an object
        // standing on that grass only ENTERS the frame beyond h/tan(pitch + half the vertical field
        // of view) — so every metre of height pushes the nearest place the mower can stand further
        // away, and the duck on it is 0.48 m tall. At the height that makes the picture read, the
        // duck is forty pixels and is not a duck.
        //
        // The way out was not a compromise on height. It was:
        //   * getting CLOSER. The first framing stood 26 m off the picture's centre for no reason
        //     any number required. At 17 m the same pitch gives half again the angular size, and
        //     distance costs the background, not the mower.
        //   * turning the picture round (see MenuLawnArt.pictureYaw). Every shape is authored to be
        //     read from the south, so its identifying feature was on the far, most-compressed side.
        //   * chalking the outline. A contour survives foreshortening because the eye reads its
        //     curvature, not its area, which is the whole reason the shape can now be read at all.
        //
        // With those three, h = 4.6 m lands the picture at thirty percent of the frame tall — up from
        // nine — with the mower nineteen percent of the frame WIDE and the duck at eight and a half
        // percent of frame height, up from four and a half. Both improved; nothing was traded.
        //
        // ---- how the numbers below were derived, and what each is answerable to ----
        //
        // Vertical screen position of anything on the ground is atan(h / distance) measured against
        // the frame's centre pitch, and the frame's centre pitch alone decides where the horizon
        // sits: at 14.75 degrees it lands at 81% of frame height, which leaves a fifth of the frame
        // for sky and the tree line to break into. Every figure in SET below is the answer to one of
        // those two equations, and they are recorded because the reader's alternative is to re-derive
        // them.
        //
        // The other three entries exist so a change can be looked at rather than argued about:
        // AUTHORED is the framing this menu shipped with, HIGH pushes height until the picture is the
        // only subject, LOW drops it until the mower is. They are all one keypress apart in play mode.

        static readonly Vector3 Pivot = new Vector3(-2f, 0.9f, -21f);
        const float Bearing = 30f;     // degrees from +Z; puts the sun 68 deg off the view axis
        const float Distance = 17f;

        static MainMenu.Vista[] Vistas()
        {
            return new[]
            {
                // h 4.6, pitch 14.75. Picture 30% of frame tall, mower 19% wide, duck 8.5% tall,
                // horizon at 81%, the bench at 67% and its awning at 81%.
                //
                // The mower's wheels are four percent below the bottom edge. That is deliberate: the
                // machine is the largest object in the frame and cropping it is what makes it read
                // as being in the foreground rather than as being small and central. The duck is
                // wholly in frame, which is the constraint that decides how far the crop can go.
                new MainMenu.Vista
                {
                    name = "SET",
                    pivot = Pivot, radius = Distance, yaw = Bearing,
                    // 4.0, down from 4.6. The front page was looking slightly DOWN on its own set,
                    // which is the one angle that makes a diorama read as a model rather than as a
                    // place — height is what turns a showground into a tabletop. Six tenths is
                    // enough to put the lens nearer the bunting's own line without lifting the
                    // horizon into the menu plates.
                    //
                    // heightSwing is left at 0.3. It is the breath in the shot, and it is measured
                    // against the mower rather than against the height, so it does not want to
                    // shrink when the camera comes down.
                    yawSwing = 2f, height = 4.0f, heightSwing = 0.3f, cycle = 30f,
                    lookAt = new Vector3(-2.25f, 0f, -21.4f), fov = 46f,
                    // 5.9 m out and 71% across. The yaw turns the machine's nose toward frame left
                    // and slightly toward the lens, which is both the three-quarter view every one of
                    // these prop models was built to be seen from and a nose pointed at its own work.
                    mowerPos = new Vector3(2.24f, 0.45f, -10.26f), mowerYaw = 97f,
                    shape = ShapeId.Heart,
                    pictureCentre = new Vector2(-2f, -21f), pictureRadius = 11f, pictureYaw = 180f,
                    buntingForward = 4.4f, buntingYaw = 26f, buntingHeight = 4.95f,
                    propsForward = 7.6f, propsLateral = -1.4f, propsYaw = -18f
                },

                // The framing this menu shipped with, kept so the change can be seen rather than
                // described. Camera 26 m off the picture at 6.4 m, which is a pitch of 10.9 degrees.
                new MainMenu.Vista
                {
                    name = "AUTHORED",
                    pivot = new Vector3(2f, 0.9f, -20f), radius = 20.9f, yaw = 47.9f,
                    yawSwing = 4.5f, height = 6.4f, heightSwing = 0.4f, cycle = 34f,
                    lookAt = new Vector3(-2f, 1f, -26f), fov = 46f,
                    mowerPos = new Vector3(7.47f, 0.45f, -11.55f), mowerYaw = 110f,
                    shape = ShapeId.Heart,
                    pictureCentre = new Vector2(-4f, -21f), pictureRadius = 10.5f, pictureYaw = 0f,
                    buntingForward = 5.2f, buntingYaw = 26f, buntingHeight = 6.7f,
                    propsForward = 9f, propsLateral = -1.6f, propsYaw = -18f
                },

                // h 6.2, pitch 19. Picture 37% of frame — the best read the shape ever gets — and the
                // duck down to 6.5%, which is where it stops being a duck.
                new MainMenu.Vista
                {
                    name = "HIGH · picture",
                    pivot = Pivot, radius = Distance, yaw = Bearing,
                    yawSwing = 1.8f, height = 6.2f, heightSwing = 0.35f, cycle = 30f,
                    lookAt = new Vector3(-2.5f, 0f, -21.87f), fov = 46f,
                    mowerPos = new Vector3(1.09f, 0.45f, -11.18f), mowerYaw = 97f,
                    shape = ShapeId.Heart,
                    pictureCentre = new Vector2(-2f, -21f), pictureRadius = 11f, pictureYaw = 180f,
                    buntingForward = 4.4f, buntingYaw = 26f, buntingHeight = 6.5f,
                    propsForward = 8.4f, propsLateral = -1.4f, propsYaw = -18f
                },

                // h 3.8, same pitch. Mower 24% of frame wide and the duck at eleven percent, against
                // a picture down to 26% and a quarter of the frame given to sky.
                new MainMenu.Vista
                {
                    name = "LOW · mower",
                    pivot = Pivot, radius = Distance, yaw = Bearing,
                    yawSwing = 1.6f, height = 3.8f, heightSwing = 0.28f, cycle = 30f,
                    lookAt = new Vector3(-0.72f, 0f, -18.78f), fov = 46f,
                    mowerPos = new Vector3(3.23f, 0.45f, -9.33f), mowerYaw = 97f,
                    shape = ShapeId.Heart,
                    pictureCentre = new Vector2(-2f, -21f), pictureRadius = 11f, pictureYaw = 180f,
                    buntingForward = 4.4f, buntingYaw = 26f, buntingHeight = 4.2f,
                    propsForward = 7f, propsLateral = -1.3f, propsYaw = -18f
                },
            };
        }

        static readonly Color Cream = new Color(0.97f, 0.94f, 0.86f);
        static readonly Color Ink = new Color(0.16f, 0.12f, 0.09f);
        static readonly Color Brick = new Color(0.72f, 0.20f, 0.17f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.45f);
        /// <summary>The art bible's wood dark, #6E4A2C. A second ink for cream plates: warm, clearly
        /// lighter than Ink, and still 6.9:1 against the plate — which is what a supporting line
        /// needs to be subordinate without being unreadable. See BuildStagesCard.</summary>
        static readonly Color WoodDark = new Color(0.431f, 0.290f, 0.173f);

        // ------------------------------------------------------------------ menu

        [MenuItem("Duck/4 · Build Menu Scene", priority = 3)]
        public static void BuildMenuMenu()
        {
            DuckSceneBuilder.BuildMaterials();
            DuckUIBuilder.ImportSprites();
            BuildMenuScene();
        }

        // ------------------------------------------------------------------ scene

        public static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // The look is not re-authored here. Sun, ambient, fog and the post profile all come from
            // the game scene's builders, so the menu cannot end up warmer, flatter or hazier than the
            // round it leads into — which is exactly what a separately tuned menu scene does after
            // two or three grading passes.
            DuckSceneBuilder.BuildLighting();
            DuckSceneBuilder.BuildEnvironmentLighting();
            DuckSceneBuilder.BuildPostProcessing();

            var cam = BuildCamera();
            var lawnArt = BuildLawn();
            var mower = BuildMower();
            // Reads the mower by name for its look target, so it has to come after it. The judges
            // then spend the whole menu watching the machine, which is most of what stops the bench
            // reading as furniture.
            DuckSceneBuilder.BuildJudgeBench();
            DuckEnvironmentBuilder.Build();

            var pennants = new List<Transform>();
            var bunting = BuildBunting(pennants);
            var props = BuildNearProps();

            var menu = BuildUI(cam);
            menu.cameraTransform = cam.transform;
            menu.mower = mower != null ? mower.transform : null;
            menu.lawnArt = lawnArt;
            menu.buntingRoot = bunting;
            menu.propsRoot = props;
            menu.pennants = pennants.ToArray();
            menu.vistas = Vistas();
            menu.playScene = Path.GetFileNameWithoutExtension(DuckSceneBuilder.ScenePath);
            BuildAudio(menu, cam);

            // Put the saved scene into the state the player's first frame is in: the camera on the
            // pose the cycle starts from, the mower and the near dressing where the opening framing
            // wants them, and PLAY already selected. Without this the scene is stored with the camera
            // wherever the transform was created, so every editor screenshot of the menu is of a
            // frame nobody ever sees.
            //
            // The one thing that cannot be saved is the picture: the cut mask is a render texture
            // built at runtime, so MenuLawnArt has to mow it every time the scene loads, and the
            // saved scene's lawn is bare.
            menu.ApplyFraming(0);
            menu.ApplyHighlight();
            // And the class list, settled on its first row with the launcher's own titles on it, so
            // the saved card is a card rather than three plates stacked at the origin with whatever
            // the last edit left on them. It is never TRUSTED — MainMenu writes both again on load —
            // but a card that cannot be looked at in the editor is a card nobody reviews.
            menu.LabelStages();
            menu.ApplyStageHighlight();
            // And the settings board, with its readings taken from MasterAudio and its marker parked
            // on the first row. Same rule again: a card that cannot be looked at in the editor is a
            // card nobody reviews, and this one is read-only at build time — nothing here writes the
            // master volume, so building the scene cannot change how loud somebody's editor is.
            menu.ApplySettingHighlight();

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterBuildScenes();
            Debug.Log("[Duck] Menu scene built.");
        }

        static Camera BuildCamera()
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            // The game's lens, minus its director: same clip planes and same HDR/MSAA choices, so
            // the menu and the first frame of the round are photographed by the same machine.
            cam.nearClipPlane = 0.18f;
            cam.farClipPlane = 420f;
            cam.fieldOfView = Vistas()[0].fov;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.backgroundColor = DuckSceneBuilder.Hex(DuckSceneBuilder.P.SkyHorizon);

            var extra = go.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            extra.antialiasingQuality = AntialiasingQuality.Medium;
            extra.renderShadows = true;

            go.AddComponent<AudioListener>();
            return cam;
        }

        /// <summary>
        /// The lawn, the cut mask that makes it a lawn rather than a green plane, and the chalk sheet
        /// the picture's outline is drawn on.
        ///
        /// CutMask is not optional here even though nothing is being played: the ground and blade
        /// shaders read _CutMask from the shader globals, and with nothing to set them the menu lawn
        /// samples an undefined texture — which comes back white, i.e. mown flat everywhere.
        /// </summary>
        static MenuLawnArt BuildLawn()
        {
            var systems = new GameObject("~ Systems").transform;
            var cutMask = systems.gameObject.AddComponent<CutMask>();
            cutMask.stampShader = Shader.Find("Duck/CutStamp");

            var art = systems.gameObject.AddComponent<MenuLawnArt>();

            var lawn = new GameObject("Lawn").transform;
            var field = lawn.gameObject.AddComponent<GrassField>();
            field.groundMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_GrassGround.mat");
            field.bladeMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_GrassBlades.mat");

            art.chalkSheet = BuildChalkSheet(lawn);
            return art;
        }

        /// <summary>
        /// The flat sheet the chalk outline is drawn on, a couple of centimetres above the grass.
        ///
        /// It gets its OWN material rather than sharing M_ChalkGuide with the round, because two of
        /// that shader's settings have to be different here and both of them would break gameplay if
        /// changed globally: the round scuffs the chalk away wherever the mower has been, and the menu
        /// picture is mown from edge to edge; and the round draws corner registration brackets, which
        /// on a menu read as a HUD element rather than as chalk.
        /// </summary>
        static MeshRenderer BuildChalkSheet(Transform lawn)
        {
            var mat = DuckSceneBuilder.EnsureMaterial("M_ChalkGuideMenu", "Duck/ChalkGuide");
            if (mat == null) return null;

            mat.SetColor("_ChalkColor", new Color(0.94f, 0.91f, 0.80f, 1f));
            // Wider and stronger than the round's 0.30 / 0.62. The line is being read from 17 to 32
            // metres away across a foreshortened plane rather than from a chase camera, and at the
            // round's width it is a couple of screen pixels at the far edge of the picture.
            mat.SetFloat("_LineWidth", 0.46f);
            mat.SetFloat("_LineAlpha", 0.74f);
            mat.SetFloat("_Patchiness", 0.5f);
            // The round fades the chalk to 25% wherever the deck has passed. The menu picture is
            // fully mown, so at that setting the outline survives only on the strip of uncut grass
            // MenuLawnArt's edge inset leaves inside it — which is a line of varying weight rather
            // than a line.
            mat.SetFloat("_ScuffFade", 0.35f);
            // Half worn away, in patches. A crisp full-strength outline over finished work reads as
            // an overlay; a broken one reads as chalk that has been mown over, which is what it is.
            mat.SetFloat("_Dissolve", 0.2f);
            mat.SetFloat("_DissolveEdge", 0.4f);
            mat.SetFloat("_AnchorAmount", 0f);
            mat.SetFloat("_GhostAmount", 0f);
            mat.SetFloat("_AnalysisAmount", 0f);
            EditorUtility.SetDirty(mat);

            var go = new GameObject("ChalkGuide");
            go.transform.SetParent(lawn, false);
            go.transform.position = new Vector3(0f, 0.03f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = DuckMeshLibrary.Quad(Field.Size, Field.Size, 24);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            // Off until MenuLawnArt has baked the outline — see MenuLawnArt.chalkSheet for what an
            // enabled sheet with no distance field in it looks like.
            mr.enabled = false;
            return mr;
        }

        /// <summary>
        /// The authored mower, parked, with every component it ships with left running.
        ///
        /// Stripping the controller and the visuals off it and treating it as a static prop was the
        /// obvious move and the wrong one: MowerVisuals is what gives the machine its engine buzz and
        /// the duck its idle, and both read at this distance. With no InputReader in the scene the
        /// controller cannot be driven — it reads a null input as zero — so the mower sits on its
        /// suspension and breathes, which is all the menu wants from it.
        /// </summary>
        static GameObject BuildMower()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mower.prefab");
            if (prefab == null)
            {
                Debug.LogWarning("[Duck] Menu: Assets/Prefabs/Mower.prefab is missing, so the menu " +
                                 "has no mower in frame. Run the model integration first.");
                return null;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.name = "Mower";
            return inst;
        }

        // ------------------------------------------------------------------ near dressing
        //
        // Two groups of geometry standing between the lens and the picture, at four and a half and
        // seven and a half metres.
        //
        // This is the one thing a flat elevation of a venue cannot be talked into: DEPTH. The set has
        // a mower at six metres, a picture from twelve to thirty, a bench at thirty-three and hills
        // at three hundred, and none of that reads as depth because there is nothing near enough for
        // the eye to measure the rest against. A cutscene panel in this project already proves the
        // device — panel 1 is a duck on a pond read entirely through out-of-scale reeds crossing the
        // bottom of frame.
        //
        // Both groups are placed from the framing by MainMenu rather than parented to the camera,
        // because parallax against a moving lens is the entire point and dressing that travels with
        // the camera has none.

        /// <summary>
        /// Bunting strung across the top of the frame, close enough to the lens to be out of scale.
        ///
        /// The run is oblique to the view on purpose: at right angles it is a horizontal row of
        /// identical pennants, and turned it has a near end and a far end. The pennants are separate
        /// objects rather than one combined mesh only because MainMenu has to be able to stir them —
        /// static cloth in the nearest layer of the frame is worse than no cloth there.
        /// </summary>
        static Transform BuildBunting(List<Transform> pennants)
        {
            var root = new GameObject("~ Near bunting").transform;

            var wood = Mat("M_WoodDark");
            var red = Mat("M_TentRed");
            var cream = Mat("M_TentCream");
            var warm = Mat("M_WoodWarm");

            const float halfRun = 5.4f, sag = 0.42f;
            Vector3 Line(float t) => new Vector3(Mathf.Lerp(-halfRun, halfRun, t),
                                                 -Mathf.Sin(t * Mathf.PI) * sag, 0f);

            // The posts. Never in shot at any framing in the table — the frame is about seven metres
            // wide where this run stands and the run is nearly eleven — so they exist only so the
            // cord is tied to something if the framing is ever widened. They hang DOWN from the cord
            // rather than standing up to it: the root's height changes with the framing, and a post
            // built to reach the ground at 4.95 m floats a metre in the air at 6.5. Buried is
            // invisible; floating is a bug.
            var post = DuckMeshLibrary.Persist(
                DuckPrimitives.ChamferBox(new Vector3(0.075f, 5f, 0.075f), 0.025f), "MenuBuntingPost");
            for (int s = -1; s <= 1; s += 2)
            {
                var go = new GameObject("Post");
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(halfRun * s, -4.4f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = post;
                go.AddComponent<MeshRenderer>().sharedMaterial = warm;
            }

            // Cord and pennants come off the SAME curve, so every pennant touches the line it hangs
            // from. They were built from two different curves once and the pennants floated.
            var cord = DuckMeshLibrary.Persist(
                DuckPrimitives.Cylinder(0.022f, 0.022f, 1f, 4, 0.004f), "MenuBuntingCord");
            const int segments = 6;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = Line(i / (float)segments), b = Line((i + 1) / (float)segments);
                var go = new GameObject("Cord");
                go.transform.SetParent(root, false);
                go.transform.localPosition = (a + b) * 0.5f;
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                go.transform.localScale = new Vector3(1f, (b - a).magnitude, 1f);
                go.AddComponent<MeshFilter>().sharedMesh = cord;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = wood;
                mr.shadowCastingMode = ShadowCastingMode.Off;   // a 22 mm cord casts nothing readable
            }

            // Larger than the venue's own pennants, because these are four metres from the lens and
            // the venue's are forty. Matching the real ones would put a row of thumbnails across the
            // top of the frame.
            var pennant = DuckMeshLibrary.Persist(DuckPrimitives.Prism(0.44f, 0.54f, 0.02f),
                                                  "MenuPennant");
            const int flags = 11;
            for (int i = 0; i < flags; i++)
            {
                float t = (i + 0.5f) / flags;
                var peg = new GameObject($"Pennant{i}");
                peg.transform.SetParent(root, false);
                peg.transform.localPosition = Line(t);

                // The cloth is a child of the peg so MainMenu can swing the peg about its local X —
                // which is along the cord — without having to know how the cloth is oriented.
                var cloth = new GameObject("Cloth");
                cloth.transform.SetParent(peg.transform, false);
                // Pegged crooked, deterministically. UnityEngine.Random here would give every rebuild
                // a different set of angles, and the whole point of building the scene from a script
                // is that two builds of the same source produce the same scene.
                float crooked = Mathf.Sin(i * 2.399f) * 6f;
                cloth.transform.localRotation = Quaternion.Euler(180f, 0f, crooked);
                cloth.AddComponent<MeshFilter>().sharedMesh = pennant;
                var mr = cloth.AddComponent<MeshRenderer>();
                mr.sharedMaterial = (i % 3 == 0) ? red : ((i % 3 == 1) ? cream : warm);
                mr.shadowCastingMode = ShadowCastingMode.Off;

                pennants.Add(peg.transform);
            }

            return root;
        }

        /// <summary>
        /// The groundskeeper's kit, standing on the lawn between the lens and the picture.
        ///
        /// Authored props from Props.fbx rather than boxes: this group is seven metres from the lens,
        /// where a hay bale is a sixth of the frame tall, and at that size a chamfered box is a
        /// chamfered box. It is also the only thing in the frame that explains the chalk — the barrow
        /// is what the line was walked out with — so it is a group with a reason to exist rather
        /// than scatter.
        /// </summary>
        static Transform BuildNearProps()
        {
            var root = new GameObject("~ Near props").transform;
            var mat = Mat("M_PropsAuthored");

            Stand(root, "HayBale", Authored("HayBale"), mat, new Vector3(0f, 0f, 0f), 24f);
            Stand(root, "Wheelbarrow", Authored("Wheelbarrow"), mat, new Vector3(1.55f, 0f, -0.7f), -108f);
            // Two stakes, at two distances, leaning differently. The plot's corner markers; the game
            // already stands these on the lawn, so they are not a menu invention.
            Stand(root, "MarkerStakeA", Authored("MarkerStake"), mat, new Vector3(-1.25f, 0f, 0.55f), 40f);
            Stand(root, "MarkerStakeB", Authored("MarkerStake"), mat, new Vector3(2.9f, 0f, 0.95f), -22f);

            return root;
        }

        static Mesh Authored(string objName)
            => DuckAssetLibrary.GetCombined("Props.fbx", objName, objName);

        /// <summary>
        /// Sit a prop on the ground at a local position, measuring the mesh rather than trusting the
        /// exporter's pivot.
        ///
        /// A yaw-only rotation cannot change a bounding box's lowest point, which is the whole reason
        /// this is one subtraction rather than eight transformed corners.
        /// </summary>
        static void Stand(Transform parent, string name, Mesh mesh, Material mat,
                          Vector3 localPos, float yaw, float scale = 1f)
        {
            if (mesh == null)
            {
                Debug.LogWarning($"[Duck] Menu: Props.fbx has no {name}, so the near dressing is " +
                                 "missing a piece and the frame has less depth than it was composed for.");
                return;
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            localPos.y -= mesh.bounds.min.y * scale;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        // ------------------------------------------------------------------ UI

        static MainMenu BuildUI(Camera camera)
        {
            var canvasGO = new GameObject("~ Menu UI", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGO.GetComponent<Canvas>();
            // Screen Space - Camera, as the HUD and the cutscene page both are: the capture rig
            // renders the camera to a texture, and an Overlay canvas is absent from every screenshot
            // and therefore from every review. It also means the page gets the same grade and bloom
            // as the world behind it.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 100;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var menu = canvasGO.AddComponent<MainMenu>();
            var root = (RectTransform)canvasGO.transform;

            menu.titlePieces = BuildTitle(root);
            menu.items = BuildChoices(root, out RectTransform pointer);
            menu.pointer = pointer;
            menu.plateIdle = DuckUIBuilder.Spr("button_256");
            menu.platePressed = DuckUIBuilder.Spr("button_pressed_256");
            menu.labelIdle = Ink;
            menu.labelSelected = Brick;
            // The one-liner's own two inks, so the colour that fixed an unreadable line lives with
            // the rest of the board's palette rather than only in a field default somebody could
            // reset in the inspector without knowing what it was for.
            menu.kickerIdle = WoodDark;
            menu.kickerSelected = new Color(0.62f, 0.24f, 0.18f);

            menu.stagesCard = BuildStagesCard(root, menu);
            menu.settingsCard = BuildSettingsCard(root, menu);
            menu.controlsCard = BuildControlsCard(root);
            menu.creditsCard = BuildCreditsCard(root);
            menu.framingLabel = BuildFramingLabel(root);

            // Last, so it covers the page rather than sitting under it.
            var fade = DuckUIBuilder.Frac("Fade", root, 0f, 0f, 1f, 1f);
            menu.fade = DuckUIBuilder.AddImage(fade, null, new Color(0.03f, 0.03f, 0.04f, 0f));
            menu.fade.enabled = false;

            return menu;
        }

        /// <summary>
        /// The title: a painted board with the masthead on it, a rosette pinned to its left edge and
        /// the class on a ribbon hanging off its bottom corner.
        ///
        /// ---- why the lettering is a picture ----
        ///
        /// It is a render of Rockwell Bold, arched and keylined as real extruded geometry, produced by
        /// Art/Blender/render_title.py — the same route and the same font as the flyer in the opening
        /// cutscene, so the front page and the flyer that starts the story are set in one voice.
        ///
        /// The project ships exactly one font, Liberation Sans, and the art bible lists default sans
        /// UI as an automatic rejection. Every attempt to make a masthead out of it — bold, wide
        /// tracking, a hard offset drop, a rosette to distract from it — produced a heading with
        /// decoration around it, because the thing that makes signwriting read is the letterform, and
        /// no amount of spacing changes a letterform.
        ///
        /// The costs of a baked masthead are real and worth stating: it is 576 KB in the first scene
        /// the player downloads, and renaming the event means re-running the script rather than
        /// editing a string. The fallback below keeps a clone that has neither the PNG nor Rockwell
        /// building a menu at all — just the weaker one.
        ///
        /// ---- where it sits ----
        ///
        /// Top right, not centred and not full width. The judges' bench lands at 27% across and 65 to
        /// 74% up this frame, and a board across the whole top covers the one group of characters in
        /// the shot that is actually animating. Everything here is clear of that band.
        /// </summary>
        static RectTransform[] BuildTitle(RectTransform root)
        {
            var group = DuckUIBuilder.Frac("Title", root, 0.295f, 0.745f, 0.875f, 0.99f);
            // Off square, as everything pinned to a board in this game is. Small: two degrees reads
            // as hand-placed, five reads as a mistake.
            group.localRotation = Quaternion.Euler(0f, 0f, -1.6f);

            var card = DuckUIBuilder.Frac("Card", group, 0f, 0f, 1f, 1f);
            var field = DuckUIBuilder.Plate(card, DuckUIBuilder.CardLight);

            // The whole writable board, and preserveAspect below decides how much of it the arch
            // actually uses. It was laid out to 0.10..0.94 of the card, which on a 265 px board is
            // 26 px up from the bottom and 16 down from the top against a rule that sits at 32 and
            // 26 — so the masthead's own keyline was crossing the board's.
            var mastheadRt = DuckUIBuilder.Frac("Masthead", field, 0f, 0f, 1f, 1f);
            var masthead = TitleSprite();
            if (masthead != null)
            {
                var img = DuckUIBuilder.AddImage(mastheadRt, masthead, Color.white);
                // Aspect preserved, so the arch cannot be squashed by the card's proportions. The
                // render already carries its own drop and keyline, so there is nothing else to draw.
                img.preserveAspect = true;
            }
            else
            {
                BuildFallbackLettering(mastheadRt);
            }

            // Hangs off the card's left edge, the way a rosette pinned to a board would.
            var rosette = DuckUIBuilder.Frac("Rosette", card, -0.075f, 0.30f, 0.075f, 0.92f);
            var rosetteImg = DuckUIBuilder.AddImage(rosette, DuckUIBuilder.Spr("rosette_S_256"), Color.white);
            rosetteImg.preserveAspect = true;
            rosette.localRotation = Quaternion.Euler(0f, 0f, 9f);

            // ---- there is no LAWN ART CLASS ribbon here any more ----
            //
            // It hung across the board's bottom-right and read "LAWN ART CLASS" in small type, on the
            // reasoning that a horticultural show bill names the show and then the class inside it.
            // That reasoning was sound about the BILL and wrong about this screen, for two reasons
            // that only became visible once the rest of the front of the game existed.
            //
            // It named ONE STAGE. Lawn Art is round one of three, and the front page is the door to
            // all of them — the stage select on this same screen offers the goose rally and Bloom
            // Rush beside it. A subtitle that names a third of the game as though it were the whole
            // thing is a caption that gets less true the further the player gets.
            //
            // And it was the fourth name in about three seconds. The splash card now says POND &
            // GREEN, the masthead says DUCK MOW, the masthead's own render already carries COUNTY
            // GARDENER OF THE YEAR under it, and then this. Each of those is doing a different job —
            // who made it, what it is called, what the event is — and the class was the only one
            // whose job was already done by the line above it.
            //
            // The ribbon sprite and its layout are not lost: DuckUIBuilder still builds ribbons the
            // same way, and this block is one Frac and one AddText if a subtitle is ever wanted back.

            // Landing order: the board with the name, then the rosette pinned on.
            return new[] { card, rosette };
        }

        /// <summary>
        /// The pre-Blender title, kept only for a checkout that has no masthead render.
        ///
        /// It is doubled lettering rather than one string because that is the only thing weight and
        /// spacing can do for a UI font, and it is recorded here as the thing the render replaced.
        /// </summary>
        static void BuildFallbackLettering(RectTransform where)
        {
            Debug.LogWarning($"[Duck] Menu: {TitleDir}/title_masthead.png is missing, so the title " +
                             "falls back to Liberation Sans. Run " +
                             "blender --background --python Art/Blender/render_title.py");

            var shadowRt = DuckUIBuilder.Frac("NameShadow", where, 0f, 0.30f, 1f, 1f);
            shadowRt.offsetMin = new Vector2(8f, -9f);
            shadowRt.offsetMax = new Vector2(8f, -9f);
            var shadow = DuckUIBuilder.AddText(shadowRt, "DUCK MOW", 130f, TextAlignmentOptions.Center,
                                               new Color(0.42f, 0.13f, 0.11f, 0.55f), 0f, false);
            shadow.fontStyle = FontStyles.Bold;
            shadow.characterSpacing = 10f;

            var nameRt = DuckUIBuilder.Frac("Name", where, 0f, 0.30f, 1f, 1f);
            var title = DuckUIBuilder.AddText(nameRt, "DUCK MOW", 130f, TextAlignmentOptions.Center,
                                              Brick, 0.20f, false);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 10f;

            var showRt = DuckUIBuilder.Frac("Show", where, 0f, 0.02f, 1f, 0.30f);
            var show = DuckUIBuilder.AddText(showRt, "COUNTY GARDENER OF THE YEAR", 26f,
                                             TextAlignmentOptions.Center, Brick, 0.14f, false);
            show.fontStyle = FontStyles.Bold;
            show.characterSpacing = 12f;
        }

        /// <summary>
        /// The masthead render, with its own import settings.
        ///
        /// It lives outside Assets/Art/Textures/UI on purpose: DuckUIBuilder.ImportSprites caps
        /// everything in that folder at 512 pixels, which is correct for a 9-sliced plate and would
        /// reduce a 1536-pixel masthead to a third of the resolution it is drawn at.
        /// </summary>
        static Sprite TitleSprite()
        {
            string path = $"{TitleDir}/title_masthead.png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null && File.Exists(path))
            {
                // On disk but not yet in the database, which is exactly the state a fresh render
                // leaves it in. Without this the first menu build after running render_title.py
                // silently falls back to Liberation Sans and the second one does not, which is the
                // most confusing possible way for this to fail.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                imp = AssetImporter.GetAtPath(path) as TextureImporter;
            }
            if (imp == null) return null;

            // Re-imported only when something is actually wrong, because a reimport is not free and
            // this runs on every menu build.
            if (imp.textureType != TextureImporterType.Sprite || imp.maxTextureSize != 2048 ||
                imp.mipmapEnabled || !imp.alphaIsTransparency)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false;
                imp.filterMode = FilterMode.Bilinear;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.alphaIsTransparency = true;
                imp.sRGBTexture = true;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.maxTextureSize = 2048;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Five plates down the left, clear of both the mower on the right and the picture behind
        /// them, with a chevron pointing at whichever is selected and the hint under them.
        ///
        /// Left rather than centred because the middle of this frame is the mown picture, and that is
        /// the one thing the menu exists to show. The column stops at 30.5% across and the picture
        /// starts at 33%, which is the constraint that fixes its width.
        ///
        /// ---- what the fourth plate cost ----
        ///
        /// THE CLASSES went in second, under PLAY, because the two plates that start a game belong
        /// together and the ones that only have reading on them belong underneath. That left three
        /// plates' worth of column holding four, and there were only two ways out of it.
        ///
        /// The column used to run from 11.5% to 50% of frame height with plates 116 px tall. Simply
        /// keeping that band and dividing it four ways drops every plate to 81 px, which is a ten
        /// percent cut to the front page's largest piece of type in order to add its smallest
        /// choice — the wrong trade, and it happens to PLAY as much as to CREDITS.
        ///
        /// So the band grew UPWARD instead, to 58.5%, and the plates only came down to 104 px.
        ///
        /// ---- what the fifth plate cost, and the wall it ran into ----
        ///
        /// SETTINGS went in fourth, above CREDITS: it is not a game, so it is not with PLAY and THE
        /// CLASSES, and it is something a player DOES, so it does not belong under the one plate on
        /// the board that is only ever read once.
        ///
        /// The arithmetic is much tighter than the fourth plate's was, because there is a hard floor
        /// under a plate's height that has nothing to do with taste. button_256 is 9-sliced with a
        /// 52-pixel top border and a 44-pixel bottom one, and a sliced sprite draws its borders at
        /// their own pixel size — so 96 px is the height at which a plate is ENTIRELY frame, with a
        /// stretched middle band of exactly zero. Under that Unity scales the two borders down to
        /// fit, which squashes the rim and turns the corners elliptical. The four-plate column at 104
        /// px was already within eight pixels of that floor; there is no fifth plate to be had by
        /// dividing the same band again, because 507 px over five rows is 101 px per row before a
        /// single pixel is spent on a gap.
        ///
        /// So the band grew upward once more, to 62.2%, and the plates sit exactly ON the floor:
        ///
        ///     column   11.5% .. 62.2% of 1080          =  547.6 px
        ///     plate    0.1753 x 547.6                  =   96.0 px   (the 9-slice floor, no squash)
        ///     step     0.2002 x 547.6                  =  109.7 px
        ///     gap      109.7 - 96.0                    =   13.6 px
        ///     unused   547.6 - (4 x 109.7 + 96.0)      =   13.1 px   below the last plate
        ///
        /// The 13.6 px gap looks alarming next to the 26 px it replaces and is not, because 21 of the
        /// sprite's 128 rows are transparent margin: 13 along the bottom for the painted drop shadow
        /// and 8 across the top. The gap between two plates as DRAWN is 13.6 + 13 + 8 = 34.6 px,
        /// against 47 before — closer, still better than a third of a plate, and the shadow rect
        /// MainMenu drives into that space is what keeps it reading as depth rather than as crowding.
        ///
        /// What it costs above: the top of the column now clears the judges' bench at 65% by 3.0% of
        /// the frame — 30 px of rect, 38 px to the plate's painted rim — where the four-plate column
        /// cleared it by 70. That is the real price of the fifth choice and it is worth stating
        /// plainly: the bench is the one group in the shot that animates, and this is now close to
        /// it. It is not covering it. A sixth plate would be, and there is no room for one.
        ///
        /// Nothing here changes with the aspect ratio. The canvas scaler matches HEIGHT, so the page
        /// is 1080 units tall in every window the game will ever open in and every fraction above is
        /// measured against a constant. Only the WIDTH of the column moves, and the labels autosize
        /// into it.
        /// </summary>
        static MainMenu.Item[] BuildChoices(RectTransform root, out RectTransform pointer)
        {
            var column = DuckUIBuilder.Frac("Choices", root, 0.075f, 0.115f, 0.305f, 0.622f);

            var labels = new[] { "PLAY", "THE CLASSES", "CONTROLS", "SETTINGS", "CREDITS" };
            // The hierarchy names are kept apart from the painted labels, because the second choice
            // is two words and "THE CLASSESShadow" is a name somebody has to read at three in the
            // morning while working out which rect is on top of which.
            var names = new[] { "Play", "Stages", "Controls", "Settings", "Credits" };
            // The ORDER here is the board's, and it is deliberately not the enum's. MainMenu.Choice
            // is appended-only because it is a storage format; this array is the running order
            // somebody pinned the plates up in. Making the two agree would mean renumbering the
            // enum, which is the one thing its comment forbids.
            var choices = new[]
            {
                MainMenu.Choice.Play, MainMenu.Choice.Stages, MainMenu.Choice.Controls,
                MainMenu.Choice.Settings, MainMenu.Choice.Credits
            };
            // Nothing is on a perfect grid except the mowing stripes. Five plates on an exact
            // vertical at an exact tilt is the one arrangement that reads as a web form, so each one
            // sits at its own angle and its own inset — small numbers, all under a degree and a half.
            //
            // The signs run - - + + -, which is not a sequence. The three original plates alternated,
            // the fourth broke the run on purpose, and the fifth doubles a lean rather than resuming
            // it. A regular zigzag is a pattern, and a pattern is exactly the thing these numbers
            // exist to avoid; plates actually nailed up by hand cluster and then disagree.
            var tilts = new[] { -1.1f, -0.35f, 0.8f, 0.45f, -0.5f };
            var insets = new[] { 0f, 6f, 9f, 7f, 3f };
            var items = new MainMenu.Item[labels.Length];

            // 96 px plates with 13.6 px between them and 13 px left under the last one, measured
            // against a column that is now 547.6 px tall. See the arithmetic block above; the two
            // numbers are kept in this proportion so the column reads as evenly spaced rather than
            // as five plates that happen to end near the bottom.
            const float rowHeight = 0.1753f, rowStep = 0.2002f;
            for (int i = 0; i < labels.Length; i++)
            {
                float y1 = 1f - i * rowStep;
                var rt = DuckUIBuilder.Frac(names[i], column, 0f, y1 - rowHeight, 1f, y1);

                // The shadow is a sibling under the plate rather than a child of it, so scaling the
                // plate on selection does not scale its own shadow with it — which would cancel
                // exactly the gap the lift is supposed to open.
                // Offsets are left at zero: MainMenu.ApplyPlates owns them, and its resting value is
                // MainMenu.shadowOffset. Two places writing the same rect is how a shadow ends up
                // hidden exactly behind its plate.
                var shadow = DuckUIBuilder.Frac($"{names[i]}Shadow", column, 0f, y1 - rowHeight, 1f, y1);
                DuckUIBuilder.AddImage(shadow, DuckUIBuilder.Spr("button_256"),
                                       new Color(0.10f, 0.08f, 0.06f, 0.38f), Image.Type.Sliced);
                shadow.SetAsFirstSibling();
                rt.SetAsLastSibling();

                var plate = DuckUIBuilder.AddImage(rt, DuckUIBuilder.Spr("button_256"), Color.white,
                                                   Image.Type.Sliced);

                // Centred on the plate's CREAM, which is not the centre of the plate. The sprite
                // spends its bottom 13 rows on a transparent drop shadow and the 15 above that on
                // the red lip, and its top 8 on margin plus 7 on the rim — so on a 96 px plate the
                // writable field runs from 28 px to 81 px, whose middle is 54.5 and not 48. The old
                // rect was centred on the rect and sat four and a half pixels low on a taller plate;
                // at 96 px that would have put the type onto the lip.
                var textRt = DuckUIBuilder.Frac("Label", rt, 0.10f, 0.24f, 0.90f, 0.88f);
                var text = DuckUIBuilder.AddText(textRt, labels[i], 44f, TextAlignmentOptions.Center,
                                                 Ink, 0.10f, false);
                text.fontStyle = FontStyles.Bold;
                text.characterSpacing = 6f;
                // AddText's default six pixels of vertical margin is right for a card of prose and
                // costs twelve pixels of a 61 px box here, which is enough to make TMP's autosizer
                // give up a point of type it does not need to. The horizontal margin is left alone.
                text.margin = new Vector4(10f, 3f, 10f, 3f);

                items[i] = new MainMenu.Item
                {
                    choice = choices[i],
                    rect = rt,
                    plate = plate,
                    label = text,
                    restTilt = tilts[i],
                    restShift = insets[i],
                    shadow = shadow
                };
            }

            // The chevron. Anchored to the column's own left edge rather than to a gutter inside it,
            // because unlike the two cards this column has the whole left margin of the frame to
            // stand in — 144 px of it at 16:9 against the 82 the marker wants. It is still driven
            // through MainMenu.MarkerScale, which is what keeps it on screen at 4:3 and narrower,
            // where 82 fixed pixels stop fitting inside 7.5% of a shrinking width.
            //
            // Brick rather than the cards' gold: this one is over grass and they are on a dark board.
            pointer = Marker("Pointer", column, 0f, 52f);
            var chevron = pointer.GetComponent<Image>();
            if (chevron != null) chevron.color = Brick;

            var hint = DuckUIBuilder.Frac("Hint", root, 0.075f, 0.055f, 0.335f, 0.105f);
            var hintText = DuckUIBuilder.AddText(hint, "ARROWS OR MOUSE  ·  ENTER TO CHOOSE", 19f,
                                                 TextAlignmentOptions.Left, Cream, 0.24f, false);
            hintText.fontStyle = FontStyles.Bold;

            return items;
        }

        /// <summary>
        /// What framing is on screen, for the review loop. Hidden outside the editor by MainMenu.
        /// </summary>
        static TMP_Text BuildFramingLabel(RectTransform root)
        {
            var rt = DuckUIBuilder.Frac("FramingLabel", root, 0.36f, 0.005f, 0.99f, 0.058f);
            var t = DuckUIBuilder.AddText(rt, "", 17f, TextAlignmentOptions.BottomRight,
                                          new Color(1f, 0.92f, 0.70f, 0.85f), 0.3f, false);
            t.fontStyle = FontStyles.Bold;
            return t;
        }

        /// <summary>
        /// THE CLASS LIST: the show's schedule, pinned up, with one plate per class.
        ///
        /// ---- why it is plates and not rows ----
        ///
        /// The other two cards are lines of type: a table of key bindings and a paragraph. Neither
        /// can be pressed, so neither needs to look pressable. This one is three CHOICES, and the
        /// game has already taught the player exactly what a choice looks like — a cream plate with
        /// dark type on it, lifting off a shadow, leaning half a degree off square. The same
        /// sprite, the same shadow rule and the same chevron are used here as on the front page,
        /// because a stage picker that invented its own idea of a button would be the first screen
        /// in the game that was not made by the same signwriter.
        ///
        /// ---- what is written on them ----
        ///
        /// Nothing here decides what a class is CALLED. The titles and the one-line descriptions
        /// are seeded from StageLauncher so the saved scene can be screenshotted in the editor, and
        /// MainMenu.LabelStages writes them again on load — so this build is a convenience and never
        /// an authority. The one thing this file does author is the ORDER, and with it the class
        /// numbers down the left: they are the show's running order, which is a fact about the
        /// schedule rather than about any one class, so it belongs to the board.
        /// </summary>
        static CanvasGroup BuildStagesCard(RectTransform root, MainMenu menu)
        {
            // Taller and a shade wider than the reading cards. Three 112-pixel plates and their
            // gaps do not fit in a board sized for seven lines of 24-point type, and shrinking them
            // to make them fit would have produced exactly the row of thin bars this card exists
            // not to be. The top stops at 68% of frame height, which is clear of the title board at
            // 74.5% — the class list must never cover the name of the show it belongs to.
            var content = BuildCard(root, "StagesCard", "THE CLASSES", out CanvasGroup group,
                                    "ARROWS  ·  ENTER TO GO  ·  ESC OR A CLICK OFF THE BOARD GOES BACK",
                                    "ONE CLASS, ENTERED ON ITS OWN",
                                    0.36f, 0.115f, 0.93f, 0.68f);

            var stages = new[] { StageId.LawnArt, StageId.GooseRally, StageId.BloomRush };
            // Same rule as the front page's column, and the same reason: three rows at one angle is
            // a form. Two leaning the same way and one against them is a schedule somebody pinned up.
            var tilts = new[] { -0.7f, -0.4f, 0.6f };
            var rows = new MainMenu.StageChoice[stages.Length];

            const float rowHeight = 0.30f, rowStep = 0.345f;
            for (int i = 0; i < stages.Length; i++)
            {
                float y1 = 1f - i * rowStep;
                string name = stages[i].ToString();

                // The shadow is a SIBLING under the plate rather than a child of it, so the plate
                // growing on selection does not grow its own shadow with it and cancel exactly the
                // gap the lift is meant to open. Offsets stay at zero here: MainMenu.ApplyStagePlates
                // owns them, and two places writing one rect is how a shadow ends up hidden behind
                // its plate.
                var shadow = DuckUIBuilder.Frac($"{name}Shadow", content, Gutter, y1 - rowHeight, 1f, y1);
                DuckUIBuilder.AddImage(shadow, DuckUIBuilder.Spr("button_256"),
                                       new Color(0.05f, 0.04f, 0.03f, 0.45f), Image.Type.Sliced);
                shadow.SetAsFirstSibling();

                var rt = DuckUIBuilder.Frac(name, content, Gutter, y1 - rowHeight, 1f, y1);
                rt.SetAsLastSibling();
                var plate = DuckUIBuilder.AddImage(rt, DuckUIBuilder.Spr("button_256"), Color.white,
                                                   Image.Type.Sliced);

                // ---- where the type sits on the plate, which is the fix for a line nobody could
                //      read ----
                //
                // A row plate is 112.6 px tall, and almost none of that is writable. The sprite is
                // 9-sliced with a 44 px bottom border and a 52 px top one, drawn at their own pixel
                // size, and inside those borders the artwork spends 13 rows on a transparent drop
                // shadow, 15 on the painted red lip, 7 on the top rim and 8 more on margin. So the
                // CREAM FIELD of a row runs from 28 px to 97.6 px above the rect's bottom edge and
                // is 69.6 px tall. Everything below 28 is lip and shadow.
                //
                // The one-liner used to be laid out in a rect from 8.4 px to 49.5 px, centred, which
                // put its baseline at 21.7 and its capitals from 21.7 to 36.8 — six and a half
                // pixels of every letter standing on the red lip. That is the "clipped by the bottom
                // edge" in the report, and it is not a clip at all: nothing is masked, the type is
                // simply drawn in the wrong place and the dark lip eats the bottom of it.
                //
                // So the two lines are laid out against the CREAM rather than against the rect. The
                // 69.6 px field carries a 40 pt title (30 px of capital) and a 21 pt one-liner
                // (15 px), which leaves 25 px to spend on three gaps — 8.5 px above the lip, 8.5 px
                // between the two lines, 8.5 px under the rim:
                //
                //     one-liner  baseline 36.5   capitals 36.5 .. 51.5   (rect 0.240 .. 0.540)
                //     title      baseline 60.1   capitals 60.1 .. 88.9   (rect 0.420 .. 0.890)
                //
                // The two rects overlap by 13 px and that is harmless — each holds one centred line
                // and the drawn letters are 8.5 px apart — but the leading between them is now a
                // number somebody chose rather than the residue of two rects that happened not to
                // collide. Both margins come down to 2 px so TMP's autosizer is not made to shrink
                // type that fits.
                //
                // The class number is raised with them, for no reason except that it is a third
                // piece of type on the same plate and it should look centred against the pair.

                // The class number, painted on the left of the plate the way a schedule numbers its
                // classes. It does not respond to selection: it is not part of the choice, it is
                // the position of the choice in the running order, and a number that lit up would
                // read as a second thing to press.
                var ordRt = DuckUIBuilder.Frac("Number", rt, 0.028f, 0.20f, 0.115f, 0.94f);
                var ord = DuckUIBuilder.AddText(ordRt, (i + 1).ToString(), 50f,
                                                TextAlignmentOptions.Center, Brick, 0.10f, false);
                ord.fontStyle = FontStyles.Bold;

                // Seeded from the launcher so the saved card is legible in the editor. Overwritten
                // on load — see the class comment.
                var titleRt = DuckUIBuilder.Frac("Title", rt, 0.135f, 0.42f, 0.97f, 0.89f);
                var title = DuckUIBuilder.AddText(titleRt, StageLauncher.TitleFor(stages[i]), 40f,
                                                  TextAlignmentOptions.Left, Ink, 0.10f, false);
                title.fontStyle = FontStyles.Bold;
                title.characterSpacing = 5f;
                title.margin = new Vector4(10f, 2f, 10f, 2f);

                // Seeded in WoodDark rather than Ink, so the saved scene shows the colour the game
                // will draw. MainMenu.ApplyStagePlates writes it again every frame from kickerIdle;
                // this is the same convenience-never-authority arrangement as the text itself.
                var kickRt = DuckUIBuilder.Frac("Kicker", rt, 0.14f, 0.24f, 0.97f, 0.54f);
                var kicker = DuckUIBuilder.AddText(kickRt, StageLauncher.KickerFor(stages[i]), 21f,
                                                   TextAlignmentOptions.Left, WoodDark, 0.08f, false);
                kicker.fontStyle = FontStyles.Bold;
                kicker.margin = new Vector4(10f, 2f, 10f, 2f);

                rows[i] = new MainMenu.StageChoice
                {
                    stage = stages[i],
                    rect = rt,
                    plate = plate,
                    title = title,
                    kicker = kicker,
                    restTilt = tilts[i],
                    shadow = shadow
                };
            }

            // The chevron, in the gutter the rows were inset to leave for it. Same sprite and same
            // driving as the front page's, so the marker the player learned on the column is the
            // marker they meet here. See Gutter for why the rows moved over.
            var pointer = Marker("Pointer", content, Gutter, 44f);

            menu.stageChoices = rows;
            menu.stagePointer = pointer;
            return group;
        }

        /// <summary>
        /// The width, as a fraction of a card's content, that the rows on it give up so their
        /// chevron has somewhere to stand.
        ///
        /// ---- the bug this exists for ----
        ///
        /// The chevron used to hang off the LEFT of a full-width row: anchored at the content's left
        /// edge with its pivot on its own right, 44 px wide and 22 px clear, so it occupied the 66
        /// pixels immediately before the rows started. On a 16:9 frame the class card's content
        /// begins 76.6 px inside the dark board and 66 px fits, with ten to spare.
        ///
        /// Ten pixels is not a margin, it is an accident, and it is aspect-dependent in the one
        /// direction the game will actually meet. The canvas scaler matches HEIGHT, so a narrower
        /// window does not scale the page down — it makes it NARROWER, and the 76.6 px inset is a
        /// percentage while the 66 px marker is not. At 4:3 the inset is 57.6 px, the marker is
        /// still 66, and the chevron is drawn eight pixels off the left edge of the board onto the
        /// grass. A button, outside the UI.
        ///
        /// The fix has two halves and needs both. MainMenu.MarkerScale brings the marker and its gap
        /// down together once the frame is narrower than 16:10, which keeps the ratio the layout was
        /// drawn at; and the rows give up this much of their width, which turns a gutter that existed
        /// by luck into one that is measured in the same currency as everything around it. Five
        /// percent is 47 px of a 941 px row at 16:9 — a plate a twentieth narrower, which nothing
        /// on it notices — and it is 47 px that shrinks with the window instead of standing still.
        /// </summary>
        const float Gutter = 0.05f;

        /// <summary>
        /// The chevron all three columns use, built once so they cannot drift apart.
        ///
        /// Anchored to the left edge of the ROWS — not of the card — with its pivot on its own right,
        /// so MainMenu can place it with a single anchoredPosition whose x is a gap and whose y is
        /// the selected row's own height. That is what lets it ride the selection springs instead of
        /// jumping between rows.
        ///
        /// icon_speed_128 is a double arrow with speed lines behind it, drawn for the results screen
        /// and never used there — it is exactly a pointer, and it already carries the game's paint.
        /// </summary>
        static RectTransform Marker(string name, RectTransform parent, float anchorX, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(anchorX, 0.5f);
            rt.anchorMax = new Vector2(anchorX, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var chevron = DuckUIBuilder.AddImage(rt, DuckUIBuilder.Spr("icon_speed_128"), Gold);
            chevron.preserveAspect = true;
            return rt;
        }

        /// <summary>
        /// THE SETTINGS BOARD: how loud the show is, and nothing else.
        ///
        /// ---- why it is two rows and not a page ----
        ///
        /// A settings screen is judged by whether every control on it does something, and this game
        /// has exactly one subsystem with a dial the player is entitled to: MasterAudio. There is no
        /// mixer, so there are no music and effects sliders to offer that would not both be the same
        /// slider wearing two labels (MasterAudio's own class comment explains why the mixer does not
        /// exist). There is no quality setting worth exposing in a WebGL build tuned for one target,
        /// and no key rebinding to hand out when every screen in the game polls two devices by hand.
        /// Two honest rows beat six, four of which lie.
        ///
        /// ---- why they are type and not plates ----
        ///
        /// The class list is cream plates because every row on it is a CHOICE, and the front page
        /// has already taught the player what a choice looks like. These are dials that hold a
        /// value. Dressing a dial as a button promises a press that does not exist — Enter on MASTER
        /// VOLUME cannot do anything sensible — so this board is laid out like CONTROLS, a name in
        /// cream and a reading in gold on the dark card, with the chevron and the colour lift
        /// carrying which row is live. See MainMenu.SettingRow.
        ///
        /// ---- the bar ----
        ///
        /// The trough and its fill are the HUD's boost gauge sprites and its exact construction, down
        /// to the fill's inset inside the trough, because a second idea of what a bar looks like is
        /// the sort of thing that gets noticed one screen later. Its fillAmount is the SLIDER
        /// POSITION and not the amplitude — MainMenu.ApplySettings has the arithmetic and the reason.
        /// The two arrow-heads either side are ASCII, and that is not laziness: the font atlas is
        /// asked for glyphs it may not carry at its peril, and a missing one is a blank rectangle in
        /// the middle of a control.
        /// </summary>
        static CanvasGroup BuildSettingsCard(RectTransform root, MainMenu menu)
        {
            var content = BuildCard(root, "SettingsCard", "SETTINGS", out CanvasGroup group,
                                    "UP DOWN TO PICK  ·  LEFT RIGHT TO CHANGE  ·  " +
                                    "ESC OR A CLICK OFF THE BOARD GOES BACK",
                                    "HOW LOUD THE SHOW IS, AND HOW MUCH IT SHAKES",
                                    // Grown from 0.15..0.655 to fit a third row. Split evenly top and
                                    // bottom rather than taken from one end, so the board stays where
                                    // the player's eye already expects it on a card they have opened
                                    // before. The content rect is 0.615 of the card, so this buys
                                    // about sixty pixels of usable height.
                                    0.36f, 0.105f, 0.92f, 0.70f);

            // The card is now 643 px tall and its content 395, carrying three rows and a bar.
            //
            // The gaps stay uneven on purpose, and the reason survives the extra row: 27 px between
            // MASTER VOLUME and its own bar, 45 px between that bar and MUTE. A settings board where
            // every gap is equal is a board on which nothing tells you which control the bar belongs
            // to. RUMBLE sits 30 px under MUTE — closer to it than the bar is to either — because
            // they are the two switches and reading them as a pair is worth more than an even column.
            //
            // The BAR's height is the one figure here that is not free. Its trough is 0.62 of the
            // bar rect and the artwork is 48 px tall nine-sliced, so the bar has to stay at 77 px or
            // the rounded ends stop being the shape they were painted. The rows absorbed the change
            // instead, coming down from 80 px to 72.
            var rows = new MainMenu.SettingRow[3];

            rows[0] = SettingLine(content, "Volume", MainMenu.Setting.MasterVolume,
                                  "MASTER VOLUME", "100%", 0.818f, 1.000f);

            // The bar is a SIBLING of the volume row rather than a child of it, so the row's rect
            // stays the label line. That is what puts the chevron beside the words instead of
            // halfway down the empty space under them, and it is why MainMenu hit-tests volumeBar
            // separately — the bar is not inside the row it belongs to.
            var bar = DuckUIBuilder.Frac("VolumeBar", content, Gutter, 0.555f, 1f, 0.750f);

            // richText OFF on both, and that is a real hazard rather than a precaution. TMP reads a
            // less-than sign as the opening of a markup tag; a lone "<" survives it today because
            // the parser gives up and prints what it could not understand, which is behaviour to
            // rely on in a control the player is looking at only if you enjoy surprises. Nothing on
            // this board is marked up, so the parser has no business being on at all.
            var leftRt = DuckUIBuilder.Frac("Less", bar, 0f, 0.18f, 0.045f, 0.82f);
            var less = DuckUIBuilder.AddText(leftRt, "<", 30f, TextAlignmentOptions.Center, Gold, 0.18f, false);
            less.fontStyle = FontStyles.Bold;
            less.margin = new Vector4(2f, 2f, 2f, 2f);
            less.richText = false;

            var rightRt = DuckUIBuilder.Frac("More", bar, 0.955f, 0.18f, 1f, 0.82f);
            var more = DuckUIBuilder.AddText(rightRt, ">", 30f, TextAlignmentOptions.Center, Gold, 0.18f, false);
            more.fontStyle = FontStyles.Bold;
            more.margin = new Vector4(2f, 2f, 2f, 2f);
            more.richText = false;

            // 47.8 px of trough against a sprite that is 48 px tall and 9-sliced with 42 px of that
            // in its two vertical borders. Drawn at its own height, in other words, which is the one
            // height at which the rounded ends of the artwork are the shape they were painted.
            var track = DuckUIBuilder.Frac("Track", bar, 0.055f, 0.19f, 0.945f, 0.81f);
            DuckUIBuilder.AddImage(track, DuckUIBuilder.Spr("progress_bar_bg_256"), Color.white,
                                   Image.Type.Sliced);

            var fillRt = DuckUIBuilder.Frac("Fill", track, 0.015f, 0.16f, 0.985f, 0.84f);
            var fill = DuckUIBuilder.AddImage(fillRt, DuckUIBuilder.Spr("progress_bar_fill_256"),
                                              new Color(1f, 0.82f, 0.42f));
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 1f;

            rows[1] = SettingLine(content, "Mute", MainMenu.Setting.Mute, "MUTE", "OFF",
                                  0.259f, 0.441f);

            // The pad's own switch. Reads OFF here and is overwritten on the first ApplySettings
            // from Haptics.Enabled, which is loaded from PlayerPrefs before any scene — the same
            // reason MASTER VOLUME is built saying 100% and then corrected.
            rows[2] = SettingLine(content, "Rumble", MainMenu.Setting.Rumble, "RUMBLE", "ON",
                                  0.001f, 0.183f);

            menu.settingRows = rows;
            menu.settingsPointer = Marker("Pointer", content, Gutter, 44f);
            menu.volumeBar = bar;
            menu.volumeFill = fill;
            menu.volumeLeftMark = less;
            menu.volumeRightMark = more;
            return group;
        }

        /// <summary>
        /// One line of the settings board: a name on the left, a reading on the right.
        ///
        /// The split is the CONTROLS card's, at 62/64 rather than its 54, because a reading is two
        /// or four characters against a key binding's twenty and the name deserves the room. The
        /// reading is set larger than the name for the same reason a scoreboard is: it is the thing
        /// that changes, and it has to be findable from across the frame while a hand is on the
        /// arrow keys rather than on the page.
        /// </summary>
        static MainMenu.SettingRow SettingLine(RectTransform content, string name,
                                               MainMenu.Setting setting, string label, string seed,
                                               float y0, float y1)
        {
            var row = DuckUIBuilder.Frac(name, content, Gutter, y0, 1f, y1);

            var labelRt = DuckUIBuilder.Frac("Name", row, 0f, 0f, 0.62f, 1f);
            var text = DuckUIBuilder.AddText(labelRt, label, 27f, TextAlignmentOptions.Left,
                                             Cream, 0.16f, false);
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 4f;

            // Seeded so the saved card can be looked at in the editor, and overwritten every frame
            // from MasterAudio — the same convenience-never-authority rule the class list's titles
            // follow, and for the same reason: a number baked into a scene is a number free to
            // disagree with the system it describes.
            var valueRt = DuckUIBuilder.Frac("Value", row, 0.64f, 0f, 1f, 1f);
            var value = DuckUIBuilder.AddText(valueRt, seed, 32f, TextAlignmentOptions.Right,
                                              Gold, 0.16f, false);
            value.fontStyle = FontStyles.Bold;

            return new MainMenu.SettingRow
            {
                setting = setting,
                rect = row,
                label = text,
                value = value
            };
        }

        static CanvasGroup BuildControlsCard(RectTransform root)
        {
            var content = BuildCard(root, "ControlsCard", "CONTROLS", out CanvasGroup group);

            var rows = new[]
            {
                ("DRIVE", "W A S D  /  ARROWS"),
                ("BOOST", "SHIFT"),
                ("HANDBRAKE SLIDE", "SPACE"),
                ("HORN", "E"),
                ("LOOK AT YOUR WORK", "F   (once a round)"),
                // "RETRY · NEXT PICTURE — R · N" stood here and both halves were dead. N went with
                // the new-picture feature; R went with the verdict's retry. What R still does is on
                // the STANDINGS board and it is a different thing entirely — it throws the whole
                // championship away and starts a new one — so it is named for that or not at all.
                // A front page that lists a key by the wrong job is worse than one that omits it.
                ("START THE EVENING AGAIN", "R   (at the standings)"),
                ("SKIP THE OPENING", "ANY KEY"),
            };

            const float top = 1f;
            float rowH = 1f / rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                float y1 = top - i * rowH;
                var row = DuckUIBuilder.Frac($"Row{i}", content, 0f, y1 - rowH + 0.012f, 1f, y1 - 0.012f);

                var what = DuckUIBuilder.Frac("What", row, 0f, 0f, 0.54f, 1f);
                DuckUIBuilder.AddText(what, rows[i].Item1, 24f, TextAlignmentOptions.Left,
                                      Cream, 0.16f, false).fontStyle = FontStyles.Bold;

                var key = DuckUIBuilder.Frac("Key", row, 0.54f, 0f, 1f, 1f);
                DuckUIBuilder.AddText(key, rows[i].Item2, 24f, TextAlignmentOptions.Right,
                                      Gold, 0.16f, false).fontStyle = FontStyles.Bold;
            }

            return group;
        }

        static CanvasGroup BuildCreditsCard(RectTransform root)
        {
            var content = BuildCard(root, "CreditsCard", "CREDITS", out CanvasGroup group);

            var lines = new[]
            {
                "A duck on a ride-on mower carves giant pictures into a",
                "county-fair lawn while animals watch and judges score it.",
                "",
                "Built in Unity 6 and the Universal Render Pipeline,",
                "for a browser. Characters and props sculpted in Blender.",
                "Every note and sound effect synthesised for this game.",
            };

            float rowH = 1f / lines.Length;
            for (int i = 0; i < lines.Length; i++)
            {
                float y1 = 1f - i * rowH;
                var row = DuckUIBuilder.Frac($"Line{i}", content, 0f, y1 - rowH, 1f, y1);
                DuckUIBuilder.AddText(row, lines[i], 24f, TextAlignmentOptions.Center,
                                      i < 2 ? Cream : new Color(0.86f, 0.82f, 0.72f), 0.16f, false);
            }

            return group;
        }

        /// <summary>
        /// A dark plate with a heading, a body area and a way out. All three cards are the same
        /// object with different rows in them, so they cannot drift apart.
        ///
        /// The footer is a PARAMETER rather than a constant, and that is not tidiness — it is the
        /// only honest way to have three cards on one mechanism. CONTROLS and CREDITS really are
        /// dismissed by pressing anything, and the class list really is not, because up, down and
        /// Enter mean something on it. A shared footer would have to be wrong on one card or the
        /// other, and the card it was wrong on is the card with a way out the player has to find.
        ///
        /// The rect is a parameter for a duller reason: the class list carries three plates rather
        /// than seven lines of type and needs a taller board to do it without shrinking them.
        /// </summary>
        static RectTransform BuildCard(RectTransform root, string name, string heading,
                                       out CanvasGroup group,
                                       string footerText = "PRESS ANYTHING TO GO BACK",
                                       string subheading = null,
                                       float x0 = 0.36f, float y0 = 0.14f,
                                       float x1 = 0.92f, float y1 = 0.66f)
        {
            var card = DuckUIBuilder.Frac(name, root, x0, y0, x1, y1);
            group = card.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            // The card's own painted margin, asked for rather than approximated. The fractions below
            // are the old ones re-expressed against the writable field, so the layout is where it
            // always was — except at the two edges where it was not: the heading's top sat 22 px
            // down on a card whose rule is at 32, and the footer's baseline sat 25 up on one whose
            // rule is at 38. Both were printing on the frame. See DuckMow.UI.CardArt.
            var field = DuckUIBuilder.Plate(card, DuckUIBuilder.CardDark);

            var title = DuckUIBuilder.Frac("Heading", field, 0.027f, 0.88f, 0.973f, 1f);
            var t = DuckUIBuilder.AddText(title, heading, 34f, TextAlignmentOptions.Center, Gold, 0.22f, false);
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 8f;

            // A subheading, where there is one, is a line UNDER the gold — quieter, and in the
            // body's cream rather than the heading's gold, so it reads as the sentence that
            // explains the heading instead of as a second heading.
            if (!string.IsNullOrEmpty(subheading))
            {
                var subRt = DuckUIBuilder.Frac("Subheading", field, 0.027f, 0.826f, 0.973f, 0.878f);
                DuckUIBuilder.AddText(subRt, subheading, 21f, TextAlignmentOptions.Center,
                                      new Color(0.86f, 0.82f, 0.72f), 0.16f, false)
                    .fontStyle = FontStyles.Bold;
            }

            var footer = DuckUIBuilder.Frac("Footer", field, 0.027f, 0f, 0.973f, 0.078f);
            DuckUIBuilder.AddText(footer, footerText, 18f, TextAlignmentOptions.Center,
                                  new Color(0.80f, 0.76f, 0.66f), 0.18f, false).fontStyle = FontStyles.Bold;

            card.gameObject.SetActive(false);   // MainMenu switches it on when it is asked for
            // The body stops short of the subheading where there is one, so the two cannot sit on
            // top of each other. Every card without one keeps the height it always had.
            float top = string.IsNullOrEmpty(subheading) ? 0.838f : 0.808f;
            return DuckUIBuilder.Frac("Content", field, 0.037f, 0.105f, 0.963f, top);
        }

        // ------------------------------------------------------------------ audio

        /// <summary>
        /// The fiddle tune and the four wooden UI notes.
        ///
        /// music_menu_loop has been in the bank since the audio pass and had nowhere to play until
        /// now; the spec's own mix figures are used rather than a guess, so the menu sits at the same
        /// level as the rest of the game.
        /// </summary>
        static void BuildAudio(MainMenu menu, Camera cam)
        {
            var music = cam.gameObject.AddComponent<AudioSource>();
            music.clip = Clip("Music/music_menu_loop");
            music.loop = true;
            music.playOnAwake = false;      // MainMenu starts it, so a paused editor scene is silent
            music.volume = 0.203f;
            music.spatialBlend = 0f;
            menu.music = music;

            var sfx = cam.gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.loop = false;
            sfx.spatialBlend = 0f;
            menu.sfx = sfx;

            menu.hoverClip = Clip("UI/ui_hover");
            menu.clickClip = Clip("UI/ui_click");
            menu.confirmClip = Clip("UI/ui_confirm");
            menu.backClip = Clip("UI/ui_back");
        }

        static AudioClip Clip(string path)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{path}.wav");
            if (c == null) Debug.LogWarning($"[Duck] menu audio clip missing: {path}");
            return c;
        }

        // ------------------------------------------------------------------ build settings

        /// <summary>
        /// Menu first, game second.
        ///
        /// This is the single authority on scene order, called by the menu builder, by the project
        /// settings pass and by the player build, because the order is what decides which scene a
        /// WebGL build opens on and it is far too easy to lose: rebuilding the game scene used to
        /// insert it at index 0 unconditionally, which silently put the player back into the round.
        /// </summary>
        public static void RegisterBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            // Every scene this method is about to insert must first be removed, INCLUDING the rally.
            //
            // It was missing from this list while being inserted below, so each call appended one
            // more copy of GooseRally. By the time anybody looked, the build settings held
            // thirty-seven of it — a list that ships the same scene dozens of times and pushes every
            // index after it around. Nothing complains: duplicates are legal, so the only symptom is
            // a bloated build and scene indices that mean nothing, both of which only show up in the
            // browser. Insert-after-remove is only idempotent if the remove covers the same set.
            scenes.RemoveAll(s => s.path == ScenePath ||
                                  s.path == DuckSceneBuilder.ScenePath ||
                                  s.path == DuckArenaBuilder.ScenePath ||
                                  s.path == DuckRallyBuilder.ScenePath);

            // Belt and braces: strip any duplicate that got in some other way, keeping the first.
            var seen = new HashSet<string>();
            scenes.RemoveAll(s => !seen.Add(s.path));

            int at = 0;
            // Only ever list a scene that is on disk. A build settings entry pointing at a missing
            // file fails the player build outright, and on a fresh clone neither has been built yet.
            if (File.Exists(ScenePath))
                scenes.Insert(at++, new EditorBuildSettingsScene(ScenePath, true));
            if (File.Exists(DuckSceneBuilder.ScenePath))
                scenes.Insert(at++, new EditorBuildSettingsScene(DuckSceneBuilder.ScenePath, true));
            // The defence arena, after the two the game can open on. Order matters only for index 0 —
            // which scene the WebGL build starts in — but it is listed last anyway because it is only
            // ever reached from a round in progress, never cold. Registered here rather than in its own
            // builder so there is exactly one place that decides what ships.
            if (File.Exists(DuckArenaBuilder.ScenePath))
                scenes.Insert(at, new EditorBuildSettingsScene(DuckArenaBuilder.ScenePath, true));
            if (File.Exists(DuckRallyBuilder.ScenePath))
                scenes.Insert(at, new EditorBuildSettingsScene(DuckRallyBuilder.ScenePath, true));

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// The scenes a player build ships, in the order it opens them.
        ///
        /// The rally is in here, and it has to be. A scene that is only ever entered mid-round is
        /// easy to forget, and forgetting it does not fail the build — it fails in the BROWSER,
        /// three rounds in, as a LoadSceneAsync that returns null and a championship that skips
        /// straight from the klaxon to the reveal with a warning nobody is looking at the console
        /// for. This list is the one place that decides what ships; if a scene can be reached at
        /// runtime it belongs here whether or not the game can open on it.
        ///
        /// Bloom Rush is here for exactly that rule, and it was missing. TurfStage.Run loads
        /// BloomRush.unity additively on round three and has a guard for the load coming back null
        /// that logs "is it in the build settings?" and lets the round carry on without the stage —
        /// which is the right thing to do to a shipped player and the worst possible thing for
        /// noticing, because the only symptom is a championship that quietly has two stages in it
        /// instead of three. The stage was reachable at runtime and absent from the build, so it
        /// never once ran in a browser.
        ///
        /// Arena.unity is still deliberately NOT here. It is a standalone review scene opened by
        /// hand from the Duck menu, nothing in the game loads it, and a scene with no runtime path
        /// to it is weight in the download rather than a stage somebody might reach.
        /// </summary>
        public static string[] PlayerScenes()
        {
            var list = new List<string>(4);
            if (File.Exists(ScenePath)) list.Add(ScenePath);
            else Debug.LogWarning("[Duck] Menu.unity has not been built, so this player will open " +
                                  "straight into a round. Run Duck/4 · Build Menu Scene.");
            list.Add(DuckSceneBuilder.ScenePath);
            if (File.Exists(DuckRallyBuilder.ScenePath)) list.Add(DuckRallyBuilder.ScenePath);
            else Debug.LogWarning("[Duck] GooseRally.unity has not been built, so the final round " +
                                  "will go straight to the reveal. Run Duck/3 · Build goose rally scene.");
            if (File.Exists(DuckTurfBuilder.ScenePath)) list.Add(DuckTurfBuilder.ScenePath);
            else Debug.LogWarning("[Duck] BloomRush.unity has not been built, so round three will " +
                                  "skip the stage and go on to the reveal. Run Duck/4 · Build bloom " +
                                  "rush scene.");
            return list.ToArray();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
