using System.Text;
using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Repeatable "why can I not see that" report. Screenshots tell you something is wrong;
    /// this tells you which layer of the stack it is wrong in.
    /// </summary>
    public static class DuckDiagnostics
    {
        [MenuItem("Duck/Diagnose · Shaders", priority = 40)]
        public static void DiagnoseShaders()
        {
            var sb = new StringBuilder("[Duck] SHADER REPORT\n");
            string[] names =
            {
                "Duck/Prop", "Duck/GrassGround", "Duck/GrassBlades",
                "Duck/ChalkGuide", "Duck/CutStamp", "Duck/SkyGradient"
            };
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (sh == null) { sb.AppendLine($"  {n}: NOT FOUND"); continue; }
                int errors = ShaderUtil.GetShaderMessageCount(sh);
                sb.AppendLine($"  {n}: supported={sh.isSupported} messages={errors}");
                if (errors > 0)
                {
                    var msgs = ShaderUtil.GetShaderMessages(sh);
                    for (int i = 0; i < msgs.Length && i < 8; i++)
                        sb.AppendLine($"      [{msgs[i].severity}] {msgs[i].message} | {msgs[i].messageDetails} (line {msgs[i].line})");
                }
            }
            Debug.Log(sb.ToString());
        }

        [MenuItem("Duck/Diagnose · Mower visibility", priority = 41)]
        public static void DiagnoseMower()
        {
            var sb = new StringBuilder("[Duck] MOWER VISIBILITY REPORT\n");

            var mower = GameObject.Find("Mower");
            var camGO = GameObject.Find("MainCamera");
            if (mower == null || camGO == null) { Debug.LogError("[Duck] Mower or MainCamera missing"); return; }

            var cam = camGO.GetComponent<Camera>();
            sb.AppendLine($"  camera pos={cam.transform.position} fwd={cam.transform.forward} fov={cam.fieldOfView} mask={cam.cullingMask} enabled={cam.enabled}");
            sb.AppendLine($"  mower  pos={mower.transform.position} scale={mower.transform.lossyScale}");
            sb.AppendLine($"  distance = {Vector3.Distance(cam.transform.position, mower.transform.position):0.00} m");

            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var renderers = mower.GetComponentsInChildren<MeshRenderer>(true);
            sb.AppendLine($"  renderers = {renderers.Length}");

            int visible = 0, inFrustum = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                bool inF = GeometryUtility.TestPlanesAABB(planes, r.bounds);
                if (r.isVisible) visible++;
                if (inF) inFrustum++;
                if (i < 5)
                {
                    var mat = r.sharedMaterial;
                    string shaderName = mat != null && mat.shader != null ? mat.shader.name : "<none>";
                    bool supported = mat != null && mat.shader != null && mat.shader.isSupported;
                    sb.AppendLine($"    [{i}] {r.name} enabled={r.enabled} isVisible={r.isVisible} inFrustum={inF} " +
                                  $"bounds={r.bounds.center}/{r.bounds.size} layer={LayerMask.LayerToName(r.gameObject.layer)} " +
                                  $"mat={(mat != null ? mat.name : "<null>")} shader={shaderName} supported={supported} " +
                                  $"meshVerts={(r.GetComponent<MeshFilter>()?.sharedMesh != null ? r.GetComponent<MeshFilter>().sharedMesh.vertexCount : -1)} " +
                                  $"hasColors={(r.GetComponent<MeshFilter>()?.sharedMesh != null && r.GetComponent<MeshFilter>().sharedMesh.colors32.Length > 0)}");
                }
            }
            sb.AppendLine($"  visible={visible}/{renderers.Length}  inFrustum={inFrustum}/{renderers.Length}");

            // What is actually between the camera and the mower?
            Vector3 from = cam.transform.position;
            Vector3 to = mower.transform.position + Vector3.up * 0.4f;
            var hits = Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to));
            sb.AppendLine($"  line of sight blockers = {hits.Length}");
            foreach (var h in hits)
                sb.AppendLine($"    {h.collider.name} @ {h.distance:0.0} m layer={LayerMask.LayerToName(h.collider.gameObject.layer)}");

            Debug.Log(sb.ToString());
        }

        [MenuItem("Duck/Diagnose · Scene stats", priority = 42)]
        public static void DiagnoseScene()
        {
            var sb = new StringBuilder("[Duck] SCENE STATS\n");
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            long tris = 0;
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            }
            sb.AppendLine($"  MeshRenderers = {renderers.Length}, total triangles (unculled) = {tris:N0}");
            sb.AppendLine($"  Lights = {Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length}");
            sb.AppendLine($"  shadow distance = {UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset?.shadowDistance}");
            Debug.Log(sb.ToString());
        }

        [MenuItem("Duck/Diagnose · Runtime state", priority = 47)]
        public static void RuntimeState()
        {
            var sb = new StringBuilder("[Duck] RUNTIME STATE\n");
            var d = Object.FindFirstObjectByType<GameDirector>();
            var m = Object.FindFirstObjectByType<MowerController>();
            var a = Object.FindFirstObjectByType<Autopilot>();
            var i = Object.FindFirstObjectByType<InputReader>();
            var cm = Object.FindFirstObjectByType<CutMask>();

            if (d == null) { Debug.LogWarning("[Duck] Not in play mode."); return; }
            sb.AppendLine($"  director: state={d.State} stateTime={d.StateTime:0.00} ticks={d.UpdateTicks} " +
                          $"timeLeft={d.TimeRemaining:0.0}/{d.RoundLength:0.0} autopilotEnabled={d.autopilotEnabled} round={d.RoundNumber}");
            sb.AppendLine($"  time: scale={Time.timeScale} delta={Time.deltaTime:0.0000} unscaled={Time.unscaledDeltaTime:0.0000} " +
                          $"frame={Time.frameCount} fixedDelta={Time.fixedDeltaTime:0.0000} runInBackground={Application.runInBackground}");
            sb.AppendLine($"  director refs: target={(d.target != null)} cutMask={(d.cutMask != null)} mower={(d.mower != null)} autopilot={(d.autopilot != null)} chalkMat={(d.chalkMaterial != null)}");

            if (i != null)
                sb.AppendLine($"  input: steer={i.Steer:0.00} throttle={i.Throttle:0.00} driving={i.DrivingEnabled} override={i.OverrideActive}");
            if (a != null)
                sb.AppendLine($"  autopilot: active={a.Active} waypoint={a.WaypointIndex}/{a.WaypointCount} mowerRef={(a.mower != null)} targetRef={(a.target != null)}");
            if (m != null)
            {
                var rb = m.GetComponent<Rigidbody>();
                sb.AppendLine($"  mower: pos={m.transform.position} fwdSpeed={m.ForwardSpeed:0.00} grounded={m.IsGrounded} blade={m.BladeEngaged} " +
                              $"cutLoad={m.CutLoad:0.000} dist={m.DistanceTravelled:0.0} vel={(rb != null ? rb.linearVelocity.ToString("0.00") : "?")} mask={m.groundMask.value}");
                sb.AppendLine($"  deck world pos = {m.DeckWorldPosition}");
            }
            if (cm != null)
                sb.AppendLine($"  cutmask: instance={(CutMask.Instance != null)} cells={cm.CutCellCount} rt={(cm.MaskTexture != null ? cm.MaskTexture.width.ToString() : "null")}");

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Background agents write into Assets/ constantly, and every write kicks the editor out
        /// of play mode. This makes iterating on feel possible.
        /// </summary>
        [MenuItem("Duck/Toggle auto asset refresh", priority = 60)]
        public static void ToggleAutoRefresh()
        {
            bool on = EditorPrefs.GetBool("kAutoRefresh", true);
            EditorPrefs.SetBool("kAutoRefresh", !on);
            Debug.Log($"[Duck] Auto asset refresh is now {(!on ? "ON" : "OFF")}");
        }
    }
}
