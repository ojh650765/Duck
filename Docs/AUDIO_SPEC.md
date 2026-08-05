# DUCK MOW — Audio Spec

63 clips, all **synthesised from scratch in Python** (numpy/scipy). No samples,
no libraries, no AI providers. Sources live in `Art/Python/audio/`, renders in
`Assets/Audio/<Category>/`.

> **One exception, added later: the cutscene narration.** The 13 spoken lines in
> `Assets/Audio/Narration/` are **not** synthesised here — they come from the
> Typecast TTS API. Nothing else in the game does, and nothing at runtime talks
> to a provider. See **§8**.

**Format:** 16-bit PCM WAV, 44 100 Hz. Mono for SFX, stereo for music, the crowd
bed and ambience. Every clip is peak-normalised to **−1.5 dBFS**; nothing clips.

---

## 1. How to re-render

```
cd C:\Duck\Art\Python\audio

python render_all.py                  # all 63 clips, then verify (~2 min)
python render_all.py engine music     # just those modules
python render_all.py --rate=22050     # same clips, half the bytes
python analyze.py                     # verify only
python analyze.py Engine blade_loop   # verify a subset
python levels.py --spec               # regenerate §4 of this document
```

Everything is seeded, so a re-render is byte-identical unless you changed that
module. Output only ever goes to `Assets/Audio/`.

| file | what it holds |
|---|---|
| `dsp.py` | oscillators, filters, envelopes, modal bodies, Karplus-Strong, loop tools, WAV writer |
| `critter.py` | shared animal source-filter voice (goat, badger, heron, goose, sheep, cow, quack) |
| `instruments.py` | the county-fair band + the step sequencer |
| `engine.py` `blade.py` `mower.py` `ui.py` `crowd.py` `ambience.py` `duck.py` `judges.py` `music.py` | one per output folder |
| `analyze.py` | measurement + hard gates |
| `levels.py` | mixer plan (targets → per-clip trims) |
| `render_all.py` | driver |

**To act on feedback:** each clip is a single function with its parameters at
the top of the file (e.g. `engine.py` → `cfg` dict per layer, `mower.py` →
`BONKS`, `music.py` → `MENU_MELODY` / `ROUND_MELODY` as plain note lists).
Change the numbers, run `python render_all.py <module>`, done.

### How the loops are made seamless

Not by crossfading. Every loop is **periodic by construction**:

* noise is filtered with an FFT multiply (`dsp.cfilt`), which is a *circular*
  convolution — the filter tail wraps into the head;
* grains, notes and reverb tails that run past the end are wrap-added
  (`dsp.wadd`, `dsp.creverb`);
* modulators use `dsp.plfo`, which only permits a whole number of cycles per
  loop; oscillators are written as `integer_cycles·t + periodic_wobble`;
* the engine firing schedule contains a whole number of firings per loop, and
  the applause clap rate is snapped so a whole number of claps tiles the loop.

`dsp.wrap_xfade` exists as a fallback with a **complementary equal-power**
crossfade (not a fade-to-zero on both ends, which dips), but nothing shipped
needed it.

---

## 2. THE ENGINE — pitch mapping for Unity

### 2.1 What each layer is

| clip | measured f₀ | as RPM (`f₀ × 60`) | character |
|---|---|---|---|
| `engine_idle_loop.wav` | **28.02 Hz** | **1 680 RPM** | lumpy, low, uneven putter |
| `engine_mid_loop.wav` | **52.00 Hz** | **3 120 RPM** | same machine, more bite |
| `engine_high_loop.wav` | **95.04 Hz** | **5 700 RPM** | opened up, upper harmonics, rasp |

All three are exactly 2.000 s (88 200 samples). The model is one dominant
firing event per revolution, so **audio fundamental = RPM / 60**. If you prefer
to think in 4-stroke firing order (RPM / 120), just double the RPM numbers —
the ratios are what matter.

The layers are built from the same firing grain and the *same absolute*
exhaust/cavity formants (168 / 425 / 1120 Hz), because a real pipe does not
change length with RPM. That is what makes them read as one machine while the
brightness opens up.

### 2.2 Playback rig

Three `AudioSource`s, all looping, all playing all the time; you crossfade
volumes and set pitch. Do **not** try to keep them phase-locked — they are
running at different pitches, so they cannot be, and they do not need to be.

```csharp
// per layer
src.pitch  = rpm / baseRpm[layer];          // 1680 / 3120 / 5700
src.volume = layerWeight(rpm) * engineGain(rpm);
```

### 2.3 Crossfade table

Crossover points are the **geometric means** of the neighbouring bases:

| crossover | RPM | pitch each layer needs there |
|---|---|---|
| idle ↔ mid | **2 289 RPM** | idle ×1.363, mid ×0.734 |
| mid ↔ high | **4 217 RPM** | mid ×1.352, high ×0.740 |

