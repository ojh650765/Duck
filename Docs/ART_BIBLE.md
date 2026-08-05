# DUCK MOW — Art Bible

The one-line pitch: **a duck on a ride-on mower carves giant pictures into a
county-fair lawn while animals watch, then judges hold up scorecards.**

Everything below is binding for all contributors. If something is not specified,
choose the option that reads best from a 25-metre chase camera AND from a
90-metre overhead reveal — those are the only two cameras that matter.

---

## 1. Tone

**Sunday afternoon at a county fair, rendered as a wooden toy set.**

Warm, sincere, slightly absurd. Nothing is edgy or gritty. The duck takes the
competition *extremely* seriously and that earnestness is the joke — so the
duck is never winking at camera. The world is neat, tended, festive: mown
stripes, bunting, striped tents, hedges clipped into shapes, a pond, a distant
red barn.

Reference feel (not to copy, to calibrate against): the chunky readable charm of
*Untitled Goose Game*, the toy-diorama staging and saturated key light of
*Overcooked*, the confident silhouette design of *Astro Bot*, the friendly
material simplicity of *Animal Crossing: New Horizons*.

**Banned:** grit, realism, dark ambient occlusion mud, photoscanned anything,
default Unity grey, unlit flat-shaded polygon soup, randomly scattered props.

---

## 2. Scale — 1 Unity unit = 1 metre. Non-negotiable.

| Thing | Size |
|---|---|
| Duck (standing, beak to feet) | 0.62 m |
| Duck (seated on mower) | 0.48 m tall |
| Mower body (L × W × H, no duck) | 1.45 × 0.92 × 0.66 m |
| Mower cutting deck width | 1.20 m |
| Mower wheel radius (rear / front) | 0.22 / 0.15 m |
| Judge animals (seated at bench) | 0.9–1.3 m |
| Spectator animals (standing) | 0.5–1.1 m |
| Fence post height | 1.05 m |
| Playfield (mowable) | 64 × 64 m |
| Full island incl. surroundings | ~150 × 150 m |
| Barn (landmark) ridge height | 11 m |
| Windmill total height | 16 m |

The duck must read as *small* against the mower and the mower must read as
*small* against the field. That size chain is what sells the scale of the
picture being cut.

---

## 3. Palette

Authored in linear-ish sRGB hex. Use these; do not invent new hues.

**Grass (the star of the show)**
| Role | Hex | Notes |
|---|---|---|
| Uncut base | `#2F6B33` | deep, slightly blue-green |
| Uncut tip | `#4E9440` | warmer, lighter at blade tip |
| Cut base | `#6E9E37` | |
| Cut tip | `#A8CB55` | bright chartreuse — the "fresh cut" pop |
| Cut stripe light | `#B6D45F` | alternating mow-stripe band |
| Cut stripe dark | `#8FB847` | |
| Cut edge shadow | `#24512A` | 1-blade-wide dark line at the cut boundary |
| Wheel track | `#5B8331` | pressed, darker, shiny-ish |

The **cut/uncut contrast is the primary readability channel of the whole game.**
Cut grass is *lighter, warmer and yellower*. Never make cut grass darker.

**Characters & props**
| Role | Hex |
|---|---|
| Duck body cream | `#F6EBD2` |
| Duck shadow cream | `#DCC9A4` |
| Duck bill / feet orange | `#F2A03D` |
| Duck bill shadow | `#D3792A` |
| Mower cherry red | `#D6423C` |
| Mower deep red (shadow) | `#A32E2D` |
| Mower cream trim | `#F4E7CF` |
| Mower engine grey | `#4A4F55` |
| Mower brass/chrome | `#C9A55A` |
| Fence white | `#F1EDE0` |
| Tent stripe red | `#D8534E` |
| Tent stripe cream | `#F5EAD6` |
| Wood warm | `#9A6B41` |
| Wood dark | `#6E4A2C` |
| Pond water | `#3E86A8` |
| Pond shallow | `#68B0C4` |
| Chalk guide line | `#F7F3E4` |
| Hedge green | `#2A5A34` |
| Dirt path | `#B99A6B` |

