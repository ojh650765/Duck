# DUCK MOW — Texture Spec

Every texture in `Assets/Art/Textures/` is generated procedurally in Python. There is
no AI image provider and no third-party image asset anywhere in this set; nothing
here carries a licence obligation.

- **Source:** `C:\Duck\Art\Python\textures\`
- **Output:** `C:\Duck\Assets\Art\Textures\<Category>\`
- **Total on disk: 4.35 MB** (budget 20 MB)
- **Estimated GPU cost with the settings below: ~15 MB** (budget 48 MB, TECH_DESIGN §0).
  `UI/` is ~6 MB of that because it is uncompressed and unatlased; packing it into one
  DXT5 Sprite Atlas takes the whole set to ~11 MB.

All colour is taken from ART_BIBLE §3. Where a hue was needed that is not in the
bible it is *derived* from bible hues and the derivation is recorded in the source
file header and in the notes below. Nothing is invented from scratch and nothing is
grey (ART_BIBLE §8.8).

---

## 0. How to re-render

```
cd C:\Duck\Art\Python\textures
python build_all.py                 # everything, ~35 s
python build_all.py ui fonts        # just those groups
```

Groups: `noise ground decals sky particles ui fonts`.

Requires Python 3.11 with numpy, PIL and scipy — no other dependencies. Every
generator is seeded, so a re-run reproduces the shipped PNGs exactly. `build_all.py`
prints the 9-slice borders and a size report at the end.

Files:

| File | What it is |
|---|---|
| `duckart.py` | shared library: palette, periodic Perlin/value/Worley noise, domain warp, height→normal, save/preview helpers |
| `uikit.py` | UI drawing kit: supersampled masks, source-over compositing, drop shadows, paper grain, bevels, stripes |
| `letters.py` | the hand-built stroke alphabet (see §8) |
| `gen_noise.py` `gen_ground.py` `gen_decals.py` `gen_sky.py` `gen_particles.py` | one per category |
| `gen_ui_panels.py` `gen_ui_more.py` `gen_ui_rosettes.py` | the UI set |
| `gen_fonts.py` | the numeral atlas |
| `preview.py` | contact sheets, 2×2 tile sheets, numeric seam checks (`python preview.py seams`) |
| `build_all.py` | the entry point |

Previews written during authoring live in `Art/Python/textures/_preview/` and are not
shipped.

**Verification already performed:** every tiling texture was assembled into a 2×2
sheet and inspected, and `preview.py seams` compares the wrap-around gradient to the
interior gradient for each (all pass). The blue-noise texture was FFT-checked — its
radially averaged power in the lowest 16 frequency bins is 0.000× the high-frequency
mean, i.e. a clean spectral hole; the spectrum image is a textbook blue-noise ring.

---

## 1. Import settings — the short version

| Class of texture | sRGB | Compression | Mips | Wrap | Filter |
|---|---|---|---|---|---|
| Ground albedo, sky, decal colour, particles | **on** | DXT1/DXT5 (see table) | on | Repeat / Clamp per table | Trilinear |
| Normal maps | n/a — set **Texture Type = Normal Map** | DXT5 | on | Repeat | Trilinear |
| Data masks (noise, wear, detail, tracks) | **off** | see table (several must stay uncompressed) | see table | Repeat | Bilinear |
| UI | **on** | none (or one Sprite Atlas, DXT5) | **off** | Clamp | Bilinear |

Rules that matter more than they look:

- **`noise_blue_256` must be uncompressed, Point filter, no mips.** Block compression
  or bilinear filtering destroys blue noise and it stops being useful for dithering.
- **`noise_perlin_rgba_512` and `noise_worley_512` must be uncompressed.** DXT5 encodes
  RGB jointly, and these have four *decorrelated* channels; compression bleeds them
  into each other.
- **Tick "Alpha Is Transparency"** on every RGBA texture in `Decals/`, `Particles/`,
  `Sky/cloud_puff` and `UI/`, otherwise you get dark halos where alpha goes to 0.
- **UI textures: mipmaps off.** Mips on UI cost memory and blur the crisp ink edges.
- Set **Non-Power-of-Two = None** on the handful of NPOT UI sprites so Unity does not
  rescale them.

---

## 2. `Noise/` — utility

| File | Size | Format |
|---|---|---|
| `noise_perlin_rgba_512.png` | 512² | RGBA |

Four decorrelated fBm octave-bands, one per channel, all seamlessly tiling. Measured
off-diagonal channel correlation 0.052.

| Ch | Content | Feature size at the recommended tiling |
|---|---|---|
| R | base freq 4, 4 octaves, domain-warped | ~2 m — large grass mottling |
| G | base freq 8, 4 octaves, domain-warped | ~1 m — mid mottling / wind gust envelope |
| B | base freq 16, 3 octaves | ~0.5 m |
| A | base freq 32, 3 octaves | ~0.25 m — per-blade jitter |

Each channel is re-centred on 0.5, so `tex * 2 - 1` is a safe signed value.
**Tiling: 1 tile = 8 m** (`uv = worldXZ / 8`). sRGB **off**, Compression **None**
(RGBA32), Wrap Repeat, Bilinear, mips on.

| File | Size | Format |
|---|---|---|
| `noise_worley_512.png` | 512² | RGBA |

Tiling cellular noise, 8×8 cells.
R = F1 (normalised distance to nearest feature), G = F2−F1 (a cell-*edge* mask, dark
on the boundaries), B = per-cell random constant, A = a 32×32-cell fine layer.
**Tiling: 1 tile = 4 m** → cells are 0.5 m, which is a good grass-clump scale.
sRGB **off**, Compression **None**, Wrap Repeat, Bilinear, mips on.

| File | Size | Format |
|---|---|---|
| `noise_blue_256.png` | 256² | L (single channel) |

True void-and-cluster blue noise (Ulichney 1993), toroidal, every one of the 65 536
ranks used exactly once. Use for stipple LOD fades on the blade chunks, alpha-test
dithering, and gradient dithering.
**Tiling: screen space, 1 tile = 256 px** (`uv = screenPos / 256`).
sRGB **off**, Compression **None** (R8), **mips off**, Wrap Repeat, **Filter Point**.

| File | Size | Format |
|---|---|---|
| `grass_blade_alpha_128.png` | 128² | RGBA |

A single tapered blade with a full root, an S-bend and a soft tip, for the optional
grass-card fallback path. **A = coverage. RGB = a value ramp along the blade
(0.70 at the root → 1.00 at the tip, plus a centre rib)** — feed it to a
`lerp(uncut_base, uncut_tip, tex.r)` so cards match the mesh blades.
sRGB **off** (RGB is a lerp factor, not colour), Compression None or DXT5,
Wrap **Clamp**, Bilinear, mips on, Alpha Is Transparency **on**.

---

## 3. `Ground/` — tiling terrain

**All Ground textures are authored at 128 px/m, i.e. 512 px = 4 m.** For a mesh of
side *S* metres set material tiling to *S* / 4. Wrap Repeat, Trilinear, mips on,
Aniso 4.

| File | Size | Format | sRGB | Compression | Notes |
|---|---|---|---|---|---|
| `dirt_path_albedo_512.png` | 512² | RGB | **on** | DXT1 | |
| `dirt_path_normal_512.png` | 512² | RGB | Normal Map type | DXT5 | OpenGL +Y-up convention |
| `gravel_albedo_512.png` | 512² | RGB | **on** | DXT1 | |
| `gravel_normal_512.png` | 512² | RGB | Normal Map type | DXT5 | OpenGL +Y-up convention |
| `apron_grass_detail_512.png` | 512² | L | **off** | None (R8) | |
| `soil_scuff_512.png` | 512² | L | **off** | None (R8) | |

**`dirt_path`** — compacted warm dirt `#B99A6B` with pebbles half-buried so only their
crowns break the surface, dry dusty shoulders, and two cart ruts running along **+V**
so the texture can be laid along a lane and tiled forwards indefinitely. Ruts sit at
u ≈ 0.28 and u ≈ 0.72, i.e. **1.76 m apart** at the authored scale — lay the lane
mesh 4 m wide and the ruts land where a cart's wheels would. Derived hues: damp =
dirt mixed 55 % to `wood_dark`, pale dust = dirt mixed 45–62 % to `fence_white`.

