using UnityEngine;

namespace DuckMow.UI
{
    /// <summary>
    /// WHERE ALONG A VOLUME BAR THE POINTER IS.
    ///
    /// Six lines, and they are here rather than in either menu for the same reason the curve moved
    /// to <see cref="MasterAudio"/>: there are two volume bars now, and the thing they have to agree
    /// about is not a number, it is a DECISION. Two screens that map a drag differently do not look
    /// different — they feel different, and only at the ends, which is the hardest kind of
    /// inconsistency to report and the easiest to dismiss.
    ///
    /// ---- the decision, which is the whole content of this file ----
    ///
    /// THE POINTER IS MAPPED THROUGH THE FILL'S RECT, NOT THE TROUGH'S.
    ///
    /// Every bar in this game is drawn as a fill sitting INSIDE a trough, inset by the trough's own
    /// rim — the painted one on the front page, a couple of generated pixels on the pause board.
    /// The player aims at the drawn end of the FILL, because that is the thing that moves and the
    /// thing they are trying to line up. Map against the trough and the grab point sits a rim's
    /// width outside the fill at both ends, so the bar reaches 100% slightly before the pointer does
    /// and stops slightly after — which presents as 100% being unreachable, and is the exact fault
    /// MainMenu's own note records having fixed.
    ///
    /// The INSET is per artwork and stays with each board. The rule about which rect to measure
    /// against is not, and it is the part that was worth lifting out.
    ///
    /// ---- and the guard, which is not paranoia ----
    ///
    /// A degenerate rect is a division by zero, and the NaN does not stop there: it travels through
    /// the curve into MasterAudio.Master, whose own note spells out where it ends up — Clamp01
    /// passes NaN straight through, AudioListener.volume takes it, and the whole game goes silent
    /// with nothing in the console. Caught here as well as there on purpose; this is the end that
    /// knows why the number would be bad.
    /// </summary>
    public static class VolumeDial
    {
        /// <summary>
        /// How far along <paramref name="fill"/> the screen point <paramref name="screen"/> falls,
        /// 0 at its left edge and 1 at its right. False when the question cannot be answered, in
        /// which case <paramref name="position"/> is untouched and the caller must not move anything.
        ///
        /// <paramref name="camera"/> is the canvas's own camera, and NULL for a ScreenSpaceOverlay
        /// canvas — for an overlay the screen point IS the canvas point, and passing a camera
        /// silently returns a local point that is wrong everywhere on screen.
        /// </summary>
        public static bool PositionAt(RectTransform fill, Vector2 screen, Camera camera,
                                      out float position)
        {
            position = 0f;
            if (fill == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(fill, screen, camera,
                                                                        out Vector2 local))
                return false;

            Rect r = fill.rect;
            if (r.width <= 1e-3f) return false;

            position = Mathf.Clamp01((local.x - r.xMin) / r.width);
            return true;
        }
    }
}
