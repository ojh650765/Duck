using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Builds the mower's particle systems.
    ///
    /// All four are emission-driven from script rather than by their own rate modules, because
    /// the interesting behaviour — throw clippings only where there is grass left to cut — is a
    /// gameplay signal, not a curve. The systems here just define how a particle looks and dies.
    /// </summary>
    public static class DuckVFXBuilder
    {
        const string TexDir = "Assets/Art/Textures/Particles";
        const string MatDir = "Assets/Materials";

        public static void Build(GameObject mowerGO, MowerController mower)
        {
            var visuals = mowerGO.GetComponent<MowerVisuals>();
            var root = new GameObject("VFX").transform;
            root.SetParent(mowerGO.transform, false);

            var vfx = mowerGO.AddComponent<MowerVFX>();
            vfx.mower = mower;

            // The deck is where cuttings come from; fall back to the controller's own deck offset.
            var deck = visuals != null && visuals.bladeSpinner != null
                ? visuals.bladeSpinner
                : CreateAnchor(root, "DeckPoint", mower.deckOffset);
            vfx.deckPoint = deck;
            vfx.exhaustPoint = visuals != null && visuals.exhaust != null
                ? visuals.exhaust
                : CreateAnchor(root, "ExhaustPoint", new Vector3(0.28f, 0.9f, 0.25f));

            vfx.clippings = BuildClippings(root);
            vfx.dust = BuildDust(root);
            vfx.exhaust = BuildExhaust(root);
            vfx.boostTrail = BuildBoost(root);
        }

        static Transform CreateAnchor(Transform parent, string name, Vector3 local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            return go.transform;
        }

        static Material ParticleMaterial(string name, string textureFile, Color tint, bool additive)
        {
            var mat = DuckSceneBuilder.EnsureMaterial(name, "Universal Render Pipeline/Particles/Unlit");
            if (mat == null) return null;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{textureFile}");
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Surface", 1f);                 // transparent
            mat.SetFloat("_Blend", additive ? 1f : 0f);   // additive vs alpha
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static ParticleSystem MakeSystem(Transform parent, string name, Material mat,
                                         int maxParticles, float gravity, bool sheet2x2)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;      // velocities come from EmitParams
            main.startLifetime = 1f;
            main.startSize = 0.1f;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            var emission = ps.emission;
            emission.rateOverTime = 0f;   // script drives every emission

            var shape = ps.shape;
            shape.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.alignment = ParticleSystemRenderSpace.View;

            if (sheet2x2)
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.numTilesX = 2;
                tsa.numTilesY = 2;
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            }

            return ps;
        }

        static ParticleSystem BuildClippings(Transform parent)
        {
            var mat = ParticleMaterial("M_FX_Clippings", "clipping_sprite_128.png",
                                       DuckSceneBuilder.Hex("#A8CB55"), false);
            var ps = MakeSystem(parent, "FX_Clippings", mat, 420, 2.6f, true);

            var main = ps.main;
            main.startRotation3D = true;

            // Cuttings tumble as they fly, which is most of what makes them read as blades of
            // grass rather than as sparks.
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(
                DuckSceneBuilder.Hex("#B6D45F"), DuckSceneBuilder.Hex("#6E9E37")));

            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.12f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = ClippingMesh();
            renderer.sharedMaterial = ParticleMaterial("M_FX_ClippingMesh", null,
                                                       DuckSceneBuilder.Hex("#A8CB55"), false);
            return ps;
        }

        /// <summary>A tiny bent blade, so cuttings are geometry rather than square sprites.</summary>
        static Mesh ClippingMesh()
        {
            const string path = "Assets/Meshes/Generated/ClippingBlade.asset";
            var cached = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (cached != null) return cached;

            var verts = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(0.32f, 0f, 1.1f), new Vector3(-0.18f, 0.22f, 1.9f)
            };
            var tris = new[] { 0, 2, 1, 0, 3, 2 };
            var cols = new Color32[verts.Length];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);

            var mesh = new Mesh { name = "ClippingBlade" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetColors(cols);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (!AssetDatabase.IsValidFolder("Assets/Meshes")) AssetDatabase.CreateFolder("Assets", "Meshes");
            if (!AssetDatabase.IsValidFolder("Assets/Meshes/Generated"))
                AssetDatabase.CreateFolder("Assets/Meshes", "Generated");
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static ParticleSystem BuildDust(Transform parent)
        {
            var mat = ParticleMaterial("M_FX_Dust", "dust_puff_128.png",
                                       new Color(0.86f, 0.83f, 0.70f, 0.55f), false);
            var ps = MakeSystem(parent, "FX_Dust", mat, 160, -0.05f, false);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeOut(new Color(0.88f, 0.86f, 0.74f), 0.5f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));
            return ps;
        }

        static ParticleSystem BuildExhaust(Transform parent)
        {
            var mat = ParticleMaterial("M_FX_Exhaust", "dust_puff_128.png",
                                       new Color(0.34f, 0.34f, 0.36f, 0.42f), false);
            var ps = MakeSystem(parent, "FX_Exhaust", mat, 90, -0.35f, false);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeOut(new Color(0.42f, 0.42f, 0.44f), 0.45f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 2.4f));
            return ps;
        }

        static ParticleSystem BuildBoost(Transform parent)
        {
            var mat = ParticleMaterial("M_FX_Boost", "spark_128.png",
                                       new Color(1f, 0.82f, 0.42f, 1f), true);
            var ps = MakeSystem(parent, "FX_Boost", mat, 120, -0.1f, false);

            var main = ps.main;
            main.startLifetime = 0.35f;
            main.startSize = 0.22f;
            main.startSpeed = 0f;

            var emission = ps.emission;
            emission.enabled = false;
            emission.rateOverTime = 55f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.7f, 0.2f, 0.2f);
            shape.position = new Vector3(0f, 0.25f, -0.7f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeOut(new Color(1f, 0.85f, 0.45f), 1f));
            return ps;
        }

        static Gradient Fade(Color a, Color b)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static Gradient FadeOut(Color c, float peak)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, 0.2f),
                    new GradientAlphaKey(0f, 1f)
                });
            return g;
        }
    }
}