Recommended weights (constant-power, `w_a² + w_b² = 1`, over a ±6 % RPM band
around each crossover):

| RPM | idle | mid | high |
|---|---|---|---|
| ≤ 2 152 | 1.00 | 0.00 | 0.00 |
| 2 289 | 0.707 | 0.707 | 0.00 |
| ≥ 2 427 | 0.00 | 1.00 | 0.00 |
| 3 964 | 0.00 | 1.00 | 0.00 |
| 4 217 | 0.00 | 0.707 | 0.707 |
| ≥ 4 470 | 0.00 | 0.00 | 1.00 |

Suggested game RPM range: **1 250 (idle) → 6 300 (max)**, giving a pitch range
of ×0.74 → ×1.36 on each layer.

### 2.4 The ±25 % question — read this

**±25 % is geometrically impossible for three layers spanning 28 → 95 Hz.**
Three layers give two crossovers; the best you can do is space the bases
geometrically, ratio `(95/28)^½ = 1.842`, which needs `1.842^½ = ×1.363` pitch
at each crossover. That is **±36 %**, and it is a hard floor, not a tuning
choice.

I therefore placed the middle layer at **52 Hz** rather than the requested
~60 Hz: 52 Hz is (near enough) the geometric mean of 28 and 95, which
*minimises* the required pitch range. At 60 Hz the idle→mid crossover would
need ×1.46 (+46 %) instead of ×1.363.

Options if ±36 % is too much for your taste:

* **Accept it.** ±36 % on a firing-event train is very benign — the formants
  are broad and there is no vibrato to smear. This is what I recommend.
* **Four layers.** `engine.BASES = {"idle":28, "low":42, "mid":63.5, "high":95}`
  gives ratio 1.50 and only **±22.5 %** — inside your original budget. Add the
  fourth entry, add a `cfg` block for it, `python render_all.py engine`.
* **Narrow the span.** Bases 34 / 57 / 95 give ±30 %, but idle stops sounding
  like an idle.

### 2.5 Layer level trims

The three files are peak-normalised to −1.5 dBFS as specified, but they have
different crest factors, so their RMS differs. These trims equalise them, which
means a constant-power crossfade produces **zero level step**:

| layer | file RMS | trim |
|---|---|---|
| idle | −12.38 dBFS | **−0.78 dB** |
| mid | −12.54 dBFS | **−0.62 dB** |
| high | −13.16 dBFS | **0.00 dB** |

Then put "the engine gets louder with RPM" on a separate **RPM → bus volume**
curve so you can tune it without re-rendering. Suggested: −6 dB at idle, 0 dB
at max, with an ease-in.

### 2.6 Start / stop handover

`engine_start.wav` (1.15 s) ends at exactly idle speed and level;
`engine_stop.wav` (0.92 s) begins there. With the §2.5 trims applied to the
loops:

| clip | trim | why |
|---|---|---|
| `engine_start` | **−0.38 dB** | its last 100 ms then matches the idle loop's RMS to 0.01 dB |
| `engine_stop` | **−1.24 dB** | its first 60 ms matches the idle loop |

Start the idle loop (pitch ×0.74) the moment `engine_start` ends; the join is
level- and pitch-continuous.

### 2.7 Blade pitch

`blade_loop.wav` is authored with the chop at **208 Hz = 4 × the mid layer's
52 Hz engine order** (312 whole blade passes in its 1.5 s loop, measured f₀
209.0 Hz). Pitch it with the **same ratio as the mid layer**: `rpm / 3120`.
`blade_cut_grass_loop.wav` is on the same grid, so pitch both together.

---

## 3. Music — the 60–160 Hz carve

`music_round_loop` and `music_round_urgent_layer` are both **16 bars at
128 BPM = exactly 30.000 s = 1 323 000 samples**, written on the same grid, so
they line up **sample-for-sample**. Play them from two sources started on the
same frame and fade the urgent layer in; there is no drift.

Two things keep the music out of the engine's way:

1. **Arrangement.** Every bass note in the round loop is voiced at 196 Hz or
   above (an upright playing high, not a sub). Nothing is *written* in the band.
2. **Bus EQ.** A 2-pole high-pass at 130 Hz plus a broad −9 dB bell at 105 Hz.
   Measured transfer:

| 60 Hz | 80 | 100 | 130 | 160 | 196 | 250 | 330 | 440 |
|---|---|---|---|---|---|---|---|---|
| −18.9 dB | −18.0 | −17.5 | −13.5 | −9.5 | −6.5 | −4.1 | −2.4 | −1.3 |

**Measured result** (fraction of total energy in 60–160 Hz, and its level
relative to the 200–800 Hz band):

