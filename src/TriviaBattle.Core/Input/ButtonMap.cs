namespace TriviaBattle.Core.Input;

/// <summary>One row of config/buttons.json.</summary>
/// <param name="ButtonId">Fixed id of the physical button, e.g. 10.</param>
/// <param name="StationId">Station the button belongs to, e.g. "A1".</param>
/// <param name="AnswerIndex">0-3 for answers A-D.</param>
public sealed record ButtonAssignment(int ButtonId, string StationId, int AnswerIndex);

/// <summary>
/// Translates between raw button ids and (station, answer). Used in both directions:
/// incoming presses (button id -> station/answer) and outgoing LED commands (station/answer -> button id).
/// </summary>
public sealed class ButtonMap
{
    private readonly Dictionary<int, ButtonAssignment> _byButtonId;
    private readonly Dictionary<(string StationId, int AnswerIndex), int> _byStationAnswer;

    public ButtonMap(IEnumerable<ButtonAssignment> assignments)
    {
        Assignments = assignments.OrderBy(a => a.ButtonId).ToList();

        foreach (var a in Assignments)
        {
            if (a.AnswerIndex is < 0 or > 3)
                throw new InvalidOperationException($"Button {a.ButtonId}: answer index must be 0-3 (A-D).");
        }

        _byButtonId = Assignments.ToDictionary(a => a.ButtonId);
        _byStationAnswer = Assignments.ToDictionary(a => (a.StationId, a.AnswerIndex), a => a.ButtonId);
    }

    public IReadOnlyList<ButtonAssignment> Assignments { get; }

    /// <summary>Returns null for a button id that isn't in the map (the press is then ignored).</summary>
    public ButtonPress? Translate(ButtonEvent e)
    {
        if (!_byButtonId.TryGetValue(e.ButtonId, out var a))
            return null;

        return new ButtonPress(a.StationId, a.AnswerIndex, e.Sequence, e.PressedAt);
    }

    public int? FindButtonId(string stationId, int answerIndex) =>
        _byStationAnswer.TryGetValue((stationId, answerIndex), out var id) ? id : null;

    /// <summary>Converts "A".."D" (as written in buttons.json) to 0..3.</summary>
    public static int AnswerLetterToIndex(string letter)
    {
        var trimmed = letter.Trim().ToUpperInvariant();
        if (trimmed.Length != 1 || trimmed[0] < 'A' || trimmed[0] > 'D')
            throw new InvalidOperationException($"Answer must be A, B, C or D (got '{letter}').");
        return trimmed[0] - 'A';
    }

    public static string AnswerIndexToLetter(int index) => ((char)('A' + index)).ToString();
}
