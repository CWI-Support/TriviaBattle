using Microsoft.EntityFrameworkCore;
using TriviaBattle.Core.Matches;
using TriviaBattle.Server.Data.Entities;

namespace TriviaBattle.Server.Data;

/// <summary>Saves finished (or aborted) matches to the history tables.</summary>
public class MatchHistory(IDbContextFactory<TriviaDbContext> dbFactory)
{
    public async Task SaveAsync(MatchResult result)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        db.Matches.Add(new MatchEntity
        {
            Id = result.MatchId,
            Mode = result.Mode,
            CategoryId = result.CategoryId,
            CategoryName = result.CategoryName,
            Difficulty = result.Difficulty,
            StartedAtUtc = result.StartedAt.UtcDateTime,
            EndedAtUtc = result.EndedAt.UtcDateTime,
            WasAborted = result.WasAborted,
            QuestionsPlayed = result.QuestionsPlayed,

            Teams = result.Teams.Select(t => new MatchTeamEntity
            {
                TeamId = t.TeamId,
                Name = t.Name,
                Score = t.Score,
                CorrectAnswers = t.CorrectAnswers,
                Rank = t.Rank,
            }).ToList(),

            Participants = result.Participants.Select(p => new MatchParticipantEntity
            {
                StationId = p.StationId,
                PlayerName = p.Name,
                PlayerProfileId = p.PlayerProfileId,
                TeamId = p.TeamId,
                Score = p.Score,
                CorrectAnswers = p.CorrectAnswers,
                Rank = p.Rank,
            }).ToList(),

            Answers = result.Answers.Select(a => new MatchAnswerEntity
            {
                RoundNumber = a.RoundNumber,
                QuestionId = a.QuestionId,
                OwnerId = a.OwnerId,
                PressedByStationId = a.PressedByStationId,
                AnswerIndex = a.AnswerIndex,
                IsCorrect = a.IsCorrect,
                Points = a.Points,
                ResponseMs = a.ResponseMs,
            }).ToList(),
        });

        await db.SaveChangesAsync();
    }
}