| clip | 60–160 Hz share | vs 200–800 Hz |
|---|---|---|
| `music_menu_loop` | 0.019 % | −35.1 dB |
| `music_round_loop` | 0.010 % | −37.5 dB |
| `music_round_urgent_layer` | 0.003 % | −42.6 dB |

The menu loop is not carved (no engine is running) — it lands there anyway
because of the arrangement.

**One design note on the urgent layer:** it is deliberately *uniform* in
intensity rather than building across its 30 s, because you fade it in for the
final 15 s of a 75 s round and will therefore enter it at an arbitrary phase.
It has to be tense wherever you join it.

### What the band actually plays

| instrument | synthesis |
|---|---|
| banjo | Karplus-Strong, hard pick, 340 Hz head resonance, fixed 5th-string drone in every roll |
| fiddle | band-limited saw + square, delayed vibrato, bow scrape, 3-peak body |
| upright bass | pizzicato triangle+sine, finger click, voiced ≥196 Hz |
| squeezebox | two detuned reeds through a wooden box, slow attack, tremolo |
| glockenspiel | inharmonic modal (1 : 2.76 : 5.40 : 8.93) |
| percussion | brush, shaker, tambourine, boot-stomp (215 Hz), woodblock |

| cue | key | tempo | form |
|---|---|---|---|
| menu | G major | 120 BPM | 12 bars, `G G C G / Em C D D / C G D G`, fiddle tune with a wrapping pickup |
| round | D major | 128 BPM | 16 bars, `D D G D / Bm G A A / D G D Bm / G A D A` |
| urgent | D major | 128 BPM | same 16 bars: tremolo double-stops, climbing banjo figure, 16th shaker, beat stomp, drifting woodblock |
| reveal | G major | 100 BPM | IV → V → I with a suspension; arrives on beat 4 with glock sparkle |
| judging bed | B minor | 120 BPM | 4 bars, pizz ostinato that drops a step in bar 3, minor-2nd rub in bar 4 |
| fanfare_good | G major | 126 BPM | pickup triplet, `G C D`, land on Gadd9 |
| fanfare_bad | — | 96 BPM | `G → Gm → F#… ` sagging, then the bellows run out (real oscillator droop, not a pitch-shifted sample) |

---

## 4. Clip list, purpose, loop, and level

`trim dB` / `volume` is what to put on the `AudioSource` (or the mixer group).
The whole table is shifted by a uniform **−10.5 dB** so nothing needs a volume
above 1.0 — **put +10.5 dB back on the AudioMixer master** to restore absolute
loudness. Relative balance is exactly as intended.

The tables below are **generated from the rendered files**, not hand-maintained
— regenerate them with `python levels.py --spec` after any re-render. The one
hand-authored number is `TARGET_RMS` at the top of `levels.py`: the loudness
each clip *should* have at the listener. If you want the engine quieter or the
crowd louder, change it there and re-run.

### Engine/ - heard for 75 s straight, so it sits under the music's peaks

| clip | purpose | s | ch | loop | trim dB | volume |
|---|---|---|---|---|---|---|
| `Engine/engine_high_loop.wav` | 95 Hz base (5 700 RPM), opened up | 2.00 | 1 | yes | -16.3 | 0.153 |
| `Engine/engine_idle_loop.wav` | idle putter, 28 Hz base (1 680 RPM) | 2.00 | 1 | yes | -17.1 | 0.140 |
| `Engine/engine_mid_loop.wav` | 52 Hz base (3 120 RPM), more bite | 2.00 | 1 | yes | -16.9 | 0.143 |
| `Engine/engine_start.wav` | pull cord: two failed coughs, then it fires | 1.15 | 1 | no | -8.4 | 0.382 |
| `Engine/engine_stop.wav` | dies with a wheeze and one last cough | 0.92 | 1 | no | -6.8 | 0.455 |

Then apply the layer trims from **2.5** and the handover trims from **2.6** on top.

### Blade/ - gated on while cutting

| clip | purpose | s | ch | loop | trim dB | volume |
|---|---|---|---|---|---|---|
| `Blade/blade_cut_grass_loop.wav` | wet shredding, **added only over uncut grass** - the reward layer | 1.50 | 1 | yes | -16.0 | 0.159 |
| `Blade/blade_disengage.wav` | clunk + spin-down + settling thunk | 0.42 | 1 | no | -15.6 | 0.167 |
| `Blade/blade_engage.wav` | clutch clunk + belt take-up + spin-up | 0.42 | 1 | no | -13.2 | 0.219 |
| `Blade/blade_loop.wav` | deck whine + 208 Hz chop; on whenever the blade spins | 1.50 | 1 | yes | -21.3 | 0.086 |
| `Blade/debris_ping_01.wav` | stone off the deck, variation 1 | 0.25 | 1 | no | -9.4 | 0.338 |
| `Blade/debris_ping_02.wav` | stone off the deck, variation 2 | 0.25 | 1 | no | -8.6 | 0.370 |
| `Blade/debris_ping_03.wav` | stone off the deck, variation 3 | 0.25 | 1 | no | -11.3 | 0.273 |
| `Blade/debris_ping_04.wav` | stone off the deck, variation 4 | 0.25 | 1 | no | -8.1 | 0.395 |