**`gravel`** — three overlapping stone layers (8 cm / 13 cm / 5 cm) in three tone
families so it is not one beige porridge, on a `dirt`→`wood_dark` bed, with
large-scale density variation so the coverage breathes. No grey: stones are `dirt`
tinted toward `fence_white` or shaded toward `wood_dark`/`wood_warm`.

**`apron_grass_detail`** — greyscale fine-blade detail for the shorter apron lawn, in
**R**. Mean is normalised to exactly 0.5, so use it signed: `detail = (tex.r - 0.5) *
amount`. Contains directional blade streaks in seven lean directions, two scales of
clumping, sparse individual bright blades, and a very faint groomed sweep.
**Recommended tiling for this one: 1 tile = 1.5 m**, not 4 m — that puts an
individual blade streak at ~2 cm, which is right. (The blade texel scale is finer
than the rest of the Ground set on purpose.)

**`soil_scuff`** — greyscale wear mask, **0 = untouched turf, 1 = bare scuffed soil**.
Two patch scales, feathered over ~0.4 m, with nibbled edges. Mean 0.18, so it stays
sparse where you project it. **Recommended tiling: 1 tile = 12 m** — this is a broad
mask for the gates and the ground in front of the judges' bench, not a detail map.

---

## 4. `Decals/` — projected onto the ground

