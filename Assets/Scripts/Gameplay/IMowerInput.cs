namespace DuckMow
{
    /// <summary>
    /// Somewhere for a mower to read its controls from.
    ///
    /// Exists so a mower can be driven by something that is not the player's keyboard without the
    /// mower knowing the difference. <see cref="MowerController"/> reads
    /// <see cref="InputReader.Instance"/> — a singleton, which is correct for the one machine the
    /// player is sitting on and useless the moment there are four of them on the same pitch.
    ///
    /// The point is that a bot is not a cheaper mower. It gets a steer value and a throttle value
    /// and the identical suspension, grip, drift and boost model underneath, so an NPC that beats
    /// the player to a goose beat them by driving there. Anything that reached past this interface
    /// to nudge a transform would be the bot cheating, and it would be visible.
    /// </summary>
    public interface IMowerInput
    {
        float Steer { get; }
        float Throttle { get; }
        bool Handbrake { get; }
        bool Boost { get; }
    }
}
