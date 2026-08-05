using System;
using UnityEngine;

namespace DuckMow
{
    public enum JudgeBias { Accuracy, Coverage, Craft }

    [Serializable]
    public class JudgeProfile
    {
        public string displayName = "Judge";
        public string species = "goat";
        public JudgeBias bias = JudgeBias.Accuracy;
        public JudgeCharacter character;

        [Tooltip("Below 1 is generous, above 1 is harsh.")]
        public float severity = 1f;
        [Tooltip("Highest mark this judge will ever give.")]
        [Range(5f, 10f)] public float ceiling = 10f;
        [Tooltip("Lowest mark this judge will ever give.")]
        [Range(0f, 5f)] public float floor = 0f;
        public float deliberationSeconds = 1.5f;

        [TextArea] public string quipGreat = "";
        [TextArea] public string quipGood = "";
        [TextArea] public string quipOkay = "";
        [TextArea] public string quipBad = "";
    }

    /// <summary>
    /// The three animal judges. Each weighs the run differently, so the same picture gets three
    /// genuinely different marks and the player learns whose approval is worth chasing.
    /// The panel runs the verdict as a paced sequence rather than dumping three numbers at once —
    /// the wait between cards is the whole reason judging is fun.
    /// </summary>
    public class JudgePanel : MonoBehaviour
    {
        public JudgeProfile[] judges = new JudgeProfile[3];

        [Header("Pacing (seconds)")]
        public float leadIn = 0.7f;
        public float betweenJudges = 0.55f;
        public float holdAfterLast = 1.5f;

        public bool Finished { get; private set; } = true;
        /// <summary>Index of the judge currently on the clock. For the capture rig and diagnostics.</summary>
        public int CurrentJudge => _index;
        public float[] Scores { get; private set; } = new float[3];
        public float Total { get; private set; }
        public string Rank { get; private set; } = "C";

        public event Action<int, float, string> OnJudgeScored;   // index, 0-10, quip
        public event Action<float, string> OnVerdict;            // total 0-30, rank letter
        public event Action<int> OnJudgeStartsDeliberating;

        // The verdict runs as an explicit state machine rather than a coroutine so it can be
        // stepped deterministically by the simulation driver, paused, and replayed.
        enum Phase { Idle, LeanIn, LeadIn, Deliberate, RaiseCard, BetweenJudges, HoldFinal, Done }

        Phase _phase = Phase.Idle;
        float _phaseTime;
        int _index;

        public void ResetPanel()
        {
            _phase = Phase.Idle;
            _phaseTime = 0f;
            _index = 0;
            Finished = true;
            Total = 0f;
            for (int i = 0; i < Scores.Length; i++) Scores[i] = 0f;
            for (int i = 0; i < judges.Length; i++)
            {
                var c = judges[i]?.character;
                if (c == null) continue;
                c.Attention = 0f;
                c.CardUp = 0f;
                c.Reaction = 0f;
                c.SetCardNumber(0);
            }
        }

        public void BeginJudging(RoundScore score)
        {
            for (int i = 0; i < judges.Length; i++) Scores[i] = ScoreFor(judges[i], score);
            _phase = Phase.LeanIn;
            _phaseTime = 0f;
            _index = 0;
            Finished = false;
        }

        void Update()
        {
            if (SimClock.Scripted) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < judges.Length; i++) judges[i]?.character?.Tick(dt);

            if (_phase == Phase.Idle || _phase == Phase.Done) return;
            _phaseTime += dt;

            switch (_phase)
            {
                case Phase.LeanIn:
                {
                    const float dur = 0.45f;
                    float v = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_phaseTime / dur));
                    for (int i = 0; i < judges.Length; i++)
                        if (judges[i]?.character != null) judges[i].character.Attention = v;
                    if (_phaseTime >= dur) Advance(Phase.LeadIn);
                    break;
                }

                case Phase.LeadIn:
                    if (_phaseTime >= leadIn)
                    {
                        OnJudgeStartsDeliberating?.Invoke(_index);
                        Advance(Phase.Deliberate);
                    }
                    break;

                case Phase.Deliberate:
                {
                    var j = Current;
                    float wait = j != null ? j.deliberationSeconds : 1f;
                    if (_phaseTime >= wait)
                    {
                        j?.character?.SetCardNumber(Mathf.RoundToInt(Scores[_index]));
                        Advance(Phase.RaiseCard);
                    }
                    break;
                }