All RGBA decals: sRGB **on**, Alpha Is Transparency **on**, Bilinear, mips on.

| File | Size | Format | Compression | Wrap |
|---|---|---|---|---|
| `chalk_line_soft_256.png` | 256² | RGBA | DXT5 | **U Repeat, V Clamp** |
| `chalk_corner_256.png` | 256² | RGBA | DXT5 | Clamp |
| `chalk_dash_256.png` | 256² | RGBA | DXT5 | **U Repeat, V Clamp** |
| `tyre_track_256.png` | 256² | L | None (R8) | **U Clamp, V Repeat** |
| `mud_splat_01..03_256.png` | 256² | RGBA | DXT5 | Clamp |
| `shadow_blob_128.png` | 128² | RGBA | DXT5 | Clamp |
| `old_mow_pattern_1024.png` | 1024² | L | None (R8) | Clamp |

**Chalk** (`#F7F3E4`) — the stroke runs along **U**; **V is the cross-section**. Built
from crushed-chalk granularity (clump noise × grit cells × fine grain), a wandering
centreline, a varying stroke width, and skips where the grass was high. Where the
chalk is thin it picks up a hint of `cut_tip` green from the grass under it.
**Scale: 1 tile = 1.5 m along the line; set V to span 0.45 m**, which makes the
painted stroke read ~0.15 m wide — a marker line, not a road marking.
`chalk_corner` is a quarter turn: it enters at the **left edge at v = 0.5** and leaves
at the **bottom edge at u = 0.5**, arc centred on the tile centre with radius 0.5, so
it butts straight onto `chalk_line` at the same V scale. `chalk_dash` is three dashes
per tile with hand-lifted tapered ends (the stroke narrows into each end rather than
being chopped).

**`tyre_track`** — greyscale, **0 = untouched, 1 = fully pressed**. Turf-tread chevron
bars with per-lug variation, a base flattening under the whole footprint, and fade in
and out along the length. Tiles along **V** (direction of travel); **U spans the tyre
plus shoulders — set U to 0.26 m** for the 0.18 m rear tyre. **V repeat every 0.55 m.**
Multiply this into the ground shader's wheel-track darkening (or into `_CutMask.G`).

**`mud_splat_01..03`** — three splats, `dirt` mixed 55–86 % toward `wood_dark`, wet
centre darker than the drying rim, with thrown teardrops and drag spatter. Centred,
with padding, so they can be scaled and rotated freely.

