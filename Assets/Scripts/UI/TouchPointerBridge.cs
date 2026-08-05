using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DuckMow
{
    /// <summary>
    /// Makes a finger act as the mouse, so the screens that are clicked can be tapped.
    ///
    /// This exists because of one hard blocker on a phone: the front page. MainMenu polls
    /// Mouse.current for its highlight and its click, deliberately — no EventSystem, no input
    /// module, no Button components, all of which is the right call for a WebGL build. But a phone
    /// has no mouse, and mobile browsers only sometimes emit compatibility mouse events after a tap
    /// (and stop doing so as soon as a page declares touch-action: none, which the template now
    /// does so that a drag on the canvas is not read as a page scroll). Without this bridge, the
    /// game on a phone opens on a menu whose items cannot be selected — everything downstream of
    /// it is unreachable, and no amount of in-game touch control fixes that.
    ///
    /// Mirroring onto the Mouse device rather than teaching each screen about Touchscreen is
    /// deliberate: it is one file, it needs no change to any screen that is already correct for a
    /// pointer, and any future clickable UI gets it for free. The cost is that it is indirection —
    /// if MainMenu ever reads Touchscreen itself, this should be deleted rather than left as a
    /// second path to the same place.
    ///
    /// Installed from a RuntimeInitializeOnLoadMethod rather than from a scene, because the scene
    /// that needs it most is built by DuckMenuBuilder and this has to be true of every scene,
    /// including any built later by someone who has never heard of it.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class TouchPointerBridge : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("~ Touch Pointer Bridge") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<TouchPointerBridge>();
        }

        static TouchPointerBridge _instance;

        Mouse _mouse;
        Vector2 _last;
        bool _wasDown;

        void Update()
        {
            var ts = Touchscreen.current;
            if (ts == null) return;                     // no touchscreen: nothing to mirror, ever

            var touch = ts.primaryTouch;
            bool down = touch.press.isPressed;
            if (!down && !_wasDown) return;             // idle: leave the real mouse alone

            // Created on demand, and only once a finger has actually been seen. A phone browser
            // has no Mouse device at all, so there is nothing to write to until we add one — and
            // adding one unconditionally would give every desktop build a second phantom mouse.
            if (_mouse == null)
            {
                _mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
                if (_mouse == null) return;
            }

            Vector2 pos = touch.position.ReadValue();

            // Position and button in ONE state event. MainMenu takes its highlight from the pointer
            // position and its activation from the click in the same frame, so a tap that delivered
            // the button press a frame before the position would activate whatever was under the
            // last touch instead of this one.
            var state = new MouseState { position = pos, delta = pos - _last };
            state = state.WithButton(MouseButton.Left, down);
            InputSystem.QueueStateEvent(_mouse, state);

            _last = pos;
            _wasDown = down;
        }
    }
}
