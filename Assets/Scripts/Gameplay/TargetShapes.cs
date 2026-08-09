using UnityEngine;

namespace DuckMow
{
    public enum ShapeId { Heart, Star, Smiley, Fish, Crown, Duckling, Mushroom, Anchor }

    /// <summary>
    /// The pictures the duck is asked to mow, as signed distance fields in normalised
    /// [-1,1] shape space (negative = inside the picture).
    ///
    /// SDFs rather than bitmaps because the guide chalk line, the reveal outline and the
    /// scoring grid all need the same shape at three different resolutions, and a distance
    /// field stays crisp at all of them. Several of these have deliberate holes — the eyes of
    /// the smiley, the crown's notches — which force the player to *stop* mowing, and that is
    /// where most of the tension in a run comes from.
    /// </summary>
    public static class TargetShapes
    {
        public static readonly ShapeId[] All =
        {
            ShapeId.Heart, ShapeId.Star, ShapeId.Smiley, ShapeId.Fish,
            ShapeId.Crown, ShapeId.Duckling, ShapeId.Mushroom, ShapeId.Anchor
        };

        public static string DisplayName(ShapeId id) => id switch
        {
            ShapeId.Heart => "A HEART",
            ShapeId.Star => "A STAR",
            ShapeId.Smiley => "A SMILING FACE",
            ShapeId.Fish => "A FISH",
            ShapeId.Crown => "A CROWN",
            ShapeId.Duckling => "A DUCKLING",
            ShapeId.Mushroom => "A MUSHROOM",
            ShapeId.Anchor => "AN ANCHOR",
            _ => "SOMETHING"
        };

        /// <summary>Flavour line the announcer uses during the briefing.</summary>
        public static string Flavour(ShapeId id) => id switch
        {
            ShapeId.Heart => "Classic. Sentimental. Very hard to get the cleft right.",
            ShapeId.Star => "Five points. Not four. Not six. Five.",
            ShapeId.Smiley => "Mind the eyes. Mowing the eyes is a disqualifiable offence.",
            ShapeId.Fish => "Last year's winner attempted this and wept.",
            ShapeId.Crown => "Regal. Pointy. Unforgiving.",
            ShapeId.Duckling => "A tribute. No pressure.",
            ShapeId.Mushroom => "Deceptively difficult. The stem is where dreams die.",
            ShapeId.Anchor => "Nautical. Nobody knows why.",
            _ => ""
        };

        /// <summary>Rough difficulty 0..1, used to pick a sensible round length.</summary>
        public static float Difficulty(ShapeId id) => id switch
        {
            ShapeId.Heart => 0.25f,
            ShapeId.Star => 0.55f,
            ShapeId.Smiley => 0.60f,
            ShapeId.Fish => 0.65f,
            ShapeId.Crown => 0.70f,
            ShapeId.Duckling => 0.80f,
            ShapeId.Mushroom => 0.50f,
            ShapeId.Anchor => 0.85f,
            _ => 0.5f
        };

        /// <summary>Signed distance in shape space. Negative inside.</summary>
        public static float Sdf(ShapeId id, Vector2 p) => id switch
        {
            ShapeId.Heart => Heart(p),
            ShapeId.Star => Star(p),
            ShapeId.Smiley => Smiley(p),
            ShapeId.Fish => Fish(p),
            ShapeId.Crown => Crown(p),
            ShapeId.Duckling => Duckling(p),
            ShapeId.Mushroom => Mushroom(p),
            ShapeId.Anchor => Anchor(p),
            _ => Circle(p, 0.6f)
        };

        // ------------------------------------------------------------------ primitives

        static float Circle(Vector2 p, float r) => p.magnitude - r;

        static float Ellipse(Vector2 p, Vector2 r)
        {
            // Cheap approximation; accurate enough for a lawn and far cheaper than the exact form.
            float k1 = new Vector2(p.x / r.x, p.y / r.y).magnitude;
            float k2 = new Vector2(p.x / (r.x * r.x), p.y / (r.y * r.y)).magnitude;
            return k2 > 1e-6f ? k1 * (k1 - 1f) / k2 : -Mathf.Min(r.x, r.y);
        }

        static float RoundedBox(Vector2 p, Vector2 b, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - b + Vector2.one * r;
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                   + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(Vector2.Dot(ba, ba), 1e-6f));
            return (pa - ba * h).magnitude - r;
        }