**`shadow_blob`** — cheap character shadow. Solid core to ~45 % of the radius then a
long falloff, with a deliberately irregular outline (five summed angular harmonics)
so it does not read as a disc. **RGB is a blue-tinted shadow colour** — `amb_ground`
`#4C6B44` pushed 40 % toward the split-tone shadow `#7FA0C8`, then darkened —
because ART_BIBLE §4 requires shadows to be blue-tinted and transparent, never black.
Alpha-blend it; do not multiply by black.

**`old_mow_pattern_1024`** — last year's winning picture, a crown, still ghosted into
the apron lawn. Greyscale, **0 = plain apron turf, 1 = strongest surviving trace**.
The signal is mostly the *mow stripes* (70 %) rather than the fill (30 %), the
silhouette is nibbled so the old cut edge is ragged, a year of patchy regrowth eats
most of it back, and the surviving cut boundary reads as a faint outline. Mean 0.11.
**Project it once over the apron ring at roughly 34 × 34 m, Clamp, and scale it by
0.12–0.18 in the shader** — at 0.30 it is already clearly a crown, which is more than
"ghosted". Not tiled.

---

## 5. `Sky/`

| File | Size | Format | sRGB | Compression | Mips | Wrap | Filter |
|---|---|---|---|---|---|---|---|
| `sky_gradient_1024x512.png` | 1024×512 | RGB | **on** | **None** (RGB24) | **off** | **U Repeat, V Clamp** | Bilinear |
| `cloud_puff_512.png` | 512² | RGBA | **on** | DXT5 | on | Clamp | Bilinear |

**`sky_gradient_1024x512`** is a 2:1 **latitude-longitude panorama** for
`Skybox/Panoramic` (Mapping: Latitude Longitude, Image Type: 360). Row 0 = zenith
(+Y), row 256 = horizon, rows below = the ground haze the hills and fence sit in
front of.

- Zenith `#4E9BD4` → horizon `#CFE7F2` on a `sin(elev)^0.62` curve, not a linear ramp,
  plus a cooler `haze` pass just above the horizon so the pale band is not milky.
- The sun sits at **u = 0.394, v = 0.244**, which is elevation 46°, yaw −38° — the
  same direction as the ART_BIBLE §4 key light. Broad warm `#FFF3D0` scatter, a tight
  glow, a soft core (no hard rim), and warm spill along the horizon on the sun's side.
- A faint high-cloud deck projected onto a flat plane so the bands compress toward the
  horizon like real cirrus, with large-scale gaps so it is drifting weather rather
  than a ring painted around the dome.
- Large-scale colour-temperature drift away from the sun, low-amplitude painterly
  breakup, and a ±1.2/255 blue-noise dither to kill 8-bit banding.

**Do not block-compress it** — DXT1 will band the gradient straight back in, and
uncompressed RGB24 at this size is only ~1.5 MB.

**`cloud_puff_512`** is a **2×2 sheet of four cumulus variations**, cells read
row-major (0 = TL, 1 = TR, 2 = BL, 3 = BR), Texture Sheet Animation X = 2, Y = 2.
Each is a union of hemispherical lobes with a flat base and a low-frequency bumpy
silhouette — chunky storybook, never wispy — shaded from a smoothed height field so
the lobes read as rounded masses with a warm sunlit crown, a cool blue underside and
a green bounce off the lawn.

---

## 6. `Particles/`

All 128², RGBA, sRGB **on**, Alpha Is Transparency **on**, Compression None (they are
tiny), Wrap Clamp, Bilinear, mips on.

| File | Layout | Notes |
|---|---|---|
| `clipping_sprite_128.png` | **2×2 sheet**, 64 px cells | Four short bent blades at different rotations and bends. Tip is `cut_tip` `#A8CB55`, root `cut_base`, with a darker fold crease. Fire these from the deck (TECH_DESIGN §3.4). |
| `dust_puff_128.png` | single | Warm dust with internal billows, so a scaling puff churns instead of just growing. Pale crown, warmer shaded belly. |
| `spark_128.png` | single | Hot cream core → brass falloff, uneven four-point flare, five thrown filaments. **Additive-friendly:** RGB stays bright where A is low, so it works in Additive or Alpha blend. |
| `confetti_128.png` | **2×2 sheet**, 64 px cells | Flat rectangle, curled streamer, disc, torn triangle. Cell colours are `tent_red`, `mower_cream`, `duck_orange`, `pond_shallow` — tint per-particle in the system for more variety. |
| `water_droplet_128.png` | single | Teardrop: circular bottom, concave cusp taper to a point at the top. Pond blue, dark refracting rim, specular pip upper-left, caustic low in the drop. |

