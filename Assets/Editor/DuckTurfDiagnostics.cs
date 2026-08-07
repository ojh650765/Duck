using System.Text;
using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Why is the Bloom Rush floor black?
    ///
    /// Every failure this mode has looks identical from a screenshot — a flat, unlit ground — and
    /// they have completely different causes: a shader that did not compile, a material pointing at
    /// the wrong shader, a mask that was never created, a mask created but never bound as a global,
    /// or an arena whose validity test rejects every cell so the whole floor is drawn as hedge soil.
    /// Guessing between those costs a rebuild each. This prints which one it is.
    /// </summary>
    public static class DuckTurfDiagnostics
    {
        [MenuItem("Duck/Bloom · Diagnose the turf", priority = 46)]
        public static void Diagnose()
        {
            var sb = new StringBuilder("[Bloom] diagnosis\n");

            foreach (string name in new[] { "Duck/TurfGround", "Duck/TurfBlades", "Duck/TurfStamp",
                                            "Duck/TurfGlow", "Duck/TurfMapUI" })
            {
                var sh = Shader.Find(name);
                if (sh == null) { sb.AppendLine($"  {name}: NOT FOUND"); continue; }

                int count = ShaderUtil.GetShaderMessageCount(sh);
                sb.AppendLine($"  {name}: supported={sh.isSupported} errors={ShaderUtil.ShaderHasError(sh)} messages={count}");
                if (count == 0) continue;

                var msgs = ShaderUtil.GetShaderMessages(sh);
                for (int i = 0; i < msgs.Length && i < 8; i++)
                    sb.AppendLine($"      [{msgs[i].severity}] line {msgs[i].line}: {msgs[i].message} {msgs[i].messageDetails}");
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_BloomTurf.mat");
            sb.AppendLine(mat == null
                ? "  M_BloomTurf.mat: MISSING"
                : $"  M_BloomTurf.mat: shader={mat.shader.name} supported={mat.shader.isSupported}");

            var ground = GameObject.Find("Ground");
            if (ground == null) sb.AppendLine("  Ground object: MISSING");
            else
            {
                var mf = ground.GetComponent<MeshFilter>();
                var mr = ground.GetComponent<MeshRenderer>();
                sb.AppendLine($"  Ground: mesh={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " verts " + mf.sharedMesh.vertexCount : "NONE")} " +
                              $"material={(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.name : "NONE")}");
            }

            // The arena's own maths, sampled where the answer is known: the hub must be playable, the
            // middle of a hedge run must not be, and a gap must be. A test that fails here explains a
            // floor drawn entirely as soil without anybody having to look at a shader.
            sb.AppendLine($"  playable at hub centre: {TurfArena.IsPlayable(0f, 0f)} (expect True)");
            Vector3 loop = TurfArena.Bearing(TurfArena.SpokeAngle(0)) * TurfArena.LoopMid;
            sb.AppendLine($"  playable on the loop:   {TurfArena.IsPlayable(loop)} (expect True)");
            Vector3 spoke = TurfArena.Bearing(TurfArena.SpokeAngle(0)) * TurfArena.HedgeMid;
            sb.AppendLine($"  playable in a spoke:    {TurfArena.IsPlayable(spoke)} (expect True)");
            Vector3 choke = TurfArena.Bearing(TurfArena.ChokeAngle(0)) * TurfArena.HedgeMid;
            sb.AppendLine($"  playable in a choke:    {TurfArena.IsPlayable(choke)} (expect True), " +
                          $"height {TurfArena.GroundHeight(choke):0.00} m (expect ~{TurfArena.RampHeight:0.00})");
            Vector3 wall = TurfArena.Bearing(TurfArena.SpokeAngle(0) + 22.5f) * TurfArena.HedgeMid;
            sb.AppendLine($"  playable inside hedge:  {TurfArena.IsPlayable(wall)} (expect False)");
            sb.AppendLine($"  playable outside arena: {TurfArena.IsPlayable(0f, TurfArena.ArenaRadius + 5f)} (expect False)");

            var mask = Object.FindFirstObjectByType<TurfMask>();
            if (mask == null) sb.AppendLine("  TurfMask: not in scene (expected outside play mode)");
            else sb.AppendLine($"  TurfMask: playable cells {mask.PlayableCells} of {TurfMask.GridRes * TurfMask.GridRes}");

            sb.AppendLine($"  binder in scene: {Object.FindFirstObjectByType<TurfPaletteBinder>() != null}");

            // The globals as the SHADER would see them, read back rather than assumed. Every symptom
            // this tool exists for looks the same from a screenshot, and "the constants were never
            // pushed" and "the constants were pushed and are wrong" are different bugs.
            sb.AppendLine($"  _TurfSize     = {Shader.GetGlobalFloat(TurfPalette.IdSize)} (expect {TurfMask.Size})");
            sb.AppendLine($"  _TurfGeometry = {Shader.GetGlobalVector(TurfPalette.IdGeometry)} " +
                          $"(expect {TurfArena.ArenaRadius}, {TurfArena.PlazaRadius}, " +
                          $"{TurfArena.HedgeInner}, {TurfArena.HedgeOuter})");
            sb.AppendLine($"  _TurfGaps     = {Shader.GetGlobalVector(TurfPalette.IdGaps)}");
            var liv = Shader.GetGlobalVectorArray(TurfPalette.IdLivery);
            sb.AppendLine($"  _TurfLivery   = {(liv == null ? "NOT SET" : liv.Length + " entries, [0] = " + liv[0])}");

            Debug.Log(sb.ToString());
            System.IO.Directory.CreateDirectory("Captures/Bloom");
            System.IO.File.WriteAllText("Captures/Bloom/diagnosis.txt", sb.ToString());
        }
    }
}