**Sky & distance**
| Role | Hex |
|---|---|
| Sky zenith | `#4E9BD4` |
| Sky horizon | `#CFE7F2` |
| Sun disc / bloom | `#FFF3D0` |
| Distant hills | `#7FA8A0` |
| Haze | `#C6DCE4` |

---

## 4. Lighting

One authored look. Do not deviate per-scene.

- **Key**: directional sun, colour `#FFF1CE`, intensity ~1.6, rotation
  `(46°, -38°, 0)`. Soft shadows, strength 0.62 — shadows are *blue-tinted and
  transparent*, never black.
- **Ambient**: gradient. Sky `#8CC0E8`, equator `#A9C79A`, ground `#4C6B44`.
  This bounce-green from below is what makes characters sit in the grass.
- **Fog**: linear, colour `#C6DCE4`, start 70 m, end 260 m. Just enough to push
  the hills back.
- **Post** (URP Volume): Bloom threshold 1.05, intensity 0.55, scatter 0.6.
  Vignette 0.22. Colour adjustments: post-exposure +0.1, saturation +12,
  contrast +8. Tonemapping: **Neutral** (ACES crushes the palette).
  Slight split-tone: shadows toward `#7FA0C8`, highlights toward `#FFE9BF`.
- **No SSAO on grass** (it eats the blades). Bake contact darkening into models.

---

## 5. Form language

- **Rounded, weighty, tapered.** Every hard edge gets a bevel of 8–15 mm at
  model scale so it catches a highlight. No razor edges, no boxes.
- **Big-to-small hierarchy**: one dominant mass, one secondary, then detail. A
  prop that reads as three similar-sized lumps is a failed prop.
- **Silhouette test**: fill the model black. If you cannot name it, redesign it.
  This is a hard gate for the duck, the mower, and all three judges.
- **Asymmetry sells character**: the duck's cap sits slightly askew, one judge
  leans, bunting sags unevenly.
- **Deliberate imperfection, never noise**: fence posts lean by ±3°, hedges vary
  ±8% in scale. Nothing is on a perfect grid except the mowing stripes.
- Flat/faceted shading is allowed on hard-surface (mower panels, barn) but
  characters are smooth-shaded with hard edges only where intended.

## 6. Materials

Everything is URP/Lit, metallic workflow, **no image textures on hero assets** —
colour comes from vertex colours or per-material colour, so the palette stays
locked and WebGL texture budget stays near zero.

- Base props: metallic 0, smoothness 0.18–0.30.
- Painted metal (mower body): metallic 0.05, smoothness 0.55.
- Chrome/brass trim: metallic 0.85, smoothness 0.75.
- Water: smoothness 0.9, transparent, with a scrolling normal ripple.
- Foliage: smoothness 0.1, slight subsurface fake via ambient tint.

Bake **ambient occlusion into vertex colour alpha** on every Blender export.
That is what stops the toy set looking like it is floating.

---

## 7. The picture is the payoff

The cut picture must be readable *as a picture* from the overhead reveal
camera at 90 m. That means:

- Target shapes are **bold and chunky** — a heart, a star, a fish, a smiley, a
  crown, a duck. Minimum feature thickness 6 m.
- The reveal camera is orthographic-ish, top-down with a 6° tilt so the world
  still has depth, held for 2.5 s before the judges appear.
- Mow stripes run perpendicular to the dominant cut direction so the picture
  reads as *mown*, not painted.

---

## 8. Anti-patterns — automatic rejection

1. A prop that is an unmodified Unity primitive visible to the player camera.
2. Repeated identical props on a visible grid.
3. Grass that reads as a flat texture being erased.
4. Any asset whose silhouette is a rounded rectangle.
5. Cut grass that is darker than uncut grass.
6. UI in default Arial/Liberation Sans, or UI without a drop shadow / outline
   over the bright field.
7. A camera that clips through the mower, a fence, or the ground.
8. Anything grey.