Sheet cells are read row-major: `0 1 / 2 3`.

---

## 7. `UI/` — county-fair signage

House style: warm cream card stock `#F5EAD6`, sign paint `#D8534E`, hand-painted ink
outline `#6E4A2C`, timber `#9A6B41`, brass `#C9A55A`, blue-tinted soft drop shadows.
Every edge is drawn as a wobbled polygon at 4× supersample so nothing is CAD-straight,
and everything carries paper grain.

**Import settings for the whole folder:** Texture Type **Sprite (2D and UI)**, sRGB
**on**, Alpha Is Transparency **on**, **Generate Mip Maps off**, Wrap **Clamp**,
Filter **Bilinear**, Compression **None**, Non-Power-of-Two **None**, Pixels Per Unit
100. If you want the memory back, pack the folder into a single 2048² Sprite Atlas
with DXT5 — but leave `panel_card`, the rosettes and `numerals_atlas` uncompressed if
you see block artefacts on the ink edges.

### 9-slice borders

Unity's Sprite Editor order is **L, B, R, T**.

| File | Dimensions | Border (L, B, R, T) | Notes |
|---|---|---|---|
| `panel_card_256.png` | 256×256 | **57, 57, 57, 61** | T is larger to contain the drop shadow |
| `panel_card_dark_256.png` | 256×256 | **57, 57, 57, 61** | same geometry, timber colourway |
| `button_256.png` | 256×128 | **52, 44, 52, 52** | |
| `button_pressed_256.png` | 256×128 | **53, 45, 53, 53** | same footprint as `button`, face sunk 5 px |
| `progress_bar_bg_256.png` | 256×48 | **24, 19, 24, 23** | |
| `progress_bar_fill_256.png` | 256×48 | **21, 17, 21, 17** | sits inside the bg with a 3 px inset |
| `boost_gauge_256.png` | 256×72 | **45, 19, 21, 23** | L is large: it holds the chevron badge |
| `minimap_frame_256.png` | 256×256 | **35, 35, 35, 39** | window is transparent |
| `scorecard_blank_256.png` | 192×256 | **28, 30, 28, 42** | |
| `banner_ribbon_512.png` | 512×256 | **140, 0, 140, 0** | horizontal only — see caveat |
| `timer_ring_256.png` | 256×256 | not sliced | radial fill |
| `rosette_*_256.png` | 256×256 | not sliced | |
| `icon_*_128.png` | 128×128 | not sliced | |
| `vignette_soft_512.png` | 512×512 | not sliced | stretch to screen |

Use **Sliced** (stretch), not **Tiled** — the centre regions are near-uniform card
stock and stretch cleanly.

### The pieces

**`panel_card` / `panel_card_dark`** — rounded card stock with a wobbled ink outline,
a hand-ruled double border line (thick + thin), four brass tacks in the corners, paper
grain, a whisper of corner vignetting, and a soft blue-green drop shadow offset 6 px
down. The dark variant is `wood_warm`→`wood_dark` with a cream/brass rule, for
contrast panels behind the light one.

**`button` / `button_pressed`** — a painted wooden token: a deep-red back plate gives
it physical thickness, a lighter red face sits 7 px proud of it, and a cream label
plate is inset for the text. Pressed drops the face to 2 px of travel, kills the top
highlight, darkens the face and deepens the inner bevel — the two are the same
footprint so swapping them does not shift the layout.

**`banner_ribbon_512`** — the subject-announcement ribbon. A sagging cream sign panel
carried on a red ribbon with swallowtail tails, brass eyelets where the panel laces to
the ribbon, cloth folds running out along each tail, and the tails darkened where they
tuck behind the panel. *Caveat:* the panel's sag is baked in, so stretching the middle
past ~1.6× flattens it visibly. For very wide layouts, scale the whole sprite instead
of 9-slicing it.

