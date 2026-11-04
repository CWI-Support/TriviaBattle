namespace TriviaBattle.Core.Matches;

/// <summary>
/// The steps a match moves through. Every match is a timed rapid-fire round:
/// <code>
/// Intro -> Playing (the match clock runs; everyone answers at their own pace) -> Results -> Finished
///                    |                ^
///                    +-> Continue? ---+   (Dungeon games: a swipe, a second paid session, goes back to Playing)
/// </code>
/// Before a match exists, the room is in "Lobby" (kiosk setup). That isn't a match phase
/// because there is no match yet; the server reports it separately.
/// </summary>
public enum MatchPhase
{
    /// <summary>Intro video plays on every screen.</summary>
    Intro,

    /// <summary>
    /// The clock is running. Each player (or team, in 2v2 Group) has their own stream of
    /// questions: answer one and the next appears straight away. See <see cref="QuestionStream"/>.
    /// </summary>
    Playing,

    /// <summary>
    /// Dungeon games: the arcade clock ran out. A short countdown runs; a continue (a swipe at any station)
    /// goes back to Playing on a fresh session clock, on the same floor; otherwise Results.
    /// </summary>
    Continue,

    /// <summary>Final results after the clock runs out.</summary>
    Results,

    /// <summary>Match is over (or was aborted). The server saves it and removes it.</summary>
    Finished,
}