                case Phase.RaiseCard:
                {
                    const float dur = 0.32f;
                    var j = Current;
                    float v = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_phaseTime / dur));
                    if (j?.character != null) j.character.CardUp = v;
                    if (_phaseTime >= dur)
                    {
                        float norm = Mathf.InverseLerp(0f, 10f, Scores[_index]);
                        j?.character?.Punch(Mathf.Lerp(-0.5f, 1.0f, norm));
                        OnJudgeScored?.Invoke(_index, Scores[_index], QuipFor(j, norm));
                        Advance(Phase.BetweenJudges);
                    }
                    break;
                }

                case Phase.BetweenJudges:
                    if (_phaseTime >= betweenJudges)
                    {
                        _index++;
                        if (_index < judges.Length)
                        {
                            OnJudgeStartsDeliberating?.Invoke(_index);
                            Advance(Phase.Deliberate);
                        }
                        else
                        {
                            Total = 0f;
                            for (int i = 0; i < Scores.Length; i++) Total += Scores[i];
                            Rank = RankFor(Total);
                            Advance(Phase.HoldFinal);
                        }
                    }
                    break;

                case Phase.HoldFinal:
                    if (_phaseTime >= holdAfterLast)
                    {
                        OnVerdict?.Invoke(Total, Rank);
                        Finished = true;
                        Advance(Phase.Done);
                    }
                    break;
            }
        }

        JudgeProfile Current => (_index >= 0 && _index < judges.Length) ? judges[_index] : null;

        void Advance(Phase next) { _phase = next; _phaseTime = 0f; }

        /// <summary>
        /// Delegates to <see cref="Scoring.Mark"/>. The marking curve lives there because the
        /// rival stations across the venue have to mark by exactly the same rules as this bench.
        /// </summary>
        float ScoreFor(JudgeProfile j, RoundScore s)
            => j == null ? 0f : Scoring.Mark(s, j.bias, j.severity, j.floor, j.ceiling);

        static string QuipFor(JudgeProfile j, float norm)
        {
            if (norm >= 0.85f && !string.IsNullOrEmpty(j.quipGreat)) return j.quipGreat;
            if (norm >= 0.62f && !string.IsNullOrEmpty(j.quipGood)) return j.quipGood;
            if (norm >= 0.35f && !string.IsNullOrEmpty(j.quipOkay)) return j.quipOkay;
            return j.quipBad;
        }

        public static string RankFor(float total)
        {
            if (total >= 27f) return "S";
            if (total >= 23f) return "A";
            if (total >= 18f) return "B";
            if (total >= 12f) return "C";
            return "D";
        }

        /// <summary>Sensible defaults so the panel has personality before anything is authored.</summary>
        public void ApplyDefaultProfiles()
        {
            if (judges == null || judges.Length < 3) judges = new JudgeProfile[3];

            judges[0] ??= new JudgeProfile();
            judges[0].displayName = "MILDRED";
            judges[0].species = "goat";
            judges[0].bias = JudgeBias.Accuracy;
            judges[0].severity = 1.45f;
            judges[0].ceiling = 10f;
            judges[0].floor = 0f;
            judges[0].deliberationSeconds = 2.0f;
            judges[0].quipGreat = "...Acceptable. Barely. Well done.";
            judges[0].quipGood = "The left edge wandered. I noticed. I always notice.";
            judges[0].quipOkay = "It is a shape. I will grant you that much.";
            judges[0].quipBad = "I have seen a goat do better. I was that goat.";

            judges[1] ??= new JudgeProfile();
            judges[1].displayName = "BORIS";
            judges[1].species = "badger";
            judges[1].bias = JudgeBias.Coverage;
            judges[1].severity = 0.72f;
            judges[1].ceiling = 10f;
            judges[1].floor = 2f;
            judges[1].deliberationSeconds = 0.9f;
            judges[1].quipGreat = "MAGNIFICENT! I felt something in my chest!";
            judges[1].quipGood = "Loads of grass cut! Loads! Marvellous stuff!";
            judges[1].quipOkay = "Bold! Confusing! But bold!";
            judges[1].quipBad = "You tried. That is the main thing. Truly.";

            judges[2] ??= new JudgeProfile();
            judges[2].displayName = "PRISCILLA";
            judges[2].species = "heron";
            judges[2].bias = JudgeBias.Craft;
            judges[2].severity = 1.25f;
            judges[2].ceiling = 9f;
            judges[2].floor = 0f;
            judges[2].deliberationSeconds = 2.6f;
            judges[2].quipGreat = "I award nine. I have never awarded ten. Do not ask.";
            judges[2].quipGood = "Competent line work. Unremarkable, but competent.";
            judges[2].quipOkay = "The outline and I are no longer speaking.";
            judges[2].quipBad = "No.";
        }
    }
}