**`timer_ring_256`** — use as the `Image` with Type **Filled**, Fill Method **Radial
360**, Fill Origin **Top**, Clockwise. Outer radius 112 px, inner 84 px, centred at
(128, 129). Twelve ticks are baked into the band — four major (full depth, at 12/3/6/9)
and eight minor (52 % depth) — so they sweep away with the fill. Warm orange
`duck_orange`→`sun` with an ink rim on both edges; it takes a colour tint cleanly if
you want it to go red in the last ten seconds.

**`minimap_frame_256`** — timber frame with a transparent window, a wood grain that
runs around the frame rather than across it, a cream inlay keyline and red painted
corner brackets. Put the minimap render texture behind it.

**`progress_bar_bg` / `progress_bar_fill`** — a dark timber trough and a chartreuse
fill. The fill carries **mow-stripe candy banding at 68°**, deliberately the same
visual language as the lawn, plus a gloss sliver along the top.

**`boost_gauge_256`** — this is the **housing**, not the fill: the window is
transparent, so put `progress_bar_fill` (or any fill image) *behind* it. Brass rim
with a painted bevel, seven segment dividers across the window, an inner shadow so the
fill sits down inside the gauge, and an orange triple-chevron badge on the left.

**`rosette_S/A/B/C/D_256`** — the rank badge. All five are built on one skeleton so
they read as the same family of prize, graded by generosity:

| | Tiers | Tails | Extras | Palette |
|---|---|---|---|---|
| **S** | three (gold / cream / gold), 22-18-15 pleats | three, long, notched | 18-point gold starburst, three sparkles | `brass`→`sun` gold |
| **A** | two (red / cream) | two | one sparkle | `tent_red` + `tent_cream` |
| **B** | two (bronze / cream) | two, shorter | — | `wood_warm`+`brass` |
| **C** | one (plain cream) | one | — | `tent_cream`, red medallion rim |
| **D** | one, small, **with a bite torn out of it**, tilted 12° and set off-centre | one short, bent 28° | — | `tent_cream` knocked 55 % toward `dirt` |

Each tier is a pleated ribbon annulus with crown/valley shading, a hard crease in each
valley, shade at the inner edge and a lit outer rim; the medallion is a grained card
face in a metal ring, and the letter is painted with the stroke alphabet (§8).

**`scorecard_blank_256`** — the card a judge holds up. Cream stock with red header and
footer bands, three pips in the header, a ruled number well in the middle, a
thumb-worn bottom-right corner and a wobbled ink edge. **Render the score digits into
the well: x 28…164, y 50…206** (192×256 space, y down). The numeral atlas (§8) is
sized to drop two digits straight in.

**`icon_speed / icon_accuracy / icon_coverage / icon_style_128`** — score-breakdown
icons: two red chevrons with trailing speed lines; a cream-and-red bullseye with a
dart landing dead centre; a plot two-thirds mown, showing mow stripes and the dark cut
edge the lawn shader draws; a gold star with two sparkles. All ink-outlined with a
small drop shadow so they hold up over the bright field (ART_BIBLE §8.6).

**`vignette_soft_512`** — screen-space overlay, alpha-blend, stretched to the screen.
A superellipse falloff (exponent 2.4) so 16:9 stretching keeps the corners heavier
than the edges, with low-frequency noise so it is not a maths gradient. Peak alpha
0.60 in the corners, exactly 0.0 across the middle. **RGB is the ART_BIBLE split-tone
shadow hue mixed toward `cut_edge_shadow`**, so it darkens without going grey. This is
*in addition to* the 0.22 post-process vignette in the URP volume — turn one down if
you use both.

---

## 8. `Fonts/` — recommendation and the fallback

### The recommendation: none of the fonts on this machine will do

I checked every family in `C:\Windows\Fonts` (312 files) and read the licence string
out of each candidate.

