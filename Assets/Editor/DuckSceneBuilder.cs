using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Builds the whole game scene from scratch, deterministically.
    ///
    /// The scene is treated as generated output rather than a hand-edited document: every prop,
    /// light and system is placed by this script. That makes art direction changes cheap (edit a
    /// number, rebuild, look) and means the layout can be regenerated after any asset swap
    /// without hunting through the hierarchy.
    /// </summary>
    public static class DuckSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        const string MatDir = "Assets/Materials";
        const string SettingsDir = "Assets/Settings";

        // ------------------------------------------------------------------ menu

        [MenuItem("Duck/1 · Import TMP Essentials", priority = 0)]
        public static void ImportTmp()
        {
            const string guidPath = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
            string full = Path.GetFullPath(guidPath);
            if (!File.Exists(full))
            {
                Debug.LogWarning($"[Duck] TMP essentials not found at {guidPath}");
                return;
            }
            AssetDatabase.ImportPackage(full, false);
            Debug.Log("[Duck] Imported TMP essential resources.");
        }

        [MenuItem("Duck/2 · Build Materials", priority = 1)]
        public static void BuildMaterialsMenu()
        {
            BuildMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log("[Duck] Materials built.");
        }

        [MenuItem("Duck/3 · Build Main Scene", priority = 2)]
        public static void BuildSceneMenu()
        {
            BuildMaterials();
            DuckUIBuilder.ImportSprites();
            BuildScene();
        }

        // ------------------------------------------------------------------ palette

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }
        /// <summary>Palette colours are authored as sRGB hex; shaders want linear.</summary>
        public static Color HexL(string hex) => Hex(hex).linear;

        public static class P
        {
            public const string UncutBase = "#2E7331";
            public const string UncutTip = "#55A542";
            public const string CutBase = "#77AC31";
            public const string CutTip = "#B8DA52";
            public const string CutEdge = "#1B5220";
            public const string Track = "#618E2E";

            public const string DuckCream = "#F6EBD2";
            public const string DuckShadow = "#DCC9A4";
            public const string Bill = "#F9A331";
            public const string BillShadow = "#D3792A";

            public const string MowerRed = "#DD3F38";
            public const string MowerDeepRed = "#A32E2D";
            public const string MowerCream = "#F4E7CF";
            public const string EngineGrey = "#4A4F55";
            public const string Brass = "#C9A55A";

            public const string FenceWhite = "#F1EDE0";
            public const string TentRed = "#DF4B45";
            public const string TentCream = "#F5EAD6";
            public const string WoodWarm = "#9A6B41";
            public const string WoodDark = "#6E4A2C";
            public const string Pond = "#328DB6";
            public const string PondShallow = "#68B0C4";
            public const string Chalk = "#F7F3E4";
            public const string Hedge = "#286330";
            public const string Dirt = "#B99A6B";

            public const string SkyZenith = "#3F90DA";
            public const string SkyHorizon = "#CFE7F2";
            public const string SunDisc = "#FFF3D0";
            public const string Hills = "#7DB08A";
            public const string Haze = "#BFE3F0";
        }

        // ------------------------------------------------------------------ materials

        public static Material EnsureMaterial(string name, string shaderName)
        {
            EnsureFolder(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[Duck] Shader not found: {shaderName}");
                return mat;
            }
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        /// <summary>A plain URP/Lit prop material in the game's palette.</summary>
        public static Material EnsureLit(string name, string hex, float smoothness = 0.22f, float metallic = 0f)
        {
            var m = EnsureMaterial(name, "Duck/Prop");
            if (m == null) return null;
            m.SetColor("_BaseColor", HexL(hex));
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetColor("_ShadowTint", HexL("#8CA8D6"));
            m.SetColor("_RimColor", HexL("#FFF3D8"));
            // Props are solid painted objects in a bright outdoor set, not studies in chiaroscuro.
            // The sun crosses the arena from the south, so the judges' stand — the one set the
            // game pushes in on — is lit entirely from behind, and at the shader's default wrap
            // every surface the judging camera can see fell to near black. Wrapping the diffuse
            // most of the way round keeps a shaded face reading as its own colour, slightly
            // darker, which is how painted props behave in this kind of picture book set. Form
            // still comes from the wrap gradient and the rim, not from the terminator.
            m.SetFloat("_Wrap", 0.65f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void BuildMaterials()
        {
            EnsureFolder(MatDir);

            var ground = EnsureMaterial("M_GrassGround", "Duck/GrassGround");
            if (ground != null)
            {
                ground.SetColor("_UncutBase", HexL(P.UncutBase));
                ground.SetColor("_UncutTip", HexL(P.UncutTip));
                ground.SetColor("_CutBase", HexL(P.CutBase));
                ground.SetColor("_CutTip", HexL(P.CutTip));
                ground.SetColor("_EdgeColor", HexL(P.CutEdge));
                ground.SetColor("_TrackColor", HexL(P.Track));
                ground.SetFloat("_MottleAmount", 0.66f);
                ground.SetFloat("_Wrap", 0.16f);
                ground.SetFloat("_StripeAmount", 0.30f);
                ground.SetFloat("_MottleScale", 0.075f);
                EditorUtility.SetDirty(ground);
            }

            var blades = EnsureMaterial("M_GrassBlades", "Duck/GrassBlades");
            if (blades != null)
            {
                blades.SetColor("_UncutBase", HexL(P.UncutBase));
                blades.SetColor("_UncutTip", HexL("#5FB244"));
                blades.SetColor("_CutBase", HexL(P.CutBase));
                blades.SetColor("_CutTip", HexL("#C2E055"));
                blades.SetColor("_Translucency", HexL("#A6E84A"));
                blades.SetFloat("_Wrap", 0.18f);
                blades.SetFloat("_AO", 0.50f);
                EditorUtility.SetDirty(blades);
            }

            var chalk = EnsureMaterial("M_ChalkGuide", "Duck/ChalkGuide");
            if (chalk != null)
            {
                chalk.SetColor("_ChalkColor", HexL(P.Chalk));
                chalk.SetFloat("_GhostAmount", 0f);
                chalk.SetFloat("_AnalysisAmount", 0f);
                chalk.SetFloat("_LineAlpha", 0.60f);
                chalk.SetFloat("_Patchiness", 0.42f);
                chalk.SetFloat("_LineWidth", 0.34f);
                EditorUtility.SetDirty(chalk);
            }

            var sky = EnsureMaterial("M_Sky", "Duck/SkyGradient");
            if (sky != null)
            {
                sky.SetColor("_Zenith", HexL(P.SkyZenith));
                sky.SetColor("_Mid", HexL("#6BB0E8"));
                sky.SetColor("_Horizon", HexL(P.SkyHorizon));
                sky.SetColor("_GroundCol", HexL("#4E8A44"));
                sky.SetColor("_SunColor", HexL(P.SunDisc));
                EditorUtility.SetDirty(sky);
            }

            // Prop palette. Everything in the world uses one of these so the whole set reads
            // as one art direction and the SRP batcher has an easy time.
            // The surround is short mown grass continuing out from the playfield, so it takes the
            // cut-grass hue rather than a separate "apron" colour that draws a rectangle around
            // the picture. Wrap is high and rim is off so a 300 m plane stays flat and calm.
            // The meadow and the aprons: grass to look at, with none of the lawn's machinery.
            //
            // These were a flat prop colour, which is most of why every overhead shot of the venue
            // sat on a dead green sheet — from the reveal and the tour cameras this material is the
            // majority of the frame. It now uses the plain grass shader: the lawn's mottling and
            // palette, no cut mask, no stripes, no blades.
            var meadow = EnsureMaterial("M_Apron", "Duck/GrassPlain");
            if (meadow != null)
            {
                meadow.SetColor("_UncutBase", HexL("#3E8A34"));
                meadow.SetColor("_UncutTip", HexL("#6FBE4C"));
                meadow.SetColor("_PatchDark", HexL("#2C6B2A"));
                meadow.SetColor("_DryTint", HexL("#8FA84A"));
                meadow.SetFloat("_MottleScale", 0.055f);
                meadow.SetFloat("_MottleAmount", 0.75f);
                // ~90 m fields and ~55 m dry ground, so the meadow has areas rather than a wash.
                meadow.SetFloat("_PatchScale", 0.011f);
                meadow.SetFloat("_PatchAmount", 0.50f);
                meadow.SetFloat("_DryAmount", 0.30f);
                // Metre-scale speckle: the part that stops it looking like paper at eye level.
                meadow.SetFloat("_GrainScale", 0.8f);
                meadow.SetFloat("_GrainAmount", 0.13f);
                meadow.SetFloat("_Wrap", 0.38f);
                meadow.SetFloat("_OldStripe", 0.055f);
                meadow.enableInstancing = true;
                EditorUtility.SetDirty(meadow);
            }

            var apron = EnsureLit("M_ApronProp", "#4E9E2E", 0.10f);
            if (apron != null)
            {
                apron.SetFloat("_Wrap", 0.42f);
                apron.SetFloat("_RimStrength", 0f);
                EditorUtility.SetDirty(apron);
            }
            EnsureLit("M_ApronOuter", "#4E9E2E", 0.10f);
            EnsureLit("M_Dirt", P.Dirt, 0.10f);
            EnsureLit("M_FenceWhite", P.FenceWhite, 0.24f);
            EnsureLit("M_WoodWarm", P.WoodWarm, 0.18f);
            EnsureLit("M_WoodDark", P.WoodDark, 0.16f);
            EnsureLit("M_Hedge", P.Hedge, 0.08f);
            var canopy = EnsureLit("M_Canopy", "#3F7E3A", 0.10f);
            if (canopy != null)
            {
                canopy.SetColor("_RimColor", HexL("#9ED36A"));
                canopy.SetFloat("_RimStrength", 0.34f);
                canopy.SetFloat("_RimPower", 2.6f);
                canopy.SetFloat("_Wrap", 0.42f);
                EditorUtility.SetDirty(canopy);
            }
            EnsureLit("M_TentRed", P.TentRed, 0.20f);
            EnsureLit("M_TentCream", P.TentCream, 0.20f);
            var hills = EnsureLit("M_Hills", P.Hills, 0.02f);
            if (hills != null)
            {
                // Distant landscape must not catch a rim light; it reads as a lit dome the
                // instant it does, and the horizon stops being a horizon.
                hills.SetFloat("_RimStrength", 0f);
                hills.SetFloat("_Wrap", 0.5f);
                EditorUtility.SetDirty(hills);
            }
            EnsureLit("M_MowerRed", P.MowerRed, 0.55f, 0.05f);
            EnsureLit("M_MowerCream", P.MowerCream, 0.45f, 0.05f);
            EnsureLit("M_EngineGrey", P.EngineGrey, 0.42f, 0.30f);
            EnsureLit("M_Brass", P.Brass, 0.75f, 0.85f);
            EnsureLit("M_Tyre", "#2B2B2E", 0.16f);
            EnsureLit("M_DuckCream", P.DuckCream, 0.20f);
            EnsureLit("M_Bill", P.Bill, 0.28f);

            // The crowd is drawn with GPU instancing and gets its colour per instance.
            var crowd = EnsureLit("M_Crowd", "#FFFFFF", 0.16f);
            if (crowd != null) { crowd.enableInstancing = true; EditorUtility.SetDirty(crowd); }

            var water = EnsureLit("M_Water", P.Pond, 0.90f, 0.10f);
            if (water != null)
            {
                water.SetColor("_RimColor", HexL(P.PondShallow));
                water.SetFloat("_RimStrength", 0.45f);
                water.SetFloat("_RimPower", 2.2f);
                EditorUtility.SetDirty(water);
            }

            AssetDatabase.SaveAssets();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ------------------------------------------------------------------ scene

        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ConfigurePipeline();
            var sun = BuildLighting();
            BuildEnvironmentLighting();
            var volume = BuildPostProcessing();

            var systems = new GameObject("~ Systems").transform;
            var input = systems.gameObject.AddComponent<InputReader>();
            var cutMask = systems.gameObject.AddComponent<CutMask>();
            cutMask.stampShader = Shader.Find("Duck/CutStamp");
            var target = systems.gameObject.AddComponent<RoundTarget>();

            var lawn = BuildLawn(out Material chalkMat);
            var mower = BuildMower();
            var camera = BuildCamera(mower);
            var judges = BuildJudgeBench();

            var director = systems.gameObject.AddComponent<GameDirector>();
            director.target = target;
            director.cutMask = cutMask;
            director.mower = mower.GetComponent<MowerController>();
            director.cameraDirector = camera;
            director.judges = judges;
            director.chalkMaterial = chalkMat;

            DuckVFXBuilder.Build(mower, mower.GetComponent<MowerController>());

            var autopilot = systems.gameObject.AddComponent<Autopilot>();
            autopilot.mower = mower.GetComponent<MowerController>();
            autopilot.target = target;
            director.autopilot = autopilot;

            camera.mower = mower.GetComponent<MowerController>();
            camera.target = mower.transform;
            camera.judgesLookAt = judges.transform;

            var revealAnchor = new GameObject("RevealAnchor").transform;
            revealAnchor.position = Vector3.zero;
            camera.revealLookAt = revealAnchor;

            DuckEnvironmentBuilder.Build();

            // ---- the rest of the championship ground ----
            var worldRoot = GameObject.Find("~ World");
            var rivals = DuckVenueBuilder.Build(worldRoot != null ? worldRoot.transform : null);

            var tournament = systems.gameObject.AddComponent<Tournament>();
            tournament.rivals = rivals;
            tournament.playerName = Venue.Player.contestant;
            tournament.playerSpecies = Venue.Player.species;
            tournament.playerLivery = Venue.Player.livery;
            director.tournament = tournament;

            var board = Object.FindFirstObjectByType<Scoreboard>();
            director.scoreboard = board;
            if (board != null) camera.scoreboardAnchor = board.transform;

            var ambience = systems.gameObject.AddComponent<NeighbourAmbience>();
            ambience.tournament = tournament;

            // Portraits of every contestant, rendered from the real models at startup so the UI
            // can show who a lawn belongs to.
            var portraits = systems.gameObject.AddComponent<ContestantPortraits>();
            var subjects = new System.Collections.Generic.List<ContestantPortraits.Subject>();

            // "Duck" is the FBX's own root: this model's parts sit at the top level of the file
            // with no Duck_Root node, so asking for one returned nothing and the player quietly
            // ended up with no portrait at all. Unity names an instantiated model root after the
            // file, which is the reliable handle when a model has no root of its own — the mower
            // is loaded the same way for the same reason.
            var duckMesh = DuckAssetLibrary.GetCombined("Duck.fbx", "Duck", "PortraitDuck");
            if (duckMesh == null)
                Debug.LogWarning("[Duck] No duck mesh for the player's portrait.");
            else
                subjects.Add(new ContestantPortraits.Subject
                {
                    contestant = Venue.Player.contestant,
                    mesh = duckMesh,
                    material = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Duck.mat"),
                    lookOffset = new Vector3(0f, 0.30f, 0f),
                    framing = 0.26f,
                    yaw = 24f
                });

            foreach (var spec in Venue.Plots)
            {
                if (spec.isPlayer) continue;
                string blenderName = char.ToUpper(spec.contestant[0]) + spec.contestant.Substring(1).ToLower();
                var mesh = DuckAssetLibrary.GetCombined("Rivals.fbx", $"{blenderName}_Root", $"Rival_{blenderName}");
                if (mesh == null)
                {
                    Debug.LogWarning($"[Duck] No portrait mesh for {spec.contestant} ({blenderName}_Root).");
                    continue;
                }
                subjects.Add(new ContestantPortraits.Subject
                {
                    contestant = spec.contestant,
                    mesh = mesh,
                    material = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Rivals.mat"),
                    lookOffset = new Vector3(0f, 0.34f, 0f),
                    framing = 0.30f,
                    yaw = 26f
                });
            }
            portraits.subjects = subjects.ToArray();

            // ---- UI ----
            var cam = camera.GetComponent<Camera>();
            var hud = DuckUIBuilder.Build(cam, director, mower.GetComponent<MowerController>(),
                                          cutMask, target, judges);

            // ---- Audio ----
            var audioGO = new GameObject("~ Audio");
            var audio = audioGO.AddComponent<AudioDirector>();
            audio.mower = mower.GetComponent<MowerController>();
            audio.director = director;
            audio.judges = judges;
            WireAudioClips(audio);
            // The neighbours borrow their engine and crowd clips from the same bank the player's
            // mower uses, so the venue never sounds like two different games running at once.
            ambience.audioDirector = audio;

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Debug.Log("[Duck] Main scene built.");
        }

        static AudioClip Clip(string path)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{path}.wav");
            if (c == null) Debug.LogWarning($"[Duck] audio clip missing: {path}");
            return c;
        }

        static AudioClip[] Clips(params string[] paths)
        {
            var list = new System.Collections.Generic.List<AudioClip>();
            foreach (var p in paths) { var c = Clip(p); if (c != null) list.Add(c); }
            return list.ToArray();
        }

        static void WireAudioClips(AudioDirector a)
        {
            a.engineIdle = Clip("Engine/engine_idle_loop");
            a.engineMid = Clip("Engine/engine_mid_loop");
            a.engineHigh = Clip("Engine/engine_high_loop");
            a.engineStart = Clip("Engine/engine_start");
            a.engineStop = Clip("Engine/engine_stop");

            a.bladeLoop = Clip("Blade/blade_loop");
            a.bladeCutGrassLoop = Clip("Blade/blade_cut_grass_loop");
            a.bladeEngage = Clip("Blade/blade_engage");
            a.bladeDisengage = Clip("Blade/blade_disengage");
            a.debrisPings = Clips("Blade/debris_ping_01", "Blade/debris_ping_02",
                                  "Blade/debris_ping_03", "Blade/debris_ping_04");

            a.driftLoop = Clip("Mower/drift_loop");
            a.boostStart = Clip("Mower/boost_start");
            a.boostLoop = Clip("Mower/boost_loop");
            a.boostEnd = Clip("Mower/boost_end");
            a.horn = Clip("Mower/horn");
            a.bonks = Clips("Mower/bonk_01", "Mower/bonk_02", "Mower/bonk_03");
            a.suspensionBumps = Clips("Mower/suspension_bump_01", "Mower/suspension_bump_02",
                                      "Mower/suspension_bump_03");

            a.birds = Clip("Ambience/birds_loop");
            a.windGrass = Clip("Ambience/wind_grass_loop");
            a.crowdAmbient = Clip("Crowd/crowd_ambient_loop");

            a.cheerSmall = Clip("Crowd/crowd_cheer_small");
            a.cheerBig = Clip("Crowd/crowd_cheer_big");
            a.gasp = Clip("Crowd/crowd_gasp");
            a.aww = Clip("Crowd/crowd_aww");
            a.laugh = Clip("Crowd/crowd_laugh");
            a.applause = Clip("Crowd/applause_loop");

            a.countdownBeep = Clip("UI/countdown_beep");
            a.countdownGo = Clip("UI/countdown_go");
            a.klaxon = Clip("UI/klaxon");
            a.scoreTick = Clip("UI/score_tick");
            a.cardRaise = Clip("UI/card_raise");
            a.stamp = Clip("UI/stamp");

            a.quackHappy = Clip("Duck/quack_happy");
            a.quackAnnoyed = Clip("Duck/quack_annoyed");
            a.quackPanic = Clip("Duck/quack_panic");
            a.quackProud = Clip("Duck/quack_proud");

            a.goatLow = Clip("Judges/judge_goat_low");
            a.goatHigh = Clip("Judges/judge_goat_high");
            a.badgerLow = Clip("Judges/judge_badger_low");
            a.badgerHigh = Clip("Judges/judge_badger_high");
            a.heronLow = Clip("Judges/judge_heron_low");
            a.heronHigh = Clip("Judges/judge_heron_high");

            a.musicRound = Clip("Music/music_round_loop");
            a.musicRoundUrgent = Clip("Music/music_round_urgent_layer");
            a.musicReveal = Clip("Music/music_reveal");
            a.musicJudgingBed = Clip("Music/music_judging_bed_loop");
            a.fanfareGood = Clip("Music/fanfare_good");
            a.fanfareBad = Clip("Music/fanfare_bad");
        }

        static void ConfigurePipeline()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                Debug.LogWarning("[Duck] No URP asset active; skipping pipeline configuration.");
                return;
            }

            var so = new SerializedObject(urp);
            SetProp(so, "m_ShadowDistance", 58f);
            SetProp(so, "m_ShadowCascadeCount", 2);
            SetProp(so, "m_Cascade2Split", 0.22f);
            SetProp(so, "m_MainLightShadowmapResolution", 2048);
            SetProp(so, "m_SoftShadowsSupported", true);
            SetProp(so, "m_MSAA", 2);
            SetProp(so, "m_SupportsHDR", false);
            SetProp(so, "m_RenderScale", 1f);
            // 2 = PerPixel. This was set to 1 (PerVertex), which silently disables main-light
            // shadows and flattens every surface in the game — the single biggest lighting bug
            // in the project so far.
            SetProp(so, "m_MainLightRenderingMode", 2);
            SetProp(so, "m_AdditionalLightsRenderingMode", 1);
            SetProp(so, "m_MainLightShadowsSupported", true);
            SetProp(so, "m_AnyShadowsSupported", true);
            SetProp(so, "m_SoftShadowsSupported", true);
            SetProp(so, "m_AdditionalLightsPerObjectLimit", 2);
            SetProp(so, "m_UseSRPBatcher", true);
            SetProp(so, "m_SupportsDynamicBatching", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
        }

        static void SetProp(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null) return;
            switch (value)
            {
                case float f when p.propertyType == SerializedPropertyType.Float: p.floatValue = f; break;
                // Enums are written by underlying value, not by list index. MSAA's values are
                // 1/2/4/8, so enumValueIndex would silently set the wrong quality.
                case int i when p.propertyType == SerializedPropertyType.Integer ||
                                p.propertyType == SerializedPropertyType.Enum: p.intValue = i; break;
                case bool b when p.propertyType == SerializedPropertyType.Boolean: p.boolValue = b; break;
            }
        }

        static Light BuildLighting()
        {
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(46f, -38f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Hex("#FFF1CE");
            light.intensity = 1.95f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.46f;
            light.shadowBias = 0.06f;
            light.shadowNormalBias = 0.35f;
            light.shadowNearPlane = 0.2f;

            go.AddComponent<UniversalAdditionalLightData>();

            RenderSettings.sun = light;
            return light;
        }

        static void BuildEnvironmentLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#6FB4EC");
            RenderSettings.ambientEquatorColor = Hex("#9FD180");
            RenderSettings.ambientGroundColor = Hex("#5C9A52");
            RenderSettings.ambientIntensity = 1.12f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex(P.Haze);
            // The band between the venue and the clip plane, and nothing else.
            //
            // It has to clear the whole championship ground, because the reveal and the tour look
            // at it from ninety metres up and two hundred metres back — at 215 m the far plots
            // were washing out white in exactly the shots the venue exists for. And it has to be
            // solid before the camera's 420 m clip plane, or the edge of the world is a cut rather
            // than distance. That leaves 300 to 405, which is where the hills live: the venue stays
            // crisp, the skyline stays hazed, and nothing is ever seen to end.
            RenderSettings.fogStartDistance = 300f;
            RenderSettings.fogEndDistance = 405f;

            var sky = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Sky.mat");
            if (sky != null) RenderSettings.skybox = sky;

            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.35f;
        }

        static Volume BuildPostProcessing()
        {
            EnsureFolder(SettingsDir);
            string path = $"{SettingsDir}/DuckPostProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            // Rebuild the profile from scratch so repeated runs never stack duplicates.
            for (int i = profile.components.Count - 1; i >= 0; i--)
            {
                var c = profile.components[i];
                profile.components.RemoveAt(i);
                Object.DestroyImmediate(c, true);
            }

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.overrideState = true;
            tone.mode.value = TonemappingMode.Neutral;   // ACES eats this palette alive

            var colour = profile.Add<ColorAdjustments>(true);
            colour.postExposure.overrideState = true; colour.postExposure.value = 0.10f;
            colour.contrast.overrideState = true; colour.contrast.value = 10f;
            colour.saturation.overrideState = true; colour.saturation.value = 20f;

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.overrideState = true; bloom.threshold.value = 1.15f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.30f;
            bloom.scatter.overrideState = true; bloom.scatter.value = 0.62f;
            bloom.tint.overrideState = true; bloom.tint.value = Hex("#FFF6E2");

            var vig = profile.Add<Vignette>(true);
            vig.intensity.overrideState = true; vig.intensity.value = 0.26f;
            vig.smoothness.overrideState = true; vig.smoothness.value = 0.5f;
            vig.color.overrideState = true; vig.color.value = Hex("#2A3A2A");

            var split = profile.Add<SplitToning>(true);
            split.shadows.overrideState = true; split.shadows.value = Hex("#5C86C8");
            split.highlights.overrideState = true; split.highlights.value = Hex("#FFE9BF");
            split.balance.overrideState = true; split.balance.value = -18f;

            EditorUtility.SetDirty(profile);

            var go = new GameObject("~ PostProcessing");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 0f;
            v.sharedProfile = profile;
            return v;
        }

        // ------------------------------------------------------------------ lawn

        static Transform BuildLawn(out Material chalkMat)
        {
            var root = new GameObject("Lawn").transform;

            var field = root.gameObject.AddComponent<GrassField>();
            field.groundMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_GrassGround.mat");
            field.bladeMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_GrassBlades.mat");

            chalkMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_ChalkGuide.mat");

            // The chalk guide is its own flat quad floating a couple of centimetres above the
            // lawn, so it can fade and switch to the analysis overlay without touching the grass.
            var chalkGO = new GameObject("ChalkGuide");
            chalkGO.transform.SetParent(root, false);
            chalkGO.transform.position = new Vector3(0f, 0.03f, 0f);
            var mf = chalkGO.AddComponent<MeshFilter>();
            mf.sharedMesh = DuckMeshLibrary.Quad(Field.Size, Field.Size, 24);
            var mr = chalkGO.AddComponent<MeshRenderer>();
            mr.sharedMaterial = chalkMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return root;
        }

        // ------------------------------------------------------------------ mower

        static GameObject BuildMower()
        {
            int mowerLayer = LayerMask.NameToLayer("Mower");

            // Prefer the authored Blender mower. The primitive stand-in below only exists so the
            // game is drivable before the art lands.
            var authored = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mower.prefab");
            if (authored != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(authored);
                inst.name = "Mower";
                inst.transform.position = new Vector3(0f, 0.45f, -Field.Half + 5.5f);
                inst.transform.rotation = Quaternion.identity;
                return inst;
            }

            var root = new GameObject("Mower");
            root.layer = mowerLayer;
            root.transform.position = new Vector3(0f, 0.45f, -Field.Half + 5.5f);
            root.tag = "Mower";

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 180f;
            rb.linearDamping = 0f;
            rb.angularDamping = 2.2f;

            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.92f, 0.52f, 1.45f);
            box.center = new Vector3(0f, 0.06f, 0f);

            var ctrl = root.AddComponent<MowerController>();
            ctrl.groundMask = ~(1 << mowerLayer);

            var pivot = new GameObject("VisualPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = Vector3.zero;

            // Placeholder chassis. Replaced wholesale by the Blender mower; the important part
            // is that the proportions and pivot names match so the swap is a drop-in.
            var proxy = DuckMeshLibrary.BuildMowerProxy(pivot);

            var visuals = root.AddComponent<MowerVisuals>();
            visuals.mower = ctrl;
            visuals.visualPivot = pivot;
            visuals.wheelFL = proxy.wheelFL;
            visuals.wheelFR = proxy.wheelFR;
            visuals.wheelRL = proxy.wheelRL;
            visuals.wheelRR = proxy.wheelRR;
            visuals.steeringColumn = proxy.steering;
            visuals.bladeSpinner = proxy.blade;
            visuals.exhaust = proxy.exhaust;
            visuals.duckRoot = proxy.duckRoot;
            visuals.duckHead = proxy.duckHead;
            visuals.duckWingL = proxy.duckWingL;
            visuals.duckWingR = proxy.duckWingR;

            SetLayerRecursive(root.transform, mowerLayer);
            return root;
        }

        static CameraDirector BuildCamera(GameObject mower)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.18f;
            // Back to 420 m, and the fog is what makes that invisible rather than the clip plane
            // being pushed out to meet the geometry. Haze finishes before the plane does, so the
            // world dissolves into sky well before anything is cut — which is both cheaper and the
            // look the skyline was designed around: the hills are meant to sit in white distance,
            // not to be resolved.
            cam.farClipPlane = 420f;
            cam.fieldOfView = 58f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.backgroundColor = Hex(P.SkyHorizon);

            var extra = go.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            extra.antialiasingQuality = AntialiasingQuality.Medium;
            extra.renderShadows = true;

            go.AddComponent<AudioListener>();

            var dir = go.AddComponent<CameraDirector>();
            dir.avoidGeometry = false;
            dir.collisionMask = 0;
            // The briefing is an establishing shot: high, behind the judges, looking north across
            // the whole arena so the player sees the chalk outline they are about to attempt.
            dir.briefingPosition = new Vector3(0f, 13.5f, -50f);
            dir.briefingLookAt = new Vector3(0f, 0.5f, 2f);
            dir.briefingFov = 50f;
            return dir;
        }

        static JudgePanel BuildJudgeBench()
        {
            var root = new GameObject("Judges");
            root.transform.position = new Vector3(0f, 0f, -Field.Half - 7.5f);
            var panel = root.AddComponent<JudgePanel>();
            panel.ApplyDefaultProfiles();

            string[] names = { "Mildred", "Boris", "Priscilla" };
            float[] xs = { -1.45f, 0f, 1.45f };
            bool anyAuthored = false;

            for (int i = 0; i < names.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Judges/Judge_{names[i]}.prefab");
                if (prefab == null) continue;
                anyAuthored = true;

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.SetParent(root.transform, false);
                // Bench height, slight splay so the trio does not read as a straight line.
                inst.transform.localPosition = new Vector3(xs[i], 0.50f, -0.10f + Mathf.Abs(xs[i]) * 0.10f);
                inst.transform.localRotation = Quaternion.Euler(0f, -xs[i] * 3.5f, 0f);
                var jc = inst.GetComponent<JudgeCharacter>();
                if (panel.judges[i] != null) panel.judges[i].character = jc;
                // They watch the mower while it works, which is most of what makes the stand
                // feel occupied rather than decorated.
                var mowerGO = GameObject.Find("Mower");
                if (jc != null && mowerGO != null) jc.lookTarget = mowerGO.transform;
            }

            if (!anyAuthored) DuckMeshLibrary.BuildJudgeProxies(root.transform, panel);
            else DuckMeshLibrary.BuildJudgeBenchOnly(root.transform);

            return panel;
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        static void AddSceneToBuildSettings(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
