using UnityEngine;

namespace DuckMow
{
    /// <summary>
    /// The end of the championship, played out rather than printed.
    ///
    /// The game already had a scoreboard, and a scoreboard is not a payoff — it is a receipt. This
    /// is the sequence that makes the last round feel like it was worth mowing: the bench reacts,
    /// something is shown that only the champion gets shown, and only then does a title card land.
    /// Three beats, every one of them a shot the camera rig can already frame properly:
    ///
    ///     BENCH    the three judges, applauding or shaking their heads, with the crowd behind them
    ///     PRIZE    a push in on the trophy on its plinth — or, on a defeat, a flight over the lawn
    ///              of whoever beat you, which is the same beat pointed at someone else
    ///     PORTRAIT the duck itself, held, under the title card
    ///
    /// A plain class, driven by <see cref="GameDirector"/>'s Ceremony state one Tick at a time. It
    /// is not a MonoBehaviour and not a coroutine for the same reason the judging panel is neither:
    /// the capture harness steps the game on a scripted clock, and a sequence that only advances on
    /// real frames cannot be photographed.
    /// </summary>
    public class VictoryCeremony
    {
        /// <summary>Seconds held on the judges before the prize beat.</summary>
        public float benchBeat = 3.0f;
        /// <summary>Seconds on the trophy, or in flight over the winner's lawn.</summary>
        public float prizeBeat = 3.6f;
        /// <summary>Seconds on the champion with the final round's card beside them.</summary>
        public float championBeat = 3.4f;
        /// <summary>Seconds on the board while the championship total is applied.</summary>
        public float boardBeat = 4.2f;
        /// <summary>Seconds the total takes to count from where it stood to where it finished.</summary>
        public float countSeconds = 1.5f;
        /// <summary>Seconds after the title card lands before the way onward is offered.</summary>
        public float promptDelay = 1.6f;

        public bool PlayerWon { get; private set; }
        /// <summary>True once the lower-third title card should be on screen.</summary>
        public bool CardUp { get; private set; }
        /// <summary>True once the player may leave. Held back so the card is read before it is dismissed.</summary>
        public bool PromptUp { get; private set; }

        enum Beat { Bench, Prize, Champion, Board, Portrait }

        Beat _beat;
        float _time;
        float _crowdTimer;
        JudgePanel _judges;
        CameraDirector _camera;
        SpectatorCrowd[] _crowds;
        int _reacted;
        PlotSpec _championPlot;
        Championship _championship;
        HUD _hud;
        CeremonyNight _night;
        Scoreboard _board;
        float _countTimer;

        /// <summary>
        /// Take over the screen. Everything the sequence needs is resolved here rather than kept as
        /// a serialized reference, because this object is created at the moment it is first used and
        /// there is nothing in the scene to hang it on.
        /// </summary>
        public void Begin(Championship championship, JudgePanel judges, CameraDirector camera)
        {
            PlayerWon = championship != null && championship.PlayerIsChampion;
            _championship = championship;
            _judges = judges;
            _camera = camera;
            _beat = Beat.Bench;
            _time = 0f;
            _crowdTimer = 0f;
            _reacted = 0;
            _countTimer = 0f;
            CardUp = false;
            PromptUp = false;
            _hud = Object.FindFirstObjectByType<HUD>();
            _night = CeremonyNight.Ensure();

            // The seats are found once, here. There is no singleton for the crowd and nothing has
            // ever asked it to react before — Excite existed and was never called from anywhere, so
            // the stands have been sitting through every result in the game completely still.
            _crowds = Object.FindObjectsByType<SpectatorCrowd>(FindObjectsSortMode.None);

            _championPlot = championship != null
                ? Venue.PlotOf(championship.Leader.name)
                : Venue.Player;

            // A cut to the bench, not a blend: the shot on screen is the plaza board, sixty metres
            // away and facing the other way, and blending between them spends two seconds sailing
            // over a hedge. The judging beat cuts here for exactly the same reason.
            if (_camera != null)
            {
                _camera.SetJudgeFocus(null);
                _camera.SetMode(CameraMode.Judges, 0f);
                _camera.SnapToCurrent();
            }
        }

        public void Tick(float dt)
        {
            _time += dt;
            _night?.Tick(dt);

            switch (_beat)
            {
                case Beat.Bench:
                    // The three of them react one after another rather than together. A bench that
                    // applauds in unison is one puppet with three heads; a stagger is a panel.
                    ReactInTurn(0.45f);
                    Swell(dt, PlayerWon ? 1.1f : 0f);
                    if (_time >= benchBeat) EnterPrize();
                    break;

                case Beat.Prize:
                    Swell(dt, PlayerWon ? 1.4f : 0f);
                    if (_time >= prizeBeat) EnterChampion();
                    break;

                case Beat.Champion:
                    Swell(dt, PlayerWon ? 1.6f : 0.2f);
                    if (_time >= championBeat) EnterBoard();
                    break;

                case Beat.Board:
                    CountUp(dt);
                    Swell(dt, PlayerWon ? 1.6f : 0.2f);
                    if (_time >= boardBeat) EnterPortrait();
                    break;

                case Beat.Portrait:
                    Swell(dt, PlayerWon ? 2.0f : 0f);
                    if (_time >= promptDelay) PromptUp = true;
                    break;
            }
        }

