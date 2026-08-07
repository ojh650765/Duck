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
                    yawSwing = 2f, height = 4.6f, heightSwing = 0.3f, cycle = 30f,
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
            DuckUIBuilder.AddImage(card, DuckUIBuilder.Spr("panel_card_256"), Color.white, Image.Type.Sliced);

            var mastheadRt = DuckUIBuilder.Frac("Masthead", card, 0.05f, 0.10f, 0.95f, 0.94f);
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

            // The class, on a ribbon pinned across the board's bottom right and running off its edge.
            // The show is on the masthead and the class is underneath it in small type, which is how a
            // horticultural show bill reads and is the whole point of the event being GARDENER OF THE
            // YEAR with lawn art as one class inside it.
            var ribbon = DuckUIBuilder.Frac("Ribbon", group, 0.55f, -0.30f, 1.06f, 0.14f);
            DuckUIBuilder.AddImage(ribbon, DuckUIBuilder.Spr("banner_ribbon_512"), Color.white, Image.Type.Sliced);
            // The ribbon's writable cream field is only the middle third of the sprite; the rest is
            // stripe, tails and transparent margin. These fractions are the ones the HUD measured off
            // the artwork — text laid out against the whole rect sits outside the plate.
            var sub = DuckUIBuilder.Frac("Class", ribbon, 0.25f, 0.40f, 0.75f, 0.68f);
            var subText = DuckUIBuilder.AddText(sub, "LAWN ART CLASS", 26f,
                                                TextAlignmentOptions.Center, Brick, 0.14f, false);
            subText.fontStyle = FontStyles.Bold;

            // Landing order: the board with the name, then the rosette pinned on, then the ribbon.
            return new[] { card, rosette, ribbon };
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
        /// Three plates down the left, clear of both the mower on the right and the picture behind
        /// them, with a chevron pointing at whichever is selected and the hint under them.
        ///
        /// Left rather than centred because the middle of this frame is the mown picture, and that is
        /// the one thing the menu exists to show. The column stops at 30.5% across and the picture
        /// starts at 33%, which is the constraint that fixes its width.
        /// </summary>
        static MainMenu.Item[] BuildChoices(RectTransform root, out RectTransform pointer)
        {
            var column = DuckUIBuilder.Frac("Choices", root, 0.075f, 0.115f, 0.305f, 0.50f);

            var labels = new[] { "PLAY", "CONTROLS", "CREDITS" };
            var choices = new[] { MainMenu.Choice.Play, MainMenu.Choice.Controls, MainMenu.Choice.Credits };
            // Nothing is on a perfect grid except the mowing stripes. Three plates on an exact
            // vertical at an exact tilt is the one arrangement that reads as a web form, so each one
            // sits at its own angle and its own inset — small numbers, all under a degree and a half.
            var tilts = new[] { -1.1f, 0.8f, -0.5f };
            var insets = new[] { 0f, 9f, 3f };
            var items = new MainMenu.Item[labels.Length];

            const float rowHeight = 0.28f, rowStep = 0.34f;
            for (int i = 0; i < labels.Length; i++)
            {
                float y1 = 1f - i * rowStep;
                var rt = DuckUIBuilder.Frac(labels[i], column, 0f, y1 - rowHeight, 1f, y1);

                // The shadow is a sibling under the plate rather than a child of it, so scaling the
                // plate on selection does not scale its own shadow with it — which would cancel
                // exactly the gap the lift is supposed to open.
                // Offsets are left at zero: MainMenu.ApplyPlates owns them, and its resting value is
                // MainMenu.shadowOffset. Two places writing the same rect is how a shadow ends up
                // hidden exactly behind its plate.
                var shadow = DuckUIBuilder.Frac($"{labels[i]}Shadow", column, 0f, y1 - rowHeight, 1f, y1);
                DuckUIBuilder.AddImage(shadow, DuckUIBuilder.Spr("button_256"),
                                       new Color(0.10f, 0.08f, 0.06f, 0.38f), Image.Type.Sliced);
                shadow.SetAsFirstSibling();
                rt.SetAsLastSibling();

                var plate = DuckUIBuilder.AddImage(rt, DuckUIBuilder.Spr("button_256"), Color.white,
                                                   Image.Type.Sliced);

                var textRt = DuckUIBuilder.Frac("Label", rt, 0.10f, 0.16f, 0.90f, 0.88f);
                var text = DuckUIBuilder.AddText(textRt, labels[i], 44f, TextAlignmentOptions.Center,
                                                 Ink, 0.10f, false);
                text.fontStyle = FontStyles.Bold;
                text.characterSpacing = 6f;

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

            // The chevron. icon_speed_128 is a double arrow with speed lines behind it, drawn for the
            // results screen and never used there — it is exactly a pointer, and it already carries
            // the game's paint.
            //
            // Anchored to the column's left edge with its pivot on its own right, so MainMenu can
            // drive it with one anchoredPosition: x is a fixed gap and y is the selected plate's own
            // local height, which is what lets it ride the plate springs instead of jumping.
            var go = new GameObject("Pointer", typeof(RectTransform));
            pointer = (RectTransform)go.transform;
            pointer.SetParent(column, false);
            pointer.anchorMin = new Vector2(0f, 0.5f);
            pointer.anchorMax = new Vector2(0f, 0.5f);
            pointer.pivot = new Vector2(1f, 0.5f);
            pointer.sizeDelta = new Vector2(52f, 52f);
            var chevron = DuckUIBuilder.AddImage(pointer, DuckUIBuilder.Spr("icon_speed_128"), Brick);
            chevron.preserveAspect = true;

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
                ("RETRY  ·  NEXT PICTURE", "R  ·  N"),
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
        /// A dark plate with a heading, a body area and a way out. Both cards are the same object
        /// with different rows in them, so they cannot drift apart.
        /// </summary>
        static RectTransform BuildCard(RectTransform root, string name, string heading,
                                       out CanvasGroup group)
        {
            var card = DuckUIBuilder.Frac(name, root, 0.36f, 0.14f, 0.92f, 0.66f);
            group = card.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            DuckUIBuilder.AddImage(card, DuckUIBuilder.Spr("panel_card_dark_256"), Color.white,
                                   Image.Type.Sliced);

            var title = DuckUIBuilder.Frac("Heading", card, 0.06f, 0.84f, 0.94f, 0.96f);
            var t = DuckUIBuilder.AddText(title, heading, 34f, TextAlignmentOptions.Center, Gold, 0.22f, false);
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 8f;

            var footer = DuckUIBuilder.Frac("Footer", card, 0.06f, 0.045f, 0.94f, 0.135f);
            DuckUIBuilder.AddText(footer, "PRESS ANYTHING TO GO BACK", 18f, TextAlignmentOptions.Center,
                                  new Color(0.80f, 0.76f, 0.66f), 0.18f, false).fontStyle = FontStyles.Bold;

            card.gameObject.SetActive(false);   // MainMenu switches it on when it is asked for
            return DuckUIBuilder.Frac("Content", card, 0.07f, 0.16f, 0.93f, 0.80f);
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
