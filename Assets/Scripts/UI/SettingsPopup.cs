using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace DuckMow.UI
{
    /// <summary>
    /// THE SETTINGS, REACHABLE FROM INSIDE A ROUND: master volume, mute, rumble.
    ///
    /// They existed already and were unreachable. The front page has a settings card and the state
    /// behind it is persistent — <see cref="MasterAudio"/> owns Master and Muted,
    /// <see cref="Haptics"/> owns Enabled, both in PlayerPrefs — but the only door to any of it was
    /// on the menu, so a player who found the game too loud in the middle of a stage had to leave
    /// the stage to turn it down. A game that can be paused and cannot be adjusted while paused is
    /// a game that asks you to quit in order to change the volume.
    ///
    /// ---- what is new here is the PRESENTATION and nothing else ----
    ///
    /// Not one line of this file knows what mute means. Every row is a view of a static that already
    /// existed, every change goes through that static's own setter, and the persistence, the
    /// debounced flush and the "unmute goes back to what it was" rule all stay where they were
    /// written. A second copy of that logic is how two screens end up disagreeing about whether the
    /// game is muted, which is a bug with no good failure mode: whichever one the player believes,
    /// the other is what they will hear.
    ///
    /// The volume CURVE moved to MasterAudio when this page was written, for the same reason. It was
    /// private to MainMenu, and two boards with two curves would print two different percentages for
    /// one amplitude — 70% here and 50% there, neither wrong, the game still lying.
    ///
    /// ---- the rows are lines of type, not plates ----
    ///
    /// MainMenu's settings board settled this and its argument is adopted whole: every other row in
    /// this game's menus is a CHOICE, and dressing a dial as a button "promises a press that does
    /// not exist" — the player hits Enter on MASTER VOLUME and nothing happens, which is exactly the
    /// failure the plate artwork exists to prevent. So the dials are a name in cream and a reading
    /// in gold on the board's own timber, selection is carried by a colour lift, and the one thing
    /// on this page that IS a choice — BACK — is the one thing wearing a plate.
    ///
    /// That plate is not decoration either. <see cref="ControlsPrimer"/> records why a card with no
    /// items at all is wrong: it would be dismissable by Escape and by nothing else, so a player on
    /// a touchscreen, where there is no Escape, could open this and never get out.
    /// </summary>
    public sealed class SettingsPopup : PopupView
    {
        public override string Id => "settings";
        public override bool PausesTime => true;
        public override bool BlocksDriving => true;
        public override bool ClosesOnEscape => true;

        /// <summary>
        /// A hundred under the pause board's 25000, alongside the controls card and for the reason
        /// that card records: two ScreenSpaceOverlay canvases at the SAME sorting order resolve
        /// against each other by an order Unity does not document.
        /// </summary>
        protected override int SortingOrder => 24900;

        // ------------------------------------------------------------------ the rows

        enum Dial { Volume, Mute, Rumble }

        /// <summary>One dial: what it is called, what it currently reads, and where it sits.</summary>
        sealed class Row
        {
            public Dial dial;
            public RectTransform rect;          // the whole line, for hit-testing
            public TextMeshProUGUI name;
            public TextMeshProUGUI value;
            public float lift;                  // 0 unselected, 1 selected, sprung
        }

        readonly Row[] _rows = new Row[3];

        /// <summary>
        /// Where the volume control is standing, in POSITION rather than amplitude.
        ///
        /// Kept rather than re-derived from <see cref="MasterAudio.Master"/> every frame, and
        /// MainMenu's note is the one being obeyed: Nudge snaps the stored amplitude to a
        /// thousandth, so a position written here and read back through a 2.5-power curve differs
        /// from itself slightly, and a readout derived fresh every frame steps 10% to 6% on a single
        /// press near the bottom. The guard in <see cref="ReadVolume"/> is what lets this page still
        /// notice somebody else moving the volume.
        /// </summary>
        float _volumePos;

        /// <summary>
        /// Which of the four things in the column is live: 0..2 are the dials, 3 is the BACK plate.
        ///
        /// One index over dials AND the plate, deliberately, because they are one column to the
        /// player and Up and Down have to walk the whole of it. The base's own index is set to -1
        /// while a dial is selected so no plate is lit — see PopupView.Index.
        /// </summary>
        int _row;

        int BackRow => _rows.Length;

        // Hold-to-repeat on Left and Right, at MainMenu's numbers rather than at numbers of my own:
        // the two settings boards are the same control and a player who has learned the front page's
        // repeat rate should not find this one faster.
        const float AdjustDelay = 0.38f, AdjustRepeat = 0.085f;
        float _held;
        int _heldDir;

        Vector2 _lastPointer;
        bool _pointerSeen;

        /// <summary>
        /// The duck this page found in place, put back on the way out.
        ///
        /// ---- why this page lifts the duck at all ----
        ///
        /// The pause board turns the game down to 30% while it is open, for a good reason of its own:
        /// an engine drone whose pitch is driven by a frozen road speed holds one flat tone under the
        /// menu. But a VOLUME SLIDER inside a screen that is itself ducking the game is a trap. The
        /// player drags the bar until the crowd sounds right, hears 0.30 x Master while they do it,
        /// presses RESUME and gets 1.0 x Master — the game comes back three times louder than the
        /// level they just chose, so they pause again and turn it down again, and the control has
        /// actively misled them twice.
        ///
        /// So while this page is up the duck is released and the player hears exactly what they are
        /// setting. The game is paused, not silent: the crowd and the ambience are still running,
        /// which is what makes the preview honest.
        ///
        /// BORROWED, not assumed. It saves whatever it found and restores that, rather than writing
        /// the pause board's 0.30 back by hand — this page has no business knowing that number, and
        /// the day something else ducks for its own reasons the borrow is still correct. Same idiom
        /// PopupStack uses for Time.timeScale, and for the same reason.
        /// </summary>
        float _duckOnEntry = 1f;

        // ------------------------------------------------------------------ layout

        const float BoardWidth = 860f;
        const float RowStep = 74f;
        const float RowsTop = 250f;
        /// <summary>Centres of the two columns of type, either side of the board's middle.</summary>
        const float NameCentre = -180f, ValueCentre = 250f;

        protected override float ItemsCentreY => _itemsCentreY;
        float _itemsCentreY;

        protected override void Compose()
        {
            // The pause board's own scrim, to the number. This page REPLACES that board — its canvas
            // goes off underneath, see IPopup.ShowsWhatIsUnder — so a lighter wash here would make
            // the world brighten as the player pressed SETTINGS and darken again as they came back,
            // which reads as the page being half-transparent rather than as a page turning.
            BuildScrim(0.78f);

            const int n = 3;
            float cursor = RowsTop + (n - 1) * RowStep + 30f;
            float itemY = cursor + 40f + ItemHeight * 0.5f;
            float hintY = itemY + ItemHeight * 0.5f + 34f;
            float height = hintY + 40f;

            BuildBoard(BoardWidth, height);
            float half = height * 0.5f;
            _itemsCentreY = half - itemY;

            BuildText("Kicker", "WHILE THE ROUND WAITS", 26f, half - 54f,
                      new Vector2(700f, 38f), Gold, false, 0.20f, 14f);
            BuildText("Title", "SETTINGS", 74f, half - 122f,
                      new Vector2(760f, 96f), Cream, false, 0.13f, 9f)
                .fontStyle = FontStyles.Bold;
            BuildRule(half - 178f, 700f);

            for (int i = 0; i < n; i++)
                _rows[i] = BuildRow((Dial)i, half - (RowsTop + i * RowStep));

            AddItem("BACK", RequestClose);

            BuildText("Hint",
                      "UP / DOWN  CHOOSE   ·   LEFT / RIGHT  ADJUST   ·   ESC  BACK", 20f,
                      half - hintY, new Vector2(800f, 30f),
                      new Color(BoardEdge.r, BoardEdge.g, BoardEdge.b, 0.62f), false, 0.14f, 8f);

            _row = 0;
            Index = -1;
            ReadVolume(true);
            Refresh();
            // The first row already lit on the frame the board lands, rather than springing up a
            // tenth of a second later once the swallow window has expired and HandleInput starts
            // running. A settings page that opens with nothing selected reads as a page that has not
            // finished loading.
            if (_rows[0] != null) _rows[0].lift = 1f;
            TickLift();
        }

        /// <summary>
        /// One dial: a name on the left, a reading on the right, and an invisible rect across both
        /// of them so the mouse has something the width of the row to be over.
        ///
        /// The rect is built rather than borrowed from either label because a pointer that only
        /// counts while it is exactly on four characters of gold is a pointer the player has to aim
        /// — and the whole row is what looks pressable.
        /// </summary>
        Row BuildRow(Dial dial, float y)
        {
            var go = new GameObject($"Row {dial}", typeof(RectTransform));
            go.transform.SetParent(Board, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(BoardWidth - 120f, RowStep - 8f);
            rt.anchoredPosition = new Vector2(0f, y);

            var name = BuildText($"{dial} name", Label(dial), 28f, y,
                                 new Vector2(420f, 44f), Cream, false, 0.14f, 8f);
            name.fontStyle = FontStyles.Bold;
            name.alignment = TextAlignmentOptions.Left;
            name.rectTransform.anchoredPosition = new Vector2(NameCentre, y);

            var value = BuildText($"{dial} value", "", 30f, y,
                                  new Vector2(260f, 44f), Gold, false, 0.16f, 8f);
            value.fontStyle = FontStyles.Bold;
            value.alignment = TextAlignmentOptions.Right;
            value.rectTransform.anchoredPosition = new Vector2(ValueCentre, y);

            return new Row { dial = dial, rect = rt, name = name, value = value };
        }

        static string Label(Dial dial) => dial switch
        {
            Dial.Volume => "MASTER VOLUME",
            Dial.Mute => "MUTE",
            _ => "RUMBLE"
        };

        // ------------------------------------------------------------------ the readings

        /// <summary>
        /// Put the bar back in step with the volume, adopting anything somebody else did to it.
        ///
        /// The tolerance is MainMenu's and stops the guard undoing the player's own edit: Nudge
        /// rounds to a thousandth, so a position written here and read back can differ from itself
        /// by half a thousandth of amplitude. Anything larger was somebody else — the front page, a
        /// cheat, a fresh Load — and is adopted.
        /// </summary>
        void ReadVolume(bool force)
        {
            float amp = MasterAudio.Master;
            if (force || Mathf.Abs(MasterAudio.AmplitudeAt(_volumePos) - amp) > 0.004f)
                _volumePos = MasterAudio.PositionAt(amp);
        }

        /// <summary>
        /// Write every reading from the model. Called after any change and every frame.
        ///
        /// Every frame rather than only on edit, and it is the same guard MainMenu keeps: these are
        /// globals this page does not own, and a row that has quietly stopped describing the thing
        /// it controls is the worst kind of settings screen, because it looks fine.
        /// </summary>
        void Refresh()
        {
            foreach (var r in _rows)
            {
                if (r?.value == null) continue;
                r.value.text = r.dial switch
                {
                    Dial.Volume => MasterAudio.Muted
                        // Said rather than left as a number the player is about to disbelieve. A bar
                        // reading 70% on a muted game describes a volume nobody can hear.
                        ? "MUTED"
                        : $"{Mathf.RoundToInt(_volumePos * 100f)}%",
                    Dial.Mute => MasterAudio.Muted ? "ON" : "OFF",
                    // Haptics.Available is re-checked rather than answered once: a pad can be
                    // plugged in at any moment, and in a browser it does not appear until the player
                    // has pressed something on it. A switch the player can flip that will never do
                    // anything is worse than a row that says so.
                    _ => !Haptics.Available ? "UNAVAILABLE" : Haptics.Enabled ? "ON" : "OFF"
                };
            }
        }

        // ------------------------------------------------------------------ the frame

        protected override void OnComposed()
        {
            _duckOnEntry = MasterAudio.Duck;
            MasterAudio.Duck = 1f;
        }

        protected override void OnClosed()
        {
            MasterAudio.Duck = _duckOnEntry;
            // The one explicit flush this page makes. MasterAudio debounces its own PlayerPrefs
            // writes so nothing here is throttled by hand, but a player who sets the volume and then
            // shuts the tab has not given the debounce time to fire — and on WebGL the flush is an
            // asynchronous FS.syncfs that wants as long as it can get. Leaving is the moment to ask.
            MasterAudio.Save();
        }

        protected override void HandleInput()
        {
            ReadVolume(false);

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;

            int move = 0, step = 0;
            bool confirm = false;
            int holdDir = 0;

            if (kb != null)
            {
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move++;
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) move--;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) step++;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) step--;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) holdDir++;
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) holdDir--;
                confirm = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame ||
                          kb.spaceKey.wasPressedThisFrame;
            }
            if (pad != null)
            {
                if (pad.dpad.down.wasPressedThisFrame) move++;
                if (pad.dpad.up.wasPressedThisFrame) move--;
                if (pad.dpad.right.wasPressedThisFrame) step++;
                if (pad.dpad.left.wasPressedThisFrame) step--;
                if (pad.dpad.right.isPressed) holdDir++;
                if (pad.dpad.left.isPressed) holdDir--;
                confirm |= pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame;
            }
            // Escape is deliberately not read. ClosesOnEscape is true and the stack owns that key.

            int count = _rows.Length + 1;
            if (move != 0) { SelectRow(((_row + move) % count + count) % count); _pointerSeen = false; }

            // ---- the mouse ----

            int over = -1;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (mouse != null)
            {
                Vector2 p = mouse.position.ReadValue();
                // Only while MOVING, the rule every other screen in this game keeps: a mouse left
                // resting on a row used to re-select it every frame, so the arrow keys could not
                // move off it.
                bool moved = !_pointerSeen || (p - _lastPointer).sqrMagnitude > 4f;
                _lastPointer = p;
                _pointerSeen = true;

                for (int i = 0; i < _rows.Length; i++)
                {
                    if (_rows[i]?.rect == null) continue;
                    // Null camera: for a ScreenSpaceOverlay canvas the screen point IS the canvas
                    // point, and passing one silently returns false for every rect on screen.
                    if (!RectTransformUtility.RectangleContainsScreenPoint(_rows[i].rect, p, null)) continue;
                    over = i;
                    if (moved) SelectRow(i);
                    break;
                }
                if (over < 0 && Items.Count > 0 && Items[0]?.rect != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(Items[0].rect, p, null))
                {
                    over = BackRow;
                    if (moved) SelectRow(BackRow);
                }
            }

            // ---- adjust, then confirm ----

            if (_row < _rows.Length)
            {
                if (step != 0) { Adjust(_rows[_row].dial, step, discrete: true); _held = 0f; _heldDir = holdDir; }
                else if (holdDir != 0 && holdDir == _heldDir)
                {
                    _held += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                    if (_held >= AdjustDelay)
                    {
                        _held -= AdjustRepeat;
                        Adjust(_rows[_row].dial, holdDir, discrete: false);
                    }
                }
                if (holdDir == 0) { _heldDir = 0; _held = 0f; }
            }

            Refresh();
            TickLift();

            if (over >= 0 && clicked) { Press(over); return; }
            if (confirm) Press(_row);
        }

        void SelectRow(int index)
        {
            if (index < 0 || index == _row) return;
            _row = index;
            // The base lights a plate by its own index. While a dial is selected there is no plate
            // to light, and -1 is how that is said.
            Index = index >= _rows.Length ? 0 : -1;
        }

        /// <summary>
        /// Enter, or a click. Only two of the four rows have anything to do here.
        ///
        /// MASTER VOLUME deliberately does NOTHING, which is MainMenu's call and is the right one: a
        /// bar has no pressed state, and inventing one — jump to full? to half? — would be a
        /// keystroke that destroys a setting the player spent twenty presses arriving at. The hint
        /// line along the bottom is what tells them the arrows are the thing that row wants.
        /// </summary>
        void Press(int index)
        {
            if (index >= _rows.Length) { Activate(0); return; }   // BACK, through the base's plate
            SelectRow(index);
            var dial = _rows[index].dial;
            if (dial == Dial.Volume) return;
            Toggle(dial);
        }

        /// <summary>
        /// Left or right on a dial. <paramref name="discrete"/> is false on a repeat, which is the
        /// flag a control that must not repeat checks.
        ///
        /// RIGHT IS ON AND LEFT IS OFF on both switches rather than either direction toggling, which
        /// is MainMenu's rule and its reasoning: on a board where every other row's right-hand end is
        /// "more", a switch that flips on both arrows is the one control the player cannot predict,
        /// and pressing right twice to end up back where you started is a bug report.
        /// </summary>
        void Adjust(Dial dial, int direction, bool discrete)
        {
            switch (dial)
            {
                case Dial.Mute:
                    if (!discrete) return;
                    bool wantMuted = direction < 0;   // LEFT is less sound, and less sound is muted
                    if (wantMuted == MasterAudio.Muted) return;
                    MasterAudio.Muted = wantMuted;
                    break;

                case Dial.Rumble:
                    if (!discrete || !Haptics.Available) return;
                    bool wantRumble = direction > 0;
                    if (wantRumble == Haptics.Enabled) return;
                    Haptics.Enabled = wantRumble;
                    // A sample on the way ON and only on the way on. A player switching rumble on has
                    // no other way to find out whether their pad does anything — the next rumble is a
                    // klaxon away — and one that fired on the way OFF would be the control disobeying
                    // the press that just turned it off.
                    if (wantRumble) Haptics.MiniTurbo();
                    break;

                default:
                    // Unmuting first, because the alternative is a control that appears not to work:
                    // a player who mutes, then reaches for the volume, would otherwise drag a bar
                    // that moves a number and changes nothing they can hear.
                    if (MasterAudio.Muted) MasterAudio.Muted = false;
                    // Stepped in POSITION and handed to Nudge as a change in AMPLITUDE. That split is
                    // what makes both true at once: the steps are even to the ear, and the stored
                    // number stays clean, because Nudge is where the rounding lives.
                    _volumePos = Mathf.Clamp01(_volumePos + direction * MasterAudio.ControlStep);
                    MasterAudio.Nudge(MasterAudio.AmplitudeAt(_volumePos) - MasterAudio.Master);
                    break;
            }
        }

        void Toggle(Dial dial)
        {
            switch (dial)
            {
                case Dial.Mute:
                    MasterAudio.Muted = !MasterAudio.Muted;
                    break;
                case Dial.Rumble:
                    if (!Haptics.Available) return;
                    Haptics.Enabled = !Haptics.Enabled;
                    if (Haptics.Enabled) Haptics.MiniTurbo();
                    break;
            }
        }

        /// <summary>
        /// The selection lift on the dials, on springs of its own.
        ///
        /// Stepped from HandleInput rather than from a Tick override because HandleInput is the one
        /// place the base guarantees is reached only when this popup is the live one — a covered
        /// popup is not ticked at all, and one whose rows kept brightening underneath a confirmation
        /// would be answering attention it does not have.
        /// </summary>
        void TickLift()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            for (int i = 0; i < _rows.Length; i++)
            {
                var r = _rows[i];
                if (r == null) continue;
                r.lift = Mathf.MoveTowards(r.lift, i == _row ? 1f : 0f, dt / 0.12f);

                // Unselected rows sink rather than the selected one glaring: the board is read as a
                // list first and operated second, so the whole of it stays legible and one line is
                // simply brighter. The same emphasis the comic page and the pause board both use.
                if (r.name != null)
                    r.name.color = new Color(Cream.r, Cream.g, Cream.b, Mathf.Lerp(0.62f, 1f, r.lift));
                if (r.value != null)
                    r.value.color = Color.Lerp(new Color(Gold.r, Gold.g, Gold.b, 0.68f), Gold, r.lift);
            }
        }
    }
}
