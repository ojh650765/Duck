using UnityEditor;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// The first thing anybody sees, dressed as this game rather than as an engine.
    ///
    /// ---- why this is a builder and not a trip to the inspector ----
    ///
    /// Every splash setting lives in ProjectSettings.asset, which no scene builder writes and
    /// nobody diffs. This project has now been bitten three times by exactly that shape — the
    /// opening story sat at an eight-second skip delay through two edits of its default because no
    /// builder wrote the field; masterEngine was baked into four scenes at a value the default
    /// disagreed with; bloomEnabled was hand-edited to 1 while its default was false, so anyone who
    /// rebuilt the scene silently lost stage three. A setting a human has to remember to set is a
    /// setting that comes back wrong. So it is written here, from one place, and re-running this is
    /// how it gets fixed if it ever drifts.
    ///
    /// ---- what is actually permitted ----
    ///
    /// Turning the Unity splash off is a LICENCE question, not a code one, and the answer has
    /// changed: it used to be Plus/Pro only, and Unity 6 made it optional on Personal as well. This
    /// asks for it and then READS BACK what it got, because a build that quietly kept the engine
    /// logo while the console said otherwise is worse than being told no. See the log at the end —
    /// it reports the state that survived, not the state that was requested.
    ///
    /// The fallback is not a failure. With the engine logo mandatory, the game's own board still
    /// goes up beside it and the field behind both stops being Unity's near-black.
    /// </summary>
    public static class DuckSplashBuilder
    {
        const string MastheadPath = "Assets/Art/Textures/Title/title_masthead.png";

        /// <summary>
        /// The cream the rest of the game's signage is painted on — DuckUIBuilder's Cream, to the
        /// value. The default background is a near-black that belongs to no scene in this project,
        /// and the masthead is a red letterform with a white keyline drawn for a pale board: on
        /// near-black the keyline reads as a halo round the letters rather than as paint.
        /// </summary>
        static readonly Color Field = new Color(0.97f, 0.94f, 0.86f, 1f);

        /// <summary>
        /// Two seconds, which is Unity's own floor for a logo and long enough to read three words.
        /// Asking for less is silently raised, so it is written as the number it will actually be.
        /// </summary>
        const float LogoSeconds = 2f;

        [MenuItem("Duck/9 · Build splash screen", priority = 90)]
        public static void Build()
        {
            var masthead = AssetDatabase.LoadAssetAtPath<Sprite>(MastheadPath);
            if (masthead == null)
            {
                // A Texture rather than a Sprite is the one failure worth naming, because the file
                // is plainly there and the cast is what came back empty — which reads as a missing
                // asset and is not one.
                Debug.LogError($"[Duck] no Sprite at {MastheadPath}. The file may be imported as a " +
                               "plain Texture; the splash logo list takes Sprites only. Set its " +
                               "Texture Type to 'Sprite (2D and UI)' and run this again.");
                return;
            }

            PlayerSettings.SplashScreen.backgroundColor = Field;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Static;
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.DarkOnLight;
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
            PlayerSettings.SplashScreen.logos = new[]
            {
                PlayerSettings.SplashScreenLogo.Create(LogoSeconds, masthead)
            };

            // Ask for the engine logo to go. On a licence that does not allow it this is ignored,
            // which is why nothing below trusts it.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            AssetDatabase.SaveAssets();

            // READ BACK, do not assume. Unity clamps these to what the licence permits at the
            // moment of assignment, so the only honest report is the one taken afterwards.
            bool splashOn = PlayerSettings.SplashScreen.show;
            bool engineLogo = PlayerSettings.SplashScreen.showUnityLogo;

            if (!splashOn)
            {
                Debug.Log("[Duck] splash screen OFF. This licence allows it, so the build opens " +
                          "straight on the game. The masthead logo and the cream field are still " +
                          "configured underneath, so if a future licence forces the splash back on " +
                          "it comes back dressed correctly rather than as Unity's default.");
                return;
            }

            Debug.LogWarning(
                "[Duck] the Unity splash CANNOT be disabled on this licence — the request was made " +
                "and did not take. Falling back, which is the plan and not a fault: the splash now " +
                "shows DUCK MOW for " + LogoSeconds + "s on the game's own cream field" +
                (engineLogo ? ", alongside the engine logo." : ", and the engine logo is off.") +
                " To remove it entirely, a Plus or Pro licence is the only lever; nothing in this " +
                "project can do it.");
        }
    }
}
