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

        TextMeshProUGUI _tier, _rally;
        Canvas _canvas;
        int _seenStrikes = -1;
        float _tierTimer;
        int _shownRally = -1;

        static readonly Color Perfect = new Color(1.00f, 0.82f, 0.25f);
        static readonly Color Good = new Color(0.62f, 0.90f, 0.45f);
        static readonly Color Normal = new Color(0.85f, 0.88f, 0.92f);

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

            _tier.text = "";
            _rally.text = "";
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
                _seenStrikes = defence.StrikeCount;
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
        }
    }
}
