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
            public const string CutBase = "#5FA83F";
            public const string CutTip = "#A0D268";
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
            // The stands, the benches and the trestles are the largest man-made mass in the venue
            // and they sit directly behind the crowd. Held at the old values they were a brown so
            // dark and so desaturated that they read as a hole, which left the spectators with
            // nothing but hedge and meadow behind them — every one of them a green of the same
            // value, which is why the crowd read as texture instead of as figures. Warmer and a
            // step lighter makes the stands the warm neutral the set was missing.
            public const string WoodWarm = "#A9773F";
            public const string WoodDark = "#7C5330";
            // Grain, knots and plank seams for the two above, used by Duck/Wood. Both are the board
            // colour taken down in value and a little further toward red, which is the direction real
            // timber moves in: a grain colour pulled toward grey instead reads as dirt on a painted
            // panel rather than as wood. Kept close enough in value that the grain is a suggestion at
            // gameplay distance — the bench fills the bottom third of every judging close-up and a
            // high-contrast grain there would be worse than the flat colour it replaces.
            public const string WoodWarmGrain = "#7A4F27";
            public const string WoodDarkGrain = "#52341C";
            public const string Pond = "#328DB6";
            public const string PondShallow = "#68B0C4";
            public const string Chalk = "#F7F3E4";
            // Cooler and a step lighter than before. The hedges ring the arena immediately behind
            // the stands, so they cannot share a hue with the meadow in front of them and the
            // canopies above them; pushing them blue-green puts a hue boundary where the
            // composition cannot afford a value one.
            public const string Hedge = "#2F6B45";
            public const string Dirt = "#B99A6B";

            public const string SkyZenith = "#3F90DA";
            public const string SkyHorizon = "#CFE7F2";
            public const string SunDisc = "#FFF3D0";
            // Back to the art bible's grey-teal, a shade lighter still. This had drifted to a
            // green, and as a green it was the fourth surface in the same narrow band as the
            // hedges, the meadow and the tree canopies — so the skyline stopped reading as
            // distance and the crowd silhouettes had nothing to sit against.
            public const string Hills = "#8FB5AE";
            public const string Haze = "#BFE3F0";
        }

        // ------------------------------------------------------------------ materials

        /// <summary>
        /// Force a texture to import as a tiling detail map.
        ///
        /// The ground textures were imported for use as sprites and decals, which leaves them on
        /// Clamp — and a clamped texture sampled far outside 0..1, which is exactly what world-space
        /// tiling does, smears its edge pixel across the entire field. The lawn would have come out
        /// one flat colour and the obvious conclusion would have been that the detail map "did
        /// nothing", so this is asserted rather than assumed.
        /// </summary>
        static void ImportAsTilingDetail(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            if (imp.wrapMode == TextureWrapMode.Repeat && imp.mipmapEnabled &&
                imp.textureType == TextureImporterType.Default) return;

            imp.textureType = TextureImporterType.Default;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = true;          // without mips this aliases into noise at distance
            imp.sRGBTexture = true;
            imp.SaveAndReimport();
        }

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
            // A deeper, still-coloured shadow. #8CA8D6 is a pale blue, and using it as the shadow
            // colour meant a shadowed face was lifted almost to a light tint — one of three reasons
            // shadows read as faint. Overcooked's shadows are strong AND coloured; the thing to
            // avoid is grey or black, not depth.
            m.SetColor("_ShadowTint", HexL("#5E7BB0"));
            m.SetColor("_RimColor", HexL("#FFF3D8"));

            // 0.38, down from 0.65 — which was above this shader's own declared Range(0, 0.6).
            //
            // The high wrap was set for a real reason: the sun crosses from the south, so the
            // judges' stand is lit from behind and at low wrap every surface the judging camera
            // sees fell to near black. But wrap works by bending the diffuse term most of the way
            // around the object, so it lightens shaded faces AND softens the terminator — it buys
            // "not black" by spending exactly the gradient a shadow is read from. At 0.65 a prop
            // under a solid awning looked the same as one in open sun.
            //
            // It is the right value to give back now because the actual cause of the black faces
            // has since been found and fixed elsewhere: the volume profile contained no
            // post-processing at all, HDR was off so tonemapping never ran, and the ambient probe
            // was a near-pure green that multiplied warm albedos away. Ambient carries the shaded
            // side properly now, so wrap can go back to shaping form instead of hiding a bug.
            m.SetFloat("_Wrap", 0.38f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Sawn timber on Duck/Wood: grain, plank seams and knots instead of one flat colour.
        /// </summary>
        /// <param name="grainAxis">
        /// The mesh's OWN long axis, not a world direction. Duck/Wood builds its pattern in object
        /// space so a rotating piece keeps its grain, which means this has to name an object axis:
        /// (1,0,0) for anything built as a box scaled long on X — the bench top, the bench front,
        /// the stand planks, the trestle tops — and (0,1,0) for legs and posts, which are scaled long
        /// on Y. Getting it wrong does not look subtly off, it looks like the grain runs across the
        /// board, so it is a required argument rather than a defaulted one.
        /// </param>
        public static Material EnsureWood(string name, string hex, string grainHex, Vector4 grainAxis,
                                          float plankWidth = 0.26f, float seamDepth = 0.55f)
        {
            var m = EnsureMaterial(name, "Duck/Wood");
            if (m == null) return null;
            m.SetColor("_BaseColor", HexL(hex));
            m.SetColor("_GrainColor", HexL(grainHex));
            m.SetVector("_GrainDir", grainAxis);

            // Lighting is set from the same values EnsureLit uses, not from Duck/Wood's own defaults.
            // Wood stands directly against props on every shot in the game — the bench against the
            // judges, the stands against the crowd — and the point of copying Duck/Prop's lighting
            // block into Duck/Wood was that those surfaces shade identically. Letting the two shaders
            // carry independent defaults would give that away again the first time either is retuned.
            m.SetColor("_ShadowTint", HexL("#5E7BB0"));
            m.SetColor("_RimColor", HexL("#FFF3D8"));
            m.SetFloat("_Wrap", 0.38f);
            m.SetFloat("_Smoothness", 0.30f);
            m.SetFloat("_Metallic", 0f);

            m.SetFloat("_PlankWidth", plankWidth);
            m.SetFloat("_SeamDepth", seamDepth);
            // Half a board, so whole boards fit between the piece's two edges. At zero the 0.78 m
            // bench top gets a seam straight down its centre line and a half board at each edge,
            // which reads as a mistake rather than as joinery.
            m.SetFloat("_PlankOffset", 0.5f);

            // "적당히" is the whole brief here. These are the values that make the surface read as
            // timber at the distance the judging camera sits at and stop short of photoreal grain,
            // which on a chunky stylised set looks worse than flat colour: grain at a third strength,
            // a 4.5 cm pitch, and knots rare enough to be a mark you notice rather than a texture.
            m.SetFloat("_GrainAmount", 0.34f);
            m.SetFloat("_GrainScale", 22f);
            m.SetFloat("_GrainStretch", 0.06f);
            m.SetFloat("_WarpFreq", 4.0f);
            m.SetFloat("_WarpAmount", 0.02f);
            m.SetFloat("_ToneVary", 0.07f);
            m.SetFloat("_PlankVary", 0.06f);
            m.SetFloat("_KnotScale", 1.8f);
            m.SetFloat("_KnotThreshold", 0.80f);
            m.SetFloat("_KnotAmount", 0.28f);

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
                // THE UNCUT GROUND IS MATCHED TO THE MEADOW, not to the palette's lawn colours.
                //
                // The plots and the meadow around them are the same grass, so where one stops and
                // the other starts must not be visible until it has been mown. It was very
                // visible: the palette's UncutBase is #2E7331 while the meadow's is #4C9337, about
                // 1.5x brighter in red and green, so the four plots read as one hard-edged dark
                // rectangle dropped into a bright field. From a distance it looked so much like a
                // huge flat shadow that it was mistaken for one.
                //
                // It survived this long because up close it is invisible — the blade layer stands
                // over the ground and supplies the brightness. But the blades fade out by 44 m, so
                // every shot wider than that is bare ground, and those are exactly the shots the
                // venue is judged on: the briefing, the tour and the overhead study.
                //
                // Held a little deeper than the meadow rather than identical, because this grass is
                // longer and denser, and the CUT colours are untouched — the mown picture needs to
                // read against this, and that contrast is the entire game.
                ground.SetColor("_UncutBase", HexL("#3E8A30"));
                ground.SetColor("_UncutTip", HexL("#6DB945"));
                ground.SetColor("_CutBase", HexL(P.CutBase));
                ground.SetColor("_CutTip", HexL(P.CutTip));
                ground.SetColor("_EdgeColor", HexL(P.CutEdge));
                ground.SetColor("_TrackColor", HexL(P.Track));
                ground.SetFloat("_MottleAmount", 0.66f);
                // 0.16 was fine for the flat playfield, where the normal points at the sun anyway,
                // but the same material runs up onto the rises and under the marquee. Those faces
                // were getting a sixth of the key. Matched to the blade layer so the two do not
                // disagree where the blades thin out.
                ground.SetFloat("_Wrap", 0.30f);
                ground.SetFloat("_StripeAmount", 0.30f);
                ground.SetFloat("_MottleScale", 0.075f);

                // Grain, on top of the mottling.
                //
                // The lawn had three octaves of value noise and no texture map at all, and value
                // noise is smooth by construction — it makes patches of lighter and darker green,
                // which is variation rather than surface. From the chase camera that read as
                // painted felt. The detail map has been sitting in the project unused since the
                // texture pass; hooking it up is most of what makes the ground look like a thing
                // made of grass rather than a coloured plane.
                var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/Art/Textures/Ground/apron_grass_detail_512.png");
                if (detail != null)
                {
                    ImportAsTilingDetail("Assets/Art/Textures/Ground/apron_grass_detail_512.png");
                    ground.SetTexture("_DetailTex", detail);
                    ground.SetFloat("_DetailScale", 0.55f);
                    ground.SetFloat("_DetailStrength", 0.55f);
                    ground.SetFloat("_DetailCutFade", 0.35f);
                }
                else
                {
                    Debug.LogWarning("[Duck] Grass detail texture missing; the lawn will be smooth.");
                }
                EditorUtility.SetDirty(ground);
            }

            var blades = EnsureMaterial("M_GrassBlades", "Duck/GrassBlades");
            if (blades != null)
            {
                // The blade ROOT is lifted off the palette's #2E7331, and the reason is the angle
                // the venue is actually judged from.
                //
                // Looking down into a blade layer, most of what reaches the eye is roots and the
                // gaps between blades, not the lit faces you see at mower height. With the root at
                // the palette value each plot read as a dark square dropped into a bright meadow —
                // clearly visible from above, and dark enough at a shallow downward angle to be
                // mistaken for an enormous flat shadow lying across the venue.
                //
                // It is not a distance problem, which is what made it confusing: blade LOD is
                // keyed on XZ distance, so from directly overhead every blade is at full detail
                // and the effect is at its strongest exactly where the game shows the picture off
                // — the overhead study beat, the venue tour and the briefing.
                //
                // The tip stays where it was, so a blade still runs dark-to-light along its length
                // and the lawn keeps its depth at ground level.
                blades.SetColor("_UncutBase", HexL("#35792E"));
                blades.SetColor("_UncutTip", HexL("#55A83D"));
                blades.SetColor("_CutBase", HexL(P.CutBase));
                blades.SetColor("_CutTip", HexL("#AEDB73"));
                blades.SetColor("_Translucency", HexL("#A6E84A"));
                // Blades are yaw-randomised, so about half of the visible blade area faces away
                // from the sun at any time. At 0.18 those faces got 0.15 of the key, and because
                // the lawn's albedo is already tiny in red and blue that multiplied the field's
                // hue down to almost pure green — the measured cause of "the map is dark and
                // green". Root darkening comes down with it for the same reason: 0.50 on top of a
                // shaded face was compounding to a black band along the bottom of every blade.
                blades.SetFloat("_Wrap", 0.42f);
                blades.SetFloat("_AO", 0.34f);
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
                // Warmed and lifted a step, and the dry areas given more of the frame. This is the
                // surface the venue is standing on and the biggest single area in every wide shot,
                // so it sets whether the set reads as sunlit or as overcast. Warm sunlit turf also
                // stops it competing with the hedge ring, which is now the cool green.
                meadow.SetColor("_UncutBase", HexL("#3E8A30"));
                meadow.SetColor("_UncutTip", HexL("#6DB945"));
                meadow.SetColor("_PatchDark", HexL("#36752F"));
                meadow.SetColor("_DryTint", HexL("#9FB552"));
                meadow.SetFloat("_MottleScale", 0.055f);
                meadow.SetFloat("_MottleAmount", 0.75f);
                // ~90 m fields and ~55 m dry ground, so the meadow has areas rather than a wash.
                meadow.SetFloat("_PatchScale", 0.011f);
                meadow.SetFloat("_PatchAmount", 0.50f);
                meadow.SetFloat("_DryAmount", 0.38f);
                // Metre-scale speckle: the part that stops it looking like paper at eye level.
                meadow.SetFloat("_GrainScale", 0.8f);
                meadow.SetFloat("_GrainAmount", 0.13f);
                meadow.SetFloat("_Wrap", 0.38f);
                meadow.SetFloat("_OldStripe", 0.055f);
                meadow.enableInstancing = true;
                EditorUtility.SetDirty(meadow);
            }

            // BARE EARTH, on the same shader as the meadow rather than on a flat lit colour.
            //
            // Every earth surface in the game — the entrance lane, the paths between plots, the
            // plaza floor, the pond bank — shared one URP/Lit material at a single unmodulated
            // #B99A6B. Those are large areas and they sit right beside a lawn that has mottle,
            // patches and grain, so they read as untextured placeholder next to it: "Ground, Path
            // 머티리얼도 너무 단색임", and the reason the venue looked like a demo build.
            //
            // Duck/GrassPlain is not really a grass shader — it is a two-colour blend driven by
            // noise at three scales, with one grass-specific term (_OldStripe) that can be turned
            // down. So earth gets the same machinery for free instead of a second, slightly
            // different implementation drifting away from it, which is the trap the straw material
            // below records falling into.
            //
            // The colours read as compacted damp earth up to dry dust, which is what a fairground
            // path in midsummer is. _OldStripe stays low but non-zero: on a path it reads as cart
            // ruts rather than mowing, and it is the cue that stops a long straight lane looking
            // extruded.
            var earth = EnsureMaterial("M_Earth", "Duck/GrassPlain");
            if (earth != null)
            {
                earth.SetColor("_UncutBase", HexL("#9C7C52"));   // damp, trodden
                earth.SetColor("_UncutTip", HexL("#C9AC7C"));    // dry, raised
                earth.SetColor("_PatchDark", HexL("#856844"));   // worn hollows
                earth.SetColor("_DryTint", HexL("#DAC59B"));     // dust
                earth.SetFloat("_MottleScale", 0.075f);
                earth.SetFloat("_MottleAmount", 0.62f);
                earth.SetFloat("_PatchScale", 0.014f);
                earth.SetFloat("_PatchAmount", 0.52f);
                earth.SetFloat("_DryAmount", 0.42f);
                // Grainier and finer than grass — earth is grit, and this is the term that carries
                // it at the range the player actually drives past it.
                earth.SetFloat("_GrainScale", 1.15f);
                earth.SetFloat("_GrainAmount", 0.19f);
                earth.SetFloat("_Wrap", 0.34f);
                earth.SetFloat("_OldStripe", 0.045f);
                earth.enableInstancing = true;
                EditorUtility.SetDirty(earth);
            }

            // Straw, despite the name. The only thing in the game that asks for M_ApronProp is the
            // hay stacks in the judges' backdrop (DuckEnvironmentBuilder.BuildJudgeBackdrop) — the
            // apron itself has used M_Apron and the plain grass shader since the meadow pass — and
            // they were being painted apron green. Four stacks of green hay were the largest mass
            // in every judging shot, and with the hedges, the tree canopies, the meadow and the
            // hills all green as well the crowd had five surfaces of the same hue and value to sit
            // against and disappeared into them. Straw is the warm accent that band was missing,
            // and it is what a hay bale is. Rename to M_Straw when nothing else is mid-edit.
            var straw = EnsureLit("M_ApronProp", "#C9A462", 0.22f);
            if (straw != null)
            {
                straw.SetFloat("_Wrap", 0.42f);
                straw.SetFloat("_RimStrength", 0.20f);
                straw.SetColor("_RimColor", HexL("#FFE9B4"));
                EditorUtility.SetDirty(straw);
            }
            // No C# consumer, but it is still on disk and possibly still on something in the scene,
            // so it keeps being authored rather than being left to drift.
            EnsureLit("M_ApronOuter", "#4E9E2E", 0.10f);
            EnsureLit("M_Dirt", P.Dirt, 0.10f);
            EnsureLit("M_FenceWhite", P.FenceWhite, 0.30f);
            // WOOD IS ON ITS OWN SHADER, not on Duck/Prop.
            //
            // Smoothness was pushed to the top of the art bible's 0.18–0.30 range for base props for
            // a real reason — with no highlight at all the bench top and the front face differed only
            // in value and the whole stand read as one slab — and it stays there. But a highlight was
            // never going to be enough on its own: these are the two largest surfaces in every judging
            // close-up, at 6.0 x 0.78 m and 6.0 x 0.66 m, and an unmodulated colour on them reads as
            // untextured placeholder no matter how it is lit.
            //
            // Duck/Wood is Duck/Prop's lighting with a procedural albedo in front of it, so nothing
            // about how these sit next to the judges or the crowd changes; they just stop being flat.
            // Grain along object X, because every piece taking these two is a box scaled long on X.
            EnsureWood("M_WoodWarm", P.WoodWarm, P.WoodWarmGrain, new Vector4(1, 0, 0, 0));
            EnsureWood("M_WoodDark", P.WoodDark, P.WoodDarkGrain, new Vector4(1, 0, 0, 0));

            // Vertical members: the bench legs, and the stand legs and fence posts once whoever owns
            // DuckEnvironmentBuilder switches them over.
            //
            // A separate material rather than a property on the geometry, because grain direction is
            // the one thing about wood that cannot be got wrong quietly. A post is scaled long on Y,
            // so it needs its grain on Y; painted with M_WoodDark it would get horizontal banding up
            // a vertical timber, which reads as a stack of blocks. Seams are off entirely — a post is
            // one piece of wood and has no joints to draw.
            EnsureWood("M_WoodPost", P.WoodDark, P.WoodDarkGrain, new Vector4(0, 1, 0, 0),
                       plankWidth: 0.5f, seamDepth: 0f);

            // The same two browns with the grain on Z, for the crowd stands.
            //
            // These exist because of how the stands are built, and the distinction is worth stating
            // once. Duck/Wood reads the mesh's own axes, and a piece spawned as its own GameObject
            // carries its rotation with it — the north scoreboard's face is turned 180 degrees and its
            // grain still runs along the board, which is the whole reason this is not a world-space
            // pattern. But the stands go through Combiner, and combining bakes every piece's transform
            // into one mesh: the per-piece orientation is gone by the time the shader sees it, so the
            // grain axis has to come from the material. The stand planks and risers are 30 m long on
            // Z, so on M_WoodWarm they would get their grain running across a 0.84 m width instead of
            // along a 30 m board — the one place in the venue where a wrong axis would be obvious,
            // because a plank that long is nothing but its own direction.
            EnsureWood("M_WoodWarmZ", P.WoodWarm, P.WoodWarmGrain, new Vector4(0, 0, 1, 0));
            EnsureWood("M_WoodDarkZ", P.WoodDark, P.WoodDarkGrain, new Vector4(0, 0, 1, 0));
            EnsureLit("M_Hedge", P.Hedge, 0.14f);
            // Tree canopies are the mass directly above and behind the crowd stands. Lifted a step
            // so hedge, canopy and hills land on three separate values instead of one.
            var canopy = EnsureLit("M_Canopy", "#4E9445", 0.10f);
            if (canopy != null)
            {
                canopy.SetColor("_RimColor", HexL("#9ED36A"));
                canopy.SetFloat("_RimStrength", 0.34f);
                canopy.SetFloat("_RimPower", 2.6f);
                canopy.SetFloat("_Wrap", 0.42f);
                EditorUtility.SetDirty(canopy);
            }
            EnsureLit("M_TentRed", P.TentRed, 0.28f);
            EnsureLit("M_TentCream", P.TentCream, 0.28f);
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

            // The on-screen driving controls, after the HUD so they sort in front of it and can be
            // laid out around it. They reveal themselves only on a touch device — see the note on
            // TouchControls.Revealed for why a phantom thumbstick on a laptop with a digitiser is
            // worse than no controls at all.
            DuckTouchBuilder.Build(cam);

            // ---- Opening story ----
            // Built after the HUD so its canvas sorts in front of one that already exists, rather
            // than in front of one that happens to be created later.
            director.intro = DuckCutsceneBuilder.Build(cam);

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

            // ---- everything the mower can hit, must be able to hit ----
            //
            // Last, because it measures the scene as built rather than as intended: combined batches,
            // authored FBX props, primitives and rival dressing all end up in the same audit. It adds
            // any collider the geometry requires and logs an error for anything standing where the
            // mower drives that cannot be made solid at all. See DuckSolidity for why this is a pass
            // over the finished scene and not a rule people are asked to remember.
            DuckSolidity.Enforce();

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            // Not "insert this scene at index 0" any more. It used to be exactly that, which put the
            // round in front of the menu again every time the game scene was rebuilt — a WebGL build
            // that opens on gameplay is indistinguishable from a menu that was never built. Scene
            // order now has one owner.
            DuckMenuBuilder.RegisterBuildScenes();
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

        /// <summary>
        /// An AudioDirector with the whole clip bank wired, for any scene that needs one.
        ///
        /// Internal so the arena can call it. The arena had NO AudioDirector at all, which meant
        /// AudioDirector.Instance was null there and every impact sound, honk, thud and crowd cue in the
        /// defence phase did nothing while succeeding silently — the same failure the missing crowd had.
        /// "Cannot be verified in a silent environment" was the wrong diagnosis: not being able to HEAR a
        /// sound and no sound being PLAYED are different problems, and the second one is checkable.
        /// </summary>
        internal static AudioDirector BuildAudioDirector(MowerController mower, JudgePanel judges,
                                                         GameDirector director = null)
        {
            var go = new GameObject("~ Audio");
            var a = go.AddComponent<AudioDirector>();
            a.mower = mower;
            a.director = director;
            a.judges = judges;
            WireAudioClips(a);
            return a;
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
            // Tonemapping is HDR-only in URP. ColorGradingLutPass builds the LDR LUT when this is
            // off and never sets the TonemapNeutral keyword at all, so the Neutral tonemap the art
            // bible calls for was silently doing nothing: everything above 1.0 hard-clipped, which
            // is why the judges' cream and the mower's red arrived as flat plateaus and why raising
            // the ambient any further used to just wash the set out.
            //
            // 0 = _32Bits, which is R11G11B10_UFloat — the same 32 bits per pixel as the RGBA8
            // target it replaces, so the WebGL bandwidth cost is nil. It does require
            // EXT_color_buffer_float, which every desktop browser in the target has.
            SetProp(so, "m_SupportsHDR", true);
            SetProp(so, "m_HDRColorBufferPrecision", 0);
            SetProp(so, "m_RenderScale", 1f);
            // 1 = PerPixel. VERIFIED against the live enum, not from memory:
            // UnityEngine.Rendering.Universal.LightRenderingMode is
            //     Disabled = 0, PerPixel = 1, PerVertex = 2
            // which is not the order it is written in or displayed in, and is the whole story here.
            //
            // This line used to say 2 with a comment explaining that 2 was PerPixel and that 1
            // would "silently disable main-light shadows and flatten every surface in the game".
            // The diagnosis was right and the mapping was backwards, so the fix set the sun to
            // PER-VERTEX and caused the exact bug it described. A per-vertex main light cannot cast
            // shadows at all, which is why the game had none: the light was Soft at 0.72 strength,
            // the camera had renderShadows on, the asset had shadows supported at 2048 with a 58 m
            // distance and two cascades — every switch anyone would think to check was correct.
            //
            // Do not "correct" this to 2 again. If shadows disappear, read the enum.
            SetProp(so, "m_MainLightRenderingMode", 1);
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

        /// <summary>Internal rather than private: the menu scene is lit by this same sun, and a
        /// second copy of these numbers is a second look to keep in step.</summary>
        internal static Light BuildLighting()
        {
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(46f, -38f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Hex("#FFF1CE");
            light.intensity = 1.95f;
            light.shadows = LightShadows.Soft;
            // 0.72, up from 0.46. Half-strength shadows were a compensation for the ambient probe
            // being too dark and too saturated: with shaded faces already collapsing toward black,
            // a full-strength shadow on top of them was unreadable, so it was faded out. Ambient
            // now carries the shaded side properly, which means a shadow can be a shadow.
            //
            // Overcooked's shadows are strong AND coloured — the contact is unmistakable, and the
            // shadowed area keeps its hue rather than going grey. That combination is what places
            // objects on the ground; without it a set reads as flat however brightly it is lit.
            light.shadowStrength = 0.9f;
            light.shadowBias = 0.06f;
            light.shadowNormalBias = 0.35f;
            light.shadowNearPlane = 0.2f;

            go.AddComponent<UniversalAdditionalLightData>();

            RenderSettings.sun = light;
            return light;
        }

        /// <summary>Internal for the same reason as BuildLighting: the menu shares this sky.</summary>
        internal static void BuildEnvironmentLighting()
        {
            // The gradient probe, rebuilt around two facts.
            //
            // First: ambientIntensity does nothing here. Unity only applies the intensity
            // multiplier to skybox ambient — for Gradient and Color the three colours ARE the
            // probe — so the 1.12 that was meant to be carrying the shaded faces was inert, and
            // every value below was doing less work than it looked like it was. It is left at 1
            // so the number on screen is the truth.
            //
            // Second: the old bands were far too saturated and in the wrong order. Equator green
            // was BRIGHTER than sky blue, which inverts the outdoor gradient and flattens form,
            // and all three were near-pure hues — so the probe was not filling shaded surfaces,
            // it was dyeing them. A prop's red channel got multiplied by 0.16 and its blue by
            // 0.09, which is how a warm mid-brown bench arrived at black and how every green in
            // the venue converged on the same green. Pale bands in the right order fill without
            // dyeing: sky clearly the brightest, warm sage at the horizon, warm olive bounce off
            // the turf underneath. Nothing reaches black, hues survive, and up still reads
            // brighter than sideways which is where the form comes from.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#BBDCF7");
            RenderSettings.ambientEquatorColor = Hex("#A9BE93");
            RenderSettings.ambientGroundColor = Hex("#7C8F52");
            RenderSettings.ambientIntensity = 1f;

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

        /// <summary>Internal so the menu is graded by the same profile, not a copy of it.</summary>
        internal static Volume BuildPostProcessing()
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
            //
            // RemoveObjectFromAsset is not optional here: an override that is still a sub-asset
            // when it is destroyed leaves a dead object id behind in the file, and the profile
            // reloads with a null in its component list.
            for (int i = profile.components.Count - 1; i >= 0; i--)
            {
                var c = profile.components[i];
                profile.components.RemoveAt(i);
                if (c == null) continue;
                AssetDatabase.RemoveObjectFromAsset(c);
                Object.DestroyImmediate(c, true);
            }

            // VolumeProfile.Add<T> is the RUNTIME API: it does a plain CreateInstance and puts the
            // result in the component list, and nothing ever makes that object part of the asset.
            // It survives until the next domain reload and then every entry in the list comes back
            // null — which is exactly the state DuckPostProfile.asset was found in, five overrides
            // all serialised as {fileID: 0}. So the game has been shipping with NO tonemapping, NO
            // grading and NO bloom since this function was written, while the code below read as
            // if the whole look pass were live. Everything else in this file is a scene object and
            // gets saved with the scene; a VolumeProfile is an asset and needs this.
            T Persisted<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            var tone = Persisted<Tonemapping>();
            tone.mode.overrideState = true;
            tone.mode.value = TonemappingMode.Neutral;   // ACES eats this palette alive

            var colour = Persisted<ColorAdjustments>();
            // Neutral's shoulder pulls the mid-highs down in exchange for not clipping them, so a
            // little exposure back on top keeps the set as bright as it was while the cut grass and
            // the characters' cream keep their hue at the top end instead of flattening to white.
            // Back to no post exposure. 0.18 was chosen while the volume profile was empty and the
            // pipeline had HDR off, so it was compensating for a grading pass that never ran. Both
            // of those are fixed now, and a lift that was invisible then is a lift on top of a live
            // tonemap — three separate brightenings (ambient, exposure, tonemap) stacked into a
            // glare with no shading left in it.
            colour.postExposure.overrideState = true; colour.postExposure.value = 0f;
            // Both of these were working against the thing this pass exists to fix. Contrast pivots
            // on mid grey and its whole effect below the pivot is to deepen shadows; global
            // saturation amplifies whatever hue already dominates, and on a frame that is four
            // greens deep that pushes them together rather than apart. The palette and the ambient
            // gradient carry the colour now, so these only need to season it.
            colour.contrast.overrideState = true; colour.contrast.value = 4f;
            colour.saturation.overrideState = true; colour.saturation.value = 8f;

            var bloom = Persisted<Bloom>();
            // Bloom runs before the tonemap, so the numbers it sees are the raw ones: with the
            // ambient gradient rebuilt, sunlit cream lands near 2.3 luminance and sunlit cut grass
            // near 1.6. 1.4 puts the characters and the chalk four times further over the knee than
            // the lawn is, which is the point — the crowd and the judges should catch the light, not
            // the sixty-four metres of grass behind them.
            // Threshold up and intensity down. The 1.4/0.26 pair was sized against the old, dimmer
            // frame; once ambient and tonemapping came online the whole image sat higher, so far
            // more of it cleared the knee than was intended and the set read as glaring.
            bloom.threshold.overrideState = true; bloom.threshold.value = 1.9f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.14f;
            bloom.scatter.overrideState = true; bloom.scatter.value = 0.62f;
            bloom.tint.overrideState = true; bloom.tint.value = Hex("#FFF6E2");
            // Bloom is the only genuinely per-pixel cost in this profile — everything else bakes
            // into the one grading LUT. Quarter-res with four iterations keeps it to a fraction of
            // a millisecond on the WebGL target and at this radius the difference is not visible.
            bloom.downscale.overrideState = true; bloom.downscale.value = BloomDownscaleMode.Quarter;
            bloom.maxIterations.overrideState = true; bloom.maxIterations.value = 4;
            bloom.highQualityFiltering.overrideState = true; bloom.highQualityFiltering.value = false;

            var vig = Persisted<Vignette>();
            // Down from 0.26, and off green. The 0.26 never actually shipped — the profile was
            // empty — so switching the profile on would have introduced a dark green frame edge on
            // the same day the complaint was that the map feels dark and empty. Kept only as much
            // as holds the eye in the middle of the picture.
            vig.intensity.overrideState = true; vig.intensity.value = 0.10f;
            vig.smoothness.overrideState = true; vig.smoothness.value = 0.6f;
            vig.color.overrideState = true; vig.color.value = Hex("#3A4A55");

            var split = Persisted<SplitToning>();
            split.shadows.overrideState = true; split.shadows.value = Hex("#5C86C8");
            split.highlights.overrideState = true; split.highlights.value = Hex("#FFE9BF");
            split.balance.overrideState = true; split.balance.value = -18f;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

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
            rb.mass = MowerContact.ChassisMass;
            rb.linearDamping = 0f;
            rb.angularDamping = 2.2f;

            // From MowerContact, for the same reason as the authored path in DuckModelIntegration:
            // this box IS the game's obstacle collision, and the venue's props are all checked
            // against the contact band it produces.
            var box = root.AddComponent<BoxCollider>();
            box.size = MowerContact.ChassisSize;
            box.center = MowerContact.ChassisCentre;

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
            // HDR on, because the pipeline asset alone is not enough — URP takes the AND of the
            // asset's supportsHDR and this flag when it picks the colour buffer format. The asset
            // was just switched on so that tonemapping would run at all (URP only sets the
            // Tonemap keyword in its HDR branch), and leaving this false would have quietly kept
            // the whole grading pass a no-op while the settings all read as if it were live.
            cam.allowHDR = true;
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

        /// <summary>Internal: the menu stands the same three judges at the same bench.</summary>
        internal static JudgePanel BuildJudgeBench()
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
                //
                // 0.68, not 0.50. The bench top is at y = 0.83, and at 0.50 a judge's shoulders sat
                // level with it: their arms hung down inside the desk and the whole skinned rig
                // reduced to a head and a bow tie floating above a plank. Every gesture the animator
                // plays — the applaud, the head shake, the lean — happens in the arms and chest, and
                // none of it was on screen. At 0.68 the base is still below the tabletop, so they
                // read as seated behind the bench rather than standing on it, while the shoulders
                // clear it by about 0.24 and the hands come to rest near desk height.
                //
                // DeskCardPosition in DuckModelIntegration is the counterweight to this number: the
                // card hinge is judge-local, so raising the judge lifts the scorecard off the bench
                // unless that y comes down by the same amount. Change one and change the other.
                inst.transform.localPosition = new Vector3(xs[i], 0.68f, -0.10f + Mathf.Abs(xs[i]) * 0.10f);
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

    }
}