        static float Triangle(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            Vector2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
            Vector2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            Vector2 d = Vector2.Min(Vector2.Min(
                new Vector2(Vector2.Dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                new Vector2(Vector2.Dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                new Vector2(Vector2.Dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }

        static float Union(float a, float b) => Mathf.Min(a, b);
        static float Subtract(float a, float b) => Mathf.Max(a, -b);

        /// <summary>Union with a rounded seam, so composed shapes read as one object.</summary>
        static float SmoothUnion(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        // ------------------------------------------------------------------ pictures

        static float Heart(Vector2 p)
        {
            // Author it upright and a little squat so it fills a square field.
            p = new Vector2(p.x * 1.18f, -p.y * 1.12f + 0.32f);
            p.x = Mathf.Abs(p.x);

            if (p.y + p.x > 1f)
                return ((p - new Vector2(0.25f, 0.75f)).magnitude - Mathf.Sqrt(2f) / 4f) * 0.82f;

            float d1 = (p - new Vector2(0f, 1f)).sqrMagnitude;
            float m = Mathf.Max(p.x + p.y, 0f) * 0.5f;
            float d2 = (p - new Vector2(m, m)).sqrMagnitude;
            return Mathf.Sqrt(Mathf.Min(d1, d2)) * Mathf.Sign(p.x - p.y) * 0.82f;
        }

        static float Star(Vector2 p)
        {
            const float r = 0.92f, rf = 0.42f;
            Vector2 k1 = new Vector2(0.809016994375f, -0.587785252292f);
            Vector2 k2 = new Vector2(-k1.x, k1.y);

            p = new Vector2(Mathf.Abs(p.x), p.y);
            p -= 2f * Mathf.Max(Vector2.Dot(k1, p), 0f) * k1;
            p -= 2f * Mathf.Max(Vector2.Dot(k2, p), 0f) * k2;
            p = new Vector2(Mathf.Abs(p.x), p.y - r);

            Vector2 ba = rf * new Vector2(-k1.y, k1.x) - new Vector2(0f, 1f);
            float h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0f, r);
            return (p - ba * h).magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
        }

        static float Smiley(Vector2 p)
        {
            float face = Circle(p, 0.88f);
            float eyeL = Circle(p - new Vector2(-0.32f, 0.30f), 0.155f);
            float eyeR = Circle(p - new Vector2(0.32f, 0.30f), 0.155f);

            // Mouth: an arc carved as the difference of two offset circles, clipped to the lower
            // half so only the smile survives and not the whole ring.
            float outer = Circle(p - new Vector2(0f, 0.06f), 0.60f);
            float inner = Circle(p - new Vector2(0f, 0.06f), 0.44f);
            float band = Mathf.Max(outer, -inner);

            // The sign here was inverted, and it is the reason the eyes and the mouth ran together.
            //
            // Inside is negative, so the half-plane that KEEPS the lower half has to be negative
            // below the cut: p.y + 0.10. Written as -(p.y + 0.10) it kept the upper half instead,
            // which left the top of the ring in place — an arc reaching y = 0.66, straight through
            // eyes that sit between y = 0.145 and y = 0.455. They were never touching by accident;
            // they were one connected hole, so the face had a slot across it rather than an
            // expression, and the mown result could not read as a smile at any size.
            float lower = p.y + 0.10f;
            float mouth = Mathf.Max(band, lower);

            float d = face;
            d = Subtract(d, eyeL);
            d = Subtract(d, eyeR);
            d = Subtract(d, mouth);
            return d;
        }

        static float Fish(Vector2 p)
        {
            p = new Vector2(p.x, p.y * 1.25f);
            float body = Ellipse(p - new Vector2(0.10f, 0f), new Vector2(0.70f, 0.42f));
            float tail = Triangle(p,
                new Vector2(-0.48f, 0f),
                new Vector2(-0.95f, 0.44f),
                new Vector2(-0.95f, -0.44f));
            float fin = Triangle(p,
                new Vector2(0.05f, 0.30f),
                new Vector2(0.02f, 0.72f),
                new Vector2(-0.32f, 0.28f));

            float d = SmoothUnion(body, tail, 0.10f);
            d = SmoothUnion(d, fin, 0.06f);
            float eye = Circle(p - new Vector2(0.46f, 0.13f), 0.10f);
            return Subtract(d, eye);
        }

        static float Crown(Vector2 p)
        {
            p.y -= 0.05f;
            float band = RoundedBox(p - new Vector2(0f, -0.42f), new Vector2(0.80f, 0.22f), 0.07f);

            float spikes = 1e9f;
            float[] xs = { -0.62f, 0f, 0.62f };
            float[] hs = { 0.42f, 0.66f, 0.42f };
            for (int i = 0; i < 3; i++)
            {
                float t = Triangle(p,
                    new Vector2(xs[i] - 0.30f, -0.24f),
                    new Vector2(xs[i] + 0.30f, -0.24f),
                    new Vector2(xs[i], hs[i]));
                spikes = Union(spikes, t);
            }

            float d = SmoothUnion(band, spikes, 0.05f);
            // Three jewels punched out of the band — small holes, big accuracy tax.
            for (int i = 0; i < 3; i++)
                d = Subtract(d, Circle(p - new Vector2(xs[i], -0.42f), 0.11f));
            return d;
        }

        static float Duckling(Vector2 p)
        {
            p *= 1.05f;
            float body = Ellipse(p - new Vector2(-0.10f, -0.24f), new Vector2(0.62f, 0.46f));
            float head = Circle(p - new Vector2(0.34f, 0.36f), 0.34f);
            float bill = Triangle(p,
                new Vector2(0.60f, 0.42f),
                new Vector2(0.98f, 0.32f),
                new Vector2(0.60f, 0.22f));
            float tail = Triangle(p,
                new Vector2(-0.62f, -0.12f),
                new Vector2(-0.96f, 0.16f),
                new Vector2(-0.62f, -0.38f));

            float d = SmoothUnion(body, head, 0.12f);
            d = SmoothUnion(d, bill, 0.03f);
            d = SmoothUnion(d, tail, 0.06f);
            return Subtract(d, Circle(p - new Vector2(0.42f, 0.44f), 0.085f));
        }

        static float Mushroom(Vector2 p)
        {
            // The stem RUNS UP INTO THE CAP, and the overlap is the whole point.
            //
            // RoundedBox takes b as the full half-extent, rounding included, so the stem used to be
            // centred at -0.46 with a half-height of 0.40 and therefore stopped at y = -0.06 — while
            // the cap, being the circle clipped to y >= 0.02, started 0.08 ABOVE that. The two solids
            // never touched. SmoothUnion's k of 0.10 is wide enough to blur a field across a gap that
            // small, which is why it looked joined on a distance plot and came apart on the lawn:
            // the blend leaves a neck far thinner than the mower, so the first pass under the cap
            // cuts the head off the stem.
            //
            // Bottom stays at -0.86 so the silhouette is unchanged; the top goes to 0.14, which is
            // 0.12 inside the cap. Everything above y = 0.02 is buried in the cap and invisible, so
            // what the player sees is the same mushroom with a neck that is actually made of stem.
            float stem = RoundedBox(p - new Vector2(0f, -0.36f), new Vector2(0.22f, 0.50f), 0.12f);
            float capOuter = Circle(p - new Vector2(0f, 0.04f), 0.78f);
            float capClip = -(p.y - 0.02f);
            float cap = Mathf.Max(capOuter, capClip);
            float d = SmoothUnion(cap, stem, 0.10f);

            float[] sx = { -0.42f, 0f, 0.40f };
            float[] sy = { 0.30f, 0.46f, 0.26f };
            float[] sr = { 0.13f, 0.16f, 0.11f };
            for (int i = 0; i < 3; i++)
                d = Subtract(d, Circle(p - new Vector2(sx[i], sy[i]), sr[i]));
            return d;
        }

        static float Anchor(Vector2 p)
        {
            p.y += 0.06f;
            float shank = RoundedBox(p - new Vector2(0f, -0.05f), new Vector2(0.11f, 0.72f), 0.08f);
            float stock = RoundedBox(p - new Vector2(0f, 0.42f), new Vector2(0.56f, 0.10f), 0.07f);

            // Fluke arc at the bottom: a thick ring clipped to its lower half.
            float ringOuter = Circle(p - new Vector2(0f, -0.30f), 0.66f);
            float ringInner = Circle(p - new Vector2(0f, -0.30f), 0.44f);
            float arc = Mathf.Max(Mathf.Max(ringOuter, -ringInner), -(-p.y - 0.28f));

            float tipL = Triangle(p, new Vector2(-0.68f, -0.34f), new Vector2(-0.44f, -0.30f), new Vector2(-0.62f, -0.02f));
            float tipR = Triangle(p, new Vector2(0.68f, -0.34f), new Vector2(0.44f, -0.30f), new Vector2(0.62f, -0.02f));

            float d = SmoothUnion(shank, stock, 0.05f);
            d = SmoothUnion(d, arc, 0.06f);
            d = Union(d, tipL);
            d = Union(d, tipR);

            float ringO = Circle(p - new Vector2(0f, 0.78f), 0.20f);
            float ringI = Circle(p - new Vector2(0f, 0.78f), 0.11f);
            return Union(d, Mathf.Max(ringO, -ringI));
        }
    }
}