- **Everything usable as a display or UI face is Microsoft-supplied and not
  redistributable.** Arial Black, Impact, Comic Sans MS, Segoe Print, Segoe UI,
  Bahnschrift, Trebuchet MS, Verdana, Corbel, Gadugi, Cascadia Code/Mono — all carry
  *"Microsoft supplied font. You may use this font to create, display, and print
  content as permitted by the license…"*, which does not cover embedding the font file
  in a web build you distribute.
- **The only SIL OFL items present are unusable in practice:** `NotoSansKR-VF.ttf`
  (10 MB), `NotoSerifKR-VF.ttf` (23 MB) — variable CJK fonts, far too large for
  WebGL — and `SansSerifCollection.ttf` (3.7 MB, 20 483 glyphs, OFL). That last one
  *is* legally shippable and could serve as the body face if you subset it, but it is
  a neutral metric-compatible sans, not a friendly one, and there is nothing here that
  works as the chunky display face.

**So: there is no chunky friendly display face on this machine that ships legally.**

If you want real fonts, download these (both SIL OFL 1.1, both fine to embed and
subset in a WebGL build) and subset them to Latin + digits before generating the TMP
asset:

| Role | Font | Why |
|---|---|---|
| **Display / headings / numbers** | **Fredoka** (or **Baloo 2** if you want it rounder still) | heavy, geometric, very round terminals — the fairground-sign warmth the bible asks for, and it holds up at the sizes the score readout needs. Fredoka SemiBold/Bold. |
| **Body / labels** | **Nunito** (or **Nunito Sans** for a tighter fit) | rounded terminals so it sits beside Fredoka as family, but a normal x-height and open counters — readable at 14–18 px over a bright green field. |

Both are on Google Fonts; grab the static `.ttf`s rather than the variable ones for
TextMeshPro. Ship the licence file alongside as OFL requires.

**I have not installed or copied anything.** No system font is used anywhere in this
pipeline — every glyph baked into a texture here is drawn from hand-authored strokes
in `letters.py`.

### The fallback that ships now

| File | Size | Format |
|---|---|---|
| `numerals_atlas_512.png` | 512² | RGBA |

Cream-faced numerals with a thick ink outline and a soft drop shadow — county-fair
signwriting, legible over the lawn without any extra outline shader.

**Layout: 4 × 4 grid of 128 px cells, row-major from the top-left.**

```
row 0:   0   1   2   3
row 1:   4   5   6   7
row 2:   8   9   ×   %
row 3:   /   +   -   .
```

Cell (col, row) → UV rect `(col/4, 1 - (row+1)/4, 0.25, 0.25)`.

Import: sRGB **on**, Alpha Is Transparency **on**, mips **off**, Wrap **Clamp**,
Filter Bilinear, Compression **None** (the ink edges block-compress badly), Sprite
Mode **Multiple** with a 4×4 grid slice if you want individual sprites.

The same stroke alphabet in `letters.py` also carries `S A B C D`, used for the rosette
medallions, and can render any of `0-9 X % / + - .` at any size — so if you need a
different display string baked into a texture later, extend `GLYPHS` rather than
reaching for a system font.

---

## 9. Where the derived hues came from

Every colour below is not in ART_BIBLE §3 and was derived from it:

| Derived | From |
|---|---|
| dirt damp / dark stone | `dirt` mixed 42–86 % → `wood_dark` |
| dust / pale stone | `dirt` mixed 45–62 % → `fence_white` |
| gravel warm stone | `dirt` mixed 55–62 % → `wood_warm` |
| gold (rosettes, gauge) | `brass` mixed 58 % → `sun` |
| shadow (all UI, decals) | `amb_ground` mixed 40–45 % → `split_shadow`, then darkened |
| vignette | `split_shadow` mixed 62 % → `cut_edge_shadow`, then darkened |
| faded rosette D | `tent_cream` mixed 55 % → `dirt` |
| cloud sunlit / shaded | white ↔ `sun` 42 %; `haze` ↔ `sky_zenith` 42 % ↔ `split_shadow` 30 % |
| chalk on worn grass | `chalk` mixed 16 % → `cut_tip` |

`duckart.py` has `mix()`, `tint()` and `shade()` for this; `shade()` deliberately
darkens toward `cut_edge_shadow`, never toward black, so nothing in the set can drift
grey.