Drive `blade_cut_grass_loop`'s volume from the same *uncut-grass-under-the-deck*
value that fires the clipping particles (TECH_DESIGN 3.4). That is what makes it
read as a reward. Round-robin the four `debris_ping` variations.

### Mower/

| clip | purpose | s | ch | loop | trim dB | volume |
|---|---|---|---|---|---|---|
| `Mower/bonk_01.wav` | collision: wood+metal impact then a wobbling boing | 0.46 | 1 | no | -6.5 | 0.475 |
| `Mower/bonk_02.wav` | collision, lower and softer | 0.50 | 1 | no | -7.4 | 0.426 |
| `Mower/bonk_03.wav` | collision, higher and more metallic | 0.42 | 1 | no | -4.9 | 0.571 |
| `Mower/boost_end.wav` | spin-down + wastegate blow-off + deflating boing | 0.62 | 1 | no | -5.6 | 0.524 |
| `Mower/boost_loop.wav` | roar + 2 646 Hz whine + flutter | 1.00 | 1 | yes | -16.4 | 0.152 |
| `Mower/boost_start.wav` | whoosh + turbo whistle that overshoots (the over-tune joke) | 0.55 | 1 | no | -11.7 | 0.261 |
| `Mower/drift_loop.wav` | grass slide - broad spectrum, no screech peak | 1.00 | 1 | yes | -18.2 | 0.123 |
| `Mower/horn.wav` | the silly parp | 0.62 | 1 | no | -14.1 | 0.198 |
| `Mower/suspension_bump_01.wav` | soft chassis knock, low | 0.24 | 1 | no | -18.5 | 0.119 |
| `Mower/suspension_bump_02.wav` | soft chassis knock, tighter | 0.21 | 1 | no | -15.2 | 0.173 |
| `Mower/suspension_bump_03.wav` | soft chassis knock, deepest | 0.27 | 1 | no | -15.1 | 0.176 |

Modulate `drift_loop` volume with lateral slip speed and `boost_loop` with
remaining boost fuel.

### UI/

| clip | purpose | s | ch | loop | trim dB | volume |
|---|---|---|---|---|---|---|
| `UI/card_flip.wav` | scorecard turning, landing on the frame | 0.32 | 1 | no | -8.5 | 0.375 |
| `UI/card_raise.wav` | card sliding up, locking with a clack | 0.50 | 1 | no | -6.8 | 0.457 |
| `UI/countdown_beep.wav` | 3, 2, 1 - marimba-class bar at A5, not a sine | 0.40 | 1 | no | -5.2 | 0.551 |
| `UI/countdown_go.wav` | GO - D major bar chord + tambourine | 0.75 | 1 | no | -2.3 | 0.767 |
| `UI/klaxon.wav` | time up: fairground PA two-tone (415 + 622 Hz) | 1.30 | 1 | no | -12.1 | 0.248 |
| `UI/score_tick.wav` | per-point counter - **28 ms, nothing below 950 Hz** | 0.03 | 1 | no | -20.6 | 0.093 |
| `UI/stamp.wav` | the rank letter landing | 0.55 | 1 | no | +0.0 | 1.000 |
| `UI/ui_back.wav` | two wooden notes down (A5 -> D5) | 0.49 | 1 | no | -12.0 | 0.251 |
| `UI/ui_click.wav` | wooden tick | 0.11 | 1 | no | -8.3 | 0.385 |
| `UI/ui_confirm.wav` | two wooden notes up (F5 -> C6) | 0.49 | 1 | no | -10.1 | 0.314 |
| `UI/ui_hover.wav` | softer, higher, shorter | 0.08 | 1 | no | -14.5 | 0.188 |

`score_tick` is designed for 30 retriggers a second: 28 ms long, high-passed at
950 Hz so repeated ticks cannot pile into mud, and short enough that consecutive
ticks never overlap. Add +-2 semitones of random pitch in Unity if you want it to
stop sounding mechanical.

### Crowd/

