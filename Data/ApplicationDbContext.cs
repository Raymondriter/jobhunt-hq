using JobHuntHQ.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Data;

public class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<JobApplication> JobApplications => Set<JobApplication>();

    public DbSet<ResumeVersion> ResumeVersions => Set<ResumeVersion>();

    public DbSet<SearchProfile> SearchProfiles => Set<SearchProfile>();

    public DbSet<InterviewRound> InterviewRounds => Set<InterviewRound>();

    public DbSet<StoryBankItem> StoryBankItems => Set<StoryBankItem>();

    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Pin the length of Identity's key-ish string columns to Identity's
        // default MaxLengthForKeys (128). IdentityDbContext reads that value
        // from IdentityOptions when the context is built through the app's DI
        // (runtime), but not when the EF tools build it via the design-time
        // factory — which otherwise leaves these at nvarchar(450) in the
        // migration snapshot while the runtime model expects nvarchar(128),
        // and EF Core then (correctly) reports pending model changes on every
        // Migrate(). Setting it explicitly makes both models identical.
        builder.Entity<IdentityUserLogin<string>>(entity =>
        {
            entity.Property(l => l.LoginProvider).HasMaxLength(128);
            entity.Property(l => l.ProviderKey).HasMaxLength(128);
        });
        builder.Entity<IdentityUserToken<string>>(entity =>
        {
            entity.Property(t => t.LoginProvider).HasMaxLength(128);
            entity.Property(t => t.Name).HasMaxLength(128);
        });

        builder.Entity<JobApplication>(entity =>
        {
            // Almost every query is "this user's applications", so index the owner.
            entity.HasIndex(a => a.UserId);
            entity.HasIndex(a => new { a.UserId, a.ExternalAttemptId });

            entity.HasOne(a => a.ResumeVersion)
                .WithMany()
                .HasForeignKey(a => a.ResumeVersionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ResumeVersion>(entity =>
        {
            entity.HasIndex(r => r.UserId);
        });

        builder.Entity<SearchProfile>(entity =>
        {
            // One search profile per user.
            entity.HasIndex(p => p.UserId).IsUnique();
        });

        builder.Entity<InterviewRound>(entity =>
        {
            entity.HasIndex(r => r.UserId);
            entity.HasIndex(r => new { r.JobApplicationId, r.RoundNumber });

            entity.HasOne(r => r.JobApplication)
                .WithMany(a => a.InterviewRounds)
                .HasForeignKey(r => r.JobApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StoryBankItem>(entity =>
        {
            entity.HasIndex(s => s.UserId);
        });

        builder.Entity<ApiToken>(entity =>
        {
            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => t.TokenHash).IsUnique();
        });
    }
}
