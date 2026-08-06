using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckMow
{
    /// <summary>
    /// What the player is told about a parry: which tier it was, and how long the rally is.
    ///
    /// The phase already had an IMGUI readout, and that is a measuring instrument — nine lines of
    /// diagnostics that ship with nothing. Two of the goal's requirements are specifically about the
    /// player being TOLD: "a clear visual indication of normal, good and perfect parries" and "a visible
    /// rally or combo indicator". Neither can be satisfied by a debug overlay, and neither was satisfied
    /// by the hit stop and the camera punch alone — those communicate FORCE, not GRADE. A perfect and a
    /// normal parry differed by 65 ms of freeze and some particle count, which nobody can name while
    /// driving.
    ///
    /// Built at runtime rather than baked into the scene, unlike everything else in the arena. The reason
    /// the geometry had to be baked was level design: a bed nobody can select is a bed nobody can move.
    /// Screen-space UI has no such dimension — it is pinned to frame coordinates, not to the world — so
    /// there is nothing to drag and nothing gained by making it inspectable.
    ///
    /// Driven by POLLING StrikeCount rather than by an event, for the same reason the capture tools poll
    /// it: an int that changes on the frame of contact cannot be missed if it is compared every frame,
    /// and it needs no subscription to leak or unhook.
    /// </summary>
    public class DefenceHud : MonoBehaviour
    {
        [Tooltip("The raid this reports on. Found in the scene if left empty.")]
        public GooseDefence defence;

        [Tooltip("Seconds the tier word stays up. Short: it is a punctuation mark, not a message.")]
        public float tierHold = 0.75f;

        TextMeshProUGUI _tier, _rally, _mine, _theirs, _result, _resultSub;
        Canvas _canvas;
        int _seenStrikes = -1;
        float _tierTimer;
        int _shownRally = -1;
        int _shownMine = -1, _shownTheirs = -1;
        /// <summary>Counts down after a counter changes, so the number that moved is the one that flashes.</summary>
        float _minePulse, _theirsPulse;
        GooseDefence.MatchResult _shownResult = GooseDefence.MatchResult.None;

        static readonly Color Perfect = new Color(1.00f, 0.82f, 0.25f);
        static readonly Color Good = new Color(0.62f, 0.90f, 0.45f);
        static readonly Color Normal = new Color(0.85f, 0.88f, 0.92f);
        /// <summary>A garden one goose from falling. Reserved for the last bed — an always-red counter warns
        /// about nothing.</summary>
        static readonly Color Critical = new Color(1.00f, 0.32f, 0.24f);
        static readonly Color Warning = new Color(1.00f, 0.68f, 0.22f);

        void Awake()
        {
            if (defence == null) defence = FindFirstObjectByType<GooseDefence>();
            Build();
        }

        void Build()
        {
            var go = new GameObject("DefenceHudCanvas");
            go.transform.SetParent(transform, false);

            var canvas = go.AddComponent<Canvas>();
            // ScreenSpaceCAMERA, not Overlay, and the reason is that the review loop cannot see Overlay.
            //
            // An overlay canvas is composited by the UI system at the end of the frame, not drawn by any
            // camera — so Camera.Render() into a RenderTexture, which is how every capture in this project
            // is taken, produces a frame with no UI in it at all. The indicator was built, wired and
            // running, and it was invisible to the only process able to check it: indistinguishable from
            // never having been written.
            //
            // Rendered by the camera it reports on, so it lands in captures and in the build alike.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            // Close to the lens so nothing in the world can intersect the text.
            canvas.planeDistance = 0.6f;
            // Above the round HUD if one exists, because a tier call that is behind the clock is not a
            // tier call.
            canvas.sortingOrder = 50;
            _canvas = canvas;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            // Match width and height equally: a phone in landscape and a desktop window differ more in
            // aspect than in size, and biasing either axis makes the word crawl across the frame.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>().enabled = false;

            _tier = Text("Tier", go.transform, 96f, new Vector2(0.5f, 0.62f), TextAlignmentOptions.Center);
            _rally = Text("Rally", go.transform, 46f, new Vector2(0.5f, 0.53f), TextAlignmentOptions.Center);

            // THE SCOREBOARD — the readout the phase never had.
            //
            // What sat here before was a twelve-second countdown, and a countdown is the one number in this
            // beat that tells the player nothing they can act on: it does not say whether they are winning,
            // it does not change when they play well, and it cannot be affected by anything they do. These
            // two can. "2 LEFT" is how close the duck's garden is to being finished and how close the
            // rival's is, which is the entire state of the match in two glances — and the numbers move
            // because of something that just happened on the pitch, which is what makes them worth reading.
            //
            // Placed at the two upper corners, facing each other across the frame: the player's own garden
            // on the left where their machine is, the rival's on the right where the far goal is. That is
            // the same left-to-right geography as the pitch, so the counters agree with the world.
            _mine = Text("MyGarden", go.transform, 34f, new Vector2(0.5f, 0.5f),
                         TextAlignmentOptions.TopLeft);
            // 72 px of top margin, up from 34 through 56. The first pass at 34 shaved the cap-heights off
            // the top line and 56 still clipped on the award beat, and the reason is a SAFE AREA problem
            // rather than a spacing one: a ScreenSpaceCamera canvas sizes itself to the camera's viewport,
            // and every capture in this project renders that camera into a fixed 1600x900 texture — so the
            // instant the Game view is not 16:9 the top and bottom of the canvas fall outside the frame.
            // The same is true of a browser window of any shape. A counter is worth nothing if its first
            // word is cropped, so it gets a margin that survives the aspect ratio being wrong.
            Corner(_mine, new Vector2(0f, 1f), new Vector2(46f, -72f), 560f);
            _theirs = Text("TheirGarden", go.transform, 34f, new Vector2(0.5f, 0.5f),
                           TextAlignmentOptions.TopRight);
            Corner(_theirs, new Vector2(1f, 1f), new Vector2(-46f, -72f), 560f);

            // The call. Big, central, and it only ever appears once — there is exactly one result per raid.
            _result = Text("Result", go.transform, 104f, new Vector2(0.5f, 0.5f),
                           TextAlignmentOptions.Center);
            _resultSub = Text("ResultSub", go.transform, 40f, new Vector2(0.5f, 0.40f),
                              TextAlignmentOptions.Center);

            // OUTLINES, and they are not decoration.
            //
            // Every one of these labels is drawn over a sunlit lawn, and the two that matter most are drawn
            // over the FLOWERBEDS — pale cream blooms and pale soil, which is exactly the value range gold
            // and white text occupy. The first captured win frame had "RAID WON" sitting on top of the
            // player's own flowers and the loss frame had red text over flattened pale beds; both were
            // readable and neither was clean. A dark contour costs nothing and makes the text independent
            // of whatever happens to be behind it, which on a moving chase camera is anything at all.
            // Thinner on the big word than on the small ones. TMP's outline width is normalised against the
            // distance field rather than measured in pixels, so the same number is a hairline on a 34 pt
            // counter and a heavy border on a 112 pt headline — 0.22 came out looking hollow, like outlined
            // type rather than gold type with a contour.
            Outline(_result, 0.14f);
            Outline(_resultSub, 0.20f);
            Outline(_mine, 0.20f);
            Outline(_theirs, 0.20f);
            // The tier word is display-sized too — 88 pt, 118 on a perfect — so it takes the headline's
            // hairline rather than the counters' contour.
            Outline(_tier, 0.13f);
            Outline(_rally, 0.18f);

            _tier.text = "";
            _rally.text = "";
            _mine.text = "";
            _theirs.text = "";
            _result.text = "";
            _resultSub.text = "";
        }

        /// <summary>
        /// Pin a label to a corner of the frame rather than to a point in it.
        ///
        /// Anchored to the corner and offset in pixels, so the counters keep their margin at any aspect
        /// ratio instead of drifting toward the middle on a narrow window — which is what a fractional
        /// anchor does, and this build has to survive a browser window of any shape.
        /// </summary>
        /// <summary>
        /// A dark contour on one label.
        ///
        /// Set through <c>fontMaterial</c>, which is TMP's per-object material INSTANCE — touching
        /// <c>fontSharedMaterial</c> here would write the outline into the font asset itself and every other
        /// piece of text in the project would inherit it. Kept subtle: enough to separate the glyph from a
        /// bright lawn, not enough to read as a cartoon sticker.
        /// </summary>
        static void Outline(TextMeshProUGUI t, float width)
        {
            if (t == null) return;
            var m = t.fontMaterial;
            if (m == null) return;
            m.EnableKeyword("OUTLINE_ON");
            t.outlineColor = new Color32(18, 24, 20, 235);
            t.outlineWidth = width;
        }

        static void Corner(TextMeshProUGUI t, Vector2 corner, Vector2 offset, float width)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = corner;
            rt.pivot = corner;
            rt.sizeDelta = new Vector2(width, t.fontSize * 2.6f);
            rt.anchoredPosition = offset;
        }

        TextMeshProUGUI Text(string name, Transform parent, float size, Vector2 anchor,
                             TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var t = go.AddComponent<TextMeshProUGUI>();
            // TMP_Settings' default rather than a serialized reference, because this object is created at
            // runtime and has no inspector for anybody to assign a font in. Without this the text renders
            // as nothing at all and looks like the component never ran.
            if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
            t.fontSize = size;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            t.characterSpacing = 6f;

            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900f, size * 1.5f);
            rt.anchoredPosition = Vector2.zero;
            return t;
        }

        void Update()
        {
            if (defence == null || _tier == null) return;

            // Camera.main is null for a frame or two while a scene loads, and a ScreenSpaceCamera canvas
            // with no camera draws nothing at all — so rebind until it takes rather than only at Awake.
            if (_canvas != null && _canvas.worldCamera == null)
            {
                _canvas.worldCamera = Camera.main;
                if (_canvas.worldCamera == null) return;
            }

            bool live = defence.State != GooseDefence.Phase.Idle &&
                        defence.State != GooseDefence.Phase.Done;
            if (!live)
            {
                _tier.text = "";
                _rally.text = "";
                _mine.text = "";
                _theirs.text = "";
                _result.text = "";
                _resultSub.text = "";
                _seenStrikes = defence.StrikeCount;
                // Reset so a retry redraws both counters from scratch. Left at their last values, the
                // second raid would open with the FIRST one's numbers on screen and only correct itself
                // when a bed happened to fall.
                _shownMine = _shownTheirs = -1;
                _shownResult = GooseDefence.MatchResult.None;
                return;
            }

            // A new strike: name it.
            if (defence.StrikeCount != _seenStrikes)
            {
                _seenStrikes = defence.StrikeCount;
                var tier = defence.LastStrikeTier;

                _tier.text = tier switch
                {
                    GooseDefence.Tier.Perfect => "PERFECT!",
                    GooseDefence.Tier.Good => "GOOD",
                    GooseDefence.Tier.Normal => "PARRY",
                    _ => ""
                };
                _tier.color = tier switch
                {
                    GooseDefence.Tier.Perfect => Perfect,
                    GooseDefence.Tier.Good => Good,
                    _ => Normal
                };
                // Perfect gets a bigger word as well as a different one, so the grade reads from the
                // corner of the eye without being read.
                _tier.fontSize = tier == GooseDefence.Tier.Perfect ? 118f : 88f;
                _tierTimer = tierHold;
            }

            // On UNSCALED time, so the word does not hang on screen for the whole hit stop and slow
            // motion and then vanish the instant the world speeds up.
            if (_tierTimer > 0f)
            {
                _tierTimer -= Time.unscaledDeltaTime;
                // Fade the last third rather than cutting, which reads as a dropped frame.
                float k = Mathf.Clamp01(_tierTimer / (tierHold * 0.34f));
                var c = _tier.color; c.a = k; _tier.color = c;
                if (_tierTimer <= 0f) _tier.text = "";
            }

            // The rally counter only appears once there IS a rally — a permanent "RALLY x0" is furniture.
            if (defence.Rally != _shownRally)
            {
                _shownRally = defence.Rally;
                _rally.text = _shownRally >= 2 ? $"RALLY  ×{_shownRally}" : "";
                // Warms toward the perfect colour as the rally climbs, so escalation is visible in the
                // indicator and not only in the speed.
                _rally.color = Color.Lerp(Normal, Perfect, Mathf.Clamp01((_shownRally - 2) / 4f));
            }

            UpdateGardens();
            UpdateResult();
        }

        /// <summary>
        /// The two bed counters. Redrawn only when a number moves, and flashed when it does.
        ///
        /// A garden losing a bed is the most consequential event in the phase and it happens fifty metres
        /// away from where the player is looking, or behind them. Without a flash the counter is a
        /// bookkeeping display that quietly disagrees with the frame; with one it is the phase telling the
        /// player that the thing that just happened MATTERED, on the frame it happened.
        ///
        /// Both flashes run on unscaled time, because a bed falls during a hit stop often enough that
        /// scaled time would hold the flash frozen and then throw it away as the world resumed.
        /// </summary>
        void UpdateGardens()
        {
            int mine = defence.MyBedsToSpare;
            int theirs = defence.TheirBedsToSpare;

            if (mine != _shownMine)
            {
                _shownMine = mine;
                // Flashed on a LOSS only. Nothing else can move this number, but saying so here means a
                // future mechanic that gives a bed back cannot accidentally alarm the player.
                _minePulse = 0.55f;
                _mine.text = $"MY GARDEN\n{mine} LEFT";
            }
            if (theirs != _shownTheirs)
            {
                _shownTheirs = theirs;
                _theirsPulse = 0.55f;
                _theirs.text = $"{defence.OpponentName}\n{theirs} LEFT";
            }

            // Colour by how close each garden is to going, not by whose it is. One bed left is red on both
            // sides — on mine that is a warning and on theirs it is an opportunity, and the player does not
            // need to be told which of those they are looking at.
            _mine.color = Fade(Tone(mine), _minePulse);
            _theirs.color = Fade(Tone(theirs), _theirsPulse);

            _minePulse = Mathf.Max(0f, _minePulse - Time.unscaledDeltaTime);
            _theirsPulse = Mathf.Max(0f, _theirsPulse - Time.unscaledDeltaTime);
        }

        static Color Tone(int left) => left <= 0 ? Critical
                                     : left == 1 ? Critical
                                     : left == 2 ? Warning : Normal;

        /// <summary>A pulse toward white, so the flash reads on top of whatever colour the counter is.</summary>
        static Color Fade(Color c, float pulse)
            => Color.Lerp(c, Color.white, Mathf.Clamp01(pulse / 0.55f) * 0.85f);

        /// <summary>
        /// The result, the moment the raid is called.
        ///
        /// Shown from the instant <see cref="GooseDefence.Match"/> turns, which is during Settle — while the
        /// last goose is still bouncing and the flattened beds are still in frame. That is deliberate: the
        /// award beat is two seconds later and is where the MARKS land, but the moment the player won or lost
        /// is the moment a garden went, and a result that only appeared on the scorecard would let the
        /// dramatic event pass in silence.
        ///
        /// It never clears itself. There is exactly one result per raid and it stays up through the award,
        /// which is the only place it could be read against the number it produced.
        /// </summary>
        void UpdateResult()
        {
            var r = defence.Match;
            if (r == _shownResult) return;
            _shownResult = r;

            if (r == GooseDefence.MatchResult.None)
            {
                _result.text = "";
                _resultSub.text = "";
                return;
            }

            var o = defence.Result;
            // A knockout and a decision are named differently on purpose. "GARDEN LOST" and "LOST ON BEDS"
            // are not the same defeat, and a player who has just been beaten on the clock is entitled to
            // know that nothing of theirs actually fell.
            _result.text = r switch
            {
                GooseDefence.MatchResult.Won => o.onBudget ? "WON ON BEDS" : "RAID WON",
                GooseDefence.MatchResult.Lost => o.onBudget ? "LOST ON BEDS" : "GARDEN LOST",
                _ => "HELD"
            };
            _result.color = r switch
            {
                GooseDefence.MatchResult.Won => Perfect,
                GooseDefence.MatchResult.Lost => Critical,
                _ => Normal
            };
            _result.fontSize = o.onBudget ? 86f : 112f;

            _resultSub.text = r switch
            {
                GooseDefence.MatchResult.Won =>
                    $"{defence.OpponentName}'S FLOWERS ARE FLAT  ·  {o.theirBedsLost} BEDS",
                GooseDefence.MatchResult.Lost =>
                    $"{o.myBedsLost} OF YOUR BEDS ARE GONE",
                _ => "NOBODY BROKE  ·  BOTH GARDENS HELD"
            };
            _resultSub.color = _result.color;

            // The tier word would otherwise sit on top of the call for the rest of its hold.
            _tier.text = "";
            _tierTimer = 0f;
            _rally.text = "";
        }
    }
}
