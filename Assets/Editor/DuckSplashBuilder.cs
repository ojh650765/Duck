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

            // The splash is ON, and the engine logo is off.
            //
            // The first pass turned the whole splash OFF, because the licence allows it and that is
            // the cleanest boot a game can have. It was also the wrong answer to what was asked: the
            // owner wanted their own mark on the front of the build, and a splash that does not run
            // shows no mark at all. A logo configured under a disabled splash is a logo nobody sees.
            //
            // So: run the splash, and ask for the engine's logo to be dropped from it. That request
            // is the licence question — on a licence that refuses it the card still shows, beside
            // the engine logo rather than instead of it, which is a fair outcome and not a failure.
            // Nothing below trusts the assignment; it reads back what survived.
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            AssetDatabase.SaveAssets();

            // READ BACK, do not assume. Unity clamps these to what the licence permits at the
            // moment of assignment, so the only honest report is the one taken afterwards.
            bool splashOn = PlayerSettings.SplashScreen.show;
            bool engineLogo = PlayerSettings.SplashScreen.showUnityLogo;

            if (!splashOn)
            {
                // Should not happen — the splash was asked to RUN. If Unity has refused to turn it
                // on, the card will not be seen and the build opens on the game with no mark at all,
                // which is precisely the outcome this builder exists to avoid. Loud, because it is
                // silent on screen.
                Debug.LogError("[Duck] the splash was asked to run and came back OFF. The card is " +
                               "configured but nothing will show it. Check Player Settings > Splash " +
                               "Image; something outside this builder is switching it off.");
                return;
            }

            if (!engineLogo)
            {
                Debug.Log($"[Duck] splash: POND & GREEN for {LogoSeconds}s on the game's own cream " +
                          "field, and the engine logo is off. This licence allows dropping it, so " +
                          "the card is the only thing on the front of the build.");
                return;
            }

            Debug.LogWarning(
                "[Duck] the engine logo CANNOT be dropped on this licence — the request was made and " +
                $"did not take. Not a failure: the splash shows POND & GREEN for {LogoSeconds}s on " +
                "the game's own cream field, alongside Unity's logo rather than instead of it. " +
                "Removing it needs a Plus or Pro licence; nothing in this project can do it.");
        }
    }
}
