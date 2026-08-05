using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// What the mower can physically touch — as code, not as a comment.
    ///
    /// ---- WHY THIS FILE EXISTS ----
    ///
    /// "무시되는 장애물이 있음" has now been reported three times, and every time it was the same
    /// arithmetic wearing a different prop:
    ///
    ///   1. Gnomes floating above the ground. The mower passed underneath them.
    ///   2. Gnomes grounded but 0.6 m tall, presenting only 0.36 m to the chassis. Glancing
    ///      contacts slipped past.
    ///   3. Props "still spawning small" — deleted rather than diagnosed.
    ///   4. And now two more.
    ///
    /// The single fact behind all four is that the mower has exactly ONE collider: a box on its
    /// root, 0.52 m tall, centred 0.06 m above its own origin, held about 0.44 m off the ground by
    /// a raycast suspension. So the machine's entire hittable surface lives in a 52 cm band well
    /// clear of the grass:
    ///
    ///     THE MOWER CAN ONLY TOUCH THINGS BETWEEN y 0.24 AND y 0.76.
    ///     Nothing below 0.24 m can be hit, whatever collider it has.
    ///
    /// That fact used to live in a comment inside the environment builder, hand-computed, in a
    /// different file from the code that places props — so a prop could be drawn, be given a
    /// perfectly correct collider derived from its own mesh, and still be impossible to hit. Every
    /// one of the four reports is that situation. Deleting the prop does not remove the mechanism;
    /// the next dressing pass re-creates it.
    ///
    /// So the band is computed here, from the same constants the mower is actually built from, and
    /// the builder is required to check every prop against it (see DuckSolidity). If the chassis or
    /// the suspension is ever retuned, the band moves with it and the checks move with the band.
    /// MowerController verifies at runtime that the ride height this file predicts is the ride
    /// height the springs actually settle at, so the contract cannot drift from reality in silence.
    /// </summary>
    public static class MowerContact
    {
        // ---- the chassis, as authored ----
        //
        // These are the numbers the mower's BoxCollider is built from. DuckModelIntegration and
        // DuckSceneBuilder both used to type them out independently, which is one more pair of
        // values that had to agree about one shape.
        public static readonly Vector3 ChassisSize = new Vector3(0.92f, 0.52f, 1.45f);
        public static readonly Vector3 ChassisCentre = new Vector3(0f, 0.06f, 0f);
        public const float ChassisMass = 180f;

        // ---- the suspension, as tuned ----
        //
        // Mirrored by MowerController's field defaults rather than the other way round, because the
        // ride height has to be knowable without a live mower — the environment builder needs it at
        // edit time, before anything is instantiated.
        public const float GravityScale = 2.1f;
        public const float SuspensionRest = 0.30f;
        public const float SuspensionTravel = 0.16f;
        public const float SuspensionStiffness = 24000f;

        /// <summary>
        /// How high the chassis origin settles above flat ground, from the spring balance: four
        /// corners share the scaled weight, and each compresses its 0.46 m ray until the spring
        /// pushes back that hard.
        ///
        /// THE UNITS WERE WRONG HERE. force / stiffness is a DISTANCE — 927 N over 24 000 N/m is
        /// 0.039 m of travel — and it was being used as a FRACTION, so the ray length was scaled by
        /// (1 − 0.039) instead of having 0.039 m taken off it. Those happen to be close for a small
        /// compression, which is why the error survived: 0.442 against 0.421, both plausible, and the
        /// doc comment claimed a runtime dump had confirmed the wrong one.
        ///
        /// MowerController.VerifyRideHeight measures 0.400 m on the real machine. Correcting the units
        /// closes most of that — 0.442 to 0.421 — and the residual 21 mm is real: ChassisMass is the
        /// chassis alone and the mower carries a duck. The verifier keeps reporting the gap rather
        /// than being widened to swallow it, because the whole reason this file exists is that a
        /// contract which cannot disagree with the machine is not a contract.
        /// </summary>
        public static float RideHeight
        {
            get
            {
                float rayLength = SuspensionRest + SuspensionTravel;
                float perCorner = ChassisMass * 9.81f * GravityScale * 0.25f;
                float compression = Mathf.Min(perCorner / SuspensionStiffness, SuspensionTravel);
                return rayLength - compression;
            }
        }

        /// <summary>Bottom of the band the mower's collider sweeps through. Nothing under this is touchable.</summary>
        public static float BandMin => RideHeight + ChassisCentre.y - ChassisSize.y * 0.5f;

        /// <summary>Top of that band. Nothing above this is touchable either.</summary>
        public static float BandMax => RideHeight + ChassisCentre.y + ChassisSize.y * 0.5f;

        /// <summary>
        /// How much of the band a prop has to fill before a hit is reliable rather than lucky.
        ///
        /// A quarter of the chassis height, 13 cm. A thin overlap is what "sometimes it collides and
        /// sometimes it doesn't" is made of: a contact of two or three centimetres, found in one
        /// timestep between two moving boxes, is one the solver may or may not resolve — and the
        /// player cannot tell that apart from a missing collider.
        ///
        /// The threshold is set where the physics stops being marginal, not where it feels generous.
        /// Against static geometry a 180 kg body with continuous detection resolves ten centimetres
        /// dependably; the venue's real props sit either well above the line (hay bale 0.29,
        /// wheelbarrow 0.22, bicycle 0.44, plinth and stakes 0.52) or hopelessly below it (a
        /// sprinkler at 0.01, a bench slab at 0.03), so nothing important balances on the exact
        /// figure. Dynamic props have a separate hazard that is not about overlap at all — a
        /// rigidbody waking inside its own collision callback swallows the contact — and that is
        /// handled by ordering in Gnome.cs, not by size.
        /// </summary>
        public static float MinContact => ChassisSize.y * 0.25f;

        /// <summary>Vertical overlap, in metres, between a prop spanning [bottom, top] and the band.</summary>
        public static float BandOverlap(float bottomY, float topY)
            => Mathf.Max(0f, Mathf.Min(topY, BandMax) - Mathf.Max(bottomY, BandMin));

        /// <summary>
        /// True if a prop spanning [bottom, top] can be hit reliably. This is the whole contract:
        /// anything the builder presents as an obstacle must satisfy it.
        /// </summary>
        public static bool CanBeHit(float bottomY, float topY)
            => BandOverlap(bottomY, topY) >= MinContact;

        /// <summary>
        /// True if a prop reaches into the band at all — i.e. the mower can be driven at the part of
        /// it that sits at chassis height. Anything entirely above or below the band can never read
        /// as "the mower drove through it", because there is nothing there to drive through.
        /// </summary>
        public static bool PresentsInBand(float bottomY, float topY)
            => BandOverlap(bottomY, topY) > 0f;

        /// <summary>
        /// How far from the origin, on either horizontal axis, the mower's chassis can get.
        ///
        /// The invisible wall just inside the fence is built FROM this number (see BuildFence), so
        /// "where the mower can drive" and "where a prop has to be solid" are the same fact rather
        /// than two numbers that have to be kept in step. Move the wall and every prop check moves
        /// with it.
        /// </summary>
        public const float ReachRadius = 39.3f;

        /// <summary>
        /// One line, for logs and audits.
        /// </summary>
        public static string Describe()
            => $"chassis {ChassisSize.y:0.00} m tall at ride height {RideHeight:0.000} m -> " +
               $"contact band y {BandMin:0.000}..{BandMax:0.000}, " +
               $"minimum reliable overlap {MinContact:0.00} m";
    }
}
