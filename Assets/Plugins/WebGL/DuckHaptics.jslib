// -----------------------------------------------------------------------------------------------
// DuckHaptics.jslib — the browser's own rumble, because Unity's does not reach it.
//
// WHY THIS FILE EXISTS AT ALL. Unity's Input System cannot vibrate a gamepad in a WebGL build.
// That is documented in the exact package this project resolves — com.unity.inputsystem@1.18.0,
// Documentation~/SupportedDevices.md: "WebGL currently doesn't support rumble." — and it is visible
// in its source: WebGLGamepad.cs is a layout and an empty class body, with no ExecuteCommand
// override and nothing that answers a DualMotorRumbleCommand. Unity's WebGL gamepad support is
// emscripten_get_gamepad_status, which reads axes, buttons and mapping and offers no vibration API;
// Unity's own WebGL runtime JavaScript contains no reference to vibrationActuator anywhere. So
// Gamepad.SetMotorSpeeds in a browser sends a device command nothing handles, gets a failure back,
// and does nothing at all — silently, with no console warning. It works perfectly in the editor,
// which is precisely how a feature like this ships broken.
//
// The BROWSER can do it. The Gamepad API exposes gamepad.vibrationActuator with a "dual-rumble"
// effect that maps exactly onto the low/high motor pair the rest of the game already thinks in. So
// Haptics.cs goes around Unity to here.
//
// WHICH BROWSERS. Chromium — Chrome, Edge, Opera, Brave — on desktop implement vibrationActuator
// and will genuinely rumble. Firefox and Safari historically do not implement it at all, and there
// every function below returns without doing anything. That is not a fallback to be improved later;
// there is no other API to fall back to. DuckHapticsSupported exists so the C# side can tell the
// difference and a settings screen can say UNAVAILABLE rather than offering a switch that lies.
//
// THE THREE RULES THIS FILE OBEYS, all of them learned from how this API misbehaves:
//
//   1. NEVER THROW. An exception crossing back into wasm from a jslib function is not a caught
//      error, it is an aborted frame. Every function is wrapped, and every wrapper returns the
//      "cannot do it" answer rather than propagating. A missing rumble is nothing; a dead frame is
//      the game.
//
//   2. NEVER LEAVE A PROMISE UNHANDLED. playEffect returns a promise and it REJECTS routinely — a
//      page that is not focused, a pad disconnected mid-effect, an effect superseded by the next
//      call. An unhandled rejection prints a red error in the console on every occurrence, which in
//      a game issuing effects several times a second is a console nobody can read any more. Every
//      promise gets a swallowing catch.
//
//   3. RE-RESOLVE THE PAD EVERY CALL. navigator.getGamepads() returns a SNAPSHOT; the objects in it
//      are not live and a handle cached from an earlier frame is stale the moment the pad is
//      re-enumerated, which browsers do on connect, disconnect and tab focus. Holding one is how
//      you end up calling playEffect on a pad that is no longer there. The array allocation this
//      costs is real but small, and Haptics.cs already throttles these calls to roughly eight a
//      second for exactly this reason.
//
// WHICH pad: the first connected one that has an actuator. This is a single-player game and Unity's
// own Gamepad.current is very nearly always that same device — but the browser does not expose the
// index Unity chose, so the two cannot be tied together, and a player with two pads plugged in may
// feel the rumble in the wrong one. Recorded rather than worked around: fixing it properly means
// matching on the device's id string across two APIs that spell it differently, which is a great
// deal of guesswork to buy a case this game does not have.
// -----------------------------------------------------------------------------------------------

