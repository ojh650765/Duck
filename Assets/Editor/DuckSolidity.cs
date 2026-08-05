using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// One answer to "can the mower hit this?", used everywhere a prop is created, and an audit that
    /// proves the answer held for the scene that was actually built.
    ///
    /// ---- THE BUG THIS FILE EXISTS TO END ----
    ///
    /// "무시되는 장애물이 있음" — some obstacles are ignored — has been reported three times and
    /// answered three times without the mechanism ever being found. Floating gnomes were grounded;
    /// short gnomes were made tall; the props that were "still small" were deleted. Then two more
    /// appeared, because there were two independent producers of drawn-but-not-solid geometry and
    /// neither had anything to do with the props that were fixed:
    ///
    ///   1. DuckMeshLibrary.Box builds every primitive prop in the game with
    ///      GameObject.CreatePrimitive, which arrives WITH a collider, and then destroys it —
    ///      unconditionally, with no reference to where the box stands. The judges' bench and the
    ///      plot's marker board are built that way and both stand inside the fence, so the mower
    ///      drove through a six-metre bench and a signpost. Nothing about them looked wrong in the
    ///      scene: they had the right size, the right position, the right layer and no collider at
    ///      all, which is invisible in a hierarchy screenshot.
    ///
    ///   2. Props shorter than the mower's contact band. The mower's only collider is a box held
    ///      0.44 m off the ground, so it can only touch things between y 0.24 and y 0.76 — see
    ///      MowerContact. A prop under about a quarter of a metre tall can be drawn, be given a
    ///      collider that matches its mesh perfectly, and still be untouchable. That is what "the
    ///      ones that are still small are the only ones that do not collide" was.
    ///
    /// Both are the same failure of authorship: solidity was a decision someone made by hand, per
    /// prop, against a number written down in a comment in another file. Here it is a function of
    /// the geometry — where the prop stands and how tall it is — so it cannot be forgotten, and the
    /// cases geometry cannot fix are reported as errors instead of shipping silently.
    /// </summary>
    public static class DuckSolidity
    {
        /// <summary>
        /// The ground and the things painted on it. These are not obstacles when they are drawn, and
        /// they must not be treated as obstacles when they carry a collider either — the surround IS
        /// the floor, and one mesh collider 940 m across would otherwise "cover" every prop in the
        /// venue and certify the whole level as solid. That mistake is not hypothetical: the first
        /// run of this audit reported zero faults for exactly that reason.
        /// </summary>
        static readonly string[] GroundLike =
        {
            "Surround", "LawnGround", "Lawn", "Apron", "Chalk", "Path", "Lane", "Grass", "Blade",
            "Hill_", "Plaza", "Basin", "Water", "PondBank", "Guide", "Stamp", "CutMask", "Ground",
        };

        /// <summary>
        /// Drawn things that are never obstacles: the ground, plus the signage, cards and text the
        /// judging sequence flies through, plus everything that already owns its collision.
        /// </summary>
        static readonly string[] NeverSolid =
        {
            // signage and text that hangs in the air by design
            "Bunting", "Pennant", "Card", "Quip", "Grade", "Label", "Title", "Subtitle", "Text",
            "Rosette",
            // things that carry their own collision, or must never gain any
            "Mower", "Duck", "Driver", "Rival", "Spectator", "Gnome", "Sails", "Bound_",
        };

        static bool NamedGround(string name)
        {
            foreach (string n in GroundLike)
                if (name.Contains(n)) return true;
            return false;
        }

        enum Excuse { None, ByName, ByBody }

        static Excuse Excused(GameObject go)
        {
            if (NamedGround(go.name)) return Excuse.ByName;
            foreach (string n in NeverSolid)
                if (go.name.Contains(n)) return Excuse.ByName;
            // Anything belonging to a rigidbody is that body's business: extra colliders would join
            // its compound shape and fight the ground it is standing on. The mower's visual proxy
            // lives under the mower's own body, and every gnome already has its capsule. Not worth
            // reporting — it is a structural fact, not a judgement call.
            if (go.GetComponentInParent<Rigidbody>() != null) return Excuse.ByBody;
            if (go.GetComponentInParent<Canvas>() != null) return Excuse.ByBody;
            return Excuse.None;
        }

        /// <summary>World-space AABB of a mesh under a transform, without asking the renderer.</summary>
        public static Bounds WorldBounds(Transform t, Mesh mesh)
        {
            Bounds local = mesh.bounds;
            Vector3 c = local.center, e = local.extents;
            var result = new Bounds(t.TransformPoint(c + new Vector3(-e.x, -e.y, -e.z)), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -e.x : e.x,
                                         (i & 2) == 0 ? -e.y : e.y,
                                         (i & 4) == 0 ? -e.z : e.z);
                result.Encapsulate(t.TransformPoint(c + corner));
            }
            return result;
        }

        /// <summary>
        /// Can the mower be driven at this box at all? True if any part of its footprint is inside
        /// the fence the mower is shut in by.
        /// </summary>
        public static bool InReach(Bounds b)
        {
            float r = MowerContact.ReachRadius;
            return b.min.x < r && b.max.x > -r && b.min.z < r && b.max.z > -r;
        }

        /// <summary>
        /// The whole decision, in one place: a prop must be solid when the mower can be driven at it
        /// AND it fills enough of the chassis's contact band for the hit to be reliable.
        /// </summary>
        public static bool MustBeSolid(Bounds b)
            => InReach(b) && MowerContact.CanBeHit(b.min.y, b.max.y);

        /// <summary>
        /// The same question for a mesh about to be placed, measured on the parts rather than on the
        /// whole.
        ///
        /// A single box round a whole prop answers wrongly for anything tall standing just outside the
        /// fence: a full-grown oak planted at x = 41 has five metres of canopy overhanging the wall, so
        /// its box straddles the playable area and spans the contact band, and the naive test gave
        /// collision to fifteen trees the mower can never touch — the trunk is outside the wall and the
        /// leaves are three metres up. So when the cheap box test says yes, it is confirmed against the
        /// mesh's own connected pieces, which is what the audit measures. Only a piece that is BOTH in
        /// reach AND in the band makes the prop solid.
        /// </summary>
        public static bool MustBeSolid(Mesh mesh, Matrix4x4 trs)
        {
            if (mesh == null) return false;
            var whole = TransformBounds(mesh.bounds, trs);
            if (!MustBeSolid(whole)) return false;

            // One piece: the box was already exact.
            var parts = DuckMeshAudit.MeshClusters(mesh, trs);
            if (parts.Count <= 1) return true;
            foreach (var part in parts)
                if (MustBeSolid(part)) return true;
            return false;
        }

        /// <summary>A local box under a matrix, in world space.</summary>
        public static Bounds TransformBounds(Bounds local, Matrix4x4 m)
        {
            Vector3 c = local.center, e = local.extents;
            var result = new Bounds(m.MultiplyPoint3x4(c - e), Vector3.zero);
            for (int i = 1; i < 8; i++)
                result.Encapsulate(m.MultiplyPoint3x4(c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return result;
        }

        /// <summary>
        /// The failure case: standing where the mower can reach it and showing something at chassis
        /// height, but too little of it for a contact to be dependable. Nothing the builder can do
        /// makes this prop solid — it is too short, or it is perched too high — so it has to move out
        /// of the playable area or change size, and until it does it will read to the player as an
        /// obstacle that the mower ignores.
        /// </summary>
        public static bool IsUnhittableObstacle(Bounds b)
            => InReach(b)
            && MowerContact.PresentsInBand(b.min.y, b.max.y)
            && !MowerContact.CanBeHit(b.min.y, b.max.y);

        // ------------------------------------------------------------------ the audit / the pass

        const string ReportFile = "DuckObstacleSolidity.txt";

        [MenuItem("Duck/Diagnose · Audit obstacle solidity (open scene)", priority = 55)]
        public static void AuditMenu() => Run(false);

        /// <summary>
        /// Called at the end of the scene build. Adds any collider the geometry says is required and
        /// reports anything that cannot be made solid.
        /// </summary>
        public static int Enforce() => Run(true);

        struct Piece
        {
            public string owner;
            public MeshRenderer renderer;
            public Bounds bounds;
            /// <summary>Why it is off the hook, if it is. Name-based excuses are reported.</summary>
            public Excuse excuse;
        }

        static int Run(bool apply)
        {
            var sb = new StringBuilder(apply ? "[Duck] OBSTACLE SOLIDITY\n" : "[Duck] OBSTACLE SOLIDITY AUDIT\n");
            sb.AppendLine($"  {MowerContact.Describe()}");
            sb.AppendLine($"  mower can reach x,z within +-{MowerContact.ReachRadius:0.0} m " +
                          $"(measured wall: {MeasuredReach()})");

            Physics.SyncTransforms();
            var solid = CollisionBoxes();
            sb.AppendLine($"  {solid.Count} collision volumes in the scene.");

            int mustBeSolid = 0, missing = 0, added = 0, unhittable = 0, outOfReach = 0, notInBand = 0;
            int excused = 0, shielded = 0;
            var report = new List<string>();

            foreach (var piece in VisualPieces())
            {
                var b = piece.bounds;
                if (!InReach(b)) { outOfReach++; continue; }

                float overlap = MowerContact.BandOverlap(b.min.y, b.max.y);
                if (overlap <= 0f) { notInBand++; continue; }

                // On the never-solid list, but standing where the mower drives at chassis height.
                // Listed rather than dropped: that list is the one hand-made judgement left in this
                // check, and a wrong entry on it is exactly the kind of silence that hid this bug.
                if (piece.excuse == Excuse.ByBody) continue;
                if (piece.excuse == Excuse.ByName)
                {
                    excused++;
                    report.Add($"    excused  {piece.owner}: y {b.min.y:0.00}..{b.max.y:0.00} " +
                               $"({overlap:0.00} m in band) at ({b.center.x:0.0}, {b.center.z:0.0}) " +
                               $"- named on the never-solid list");
                    continue;
                }

                if (overlap < MowerContact.MinContact)
                {
                    // Only a fault if the mower can actually GET to it. A prop's own solid parts
                    // shield the rest of it: the bench's top slab shows 3 cm in the band but stands
                    // behind a 0.66 m front panel, a stake's cap sits on top of the stake, a trophy
                    // sits on its plinth. Reporting those buries the real finding — a prop with
                    // nothing solid anywhere near it, which is the one the mower sails through.
                    if (Shielded(b, solid)) { shielded++; continue; }

                    unhittable++;
                    report.Add($"    UNHITTABLE {piece.owner}: spans y {b.min.y:0.00}..{b.max.y:0.00}, " +
                               $"only {overlap:0.00} m of the {MowerContact.MinContact:0.00} m the chassis " +
                               $"needs, and nothing solid within {ShieldMargin:0.0} m of it, at " +
                               $"({b.center.x:0.0}, {b.center.z:0.0}). Too small to be an obstacle where " +
                               $"the mower can drive — move it outside the fence or size it up.");
                    continue;
                }

                mustBeSolid++;
                string by = CoveredBy(b, solid);
                if (by != null)
                {
                    report.Add($"    solid    {piece.owner}: y {b.min.y:0.00}..{b.max.y:0.00} " +
                               $"({overlap:0.00} m in band) at ({b.center.x:0.0}, {b.center.z:0.0}) " +
                               $"size {b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00} <- {by}");
                    continue;
                }

                missing++;
                report.Add($"    NOT SOLID {piece.owner}: spans y {b.min.y:0.00}..{b.max.y:0.00} " +
                           $"({overlap:0.00} m in the band) at ({b.center.x:0.0}, {b.center.z:0.0}) " +
                           $"with no collision volume{(apply ? " — collider added from its own mesh." : ".")}");

                if (apply && AddCollider(piece.renderer))
                {
                    added++;
                    // So later pieces sharing this renderer, and the Covered test itself, see it.
                    Physics.SyncTransforms();
                    solid = CollisionBoxes();
                }
            }

            sb.AppendLine($"  {mustBeSolid} obstacles must be solid; {missing} were not" +
                          (apply ? $"; {added} fixed." : "."));
            sb.AppendLine($"  {unhittable} in reach but too small to hit, {shielded} too small but " +
                          $"standing behind something solid, {excused} excused by name.");
            sb.AppendLine($"  {outOfReach} pieces out of the mower's reach, " +
                          $"{notInBand} entirely above or below the contact band — neither can be driven through.");
            if (report.Count == 0)
                report.Add("    Every obstacle the mower can reach is solid.");

            // The full table goes to a file. A hundred-odd lines in the console gets truncated by
            // every reader of it, which is how an audit ends up being trusted rather than read.
            string full = sb.ToString() + string.Join("\n", report) + "\n";
            string path = System.IO.Path.Combine(Application.dataPath, "..", "Temp", ReportFile);
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, full);
                sb.AppendLine($"  full table: Temp/{ReportFile}");
            }
            catch (System.Exception e) { sb.AppendLine($"  (could not write the table: {e.Message})"); }

            // The console gets the summary and the faults, never the passes.
            foreach (string line in report)
                if (!line.Contains("solid    ") && !line.Contains("excused  ")) sb.AppendLine(line);

            string text = sb.ToString();
            if (unhittable > 0 || (missing > added))
                Debug.LogError(text);
            else
                Debug.Log(text);

            return unhittable + (missing - added);
        }

        /// <summary>The fence, as built, so the audit is not taking the contract's word for it.</summary>
        static string MeasuredReach()
        {
            float reach = float.PositiveInfinity;
            foreach (var box in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include,
                                                                     FindObjectsSortMode.None))
            {
                if (!box.name.StartsWith("Bound_")) continue;
                var b = box.bounds;
                reach = b.size.x < b.size.z
                    ? Mathf.Min(reach, Mathf.Abs(b.center.x) - b.size.x * 0.5f)
                    : Mathf.Min(reach, Mathf.Abs(b.center.z) - b.size.z * 0.5f);
            }
            return float.IsInfinity(reach) ? "no bound walls found" : $"+-{reach:0.0} m";
        }

        /// <summary>
        /// Every prop in the scene as its own box, combined batches split back into the individual
        /// props baked into them. A batch is one GameObject with one mesh holding dozens of props, so
        /// its own bounds say nothing useful about any of them.
        /// </summary>
        static List<Piece> VisualPieces()
        {
            var pieces = new List<Piece>();
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,
                                                                     FindObjectsSortMode.None))
            {
                var excuse = Excused(mr.gameObject);
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                if (mesh.vertexCount == 0) continue;

                // A whole-batch box is enough when the batch is one prop, and splitting a grass or
                // blade layer costs seconds for nothing.
                if (mesh.triangles.Length > 600000)
                {
                    var whole = WorldBounds(mr.transform, mesh);
                    pieces.Add(new Piece { owner = mr.name, renderer = mr, bounds = whole, excuse = excuse });
                    continue;
                }

                var clusters = DuckMeshAudit.MeshClusters(mesh, mr.localToWorldMatrix);
                for (int i = 0; i < clusters.Count; i++)
                    pieces.Add(new Piece
                    {
                        owner = clusters.Count > 1 ? $"{mr.name}#{i}" : mr.name,
                        renderer = mr,
                        bounds = clusters[i],
                        excuse = excuse
                    });
            }
            return pieces;
        }

        /// <summary>
        /// Every volume that can actually stop the mower, split the same way. Triggers are excluded
        /// because they do not block anything, and the invisible bound walls are excluded because a
        /// wall standing in front of a prop must not certify the prop as solid.
        /// </summary>
        static List<(Bounds box, string owner)> CollisionBoxes()
        {
            var boxes = new List<(Bounds, string)>();
            foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None))
            {
                // A trigger blocks nothing, the bound walls must not vouch for props standing behind
                // them, and the ground is the floor rather than an obstacle.
                if (col.isTrigger) continue;
                if (col.name.StartsWith("Bound_")) continue;
                if (NamedGround(col.name)) continue;
                if (col.gameObject.layer == LayerMask.NameToLayer("Grass")) continue;
                if (col.gameObject.layer == LayerMask.NameToLayer("Ground")) continue;

                if (col is MeshCollider mc && mc.sharedMesh != null &&
                    mc.sharedMesh.triangles.Length <= 600000)
                    foreach (var b in DuckMeshAudit.MeshClusters(mc.sharedMesh, mc.transform.localToWorldMatrix))
                        boxes.Add((b, $"{col.name}:{col.GetType().Name}"));
                else
                    boxes.Add((col.bounds, $"{col.name}:{col.GetType().Name}"));
            }
            return boxes;
        }

        /// <summary>
        /// Is there collision geometry standing where this prop is drawn? It has to be hittable in
        /// its own right, and it has to cover most of the prop's footprint — a collider that clips a
        /// corner of a bench is not a solid bench.
        /// </summary>
        static bool Covered(Bounds visual, List<(Bounds box, string owner)> solid)
            => CoveredBy(visual, solid) != null;

        /// <summary>
        /// How close solid geometry has to be to count as standing in front of something. Just over
        /// the mower's own half-width, so "the mower would have hit that first" is literally true.
        /// </summary>
        static float ShieldMargin => MowerContact.ChassisSize.x * 0.5f + 0.1f;

        /// <summary>
        /// Is there something solid close enough that the mower is stopped before it reaches this?
        /// </summary>
        static bool Shielded(Bounds visual, List<(Bounds box, string owner)> solid)
        {
            float m = ShieldMargin;
            foreach (var (c, _) in solid)
            {
                if (!MowerContact.CanBeHit(c.min.y, c.max.y)) continue;
                if (c.max.x < visual.min.x - m || c.min.x > visual.max.x + m) continue;
                if (c.max.z < visual.min.z - m || c.min.z > visual.max.z + m) continue;
                return true;
            }
            return false;
        }

        /// <summary>The collider that makes this prop solid, or null if nothing does.</summary>
        static string CoveredBy(Bounds visual, List<(Bounds box, string owner)> solid)
        {
            float needX = Mathf.Max(visual.size.x * 0.5f, 0.03f);
            float needZ = Mathf.Max(visual.size.z * 0.5f, 0.03f);
            foreach (var (c, owner) in solid)
            {
                if (!MowerContact.CanBeHit(c.min.y, c.max.y)) continue;
                float px = Mathf.Min(c.max.x, visual.max.x) - Mathf.Max(c.min.x, visual.min.x);
                float pz = Mathf.Min(c.max.z, visual.max.z) - Mathf.Max(c.min.z, visual.min.z);
                float py = Mathf.Min(c.max.y, visual.max.y) - Mathf.Max(c.min.y, visual.min.y);
                if (px >= needX && pz >= needZ && py > 0f) return owner;
            }
            return null;
        }

        /// <summary>
        /// Give a prop collision derived from the mesh it is drawn with, so the two cannot disagree
        /// about its shape, its size or where it is.
        /// </summary>
        static bool AddCollider(MeshRenderer mr)
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            var mc = mr.GetComponent<MeshCollider>();
            if (mc == null) mc = mr.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            return true;
        }
    }
}
