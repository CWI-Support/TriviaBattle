using Microsoft.EntityFrameworkCore;
using TriviaBattle.Server.Data.Entities;

namespace TriviaBattle.Server.Data;

/// <summary>
/// The SQLite database. Schema changes are made with EF Core migrations:
/// <code>
/// dotnet tool restore
/// dotnet ef migrations add &lt;Name&gt; --project src/TriviaBattle.Server
/// </code>
/// Migrations are applied automatically when the server starts (see DatabaseStartup).
/// </summary>
public class TriviaDbContext(DbContextOptions<TriviaDbContext> options) : DbContext(options)
{
    public DbSet<CategoryEntity> Categories => Set<CategoryEntity>();
    public DbSet<QuestionEntity> Questions => Set<QuestionEntity>();
    public DbSet<PlayerProfileEntity> PlayerProfiles => Set<PlayerProfileEntity>();
    public DbSet<MatchEntity> Matches => Set<MatchEntity>();
    public DbSet<MatchTeamEntity> MatchTeams => Set<MatchTeamEntity>();
    public DbSet<MatchParticipantEntity> MatchParticipants => Set<MatchParticipantEntity>();
    public DbSet<MatchAnswerEntity> MatchAnswers => Set<MatchAnswerEntity>();
    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CategoryEntity>(category =>
        {
            category.ToTable("Categories");
            category.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<QuestionEntity>(question =>
        {
            question.ToTable("Questions");
            question.Property(q => q.Difficulty).HasConversion<string>();
            question.HasIndex(q => new { q.CategoryId, q.Difficulty, q.IsActive });
            question.HasOne(q => q.Category)
                .WithMany(c => c.Questions)
                .HasForeignKey(q => q.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerProfileEntity>(player =>
        {
            player.ToTable("PlayerProfiles");
            player.HasIndex(p => p.RfidTag).IsUnique();
        });

        // Match history. Deleting a match deletes its teams, participants and answers.
        modelBuilder.Entity<MatchEntity>(match =>
        {
            match.ToTable("Matches");
            match.HasIndex(m => new { m.Mode, m.WasAborted, m.EndedAtUtc });
            match.HasMany(m => m.Teams).WithOne().HasForeignKey(t => t.MatchId).OnDelete(DeleteBehavior.Cascade);
            match.HasMany(m => m.Participants).WithOne().HasForeignKey(p => p.MatchId).OnDelete(DeleteBehavior.Cascade);
            match.HasMany(m => m.Answers).WithOne().HasForeignKey(a => a.MatchId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MatchTeamEntity>().ToTable("MatchTeams");
        modelBuilder.Entity<MatchParticipantEntity>().ToTable("MatchParticipants");
        modelBuilder.Entity<MatchAnswerEntity>().ToTable("MatchAnswers");

        modelBuilder.Entity<AppSettingEntity>(setting =>
        {
            setting.ToTable("AppSettings");
            setting.HasKey(s => s.Key);
        });
    }
}