| clip | purpose | s | ch | loop | trim dB | volume |
|---|---|---|---|---|---|---|
| `Crowd/applause_loop.wav` | 13 clappers, seamless | 3.00 | 2 | yes | -4.8 | 0.575 |
| `Crowd/crowd_ambient_loop.wav` | whole-round bed: murmur + 8 animals | 8.00 | 2 | yes | -23.6 | 0.066 |
| `Crowd/crowd_aww.wav` | descending sympathetic aww | 1.60 | 2 | no | -9.7 | 0.328 |
| `Crowd/crowd_cheer_big.wav` | big score reaction | 2.60 | 2 | no | -2.9 | 0.718 |
| `Crowd/crowd_cheer_small.wav` | modest score reaction | 2.40 | 2 | no | -7.1 | 0.441 |
| `Crowd/crowd_gasp.wav` | collective inhale, then hush | 1.50 | 2 | no | -7.7 | 0.414 |
| `Crowd/crowd_laugh.wav` | ha-ha-ha + a goose and a goat losing it | 1.60 | 2 | no | -8.3 | 0.383 |

The bed carries a sheep, a goose, a quack, a goat, a cow, a goose, a quack and a
sheep in that order over its 8 s, all low-passed at 2.6 kHz for distance.

### Ambience/

| clip | purpose | s | ch | loop | trim dB | volume |
|---|---|---|---|---|---|---|
| `Ambience/birds_loop.wav` | 7 distinct species, 9 calls over 12 s | 12.00 | 2 | yes | -17.8 | 0.129 |
| `Ambience/pond_loop.wav` | laps, 3 drips, one resident duck | 5.00 | 2 | yes | -25.4 | 0.054 |
| `Ambience/wind_grass_loop.wav` | gusts that move level and brightness together | 6.00 | 2 | yes | -24.8 | 0.058 |
| `Ambience/windmill_creak.wav` | stick-slip wood under load - **mono, it is positional** | 2.00 | 1 | no | -21.2 | 0.087 |

Bird species: two-note whistle, chirp cluster, five-note warble, wood-pigeon coo,
trill, peep, and a hedgerow caw, placed at 0.35 / 1.90 / 3.10 / 5.05 / 6.85 /
8.20 / 8.95 / 10.40 / 11.35 s with per-call distance filtering, so the 12 s
repeat is hard to hear. Fire `windmill_creak` on a randomised 20-40 s timer at
the windmill's transform.

### Duck/

| clip | purpose | s | ch | loop | trim dB | volume |
|---|---|---|---|---|---|---|
| `Duck/quack_annoyed.wav` | one long flat quack that sags, plus a grumble | 0.52 | 1 | no | -8.9 | 0.358 |
| `Duck/quack_happy.wav` | two bright quacks, the second lifts | 0.58 | 1 | no | -8.8 | 0.362 |
| `Duck/quack_panic.wav` | four accelerating rising quacks + a gasp | 0.72 | 1 | no | -7.6 | 0.419 |
| `Duck/quack_proud.wav` | one long *rising* held quack + bill clatter | 0.78 | 1 | no | -9.3 | 0.342 |

The quack is a very narrow glottal pulse (width 0.145) through a bill that opens
and then closes: F1 sweeps 640 -> 1070 -> 700 Hz and F2 falls 2250 -> 1420 Hz
across the note. Emotion is carried only by syllable count/rate, pitch direction
and roughness - the vowel gesture stays the same, which is why they read as the
same duck.

### Judges/ - one set per judge, `low` for a bad score, `high` for a good one

| clip | purpose | s | ch | loop | trim dB | volume |
|---|---|---|---|---|---|---|
| `Judges/judge_badger_high.wav` | Boris, delighted: four rising chuffs, a bark, applause | 1.05 | 1 | no | -11.5 | 0.266 |
| `Judges/judge_badger_low.wav` | Boris, deflated: one chuff sinking into a grumble | 0.72 | 1 | no | -10.8 | 0.288 |
| `Judges/judge_goat_high.wav` | Mildred, grudging: two bleats, the second lifts | 0.95 | 1 | no | -11.0 | 0.282 |
| `Judges/judge_goat_low.wav` | Mildred, dismissive: drops a fourth, cut off, then chews | 0.62 | 1 | no | -10.9 | 0.285 |
| `Judges/judge_heron_high.wav` | Priscilla: two croaks, the second barely lifts | 0.82 | 1 | no | -7.9 | 0.404 |
| `Judges/judge_heron_low.wav` | Priscilla: a flat croak with no inflection at all | 0.55 | 1 | no | -6.0 | 0.502 |

Species separation is carried by three orthogonal parameters, not by the vowel:
goat = 245 Hz buzz with a hard **23 Hz bleat tremolo**; badger = 120 Hz and
mostly **breath** (noise 0.55-0.62) in several bursts; heron = **88 Hz** with a
very narrow pulse (width 0.13) and heavy roughness.

### Music/

