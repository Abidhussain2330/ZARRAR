using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BURAQ.Models;

public class AppDbContext(DbContextOptions<AppDbContext> options) :
    IdentityDbContext<UserInfo, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        TypeInfoResolver = new AppJsonSerializerContext()
    };

    public DbSet<Post> Posts { get; set; } = null!;
    public DbSet<Game> Games { get; set; } = null!;
    public DbSet<Team> Teams { get; set; } = null!;
    public DbSet<Config> Configs { get; set; } = null!;
    public DbSet<LogModel> Logs { get; set; } = null!;
    public DbSet<Division> Divisions { get; set; } = null!;
    public DbSet<LocalFile> Files { get; set; } = null!;
    public DbSet<CheatInfo> CheatInfo { get; set; } = null!;
    public DbSet<Container> Containers { get; set; } = null!;
    public DbSet<GameEvent> GameEvents { get; set; } = null!;
    public DbSet<Submission> Submissions { get; set; } = null!;
    public DbSet<Attachment> Attachments { get; set; } = null!;
    public DbSet<GameNotice> GameNotices { get; set; } = null!;
    public DbSet<FlagContext> FlagContexts { get; set; } = null!;
    public DbSet<Participation> Participations { get; set; } = null!;
    public DbSet<GameInstance> GameInstances { get; set; } = null!;
    public DbSet<GameChallenge> GameChallenges { get; set; } = null!;
    public DbSet<FirstSolve> FirstSolves { get; set; } = null!;
    public DbSet<HintUnlock> HintUnlocks { get; set; } = null!;
    public DbSet<ExerciseInstance> ExerciseInstances { get; set; } = null!;
    public DbSet<ExerciseChallenge> ExerciseChallenges { get; set; } = null!;
    public DbSet<UserParticipation> UserParticipations { get; set; } = null!;
    public DbSet<ExerciseDependency> ExerciseDependencies { get; set; } = null!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<ApiToken> ApiTokens { get; set; } = null!;
    public DbSet<UserPreferences> UserPreferences { get; set; } = null!;
    public DbSet<SocAlert> SocAlerts { get; set; } = null!;
    public DbSet<SocEvent> SocEvents { get; set; } = null!;
    public DbSet<ChallengeSubmission> ChallengeSubmissions { get; set; } = null!;
    // Restored: these sets were dropped from this file by the 2026-09-30 challenge-submission
    // change, although their tables, migrations and model classes all still exist.
    public DbSet<SocDetectionJob> SocDetectionJobs { get; set; } = null!;
    public DbSet<SocEventOutboxEntry> SocEventOutbox { get; set; } = null!;
    public DbSet<SocRiskCase> SocRiskCases { get; set; } = null!;
    public DbSet<SocRiskEvidence> SocRiskEvidence { get; set; } = null!;
    public DbSet<SharedRuntimeConfiguration> SharedRuntimeConfigurations { get; set; } = null!;
    public DbSet<SharedRuntimeSession> SharedRuntimeSessions { get; set; } = null!;
    public DbSet<VariantAssignment> VariantAssignments { get; set; } = null!;
    public DbSet<VariantBuildJob> VariantBuildJobs { get; set; } = null!;
    public DbSet<VariantDatasetItem> VariantDatasetItems { get; set; } = null!;
    public DbSet<VariantTemplate> VariantTemplates { get; set; } = null!;

    private static ValueConverter<T?, string> GetJsonConverter<T>() where T : class, new() =>
        new(
            v => JsonSerializer.Serialize(v ?? new(), JsonOptions),
            v => JsonSerializer.Deserialize<T>(v, JsonOptions)
        );

    private static ValueComparer<TList> GetEnumerableComparer<TList, T>()
        where T : notnull
        where TList : IEnumerable<T>, new() =>
        new(
            (c1, c2) => (c1 == null && c2 == null) || (c2 != null && c1 != null && c1.SequenceEqual(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())));

    /// <summary>
    /// The schema is owned by the checked-in migrations. Small differences between the runtime
    /// model and the migration snapshot must not stop the server from starting.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // var setConverter = GetJsonConverter<HashSet<string>>();
        // var setComparer = GetEnumerableComparer<HashSet<string>, string>();
        var listConverter = GetJsonConverter<List<string>>();
        var listComparer = GetEnumerableComparer<List<string>, string>();
        var intListConverter = GetJsonConverter<List<int>>();
        var intListComparer = GetEnumerableComparer<List<int>, int>();

        builder.Entity<UserInfo>(entity =>
        {
            entity.Property(e => e.Role)
                .HasConversion<int>();

            entity.Property(e => e.UserName)
                .HasMaxLength(Limits.MaxUserNameLength);

            entity.Property(e => e.ExerciseVisible)
                .HasDefaultValue(true);

            entity.Property(e => e.IsChallengeDeveloper)
                .HasDefaultValue(false);

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ChallengeSubmission>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.PackageName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.PackageHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ReviewNote).HasMaxLength(2000);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.HasIndex(e => new { e.DeveloperId, e.SubmittedUtc });
            entity.HasOne(e => e.Developer)
                .WithMany()
                .HasForeignKey(e => e.DeveloperId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Game>(entity =>
        {
            entity.HasMany(e => e.GameEvents)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.GameNotices)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Challenges)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Divisions)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Teams)
                .WithMany(e => e.Games)
                .UsingEntity<Participation>(
                    e => e.HasOne(p => p.Team)
                        .WithMany(t => t.Participations)
                        .HasForeignKey(p => p.TeamId),
                    e => e.HasOne(p => p.Game)
                        .WithMany(g => g.Participations)
                        .HasForeignKey(p => p.GameId)
                );
        });

        builder.Entity<Post>(entity =>
        {
            entity.HasOne(e => e.Author)
                .WithMany()
                .HasForeignKey(e => e.AuthorId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.Tags)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Navigation(e => e.Author).AutoInclude();
        });

        builder.Entity<Team>(entity =>
        {
            entity.HasMany(e => e.Members)
                .WithMany(e => e.Teams);

            entity.HasOne(e => e.Captain)
                .WithMany()
                .HasForeignKey(e => e.CaptainId);

            entity.HasOne(e => e.Captain)
                .WithMany()
                .HasForeignKey(e => e.CaptainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Participation>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<int>();

            entity.HasMany(e => e.Instances).WithOne();

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.Participation)
                .HasForeignKey(e => e.ParticipationId);

            entity.HasMany(e => e.FirstSolves)
                .WithOne(e => e.Participation)
                .HasForeignKey(e => e.ParticipationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Members)
                .WithOne(e => e.Participation)
                .HasForeignKey(e => e.ParticipationId);

            entity.HasOne(e => e.Writeup)
                .WithMany();

            entity.HasOne(e => e.Division)
                .WithMany()
                .HasForeignKey(e => e.DivisionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Game).AutoInclude();
            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.Members).AutoInclude();
            entity.Navigation(e => e.Writeup).AutoInclude();

            entity.HasMany(e => e.Challenges)
                .WithMany(e => e.Teams)
                .UsingEntity<GameInstance>(
                    e => e.HasOne(i => i.Challenge)
                        .WithMany(c => c.Instances)
                        .HasForeignKey(i => i.ChallengeId),
                    e => e.HasOne(i => i.Participation)
                        .WithMany(p => p.Instances)
                        .HasForeignKey(i => i.ParticipationId)
                        .OnDelete(DeleteBehavior.Cascade),
                    e => e.HasKey(i => new { i.ChallengeId, i.ParticipationId })
                );
        });

        builder.Entity<GameInstance>(entity =>
        {
            entity.HasOne(e => e.FlagContext)
                .WithMany()
                .HasForeignKey(e => e.FlagId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Container)
                .WithOne(e => e.GameInstance)
                .HasForeignKey<Container>(e => e.GameInstanceId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.Navigation(e => e.Container).AutoInclude();
            entity.Navigation(e => e.Challenge).AutoInclude();
        });

        builder.Entity<HintUnlock>(entity =>
        {
            entity.HasOne(e => e.Participation)
                .WithMany(e => e.HintUnlocks)
                .HasForeignKey(e => e.ParticipationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Challenge)
                .WithMany(e => e.HintUnlocks)
                .HasForeignKey(e => e.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ExerciseInstance>(entity =>
        {
            entity.HasOne(e => e.Container)
                .WithOne(e => e.ExerciseInstance)
                .HasForeignKey<Container>(e => e.ExerciseInstanceId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.FlagContext)
                .WithMany()
                .HasForeignKey(e => e.FlagId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Container).AutoInclude();
            entity.Navigation(e => e.Exercise).AutoInclude();
        });

        builder.Entity<Container>(entity =>
        {
            entity.HasOne(e => e.GameInstance)
                .WithOne(e => e.Container)
                .HasForeignKey<GameInstance>(e => e.ContainerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ExerciseInstance)
                .WithOne(e => e.Container)
                .HasForeignKey<ExerciseInstance>(e => e.ContainerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<GameChallenge>(entity =>
        {
            entity.Property(e => e.Hints)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.HintPenalties)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);

            entity.Property(e => e.NetworkMode)
                .HasConversion<byte>()
                .HasDefaultValue(NetworkMode.Isolated);

            entity.HasMany(e => e.Flags)
                .WithOne(e => e.Challenge)
                .HasForeignKey(e => e.ChallengeId);

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.GameChallenge)
                .HasForeignKey(e => e.ChallengeId);

            entity.HasMany(e => e.FirstSolves)
                .WithOne(e => e.Challenge)
                .HasForeignKey(e => e.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Attachment)
                .WithMany()
                .HasForeignKey(e => e.AttachmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.TestContainer)
                .WithMany()
                .HasForeignKey(e => e.TestContainerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Attachment).AutoInclude();
            entity.Navigation(e => e.TestContainer).AutoInclude();

            entity.HasIndex(e => e.GameId);

            entity.HasMany(e => e.DivisionConfigs)
                .WithOne(e => e.Challenge)
                .HasForeignKey(e => e.ChallengeId);
        });

        builder.Entity<Division>(entity =>
        {
            entity.HasMany(e => e.ChallengeConfigs)
                .WithOne(e => e.Division)
                .HasForeignKey(e => e.DivisionId);
        });

        builder.Entity<ExerciseChallenge>(entity =>
        {
            entity.Property(e => e.Hints)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.NetworkMode)
                .HasConversion<byte>()
                .HasDefaultValue(NetworkMode.Isolated);

            entity.HasOne(e => e.Attachment)
                .WithMany()
                .HasForeignKey(e => e.AttachmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.Tags)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.HasMany(e => e.Flags)
                .WithOne(e => e.Exercise)
                .HasForeignKey(e => e.ExerciseId);

            entity.HasOne(e => e.TestContainer)
                .WithMany()
                .HasForeignKey(e => e.TestContainerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Dependencies)
                .WithMany()
                .UsingEntity<ExerciseDependency>(
                    l => l.HasOne(e => e.Target).WithMany().HasForeignKey(e => e.TargetId),
                    r => r.HasOne(e => e.Source).WithMany().HasForeignKey(e => e.SourceId)
                );

            entity.Navigation(e => e.Attachment).AutoInclude();
            entity.Navigation(e => e.TestContainer).AutoInclude();
        });

        builder.Entity<Submission>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>();

            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.User).AutoInclude();
            entity.Navigation(e => e.GameChallenge).AutoInclude();
        });

        builder.Entity<FlagContext>(entity =>
        {
            entity.HasOne(e => e.Attachment)
                .WithMany()
                .HasForeignKey(e => e.AttachmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Attachment).AutoInclude();
        });

        builder.Entity<Attachment>(entity =>
        {
            entity.HasOne(e => e.LocalFile)
                .WithMany()
                .HasForeignKey(e => e.LocalFileId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.LocalFile).AutoInclude();
        });

        builder.Entity<GameNotice>(entity =>
        {
            entity.Property(e => e.Values)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);
        });

        builder.Entity<GameEvent>(entity =>
        {
            entity.Property(e => e.Values)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);

            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.User).AutoInclude();
        });

        builder.Entity<CheatInfo>(entity =>
        {
            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId);

            entity.HasOne(e => e.SourceTeam)
                .WithMany()
                .HasForeignKey(e => e.SourceTeamId);

            entity.HasOne(e => e.SubmitTeam)
                .WithMany()
                .HasForeignKey(e => e.SubmitTeamId);

            entity.HasOne(e => e.Submission)
                .WithMany()
                .HasForeignKey(e => e.SubmissionId);

            entity.HasKey(e => e.SubmissionId);
        });

        builder.Entity<FirstSolve>(entity =>
        {
            entity.HasKey(e => new { e.ParticipationId, e.ChallengeId });

            entity.HasOne(e => e.Submission)
                .WithMany()
                .HasForeignKey(e => e.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserParticipation>(entity =>
        {
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId);

            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId);

            entity.HasKey(e => new { e.GameId, e.TeamId, e.UserId });
        });

        builder.Entity<ApiToken>(entity =>
        {
            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<UserPreferences>(entity =>
        {
            // One row per user, removed with the user. The unique index that enforces the
            // one-to-one is declared on the entity itself.
            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<UserPreferences>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Motion)
                .HasConversion<byte>();
        });

        builder.Entity<LogModel>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(Limits.MaxLogStatusLength);
        });

        builder.Entity<SocAlert>(entity =>
        {
            entity.Property(e => e.Severity).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.Category).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        });

        builder.Entity<SocEvent>(entity =>
        {
            entity.Property(e => e.Severity).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.Category).HasConversion<string>().HasMaxLength(32);
        });
        // ---- Restored configuration (mirrors Migrations/AppDbContextModelSnapshot.cs) ----

        builder.Entity<SocDetectionJob>(entity =>
        {
            entity.HasOne(e => e.Submission)
                .WithOne()
                .HasForeignKey<SocDetectionJob>(e => e.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SocRiskCase>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.AssignedAdmin)
                .WithMany()
                .HasForeignKey(e => e.AssignedAdminId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PrimaryTeam)
                .WithMany()
                .HasForeignKey(e => e.PrimaryTeamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RelatedTeam)
                .WithMany()
                .HasForeignKey(e => e.RelatedTeamId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SocRiskEvidence>(entity =>
        {
            entity.HasOne(e => e.RiskCase)
                .WithMany(c => c.Evidence)
                .HasForeignKey(e => e.RiskCaseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Alert)
                .WithMany()
                .HasForeignKey(e => e.AlertId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Submission)
                .WithMany()
                .HasForeignKey(e => e.SubmissionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SharedRuntimeConfiguration>(entity =>
        {
            entity.HasOne(e => e.Challenge)
                .WithOne(c => c.SharedRuntime)
                .HasForeignKey<SharedRuntimeConfiguration>(e => e.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SharedRuntimeSession>(entity =>
        {
            entity.HasOne(e => e.RuntimeConfiguration)
                .WithMany(c => c.Sessions)
                .HasForeignKey(e => e.RuntimeConfigurationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Participation)
                .WithMany(p => p.SharedRuntimeSessions)
                .HasForeignKey(e => e.ParticipationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VariantTemplate>(entity =>
        {
            entity.HasOne(e => e.Challenge)
                .WithMany(c => c.VariantTemplates)
                .HasForeignKey(e => e.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VariantAssignment>(entity =>
        {
            entity.HasOne(e => e.Challenge)
                .WithMany(c => c.VariantAssignments)
                .HasForeignKey(e => e.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FlagContext)
                .WithOne(f => f.VariantAssignment)
                .HasForeignKey<VariantAssignment>(e => e.FlagContextId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Participation)
                .WithMany(p => p.VariantAssignments)
                .HasForeignKey(e => e.ParticipationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VariantBuildJob>(entity =>
        {
            entity.HasOne(e => e.Participation)
                .WithMany(p => p.VariantBuildJobs)
                .HasForeignKey(e => e.ParticipationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Template)
                .WithMany(t => t.Jobs)
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.VariantAssignment)
                .WithOne(a => a.BuildJob)
                .HasForeignKey<VariantBuildJob>(e => e.VariantAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VariantDatasetItem>(entity =>
        {
            entity.HasOne(e => e.Challenge)
                .WithMany(c => c.VariantDatasetItems)
                .HasForeignKey(e => e.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FlagContext)
                .WithOne(f => f.VariantDatasetItem)
                .HasForeignKey<VariantDatasetItem>(e => e.FlagContextId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SelectedParticipation)
                .WithMany()
                .HasForeignKey(e => e.SelectedParticipationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Template)
                .WithMany(t => t.DatasetItems)
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
