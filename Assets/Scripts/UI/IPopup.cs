namespace DuckMow.UI
{
    /// <summary>
    /// One screen that sits in front of the game and takes the player's attention until it is
    /// dismissed: the pause menu, the "are you sure?" over the top of it, and whatever else earns
    /// the right to interrupt a round later.
    ///
    /// An interface rather than a base class, deliberately. The three things a popup has to tell
    /// <see cref="PopupStack"/> — does it stop the clock, does it stop the mower, does Escape shut
    /// it — are answers, not behaviour, and a base class would drag a MonoBehaviour, a canvas and a
    /// lifetime into a contract that needs none of them. A popup here can be a MonoBehaviour that
    /// builds a canvas, or it can be four fields and a coroutine; the stack does not care and must
    /// not care, because the moment it cares there is exactly one way to write a popup and the next
    /// one that does not fit gets bolted onto the side of the stack instead.
    ///
    /// The lifetime, in the order it happens:
    ///
    ///   Push        <see cref="OnPush"/> on the newcomer, and <see cref="OnCovered"/> on whatever
    ///               was on top before it. The covered one is still in the stack and still owns its
    ///               canvas — it is behind something, not gone.
    ///   Every frame <see cref="Tick"/>, on the TOP popup only, with an UNSCALED delta. Covered
    ///               popups do not tick: a menu that keeps reading the mouse from underneath a
    ///               confirmation is how a player cancels a quit and resumes at the same time.
    ///   Pop         <see cref="OnPop"/> on the one leaving, then <see cref="OnRevealed"/> on the
    ///               one it was covering, if there is one.
    ///
    /// Every callback is allowed to be a no-op and none of them may assume they are paired with a
    /// frame of <see cref="Tick"/> — a popup can be pushed and popped inside one frame, and one
    /// that only tears itself down in Tick would leak a canvas when that happens.
    /// </summary>
    public interface IPopup
    {
        /// <summary>
        /// What this popup is, for logs and for a caller that wants to know whether the thing
        /// already open is the one it was about to push. Not used to look popups up — the stack is
        /// four entries deep on a bad day and a dictionary would be ceremony.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// True if the world should stop while this is up.
        ///
        /// The stack ORs this across every popup in it, not just the top one: a confirmation pushed
        /// on top of the pause menu must not restart the round clock because the confirmation
        /// itself had no opinion about time. Whatever the clock was doing when the FIRST pausing
        /// popup arrived is what it goes back to when the LAST one leaves — see PopupStack, which
        /// takes that promise seriously enough to keep re-reading the clock while it holds it.
        /// </summary>
        bool PausesTime { get; }

        /// <summary>
        /// True if the mower must not answer the controls while this is up.
        ///
        /// Separate from <see cref="PausesTime"/> because they are genuinely different questions. A
        /// popup that pauses does not need this — nothing moves at timeScale zero — but a popup that
        /// does NOT pause almost certainly does, or the player drives blind behind it. ORed across
        /// the stack for the same reason.
        /// </summary>
        bool BlocksDriving { get; }

        /// <summary>
        /// True if Escape dismisses this popup.
        ///
        /// False makes the popup modal to the key, not to the stack: Escape is still CONSUMED — the
        /// game underneath never sees it, so it cannot open a second pause menu on top of this one —
        /// it simply does not close anything. That is the right answer for a destructive
        /// confirmation, where the whole point is that the player has to say which way.
        /// </summary>
        bool ClosesOnEscape { get; }

        /// <summary>Called once as this becomes the top of the stack. Build, show, take focus.</summary>
        void OnPush();

        /// <summary>
        /// Called once as this leaves the stack, whether it asked to or the stack was emptied out
        /// from under it. The only place a popup is guaranteed to be told it is over, so it is the
        /// only correct place to tear down anything that outlives a frame.
        /// </summary>
        void OnPop();

        /// <summary>Called when something is pushed on top of this. Dim, stop listening, stay alive.</summary>
        void OnCovered();

        /// <summary>Called when the thing on top of this pops and it is the top again.</summary>
        void OnRevealed();

        /// <summary>
        /// One frame of the popup's own life, on the UNSCALED clock — because the popup is very
        /// probably the reason the scaled one is stopped, and a menu that cannot animate while it is
        /// up is a still image the player assumes is a hang.
        ///
        /// Return TRUE to ask to be closed. That is the whole exit protocol: a popup never removes
        /// itself, it says it would like to go and the stack does the popping, which is what keeps
        /// the time and driving locks balanced. A popup that called Pop on the stack from inside its
        /// own Tick would be mutating the list the stack is in the middle of walking.
        /// </summary>
        bool Tick(float unscaledDt);
    }
}