| clip | purpose | s | ch | loop | trim dB | volume |
|---|---|---|---|---|---|---|
| `Music/fanfare_bad.wav` | verdict, deflating | 3.00 | 2 | no | -10.1 | 0.312 |
| `Music/fanfare_good.wav` | verdict, good | 3.00 | 2 | no | -10.9 | 0.286 |
| `Music/music_judging_bed_loop.wav` | under the judges | 8.00 | 2 | yes | -16.7 | 0.146 |
| `Music/music_menu_loop.wav` | menu / briefing | 24.00 | 2 | yes | -13.8 | 0.203 |
| `Music/music_reveal.wav` | overhead reveal ta-daa (one-shot) | 6.00 | 2 | no | -10.5 | 0.297 |
| `Music/music_round_loop.wav` | the 75 s round | 30.00 | 2 | yes | -16.7 | 0.146 |
| `Music/music_round_urgent_layer.wav` | additive; fade in over the last 15 s | 30.00 | 2 | yes | -17.9 | 0.127 |

`music_reveal` is 6 s and lands its big chord at **2.4 s** - fire it as the camera
starts rising so the arrival coincides with the picture appearing.

---

## 5. Suggested mixer groups

```
Master  (+10.5 dB, see §4)
├── Music        music_*, fanfare_*
├── SFX
│   ├── Engine   engine_* (+ RPM→volume curve, §2.5)
│   ├── Blade    blade_*, debris_ping_*
│   ├── Mower    bonk_*, horn, drift, boost_*, suspension_*
│   └── Voice    quack_*, judge_*
├── Crowd        crowd_*, applause_*
├── Ambience     birds, wind, pond, windmill
└── UI           countdown_*, klaxon, ui_*, score_tick, card_*, stamp
```

Duck under Music by 3–4 dB during Reveal/Judging, and duck Music by ~2 dB while
`klaxon` plays.

---

## 6. File size

**63 clips, 27.64 MB uncompressed** at 44.1 kHz / 16-bit:

| folder | MB |
|---|---|
| Music | 17.50 |
| Ambience | 4.04 |
| Crowd | 3.48 |
| Engine | 0.68 |
| Mower | 0.50 |
| UI | 0.42 |
| Blade | 0.41 |
| Judges | 0.40 |
| Duck | 0.22 |

**This exceeds the 12 MB uncompressed target, and the target is not reachable
with this clip list.** 12 MB at 44.1 kHz / 16-bit / stereo is 68 seconds of
stereo audio *in total*; the requested music alone is 104 s of stereo
(24 + 30 + 30 + 6 + 8 + 3 + 3), before any ambience or crowd. The arithmetic
does not close, at any level of synthesis skill.

Three ways to deal with it, in the order I would pick them:

1. **Ignore the uncompressed number** — it is a source-asset figure, not a
   shipping one. Unity's WebGL builds encode to Vorbis. At quality 60
   (≈128 kbps stereo, ≈80 kbps mono) this set is **≈2.6 MB in the build**.
   Recommended import settings:

   | folder | load type | compression | quality |
   |---|---|---|---|
   | Music | Streaming | Vorbis | 60 |
   | Crowd / Ambience beds | Compressed In Memory | Vorbis | 55 |
   | Engine / Blade loops | Compressed In Memory | Vorbis | 70 |
   | short SFX, UI | Decompress On Load | Vorbis | 70 |

   Force To Mono on everything in Engine/Blade/Mower/UI/Duck/Judges (they are
   already mono). Leave Preload Audio Data off for Music.

2. **`python render_all.py --rate=22050`** — re-renders every clip at
   22.05 kHz for **13.8 MB**. The resampler is FFT-based (circular), so loops
   stay sample-exact seamless at the lower rate. Measured energy above 11 kHz,
   which is what you would lose:

   | folder | worst clip | median |
   |---|---|---|
   | Music (17.5 MB, the bulk) | 0.035 % (`music_reveal`) | 0.010 % |
   | Ambience | 0.141 % (`wind_grass_loop`) | 0.043 % |
   | Crowd | 0.764 % (`applause_loop`) | 0.002 % |
   | worst in the whole set | 3.02 % (`blade_cut_grass_loop`) | — |

   So the honest version is: **halve the rate on Music, Crowd and Ambience
   only** (`python render_all.py music crowd ambience --rate=22050`, giving
   **15.1 MB**) and leave the SFX at 44.1 kHz, because the shred layer, the
   stone pings and the wastegate genuinely use the top octave. Halving
   everything to reach 13.8 MB costs `blade_cut_grass_loop` 3 % of its energy —
   audible as a slightly duller shred, which is a real cost on the one clip
   that is meant to be the reward for good play.

3. **Cut content.** The only meaningful lever is the round pair: halving
   `music_round_loop` and `music_round_urgent_layer` to 8 bars (15 s) saves
   5.3 MB but doubles how often the tune repeats across a 75 s round. Say the
   word and I will re-cut the arrangement so 8 bars stands up to it.

---

## 7. Verification

Every clip is measured; `python analyze.py` prints the table and runs hard
gates. Current state: **all 63 clips pass**.

