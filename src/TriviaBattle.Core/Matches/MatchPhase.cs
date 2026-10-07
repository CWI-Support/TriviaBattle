namespace TriviaBattle.Core.Matches;

/// <summary>
/// The steps a match moves through. The flow is:
/// <code>
/// Intro -> QuestionLeadIn -> QuestionOpen -> Reveal -> Standings -> QuestionLeadIn -> ...
///                                                  \-> Results (after the last question) -> Finished
/// </code>
/// Before a match exists, the room is in "Lobby" (kiosk setup). That isn't a match phase
/// because there is no match yet; the server reports it separately.
/// </summary>
public enum MatchPhase
{
    /// <summary>Intro video plays on every screen.</summary>
    Intro,

    /// <summary>Question number and category shown; buttons not live yet.</summary>
    QuestionLeadIn,

    /// <summary>Question and answers shown, timer running, buttons live.</summary>
    QuestionOpen,

    /// <summary>Correct answer and points for this question shown.</summary>
    Reveal,

    /// <summary>Running scores between questions.</summary>
    Standings,

    /// <summary>Final results after the last question.</summary>
    Results,

    /// <summary>Match is over (or was aborted). The server saves it and removes it.</summary>
    Finished,
}
