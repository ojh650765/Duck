using System.Collections.Generic;
using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The defence arena: two flower gardens facing each other across an open pitch, with the judges'
    /// bench at the near end. Lives in its own scene, <c>Assets/Scenes/Arena.unity</c>.
    ///
    /// THIS COMPONENT BUILDS NOTHING. It binds. Every bed, picket, board and anchor is a real
    /// GameObject saved in the scene file, authored by <c>DuckArenaBuilder</c> and referenced by the
    /// serialized fields below; at runtime this only wraps them in something the phase can ask
    /// questions of.
    ///
    /// That split is the entire point and it was arrived at the hard way. An earlier version generated
    /// the whole arena in Awake, which passes every test except the one that matters: nobody could
    /// look at it. Geometry that exists only in play mode cannot be selected, cannot be dragged, and
    /// cannot be judged without pressing play — so the only available adjustment was to edit a
    /// constant, rebuild, play, and screenshot. For an arena whose entire job is to feel good to drive
    /// around, a minutes-long loop for "move that bed two metres" is the wrong tool, and it made the
    /// bake look like a finishing step when it is really what makes every later decision cheap.
    ///
    /// This is also the project's own established pattern rather than a new idea: DuckSceneBuilder
    /// treats the scene as generated output — "edit a number, rebuild, look" — and the load-bearing
    /// half of that is that the result is SAVED. Deterministic generation and a hand-inspectable
    /// result are only in tension if the generator forgets to write a file.
    ///
    /// Greybox still: coloured boxes, and the authored `Flowerbed` in Foliage.fbx is the drop-in for
    /// the art pass. But they are coloured boxes somebody can select.
    /// </summary>
    public class DefenceArena : MonoBehaviour
    {
        /// <summary>
        /// One flower bed. Alive until a goose lands on it, and it does not grow back.
        ///
        /// Wraps a baked transform rather than owning one, so the layout belongs to the scene and this
        /// only tracks what has happened to it during a round.
        /// </summary>
        public class Bed
        {
            public Transform body;
            public Vector3 position;
            public Vector3 standingScale;
            public bool alive;
        }

        [Header("Baked layout — filled by DuckArenaBuilder, editable in the scene")]
        [Tooltip("The player's flowerbeds. Drag one in the scene and it stays dragged; the phase reads " +
                 "whatever is here.")]
        public Transform[] playerBeds = new Transform[0];
        public Transform[] opponentBeds = new Transform[0];

        [Header("Anchors")]
        [Tooltip("Where the mower starts, facing down the pitch.")]
        public Transform spawnAnchor;
        [Tooltip("Where the duck stands for its closing portrait, in front of the garden it defended.")]
        public Transform portraitAnchor;
        [Tooltip("The tall board on the opponent's end. Also what the camera holds between exchanges " +
                 "so the far goal stays in shot — a flat garden 50 m away spans about a degree and a " +
                 "half and might as well not be there; a board has height the frame can find.")]
        public Transform opponentMarker;
        [Tooltip("Centre of the player's garden. Used to aim dives and to place the award beat.")]
        public Transform playerGarden;
        [Tooltip("Centre of the opponent's garden.")]
        public Transform opponentGarden;
        /// <summary>
        /// Where the judges' bench stands: near end, off to one side, angled in down the pitch.
        ///
        /// An anchor rather than a position, so it can be dragged in the scene like everything else —
        /// which matters here more than most, because whether the bench lands in frame depends on a low
        /// chase camera and is the kind of thing only a human looking at a frame can settle.
        ///
        /// Not behind the player and not mid-pitch. Behind is never in shot, since the camera looks down
        /// the pitch at the far goal for most of a rally; mid-pitch is an obstacle, and would break the
        /// venue's standing rule that nothing is drawn between the player and what they are working on.
        /// </summary>
        [Tooltip("Where the judges' bench stands. See the summary — drag it and look.")]
        public Transform benchAnchor;

        [Header("Damage look")]
        [Tooltip("What a flattened bed is repainted with. A disk asset, not a runtime material — a " +
                 "scene full of HideFlags.DontSave materials looks wrong the moment it is opened " +
                 "without pressing play, which defeats the point of baking it.")]
        public Material flattenedMaterial;

        [Tooltip("Every picket and rail of the player's fence, so a goose coming down can knock some " +
                 "of them flat. Left empty is fine — the fence simply stays intact.")]
        public Transform[] fencePieces = new Transform[0];

        // ------------------------------------------------------------------ derived

        public Vector3 PlayerGardenCentre => playerGarden != null ? playerGarden.position : transform.position;
        public Vector3 OpponentGardenCentre => opponentGarden != null ? opponentGarden.position
                                                                     : transform.position + Vector3.forward * 50f;
        /// <summary>Unit vector from the player's goal toward the opponent's. The pitch's long axis.</summary>
        public Vector3 Toward
        {
            get
            {
                Vector3 d = OpponentGardenCentre - PlayerGardenCentre;
                d.y = 0f;
                return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
            }
        }
        /// <summary>Centre-to-centre distance between the goals. Measured, not configured.</summary>
        public float GardenGap => Vector3.Distance(PlayerGardenCentre, OpponentGardenCentre);

        public Vector3 SpawnPoint => spawnAnchor != null ? spawnAnchor.position
                                                         : PlayerGardenCentre - Toward * 4f + Vector3.up * 0.4f;
        public Quaternion SpawnRotation => spawnAnchor != null ? spawnAnchor.rotation
                                                              : Quaternion.LookRotation(Toward, Vector3.up);
        public Transform OpponentMarker => opponentMarker;

        /// <summary>
        /// Where the bench belongs. Falls back to a computed spot at the near end, off to the side, so
        /// the phase still stages correctly in a scene whose anchor has not been authored yet.
        /// </summary>
        public Vector3 BenchPosition
        {
            get
            {
                if (benchAnchor != null) return benchAnchor.position;
                Vector3 side = Vector3.Cross(Vector3.up, Toward).normalized;
                return PlayerGardenCentre + side * (gardenHalfWidth + 12f) + Toward * (gardenHalfWidth + 4f);
            }
        }

        public Quaternion BenchRotation
        {
            get
            {
                if (benchAnchor != null) return benchAnchor.rotation;
                Vector3 faceIn = (PlayerGardenCentre + Toward * (GardenGap * 0.35f)) - BenchPosition;
                faceIn.y = 0f;
                return faceIn.sqrMagnitude > 1e-4f
                    ? Quaternion.LookRotation(faceIn.normalized, Vector3.up)
                    : Quaternion.LookRotation(Toward, Vector3.up);
            }
        }

        public List<Bed> PlayerBeds { get; } = new List<Bed>(32);
        public List<Bed> OpponentBeds { get; } = new List<Bed>(32);

        public int PlayerBedsLost { get; private set; }
        public int OpponentBedsLost { get; private set; }
        public bool Bound { get; private set; }

        // ------------------------------------------------------------------ binding

        /// <summary>
        /// Take the baked layout and reset it to standing. Called at the start of every round.
        ///
        /// Takes no arguments, and that is the visible consequence of dropping the cut-mask link: the
        /// garden used to be planted from the picture the player had just mown, which meant this needed
        /// the target shape and the mask, and meant the layout could not be baked at all. Authored beds
        /// cost that connection — the garden is no longer literally what you made — and buy two things
        /// worth more: the arena becomes a saved scene somebody can art-direct, and the beds can be
        /// placed FOR THE ENCOUNTER rather than derived from it.
        ///
        /// The derived version had a flaw that only shows up when written down: a mask-planted layout is
        /// only as good as the player's mowing, so a bad round produced beds bunched in a corner and the
        /// defence phase got worse exactly when the player had already done badly. That is a difficulty
        /// curve running backwards.
        /// </summary>
        public void Bind()
        {
            PlayerBeds.Clear();
            OpponentBeds.Clear();
            PlayerBedsLost = 0;
            OpponentBedsLost = 0;

            Collect(playerBeds, PlayerBeds);
            Collect(opponentBeds, OpponentBeds);
            ResetFence();
            Bound = PlayerBeds.Count > 0;

            if (!Bound)
                Debug.LogWarning("[Duck] the arena has no beds bound — run " +
                                 "'Duck/2 · Build defence arena scene' and save the scene.");
        }

        void Collect(Transform[] source, List<Bed> into)
        {
            if (source == null) return;
            foreach (var t in source)
            {
                if (t == null) continue;

                // The standing pose is read off the scene rather than assumed, so a bed somebody has
                // scaled or rotated by hand is restored to what THEY left, not to what the builder
                // originally wrote. Anything else would quietly undo hand tuning on the first retry.
                var bed = new Bed
                {
                    body = t,
                    position = t.position,
                    standingScale = t.localScale,
                    alive = true
                };
                Stand(bed);
                into.Add(bed);
            }
        }

        void Stand(Bed b)
        {
            b.alive = true;
            if (b.body == null) return;
            b.body.localScale = b.standingScale;
            b.body.gameObject.SetActive(true);
            var r = b.body.GetComponent<MeshRenderer>();
            if (r != null && _standingMaterial != null) r.sharedMaterial = _standingMaterial;
        }

        Material _standingMaterial;

        void Awake()
        {
            // Remembered from the authored state so a flattened bed can be repainted back. Read once,
            // before anything has been flattened.
            foreach (var t in playerBeds)
            {
                if (t == null) continue;
                var r = t.GetComponent<MeshRenderer>();
                if (r == null) continue;
                _standingMaterial = r.sharedMaterial;
                break;
            }
        }

        // ------------------------------------------------------------------ damage

        /// <summary>
        /// Flatten every living bed within <paramref name="radius"/>, up to <paramref name="limit"/>, and
        /// never past the cap the caller is holding. Returns how many died.
        ///
        /// Flattened rather than hidden: bare soil where a bed used to be reads as never having been
        /// planted, and the player has to be able to see that something was DESTROYED rather than that
        /// something is missing.
        /// </summary>
        public int Damage(Vector3 world, float radius, bool playerSide, int limit, int remainingCap)
        {
            var beds = playerSide ? PlayerBeds : OpponentBeds;
            int allowed = Mathf.Min(limit, Mathf.Max(0, remainingCap));
            if (allowed <= 0) return 0;

            float r2 = radius * radius;
            int killed = 0;

            for (int i = 0; i < beds.Count && killed < allowed; i++)
            {
                var b = beds[i];
                if (!b.alive) continue;
                if ((b.position - world).sqrMagnitude > r2) continue;

                b.alive = false;
                killed++;
                if (b.body == null) continue;

                b.body.localScale = new Vector3(b.standingScale.x * 1.2f,
                                                b.standingScale.y * 0.16f,
                                                b.standingScale.z * 1.3f);
                var r = b.body.GetComponent<MeshRenderer>();
                if (r != null && flattenedMaterial != null) r.sharedMaterial = flattenedMaterial;
            }

            if (playerSide) PlayerBedsLost += killed; else OpponentBedsLost += killed;
            return killed;
        }

        /// <summary>
        /// Knock the fence flat where a goose came through it.
        ///
        /// "The goose should smash through the fence" was the one line of the miss brief with nothing
        /// behind it: the fence was decoration a goose passed straight through, so a bird that flattened
        /// three flowerbeds left the barrier around them untouched — which reads as the fence not being
        /// there rather than as the goose being powerful.
        ///
        /// Toppled by ROTATION rather than by physics. These are static, collider-free boards; giving
        /// them rigidbodies would add a dozen simulated objects to the exact frames a hit stop has
        /// already made expensive, for a result nobody can tell apart from a board lying at 78 degrees.
        ///
        /// Measured against the SEGMENT the goose flew, not against its landing point. A bird that came
        /// down in the middle of the garden entered somewhere on the perimeter, and it is that crossing
        /// the player watched — toppling boards around the crater would put the damage where the goose
        /// ended up rather than where it broke in.
        /// </summary>
        public int SmashFence(Vector3 from, Vector3 to, float radius, int limit)
        {
            if (fencePieces == null || fencePieces.Length == 0 || limit <= 0) return 0;

            Vector3 seg = to - from;
            float segLenSq = Mathf.Max(seg.sqrMagnitude, 1e-4f);
            float r2 = radius * radius;
            int hit = 0;

            for (int i = 0; i < fencePieces.Length && hit < limit; i++)
            {
                var t = fencePieces[i];
                if (t == null || _toppled.Contains(t)) continue;

                // Distance from the board to the flight path, flattened: height is irrelevant to whether
                // the goose passed through this bit of fence.
                Vector3 p = t.position; p.y = 0f;
                Vector3 a = from; a.y = 0f;
                Vector3 b = to; b.y = 0f;
                float k = Mathf.Clamp01(Vector3.Dot(p - a, b - a) / segLenSq);
                if ((p - (a + (b - a) * k)).sqrMagnitude > r2) continue;

                // Fall AWAY from the flight direction, so the damage points the way the bird went.
                Vector3 push = seg; push.y = 0f;
                if (push.sqrMagnitude < 1e-4f) push = Vector3.forward;
                push.Normalize();
                Vector3 axis = Vector3.Cross(Vector3.up, push);

                t.rotation = Quaternion.AngleAxis(78f, axis) * t.rotation;
                // Dropped to the ground, because a board rotated about its own centre leaves one end
                // buried and the other in the air.
                t.position = new Vector3(t.position.x, 0.06f, t.position.z) + push * 0.18f;
                _toppled.Add(t);
                hit++;
            }
            return hit;
        }

        readonly HashSet<Transform> _toppled = new();
        readonly List<(Transform t, Vector3 pos, Quaternion rot)> _fenceStanding = new();

        /// <summary>
        /// Remember where the fence stands, and stand it back up.
        ///
        /// Captured on the first bind only. Re-reading the poses on every bind would record the TOPPLED
        /// boards as their standing pose the second time round, so the fence would be permanently flat
        /// after one raid and every retry would start in the wreckage of the last one.
        /// </summary>
        void ResetFence()
        {
            if (_fenceStanding.Count == 0 && fencePieces != null)
                foreach (var t in fencePieces)
                    if (t != null) _fenceStanding.Add((t, t.position, t.rotation));

            foreach (var (t, pos, rot) in _fenceStanding)
                if (t != null) t.SetPositionAndRotation(pos, rot);

            _toppled.Clear();
        }

        /// <summary>The living bed closest to a point, or null. What a goose picks as its target.</summary>
        public Bed NearestAlive(Vector3 world, bool playerSide)
        {
            var beds = playerSide ? PlayerBeds : OpponentBeds;
            Bed best = null;
            float bestSq = float.MaxValue;
            foreach (var b in beds)
            {
                if (!b.alive) continue;
                float d = (b.position - world).sqrMagnitude;
                if (d >= bestSq) continue;
                best = b; bestSq = d;
            }
            return best;
        }

        /// <summary>A living bed at random, for something aiming at a garden as a whole.</summary>
        public Bed AnyAlive(System.Random rng, bool playerSide)
        {
            var beds = playerSide ? PlayerBeds : OpponentBeds;
            int alive = AliveCount(playerSide);
            if (alive <= 0) return null;

            int want = rng.Next(alive);
            foreach (var b in beds)
            {
                if (!b.alive) continue;
                if (want-- == 0) return b;
            }
            return null;
        }

        public int AliveCount(bool playerSide)
        {
            var beds = playerSide ? PlayerBeds : OpponentBeds;
            int n = 0;
            foreach (var b in beds) if (b.alive) n++;
            return n;
        }

        /// <summary>
        /// Is this point standing in one of the gardens?
        ///
        /// Used to decide what a thrown goose actually hit, rather than trusting what it was aimed at. A
        /// throw that falls short lands on open pitch and harms nobody; one sent into the player's own
        /// flowers harms the player.
        /// </summary>
        public bool IsInsideGarden(Vector3 world, bool playerSide)
        {
            Vector3 c = playerSide ? PlayerGardenCentre : OpponentGardenCentre;
            float half = gardenHalfWidth;
            return Mathf.Abs(world.x - c.x) <= half && Mathf.Abs(world.z - c.z) <= half;
        }

        [Tooltip("Half the frontage of a garden, for the inside-garden test. Kept as a number rather " +
                 "than measured off the beds because a garden with most of its beds flattened must not " +
                 "quietly shrink its own goalmouth.")]
        public float gardenHalfWidth = 5f;
    }
}