Gates:

* peak within 0.35 dB of −1.5 dBFS, never above −0.5;
* for loops: the wrap-around sample step must not be the largest step in the
  clip, and must be below the clip's RMS;
* for loops: a 1024-sample window straddling the wrap must sit between the 2nd
  and 98th percentile of interior window levels (this is what catches the
  fade-both-ends dip).

### Loop seams — measured

`step` = the wrap-around sample step in dBFS and its percentile among the
clip's own interior sample-to-sample steps. `lvl` = the seam window's level
versus the median interior window, and its percentile.

| loop | step dBFS | step pct | seam lvl | seam pct |
|---|---|---|---|---|
| engine_idle_loop | −40.0 | 87 | +0.25 dB | 56 |
| engine_mid_loop | −36.9 | 78 | −0.40 dB | 32 |
| engine_high_loop | −39.5 | 32 | +0.15 dB | 62 |
| blade_loop | −31.6 | 52 | −0.01 dB | 49 |
| blade_cut_grass_loop | −21.9 | 72 | +0.45 dB | 62 |
| drift_loop | −35.1 | 27 | +0.63 dB | 68 |
| boost_loop | −17.9 | 95 | −0.72 dB | 41 |
| crowd_ambient_loop | −35.1 | 47 | +1.43 dB | 79 |
| applause_loop | −59.2 | 0 | −0.16 dB | 46 |
| birds_loop | −41.3 | 55 | −10.20 dB | 16 |
| wind_grass_loop | −28.5 | 53 | +0.30 dB | 56 |
| pond_loop | −30.6 | 67 | −0.75 dB | 37 |
| music_menu_loop | −49.6 | 3 | −0.41 dB | 42 |
| music_round_loop | −36.7 | 28 | +0.34 dB | 58 |
| music_round_urgent_layer | −37.3 | 30 | +0.30 dB | 56 |
| music_judging_bed_loop | −43.3 | 40 | +3.10 dB | 76 |

`birds_loop`'s −10.2 dB seam level is not a defect: the loop point deliberately
falls in a gap between bird calls (there is still a quiet air bed there, not
digital silence). Its sample step is −41.3 dBFS at the 55th percentile, i.e.
mathematically continuous.

### Spectral sanity — key clips

| clip | measured f₀ | intended | harmonicity | centroid | crest |
|---|---|---|---|---|---|
| engine_idle_loop | 28.02 Hz | 28 | 0.66 | 164 Hz | 10.9 dB |
| engine_mid_loop | 52.00 Hz | 52 | 0.78 | 277 Hz | 11.0 dB |
| engine_high_loop | 95.04 Hz | 95 | 0.77 | 606 Hz | 11.7 dB |
| blade_loop | 209.0 Hz | 208 (4 × 52) | 0.76 | 1 011 Hz | 12.6 dB |
| horn | 302.1 Hz | 302 | 0.99 | 683 Hz | 8.9 dB |
| klaxon | 207.0 Hz | 207.6 (missing fundamental of 415 + 622) | 0.96 | 895 Hz | 7.9 dB |
| countdown_beep | A5 bar | 880 Hz + 4× + 9.8× | 1.00 | 899 Hz | 18.8 dB |
| score_tick | 1 696 Hz | 1 720 | 0.46 | 1 796 Hz | 18.3 dB |
| quack_happy | 341.9 Hz | 330 → 352 | 0.67 | 1 398 Hz | 15.1 dB |
| quack_annoyed | 219.4 Hz | 252 → 196 | 0.44 | 1 118 Hz | 15.0 dB |
| judge_goat_low | 229.7 Hz | 248 falling to 182 | 0.31 | 778 Hz | 13.0 dB |
| judge_heron_low | 84.3 Hz | 88 | 0.35 | 826 Hz | 18.0 dB |
| judge_heron_high | 101.8 Hz | 92 rising to 112 | 0.31 | 1 003 Hz | 16.1 dB |
| music_round_loop | — | D major, 128 BPM | — | 1 237 Hz | 14.3 dB |

The engine layers' octave-band energy opens up exactly as intended — the
progression from idle to high is the point:

| clip | 20–80 | 80–160 | 160–320 | 320–640 | 640–1.3k | 1.3–2.6k | 2.6–5k |
|---|---|---|---|---|---|---|---|
| engine_idle_loop | 25 % | 24 % | 45 % | 5 % | 1 % | 0.2 % | — |
| engine_mid_loop | 10 % | 27 % | 40 % | 16 % | 5 % | 2 % | — |
| engine_high_loop | 0.5 % | 8 % | 32 % | 31 % | 17 % | 10 % | 1 % |

