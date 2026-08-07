using System.IO;
using UnityEditor;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Builds the asset <see cref="CurtainAudioSet"/> is loaded from, and fills it off disk.
    ///
    /// It lives under Resources for one reason and it is worth stating, because Resources is
    /// otherwise something this project avoids: the transition's clips have to be resident across a
    /// scene load, played by an object that belongs to no scene. Every other route — a field on a
    /// component, an addressable, an asset referenced by a scene — is a reference held by something
    /// the load destroys.
    ///
    /// Wired by NAME rather than by hand so a re-render of the audio pipeline cannot silently leave
    /// the game pointing at last week's whoosh, and so this is re-runnable after every render.
    /// </summary>
    public static class DuckCurtainAudio
    {
        const string Folder = "Assets/Resources";
        const string AssetPath = Folder + "/CurtainAudio.asset";
        const string ClipFolder = "Assets/Audio/Transition";

        [MenuItem("Duck/9 · Build transition audio set")]
        public static void Build()
        {
            if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);

            var set = AssetDatabase.LoadAssetAtPath<CurtainAudioSet>(AssetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<CurtainAudioSet>();
                AssetDatabase.CreateAsset(set, AssetPath);
            }

            int found = 0;
            set.leafSweep = Clip("transition_leaf_sweep", ref found);
            set.bannerWhoosh = Clip("transition_banner_whoosh", ref found);
            set.riser = Clip("transition_riser", ref found);
            set.impact = Clip("transition_impact", ref found);
            set.stampSmall = Clip("transition_stamp_small", ref found);
            set.fanfareBig = Clip("transition_fanfare_big", ref found);
            set.crowdSwell = Clip("transition_crowd_swell", ref found);
            set.gateCreak = Clip("transition_gate_creak", ref found);

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (found == 0)
                Debug.LogWarning($"[Curtain] no clips found under {ClipFolder}. Render them with " +
                                 "'python Art/Python/audio/render_all.py transition', then run this again.");
            else
                Debug.Log($"[Curtain] transition audio set built: {found}/8 clips wired at {AssetPath}.");
        }

        static AudioClip Clip(string name, ref int found)
        {
            // The pipeline writes .wav; the importer may have been pointed elsewhere since. Asking
            // the database by name rather than by extension survives both.
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:AudioClip", new[] { ClipFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != name) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                Configure(path);
                found++;
                return clip;
            }
            return null;
        }

        /// <summary>
        /// Transition clips are short, played at unpredictable moments, and must not stall.
        ///
        /// DecompressOnLoad rather than the project default, because a compressed clip asked for on
        /// the frame a scene changes is a decode on the worst frame available — which shows up as
        /// exactly the hitch the whole curtain exists to hide. They are small enough that holding
        /// them uncompressed in memory is free.
        /// </summary>
        static void Configure(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;

            var settings = importer.defaultSampleSettings;
            bool changed = false;
            if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
            { settings.loadType = AudioClipLoadType.DecompressOnLoad; changed = true; }
            if (settings.compressionFormat != AudioCompressionFormat.Vorbis)
            { settings.compressionFormat = AudioCompressionFormat.Vorbis; changed = true; }
            if (!settings.preloadAudioData) { settings.preloadAudioData = true; changed = true; }
            // Not deferred to a background thread: a clip still loading when the curtain closes is a
            // silent transition, which is the one thing worse than a loud one.
            if (importer.loadInBackground) { importer.loadInBackground = false; changed = true; }

            if (!changed) return;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
    }
}
