using UnityEditor;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Forces the player loop to keep ticking while the Unity editor is unfocused.
    ///
    /// This whole project is driven through the editor from outside, so the editor window is
    /// almost never focused and its Game View is almost never repainted. Unity then advances
    /// play mode by a single frame every few seconds, which makes every timing-dependent thing —
    /// the round clock, the autopilot, the capture rig — quietly do nothing. Pumping the player
    /// loop from EditorApplication.update (which does keep firing, since the MCP bridge lives on
    /// it) gives a steady frame rate regardless of focus.
    /// </summary>
    [InitializeOnLoad]
    public static class DuckPlayerLoopPump
    {
        const string PrefKey = "Duck.PumpPlayerLoop";
        const string MenuPath = "Duck/Play · Force player loop (unfocused)";

        static bool _enabled;

        static DuckPlayerLoopPump()
        {
            _enabled = EditorPrefs.GetBool(PrefKey, true);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem(MenuPath, priority = 27)]
        static void Toggle()
        {
            _enabled = !_enabled;
            EditorPrefs.SetBool(PrefKey, _enabled);
            Debug.Log($"[Duck] Forced player loop {(_enabled ? "ON" : "OFF")}");
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, _enabled);
            return true;
        }

        static void Tick()
        {
            if (!_enabled) return;
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            // One player loop iteration per editor tick. Unity coalesces repeat calls within a
            // single editor frame, so asking for more than one here would achieve nothing.
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
