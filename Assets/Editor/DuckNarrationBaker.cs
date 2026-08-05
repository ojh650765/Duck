using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace DuckMow.EditorTools
{
    /// <summary>
    /// Renders the opening story's narration to speech with Typecast's API and drops the WAVs where
    /// <see cref="DuckCutsceneBuilder"/> will find them.
    ///
    /// The lines are not authored here. They are read straight out of the panel table via
    /// <see cref="DuckCutsceneBuilder.NarrationTable"/>, because a copy of the script in a second
    /// file is a copy that will be edited in one place only — and the two lines carrying "$10,000"
    /// are the ones the whole story turns on. Rendering the table means the voice cannot say
    /// something the band does not show.
    ///
    /// It is a bake and not a runtime call. Nothing in the shipped game talks to Typecast: the
    /// request happens once in the editor, the result is an asset, and the WebGL build contains
    /// audio files like any other. That also means the game has no API key in it, which is the only
    /// acceptable arrangement for a browser build — anything shipped to a browser is public.
    /// </summary>
    public static class DuckNarrationBaker
    {
        const string Endpoint = "https://api.typecast.ai/v1/text-to-speech";
        const string VoicesEndpoint = "https://api.typecast.ai/v2/voices";

        // Read from the environment first so a key never has to touch the disk. The file is the
        // fallback for a GUI-launched editor, which does not inherit a shell's exports on Windows.
        const string KeyEnvVar = "TYPECAST_API_KEY";
        const string KeyFileRelative = ".secrets/typecast.key";

        // ------------------------------------------------------------------ the voice

        // Which voice reads the story. This is a real decision and it is unverified: it is the id
        // Typecast's own documentation uses in its example, kept here so the bake runs the first time
        // rather than refusing until somebody picks. Run "Duck/Diagnose · List Typecast voices",
        // choose one — a low, unhurried, storybook read, not an announcer — and paste it here.
        const string VoiceId = "tc_60e5426de8b95f1d3000d7b5";

        // ssfm-v30 is the current model and the only one whose "smart" prompt exists, which is what
        // this sequence wants: see BuildRequest.
        const string Model = "ssfm-v30";
        const string Language = "eng";

        // Slightly under speed. These are storybook lines with full stops in the middle of them and
        // the default read pushes through the pauses; the page's whole pacing assumes the narrator
        // is in no hurry. This is the first number to change if the reading feels wrong.
        const float Tempo = 0.94f;

        // Loudness rather than volume, because the thirteen lines are separate requests and would
        // otherwise land at thirteen slightly different levels — which on a band that is the only
        // text in the sequence reads as the narrator moving toward and away from the microphone.
        // -16 LUFS is a normal speech target; it is NOT measured against this game's mix, so if the
        // narration sits wrong against the panel stings, change it here and re-bake rather than
        // reaching for ComicSequence.narrationVolume, which moves all thirteen together.
        const float TargetLufs = -16f;

        // Fixed, so a re-bake of an unchanged line is the same audio. The audio spec's whole
        // verification story rests on renders being reproducible, and a TTS call that returns a
        // different reading every time would make "did the timing change?" unanswerable.
        const int SeedBase = 20260805;

        // ------------------------------------------------------------------ menu

        [MenuItem("Duck/5 · Bake cutscene narration (Typecast)", priority = 4)]
        public static void Bake() => Run(force: false);

        [MenuItem("Duck/5 · Re-bake cutscene narration (overwrite)", priority = 4)]
        public static void Rebake() => Run(force: true);

        [MenuItem("Duck/Diagnose · List Typecast voices", priority = 55)]
        public static void ListVoices()
        {
            if (!TryGetKey(out string key)) return;
            try
            {
                string json = Encoding.UTF8.GetString(Get(VoicesEndpoint, key));
                foreach (string line in SummariseVoices(json)) Debug.Log(line);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Duck] could not list Typecast voices: {Explain(e)}");
            }
        }

        // ------------------------------------------------------------------ the bake

        static void Run(bool force)
        {
            if (!TryGetKey(out string key)) return;

            var table = DuckCutsceneBuilder.NarrationTable();
            EnsureFolder(DuckCutsceneBuilder.NarrationFolder);

            int written = 0, skipped = 0, failed = 0;
            try
            {
                // Counted up front only so the progress bar is honest. A bake of thirteen lines takes
                // long enough that a frozen editor with no bar looks like a hang, and the first
                // instinct on a hung editor is to kill it — halfway through a set of paid requests.
                int total = 0;
                foreach (var lines in table) total += lines.Length;

                int done = 0;
                for (int i = 0; i < table.Length; i++)
                {
                    for (int k = 0; k < table[i].Length; k++)
                    {
                        string text = table[i][k];
                        string path = DuckCutsceneBuilder.NarrationPath(i, k);
                        done++;

                        if (string.IsNullOrWhiteSpace(text)) continue;
                        if (!force && File.Exists(path)) { skipped++; continue; }

                        EditorUtility.DisplayProgressBar("Baking narration",
                            $"{done}/{total}  {text}", (float)done / Mathf.Max(total, 1));

                        try
                        {
                            byte[] wav = Post(Endpoint, key,
                                BuildRequest(text, Neighbour(table, i, k, -1),
                                                   Neighbour(table, i, k, +1), i * 31 + k));
                            File.WriteAllBytes(path, wav);
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                            written++;
                        }
                        catch (Exception e)
                        {
                            // Named per line rather than aborting the run: a 422 on one sentence is
                            // a sentence problem, and thirteen paid requests should not be thrown
                            // away because the eleventh had a character the model dislikes.
                            failed++;
                            Debug.LogError($"[Duck] narration line {i + 1}.{k + 1} failed " +
                                           $"(\"{text}\"): {Explain(e)}");
                        }

                        // Serial and unhurried. The free tier allows two concurrent requests; there
                        // is nothing to gain from parallelising thirteen short calls and a 429
                        // partway through a bake costs real credit to recover from.
                        Thread.Sleep(150);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (written > 0) DuckCutsceneBuilder.ImportNarration();

            Debug.Log($"[Duck] narration bake: {written} written, {skipped} already present, " +
                      $"{failed} failed → {DuckCutsceneBuilder.NarrationFolder}. " +
                      "Run Duck/3 to re-time the page around them.");
        }

        /// <summary>The line before or after this one, across panel boundaries. "" at the ends.</summary>
        static string Neighbour(string[][] table, int panel, int line, int step)
        {
            int k = line + step;
            if (k >= 0 && k < table[panel].Length) return table[panel][k] ?? "";

            int p = panel + step;
            while (p >= 0 && p < table.Length)
            {
                if (table[p].Length > 0)
                    return (step > 0 ? table[p][0] : table[p][table[p].Length - 1]) ?? "";
                p += step;
            }
            return "";
        }

        /// <summary>
        /// One request body.
        ///
        /// The prompt is "smart" with the surrounding lines attached rather than a preset emotion,
        /// and that is the whole reason this sequence is worth baking with a model that has it. A
        /// preset would need an emotional label per line — the story runs contentment, threat,
        /// despair, greed, resolve — and thirteen hand-picked labels is thirteen guesses nobody can
        /// check without listening. Handing over the previous and next sentence instead lets the
        /// reading carry its own continuity, which is exactly what makes a narrator sound like one
        /// person telling one story rather than thirteen separate takes.
        ///
        /// Written by hand rather than through JsonUtility: the body has a discriminated union in it
        /// (emotion_type) and nested objects with names that are not legal C# fields, and modelling
        /// that as serialisable classes is more code than the four lines it replaces.
        /// </summary>
        static string BuildRequest(string text, string previous, string next, int seedOffset)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"voice_id\":\"").Append(VoiceId).Append("\",");
            sb.Append("\"model\":\"").Append(Model).Append("\",");
            sb.Append("\"language\":\"").Append(Language).Append("\",");
            sb.Append("\"text\":").Append(Quote(text)).Append(',');
            sb.Append("\"seed\":").Append(SeedBase + seedOffset).Append(',');
            sb.Append("\"prompt\":{\"emotion_type\":\"smart\"");
            if (!string.IsNullOrEmpty(previous)) sb.Append(",\"previous_text\":").Append(Quote(previous));
            if (!string.IsNullOrEmpty(next)) sb.Append(",\"next_text\":").Append(Quote(next));
            sb.Append("},");
            // WAV and not MP3. The clip is re-encoded on import anyway, so an MP3 here would be a
            // lossy generation the build's own encoder then has to compress again.
            sb.Append("\"output\":{\"audio_format\":\"wav\",\"audio_tempo\":")
              .Append(Tempo.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"target_lufs\":")
              .Append(TargetLufs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
              .Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            sb.Append('"');
            foreach (char c in s)
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            sb.Append('"');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the key

        /// <summary>
        /// Find the key, or say exactly where to put one.
        ///
        /// Environment variable first and a git-ignored file second, and never a field on a component
        /// or a const in this file: this repository is public enough to be published to GitHub Pages
        /// by its own build step, and a key committed once is a key that has to be rotated. The
        /// failure message is deliberately long — the whole pipeline is finished and waiting on this
        /// one thing, and "no key" with no address is how that gets misread as "not implemented".
        /// </summary>
        static bool TryGetKey(out string key)
        {
            key = Environment.GetEnvironmentVariable(KeyEnvVar);
            if (!string.IsNullOrWhiteSpace(key)) { key = key.Trim(); return true; }

            string path = KeyFilePath();
            if (File.Exists(path))
            {
                key = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(key)) return true;
            }

            Debug.LogError(
                "[Duck] no Typecast API key, so the narration cannot be baked. Everything else is " +
                "in place — the cutscene plays silently until the clips exist. Supply the key one " +
                $"of two ways:\n" +
                $"  1. set the {KeyEnvVar} environment variable and restart Unity, or\n" +
                $"  2. put the key, on its own, in {path}\n" +
                "The second path is git-ignored. Do not paste the key into a script or a prefab.");
            key = null;
            return false;
        }

        static string KeyFilePath()
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(root, KeyFileRelative.Replace('/', Path.DirectorySeparatorChar));
        }

        // ------------------------------------------------------------------ http

        // HttpWebRequest and not UnityWebRequest or HttpClient. UnityWebRequest completes on the
        // editor's update loop, so a blocking wait for one inside a menu item deadlocks; HttpClient
        // posts its continuations to the captured synchronisation context, so .Result on the main
        // thread deadlocks the same way. A synchronous HttpWebRequest is the one shape that is simply
        // done when it returns, which is what a bake wants.

        static byte[] Post(string url, string key, string body)
        {
            var req = Make(url, key);
            req.Method = "POST";
            req.ContentType = "application/json";
            byte[] payload = Encoding.UTF8.GetBytes(body);
            req.ContentLength = payload.Length;
            using (var s = req.GetRequestStream()) s.Write(payload, 0, payload.Length);
            return Read(req);
        }

        static byte[] Get(string url, string key)
        {
            var req = Make(url, key);
            req.Method = "GET";
            return Read(req);
        }

        static HttpWebRequest Make(string url, string key)
        {
            // Mono's default protocol list on some editor installs still offers SSL3/TLS1.0, which
            // this endpoint refuses. Set explicitly rather than debug a handshake error later.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Headers["X-API-KEY"] = key;
            req.Accept = "*/*";
            req.Timeout = 120000;             // synthesis of a whole sentence, not a status ping
            req.ReadWriteTimeout = 120000;
            return req;
        }

        static byte[] Read(HttpWebRequest req)
        {
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var mem = new MemoryStream())
            {
                if (stream == null) throw new IOException("empty response");
                stream.CopyTo(mem);
                return mem.ToArray();
            }
        }

        /// <summary>
        /// Turn a WebException into something actionable.
        ///
        /// The API answers failures with a JSON body and the status code alone does not say which
        /// failure it was — 422 in particular covers everything from an unspeakable string to a
        /// language mismatch. Without reading the body a bad voice_id and an empty wallet look
        /// identical from here.
        /// </summary>
        static string Explain(Exception e)
        {
            if (!(e is WebException we) || we.Response == null) return e.Message;
            var resp = (HttpWebResponse)we.Response;
            string body = "";
            try
            {
                using (var s = resp.GetResponseStream())
                using (var r = new StreamReader(s ?? Stream.Null))
                    body = r.ReadToEnd();
            }
            catch { /* the status code is still worth reporting */ }

            string hint = (int)resp.StatusCode switch
            {
                401 => "  → the key was rejected; check it is the whole key and has not been rotated.",
                402 => "  → out of credit on the Typecast account.",
                400 => $"  → most likely the voice_id: {VoiceId} is the documented example and may " +
                       "not exist on this account. Run Duck/Diagnose · List Typecast voices.",
                404 => $"  → voice not found: {VoiceId}. Run Duck/Diagnose · List Typecast voices.",
                429 => "  → rate limited; bake again, it skips what is already on disk.",
                _ => ""
            };
            return $"HTTP {(int)resp.StatusCode} {resp.StatusCode}. {body}\n{hint}";
        }

        // ------------------------------------------------------------------ voices

        [Serializable] class VoiceList { public Voice[] items; }
        [Serializable]
        class Voice
        {
            public string voice_id;
            public string voice_name;
            public string gender;
            public string age;
        }

        /// <summary>
        /// One log line per voice. The endpoint returns a bare JSON array, which JsonUtility cannot
        /// parse at the top level, so it is wrapped first.
        /// </summary>
        static IEnumerable<string> SummariseVoices(string json)
        {
            string trimmed = json.Trim();
            int open = trimmed.IndexOf('[');
            int close = trimmed.LastIndexOf(']');
            if (open < 0 || close <= open)
            {
                yield return $"[Duck] Typecast voices, unrecognised response: {trimmed}";
                yield break;
            }

            VoiceList list = null;
            try { list = JsonUtility.FromJson<VoiceList>("{\"items\":" + trimmed.Substring(open, close - open + 1) + "}"); }
            catch { /* falls through to the raw dump below */ }

            if (list?.items == null || list.items.Length == 0)
            {
                yield return $"[Duck] Typecast voices, could not read the list: {trimmed}";
                yield break;
            }

            yield return $"[Duck] {list.items.Length} Typecast voice(s). Paste one id into " +
                         "DuckNarrationBaker.VoiceId.";
            foreach (var v in list.items)
                yield return $"    {v.voice_id}  {v.voice_name}  ({v.gender}, {v.age})";
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