        /// <summary>
        /// The champion, framed right, with the FINAL ROUND's card on the left.
        ///
        /// Only the last round. The championship total gets its own beat on the board a moment later,
        /// and showing a running total here would answer the board's question before it is asked —
        /// the point of the two shots is that you see what you just earned, then watch it land.
        /// </summary>
        void EnterChampion()
        {
            _beat = Beat.Champion;
            _time = 0f;

            if (_camera != null)
            {
                _camera.SetMode(CameraMode.Verdict, 0.9f);
            }

            if (_hud == null || _championship == null) return;
            _hud.ceremonyResultsHold = true;
            _hud.ShowFinalRound(_championship.LastRoundPlace, _championship.LastRoundPoints);
        }

        void EnterBoard()
        {
            _beat = Beat.Board;
            _time = 0f;
            _countTimer = 0f;
            if (_hud != null) _hud.ceremonyResultsHold = false;
            if (_camera == null) return;
            _camera.SetMode(CameraMode.Scoreboard, 1.1f);
        }

        /// <summary>Walk the board's total from what it was before the last round to what it is now.</summary>
        void CountUp(float dt)
        {
            if (_championship == null) return;
            if (_board == null) _board = Object.FindFirstObjectByType<Scoreboard>();
            if (_board == null) return;
            _countTimer += dt;
            float k = Mathf.Clamp01(_countTimer / Mathf.Max(countSeconds, 0.01f));
            k = k * k * (3f - 2f * k);
            _board.BlendPlayerTotal(_championship.PointsBeforeLastRound,
                                    _championship.PlayerPoints, k);
        }

        void EnterPrize()
        {
            _beat = Beat.Prize;
            _time = 0f;
            if (_camera == null) return;

            if (PlayerWon)
            {
                // The trophy has stood on its plinth by the judges since the ground was built and
                // has never once been looked at. Winning is the reason it is there.
                _camera.SetMode(CameraMode.Trophy, 0f);
                _camera.SnapToCurrent();
            }
            else
            {
                // A defeat gets the same beat aimed at the winner's lawn. Flown rather than cut:
                // the travel across the venue is what says the title went somewhere else.
                _camera.SetTourTarget(new Vector3(_championPlot.centre.x, 0f, _championPlot.centre.y),
                                      _championPlot.size);
                _camera.SetMode(CameraMode.VenueTour, 1.0f);
            }
        }

        void EnterPortrait()
        {
            _beat = Beat.Portrait;
            _time = 0f;
            CardUp = true;
            if (_hud != null) _hud.ceremonyResultsHold = false;
            // Night falls on the last beat, win or lose. The fireworks only go up for a champion,
            // which is handled inside CeremonyNight by simply not asking for them.
            _night?.Begin();
            if (!PlayerWon && _night != null) _night.shellsPerBurst = 0;
            AudioDirector.Instance?.PlayFanfare(PlayerWon);
            if (PlayerWon) AudioDirector.Instance?.CrowdCheer(1f, applaud: true);

            // Ends on the duck, win or lose. The verdict portrait is the one shot in the game that
            // is framed to leave room for text, which is what the title card needs — and a
            // championship has to finish on the character it happened to, not on scenery.
            if (_camera == null) return;
            _camera.SetMode(CameraMode.Verdict, 0f);
            _camera.SnapToCurrent();
        }

        /// <summary>
        /// One judge per interval, in seating order, each reacting to the result the whole panel just
        /// produced. <see cref="JudgeCharacter.Punch"/> already picks the applaud or the head shake
        /// from the sign of the amount.
        /// </summary>
        void ReactInTurn(float interval)
        {
            if (_judges == null || _judges.judges == null) return;
            while (_reacted < _judges.judges.Length && _time >= _reacted * interval)
            {
                var c = _judges.judges[_reacted]?.character;
                _reacted++;
                if (c == null) continue;
                c.Punch(PlayerWon ? 1f : -0.6f);
                AudioDirector.Instance?.CrowdCheer(PlayerWon ? 0.9f : 0.25f, applaud: PlayerWon);
            }
        }

        /// <summary>
        /// Keep the stands moving. Excitement decays on its own, so a single call would produce one
        /// bounce and then stillness through the most important shot in the game.
        /// </summary>
        void Swell(float dt, float perSecond)
        {
            if (perSecond <= 0f || _crowds == null) return;
            _crowdTimer -= dt;
            if (_crowdTimer > 0f) return;
            _crowdTimer = 0.4f;
            foreach (var c in _crowds) c?.Excite(perSecond * 0.4f);
        }
    }
}
