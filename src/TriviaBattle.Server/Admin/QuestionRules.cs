namespace TriviaBattle.Server.Admin;

/// <summary>
/// What makes a question valid. Used by the admin editor and by import, so both enforce the same rules.
/// Limits keep text readable on the screens (a long question won't fit on a portrait player screen).
/// </summary>
public static class QuestionRules
{
    public const int MaxQuestionLength = 250;
    public const int MaxAnswerLength = 80;

    public static List<string> Validate(string? text, IReadOnlyList<string?> answers, int correctIndex, string? mediaUrl)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            problems.Add("The question text is empty.");
        else if (text.Trim().Length > MaxQuestionLength)
            problems.Add($"The question is longer than {MaxQuestionLength} characters.");

        if (answers.Count != 4)
        {
            problems.Add("There must be exactly 4 answers (A-D).");
            return problems;
        }

        for (var i = 0; i < 4; i++)
        {
            var letter = (char)('A' + i);
            if (string.IsNullOrWhiteSpace(answers[i]))
                problems.Add($"Answer {letter} is empty.");
            else if (answers[i]!.Trim().Length > MaxAnswerLength)
                problems.Add($"Answer {letter} is longer than {MaxAnswerLength} characters.");
        }

        var distinct = answers.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!.Trim().ToLowerInvariant()).Distinct().Count();
        if (distinct < answers.Count(a => !string.IsNullOrWhiteSpace(a)))
            problems.Add("Two answers are the same.");

        if (correctIndex is < 0 or > 3)
            problems.Add("The correct answer must be A, B, C or D.");

        if (!string.IsNullOrWhiteSpace(mediaUrl) && !(mediaUrl.StartsWith('/') || mediaUrl.StartsWith("http://") || mediaUrl.StartsWith("https://")))
            problems.Add("The media link must start with / or http(s)://.");

        return problems;
    }
}
