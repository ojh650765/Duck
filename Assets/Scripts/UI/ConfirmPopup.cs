using System;
using TMPro;
using UnityEngine;

namespace DuckMow.UI
{
    /// <summary>
    /// "ARE YOU SURE" — the one popup in this game that exists to be in the WAY.
    ///
    /// Everything else on screen is trying to get out of the player's hands as fast as it can. This
    /// is the opposite: it is the second press a destructive choice is worth, and its whole design
    /// brief is that it must be impossible to cross by accident and trivial to back out of.
    ///
    /// ---- why it is a separate popup rather than a mode of the pause board ----
    ///
    /// Because it is the thing that proves the stack is real. A confirmation implemented as a state
    /// INSIDE the pause menu means the pause menu has to remember what it looked like before the
    /// question, tear its own board down, put a different one up, and rebuild the first one on
    /// cancel — and every one of those steps is a chance for the two layouts to disagree. Pushed as
    /// a popup of its own, cancelling restores nothing, because nothing was ever unwound: the pause
    /// board is still sitting exactly where it was, dimmed, underneath.
    ///
    /// That is also why the scrim here is light. The world was already dimmed by whatever pushed
    /// this — the question is not covering the game, it is covering the board that asked it, and a
    /// second full-strength wash on top of the first turns the pause menu black. See
    /// PopupView.OnCovered for the other half of the same effect.
    ///
    /// ---- reusable on purpose ----
    ///
    /// It takes a prompt and an <see cref="Action"/> and knows nothing else. Any popup that needs a
    /// second press — abandoning a championship, throwing away a settings change, whatever comes
    /// next — pushes one of these rather than growing a confirmation of its own.
    /// </summary>
    public sealed class ConfirmPopup : PopupView
    {
        readonly string _prompt;
        readonly string _confirmLabel;
        readonly string _cancelLabel;
        readonly Action _onConfirm;

        /// <summary>
        /// A question and what to do if the answer is yes.
        ///
        /// The labels are arguments rather than fixed words because "CONFIRM / CANCEL" is the
        /// language of a dialogue box and this game does not have dialogue boxes — a board asking
        /// about leaving says QUIT and STAY, which is shorter, warmer, and tells the player what
        /// each answer DOES instead of how sure they are.
        /// </summary>
        public ConfirmPopup(string prompt, Action onConfirm,
                            string confirmLabel = "CONFIRM", string cancelLabel = "CANCEL")
        {
            _prompt = string.IsNullOrEmpty(prompt) ? "ARE YOU SURE?" : prompt;
            _onConfirm = onConfirm;
            _confirmLabel = string.IsNullOrEmpty(confirmLabel) ? "CONFIRM" : confirmLabel;
            _cancelLabel = string.IsNullOrEmpty(cancelLabel) ? "CANCEL" : cancelLabel;
        }

        public override string Id => "confirm";
        public override bool PausesTime => true;
        public override bool BlocksDriving => true;

        /// <summary>
        /// Escape CANCELS, which is the whole reason this flag is true rather than the popup reading
        /// the key itself. The stack pops the top popup on Escape and popping this one is exactly
        /// cancelling it — there is no state to unwind and no handler to skip, because the confirm
        /// action only ever runs from CONFIRM. Escape can therefore never be the destructive answer,
        /// on any confirmation, without anybody having to remember to make it so.
        /// </summary>
        public override bool ClosesOnEscape => true;

        /// <summary>
        /// Ten above the pause board, and still fifteen thousand below the curtain.
        ///
        /// The gap is small on purpose: these are the same family of surface and the ordering between
        /// them is a stacking question, not a layering one. Anything that wants to sit above ALL
        /// popups belongs in the next band up, not in this one's gaps.
        /// </summary>
        protected override int SortingOrder => 25010;

        // A narrower column than the pause board's. Two answers to one question is not a menu, and
        // giving them the full width of a menu makes them read as one.
        protected override float ItemWidth => 480f;
        protected override float ItemsCentreY => -76f;

        protected override void Compose()
        {
            // Light. See the class comment: this normally sits on a board that has already dimmed
            // the world, and dimming it twice is how a pause menu ends up black. It is still enough
            // to hold on its own if something ever pushes a confirmation with nothing underneath it.
            BuildScrim(0.34f);

            const int n = 2;
            float height = 332f + n * ItemStep;
            BuildBoard(820f, height);
            float half = height * 0.5f;

            BuildText("Kicker", "ARE YOU SURE", 26f, half - 52f,
                      new Vector2(700f, 36f), Gold, false, 0.20f, 14f);

            // WRAPPING, and auto-sized down to just over half. This is the one string on any of
            // these boards that is written by the caller rather than by the layout, so it is the one
            // that can be any length at all — and a prompt with its last three words cut off is a
            // question the player answers without having read it.
            BuildText("Prompt", _prompt, 40f, half - 142f,
                      new Vector2(720f, 140f), Cream, true, 0.14f, 4f)
                .fontStyle = FontStyles.Bold;

            BuildRule(half - 232f, 640f);

            AddItem(_confirmLabel, Confirm);
            AddItem(_cancelLabel, RequestClose);

            BuildText("Hint", "ESC  " + _cancelLabel, 20f, -half + 38f,
                      new Vector2(720f, 30f),
                      new Color(BoardEdge.r, BoardEdge.g, BoardEdge.b, 0.62f), false, 0.14f, 8f);
        }

        /// <summary>
        /// The board opens on the SAFE answer.
        ///
        /// Every other menu in this game opens on its first item, because on every other menu the
        /// first item is the one the player came for. Here it is the opposite: the player arrived by
        /// choosing something destructive and is now being asked a question, and the default answer
        /// to a question nobody has read yet must be the one that changes nothing. A player who
        /// mashes Enter through this board stays in their round.
        ///
        /// Set from OnComposed rather than from Compose because the base deals the items their
        /// positions only after Compose returns — a selection set before the layout exists would put
        /// the marker beside a row that has not been placed yet.
        ///
        /// Note that this popup deliberately does NOT duck the game's audio the way PausePopup does.
        /// It is normally pushed on top of one that already has, and a second duck would take the
        /// round down to a tenth of its volume and back up in two steps.
        /// </summary>
        protected override void OnComposed()
        {
            SetSelection(1);
        }

        /// <summary>
        /// Yes.
        ///
        /// The handler runs first and the close request goes in afterwards, and the ORDER matters:
        /// most confirm handlers in this game end by tearing the popup stack down themselves (a quit
        /// or a scene change has no use for the boards that led to it), and PopupView.RunAction
        /// notices the stack got shorter and throws this request away. So the request is only ever
        /// honoured by handlers that left the stack alone — a settings toggle, say — which is
        /// exactly the set of handlers that want the question to close behind them.
        /// </summary>
        void Confirm()
        {
            _onConfirm?.Invoke();
            RequestClose();
        }
    }
}
