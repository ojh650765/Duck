using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using DuckMow;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Builds the on-screen driving controls from the same county-fair UI art as the HUD.
    ///
    /// Built by editor script rather than authored as a prefab for the reason every other screen in
    /// this project is: the layout numbers and the reasons behind them live next to each other in
    /// source, and a scene rebuild cannot lose them.
    ///
    /// Two constraints set the geometry, and both are physical rather than aesthetic:
    ///
    ///   Nothing may sit where the HUD already is. The clock is top centre, the picture card and
    ///   the aerial token are down the top right, and the boost gauge is bottom left. What is left
    ///   for two thumbs is the bottom-left quarter and the right-hand edge below the card, which is
    ///   exactly where these go.
    ///
    ///   Every target is at least 48 CSS pixels across. The canvas scaler matches screen HEIGHT
    ///   against a 1080-high reference, so a size in reference pixels is a fixed share of screen
    ///   height: 150 reference px is 0.139 of the height, which on a phone's 390 CSS-px-tall
    ///   landscape viewport is 54 CSS px. Anything under about 130 reference px is a target that
    ///   needs a second attempt, and a brake that needs a second attempt is not a brake.
    /// </summary>
    public static class DuckTouchBuilder
    {
        const string RootName = "~ Touch Controls";

        /// <summary>
        /// Rebuild the cluster in whatever scene is open.
        ///
        /// Separate from the full scene rebuild because this is the part that wants iterating on a
        /// device: change a position, rebuild, build the player, hold the phone. Rebuilding the
        /// whole scene to move a button would take twenty minutes a try.
        /// </summary>
        [MenuItem("Duck/7 · Build touch controls", priority = 7)]
        public static void BuildInOpenScene()
        {
            var hud = Object.FindFirstObjectByType<HUD>();
            var cam = hud != null ? hud.GetComponent<Canvas>().worldCamera : null;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                Debug.LogError("[Duck] No camera in the open scene to hang the touch controls on.");
                return;
            }

            Build(cam);
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[Duck] Touch controls built. Save the scene to keep them.");
        }

        /// <summary>
        /// Build (or replace) the cluster, parented to nothing and pointed at <paramref name="cam"/>.
        /// </summary>
        public static TouchControls Build(Camera cam)
        {
            // Replace rather than add. Running this twice used to leave two clusters stacked, both
            // claiming touches, and the second one's stick fought the first one's for the override.
            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var go = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            var canvas = go.GetComponent<Canvas>();

            // Screen Space - Camera, matching the HUD, so the capture rig — which renders the
            // camera to a texture — can see these. An Overlay canvas is invisible to every
            // screenshot and every review, which for a control cluster means nobody ever reviews
            // whether it is in the way of the game.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.95f;   // nearer than the HUD's 1.0, behind the cutscene's page
            canvas.sortingOrder = 150;      // HUD is 100, the opening comic is 200

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Match height, like the HUD. It is also what makes a reference-pixel size a fixed
            // share of screen height, which is the whole basis of the 48-px argument above.
            scaler.matchWidthOrHeight = 1f;

            var root = (RectTransform)go.transform;
            var touch = go.AddComponent<TouchControls>();
            touch.group = go.GetComponent<CanvasGroup>();

            BuildStick(root, touch);
            touch.buttons = new[]
            {
                // Bottom right, biggest, where the right thumb rests: the handbrake. It is the
                // control that turns the mower's wide arc into a corner sharp enough to draw with,
                // so it is the one that must never be missed.
                Button(root, touch, TouchControls.Action.Handbrake, "BRAKE", 0.905f, 0.145f, 190f),
                // Boost sits inboard of the brake and lower, next to the gauge that meters it.
                Button(root, touch, TouchControls.Action.Boost, "BOOST", 0.745f, 0.115f, 150f),
                // Horn and look are single presses, so they go up and out of the way of a thumb
                // that is resting on the brake. Look is stacked under the aerial token in the HUD
                // (0.628–0.694 of height) so the button and the thing it spends are read together.
                Button(root, touch, TouchControls.Action.Horn, "HORN", 0.745f, 0.315f, 140f),
                Button(root, touch, TouchControls.Action.Aerial, "LOOK", 0.905f, 0.355f, 150f),
            };

            return touch;
        }

        static void BuildStick(RectTransform root, TouchControls touch)
        {
            // The claimable region, invisible and much larger than the ring drawn inside it.
            //
            // Large because the ring is not a target: TouchControls moves it to wherever the thumb
            // landed (see MoveRingTo), so this rectangle is only answering "did the player mean to
            // steer?". The left 40% of the screen below the halfway line can mean nothing else.
            //
            // The bottom edge is at 0.13 rather than 0 so the ring is never drawn on top of the
            // boost gauge, which lives at 0.035–0.125 of the height in the corner underneath.
            var area = DuckUIBuilder.Frac("StickArea", root, 0f, 0.13f, 0.40f, 0.70f);
            touch.stickArea = area;

            // The ring is a sibling of the area, not a child of it, because it gets MOVED: as a
            // child its position would be expressed in the area's space and clamped by nothing,
            // and a thumb landing near the edge would put the ring half outside its own parent.
            var ring = Pin("StickRing", root, 0.135f, 0.325f, 300f);
            // timer_ring_256 is the HUD's clock ring, reused. It is the only round element in the
            // set and it already reads as a dial rather than a plate, which is what a thumb ring
            // wants to be. Held well back in alpha: this sits over grass the player is steering
            // across, and it must not compete with it.
            var ringImg = DuckUIBuilder.AddImage(ring, DuckUIBuilder.Spr("timer_ring_256"),
                                                 new Color(0.97f, 0.94f, 0.86f, 0.30f));
            ringImg.preserveAspect = true;
            touch.stickRing = ring;

            var knob = Pin("StickKnob", ring, 0.5f, 0.5f, 120f);
            var knobImg = DuckUIBuilder.AddImage(knob, DuckUIBuilder.Spr("button_256"),
                                                new Color(1f, 1f, 1f, 0.72f), Image.Type.Sliced);
            knobImg.preserveAspect = false;
            touch.stickKnob = knob;
        }

        static TouchControls.TouchButton Button(RectTransform root, TouchControls touch,
                                                TouchControls.Action action, string label,
                                                float x, float y, float size)
        {
            var rect = Pin(label, root, x, y, size);
            var idle = DuckUIBuilder.Spr("button_256");
            var pressed = DuckUIBuilder.Spr("button_pressed_256");
            var img = DuckUIBuilder.AddImage(rect, idle, new Color(1f, 1f, 1f, 0.88f), Image.Type.Sliced);

            // Inset from the plate's own 9-slice border so a four-letter word cannot ride up onto
            // the painted bevel, which is what made "BRAKE" look like it was falling off the top.
            var text = DuckUIBuilder.Frac("Label", rect, 0.12f, 0.20f, 0.88f, 0.80f);
            var t = DuckUIBuilder.AddText(text, label, size * 0.19f, TMPro.TextAlignmentOptions.Center,
                                          new Color(0.16f, 0.12f, 0.09f), 0.10f, false);
            t.fontStyle = TMPro.FontStyles.Bold;

            return new TouchControls.TouchButton
            {
                action = action,
                rect = rect,
                image = img,
                idle = idle,
                pressed = pressed
            };
        }

        /// <summary>
        /// A square child pinned to one point of its parent, sized in reference pixels.
        ///
        /// Deliberately NOT the fraction-anchored Frac the rest of the UI uses. A rectangle
        /// anchored by fractions of width and height is only square at one aspect ratio, and a
        /// round button that goes oval when the phone is a different shape looks broken. Anchoring
        /// a point and giving it a size keeps it square everywhere, and because the scaler matches
        /// height, that size is still a constant share of the screen.
        /// </summary>
        static RectTransform Pin(string name, Transform parent, float x, float y, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(x, y);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            return rt;
        }
    }
}
