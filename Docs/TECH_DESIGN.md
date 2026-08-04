# DUCK MOW — Technical Design

Unity 6000.3.6f1 · URP 17.3 · target **WebGL2**, stable 60 fps on a mid desktop.

## 0. WebGL2 constraints that drive every decision

- **No compute shaders.** No `DrawMeshInstancedIndirect` with GPU-built args.
  No `AsyncGPUReadback`. No geometry/tessellation shaders.
- Vertex texture fetch **is** available (WebGL2/GLES3) — this is the hinge the
  whole grass system swings on.
- `Graphics.DrawMeshInstanced` (CPU-supplied matrices, 1023/batch) works.
- Render textures, MRT, and blend ops work. Float RTs work; prefer `RGBA32`.
- Keep total texture memory < 48 MB, draw calls < 350, tris < 900k.
- Fixed timestep 1/60. No physics-heavy solver work.

---

## 1. Game states

`Boot → Briefing → Countdown → Mowing → Klaxon → Reveal → Judging → Verdict → (Retry|Next)`

- **Briefing** (2.0 s): judges' bench, subject announced ("TODAY'S SUBJECT: A HEART").
- **Countdown** (3.0 s): chase camera live, engine idling, 3-2-1-GO.
- **Mowing** (75 s): the game.
- **Klaxon** (1.2 s): time up, engine cuts, mower coasts, camera starts rising.
- **Reveal** (4.5 s): camera to overhead, dust settles, cut mask sweep-lights the
  picture, target outline ghosts in for comparison.
- **Judging** (~6 s): three animal judges score in sequence, each with a card
  raise + quip. Crowd reacts per score.
- **Verdict**: total, rank letter, best/worst callout, `[R] RETRY` / `[N] NEW SUBJECT`.
- **Retry is instant** — no scene reload. Everything resets in one frame.

## 2. Coordinate contract

- Playfield is centred on world origin, **64 × 64 m**, `x,z ∈ [-32, +32]`, y = 0.
- `MaskUV = (worldXZ + 32) / 64`.
- Cut mask RT: **1024²** `RGBA32`, i.e. 16 px/m, 6.25 cm/px.
- CPU score grid: **256²** `byte[]`, 25 cm/cell. Never read back from the GPU.

## 3. Grass — hybrid ground + blades

Two layers sampling one shared mask. Nothing is "erased"; the mask *drives
geometry*.

### 3.1 The cut mask RT (`_CutMask`, RGBA32, 1024²)
| Channel | Meaning |
|---|---|
| R | cut amount 0→1 (0 = untouched, 1 = fully mown) |
| G | wheel-track pressure |
| B | mow direction, `angle/π` wrapped to 0..1 (drives stripe phase) |
| A | clipping freshness (drives the yellow-green "just cut" tint) |

Painted by `CutMaskPainter`: each `FixedUpdate` the mower's blade segment
(previous blade centre → current blade centre, width 1.20 m) is drawn as a
**capsule quad** into the RT via `CommandBuffer.DrawMesh` with an ortho matrix.
Interpolated segments guarantee no gaps at any speed. Standard alpha blend, so
the *most recent* pass wins the direction channel. Cost: one tiny draw/frame.

### 3.2 Ground layer (carries the overhead read)
`Shader Duck/GrassGround` on a 64-chunk subdivided plane.
- Samples `_CutMask`, lerps uncut→cut palette.
- Mow stripes: `sin(dot(worldXZ, dirFromB) * stripeFreq)` banded ±4 % luma,
  only where R > 0.5. This is what makes it read as *mown*.
- Cut boundary: `fwidth`/gradient of R produces a 1-blade dark rim (`#24512A`)
  so the picture has a drawn edge instead of a soft smear.
- Wheel tracks from G darken and flatten.
- Large-scale mottling noise so uncut grass is never a flat colour field.

### 3.3 Blade layer (carries the chase-camera read)
- Field split into **8 × 8 chunks** (8 m each). Each chunk holds a baked mesh of
  jittered blades; blade = 3 tris (tapered, 4 verts + tip).
- Per-vertex: root XZ, `uv.y` = height along blade, `uv2` = per-blade random.
- Vertex shader samples `_CutMask` at the root:
  `height = lerp(0.42 m, 0.06 m, cut)`, plus lateral squash and a droop so cut
  stubble looks *crushed*, not scaled.
- Wind: two summed sines in world space × height² × per-blade phase. Gusts
  travel as a slow low-frequency wave so the whole field breathes.
- **LOD by chunk distance to camera**: L0 <18 m (full density), L1 18–34 m (40 %),
  L2 >34 m (none — ground shader carries it). Swapped by enabling prebuilt
  mesh variants; no per-frame allocation.
- Budget: ≈ 95 k blades visible worst case ≈ 285 k tris.

### 3.4 Feedback when cutting
- Clipping burst particles (pooled, world-space, ~30 alive) fired from the deck
  proportional to *uncut* grass under the blade — so mowing already-cut ground
  produces nothing. That single rule is most of the "physicality".
- Deck rattle audio + controller-ish camera micro-shake scale with the same value.
- A thin arc of bent grass ahead of the blade (shader: bend toward mower).

## 4. Mower feel

