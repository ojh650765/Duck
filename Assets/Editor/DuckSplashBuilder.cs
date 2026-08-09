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
        /// <summary>
        /// The roundel, not the masthead.
        ///
        /// Both exist and either would work, and the reason to pick this one is what a splash IS. A
        /// masthead is a title — it names the game, and the front page already does that a second
        /// later on a board built for it. A roundel is a STAMP, and a stamp is what goes on the front
        /// of a picture: it says who made this before the thing itself starts. Showing the title
        /// twice in three seconds makes the first one an advertisement for the second.
        ///
        /// It is also the shape that survives the format. A splash logo is centred on whatever the
        /// window happens to be, and a wordmark 1536 px wide either shrinks to unreadable on a narrow
        /// window or dominates a wide one. A disc is the same disc at every aspect.
        /// </summary>
        const string LogoPath = "Assets/Art/Textures/Title/title_splash.png";

        /// <summary>
        /// The cream the rest of the game's signage is painted on — DuckUIBuilder's Cream, to the
        /// value. The default background is a near-black that belongs to no scene in this project.
        ///
        /// It matters more for a roundel than it would for a wordmark. The mark's outer band is the
        /// game's brick red and it is a DISC on transparency, so the background is not behind the
        /// logo, it is the thing the logo is stamped on. On near-black a red ring reads as a warning
        /// light; on cream it reads as paint on a board, which is what everything else in this game
        /// is.
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
            var logo = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
            if (logo == null)
            {
                // A Texture rather than a Sprite is the one failure worth naming, because the file
                // is plainly there and the cast is what came back empty — which reads as a missing
                // asset and is not one.
                Debug.LogError($"[Duck] no Sprite at {LogoPath}. The file may be imported as a " +
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
                PlayerSettings.SplashScreenLogo.Create(LogoSeconds, logo)
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
                          "straight on the game. The roundel and the cream field are still " +
                          "configured underneath, so if a future licence forces the splash back on " +
                          "it comes back dressed correctly rather than as Unity's default.");
                return;
            }

            Debug.LogWarning(
                "[Duck] the Unity splash CANNOT be disabled on this licence — the request was made " +
                "and did not take. Falling back, which is the plan and not a fault: the splash now " +
                "shows the roundel for " + LogoSeconds + "s on the game's own cream field" +
                (engineLogo ? ", alongside the engine logo." : ", and the engine logo is off.") +
                " To remove it entirely, a Plus or Pro licence is the only lever; nothing in this " +
                "project can do it.");
        }
    }
}
