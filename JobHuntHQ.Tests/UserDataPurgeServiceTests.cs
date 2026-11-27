using JobHuntHQ.Models;
using JobHuntHQ.Services;

namespace JobHuntHQ.Tests;

public class UserDataPurgeServiceTests
{
    [Fact]
    public async Task PurgeUserDataAsync_deletes_only_that_users_rows_and_resume_blobs()
    {
        var (db, connection) = SqliteAppDb.Create();
        await using var _ = connection;
        await using var context = db;

        await SeedUserAsync(context, "user-a", "blob-a.pdf");
        await SeedUserAsync(context, "user-b", "blob-b.pdf");
        context.ResumeVersions.Add(new ResumeVersion
        {
            UserId = "user-a",
            Name = "Notes only",
            BlobName = "   ",
            CreatedUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var storage = new RecordingStorage();
        var sut = new UserDataPurgeService(context, storage);

        await sut.PurgeUserDataAsync("user-a");

        Assert.Equal(new[] { "blob-a.pdf" }, storage.Deleted);
        Assert.Empty(context.JobApplications.Where(a => a.UserId == "user-a"));
        Assert.Empty(context.InterviewRounds.Where(r => r.UserId == "user-a"));
        Assert.Empty(context.ResumeVersions.Where(r => r.UserId == "user-a"));
        Assert.Empty(context.StoryBankItems.Where(s => s.UserId == "user-a"));
        Assert.Empty(context.ApiTokens.Where(t => t.UserId == "user-a"));
        Assert.Empty(context.SearchProfiles.Where(p => p.UserId == "user-a"));

        Assert.Single(context.JobApplications.Where(a => a.UserId == "user-b"));
        Assert.Single(context.InterviewRounds.Where(r => r.UserId == "user-b"));
        Assert.Single(context.ResumeVersions.Where(r => r.UserId == "user-b"));
        Assert.Single(context.StoryBankItems.Where(s => s.UserId == "user-b"));
        Assert.Single(context.ApiTokens.Where(t => t.UserId == "user-b"));
        Assert.Single(context.SearchProfiles.Where(p => p.UserId == "user-b"));
        Assert.Equal("blob-b.pdf", context.ResumeVersions.Single(r => r.UserId == "user-b").BlobName);
    }

    [Fact]
    public async Task PurgeUserDataAsync_is_a_no_op_for_an_unknown_user()
    {
        var (db, connection) = SqliteAppDb.Create();
        await using var _ = connection;
        await using var context = db;
        await SeedUserAsync(context, "user-b", "blob-b.pdf");
        var storage = new RecordingStorage();

        await new UserDataPurgeService(context, storage).PurgeUserDataAsync("missing");

        Assert.Empty(storage.Deleted);
        Assert.Single(context.JobApplications);
        Assert.Single(context.ResumeVersions);
    }

    private static async Task SeedUserAsync(JobHuntHQ.Data.ApplicationDbContext context, string userId, string blobName)
    {
        var now = DateTime.UtcNow;
        var application = new JobApplication
        {
            UserId = userId,
            Company = "Contoso",
            RoleTitle = "Engineer",
            DateSaved = new DateOnly(2026, 11, 1),
            CreatedUtc = now,
            UpdatedUtc = now
        };
        context.JobApplications.Add(application);
        await context.SaveChangesAsync();

        context.InterviewRounds.Add(new InterviewRound
        {
            UserId = userId,
            JobApplicationId = application.Id,
            RoundType = "Recruiter Screen",
            CreatedUtc = now,
            UpdatedUtc = now
        });
        context.ResumeVersions.Add(new ResumeVersion
        {
            UserId = userId,
            Name = "Main",
            BlobName = blobName,
            CreatedUtc = now
        });
        context.StoryBankItems.Add(new StoryBankItem
        {
            UserId = userId,
            Title = "Outage",
            CreatedUtc = now,
            UpdatedUtc = now
        });
        context.ApiTokens.Add(new ApiToken
        {
            UserId = userId,
            Name = "Muse",
            TokenPrefix = "jhq_testpref",
            TokenHash = ApiToken.ComputeSha256Hex(userId),
            Scopes = "applications:read",
            CreatedUtc = now
        });
        context.SearchProfiles.Add(new SearchProfile
        {
            UserId = userId,
            TargetRoles = "engineer",
            CreatedUtc = now,
            UpdatedUtc = now
        });
        await context.SaveChangesAsync();
    }

    private sealed class RecordingStorage : IResumeStorageService
    {
        public List<string> Deleted { get; } = new();

        public Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Stream?> DownloadAsync(string blobName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
        {
            Deleted.Add(blobName);
            return Task.CompletedTask;
        }
    }
}