Every clip has a real attack transient rather than a noise wall: crest factors
run 7.9 dB (klaxon, dense by design) to 23.2 dB (applause), and no clip in the
set is a plain filtered-noise bed — the "shhh" failure mode is checked by the
crest and centroid columns in `analyze.py`.

### Engine crossfade continuity — measured

Resampling `engine_idle_loop` up 36 % and `engine_mid_loop` down 27 % so both
sit at the 38.2 Hz crossover:

* level difference after the §2.5 trims: **0.0 dB** (the trims equalise RMS,
  and FFT resampling preserves RMS);
* pitch is identical by construction, so there is no pitch jump — only a
  gradual timbre morph across the crossfade band. The largest single-octave
  energy difference at the idle↔mid crossover is 8.9 dB (in 160–320 Hz) and at
  mid↔high 11.6 dB (in 640–1.3 kHz). That difference *is* the "engine opens up"
  effect; a ±6 % RPM crossfade band spreads it over roughly 275 RPM.

---

## 8. Narration/ — the one clip set not synthesised here

The opening cutscene's 13 narration lines are spoken aloud. They are rendered by
**Typecast** (`api.typecast.ai`), not by `Art/Python/audio/`, and that is a real
exception to the rule at the top of this document rather than an oversight. The
reason is simply that nothing in `dsp.py` synthesises English speech, and the
band those lines sit in is the only thing telling the opening story.

Everything else about the exception is bounded:

* **Nothing at runtime talks to a provider.** The bake happens once in the
  editor, the result is 13 WAVs in `Assets/Audio/Narration/`, and the build
  contains audio files like any other. **No API key exists in the game**, which
  is the only defensible arrangement for a WebGL build.
* **The lines are not authored twice.** `DuckNarrationBaker` reads them out of
  `DuckCutsceneBuilder`'s panel table, so the voice cannot say something the
  narration band does not show.
* **Reproducible, like the rest.** Fixed `seed` per line, so re-baking an
  unchanged line returns the same reading.

### How to re-render

```
Duck/Diagnose · List Typecast voices             # pick a voice_id
Duck/5 · Bake cutscene narration (Typecast)      # skips lines already on disk
Duck/5 · Re-bake cutscene narration (overwrite)  # after changing voice or tempo
Duck/3 · Rebuild opening cutscene (open scene)   # re-times the page around them
```

**The key is not in this repository and must not be.** Either set
`TYPECAST_API_KEY` in the environment, or put the key on its own line in
`.secrets/typecast.key` (git-ignored — `/.secrets/` in `.gitignore`).

The knobs are consts at the top of `Assets/Editor/DuckNarrationBaker.cs`:
`VoiceId`, `Model` (`ssfm-v30`), `Tempo` (0.94), `TargetLufs` (−16). Note that
`TargetLufs` is *not* the −1.5 dBFS peak normalisation the other 63 clips use —
speech wants a loudness target, not a peak target, and 13 separate API calls
would otherwise land at 13 slightly different levels. The one runtime knob is
`ComicSequence.narrationVolume` (0.85), which moves all 13 together.

### Timing — why this changes the cutscene's length

A written line has no duration, so the sequence used to split a panel's hold
evenly between its lines. A *spoken* line does, so it cannot. `Duck/3` now
measures each imported clip and writes a per-line hold into `ComicPanel`:

```
hold = leadIn? + leadOut? + max(clip.length + 0.45, chars/17, 1.5)
panel.duration = max(authored duration, Σ holds)
```

Durations only ever grow, never shrink, so no beat anyone tuned gets shortened.
`Duck/3` logs the page's total panel time before and after. With no clips
present the holds are left empty and the even split applies — i.e. the page
behaves exactly as it did before there was a voice.

### Import settings and cost

| setting | value | why |
|---|---|---|
| load type | Compressed In Memory | 13 × ~3 s of PCM would be ~3.5 MB resident for a one-off |
| compression | Vorbis q 0.5 (default), **AAC** (WebGL override) | AAC is WebGL's only format; spelled out so the Inspector matches the build |
| force to mono | yes | one narrator, centre, every device |
| preload | yes | a line must not hitch on the frame it starts |
| streaming | **never** | WebGL has no file handles to stream from |

Roughly **3.5 MB** of source WAV, **≈0.4 MB** in the build, **≈0.4 MB** resident.
Against the 27.6 MB / ≈2.6 MB the other 63 clips already cost, this is noise.

### Licensing — read before shipping

Typecast's usage policy: generated audio may be used commercially **during the
subscription period**, and after it expires *previously downloaded audio may
continue to be used* but nothing new may be rendered. Redistributing the audio
**as isolated files or as a sound library** is prohibited — embedding it in the
game is the intended use, shipping the WAVs as a downloadable pack is not. The
free plan requires attribution; paid plans include a commercial licence.
**Confirm the account's plan before shipping**, and note the clips are checked
into this repository as project assets.