Rigidbody (mass 180, drag 0, angularDrag 2, interpolate, continuous-dynamic),
**custom arcade model** in `FixedUpdate` — no WheelColliders.

- 4 raycast suspension springs (rest 0.30 m, k 22000, c 2200) → ride, lean,
  and bumps for free.
- Throttle → force along forward, curve-limited to `vMax` 11.5 m/s.
  Reverse `vMax` 4 m/s.
- Steering: target yaw rate = `steerInput * maxYawRate * speedFalloff(speed)`.
  `maxYawRate` 145 °/s at low speed falling to 55 °/s at top speed. **This is the
  speed/accuracy tension** — fast is imprecise.
- Lateral grip: kill sideways velocity by `grip` (0.92 normally). Handbrake
  drops it to 0.34 → drift, with a counter-steer assist so drifts are catchable.
- Boost (limited fuel, refills by mowing *uncut* grass — rewards good play):
  +45 % vMax, yaw rate ×0.7, FOV +9°, speed lines, exhaust puffs.
  So boost is genuinely a trade: you go fast but you cannot corner.
- Brake: strong, plus a nose-dive from the suspension.
- Collisions: impulse + spin + a "bonk" squash on the mower mesh (procedural
  scale punch), camera shake, comedy horn.
- Blade only cuts while `speed > 0.4` and engine on; blade audio pitch follows
  load.

**Camera**: custom spring-arm chase (no Cinemachine dependency).
Position lag 0.12 s, look-ahead by velocity, FOV 58→67 with speed, roll ±3° into
turns, height rises with speed. Ground/prop clearance by spherecast. Shake via
a single impulse accumulator.

## 5. Scoring

CPU 256² grids: `target[]` (rasterised from the shape definition) and `cut[]`.

```
coverage = |cut ∩ target| / |target|
spill    = |cut \ target| / |cut|
accuracy = coverage * (1 - 0.65 * spill)
```
- `edgeBonus`: fraction of target-boundary cells that are cut but whose outside
  neighbour is not → rewards following the outline cleanly.
- `styleScore`: drift metres, boost metres, gnome bonks, clean-lap combo.
- Three judges each weight these differently (see §6), each returns 0–10.

## 6. Judges

Three animals on a bench under a striped awning, each an authored Blender
character with a distinct silhouette and bias:

| Judge | Bias | Personality |
|---|---|---|
| **Mildred** the goat | accuracy 0.75 / coverage 0.25 | severe, chews the scorecard |
| **Boris** the badger | coverage 0.7 / style 0.3 | loud, generous, applauds |
| **Priscilla** the heron | edge cleanliness 0.6 / style 0.4 | aloof, slow blink, hard to please |

Each: idle → lean-in → raise card → quip → settle. Card numbers flip. Crowd
sound scales to the score. Total /30 → rank `S ≥27, A ≥23, B ≥18, C ≥12, D`.

## 7. Map (compact, composed — see ART_BIBLE §1)

Concentric rings around the 64 m playfield:

1. **Playfield** — the mowable lawn, chalk guide outline, 4 corner marker stakes.
2. **Apron ring** (to 46 m) — clipped grass, judge bench + awning on the north
   edge, scoreboard, hay bales, a sprinkler that pops, garden gnomes (bonkable).
3. **Spectator ring** (to 60 m) — white picket fence, bunting, tiered stands with
   ~40 animal spectators (instanced, 5 species × colour variants, idle bob and
   reactive cheer), striped refreshment tents, parked wheelbarrows, a bicycle rack.
4. **Landscape** (to 150 m) — pond with real ducks, hedgerows, an orchard, a
   dirt lane, the red barn, the windmill (turning), distant hills, birds.

Environmental storytelling: a trophy on a plinth, last year's winning picture
faded into the apron lawn, a "MOW-OFF '26" banner, a judge's thermos, a gnome
knocked into the pond.

## 8. Audio

All procedurally synthesised (no provider keys) — see `Docs/AUDIO_SPEC.md`.
Engine = layered saw/noise loop with pitch driven by RPM (RPM from speed +
throttle, with a little lag). Blade = filtered noise loop, gated on cutting.
Crowd = pink-noise beds + burst cheers. Music = jaunty light-country loop.

## 9. Performance plan

- SRP Batcher on; all props share ≤ 8 materials.
- Spectators & foliage via `DrawMeshInstanced` with per-instance colour in a
  `MaterialPropertyBlock`.
- Static batching for the fence/stands; shadow casting off for anything < 0.4 m
  or beyond 60 m.
- Shadow distance 55 m, 2 cascades, 1024 atlas.
- Particle pools, zero runtime `Instantiate` during a round.
- Depth prepass off; MSAA 2×; render scale 1.0; HDR off (bloom in LDR is fine
  and halves the bandwidth).
- Frame budget: grass 3.5 ms, everything else 7 ms.

## 10. Folder layout (Assets/)

```
Art/Models/      FBX from Blender
Art/Textures/    procedural PNGs
Audio/           synthesised WAVs
Materials/
Prefabs/
Scenes/Main.unity
Scripts/Gameplay/ Grass/ Camera/ UI/ Audio/ Environment/ Judging/ Util/
Shaders/
```