mergeInto(LibraryManager.library, {

    // Returns 1 when there is a connected pad this browser can actually drive, 0 otherwise.
    // Called about once a second by the C# side, not per frame — see Haptics.WebSupported.
    DuckHapticsSupported: function () {
        try {
            if (typeof navigator === "undefined" || !navigator.getGamepads) return 0;
            var pads = navigator.getGamepads();
            if (!pads) return 0;
            for (var i = 0; i < pads.length; i++) {
                var p = pads[i];
                if (!p || !p.connected) continue;
                var a = p.vibrationActuator;
                if (a && typeof a.playEffect === "function") return 1;
            }
            return 0;
        } catch (e) {
            return 0;
        }
    },

    // Start (or restart) a dual-rumble effect. low/high are 0..1 and map onto the strong and weak
    // magnitudes; durationMs is how long the browser should run it for if nobody refreshes it.
    //
    // The duration is a DEAD MAN'S SWITCH as much as a parameter. Unlike SetMotorSpeeds, which
    // latches until something clears it, an effect issued here stops on its own — so a build that
    // crashes, a tab that is closed, or a C# side that somehow stops ticking leaves the pad quiet a
    // fifth of a second later instead of buzzing until it is unplugged. Continuous rumble is built
    // by re-issuing before the previous effect expires, which is Haptics.DriveWeb's whole job.
    DuckHapticsPlay: function (low, high, durationMs) {
        try {
            if (typeof navigator === "undefined" || !navigator.getGamepads) return;
            var pads = navigator.getGamepads();
            if (!pads) return;

            var strong = low; if (!(strong >= 0)) strong = 0; if (strong > 1) strong = 1;
            var weak = high; if (!(weak >= 0)) weak = 0; if (weak > 1) weak = 1;
            var ms = durationMs; if (!(ms > 0)) ms = 0; if (ms > 2000) ms = 2000;

            for (var i = 0; i < pads.length; i++) {
                var p = pads[i];
                if (!p || !p.connected) continue;
                var a = p.vibrationActuator;
                if (!a || typeof a.playEffect !== "function") continue;

                // Older Chromium exposed canPlayEffectType; current builds expose an effects array.
                // Both are optional, so absence is not evidence of anything and only an explicit
                // "no" is honoured.
                if (typeof a.canPlayEffectType === "function" && !a.canPlayEffectType("dual-rumble")) continue;
                if (a.effects && a.effects.indexOf && a.effects.indexOf("dual-rumble") < 0) continue;

                var pr = a.playEffect("dual-rumble", {
                    startDelay: 0,
                    duration: ms,
                    strongMagnitude: strong,
                    weakMagnitude: weak
                });
                // Rule 2. This is not defensive style, it is required: rejections here are routine.
                if (pr && typeof pr.catch === "function") pr.catch(function () {});
                return;
            }
        } catch (e) {
            // Rule 1.
        }
    },

    // Silence, now, on every pad rather than on the one we happened to pick last time.
    //
    // EVERY pad deliberately: this is the recovery path, called on pause, on focus loss, on scene
    // change and on quit, and it may not assume the pad it is stopping is the pad it started. A
    // stop that misses is the single worst outcome this feature has.
    DuckHapticsStop: function () {
        try {
            if (typeof navigator === "undefined" || !navigator.getGamepads) return;
            var pads = navigator.getGamepads();
            if (!pads) return;
            for (var i = 0; i < pads.length; i++) {
                var p = pads[i];
                if (!p) continue;
                var a = p.vibrationActuator;
                if (!a) continue;
                // reset() is the spec's own "stop everything" and is what Chromium implements;
                // a zero-magnitude effect is the fallback for anything that has playEffect but not
                // reset. Both are attempted, because a stop is worth being redundant about.
                try {
                    if (typeof a.reset === "function") {
                        var r = a.reset();
                        if (r && typeof r.catch === "function") r.catch(function () {});
                    }
                } catch (e2) {}
                try {
                    if (typeof a.playEffect === "function") {
                        var pr = a.playEffect("dual-rumble", {
                            startDelay: 0,
                            duration: 0,
                            strongMagnitude: 0,
                            weakMagnitude: 0
                        });
                        if (pr && typeof pr.catch === "function") pr.catch(function () {});
                    }
                } catch (e3) {}
            }
        } catch (e) {
        }
    }
});
